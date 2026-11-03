using System.Globalization;

namespace FocusCoach.Core;

/// <summary>割り込みのカテゴリ(利用者が増やせる。既定は1つだけ。design-0.12A-interrupt-closure.md D2)。</summary>
public static class InterruptCategories
{
    /// <summary>既定のカテゴリの内部の名前(DB に保存される。変えない)。表示は <see cref="Display"/> で言語に合わせる。</summary>
    public const string Default = "割込み作業";

    /// <summary>画面に出す名前。既定のカテゴリだけ、今の言語の名前にする(利用者が作った名前はそのまま)。</summary>
    public static string Display(string name) => name == Default ? Loc.T("category.default") : name;

    /// <summary>画面で選んだ・入力した名前を、保存する名前に戻す(どの言語の既定の名前も、内部の既定の名前にする)。</summary>
    public static string Canonical(string shown)
    {
        var s = shown.Trim();
        if (s.Length == 0) return Default;
        foreach (var lang in Loc.Languages)
            if (Loc.Table(lang).TryGetValue("category.default", out var d) && s == d) return Default;
        return s;
    }
}

/// <summary>再入場ガイド(S-11b)を出すかどうかの判断。</summary>
public sealed record ReentryDecision(bool Show, int Seconds, string Reason);

/// <summary>
/// 再入場ガイド(割り込みを閉じたあと、決まった秒数で元の作業に戻す。design-0.12A §4)の判断。純粋関数。
/// - 秒数 0 は「出さない」。短い割り込み(既定60秒未満)でも出さない(毎回だと煩わしく、切られてしまうため)。
/// - 時間切れでフォーカスを移すのは、直前に他のアプリでキー入力が無いときだけ(打っている文字が別の場所に入らないように)。
/// </summary>
public static class ReentryPolicy
{
    public const int MinSeconds = 5, MaxSeconds = 600;
    /// <summary>この秒数以内にキー・マウスの入力があれば、時間切れでもフォーカスを移さない。</summary>
    public const double QuietSec = 2;
    public const int ExtendSec = 30;

    public static ReentryDecision Decide(double interruptSec, int guideSec, int minInterruptSec)
    {
        if (guideSec <= 0) return new(false, 0, "off");
        if (!double.IsFinite(interruptSec) || interruptSec < minInterruptSec) return new(false, 0, "short");
        return new(true, Math.Clamp(guideSec, MinSeconds, MaxSeconds), "show");
    }

    public static bool MayMoveFocus(double secSinceLastInput) => !double.IsFinite(secSinceLastInput) || secSinceLastInput >= QuietSec;

    /// <summary>0.12A の既定値(日本語)。設定ファイルに保存されたままの人も、未設定(言語ごとの既定文)として扱う。</summary>
    public const string LegacyDefaultMessage = "{sec}秒で、元の作業に集中しましょう";

    /// <summary>
    /// 誘いかけの一言。未設定(null)・0.12A の既定文のままなら、今の言語の既定文。空白だけなら出さない。
    /// 利用者が書いた文は、{sec} を秒数に置き換える。
    /// </summary>
    public static string Message(string? template, int sec)
    {
        if (template is null || template == LegacyDefaultMessage) return Loc.T("re.message", sec);
        return string.IsNullOrWhiteSpace(template) ? "" : template.Replace("{sec}", sec.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>残り時間の表示(m:ss)。</summary>
    public static string Remaining(double sec) => TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, sec))).ToString(@"m\:ss", CultureInfo.InvariantCulture);
}

public enum DueBucket { Overdue, Today, Tomorrow, Later, None }

/// <summary>あとでやること(S-12)の1行。Due は日付だけ(時刻は持たない)。</summary>
public sealed record FollowupItem(long Id, string Text, DateOnly? Due, string Category, string Title, string TaskName,
    DateTimeOffset CreatedAt, bool Done = false, DateTimeOffset? DoneAt = null);

/// <summary>あとでやることの期限の区分・並び・表示(design-0.12A §5)。純粋関数。</summary>
public static class Followups
{
    public static DueBucket Bucket(DateOnly? due, DateOnly today) => due switch
    {
        null => DueBucket.None,
        { } d when d < today => DueBucket.Overdue,
        { } d when d == today => DueBucket.Today,
        { } d when d == today.AddDays(1) => DueBucket.Tomorrow,
        _ => DueBucket.Later,
    };

    /// <summary>期限切れ → 今日 → 明日 → 日付順 → 期限なし(古い順)。完了したものは最後(新しく完了した順)。</summary>
    public static List<FollowupItem> Sort(IEnumerable<FollowupItem> items, DateOnly today) =>
        items.OrderBy(i => i.Done)
             .ThenBy(i => i.Done ? 0 : (int)Bucket(i.Due, today))
             .ThenBy(i => i.Done ? DateOnly.MinValue : i.Due ?? DateOnly.MaxValue)
             .ThenByDescending(i => i.Done ? i.DoneAt ?? i.CreatedAt : DateTimeOffset.MinValue)
             .ThenBy(i => i.CreatedAt)
             .ToList();

    public static string DueLabel(DateOnly? due, DateOnly today) => Bucket(due, today) switch
    {
        DueBucket.None => "",
        DueBucket.Overdue => Loc.T("due.overdue", Loc.Date(due!.Value)),
        DueBucket.Today => Loc.T("due.today"),
        DueBucket.Tomorrow => Loc.T("due.tomorrow"),
        _ => Loc.Date(due!.Value),
    };

    public static string BucketHeading(DueBucket b) => b switch
    {
        DueBucket.Overdue => Loc.T("due.heading.overdue"),
        DueBucket.Today => Loc.T("due.heading.today"),
        DueBucket.Tomorrow => Loc.T("due.heading.tomorrow"),
        DueBucket.Later => Loc.T("due.heading.later"),
        _ => Loc.T("due.heading.none"),
    };

    /// <summary>ミニパネル・トレイに出す小さな件数(「あとで 3 ・今日 1」)。0件なら空。</summary>
    public static string Badge(IEnumerable<FollowupItem> items, DateOnly today)
    {
        var open = items.Where(i => !i.Done).ToList();
        if (open.Count == 0) return "";
        var due = open.Count(i => Bucket(i.Due, today) is DueBucket.Overdue or DueBucket.Today);
        return due > 0 ? Loc.T("followup.badge_due", open.Count, due) : Loc.T("followup.badge", open.Count);
    }

    /// <summary>出口カードの期限の選択(today / tomorrow / none / yyyy-MM-dd)を日付にする。</summary>
    public static DateOnly? ParseDueChoice(string? choice, DateOnly today) => choice switch
    {
        null or "" or "none" => null,
        "today" => today,
        "tomorrow" => today.AddDays(1),
        _ => DateOnly.TryParseExact(choice, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
    };

    public static string FormatDue(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
}
