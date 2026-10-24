using System.Diagnostics;
using UwDiff.Core;
using UwDiff.Core.Diff;
using UwView.Core;
using UwView.Core.Documents;
using static Uvd.Cli.Program;

namespace Uvd.Cli;

/// <summary>表の比較（キー照合）と、順番を問わないテキストの比較。</summary>
internal static class TableCommand
{
    public static int Run(CliArgs a)
    {
        InputFile io = InputFile.Probe(a.Positional[0]), inew = InputFile.Probe(a.Positional[1]);
        foreach (InputFile f in new[] { io, inew })
            if (f.Office is OfficeKind.Docx or OfficeKind.Pdf)
                throw new ArgumentException($"{f.KindName} は表として照合できません（-key を外すとテキストとして比べます）: {f.Path}");
        PrepareResult(a, "table");
        if (io.Office == OfficeKind.Xlsx || inew.Office == OfficeKind.Xlsx) return RunExcel(a, io, inew);

        KeyedDiffResult r = Engine(a, io, inew, a.OutPath, null, false, null).Run();
        Print(a, r);
        return r.Added + r.Deleted + r.ChangedCells > 0 ? 1 : 0;
    }

    /// <summary>順番を問わない比較（行全体を 1 つの値として、同じ行が何回あるかまで比べる）。</summary>
    public static int RunUnordered(CliArgs a)
    {
        InputFile io = InputFile.Probe(a.Positional[0]), inew = InputFile.Probe(a.Positional[1]);
        PrepareResult(a, "unordered");
        KeyedDiffResult r = Engine(a, io, inew, a.OutPath, null, false, null, wholeLine: true).Run();
        Print(a, r, unordered: true);
        return r.Added + r.Deleted > 0 ? 1 : 0;
    }

    /// <summary>画面に渡すときは、差分を必ずファイルに書く（-o が無ければ結果の置き場所に）。</summary>
    private static void PrepareResult(CliArgs a, string mode)
    {
        if (a.ResultDir == null) return;
        a.OutPath ??= Path.Combine(a.ResultDir, "diff.csv");
        a.NewResult(mode, a.OutPath);
    }

    private static KeyedDiffEngine Engine(CliArgs a, InputFile io, InputFile inew, string? outPath, (string, string)? extra, bool append,
        (string Old, string New)? sheetPaths, bool wholeLine = false)
    {
        long lastShown = 0;
        var sw = Stopwatch.StartNew();
        Func<Stream>? Open(InputFile f) => f.IsOffice ? () => OfficeText.OpenStream(f.Path, f.Office, a.Raw) : f.IsCompressed ? f.OpenContent : null;
        return new KeyedDiffEngine(new KeyedDiffOptions
        {
            OldPath = sheetPaths?.Old ?? io.Path,
            NewPath = sheetPaths?.New ?? inew.Path,
            OldOpen = sheetPaths != null ? null : Open(io),
            NewOpen = sheetPaths != null ? null : Open(inew),
            OldLength = sheetPaths != null ? 0 : io.ContentLength,
            NewLength = sheetPaths != null ? 0 : inew.ContentLength,
            OutPath = outPath,
            KeyColumns = a.Keys,
            WholeLine = wholeLine,
            OldEncoding = sheetPaths != null || io.IsOffice ? FileEncoding.Utf8 : a.Format.OldEncoding,
            NewEncoding = sheetPaths != null || inew.IsOffice ? FileEncoding.Utf8 : a.Format.NewEncoding,
            Delimiter = sheetPaths != null ? (byte)'\t' : a.Format.Delimiter,
            Quotes = sheetPaths == null && a.Format.Quotes,
            ExtraColumn = extra,
            AppendOutput = append,
            BlockSizeMB = a.Block,
            Workers = a.Workers,
            Shards = a.Shards,
            MemoryLimitBytes = a.Mem,
            TempDirectory = a.TempDir,
            CheckDuplicates = a.Dup,
            ReadConcurrently = !a.Serial,
            ParseOnly = a.ParseOnly,
            Progress = a.Quiet ? null : (done, total) =>
            {
                long now = sw.ElapsedMilliseconds;
                if (now - lastShown < 1000) return;
                lastShown = now;
                Console.Error.Write($"\r  読み込み {done * 100.0 / Math.Max(1, total),5:F1}%  {sw.Elapsed.TotalSeconds,6:F1} 秒");
            },
        });
    }

    /// <summary>Excel: シートごとに表にして照合し、差分を 1 本にまとめる（区分の次に「シート」の列）。片方が CSV なら Excel の最初のシートと比べる。</summary>
    private static int RunExcel(CliArgs a, InputFile io, InputFile inew)
    {
        string tmp = Path.Combine(a.TempDir ?? Path.GetTempPath(), $"uvd-xlsx-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        int seq = 0;
        string NewTemp() => Path.Combine(tmp, $"sheet-{++seq}.tsv");
        try
        {
            List<(string Sheet, string Path)> Sheets(InputFile f) => f.Office == OfficeKind.Xlsx
                ? ExcelSheets.Split(f.Path, a.Raw, NewTemp)
                : throw new ArgumentException("Excel と CSV・TSV の照合は、まだできません。Excel を CSV で保存してから比べてください");
            var so = Sheets(io);
            var sn = Sheets(inew);
            var names = so.Select(s => s.Sheet).Concat(sn.Select(s => s.Sheet)).Distinct().ToList();
            bool first = true, differs = false;
            var totals = new KeyedDiffResult();
            foreach (string name in names)
            {
                string? po = so.FirstOrDefault(s => s.Sheet == name).Path, pn = sn.FirstOrDefault(s => s.Sheet == name).Path;
                // 片方にしか無いシートは、相手を見出しだけの表として比べる（すべて追加か削除）
                if (po == null) { po = NewTemp(); File.WriteAllLines(po, File.ReadLines(pn!).Take(1)); Console.WriteLine($"シート「{name}」は新にだけある"); }
                if (pn == null) { pn = NewTemp(); File.WriteAllLines(pn, File.ReadLines(po).Take(1)); Console.WriteLine($"シート「{name}」は旧にだけある"); }
                KeyedDiffResult r;
                try
                {
                    r = Engine(a, io, inew, a.OutPath, ("シート", name), !first, (po, pn)).Run();
                }
                catch (ArgumentException ex)
                {
                    Console.WriteLine($"シート「{name}」: 照合できない（{ex.Message}）");
                    differs = true;
                    continue;
                }
                if (r.OldRows == 0 && r.NewRows == 0 && !first) continue;
                first = false;
                Console.WriteLine($"シート「{name}」: 追加 {r.Added:N0}／削除 {r.Deleted:N0}／変更 {r.ChangedRows:N0} 行（{r.ChangedCells:N0} セル）／同じ {r.SameRows:N0}");
                totals.Added += r.Added; totals.Deleted += r.Deleted; totals.ChangedRows += r.ChangedRows; totals.ChangedCells += r.ChangedCells; totals.SameRows += r.SameRows;
                totals.OldRows += r.OldRows; totals.NewRows += r.NewRows;
                totals.KeyColumnNames = r.KeyColumnNames;
            }
            Console.WriteLine($"合計（{names.Count} シート）: 追加 {totals.Added:N0}／削除 {totals.Deleted:N0}／変更 {totals.ChangedRows:N0} 行（{totals.ChangedCells:N0} セル）／同じ {totals.SameRows:N0}");
            if (a.JsonPath != null)
                OutputGuard.WriteAllText(a.JsonPath, "{" + $"\"old\":{J(io.Path)},\"new\":{J(inew.Path)},\"sheets\":{names.Count},\"keys\":{L(totals.KeyColumnNames)}," +
                    $"\"oldRows\":{totals.OldRows},\"newRows\":{totals.NewRows},\"added\":{totals.Added},\"deleted\":{totals.Deleted},\"changedRows\":{totals.ChangedRows},\"changedCells\":{totals.ChangedCells},\"same\":{totals.SameRows}" + "}\n");
            return differs || totals.Added + totals.Deleted + totals.ChangedCells > 0 ? 1 : 0;
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void Print(CliArgs a, KeyedDiffResult r, bool unordered = false)
    {
        if (!a.Quiet) Console.Error.WriteLine();
        string keyName = string.Join("＋", r.KeyColumnNames);
        string what = unordered ? "行（順番を問わない）" : "キー: " + keyName;
        Console.WriteLine($"旧 {r.OldRows:N0} 行（{r.OldEncoding}・{(unordered ? "" : r.OldDelimiter + "・")}{Mb(r.OldBytes)}）／新 {r.NewRows:N0} 行（{r.NewEncoding}・{(unordered ? "" : r.NewDelimiter + "・")}{Mb(r.NewBytes)}）  {what}");
        if (r.Transcoded) Console.WriteLine("  文字コードが違う（または UTF-16 の）ため、UTF-8 にそろえて比べた");
        if (!unordered)
        {
            if (!r.SameLayout) Console.WriteLine("  旧と新で列の並びか区切りが違うため、列の名前で対応付けて比べた");
            if (r.OldOnlyColumns.Count > 0) Console.WriteLine($"  旧にしか無い列（新は空として比べた）: {string.Join(", ", r.OldOnlyColumns)}");
            if (r.NewOnlyColumns.Count > 0) Console.WriteLine($"  新にしか無い列（旧は空として比べた）: {string.Join(", ", r.NewOnlyColumns)}");
            Console.WriteLine($"追加 {r.Added:N0}／削除 {r.Deleted:N0}／変更 {r.ChangedRows:N0} 行（{r.ChangedCells:N0} セル）／同じ {r.SameRows:N0}");
            if (a.Dup) Console.WriteLine($"キーの重複: 旧 {r.OldDuplicateKeys:N0}・新 {r.NewDuplicateKeys:N0}");
        }
        else Console.WriteLine($"新にだけある行 {r.Added:N0}／旧にだけある行 {r.Deleted:N0}／両方にある行 {r.SameRows:N0}（空の行は数えない）");
        Console.WriteLine($"時間: 合計 {r.Total.TotalSeconds:F2} 秒（読み込みと照合 {r.ReadAndMatch.TotalSeconds:F2}・後処理 {r.Finish.TotalSeconds:F2}）");
        Console.WriteLine($"待ち行の最大: {r.PeakPendingRows:N0} 行・{Mb(r.PeakPendingBytes)}／.NET のメモリの最大（見立て）: {Mb(r.PeakManagedBytes)}／上限 {Mb(r.MemoryLimitBytes)}");
        if (r.SpilledShards > 0) Console.WriteLine($"一時ファイルへ逃がした区画: {r.SpilledShards}／{r.Shards}（{Mb(r.SpillBytes)}）");
        Console.WriteLine($"内訳: 行に切る 旧 {r.ParseOldBusy.TotalSeconds:F2}・新 {r.ParseNewBusy.TotalSeconds:F2} 秒／照合 {r.MatchBusy.TotalSeconds:F2} 秒（{r.Workers} スレッドの合計）／詰め直し {r.Compactions} 回／キーの記録の一時ファイル {Mb(r.KeyLogBytesOnDisk)}");
        Console.WriteLine($"照合スレッド {r.Workers}・区画 {r.Shards}・塊 {a.Block} MB・{(a.Serial ? "1 本ずつ読む" : "2 本を同時に読む")}");

        if (a.JsonPath != null)
        {
            OutputGuard.WriteAllText(a.JsonPath,
                "{" +
                $"\"old\":{J(a.Positional[0])},\"new\":{J(a.Positional[1])},\"mode\":{J(unordered ? "unordered" : "table")},\"key\":{J(keyName)},\"keys\":{L(r.KeyColumnNames)}," +
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
                $"\"workers\":{r.Workers},\"shards\":{r.Shards},\"blockMB\":{a.Block},\"serial\":{(a.Serial ? "true" : "false")}" +
                "}\n");
        }
    }

    /// <summary>フォルダの比較の明細用: 1 組の表を照合して out へ書く（要約は出さない）。違いがあれば true。</summary>
    internal static bool DiffQuiet(CliArgs a, InputFile io, InputFile inew, string outPath)
    {
        var quiet = new CliArgs { Quiet = true };
        quiet.Keys.AddRange(a.Keys);
        KeyedDiffResult r = Engine(MergeFormat(quiet, a), io, inew, outPath, null, false, null).Run();
        return r.Added + r.Deleted + r.ChangedCells > 0;
    }

    private static CliArgs MergeFormat(CliArgs dst, CliArgs src)
    {
        dst.Format.OldEncoding = src.Format.OldEncoding;
        dst.Format.NewEncoding = src.Format.NewEncoding;
        dst.Format.Delimiter = src.Format.Delimiter;
        dst.Format.Quotes = src.Format.Quotes;
        dst.TempDir = src.TempDir;
        dst.Mem = src.Mem;
        dst.Dup = src.Dup;
        return dst;
    }
}
