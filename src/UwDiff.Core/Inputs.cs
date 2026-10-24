using System.Text;
using UwDiff.Core.Text;
using UwView.Core;
using UwView.Core.Documents;

namespace UwDiff.Core;

/// <summary>
/// 比べる 1 本のファイルの種類と、中身の開き方。
/// 圧縮（gz・bz2・xz・lzma・zst・lz4・br）は展開した中身、Word・PDF は取り出した文字、Excel はシートごとの表か取り出した文字を比べる。
/// 見分けと取り出しは UwView.Core の部品（UwView.Formats に写したもの）を使う。
/// </summary>
public sealed class InputFile
{
    public required string Path { get; init; }
    public CompressedKind Compression { get; init; }
    public OfficeKind Office { get; init; }
    /// <summary>ファイルの大きさ（圧縮なら圧縮したまま）。</summary>
    public long Length { get; init; }

    public bool IsCompressed => Compression != CompressedKind.None;
    public bool IsOffice => Office != OfficeKind.None;

    /// <summary>中身の名前（圧縮なら拡張子を除いた名前。表かどうかの見分けに使う）。</summary>
    public string InnerName => IsCompressed ? CompressedFormats.PlainPath(Path, Compression) : Path;

    /// <summary>中身が CSV・TSV らしい名前か。</summary>
    public bool LooksTable => System.IO.Path.GetExtension(InnerName).ToLowerInvariant() is ".csv" or ".tsv" or ".tab";

    /// <summary>種類の表示名。</summary>
    public string KindName => IsOffice ? Office switch { OfficeKind.Docx => "Word", OfficeKind.Xlsx => "Excel", _ => "PDF" }
        : IsCompressed ? CompressedFormats.Name(Compression) + " の中身" : "テキスト";

    public static InputFile Probe(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) throw new FileNotFoundException("ファイルが見つかりません", path);
        OfficeKind office = OfficeText.Check(path);   // 読めない Word・Excel・PDF は OfficeRejectedException
        CompressedKind comp = office == OfficeKind.None ? CompressedFormats.Sniff(path) : CompressedKind.None;
        return new InputFile { Path = path, Compression = comp, Office = office, Length = fi.Length };
    }

    /// <summary>中身（展開したもの）を先頭から読むストリーム。Word・Excel・PDF には使わない。</summary>
    public Stream OpenContent() => IsCompressed
        ? CompressedFormats.Open(Path, Compression)
        : new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);

    /// <summary>中身の大きさの見積もり（圧縮は展開後の見積もり）。</summary>
    public long ContentLength => IsCompressed ? Math.Max(Length, CompressedInput.GuessPlainSize(Path)) : Length;

    /// <summary>テキストとして比べるときの読み方。Word・Excel・PDF は取り出した文字（UTF-8）。</summary>
    public TextSource AsText(FileEncoding? encoding, bool raw = false) => new()
    {
        Name = Path,
        Open = IsOffice ? () => OfficeText.OpenStream(Path, Office, raw) : OpenContent,
        Encoding = IsOffice ? FileEncoding.Utf8 : encoding,
        Length = IsOffice ? Length : ContentLength,
        Modified = File.GetLastWriteTime(Path),
    };
}

/// <summary>
/// Excel のシートを、1 枚ずつ表（TSV・UTF-8・引用符なし）にして一時ファイルに書く。シートの 1 行目を見出しとする。
/// セルの値は UwView.Core の取り出し（既定は表示されている値、raw なら元の値）のまま。セルの中のタブ・改行は空白になっている。
/// </summary>
public static class ExcelSheets
{
    /// <summary>シート名 → 一時ファイルのパス（ブックの順）。</summary>
    public static List<(string Sheet, string Path)> Split(string xlsxPath, bool raw, Func<string> newTempPath)
    {
        var result = new List<(string, string)>();
        string? current = null;
        StreamWriter? w = null;
        try
        {
            foreach (string line in OfficeText.Lines(xlsxPath, OfficeKind.Xlsx, raw))
            {
                // 「シート名!行番号<TAB>A<TAB>B…」。シート名に ! があってもよいように、タブの前の最後の ! で切る
                int tab = line.IndexOf('\t');
                string label = tab < 0 ? line : line[..tab];
                int bang = label.LastIndexOf('!');
                string sheet = bang < 0 ? label : label[..bang];
                string cells = tab < 0 ? "" : line[(tab + 1)..];
                if (sheet != current)
                {
                    w?.Dispose();
                    current = sheet;
                    string p = newTempPath();
                    result.Add((sheet, p));
                    w = new StreamWriter(p, false, new UTF8Encoding(false), 1 << 16) { NewLine = "\n" };
                }
                w!.WriteLine(cells);
            }
        }
        finally
        {
            w?.Dispose();
        }
        return result;
    }
}
