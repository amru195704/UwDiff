namespace UwDiff.Core.Text;

/// <summary>編集の 1 区間（同じ・削除・追加が何行続くか）。</summary>
public readonly record struct EditRun(EditKind Kind, int Count);

public enum EditKind : byte
{
    Equal,
    Delete,
    Insert,
}

/// <summary>
/// Myers の差分（O((N+M)D)）。区間が小さいとき（アンカーとアンカーの間）に使う。
/// 違いの数 D が上限を超えたら null を返す（呼ぶ側は「全部を置き換え」にする）。
/// </summary>
public static class Myers
{
    /// <summary>a の n 行と b の m 行を比べ、編集の並びを返す。equal(i, j) で行の一致を判定する。</summary>
    public static List<EditRun>? Diff(int n, int m, Func<int, int, bool> equal, int maxD)
    {
        var runs = new List<EditRun>();
        if (n == 0 && m == 0) return runs;
        if (n == 0) { runs.Add(new EditRun(EditKind.Insert, m)); return runs; }
        if (m == 0) { runs.Add(new EditRun(EditKind.Delete, n)); return runs; }

        int max = Math.Min(n + m, maxD);
        int off = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        int found = -1;
        for (int d = 0; d <= max; d++)
        {
            trace.Add((int[])v.Clone());
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[off + k - 1] < v[off + k + 1]) ? v[off + k + 1] : v[off + k - 1] + 1;
                int y = x - k;
                while (x < n && y < m && equal(x, y)) { x++; y++; }
                v[off + k] = x;
                if (x >= n && y >= m) { found = d; break; }
            }
            if (found >= 0) break;
        }
        if (found < 0) return null;

        // 後ろからたどって編集を集める
        var edits = new List<EditKind>(n + m);
        int cx = n, cy = m;
        for (int d = found; d > 0; d--)
        {
            int[] pv = trace[d];
            int k = cx - cy;
            int pk = k == -d || (k != d && pv[off + k - 1] < pv[off + k + 1]) ? k + 1 : k - 1;
            int px = pv[off + pk];
            int py = px - pk;
            while (cx > px && cy > py) { edits.Add(EditKind.Equal); cx--; cy--; }
            if (cx == px) { edits.Add(EditKind.Insert); cy--; }
            else { edits.Add(EditKind.Delete); cx--; }
        }
        while (cx > 0 && cy > 0) { edits.Add(EditKind.Equal); cx--; cy--; }
        edits.Reverse();

        foreach (EditKind e in edits)
        {
            if (runs.Count > 0 && runs[^1].Kind == e) runs[^1] = new EditRun(e, runs[^1].Count + 1);
            else runs.Add(new EditRun(e, 1));
        }
        return runs;
    }

    /// <summary>文字単位の違い（画面で行の中の違いを示すため）。長すぎる行は全体を置き換えとみなす。</summary>
    public static List<EditRun> Chars(string a, string b, int maxD = 2000)
    {
        // 共通の先頭・末尾を先に除く（ほとんどの変更は 1 か所なので速い）
        int p = 0;
        while (p < a.Length && p < b.Length && a[p] == b[p]) p++;
        int s = 0;
        while (s < a.Length - p && s < b.Length - p && a[a.Length - 1 - s] == b[b.Length - 1 - s]) s++;
        var runs = new List<EditRun>();
        if (p > 0) runs.Add(new EditRun(EditKind.Equal, p));
        int n = a.Length - p - s, m = b.Length - p - s;
        List<EditRun>? mid = Diff(n, m, (i, j) => a[p + i] == b[p + j], maxD);
        if (mid == null)
        {
            if (n > 0) runs.Add(new EditRun(EditKind.Delete, n));
            if (m > 0) runs.Add(new EditRun(EditKind.Insert, m));
        }
        else runs.AddRange(mid);
        if (s > 0) runs.Add(new EditRun(EditKind.Equal, s));
        return runs;
    }
}
