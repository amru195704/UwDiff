using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using UwView.Core.Cli;

namespace UwDiff.App;

/// <summary>知らせ・選択の小さな窓（文と、いくつかのボタン。押したボタンの番号を返す。閉じたら -1）。</summary>
public static class MessageBox
{
    /// <summary>試験用: 窓を出さずに答えを返す（null なら本当に出す）。</summary>
    internal static Func<string, string[], int>? Override { get; set; }

    public static async Task<int> ShowAsync(Window owner, string text, params string[] buttons)
    {
        if (Override != null) return Override(text, buttons);
        if (buttons.Length == 0) buttons = new[] { Loc.T("Ok") };
        int chosen = -1;
        var w = new Window
        {
            Title = "UwDiff",
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 640,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        for (int i = 0; i < buttons.Length; i++)
        {
            int k = i;
            var b = new Button { Content = buttons[i], MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (i == 0) b.Classes.Add("accent");
            b.Click += (_, _) => { chosen = k; w.Close(); };
            row.Children.Add(b);
        }
        w.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children = { new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 600 }, row },
        };
        await w.ShowDialog(owner);
        return chosen;
    }
}

/// <summary>設定（いまは言語だけ）。</summary>
public sealed class SettingsWindow : Window
{
    private readonly RadioButton _ja = new() { GroupName = "lang" }, _en = new() { GroupName = "lang" };
    private readonly TextBlock _label = new() { FontWeight = FontWeight.SemiBold };
    private readonly Button _close = new() { HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90 };

    public SettingsWindow()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _ja.IsChecked = Loc.Ja;
        _en.IsChecked = !Loc.Ja;
        _ja.IsCheckedChanged += (_, _) => { if (_ja.IsChecked == true) Loc.Set(true); };
        _en.IsCheckedChanged += (_, _) => { if (_en.IsChecked == true) Loc.Set(false); };
        _close.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 10,
            MinWidth = 320,
            Children = { _label, _ja, _en, _close },
        };
        Texts();
        Loc.Changed += Texts;
        Closed += (_, _) => Loc.Changed -= Texts;
    }

    private void Texts()
    {
        Title = Loc.T("SettingsTitle");
        _label.Text = Loc.T("Language");
        _ja.Content = Loc.T("LangJa");
        _en.Content = Loc.T("LangEn");
        ToolTip.SetTip(_ja, Loc.T("TipLanguage"));
        ToolTip.SetTip(_en, Loc.T("TipLanguage"));
        _close.Content = Loc.T("Close");
    }
}

/// <summary>UwDiff について（UwView と同じ形: 名前・版とビルド番号・説明・©・リンク）。</summary>
public static class AboutWindow
{
    public static Task ShowAsync(Window owner) => Make().ShowDialog(owner);

    internal static Window Make() => new()
    {
        Title = Loc.T("AppAbout"),
        Width = 420,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = new StackPanel
        {
            Margin = new Thickness(28, 24),
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "UwDiff", FontSize = 26, FontWeight = FontWeight.Bold },
                new TextBlock { Text = $"Version {Uvd.Cli.Program.Version}  (build {Uvd.Cli.Program.BuildNumber})" },
                new TextBlock { Text = Loc.T("AboutText"), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "© 2026 amru195704 (Y4U)", Margin = new Thickness(0, 10, 0, 0) },
                Link(Loc.T("AboutNews"), Links.News),
                Link(Loc.T("AboutHowTo"), Links.HowTo),
                Link(Loc.T("HelpSupport"), Links.Support),
                Link("GitHub", Links.RepoHome),
                Link(Loc.T("License"), Links.LicenseFile),
            },
        },
    };

    /// <summary>押すと既定のブラウザで開く、リンクの形の文字。</summary>
    private static TextBlock Link(string label, string url)
    {
        var t = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.Parse("#2F6FDB")),
            TextDecorations = TextDecorations.Underline,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(t, url);
        t.PointerPressed += (_, _) => Links.Open(url);
        return t;
    }
}

/// <summary>
/// メニュー「コマンドライン…」: uvd の引数を打って実行する。比べた結果は新しいタブで開く（uvd -open と同じ。画面はもう一度比べない）。
/// apply や -h などは出力を窓に出す。
/// </summary>
public sealed class CommandLineWindow : Window
{
    private readonly MainWindow _main;
    internal readonly TextBox _args = new() { MinWidth = 640, FontFamily = Palette.Mono };
    private readonly Button _run = new() { Classes = { "accent" }, MinWidth = 80 };
    private readonly Button _help = new();
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 760, Foreground = Palette.Muted };
    private readonly SelectableTextBlock _output = new() { FontFamily = Palette.Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap };

    public CommandLineWindow(MainWindow main, string initial)
    {
        _main = main;
        Width = 820;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _args.Text = initial;
        _run.Click += async (_, _) => await RunAsync();
        _help.Click += async (_, _) => { _args.Text = "-h"; await RunAsync(); };
        _args.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await RunAsync(); };
        var top = new DockPanel { LastChildFill = true };
        var label = new TextBlock { Text = "uvd", VerticalAlignment = VerticalAlignment.Center, FontFamily = Palette.Mono, Margin = new Thickness(0, 0, 8, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0, 0, 0), Children = { _run, _help } };
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(label);
        top.Children.Add(buttons);
        top.Children.Add(_args);
        var root = new DockPanel { Margin = new Thickness(14) };
        var head = new StackPanel { Spacing = 8, Children = { top, _hint } };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        root.Children.Add(new ScrollViewer { Content = _output, Margin = new Thickness(0, 10, 0, 0) });
        Content = root;
        Texts();
        Loc.Changed += Texts;
        Closed += (_, _) => Loc.Changed -= Texts;
        Opened += (_, _) => _args.Focus();
    }

    private void Texts()
    {
        Title = Loc.T("CmdTitle");
        _run.Content = Loc.T("Run");
        _help.Content = Loc.T("CmdHelpButton");
        _hint.Text = Loc.T("CmdHelp");
        ToolTip.SetTip(_run, Loc.T("TipRun"));
        ToolTip.SetTip(_help, Loc.T("TipCmdHelpButton"));
        ToolTip.SetTip(_args, Loc.T("TipCmdArgs"));
    }

    internal async Task RunAsync()
    {
        List<string> args = Split(_args.Text ?? "");
        if (args.Count > 0 && (args[0] == "uvd" || args[0].EndsWith("/uvd") || args[0].EndsWith("\\uvd.exe"))) args.RemoveAt(0);
        if (args.Count == 0) return;
        _run.IsEnabled = false;
        try
        {
            // 比べる形（ファイル 2 つ・フォルダ 2 つ）なら結果を画面で開く。apply・-h・--version はそのまま
            bool compare = args[0] != "apply" && !args.Contains("-h") && !args.Contains("--help") && !args.Contains("--version");
            args.Remove("-open");
            string? dir = null;
            if (compare)
            {
                dir = UwDiff.Core.ResultPackage.NewFolder(_main.TempDir);
                args.AddRange(new[] { "-result", dir, "-q" });
            }
            UvdRunner.Outcome o = await UvdRunner.RunAsync(args.ToArray(), null);
            _output.Text = (o.Output + (o.Error.Length > 0 ? "\n" + o.Error : "")).Trim() + $"\n\n（exit {o.ExitCode}）";
            if (dir != null && o.ExitCode != 2 && File.Exists(Path.Combine(dir, "result.json"))) _main.OpenResult(dir);
        }
        finally
        {
            _run.IsEnabled = true;
        }
    }

    /// <summary>引数を空白で分ける（" か ' で囲んだ所は 1 つ。\" は "）。</summary>
    internal static List<string> Split(string s)
    {
        var r = new List<string>();
        var cur = new StringBuilder();
        bool any = false;
        char q = '\0';
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (q != '\0')
            {
                if (c == q) q = '\0';
                else if (c == '\\' && q == '"' && i + 1 < s.Length && s[i + 1] == '"') { cur.Append('"'); i++; }
                else cur.Append(c);
                continue;
            }
            if (c is '"' or '\'') { q = c; any = true; continue; }
            if (char.IsWhiteSpace(c))
            {
                if (any || cur.Length > 0) r.Add(cur.ToString());
                cur.Clear();
                any = false;
                continue;
            }
            cur.Append(c);
        }
        if (any || cur.Length > 0) r.Add(cur.ToString());
        return r;
    }
}

/// <summary>
/// メニュー「コマンドライン設定…」: uvd をコンソールで名前だけで打てるようにする／解除する（UwView と同じ。中身は CliCommandSetup）。
/// mac・Linux は /usr/local/bin/uvd のリンク（管理者のパスワードを聞かれることがある）、Windows はユーザーの PATH。
/// </summary>
public static class CliCommandDialog
{
    public static async Task RunAsync(Window owner)
    {
        CliCommandStatus status = CliCommandSetup.Inspect("uvd");
        switch (status.State)
        {
            case CliCommandState.Missing:
                await MessageBox.ShowAsync(owner, Loc.T("CliMissing"));
                return;
            case CliCommandState.AppNotInPlace:
                await MessageBox.ShowAsync(owner, Loc.T("CliNotInPlace"));
                return;
            case CliCommandState.Installed:
            {
                string where = OperatingSystem.IsWindows() ? status.Location : $"{status.Location} → {status.Launcher}";
                int c = await MessageBox.ShowAsync(owner, Loc.F("CliInstalled", where), Loc.T("CliReinstall"), Loc.T("CliRemove"), Loc.T("Close"));
                if (c == 0) await Report(owner, status, await CliCommandSetup.InstallAsync(status), true);
                if (c == 1) await Report(owner, status, await CliCommandSetup.UninstallAsync(status), false);
                return;
            }
            case CliCommandState.OtherTarget:
                if (await MessageBox.ShowAsync(owner, Loc.F("CliOther", status.Existing, Plan(status)), Loc.T("CliReplace"), Loc.T("Cancel")) != 0) return;
                await Report(owner, status, await CliCommandSetup.InstallAsync(status), true);
                return;
            default:
                if (await MessageBox.ShowAsync(owner, Loc.F("CliAsk", Plan(status)), Loc.T("CliDoInstall"), Loc.T("Cancel")) != 0) return;
                await Report(owner, status, await CliCommandSetup.InstallAsync(status), true);
                return;
        }
    }

    private static string Plan(CliCommandStatus s) => OperatingSystem.IsWindows() ? Loc.F("CliPlanWin", s.Location) : Loc.F("CliPlanLink", s.Location, s.Launcher);

    private static async Task Report(Window owner, CliCommandStatus s, CliCommandResult r, bool install)
    {
        if (r.Cancelled) return;
        if (!r.Ok) { await MessageBox.ShowAsync(owner, Loc.F("CliFailed", r.Error)); return; }
        await MessageBox.ShowAsync(owner, install ? Loc.F("CliDone", r.InstalledAt ?? s.Location) : Loc.T("CliRemoved"));
    }
}
