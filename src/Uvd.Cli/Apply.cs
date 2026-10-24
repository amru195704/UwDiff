using UwDiff.Core.Apply;

namespace Uvd.Cli;

/// <summary>uvd apply 旧 新 差分.csv -o 結果: 差分（採る行だけ残したもの）を旧に反映する。</summary>
internal static class Apply
{
    public static int Run(string[] args)
    {
        var pos = new List<string>();
        var fmt = new Program.FormatOptions();
        string? outPath = null;
        bool quiet = false;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} の後に値が要ります");
            if (fmt.TryParse(a, Next)) continue;
            switch (a)
            {
                case "-o": outPath = Next(); break;
                case "-q": quiet = true; break;
                default:
                    if (a.StartsWith('-') && a.Length > 1) throw new ArgumentException("知らないオプション: " + a);
                    pos.Add(a);
                    break;
            }
        }
        if (pos.Count != 3 || outPath == null)
            throw new ArgumentException("使い方: uvd apply 旧 新 差分.csv -o 結果（-h で詳しく）");

        ApplyResult r = new ApplyEngine(new ApplyOptions
        {
            OldPath = pos[0],
            NewPath = pos[1],
            DiffPath = pos[2],
            OutPath = outPath,
            OldEncoding = fmt.OldEncoding,
            NewEncoding = fmt.NewEncoding,
            Delimiter = fmt.Delimiter,
            Quotes = fmt.Quotes,
        }).Run();

        long problems = r.Conflicts + r.Unencodable + r.MissingKeys + r.UnknownColumns;
        if (!quiet || problems > 0)
        {
            Console.WriteLine($"差分: 追加 {r.DiffAdded:N0}・削除 {r.DiffDeleted:N0}・変更 {r.DiffChanged:N0} セル（キー: {string.Join("＋", r.KeyColumnNames)}）");
            Console.WriteLine($"結果: 旧 {r.OldRows:N0} 行 → 残す {r.KeptRows:N0}（うち変更 {r.ChangedRows:N0} 行・{r.ChangedCells:N0} セル）・削除 {r.DeletedRows:N0}・追加 {r.AddedRows:N0}（{r.OldEncoding}・{r.OldDelimiter}）  {r.Total.TotalSeconds:F2} 秒");
            if (problems > 0)
            {
                Console.WriteLine($"反映しなかったもの: 旧値が違う {r.Conflicts:N0}・文字コードで書けない {r.Unencodable:N0}・キーが見つからない {r.MissingKeys:N0}・旧に無い列 {r.UnknownColumns:N0}");
                foreach (string p in r.Problems) Console.WriteLine("  " + p);
            }
        }
        // 反映しなかったものがあれば 1（結果のファイルは書いてある）
        return problems > 0 ? 1 : 0;
    }
}
