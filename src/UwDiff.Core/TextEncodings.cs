using System.Text;
using System.Text.Unicode;

namespace UwDiff.Core;

/// <summary>扱う文字コード。</summary>
public enum FileEncoding
{
    Utf8,
    Cp932,
    EucJp,
    Utf16Le,
    Utf16Be,
}

public static class TextEncodings
{
    static TextEncodings()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding Get(FileEncoding e) => e switch
    {
        FileEncoding.Utf8 => new UTF8Encoding(false),
        // .NET の 932 は Windows と同じ表（0x8160 → U+FF5E、NEC 特殊・IBM 拡張を含む）
        FileEncoding.Cp932 => Encoding.GetEncoding(932),
        // 51932 は Windows の EUC-JP（CP932 と同じ文字の対応。半角カナは 0x8E＋1 バイト）
        FileEncoding.EucJp => Encoding.GetEncoding(51932),
        FileEncoding.Utf16Le => new UnicodeEncoding(false, false),
        FileEncoding.Utf16Be => new UnicodeEncoding(true, false),
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };

    public static string Name(FileEncoding e) => e switch
    {
        FileEncoding.Utf8 => "UTF-8",
        FileEncoding.Cp932 => "CP932",
        FileEncoding.EucJp => "EUC-JP",
        FileEncoding.Utf16Le => "UTF-16LE",
        FileEncoding.Utf16Be => "UTF-16BE",
        _ => e.ToString(),
    };

    /// <summary>
    /// 区切り（, タブ）・引用符・改行が 1 バイトの ASCII で、2 バイト文字のどのバイトとも重ならない文字コードか。
    /// そうなら変換せずにバイト列のまま行・列に切れる（UTF-8・CP932・EUC-JP）。UTF-16 は UTF-8 に直してから切る。
    /// </summary>
    public static bool IsAsciiCompatible(FileEncoding e) => e is FileEncoding.Utf8 or FileEncoding.Cp932 or FileEncoding.EucJp;

    /// <summary>BOM のバイト列（書き出しで元の BOM を保つため）。</summary>
    public static byte[] Bom(FileEncoding e, int bomLength) => bomLength == 0 ? Array.Empty<byte>() : e switch
    {
        FileEncoding.Utf8 => new byte[] { 0xEF, 0xBB, 0xBF },
        FileEncoding.Utf16Le => new byte[] { 0xFF, 0xFE },
        FileEncoding.Utf16Be => new byte[] { 0xFE, 0xFF },
        _ => Array.Empty<byte>(),
    };

    /// <summary>CLI の名前から（auto は null）。</summary>
    public static FileEncoding? Parse(string s) => s.ToLowerInvariant().Replace("_", "-") switch
    {
        "auto" => null,
        "utf8" or "utf-8" => FileEncoding.Utf8,
        "cp932" or "sjis" or "shift-jis" or "shiftjis" => FileEncoding.Cp932,
        "eucjp" or "euc-jp" or "euc" => FileEncoding.EucJp,
        "utf16" or "utf-16" or "utf16le" or "utf-16le" => FileEncoding.Utf16Le,
        "utf16be" or "utf-16be" => FileEncoding.Utf16Be,
        _ => throw new ArgumentException("文字コードは auto・utf8・cp932・eucjp・utf16le・utf16be のどれか: " + s),
    };

    /// <summary>
    /// 先頭の一部から文字コードを決める。bomLength に BOM の長さを返す。
    /// 1. BOM（UTF-8・UTF-16LE/BE）
    /// 2. BOM の無い UTF-16（ASCII の文字の片側が 0 になる）
    /// 3. UTF-8 として正しければ UTF-8
    /// 4. CP932 と EUC-JP のどちらとして読んだときに崩れが少ないか（同じなら CP932）
    /// </summary>
    public static FileEncoding Detect(ReadOnlySpan<byte> head, bool isWholeFile, out int bomLength)
    {
        bomLength = 0;
        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
        {
            bomLength = 3;
            return FileEncoding.Utf8;
        }
        if (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xFE) { bomLength = 2; return FileEncoding.Utf16Le; }
        if (head.Length >= 2 && head[0] == 0xFE && head[1] == 0xFF) { bomLength = 2; return FileEncoding.Utf16Be; }

        FileEncoding? u16 = DetectUtf16NoBom(head);
        if (u16 != null) return u16.Value;

        // 末尾で 1 文字が途中で切れている分は判定から外す
        ReadOnlySpan<byte> s = head;
        if (!isWholeFile)
        {
            int cut = 0;
            for (int k = 1; k <= 3 && k <= s.Length; k++)
            {
                byte b = s[^k];
                if ((b & 0xC0) == 0x80) continue;          // 続きのバイト
                if (b >= 0xC0) cut = k;                    // 先頭のバイト → ここから切る
                break;
            }
            s = s[..^cut];
        }
        if (Utf8.IsValid(s)) return FileEncoding.Utf8;
        int eucErr = CountEucErrors(head, isWholeFile), cpErr = CountCp932Errors(head, isWholeFile);
        if (eucErr != cpErr) return eucErr < cpErr ? FileEncoding.EucJp : FileEncoding.Cp932;
        // 崩れの数が同じ（EUC-JP の文字は CP932 として読んでも崩れないことが多い）: CP932 の日本語には、かな・よく使う漢字の
        // 先頭バイト 0x81〜0x9F（0x8E・0x8F を除く。EUC-JP の半角カナと補助漢字の印と重なるため）がほぼ必ず出る。無ければ EUC-JP
        return HasCp932LowLead(head) ? FileEncoding.Cp932 : FileEncoding.EucJp;
    }

    private static bool HasCp932LowLead(ReadOnlySpan<byte> s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            byte b = s[i];
            if (b < 0x80 || (b >= 0xA1 && b <= 0xDF)) continue;
            if (b >= 0x81 && b <= 0x9F && b != 0x8E && b != 0x8F) return true;
            i++;   // 2 バイト文字の 2 バイト目を飛ばす
        }
        return false;
    }

    /// <summary>BOM の無い UTF-16: 偶数番目か奇数番目の片方だけに 0 が多ければ UTF-16。</summary>
    private static FileEncoding? DetectUtf16NoBom(ReadOnlySpan<byte> head)
    {
        int n = Math.Min(head.Length, 1 << 16) & ~1;
        if (n < 4) return null;
        int zeroEven = 0, zeroOdd = 0;
        for (int i = 0; i < n; i += 2)
        {
            if (head[i] == 0) zeroEven++;
            if (head[i + 1] == 0) zeroOdd++;
        }
        int pairs = n / 2;
        // 見出しと数字・記号が多い表なら、ASCII の文字の割合は十分に高い
        if (zeroOdd * 10 >= pairs * 3 && zeroEven * 20 < pairs) return FileEncoding.Utf16Le;
        if (zeroEven * 10 >= pairs * 3 && zeroOdd * 20 < pairs) return FileEncoding.Utf16Be;
        return null;
    }

    private static int CountCp932Errors(ReadOnlySpan<byte> s, bool whole)
    {
        int err = 0;
        for (int i = 0; i < s.Length; i++)
        {
            byte b = s[i];
            if (b < 0x80 || (b >= 0xA1 && b <= 0xDF)) continue;     // ASCII・半角カナ
            if ((b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC))
            {
                if (i + 1 >= s.Length) { if (whole) err++; break; }
                byte t = s[i + 1];
                if ((t >= 0x40 && t <= 0x7E) || (t >= 0x80 && t <= 0xFC)) { i++; continue; }
            }
            err++;
        }
        return err;
    }

    private static int CountEucErrors(ReadOnlySpan<byte> s, bool whole)
    {
        int err = 0;
        for (int i = 0; i < s.Length; i++)
        {
            byte b = s[i];
            if (b < 0x80) continue;
            int need = b == 0x8F ? 2 : 1;
            bool lead = b == 0x8E || b == 0x8F || (b >= 0xA1 && b <= 0xFE);
            if (!lead) { err++; continue; }
            if (i + need >= s.Length) { if (whole) err++; break; }
            bool ok = true;
            for (int k = 1; k <= need; k++)
            {
                byte t = s[i + k];
                if (b == 0x8E ? t < 0xA1 || t > 0xDF : t < 0xA1 || t > 0xFE) ok = false;
            }
            if (ok) i += need;
            else err++;
        }
        return err;
    }
}
