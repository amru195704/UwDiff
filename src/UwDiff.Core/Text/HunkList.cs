using System.Runtime.InteropServices;

namespace UwDiff.Core.Text;

/// <summary>変更の 1 か所（旧の OldLine 行目から OldCount 行を消し、新の NewLine 行目から NewCount 行を足す。行番号は 1 始まり）。</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Hunk(long OldLine, int OldCount, long NewLine, int NewCount);

/// <summary>
/// 変更の位置だけを記録する sink（画面で左右に並べて見せるため。行の中身は持たない）。
/// 片方が終わった後の大きな追加・削除は塊ごとに来るので、続いたものは 1 つにまとめる。
/// </summary>
public sealed class HunkRecorder : ITextDiffSink
{
    public List<Hunk> Hunks { get; } = new();
    public long OldLines { get; private set; }
    public long NewLines { get; private set; }

    public void Equal(TextDiffCursor a, TextDiffCursor b, int count) { }

    public void Change(TextDiffCursor a, int del, TextDiffCursor b, int ins)
    {
        if (Hunks.Count > 0)
        {
            Hunk last = Hunks[^1];
            if (last.OldLine + last.OldCount == a.LineNo && last.NewLine + last.NewCount == b.LineNo
                && (long)last.OldCount + del <= int.MaxValue && (long)last.NewCount + ins <= int.MaxValue)
            {
                Hunks[^1] = last with { OldCount = last.OldCount + del, NewCount = last.NewCount + ins };
                return;
            }
        }
        Hunks.Add(new Hunk(a.LineNo, del, b.LineNo, ins));
    }

    public void End(TextDiffCursor a, TextDiffCursor b)
    {
        OldLines = a.LineNo - 1;
        NewLines = b.LineNo - 1;
    }

    /// <summary>変更の位置を書く（1 件 24 バイト・リトルエンディアン）。</summary>
    public static void Save(string path, IReadOnlyList<Hunk> hunks)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        Span<byte> b = stackalloc byte[24];
        foreach (Hunk h in hunks)
        {
            MemoryMarshal.Write(b, h.OldLine);
            MemoryMarshal.Write(b[8..], h.OldCount);
            MemoryMarshal.Write(b[12..], h.NewLine);
            MemoryMarshal.Write(b[20..], h.NewCount);
            fs.Write(b);
        }
    }

    public static List<Hunk> Load(string path)
    {
        byte[] all = File.ReadAllBytes(path);
        var list = new List<Hunk>(all.Length / 24);
        for (int i = 0; i + 24 <= all.Length; i += 24)
        {
            ReadOnlySpan<byte> b = all.AsSpan(i, 24);
            list.Add(new Hunk(MemoryMarshal.Read<long>(b), MemoryMarshal.Read<int>(b[8..]), MemoryMarshal.Read<long>(b[12..]), MemoryMarshal.Read<int>(b[20..])));
        }
        return list;
    }
}

/// <summary>
/// 1 本のファイルの行の位置の間引き索引（Every 行ごとの先頭の位置）。巨大なファイルの好きな行を画面に出すために使う。
/// 行の切り方は比較と同じ（\n で切る。最後の改行の無い行も 1 行）。文字コードは ASCII と重なるもの（UTF-8・CP932・EUC-JP）であること。
/// </summary>
public sealed class LineIndex
{
    public const int Every = 1024;
    private readonly string _path;
    private readonly long[] _starts;      // _starts[k] = (k × Every) 行目（0 始まり）の先頭の位置
    public long Lines { get; }
    public long StartOffset { get; }

    private LineIndex(string path, long[] starts, long lines, long startOffset)
    {
        _path = path;
        _starts = starts;
        Lines = lines;
        StartOffset = startOffset;
    }

    /// <summary>ファイルを 1 回読んで索引を作る（startOffset は BOM の後ろ）。</summary>
    public static LineIndex Build(string path, long startOffset, Action<long, long>? progress = null, CancellationToken ct = default)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
        long len = fs.Length;
        fs.Position = startOffset;
        var starts = new List<long> { startOffset };
        byte[] buf = GC.AllocateUninitializedArray<byte>(4 << 20);
        long pos = startOffset, line = 0;
        long lastProgress = Environment.TickCount64;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int n = fs.Read(buf, 0, buf.Length);
            if (n == 0) break;
            ReadOnlySpan<byte> s = buf.AsSpan(0, n);
            int i = 0;
            while (true)
            {
                int k = s[i..].IndexOf((byte)'\n');
                if (k < 0) break;
                i += k + 1;
                line++;
                if (line % Every == 0) starts.Add(pos + i);
            }
            pos += n;
            if (progress != null && Environment.TickCount64 - lastProgress > 200)
            {
                lastProgress = Environment.TickCount64;
                progress(pos, len);
            }
        }
        // 改行で終わらない最後の行も 1 行
        if (pos > startOffset && (line == 0 || LastByte(path) != (byte)'\n')) line++;
        if (starts.Count > 1 && starts[^1] >= pos && line % Every == 0) starts.RemoveAt(starts.Count - 1);
        return new LineIndex(path, starts.ToArray(), line, startOffset);
    }

    /// <summary>比べたときの記録から作る（starts はストリームの中の位置なので、startOffset＝BOM の長さを足す）。</summary>
    public static LineIndex FromStarts(string path, long[] starts, long lines, long startOffset)
    {
        if (starts.Length == 0) starts = new[] { 0L };
        return new LineIndex(path, starts.Select(s => s + startOffset).ToArray(), lines, startOffset);
    }

    /// <summary>
    /// 索引を保存する（見出し: 元のファイルの大きさ・更新日時・行数・BOM の長さ。読むときに合わなければ使わない）。
    /// </summary>
    public void Save(string file)
    {
        var fi = new FileInfo(_path);
        using var w = new BinaryWriter(new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16));
        w.Write(fi.Length);
        w.Write(fi.LastWriteTimeUtc.Ticks);
        w.Write(Lines);
        w.Write(StartOffset);
        w.Write(_starts.Length);
        foreach (long s in _starts) w.Write(s);
    }

    /// <summary>保存した索引を読む。元のファイルが変わっていれば null。</summary>
    public static LineIndex? Load(string file, string path)
    {
        if (!File.Exists(file)) return null;
        var fi = new FileInfo(path);
        using var r = new BinaryReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16));
        if (r.ReadInt64() != fi.Length || r.ReadInt64() != fi.LastWriteTimeUtc.Ticks) return null;
        long lines = r.ReadInt64(), start = r.ReadInt64();
        var starts = new long[r.ReadInt32()];
        for (int i = 0; i < starts.Length; i++) starts[i] = r.ReadInt64();
        return new LineIndex(path, starts, lines, start);
    }

    private static byte LastByte(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length == 0) return 0;
        fs.Position = fs.Length - 1;
        return (byte)fs.ReadByte();
    }

    /// <summary>first 行目（1 始まり）から count 行を読む（改行と行末の \r は除く）。ファイルの終わりを超えた分は返さない。</summary>
    public List<byte[]> Read(long first, int count)
    {
        var result = new List<byte[]>(count);
        if (first < 1 || first > Lines || count <= 0) return result;
        long zero = first - 1;
        long block = zero / Every;
        int skip = (int)(zero % Every);
        using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        fs.Position = _starts[block];
        var cur = new List<byte>(256);
        int b;
        int seen = 0;
        while (result.Count < count && (b = fs.ReadByte()) >= 0)
        {
            if (b == '\n')
            {
                if (seen >= skip) result.Add(Trim(cur));
                seen++;
                cur.Clear();
                continue;
            }
            if (seen >= skip) cur.Add((byte)b);
        }
        if (result.Count < count && cur.Count > 0 && seen >= skip && first - 1 + result.Count < Lines) result.Add(Trim(cur));
        return result;
    }

    private static byte[] Trim(List<byte> l)
    {
        int n = l.Count;
        if (n > 0 && l[n - 1] == (byte)'\r') n--;
        var a = new byte[n];
        l.CopyTo(0, a, 0, n);
        return a;
    }
}
