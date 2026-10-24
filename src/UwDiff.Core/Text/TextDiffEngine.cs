using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace UwDiff.Core.Text;

/// <summary>比べる 1 本（ファイル、または圧縮の中身・Word などから取り出した文字）。</summary>
public sealed class TextSource
{
    /// <summary>画面・出力に出す名前。</summary>
    public required string Name { get; init; }
    /// <summary>読むストリームを開く（先頭から）。</summary>
    public required Func<Stream> Open { get; init; }
    /// <summary>文字コード（null なら先頭から判定）。</summary>
    public FileEncoding? Encoding { get; init; }
    /// <summary>大きさの見積もり（進み具合の表示用。分からなければ 0）。</summary>
    public long Length { get; init; }
    /// <summary>更新日時（unified diff の見出し用）。</summary>
    public DateTime? Modified { get; init; }

    public static TextSource FromFile(string path, FileEncoding? enc = null)
    {
        var fi = new FileInfo(path);
        return new TextSource
        {
            Name = path,
            Open = () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan),
            Encoding = enc,
            Length = fi.Length,
            Modified = fi.LastWriteTime,
        };
    }
}

/// <summary>テキストの比較の設定。</summary>
public sealed class TextDiffOptions
{
    public LineCompare Compare { get; init; } = new();
    /// <summary>目印（アンカー）を探す範囲の上限（行）。これを超えて目印が無ければ、その範囲を置き換えとして出す。</summary>
    public int MaxLookahead { get; init; } = 2_000_000;
    /// <summary>アンカーの間を細かく比べるときの違いの数の上限（超えたらその区間は置き換え）。</summary>
    public int MaxEditsPerSegment { get; init; } = 4000;
    /// <summary>1 回に読む大きさ（バイト）。</summary>
    public int ChunkBytes { get; init; } = 8 << 20;
    /// <summary>画面のために、行の位置の間引き索引も作る（ファイルをもう一度読まずに済む）。</summary>
    public bool RecordLineIndex { get; init; }
    public Action<long, long>? Progress { get; init; }
}

/// <summary>テキストの比較の結果の要約。</summary>
public sealed class TextDiffResult
{
    public long OldLines, NewLines;
    public long EqualLines, DeletedLines, InsertedLines;
    /// <summary>変更のまとまり（削除と追加が続いた 1 か所）の数。</summary>
    public long Changes;
    public string OldEncoding = "", NewEncoding = "";
    public bool Transcoded;
    /// <summary>目印が見つからず、範囲ごと置き換えとして出した回数（大きな違いがある所）。</summary>
    public long Fallbacks;
    public long PeakWindowLines;
    /// <summary>RecordLineIndex のとき: LineIndex.Every 行ごとの先頭の位置（比べたストリームの中の位置。BOM の後ろから数える）。</summary>
    public long[]? OldLineStarts, NewLineStarts;
    /// <summary>旧・新を読んだまま（変換せず）比べたか。そのときだけ行の位置がファイルの位置と合う。</summary>
    public bool OldRaw, NewRaw;
    public TimeSpan Total;
    public bool Same => DeletedLines == 0 && InsertedLines == 0;
}

/// <summary>差分を受け取る側（unified diff の書き出し・画面用の記録など）。</summary>
public interface ITextDiffSink
{
    /// <summary>旧・新の先頭から count 行が同じ。</summary>
    void Equal(TextDiffCursor a, TextDiffCursor b, int count);
    /// <summary>旧の先頭から del 行を消し、新の先頭から ins 行を足す（どちらかは 0 でもよい）。</summary>
    void Change(TextDiffCursor a, int del, TextDiffCursor b, int ins);
    void End(TextDiffCursor a, TextDiffCursor b);
}

/// <summary>sink から見た片側の行（先頭からの番号で読む）。</summary>
public sealed class TextDiffCursor
{
    internal LineWindow W = null!;
    /// <summary>先頭の行の行番号（1 始まり）。</summary>
    public long LineNo => W.LineNo;
    public ReadOnlySpan<byte> Line(int i) => W.Line(i);
    public bool IsUnterminated(int i) => W.IsUnterminated(i);
}

/// <summary>
/// 2 本のテキストを行の並びを保って比べる（巨大なファイル向け）。
/// 2 本を同時に先頭から 1 回だけ読み（読み込みと行に切る処理はファイルごとの別スレッド）、
/// 同じ行が続く間は進み、違いが出たら、その先で旧・新のどちらにも 1 回だけ現れる行（アンカー）を探す（探す範囲は必要なだけ広げる）。
/// アンカーの組から最長の増加列の先頭を次の目印にし、目印までの区間を Myers で細かく比べる。
/// 持つのは、違いの所の前後だけ。旧と新が同じ文字コード（UTF-8・CP932・EUC-JP）ならバイト列のまま比べ、違えば UTF-8 にそろえる。
/// </summary>
public sealed class TextDiffEngine
{
    private readonly TextDiffOptions _o;

    public TextDiffEngine(TextDiffOptions o) => _o = o;

    /// <summary>ストリームの先頭を読んで文字コードを決め、比べるためのストリーム（BOM を除き、必要なら UTF-8 に直したもの）を返す。</summary>
    internal static (Stream Stream, FileEncoding Encoding) OpenWork(TextSource src, Func<FileEncoding, bool> transcode)
    {
        Stream s = src.Open();
        byte[] head = new byte[1 << 20];
        int n = 0;
        while (n < head.Length)
        {
            int k = s.Read(head, n, head.Length - n);
            if (k == 0) break;
            n += k;
        }
        FileEncoding det = TextEncodings.Detect(head.AsSpan(0, n), n < head.Length, out int bom);
        FileEncoding enc = src.Encoding ?? det;
        if (src.Encoding != null && src.Encoding != det)
            bom = TextEncodings.Bom(enc, 1) is { Length: > 0 } b && head.AsSpan(0, n).StartsWith(b) ? b.Length : 0;
        Stream work = new PrefixStream(head, bom, n, s);
        if (transcode(enc)) work = System.Text.Encoding.CreateTranscodingStream(work, TextEncodings.Get(enc), new UTF8Encoding(false), leaveOpen: false);
        return (work, enc);
    }

    public TextDiffResult Run(TextSource oldSrc, TextSource newSrc, ITextDiffSink sink)
    {
        var sw = Stopwatch.StartNew();
        var res = new TextDiffResult();
        // 文字コード: 先に両方の先頭を見て決める
        FileEncoding eo = Peek(oldSrc), en = Peek(newSrc);
        bool same = eo == en && TextEncodings.IsAsciiCompatible(eo);
        res.Transcoded = !same && !(eo == FileEncoding.Utf8 && en == FileEncoding.Utf8);
        res.OldEncoding = TextEncodings.Name(eo);
        res.NewEncoding = TextEncodings.Name(en);
        bool Trans(FileEncoding e) => !same && e != FileEncoding.Utf8;
        // CP932 のバイト列のまま比べるときは、大文字・小文字をそろえる所で 2 バイト目を変えない
        LineCompare cmp = _o.Compare.For(same && eo == FileEncoding.Cp932);
        var (so, _) = OpenWork(new TextSource { Name = oldSrc.Name, Open = oldSrc.Open, Encoding = eo }, Trans);
        var (sn, _) = OpenWork(new TextSource { Name = newSrc.Name, Open = newSrc.Open, Encoding = en }, Trans);

        using var cts = new CancellationTokenSource();
        var qa = new BlockingCollection<LineChunk>(3);
        var qb = new BlockingCollection<LineChunk>(3);
        var pa = new LineProducer(so, cmp, _o.ChunkBytes) { LineStarts = _o.RecordLineIndex ? new List<long>() : null };
        var pb = new LineProducer(sn, cmp, _o.ChunkBytes) { LineStarts = _o.RecordLineIndex ? new List<long>() : null };
        res.OldRaw = !Trans(eo);
        res.NewRaw = !Trans(en);
        Exception? failure = null;
        Thread Start(LineProducer p, BlockingCollection<LineChunk> q, Stream s, string name)
        {
            var t = new Thread(() =>
            {
                try { using (s) p.Run(q, cts.Token); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Interlocked.CompareExchange(ref failure, ex, null); cts.Cancel(); }
            }) { IsBackground = true, Name = name };
            t.Start();
            return t;
        }
        Thread ta = Start(pa, qa, so, "uvd-lines-old"), tb = Start(pb, qb, sn, "uvd-lines-new");

        var a = new LineWindow(qa, cts.Token);
        var b = new LineWindow(qb, cts.Token);
        var ca = new TextDiffCursor { W = a };
        var cb = new TextDiffCursor { W = b };
        long total = oldSrc.Length + newSrc.Length;
        long lastProgress = 0;
        try
        {
            var dict = new Dictionary<ulong, long>();
            byte[] t1 = new byte[256], t2 = new byte[256];
            bool Eq(LineWindow x, int i, LineWindow y, int j)
            {
                x.Get(i, out byte[] dx, out int sx, out int lx, out ulong hx);
                y.Get(j, out byte[] dy, out int sy, out int ly, out ulong hy);
                if (hx != hy) return false;
                ReadOnlySpan<byte> u = cmp.Normalize(dx.AsSpan(sx, lx), ref t1);
                ReadOnlySpan<byte> v = cmp.Normalize(dy.AsSpan(sy, ly), ref t2);
                return u.SequenceEqual(v);
            }

            while (true)
            {
                // 1) 同じ行が続く間は進む
                while (true)
                {
                    int na = a.Ensure(1), nb = b.Ensure(1);
                    if (na == 0 || nb == 0) break;
                    int lim = Math.Min(a.Available, b.Available);
                    int k = 0;
                    while (k < lim && Eq(a, k, b, k)) k++;
                    if (k == 0) break;
                    sink.Equal(ca, cb, k);
                    res.EqualLines += k;
                    a.Consume(k);
                    b.Consume(k);
                    if (_o.Progress != null && Environment.TickCount64 - lastProgress > 500)
                    {
                        lastProgress = Environment.TickCount64;
                        _o.Progress(pa.BytesRead + pb.BytesRead, total);
                    }
                }
                bool endA = a.AtEnd, endB = b.AtEnd;
                if (endA && endB) break;
                if (endA || endB)
                {
                    // 片方が終わった: 残りは全部追加か削除（大きくてもよいように塊ごとに）
                    LineWindow w = endA ? b : a;
                    while (!w.AtEnd)
                    {
                        int n = w.Ensure(65536);
                        if (endA) { sink.Change(ca, 0, cb, n); res.InsertedLines += n; b.Consume(n); }
                        else { sink.Change(ca, n, cb, 0); res.DeletedLines += n; a.Consume(n); }
                    }
                    res.Changes++;
                    continue;
                }

                // 2) 違いが出た: 次の目印（アンカー）を探す
                int look = 16;
                (int ia, int ib) anchor = (-1, -1);
                int wa = 0, wb = 0;
                while (true)
                {
                    wa = a.Ensure(look);
                    wb = b.Ensure(look);
                    res.PeakWindowLines = Math.Max(res.PeakWindowLines, Math.Max(wa, wb));
                    anchor = FindAnchor(a, wa, b, wb, dict, Eq);
                    bool allA = a.Eof && wa == a.Available, allB = b.Eof && wb == b.Available;
                    bool complete = allA && allB;
                    // 見つかった目印が窓の端に近すぎなければ確定（端だと、窓の外にも同じ行があるかもしれない）
                    if (anchor.ia >= 0 && (allA || anchor.ia < wa * 3 / 4) && (allB || anchor.ib < wb * 3 / 4)) break;
                    if (complete || look >= _o.MaxLookahead) break;
                    look = (int)Math.Min((long)look * 4, _o.MaxLookahead);
                }

                if (anchor.ia < 0)
                {
                    // 目印が無い: 片方の先頭の行が相手の窓にあれば、そこまでを追加か削除にする。無ければ窓ごと置き換え
                    int ja = IndexOf(b, wb, a, 0, Eq), jb = IndexOf(a, wa, b, 0, Eq);
                    if (ja >= 0 && (jb < 0 || ja <= jb)) anchor = (0, ja);
                    else if (jb >= 0) anchor = (jb, 0);
                    else
                    {
                        anchor = (wa, wb);
                        res.Fallbacks++;
                    }
                }

                // 3) 目印までの区間を細かく比べて出す
                EmitSegment(a, anchor.ia, b, anchor.ib, sink, ca, cb, res, Eq);
            }
            sink.End(ca, cb);
        }
        catch (OperationCanceledException) when (failure != null) { }
        finally
        {
            cts.Cancel();
            ta.Join();
            tb.Join();
        }
        if (failure != null) throw new InvalidOperationException("読み込みに失敗しました: " + failure.Message, failure);
        res.OldLines = a.LineNo - 1;
        res.NewLines = b.LineNo - 1;
        res.OldLineStarts = pa.LineStarts?.ToArray();
        res.NewLineStarts = pb.LineStarts?.ToArray();
        res.Total = sw.Elapsed;
        return res;
    }

    private static FileEncoding Peek(TextSource src)
    {
        if (src.Encoding != null) return src.Encoding.Value;
        using Stream s = src.Open();
        byte[] head = new byte[1 << 20];
        int n = 0;
        while (n < head.Length)
        {
            int k = s.Read(head, n, head.Length - n);
            if (k == 0) break;
            n += k;
        }
        return TextEncodings.Detect(head.AsSpan(0, n), n < head.Length, out _);
    }

    private static int IndexOf(LineWindow hay, int n, LineWindow needle, int at, Func<LineWindow, int, LineWindow, int, bool> eq)
    {
        for (int j = 0; j < n; j++)
            if (eq(needle, at, hay, j)) return j;
        return -1;
    }

    /// <summary>
    /// 窓の中で旧・新のどちらにも 1 回だけ現れる行の組を集め、旧の順に並べたときの新の位置の最長の増加列を求め、その先頭を返す。
    /// 無ければ (-1, -1)。
    /// </summary>
    private static (int, int) FindAnchor(LineWindow a, int na, LineWindow b, int nb, Dictionary<ulong, long> dict,
        Func<LineWindow, int, LineWindow, int, bool> eq)
    {
        // dict: ハッシュ → 旧の位置（下位 31 ビット）・旧の数（2 以上は重複）・新の位置・新の数
        dict.Clear();
        const long Dup = -1;
        for (int i = 0; i < na; i++)
        {
            ulong h = a.HashAt(i);
            dict[h] = dict.TryGetValue(h, out long v) ? Dup : ((long)i << 32) | 0xFFFFFFFFL;
        }
        var pairs = new List<(int ia, int ib)>();
        var seenB = new Dictionary<ulong, int>();
        for (int j = 0; j < nb; j++)
        {
            ulong h = b.HashAt(j);
            if (!dict.TryGetValue(h, out long v) || v == Dup) continue;
            if (seenB.ContainsKey(h)) seenB[h] = -1;
            else seenB[h] = j;
        }
        foreach (var (h, j) in seenB)
        {
            if (j < 0) continue;
            int i = (int)(dict[h] >> 32);
            if (eq(a, i, b, j)) pairs.Add((i, j));
        }
        if (pairs.Count == 0) return (-1, -1);
        pairs.Sort((x, y) => x.ia.CompareTo(y.ia));
        // 最長の増加列（patience）
        var tails = new List<int>();          // 長さ k+1 の増加列の最後の pairs の番号
        var prev = new int[pairs.Count];
        for (int p = 0; p < pairs.Count; p++)
        {
            int ib = pairs[p].ib;
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (pairs[tails[mid]].ib < ib) lo = mid + 1; else hi = mid;
            }
            prev[p] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count) tails.Add(p); else tails[lo] = p;
        }
        int cur = tails[^1];
        while (prev[cur] >= 0) cur = prev[cur];
        return pairs[cur];
    }

    /// <summary>旧の先頭 na 行・新の先頭 nb 行の区間を Myers で比べて sink に出し、済ませる。</summary>
    private void EmitSegment(LineWindow a, int na, LineWindow b, int nb, ITextDiffSink sink, TextDiffCursor ca, TextDiffCursor cb,
        TextDiffResult res, Func<LineWindow, int, LineWindow, int, bool> eq)
    {
        List<EditRun>? runs = (long)na + nb <= 200_000 ? Myers.Diff(na, nb, (i, j) => eq(a, i, b, j), _o.MaxEditsPerSegment) : null;
        if (runs == null)
        {
            runs = new List<EditRun>();
            if (na > 0) runs.Add(new EditRun(EditKind.Delete, na));
            if (nb > 0) runs.Add(new EditRun(EditKind.Insert, nb));
        }
        int k = 0;
        while (k < runs.Count)
        {
            EditRun r = runs[k];
            if (r.Kind == EditKind.Equal)
            {
                sink.Equal(ca, cb, r.Count);
                res.EqualLines += r.Count;
                a.Consume(r.Count);
                b.Consume(r.Count);
                k++;
                continue;
            }
            // 続いた削除・追加をまとめて 1 か所の変更にする
            int del = 0, ins = 0;
            while (k < runs.Count && runs[k].Kind != EditKind.Equal)
            {
                if (runs[k].Kind == EditKind.Delete) del += runs[k].Count; else ins += runs[k].Count;
                k++;
            }
            sink.Change(ca, del, cb, ins);
            res.DeletedLines += del;
            res.InsertedLines += ins;
            res.Changes++;
            a.Consume(del);
            b.Consume(ins);
        }
    }
}

/// <summary>先に読んだ先頭の後に、残りのストリームを続けて読ませる。</summary>
internal sealed class PrefixStream(byte[] head, int start, int end, Stream rest) : Stream
{
    private int _pos = start;

    public override int Read(Span<byte> buffer)
    {
        if (_pos < end)
        {
            int n = Math.Min(buffer.Length, end - _pos);
            head.AsSpan(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
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
