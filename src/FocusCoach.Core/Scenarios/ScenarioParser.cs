using System.Globalization;
using System.Text;

namespace FocusCoach.Core.Scenarios;

public abstract record ScenarioStep(int Line, string Text);
public sealed record CommandStep(int Line, string Text, TimeSpan At, string Command, IReadOnlyDictionary<string, string> Args, bool MustReject) : ScenarioStep(Line, Text);
public sealed record ExpectStep(int Line, string Text, TimeSpan? At, string Path, string Op, string Value) : ScenarioStep(Line, Text);

public sealed class ScenarioParseException(int line, string message) : Exception($"line {line}: {message}")
{
    public int Line { get; } = line;
}

/// <summary>
/// シナリオDSL(08章 §8.2)。1行1ステップ。
///   at HH:MM[:SS] <command> key=value ...      コマンド(時刻は開始からの仮想時刻)
///   at HH:MM[:SS] !<command> ...                コマンドが拒否されることを期待する
///   expect <path> <op> <value>                  期待値(op: == != &gt;= &lt;= &gt; &lt; ~=)
///   set name=value                              パラメータ変更(= set コマンド)
/// '#' 以降はコメント(引用符の外のみ)。
/// </summary>
public static class ScenarioParser
{
    public static List<ScenarioStep> Parse(string text)
    {
        var steps = new List<ScenarioStep>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = StripComment(lines[i]).Trim();
            if (raw.Length == 0) continue;
            var no = i + 1;
            var tok = Tokenize(raw, no);
            if (tok.Count == 0) continue;

            TimeSpan? at = null;
            var idx = 0;
            if (tok[0] == "at")
            {
                if (tok.Count < 3) throw new ScenarioParseException(no, "at needs a time and a command");
                at = ParseTime(tok[1], no);
                idx = 2;
            }

            var head = tok[idx];
            if (head == "expect")
            {
                if (tok.Count - idx < 4) throw new ScenarioParseException(no, "expect <path> <op> <value>");
                steps.Add(new ExpectStep(no, raw, at, tok[idx + 1], tok[idx + 2], string.Join(' ', tok.Skip(idx + 3))));
                continue;
            }

            if (at is null) throw new ScenarioParseException(no, "command needs 'at <time>'");
            var reject = head.StartsWith('!');
            if (reject) head = head[1..];
            var args = new Dictionary<string, string>();
            var rest = tok.Skip(idx + 1).ToList();
            if (head == "set")
            {
                // set name=value
                if (rest.Count != 1 || !rest[0].Contains('=')) throw new ScenarioParseException(no, "set name=value");
                var eq = rest[0].IndexOf('=');
                args["name"] = rest[0][..eq];
                args["value"] = rest[0][(eq + 1)..];
            }
            else
            {
                foreach (var t in rest)
                {
                    var eq = t.IndexOf('=');
                    if (eq <= 0) throw new ScenarioParseException(no, $"expected key=value: {t}");
                    args[t[..eq]] = t[(eq + 1)..];
                }
            }
            steps.Add(new CommandStep(no, raw, at.Value, head, args, reject));
        }
        return steps;
    }

    public static TimeSpan ParseTime(string t, int line)
    {
        var p = t.Split(':');
        if (p.Length is < 2 or > 3 || !int.TryParse(p[0], out var h) || !int.TryParse(p[1], out var m))
            throw new ScenarioParseException(line, $"bad time: {t}");
        var s = p.Length == 3 && int.TryParse(p[2], out var ss) ? ss : 0;
        return new TimeSpan(h, m, s);
    }

    /// <summary>"90", "12m", "1h30m", "45s" → 秒。数値でなければ null。</summary>
    public static double? ParseQuantity(string v)
    {
        v = v.Trim();
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain)) return plain;
        double total = 0;
        var num = new StringBuilder();
        var any = false;
        foreach (var c in v)
        {
            if (char.IsDigit(c) || c == '.') num.Append(c);
            else if (c is 'h' or 'm' or 's' && num.Length > 0 &&
                     double.TryParse(num.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            {
                total += c switch { 'h' => n * 3600, 'm' => n * 60, _ => n };
                num.Clear();
                any = true;
            }
            else return null;
        }
        return any && num.Length == 0 ? total : null;
    }

    private static string StripComment(string line)
    {
        var inQuote = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuote = !inQuote;
            else if (line[i] == '#' && !inQuote && (i == 0 || char.IsWhiteSpace(line[i - 1]))) return line[..i];
        }
        return line;
    }

    /// <summary>空白区切り。key="a b" のように引用符で囲んだ値は1トークンにする(引用符は外す)。</summary>
    private static List<string> Tokenize(string s, int line)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        var inQuote = false;
        var has = false;
        foreach (var c in s)
        {
            if (c == '"') { inQuote = !inQuote; has = true; continue; }
            if (char.IsWhiteSpace(c) && !inQuote)
            {
                if (has) { res.Add(sb.ToString()); sb.Clear(); has = false; }
                continue;
            }
            sb.Append(c);
            has = true;
        }
        if (inQuote) throw new ScenarioParseException(line, "unterminated quote");
        if (has) res.Add(sb.ToString());
        return res;
    }
}
