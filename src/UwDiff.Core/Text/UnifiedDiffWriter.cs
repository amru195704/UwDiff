using System.Buffers;
using System.Globalization;
using System.Text;

namespace UwDiff.Core.Text;

/// <summary>
/// 差分を unified diff（diff -u と同じ形）で書く。前後の文脈は context 行。変更の間が 2×context 行以下ならひとまとめにする。
/// 行はバイト列のまま書く（旧と新の文字コードが違って UTF-8 にそろえたときは UTF-8）。
/// まとまり（hunk）は書き終えるまで見出しの行数が決まらないので貯める。大きくなったら一時ファイルへ。
/// </summary>
public sealed class UnifiedDiffWriter : ITextDiffSink, IDisposable
{
    private const int SpillAt = 64 << 20;
    private readonly Stream _out;
    private readonly int _ctx;
    private readonly string _header;
    private readonly Func<string> _tempPath;
    private bool _headerWritten;

    private readonly List<byte[]> _ring = new();     // 変更の前の文脈（まとまりを開く前の、直前の同じ行）
    private readonly List<byte[]> _tail = new();     // まとまりを開いた後、最後の変更から続く同じ行（2×context 行まで）
    private long _tailCount;
    private bool _open;
    private long _oldStart, _newStart, _oldCount, _newCount;
    private readonly ArrayBufferWriter<byte> _buf = new(1 << 16);
    private FileStream? _spill;

    public long Hunks { get; private set; }

    public UnifiedDiffWriter(Stream output, TextSource oldSrc, TextSource newSrc, int context = 3, Func<string>? tempPath = null)
    {
        _out = output;
        _ctx = Math.Max(0, context);
        _tempPath = tempPath ?? (() => Path.GetTempFileName());
        // diff -u と同じ「2026-10-24 10:00:00.000 +0900」の形
        static string Stamp(DateTime? t)
        {
            if (t is not DateTime d) return "";
            TimeSpan off = TimeZoneInfo.Local.GetUtcOffset(d);
            return "\t" + d.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + (off < TimeSpan.Zero ? " -" : " +") + off.Duration().ToString("hhmm", CultureInfo.InvariantCulture);
        }
        _header = $"--- {oldSrc.Name}{Stamp(oldSrc.Modified)}\n+++ {newSrc.Name}{Stamp(newSrc.Modified)}\n";
    }

    public void Equal(TextDiffCursor a, TextDiffCursor b, int count)
    {
        if (!_open)
        {
            for (int i = Math.Max(0, count - _ctx); i < count; i++) PushRing(a.Line(i).ToArray());
            return;
        }
        int k = 0;
        while (k < count && _tail.Count < 2 * _ctx) _tail.Add(a.Line(k++).ToArray());
        _tailCount += count;
        if (_tailCount <= 2 * _ctx) return;

        // 間が開いた: 文脈を context 行つけて閉じ、次のための直前の文脈を残す
        for (int i = 0; i < _ctx && i < _tail.Count; i++) Context(_tail[i]);
        CloseHunk();
        _ring.Clear();
        int rest = count - k;
        if (rest >= _ctx)
        {
            for (int i = count - _ctx; i < count; i++) _ring.Add(a.Line(i).ToArray());
        }
        else
        {
            for (int i = Math.Max(0, _tail.Count - (_ctx - rest)); i < _tail.Count; i++) _ring.Add(_tail[i]);
            for (int i = k; i < count; i++) _ring.Add(a.Line(i).ToArray());
        }
        _tail.Clear();
        _tailCount = 0;
    }

    public void Change(TextDiffCursor a, int del, TextDiffCursor b, int ins)
    {
        if (!_open)
        {
            _open = true;
            _oldStart = a.LineNo - _ring.Count;
            _newStart = b.LineNo - _ring.Count;
            _oldCount = _newCount = 0;
            foreach (byte[] l in _ring) Context(l);
            _ring.Clear();
        }
        else
        {
            foreach (byte[] l in _tail) Context(l);
        }
        _tail.Clear();
        _tailCount = 0;
        for (int i = 0; i < del; i++) Put((byte)'-', a.Line(i), a.IsUnterminated(i));
        _oldCount += del;
        for (int i = 0; i < ins; i++) Put((byte)'+', b.Line(i), b.IsUnterminated(i));
        _newCount += ins;
    }

    public void End(TextDiffCursor a, TextDiffCursor b)
    {
        if (_open)
        {
            for (int i = 0; i < _ctx && i < _tail.Count; i++) Context(_tail[i]);
            CloseHunk();
        }
        _out.Flush();
    }

    private void PushRing(byte[] line)
    {
        if (_ctx == 0) return;
        if (_ring.Count == _ctx) _ring.RemoveAt(0);
        _ring.Add(line);
    }

    private void Context(byte[] line)
    {
        Put((byte)' ', line, false);
        _oldCount++;
        _newCount++;
    }

    private void Put(byte mark, ReadOnlySpan<byte> line, bool unterminated)
    {
        Span<byte> d = _buf.GetSpan(line.Length + 2);
        d[0] = mark;
        line.CopyTo(d[1..]);
        d[line.Length + 1] = (byte)'\n';
        _buf.Advance(line.Length + 2);
        if (unterminated) _buf.Write("\\ No newline at end of file\n"u8);
        if (_buf.WrittenCount >= SpillAt)
        {
            _spill ??= new FileStream(_tempPath(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
            _spill.Write(_buf.WrittenSpan);
            _buf.Clear();
        }
    }

    private void CloseHunk()
    {
        if (!_headerWritten)
        {
            _out.Write(Encoding.UTF8.GetBytes(_header));
            _headerWritten = true;
        }
        static string Range(long start, long count) => count == 1 ? start.ToString(CultureInfo.InvariantCulture)
            : $"{(count == 0 ? start - 1 : start).ToString(CultureInfo.InvariantCulture)},{count.ToString(CultureInfo.InvariantCulture)}";
        _out.Write(Encoding.ASCII.GetBytes($"@@ -{Range(_oldStart, _oldCount)} +{Range(_newStart, _newCount)} @@\n"));
        if (_spill != null)
        {
            _spill.Flush();
            _spill.Position = 0;
            _spill.CopyTo(_out, 1 << 20);
            _spill.SetLength(0);
        }
        _out.Write(_buf.WrittenSpan);
        _buf.Clear();
        _open = false;
        Hunks++;
    }

    public void Dispose()
    {
        _spill?.Dispose();
        _spill = null;
    }
}
