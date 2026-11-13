using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using UwDiff.App;

namespace UwDiff.UiTests;

/// <summary>
/// 支援の案内（帯）の試験（指示-20261113-06）: 出す条件（30 日・10 日）・［後で］の 90 日・年 1 回・年末の期間（12/15〜1/10）・
/// 設定で止める・［支援する］［もう表示しない］の後は出さない。日付は差し替えて試す（端末の日付に頼らない）。
/// CLI は --version・--help の最後にだけ 1 行出し、差分の出力と終了コードには出さない。
/// </summary>
public class SupportPromptTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    /// <summary>first の日から始めて、used 日だけ使った記録（最後に使った日は last）。</summary>
    private static SupportPromptState Used(DateOnly first, int used, DateOnly last) => new()
    {
        FirstUseDay = first.DayNumber,
        LastUseDay = last.DayNumber,
        UsedDays = used,
    };

    // ---- 決まり（画面なし） ----

    [Fact]
    public void 使った日を数える_同じ日は1日()
    {
        var s = new SupportPromptState();
        Assert.True(SupportPrompt.RecordUse(s, D(2027, 3, 1)));
        Assert.False(SupportPrompt.RecordUse(s, D(2027, 3, 1)));   // 同じ日の 2 回目は数えない
        Assert.True(SupportPrompt.RecordUse(s, D(2027, 3, 5)));
        Assert.Equal(D(2027, 3, 1).DayNumber, s.FirstUseDay);
        Assert.Equal(D(2027, 3, 5).DayNumber, s.LastUseDay);
        Assert.Equal(2, s.UsedDays);
    }

    [Fact]
    public void 条件_初回から30日以上かつ使った日10日以上()
    {
        var first = D(2027, 3, 1);
        // 29 日目は出さない・30 日目から出す
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(Used(first, 20, first.AddDays(29)), first.AddDays(29)));
        Assert.Equal(SupportPromptKind.First, SupportPrompt.Decide(Used(first, 20, first.AddDays(30)), first.AddDays(30)));
        // 使った日が 9 日なら、何日たっても出さない
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(Used(first, 9, first.AddDays(200)), first.AddDays(200)));
        Assert.Equal(SupportPromptKind.First, SupportPrompt.Decide(Used(first, 10, first.AddDays(200)), first.AddDays(200)));
        // 記録が無い（初回の日が 0）なら出さない
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(new SupportPromptState { UsedDays = 50 }, first));
    }

    [Fact]
    public void 後でを押すと90日後に1回だけ再表示()
    {
        var first = D(2027, 2, 1);
        var day1 = D(2027, 3, 10);
        var s = Used(first, 15, day1);
        Assert.Equal(SupportPromptKind.First, SupportPrompt.Decide(s, day1));
        SupportPrompt.MarkShown(s, SupportPromptKind.First, day1);   // ［後で］は出した記録だけ
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, day1));
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, day1.AddDays(89)));
        var day2 = day1.AddDays(90);
        Assert.Equal(SupportPromptKind.Reminder, SupportPrompt.Decide(s, day2));
        SupportPrompt.MarkShown(s, SupportPromptKind.Reminder, day2);
        // 2 回出したら、期間の外ではもう出さない
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, day2.AddDays(90)));
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, D(2028, 11, 30)));
    }

    [Fact]
    public void 年末の期間は12月15日から1月10日()
    {
        Assert.Equal(0, SupportPrompt.SeasonYear(D(2027, 12, 14)));
        Assert.Equal(2027, SupportPrompt.SeasonYear(D(2027, 12, 15)));
        Assert.Equal(2027, SupportPrompt.SeasonYear(D(2027, 12, 31)));
        Assert.Equal(2027, SupportPrompt.SeasonYear(D(2028, 1, 1)));
        Assert.Equal(2027, SupportPrompt.SeasonYear(D(2028, 1, 10)));
        Assert.Equal(0, SupportPrompt.SeasonYear(D(2028, 1, 11)));
    }

    [Fact]
    public void 年末の呼びかけは期間中に1回_条件は外さない()
    {
        // 条件を満たしていれば期間中の最初の起動で年末の帯（最初の帯を兼ねる）
        var first = D(2027, 9, 1);
        var s = Used(first, 40, D(2027, 12, 20));
        Assert.Equal(SupportPromptKind.Season, SupportPrompt.Decide(s, D(2027, 12, 20)));
        SupportPrompt.MarkShown(s, SupportPromptKind.Season, D(2027, 12, 20));
        Assert.Equal(1, s.ShownCount);
        Assert.Equal(2027, s.LastSeasonYear);
        // 同じ期間の中ではもう出さない（年が替わっても 1/10 まで同じ期間）
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, D(2027, 12, 31)));
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, D(2028, 1, 5)));
        // 次は 90 日後の［後で］の 1 回
        Assert.Equal(SupportPromptKind.Reminder, SupportPrompt.Decide(s, D(2027, 12, 20).AddDays(90)));

        // オーナー決定 2026-11-13（案 a）: 期間中でも 30 日・10 日の条件は外さない
        var newcomer = Used(D(2027, 12, 1), 12, D(2027, 12, 20));   // 19 日目
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(newcomer, D(2027, 12, 20)));
        var rare = Used(D(2027, 6, 1), 9, D(2027, 12, 20));           // 使った日 9 日
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(rare, D(2027, 12, 20)));
    }

    [Fact]
    public void 帯は年1回まで_年末の帯の直前90日に出していれば出さない()
    {
        var first = D(2027, 1, 1);
        var s = Used(first, 100, D(2027, 11, 1));
        SupportPrompt.MarkShown(s, SupportPromptKind.First, D(2027, 11, 1));
        // 11/1 に出した人には 12/20 の年末の帯も出さない（90 日あける）
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, D(2027, 12, 20)));
        // 翌年の期間には出る
        Assert.Equal(SupportPromptKind.Season, SupportPrompt.Decide(s, D(2028, 12, 20)));
    }

    [Fact]
    public void 支援した人ともう表示しないにした人には年末も出さない()
    {
        var first = D(2027, 1, 1);
        var a = Used(first, 100, D(2027, 12, 20));
        SupportPrompt.MarkSupported(a);
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(a, D(2027, 12, 20)));
        var b = Used(first, 100, D(2027, 12, 20));
        SupportPrompt.MarkNeverShow(b);
        Assert.True(b.Disabled);
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(b, D(2027, 12, 20)));
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(b, D(2027, 6, 1)));
    }

    // ---- 画面 ----

    /// <summary>帯を出す条件を満たした記録にして窓を開く（日付は差し替え）。終わったら <see cref="Reset"/>。</summary>
    private static MainWindow OpenWith(SupportPromptState s, DateOnly today)
    {
        Copy(s, Loc.Support);
        SupportPrompt.Today = () => today;
        var w = new MainWindow(Array.Empty<string>()) { Width = 1200, Height = 700 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Copy(SupportPromptState from, SupportPromptState to)
    {
        to.Disabled = from.Disabled;
        to.Supported = from.Supported;
        to.FirstUseDay = from.FirstUseDay;
        to.LastUseDay = from.LastUseDay;
        to.UsedDays = from.UsedDays;
        to.LastShownDay = from.LastShownDay;
        to.ShownCount = from.ShownCount;
        to.LastSeasonYear = from.LastSeasonYear;
    }

    private static void Reset()
    {
        Copy(new SupportPromptState(), Loc.Support);
        SupportPrompt.Today = () => DateOnly.FromDateTime(DateTime.Now);
        Links.Override = null;
        Loc.SaveSettings();
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void 初めての起動では帯を出さない()
    {
        try
        {
            var today = D(2027, 3, 1);
            var w = OpenWith(new SupportPromptState(), today);
            Assert.False(w._supportBar.IsVisible);
            Assert.Equal(today.DayNumber, Loc.Support.FirstUseDay);   // 起動で使った日を数える
            Assert.Equal(1, Loc.Support.UsedDays);
            w.Close();
        }
        finally { Reset(); }
    }

    [AvaloniaFact]
    public void 条件を満たすと起動直後に帯を出し_作業はそのままできる()
    {
        try
        {
            Loc.Set(true);
            var today = D(2027, 4, 1);
            var w = OpenWith(Used(D(2027, 2, 1), 20, today.AddDays(-1)), today);
            Assert.True(w._supportBar.IsVisible);
            Assert.Equal(SupportPromptKind.First, w.SupportShown);
            Assert.Equal("UwDiff をお使いいただき、ありがとうございます", w._supportTitle.Text);
            Assert.DoesNotContain("Pro", w._supportBody1.Text + w._supportBody2.Text + w._supportBody3.Text);
            Assert.Equal(today.DayNumber, Loc.Support.LastShownDay);   // 出した時点で記録
            Assert.Equal(1, Loc.Support.ShownCount);
            // モーダルではない: 帯が出ていても上の欄・比べるボタンは使える
            Assert.True(w._compare.IsEnabled);
            Assert.True(w._old.IsEnabled);
            UiTests.Shot(w, "support-banner");
            // 英語に切り替えると帯の文も替わる
            Loc.Set(false);
            Assert.Equal("Thanks for using UwDiff", w._supportTitle.Text);
            Loc.Set(true);
            // ［後で］: 閉じるだけ（出した日は記録済み＝90 日後に 1 回）
            Click(w._supportLater);
            Assert.False(w._supportBar.IsVisible);
            Assert.False(Loc.Support.Disabled);
            Assert.False(Loc.Support.Supported);
            w.Close();
            // 次の日に起動しても出さない
            var w2 = OpenWith(Loc.Support, today.AddDays(1));
            Assert.False(w2._supportBar.IsVisible);
            w2.Close();
        }
        finally { Reset(); }
    }

    [AvaloniaFact]
    public void 支援するはSponsorsを開き以後は出さない()
    {
        try
        {
            string? opened = null;
            Links.Override = u => opened = u;
            var today = D(2027, 4, 1);
            var w = OpenWith(Used(D(2027, 2, 1), 20, today), today);
            Assert.True(w._supportBar.IsVisible);
            Click(w._supportDonate);
            Assert.Equal("https://github.com/sponsors/amru195704", opened);
            Assert.True(Loc.Support.Supported);
            Assert.False(w._supportBar.IsVisible);
            w.Close();
            // 年末の期間でも出さない
            var w2 = OpenWith(Loc.Support, D(2027, 12, 20));
            Assert.False(w2._supportBar.IsVisible);
            w2.Close();
        }
        finally { Reset(); }
    }

    [AvaloniaFact]
    public void もう表示しないは設定の表示しないと同じ()
    {
        try
        {
            var today = D(2027, 4, 1);
            var w = OpenWith(Used(D(2027, 2, 1), 20, today), today);
            Click(w._supportNever);
            Assert.False(w._supportBar.IsVisible);
            Assert.True(Loc.Support.Disabled);
            // 設定の窓のチェックも入っている
            var st = new SettingsWindow();
            Assert.True(st._noSupport.IsChecked);
            // 設定で外すと、また条件で出せるようになる
            st._noSupport.IsChecked = false;
            Assert.False(Loc.Support.Disabled);
            w.Close();
        }
        finally { Reset(); }
    }

    [AvaloniaFact]
    public void 設定で止めると条件を満たしても出さない()
    {
        try
        {
            var st = new SettingsWindow();
            st._noSupport.IsChecked = true;
            Assert.True(Loc.Support.Disabled);
            var s = Used(D(2027, 2, 1), 20, D(2027, 4, 1));
            s.Disabled = true;
            var w = OpenWith(s, D(2027, 4, 1));
            Assert.False(w._supportBar.IsVisible);
            w.Close();
            // 設定の記録は settings.json に残る（言語と同じファイル。外部へは送らない）
            string json = File.ReadAllText(Path.Combine(Loc.SettingsFolder, "settings.json"));
            Assert.Contains("\"SupportBanner\"", json);
            Assert.Contains("\"Disabled\":true", json);
        }
        finally { Reset(); }
    }

    [AvaloniaFact]
    public void 年末の期間は年末の帯を出す()
    {
        try
        {
            Loc.Set(true);
            var today = D(2027, 12, 16);
            var w = OpenWith(Used(D(2027, 6, 1), 30, today), today);
            Assert.True(w._supportBar.IsVisible);
            Assert.Equal(SupportPromptKind.Season, w.SupportShown);
            Assert.Equal("今年もお使いいただき、ありがとうございました", w._supportTitle.Text);
            Assert.Equal(2027, Loc.Support.LastSeasonYear);
            UiTests.Shot(w, "support-banner-season");
            w.Close();
        }
        finally { Reset(); }
    }

    [AvaloniaFact]
    public async Task 比べている途中には出さず_比較が終わってから出す()
    {
        try
        {
            var today = D(2027, 4, 1);
            // 起動の引数で比べる: 起動直後の判定は比較の後になる
            string dir = Path.Combine(Path.GetTempPath(), $"uvd-support-{Environment.ProcessId}");
            Directory.CreateDirectory(dir);
            string o = Path.Combine(dir, "a.txt"), n = Path.Combine(dir, "b.txt");
            File.WriteAllText(o, "1\n2\n3\n");
            File.WriteAllText(n, "1\n2x\n3\n");
            Copy(Used(D(2027, 2, 1), 20, today), Loc.Support);
            SupportPrompt.Today = () => today;
            var w = new MainWindow(new[] { o, n }) { Width = 1200, Height = 700 };
            w.Show();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (w._tabs.ItemCount == 0 && sw.ElapsedMilliseconds < 20000)
            {
                Assert.False(w._supportBar.IsVisible);   // 比べている途中は出さない
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Single(w._tabs.Items);
            Assert.True(w._supportBar.IsVisible);
            // 帯が出ていても、もう一度比べられる（モーダルではない）
            await w.CompareAsync(o, n);
            Assert.Equal(2, w._tabs.ItemCount);
            w.Close();
        }
        finally { Reset(); }
    }

    // ---- CLI ----

    private static (int Code, string Out, string Err) Uvd(params string[] args)
    {
        TextWriter o = Console.Out, e = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        Console.SetOut(so);
        Console.SetError(se);
        try { return (global::Uvd.Cli.Program.Run(args), so.ToString(), se.ToString()); }
        finally { Console.SetOut(o); Console.SetError(e); }
    }

    private const string Sponsors = "https://github.com/sponsors/amru195704";

    [Fact]
    public void CLIはversionとhelpの最後に1行だけ()
    {
        foreach (var a in new[] { "--version", "-version", "-h", "--help" })
        {
            var (code, outText, err) = Uvd(a);
            Assert.Equal(0, code);
            var lines = outText.TrimEnd('\r', '\n').Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            Assert.EndsWith(Sponsors, lines[^1]);
            Assert.Equal(1, lines.Count(l => l.Contains(Sponsors)));   // 1 行だけ
            Assert.Equal("", err);                                    // 標準エラーには出さない
        }
    }

    [Fact]
    public void CLIの差分の出力と終了コードには出さない()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"uvd-support-cli-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        string o = Path.Combine(dir, "a.txt"), n = Path.Combine(dir, "b.txt"), same = Path.Combine(dir, "c.txt");
        File.WriteAllText(o, "1\n2\n3\n");
        File.WriteAllText(n, "1\n2x\n3\n");
        File.WriteAllText(same, "1\n2\n3\n");
        // 違いあり → 1、差分のファイルにも要約にも支援の行は無い
        string diff = Path.Combine(dir, "a-b.diff");
        var (code, outText, err) = Uvd(o, n, "-o", diff, "-q");
        Assert.Equal(1, code);
        string body = File.ReadAllText(diff);
        Assert.Contains("+2x", body);
        Assert.DoesNotContain("sponsors", body);
        Assert.DoesNotContain("sponsors", outText);
        Assert.DoesNotContain("sponsors", err);
        // 同じ → 0
        (code, outText, err) = Uvd(o, same, "-q");
        Assert.Equal(0, code);
        Assert.DoesNotContain("sponsors", outText + err);
        // 使い方の誤り → 2（標準エラーにも出さない）
        (code, outText, err) = Uvd(o);
        Assert.Equal(2, code);
        Assert.DoesNotContain("sponsors", outText + err);
    }
}
