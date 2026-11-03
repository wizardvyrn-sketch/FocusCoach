using System.Text.RegularExpressions;

namespace FocusCoach.Core;

public enum ExclusionKind { Process, UrlDomain, PathPrefix, TitleRegex }
public enum ExclusionAction { Ignore, Mask }

public sealed record ExclusionRule(ExclusionKind Kind, string Pattern, ExclusionAction Action);

/// <summary>
/// 除外ルール(docs/spec/04 §4.4)。記録の時点で適用する純粋関数。
/// [仮案-04] 既定ルールは最小限(プライベートブラウズ、パスワード管理)。実利用で見直す。
/// </summary>
public sealed class ExclusionRules(IEnumerable<ExclusionRule>? rules = null)
{
    public static readonly IReadOnlyList<ExclusionRule> Defaults =
    [
        new(ExclusionKind.TitleRegex, @"InPrivate|シークレット|Incognito", ExclusionAction.Ignore),
        new(ExclusionKind.Process, "KeePass", ExclusionAction.Ignore),
        new(ExclusionKind.Process, "1Password", ExclusionAction.Ignore),
        new(ExclusionKind.Process, "Bitwarden", ExclusionAction.Ignore),
    ];

    private readonly List<ExclusionRule> _rules = (rules ?? Defaults).ToList();

    public IReadOnlyList<ExclusionRule> Rules => _rules;

    /// <summary>前面ウィンドウに対する判定。戻り値: 無視するか、マスクするか。</summary>
    public ExclusionAction? ForWindow(string process, string title) => Evaluate(process, title, null);

    /// <summary>資料(kind: folder/file/url)に対する判定。</summary>
    public ExclusionAction? ForResource(string kind, string location)
    {
        string? path = kind is "folder" or "file" ? location : null;
        string? domain = kind == "url" && Uri.TryCreate(location, UriKind.Absolute, out var u) ? u.Host : null;
        ExclusionAction? res = null;
        foreach (var r in _rules)
        {
            var hit = r.Kind switch
            {
                ExclusionKind.PathPrefix => path is not null && path.StartsWith(r.Pattern, StringComparison.OrdinalIgnoreCase),
                ExclusionKind.UrlDomain => domain is not null && domain.EndsWith(r.Pattern, StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
            if (hit) res = Stronger(res, r.Action);
        }
        return res;
    }

    private ExclusionAction? Evaluate(string process, string title, string? _)
    {
        ExclusionAction? res = null;
        foreach (var r in _rules)
        {
            var hit = r.Kind switch
            {
                ExclusionKind.Process => process.Contains(r.Pattern, StringComparison.OrdinalIgnoreCase),
                ExclusionKind.TitleRegex => SafeMatch(title, r.Pattern),
                _ => false,
            };
            if (hit) res = Stronger(res, r.Action);
        }
        return res;
    }

    private static ExclusionAction Stronger(ExclusionAction? a, ExclusionAction b) =>
        a == ExclusionAction.Ignore || b == ExclusionAction.Ignore ? ExclusionAction.Ignore : ExclusionAction.Mask;

    private static bool SafeMatch(string text, string pattern)
    {
        try { return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50)); }
        catch (RegexMatchTimeoutException) { return false; }
        catch (ArgumentException) { return false; }
    }
}
