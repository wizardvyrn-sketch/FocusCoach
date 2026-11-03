using System.Text.RegularExpressions;
using FocusCoach.Core.Scenarios;
using Xunit;

namespace FocusCoach.Core.Tests;

/// <summary>多言語化(docs/review/design-0.13A-i18n.md)。辞書の整合、日本語の直書きの取りこぼし、英語での出力。</summary>
public class LocalizationTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)(?::[^}]*)?\}", RegexOptions.Compiled);
    private static readonly Regex Japanese = new(@"[぀-ヿ一-鿿]", RegexOptions.Compiled);

    // ---------------- 辞書 ----------------

    [Fact]
    public void Ja_and_en_have_the_same_keys()
    {
        var ja = Loc.Table("ja").Keys.ToHashSet();
        var en = Loc.Table("en").Keys.ToHashSet();
        Assert.Empty(ja.Except(en));   // 英語が欠けているキー
        Assert.Empty(en.Except(ja));   // 日本語が欠けているキー
        Assert.True(ja.Count > 200);
    }

    [Fact]
    public void Placeholders_match_between_languages_and_formats_are_valid()
    {
        var ja = Loc.Table("ja"); var en = Loc.Table("en");
        foreach (var (key, jaText) in ja)
        {
            var enText = en[key];
            var a = Placeholder.Matches(jaText).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToArray();
            var b = Placeholder.Matches(enText).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToArray();
            Assert.True(a.SequenceEqual(b), $"placeholders differ: {key}  ja=[{string.Join(",", a)}] en=[{string.Join(",", b)}]");
            // 書式として壊れていないこと(波括弧の閉じ忘れなど)。引数を十分に渡して例外にならないこと
            var args = Enumerable.Range(0, 6).Select(i => (object)(i + 1.5)).ToArray();
            foreach (var t in new[] { jaText, enText }) _ = string.Format(System.Globalization.CultureInfo.InvariantCulture, t, args);
        }
    }

    [Fact]
    public void English_text_contains_no_japanese_except_language_names_and_bilingual_labels()
    {
        // 言語の名前・「Language / 言語」の併記は、どの言語でも日本語を含めてよい
        var allowed = new HashSet<string> { "lang.ja", "lang.tooltip", "lang.tooltip.nokey", "lang.menu" };
        foreach (var (key, text) in Loc.Table("en"))
            if (!allowed.Contains(key)) Assert.False(Japanese.IsMatch(text), $"Japanese text in en: {key} = {text}");
    }

    [Fact]
    public void Missing_key_is_visible_and_does_not_throw()
    {
        Assert.Equal("⟦no.such.key⟧", Loc.T("no.such.key"));
        Assert.Equal("⟦no.such.key⟧", Loc.T("no.such.key", 1, 2));
    }

    // ---------------- 言語の決め方 ----------------

    [Theory]
    [InlineData("ja", "en", "ja")]
    [InlineData("en", "ja", "en")]
    [InlineData("auto", "ja", "ja")]     // 日本語の OS では日本語
    [InlineData("auto", "en", "en")]
    [InlineData("auto", "fr", "en")]     // それ以外の OS では英語
    [InlineData(null, "ja", "ja")]
    [InlineData("xx", "fr", "en")]
    [InlineData(" JA ", "fr", "ja")]
    public void Resolve_picks_language(string? setting, string os, string expected) => Assert.Equal(expected, Loc.Resolve(setting, os));

    [Fact]
    public void Language_override_is_scoped_and_next_toggles()
    {
        Assert.Equal("ja", Loc.Language);   // 既定(他のテストを乱さない)
        using (Loc.Use("en"))
        {
            Assert.Equal("en", Loc.Language);
            Assert.Equal("Done", Loc.T("exit.done"));
        }
        Assert.Equal("ja", Loc.Language);
        Assert.Equal("完了", Loc.T("exit.done"));
        Assert.Equal("en", Loc.Next("ja"));
        Assert.Equal("ja", Loc.Next("en"));
    }

    // ---------------- Core の文を英語で ----------------

    [Fact]
    public void Duration_and_dates_follow_the_language()
    {
        Assert.Equal("1時間20分", Loc.Duration(4800)); Assert.Equal("3分", Loc.Duration(180)); Assert.Equal("40秒", Loc.Duration(40));
        using (Loc.Use("en"))
        {
            Assert.Equal("1h 20m", Loc.Duration(4800)); Assert.Equal("3m", Loc.Duration(180)); Assert.Equal("40s", Loc.Duration(40));
            Assert.Equal("Oct 30", Loc.Date(new DateOnly(2026, 10, 30)));
            Assert.Equal("Oct 30 18:44", Loc.DateTime(new DateTimeOffset(2026, 10, 30, 18, 44, 0, TimeSpan.Zero)));
        }
        Assert.Equal("10/30", Loc.Date(new DateOnly(2026, 10, 30)));
    }

    [Fact]
    public void Followup_labels_in_english()
    {
        var today = new DateOnly(2026, 10, 29);
        using var _ = Loc.Use("en");
        Assert.Equal("Today", Followups.DueLabel(today, today));
        Assert.Equal("Tomorrow", Followups.DueLabel(today.AddDays(1), today));
        Assert.Equal("Nov 3", Followups.DueLabel(new DateOnly(2026, 11, 3), today));
        Assert.Equal("Overdue Oct 27", Followups.DueLabel(new DateOnly(2026, 10, 27), today));
        Assert.Equal("Due today", Followups.BucketHeading(DueBucket.Today));
        FollowupItem F(long id, DateOnly? d, bool done = false) => new(id, "x", d, "c", "", "", DateTimeOffset.Now, done);
        Assert.Equal("Follow-ups 2", Followups.Badge([F(1, null), F(2, today.AddDays(1))], today));
        Assert.Equal("Follow-ups 3 · 2 due today", Followups.Badge([F(1, null), F(2, today), F(3, today.AddDays(-1))], today));
    }

    [Fact]
    public void Step_validation_errors_in_english()
    {
        using var _ = Loc.Use("en");
        var (_, errors) = StepList.Normalize([("", "10"), ("a:b", ""), ("x", "5"), ("x", "5"), ("y", "99999")]);
        Assert.Contains("Row 1: enter a name", errors);
        Assert.Contains(errors, e => e.StartsWith("Row 2: the name can't contain commas or colons"));
        Assert.Contains(errors, e => e.Contains("“x” is duplicated"));
        Assert.Contains(errors, e => e.StartsWith("Row 5: enter a target between 1 and"));
        Assert.Equal("Enter at least one step", StepList.Normalize([]).Errors.Single());
        Assert.Equal("(no steps)", StepList.Summary([]));
        Assert.Equal("Draft 25 min  →  Write", StepList.Summary([new("Draft", 25), new("Write", null)]));
    }

    [Fact]
    public void Guide_and_comparison_texts_in_english()
    {
        using var _ = Loc.Use("en");
        var p = new ParamRegistry();
        Assert.Equal("high", Guide.Build(Enumerable.Range(0, 5).Select(i => Run(i, 3600)).ToList(), p).Confidence);
        var current = Run(10, 3600, 900);
        var history = new[] { Run(1, 3600, 300), Run(2, 3600, 300), Run(3, 3600, 300) };
        var cmp = Compare.Build(current, history, p);
        Assert.Equal("Total time", cmp.Rows[0].Label);
        Assert.Equal("Active time", cmp.Rows[1].Label);
        Assert.Contains(cmp.Improvements, t => t.Contains("longer than your median") && t.Contains("10m"));
    }

    private static RunSummary Run(int day, double total, double sub = 600) =>
        new($"r{day}", new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero).AddDays(day), total, total * 0.9, 80, 10,
            [new SubtaskSummary("Draft", sub, null)]);

    [Fact]
    public void Default_subtask_names_follow_the_language_at_creation()
    {
        string Name(string lang)
        {
            using var _ = Loc.Use(lang);
            var r = ScenarioRunner.Run("at 00:00 start_task category=x\nat 00:10 next_subtask\nat 00:20 end_task");
            return r.Session.State.Subtasks[0].Name + "|" + r.Session.State.Subtasks[1].Name;
        }
        Assert.Equal("サブタスク1|サブタスク2", Name("ja"));
        Assert.Equal("Subtask 1|Subtask 2", Name("en"));
    }

    // ---------------- 既定カテゴリ・誘いかけの一言 ----------------

    [Fact]
    public void Default_category_is_displayed_per_language_and_mapped_back()
    {
        Assert.Equal("割込み作業", InterruptCategories.Display(InterruptCategories.Default));
        Assert.Equal("営業", InterruptCategories.Display("営業"));      // 利用者が作った名前はそのまま
        using (Loc.Use("en"))
        {
            Assert.Equal("Interruptions", InterruptCategories.Display(InterruptCategories.Default));
            Assert.Equal("Sales", InterruptCategories.Display("Sales"));
            // どちらの言語で表示された既定の名前も、保存する名前(内部の既定)に戻る
            Assert.Equal(InterruptCategories.Default, InterruptCategories.Canonical("Interruptions"));
            Assert.Equal(InterruptCategories.Default, InterruptCategories.Canonical(" 割込み作業 "));
            Assert.Equal(InterruptCategories.Default, InterruptCategories.Canonical(""));
            Assert.Equal("Sales", InterruptCategories.Canonical(" Sales "));
        }
    }

    [Fact]
    public void Reentry_message_unset_or_legacy_default_uses_the_language_default()
    {
        Assert.Equal("40秒で、元の作業に集中しましょう", ReentryPolicy.Message(null, 40));
        Assert.Equal("40秒で、元の作業に集中しましょう", ReentryPolicy.Message(ReentryPolicy.LegacyDefaultMessage, 40));
        using (Loc.Use("en"))
        {
            Assert.Equal("Focus on your task for 40 seconds.", ReentryPolicy.Message(null, 40));
            Assert.Equal("Focus on your task for 40 seconds.", ReentryPolicy.Message(ReentryPolicy.LegacyDefaultMessage, 40));   // 0.12A で保存された既定文
            Assert.Equal("Go: 40", ReentryPolicy.Message("Go: {sec}", 40));    // 利用者が書いた文はそのまま
            Assert.Equal("", ReentryPolicy.Message("  ", 40));                  // 空白だけなら出さない
        }
    }

    // ---------------- 日本語の直書きの取りこぼし防止 ----------------

    /// <summary>
    /// 画面に出す日本語は辞書(Strings/*.json)に置く。ソースに日本語の文字列リテラルが残っていないことを調べる(移行の取りこぼし防止)。
    /// 許可: コメント、内部で使う固定の値(既定カテゴリの内部名・0.12A の既定文・ブラウザのシークレット画面を除外する正規表現)。
    /// </summary>
    [Fact]
    public void No_japanese_string_literals_remain_in_source()
    {
        var allowed = new[] { "public const string Default =", "public const string LegacyDefaultMessage =", @"InPrivate|シークレット|Incognito" };
        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(TestPaths.RepoRoot, "src"), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var inComment = false;
            var n = 0;
            foreach (var raw in File.ReadLines(file))
            {
                n++;
                var line = raw;
                if (file.EndsWith(".xaml"))
                {
                    // XAML のコメント(複数行あり)は読み飛ばす
                    if (inComment) { if (line.Contains("-->")) inComment = false; continue; }
                    if (line.Contains("<!--") && !line.Contains("-->")) { inComment = true; continue; }
                    line = Regex.Replace(line, "<!--.*?-->", "");
                    if (Regex.IsMatch(line, "\"[^\"{]*[぀-ヿ一-鿿][^\"]*\"") && !line.Contains("{loc:T")) offenders.Add($"{Path.GetFileName(file)}:{n}: {raw.Trim()}");
                    continue;
                }
                var code = Regex.Replace(line, @"//.*$", "");
                if (!Japanese.IsMatch(code) || !code.Contains('"')) continue;
                if (allowed.Any(a => code.Contains(a))) continue;
                offenders.Add($"{Path.GetFileName(file)}:{n}: {raw.Trim()}");
            }
        }
        Assert.True(offenders.Count == 0, "Japanese string literals remain (move them to Strings/*.json):\n" + string.Join("\n", offenders));
    }
}
