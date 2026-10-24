using System.Runtime.InteropServices;

namespace UwDiff.Core;

/// <summary>
/// 書き出しで元のファイルを壊さないための守り。
/// 1. 出力先が入力と同じファイルなら断る（リンクをたどった実際の場所で比べる。mac・Windows は大文字・小文字を区別しない）。
/// 2. 出力は同じフォルダの一時ファイルに書き、書き終えてから名前を替える（Commit）。見抜けない別名（ハードリンクなど）でも、
///    読んでいる途中の入力が切り詰められることはない。途中で失敗したら一時ファイルを消し、出力先は元のまま残る。
/// </summary>
public static class OutputGuard
{
    /// <summary>出力先 output が inputs のどれかと同じファイルなら ArgumentException。null・"-"（標準出力）は見ない。</summary>
    public static void Check(string? output, string what, params string?[] inputs)
    {
        if (string.IsNullOrEmpty(output) || output == "-") return;
        string o = RealPath(output);
        foreach (string? i in inputs)
        {
            if (string.IsNullOrEmpty(i)) continue;
            if (Same(o, RealPath(i)))
                throw new ArgumentException($"{what}の書き出し先が入力と同じファイルです（元のファイルを壊さないよう止めました）: {output}");
        }
    }

    private static bool Same(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsMacOS() || OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// リンクをたどった実際の場所。まだ無いファイルは、あるところまでの親フォルダをたどり、残りの名前を足す。
    /// </summary>
    public static string RealPath(string path)
    {
        string full = Path.GetFullPath(path);
        string rest = "";
        string cur = full;
        // 無いところは後ろに回し、あるところまで遡る
        while (!File.Exists(cur) && !Directory.Exists(cur))
        {
            string? parent = Path.GetDirectoryName(cur);
            if (parent == null || parent == cur) return full;
            rest = rest.Length == 0 ? Path.GetFileName(cur) : Path.Combine(Path.GetFileName(cur), rest);
            cur = parent;
        }
        string real = Resolve(cur);
        return rest.Length == 0 ? real : Path.Combine(real, rest);
    }

    private static string Resolve(string existing)
    {
        if (!OperatingSystem.IsWindows() && s_libc)
        {
            try
            {
                IntPtr p = realpath(existing, IntPtr.Zero);
                if (p != IntPtr.Zero)
                {
                    try { return Marshal.PtrToStringUTF8(p) ?? existing; }
                    finally { free(p); }
                }
                return existing;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                s_libc = false;   // libc を読めない環境: 下の .NET の仕組みでたどる
            }
        }
        // Windows（と libc を読めないとき）: リンク（シンボリックリンク・ジャンクション）をたどる。途中のフォルダのリンクも 1 段ずつ
        try
        {
            FileSystemInfo fi = File.Exists(existing) ? new FileInfo(existing) : new DirectoryInfo(existing);
            if (fi.ResolveLinkTarget(returnFinalTarget: true) is { } t) return Path.GetFullPath(t.FullName);
            string? parent = Path.GetDirectoryName(existing);
            return parent == null ? existing : Path.Combine(Resolve(parent), Path.GetFileName(existing));
        }
        catch (IOException) { return existing; }
    }

    private static bool s_libc = true;

    [DllImport("libc", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr realpath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr resolved);

    [DllImport("libc")]
    private static extern void free(IntPtr p);

    /// <summary>
    /// 一時ファイルに書いて、Commit で出力先に入れ替えるストリーム。Commit せずに閉じたら一時ファイルを消す（出力先は元のまま）。
    /// 出力先がリンクなら、リンクの先（実際のファイル）を入れ替える。
    /// </summary>
    public sealed class SafeFile : FileStream
    {
        private readonly string _target;
        private readonly string _temp;   // 閉じた後は Name を読めないので、覚えておく
        private bool _committed, _done;

        public string TempPath => _temp;

        private SafeFile(string temp, string target, int bufferSize)
            : base(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, bufferSize)
        {
            _target = target;
            _temp = temp;
        }

        public static SafeFile Create(string path, int bufferSize = 1 << 20)
        {
            string target = RealPath(path);
            string dir = Path.GetDirectoryName(target) ?? ".";
            Directory.CreateDirectory(dir);
            string temp = Path.Combine(dir, $".{Path.GetFileName(target)}.uvd-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}.tmp");
            return new SafeFile(temp, target, bufferSize);
        }

        /// <summary>書き終えた: 出力先に入れ替える（閉じてから名前を替える）。</summary>
        public void Commit()
        {
            Flush();
            _committed = true;
            Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing || _done) return;
            _done = true;   // 二度目の Dispose では何もしない
            if (_committed) File.Move(_temp, _target, overwrite: true);
            else if (File.Exists(_temp))
            {
                try { File.Delete(_temp); } catch (IOException) { }
            }
        }
    }
}
