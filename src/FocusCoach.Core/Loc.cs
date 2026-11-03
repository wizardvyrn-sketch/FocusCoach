using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace FocusCoach.Core;

/// <summary>
/// 画面に出す文言の辞書と、表示言語(prototype/review/design-0.13A-i18n.md)。画面の部品に依存しないので、Core の文(ガイド・検証エラーなど)も使う。
/// 辞書は埋め込みの JSON(Strings/ja.json、Strings/en.json)。キーが無いときは 言語 → en → 「⟦キー⟧」の順で補い、実行は止めない。
/// 言語を足すには、Strings/xx.json を足して <see cref="Languages"/> に加える。ただし切り替えの操作(ボタン・トレイ・ホットキー。<see cref="Next"/>)は ja ⇄ en の2つ固定なので、3言語目には選ぶ方法も要る。
/// </summary>
public static class Loc
{
    public const string Ja = "ja", En = "en";
    public static readonly IReadOnlyList<string> Languages = [Ja, En];

    private static readonly object Gate = new();
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Tables = [];
    private static string _language = Ja;
    private static readonly AsyncLocal<string?> Override = new();

    /// <summary>今の表示言語(ja / en)。</summary>
    public static string Language => Override.Value ?? _language;

    /// <summary>言語が替わったとき(UI が文字を更新する)。UI スレッドとは限らない。</summary>
    public static event Action? Changed;

    /// <summary>設定の値(auto / ja / en)と OS の表示言語から、使う言語を決める。auto は、OS が日本語なら ja、それ以外は en。</summary>
    public static string Resolve(string? setting, string? osLanguage = null)
    {
        var s = setting?.Trim().ToLowerInvariant();
        if (s is Ja or En) return s;
        var os = (osLanguage ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).ToLowerInvariant();
        return os == Ja ? Ja : En;
    }

    /// <summary>表示言語を替える。未対応の言語は en にする。変わったときだけ Changed を出す。</summary>
    public static void SetLanguage(string code)
    {
        var c = Languages.Contains(code) ? code : En;
        bool changed;
        lock (Gate) { changed = _language != c; _language = c; }
        if (changed) Changed?.Invoke();
    }

    /// <summary>次の言語(ja → en → ja)。切り替えのボタン・ホットキー用。</summary>
    public static string Next(string current) => current == Ja ? En : Ja;

    /// <summary>テスト用: このスレッド(非同期の流れ)の中だけ言語を替える。他のテストに影響しない。</summary>
    public static IDisposable Use(string code)
    {
        var prev = Override.Value;
        Override.Value = Languages.Contains(code) ? code : En;
        return new Restore(() => Override.Value = prev);
    }

    private sealed class Restore(Action undo) : IDisposable { public void Dispose() => undo(); }

    /// <summary>文言を引く。args があれば string.Format(InvariantCulture)。キー欠けは ⟦キー⟧ を返す(画面で気づける)。</summary>
    public static string T(string key, params object?[] args)
    {
        var text = Lookup(Language, key) ?? Lookup(En, key) ?? $"⟦{key}⟧";
        if (args.Length == 0) return text;
        try { return string.Format(CultureInfo.InvariantCulture, text, args); }
        catch (FormatException) { return text; }   // 辞書の書式の誤りで落とさない(テストで検出する)
    }

    public static bool Has(string key) => Lookup(Language, key) is not null;

    /// <summary>辞書の中身(テストが ja / en のキーの食い違いを調べる)。</summary>
    public static IReadOnlyDictionary<string, string> Table(string language) => Load(language);

    private static string? Lookup(string language, string key) => Load(language).TryGetValue(key, out var v) ? v : null;

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        lock (Gate)
        {
            if (Tables.TryGetValue(language, out var t)) return t;
            var name = $"FocusCoach.Core.Strings.{language}.json";
            using var s = typeof(Loc).Assembly.GetManifestResourceStream(name);
            IReadOnlyDictionary<string, string> table = s is null
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? [];
            Tables[language] = table;
            return table;
        }
    }

    // ---------------- 日付・時間の書き方(言語に合わせる) ----------------

    /// <summary>日付(ja: 10/30、en: Oct 30)。</summary>
    public static string Date(DateTimeOffset d) => d.ToString(Language == Ja ? "M/d" : "MMM d", CultureInfo.InvariantCulture);
    public static string Date(DateOnly d) => d.ToString(Language == Ja ? "M/d" : "MMM d", CultureInfo.InvariantCulture);

    /// <summary>日付と時刻(ja: 10/30 18:44、en: Oct 30 18:44)。</summary>
    public static string DateTime(DateTimeOffset d) => d.ToString(Language == Ja ? "M/d HH:mm" : "MMM d HH:mm", CultureInfo.InvariantCulture);

    /// <summary>所要時間の短い書き方(ja: 1時間20分 / 3分 / 40秒、en: 1h 20m / 3m / 40s)。</summary>
    public static string Duration(double sec)
    {
        if (sec >= 3600) return T("time.hm", (int)(sec / 3600), (int)(sec % 3600 / 60));
        if (sec >= 60) return T("time.m", (int)Math.Round(sec / 60));
        return T("time.s", (int)sec);
    }
}
