using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UwDiff.App;
using UwDiff.App.Views;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AvaloniaTestApplication(typeof(UwDiff.UiTests.TestAppBuilder))]

namespace UwDiff.UiTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App.App>()
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

    private static void Shot(Window w, string name)
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
        v._kind.SelectedItem = "変更";
        await WaitUntil(() => v._visible.Length == 2, "区分で絞れない");
        v._kind.SelectedItem = "すべて";
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
    public async Task 比べられないときは理由を出す()
    {
        var w = Open();
        await w.CompareAsync(Path.Combine(Work, "無い.txt"), Path.Combine(Work, "無い2.txt"));
        Assert.Empty(w._tabs.Items);
        Assert.Contains("見つかりません", w._status.Text);
        w.Close();
    }
}
