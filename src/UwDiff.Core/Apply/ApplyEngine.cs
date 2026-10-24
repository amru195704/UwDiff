using System.Buffers;
using System.Text;

namespace UwDiff.Core.Apply;

/// <summary>反映の設定。</summary>
public sealed class ApplyOptions
{
    public required string OldPath { get; init; }
    public required string NewPath { get; init; }
    /// <summary>uvd の差分（区分,キー列…,列,旧値,新値）。採らない行を消したもの。Excel で保存し直した CP932・タブ区切りでもよい。</summary>
    public required string DiffPath { get; init; }
    public required string OutPath { get; init; }
    public FileEncoding? OldEncoding { get; init; }
    public FileEncoding? NewEncoding { get; init; }
    public byte? Delimiter { get; init; }
    public bool Quotes { get; init; } = true;
}

/// <summary>反映の結果。</summary>
public sealed class ApplyResult
{
    public string[] KeyColumnNames { get; set; } = Array.Empty<string>();
    public string OldEncoding { get; set; } = "";
    public string OldDelimiter { get; set; } = "";
    public long DiffAdded, DiffDeleted, DiffChanged;
    public long OldRows, KeptRows, DeletedRows, ChangedRows, ChangedCells, AddedRows;
    /// <summary>旧値が差分の旧値と合わず、反映しなかったセルの数。</summary>
    public long Conflicts;
    /// <summary>新の値が旧の文字コードで書けず、反映しなかったセル・行の数。</summary>
    public long Unencodable;
    /// <summary>差分のキーが旧（削除・変更）か新（追加）に見つからなかった数。</summary>
    public long MissingKeys;
    /// <summary>旧に無い列への変更（反映しない）の数。</summary>
    public long UnknownColumns;
    public List<string> Problems { get; } = new();
    public TimeSpan Total { get; set; }
}

/// <summary>
/// 差分（uvd の出力から採る行だけを残したもの）を旧のファイルに反映して、新しいファイルを書き出す。元のファイルは変えない。
/// - 旧の行の順番・改行・引用符・文字コード・BOM・区切りを保つ。変えない行はバイト列のまま写す。
/// - 削除: そのキーの行を書かない。変更: そのセルだけを新値にする（今の値が差分の旧値と同じときだけ）。
/// - 追加: 新のファイルからそのキーの行を取り、旧の列の並び・文字コード・区切りに直して末尾に足す。
/// - キーが重複していれば、同じキーの行すべてに当てる。
/// 旧・新は 1 回ずつ順に読むだけ。メモリに持つのは差分だけ。
/// </summary>
public sealed class ApplyEngine
{
    private readonly ApplyOptions _o;

    public ApplyEngine(ApplyOptions o) => _o = o;

    private sealed class Entry
    {
        public required byte[][] Key;     // キーの値（照合に使う側の文字コードのバイト列）
        public required string KeyText;
        public Entry? Next;               // ハッシュが同じ別のキー
        public bool Delete;
        public bool Found;
        public List<Change>? Changes;
    }

    private sealed record Change(int Column, string Name, byte[] OldValue, byte[]? NewValue, string OldText, string NewText);

    private sealed class Lookup
    {
        private readonly Dictionary<ulong, Entry> _map = new();
        public IEnumerable<Entry> All => _map.Values.SelectMany(e => Chain(e));
        public int Count => _map.Count;

        private static IEnumerable<Entry> Chain(Entry? e)
        {
            for (; e != null; e = e.Next) yield return e;
        }

        public Entry GetOrAdd(byte[][] key, string text)
        {
            ulong h = HashKey(key);
            if (_map.TryGetValue(h, out Entry? e))
            {
                for (Entry? x = e; x != null; x = x.Next)
                    if (SameKey(x.Key, key)) return x;
            }
            var n = new Entry { Key = key, KeyText = text, Next = e };
            _map[h] = n;
            return n;
        }

        public Entry? Find(ulong h, ReadOnlySpan<byte> row, byte delim, int[] keyIdx, bool quotes, byte[] tmp, Cp932Canon? canon)
        {
            if (!_map.TryGetValue(h, out Entry? e)) return null;
            for (; e != null; e = e.Next)
            {
                bool ok = true;
                for (int k = 0; k < keyIdx.Length && ok; k++)
                    ok = FieldValue(row, delim, keyIdx[k], quotes, tmp, canon).SequenceEqual(e.Key[k]);
                if (ok) return e;
            }
            return null;
        }

        private static bool SameKey(byte[][] a, byte[][] b)
        {
            for (int k = 0; k < a.Length; k++) if (!a[k].AsSpan().SequenceEqual(b[k])) return false;
            return true;
        }
    }

    /// <summary>キーのハッシュ（照合エンジンと同じ考え方: 列ごとの値を順につなぐ）。</summary>
    private static ulong HashKey(byte[][] key)
    {
        if (key.Length == 1) return Hashing.Hash(key[0]);
        ulong h = 0;
        for (int k = 0; k < key.Length; k++) h = Hashing.Hash(key[k], h + (ulong)k);
        return h;
    }

    private static ulong HashRowKey(ReadOnlySpan<byte> row, byte delim, int[] keyIdx, bool quotes, byte[] tmp, Cp932Canon? canon)
    {
        if (keyIdx.Length == 1) return Hashing.Hash(FieldValue(row, delim, keyIdx[0], quotes, tmp, canon));
        ulong h = 0;
        for (int k = 0; k < keyIdx.Length; k++) h = Hashing.Hash(FieldValue(row, delim, keyIdx[k], quotes, tmp, canon), h + (ulong)k);
        return h;
    }

    /// <summary>行の index 番目の列の値（引用符を外したもの。tmp を使う）。無ければ空。CP932 なら同じ文字の符号を 1 つにそろえる。</summary>
    private static ReadOnlySpan<byte> FieldValue(ReadOnlySpan<byte> row, byte delim, int index, bool quotes, byte[] tmp, Cp932Canon? canon)
    {
        if (!CsvBytes.TryGetField(row, delim, index, out CsvBytes.Field f, quotes)) return default;
        ReadOnlySpan<byte> v = row.Slice(f.Start, f.Length);
        if (f.Quoted) v = tmp.AsSpan(0, CsvBytes.Unquote(v, true, tmp));
        return canon == null ? v : canon.Apply(v);
    }

    /// <summary>
    /// CP932 では同じ文字に符号が 2 つあるものがある（NEC 選定 IBM 拡張 0xED・0xEE と IBM 拡張 0xFA〜0xFC、NEC 特殊の一部 0x87 と 0x81）。
    /// 差分の文字（.NET で符号にしたもの）とファイルのバイト列を突き合わせるため、該当する符号を含む値だけ、いったん文字に直して符号にし直す。
    /// </summary>
    private sealed class Cp932Canon(Encoding enc)
    {
        private char[] _chars = new char[256];
        private byte[] _buf = new byte[512];

        public ReadOnlySpan<byte> Apply(ReadOnlySpan<byte> v)
        {
            if (!Needs(v)) return v;
            if (_chars.Length < v.Length) _chars = new char[v.Length * 2];
            int nc = enc.GetChars(v, _chars);
            int max = enc.GetMaxByteCount(nc);
            if (_buf.Length < max) _buf = new byte[max * 2];
            return _buf.AsSpan(0, enc.GetBytes(_chars.AsSpan(0, nc), _buf));
        }

        private static bool Needs(ReadOnlySpan<byte> v)
        {
            for (int i = 0; i < v.Length; i++)
            {
                byte b = v[i];
                if (b < 0x80 || (b >= 0xA1 && b <= 0xDF)) continue;
                if (b == 0x87 || b == 0xED || b == 0xEE || b >= 0xFA) return true;
                i++;
            }
            return false;
        }
    }

    /// <summary>
    /// 行を書く文字コード（UTF-16 は UTF-8 に直して扱う）。書けない文字は例外にする。
    /// 復号は既定のまま（Encoding.GetEncoding(932, …) でフォールバックを指定すると、NEC 選定 IBM 拡張 0xED・0xEE などが ? になるため、複製して符号化の側だけ変える）。
    /// </summary>
    private static Encoding WorkEncoding(FileEncoding e)
    {
        var enc = (Encoding)(TextEncodings.IsAsciiCompatible(e) ? TextEncodings.Get(e) : new UTF8Encoding(false)).Clone();
        enc.EncoderFallback = EncoderFallback.ExceptionFallback;
        return enc;
    }

    public ApplyResult Run()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = new ApplyResult();
        if (Path.GetFullPath(_o.OutPath) == Path.GetFullPath(_o.OldPath) || Path.GetFullPath(_o.OutPath) == Path.GetFullPath(_o.NewPath))
            throw new ArgumentException("書き出し先が旧か新と同じです（元のファイルは変えません。別の名前にしてください）");

        CsvFile fo = CsvFile.Probe(_o.OldPath, _o.OldEncoding, _o.Delimiter, _o.Quotes);
        CsvFile fn = CsvFile.Probe(_o.NewPath, _o.NewEncoding, _o.Delimiter, _o.Quotes);
        Encoding encO = WorkEncoding(fo.Encoding), encN = WorkEncoding(fn.Encoding);
        Cp932Canon? canonO = fo.Encoding == FileEncoding.Cp932 ? new Cp932Canon(encO) : null;
        Cp932Canon? canonN = fn.Encoding == FileEncoding.Cp932 ? new Cp932Canon(encN) : null;
        res.OldEncoding = TextEncodings.Name(fo.Encoding);
        res.OldDelimiter = fo.DelimiterName;

        // 差分を読む
        var changes = new Lookup();   // 削除・変更（旧の文字コードのキー）
        var adds = new Lookup();      // 追加（新の文字コードのキー）
        string[] keyNames = ReadDiff(fo, encO, encN, changes, adds, res);
        res.KeyColumnNames = keyNames;
        int[] keyO = keyNames.Select(fo.FindColumn).ToArray();
        int[]? keyN = res.DiffAdded > 0 ? keyNames.Select(fn.FindColumn).ToArray() : null;

        byte[] tmp = new byte[1 << 16];
        using (var outFs = new FileStream(_o.OutPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            outFs.Write(TextEncodings.Bom(fo.Encoding, fo.BomLength));
            using Stream output = TextEncodings.IsAsciiCompatible(fo.Encoding)
                ? new NonClosingStream(outFs)
                : Encoding.CreateTranscodingStream(outFs, TextEncodings.Get(fo.Encoding), new UTF8Encoding(false), leaveOpen: true);
            var w = new ArrayBufferWriter<byte>(1 << 20);
            void FlushIfBig()
            {
                if (w.WrittenCount < (4 << 20)) return;
                output.Write(w.WrittenSpan);
                w.Clear();
            }

            // 1) 旧を順に読み、削除・変更を当てる
            bool endsWithNewline = true;
            var fields = new List<CsvBytes.Field>();
            using (Stream src = RowReader.OpenWork(fo))
            {
                var rr = new RowReader(src, _o.Quotes);
                bool header = true;
                while (rr.Next(out ReadOnlySpan<byte> row, out ReadOnlySpan<byte> nl))
                {
                    endsWithNewline = nl.Length > 0;
                    if (header || row.Length == 0)
                    {
                        if (row.Length > 0) header = false;
                        w.Write(row); w.Write(nl);
                        FlushIfBig();
                        continue;
                    }
                    res.OldRows++;
                    if (tmp.Length < row.Length) tmp = new byte[row.Length * 2];
                    Entry? e = changes.Count == 0 ? null : changes.Find(HashRowKey(row, fo.Delimiter, keyO, _o.Quotes, tmp, canonO), row, fo.Delimiter, keyO, _o.Quotes, tmp, canonO);
                    if (e == null)
                    {
                        res.KeptRows++;
                        w.Write(row); w.Write(nl);
                        FlushIfBig();
                        continue;
                    }
                    e.Found = true;
                    if (e.Delete)
                    {
                        res.DeletedRows++;
                        continue;
                    }
                    res.KeptRows++;
                    if (WriteChangedRow(w, row, fo, encO, e.Changes!, fields, tmp, res, e.KeyText)) res.ChangedRows++;
                    w.Write(nl);
                    FlushIfBig();
                }
            }

            // 2) 新から追加の行を取り、旧の形に直して足す
            if (keyN != null)
            {
                bool sameForm = fo.Header.SequenceEqual(fn.Header) && fo.Delimiter == fn.Delimiter && encO.CodePage == encN.CodePage;
                int[] map = fo.Header.Select(h => Array.IndexOf(fn.Header, h)).ToArray();
                var nf = new List<CsvBytes.Field>();
                using Stream src = RowReader.OpenWork(fn);
                var rr = new RowReader(src, _o.Quotes);
                bool header = true;
                while (rr.Next(out ReadOnlySpan<byte> row, out _))
                {
                    if (row.Length == 0) continue;
                    if (header) { header = false; continue; }
                    if (tmp.Length < row.Length) tmp = new byte[row.Length * 2];
                    Entry? e = adds.Find(HashRowKey(row, fn.Delimiter, keyN, _o.Quotes, tmp, canonN), row, fn.Delimiter, keyN, _o.Quotes, tmp, canonN);
                    if (e == null) continue;
                    e.Found = true;
                    if (!endsWithNewline) { w.Write(fo.NewLine); endsWithNewline = true; }
                    if (sameForm) w.Write(row);
                    else if (!WriteConvertedRow(w, row, fn, fo, map, encN, encO, nf, tmp))
                    {
                        res.Unencodable++;
                        Problem(res, $"追加 {e.KeyText}: 新の行に旧の文字コード（{res.OldEncoding}）で書けない文字があるため足さなかった");
                        continue;
                    }
                    w.Write(fo.NewLine);
                    res.AddedRows++;
                    FlushIfBig();
                }
            }
            output.Write(w.WrittenSpan);
        }

        foreach (Entry e in changes.All.Concat(adds.All))
        {
            if (e.Found) continue;
            res.MissingKeys++;
            Problem(res, $"キー {e.KeyText} が{(e.Delete || e.Changes != null ? "旧" : "新")}に見つからない");
        }
        res.Total = sw.Elapsed;
        return res;
    }

    private static void Problem(ApplyResult res, string s)
    {
        if (res.Problems.Count < 20) res.Problems.Add(s);
    }

    /// <summary>変更を当てた行を書く。1 つでもセルを変えたら true。</summary>
    private bool WriteChangedRow(ArrayBufferWriter<byte> w, ReadOnlySpan<byte> row, CsvFile fo, Encoding encO, List<Change> changes,
        List<CsvBytes.Field> fields, byte[] tmp, ApplyResult res, string keyText)
    {
        CsvBytes.Split(row, fo.Delimiter, fields, _o.Quotes);
        int maxCol = fields.Count - 1;
        foreach (Change c in changes) maxCol = Math.Max(maxCol, c.Column);
        var replace = new byte[]?[maxCol + 1];
        bool any = false;
        foreach (Change c in changes)
        {
            ReadOnlySpan<byte> cur = default;
            if (c.Column < fields.Count)
            {
                CsvBytes.Field f = fields[c.Column];
                cur = row.Slice(f.Start, f.Length);
                if (f.Quoted) cur = tmp.AsSpan(0, CsvBytes.Unquote(cur, true, tmp));
            }
            if (c.NewValue == null)
            {
                res.Unencodable++;
                Problem(res, $"変更 {keyText} {c.Name}: 新値を旧の文字コード（{res.OldEncoding}）で書けないため反映しなかった");
                continue;
            }
            // バイト列が違っても、文字として同じなら同じ値（CP932 の同じ文字の 2 つの符号など）
            if (!cur.SequenceEqual(c.OldValue) && encO.GetString(cur) != c.OldText)
            {
                res.Conflicts++;
                Problem(res, $"変更 {keyText} {c.Name}: 旧の今の値が差分の旧値「{c.OldText}」と違うため反映しなかった");
                continue;
            }
            replace[c.Column] = c.NewValue;
            any = true;
        }
        if (!any)
        {
            w.Write(row);
            return false;
        }
        for (int i = 0; i < replace.Length; i++)
        {
            if (i > 0) WriteByte(w, fo.Delimiter);
            bool has = i < fields.Count;
            if (replace[i] is byte[] v)
            {
                res.ChangedCells++;
                // 元が引用符つきなら引用符つきのまま、そうでなければ要るときだけ囲む
                bool force = has && fields[i].Quoted;
                if (!_o.Quotes && (v.AsSpan().IndexOf(fo.Delimiter) >= 0 || v.AsSpan().IndexOfAny((byte)'\r', (byte)'\n') >= 0))
                    throw new InvalidDataException($"変更 {keyText}: 引用符を使わない設定（-quote off）では、区切りや改行を含む値を書けません");
                if (_o.Quotes) CsvBytes.WriteField(w, v, fo.Delimiter, force);
                else w.Write(v);
            }
            else if (has)
            {
                CsvBytes.Field f = fields[i];
                w.Write(row.Slice(f.Start, f.Length));
            }
        }
        return true;
    }

    /// <summary>新の行を、旧の列の並び・文字コード・区切りに直して書く。旧の文字コードで書けなければ何も書かずに false。</summary>
    private bool WriteConvertedRow(ArrayBufferWriter<byte> w, ReadOnlySpan<byte> row, CsvFile fn, CsvFile fo, int[] map,
        Encoding encN, Encoding encO, List<CsvBytes.Field> nf, byte[] tmp)
    {
        CsvBytes.Split(row, fn.Delimiter, nf, _o.Quotes);
        var parts = new byte[map.Length][];
        bool sameEnc = encN.CodePage == encO.CodePage;
        for (int i = 0; i < map.Length; i++)
        {
            int j = map[i];
            if (j < 0 || j >= nf.Count) { parts[i] = Array.Empty<byte>(); continue; }
            CsvBytes.Field f = nf[j];
            ReadOnlySpan<byte> v = row.Slice(f.Start, f.Length);
            if (f.Quoted) v = tmp.AsSpan(0, CsvBytes.Unquote(v, true, tmp));
            if (sameEnc) parts[i] = v.ToArray();
            else
            {
                try { parts[i] = encO.GetBytes(encN.GetString(v)); }
                catch (EncoderFallbackException) { return false; }
            }
        }
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) WriteByte(w, fo.Delimiter);
            if (_o.Quotes) CsvBytes.WriteField(w, parts[i], fo.Delimiter);
            else w.Write(parts[i]);
        }
        return true;
    }

    private static void WriteByte(ArrayBufferWriter<byte> w, byte b)
    {
        w.GetSpan(1)[0] = b;
        w.Advance(1);
    }

    /// <summary>差分を読み、削除・変更・追加の表を作る。キー列の名前を返す。</summary>
    private string[] ReadDiff(CsvFile fo, Encoding encO, Encoding encN, Lookup changes, Lookup adds, ApplyResult res)
    {
        // 差分は uvd の UTF-8・カンマ区切りのほか、Excel で保存し直したもの（CP932・タブ区切り・UTF-16）も読む
        CsvFile fd = CsvFile.Probe(_o.DiffPath, null, null, true);
        Encoding encD = TextEncodings.IsAsciiCompatible(fd.Encoding) ? TextEncodings.Get(fd.Encoding) : new UTF8Encoding(false);
        string[] h = fd.Header;
        if (h.Length < 5 || h[0] != "区分" || h[^3] != "列" || h[^2] != "旧値" || h[^1] != "新値")
            throw new InvalidDataException($"差分の見出しが「区分,キー列…,列,旧値,新値」ではありません: {_o.DiffPath}（見出し: {string.Join(", ", h)}）");
        string[] keyNames = h[1..^3];
        int nk = keyNames.Length;

        using Stream src = RowReader.OpenWork(fd);
        var rr = new RowReader(src, true);
        var fields = new List<CsvBytes.Field>();
        byte[] tmp = new byte[4096];
        bool header = true;
        long line = 1;
        string Value(ReadOnlySpan<byte> row, int i)
        {
            if (i >= fields.Count) return "";
            CsvBytes.Field f = fields[i];
            ReadOnlySpan<byte> raw = row.Slice(f.Start, f.Length);
            if (tmp.Length < raw.Length) tmp = new byte[raw.Length * 2];
            return encD.GetString(tmp, 0, CsvBytes.Unquote(raw, f.Quoted, tmp));
        }
        while (rr.Next(out ReadOnlySpan<byte> row, out _))
        {
            line++;
            if (row.Length == 0) continue;
            if (header) { header = false; continue; }
            CsvBytes.Split(row, fd.Delimiter, fields, true);
            string kind = Value(row, 0);
            var keyText = new string[nk];
            for (int k = 0; k < nk; k++) keyText[k] = Value(row, 1 + k);
            string shown = string.Join("＋", keyText);
            switch (kind)
            {
                case "削除":
                {
                    res.DiffDeleted++;
                    Entry e = changes.GetOrAdd(EncodeKey(keyText, encO, "旧"), shown);
                    e.Delete = true;
                    break;
                }
                case "追加":
                {
                    res.DiffAdded++;
                    adds.GetOrAdd(EncodeKey(keyText, encN, "新"), shown);
                    break;
                }
                case "変更":
                {
                    res.DiffChanged++;
                    string col = Value(row, 1 + nk), ov = Value(row, 2 + nk), nv = Value(row, 3 + nk);
                    int ci = Array.IndexOf(fo.Header, col);
                    if (ci < 0)
                    {
                        res.UnknownColumns++;
                        Problem(res, $"変更 {shown} {col}: 旧に無い列なので反映しない");
                        break;
                    }
                    byte[]? nb;
                    try { nb = encO.GetBytes(nv); } catch (EncoderFallbackException) { nb = null; }
                    byte[] ob;
                    try { ob = encO.GetBytes(ov); } catch (EncoderFallbackException) { ob = Encoding.UTF8.GetBytes("￿" + ov); }
                    Entry e = changes.GetOrAdd(EncodeKey(keyText, encO, "旧"), shown);
                    (e.Changes ??= new List<Change>()).Add(new Change(ci, col, ob, nb, ov, nv));
                    break;
                }
                default:
                    throw new InvalidDataException($"差分の {line} 行目の区分「{kind}」が 追加・削除・変更 のどれでもありません");
            }
        }
        return keyNames;
    }

    private static byte[][] EncodeKey(string[] keyText, Encoding enc, string side)
    {
        var r = new byte[keyText.Length][];
        for (int k = 0; k < keyText.Length; k++)
        {
            try { r[k] = enc.GetBytes(keyText[k]); }
            catch (EncoderFallbackException) { throw new InvalidDataException($"差分のキー「{keyText[k]}」を{side}の文字コードで書けません"); }
        }
        return r;
    }

    /// <summary>Dispose で下のストリームを閉じない包み（書き出しの FileStream は外側で閉じる）。</summary>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
        protected override void Dispose(bool disposing) => inner.Flush();
    }
}
