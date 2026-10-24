using System.Diagnostics;
using System.Globalization;
using UwDiff.Core;
using UwDiff.Core.Diff;

namespace Uvd.Cli;

/// <summary>
/// uvd（段階 1）: 2 本の表（CSV・TSV）をキー列で照合し、差分を CSV で書き出す。差分から採った変更を反映したファイルも作る。
/// 構文は段階 1 の時点のもの（GUI と合わせて段階 4 で見直す）。
/// </summary>
internal static class Program
{
    private const string Usage = """
        使い方:
          uvd 旧 新 -key 列名 [-o 差分.csv] [オプション]          表をキー列で照合する
          uvd apply 旧 新 差分.csv -o 結果 [オプション]             差分（採る行だけ残したもの）を旧に反映する

        照合のオプション:
          -key 列名            キー列（見出しの名前か #1 のような列番号）。複数は -key A,B か -key A -key B
          -o 差分.csv          差分の書き出し先（区分,キー列…,列,旧値,新値・UTF-8）
          -enc 文字コード      旧・新の両方（auto・utf8・cp932・eucjp・utf16le・utf16be。既定 auto）
          -enc-old / -enc-new  片方だけ
          -d 区切り            auto・,・tab（既定 auto: .tsv・.tab はタブ、.csv はカンマ、ほかは見出しの行から）
          -quote on|off        引用符（"）を CSV の決まりどおり扱うか（既定 on。引用符を使わない TSV は off）
          -mem 大きさ          待ち行に使うメモリの上限（例 4GB・512MB。既定は実メモリの 4 割）。超えた分は一時ファイルへ
          -tmp フォルダ        一時ファイルの置き場所（既定は OS の一時フォルダ）
          -json 結果.json      要約を JSON で書く
          -nodup               キーの重複を数えない
          -q                   進み具合を出さない
          （試験用: -block MB・-workers N・-shards N・-serial・-parseonly）

        反映のオプション:
          -o 結果              書き出し先（旧の文字コード・BOM・区切り・改行・引用符を保つ）
          -enc・-enc-old・-enc-new・-d・-quote は照合と同じ
        """;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "apply") return Apply.Run(args[1..]);
            return Run(args);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine("uvd: ファイルが見つかりません: " + ex.FileName);
            return 2;
        }
        catch (DirectoryNotFoundException ex)
        {
            Console.Error.WriteLine("uvd: フォルダが見つかりません: " + ex.Message);
            return 2;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("uvd: " + ex.Message);
            return 2;
        }
    }

    /// <summary>照合と反映で共通の、ファイルの形のオプション。</summary>
    internal sealed class FormatOptions
    {
        public FileEncoding? OldEncoding, NewEncoding;
        public byte? Delimiter;
        public bool Quotes = true;

        /// <summary>a が形のオプションなら読んで true。</summary>
        public bool TryParse(string a, Func<string> next)
        {
            switch (a)
            {
                case "-enc": OldEncoding = NewEncoding = TextEncodings.Parse(next()); return true;
                case "-enc-old": OldEncoding = TextEncodings.Parse(next()); return true;
                case "-enc-new": NewEncoding = TextEncodings.Parse(next()); return true;
                case "-d":
                    Delimiter = next() switch
                    {
                        "auto" => null,
                        "tab" or "\\t" or "\t" => (byte)'\t',
                        "," or "comma" => (byte)',',
                        ";" => (byte)';',
                        "|" => (byte)'|',
                        string x => throw new ArgumentException("-d は auto・,・tab・;・| のどれか: " + x),
                    };
                    return true;
                case "-quote":
                    Quotes = next().ToLowerInvariant() switch
                    {
                        "on" or "yes" => true,
                        "off" or "no" or "none" => false,
                        string x => throw new ArgumentException("-quote は on か off: " + x),
                    };
                    return true;
                default: return false;
            }
        }
    }

    internal static long ParseSize(string s)
    {
        string t = s.Trim().ToUpperInvariant();
        long mul = 1;
        if (t.EndsWith("GB")) { mul = 1L << 30; t = t[..^2]; }
        else if (t.EndsWith("MB")) { mul = 1L << 20; t = t[..^2]; }
        else if (t.EndsWith("KB")) { mul = 1L << 10; t = t[..^2]; }
        else if (t.EndsWith('G')) { mul = 1L << 30; t = t[..^1]; }
        else if (t.EndsWith('M')) { mul = 1L << 20; t = t[..^1]; }
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v <= 0)
            throw new ArgumentException("大きさは 4GB・512MB のように: " + s);
        return (long)(v * mul);
    }

    private static int Run(string[] args)
    {
        var pos = new List<string>();
        var keys = new List<string>();
        var fmt = new FormatOptions();
        string? outPath = null, jsonPath = null, tempDir = null;
        int block = 32, workers = 0, shards = 0;
        long mem = 0;
        bool dup = true, serial = false, quiet = false, parseOnly = false;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} の後に値が要ります");
            if (fmt.TryParse(a, Next)) continue;
            switch (a)
            {
                case "-key": keys.AddRange(Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
                case "-o": outPath = Next(); break;
                case "-json": jsonPath = Next(); break;
                case "-mem": mem = ParseSize(Next()); break;
                case "-tmp": tempDir = Next(); break;
                case "-block": block = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "-workers": workers = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "-shards": shards = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "-nodup": dup = false; break;
                case "-serial": serial = true; break;
                case "-q": quiet = true; break;
                case "-parseonly": parseOnly = true; break;
                case "-h" or "--help":
                    Console.WriteLine(Usage);
                    return 0;
                default:
                    if (a.StartsWith('-') && a.Length > 1) throw new ArgumentException("知らないオプション: " + a);
                    pos.Add(a);
                    break;
            }
        }
        if (pos.Count != 2 || keys.Count == 0)
            throw new ArgumentException("使い方: uvd 旧 新 -key 列名 [-o 差分.csv]（-h で詳しく）");

        long lastShown = 0;
        var sw = Stopwatch.StartNew();
        var engine = new KeyedDiffEngine(new KeyedDiffOptions
        {
            OldPath = pos[0],
            NewPath = pos[1],
            OutPath = outPath,
            KeyColumns = keys,
            OldEncoding = fmt.OldEncoding,
            NewEncoding = fmt.NewEncoding,
            Delimiter = fmt.Delimiter,
            Quotes = fmt.Quotes,
            BlockSizeMB = block,
            Workers = workers,
            Shards = shards,
            MemoryLimitBytes = mem,
            TempDirectory = tempDir,
            CheckDuplicates = dup,
            ReadConcurrently = !serial,
            ParseOnly = parseOnly,
            Progress = quiet ? null : (done, total) =>
            {
                long now = sw.ElapsedMilliseconds;
                if (now - lastShown < 1000) return;
                lastShown = now;
                Console.Error.Write($"\r  読み込み {done * 100.0 / Math.Max(1, total),5:F1}%  {sw.Elapsed.TotalSeconds,6:F1} 秒");
            },
        });
        KeyedDiffResult r = engine.Run();
        if (!quiet) Console.Error.WriteLine();

        static string Mb(long b) => (b / 1048576.0).ToString("F0", CultureInfo.InvariantCulture) + " MB";
        string keyName = string.Join("＋", r.KeyColumnNames);
        Console.WriteLine($"旧 {r.OldRows:N0} 行（{r.OldEncoding}・{r.OldDelimiter}・{Mb(r.OldBytes)}）／新 {r.NewRows:N0} 行（{r.NewEncoding}・{r.NewDelimiter}・{Mb(r.NewBytes)}）  キー: {keyName}");
        if (r.Transcoded) Console.WriteLine("  文字コードが違う（または UTF-16 の）ため、UTF-8 にそろえて比べた");
        if (!r.SameLayout) Console.WriteLine("  旧と新で列の並びか区切りが違うため、列の名前で対応付けて比べた");
        if (r.OldOnlyColumns.Count > 0) Console.WriteLine($"  旧にしか無い列（新は空として比べた）: {string.Join(", ", r.OldOnlyColumns)}");
        if (r.NewOnlyColumns.Count > 0) Console.WriteLine($"  新にしか無い列（旧は空として比べた）: {string.Join(", ", r.NewOnlyColumns)}");
        Console.WriteLine($"追加 {r.Added:N0}／削除 {r.Deleted:N0}／変更 {r.ChangedRows:N0} 行（{r.ChangedCells:N0} セル）／同じ {r.SameRows:N0}");
        if (dup) Console.WriteLine($"キーの重複: 旧 {r.OldDuplicateKeys:N0}・新 {r.NewDuplicateKeys:N0}");
        Console.WriteLine($"時間: 合計 {r.Total.TotalSeconds:F2} 秒（読み込みと照合 {r.ReadAndMatch.TotalSeconds:F2}・後処理 {r.Finish.TotalSeconds:F2}）");
        Console.WriteLine($"待ち行の最大: {r.PeakPendingRows:N0} 行・{Mb(r.PeakPendingBytes)}／.NET のメモリの最大（見立て）: {Mb(r.PeakManagedBytes)}／上限 {Mb(r.MemoryLimitBytes)}");
        if (r.SpilledShards > 0) Console.WriteLine($"一時ファイルへ逃がした区画: {r.SpilledShards}／{r.Shards}（{Mb(r.SpillBytes)}）");
        Console.WriteLine($"内訳: 行に切る 旧 {r.ParseOldBusy.TotalSeconds:F2}・新 {r.ParseNewBusy.TotalSeconds:F2} 秒／照合 {r.MatchBusy.TotalSeconds:F2} 秒（{r.Workers} スレッドの合計）／詰め直し {r.Compactions} 回／キーの記録の一時ファイル {Mb(r.KeyLogBytesOnDisk)}");
        Console.WriteLine($"照合スレッド {r.Workers}・区画 {r.Shards}・塊 {block} MB・{(serial ? "1 本ずつ読む" : "2 本を同時に読む")}");

        if (jsonPath != null)
        {
            string J(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string F(double d) => d.ToString("F3", CultureInfo.InvariantCulture);
            string L(IEnumerable<string> xs) => "[" + string.Join(",", xs.Select(J)) + "]";
            File.WriteAllText(jsonPath,
                "{" +
                $"\"old\":{J(pos[0])},\"new\":{J(pos[1])},\"key\":{J(keyName)},\"keys\":{L(r.KeyColumnNames)}," +
                $"\"oldEncoding\":{J(r.OldEncoding)},\"newEncoding\":{J(r.NewEncoding)},\"transcoded\":{(r.Transcoded ? "true" : "false")}," +
                $"\"oldDelimiter\":{J(r.OldDelimiter)},\"newDelimiter\":{J(r.NewDelimiter)}," +
                $"\"oldOnlyColumns\":{L(r.OldOnlyColumns)},\"newOnlyColumns\":{L(r.NewOnlyColumns)}," +
                $"\"oldRows\":{r.OldRows},\"newRows\":{r.NewRows},\"oldBytes\":{r.OldBytes},\"newBytes\":{r.NewBytes}," +
                $"\"added\":{r.Added},\"deleted\":{r.Deleted},\"changedRows\":{r.ChangedRows},\"changedCells\":{r.ChangedCells},\"same\":{r.SameRows}," +
                $"\"oldDupKeys\":{r.OldDuplicateKeys},\"newDupKeys\":{r.NewDuplicateKeys}," +
                $"\"totalSec\":{F(r.Total.TotalSeconds)},\"matchSec\":{F(r.ReadAndMatch.TotalSeconds)},\"finishSec\":{F(r.Finish.TotalSeconds)}," +
                $"\"peakPendingRows\":{r.PeakPendingRows},\"peakPendingBytes\":{r.PeakPendingBytes},\"peakManagedBytes\":{r.PeakManagedBytes}," +
                $"\"memoryLimit\":{r.MemoryLimitBytes},\"spilledShards\":{r.SpilledShards},\"spillBytes\":{r.SpillBytes},\"keyLogBytesOnDisk\":{r.KeyLogBytesOnDisk}," +
                $"\"parseOldSec\":{F(r.ParseOldBusy.TotalSeconds)},\"parseNewSec\":{F(r.ParseNewBusy.TotalSeconds)},\"matchBusySec\":{F(r.MatchBusy.TotalSeconds)},\"compactions\":{r.Compactions}," +
                $"\"workers\":{r.Workers},\"shards\":{r.Shards},\"blockMB\":{block},\"serial\":{(serial ? "true" : "false")}" +
                "}\n");
        }
        return 0;
    }
}
