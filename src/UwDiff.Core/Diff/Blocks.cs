using System.Collections.Concurrent;
using System.Text;

namespace UwDiff.Core.Diff;

/// <summary>
/// ファイルから読んだ塊。先頭に Reserve バイトの空きを持ち、前の塊で切れた行の残りをそこへ写してから行に切る。
/// 照合スレッドが使い終わる（Ref が 0 になる）まで再利用しない。
/// </summary>
internal sealed class Block
{
    public readonly byte[] Data;
    public int Start;
    public int End;
    public bool Last;
    public int Ref;
    private readonly BlockPool _pool;

    public Block(int size, BlockPool pool)
    {
        Data = GC.AllocateUninitializedArray<byte>(size);
        _pool = pool;
    }

    public void AddRef() => Interlocked.Increment(ref Ref);

    public void Release()
    {
        if (Interlocked.Decrement(ref Ref) == 0) _pool.Return(this);
    }
}

internal sealed class BlockPool
{
    private readonly BlockingCollection<Block> _free = new();

    public BlockPool(int count, int size)
    {
        for (int i = 0; i < count; i++) _free.Add(new Block(size, this));
    }

    public Block Rent(CancellationToken ct)
    {
        Block b = _free.Take(ct);
        b.Ref = 1; // 読む側・行に切る側の持ち分
        b.Last = false;
        return b;
    }

    public void Return(Block b) => _free.Add(b);
}

/// <summary>
/// 2 本を同じ割合ずつ読むための歩調合わせ。片方が先に進みすぎると、相手が追いつくまで待つ。
/// 行順がほぼ同じ 2 本では、これで待ち行（相手がまだ来ていない行）が小さく保たれる。
/// </summary>
internal sealed class Pacer
{
    private readonly long[] _read = new long[2];
    private readonly long[] _total;
    private readonly bool[] _done = new bool[2];
    private readonly long _slack;
    private readonly object _lock = new();

    public Pacer(long total0, long total1, long slack)
    {
        _total = new[] { Math.Max(1, total0), Math.Max(1, total1) };
        _slack = slack;
    }

    public void Advance(int side, long n)
    {
        lock (_lock)
        {
            _read[side] += n;
            Monitor.PulseAll(_lock);
        }
    }

    public void Done(int side)
    {
        lock (_lock)
        {
            _done[side] = true;
            Monitor.PulseAll(_lock);
        }
    }

    public void WaitTurn(int side, CancellationToken ct)
    {
        lock (_lock)
        {
            while (!_done[1 - side] && !ct.IsCancellationRequested)
            {
                // 相手の進み具合を自分のファイルの大きさに直して比べる
                double expected = (double)_read[1 - side] * _total[side] / _total[1 - side];
                if (_read[side] - expected <= _slack) return;
                Monitor.Wait(_lock, 50);
            }
        }
    }
}

/// <summary>
/// 1 本のファイルを先頭から順に読み、塊にして渡す。文字コードを UTF-8 にそろえる必要があるときは、ここで変換する。
/// </summary>
internal sealed class FileReader
{
    private readonly string _path;
    private readonly BlockPool _pool;
    private readonly int _reserve;
    private readonly int _payload;
    private readonly Encoding? _transcodeFrom;
    private readonly int _skip;
    public long BytesRead;
    private readonly Pacer? _pacer;
    private readonly int _side;

    /// <param name="bomLength">BOM の長さ。UTF-8 に直すときは読む側で飛ばす（バイト列のまま読むときは行に切る側で飛ばす）。</param>
    public FileReader(string path, BlockPool pool, int reserve, int payload, Encoding? transcodeFrom, int bomLength, Pacer? pacer = null, int side = 0)
    {
        _skip = transcodeFrom != null ? bomLength : 0;
        _pacer = pacer;
        _side = side;
        _path = path;
        _pool = pool;
        _reserve = reserve;
        _payload = payload;
        _transcodeFrom = transcodeFrom;
    }

    public void Run(BlockingCollection<Block> output, CancellationToken ct)
    {
        try
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
            if (_skip > 0) fs.Seek(_skip, SeekOrigin.Begin);
            if (_transcodeFrom == null) ReadRaw(fs, output, ct);
            else ReadTranscoded(fs, output, ct);
        }
        finally
        {
            _pacer?.Done(_side);
            output.CompleteAdding();
        }
    }

    private void ReadRaw(FileStream fs, BlockingCollection<Block> output, CancellationToken ct)
    {
        while (true)
        {
            Block b = _pool.Rent(ct);
            _pacer?.WaitTurn(_side, ct);
            int n = ReadFully(fs, b.Data.AsSpan(_reserve, _payload));
            Interlocked.Add(ref BytesRead, n);
            _pacer?.Advance(_side, n);
            b.Start = _reserve;
            b.End = _reserve + n;
            b.Last = n < _payload;
            output.Add(b, ct);
            if (b.Last) return;
        }
    }

    private void ReadTranscoded(FileStream fs, BlockingCollection<Block> output, CancellationToken ct)
    {
        // UTF-8 にすると最大 3 倍（半角カナは CP932 の 1 バイト → UTF-8 の 3 バイト。UTF-16 は 1.5 倍）になるので、生で読む量を 1/3 にする
        int rawSize = _payload / 3 - 16;
        byte[] raw = GC.AllocateUninitializedArray<byte>(rawSize);
        char[] chars = GC.AllocateUninitializedArray<char>(rawSize + 16);
        Decoder dec = _transcodeFrom!.GetDecoder();
        Encoder enc = new UTF8Encoding(false).GetEncoder();
        while (true)
        {
            _pacer?.WaitTurn(_side, ct);
            int n = ReadFully(fs, raw);
            Interlocked.Add(ref BytesRead, n);
            _pacer?.Advance(_side, n);
            bool last = n < rawSize;
            int nc = dec.GetChars(raw, 0, n, chars, 0, last);
            Block b = _pool.Rent(ct);
            int nb = enc.GetBytes(chars, 0, nc, b.Data, _reserve, last);
            b.Start = _reserve;
            b.End = _reserve + nb;
            b.Last = last;
            output.Add(b, ct);
            if (last) return;
        }
    }

    private static int ReadFully(FileStream fs, Span<byte> dest)
    {
        int total = 0;
        while (total < dest.Length)
        {
            int n = fs.Read(dest[total..]);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
