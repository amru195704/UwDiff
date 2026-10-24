using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UwDiff.Core.Diff;

/// <summary>行の切り出し結果（塊の中の位置とキーのハッシュ）。</summary>
internal struct RowRef
{
    public int Start;
    public int Length;
    public int KeyStart;   // 行の先頭からのオフセット（キーが複数列のときは使わない）
    public int KeyLength;
    public bool KeyQuoted;
    public ulong KeyHash;
}

/// <summary>
/// 待ち行の中身を入れておく場所。1 件は「見出し 9 バイト（行の長さ・キーの位置と長さ・引用符の有無）＋行のバイト列」。
/// 使い終わった分が増えたら、待っている行だけを新しい場所へ詰め直す。
/// </summary>
/// <summary>待ち行に使っているメモリの合計（全区画で共有。塊を確保・返すときだけ足し引きする）。</summary>
internal sealed class MemoryMeter
{
    public long Bytes;
    public void Add(long n) => Interlocked.Add(ref Bytes, n);
    public long Read() => Volatile.Read(ref Bytes);
}

internal sealed class Arena
{
    public const int ChunkBits = 22;
    public const int ChunkSize = 1 << ChunkBits;   // 4MB
    public const int HeaderSize = 9;
    private static readonly ConcurrentBag<byte[]> s_spare = new();

    public readonly List<byte[]> Chunks = new();
    private readonly MemoryMeter _meter;
    private int _pos = ChunkSize;
    public long Allocated;
    public long Live;

    public Arena(MemoryMeter meter) => _meter = meter;

    public static void ReleaseSpare() => s_spare.Clear();

    /// <summary>行を入れ、場所（塊の番号 × ChunkSize ＋ オフセット）を返す。</summary>
    public long Add(ReadOnlySpan<byte> row, int keyStart, int keyLength, bool keyQuoted)
    {
        int need = HeaderSize + row.Length;
        byte[] chunk;
        int off;
        if (need > ChunkSize)
        {
            chunk = new byte[need];
            Chunks.Add(chunk);
            _meter.Add(need);
            off = 0;
            _pos = ChunkSize;
        }
        else
        {
            if (_pos + need > ChunkSize)
            {
                Chunks.Add(s_spare.TryTake(out byte[]? c) ? c : GC.AllocateUninitializedArray<byte>(ChunkSize));
                _meter.Add(ChunkSize);
                _pos = 0;
            }
            chunk = Chunks[^1];
            off = _pos;
            _pos += need;
        }
        Span<byte> d = chunk.AsSpan(off);
        BinaryWrite(d, row.Length, keyStart, keyLength, keyQuoted);
        row.CopyTo(d[HeaderSize..]);
        Allocated += need;
        Live += need;
        return ((long)(Chunks.Count - 1) << ChunkBits) | (uint)off;
    }

    /// <summary>詰め直し用: 別の Arena の 1 件をそのまま写す。</summary>
    public long CopyFrom(Arena src, long loc)
    {
        ReadOnlySpan<byte> rec = src.Record(loc);
        int len = MemoryMarshal.Read<int>(rec);
        ReadOnlySpan<byte> whole = rec[..(HeaderSize + len)];
        ushort ks = MemoryMarshal.Read<ushort>(rec[4..]);
        ushort kl = MemoryMarshal.Read<ushort>(rec[6..]);
        long nl = Add(whole[HeaderSize..], ks, kl, rec[8] != 0);
        return nl;
    }

    private static void BinaryWrite(Span<byte> d, int len, int keyStart, int keyLength, bool keyQuoted)
    {
        MemoryMarshal.Write(d, in len);
        // キーの位置は 65535 を超えたら 0xFFFF（使うときに行から探し直す）
        ushort ks = keyStart > 0xFFFE || keyLength > 0xFFFE ? (ushort)0xFFFF : (ushort)keyStart;
        ushort kl = ks == 0xFFFF ? (ushort)0xFFFF : (ushort)keyLength;
        MemoryMarshal.Write(d[4..], in ks);
        MemoryMarshal.Write(d[6..], in kl);
        d[8] = keyQuoted ? (byte)1 : (byte)0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> Record(long loc) => Chunks[(int)(loc >> ChunkBits)].AsSpan((int)(loc & (ChunkSize - 1)));

    public void Free(int rowLength) => Live -= HeaderSize + rowLength;

    public void RecycleAll()
    {
        long n = 0;
        foreach (byte[] c in Chunks)
        {
            n += c.Length;
            if (c.Length == ChunkSize) s_spare.Add(c);
        }
        _meter.Add(-n);
        Chunks.Clear();
        Live = Allocated = 0;
        _pos = ChunkSize;
    }
}

/// <summary>
/// 待ち行を逃がす一時ファイル（1 区画ぶん）。1 件は「旧新 1・キーのハッシュ 8・行の長さ 4・キーの位置 4・キーの長さ 4・引用符 1＋行」。
/// </summary>
internal sealed class SpillFile : IDisposable
{
    public const int RecordHeader = 22;
    public readonly string Path;
    private FileStream? _fs;
    private readonly byte[] _hdr = new byte[RecordHeader];
    public long Bytes;

    public SpillFile(string path)
    {
        Path = path;
        _fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
    }

    public void Write(ReadOnlySpan<byte> row, in RowRef r, byte side)
    {
        Span<byte> h = _hdr;
        h[0] = side;
        ulong kh = r.KeyHash;
        int len = row.Length, ks = r.KeyStart, kl = r.KeyLength;
        MemoryMarshal.Write(h[1..], in kh);
        MemoryMarshal.Write(h[9..], in len);
        MemoryMarshal.Write(h[13..], in ks);
        MemoryMarshal.Write(h[17..], in kl);
        h[21] = r.KeyQuoted ? (byte)1 : (byte)0;
        _fs!.Write(h);
        _fs.Write(row);
        Bytes += RecordHeader + row.Length;
    }

    public void Close()
    {
        _fs?.Dispose();
        _fs = null;
    }

    public void Dispose() => Close();

    /// <summary>一時ファイルの件を先頭から順に読む。action に（行のバイト列を含む配列・行の位置・旧新）を渡す。</summary>
    public static void ReadAll(string path, Action<byte[], RowRef, byte> action)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1 << 20, FileOptions.SequentialScan);
        byte[] h = new byte[RecordHeader];
        byte[] buf = new byte[1 << 16];
        while (true)
        {
            int got = ReadFully(fs, h);
            if (got == 0) return;
            if (got < RecordHeader) throw new EndOfStreamException("待ち行の一時ファイルが途中で切れています: " + path);
            byte side = h[0];
            RowRef r;
            r.KeyHash = MemoryMarshal.Read<ulong>(h.AsSpan(1));
            r.Length = MemoryMarshal.Read<int>(h.AsSpan(9));
            r.KeyStart = MemoryMarshal.Read<int>(h.AsSpan(13));
            r.KeyLength = MemoryMarshal.Read<int>(h.AsSpan(17));
            r.KeyQuoted = h[21] != 0;
            r.Start = 0;
            if (buf.Length < r.Length) buf = new byte[Math.Max(r.Length, buf.Length * 2)];
            if (ReadFully(fs, buf.AsSpan(0, r.Length)) < r.Length) throw new EndOfStreamException("待ち行の一時ファイルが途中で切れています: " + path);
            action(buf, r, side);
        }
    }

    private static int ReadFully(Stream s, Span<byte> dest)
    {
        int total = 0;
        while (total < dest.Length)
        {
            int n = s.Read(dest[total..]);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}

/// <summary>
/// キーのハッシュで振り分けた 1 区画。1 つの照合スレッドだけが触る（鍵は要らない）。
/// 待ち行は開番地法の表（1 件 16 バイト: キーのハッシュ・場所と旧新の印）に持ち、行の中身は Arena に置く。
/// 行が来たら、相手側の待ち行に同じキーがあれば突き合わせて捨て、無ければ自分を待ち行に入れる。
/// 待ち行の合計がメモリの上限を超えたら、区画の待ち行を一時ファイルへ逃がし、以後この区画に来た行も一時ファイルへ書く。
/// 逃がした区画は読み終えた後に一時ファイルから照合する（大きすぎればハッシュでさらに分けてから）。
/// </summary>
internal sealed class Shard
{
    private struct Slot
    {
        public ulong KeyHash;
        public ulong Loc;   // 0 = 空き。(場所 + 1) << 1 | 旧新
    }

    private readonly KeyedDiffEngine _e;
    private Slot[] _slots = new Slot[1024];
    private int _mask = 1023;          // 表の大きさは 2 のべき
    private int _used;
    private Arena _arena;
    private readonly List<CsvBytes.Field> _fo = new(16);
    private readonly List<CsvBytes.Field> _fn = new(16);
    private readonly List<CsvBytes.Field> _keyParts = new(4);
    private SpillFile? _spill;
    private bool _resolving;

    public long PendingRows => _used;
    public long PendingBytes => _arena.Live;
    public bool Spilled => _spill != null;

    // 重複を数えるためのキーのハッシュ（旧・新）
    public readonly KeyLog OldKeys;
    public readonly KeyLog NewKeys;

    public Shard(KeyedDiffEngine e)
    {
        _e = e;
        _arena = new Arena(e.Meter);
        e.Meter.Add((long)_slots.Length * 16);
        OldKeys = new KeyLog(e.KeyLogEntriesPerSide, () => e.NewTempPath("keys"));
        NewKeys = new KeyLog(e.KeyLogEntriesPerSide, () => e.NewTempPath("keys"));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Home(ulong h) => (int)(h & (ulong)_mask);

    public void Process(byte[] data, ref RowRef r, byte side, DiffSink sink)
    {
        if (_e.CheckDuplicates)
        {
            if (side == 0) OldKeys.Add(r.KeyHash);
            else NewKeys.Add(r.KeyHash);
        }
        if (_spill != null)
        {
            _spill.Write(data.AsSpan(r.Start, r.Length), in r, side);
            return;
        }
        ProcessCore(data, ref r, side, sink);
    }

    private void ProcessCore(byte[] data, ref RowRef r, byte side, DiffSink sink)
    {
        ReadOnlySpan<byte> row = data.AsSpan(r.Start, r.Length);

        // 相手側の同じキーを探す
        int i = Home(r.KeyHash);
        while (true)
        {
            ref Slot s = ref _slots[i];
            if (s.Loc == 0) break;
            if (s.KeyHash == r.KeyHash && (byte)(s.Loc & 1) != side)
            {
                long loc = (long)(s.Loc >> 1) - 1;
                ReadOnlySpan<byte> rec = _arena.Record(loc);
                int olen = MemoryMarshal.Read<int>(rec);
                ReadOnlySpan<byte> other = rec.Slice(Arena.HeaderSize, olen);
                byte oside = (byte)(s.Loc & 1);
                if (KeyMatch(rec, other, oside, row, side, in r))
                {
                    if (side == 1) Compare(other, row, sink);
                    else Compare(row, other, sink);
                    _arena.Free(olen);
                    RemoveAt(i);
                    return;
                }
            }
            i = (i + 1) & _mask;
        }

        // 無ければ待ち行に入れる
        long nl = _arena.Add(row, r.KeyStart, r.KeyLength, r.KeyQuoted);
        Insert(r.KeyHash, ((ulong)(nl + 1) << 1) | side);
        MaybeCompact();
        if (!_resolving && _arena.Live >= _e.EvictMinBytes && _e.Meter.Read() > _e.MemoryLimitBytes) Evict();
    }

    private bool KeyMatch(ReadOnlySpan<byte> rec, ReadOnlySpan<byte> other, byte oside, ReadOnlySpan<byte> row, byte side, in RowRef r)
    {
        if (_e.SingleKey)
        {
            GetKey(rec, other, oside, out ReadOnlySpan<byte> okey, out bool oq);
            return KeyEquals(okey, oq, row.Slice(r.KeyStart, r.KeyLength), r.KeyQuoted);
        }
        int[] ko = _e.KeyIndexes(oside), kn = _e.KeyIndexes(side);
        byte dO = _e.Delim(oside), dN = _e.Delim(side);
        bool q = _e.Quotes;
        for (int k = 0; k < ko.Length; k++)
        {
            ReadOnlySpan<byte> a = default, b = default;
            bool aq = false, bq = false;
            if (CsvBytes.TryGetField(other, dO, ko[k], out CsvBytes.Field fa, q)) { a = other.Slice(fa.Start, fa.Length); aq = fa.Quoted; }
            if (CsvBytes.TryGetField(row, dN, kn[k], out CsvBytes.Field fb, q)) { b = row.Slice(fb.Start, fb.Length); bq = fb.Quoted; }
            if (!KeyEquals(a, aq, b, bq)) return false;
        }
        return true;
    }

    /// <summary>1 列のキーの位置（Arena に置いた行から）。</summary>
    private void GetKey(ReadOnlySpan<byte> rec, ReadOnlySpan<byte> row, byte side, out ReadOnlySpan<byte> key, out bool quoted)
    {
        ushort ks = MemoryMarshal.Read<ushort>(rec[4..]);
        quoted = rec[8] != 0;
        if (ks != 0xFFFF)
        {
            ushort kl = MemoryMarshal.Read<ushort>(rec[6..]);
            key = row.Slice(ks, kl);
            return;
        }
        if (CsvBytes.TryGetField(row, _e.Delim(side), _e.KeyIndexes(side)[0], out CsvBytes.Field f, _e.Quotes))
        {
            key = row.Slice(f.Start, f.Length);
            quoted = f.Quoted;
        }
        else key = default;
    }

    /// <summary>行のキーの列の位置を _keyParts に入れる（書き出し用）。</summary>
    private void KeyParts(ReadOnlySpan<byte> row, byte side)
    {
        _keyParts.Clear();
        foreach (int k in _e.KeyIndexes(side))
            _keyParts.Add(CsvBytes.TryGetField(row, _e.Delim(side), k, out CsvBytes.Field f, _e.Quotes) ? f : new CsvBytes.Field(row.Length, 0, false));
    }

    private void Insert(ulong keyHash, ulong loc)
    {
        if ((_used + 1) * 10 > _slots.Length * 7) Grow();
        int i = Home(keyHash);
        while (_slots[i].Loc != 0) i = (i + 1) & _mask;
        _slots[i].KeyHash = keyHash;
        _slots[i].Loc = loc;
        _used++;
    }

    private void Grow()
    {
        Slot[] old = _slots;
        _slots = new Slot[old.Length * 2];
        _mask = _slots.Length - 1;
        _e.Meter.Add((long)old.Length * 16);
        foreach (Slot s in old)
        {
            if (s.Loc == 0) continue;
            int i = Home(s.KeyHash);
            while (_slots[i].Loc != 0) i = (i + 1) & _mask;
            _slots[i] = s;
        }
    }

    /// <summary>線形探査の表から 1 件消し、後ろの件を詰める（墓標を残さない）。</summary>
    private void RemoveAt(int i)
    {
        int j = i;
        while (true)
        {
            j = (j + 1) & _mask;
            if (_slots[j].Loc == 0) break;
            int k = Home(_slots[j].KeyHash);
            // j の本来の位置 k が (i, j] の外にあれば、i へ動かしてよい
            bool between = i <= j ? (i < k && k <= j) : (i < k || k <= j);
            if (between) continue;
            _slots[i] = _slots[j];
            i = j;
        }
        _slots[i] = default;
        _used--;
    }

    private static bool KeyEquals(ReadOnlySpan<byte> a, bool aq, ReadOnlySpan<byte> b, bool bq)
    {
        if (!aq && !bq) return a.SequenceEqual(b);
        return CsvBytes.ValueEquals(a, aq, b, bq);
    }

    /// <summary>旧の行と新の行を比べ、違う列を書き出す（旧にしか無い列は新を空、新にしか無い列は旧を空として比べる）。</summary>
    private void Compare(ReadOnlySpan<byte> oldRow, ReadOnlySpan<byte> newRow, DiffSink sink)
    {
        if (_e.SameLayout && oldRow.SequenceEqual(newRow))
        {
            sink.SameRows++;
            return;
        }
        bool q = _e.Quotes;
        CsvBytes.Split(oldRow, _e.DelimOld, _fo, q);
        CsvBytes.Split(newRow, _e.DelimNew, _fn, q);
        bool any = false;
        foreach (CompareColumn c in _e.Columns)
        {
            ReadOnlySpan<byte> ov = default, nv = default;
            bool oq = false, nq = false;
            if (c.Old >= 0 && c.Old < _fo.Count) { var f = _fo[c.Old]; ov = oldRow.Slice(f.Start, f.Length); oq = f.Quoted; }
            if (c.New >= 0 && c.New < _fn.Count) { var f = _fn[c.New]; nv = newRow.Slice(f.Start, f.Length); nq = f.Quoted; }
            if (CsvBytes.ValueEquals(ov, oq, nv, nq)) continue;
            if (!any)
            {
                any = true;
                _keyParts.Clear();
                foreach (int k in _e.KeyIndexOld) _keyParts.Add(k < _fo.Count ? _fo[k] : new CsvBytes.Field(oldRow.Length, 0, false));
            }
            sink.WriteChanged(oldRow, _keyParts, c.NameUtf8, ov, oq, nv, nq);
        }
        if (any) sink.ChangedRows++;
        else sink.SameRows++;
    }

    /// <summary>書いた量が待ち行の 1.25 倍を超えたら、待っている行だけを新しい場所へ詰め直す。</summary>
    private void MaybeCompact()
    {
        Arena a = _arena;
        if (a.Allocated < (16L << 20) || a.Allocated < a.Live + a.Live / 4) return;
        var na = new Arena(_e.Meter);
        for (int i = 0; i < _slots.Length; i++)
        {
            ulong l = _slots[i].Loc;
            if (l == 0) continue;
            long loc = (long)(l >> 1) - 1;
            long nl = na.CopyFrom(a, loc);
            _slots[i].Loc = ((ulong)(nl + 1) << 1) | (l & 1);
        }
        na.Allocated = na.Live;
        a.RecycleAll();
        _arena = na;
        Interlocked.Increment(ref _e.Compactions);
    }

    /// <summary>待ち行を一時ファイルへ逃がし、以後この区画に来る行も一時ファイルへ書くようにする。</summary>
    private void Evict()
    {
        _spill = new SpillFile(_e.NewTempPath("pending"));
        for (int i = 0; i < _slots.Length; i++)
        {
            ulong l = _slots[i].Loc;
            if (l == 0) continue;
            WriteSlotTo(_spill, _slots[i].KeyHash, l);
        }
        ResetTable();
        Interlocked.Increment(ref _e.SpilledShards);
    }

    private void WriteSlotTo(SpillFile f, ulong keyHash, ulong l)
    {
        long loc = (long)(l >> 1) - 1;
        byte side = (byte)(l & 1);
        ReadOnlySpan<byte> rec = _arena.Record(loc);
        int len = MemoryMarshal.Read<int>(rec);
        ReadOnlySpan<byte> row = rec.Slice(Arena.HeaderSize, len);
        RowRef r = default;
        r.KeyHash = keyHash;
        if (_e.SingleKey)
        {
            GetKey(rec, row, side, out ReadOnlySpan<byte> key, out bool q);
            r.KeyStart = key.IsEmpty ? len : (int)Unsafe.ByteOffset(ref MemoryMarshal.GetReference(row), ref MemoryMarshal.GetReference(key));
            r.KeyLength = key.Length;
            r.KeyQuoted = q;
        }
        else
        {
            r.KeyStart = 0xFFFF;
            r.KeyLength = 0xFFFF;
        }
        f.Write(row, in r, side);
    }

    private void ResetTable() => SetTable(1024);

    private void SetTable(int size)
    {
        _e.Meter.Add((long)(size - _slots.Length) * 16);
        _slots = new Slot[size];
        _mask = size - 1;
        _used = 0;
        _arena.RecycleAll();
    }

    /// <summary>読み終わった後: 残った待ち行を「削除」「追加」として書き出す。逃がした区画は一時ファイルから照合してから。</summary>
    public void Finish(DiffSink sink)
    {
        if (_spill == null)
        {
            Flush(sink);
            return;
        }
        _spill.Close();
        Interlocked.Add(ref _e.SpillBytes, _spill.Bytes);
        string path = _spill.Path;
        _spill = null;
        _resolving = true;
        ResolveFile(path, 0, sink);
    }

    private void ResolveFile(string path, int depth, DiffSink sink)
    {
        long size = new FileInfo(path).Length;
        if (size + size / 2 <= _e.ResolveLimitBytes || depth >= 3)
        {
            ResetTable();
            SpillFile.ReadAll(path, (buf, r, side) => ProcessCore(buf, ref r, side, sink));
            File.Delete(path);
            Flush(sink);
            return;
        }
        // 大きすぎる: キーのハッシュの別のビットで 16 に分けてから、1 つずつ照合する
        const int parts = 16;
        var subs = new SpillFile[parts];
        try
        {
            for (int k = 0; k < parts; k++) subs[k] = new SpillFile(_e.NewTempPath("part"));
            int shift = 28 + 4 * depth;
            SpillFile.ReadAll(path, (buf, r, side) => subs[(int)((r.KeyHash >> shift) & (parts - 1))].Write(buf.AsSpan(0, r.Length), in r, side));
        }
        finally
        {
            foreach (SpillFile? s in subs) s?.Close();
        }
        File.Delete(path);
        foreach (SpillFile s in subs) ResolveFile(s.Path, depth + 1, sink);
    }

    private void Flush(DiffSink sink)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            ulong l = _slots[i].Loc;
            if (l == 0) continue;
            long loc = (long)(l >> 1) - 1;
            byte side = (byte)(l & 1);
            ReadOnlySpan<byte> rec = _arena.Record(loc);
            int len = MemoryMarshal.Read<int>(rec);
            ReadOnlySpan<byte> row = rec.Slice(Arena.HeaderSize, len);
            KeyParts(row, side);
            sink.WriteAddedOrDeleted(side == 1, row, _keyParts);
        }
        SetTable(1);
    }

    /// <summary>重複したキーのハッシュの数を数える（並べて隣と同じものを数える）。</summary>
    public static long CountDuplicates(ulong[] keys, int n, List<ulong>? examples)
    {
        if (n < 2) return 0;
        Array.Sort(keys, 0, n);
        long dup = 0;
        for (int i = 1; i < n; i++)
        {
            if (keys[i] == keys[i - 1] && (i < 2 || keys[i - 1] != keys[i - 2]))
            {
                dup++;
                if (examples != null && examples.Count < 20) examples.Add(keys[i]);
            }
        }
        return dup;
    }
}

/// <summary>比べる 1 列（旧の位置・新の位置。片方にしか無ければ -1）。</summary>
internal readonly record struct CompareColumn(int Old, int New, byte[] NameUtf8);
