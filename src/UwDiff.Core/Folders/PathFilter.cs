using UwView.Core.Cli;

namespace UwDiff.Core.Folders;

/// <summary>
/// フォルダの比較で、比べるファイルを選ぶ（-include）・外す（-exclude・--ignore-file）。書き方は uvf / uvp にそろえる。
///
/// 含める（uvf のファイルの指定と同じ書き方）:
///   区切りはカンマ（名前に空白を含むならこちら）。カンマが無ければ空白・タブ。<c>;</c> もカンマと同じに扱う（UwDiff の以前の書き方）。
///   <c>*</c>・<c>?</c> はフォルダの区切りをまたがない、<c>**</c> はフォルダを何段でも（0 段も）、<c>{a,b}</c> は「a か b」。
///   旧・新それぞれのフォルダから見たパスに当てる: <c>*.csv</c> は直下だけ、<c>**/*.csv</c> は下の階層も。空ならすべて。
/// 除く（uvf の除外 <c>--ignore-file</c> と同じ .gitignore の書き方。区切りは含めると同じで、1 つずつが 1 行）:
///   <c>/</c> が無ければどの深さの名前にも当たる（<c>.git</c>・<c>*.tmp</c>）。末尾の <c>/</c> はフォルダだけ。先頭の <c>/</c> は直下から。
///   <c>!</c> で取り消し。フォルダに当たれば、その下をすべて除く。<c>--ignore-file</c> のファイルは後に書いたものほど強く、欄の指定より弱い。
/// 大文字・小文字は区別する（uvf・git と同じ）。
/// </summary>
public sealed class PathFilter
{
    private readonly string[] _includeRules;
    private readonly string[] _excludeRules;
    private readonly IReadOnlyList<string> _ignoreFiles;
    private readonly Dictionary<string, (IgnoreFile? Include, IgnoreFile? Exclude, List<IgnoreFile> Files)> _byRoot = new();

    public bool HasInclude => _includeRules.Length > 0;

    public PathFilter(string? include, string? exclude, IReadOnlyList<string>? ignoreFiles = null)
    {
        // 含める: uvf の「*.log は直下、**/*.log は下の階層も」を、.gitignore の規則に直す（先頭に / を付けて直下から見る）
        _includeRules = Split(include).SelectMany(Braces.Expand)
            .Select(p => p.Replace('\\', '/'))
            .Select(p => p.StartsWith("./", StringComparison.Ordinal) ? p[2..] : p)
            .Where(p => p.Length > 0)
            .Select(p => p.StartsWith('/') ? p : "/" + p)
            .ToArray();
        _excludeRules = Split(exclude).ToArray();
        _ignoreFiles = ignoreFiles ?? Array.Empty<string>();
        foreach (string f in _ignoreFiles)
            if (!File.Exists(f)) throw new FileNotFoundException("除外のファイルが見つかりません", f);
    }

    /// <summary>uvf と同じ区切り: 波かっこの外にカンマ（か ;）があればそれで、無ければ空白・タブで分ける。</summary>
    public static IEnumerable<string> Split(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return Array.Empty<string>();
        bool comma = Braces.HasTopLevel(spec, ',') || Braces.HasTopLevel(spec, ';');
        string[] parts = comma ? Braces.SplitTopLevel(spec, c => c is ',' or ';') : Braces.SplitTopLevel(spec, c => c is ' ' or '\t');
        return parts.Select(p => p.Trim()).Where(p => p.Length > 0);
    }

    private (IgnoreFile? Include, IgnoreFile? Exclude, List<IgnoreFile> Files) For(string root)
    {
        if (_byRoot.TryGetValue(root, out var r)) return r;
        IgnoreFile? inc = _includeRules.Length > 0 ? IgnoreFile.Parse(string.Join("\n", _includeRules), root) : null;
        IgnoreFile? exc = _excludeRules.Length > 0 ? IgnoreFile.Parse(string.Join("\n", _excludeRules), root) : null;
        var files = new List<IgnoreFile>();
        foreach (string f in _ignoreFiles)
            if (IgnoreFile.Load(f, root) is { } loaded) files.Add(loaded);
        files.Reverse();   // 後に書いたものほど強い
        r = (inc, exc, files);
        _byRoot[root] = r;
        return r;
    }

    /// <summary>除くか（root は比べるフォルダの絶対パス、fullPath はその下のファイル・フォルダの絶対パス）。</summary>
    public bool Excluded(string root, string fullPath, bool isDirectory)
    {
        var (_, exc, files) = For(root);
        if (exc != null)
        {
            IgnoreMatch m = exc.Match(fullPath, isDirectory);
            if (m != IgnoreMatch.None) return m == IgnoreMatch.Ignore;
        }
        foreach (IgnoreFile f in files)
        {
            IgnoreMatch m = f.Match(fullPath, isDirectory);
            if (m != IgnoreMatch.None) return m == IgnoreMatch.Ignore;
        }
        return false;
    }

    /// <summary>含めるか（含めるの指定が無ければすべて含める）。ファイルだけに使う。</summary>
    public bool Included(string root, string fullPath)
    {
        var (inc, _, _) = For(root);
        return inc == null || inc.Match(fullPath, false) == IgnoreMatch.Ignore;
    }
}
