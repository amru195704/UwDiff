using System.Globalization;
using UwDiff.Core;

namespace Uvd.Cli;

/// <summary>
/// uvd: 2 つ（2 本のファイル、または 2 つのフォルダ）を比べる。
/// 比べ方は入力とオプションで決める: フォルダどうし → フォルダの比較／-key あり → 表の比較／-unordered → 順番を問わない比較／それ以外 → テキストの比較。
/// 終了コードは diff と同じ: 0 = 同じ、1 = 違いあり、2 = エラー。
/// </summary>
public static class Program
{
    internal const string Usage = """
        使い方:
          uvd 旧 新 [-o 差分]                         テキストの比較（行の並びを保つ。unified diff）
          uvd 旧 新 -key 列名 [-o 差分.csv]           表（CSV・TSV・Excel）をキー列で照合する
          uvd 旧 新 -unordered [-o 差分.csv]          行の順番を問わずに比べる（同じ行の数まで）
          uvd 旧フォルダ 新フォルダ [-o 一覧.csv]      フォルダどうしを比べる（ファイル名で対応付け）
          uvd apply 旧 新 差分.csv -o 結果             表の差分（採る行だけ残したもの）を旧に反映する
          uvd -open                                   画面（UwDiff）を開く
          uvd --version                               版数

        共通:
          -o 出力              差分の書き出し先（無ければ要約だけ）
          -enc 文字コード      旧・新の両方（auto・utf8・cp932・eucjp・utf16le・utf16be。既定 auto）
          -enc-old / -enc-new  片方だけ
          -json 結果.json      要約を JSON で書く
          -tmp フォルダ        一時ファイルの置き場所（既定は OS の一時フォルダ）
          -q                   進み具合を出さない
          -open                結果を画面（UwDiff）で開く（画面はもう一度比べない）
          圧縮（gz・bz2・xz・lzma・zst・lz4・br）は中身を、Word（.docx）・PDF は取り出した文字を、Excel（.xlsx）は
          -key があればシートごとの表、無ければ取り出した文字を比べる。

        テキストの比較:
          -U 行数              前後の文脈の行数（既定 3）
          -b                   空白の量の違いを無視する（続いた空白を 1 つとみなし、行頭・行末の空白は無視）
          -w                   空白・タブをすべて無視する
          -i                   英字の大文字・小文字を区別しない
          -strict-eol          改行コード（\r\n と \n）の違いも違いとみなす（既定は無視）

        表の比較:
          -key 列名            キー列（見出しの名前か #1 のような列番号）。複数は -key A,B か -key A -key B
          -d 区切り            auto・,・tab（既定 auto: .tsv・.tab はタブ、.csv はカンマ、ほかは見出しの行から）
          -quote on|off        引用符（"）を CSV の決まりどおり扱うか（既定 on。引用符を使わない TSV は off）
          -mem 大きさ          待ち行に使うメモリの上限（例 4GB・512MB。既定は実メモリの 4 割）。超えた分は一時ファイルへ
          -nodup               キーの重複を数えない
          -raw                 Excel のセルを表示の形でなく元の値で比べる
          （試験用: -block MB・-workers N・-shards N・-serial・-parseonly）

        フォルダの比較:
          -include 型          比べるファイル（例 "*.csv;*.log"。既定はすべて）
          -exclude 型          除くファイル・フォルダ（例 "*.tmp;.git"）
          -detail フォルダ     中身が違うファイルの差分を 1 本ずつ書く（テキストは .diff、-key があれば表の .csv）

        反映のオプション:
          -o 結果              書き出し先（旧の文字コード・BOM・区切り・改行・引用符を保つ）
          -enc・-enc-old・-enc-new・-d・-quote は表の比較と同じ
        """;

    private static int Main(string[] args) => Run(args);

    /// <summary>
    /// uvd を実行する（画面からも同じプロセスの中で呼ぶ）。-result があれば結果を画面用の置き場所に書き、-open なら画面を起動する。
    /// </summary>
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "apply") return Apply.Run(args[1..]);
            if (args.Length == 1 && args[0] is "--version" or "-version")
            {
                Console.WriteLine($"uvd (UwDiff) {Version} (build {BuildNumber})");
                return 0;
            }
            CliArgs a = CliArgs.Parse(args);
            if (a.Help)
            {
                Console.WriteLine(Usage);
                return 0;
            }
            // uvd -open だけ: 画面を開く（比べるものは画面で選ぶ）
            if (a.Open && a.Positional.Count == 0) return LaunchGui(null) ? 0 : 2;
            if (a.Positional.Count != 2) throw new ArgumentException("使い方: uvd 旧 新 [-key 列名] [-o 差分]（-h で詳しく）");
            if (a.Open && a.ResultDir == null) a.ResultDir = ResultPackage.NewFolder(a.TempDir);
            if (a.ResultDir != null) Directory.CreateDirectory(a.ResultDir);
            // 画面に渡す要約: 標準出力に出す行を写しておく
            TextWriter stdout = Console.Out;
            var captured = new StringWriter();
            if (a.ResultDir != null) Console.SetOut(new TeeWriter(stdout, captured));
            int rc;
            try
            {
                rc = Dispatch(a);
            }
            finally
            {
                if (a.ResultDir != null) Console.SetOut(stdout);
            }
            if (a.ResultDir != null && a.Result != null)
            {
                a.Result.Summary = captured.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
                a.Result.Save(a.ResultDir);
                if (a.Open) LaunchGui(a.ResultDir);
            }
            return rc;
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
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException
                                   or UwView.Core.Documents.OfficeRejectedException)
        {
            Console.Error.WriteLine("uvd: " + ex.Message);
            return 2;
        }
    }

    private static int Dispatch(CliArgs a)
    {
        bool dirs = Directory.Exists(a.Positional[0]) && Directory.Exists(a.Positional[1]);
        if (dirs) return FolderCommand.Run(a);
        if (Directory.Exists(a.Positional[0]) || Directory.Exists(a.Positional[1]))
            throw new ArgumentException("ファイルとフォルダは比べられません（両方ファイルか、両方フォルダにしてください）");
        if (a.Unordered) return TableCommand.RunUnordered(a);
        if (a.Keys.Count > 0) return TableCommand.Run(a);
        return TextCommand.Run(a);
    }

    /// <summary>
    /// 画面（UwDiff）を起動して結果の置き場所を渡す（null なら何も渡さず開くだけ）。画面は uvd と同じフォルダに置く（環境変数 UVD_GUI で別の場所も指定できる）。
    /// </summary>
    private static bool LaunchGui(string? resultDir)
    {
        string dir = AppContext.BaseDirectory;
        string? gui = Environment.GetEnvironmentVariable("UVD_GUI");
        if (string.IsNullOrEmpty(gui))
        {
            foreach (string name in new[] { "UwDiff.exe", "UwDiff" })
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p)) { gui = p; break; }
            }
        }
        if (string.IsNullOrEmpty(gui) || !File.Exists(gui))
        {
            Console.Error.WriteLine($"uvd: 画面（UwDiff）が見つかりません（{dir}）" + (resultDir != null ? $"。結果は {resultDir} にあります" : ""));
            return false;
        }
        var psi = new System.Diagnostics.ProcessStartInfo(gui) { UseShellExecute = false };
        if (resultDir != null)
        {
            psi.ArgumentList.Add(ResultPackage.Argument);
            psi.ArgumentList.Add(resultDir);
        }
        System.Diagnostics.Process.Start(psi);
        return true;
    }

    /// <summary>書いたものを 2 か所へ送る（標準出力と、画面に渡す要約）。</summary>
    private sealed class TeeWriter(TextWriter a, TextWriter b) : TextWriter
    {
        public override System.Text.Encoding Encoding => a.Encoding;
        public override void Write(char value) { a.Write(value); b.Write(value); }
        public override void Write(string? value) { a.Write(value); b.Write(value); }
        public override void WriteLine(string? value) { a.WriteLine(value); b.WriteLine(value); }
        public override void Flush() { a.Flush(); b.Flush(); }
    }

    /// <summary>照合と反映で共通の、ファイルの形のオプション。</summary>
    public sealed class FormatOptions
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

    /// <summary>版数（Directory.Build.props の InformationalVersion）。</summary>
    public static string Version => typeof(Program).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "?";

    /// <summary>ビルド番号（Directory.Build.props の BuildNumber＝コンパイル日時 yy.MM.dd.HH）。</summary>
    public static string BuildNumber => typeof(Program).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .OfType<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(m => m.Key == "BuildNumber")?.Value ?? "?";

    internal static string Mb(long b) => (b / 1048576.0).ToString("F0", CultureInfo.InvariantCulture) + " MB";

    internal static string J(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
    internal static string F(double d) => d.ToString("F3", CultureInfo.InvariantCulture);
    internal static string L(IEnumerable<string> xs) => "[" + string.Join(",", xs.Select(J)) + "]";
}

/// <summary>比べるときのオプション（すべての比べ方で共通に読む）。</summary>
public sealed class CliArgs
{
    public readonly List<string> Positional = new();
    public readonly List<string> Keys = new();
    public readonly Program.FormatOptions Format = new();
    public string? OutPath, JsonPath, TempDir, DetailDir, Include, Exclude;
    public int Block = 32, Workers, Shards, Context = 3;
    public long Mem;
    public bool Dup = true, Serial, Quiet, ParseOnly, Help, Unordered, Raw;
    public bool IgnoreSpaceChange, IgnoreAllSpace, IgnoreCase, StrictEol;
    /// <summary>結果を画面用の置き場所に書く（-result）。-open なら一時フォルダ。</summary>
    public string? ResultDir;
    public bool Open;
    /// <summary>コマンドが埋める、画面に渡す結果（ResultDir があるときだけ）。</summary>
    internal ResultPackage? Result;

    internal ResultPackage NewResult(string mode, string? resultFile) => Result = new ResultPackage
    {
        Mode = mode,
        Old = Path.GetFullPath(Positional[0]),
        New = Path.GetFullPath(Positional[1]),
        ResultFile = resultFile == null ? null : Path.GetFullPath(resultFile),
        Keys = new List<string>(Keys),
        IgnoreSpaceChange = IgnoreSpaceChange,
        IgnoreAllSpace = IgnoreAllSpace,
        IgnoreCase = IgnoreCase,
        StrictEol = StrictEol,
        OldEncoding = Format.OldEncoding?.ToString(),
        NewEncoding = Format.NewEncoding?.ToString(),
    };

    public static CliArgs Parse(string[] args)
    {
        var c = new CliArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} の後に値が要ります");
            int Int() => int.Parse(Next(), CultureInfo.InvariantCulture);
            if (c.Format.TryParse(a, Next)) continue;
            switch (a)
            {
                case "-key": c.Keys.AddRange(Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
                case "-o": c.OutPath = Next(); break;
                case "-json": c.JsonPath = Next(); break;
                case "-mem": c.Mem = Program.ParseSize(Next()); break;
                case "-tmp": c.TempDir = Next(); break;
                case "-detail": c.DetailDir = Next(); break;
                case "-include": c.Include = Next(); break;
                case "-exclude": c.Exclude = Next(); break;
                case "-block": c.Block = Int(); break;
                case "-workers": c.Workers = Int(); break;
                case "-shards": c.Shards = Int(); break;
                case "-U": c.Context = Int(); break;
                case "-b": c.IgnoreSpaceChange = true; break;
                case "-w": c.IgnoreAllSpace = true; break;
                case "-i": c.IgnoreCase = true; break;
                case "-strict-eol": c.StrictEol = true; break;
                case "-unordered": c.Unordered = true; break;
                case "-raw": c.Raw = true; break;
                case "-result": c.ResultDir = Next(); break;
                case "-open": c.Open = true; break;
                case "-nodup": c.Dup = false; break;
                case "-serial": c.Serial = true; break;
                case "-q": c.Quiet = true; break;
                case "-parseonly": c.ParseOnly = true; break;
                case "-h" or "--help": c.Help = true; break;
                default:
                    if (a.StartsWith('-') && a.Length > 1) throw new ArgumentException("知らないオプション: " + a);
                    c.Positional.Add(a);
                    break;
            }
        }
        return c;
    }
}
