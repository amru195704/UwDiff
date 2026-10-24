using System.Text;
using UwDiff.Core;
using UwDiff.Core.Text;

namespace UwDiff.UiTests;

/// <summary>比べたときに記録した行の位置の索引が、ファイルを読み直して作った索引と同じ行を返すか。</summary>
public class LineIndexTests
{
    [Theory]
    [InlineData(5000, "\n", false, true)]
    [InlineData(5000, "\r\n", true, true)]
    [InlineData(1024, "\n", false, false)]
    [InlineData(2048, "\n", true, false)]
    [InlineData(1, "\n", false, false)]
    [InlineData(0, "\n", false, true)]
    public void 記録した索引と読み直した索引が同じ(int lines, string nl, bool bom, bool finalNewline)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"uvd-idx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string o = Path.Combine(dir, "o.txt"), n = Path.Combine(dir, "n.txt");
        var ls = Enumerable.Range(0, lines).Select(i => $"{i} 行 {new string('x', i % 50)}").ToList();
        string text = string.Join(nl, ls) + (finalNewline && lines > 0 ? nl : "");
        byte[] body = Encoding.UTF8.GetBytes(text);
        File.WriteAllBytes(o, bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body);
        File.WriteAllText(n, "別のファイル\n");
        TextDiffResult r = new TextDiffEngine(new TextDiffOptions { RecordLineIndex = true, ChunkBytes = 4096 })
            .Run(TextSource.FromFile(o), TextSource.FromFile(n), new HunkRecorder());
        Assert.True(r.OldRaw);
        LineIndex rec = LineIndex.FromStarts(o, r.OldLineStarts!, r.OldLines, bom ? 3 : 0);
        LineIndex built = LineIndex.Build(o, bom ? 3 : 0);
        Assert.Equal(built.Lines, rec.Lines);
        Assert.Equal(lines, rec.Lines);
        foreach (long first in new long[] { 1, 2, 1023, 1024, 1025, 2047, 2048, 2049, Math.Max(1, lines - 3) })
        {
            if (first > lines) continue;
            var a = rec.Read(first, 5).Select(x => Encoding.UTF8.GetString(x)).ToList();
            var b = built.Read(first, 5).Select(x => Encoding.UTF8.GetString(x)).ToList();
            Assert.Equal(b, a);
            Assert.Equal(ls.Skip((int)first - 1).Take(5).ToList(), a);
        }
        Directory.Delete(dir, true);
    }
}
