using Avalonia.Controls;
using Avalonia.Platform;

namespace UwDiff.App;

/// <summary>窓のアイコン（Assets/Icon.ico。絵は tools/make_icon.py で作る）。読めなければ付けない。</summary>
internal static class AppIcon
{
    private static readonly Lazy<WindowIcon?> _value = new(() =>
    {
        try { using var s = AssetLoader.Open(new Uri("avares://UwDiff/Assets/Icon.ico")); return new WindowIcon(s); }
        catch { return null; }
    });

    public static WindowIcon? Value => _value.Value;
}
