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
/// </summary>
public sealed class MainWindow : Window
{
    internal readonly TextBox _old = PathBox("旧（ファイルかフォルダ。ここへ落としてもよい）");
    internal readonly TextBox _new = PathBox("新");
    internal readonly TextBox _key = new() { PlaceholderText = "キー列（表として比べるとき。例: 点名,種別）", Width = 280 };
    private readonly CheckBox _b = new() { Content = "空白の量を無視" };
    private readonly CheckBox _w = new() { Content = "空白をすべて無視" };
    private readonly CheckBox _i = new() { Content = "大文字・小文字を無視" };
    private readonly CheckBox _unordered = new() { Content = "順番を問わない" };
    private readonly Button _compare = new() { Content = "比べる", Classes = { "accent" }, Padding = new Thickness(18, 5) };
    internal readonly TabControl _tabs = new();
    internal readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4) };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 4, IsVisible = false };
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"uwdiff-{Environment.ProcessId}");

    private static TextBox PathBox(string watermark) => new() { PlaceholderText = watermark, Margin = new Thickness(6, 2) };

    public MainWindow(string[] args)
    {
        Title = $"UwDiff {Uvd.Cli.Program.Version} (build {Uvd.Cli.Program.BuildNumber})";
        Width = 1280;
        Height = 820;
        Directory.CreateDirectory(_tempDir);

        var paths = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(8, 8, 8, 0) };
        AddPathRow(paths, 0, "旧", _old);
        AddPathRow(paths, 1, "新", _new);
        var opts = new WrapPanel { Margin = new Thickness(8, 4), Orientation = Orientation.Horizontal };
        foreach (Control c in new Control[] { _key, _b, _w, _i, _unordered, _compare })
        {
            c.Margin = new Thickness(6, 2);
            c.VerticalAlignment = VerticalAlignment.Center;
            opts.Children.Add(c);
        }
        _compare.Click += async (_, _) => await CompareAsync(_old.Text, _new.Text);

        var top = new StackPanel { Children = { paths, opts, _progress } };
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
        Closed += (_, _) => { try { Directory.Delete(_tempDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } };
        Opened += async (_, _) => await StartAsync(args);
    }

    private void AddPathRow(Grid g, int row, string label, TextBox box)
    {
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
        var file = new Button { Content = "ファイル…", Margin = new Thickness(2) };
        var dir = new Button { Content = "フォルダ…", Margin = new Thickness(2) };
        file.Click += async (_, _) =>
        {
            var r = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = label + "のファイル", AllowMultiple = false });
            if (r.Count > 0 && r[0].TryGetLocalPath() is { } p) box.Text = p;
        };
        dir.Click += async (_, _) =>
        {
            var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = label + "のフォルダ", AllowMultiple = false });
            if (r.Count > 0 && r[0].TryGetLocalPath() is { } p) box.Text = p;
        };
        foreach (var (c, col) in new (Control, int)[] { (l, 0), (box, 1), (file, 2), (dir, 3) })
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, col);
            g.Children.Add(c);
        }
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

    /// <summary>uvd で比べて（-result で結果の置き場所に書かせて）、タブで開く。</summary>
    internal async Task CompareAsync(string? oldPath, string? newPath, string[]? extra = null)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
        {
            Status("旧と新を選んでください", true);
            return;
        }
        string dir = ResultPackage.NewFolder(_tempDir);
        var args = new List<string> { oldPath.Trim(), newPath.Trim(), "-result", dir, "-tmp", _tempDir };
        if (extra != null) args.AddRange(extra);
        else
        {
            if (!string.IsNullOrWhiteSpace(_key.Text)) { args.Add("-key"); args.Add(_key.Text.Trim()); }
            if (_b.IsChecked == true) args.Add("-b");
            if (_w.IsChecked == true) args.Add("-w");
            if (_i.IsChecked == true) args.Add("-i");
            if (_unordered.IsChecked == true) args.Add("-unordered");
        }
        _compare.IsEnabled = false;
        _progress.IsVisible = true;
        _progress.IsIndeterminate = true;
        Status("比べています…");
        try
        {
            UvdRunner.Outcome o = await UvdRunner.RunAsync(args.ToArray(), p => Dispatcher.UIThread.Post(() => { _progress.IsIndeterminate = false; _progress.Value = p; }));
            if (o.ExitCode == 2 || !File.Exists(Path.Combine(dir, "result.json")))
            {
                Status("比べられません: " + (o.Error.Length > 0 ? o.Error : o.Output), true);
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
            Status("結果を読めません: " + ex.Message, true);
            return;
        }
        Control view = pkg.Mode switch
        {
            "text" => new TextDiffView(pkg, _tempDir),
            "folder" => new FolderDiffView(pkg, (a, b) => _ = CompareAsync(a, b, string.IsNullOrWhiteSpace(_key.Text) ? Array.Empty<string>() : new[] { "-key", _key.Text.Trim() })),
            _ => new TableDiffView(pkg, (s, err) => Status(s, err)),
        };
        string title = pkg.Mode == "folder" ? Path.GetFileName(pkg.Old.TrimEnd('/', '\\')) : Path.GetFileName(pkg.Old);
        var close = new Button { Content = "×", Padding = new Thickness(4, 0), Margin = new Thickness(6, 0, 0, 0), Background = Brushes.Transparent };
        var tab = new TabItem
        {
            Header = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }, close } },
            Content = view,
        };
        close.Click += (_, _) => _tabs.Items.Remove(tab);
        _tabs.Items.Add(tab);
        _tabs.SelectedItem = tab;
        if (string.IsNullOrEmpty(_old.Text)) { _old.Text = pkg.Old; _new.Text = pkg.New; }
        Status(string.Join("\n", pkg.Summary.Take(4)));
    }
}
