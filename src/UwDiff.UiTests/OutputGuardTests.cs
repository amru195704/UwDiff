using UwDiff.Core;

namespace UwDiff.UiTests;

/// <summary>書き出しの守り（一時ファイルから入れ替える）の、名前の付け替えに失敗したときの後片付け。</summary>
public class OutputGuardTests
{
    [Fact]
    public void 入れ替えに失敗しても一時ファイルを残さない()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"uvd-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, "out.diff");
        var f = OutputGuard.SafeFile.Create(target);
        f.Write(new byte[1 << 20]);
        string temp = f.TempPath;
        Assert.True(File.Exists(temp));
        // 書いている間に、書き出し先にフォルダができた（名前の付け替えが失敗する）
        Directory.CreateDirectory(target);
        Assert.ThrowsAny<IOException>(() => f.Commit());
        Assert.False(File.Exists(temp));
        f.Dispose();   // 二度目も何も起きない
        Directory.Delete(dir, true);
    }

    [Fact]
    public void 入れ替えずに閉じれば一時ファイルを消し書き出し先は元のまま()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"uvd-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, "keep.txt");
        File.WriteAllText(target, "KEEP");
        using (var f = OutputGuard.SafeFile.Create(target)) f.Write("new"u8);
        Assert.Equal("KEEP", File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(dir));
        Directory.Delete(dir, true);
    }
}
