using System.Diagnostics;
using System.Globalization;
using System.Text;
using UwDiff.Core;
using UwDiff.Core.Folders;
using UwView.Core;
using static Uvd.Cli.Program;

namespace Uvd.Cli;

/// <summary>
/// フォルダどうしの比較。-o で一覧（区分,パス,旧の大きさ,新の大きさ,旧の更新日時,新の更新日時・UTF-8）を書く。
/// -detail で中身が違うファイルの差分を 1 本ずつ書く（テキストは .diff、-key があり表なら .csv）。
/// </summary>
internal static class FolderCommand
{
    public static int Run(CliArgs a)
    {
        var sw = Stopwatch.StartNew();
        long lastShown = 0;
        FolderDiffResult r = FolderDiff.Run(new FolderDiffOptions
        {
            OldDir = a.Positional[0],
            NewDir = a.Positional[1],
            Include = a.Include,
            Exclude = a.Exclude,
            Workers = a.Workers,
            Progress = a.Quiet ? null : (done, total) =>
            {
                long now = sw.ElapsedMilliseconds;
                if (now - lastShown < 1000) return;
                lastShown = now;
                Console.Error.Write($"\r  中身の比較 {done * 100.0 / Math.Max(1, total),5:F1}%  {sw.Elapsed.TotalSeconds,6:F1} 秒");
            },
        });
        if (!a.Quiet) Console.Error.WriteLine();
        if (a.ResultDir != null)
        {
            a.OutPath ??= Path.Combine(a.ResultDir, "folder.csv");
            a.NewResult("folder", a.OutPath);
        }

        if (a.OutPath != null)
        {
            using var w = new StreamWriter(a.OutPath, false, new UTF8Encoding(false), 1 << 16) { NewLine = "\n" };
            w.WriteLine("区分,パス,旧の大きさ,新の大きさ,旧の更新日時,新の更新日時");
            static string T(DateTime? t) => t?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
            static string Q(string s) => s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            foreach (FolderEntry e in r.Entries)
                w.WriteLine($"{FolderEntry.KindName(e.Kind)},{Q(e.RelPath)},{(e.Kind == FolderEntryKind.OnlyNew ? "" : e.OldSize)},{(e.Kind == FolderEntryKind.OnlyOld ? "" : e.NewSize)},{T(e.OldTime)},{T(e.NewTime)}");
        }

        Console.WriteLine($"ファイル: 旧だけ {r.OnlyOld:N0}／新だけ {r.OnlyNew:N0}／違う {r.Different:N0}／同じ {r.Same:N0}");
        Console.WriteLine($"時間: {r.Total.TotalSeconds:F2} 秒（中身を読んで比べた量 {Mb(r.BytesCompared)}）");

        long detailed = 0, detailFailed = 0;
        if (a.DetailDir != null && r.Different > 0)
        {
            Directory.CreateDirectory(a.DetailDir);
            var diffs = r.Entries.Where(e => e.Kind == FolderEntryKind.Different).ToList();
            int par = Math.Max(1, Environment.ProcessorCount / 4);
            Parallel.ForEach(diffs, new ParallelOptions { MaxDegreeOfParallelism = par }, e =>
            {
                string po = Path.Combine(a.Positional[0], e.RelPath), pn = Path.Combine(a.Positional[1], e.RelPath);
                try
                {
                    InputFile io = InputFile.Probe(po), inew = InputFile.Probe(pn);
                    bool table = a.Keys.Count > 0 && (io.LooksTable || io.Office == OfficeKind.Xlsx) && !io.IsOffice;
                    string outPath = Path.Combine(a.DetailDir, e.RelPath + (table ? ".csv" : ".diff"));
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                    if (table) TableCommand.DiffQuiet(a, io, inew, outPath);
                    else TextCommand.DiffQuiet(a, io, inew, outPath);
                    Interlocked.Increment(ref detailed);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or InvalidOperationException or NotSupportedException
                                           or UnauthorizedAccessException or UwView.Core.Documents.OfficeRejectedException)
                {
                    Interlocked.Increment(ref detailFailed);
                    lock (r) Console.Error.WriteLine($"  明細を作れない: {e.RelPath}（{ex.Message}）");
                }
            });
            Console.WriteLine($"明細: {detailed:N0} 本を {a.DetailDir} に書いた" + (detailFailed > 0 ? $"（作れなかった {detailFailed:N0} 本）" : ""));
        }

        if (a.JsonPath != null)
            File.WriteAllText(a.JsonPath, "{" +
                $"\"old\":{J(a.Positional[0])},\"new\":{J(a.Positional[1])},\"mode\":\"folder\",\"onlyOld\":{r.OnlyOld},\"onlyNew\":{r.OnlyNew}," +
                $"\"different\":{r.Different},\"same\":{r.Same},\"bytesCompared\":{r.BytesCompared},\"detailed\":{detailed},\"detailFailed\":{detailFailed}," +
                $"\"totalSec\":{F(r.Total.TotalSeconds)}" + "}\n");
        return r.OnlyOld + r.OnlyNew + r.Different > 0 ? 1 : 0;
    }
}
