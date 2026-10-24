using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace UwDiff.Core.Diff;

/// <summary>
/// 2 本の表（CSV・TSV）をキー列で照合する。
/// 2 本を同時に先頭から 1 回だけ読み、行に切ったらすぐキーのハッシュで区画に振り分け、
/// 相手側の待ち行と突き合わせる。済んだ行は捨てるので、持つのは相手がまだ来ていない行だけ。
/// 旧と新が同じ文字コード（UTF-8・CP932・EUC-JP）ならバイト列のまま比べ、違えば両方を UTF-8 にそろえて比べる。
/// UTF-16 は区切りや改行がバイト単位で見分けられないので、同じ文字コードどうしでも UTF-8 に直して比べる。
/// 待ち行がメモリの上限を超えたら一時ファイルへ逃がす（止まらない）。
/// </summary>
public sealed class KeyedDiffEngine
{
    private const int Reserve = 4 << 20;      // 前の塊で切れた行の残りを写す場所（1 行の上限）
    private const int BatchSize = 4096;

    private readonly KeyedDiffOptions _o;

    // 照合スレッドから読む設定
    internal byte DelimOld, DelimNew;
    internal bool Quotes;
    internal bool SameLayout;
    internal CompareColumn[] Columns = Array.Empty<CompareColumn>();
    internal Encoding WorkEncoding = Encoding.UTF8;
    internal bool WorkIsUtf8 = true;
    internal bool CheckDuplicates;
    internal bool SingleKey;
    internal int[] KeyIndexOld = Array.Empty<int>();
    internal int[] KeyIndexNew = Array.Empty<int>();
    internal int Compactions;
    internal int SpilledShards;
    internal long SpillBytes;
    internal readonly MemoryMeter Meter = new();
    internal long MemoryLimitBytes;
    internal long EvictMinBytes;
    internal long ResolveLimitBytes;
    internal int KeyLogEntriesPerSide;

    internal int[] KeyIndexes(byte side) => side == 0 ? KeyIndexOld : KeyIndexNew;
    internal byte Delim(byte side) => side == 0 ? DelimOld : DelimNew;

    private Shard[] _shards = Array.Empty<Shard>();
    private Worker[] _workers = Array.Empty<Worker>();
    private readonly ConcurrentBag<RowRef[]> _batchPool = new();
    private CancellationTokenSource _cts = new();
    private Exception? _failure;
    private string? _tempDir;
    private int _tempSeq;
    private readonly object _tempLock = new();

    public KeyedDiffEngine(KeyedDiffOptions options) => _o = options;

    private sealed class Batch
    {
        public required Block Block;
        public required RowRef[] Rows;
        public int Count;
        public int Shard;
        public byte Side;
    }

    private sealed class Worker
    {
        public readonly BlockingCollection<Batch> Queue = new(256);
        public readonly DiffSink Sink;
        public readonly List<int> Shards = new();
        public long BusyTicks;
        public Worker(DiffSink sink) => Sink = sink;
    }

    private sealed class FileInfoX
    {
        public required CsvFile File;
        public required byte Side;
        public int[] KeyIndex = Array.Empty<int>();
        public Encoding? TranscodeFrom;
        public long Rows;
        public FileReader? Reader;
        public long BusyTicks;
    }

    /// <summary>一時ファイルの名前を作る（フォルダは最初に要るときに作り、終わったら消す）。</summary>
    internal string NewTempPath(string prefix)
    {
        lock (_tempLock)
        {
            if (_tempDir == null)
            {
                string root = _o.TempDirectory ?? Path.GetTempPath();
                _tempDir = Path.Combine(root, $"uvd-{Environment.ProcessId}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(_tempDir);
            }
            return Path.Combine(_tempDir, $"{prefix}-{++_tempSeq}.tmp");
        }
    }

    public KeyedDiffResult Run()
    {
        try
        {
            return RunCore();
        }
        finally
        {
            foreach (Worker w in _workers) w.Sink.Dispose();
            foreach (Shard s in _shards) { s.OldKeys.Dispose(); s.NewKeys.Dispose(); }
            if (_tempDir != null)
            {
                try { Directory.Delete(_tempDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    private KeyedDiffResult RunCore()
    {
        var res = new KeyedDiffResult();
        var total = Stopwatch.StartNew();
        CheckDuplicates = _o.CheckDuplicates;
        Quotes = _o.Quotes;
        if (_o.KeyColumns.Count == 0) throw new ArgumentException("キー列を 1 つ以上指定してください");

        var fo = new FileInfoX { File = CsvFile.Probe(_o.OldPath, _o.OldEncoding, _o.Delimiter, _o.Quotes), Side = 0 };
        var fn = new FileInfoX { File = CsvFile.Probe(_o.NewPath, _o.NewEncoding, _o.Delimiter, _o.Quotes), Side = 1 };
        DelimOld = fo.File.Delimiter;
        DelimNew = fn.File.Delimiter;

        // 同じ文字コード（バイト列で区切れるもの）ならそのまま、違えば（または UTF-16 なら）UTF-8 にそろえる
        FileEncoding eo = fo.File.Encoding, en = fn.File.Encoding;
        if (eo == en && TextEncodings.IsAsciiCompatible(eo))
        {
            WorkEncoding = TextEncodings.Get(eo);
            WorkIsUtf8 = eo == FileEncoding.Utf8;
        }
        else
        {
            if (eo != FileEncoding.Utf8) fo.TranscodeFrom = TextEncodings.Get(eo);
            if (en != FileEncoding.Utf8) fn.TranscodeFrom = TextEncodings.Get(en);
            res.Transcoded = true;
            WorkEncoding = new UTF8Encoding(false);
            WorkIsUtf8 = true;
        }

        // キーと列の対応（名前で）
        string[] ho = fo.File.Header, hn = fn.File.Header;
        fo.KeyIndex = _o.KeyColumns.Select(fo.File.FindColumn).ToArray();
        // 新のキー列は、旧で見つけた列の名前で探す（"#2" で指定しても、新で列の位置が違えば名前で合わせる）
        fn.KeyIndex = new int[fo.KeyIndex.Length];
        for (int k = 0; k < fo.KeyIndex.Length; k++)
        {
            int j = Array.IndexOf(hn, ho[fo.KeyIndex[k]]);
            fn.KeyIndex[k] = j >= 0 ? j : fn.File.FindColumn(_o.KeyColumns[k]);
        }
        KeyIndexOld = fo.KeyIndex;
        KeyIndexNew = fn.KeyIndex;
        SingleKey = KeyIndexOld.Length == 1;
        SameLayout = ho.SequenceEqual(hn) && DelimOld == DelimNew;
        var cols = new List<CompareColumn>();
        for (int i = 0; i < ho.Length; i++)
        {
            int j = Array.IndexOf(hn, ho[i]);
            cols.Add(new CompareColumn(i, j, Encoding.UTF8.GetBytes(ho[i])));
            if (j < 0) res.OldOnlyColumns.Add(ho[i]);
        }
        for (int j = 0; j < hn.Length; j++)
        {
            if (Array.IndexOf(ho, hn[j]) >= 0) continue;
            cols.Add(new CompareColumn(-1, j, Encoding.UTF8.GetBytes(hn[j])));
            res.NewOnlyColumns.Add(hn[j]);
        }
        Columns = cols.ToArray();

        int workers = _o.Workers > 0 ? _o.Workers : Math.Max(2, Environment.ProcessorCount - 2);
        int shards = _o.Shards > 0 ? _o.Shards : workers * 8;
        res.Workers = workers;
        res.Shards = shards;

        // メモリの上限: 待ち行（置き場と表）。キーの記録はその 1/8（512MB まで）をメモリに持ち、超えた分は一時ファイル
        long limit = _o.MemoryLimitBytes > 0 ? _o.MemoryLimitBytes : (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes * 0.4);
        res.MemoryLimitBytes = limit;
        MemoryLimitBytes = limit;
        EvictMinBytes = Math.Max(16L << 10, limit / (shards * 8L));
        ResolveLimitBytes = Math.Max(16L << 20, limit / workers);
        long keyLogBytes = Math.Min(512L << 20, limit / 8);
        KeyLogEntriesPerSide = (int)Math.Min(int.MaxValue / 2, keyLogBytes / 8 / (shards * 2L));

        _shards = new Shard[shards];
        _workers = new Worker[workers];
        for (int w = 0; w < workers; w++)
            _workers[w] = new Worker(new DiffSink(_o.OutPath != null, WorkEncoding, WorkIsUtf8, () => NewTempPath("out")));
        for (int s = 0; s < shards; s++)
        {
            _shards[s] = new Shard(this);
            _workers[s % workers].Shards.Add(s);
        }

        int payload = _o.BlockSizeMB << 20;
        long bytesO = fo.File.Length, bytesN = fn.File.Length;
        // 2 本を同時に読むときは歩調を合わせる（先に進みすぎた側は塊 2 つ分の差まで待つ）
        Pacer? pacer = _o.ReadConcurrently ? new Pacer(bytesO, bytesN, 2L * payload) : null;
        fo.Reader = new FileReader(fo.File.Path, new BlockPool(_o.BlocksPerFile, Reserve + payload), Reserve, payload, fo.TranscodeFrom, fo.File.BomLength, pacer, 0);
        fn.Reader = new FileReader(fn.File.Path, new BlockPool(_o.BlocksPerFile, Reserve + payload), Reserve, payload, fn.TranscodeFrom, fn.File.BomLength, pacer, 1);

        var match = Stopwatch.StartNew();
        long peakBytes = 0, peakRows = 0, peakManaged = 0;
        using var monitorStop = new ManualResetEventSlim(false);
        var monitor = new Thread(() =>
        {
            while (!monitorStop.Wait(50))
            {
                long b = 0, r = 0;
                foreach (Shard s in _shards) { b += s.PendingBytes; r += s.PendingRows; }
                peakBytes = Math.Max(peakBytes, b);
                peakRows = Math.Max(peakRows, r);
                peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
                _o.Progress?.Invoke(fo.Reader.BytesRead + fn.Reader.BytesRead, bytesO + bytesN);
            }
        }) { IsBackground = true, Name = "uvd-monitor" };
        monitor.Start();

        var workerThreads = new List<Thread>();
        foreach (Worker w in _workers)
        {
            var t = new Thread(() => WorkerLoop(w)) { IsBackground = true, Name = "uvd-match" };
            t.Start();
            workerThreads.Add(t);
        }

        int parsersLeft = 2;
        void ParserDone()
        {
            if (Interlocked.Decrement(ref parsersLeft) == 0)
                foreach (Worker w in _workers) w.Queue.CompleteAdding();
        }

        var oldBlocks = new BlockingCollection<Block>(2);
        var newBlocks = new BlockingCollection<Block>(2);
        var threads = new List<Thread>();
        Thread Start(string name, Action a)
        {
            var t = new Thread(() =>
            {
                try { a(); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Fail(ex); }
            }) { IsBackground = true, Name = name };
            t.Start();
            threads.Add(t);
            return t;
        }

        if (_o.ReadConcurrently)
        {
            Start("uvd-read-old", () => fo.Reader.Run(oldBlocks, _cts.Token));
            Start("uvd-read-new", () => fn.Reader.Run(newBlocks, _cts.Token));
        }
        else
        {
            Start("uvd-read", () =>
            {
                fo.Reader.Run(oldBlocks, _cts.Token);
                fn.Reader.Run(newBlocks, _cts.Token);
            });
        }
        Start("uvd-parse-old", () => { try { Parse(fo, oldBlocks); } finally { ParserDone(); } });
        Start("uvd-parse-new", () => { try { Parse(fn, newBlocks); } finally { ParserDone(); } });

        foreach (Thread t in threads) t.Join();
        foreach (Thread t in workerThreads) t.Join();
        match.Stop();
        if (_failure != null)
        {
            monitorStop.Set();
            monitor.Join();
            throw new InvalidOperationException("照合に失敗しました: " + _failure.Message, _failure);
        }

        // 読み終えた後: 逃がした区画の照合、残った待ち行（削除・追加）、重複の数え上げ
        var finish = Stopwatch.StartNew();
        var dupExamples = new ConcurrentBag<ulong>();
        long oldDup = 0, newDup = 0, keyLogDisk = 0;
        Parallel.ForEach(_workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, w =>
        {
            foreach (int s in w.Shards)
            {
                Shard sh = _shards[s];
                sh.Finish(w.Sink);
                if (CheckDuplicates)
                {
                    Interlocked.Add(ref keyLogDisk, sh.OldKeys.BytesOnDisk + sh.NewKeys.BytesOnDisk);
                    var ex = new List<ulong>();
                    ulong[] ok = sh.OldKeys.TakeAll(out int on);
                    Interlocked.Add(ref oldDup, Shard.CountDuplicates(ok, on, ex));
                    ok = Array.Empty<ulong>();
                    ulong[] nk = sh.NewKeys.TakeAll(out int nn);
                    Interlocked.Add(ref newDup, Shard.CountDuplicates(nk, nn, ex));
                    foreach (ulong x in ex) dupExamples.Add(x);
                }
            }
        });
        monitorStop.Set();
        monitor.Join();

        if (_o.OutPath != null)
        {
            using var outFs = new FileStream(_o.OutPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            var head = new ArrayBufferWriter<byte>();
            head.Write("区分"u8);
            foreach (int k in fo.KeyIndex)
            {
                head.Write(","u8);
                CsvBytes.WriteField(head, Encoding.UTF8.GetBytes(ho[k]), (byte)',');
            }
            head.Write(",列,旧値,新値\n"u8);
            outFs.Write(head.WrittenSpan);
            foreach (Worker w in _workers) w.Sink.CopyTo(outFs);
        }
        finish.Stop();
        Arena.ReleaseSpare();

        foreach (Worker w in _workers)
        {
            res.Added += w.Sink.Added;
            res.Deleted += w.Sink.Deleted;
            res.ChangedRows += w.Sink.ChangedRows;
            res.ChangedCells += w.Sink.ChangedCells;
            res.SameRows += w.Sink.SameRows;
        }
        res.OldRows = fo.Rows;
        res.NewRows = fn.Rows;
        res.OldBytes = bytesO;
        res.NewBytes = bytesN;
        res.OldEncoding = TextEncodings.Name(eo);
        res.NewEncoding = TextEncodings.Name(en);
        res.OldDelimiter = fo.File.DelimiterName;
        res.NewDelimiter = fn.File.DelimiterName;
        res.SameLayout = SameLayout;
        res.KeyColumnNames = fo.KeyIndex.Select(i => ho[i]).ToArray();
        res.OldDuplicateKeys = oldDup;
        res.NewDuplicateKeys = newDup;
        foreach (ulong h in dupExamples.Take(10)) res.DuplicateExamples.Add($"キーのハッシュ {h:X16}");
        res.PeakPendingBytes = peakBytes;
        res.PeakPendingRows = peakRows;
        res.PeakManagedBytes = Math.Max(peakManaged, GC.GetGCMemoryInfo().HeapSizeBytes);
        res.ReadAndMatch = match.Elapsed;
        res.ParseOldBusy = Stopwatch.GetElapsedTime(0, fo.BusyTicks);
        res.ParseNewBusy = Stopwatch.GetElapsedTime(0, fn.BusyTicks);
        res.MatchBusy = Stopwatch.GetElapsedTime(0, _workers.Sum(w => w.BusyTicks));
        res.Compactions = Compactions;
        res.SpilledShards = SpilledShards;
        res.SpillBytes = SpillBytes;
        res.KeyLogBytesOnDisk = keyLogDisk;
        res.Finish = finish.Elapsed;
        res.Total = total.Elapsed;
        return res;
    }

    private void Fail(Exception ex)
    {
        Interlocked.CompareExchange(ref _failure, ex, null);
        _cts.Cancel();
        foreach (Worker w in _workers)
        {
            try { w.Queue.CompleteAdding(); } catch { }
        }
    }

    /// <summary>塊を行に切り、キーと行のハッシュを付けて区画ごとの束にして照合スレッドへ渡す。</summary>
    private void Parse(FileInfoX f, BlockingCollection<Block> blocks)
    {
        byte delim = f.File.Delimiter;
        bool quotes = _o.Quotes;
        int[] keyIdx = f.KeyIndex;
        bool single = keyIdx.Length == 1;
        int key0 = keyIdx[0];
        // BOM はバイト列のまま読むときだけ行に切る側で飛ばす（UTF-8 に直すときは読む側で飛ばす）
        int skipBom = f.TranscodeFrom == null ? f.File.BomLength : 0;
        int nShards = _shards.Length;
        var pending = new RowRef[]?[nShards];
        var counts = new int[nShards];
        byte[] carry = new byte[Reserve];
        int carryLen = 0;
        bool header = true;
        bool first = true;
        long rows = 0;
        byte[] keyTmp = new byte[1024];

        foreach (Block b in blocks.GetConsumingEnumerable(_cts.Token))
        {
            long t0 = Stopwatch.GetTimestamp();
            int start = b.Start;
            if (first)
            {
                if (skipBom > 0 && b.End - start >= skipBom) start += skipBom;
                first = false;
            }
            if (carryLen > 0)
            {
                start -= carryLen;
                carry.AsSpan(0, carryLen).CopyTo(b.Data.AsSpan(start));
                carryLen = 0;
            }
            byte[] d = b.Data;
            int end = b.End;
            int pos = start;
            ReadOnlySpan<byte> all = d.AsSpan(0, end);
            while (pos < end)
            {
                int nl = CsvBytes.FindRowEnd(all, pos, quotes);
                int rowEnd;
                if (nl < 0)
                {
                    if (!b.Last) break;   // 次の塊へ持ち越す
                    rowEnd = end;
                }
                else rowEnd = nl;
                int len = rowEnd - pos;
                if (len > 0 && d[pos + len - 1] == (byte)'\r') len--;
                int rowStart = pos;
                pos = nl < 0 ? end : nl + 1;
                if (len == 0) continue;           // 空行は飛ばす
                if (header) { header = false; continue; }

                ReadOnlySpan<byte> row = d.AsSpan(rowStart, len);
                RowRef r;
                r.Start = rowStart;
                r.Length = len;
                if (single)
                {
                    if (CsvBytes.TryGetField(row, delim, key0, out CsvBytes.Field kf, quotes))
                    {
                        r.KeyStart = kf.Start;
                        r.KeyLength = kf.Length;
                        r.KeyQuoted = kf.Quoted;
                    }
                    else
                    {
                        r.KeyStart = len;
                        r.KeyLength = 0;
                        r.KeyQuoted = false;
                    }
                    ReadOnlySpan<byte> key = row.Slice(r.KeyStart, r.KeyLength);
                    if (r.KeyQuoted)
                    {
                        if (keyTmp.Length < key.Length) keyTmp = new byte[key.Length * 2];
                        int kl = CsvBytes.Unquote(key, true, keyTmp);
                        r.KeyHash = Hashing.Hash(keyTmp.AsSpan(0, kl));
                    }
                    else r.KeyHash = Hashing.Hash(key);
                }
                else
                {
                    // 複数列のキー: 列ごとの値（引用符を外したもの）を順につないだハッシュ。照合では列ごとに確かめる
                    r.KeyStart = 0xFFFF;
                    r.KeyLength = 0xFFFF;
                    r.KeyQuoted = false;
                    ulong h = 0;
                    for (int k = 0; k < keyIdx.Length; k++)
                    {
                        ReadOnlySpan<byte> v = default;
                        if (CsvBytes.TryGetField(row, delim, keyIdx[k], out CsvBytes.Field kf, quotes))
                        {
                            v = row.Slice(kf.Start, kf.Length);
                            if (kf.Quoted)
                            {
                                if (keyTmp.Length < v.Length) keyTmp = new byte[v.Length * 2];
                                v = keyTmp.AsSpan(0, CsvBytes.Unquote(v, true, keyTmp));
                            }
                        }
                        h = Hashing.Hash(v, h + (ulong)k);
                    }
                    r.KeyHash = h;
                }
                rows++;

                int sh = (int)((uint)(r.KeyHash >> 40) % (uint)nShards);
                RowRef[]? arr = pending[sh];
                if (arr == null)
                {
                    arr = _batchPool.TryTake(out RowRef[]? a) ? a : new RowRef[BatchSize];
                    pending[sh] = arr;
                }
                arr[counts[sh]++] = r;
                if (counts[sh] == BatchSize)
                {
                    Send(b, f.Side, sh, arr, counts[sh]);
                    pending[sh] = null;
                    counts[sh] = 0;
                }
            }
            // 塊の終わり: 残りの束をすべて渡す（束は 1 つの塊だけを指すようにする）
            for (int sh = 0; sh < nShards; sh++)
            {
                if (counts[sh] > 0)
                {
                    Send(b, f.Side, sh, pending[sh]!, counts[sh]);
                    pending[sh] = null;
                    counts[sh] = 0;
                }
            }
            // 切れた行の残りを持ち越す
            if (pos < end)
            {
                carryLen = end - pos;
                if (carryLen > Reserve) throw new InvalidDataException($"1 行が {Reserve >> 20}MB を超えています（{f.File.Path}）。引用符の閉じ忘れなら -quote off を試してください");
                d.AsSpan(pos, carryLen).CopyTo(carry);
            }
            b.Release();
            f.BusyTicks += Stopwatch.GetTimestamp() - t0;
        }
        f.Rows = rows;
    }

    private void Send(Block b, byte side, int shard, RowRef[] rows, int count)
    {
        if (_o.ParseOnly)
        {
            _batchPool.Add(rows);
            return;
        }
        b.AddRef();
        var batch = new Batch { Block = b, Rows = rows, Count = count, Shard = shard, Side = side };
        _workers[shard % _workers.Length].Queue.Add(batch, _cts.Token);
    }

    private void WorkerLoop(Worker w)
    {
        try
        {
            foreach (Batch batch in w.Queue.GetConsumingEnumerable())
            {
                long t0 = Stopwatch.GetTimestamp();
                Shard sh = _shards[batch.Shard];
                byte[] data = batch.Block.Data;
                RowRef[] rows = batch.Rows;
                for (int i = 0; i < batch.Count; i++) sh.Process(data, ref rows[i], batch.Side, w.Sink);
                batch.Block.Release();
                _batchPool.Add(rows);
                w.BusyTicks += Stopwatch.GetTimestamp() - t0;
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
}
