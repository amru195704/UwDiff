using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace UwDiff.App;

public sealed class App : Application
{
    public static string[] StartArgs { get; set; } = Array.Empty<string>();

    public override void Initialize()
    {
        Name = "UwDiff";
        RequestedThemeVariant = ThemeVariant.Default;   // OS のライト／ダークに合わせる
        Styles.Add(new FluentTheme());
        // 一覧の 1 行を詰める（差分は行数が多いので、一度に多く見せる）
        Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(ListBoxItem.PaddingProperty, new Thickness(4, 0)),
                new Setter(Layoutable.MinHeightProperty, 0d),
            },
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(StartArgs);
        base.OnFrameworkInitializationCompleted();
    }
}
