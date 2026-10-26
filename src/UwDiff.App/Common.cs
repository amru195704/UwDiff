using System.Collections;
using System.Collections.Specialized;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Media;
using UwDiff.Core;
using UwDiff.Core.Text;

namespace UwDiff.App;

/// <summary>
/// 要るときだけ項目を作る一覧（数億行でも、画面に出ている分だけ作る）。Avalonia の ListBox は IList を直接使い、写さない。
/// </summary>
public sealed class VirtualList<T> : IList, IReadOnlyList<T>, INotifyCollectionChanged where T : class
{
    private readonly Func<int, T> _make;
    private readonly Dictionary<int, T> _cache = new();
    private readonly Queue<int> _order = new();
    public int Count { get; }

    public VirtualList(int count, Func<int, T> make)
    {
        Count = count;
        _make = make;
    }

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (_cache.TryGetValue(index, out T? v)) return v;
            v = _make(index);
            _cache[index] = v;
            _order.Enqueue(index);
            if (_order.Count > 2000) _cache.Remove(_order.Dequeue());
            return v;
        }
    }

    object? IList.this[int index] { get => this[index]; set => throw new NotSupportedException(); }
    public event NotifyCollectionChangedEventHandler? CollectionChanged { add { } remove { } }
    public bool IsFixedSize => true;
    public bool IsReadOnly => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Contains(object? value) => IndexOf(value) >= 0;
    public int IndexOf(object? value)
    {
        foreach (var (k, v) in _cache) if (ReferenceEquals(v, value)) return k;
        return -1;
    }
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public void CopyTo(Array array, int index)
    {
        for (int i = 0; i < Count; i++) array.SetValue(this[i], index + i);
    }
    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < Count; i++) yield return this[i];
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>表示用ファイルの行を、256 行の塊で読んで覚えておく。</summary>
public sealed class LineCache
{
    private const int Block = 256;
    private readonly DisplayFile _f;
    private readonly Encoding _enc;
    private readonly Dictionary<long, string[]> _blocks = new();
    private readonly Queue<long> _order = new();
    private readonly object _lock = new();

    public LineCache(DisplayFile f)
    {
        _f = f;
        _enc = TextEncodings.Get(f.Encoding);
    }

    public long Lines => _f.Index.Lines;

    /// <summary>no 行目（1 始まり）。長すぎる行は画面のために切る。</summary>
    public string Get(long no)
    {
        long b = (no - 1) / Block;
        lock (_lock)
        {
            if (!_blocks.TryGetValue(b, out string[]? lines))
            {
                lines = _f.Index.Read(b * Block + 1, Block).Select(x => Decode(x)).ToArray();
                _blocks[b] = lines;
                _order.Enqueue(b);
                if (_order.Count > 512) _blocks.Remove(_order.Dequeue());
            }
            int i = (int)((no - 1) % Block);
            return i < lines.Length ? lines[i] : "";
        }
    }

    private string Decode(byte[] b)
    {
        string s = _enc.GetString(b, 0, Math.Min(b.Length, 8000));
        if (b.Length > 8000) s += Loc.T("LongLine");
        return s.Replace("\t", "    ");
    }
}

/// <summary>uvd を同じプロセスの中で 1 回ずつ実行する（標準出力・標準エラーを受け取り、進み具合を拾う）。</summary>
public static partial class UvdRunner
{
    private static readonly SemaphoreSlim s_one = new(1, 1);

    public sealed record Outcome(int ExitCode, string Output, string Error);

    public static async Task<Outcome> RunAsync(string[] args, Action<double>? progress)
    {
        await s_one.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                TextWriter oldOut = Console.Out, oldErr = Console.Error;
                var output = new StringWriter();
                var error = new ProgressWriter(progress);
                Console.SetOut(output);
                Console.SetError(error);
                try
                {
                    int rc = Uvd.Cli.Program.Run(args);
                    return new Outcome(rc, output.ToString(), error.Text);
                }
                finally
                {
                    Console.SetOut(oldOut);
                    Console.SetError(oldErr);
                }
            });
        }
        finally
        {
            s_one.Release();
        }
    }

    [GeneratedRegex(@"(\d+(?:\.\d)?)%")]
    private static partial Regex Percent();

    /// <summary>「\r  読み込み  45.3%」のような進み具合を拾い、それ以外の行（エラー）は貯める。</summary>
    private sealed class ProgressWriter(Action<double>? progress) : TextWriter
    {
        private readonly StringBuilder _text = new();
        private readonly StringBuilder _line = new();
        public override Encoding Encoding => Encoding.UTF8;
        public string Text => _text.ToString().Trim();

        public override void Write(char value)
        {
            if (value == '\r' || value == '\n')
            {
                string l = _line.ToString();
                _line.Clear();
                Match m = Percent().Match(l);
                if (l.Contains("読み込み") || l.Contains("中身の比較"))
                {
                    if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double p))
                        progress?.Invoke(p / 100);
                }
                else if (l.Trim().Length > 0) _text.AppendLine(l);
                return;
            }
            _line.Append(value);
        }
    }
}

/// <summary>
/// 結果のタブの中身。タブを閉じたとき（と窓を閉じたとき）に Release を呼ぶ。
/// タブを切り替えるだけでも画面から外れる（OnDetachedFromVisualTree が鳴る）ので、後片付けをそこでしてはいけない。
/// </summary>
public interface IResultView
{
    void Release();
}

/// <summary>差分の色（ライト・ダークのどちらでも見えるよう、半透明にする）。</summary>
public static class Palette
{
    public static readonly IBrush Deleted = new SolidColorBrush(Color.Parse("#40F05050"));
    public static readonly IBrush Inserted = new SolidColorBrush(Color.Parse("#4040B040"));
    public static readonly IBrush DeletedStrong = new SolidColorBrush(Color.Parse("#90F05050"));
    public static readonly IBrush InsertedStrong = new SolidColorBrush(Color.Parse("#9040B040"));
    public static readonly IBrush Changed = new SolidColorBrush(Color.Parse("#30E0A020"));
    public static readonly IBrush Fold = new SolidColorBrush(Color.Parse("#20808080"));
    public static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#888888"));
    /// <summary>
    /// 本文の等幅フォント。並べた名前のうち、この機械に実際にあるものを 1 つ選ぶ（どれも無ければ画面の既定のフォント）。
    /// 「Menlo, Consolas, …」のように並べて渡すと、Linux で 1 つも無いときに代わりへ切り替わらず落ちることがあった。
    /// </summary>
    public static FontFamily Mono => s_mono ??= PickMono();
    private static FontFamily? s_mono;

    private static readonly string[] MonoNames =
    {
        "Menlo", "Consolas", "Cascadia Mono", "DejaVu Sans Mono", "Liberation Mono", "Noto Sans Mono", "Noto Mono",
        "Ubuntu Mono", "Source Code Pro", "JetBrains Mono", "Hack", "Noto Sans Mono CJK JP", "FreeMono", "Courier New",
    };

    private static FontFamily PickMono()
    {
        try
        {
            var have = new HashSet<string>(FontManager.Current.SystemFonts.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            foreach (string n in MonoNames)
                if (have.Contains(n)) return new FontFamily(n);
        }
        catch (Exception) { }   // フォントを数えられない環境: 既定のフォントで出す（落とさない）
        return FontFamily.Default;
    }

    public static IBrush? OfKind(string kind) => kind switch
    {
        "追加" or "新だけ" => Inserted,
        "削除" or "旧だけ" => Deleted,
        "変更" or "違う" => Changed,
        _ => null,
    };
}
