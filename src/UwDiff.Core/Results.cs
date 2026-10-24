using System.Text;
using System.Text.Json;

namespace UwDiff.Core;

/// <summary>
/// CLI の結果を GUI へ渡すための置き場所（-open。GUI はもう一度比べない）。フォルダに result.json と、比べ方ごとの結果のファイルを置く。
/// - text: 変更の位置（hunks.bin）。GUI は旧・新を読んで左右に並べる
/// - table: 差分 CSV（区分,キー列…,列,旧値,新値）
/// - folder: 一覧 CSV（区分,パス,…）
/// </summary>
public sealed class ResultPackage
{
    public const string Argument = "--result";
    public string Mode { get; set; } = "text";
    public string Old { get; set; } = "";
    public string New { get; set; } = "";
    public string? OldEncoding { get; set; }
    public string? NewEncoding { get; set; }
    /// <summary>text: 変更の位置のファイル。table・folder: 差分・一覧の CSV。</summary>
    public string? ResultFile { get; set; }
    public List<string> Keys { get; set; } = new();
    /// <summary>text: 比べたときに作った行の位置の索引（画面がファイルを読み直さずに済む。無ければ画面が作る）。</summary>
    public string? OldIndex { get; set; }
    public string? NewIndex { get; set; }
    public bool IgnoreSpaceChange { get; set; }
    public bool IgnoreAllSpace { get; set; }
    public bool IgnoreCase { get; set; }
    public bool StrictEol { get; set; }
    /// <summary>要約（CLI が出したのと同じ行）。</summary>
    public List<string> Summary { get; set; } = new();

    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    public void Save(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "result.json"), JsonSerializer.Serialize(this, s_json), new UTF8Encoding(false));
    }

    public static ResultPackage Load(string dir) =>
        JsonSerializer.Deserialize<ResultPackage>(File.ReadAllText(Path.Combine(dir, "result.json"))) ?? throw new InvalidDataException("result.json が読めません: " + dir);

    /// <summary>結果を置く新しいフォルダ（OS の一時フォルダの下）。</summary>
    public static string NewFolder(string? tempRoot = null)
    {
        string d = Path.Combine(tempRoot ?? Path.GetTempPath(), $"uvd-result-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(d);
        return d;
    }
}

/// <summary>
/// CSV（UTF-8 など ASCII と重なる文字コード）の行の位置の索引。差分 CSV・フォルダの一覧を、全部をメモリに載せずに画面に出すために使う。
/// 1 行目（見出し）は Header に、データの行は 0 から番号を付ける。行の先頭の列（区分）だけは全部覚える（絞り込み用）。
/// </summary>
public sealed class CsvRowIndex
{
    private readonly string _path;
    private readonly long[] _starts;
    private readonly int[] _lengths;
    private readonly Encoding _enc;
    public string[] Header { get; }
    /// <summary>データの行ごとの先頭の列（区分）。同じ文字列は共有する。</summary>
    public string[] Kinds { get; }
    public int Count => _starts.Length;

    private CsvRowIndex(string path, long[] starts, int[] lengths, string[] header, string[] kinds, Encoding enc)
    {
        _path = path;
        _starts = starts;
        _lengths = lengths;
        Header = header;
        Kinds = kinds;
        _enc = enc;
    }

    public static CsvRowIndex Build(string path)
    {
        CsvFile f = CsvFile.Probe(path, null, (byte)',', true);
        Encoding enc = TextEncodings.IsAsciiCompatible(f.Encoding) ? TextEncodings.Get(f.Encoding) : throw new NotSupportedException("UTF-16 の CSV は画面に出せません: " + path);
        var starts = new List<long>();
        var lengths = new List<int>();
        var kinds = new List<string>();
        var pool = new Dictionary<string, string>();
        var fields = new List<CsvBytes.Field>();
        byte[] tmp = new byte[256];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);
        byte[] buf = new byte[4 << 20];
        int start = 0, end = 0;
        long baseOffset = 0;      // buf[0] のファイルでの位置
        bool header = true, eof = false;
        string[]? headerFields = null;
        fs.Position = f.BomLength;
        baseOffset = f.BomLength;
        while (true)
        {
            int nl = CsvBytes.FindRowEnd(buf.AsSpan(0, end), start, true);
            if (nl < 0)
            {
                if (eof)
                {
                    if (start < end) Add(start, end - start);
                    break;
                }
                if (start > 0)
                {
                    Buffer.BlockCopy(buf, start, buf, 0, end - start);
                    baseOffset += start;
                    end -= start;
                    start = 0;
                }
                if (end == buf.Length) Array.Resize(ref buf, buf.Length * 2);
                int n = fs.Read(buf, end, buf.Length - end);
                if (n == 0) eof = true;
                end += n;
                continue;
            }
            Add(start, nl - start);
            start = nl + 1;
        }
        return new CsvRowIndex(path, starts.ToArray(), lengths.ToArray(), headerFields ?? Array.Empty<string>(), kinds.ToArray(), enc);

        void Add(int s, int len)
        {
            if (len > 0 && buf[s + len - 1] == (byte)'\r') len--;
            if (len == 0) return;
            if (header)
            {
                header = false;
                headerFields = ParseRow(buf.AsSpan(s, len), fields, enc);
                return;
            }
            starts.Add(baseOffset + s);
            lengths.Add(len);
            ReadOnlySpan<byte> row = buf.AsSpan(s, len);
            string kind = CsvBytes.TryGetField(row, (byte)',', 0, out CsvBytes.Field k) ? Decode(row.Slice(k.Start, k.Length), k.Quoted, ref tmp, enc) : "";
            if (!pool.TryGetValue(kind, out string? shared)) pool[kind] = shared = kind;
            kinds.Add(shared);
        }
    }

    private static string Decode(ReadOnlySpan<byte> raw, bool quoted, ref byte[] tmp, Encoding enc)
    {
        if (tmp.Length < raw.Length) tmp = new byte[raw.Length * 2];
        return enc.GetString(tmp, 0, CsvBytes.Unquote(raw, quoted, tmp));
    }

    private static string[] ParseRow(ReadOnlySpan<byte> row, List<CsvBytes.Field> fields, Encoding enc)
    {
        CsvBytes.Split(row, (byte)',', fields, true);
        var r = new string[fields.Count];
        byte[] tmp = new byte[256];
        for (int i = 0; i < fields.Count; i++) r[i] = Decode(row.Slice(fields[i].Start, fields[i].Length), fields[i].Quoted, ref tmp, enc);
        return r;
    }

    /// <summary>データの i 行目の列。</summary>
    public string[] Row(int i)
    {
        using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1);
        fs.Position = _starts[i];
        byte[] b = new byte[_lengths[i]];
        fs.ReadExactly(b);
        return ParseRow(b, new List<CsvBytes.Field>(), _enc);
    }

    /// <summary>データの i 行目のバイト列（改行を除く）。採った行だけの差分を書き出すときに使う。</summary>
    public byte[] RawRow(FileStream fs, int i)
    {
        fs.Position = _starts[i];
        byte[] b = new byte[_lengths[i]];
        fs.ReadExactly(b);
        return b;
    }

    public FileStream OpenRaw() => new(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
}
