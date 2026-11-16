using Avalonia.Headless.XUnit;
using UwDiff.App;
using Xunit;

namespace UwDiff.UiTests;

/// <summary>窓のアイコン（Assets/Icon.ico）が実行ファイルに入っていて、本体の窓に付くこと。</summary>
public class AppIconTests
{
    [AvaloniaFact]
    public void アイコンが読めて本体の窓に付く()
    {
        Assert.NotNull(AppIcon.Value);
        var w = new MainWindow([]);
        try { Assert.Same(AppIcon.Value, w.Icon); }
        finally { w.Close(); }
    }
}
