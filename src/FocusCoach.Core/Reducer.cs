using System.Text.RegularExpressions;

namespace FocusCoach.Core;

/// <summary>状態遷移はここだけ。副作用なし。リプレイも同じ関数を通す。</summary>
public static partial class Reducer
{
    public static void Apply(RunState s, CoreEvent e)
    {
        s.EventCount++;
        s.LastEventAt = e.Ts;
        switch (e.Type)
        {
            case Ev.SessionStart:
                s.RunId = e.Get("run_id", s.RunId);
                foreach (var (k, v) in e.Data)
                    if (k.StartsWith("param.", StringComparison.Ordinal))
                        s.Params.TrySet(k[6..], v, out _, out _);
                break;

            case Ev.TaskStarted:
                s.Status = RunStatus.Running;
                s.StartedAt = e.Ts;
                s.Category = e.Get("category");
                s.Project = e.Get("project");
                s.TargetSec = e.Has("target_sec") ? e.GetNum("target_sec") : null;
                s.FirstStep = e.Get("first_step");
                foreach (var app in e.Get("task_apps").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    s.TaskApps.Add(TaskWindowClassifier.NormalizeProcess(app));
                break;

            case Ev.SubtaskAdded:
                s.Subtasks.Add(new SubtaskState
                {
                    Index = s.Subtasks.Count,
                    Name = e.Get("name"),
                    TargetSec = e.Has("target_sec") ? e.GetNum("target_sec") : null,
                });
                break;

            case Ev.SubtaskStarted:
            {
                var idx = (int)e.GetNum("index");
                CloseActiveInterval(s, e.Ts);
                // [仮案-02] goto で別のサブタスクへ移るとき、元が未完了なら Pending に戻す(後で戻れる)
                if (s.Active is { } prev && prev.Status == SubtaskStatus.Active) prev.Status = SubtaskStatus.Pending;
                s.ActiveIndex = idx;
                var st = s.Subtasks[idx];
                st.Status = SubtaskStatus.Active;
                st.Intervals.Add(new TimeSpanRec { Start = e.Ts });
                break;
            }

            case Ev.SubtaskCompleted:
            case Ev.SubtaskSkipped:
            {
                var idx = (int)e.GetNum("index");
                CloseActiveInterval(s, e.Ts);
                s.Subtasks[idx].Status = e.Type == Ev.SubtaskCompleted ? SubtaskStatus.Done : SubtaskStatus.Skipped;
                if (s.ActiveIndex == idx) s.ActiveIndex = -1;
                break;
            }

            case Ev.Paused:
                s.Pauses.Add(new PauseRec { Start = e.Ts, Reason = e.Get("reason", "other") });
                break;

            case Ev.Resumed:
                if (s.OpenPause is { } p) p.End = e.Ts;
                break;

            case Ev.FocusChanged:
                if (s.CurrentForeground is { } cur) cur.End = e.Ts;
                s.Foreground.Add(new ForegroundSeg
                {
                    Start = e.Ts,
                    Process = e.Get("process"),
                    Title = e.Get("title"),
                    Hwnd = e.Get("hwnd"),
                    TaskWindow = e.GetBool("task_window"),
                });
                break;

            case Ev.IdleStarted:
                s.OpenIdleFrom = e.GetTime("last_input_at", e.Ts);
                break;

            case Ev.IdleEnded:
                if (s.OpenIdleFrom is { } from)
                {
                    if ((e.Ts - from).TotalSeconds >= s.Params.IdleThresholdSec)
                        s.IdleSpans.Add(new TimeSpanRec { Start = from, End = e.Ts });
                    s.OpenIdleFrom = null;
                }
                break;

            case Ev.WindowOpened:
                if (s.OpenedDuring.All(w => w.Hwnd != e.Get("hwnd")))
                    s.OpenedDuring.Add(new OpenedWindow
                    {
                        Hwnd = e.Get("hwnd"), Process = e.Get("process"), Title = e.Get("title"), OpenedAt = e.Ts,
                    });
                break;

            case Ev.WindowClosed:
                foreach (var w in s.OpenedDuring.Where(w => w.Hwnd == e.Get("hwnd") && w.ClosedAt is null))
                    w.ClosedAt = e.Ts;
                break;

            case Ev.ResourceSeen:
            {
                var kind = e.Get("kind");
                var loc = Normalize(kind, e.Get("location"));
                var key = kind + "|" + loc;
                if (s.Resources.TryGetValue(key, out var r))
                {
                    r.LastSeen = e.Ts;
                    if (e.Has("name")) r.DisplayName = e.Get("name");
                }
                else
                {
                    s.Resources[key] = new ResourceRec
                    {
                        Kind = kind, Location = loc, DisplayName = e.Get("name", FileNameOf(loc)),
                        Source = e.Get("source"), FirstSeen = e.Ts, LastSeen = e.Ts,
                        SubtaskIndex = s.ActiveIndex >= 0 ? s.ActiveIndex : null,
                    };
                }
                break;
            }

            case Ev.NoteAdded:
                s.Notes.Add(new NoteRec(e.Ts, s.ActiveIndex >= 0 ? s.ActiveIndex : null, e.Get("text")));
                break;

            case Ev.InterruptMarked:
                s.Interrupts.Add(new InterruptRec(e.Ts, e.Get("kind", "now"), e.Get("note"), s.ActiveIndex >= 0 ? s.ActiveIndex : null));
                if (s.Active is { } a && e.Get("note").Length > 0) a.ResumeNote = e.Get("note");
                break;

            case Ev.InterruptDeferred:
                s.Deferred.Add(e.Get("text"));
                s.Interrupts.Add(new InterruptRec(e.Ts, "defer", e.Get("text"), s.ActiveIndex >= 0 ? s.ActiveIndex : null));
                break;

            case Ev.InterruptClosed:
                s.InterruptCloses.Add(new InterruptCloseRec(e.Ts, e.Get("category"), e.Get("title"), e.Get("outcome"), e.Get("followup"), e.Get("due"),
                    e.GetNum("interrupt_sec", 0), e.Get("via", "f9"), (int)e.GetNum("guide_sec", 0)));
                break;

            case Ev.ReentryEnded:
                s.Reentries.Add(new ReentryRec(e.Ts, e.Get("how"), e.GetNum("shown_sec", 0), (int)e.GetNum("extends", 0)));
                break;

            case Ev.TrayItemAdded:
                s.Tray.Add(new TrayItem(e.Ts, s.ActiveIndex >= 0 ? s.ActiveIndex : null, e.Get("kind"), e.Get("ref"), e.Get("note")));
                break;

            case Ev.ResumeCardShown:
                break; // 表示の記録のみ。状態は変えない。

            case Ev.InterruptNoteAdded:
            {
                var idx = (int)e.GetNum("index", -1);
                if (idx >= 0 && idx < s.Subtasks.Count)
                {
                    // 空の行は「変更しない」(どちらか1行だけ書き直せる)
                    if (e.Get("text").Length > 0) s.Subtasks[idx].ResumeNote = e.Get("text");
                    if (e.Get("next").Length > 0) s.Subtasks[idx].NextStep = e.Get("next");
                }
                break;
            }

            case Ev.AwayReclassified:
            {
                // 過去の区間を割り込みに分類し直す(09章 §9.4.1a)。区間は Session が検証済み。
                var rFrom = e.GetTime("from", e.Ts);
                var rTo = e.GetTime("to", e.Ts);
                var idx = (int)e.GetNum("index", -1);
                if (e.Get("target") == "idle")
                {
                    s.IdleSpans.RemoveAll(x => x.Start == rFrom && x.End == rTo);
                    InsertPause(s, new PauseRec { Start = rFrom, End = rTo, Reason = "interrupt" });
                }
                else
                {
                    foreach (var rp in s.Pauses.Where(x => x.Start == rFrom)) rp.Reason = "interrupt";
                }
                s.Interrupts.Add(new InterruptRec(rFrom, "now", "", idx >= 0 ? idx : null));
                break;
            }

            case Ev.WsSnapshot:
                s.WsSnapshots[e.Get("phase", "start")] = (e.Get("boot_id"), e.Get("windows", "[]"));
                break;

            case Ev.WsMinimize:
            case Ev.WsRestore:
            case Ev.WsClose:
                break; // 操作結果の記録のみ

            case Ev.TaskSuspended:
                // 持ち越し(F-15)。サブタスクの区間を閉じ、中断の一時停止を開く。どのサブタスクだったかは覚えておく。
                s.SuspendedActiveIndex = s.ActiveIndex;
                CloseActiveInterval(s, e.Ts);
                if (s.OpenPause is { } sp) sp.End = e.Ts;
                if (s.CurrentForeground is { } sf) sf.End = e.Ts;
                CloseIdle(s, e.Ts);
                s.Pauses.Add(new PauseRec { Start = e.Ts, Reason = "suspend" });
                s.Status = RunStatus.Suspended;
                break;

            case Ev.SessionResumed:
                s.ResumeCount++;
                if (e.Get("reason") == "next_day")
                {
                    if (s.OpenPause is { Reason: "suspend" } susp) susp.End = e.Ts;
                    s.Status = RunStatus.Running;
                    if (s.SuspendedActiveIndex >= 0 && s.SuspendedActiveIndex < s.Subtasks.Count)
                    {
                        s.ActiveIndex = s.SuspendedActiveIndex;
                        s.Subtasks[s.ActiveIndex].Intervals.Add(new TimeSpanRec { Start = e.Ts });
                    }
                    s.SuspendedActiveIndex = -1;
                }
                else
                {
                    // 異常終了(crash): 最後のイベントから再開までの空白を一時停止として挿入する(F-16)
                    var gapFrom = e.GetTime("gap_from", e.Ts);
                    if (e.Ts > gapFrom) InsertPause(s, new PauseRec { Start = gapFrom, End = e.Ts, Reason = "crash" });
                    s.Status = RunStatus.Running;
                }
                break;

            case Ev.ParamSet:
                s.Params.TrySet(e.Get("name"), e.Get("new"), out _, out _);
                break;

            case Ev.TaskEnded:
            case Ev.TaskAborted:
                s.EndedAt = e.Ts;
                s.Status = e.Type == Ev.TaskEnded ? RunStatus.Completed : RunStatus.Aborted;
                CloseActiveInterval(s, e.Ts);
                if (s.OpenPause is { } op) op.End = e.Ts;
                if (s.CurrentForeground is { } f) f.End = e.Ts;
                CloseIdle(s, e.Ts);
                break;
        }
    }

    public static RunState Replay(IEnumerable<CoreEvent> events)
    {
        var s = new RunState();
        foreach (var e in events) Apply(s, e);
        return s;
    }

    private static void CloseIdle(RunState s, DateTimeOffset at)
    {
        if (s.OpenIdleFrom is { } idle)
        {
            if ((at - idle).TotalSeconds >= s.Params.IdleThresholdSec)
                s.IdleSpans.Add(new TimeSpanRec { Start = idle, End = at });
            s.OpenIdleFrom = null;
        }
    }

    /// <summary>開始時刻の順を保って一時停止を挿入する(後から分かった区間を入れるため)。</summary>
    private static void InsertPause(RunState s, PauseRec p)
    {
        var i = s.Pauses.FindIndex(x => x.Start > p.Start);
        if (i < 0) s.Pauses.Add(p); else s.Pauses.Insert(i, p);
    }

    private static void CloseActiveInterval(RunState s, DateTimeOffset at)
    {
        if (s.Active is { } a && a.Intervals.Count > 0 && a.Intervals[^1].End is null)
            a.Intervals[^1].End = at;
    }

    /// <summary>
    /// 場所の正規化(docs/spec/04: 大文字小文字、末尾区切り、URLのフラグメント)。
    /// [仮案-03] パスは小文字化して比較する(Windows前提)。URLはホスト部のみ小文字化したいが、簡易に全体を小文字化せず、フラグメントと末尾スラッシュだけ除く。
    /// </summary>
    public static string Normalize(string kind, string location)
    {
        var l = location.Trim();
        if (kind == "url")
        {
            var hash = l.IndexOf('#');
            if (hash >= 0) l = l[..hash];
            return l.TrimEnd('/');
        }
        l = l.Replace('/', '\\').TrimEnd('\\');
        return l.ToLowerInvariant();
    }

    /// <summary>
    /// 末尾の名前を返す。Core は OS 非依存なので Path.GetFileName は使わない(Linux では '\' が区切りにならない)。
    /// </summary>
    public static string FileNameOf(string location)
    {
        var t = location.TrimEnd('/', '\\');
        var i = t.LastIndexOfAny(['/', '\\']);
        return i >= 0 ? t[(i + 1)..] : t;
    }

    /// <summary>"名前" または "名前:分" をカンマ/読点で区切った一覧を解析する。</summary>
    public static List<(string Name, double? TargetSec)> ParseSubtaskList(string spec)
    {
        var res = new List<(string, double?)>();
        foreach (var part in SplitRegex().Split(spec).Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            var i = part.LastIndexOf(':');
            if (i > 0 && double.TryParse(part[(i + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var min))
                res.Add((part[..i].Trim(), min * 60));
            else
                res.Add((part, null));
        }
        return res;
    }

    [GeneratedRegex("[,、，]")]
    private static partial Regex SplitRegex();
}
