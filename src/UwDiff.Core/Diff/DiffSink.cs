using System.Buffers;
using System.Text;

namespace UwDiff.Core.Diff;

/// <summary>
/// 照合スレッドごとの差分の書き出し先。メモリに貯め、一定量を超えたら一時ファイルへ出し、最後にまとめて 1 本にする。
/// 形は「区分,キー列…,列,旧値,新値」・UTF-8（キーが複数列なら、キーの列がその数だけ並ぶ）。値は UTF-8 に直して書く。
/// </summary>
internal sealed class DiffSink : IDisposable
{
    private const int SpillAt = 32 << 20;
    private static readonly byte[] KindAdded = Encoding.UTF8.GetBytes("追加");
    private static readonly byte[] KindDeleted = Encoding.UTF8.GetBytes("削除");
    private static readonly byte[] KindChanged = Encoding.UTF8.GetBytes("変更");

    private readonly ArrayBufferWriter<byte> _buf = new(1 << 20);
    private readonly bool _enabled;
    private readonly Encoding _enc;
    private readonly bool _encIsUtf8;
    private readonly Func<string> _newPath;
    private readonly byte[]? _extra;   // 区分の次に足す列（「,値」の形。CSV として書ける形にしたもの）
    private FileStream? _file;
    private char[] _chars = new char[1024];
    private byte[] _tmp = new byte[4096];

    public long Added, Deleted, ChangedRows, ChangedCells, SameRows;

    public DiffSink(bool enabled, Encoding workEncoding, bool workIsUtf8, Func<string> newPath, string? extraColumn = null)
    {
        if (extraColumn != null)
        {
            var w = new ArrayBufferWriter<byte>();
            w.Write(","u8);
            CsvBytes.WriteField(w, Encoding.UTF8.GetBytes(extraColumn), (byte)',');
            _extra = w.WrittenSpan.ToArray();
        }
        _enabled = enabled;
        _enc = workEncoding;
        _encIsUtf8 = workIsUtf8;
        _newPath = newPath;
    }

    public void WriteAddedOrDeleted(bool added, ReadOnlySpan<byte> row, List<CsvBytes.Field> keyParts)
    {
        if (added) Added++; else Deleted++;
        if (!_enabled) return;
        Put(added ? KindAdded : KindDeleted);
        if (_extra != null) Put(_extra);
        PutKey(row, keyParts);
        Put(",,,\n"u8);
        MaybeSpill();
    }

    public void WriteChanged(ReadOnlySpan<byte> row, List<CsvBytes.Field> keyParts, byte[] columnNameUtf8,
        ReadOnlySpan<byte> oldRaw, bool oldQuoted, ReadOnlySpan<byte> newRaw, bool newQuoted)
    {
        ChangedCells++;
        if (!_enabled) return;
        Put(KindChanged);
        if (_extra != null) Put(_extra);
        PutKey(row, keyParts);
        PutByte((byte)',');
        CsvBytes.WriteField(_buf, columnNameUtf8, (byte)',');
        PutByte((byte)',');
        PutValue(oldRaw, oldQuoted);
        PutByte((byte)',');
        PutValue(newRaw, newQuoted);
        PutByte((byte)'\n');
        MaybeSpill();
    }

    /// <summary>貯めた差分をすべて out へ書く。</summary>
    public void CopyTo(Stream output)
    {
        if (_file != null)
        {
            _file.Flush();
            _file.Position = 0;
            _file.CopyTo(output, 1 << 20);
        }
        output.Write(_buf.WrittenSpan);
    }

    public void Dispose()
    {
        _file?.Dispose();
        _file = null;
    }

    private void MaybeSpill()
    {
        if (_buf.WrittenCount < SpillAt) return;
        _file ??= new FileStream(_newPath(), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
        _file.Write(_buf.WrittenSpan);
        _buf.Clear();
    }

    private void PutKey(ReadOnlySpan<byte> row, List<CsvBytes.Field> keyParts)
    {
        foreach (CsvBytes.Field f in keyParts)
        {
            PutByte((byte)',');
            PutValue(row.Slice(f.Start, f.Length), f.Quoted);
        }
    }

    private void Put(ReadOnlySpan<byte> s) => _buf.Write(s);

    private void PutByte(byte b)
    {
        Span<byte> d = _buf.GetSpan(1);
        d[0] = b;
        _buf.Advance(1);
    }

    /// <summary>生の列（引用符つきかも）を、値に直して UTF-8 で CSV の 1 列として書く。</summary>
    private void PutValue(ReadOnlySpan<byte> raw, bool quoted)
    {
        if (_tmp.Length < raw.Length) _tmp = new byte[raw.Length * 2];
        int n = CsvBytes.Unquote(raw, quoted, _tmp);
        ReadOnlySpan<byte> v = _tmp.AsSpan(0, n);
        if (_encIsUtf8)
        {
            CsvBytes.WriteField(_buf, v, (byte)',');
            return;
        }
        int nc = _enc.GetMaxCharCount(n);
        if (_chars.Length < nc) _chars = new char[nc * 2];
        int c = _enc.GetChars(v, _chars);
        int nb = Encoding.UTF8.GetMaxByteCount(c);
        byte[] u = ArrayPool<byte>.Shared.Rent(nb);
        try
        {
            int len = Encoding.UTF8.GetBytes(_chars.AsSpan(0, c), u);
            CsvBytes.WriteField(_buf, u.AsSpan(0, len), (byte)',');
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(u);
        }
    }
}
