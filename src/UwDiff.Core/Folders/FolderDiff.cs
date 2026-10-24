using System.Diagnostics;
using System.IO.Enumeration;

namespace UwDiff.Core.Folders;

public enum FolderEntryKind
{
    /// <summary>旧にだけある。</summary>
    OnlyOld,
    /// <summary>新にだけある。</summary>
    OnlyNew,
    /// <summary>中身が同じ。</summary>
    Same,
    /// <summary>中身が違う。</summary>
    Different,
}

/// <summary>フォルダの比較の 1 件（旧・新のフォルダからの相対パス。区切りは /）。</summary>
public sealed record FolderEntry(string RelPath, FolderEntryKind Kind, long OldSize, long NewSize, DateTime? OldTime, DateTime? NewTime)
{
    public static string KindName(FolderEntryKind k) => k switch
    {
        FolderEntryKind.OnlyOld => "旧だけ",
        FolderEntryKind.OnlyNew => "新だけ",
        FolderEntryKind.Same => "同じ",
        _ => "違う",
    };
}

public sealed class FolderDiffOptions
{
    public required string OldDir { get; init; }
    public required string NewDir { get; init; }
    /// <summary>比べるファイルの名前の型（"*.csv;*.log" のように ; 区切り。null ならすべて）。</summary>
    public string? Include { get; init; }
    /// <summary>除くファイル・フォルダの名前の型（".git;*.tmp"。フォルダの名前に合えば、その下をすべて除く）。</summary>
    public string? Exclude { get; init; }
    /// <summary>中身を同時に比べるファイルの数（0 なら CPU の数から）。</summary>
    public int Workers { get; init; }
    public Action<long, long>? Progress { get; init; }
}

public sealed class FolderDiffResult
{
    public List<FolderEntry> Entries { get; } = new();
    public long OnlyOld, OnlyNew, Same, Different;
    /// <summary>中身を読んで比べたバイト数（旧・新の合計）。</summary>
    public long BytesCompared;
    public TimeSpan Total;
}

/// <summary>
/// 2 つのフォルダを、下の階層まで、ファイル名（相対パス）で対応付けて比べる。
/// 大きさが違えば「違う」。同じ大きさなら中身をバイト単位で先頭から比べ（違いが見つかった所でやめる）、ファイルごとに並列で行う。
/// 名前の大文字・小文字は、Windows と macOS では区別しない（ファイルシステムの既定に合わせる）。
/// </summary>
public static class FolderDiff
{
    public static StringComparer NameComparer { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static FolderDiffResult Run(FolderDiffOptions o)
    {
        var sw = Stopwatch.StartNew();
        var res = new FolderDiffResult();
        string[] inc = Patterns(o.Include), exc = Patterns(o.Exclude);
        Dictionary<string, FileInfo> olds = List(o.OldDir, inc, exc), news = List(o.NewDir, inc, exc);

        var both = new List<(string Rel, FileInfo A, FileInfo B)>();
        var entries = new List<FolderEntry>();
        foreach (var (rel, a) in olds)
        {
            if (news.TryGetValue(rel, out FileInfo? b)) both.Add((rel, a, b));
            else entries.Add(new FolderEntry(rel, FolderEntryKind.OnlyOld, a.Length, 0, a.LastWriteTime, null));
        }
        foreach (var (rel, b) in news)
            if (!olds.ContainsKey(rel)) entries.Add(new FolderEntry(rel, FolderEntryKind.OnlyNew, 0, b.Length, null, b.LastWriteTime));

        long total = both.Sum(x => x.A.Length == x.B.Length ? x.A.Length * 2 : 0), done = 0, lastShown = 0;
        var results = new FolderEntry[both.Count];
        int workers = o.Workers > 0 ? o.Workers : Math.Clamp(Environment.ProcessorCount, 2, 8);
        // 大きいファイルから始める（最後に大きいのが 1 本だけ残って待つのを避ける）
        int[] order = Enumerable.Range(0, both.Count).OrderByDescending(i => both[i].A.Length).ToArray();
        Parallel.ForEach(order, new ParallelOptions { MaxDegreeOfParallelism = workers }, () => (new byte[1 << 20], new byte[1 << 20]), (i, _, buf) =>
        {
            var (rel, a, b) = both[i];
            bool same = a.Length == b.Length && SameBytes(a.FullName, b.FullName, buf.Item1, buf.Item2, n =>
            {
                long d = Interlocked.Add(ref done, n);
                long now = Environment.TickCount64;
                if (o.Progress != null && now - Interlocked.Read(ref lastShown) > 500)
                {
                    Interlocked.Exchange(ref lastShown, now);
                    o.Progress(d, total);
                }
            });
            results[i] = new FolderEntry(rel, same ? FolderEntryKind.Same : FolderEntryKind.Different, a.Length, b.Length, a.LastWriteTime, b.LastWriteTime);
            return buf;
        }, _ => { });
        entries.AddRange(results);
        entries.Sort((x, y) => StringComparer.Ordinal.Compare(x.RelPath, y.RelPath));
        res.Entries.AddRange(entries);
        foreach (FolderEntry e in entries)
        {
            switch (e.Kind)
            {
                case FolderEntryKind.OnlyOld: res.OnlyOld++; break;
                case FolderEntryKind.OnlyNew: res.OnlyNew++; break;
                case FolderEntryKind.Same: res.Same++; break;
                default: res.Different++; break;
            }
        }
        res.BytesCompared = done;
        res.Total = sw.Elapsed;
        return res;
    }

    private static string[] Patterns(string? s) =>
        string.IsNullOrWhiteSpace(s) ? Array.Empty<string>() : s.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool Matches(string name, string[] patterns)
    {
        foreach (string p in patterns)
            if (FileSystemName.MatchesSimpleExpression(p, name, ignoreCase: !OperatingSystem.IsLinux())) return true;
        return false;
    }

    /// <summary>フォルダの下のファイル（相対パス → 情報）。除くフォルダの下には入らない。リンク先のフォルダはたどらない。</summary>
    private static Dictionary<string, FileInfo> List(string dir, string[] inc, string[] exc)
    {
        var root = new DirectoryInfo(dir);
        if (!root.Exists) throw new DirectoryNotFoundException(dir);
        var map = new Dictionary<string, FileInfo>(NameComparer);
        var opts = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = 0, ReturnSpecialDirectories = false };
        var stack = new Stack<(DirectoryInfo Dir, string Rel)>();
        stack.Push((root, ""));
        while (stack.Count > 0)
        {
            var (d, rel) = stack.Pop();
            foreach (FileSystemInfo e in d.EnumerateFileSystemInfos("*", opts))
            {
                if (exc.Length > 0 && Matches(e.Name, exc)) continue;
                string r = rel.Length == 0 ? e.Name : rel + "/" + e.Name;
                if (e is DirectoryInfo sub)
                {
                    if ((sub.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push((sub, r));
                    continue;
                }
                if (inc.Length > 0 && !Matches(e.Name, inc)) continue;
                map[r] = (FileInfo)e;
            }
        }
        return map;
    }

    /// <summary>2 本の中身がバイト単位で同じか（違いが見つかった所でやめる）。</summary>
    private static bool SameBytes(string pa, string pb, byte[] ba, byte[] bb, Action<long> progress)
    {
        using var fa = new FileStream(pa, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        using var fb = new FileStream(pb, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        while (true)
        {
            int na = ReadFully(fa, ba), nb = ReadFully(fb, bb);
            progress(na + nb);
            if (na != nb || !ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return false;
            if (na < ba.Length) return true;
        }
    }

    private static int ReadFully(Stream s, byte[] buf)
    {
        int total = 0;
        while (total < buf.Length)
        {
            int n = s.Read(buf, total, buf.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
