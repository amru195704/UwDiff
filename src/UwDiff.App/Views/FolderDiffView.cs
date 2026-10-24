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
public sealed class FolderDiffView : DockPanel
{
    private readonly ResultPackage _pkg;
    private readonly Action<string, string> _openPair;
    internal readonly ListBox _list = new();
    private readonly ComboBox _kind = new() { ItemsSource = new[] { "違いだけ", "すべて", "違う", "旧だけ", "新だけ", "同じ" }, SelectedIndex = 0, Width = 120 };
    internal readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
    private CsvRowIndex? _index;
    private int[] _visible = Array.Empty<int>();

    public FolderDiffView(ResultPackage pkg, Action<string, string> openPair)
    {
        _pkg = pkg;
        _openPair = openPair;
        _kind.SelectionChanged += (_, _) => Filter();
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6),
            Children = { new TextBlock { Text = "区分", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, _kind, _summary,
                         new TextBlock { Text = "（「違う」の行をダブルクリックで差分を開く）", Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Center } },
        };
        SetDock(bar, Dock.Top);
        Children.Add(bar);
        var head = RowGrid();
        string[] names = { "区分", "パス", "旧の大きさ", "新の大きさ", "旧の更新日時", "新の更新日時" };
        for (int i = 0; i < names.Length; i++) { ((TextBlock)head.Children[i]).Text = names[i]; ((TextBlock)head.Children[i]).FontWeight = FontWeight.SemiBold; }
        head.Margin = new Thickness(8, 0, 8, 2);
        SetDock(head, Dock.Top);
        Children.Add(head);
        _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<FolderRow>((_, _) => new FolderRowView(), supportsRecycling: true);
        _list.FontSize = 12;
        _list.DoubleTapped += (_, _) =>
        {
            if (_list.SelectedItem is FolderRow r && r.Cells.Length > 1 && r.Cells[0] == "違う")
                _openPair(Path.Combine(_pkg.Old, r.Cells[1]), Path.Combine(_pkg.New, r.Cells[1]));
        };
        Children.Add(_list);
        _ = LoadAsync();
    }

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
        string k = _kind.SelectedItem as string ?? "違いだけ";
        var v = new List<int>();
        for (int i = 0; i < _index.Count; i++)
        {
            string kind = _index.Kinds[i];
            if (k == "すべて" || kind == k || (k == "違いだけ" && kind != "同じ")) v.Add(i);
        }
        _visible = v.ToArray();
        _list.ItemsSource = new VirtualList<FolderRow>(_visible.Length, j => new FolderRow { Cells = _index.Row(_visible[j]) });
        int same = _index.Kinds.Count(x => x == "同じ"), diff = _index.Kinds.Count(x => x == "違う"), oo = _index.Kinds.Count(x => x == "旧だけ"), on = _index.Kinds.Count(x => x == "新だけ");
        _summary.Text = $"旧だけ {oo:N0}・新だけ {on:N0}・違う {diff:N0}・同じ {same:N0}";
    }

    private sealed class FolderRowView : Grid
    {
        public FolderRowView() => Fill(this);

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is not FolderRow r) return;
            for (int i = 0; i < Children.Count; i++) ((TextBlock)Children[i]).Text = i < r.Cells.Length ? Fmt(i, r.Cells[i]) : "";
            Background = r.Cells.Length > 0 ? Palette.OfKind(r.Cells[0]) : null;
        }

        private static string Fmt(int col, string v) => col is 2 or 3 && long.TryParse(v, out long n) ? n.ToString("N0") : v;
    }
}
