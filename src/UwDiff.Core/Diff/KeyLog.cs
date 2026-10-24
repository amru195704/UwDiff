using System.Runtime.InteropServices;

namespace UwDiff.Core.Diff;

/// <summary>
/// キーの重複を数えるための、キーのハッシュの記録（1 区画・片側ぶん）。
/// 決めた量まではメモリに持ち、超えた分は一時ファイルへ書き出す（段階 0 は全部メモリで、10GB で約 1.7GB 使った）。
/// 読み終えた後に区画ごとに読み戻して並べ、隣と同じものを数える（区画ごとなので、読み戻す量は全体の 1/区画数）。
/// </summary>
internal sealed class KeyLog : IDisposable
{
    private ulong[] _buf = new ulong[1024];
    private int _n;
    private readonly int _memCap;
    private readonly Func<string> _newPath;
    private FileStream? _file;
    private long _onDisk;

    public KeyLog(int memCapEntries, Func<string> newPath)
    {
        _memCap = Math.Max(1024, memCapEntries);
        _newPath = newPath;
    }

    public long Count => _onDisk + _n;
    public long BytesOnDisk => _onDisk * 8;

    public void Add(ulong v)
    {
        if (_n == _buf.Length)
        {
            if (_buf.Length < _memCap) Array.Resize(ref _buf, Math.Min(_memCap, _buf.Length + _buf.Length / 2));
            else Spill();
        }
        _buf[_n++] = v;
    }

    private void Spill()
    {
        _file ??= new FileStream(_newPath(), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
        _file.Write(MemoryMarshal.AsBytes(_buf.AsSpan(0, _n)));
        _onDisk += _n;
        _n = 0;
    }

    /// <summary>全部を 1 つの配列に読み戻す（記録は空になる）。</summary>
    public ulong[] TakeAll(out int count)
    {
        long total = Count;
        if (total > Array.MaxLength) throw new InvalidOperationException("1 区画のキーが多すぎます（区画の数を増やしてください: -shards）");
        count = (int)total;
        if (_file == null)
        {
            ulong[] mem = _buf;
            _buf = Array.Empty<ulong>();
            _n = 0;
            return mem;
        }
        ulong[] all = GC.AllocateUninitializedArray<ulong>(count);
        _file.Flush();
        _file.Position = 0;
        Span<byte> dest = MemoryMarshal.AsBytes(all.AsSpan(0, (int)_onDisk));
        while (dest.Length > 0)
        {
            int k = _file.Read(dest);
            if (k == 0) throw new EndOfStreamException("キーの記録の一時ファイルが途中で切れています");
            dest = dest[k..];
        }
        _buf.AsSpan(0, _n).CopyTo(all.AsSpan((int)_onDisk));
        Dispose();
        _buf = Array.Empty<ulong>();
        _n = 0;
        _onDisk = 0;
        return all;
    }

    public void Dispose()
    {
        _file?.Dispose();
        _file = null;
    }
}
