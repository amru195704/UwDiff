namespace UwDiff.Core.Text;

/// <summary>
/// 先頭から同じ部分を、行に切らずハッシュも付けずに、バイト列のまま比べて飛ばす（GNU diff の先頭の切り落としと同じ考え）。
/// 2 本を塊ごとに同時に読み、塊がまるごと同じなら改行の数だけ数えて次へ進む。違うバイトが出たら、その行の先頭から
/// さらに keepLines 行だけ戻った所までを「飛ばした」とし、残り（戻した行・違いの出た塊・まだ読んでいない所）を
/// 続けて読めるストリームにして返す。戻す行は、unified diff の前の文脈（-U）に使う。
/// 同じバイト列の行は、空白・大文字小文字・改行コードを無視する指定があっても同じ行なので、どの比べ方でも結果は変わらない。
/// </summary>
internal static class CommonPrefix
{
    /// <summary>飛ばした結果。Old・New はこの後に読むストリーム（飛ばした所の後ろから）。</summary>
    internal sealed record Result(Stream Old, Stream New, long Bytes, long Lines, List<long>? LineStarts);

    /// <summary>まるごと同じだった塊（旧の分。新も同じバイト列）。</summary>
    private sealed record Block(byte[] Data, int Length, long Offset, long LinesBefore, int Newlines);

    /// <param name="record">行の位置の間引き索引（LineIndex.Every 行ごと）も作る</param>
    /// <param name="progress">読んだバイト数（旧＋新）</param>
    public static Result Skip(Stream so, Stream sn, int chunkBytes, int keepLines, bool record, Action<long>? progress, CancellationToken ct)
    {
        int keep = Math.Max(0, keepLines) + 1;   // 違いの出た行の手前の改行から数えて keep 個目の改行の後ろまでを飛ばす
        var retained = new List<Block>();        // 最後の keep 行を含むのに要る、まるごと同じだった塊
        var free = new Stack<byte[]>();
        var starts = record ? new List<long> { 0 } : null;
        long offset = 0, lines = 0;
        // 最初は小さく読み、同じ間は大きくする（1 行目の近くで違うファイルでは、ほとんど読まずに普通の比べ方へ渡す）
        int size = Math.Min(chunkBytes, 64 << 10);
        byte[] Rent() => free.Count > 0 && free.Peek().Length >= size ? free.Pop() : GC.AllocateUninitializedArray<byte>(size);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            byte[] ba = Rent(), bb = Rent();
            // 2 本を同時に読む（読むのを待つ間に、もう 1 本も読む）
            var ta = Task.Run(() => Fill(so, ba, size));
            int nb = Fill(sn, bb, size);
            int na = ta.GetAwaiter().GetResult();
            progress?.Invoke(offset * 2 + na + nb);
            int n = Math.Min(na, nb);
            int m = ba.AsSpan(0, n).CommonPrefixLength(bb.AsSpan(0, n));
            if (m == size && na == size && nb == size)
            {
                // 塊がまるごと同じ: 改行を数えて次へ（新の塊はもう要らない）
                int nl = Count(ba.AsSpan(0, na), offset, lines, starts);
                retained.Add(new Block(ba, na, offset, lines, nl));
                free.Push(bb);
                offset += na;
                lines += nl;
                Trim(retained, keep, free);
                if (size < chunkBytes) { size = Math.Min(chunkBytes, size * 16); free.Clear(); }
                continue;
            }

            // 違いが出た（または終わった）。同じなのは、この塊の先頭 m バイトまで
            var cur = new Block(ba, m, offset, lines, 0);
            (Block blk, int pos) = FindBoundary(retained, cur, keep);
            long skipBytes, skipLines;
            var segA = new List<ArraySegment<byte>>();
            var segB = new List<ArraySegment<byte>>();
            if (pos < 0)
            {
                // 飛ばせる改行が無い（ファイルの先頭から）: 何も飛ばさない
                skipBytes = 0;
                skipLines = 0;
                foreach (Block r in retained) { segA.Add(new(r.Data, 0, r.Length)); segB.Add(new(r.Data, 0, r.Length)); }
                segA.Add(new(ba, 0, na));
                segB.Add(new(bb, 0, nb));
            }
            else
            {
                skipBytes = blk.Offset + pos + 1;
                skipLines = blk.LinesBefore + blk.Data.AsSpan(0, pos + 1).Count((byte)'\n');
                if (ReferenceEquals(blk, cur))
                {
                    segA.Add(new(ba, pos + 1, na - pos - 1));
                    segB.Add(new(bb, pos + 1, nb - pos - 1));
                }
                else
                {
                    bool after = false;
                    foreach (Block r in retained)
                    {
                        if (ReferenceEquals(r, blk)) { after = true; segA.Add(new(r.Data, pos + 1, r.Length - pos - 1)); segB.Add(new(r.Data, pos + 1, r.Length - pos - 1)); continue; }
                        if (after) { segA.Add(new(r.Data, 0, r.Length)); segB.Add(new(r.Data, 0, r.Length)); }
                    }
                    segA.Add(new(ba, 0, na));
                    segB.Add(new(bb, 0, nb));
                }
            }
            if (starts != null)
            {
                // 飛ばした行の分だけ残す（その後ろは、行に切る側が数える）
                if (skipLines > 0) Count(ba.AsSpan(0, (int)Math.Max(0, Math.Min(m, skipBytes - offset))), offset, lines, starts);
                int keepStarts = (int)((skipLines + LineIndex.Every - 1) / LineIndex.Every);
                if (starts.Count > keepStarts) starts.RemoveRange(keepStarts, starts.Count - keepStarts);
            }
            return new Result(new SegmentStream(segA, so), new SegmentStream(segB, sn), skipBytes, skipLines, starts);
        }
    }

    private static int Fill(Stream s, byte[] buf, int size)
    {
        int n = 0;
        while (n < size)
        {
            int k = s.Read(buf, n, size - n);
            if (k == 0) break;
            n += k;
        }
        return n;
    }

    /// <summary>改行を数える。starts があれば、Every 行ごとの行の先頭の位置も足す（lines は span の先頭までの行数）。</summary>
    private static int Count(ReadOnlySpan<byte> span, long offset, long lines, List<long>? starts)
    {
        if (starts == null) return span.Count((byte)'\n');
        int nl = 0, p = 0;
        while (true)
        {
            int k = span[p..].IndexOf((byte)'\n');
            if (k < 0) break;
            p += k + 1;
            nl++;
            long line = lines + nl;   // この改行の後ろの行の番号（0 始まり）
            if (line % LineIndex.Every == 0) starts.Add(offset + p);
        }
        return nl;
    }

    /// <summary>先頭の塊を、残りの塊だけで keep 個の改行を含むなら外す（使い回す）。</summary>
    private static void Trim(List<Block> retained, int keep, Stack<byte[]> free)
    {
        while (retained.Count > 1)
        {
            long rest = 0;
            for (int i = 1; i < retained.Count; i++) rest += retained[i].Newlines;
            if (rest < keep) return;
            free.Push(retained[0].Data);
            retained.RemoveAt(0);
        }
    }

    /// <summary>
    /// 違いの出た所（cur の Length バイト目）から後ろ向きに keep 個目の改行を探す。見つかった塊と、その中の位置を返す。
    /// 見つからなければ (null, -1)（ファイルの先頭から keep 行も無い）。
    /// </summary>
    private static (Block, int) FindBoundary(List<Block> retained, Block cur, int keep)
    {
        int left = keep;
        for (int b = retained.Count; b >= 0; b--)
        {
            Block blk = b == retained.Count ? cur : retained[b];
            ReadOnlySpan<byte> span = blk.Data.AsSpan(0, blk.Length);
            while (true)
            {
                int k = span.LastIndexOf((byte)'\n');
                if (k < 0) break;
                if (--left == 0) return (blk, k);
                span = span[..k];
            }
        }
        return (null!, -1);
    }
}

/// <summary>手元にある塊を順に読ませ、その後に残りのストリームを続ける。</summary>
internal sealed class SegmentStream(List<ArraySegment<byte>> segments, Stream rest) : Stream
{
    private int _seg;
    private int _pos;

    public override int Read(Span<byte> buffer)
    {
        while (_seg < segments.Count)
        {
            ArraySegment<byte> s = segments[_seg];
            if (_pos < s.Count)
            {
                int n = Math.Min(buffer.Length, s.Count - _pos);
                s.AsSpan(_pos, n).CopyTo(buffer);
                _pos += n;
                return n;
            }
            _seg++;
            _pos = 0;
        }
        return rest.Read(buffer);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) rest.Dispose();
        base.Dispose(disposing);
    }
}
