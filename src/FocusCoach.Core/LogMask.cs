using System.Security.Cryptography;
using System.Text;

namespace FocusCoach.Core;

/// <summary>
/// アプリログに出すタイトル・パス・URLのマスク(08章 §8.4)。種別とハッシュの先頭8桁だけを残す。
/// 同じ値は同じハッシュになるので、ログの中で同じ対象かどうかは追える。
/// </summary>
public static class LogMask
{
    public static string Mask(string kind, string? value)
    {
        if (string.IsNullOrEmpty(value)) return $"{kind}:(empty)";
        var h = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"{kind}:{Convert.ToHexString(h)[..8].ToLowerInvariant()}";
    }
}
