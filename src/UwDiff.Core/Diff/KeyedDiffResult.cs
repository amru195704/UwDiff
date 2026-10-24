namespace UwDiff.Core.Diff;

/// <summary>キー照合の結果の要約と、測った時間・メモリ。</summary>
public sealed class KeyedDiffResult
{
    public long OldRows { get; set; }
    public long NewRows { get; set; }
    public long Added { get; set; }
    public long Deleted { get; set; }
    /// <summary>変わった行の数。</summary>
    public long ChangedRows { get; set; }
    /// <summary>変わったセルの数（出力の「変更」の行数）。</summary>
    public long ChangedCells { get; set; }
    /// <summary>キーが同じで中身も同じ行の数。</summary>
    public long SameRows { get; set; }

    /// <summary>キーが重複している値の数（旧・新それぞれ）。重複の数え方は CheckDuplicates が true のときだけ正確。</summary>
    public long OldDuplicateKeys { get; set; }
    public long NewDuplicateKeys { get; set; }
    public List<string> DuplicateExamples { get; } = new();

    public string OldEncoding { get; set; } = "";
    public string NewEncoding { get; set; } = "";
    public bool Transcoded { get; set; }
    public bool SameLayout { get; set; }
    public string OldDelimiter { get; set; } = "";
    public string NewDelimiter { get; set; } = "";
    /// <summary>キー列の名前（旧の見出しの名前）。</summary>
    public string[] KeyColumnNames { get; set; } = Array.Empty<string>();
    /// <summary>旧にしか無い列・新にしか無い列（相手側は空として比べた）。</summary>
    public List<string> OldOnlyColumns { get; } = new();
    public List<string> NewOnlyColumns { get; } = new();

    public long OldBytes { get; set; }
    public long NewBytes { get; set; }

    public TimeSpan Total { get; set; }
    public TimeSpan ReadAndMatch { get; set; }
    public TimeSpan Finish { get; set; }

    /// <summary>待ち行（相手がまだ来ていない行）の中身の最大バイト数。</summary>
    public long PeakPendingBytes { get; set; }
    /// <summary>待ち行の最大行数。</summary>
    public long PeakPendingRows { get; set; }
    /// <summary>.NET が確保していたメモリの最大（GC の見立て）。</summary>
    public long PeakManagedBytes { get; set; }

    /// <summary>行に切る処理に使った時間（旧・新。読み込み待ちを除く）。</summary>
    public TimeSpan ParseOldBusy { get; set; }
    public TimeSpan ParseNewBusy { get; set; }
    /// <summary>照合スレッドが実際に働いた時間の合計（待ちを除く）。</summary>
    public TimeSpan MatchBusy { get; set; }
    /// <summary>詰め直しの回数。</summary>
    public int Compactions { get; set; }

    /// <summary>待ち行のメモリの上限（バイト）。</summary>
    public long MemoryLimitBytes { get; set; }
    /// <summary>待ち行を一時ファイルへ逃がした区画の数と、逃がした量（バイト）。</summary>
    public int SpilledShards { get; set; }
    public long SpillBytes { get; set; }
    /// <summary>キーの記録のうち一時ファイルへ出した量（バイト）。</summary>
    public long KeyLogBytesOnDisk { get; set; }

    public int Workers { get; set; }
    public int Shards { get; set; }
}
