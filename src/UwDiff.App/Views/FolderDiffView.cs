using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwDiff.Core;

namespace UwDiff.App.Views;

public sealed class FolderRow
{
    public string[] Cells = Array.Empty<string>();
}

/// <summary>
/// フォルダの比較の一覧（区分,パス,旧の大きさ,新の大きさ,旧の更新日時,新の更新日時）。区分で絞り込み、
/// 「違う」の行をダブルクリックすると、その 1 組の差分を新しいタブで開く。
/// </summary>
public sealed class FolderDiffView : DockPanel, IResultView
{
    private readonly ResultPackage _pkg;
    private readonly Action<string, string> _openPair;
    internal readonly ListBox _list = new();
    /// <summary>区分の絞り込み（並びは KindValues。一覧の区分は日本語なので、値はそのまま、見せる文字だけ訳す）。</summary>
    private readonly ComboBox _kind = new() { Width = 130 };
    private static readonly string[] KindValues = { "違いだけ", "すべて", "違う", "旧だけ", "新だけ", "同じ" };
    private static readonly string[] KindKeys = { "DiffsOnly", "All", "Different", "OnlyOld", "OnlyNew", "Same" };
    private readonly TextBlock _kindLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
    private readonly TextBlock _hint = new() { Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _head = RowGrid();
    private bool _relabeling;
    internal readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
    private CsvRowIndex? _index;
    private int[] _visible = Array.Empty<int>();

    public FolderDiffView(ResultPackage pkg, Action<string, string> openPair)
    {
        _pkg = pkg;
        _openPair = openPair;
        _kind.SelectionChanged += (_, _) => { if (!_relabeling) Filter(); };
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6),
            Children = { _kindLabel, _kind, _summary, _hint },
        };
        SetDock(bar, Dock.Top);
        Children.Add(bar);
        foreach (TextBlock t in _head.Children.OfType<TextBlock>()) t.FontWeight = FontWeight.SemiBold;
        _head.Margin = new Thickness(8, 0, 8, 2);
        SetDock(_head, Dock.Top);
        Children.Add(_head);
        _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<FolderRow>((_, _) => new FolderRowView(), supportsRecycling: true);
        _list.FontSize = 12;
        _list.DoubleTapped += (_, _) =>
        {
            if (_list.SelectedItem is FolderRow r && r.Cells.Length > 1 && r.Cells[0] == "違う")
                _openPair(Path.Combine(_pkg.Old, r.Cells[1]), Path.Combine(_pkg.New, r.Cells[1]));
        };
        Children.Add(_list);
        Texts();
        Loc.Changed += Texts;
        _ = LoadAsync();
    }

    /// <summary>文言を、いまの言語で入れ直す。</summary>
    private void Texts()
    {
        int sel = Math.Max(0, _kind.SelectedIndex);
        _relabeling = true;
        _kind.ItemsSource = KindKeys.Select(Loc.T).ToArray();
        _kind.SelectedIndex = sel;
        _relabeling = false;
        _kindLabel.Text = Loc.T("Kind");
        _hint.Text = Loc.T("FolderHint");
        ToolTip.SetTip(_kind, Loc.T("TipFolderKind"));
        string[] names = { "Kind", "Path", "OldSize", "NewSize", "OldTime", "NewTime" };
        for (int i = 0; i < names.Length; i++) ((TextBlock)_head.Children[i]).Text = Loc.T(names[i]);
        if (_index != null) Filter();
    }

    public void Release() => Loc.Changed -= Texts;

    internal static Grid RowGrid() => Fill(new Grid());

    /// <summary>列（区分・パス・大きさ 2・更新日時 2）と、その中の文字を入れる。</summary>
    internal static Grid Fill(Grid g)
    {
        g.ColumnDefinitions = new ColumnDefinitions("60,*,100,100,140,140");
        g.Height = 22;
        for (int i = 0; i < 6; i++)
        {
            var t = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0),
                                    TextAlignment = i is 2 or 3 ? TextAlignment.Right : TextAlignment.Left };
            Grid.SetColumn(t, i);
            g.Children.Add(t);
        }
        return g;
    }

    private async Task LoadAsync()
    {
        if (_pkg.ResultFile == null) return;
        _index = await Task.Run(() => CsvRowIndex.Build(_pkg.ResultFile));
        Filter();
    }

    private void Filter()
    {
        if (_index == null) return;
        string k = KindValues[Math.Clamp(_kind.SelectedIndex, 0, KindValues.Length - 1)];
        var v = new List<int>();
        for (int i = 0; i < _index.Count; i++)
        {
            string kind = _index.Kinds[i];
            if (k == "すべて" || kind == k || (k == "違いだけ" && kind != "同じ")) v.Add(i);
        }
        _visible = v.ToArray();
        _list.ItemsSource = new VirtualList<FolderRow>(_visible.Length, j => new FolderRow { Cells = _index.Row(_visible[j]) });
        int same = _index.Kinds.Count(x => x == "同じ"), diff = _index.Kinds.Count(x => x == "違う"), oo = _index.Kinds.Count(x => x == "旧だけ"), on = _index.Kinds.Count(x => x == "新だけ");
        _summary.Text = Loc.F("FolderSummary", oo, on, diff, same);
    }

    private sealed class FolderRowView : Grid
    {
        public FolderRowView() => Fill(this);

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is not FolderRow r) return;
            for (int i = 0; i < Children.Count; i++) ((TextBlock)Children[i]).Text = i < r.Cells.Length ? (i == 0 ? Loc.Kind(r.Cells[0]) : Fmt(i, r.Cells[i])) : "";
            ToolTip.SetTip(this, r.Cells.Length > 1 ? r.Cells[1] : null);
            Background = r.Cells.Length > 0 ? Palette.OfKind(r.Cells[0]) : null;
        }

        private static string Fmt(int col, string v) => col is 2 or 3 && long.TryParse(v, out long n) ? n.ToString("N0") : v;
    }
}
