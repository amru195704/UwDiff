using System.Diagnostics;
using UwDiff.Core;
using UwDiff.Core.Text;
using static Uvd.Cli.Program;

namespace Uvd.Cli;

/// <summary>テキストの比較（行の並びを保つ）。-o で unified diff を書く（-o - なら標準出力へ。そのとき要約は標準エラーへ）。</summary>
internal static class TextCommand
{
    public static int Run(CliArgs a)
    {
        InputFile io = InputFile.Probe(a.Positional[0]), inew = InputFile.Probe(a.Positional[1]);
        TextSource so = io.AsText(a.Format.OldEncoding, a.Raw), sn = inew.AsText(a.Format.NewEncoding, a.Raw);
        bool toStdout = a.OutPath == "-";
        TextWriter info = toStdout ? Console.Error : Console.Out;
        var sw = Stopwatch.StartNew();
        long lastShown = 0;
        var engine = new TextDiffEngine(Options(a, a.ResultDir != null, a.Quiet ? null : (done, total) =>
        {
            long now = sw.ElapsedMilliseconds;
            if (now - lastShown < 1000) return;
            lastShown = now;
            Console.Error.Write($"\r  読み込み {done * 100.0 / Math.Max(1, total),5:F1}%  {sw.Elapsed.TotalSeconds,6:F1} 秒");
        }));

        TextDiffResult r;
        long hunks = 0;
        // 画面に渡すときは、変更の位置も記録する
        HunkRecorder? rec = a.ResultDir != null ? new HunkRecorder() : null;
        if (a.OutPath == null)
        {
            r = engine.Run(so, sn, rec ?? (ITextDiffSink)new CountingSink());
        }
        else
        {
            // 一時ファイルに書き、終わってから -o の名前にする（途中で失敗しても、-o の先は元のまま）
            using Stream output = toStdout ? Console.OpenStandardOutput() : OutputGuard.SafeFile.Create(a.OutPath);
            using var w = new UnifiedDiffWriter(output, so, sn, a.Context, TempPath(a));
            r = engine.Run(so, sn, rec == null ? w : new TeeSink(w, rec));
            hunks = w.Hunks;
            (output as OutputGuard.SafeFile)?.Commit();
        }
        if (rec != null)
        {
            string hp = Path.Combine(a.ResultDir!, "hunks.bin");
            HunkRecorder.Save(hp, rec.Hunks);
            ResultPackage pkg = a.NewResult("text", hp);
            pkg.OldEncoding = r.OldEncoding;
            pkg.NewEncoding = r.NewEncoding;
            // 普通のテキストを変換せずに比べたときは、行の位置がそのままファイルの位置なので、画面のために保存する
            pkg.OldIndex = SaveIndex(io, r.OldRaw, r.OldLineStarts, r.OldLines, Path.Combine(a.ResultDir!, "old.idx"));
            pkg.NewIndex = SaveIndex(inew, r.NewRaw, r.NewLineStarts, r.NewLines, Path.Combine(a.ResultDir!, "new.idx"));
        }
        if (!a.Quiet) Console.Error.WriteLine();

        string Kind(InputFile f) => f.IsOffice || f.IsCompressed ? $"{f.KindName}・" : "";
        info.WriteLine($"旧 {r.OldLines:N0} 行（{Kind(io)}{r.OldEncoding}）／新 {r.NewLines:N0} 行（{Kind(inew)}{r.NewEncoding}）");
        if (r.Transcoded) info.WriteLine("  文字コードが違う（または UTF-16 の）ため、UTF-8 にそろえて比べた（差分の行も UTF-8）");
        if (io.IsOffice || inew.IsOffice) info.WriteLine("  Word・Excel・PDF は、取り出した文字を比べた（書式・図・数式は比べない）");
        info.WriteLine(r.Same ? "同じ" : $"削除 {r.DeletedLines:N0} 行／追加 {r.InsertedLines:N0} 行／変更の箇所 {r.Changes:N0}／同じ {r.EqualLines:N0} 行");
        if (r.Fallbacks > 0) info.WriteLine($"  目印が見つからず範囲ごと置き換えとして出した所: {r.Fallbacks:N0}（大きく入れ替わった所）");
        info.WriteLine($"時間: {r.Total.TotalSeconds:F2} 秒／目印を探した範囲の最大 {r.PeakWindowLines:N0} 行");

        if (a.JsonPath != null)
            File.WriteAllText(a.JsonPath, "{" +
                $"\"old\":{J(io.Path)},\"new\":{J(inew.Path)},\"mode\":\"text\",\"oldKind\":{J(io.KindName)},\"newKind\":{J(inew.KindName)}," +
                $"\"oldEncoding\":{J(r.OldEncoding)},\"newEncoding\":{J(r.NewEncoding)},\"transcoded\":{(r.Transcoded ? "true" : "false")}," +
                $"\"oldLines\":{r.OldLines},\"newLines\":{r.NewLines},\"equal\":{r.EqualLines},\"deleted\":{r.DeletedLines},\"inserted\":{r.InsertedLines}," +
                $"\"changes\":{r.Changes},\"hunks\":{hunks},\"fallbacks\":{r.Fallbacks},\"peakWindowLines\":{r.PeakWindowLines},\"totalSec\":{F(r.Total.TotalSeconds)}" + "}\n");
        return r.Same ? 0 : 1;
    }

    private static string? SaveIndex(InputFile f, bool raw, long[]? starts, long lines, string file)
    {
        if (!raw || starts == null || f.IsOffice || f.IsCompressed) return null;
        byte[] head = new byte[4];
        int n;
        using (var fs = new FileStream(f.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) n = fs.ReadAtLeast(head, 4, false);
        int bom = n >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0;
        LineIndex.FromStarts(f.Path, starts, lines, bom).Save(file);
        return file;
    }

    internal static TextDiffOptions Options(CliArgs a, Action<long, long>? progress) => Options(a, false, progress);

    internal static TextDiffOptions Options(CliArgs a, bool recordIndex, Action<long, long>? progress) => new()
    {
        RecordLineIndex = recordIndex,
        Compare = new LineCompare
        {
            IgnoreEol = !a.StrictEol,
            IgnoreSpaceChange = a.IgnoreSpaceChange,
            IgnoreAllSpace = a.IgnoreAllSpace,
            IgnoreCase = a.IgnoreCase,
        },
        Progress = progress,
    };

    internal static Func<string> TempPath(CliArgs a) => () => Path.Combine(a.TempDir ?? Path.GetTempPath(), $"uvd-hunk-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");

    /// <summary>フォルダの比較の明細用: 1 組を比べて unified diff を書く（要約は出さない）。違いがあれば true。</summary>
    internal static bool DiffQuiet(CliArgs a, InputFile io, InputFile inew, string outPath)
    {
        TextSource so = io.AsText(a.Format.OldEncoding, a.Raw), sn = inew.AsText(a.Format.NewEncoding, a.Raw);
        OutputGuard.Check(outPath, "明細", io.Path, inew.Path);
        using var output = OutputGuard.SafeFile.Create(outPath);
        using var w = new UnifiedDiffWriter(output, so, sn, a.Context, TempPath(a));
        bool differs = !new TextDiffEngine(Options(a, null)).Run(so, sn, w).Same;
        output.Commit();
        return differs;
    }

    /// <summary>2 つの sink に同じ差分を渡す。</summary>
    private sealed class TeeSink(ITextDiffSink x, ITextDiffSink y) : ITextDiffSink
    {
        public void Equal(TextDiffCursor a, TextDiffCursor b, int count) { x.Equal(a, b, count); y.Equal(a, b, count); }
        public void Change(TextDiffCursor a, int del, TextDiffCursor b, int ins) { x.Change(a, del, b, ins); y.Change(a, del, b, ins); }
        public void End(TextDiffCursor a, TextDiffCursor b) { x.End(a, b); y.End(a, b); }
    }

    /// <summary>差分を書かずに数えるだけ。</summary>
    private sealed class CountingSink : ITextDiffSink
    {
        public void Equal(TextDiffCursor a, TextDiffCursor b, int count) { }
        public void Change(TextDiffCursor a, int del, TextDiffCursor b, int ins) { }
        public void End(TextDiffCursor a, TextDiffCursor b) { }
    }
}
