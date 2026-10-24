using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using UwDiff.Core;
using UwDiff.Core.Text;

namespace UwDiff.App.Views;

/// <summary>画面の 1 行（左右に旧・新）。</summary>
public sealed class TextRow
{
    public long? OldNo, NewNo;
    public string? OldText, NewText;
    public RowKind Kind;
    public long FoldCount;
}

public enum RowKind { Same, Changed, Deleted, Inserted, Fold }

/// <summary>
/// テキストの差分を左右に並べて見せる。変更の位置（Hunk）と、旧・新の行を好きな所から読む索引から、画面に出る行だけを作る。
/// 変更の所は、旧の行と新の行を上から順に組にして並べ、組になった行は文字単位の違いを濃く塗る。
/// 「変更の所だけ」にすると、同じ行が続く所を前後 3 行だけ残して畳む。
/// </summary>
public sealed class TextDiffView : DockPanel, IResultView
{
    private const int Context = 3;

    private readonly ResultPackage _pkg;
    private readonly string _tempDir;
    internal readonly ListBox _list = new();
    internal readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
    internal readonly CheckBox _foldBox = new() { IsChecked = true, Margin = new Thickness(12, 0, 0, 0) };
    private readonly Button _prev = new(), _next = new() { Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBlock _oldHead = new() { TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _newHead = new() { TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeight.SemiBold };
    private string? _error;
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 4, IsVisible = false };
    private LineCache? _old, _new;
    private DisplayFile? _oldFile, _newFile;
    private List<Hunk> _hunks = new();
    private Seg[] _segs = Array.Empty<Seg>();
    private long[] _rowStart = Array.Empty<long>();   // 区間ごとの、最初の行の画面での位置
    internal List<int> _changeRows = new();            // 変更の区間の、最初の行の画面での位置

    private readonly record struct Seg(RowKind Kind, long OldStart, long NewStart, long OldCount, long NewCount)
    {
        public long Rows => Kind switch { RowKind.Fold => 1, RowKind.Same => OldCount, _ => Math.Max(OldCount, NewCount) };
    }

    public TextDiffView(ResultPackage pkg, string tempDir)
    {
        _pkg = pkg;
        _tempDir = tempDir;
        _prev.Click += (_, _) => Jump(-1);
        _next.Click += (_, _) => Jump(+1);
        _foldBox.IsCheckedChanged += (_, _) => Rebuild();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6), Children = { _prev, _next, _foldBox, _summary } };
        SetDock(bar, Dock.Top);
        Children.Add(bar);

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("92,*,92,*"), Margin = new Thickness(8, 0, 8, 4) };
        head.Children.Add(Cell(_oldHead, 0, 2));
        head.Children.Add(Cell(_newHead, 2, 2));
        ToolTip.SetTip(_oldHead, pkg.Old);
        ToolTip.SetTip(_newHead, pkg.New);
        SetDock(head, Dock.Top);
        Children.Add(head);
        SetDock(_progress, Dock.Top);
        Children.Add(_progress);

        _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<TextRow>((_, _) => new TextRowView(), supportsRecycling: true);
        _list.FontFamily = Palette.Mono;
        _list.FontSize = 12;
        Children.Add(_list);
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.F7) Jump((e.KeyModifiers & Avalonia.Input.KeyModifiers.Shift) != 0 ? -1 : +1);
        };
        Texts();
        Loc.Changed += Texts;
        _ = LoadAsync();
    }

    /// <summary>文言を、いまの言語で入れ直す（行の見た目は作り直す）。</summary>
    private void Texts()
    {
        _prev.Content = Loc.T("PrevChange");
        _next.Content = Loc.T("NextChange");
        _foldBox.Content = Loc.T("ChangesOnly");
        ToolTip.SetTip(_prev, Loc.T("TipPrevChange"));
        ToolTip.SetTip(_next, Loc.T("TipNextChange"));
        ToolTip.SetTip(_foldBox, Loc.T("TipChangesOnly"));
        _oldHead.Text = Loc.T("Old") + ": " + _pkg.Old;
        _newHead.Text = Loc.T("New") + ": " + _pkg.New;
        if (_error != null) _summary.Text = Loc.F("CannotRead", _error);
        else if (_old != null) Rebuild();
        else _summary.Text = Loc.T("Indexing");
    }

    private static Control Cell(Control c, int col, int span = 1)
    {
        Grid.SetColumn(c, col);
        Grid.SetColumnSpan(c, span);
        return c;
    }

    private async Task LoadAsync()
    {
        _progress.IsVisible = true;
        _summary.Text = Loc.T("Indexing");
        try
        {
            FileEncoding? eo = _pkg.OldEncoding is { } a ? TextEncodings.Parse(a) : null;
            FileEncoding? en = _pkg.NewEncoding is { } b ? TextEncodings.Parse(b) : null;
            double po = 0, pn = 0;
            void Show() => Dispatcher.UIThread.Post(() => _progress.Value = (po + pn) / 2);
            var to = Task.Run(() => DisplayFile.Prepare(_pkg.Old, eo, false, _tempDir, (d, t) => { po = (double)d / Math.Max(1, t); Show(); }, default, _pkg.OldIndex));
            var tn = Task.Run(() => DisplayFile.Prepare(_pkg.New, en, false, _tempDir, (d, t) => { pn = (double)d / Math.Max(1, t); Show(); }, default, _pkg.NewIndex));
            _oldFile = await to;
            _newFile = await tn;
            _hunks = _pkg.ResultFile != null ? await Task.Run(() => HunkRecorder.Load(_pkg.ResultFile)) : new List<Hunk>();
            _old = new LineCache(_oldFile);
            _new = new LineCache(_newFile);
            Rebuild();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            _summary.Text = Loc.F("CannotRead", ex.Message);
        }
        finally
        {
            _progress.IsVisible = false;
        }
    }

    /// <summary>区間（同じ・変更・畳み）の並びを作り直す。</summary>
    private void Rebuild()
    {
        if (_old == null || _new == null) return;
        bool fold = _foldBox.IsChecked == true;
        var segs = new List<Seg>();
        long o = 1, n = 1;
        void AddSame(long os, long ns, long count, bool atStart, bool atEnd)
        {
            if (count <= 0) return;
            long keepHead = atStart ? 0 : Context, keepTail = atEnd ? 0 : Context;
            if (!fold || count <= keepHead + keepTail + 1)
            {
                segs.Add(new Seg(RowKind.Same, os, ns, count, count));
                return;
            }
            if (keepHead > 0) segs.Add(new Seg(RowKind.Same, os, ns, keepHead, keepHead));
            segs.Add(new Seg(RowKind.Fold, os + keepHead, ns + keepHead, count - keepHead - keepTail, count - keepHead - keepTail));
            if (keepTail > 0) segs.Add(new Seg(RowKind.Same, os + count - keepTail, ns + count - keepTail, keepTail, keepTail));
        }
        foreach (Hunk h in _hunks)
        {
            AddSame(o, n, h.OldLine - o, segs.Count == 0, false);
            RowKind k = h.OldCount == 0 ? RowKind.Inserted : h.NewCount == 0 ? RowKind.Deleted : RowKind.Changed;
            segs.Add(new Seg(k, h.OldLine, h.NewLine, h.OldCount, h.NewCount));
            o = h.OldLine + h.OldCount;
            n = h.NewLine + h.NewCount;
        }
        AddSame(o, n, _old.Lines - o + 1, segs.Count == 0, true);

        _segs = segs.ToArray();
        _rowStart = new long[_segs.Length + 1];
        _changeRows = new List<int>();
        for (int i = 0; i < _segs.Length; i++)
        {
            if (_segs[i].Kind is RowKind.Changed or RowKind.Inserted or RowKind.Deleted) _changeRows.Add((int)Math.Min(int.MaxValue, _rowStart[i]));
            _rowStart[i + 1] = _rowStart[i] + _segs[i].Rows;
        }
        long total = _rowStart[^1];
        if (total > int.MaxValue) total = int.MaxValue;
        _list.ItemsSource = new VirtualList<TextRow>((int)total, MakeRow);
        long del = _hunks.Sum(h => (long)h.OldCount), ins = _hunks.Sum(h => (long)h.NewCount);
        _summary.Text = _hunks.Count == 0 ? Loc.F("TextSame", _old.Lines)
            : Loc.F("TextSummary", _hunks.Count, del, ins, _old.Lines, _new.Lines);
    }

    private TextRow MakeRow(int row)
    {
        int lo = 0, hi = _segs.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_rowStart[mid] <= row) lo = mid; else hi = mid - 1;
        }
        Seg s = _segs[lo];
        long k = row - _rowStart[lo];
        var r = new TextRow { Kind = s.Kind };
        switch (s.Kind)
        {
            case RowKind.Fold:
                r.FoldCount = s.OldCount;
                break;
            case RowKind.Same:
                r.OldNo = s.OldStart + k;
                r.NewNo = s.NewStart + k;
                r.OldText = _old!.Get(r.OldNo.Value);
                r.NewText = r.OldText == _new!.Get(r.NewNo.Value) ? r.OldText : _new.Get(r.NewNo.Value);
                break;
            default:
                if (k < s.OldCount) { r.OldNo = s.OldStart + k; r.OldText = _old!.Get(r.OldNo.Value); }
                if (k < s.NewCount) { r.NewNo = s.NewStart + k; r.NewText = _new!.Get(r.NewNo.Value); }
                r.Kind = r.OldNo != null && r.NewNo != null ? RowKind.Changed : r.OldNo != null ? RowKind.Deleted : RowKind.Inserted;
                break;
        }
        return r;
    }

    internal void Jump(int dir)
    {
        if (_changeRows.Count == 0) return;
        int cur = _list.SelectedIndex;
        int target = -1;
        if (dir > 0) target = _changeRows.FirstOrDefault(x => x > cur, -1);
        else for (int i = _changeRows.Count - 1; i >= 0; i--) if (_changeRows[i] < cur) { target = _changeRows[i]; break; }
        if (target < 0) target = dir > 0 ? _changeRows[0] : _changeRows[^1];
        _list.SelectedIndex = target;
        _list.ScrollIntoView(target);
    }

    /// <summary>タブを閉じたとき: 表示用に書き出した一時ファイルを消す。</summary>
    public void Release()
    {
        Loc.Changed -= Texts;
        foreach (DisplayFile? f in new[] { _oldFile, _newFile })
            if (f is { Temporary: true }) try { File.Delete(f.Path); } catch (IOException) { }
    }
}

/// <summary>1 行の見た目（行番号・旧・行番号・新）。DataContext が替わるたびに中身を入れ替える（再利用される）。</summary>
public sealed class TextRowView : Grid
{
    private readonly TextBlock _oldNo = Num(), _newNo = Num();
    private readonly TextBlock _old = Text(), _new = Text();
    private readonly Border _oldBg = new(), _newBg = new();
    private readonly TextBlock _fold = new() { HorizontalAlignment = HorizontalAlignment.Center, Foreground = Palette.Muted, FontStyle = FontStyle.Italic };

    private static TextBlock Num() => new() { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0), Foreground = Palette.Muted };
    private static TextBlock Text() => new() { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };

    public TextRowView()
    {
        ColumnDefinitions = new ColumnDefinitions("92,*,92,*");
        Height = 18;
        Add(_oldBg, 1); Add(_newBg, 3);
        Add(_oldNo, 0); Add(_old, 1); Add(_newNo, 2); Add(_new, 3);
        Add(_fold, 0, 4);
    }

    private void Add(Control c, int col, int span = 1)
    {
        SetColumn(c, col);
        SetColumnSpan(c, span);
        Children.Add(c);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not TextRow r) return;
        bool fold = r.Kind == RowKind.Fold;
        _fold.IsVisible = fold;
        _fold.Text = fold ? Loc.F("FoldRow", r.FoldCount) : "";
        _oldNo.Text = r.OldNo?.ToString("N0") ?? "";
        _newNo.Text = r.NewNo?.ToString("N0") ?? "";
        _oldBg.Background = r.Kind is RowKind.Deleted or RowKind.Changed ? Palette.Deleted : null;
        _newBg.Background = r.Kind is RowKind.Inserted or RowKind.Changed ? Palette.Inserted : null;
        if (r.Kind == RowKind.Changed && r.OldText != null && r.NewText != null && r.OldText.Length + r.NewText.Length < 4000)
        {
            // 組になった行: 文字単位の違いを濃く塗る
            List<Core.Text.EditRun> runs = Myers.Chars(r.OldText, r.NewText);
            Fill(_old, r.OldText, runs, EditKind.Delete, Palette.DeletedStrong);
            Fill(_new, r.NewText, runs, EditKind.Insert, Palette.InsertedStrong);
        }
        else
        {
            Plain(_old, r.OldText);
            Plain(_new, r.NewText);
        }
    }

    private static void Plain(TextBlock t, string? s)
    {
        t.Inlines?.Clear();
        t.Text = s ?? "";
    }

    /// <summary>runs のうち、その側にある部分（同じ＋自分の側の違い）を順に並べ、違いだけ濃く塗る。</summary>
    private static void Fill(TextBlock t, string s, List<Core.Text.EditRun> runs, EditKind mine, IBrush strong)
    {
        t.Text = null;
        var inlines = new InlineCollection();
        int pos = 0;
        foreach (var run in runs)
        {
            if (run.Kind != EditKind.Equal && run.Kind != mine) continue;
            int len = Math.Min(run.Count, s.Length - pos);
            if (len <= 0) break;
            var piece = new Run(s.Substring(pos, len));
            if (run.Kind == mine) piece.Background = strong;
            inlines.Add(piece);
            pos += len;
        }
        if (pos < s.Length) inlines.Add(new Run(s[pos..]));
        t.Inlines = inlines;
    }
}
