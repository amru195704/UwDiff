using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UwDiff.App;
using UwDiff.App.Views;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AvaloniaTestApplication(typeof(UwDiff.UiTests.TestAppBuilder))]

namespace UwDiff.UiTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        // 試験では本物の設定（言語）を書き換えない
        Environment.SetEnvironmentVariable("UWDIFF_SETTINGS", Path.Combine(Path.GetTempPath(), $"uvd-ui-settings-{Environment.ProcessId}"));
        return Build();
    }

    private static AppBuilder Build() => AppBuilder.Configure<App.App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();
}

/// <summary>
/// 画面の試験: 旧・新を選んで「比べる」→ テキスト・表・フォルダのタブが開き、中身が正しく出るか。表は採る行を選んで反映できるか。
/// 写し（PNG）も撮る（説明書・見た目の確かめ用）。
/// </summary>
public class UiTests
{
    private static readonly string Work = Path.Combine(Path.GetTempPath(), $"uvd-ui-{Environment.ProcessId}");
    private static string Shots
    {
        get
        {
            string d = Environment.GetEnvironmentVariable("UVD_SHOTS") is { Length: > 0 } s ? s : Path.Combine(Path.GetTempPath(), "uvd-shots");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    private static string Write(string name, string text, Encoding? enc = null)
    {
        Directory.CreateDirectory(Work);
        string p = Path.Combine(Work, name);
        File.WriteAllText(p, text, enc ?? new UTF8Encoding(false));
        return p;
    }

    private static async Task WaitUntil(Func<bool> cond, string what, int ms = 20000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond())
        {
            Dispatcher.UIThread.RunJobs();
            if (sw.ElapsedMilliseconds > ms) throw new Xunit.Sdk.XunitException("待っても " + what);
            await Task.Delay(20);
        }
        Dispatcher.UIThread.RunJobs();
    }

    internal static void Shot(Window w, string name)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var size = new PixelSize((int)w.Bounds.Width, (int)w.Bounds.Height);
        using var bmp = new RenderTargetBitmap(size, new Vector(96, 96));
        bmp.Render(w);
        bmp.Save(Path.Combine(Shots, name + ".png"));
    }

    private static MainWindow Open()
    {
        var w = new MainWindow(Array.Empty<string>()) { Width = 1200, Height = 700 };
        w.Show();
        return w;
    }

    private static T Current<T>(MainWindow w) where T : Control => (T)((TabItem)w._tabs.SelectedItem!).Content!;

    [AvaloniaFact]
    public async Task テキストを比べて左右に並べる()
    {
        var oldLines = Enumerable.Range(1, 2000).Select(i => $"{i:0000} 杭 P{i} 確認").ToList();
        var newLines = oldLines.ToList();
        newLines[9] = "0010 杭 P10 移設";      // 1 か所の変更
        newLines.RemoveAt(500);                 // 削除
        newLines.Insert(1500, "追加の行");      // 追加
        string o = Write("old.txt", string.Join("\n", oldLines) + "\n");
        string n = Write("new.txt", string.Join("\n", newLines) + "\n", Encoding.GetEncoding("utf-8"));
        var w = Open();
        w._old.Text = o;
        w._new.Text = n;
        await w.CompareAsync(o, n);
        Assert.Single(w._tabs.Items);
        var v = Current<TextDiffView>(w);
        await WaitUntil(() => v._changeRows.Count > 0, "変更の位置が出ない");
        Assert.Equal(3, v._changeRows.Count);
        Assert.Contains("変更 3 か所", v._summary.Text);
        // 「変更の所だけ」: 同じ行は前後 3 行だけ残して畳む
        int folded = v._list.ItemCount;
        Assert.True(folded < 40, $"畳んだ行数 {folded}");
        v.Jump(+1);
        var row = (TextRow)v._list.SelectedItem!;
        Assert.Equal(RowKind.Changed, row.Kind);
        Assert.Equal("0010 杭 P10 確認", row.OldText);
        Assert.Equal("0010 杭 P10 移設", row.NewText);
        Shot(w, "text");
        v._foldBox.IsChecked = false;
        await WaitUntil(() => v._list.ItemCount > 1000, "畳みを外しても全部の行が出ない");
        Assert.Equal(2001, v._list.ItemCount);   // 旧 2000 行 + 追加 1 行（削除行と追加行は別の行）
        w.Close();
    }

    [AvaloniaFact]
    public async Task 表を照合して採った変更だけ反映する()
    {
        string o = Write("old.csv", "点名,X,備考\nP1,1.0,a\nP2,2.0,b\nP3,3.0,c\n");
        string n = Write("new.csv", "点名,X,備考\nP1,1.5,a\nP3,3.0,c2\nP4,4.0,d\n");
        var w = Open();
        w._key.Text = "点名";
        await w.CompareAsync(o, n);
        var v = Current<TableDiffView>(w);
        await WaitUntil(() => v._visible.Length == 4, "差分の行が出ない");   // 変更 2（P1 の X・P3 の備考）・削除 1（P2）・追加 1（P4）
        v._kind.SelectedIndex = 3;   // 変更
        await WaitUntil(() => v._visible.Length == 2, "区分で絞れない");
        v._kind.SelectedIndex = 0;   // すべて
        await WaitUntil(() => v._visible.Length == 4, "絞りを戻せない");
        Shot(w, "table");
        // P3 の備考の変更だけ外して反映する
        for (int i = 0; i < v._taken.Length; i++)
            if (((DiffRow)v._list.Items[i]!).Cells is var c && c[1] == "P3" && c[0] == "変更") v._taken[i] = false;
        string outPath = Path.Combine(Work, "applied.csv");
        var r = await v.ApplyToAsync(outPath);
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("点名,X,備考\nP1,1.5,a\nP3,3.0,c\nP4,4.0,d\n", File.ReadAllText(outPath));
        w.Close();
    }

    [AvaloniaFact]
    public async Task フォルダを比べて違うファイルを開く()
    {
        string a = Path.Combine(Work, "dirA"), b = Path.Combine(Work, "dirB");
        Directory.CreateDirectory(Path.Combine(a, "sub"));
        Directory.CreateDirectory(Path.Combine(b, "sub"));
        File.WriteAllText(Path.Combine(a, "same.txt"), "x\n");
        File.WriteAllText(Path.Combine(b, "same.txt"), "x\n");
        File.WriteAllText(Path.Combine(a, "sub", "diff.txt"), "1\n2\n3\n");
        File.WriteAllText(Path.Combine(b, "sub", "diff.txt"), "1\n2x\n3\n");
        File.WriteAllText(Path.Combine(a, "old_only.txt"), "o");
        File.WriteAllText(Path.Combine(b, "new_only.txt"), "n");
        var w = Open();
        await w.CompareAsync(a, b);
        var v = Current<FolderDiffView>(w);
        await WaitUntil(() => v._list.ItemCount == 3, "違いの一覧が出ない");   // 既定は「違いだけ」
        Assert.Contains("旧だけ 1・新だけ 1・違う 1・同じ 1", v._summary.Text);
        Shot(w, "folder");
        await w.CompareAsync(Path.Combine(a, "sub", "diff.txt"), Path.Combine(b, "sub", "diff.txt"));
        Assert.Equal(2, w._tabs.Items.Count);
        var t = Current<TextDiffView>(w);
        await WaitUntil(() => t._changeRows.Count == 1, "1 組の差分が開かない");
        w.Close();
    }

    /// <summary>巨大なファイル（UVD_UI_BIG_OLD・UVD_UI_BIG_NEW があるときだけ）: 比べて画面に出し、最初と最後の変更へ飛べるまでの時間。</summary>
    [AvaloniaFact]
    public async Task 巨大なテキストを開く()
    {
        if (Environment.GetEnvironmentVariable("UVD_UI_BIG_OLD") is not { Length: > 0 } o || Environment.GetEnvironmentVariable("UVD_UI_BIG_NEW") is not { Length: > 0 } n) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var w = Open();
        await w.CompareAsync(o, n);
        long compared = sw.ElapsedMilliseconds;
        var v = Current<TextDiffView>(w);
        await WaitUntil(() => v._changeRows.Count > 0, "変更の位置が出ない", 600_000);
        long shown = sw.ElapsedMilliseconds;
        v.Jump(-1);   // 最後の変更へ
        var last = (TextRow)v._list.SelectedItem!;
        long jumped = sw.ElapsedMilliseconds;
        File.AppendAllText(Path.Combine(Shots, "big.txt"), $"{DateTime.Now:s} 比べる {compared} ms・画面に出る {shown} ms・最後の変更へ {jumped - shown} ms・変更 {v._changeRows.Count:N0} か所・最後 旧 {last.OldNo} 行目\n");
        Shot(w, "text_big");
        Assert.NotNull(last.OldNo ?? last.NewNo);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 日本語と英語を切り替える()
    {
        Loc.Set(true);
        string o = Write("lang_old.txt", "a\nb\nc\n"), n = Write("lang_new.txt", "a\nB\nc\n");
        var w = Open();
        await w.CompareAsync(o, n);
        var v = Current<TextDiffView>(w);
        await WaitUntil(() => v._changeRows.Count == 1, "差分が出ない");
        Assert.Equal("比べる", w._compare.Content);
        Assert.Contains("変更 1 か所", v._summary.Text);
        Assert.Equal("旧と新を比べて、結果を新しいタブで開く", ToolTip.GetTip(w._compare));
        Shot(w, "lang_ja");
        Loc.Set(false);
        await WaitUntil(() => (string?)w._compare.Content == "Compare", "英語にならない");
        Assert.Contains("1 change(s)", v._summary.Text);
        Assert.Equal("Compare old and new and open the result in a new tab", ToolTip.GetTip(w._compare));
        // メニュー: ファイル・ヘルプ（mac 以外は設定・終了・について も入る）
        var menu = NativeMenu.GetMenu(w)!;
        Assert.Equal(new[] { "File", "Help" }, menu.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToArray());
        var file = menu.Items.OfType<NativeMenuItem>().First().Menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToList();
        Assert.Contains("Command Line…", file);
        var help = menu.Items.OfType<NativeMenuItem>().Last().Menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToList();
        Assert.Contains("Command Line Setup…", help);
        Assert.Contains("GitHub: UwDiff", help);
        Shot(w, "lang_en");
        Loc.Set(true);
        await WaitUntil(() => (string?)w._compare.Content == "比べる", "日本語に戻らない");
        Assert.Equal("ファイル", NativeMenu.GetMenu(w)!.Items.OfType<NativeMenuItem>().First().Header);
        w.Close();
    }

    [AvaloniaFact]
    public void アプリのメニューとについてと言語の切り替え()
    {
        Loc.Set(true);
        // アプリのメニュー（mac の UwDiff メニュー）は起動のときに入っている
        var app = NativeMenu.GetMenu(Avalonia.Application.Current!)!;
        Assert.Equal(new[] { "UwDiff について", "設定…" }, app.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Select(i => i.Header).ToArray());
        var w = Open();
        // 上の欄の切り替え
        Assert.Equal(0, w._lang.SelectedIndex);
        w._lang.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.False(Loc.Ja);
        Assert.Equal(new[] { "About UwDiff", "Settings…" }, app.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Select(i => i.Header).ToArray());
        Assert.Equal("Compare", w._compare.Content);
        // について: 版とビルド番号
        var about = AboutWindow.Make();
        about.Show();
        var texts = about.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains($"Version {Uvd.Cli.Program.Version}  (build {Uvd.Cli.Program.BuildNumber})", texts);
        Assert.Contains("What's new (releases)", texts);
        Shot(about, "about_en");
        about.Close();
        w._lang.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.True(Loc.Ja);
        Assert.Equal("UwDiff について", app.Items.OfType<NativeMenuItem>().First().Header);
        w.Close();
    }

    [AvaloniaFact]
    public async Task コマンドラインで比べて結果を開く()
    {
        Loc.Set(true);
        string o = Write("cmd old.csv", "点名,X\nP1,1\nP2,2\n"), n = Write("cmd_new.csv", "点名,X\nP1,1.5\nP3,3\n");
        var w = Open();
        var c = new CommandLineWindow(w, "");
        c._args.Text = $"uvd \"{o}\" {n} -key 点名";
        await c.RunAsync();
        Assert.Single(w._tabs.Items);
        var v = Current<TableDiffView>(w);
        await WaitUntil(() => v._visible.Length == 3, "差分の行が出ない");   // 変更 P1 X・削除 P2・追加 P3
        // apply は結果のタブを開かず、出力だけ
        c._args.Text = "--version";
        await c.RunAsync();
        Assert.Single(w._tabs.Items);
        w.Close();
    }

    [Fact]
    public void 引数を空白で分ける()
    {
        Assert.Equal(new[] { "a b.csv", "c", "-key", "点名,種別" }, CommandLineWindow.Split("\"a b.csv\" c -key 点名,種別"));
        Assert.Equal(new[] { "x", "", "y" }, CommandLineWindow.Split("x '' y"));
        Assert.Equal(new[] { "say \"hi\"" }, CommandLineWindow.Split("\"say \\\"hi\\\"\""));
    }

    [AvaloniaFact]
    public async Task タブを切り替えても表が読める()
    {
        Loc.Set(true);
        string o = Write("tab_old.csv", "点名,X\n" + string.Concat(Enumerable.Range(1, 300).Select(i => $"P{i},{i}\n")));
        string n = Write("tab_new.csv", "点名,X\n" + string.Concat(Enumerable.Range(1, 300).Select(i => $"P{i},{(i % 3 == 0 ? i + 1 : i)}\n")));
        var w = Open();
        w._key.Text = "点名";
        await w.CompareAsync(o, n);
        var t = Current<TableDiffView>(w);
        await WaitUntil(() => t._visible.Length == 100, "差分の行が出ない");
        w._key.Text = "";
        await w.CompareAsync(o, n);        // 2 つめのタブ（テキスト）を開く → 1 つめは画面から外れる
        Assert.Equal(2, w._tabs.Items.Count);
        w._tabs.SelectedIndex = 0;         // 戻る
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("変更", t.Row(99)[0]);  // 読み口が閉じられていない
        w.Close();
    }

    [AvaloniaFact]
    public async Task 区切りと引用符の指定で比べた表を画面から反映する()
    {
        Loc.Set(true);
        // セミコロン区切り・引用符を使わない（値の中の " は普通の文字）
        string o = Write("semi_old.txt", "id;x;memo\n1;a;5\" pipe\n2;b;x\n");
        string n = Write("semi_new.txt", "id;x;memo\n1;A;5\" pipe\n3;c;y\n");
        var w = Open();
        await w.CompareAsync(o, n, new[] { "-key", "id", "-d", ";", "-quote", "off" });
        var v = Current<TableDiffView>(w);
        await WaitUntil(() => v._visible.Length == 3, "差分の行が出ない");   // 変更 1 の x・削除 2・追加 3
        string outPath = Path.Combine(Work, "semi_applied.txt");
        var r = await v.ApplyToAsync(outPath);
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("id;x;memo\n1;A;5\" pipe\n3;c;y\n", File.ReadAllText(outPath));
        w.Close();
    }

    [AvaloniaFact]
    public async Task フォルダのときだけ含める除くを使える()
    {
        Loc.Set(true);
        string a = Path.Combine(Work, "incA"), b = Path.Combine(Work, "incB");
        foreach (string d in new[] { a, b }) { Directory.CreateDirectory(Path.Combine(d, ".git")); Directory.CreateDirectory(Path.Combine(d, "sub")); }
        File.WriteAllText(Path.Combine(a, "x.csv"), "1\n"); File.WriteAllText(Path.Combine(b, "x.csv"), "2\n");
        File.WriteAllText(Path.Combine(a, "y.log"), "1\n"); File.WriteAllText(Path.Combine(b, "y.log"), "2\n");
        File.WriteAllText(Path.Combine(a, "z.txt"), "1\n"); File.WriteAllText(Path.Combine(b, "z.txt"), "2\n");
        File.WriteAllText(Path.Combine(a, ".git", "HEAD"), "1"); File.WriteAllText(Path.Combine(b, ".git", "HEAD"), "2");
        File.WriteAllText(Path.Combine(a, "sub", "w.tmp"), "1"); File.WriteAllText(Path.Combine(b, "sub", "w.tmp"), "2");
        var w = Open();
        // ファイルのときは入力できない
        w._old.Text = Path.Combine(a, "x.csv");
        w._new.Text = Path.Combine(b, "x.csv");
        Dispatcher.UIThread.RunJobs();
        Assert.False(w._include.IsEnabled);
        Assert.False(w._exclude.IsEnabled);
        Assert.Equal("旧と新にフォルダを指定したときだけ使える", ToolTip.GetTip(w._include));
        // 片方だけフォルダでも入力できない
        w._old.Text = a;
        Dispatcher.UIThread.RunJobs();
        Assert.False(w._include.IsEnabled);
        // 両方フォルダなら入力でき、比べるときに渡す
        w._new.Text = b;
        Dispatcher.UIThread.RunJobs();
        Assert.True(w._include.IsEnabled);
        Assert.True(w._exclude.IsEnabled);
        w._include.Text = "*.csv;*.log;*.tmp";
        w._exclude.Text = ".git;sub";
        await w.CompareAsync(w._old.Text, w._new.Text);
        var v = Current<FolderDiffView>(w);
        await WaitUntil(() => v._list.ItemCount == 2, "含める・除くが効かない");   // x.csv・y.log だけ（z.txt は含めず、.git・sub は除く）
        Assert.Contains("違う 2", v._summary.Text);
        Shot(w, "folder_include");
        w.Close();
    }

    [AvaloniaFact]
    public async Task 結果から開くと比べ方も上の欄に入る()
    {
        // uvd -open と同じ: 書き出した結果の置き場所を、別の窓で開く
        string o = Write("open_old.csv", "id,x\n1,a\n2,b\n");
        string n = Write("open_new.csv", "id,x\n1,A\n3,c\n");
        var w = Open();
        await w.CompareAsync(o, n, new[] { "-key", "id", "-b", "-i" });
        var v = Current<TableDiffView>(w);
        await WaitUntil(() => v._visible.Length == 3, "差分の行が出ない");
        string dir = Path.GetDirectoryName(v._pkg.ResultFile!)!;
        var w2 = Open();
        w2.OpenResult(dir);
        Assert.Equal(o, w2._old.Text);
        Assert.Equal("id", w2._key.Text);
        Assert.True(w2._b.IsChecked);
        Assert.True(w2._i.IsChecked);
        Assert.False(w2._w.IsChecked);
        w2.Close();
        w.Close();
    }

    [AvaloniaFact]
    public void 等幅フォントはこの機械にあるものを選ぶ()
    {
        // 並べた名前をそのまま渡さない（Linux で 1 つも無いと落ちた）。ある名前か、既定のフォント
        var mono = Palette.Mono;
        var have = Avalonia.Media.FontManager.Current.SystemFonts.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(",", mono.Name);
        Assert.True(have.Contains(mono.Name) || mono == Avalonia.Media.FontFamily.Default, mono.Name);
        if (OperatingSystem.IsMacOS()) Assert.Equal("Menlo", mono.Name);
    }

    [AvaloniaFact]
    public async Task 比べられないときは理由を出す()
    {
        var w = Open();
        await w.CompareAsync(Path.Combine(Work, "無い.txt"), Path.Combine(Work, "無い2.txt"));
        Assert.Empty(w._tabs.Items);
        Assert.Contains("見つかりません", w._status.Text);
        w.Close();
    }
}
