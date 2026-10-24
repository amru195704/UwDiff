namespace UwDiff.Core.Diff;

/// <summary>キー照合の設定。</summary>
public sealed class KeyedDiffOptions
{
    public required string OldPath { get; init; }
    public required string NewPath { get; init; }

    /// <summary>差分の書き出し先（区分,キー列…,列,旧値,新値・UTF-8）。null なら書き出さない（件数だけ）。</summary>
    public string? OutPath { get; init; }

    /// <summary>キー列（1 列以上）。見出しの名前、または "#1" のような 1 始まりの列番号。複数なら組み合わせがキー。</summary>
    public required IReadOnlyList<string> KeyColumns { get; init; }

    /// <summary>文字コードを固定する（null なら自動判定）。</summary>
    public FileEncoding? OldEncoding { get; init; }
    public FileEncoding? NewEncoding { get; init; }

    /// <summary>区切り文字（null なら拡張子と見出しの行から決める。旧と新で違ってもよい）。</summary>
    public byte? Delimiter { get; init; }

    /// <summary>引用符（"）を CSV の決まりどおりに扱う（false なら引用符も普通の文字。引用符を使わない TSV 用）。</summary>
    public bool Quotes { get; init; } = true;

    /// <summary>1 回に読む大きさ（MB）。</summary>
    public int BlockSizeMB { get; init; } = 32;

    /// <summary>ファイルごとに同時に持つ読み込み用の塊の数。</summary>
    public int BlocksPerFile { get; init; } = 4;

    /// <summary>照合スレッドの数（0 なら CPU の数から決める）。</summary>
    public int Workers { get; init; }

    /// <summary>キーの振り分け先の数（0 なら照合スレッド × 8）。</summary>
    public int Shards { get; init; }

    /// <summary>
    /// 待ち行（相手がまだ来ていない行）に使ってよいメモリの上限（バイト）。超えたら待ち行を一時ファイルへ逃がす。
    /// 0 なら実メモリの 4 割。
    /// </summary>
    public long MemoryLimitBytes { get; init; }

    /// <summary>一時ファイルを置くフォルダ（null なら OS の一時フォルダ）。</summary>
    public string? TempDirectory { get; init; }

    /// <summary>キーの重複を正確に数える（キーのハッシュを記録して最後に並べる。メモリに持つのは上限まで、超えた分は一時ファイル）。</summary>
    public bool CheckDuplicates { get; init; } = true;

    /// <summary>2 本を同時に読む（false なら旧を読み終えてから新を読む。試験用）。</summary>
    public bool ReadConcurrently { get; init; } = true;

    /// <summary>試験用: 行に切るだけで照合しない（読み込みと行に切る速さを測る）。</summary>
    public bool ParseOnly { get; init; }

    /// <summary>進み具合を知らせる（読んだバイト数・全体のバイト数）。</summary>
    public Action<long, long>? Progress { get; init; }
}
