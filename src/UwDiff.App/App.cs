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

    private NativeMenuItem? _about, _settings;

    public override void Initialize()
    {
        // mac のアプリのメニュー（UwDiff について・設定…）。窓を作る前に入れる（後から入れると Avalonia の既定の
        // 「About Avalonia」のままになる。UwView は App.axaml で入れている）。Services・Hide・Quit は OS が足す
        _about = new NativeMenuItem();
        _about.Click += (_, _) => { if (Main is { } w) _ = AboutWindow.ShowAsync(w); };
        _settings = new NativeMenuItem { Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.OemComma, Avalonia.Input.KeyModifiers.Meta) };
        _settings.Click += (_, _) => Main?.OpenSettings();
        var appMenu = new NativeMenu();
        appMenu.Items.Add(_about);
        appMenu.Items.Add(new NativeMenuItemSeparator());
        appMenu.Items.Add(_settings);
        NativeMenu.SetMenu(this, appMenu);
        AppMenuTexts();
        Loc.Changed += AppMenuTexts;
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

    private void AppMenuTexts()
    {
        if (_about != null) _about.Header = Loc.T("AppAbout");
        if (_settings != null) _settings.Header = Loc.T("AppSettings");
    }

    private static MainWindow? Main => (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(StartArgs);
        base.OnFrameworkInitializationCompleted();
    }
}
