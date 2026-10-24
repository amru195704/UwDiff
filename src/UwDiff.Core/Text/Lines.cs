using System.Collections.Concurrent;

namespace UwDiff.Core.Text;

/// <summary>行の比べ方（空白・改行コード・大文字小文字の違いを無視するか）。</summary>
public sealed class LineCompare
{
    /// <summary>改行コード（\r\n と \n）の違いを無視する（行末の \r を比べない）。</summary>
    public bool IgnoreEol { get; init; } = true;
    /// <summary>空白の量の違いを無視する（続いた空白・タブを 1 つとみなし、行末の空白を無視）。</summary>
    public bool IgnoreSpaceChange { get; init; }
    /// <summary>空白・タブをすべて無視する。</summary>
    public bool IgnoreAllSpace { get; init; }
    /// <summary>ASCII の大文字・小文字を区別しない。</summary>
    public bool IgnoreCase { get; init; }

    internal bool Plain => !IgnoreSpaceChange && !IgnoreAllSpace && !IgnoreCase;

    /// <summary>比べる中身（改行を除いた行から、無視する部分を除いたもの）。Plain なら tmp を使わない。</summary>
    internal ReadOnlySpan<byte> Normalize(ReadOnlySpan<byte> line, ref byte[] tmp)
    {
        if (IgnoreEol && line.Length > 0 && line[^1] == (byte)'\r') line = line[..^1];
        if (Plain) return line;
        if (tmp.Length < line.Length) tmp = new byte[line.Length * 2];
        int o = 0;
        bool space = false;
        foreach (byte b0 in line)
        {
            byte b = b0;
            bool isSpace = b == (byte)' ' || b == (byte)'\t' || (b == (byte)'\r' && !IgnoreEol);
            if (isSpace && (IgnoreAllSpace || IgnoreSpaceChange))
            {
                space = true;
                continue;
            }
            if (space && IgnoreSpaceChange && o > 0) tmp[o++] = (byte)' ';
            space = false;
            if (IgnoreCase && b >= (byte)'A' && b <= (byte)'Z') b = (byte)(b + 32);
            tmp[o++] = b;
        }
        return tmp.AsSpan(0, o);
    }
}

/// <summary>読み込んだ行の塊（1 つの読み込み単位）。行は Data の中の位置で持つ（改行の \n は含まない。\r は含む）。</summary>
internal sealed class LineChunk
{
    public byte[] Data = Array.Empty<byte>();
    public int[] Start = new int[4096];
    public int[] Length = new int[4096];
    public ulong[] Hash = new ulong[4096];
    public int Count;
    /// <summary>この塊の最後の行が、ファイルの終わりで改行が無いまま終わっている。</summary>
    public bool LastUnterminated;

    public void Add(int start, int length, ulong hash)
    {
        if (Count == Start.Length)
        {
            int n = Count * 2;
            Array.Resize(ref Start, n);
            Array.Resize(ref Length, n);
            Array.Resize(ref Hash, n);
        }
        Start[Count] = start;
        Length[Count] = length;
        Hash[Count] = hash;
        Count++;
    }
}

/// <summary>
/// 1 本を先頭から読み、行に切ってハッシュを付け、塊にして渡す（ファイルごとに 1 本のスレッド）。
/// </summary>
internal sealed class LineProducer
{
    private readonly Stream _s;
    private readonly LineCompare _cmp;
    private readonly int _chunkBytes;
    public long BytesRead;
    /// <summary>LineIndex.Every 行ごとの、行の先頭の位置（読んだストリームの中の位置）。記録するときだけ。</summary>
    public List<long>? LineStarts;
    public long Lines;
    private long _chunkBase;   // いまの塊の Data[0] のストリームの中の位置

    public LineProducer(Stream s, LineCompare cmp, int chunkBytes)
    {
        _s = s;
        _cmp = cmp;
        _chunkBytes = chunkBytes;
    }

    public void Run(BlockingCollection<LineChunk> output, CancellationToken ct)
    {
        try
        {
            byte[] carry = Array.Empty<byte>();
            int carryLen = 0;
            byte[] tmp = new byte[256];
            bool eof = false;
            while (!eof)
            {
                ct.ThrowIfCancellationRequested();
                var c = new LineChunk();
                int size = Math.Max(_chunkBytes, carryLen * 2);
                c.Data = GC.AllocateUninitializedArray<byte>(size);
                carry.AsSpan(0, carryLen).CopyTo(c.Data);
                int end = carryLen;
                while (end < size)
                {
                    int n = _s.Read(c.Data, end, size - end);
                    if (n == 0) { eof = true; break; }
                    end += n;
                    BytesRead += n;
                }
                ReadOnlySpan<byte> all = c.Data.AsSpan(0, end);
                int pos = 0;
                while (pos < end)
                {
                    int k = all[pos..].IndexOf((byte)'\n');
                    if (k < 0) break;
                    AddLine(c, pos, k, ref tmp);
                    pos += k + 1;
                }
                if (eof && pos < end)
                {
                    // 改行で終わらない最後の行は、改行のある同じ中身の行とは別の行として扱う（unified diff で「改行なし」を表せるように）
                    AddLine(c, pos, end - pos, ref tmp, UnterminatedSalt);
                    c.LastUnterminated = true;
                    pos = end;
                }
                carryLen = end - pos;
                _chunkBase += pos;
                if (carryLen > 0)
                {
                    if (carry.Length < carryLen) carry = new byte[Math.Max(carryLen, carry.Length * 2)];
                    c.Data.AsSpan(pos, carryLen).CopyTo(carry);
                }
                if (c.Count > 0 || eof) output.Add(c, ct);
            }
        }
        finally
        {
            output.CompleteAdding();
        }
    }

    private const ulong UnterminatedSalt = 0x5A17_0F_E0F0_0001UL;

    private void AddLine(LineChunk c, int start, int len, ref byte[] tmp, ulong seed = 0)
    {
        if (LineStarts != null && Lines % LineIndex.Every == 0) LineStarts.Add(_chunkBase + start);
        Lines++;
        ReadOnlySpan<byte> v = _cmp.Normalize(c.Data.AsSpan(start, len), ref tmp);
        c.Add(start, len, Hashing.Hash(v, seed));
    }
}

/// <summary>
/// 比べる側から見た、まだ済んでいない行の並び（先頭から順に済ませて捨てる）。必要な分だけ塊を取り込む。
/// </summary>
internal sealed class LineWindow
{
    private readonly BlockingCollection<LineChunk> _q;
    private readonly List<LineChunk> _chunks = new();
    private int _head;
    private int _avail;
    private bool _eof;
    private bool _lastUnterminated;
    private readonly CancellationToken _ct;

    /// <summary>先頭の行の行番号（1 始まり）。</summary>
    public long LineNo { get; private set; } = 1;
    public int Available => _avail;
    /// <summary>もう取り込む塊が無い（Available が残りのすべて）。</summary>
    public bool Eof => _eof;
    /// <summary>取り込んでいる行のバイト数（見積もり）。</summary>
    public long BufferedBytes { get; private set; }

    public LineWindow(BlockingCollection<LineChunk> q, CancellationToken ct)
    {
        _q = q;
        _ct = ct;
    }

    /// <summary>n 行が使えるようにする（足りなければ終わりまで）。使える行数を返す。</summary>
    public int Ensure(int n)
    {
        while (_avail < n && !_eof)
        {
            if (_q.TryTake(out LineChunk? c, Timeout.Infinite, _ct))
            {
                _chunks.Add(c);
                _avail += c.Count;
                BufferedBytes += c.Data.Length;
                if (c.LastUnterminated) _lastUnterminated = true;
            }
            else _eof = true;
        }
        return Math.Min(n, _avail);
    }

    public bool AtEnd => _avail == 0 && Ensure(1) == 0;

    /// <summary>i 番目（先頭から）の行。</summary>
    public void Get(int i, out byte[] data, out int start, out int length, out ulong hash)
    {
        i += _head;
        foreach (LineChunk c in _chunks)
        {
            if (i < c.Count)
            {
                data = c.Data;
                start = c.Start[i];
                length = c.Length[i];
                hash = c.Hash[i];
                return;
            }
            i -= c.Count;
        }
        throw new ArgumentOutOfRangeException(nameof(i));
    }

    public ReadOnlySpan<byte> Line(int i)
    {
        Get(i, out byte[] d, out int s, out int l, out _);
        return d.AsSpan(s, l);
    }

    public ulong HashAt(int i)
    {
        Get(i, out _, out _, out _, out ulong h);
        return h;
    }

    /// <summary>i 番目の行がファイルの最後の行で、改行が無いまま終わっているか。</summary>
    public bool IsUnterminated(int i) => _lastUnterminated && _eof && i == _avail - 1;

    /// <summary>先頭の chunk 内で、続けて使える行（塊の境目まで）。速い一致の走査用。</summary>
    internal LineChunk? FirstChunk(out int index)
    {
        index = _head;
        return _chunks.Count > 0 ? _chunks[0] : null;
    }

    public void Consume(int n)
    {
        _head += n;
        _avail -= n;
        LineNo += n;
        while (_chunks.Count > 0 && _head >= _chunks[0].Count)
        {
            _head -= _chunks[0].Count;
            BufferedBytes -= _chunks[0].Data.Length;
            _chunks.RemoveAt(0);
        }
    }
}
