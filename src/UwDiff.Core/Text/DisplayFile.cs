using System.Text;
using UwView.Core.Documents;

namespace UwDiff.Core.Text;

/// <summary>
/// 画面に左右に並べるための、行を好きな所から読めるファイル。
/// 普通のテキスト（UTF-8・CP932・EUC-JP）はそのまま使い、圧縮・Word・Excel・PDF・UTF-16 は、比べたときと同じ行の並びを
/// 平らな一時ファイル（UTF-16 と Word などは UTF-8）に書き出してから使う。行の切り方は比較と同じなので、変更の位置の行番号がそのまま合う。
/// </summary>
public sealed class DisplayFile
{
    public required string Path { get; init; }
    public required long Offset { get; init; }
    public required FileEncoding Encoding { get; init; }
    public required LineIndex Index { get; init; }
    /// <summary>書き出した一時ファイルか（閉じるときに消す）。</summary>
    public bool Temporary { get; init; }

    /// <param name="savedIndex">比べたときに作った索引のファイル（あれば読み直さずに使う。元のファイルが変わっていれば作り直す）。</param>
    public static DisplayFile Prepare(string path, FileEncoding? encoding, bool raw, string tempDir, Action<long, long>? progress = null,
        CancellationToken ct = default, string? savedIndex = null)
    {
        InputFile f = InputFile.Probe(path);
        if (savedIndex != null && encoding is FileEncoding se && !f.IsOffice && !f.IsCompressed && LineIndex.Load(savedIndex, path) is { } saved)
            return new DisplayFile { Path = path, Offset = saved.StartOffset, Encoding = se, Index = saved };
        string file = path;
        bool temp = false;
        if (f.IsOffice || f.IsCompressed)
        {
            file = System.IO.Path.Combine(tempDir, $"view-{Guid.NewGuid():N}.txt");
            using Stream src = f.IsOffice ? OfficeText.OpenStream(path, f.Office, raw) : f.OpenContent();
            using var dst = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
            src.CopyTo(dst, 1 << 20);
            temp = true;
            if (f.IsOffice) encoding = FileEncoding.Utf8;
        }
        // 文字コードと BOM
        byte[] head = new byte[1 << 20];
        int n;
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            n = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        FileEncoding det = TextEncodings.Detect(head.AsSpan(0, n), n < head.Length, out int bom);
        FileEncoding enc = encoding ?? det;
        if (encoding != null && encoding != det)
            bom = TextEncodings.Bom(enc, 1) is { Length: > 0 } b && head.AsSpan(0, n).StartsWith(b) ? b.Length : 0;
        if (!TextEncodings.IsAsciiCompatible(enc))
        {
            // UTF-16 は UTF-8 に直す（行の位置を \n で探せるように）
            string utf8 = System.IO.Path.Combine(tempDir, $"view-{Guid.NewGuid():N}.txt");
            using (var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                src.Position = bom;
                using Stream conv = System.Text.Encoding.CreateTranscodingStream(src, TextEncodings.Get(enc), new UTF8Encoding(false), leaveOpen: false);
                using var dst = new FileStream(utf8, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
                conv.CopyTo(dst, 1 << 20);
            }
            if (temp) File.Delete(file);
            file = utf8;
            temp = true;
            enc = FileEncoding.Utf8;
            bom = 0;
        }
        return new DisplayFile
        {
            Path = file,
            Offset = bom,
            Encoding = enc,
            Index = LineIndex.Build(file, bom, progress, ct),
            Temporary = temp,
        };
    }
}
