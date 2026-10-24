using System.Text;

namespace UwDiff.Core;

/// <summary>
/// ストリームを先頭から 1 行ずつ読む（引用符の中の改行は行の区切りにしない）。行と、その行の改行（\r\n・\n・ファイルの終わりなら空）を返す。
/// 返した行は次に Next を呼ぶまで有効。反映（apply）のように 1 本を順に読むだけの処理で使う。
/// </summary>
public sealed class RowReader
{
    private const int MaxRow = 256 << 20;
    private readonly Stream _s;
    private readonly bool _quotes;
    private byte[] _buf = new byte[1 << 20];
    private int _start, _end;
    private bool _eof;

    public RowReader(Stream s, bool quotes)
    {
        _s = s;
        _quotes = quotes;
    }

    /// <summary>
    /// ファイルを開き、BOM を飛ばし、UTF-8・CP932・EUC-JP ならそのまま、UTF-16 なら UTF-8 に直して読むストリームを返す。
    /// </summary>
    public static Stream OpenWork(CsvFile f)
    {
        var fs = new FileStream(f.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        fs.Seek(f.BomLength, SeekOrigin.Begin);
        if (TextEncodings.IsAsciiCompatible(f.Encoding)) return fs;
        return Encoding.CreateTranscodingStream(fs, TextEncodings.Get(f.Encoding), new UTF8Encoding(false), leaveOpen: false);
    }

    public bool Next(out ReadOnlySpan<byte> row, out ReadOnlySpan<byte> newline)
    {
        while (true)
        {
            int nl = CsvBytes.FindRowEnd(_buf.AsSpan(0, _end), _start, _quotes);
            if (nl >= 0)
            {
                int len = nl - _start;
                bool cr = len > 0 && _buf[nl - 1] == (byte)'\r';
                row = _buf.AsSpan(_start, cr ? len - 1 : len);
                newline = _buf.AsSpan(cr ? nl - 1 : nl, cr ? 2 : 1);
                _start = nl + 1;
                return true;
            }
            if (_eof)
            {
                if (_start >= _end)
                {
                    row = default;
                    newline = default;
                    return false;
                }
                row = _buf.AsSpan(_start, _end - _start);
                newline = default;
                _start = _end;
                return true;
            }
            Fill();
        }
    }

    private void Fill()
    {
        if (_start > 0)
        {
            Buffer.BlockCopy(_buf, _start, _buf, 0, _end - _start);
            _end -= _start;
            _start = 0;
        }
        if (_end == _buf.Length)
        {
            if (_buf.Length >= MaxRow) throw new InvalidDataException($"1 行が {MaxRow >> 20}MB を超えています。引用符の閉じ忘れなら -quote off を試してください");
            Array.Resize(ref _buf, _buf.Length * 2);
        }
        int n = _s.Read(_buf, _end, _buf.Length - _end);
        if (n == 0) _eof = true;
        _end += n;
    }
}
