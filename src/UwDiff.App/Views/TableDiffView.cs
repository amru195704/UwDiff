using System.Buffers;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using UwDiff.Core;

namespace UwDiff.App.Views;

/// <summary>差分 CSV の 1 行。</summary>
public sealed class DiffRow
{
    public int Index;
    public string[] Cells = Array.Empty<string>();
    public bool Taken;
}

/// <summary>
/// 表の差分（区分,シート?,キー列…,列,旧値,新値）を一覧で見せる。区分で絞り込み、文字で探し、採る行を選んで旧に反映した新しいファイルを書き出す。
/// 差分 CSV は全部をメモリに載せず、行の位置の索引から画面に出る行だけ読む。
/// </summary>
public sealed class TableDiffView : DockPanel, IResultView
{
    private readonly ResultPackage _pkg;
    private readonly Action<string, bool> _status;
    internal readonly ListBox _list = new();
    /// <summary>区分の絞り込み（並びは KindValues。差分 CSV の区分は日本語なので、値はそのまま、見せる文字だけ訳す）。</summary>
    internal readonly ComboBox _kind = new() { Width = 120 };
    private static readonly string[] KindValues = { "すべて", "追加", "削除", "変更" };
    private static readonly string[] KindKeys = { "All", "Added", "Deleted", "Changed" };
    internal readonly TextBox _search = new() { Width = 220, Margin = new Thickness(8, 0, 0, 0) };
    internal readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
    private readonly Button _apply = new() { Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _all = new() { Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _none = new() { Margin = new Thickness(4, 0, 0, 0) };
    private readonly TextBlock _kindLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
    private Grid? _head;
    private CsvRowIndex? _index;
    internal bool[] _taken = Array.Empty<bool>();
    internal int[] _visible = Array.Empty<int>();
    private FileStream? _raw;
    private readonly object _rawLock = new();
    private string[] _header = Array.Empty<string>();

    public TableDiffView(ResultPackage pkg, Action<string, bool> status)
    {
        _pkg = pkg;
        _status = status;
        _all.Click += (_, _) => SetTaken(true);
        _none.Click += (_, _) => SetTaken(false);
        _apply.Click += async (_, _) => await ApplyAsync();
        _kind.SelectionChanged += (_, _) => { if (!_relabeling) Filter(); };
        _search.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Filter(); };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6), Children = { _kindLabel, _kind, _search, _all, _none, _apply, _summary } };
        SetDock(bar, Dock.Top);
        Children.Add(bar);
        _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DiffRow>((_, _) => new DiffRowView(this), supportsRecycling: true);
        _list.FontSize = 12;
        Texts();
        Loc.Changed += Texts;
        _ = LoadAsync();
    }

    private bool _relabeling;

    /// <summary>文言を、いまの言語で入れ直す。</summary>
    private void Texts()
    {
        int sel = Math.Max(0, _kind.SelectedIndex);
        _relabeling = true;
        _kind.ItemsSource = KindKeys.Select(Loc.T).ToArray();
        _kind.SelectedIndex = sel;
        _relabeling = false;
        _kindLabel.Text = Loc.T("Kind");
        _search.PlaceholderText = Loc.T("SearchPlaceholder");
        _all.Content = Loc.T("TakeAll");
        _none.Content = Loc.T("TakeNone");
        _apply.Content = Loc.T("Apply");
        ToolTip.SetTip(_kind, Loc.T("TipKind"));
        ToolTip.SetTip(_search, Loc.T("TipSearch"));
        ToolTip.SetTip(_all, Loc.T("TipTakeAll"));
        ToolTip.SetTip(_none, Loc.T("TipTakeNone"));
        ToolTip.SetTip(_apply, Loc.T(_index != null && !_apply.IsEnabled ? "TipApplyDisabled" : "TipApply"));
        // 反映できないときも理由が見えるよう、押せないボタンにもツールチップを出す
        ToolTip.SetShowOnDisabled(_apply, true);
        if (_head != null) for (int i = 0; i < _header.Length; i++) ((TextBlock)_head.Children[i + 1]).Text = HeaderName(_header[i]);
        if (_head != null) ((TextBlock)_head.Children[0]).Text = Loc.T("Take");
        if (_index != null) { UpdateSummary(); _list.ItemsSource = MakeList(); }
        else _summary.Text = Loc.T("Reading");
    }

    /// <summary>差分 CSV の見出し（日本語）を、いまの言語に（キー列の名前はそのまま）。</summary>
    private static string HeaderName(string h) => Loc.Ja ? h : h switch
    {
        "区分" => "Kind",
        "シート" => "Sheet",
        "列" => "Column",
        "旧値" => "Old value",
        "新値" => "New value",
        "行" => "Line",
        _ => h,
    };

    private async Task LoadAsync()
    {
        if (_pkg.ResultFile == null) return;
        _summary.Text = Loc.T("Reading");
        _index = await Task.Run(() => CsvRowIndex.Build(_pkg.ResultFile));
        _header = _index.Header;
        _raw = _index.OpenRaw();
        _taken = Enumerable.Repeat(true, _index.Count).ToArray();
        // 見出しの行
        _head = DiffRowView.Layout(_header.Length, isHeader: true);
        _head.Margin = new Thickness(8, 0, 8, 2);
        SetDock(_head, Dock.Top);
        Children.Insert(1, _head);
        Children.Add(_list);
        // 反映できるのは CSV・TSV の照合（Excel のシート・順番を問わない比較は差分の一覧まで）
        _apply.IsEnabled = _pkg.Mode == "table" && !_header.Contains("シート");
        Texts();
        Filter();
    }

    internal string[] Row(int i)
    {
        lock (_rawLock)
        {
            byte[] b = _index!.RawRow(_raw!, i);
            var fields = new List<CsvBytes.Field>();
            CsvBytes.Split(b, (byte)',', fields, true);
            var r = new string[fields.Count];
            byte[] tmp = new byte[Math.Max(16, b.Length)];
            for (int k = 0; k < fields.Count; k++)
                r[k] = Encoding.UTF8.GetString(tmp, 0, CsvBytes.Unquote(b.AsSpan(fields[k].Start, fields[k].Length), fields[k].Quoted, tmp));
            return r;
        }
    }

    internal void Toggle(DiffRow r, bool value)
    {
        r.Taken = value;
        _taken[r.Index] = value;
        UpdateSummary();
    }

    private void SetTaken(bool value)
    {
        foreach (int i in _visible) _taken[i] = value;
        _list.ItemsSource = MakeList();
        UpdateSummary();
    }

    internal async void Filter()
    {
        if (_index == null) return;
        string kind = KindValues[Math.Clamp(_kind.SelectedIndex, 0, KindValues.Length - 1)];
        string q = _search.Text?.Trim() ?? "";
        CsvRowIndex index = _index;
        // 文字で探すときは全部の行を読むので、画面を止めないよう裏で
        int[] vis = await Task.Run(() =>
        {
            var v = new List<int>(index.Count);
            for (int i = 0; i < index.Count; i++)
            {
                if (kind != "すべて" && index.Kinds[i] != kind) continue;
                if (q.Length > 0 && !Row(i).Any(c => c.Contains(q, StringComparison.OrdinalIgnoreCase))) continue;
                v.Add(i);
            }
            return v.ToArray();
        });
        _visible = vis;
        _list.ItemsSource = MakeList();
        UpdateSummary();
    }

    private VirtualList<DiffRow> MakeList() => new(_visible.Length, k => new DiffRow { Index = _visible[k], Cells = Row(_visible[k]), Taken = _taken[_visible[k]] });

    private void UpdateSummary()
    {
        if (_index == null) return;
        int a = 0, d = 0, c = 0;
        foreach (string k in _index.Kinds) { if (k == "追加") a++; else if (k == "削除") d++; else if (k == "変更") c++; }
        _summary.Text = Loc.F("TableSummary", a, d, c, _visible.Length, _taken.Count(x => x));
    }

    /// <summary>採った行だけの差分を書き、uvd apply で旧に反映した新しいファイルを書き出す。</summary>
    private async Task ApplyAsync()
    {
        if (_index == null || TopLevel.GetTopLevel(this) is not { } top) return;
        string ext = Path.GetExtension(_pkg.Old);
        IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T("SaveApplied"),
            SuggestedFileName = Path.GetFileNameWithoutExtension(_pkg.Old) + Loc.T("AppliedSuffix") + ext,
        });
        if (file?.TryGetLocalPath() is not { } outPath) return;
        await ApplyToAsync(outPath);
    }

    /// <summary>採った行だけで反映して outPath に書く（保存先を選んだ後。試験からも呼ぶ）。</summary>
    internal async Task<UvdRunner.Outcome> ApplyToAsync(string outPath)
    {
        if (_index == null) return new UvdRunner.Outcome(2, "", "差分を読み終えていません");
        CsvRowIndex index = _index;
        string sel = Path.Combine(Path.GetDirectoryName(_pkg.ResultFile!)!, "selected.csv");
        await Task.Run(() =>
        {
            using var w = new FileStream(sel, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
            var head = new ArrayBufferWriter<byte>();
            for (int i = 0; i < _header.Length; i++)
            {
                if (i > 0) head.Write(","u8);
                CsvBytes.WriteField(head, Encoding.UTF8.GetBytes(_header[i]), (byte)',');
            }
            head.Write("\n"u8);
            w.Write(head.WrittenSpan);
            lock (_rawLock)
            {
                for (int i = 0; i < _index.Count; i++)
                {
                    if (!_taken[i]) continue;
                    w.Write(_index.RawRow(_raw!, i));
                    w.WriteByte((byte)'\n');
                }
            }
        });
        var args = new List<string> { "apply", _pkg.Old, _pkg.New, sel, "-o", outPath, "-q" };
        // 比べたときと同じ区切り・引用符の扱いで読む（-d ;・-quote off で比べた表も反映できるように）
        if (_pkg.Delimiter != null) { args.Add("-d"); args.Add(_pkg.Delimiter); }
        if (!_pkg.Quotes) { args.Add("-quote"); args.Add("off"); }
        if (_pkg.OldEncoding != null) { args.Add("-enc-old"); args.Add(_pkg.OldEncoding); }
        if (_pkg.NewEncoding != null) { args.Add("-enc-new"); args.Add(_pkg.NewEncoding); }
        UvdRunner.Outcome o = await UvdRunner.RunAsync(args.ToArray(), null);
        string msg = (o.Output + "\n" + o.Error).Trim();
        _status(o.ExitCode == 0 ? Loc.F("Applied", outPath) : o.ExitCode == 1 ? Loc.F("AppliedPartly", outPath, msg) : Loc.F("ApplyFailed", msg), o.ExitCode == 2);
        return o;
    }

    /// <summary>タブを閉じたとき: 差分 CSV の読み口を閉じる。</summary>
    public void Release()
    {
        Loc.Changed -= Texts;
        lock (_rawLock) _raw?.Dispose();
    }

    /// <summary>1 行の見た目: 採る・区分・…列…。</summary>
    private sealed class DiffRowView : Grid
    {
        private readonly TableDiffView _owner;
        private readonly CheckBox _check = new() { MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        private DiffRow? _row;

        public DiffRowView(TableDiffView owner)
        {
            _owner = owner;
            int n = Math.Max(5, owner._header.Length);
            ColumnDefinitions = Columns(n);
            Height = 22;
            Children.Add(_check);
            for (int i = 0; i < n; i++)
            {
                var t = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
                SetColumn(t, i + 1);
                Children.Add(t);
            }
            ToolTip.SetTip(_check, Loc.T("TipTakeRow"));
            _check.IsCheckedChanged += (_, _) => { if (_row != null && _check.IsChecked != _row.Taken) _owner.Toggle(_row, _check.IsChecked == true); };
        }

        /// <summary>列の幅: 採る 28・区分 50・（シート・キー…）140・列 120・旧値と新値は残りを半分ずつ。</summary>
        private static ColumnDefinitions Columns(int n)
        {
            var parts = new List<string> { "28", "50" };
            for (int i = 1; i < n - 3; i++) parts.Add("140");
            parts.AddRange(new[] { "120", "*", "*" });
            return new ColumnDefinitions(string.Join(",", parts));
        }

        public static Grid Layout(int n, bool isHeader)
        {
            var g = new Grid { ColumnDefinitions = Columns(Math.Max(5, n)) };
            g.Children.Add(new TextBlock { Text = isHeader ? Loc.T("Take") : "" });
            for (int i = 0; i < Math.Max(5, n); i++)
            {
                var t = new TextBlock { FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                SetColumn(t, i + 1);
                g.Children.Add(t);
            }
            return g;
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            _row = DataContext as DiffRow;
            if (_row == null) return;
            _check.IsChecked = _row.Taken;
            for (int i = 1; i < Children.Count; i++)
                ((TextBlock)Children[i]).Text = i - 1 < _row.Cells.Length ? (i == 1 ? Loc.Kind(_row.Cells[0]) : _row.Cells[i - 1].Replace('\n', '⏎')) : "";
            Background = _row.Cells.Length > 0 ? Palette.OfKind(_row.Cells[0]) : null;
        }
    }
}
