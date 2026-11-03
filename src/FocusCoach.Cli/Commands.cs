using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using FocusCoach.Core;
using FocusCoach.Core.Scenarios;
using FocusCoach.Core.Workspace;
using FocusCoach.Storage;

namespace FocusCoach.Cli;

public static class Commands
{
    private const string Usage = """
        fc <command>
          scenario <file> [--events <out.jsonl>] [--db <path>] [-v]   シナリオを仮想時刻で実行し、expect を検証する(失敗で終了コード1)
          replay <session.jsonl> [-v]                                  記録したイベントから状態と指標を再計算する
          guide --db <path> --category <name>                          過去実績に基づく開始前ガイドを表示する
          workspace plan <windows.json> --mode start|resume|finish [--snapshot <windows.json>] [--project <name>]
                                                                       ウィンドウ列挙のJSONから、行う予定の操作を一覧表示する(実際には動かさない)
          diag [--data-root <dir>]                                     環境の診断(個人データは出力しない)
        """;

    public static Task<int> RunAsync(string[] args, TextWriter? output = null)
    {
        var old = Console.Out;
        if (output is not null) Console.SetOut(output);
        try { return Task.FromResult(Run(args)); }
        finally { if (output is not null) Console.SetOut(old); }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help") { Console.WriteLine(Usage); return args.Length == 0 ? 2 : 0; }
        try
        {
            return args[0] switch
            {
                "scenario" => Scenario(args[1..]),
                "replay" => Replay(args[1..]),
                "guide" => GuideCmd(args[1..]),
                "workspace" => WorkspaceCmd(args[1..]),
                "diag" => Diag(args[1..]),
                _ => Fail($"unknown command: {args[0]}\n{Usage}"),
            };
        }
        catch (ScenarioParseException ex) { return Fail("scenario parse error: " + ex.Message); }
        catch (FileNotFoundException ex) { return Fail("file not found: " + ex.FileName); }
    }

    private static int Fail(string msg) { Console.Error.WriteLine(msg); return 2; }

    private static string? Opt(string[] a, string name)
    {
        var i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    public static string Hms(double sec)
    {
        var t = TimeSpan.FromSeconds(Math.Round(sec));
        return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
    }

    private static int Scenario(string[] a)
    {
        if (a.Length == 0) return Fail("scenario: file is required");
        var verbose = a.Contains("-v");
        var text = File.ReadAllText(a[0]);
        var eventsOut = Opt(a, "--events");
        JsonlEventWriter? writer = null;
        if (eventsOut is not null)
        {
            if (File.Exists(eventsOut)) File.Delete(eventsOut);
            writer = new JsonlEventWriter(eventsOut);
        }
        ScenarioResult r;
        try { r = ScenarioRunner.Run(text, writer, runId: "scn-" + Path.GetFileNameWithoutExtension(a[0])); }
        finally { writer?.Dispose(); }

        if (verbose)
            foreach (var e in r.Events) Console.WriteLine($"  #{e.Seq,-3} {e.Ts:HH:mm:ss} {e.Type,-20} {string.Join(' ', e.Data.Select(kv => $"{kv.Key}={kv.Value}"))}");
        foreach (var err in r.Errors) Console.WriteLine("ERROR " + err);
        foreach (var c in r.Checks) Console.WriteLine($"{(c.Passed ? "PASS" : "FAIL")} line {c.Line}: {c.Text}{(c.Passed ? "" : "   -> " + c.Message)}");
        PrintMetrics(r.Metrics);

        var dbPath = Opt(a, "--db");
        if (dbPath is not null && r.Session.State.Status is RunStatus.Completed)
        {
            using var db = new FocusCoachDb(dbPath);
            db.SaveRun(r.Session.State, r.Metrics);
            Console.WriteLine($"saved to {dbPath} (runs={db.CountRuns()})");
        }
        Console.WriteLine(r.Passed ? $"OK ({r.Checks.Count} checks, {r.Events.Count} events)" : "NG");
        return r.Passed ? 0 : 1;
    }

    private static int Replay(string[] a)
    {
        if (a.Length == 0) return Fail("replay: file is required");
        var events = JsonlEventReader.Read(a[0]);
        if (events.Count == 0) return Fail("replay: no events");
        var state = Reducer.Replay(events);
        Console.WriteLine($"run_id={state.RunId} status={state.Status} category={state.Category} events={events.Count}");
        if (a.Contains("-v"))
            foreach (var e in events) Console.WriteLine($"  #{e.Seq,-3} {e.Ts:HH:mm:ss} {e.Type}");
        PrintMetrics(MetricsCalculator.Compute(state, events[^1].Ts));
        return 0;
    }

    private static void PrintMetrics(RunMetrics m)
    {
        Console.WriteLine($"-- metrics (formula v{m.FormulaVersion}) --");
        Console.WriteLine($"total {Hms(m.TotalSec)}  paused {Hms(m.PausedSec)}  idle {Hms(m.IdleSec)}  active {Hms(m.ActiveSec)}");
        Console.WriteLine($"on-task {Hms(m.OnTaskSec)}  off-task {Hms(m.OffTaskSec)}  switches {m.SwitchCount} ({m.SwitchPerHour:0.0}/h)  focus score {m.FocusScore:0.0}");
        Console.WriteLine($"interrupts now={m.InterruptCount} deferred={m.DeferredCount} paused-by-interrupt {Hms(m.InterruptPausedSec)}");
        foreach (var s in m.Subtasks)
            Console.WriteLine($"  [{s.Index + 1}] {s.Name,-20} {s.Status,-8} {Hms(s.DurationSec)}{(s.TargetSec is { } t ? $" / target {Hms(t)}" : "")}{(s.SearchSec is { } q ? $"  search {Hms(q)}" : "")}");
        Console.WriteLine("timeline " + string.Join(' ', m.Timeline.Select(x => x is null ? "-" : (x.Value * 100).ToString("0"))));
    }

    private static int GuideCmd(string[] a)
    {
        var dbPath = Opt(a, "--db");
        var cat = Opt(a, "--category");
        if (dbPath is null || cat is null) return Fail("guide: --db and --category are required");
        using var db = new FocusCoachDb(dbPath);
        var plan = Guide.Build(db.LoadHistory(cat), new ParamRegistry());
        Console.WriteLine($"samples={plan.SampleCount} confidence={plan.Confidence}");
        if (plan.TargetSec is null) { Console.WriteLine("no history: enter subtasks manually"); return 0; }
        Console.WriteLine($"median total {Hms(plan.MedianTotalSec!.Value)}  target {Hms(plan.TargetSec.Value)}");
        foreach (var s in plan.Subtasks) Console.WriteLine($"  {s.Name,-20} median {Hms(s.MedianSec)}  target {Hms(s.TargetSec)}");
        return 0;
    }

    private static readonly JsonSerializerOptions JsonOpt = new() { PropertyNameCaseInsensitive = true };

    private static int WorkspaceCmd(string[] a)
    {
        if (a.Length < 2 || a[0] != "plan") return Fail("workspace: usage: workspace plan <windows.json> --mode start|resume|finish");
        var windows = JsonSerializer.Deserialize<List<WindowInfo>>(File.ReadAllText(a[1]), JsonOpt) ?? [];
        var mode = Opt(a, "--mode") ?? "start";
        IReadOnlyList<WsStep> steps;
        switch (mode)
        {
            case "start":
                steps = WorkspacePlanner.PlanStart(windows);
                break;
            case "resume":
            {
                var snapPath = Opt(a, "--snapshot") ?? throw new ArgumentException("--snapshot is required for resume");
                var snap = JsonSerializer.Deserialize<List<WindowInfo>>(File.ReadAllText(snapPath), JsonOpt) ?? [];
                steps = WorkspacePlanner.PlanResume(snap, windows);
                break;
            }
            case "finish":
                steps = WorkspacePlanner.PlanFinish(windows, Opt(a, "--project") ?? "", Opt(a, "--last-active"));
                break;
            default:
                return Fail("workspace: --mode must be start|resume|finish");
        }
        Console.WriteLine("(dry-run: no window is touched)");
        foreach (var s in steps)
            Console.WriteLine($"{s.Action,-8} {s.Window.Process,-14} {Mask(s.Window.Title),-24} {s.Reason}");
        Console.WriteLine($"{steps.Count(s => s.Action != WsAction.Keep)} of {steps.Count} windows would change");
        return 0;
    }

    /// <summary>08章 §8.4: ログ・診断にタイトルを生で出さない。長さと先頭の1文字だけ。</summary>
    private static string Mask(string title) => title.Length == 0 ? "" : $"{title[0]}…({title.Length})";

    private static int Diag(string[] a)
    {
        var root = Opt(a, "--data-root") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusCoach");
        Console.WriteLine($"app_version   {AppInfo.Version} (formula v{AppInfo.FormulaVersion})");
        Console.WriteLine($"os            {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        Console.WriteLine($"dotnet        {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"windows       {OperatingSystem.IsWindows()}");
        Console.WriteLine($"data_root     exists={Directory.Exists(root)}");
        try
        {
            var di = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)) ?? "/");
            Console.WriteLine($"free_space_gb {di.AvailableFreeSpace / 1024 / 1024 / 1024}");
        }
        catch (Exception ex) { Console.WriteLine($"free_space_gb unknown ({ex.GetType().Name})"); }
        var dbPath = Path.Combine(root, "focuscoach.db");
        if (File.Exists(dbPath))
        {
            using var db = new FocusCoachDb(dbPath);
            Console.WriteLine($"db_schema     v{db.UserVersion} (expected v{FocusCoachDb.SchemaVersion}) runs={db.CountRuns()}");
        }
        else Console.WriteLine("db_schema     (no database)");
        Console.WriteLine($"params        {ParamRegistry.Definitions.Count} registered");
        // [仮案-15] Windows 固有の診断(Explorer COM / Recent / ブラウザ履歴 / モニタ / マイク)は Platform.Windows 側に実装し、
        // Cli から実行時に読み込む方式にする予定。現状は未実装。
        Console.WriteLine(OperatingSystem.IsWindows() ? "windows_diag  not implemented yet (TODO)" : "windows_diag  skipped (not Windows)");
        return 0;
    }
}
