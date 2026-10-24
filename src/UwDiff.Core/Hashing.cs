using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UwDiff.Core;

/// <summary>
/// 照合用の 64 ビットハッシュ（暗号用ではない）。キーで振り分け、行が同じかを先に見るために使う。
/// 同じ値なら必ず同じハッシュになる。違う値で同じになることは極めてまれだが、照合では必ずバイト列でも確かめる。
/// </summary>
public static class Hashing
{
    private const ulong P1 = 0x9E3779B185EBCA87UL;
    private const ulong P2 = 0xC2B2AE3D27D4EB4FUL;
    private const ulong P3 = 0x165667B19E3779F9UL;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Mix(ulong a, ulong b)
    {
        UInt128 m = (UInt128)a * b;
        return (ulong)m ^ (ulong)(m >> 64);
    }

    public static ulong Hash(ReadOnlySpan<byte> s, ulong seed = 0)
    {
        ulong h = seed ^ ((ulong)s.Length * P1);
        int i = 0;
        int n = s.Length;
        ref byte r = ref MemoryMarshal.GetReference(s);
        while (n - i >= 16)
        {
            ulong a = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref r, i));
            ulong b = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref r, i + 8));
            h = Mix(a ^ P2 ^ h, b ^ P3);
            i += 16;
        }
        if (n - i >= 8)
        {
            ulong a = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref r, i));
            h = Mix(a ^ P2 ^ h, P3 ^ (ulong)n);
            i += 8;
        }
        if (i < n)
        {
            ulong t = 0;
            int k = 0;
            for (; i < n; i++, k += 8) t |= (ulong)Unsafe.Add(ref r, i) << k;
            h = Mix(t ^ P1 ^ h, P2 ^ (ulong)n);
        }
        // 仕上げ
        h ^= h >> 33; h *= P2; h ^= h >> 29; h *= P3; h ^= h >> 32;
        return h;
    }
}
