using System.Buffers;

namespace UwDiff.Core;

/// <summary>
/// 1 行（改行を除いたバイト列）を区切り文字で列に分ける。文字コードは変換しない。
/// UTF-8・CP932・EUC-JP とも、区切り（, や タブ）・引用符（"）・改行は 1 バイトの ASCII で、
/// 2 バイト文字の 2 バイト目とは重ならない（CP932 の 2 バイト目は 0x40〜0x7E・0x80〜0xFC、EUC-JP は 0xA1 以上）。
/// quotes が false なら引用符を特別扱いしない（引用符を使わない TSV 用）。
/// </summary>
public static class CsvBytes
{
    public const byte Quote = (byte)'"';

    /// <summary>列の位置（行の先頭からのオフセットと長さ。引用符を含む生のまま）。</summary>
    public readonly record struct Field(int Start, int Length, bool Quoted);

    /// <summary>行を列に分けて fields に入れる（fields は先に Clear される）。</summary>
    public static void Split(ReadOnlySpan<byte> row, byte delim, List<Field> fields, bool quotes = true)
    {
        fields.Clear();
        int i = 0;
        int n = row.Length;
        while (true)
        {
            int start = i;
            if (quotes && i < n && row[i] == Quote)
            {
                // 引用符つきの列: 次の単独の " まで（"" は中身の "）
                i++;
                while (i < n)
                {
                    if (row[i] == Quote)
                    {
                        if (i + 1 < n && row[i + 1] == Quote) { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
                // 閉じ引用符のあとに区切りまでのゴミがあれば含める
                int d = row[i..].IndexOf(delim);
                int end = d < 0 ? n : i + d;
                fields.Add(new Field(start, end - start, true));
                if (d < 0) return;
                i = end + 1;
            }
            else
            {
                int d = row[i..].IndexOf(delim);
                int end = d < 0 ? n : i + d;
                fields.Add(new Field(start, end - start, false));
                if (d < 0) return;
                i = end + 1;
            }
        }
    }

    /// <summary>index 番目の列だけを取り出す。無ければ false。</summary>
    public static bool TryGetField(ReadOnlySpan<byte> row, byte delim, int index, out Field field, bool quotes = true)
    {
        int i = 0;
        int n = row.Length;
        int col = 0;
        while (true)
        {
            int start = i;
            int end;
            bool quoted = false;
            if (quotes && i < n && row[i] == Quote)
            {
                quoted = true;
                i++;
                while (i < n)
                {
                    if (row[i] == Quote)
                    {
                        if (i + 1 < n && row[i + 1] == Quote) { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
            }
            int d = row[i..].IndexOf(delim);
            end = d < 0 ? n : i + d;
            if (col == index)
            {
                field = new Field(start, end - start, quoted);
                return true;
            }
            if (d < 0) { field = default; return false; }
            i = end + 1;
            col++;
        }
    }

    /// <summary>列の値（引用符を外し "" を " にしたもの）を dest に書き、長さを返す。</summary>
    public static int Unquote(ReadOnlySpan<byte> raw, bool quoted, Span<byte> dest)
    {
        if (!quoted)
        {
            raw.CopyTo(dest);
            return raw.Length;
        }
        int o = 0;
        int i = 1;
        int n = raw.Length;
        while (i < n)
        {
            byte b = raw[i];
            if (b == Quote)
            {
                if (i + 1 < n && raw[i + 1] == Quote) { dest[o++] = Quote; i += 2; continue; }
                i++;
                // 閉じ引用符のあと（通常は無い）はそのまま足す
                while (i < n) dest[o++] = raw[i++];
                break;
            }
            dest[o++] = b;
            i++;
        }
        return o;
    }

    /// <summary>s の from から、引用符の外の改行（\n）の位置を探す。無ければ -1。</summary>
    public static int FindRowEnd(ReadOnlySpan<byte> s, int from, bool quotes = true)
    {
        if (!quotes)
        {
            int k = s[from..].IndexOf((byte)'\n');
            return k < 0 ? -1 : from + k;
        }
        bool inQ = false;
        int i = from;
        while (i < s.Length)
        {
            int k = inQ ? s[i..].IndexOf(Quote) : s[i..].IndexOfAny(Quote, (byte)'\n');
            if (k < 0) break;
            i += k;
            if (s[i] == Quote) { inQ = !inQ; i++; continue; }
            return i;
        }
        return -1;
    }

    /// <summary>値を 1 列として書くとき、引用符で囲む必要があるか。</summary>
    public static bool NeedsQuote(ReadOnlySpan<byte> v, byte delim) =>
        v.IndexOfAny(Quote, delim, (byte)'\n') >= 0 || v.IndexOf((byte)'\r') >= 0;

    /// <summary>値を 1 列として書く（引用符が要るとき・force のときは囲み、中の " は "" にする）。</summary>
    public static void WriteField(System.Buffers.IBufferWriter<byte> w, ReadOnlySpan<byte> v, byte delim, bool force = false)
    {
        if (!force && !NeedsQuote(v, delim))
        {
            w.Write(v);
            return;
        }
        w.Write("\""u8);
        int i = 0;
        while (i < v.Length)
        {
            int q = v[i..].IndexOf(Quote);
            if (q < 0) { w.Write(v[i..]); break; }
            w.Write(v.Slice(i, q + 1));
            w.Write("\""u8);
            i += q + 1;
        }
        w.Write("\""u8);
    }

    /// <summary>値の比較用: 引用符を外した値どうしが同じか。</summary>
    public static bool ValueEquals(ReadOnlySpan<byte> a, bool aq, ReadOnlySpan<byte> b, bool bq)
    {
        if (!aq && !bq) return a.SequenceEqual(b);
        byte[] ra = ArrayPool<byte>.Shared.Rent(a.Length);
        byte[] rb = ArrayPool<byte>.Shared.Rent(b.Length);
        try
        {
            int la = Unquote(a, aq, ra);
            int lb = Unquote(b, bq, rb);
            return ra.AsSpan(0, la).SequenceEqual(rb.AsSpan(0, lb));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(ra);
            ArrayPool<byte>.Shared.Return(rb);
        }
    }
}
