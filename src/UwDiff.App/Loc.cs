using System.Runtime.InteropServices;
using System.Text.Json;

namespace UwDiff.App;

/// <summary>
/// 画面の言語（日本語・英語）と文言。設定の窓で切り替えると Changed が鳴り、開いている窓・タブが文言を入れ替える。
/// 選んだ言語は設定ファイルに覚える。初めて起動したときは OS の言語に合わせる（日本語でなければ英語）。
/// CLI（uvd）の出力は日本語のまま（画面の状態欄にそのまま出すことがある）。
/// </summary>
public static class Loc
{
    public static bool Ja { get; private set; } = true;
    public static event Action? Changed;

    /// <summary>文言（日本語, 英語）。</summary>
    private static readonly Dictionary<string, (string Ja, string En)> S = new()
    {
        // メニュー
        ["AppAbout"] = ("UwDiff について", "About UwDiff"),
        ["AppSettings"] = ("設定…", "Settings…"),
        ["FileMenu"] = ("ファイル", "File"),
        ["OpenOld"] = ("旧を開く…", "Open Old…"),
        ["OpenNew"] = ("新を開く…", "Open New…"),
        ["CompareMenu"] = ("比べる", "Compare"),
        ["CmdMenu"] = ("コマンドライン…", "Command Line…"),
        ["OpenResult"] = ("結果を開く…", "Open Result…"),
        ["CloseTab"] = ("閉じる", "Close"),
        ["CloseAll"] = ("すべて閉じる", "Close All"),
        ["Quit"] = ("終了", "Quit"),
        ["HelpMenu"] = ("ヘルプ", "Help"),
        ["HelpHowTo"] = ("使い方", "How to Use"),
        ["HelpNews"] = ("最新情報", "What's New"),
        ["HelpSupport"] = ("お問い合わせ", "Contact"),
        ["CliCommandMenu"] = ("コマンドライン設定…", "Command Line Setup…"),
        // 上の欄
        ["Old"] = ("旧", "Old"),
        ["New"] = ("新", "New"),
        ["OldPlaceholder"] = ("旧（ファイルかフォルダ。ここへ落としてもよい）", "Old (file or folder; you can drop it here)"),
        ["NewPlaceholder"] = ("新", "New"),
        ["PickFile"] = ("ファイル…", "File…"),
        ["PickFolder"] = ("フォルダ…", "Folder…"),
        ["KeyPlaceholder"] = ("キー列（表として比べるとき。例: 点名,種別）", "Key columns (to compare as tables, e.g. ID,Type)"),
        ["IgnoreSpaceChange"] = ("空白の量を無視", "Ignore whitespace amount"),
        ["IgnoreAllSpace"] = ("空白をすべて無視", "Ignore all whitespace"),
        ["IgnoreCase"] = ("大文字・小文字を無視", "Ignore case"),
        ["Unordered"] = ("順番を問わない", "Ignore order"),
        ["Compare"] = ("比べる", "Compare"),
        // ツールチップ
        ["TipOld"] = ("比べる元（旧）のファイルかフォルダ。窓へ 2 つ落とすと旧・新に入る", "The old file or folder. Drop two files on the window to fill both"),
        ["TipNew"] = ("比べる先（新）のファイルかフォルダ", "The new file or folder"),
        ["TipPickFile"] = ("ファイルを選ぶ（テキスト・CSV・TSV・圧縮・Word・Excel・PDF）", "Choose a file (text, CSV, TSV, compressed, Word, Excel, PDF)"),
        ["TipPickFolder"] = ("フォルダを選ぶ（フォルダどうしを比べる）", "Choose a folder (compare two folders)"),
        ["TipKey"] = ("入れると表（CSV・TSV・Excel）としてキー列で照合する。行の順番が違ってもよい。複数はカンマで。空ならテキストとして比べる", "Compare as tables (CSV, TSV, Excel) by these key columns; row order may differ. Separate several with commas. Leave empty to compare as text"),
        ["TipIgnoreSpaceChange"] = ("テキスト: 続いた空白を 1 つとみなし、行頭・行末の空白を無視する（-b）", "Text: treat runs of spaces as one and ignore leading/trailing spaces (-b)"),
        ["TipIgnoreAllSpace"] = ("テキスト: 空白・タブをすべて無視する（-w）", "Text: ignore all spaces and tabs (-w)"),
        ["TipIgnoreCase"] = ("テキスト: 英字の大文字・小文字を区別しない（-i）", "Text: ignore ASCII case (-i)"),
        ["TipUnordered"] = ("行の順番を見ずに、増えた行・減った行だけを比べる（同じ行の数まで。-unordered）", "Compare which lines were added or removed, regardless of order (-unordered)"),
        ["TipCompare"] = ("旧と新を比べて、結果を新しいタブで開く", "Compare old and new and open the result in a new tab"),
        ["TipCloseTab"] = ("このタブを閉じる", "Close this tab"),
        // テキストの結果
        ["PrevChange"] = ("◀ 前の変更", "◀ Previous"),
        ["NextChange"] = ("次の変更 ▶", "Next ▶"),
        ["ChangesOnly"] = ("変更の所だけ", "Changes only"),
        ["TipPrevChange"] = ("前の変更の所へ（Shift+F7）", "Go to the previous change (Shift+F7)"),
        ["TipNextChange"] = ("次の変更の所へ（F7）", "Go to the next change (F7)"),
        ["TipChangesOnly"] = ("同じ行が続く所を、前後 3 行だけ残して畳む", "Fold unchanged runs, keeping 3 lines of context"),
        ["Indexing"] = ("行の位置を調べています…", "Indexing lines…"),
        ["CannotRead"] = ("読めません: {0}", "Cannot read: {0}"),
        ["TextSame"] = ("同じ（{0:N0} 行）", "Same ({0:N0} lines)"),
        ["TextSummary"] = ("変更 {0:N0} か所・削除 {1:N0} 行・追加 {2:N0} 行（旧 {3:N0} 行／新 {4:N0} 行）", "{0:N0} change(s), {1:N0} deleted, {2:N0} added (old {3:N0} lines / new {4:N0} lines)"),
        ["FoldRow"] = ("…… 同じ行 {0:N0} 行 ……", "…… {0:N0} unchanged lines ……"),
        ["LongLine"] = (" …（長い行の残りは省略）", " … (rest of the long line omitted)"),
        // 表の結果
        ["Kind"] = ("区分", "Kind"),
        ["All"] = ("すべて", "All"),
        ["Added"] = ("追加", "Added"),
        ["Deleted"] = ("削除", "Deleted"),
        ["Changed"] = ("変更", "Changed"),
        ["SearchPlaceholder"] = ("キー・値で探す（Enter）", "Search keys / values (Enter)"),
        ["TakeAll"] = ("すべて採る", "Take All"),
        ["TakeNone"] = ("すべて外す", "Take None"),
        ["Apply"] = ("採った変更を反映…", "Apply Taken Changes…"),
        ["Take"] = ("採る", "Take"),
        ["TipKind"] = ("区分で絞り込む", "Filter by kind"),
        ["TipSearch"] = ("キーや値に、この文字を含む行だけを出す（Enter で探す）", "Show only rows whose keys or values contain this text (press Enter)"),
        ["TipTakeAll"] = ("表示している行をすべて採る", "Take all rows shown"),
        ["TipTakeNone"] = ("表示している行をすべて外す", "Untake all rows shown"),
        ["TipApply"] = ("採った変更だけを旧に当てた新しいファイルを書き出す（元のファイルは変えない。旧の文字コード・区切り・改行を保つ）", "Write a new file = old file with only the taken changes applied (originals are not modified; encoding, delimiter and line endings are kept)"),
        ["TipApplyDisabled"] = ("Excel のシート・順番を問わない比較は、差分の一覧まで（反映はできない）", "Excel sheets and order-independent results can be listed but not applied"),
        ["TipTakeRow"] = ("この変更を採る（反映する）", "Take (apply) this change"),
        ["Reading"] = ("読んでいます…", "Reading…"),
        ["TableSummary"] = ("追加 {0:N0}・削除 {1:N0}・変更 {2:N0} セル／表示 {3:N0}・採る {4:N0}", "{0:N0} added, {1:N0} deleted, {2:N0} changed cells / {3:N0} shown, {4:N0} taken"),
        ["SaveApplied"] = ("反映したファイルの保存先", "Save the applied file as"),
        ["AppliedSuffix"] = ("_反映", "_applied"),
        ["Applied"] = ("反映しました: {0}", "Applied: {0}"),
        ["AppliedPartly"] = ("反映しました（反映しなかったものがあります）: {0}\n{1}", "Applied (some changes were not applied): {0}\n{1}"),
        ["ApplyFailed"] = ("反映できません: {0}", "Could not apply: {0}"),
        // フォルダの結果
        ["DiffsOnly"] = ("違いだけ", "Differences"),
        ["Different"] = ("違う", "Different"),
        ["OnlyOld"] = ("旧だけ", "Only old"),
        ["OnlyNew"] = ("新だけ", "Only new"),
        ["Same"] = ("同じ", "Same"),
        ["Path"] = ("パス", "Path"),
        ["OldSize"] = ("旧の大きさ", "Old size"),
        ["NewSize"] = ("新の大きさ", "New size"),
        ["OldTime"] = ("旧の更新日時", "Old modified"),
        ["NewTime"] = ("新の更新日時", "New modified"),
        ["FolderHint"] = ("（「違う」の行をダブルクリックで差分を開く）", "(double-click a \"Different\" row to open its diff)"),
        ["FolderSummary"] = ("旧だけ {0:N0}・新だけ {1:N0}・違う {2:N0}・同じ {3:N0}", "{0:N0} only old, {1:N0} only new, {2:N0} different, {3:N0} same"),
        ["TipFolderKind"] = ("区分で絞り込む（既定は違いだけ）", "Filter by kind (default: differences only)"),
        // 状態・知らせ
        ["NeedBoth"] = ("旧と新を選んでください", "Choose both old and new"),
        ["Comparing"] = ("比べています…", "Comparing…"),
        ["CannotCompare"] = ("比べられません: {0}", "Cannot compare: {0}"),
        ["CannotLoadResult"] = ("結果を読めません: {0}", "Cannot read the result: {0}"),
        ["ResultOpened"] = ("{0} を開きました", "Opened {0}"),
        ["OpenResultTitle"] = ("結果の置き場所（uvd -result・-open が書いたフォルダ）", "Result folder (written by uvd -result / -open)"),
        ["Ok"] = ("OK", "OK"),
        ["Cancel"] = ("やめる", "Cancel"),
        ["Close"] = ("閉じる", "Close"),
        // 設定
        ["SettingsTitle"] = ("設定", "Settings"),
        ["Language"] = ("言語", "Language"),
        ["LangJa"] = ("日本語", "日本語 (Japanese)"),
        ["LangEn"] = ("English", "English"),
        ["TipLanguage"] = ("画面の言語を切り替える（CLI の出力は日本語のまま）", "Switch the display language (CLI output stays Japanese)"),
        // について
        ["AboutText"] = ("どんな形式・文字コード・OS でも、多数・巨大ファイルを高速に比べる無料の道具。", "A free tool that compares many and huge files fast — any format, any encoding, any OS."),
        ["License"] = ("ライセンス", "License"),
        ["AboutNews"] = ("最新情報（リリース）", "What's new (releases)"),
        ["AboutHowTo"] = ("使い方（操作マニュアル）", "How to use (user manual)"),
        ["TipLangBox"] = ("画面の言語（日本語・English）。設定… でも切り替えられる", "Display language (Japanese / English). Also in Settings…"),
        // コマンドライン
        ["CmdTitle"] = ("コマンドライン（uvd）", "Command Line (uvd)"),
        ["CmdHelp"] = ("uvd に渡す引数を入れて「実行」。比べた結果は新しいタブで開く（uvd -open と同じ）。例: 旧.csv 新.csv -key 点名／apply 旧.csv 新.csv 差分.csv -o 結果.csv", "Enter the arguments for uvd and click Run. Comparison results open in a new tab (same as uvd -open). e.g. old.csv new.csv -key ID / apply old.csv new.csv diff.csv -o out.csv"),
        ["Run"] = ("実行", "Run"),
        ["TipRun"] = ("uvd を実行する（Enter）", "Run uvd (Enter)"),
        ["TipCmdArgs"] = ("uvd の引数（先頭の uvd は無くてもよい）。空白を含むパスは \" で囲む", "Arguments for uvd (the leading \"uvd\" is optional). Quote paths with spaces"),
        ["CmdHelpButton"] = ("オプション一覧", "Options"),
        ["TipCmdHelpButton"] = ("uvd -h の出力を見る", "Show the output of uvd -h"),
        // コマンドライン設定
        ["CliMissing"] = ("この版には uvd が入っていません。配布版（dmg / zip / tar.gz）のアプリで実行してください。", "This build does not include uvd. Run this from the released app (dmg / zip / tar.gz)."),
        ["CliNotInPlace"] = ("ディスクイメージ（dmg）から直接起動しています。\nアプリを「アプリケーション」フォルダへ移し、そこから起動してからもう一度実行してください。", "The app is running directly from the disk image (dmg).\nMove it to the Applications folder, launch it from there, and try again."),
        ["CliInstalled"] = ("uvd はコマンドラインで使えます。\n\n{0}\n\nアプリを入れ替えたあとなどは「登録し直す」を選んでください。\n使うのをやめるときは「解除する」を選んでください。", "uvd is available on the command line.\n\n{0}\n\nAfter replacing the app, choose \"Re-install\". To stop using it, choose \"Remove\"."),
        ["CliReinstall"] = ("登録し直す", "Re-install"),
        ["CliRemove"] = ("解除する", "Remove"),
        ["CliOther"] = ("uvd は既に別の場所で登録されています:\n  {0}\n\nこのアプリの uvd に置き換えますか？\n\n{1}", "uvd is already registered from another place:\n  {0}\n\nReplace it with this app's uvd?\n\n{1}"),
        ["CliReplace"] = ("置き換える", "Replace"),
        ["CliAsk"] = ("コンソールで uvd と打つだけで使えるようにします。\n\n{0}", "Make uvd available by name in a terminal.\n\n{0}"),
        ["CliDoInstall"] = ("使えるようにする", "Install"),
        ["CliPlanWin"] = ("ユーザーの環境変数 PATH に次のフォルダを追加します（管理者権限は要りません）:\n  {0}", "This adds the following folder to your user PATH (no administrator rights needed):\n  {0}"),
        ["CliPlanLink"] = ("次のリンクを作ります:\n  {0} → {1}\n\n管理者のパスワードを聞かれることがあります。", "This creates the link:\n  {0} → {1}\n\nYou may be asked for an administrator password."),
        ["CliFailed"] = ("できませんでした。\n{0}", "Failed.\n{0}"),
        ["CliRemoved"] = ("uvd の登録を解除しました。", "Removed uvd."),
        ["CliDone"] = ("uvd を使えるようにしました（{0}）。\n新しく開いたターミナルから使えます。\n\n  uvd -open\n  uvd 旧 新 -open\n  uvd 旧.csv 新.csv -key 点名 -o 差分.csv", "uvd is installed ({0}).\nOpen a new terminal window to use it.\n\n  uvd -open\n  uvd old new -open\n  uvd old.csv new.csv -key ID -o diff.csv"),
    };

    public static string T(string key) => S.TryGetValue(key, out var v) ? (Ja ? v.Ja : v.En) : key;
    public static string F(string key, params object?[] args) => string.Format(System.Globalization.CultureInfo.InvariantCulture, T(key), args);

    /// <summary>差分・一覧の区分（データは日本語）を、いまの言語の表示に。</summary>
    public static string Kind(string kind) => Ja ? kind : kind switch
    {
        "追加" => "Added",
        "削除" => "Deleted",
        "変更" => "Changed",
        "旧だけ" => "Only old",
        "新だけ" => "Only new",
        "違う" => "Different",
        "同じ" => "Same",
        _ => kind,
    };

    public static void Set(bool ja)
    {
        if (Ja == ja) return;
        Ja = ja;
        Save();
        Changed?.Invoke();
    }

    // ---- 設定ファイル ----
    private sealed class Settings
    {
        public string? Language { get; set; }
    }

    public static string SettingsFolder => Environment.GetEnvironmentVariable("UWDIFF_SETTINGS") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UwDiff");

    private static string SettingsFile => Path.Combine(SettingsFolder, "settings.json");

    /// <summary>起動時に一度: 覚えた言語、無ければ OS の言語。</summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsFile) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile)) is { Language: { } l })
            {
                Ja = l == "ja";
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        Ja = OsIsJapanese();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new Settings { Language = Ja ? "ja" : "en" }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// OS の言語が日本語か。InvariantGlobalization なので CultureInfo では分からない:
    /// mac は AppleLanguages、Windows は GetUserDefaultUILanguage、Linux は LANG などを見る。
    /// </summary>
    private static bool OsIsJapanese()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return (GetUserDefaultUILanguage() & 0x3FF) == 0x11;
            if (OperatingSystem.IsMacOS())
            {
                var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/defaults", "read -g AppleLanguages") { RedirectStandardOutput = true, UseShellExecute = false };
                using var p = System.Diagnostics.Process.Start(psi)!;
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(2000);
                // 先頭の言語（"(\n    \"ja-JP\",\n …"）
                int q = o.IndexOf('"');
                return q >= 0 && o.AsSpan(q + 1).StartsWith("ja");
            }
            string lang = Environment.GetEnvironmentVariable("LC_ALL") is { Length: > 0 } a ? a
                : Environment.GetEnvironmentVariable("LC_MESSAGES") is { Length: > 0 } m ? m
                : Environment.GetEnvironmentVariable("LANG") ?? "";
            return lang.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or EntryPointNotFoundException or DllNotFoundException)
        {
            return true;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();
}

/// <summary>ヘルプのリンク先（英語の画面では英語の文書を開く）。</summary>
public static class Links
{
    public const string Repo = "https://github.com/amru195704/UwDiff";
    public static string HowTo => Loc.Ja ? Repo + "/blob/main/docs/manual.ja.md" : Repo + "/blob/main/docs/manual.en.md";
    public static string News => Repo + "/releases";
    public static string Support => Repo + "/issues";
    public static string RepoHome => Loc.Ja ? Repo : Repo + "/blob/main/README.en.md";
    public static string Profile => Loc.Ja ? "https://github.com/amru195704" : "https://github.com/amru195704/amru195704/blob/main/README.en.md";
    public static string LicenseFile => Loc.Ja ? Repo + "/blob/main/LICENSEjp.txt" : Repo + "/blob/main/LICENSE";

    public static void Open(string url)
    {
        try
        {
            if (OperatingSystem.IsMacOS()) System.Diagnostics.Process.Start("open", url);
            else if (OperatingSystem.IsWindows()) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            else System.Diagnostics.Process.Start("xdg-open", url);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
