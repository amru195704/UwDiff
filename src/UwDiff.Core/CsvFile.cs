using System.Text;

namespace UwDiff.Core;

/// <summary>
/// 表のファイル（CSV・TSV）の形: 文字コード・BOM・区切り・引用符の扱い・見出し・改行。先頭だけを読んで決める。
/// 照合（KeyedDiffEngine）と反映（ApplyEngine）で同じ決め方を使う。
/// </summary>
public sealed class CsvFile
{
    /// <summary>先頭を読む量（見出しの 1 行もこの中に収まること）。</summary>
    public const int HeadSize = 4 << 20;

    public required string Path { get; init; }
    /// <summary>中身を先頭から読むストリームを開く（圧縮ファイルなら展開したもの）。</summary>
    public Func<Stream> Open { get; private init; } = null!;
    public FileEncoding Encoding { get; private set; }
    public int BomLength { get; private set; }
    public byte Delimiter { get; private set; }
    public bool Quotes { get; private set; }
    public string[] Header { get; private set; } = Array.Empty<string>();
    /// <summary>見出しの行の改行（\r\n か \n）。足す行の改行に使う。</summary>
    public byte[] NewLine { get; private set; } = "\n"u8.ToArray();
    public long Length { get; private set; }

    public string DelimiterName => Delimiter switch { (byte)'\t' => "タブ", (byte)',' => "カンマ", _ => ((char)Delimiter).ToString() };

    /// <summary>
    /// ファイルの先頭を読んで形を決める。
    /// delimiter が null なら、拡張子（.tsv・.tab はタブ、.csv はカンマ）、それ以外は見出しの行のタブとカンマの数で決める。
    /// </summary>
    /// <param name="noHeader">見出しの行が無い（順番を問わないテキストの比較で、行全体を 1 列として扱うとき）。見出しは「行」の 1 列にする。</param>
    /// <param name="open">中身を開く（null ならファイルそのもの）。圧縮ファイルの中身を読むときに渡す。</param>
    /// <param name="length">中身の大きさの見積もり（open を渡したとき。進み具合と 2 本の歩調合わせに使う）。</param>
    public static CsvFile Probe(string path, FileEncoding? encoding, byte? delimiter, bool quotes, bool noHeader = false,
        Func<Stream>? open = null, long length = 0)
    {
        open ??= () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        var f = new CsvFile { Path = path, Open = open };
        using var fs = open();
        byte[] head = new byte[HeadSize];
        int n = 0;
        while (n < head.Length)
        {
            int k = fs.Read(head, n, head.Length - n);
            if (k == 0) break;
            n += k;
        }
        bool whole = n < head.Length;
        f.Length = length > 0 ? length : (fs.CanSeek ? fs.Length : n);
        FileEncoding detected = TextEncodings.Detect(head.AsSpan(0, n), whole, out int bom);
        f.Encoding = encoding ?? detected;
        // 文字コードを指定されたときも、その文字コードの BOM があれば飛ばす
        if (encoding != null && encoding != detected)
            bom = TextEncodings.Bom(f.Encoding, 1) is { Length: > 0 } b && head.AsSpan(0, n).StartsWith(b) ? b.Length : 0;
        f.BomLength = bom;
        f.Quotes = quotes;

        // 行に切るときと同じバイト列（UTF-16 なら UTF-8 に直したもの）で見出しを読む
        byte[] work = WorkBytes(head.AsSpan(bom, n - bom), f.Encoding);
        int end = CsvBytes.FindRowEnd(work, 0, quotes);
        if (end < 0) end = work.Length;
        ReadOnlySpan<byte> row = work.AsSpan(0, end);
        if (row.Length > 0 && row[^1] == (byte)'\r')
        {
            row = row[..^1];
            f.NewLine = "\r\n"u8.ToArray();
        }
        f.Delimiter = delimiter ?? GuessDelimiter(path, row, quotes);

        if (noHeader)
        {
            f.Header = new[] { "行" };
            return f;
        }
        var fields = new List<CsvBytes.Field>();
        CsvBytes.Split(row, f.Delimiter, fields, quotes);
        Encoding enc = TextEncodings.IsAsciiCompatible(f.Encoding) ? TextEncodings.Get(f.Encoding) : new UTF8Encoding(false);
        var names = new string[fields.Count];
        for (int i = 0; i < fields.Count; i++)
        {
            CsvBytes.Field x = fields[i];
            byte[] tmp = new byte[x.Length];
            int m = CsvBytes.Unquote(row.Slice(x.Start, x.Length), x.Quoted, tmp);
            names[i] = enc.GetString(tmp, 0, m);
        }
        f.Header = names;
        return f;
    }

    /// <summary>ASCII と重なる文字コードならそのまま、UTF-16 なら UTF-8 に直したバイト列（末尾の切れた文字は捨てる）。</summary>
    private static byte[] WorkBytes(ReadOnlySpan<byte> s, FileEncoding e)
    {
        if (TextEncodings.IsAsciiCompatible(e)) return s.ToArray();
        Decoder dec = TextEncodings.Get(e).GetDecoder();
        char[] chars = new char[dec.GetCharCount(s, false)];
        int nc = dec.GetChars(s, chars, false);
        return new UTF8Encoding(false).GetBytes(chars, 0, nc);
    }

    private static byte GuessDelimiter(string path, ReadOnlySpan<byte> header, bool quotes)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".tsv" or ".tab") return (byte)'\t';
        if (ext == ".csv") return (byte)',';
        int tabs = 0, commas = 0;
        bool inQ = false;
        foreach (byte b in header)
        {
            if (quotes && b == CsvBytes.Quote) inQ = !inQ;
            else if (!inQ && b == (byte)'\t') tabs++;
            else if (!inQ && b == (byte)',') commas++;
        }
        return tabs > commas ? (byte)'\t' : (byte)',';
    }

    /// <summary>
    /// 列の指定（見出しの名前、または "#1" のような 1 始まりの列番号）を列の位置にする。
    /// 名前が見出しにあれば名前を優先する。
    /// </summary>
    public int FindColumn(string name)
    {
        int i = Array.IndexOf(Header, name);
        if (i >= 0) return i;
        if (name.StartsWith('#') && int.TryParse(name.AsSpan(1), out int k) && k >= 1 && k <= Header.Length) return k - 1;
        throw new ArgumentException($"列「{name}」が見出しにありません: {Path}（見出し: {string.Join(", ", Header)}）");
    }
}
