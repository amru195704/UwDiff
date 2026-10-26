using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using UwDiff.App.Views;
using UwDiff.Core;

namespace UwDiff.App;

/// <summary>
/// UwDiff の画面。上で旧・新（ファイルかフォルダ）と比べ方を選んで「比べる」。結果はタブで開く（テキスト・表・フォルダ）。
/// 比べる処理は uvd を同じプロセスの中で呼び（-result で結果の置き場所に書かせ）、それを読んで見せる。
/// ファイルを 2 つ落とすと旧・新に入る。
/// メニューは UwView と同じ並び: mac はメニューバー（UwDiff・ファイル・ヘルプ）、Windows・Linux は窓の上（設定と「について」はファイル・ヘルプに入る）。
/// 文言は Loc（日本語・英語）。設定で切り替えると、その場で入れ替える。
/// </summary>
public sealed class MainWindow : Window
{
    internal readonly TextBox _old = PathBox();
    internal readonly TextBox _new = PathBox();
    internal readonly TextBox _key = new() { Width = 300 };
    internal readonly CheckBox _b = new(), _w = new(), _i = new(), _unordered = new();
    internal readonly Button _compare = new() { Classes = { "accent" }, Padding = new Thickness(18, 5) };
    internal readonly TabControl _tabs = new();
    internal readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4) };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 4, IsVisible = false };
    private readonly NativeMenuBar _menuBar = new();
    internal readonly ComboBox _lang = new() { ItemsSource = new[] { "日本語", "English" }, Width = 120 };
    private readonly List<(TextBlock Label, Button File, Button Folder, bool IsOld)> _pathRows = new();
    /// <summary>フォルダどうしを比べるときの、含める・除く名前の型（-include・-exclude）。旧・新がどちらもフォルダのときだけ入力できる。</summary>
    internal readonly TextBox _include = new() { Width = 240, Margin = new Thickness(6, 2) };
    internal readonly TextBox _exclude = new() { Width = 240, Margin = new Thickness(6, 2) };
    private readonly TextBlock _includeLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly TextBlock _excludeLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };

    /// <summary>旧・新がどちらもフォルダか（含める・除くを使えるのはこのときだけ）。</summary>
    internal bool FolderMode => IsDir(_old.Text) && IsDir(_new.Text);
    private static bool IsDir(string? p) => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p.Trim());
    internal string TempDir { get; } = Path.Combine(Path.GetTempPath(), $"uwdiff-{Environment.ProcessId}");

    private static TextBox PathBox() => new() { Margin = new Thickness(6, 2) };
    private static KeyModifiers Cmd => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    public MainWindow(string[] args)
    {
        Width = 1280;
        Height = 820;
        Directory.CreateDirectory(TempDir);

        // 旧・新の行の右に、含める（旧の行）・除く（新の行）。パスの欄はその分短くなる
        var paths = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(8, 8, 8, 0) };
        AddPathRow(paths, 0, _old, true);
        AddPathRow(paths, 1, _new, false);
        foreach (var (c, row, col) in new (Control, int, int)[] { (_includeLabel, 0, 4), (_include, 0, 5), (_excludeLabel, 1, 4), (_exclude, 1, 5) })
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, col);
            paths.Children.Add(c);
        }
        ToolTip.SetShowOnDisabled(_include, true);
        ToolTip.SetShowOnDisabled(_exclude, true);
        _old.TextChanged += (_, _) => UpdateFolderMode();
        _new.TextChanged += (_, _) => UpdateFolderMode();
        var opts = new WrapPanel { Margin = new Thickness(8, 4), Orientation = Orientation.Horizontal };
        _lang.SelectedIndex = Loc.Ja ? 0 : 1;
        _lang.SelectionChanged += (_, _) => { if (_lang.SelectedIndex >= 0) Loc.Set(_lang.SelectedIndex == 0); };
        foreach (Control c in new Control[] { _key, _b, _w, _i, _unordered, _compare, _lang })
        {
            c.Margin = new Thickness(6, 2);
            c.VerticalAlignment = VerticalAlignment.Center;
            opts.Children.Add(c);
        }
        _compare.Click += async (_, _) => await CompareAsync(_old.Text, _new.Text);

        // Windows・Linux は窓の上にメニュー（mac は NativeMenuBar が隠れ、メニューバーに出る）
        var top = new StackPanel { Children = { _menuBar, paths, opts, _progress } };
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(_status);
        root.Children.Add(_tabs);
        Content = root;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            if (files == null || files.Count == 0) return;
            if (files.Count >= 2) { _old.Text = files[0]; _new.Text = files[1]; }
            else if (string.IsNullOrEmpty(_old.Text)) _old.Text = files[0];
            else _new.Text = files[0];
        });

        // mac 以外はメニューの近道を窓のキーで受ける（mac はメニューバーが受ける）
        if (!OperatingSystem.IsMacOS())
        {
            void Bind(Key k, KeyModifiers m, Action a) => KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(k, m), Command = new Relay(a) });
            Bind(Key.O, Cmd, () => _ = PickInto(_old, true));
            Bind(Key.Enter, Cmd, () => _ = CompareAsync(_old.Text, _new.Text));
            Bind(Key.K, Cmd | KeyModifiers.Shift, OpenCommandLine);
            Bind(Key.W, Cmd, CloseTab);
            Bind(Key.OemComma, Cmd, OpenSettings);
        }

        Texts();
        UpdateFolderMode();
        Loc.Changed += Texts;
        Closed += (_, _) =>
        {
            Loc.Changed -= Texts;
            foreach (TabItem t in _tabs.Items.OfType<TabItem>().ToList()) (t.Content as IResultView)?.Release();
            try { Directory.Delete(TempDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        };
        Opened += async (_, _) => await StartAsync(args);
    }

    private void AddPathRow(Grid g, int row, TextBox box, bool isOld)
    {
        var l = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
        var file = new Button { Margin = new Thickness(2) };
        var dir = new Button { Margin = new Thickness(2) };
        file.Click += async (_, _) => await PickInto(box, isOld);
        dir.Click += async (_, _) =>
        {
            var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc.T(isOld ? "Old" : "New"), AllowMultiple = false });
            if (r.Count > 0 && r[0].TryGetLocalPath() is { } p) box.Text = p;
        };
        foreach (var (c, col) in new (Control, int)[] { (l, 0), (box, 1), (file, 2), (dir, 3) })
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, col);
            g.Children.Add(c);
        }
        _pathRows.Add((l, file, dir, isOld));
    }

    private async Task PickInto(TextBox box, bool isOld)
    {
        var r = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Loc.T(isOld ? "OpenOld" : "OpenNew"), AllowMultiple = false });
        if (r.Count > 0 && r[0].TryGetLocalPath() is { } p) box.Text = p;
    }

    /// <summary>文言（と、文言を持つメニュー）を、いまの言語で入れ直す。</summary>
    private void Texts()
    {
        Title = $"UwDiff {Uvd.Cli.Program.Version} (build {Uvd.Cli.Program.BuildNumber})";
        foreach (var (label, file, folder, isOld) in _pathRows)
        {
            label.Text = Loc.T(isOld ? "Old" : "New");
            file.Content = Loc.T("PickFile");
            folder.Content = Loc.T("PickFolder");
            ToolTip.SetTip(file, Loc.T("TipPickFile"));
            ToolTip.SetTip(folder, Loc.T("TipPickFolder"));
        }
        _old.PlaceholderText = Loc.T("OldPlaceholder");
        _new.PlaceholderText = Loc.T("NewPlaceholder");
        _key.PlaceholderText = Loc.T("KeyPlaceholder");
        _b.Content = Loc.T("IgnoreSpaceChange");
        _w.Content = Loc.T("IgnoreAllSpace");
        _i.Content = Loc.T("IgnoreCase");
        _unordered.Content = Loc.T("Unordered");
        _compare.Content = Loc.T("Compare");
        ToolTip.SetTip(_old, Loc.T("TipOld"));
        ToolTip.SetTip(_new, Loc.T("TipNew"));
        ToolTip.SetTip(_key, Loc.T("TipKey"));
        ToolTip.SetTip(_b, Loc.T("TipIgnoreSpaceChange"));
        ToolTip.SetTip(_w, Loc.T("TipIgnoreAllSpace"));
        ToolTip.SetTip(_i, Loc.T("TipIgnoreCase"));
        ToolTip.SetTip(_unordered, Loc.T("TipUnordered"));
        ToolTip.SetTip(_compare, Loc.T("TipCompare"));
        ToolTip.SetTip(_lang, Loc.T("TipLangBox"));
        _includeLabel.Text = Loc.T("Include");
        _excludeLabel.Text = Loc.T("Exclude");
        _include.PlaceholderText = Loc.T("IncludePlaceholder");
        _exclude.PlaceholderText = Loc.T("ExcludePlaceholder");
        UpdateFolderMode();
        if (_lang.SelectedIndex != (Loc.Ja ? 0 : 1)) _lang.SelectedIndex = Loc.Ja ? 0 : 1;
        foreach (TabItem t in _tabs.Items.OfType<TabItem>())
            if (t.Header is StackPanel { Children: [_, Button close] }) ToolTip.SetTip(close, Loc.T("TipCloseTab"));
        BuildMenus();
    }

    // ---- メニュー（一度だけ作り、言語が替わったら文言だけ入れ替える） ----
    private readonly List<(NativeMenuItem Item, string Key)> _menuTexts = new();

    private NativeMenuItem Item(string key, Action click, Key? key2 = null, KeyModifiers mods = KeyModifiers.None)
    {
        var it = new NativeMenuItem(Loc.T(key));
        it.Click += (_, _) => click();
        if (key2 is Key k) it.Gesture = new KeyGesture(k, mods);
        _menuTexts.Add((it, key));
        return it;
    }

    private NativeMenuItem Sub(string key, params NativeMenuItemBase[] items)
    {
        var m = new NativeMenu();
        foreach (var i in items) m.Items.Add(i);
        var it = new NativeMenuItem(Loc.T(key)) { Menu = m };
        _menuTexts.Add((it, key));
        return it;
    }

    private void BuildMenus()
    {
        if (_menuTexts.Count > 0)
        {
            foreach (var (item, key) in _menuTexts) item.Header = Loc.T(key);
            return;
        }
        bool mac = OperatingSystem.IsMacOS();
        var file = new List<NativeMenuItemBase>
        {
            Item("OpenOld", () => _ = PickInto(_old, true), Key.O, Cmd),
            Item("OpenNew", () => _ = PickInto(_new, false)),
            Item("CompareMenu", () => _ = CompareAsync(_old.Text, _new.Text), Key.Enter, Cmd),
            Item("CmdMenu", OpenCommandLine, Key.K, Cmd | KeyModifiers.Shift),
            Item("OpenResult", () => _ = OpenResultFolderAsync()),
            new NativeMenuItemSeparator(),
            Item("CloseTab", CloseTab, Key.W, Cmd),
            Item("CloseAll", CloseAll),
        };
        if (!mac)
        {
            file.Add(new NativeMenuItemSeparator());
            file.Add(Item("AppSettings", OpenSettings, Key.OemComma, Cmd));
            file.Add(Item("Quit", Close));
        }
        var help = new List<NativeMenuItemBase>
        {
            Item("HelpHowTo", () => Links.Open(Links.HowTo)),
            Item("HelpNews", () => Links.Open(Links.News)),
            Item("HelpSupport", () => Links.Open(Links.Support)),
            new NativeMenuItemSeparator(),
            Item("GitHub: UwDiff", () => Links.Open(Links.RepoHome)),
            Item("GitHub: amru195704", () => Links.Open(Links.Profile)),
            new NativeMenuItemSeparator(),
            Item("CliCommandMenu", () => _ = CliCommandDialog.RunAsync(this)),
        };
        if (!mac)
        {
            help.Add(new NativeMenuItemSeparator());
            help.Add(Item("AppAbout", () => _ = AboutWindow.ShowAsync(this)));
        }
        var menu = new NativeMenu();
        menu.Items.Add(Sub("FileMenu", file.ToArray()));
        menu.Items.Add(Sub("HelpMenu", help.ToArray()));
        NativeMenu.SetMenu(this, menu);
        // mac のアプリのメニュー（UwDiff について・設定…）は App.Initialize で入れる
    }

    internal void OpenSettings() => _ = new SettingsWindow().ShowDialog(this);

    /// <summary>コマンドラインの窓（いまの旧・新・比べ方を引数にして開く）。</summary>
    internal void OpenCommandLine()
    {
        static string Q(string s) => s.Contains(' ') || s.Length == 0 ? "\"" + s + "\"" : s;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_old.Text)) parts.Add(Q(_old.Text.Trim()));
        if (!string.IsNullOrWhiteSpace(_new.Text)) parts.Add(Q(_new.Text.Trim()));
        parts.AddRange(Options().Select(Q));
        _ = new CommandLineWindow(this, string.Join(" ", parts)).ShowDialog(this);
    }

    private async Task OpenResultFolderAsync()
    {
        var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc.T("OpenResultTitle"), AllowMultiple = false });
        if (r.Count > 0 && r[0].TryGetLocalPath() is { } p) OpenResult(p);
    }

    private void CloseTab()
    {
        if (_tabs.SelectedItem is TabItem t) Remove(t);
    }

    private void CloseAll()
    {
        foreach (TabItem t in _tabs.Items.OfType<TabItem>().ToList()) Remove(t);
    }

    private void Remove(TabItem t)
    {
        (t.Content as IResultView)?.Release();
        _tabs.Items.Remove(t);
    }

    /// <summary>起動の引数: --result 置き場所（uvd -open から）、または 旧 新 [uvd のオプション]。</summary>
    private async Task StartAsync(string[] args)
    {
        if (args.Length >= 2 && args[0] == ResultPackage.Argument)
        {
            OpenResult(args[1]);
            return;
        }
        if (args.Length >= 2)
        {
            _old.Text = args[0];
            _new.Text = args[1];
            await CompareAsync(args[0], args[1], args[2..]);
        }
    }

    private void Status(string text, bool error = false)
    {
        _status.Text = text;
        _status.Foreground = error ? Brushes.IndianRed : null;
    }

    /// <summary>含める・除くは、旧・新がどちらもフォルダのときだけ入力できる（ツールチップで理由も出す）。</summary>
    private void UpdateFolderMode()
    {
        bool on = FolderMode;
        _include.IsEnabled = _exclude.IsEnabled = on;
        _includeLabel.Opacity = _excludeLabel.Opacity = on ? 1 : 0.45;
        ToolTip.SetTip(_include, Loc.T(on ? "TipInclude" : "TipFolderOnly"));
        ToolTip.SetTip(_exclude, Loc.T(on ? "TipExclude" : "TipFolderOnly"));
    }

    /// <summary>上で選んだ比べ方を uvd のオプションにする。</summary>
    private List<string> Options()
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(_key.Text)) { args.Add("-key"); args.Add(_key.Text.Trim()); }
        if (_b.IsChecked == true) args.Add("-b");
        if (_w.IsChecked == true) args.Add("-w");
        if (_i.IsChecked == true) args.Add("-i");
        if (_unordered.IsChecked == true) args.Add("-unordered");
        if (FolderMode)
        {
            if (!string.IsNullOrWhiteSpace(_include.Text)) { args.Add("-include"); args.Add(_include.Text.Trim()); }
            if (!string.IsNullOrWhiteSpace(_exclude.Text)) { args.Add("-exclude"); args.Add(_exclude.Text.Trim()); }
        }
        return args;
    }

    /// <summary>uvd で比べて（-result で結果の置き場所に書かせて）、タブで開く。</summary>
    internal async Task CompareAsync(string? oldPath, string? newPath, string[]? extra = null)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
        {
            Status(Loc.T("NeedBoth"), true);
            return;
        }
        string dir = ResultPackage.NewFolder(TempDir);
        var args = new List<string> { oldPath.Trim(), newPath.Trim(), "-result", dir, "-tmp", TempDir };
        args.AddRange(extra ?? Options().ToArray());
        _compare.IsEnabled = false;
        _progress.IsVisible = true;
        _progress.IsIndeterminate = true;
        Status(Loc.T("Comparing"));
        try
        {
            UvdRunner.Outcome o = await UvdRunner.RunAsync(args.ToArray(), p => Dispatcher.UIThread.Post(() => { _progress.IsIndeterminate = false; _progress.Value = p; }));
            if (o.ExitCode == 2 || !File.Exists(Path.Combine(dir, "result.json")))
            {
                Status(Loc.F("CannotCompare", o.Error.Length > 0 ? o.Error : o.Output), true);
                return;
            }
            OpenResult(dir);
        }
        finally
        {
            _compare.IsEnabled = true;
            _progress.IsVisible = false;
        }
    }

    internal void OpenResult(string dir)
    {
        ResultPackage pkg;
        try { pkg = ResultPackage.Load(dir); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Status(Loc.F("CannotLoadResult", ex.Message), true);
            return;
        }
        Control view = pkg.Mode switch
        {
            "text" => new TextDiffView(pkg, TempDir),
            "folder" => new FolderDiffView(pkg, (a, b) => _ = CompareAsync(a, b, string.IsNullOrWhiteSpace(_key.Text) ? Array.Empty<string>() : new[] { "-key", _key.Text.Trim() })),
            _ => new TableDiffView(pkg, (s, err) => Status(s, err)),
        };
        string title = pkg.Mode == "folder" ? Path.GetFileName(pkg.Old.TrimEnd('/', '\\')) : Path.GetFileName(pkg.Old);
        var close = new Button { Content = "×", Padding = new Thickness(4, 0), Margin = new Thickness(6, 0, 0, 0), Background = Brushes.Transparent };
        ToolTip.SetTip(close, Loc.T("TipCloseTab"));
        var tab = new TabItem
        {
            Header = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }, close } },
            Content = view,
        };
        ToolTip.SetTip(tab, pkg.Mode == "folder" ? pkg.Old + " ⇔ " + pkg.New : pkg.Old + "\n" + pkg.New);
        close.Click += (_, _) => Remove(tab);
        _tabs.Items.Add(tab);
        _tabs.SelectedItem = tab;
        if (string.IsNullOrEmpty(_old.Text))
        {
            // uvd -open などで結果から開いたとき: 比べ直せるよう、比べ方も上の欄に写す
            _old.Text = pkg.Old;
            _new.Text = pkg.New;
            if (pkg.Keys.Count > 0) _key.Text = string.Join(",", pkg.Keys);
            _b.IsChecked = pkg.IgnoreSpaceChange;
            _w.IsChecked = pkg.IgnoreAllSpace;
            _i.IsChecked = pkg.IgnoreCase;
            _unordered.IsChecked = pkg.Mode == "unordered";
        }
        // CLI の要約は日本語。英語の画面では、開いたことだけを知らせる（中身の要約は各タブが英語で出す）
        Status(Loc.Ja ? string.Join("\n", pkg.Summary.Take(4)) : Loc.F("ResultOpened", title));
    }

    /// <summary>キーの割り当て用の小さなコマンド。</summary>
    private sealed class Relay(Action a) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => a();
    }
}
