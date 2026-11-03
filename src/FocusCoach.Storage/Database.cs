using System.Globalization;
using System.Text.Json;
using FocusCoach.Core;
using Microsoft.Data.Sqlite;

namespace FocusCoach.Storage;

/// <summary>
/// SQLite の薄いリポジトリ層(ORM なし。07章 §7.1)。スキーマは docs/spec/04 に対応する。
/// [仮案-14] 今回のプロトタイプで実装したのは種別・実行・サブタスク・一時停止・アクセス記録・メモ・指標のみ。
/// window_records / settings / exclusion_rules / category_change_log は未実装。
/// </summary>
public sealed class FocusCoachDb : IDisposable
{
    public const int SchemaVersion = 6;
    private readonly SqliteConnection _c;

    public FocusCoachDb(string path)
    {
        if (path != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // 外部キー制約は既定値に頼らず明示する(R-40)
        _c = new SqliteConnection($"Data Source={path};Foreign Keys=True");
        _c.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Migrate();
    }

    public int UserVersion => (int)(long)Scalar("PRAGMA user_version;")!;

    private void Migrate()
    {
        if (UserVersion < 1) MigrateV1();
        if (UserVersion < 2)
        {
            // v2(2026-10-04): 指標の式 v2 で追加した列
            Exec("ALTER TABLE run_metrics ADD COLUMN work_days INTEGER NOT NULL DEFAULT 1;");
            Exec("ALTER TABLE run_metrics ADD COLUMN same_day INTEGER NOT NULL DEFAULT 1;");
            Exec("PRAGMA user_version=2;");
        }
        if (UserVersion < 3)
        {
            // v3(2026-10-04): 着手の一手(I-05)と次の一手(I-01)
            Exec("ALTER TABLE task_runs ADD COLUMN first_step TEXT NULL;");
            Exec("ALTER TABLE subtask_runs ADD COLUMN next_step TEXT NULL;");
            Exec("PRAGMA user_version=3;");
        }
        if (UserVersion < 4)
        {
            // v4(2026-10-04): 手順エディタ(05 F-01)。登録した手順に目安(分)を持たせる
            Exec("ALTER TABLE subtask_templates ADD COLUMN target_min REAL NULL;");
            Exec("PRAGMA user_version=4;");
        }
        if (UserVersion < 5)
        {
            // v5(2026-10-06): 開始準備の入力の下書き(05 F-01、design-0.10A-usability.md R2)。1タスクに1つ
            Exec("""
                ALTER TABLE task_categories ADD COLUMN draft_project TEXT NULL;
                ALTER TABLE task_categories ADD COLUMN draft_steps TEXT NULL;
                ALTER TABLE task_categories ADD COLUMN draft_task_apps TEXT NULL;
                ALTER TABLE task_categories ADD COLUMN draft_first_step TEXT NULL;
                ALTER TABLE task_categories ADD COLUMN last_used_at TEXT NULL;
                PRAGMA user_version=5;
                """);
        }
        if (UserVersion < 6)
        {
            // v6(2026-10-29): 割り込みの出口カードとあとでやること(design-0.12A-interrupt-closure.md §6.2)
            Exec("""
                CREATE TABLE IF NOT EXISTS interrupt_categories(
                  id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE, archived INTEGER NOT NULL DEFAULT 0,
                  created_at TEXT NOT NULL, last_used_at TEXT NULL);
                CREATE TABLE IF NOT EXISTS interrupt_records(
                  id INTEGER PRIMARY KEY, run_id TEXT NULL, started_at TEXT NOT NULL, ended_at TEXT NOT NULL,
                  category_id INTEGER NULL REFERENCES interrupt_categories(id), title TEXT NOT NULL DEFAULT '', outcome TEXT NOT NULL,
                  reentry_how TEXT NULL, reentry_sec REAL NULL);
                CREATE TABLE IF NOT EXISTS followups(
                  id INTEGER PRIMARY KEY, created_at TEXT NOT NULL, source TEXT NOT NULL, text TEXT NOT NULL, due_date TEXT NULL,
                  status TEXT NOT NULL DEFAULT 'open', done_at TEXT NULL, category_id INTEGER NULL REFERENCES interrupt_categories(id),
                  interrupt_id INTEGER NULL REFERENCES interrupt_records(id), title TEXT NOT NULL DEFAULT '', task_name TEXT NULL,
                  windows_json TEXT NULL, updated_at TEXT NOT NULL);
                """);
            // 既定のカテゴリは1つだけ(新しいメンバーにも分かる一般的な初期設定。design-0.12A D2・D6)
            using (var seed = Cmd("INSERT INTO interrupt_categories(name, created_at) VALUES($n,$t) ON CONFLICT(name) DO NOTHING;",
                ("$n", InterruptCategories.Default), ("$t", DateTimeOffset.Now.ToString("O"))))
                seed.ExecuteNonQuery();
            Exec("PRAGMA user_version=6;");
        }
    }

    private void MigrateV1()
    {
        Exec("""
            CREATE TABLE task_categories(
              id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE, description TEXT NOT NULL DEFAULT '',
              merged_into_id INTEGER NULL, archived INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE subtask_templates(
              id INTEGER PRIMARY KEY, category_id INTEGER NOT NULL REFERENCES task_categories(id), seq INTEGER NOT NULL,
              name TEXT NOT NULL, aliases TEXT NOT NULL DEFAULT '[]', active INTEGER NOT NULL DEFAULT 1, UNIQUE(category_id, name));
            CREATE TABLE task_runs(
              id TEXT PRIMARY KEY, category_id INTEGER NOT NULL REFERENCES task_categories(id), project_name TEXT NOT NULL DEFAULT '',
              target_sec INTEGER NULL, started_at TEXT NOT NULL, ended_at TEXT NULL, status TEXT NOT NULL, app_version TEXT NOT NULL);
            CREATE TABLE subtask_runs(
              id INTEGER PRIMARY KEY, task_run_id TEXT NOT NULL REFERENCES task_runs(id) ON DELETE CASCADE, template_id INTEGER NULL,
              name TEXT NOT NULL, seq INTEGER NOT NULL, status TEXT NOT NULL,
              duration_sec REAL NOT NULL DEFAULT 0, search_sec REAL NULL, target_sec REAL NULL, resume_note TEXT NULL);
            CREATE TABLE subtask_intervals(
              subtask_run_id INTEGER NOT NULL REFERENCES subtask_runs(id) ON DELETE CASCADE, started_at TEXT NOT NULL, ended_at TEXT NULL);
            CREATE TABLE pauses(
              task_run_id TEXT NOT NULL REFERENCES task_runs(id) ON DELETE CASCADE, started_at TEXT NOT NULL, ended_at TEXT NULL, reason TEXT NOT NULL);
            CREATE TABLE resource_accesses(
              id INTEGER PRIMARY KEY, task_run_id TEXT NOT NULL REFERENCES task_runs(id) ON DELETE CASCADE, subtask_run_id INTEGER NULL,
              kind TEXT NOT NULL, location TEXT NOT NULL, display_name TEXT NOT NULL, source TEXT NOT NULL,
              first_seen_at TEXT NOT NULL, last_seen_at TEXT NOT NULL, launcher_pin INTEGER NULL);
            CREATE TABLE notes(
              id INTEGER PRIMARY KEY, task_run_id TEXT NOT NULL REFERENCES task_runs(id) ON DELETE CASCADE, subtask_run_id INTEGER NULL,
              created_at TEXT NOT NULL, kind TEXT NOT NULL DEFAULT 'text', text TEXT NOT NULL);
            CREATE TABLE run_metrics(
              task_run_id TEXT PRIMARY KEY REFERENCES task_runs(id) ON DELETE CASCADE, formula_version INTEGER NOT NULL,
              total_sec REAL, paused_sec REAL, idle_sec REAL, active_sec REAL, on_task_sec REAL, off_task_sec REAL,
              switch_count INTEGER, switch_per_hour REAL, focus_score REAL, timeline TEXT,
              interrupt_count INTEGER, deferred_count INTEGER, interrupt_paused_sec REAL);
            """);
        Exec("PRAGMA user_version=1;");
    }

    /// <summary>
    /// 実行の見出し行を作成・更新する。開始・中断・再開のたびに呼び、異常終了しても行が残るようにする(R-10、04 task_runs.status)。
    /// 終了時は SaveRun が全体を書き直す。
    /// </summary>
    public void UpsertRunHeader(RunState s)
    {
        var catId = EnsureCategory(s.Category);
        using var cmd = Cmd("""
            INSERT INTO task_runs(id,category_id,project_name,target_sec,started_at,ended_at,status,app_version,first_step)
            VALUES($id,$c,$p,$ts,$s,$e,$st,$v,$fs)
            ON CONFLICT(id) DO UPDATE SET status=excluded.status, ended_at=excluded.ended_at, category_id=excluded.category_id;
            """,
            ("$id", s.RunId), ("$c", catId), ("$p", s.Project), ("$ts", s.TargetSec is { } t ? (long)t : null),
            ("$s", s.StartedAt?.ToString("O")), ("$e", s.EndedAt?.ToString("O")), ("$st", s.Status.ToString().ToLowerInvariant()), ("$v", AppInfo.Version),
            ("$fs", s.FirstStep.Length > 0 ? s.FirstStep : null));
        cmd.ExecuteNonQuery();
    }

    /// <summary>異常終了のまま回復しなかった実行を interrupted にする(F-16)。</summary>
    public void MarkInterrupted(string runId)
    {
        using var cmd = Cmd("UPDATE task_runs SET status='interrupted' WHERE id=$id AND status IN ('running','suspended');", ("$id", runId));
        cmd.ExecuteNonQuery();
    }

    public string? GetStatus(string runId) => Scalar("SELECT status FROM task_runs WHERE id=$id;", ("$id", runId)) as string;

    /// <summary>種別を探し、無ければ作る。「一覧から外した」種別でも、使われたら一覧に戻す。</summary>
    public long EnsureCategory(string name)
    {
        var now = DateTimeOffset.Now.ToString("O");
        using var cmd = Cmd("INSERT INTO task_categories(name, created_at, updated_at) VALUES($n,$t,$t) ON CONFLICT(name) DO NOTHING;", ("$n", name), ("$t", now));
        cmd.ExecuteNonQuery();
        using var back = Cmd("UPDATE task_categories SET archived=0 WHERE name=$n AND archived=1;", ("$n", name));
        back.ExecuteNonQuery();
        return (long)Scalar("SELECT id FROM task_categories WHERE name=$n;", ("$n", name))!;
    }

    /// <summary>実行を保存する(状態と指標)。同じ run_id は置き換える。</summary>
    public void SaveRun(RunState s, RunMetrics m)
    {
        var catId = EnsureCategory(s.Category);
        using var tx = _c.BeginTransaction();
        using (var del = Cmd("DELETE FROM task_runs WHERE id=$id;", ("$id", s.RunId))) del.ExecuteNonQuery();

        using (var cmd = Cmd("INSERT INTO task_runs(id,category_id,project_name,target_sec,started_at,ended_at,status,app_version,first_step) VALUES($id,$c,$p,$ts,$s,$e,$st,$v,$fs);",
            ("$id", s.RunId), ("$c", catId), ("$p", s.Project), ("$ts", s.TargetSec is { } t ? (long)t : null),
            ("$s", s.StartedAt?.ToString("O")), ("$e", s.EndedAt?.ToString("O")), ("$st", s.Status.ToString().ToLowerInvariant()), ("$v", AppInfo.Version),
            ("$fs", s.FirstStep.Length > 0 ? s.FirstStep : null)))
            cmd.ExecuteNonQuery();

        var subIds = new Dictionary<int, long>();
        foreach (var st in s.Subtasks)
        {
            var sm = m.Subtasks.First(x => x.Index == st.Index);
            using var ins = Cmd("INSERT INTO subtask_runs(task_run_id,name,seq,status,duration_sec,search_sec,target_sec,resume_note,next_step) VALUES($r,$n,$q,$st,$d,$se,$tg,$rn,$ns); SELECT last_insert_rowid();",
                ("$r", s.RunId), ("$n", st.Name), ("$q", st.Index), ("$st", st.Status.ToString().ToLowerInvariant()),
                ("$d", sm.DurationSec), ("$se", sm.SearchSec), ("$tg", st.TargetSec), ("$rn", st.ResumeNote), ("$ns", st.NextStep));
            var id = (long)ins.ExecuteScalar()!;
            subIds[st.Index] = id;
            foreach (var iv in st.Intervals)
                using (var ci = Cmd("INSERT INTO subtask_intervals VALUES($i,$s,$e);", ("$i", id), ("$s", iv.Start.ToString("O")), ("$e", iv.End?.ToString("O"))))
                    ci.ExecuteNonQuery();
        }
        foreach (var p in s.Pauses)
            using (var cp = Cmd("INSERT INTO pauses VALUES($r,$s,$e,$re);", ("$r", s.RunId), ("$s", p.Start.ToString("O")), ("$e", p.End?.ToString("O")), ("$re", p.Reason)))
                cp.ExecuteNonQuery();
        foreach (var r in s.Resources.Values)
            using (var cr = Cmd("INSERT INTO resource_accesses(task_run_id,subtask_run_id,kind,location,display_name,source,first_seen_at,last_seen_at) VALUES($r,$sr,$k,$l,$d,$so,$f,$la);",
                ("$r", s.RunId), ("$sr", r.SubtaskIndex is { } si && subIds.TryGetValue(si, out var sid) ? sid : null), ("$k", r.Kind), ("$l", r.Location),
                ("$d", r.DisplayName), ("$so", r.Source), ("$f", r.FirstSeen.ToString("O")), ("$la", r.LastSeen.ToString("O"))))
                cr.ExecuteNonQuery();
        foreach (var n in s.Notes)
            using (var cn = Cmd("INSERT INTO notes(task_run_id,subtask_run_id,created_at,text) VALUES($r,$sr,$c,$t);",
                ("$r", s.RunId), ("$sr", n.SubtaskIndex is { } ni && subIds.TryGetValue(ni, out var nid) ? nid : null), ("$c", n.At.ToString("O")), ("$t", n.Text)))
                cn.ExecuteNonQuery();
        using (var cm = Cmd("""
            INSERT INTO run_metrics(task_run_id,formula_version,total_sec,paused_sec,idle_sec,active_sec,on_task_sec,off_task_sec,
              switch_count,switch_per_hour,focus_score,timeline,interrupt_count,deferred_count,interrupt_paused_sec,work_days,same_day)
            VALUES($r,$fv,$t,$p,$i,$a,$on,$off,$sw,$swh,$fs,$tl,$ic,$dc,$ips,$wd,$sd);
            """,
            ("$r", s.RunId), ("$fv", m.FormulaVersion), ("$t", m.TotalSec), ("$p", m.PausedSec), ("$i", m.IdleSec), ("$a", m.ActiveSec),
            ("$on", m.OnTaskSec), ("$off", m.OffTaskSec), ("$sw", m.SwitchCount), ("$swh", m.SwitchPerHour), ("$fs", m.FocusScore),
            ("$tl", JsonSerializer.Serialize(m.Timeline)), ("$ic", m.InterruptCount), ("$dc", m.DeferredCount), ("$ips", m.InterruptPausedSec),
            ("$wd", m.WorkDays), ("$sd", m.SameDay ? 1 : 0)))
            cm.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>
    /// 種別ごとの完了した実行の要約(ガイド・比較の入力)。新しい順。
    /// 統合された種別(merged_into_id)の実行は統合先にも含める。
    /// </summary>
    public List<RunSummary> LoadHistory(string category, int limit = 50)
    {
        var res = new List<RunSummary>();
        using var cmd = Cmd("""
            SELECT r.id, r.started_at, m.total_sec, m.active_sec, m.focus_score, m.switch_per_hour
            FROM task_runs r JOIN run_metrics m ON m.task_run_id=r.id JOIN task_categories c ON c.id=r.category_id
            WHERE r.status='completed' AND (c.name=$n OR c.merged_into_id=(SELECT id FROM task_categories WHERE name=$n))
            ORDER BY r.started_at DESC LIMIT $l;
            """, ("$n", category), ("$l", limit));
        var heads = new List<(string, DateTimeOffset, double, double, double, double)>();
        using (var rd = cmd.ExecuteReader())
            while (rd.Read())
                heads.Add((rd.GetString(0), DateTimeOffset.Parse(rd.GetString(1), CultureInfo.InvariantCulture), rd.GetDouble(2), rd.GetDouble(3), rd.GetDouble(4), rd.GetDouble(5)));
        foreach (var (id, started, total, active, score, swh) in heads)
        {
            var subs = new List<SubtaskSummary>();
            using var sc = Cmd("SELECT name, duration_sec, search_sec FROM subtask_runs WHERE task_run_id=$r AND status='done' ORDER BY seq;", ("$r", id));
            using var sr = sc.ExecuteReader();
            while (sr.Read()) subs.Add(new SubtaskSummary(sr.GetString(0), sr.GetDouble(1), sr.IsDBNull(2) ? null : sr.GetDouble(2)));
            res.Add(new RunSummary(id, started, total, active, score, swh, subs));
        }
        return res;
    }

    /// <summary>
    /// 同じ種別で、前回書いた「最初の一手」(05 F-02 の初期値)。状態は問わない(中断・異常終了の実行も含む)。
    /// [仮案-32] 「前回の次の一手」ではなく「前回の最初の一手」を初期値にする。完了した実行の次の一手は途中の状態なので、
    /// 新しい実行の着手には合わないため。持ち越しの終業メモ(I-04)を作ったら、そちらを優先する。
    /// </summary>
    public string? LoadLastFirstStep(string category) => Scalar("""
        SELECT r.first_step FROM task_runs r JOIN task_categories c ON c.id=r.category_id
        WHERE c.name=$n AND r.first_step IS NOT NULL AND r.first_step<>'' ORDER BY r.started_at DESC LIMIT 1;
        """, ("$n", category)) as string;

    /// <summary>
    /// 開始準備のプルダウンに出すタスク(05 F-01、03 S-02)。実行が1回でもある(中止・中断中を含む)、
    /// 手順を登録した、または下書きがあるタスク。統合済み・「一覧から外した」ものは出さない。
    /// 並びは最後に使った日時(実行の開始、下書きの保存)の新しい順で、同じなら名前順。
    /// </summary>
    public List<CategoryInfo> ListCategories()
    {
        var res = new List<CategoryInfo>();
        using var cmd = Cmd("""
            SELECT c.name,
              (SELECT COUNT(*) FROM task_runs r WHERE r.category_id=c.id AND r.status='completed') AS done,
              (SELECT MAX(r.started_at) FROM task_runs r WHERE r.category_id=c.id AND r.status='completed') AS last_done,
              (SELECT COUNT(*) FROM subtask_templates t WHERE t.category_id=c.id AND t.active=1) AS steps,
              (SELECT COUNT(*) FROM task_runs r WHERE r.category_id=c.id) AS runs,
              (SELECT MAX(r.started_at) FROM task_runs r WHERE r.category_id=c.id) AS last_run,
              c.last_used_at
            FROM task_categories c
            WHERE c.merged_into_id IS NULL AND c.archived=0
            """);
        using (var rd = cmd.ExecuteReader())
            while (rd.Read())
            {
                var done = rd.GetInt32(1);
                var steps = rd.GetInt32(3);
                var runs = rd.GetInt32(4);
                var draftAt = Parse(rd, 6);
                if (done == 0 && steps == 0 && runs == 0 && draftAt is null) continue;
                var lastRun = Parse(rd, 5);
                var lastUsed = new[] { lastRun, draftAt }.Where(x => x is not null).DefaultIfEmpty(null).Max();
                res.Add(new CategoryInfo(rd.GetString(0), done, Parse(rd, 2), steps > 0, draftAt is not null, lastUsed));
            }
        return res.OrderByDescending(c => c.LastUsed ?? DateTimeOffset.MinValue).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();

        static DateTimeOffset? Parse(SqliteDataReader rd, int i) =>
            rd.IsDBNull(i) ? null : DateTimeOffset.Parse(rd.GetString(i), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 開始準備の入力を下書きとして保存する(1タスクに1つ。上書き)。タスク名が空なら何もしない。
    /// 「一覧から外した」タスクでも、保存すると一覧に戻る(EnsureCategory)。
    /// </summary>
    public void SaveDraft(TaskDraft d)
    {
        var name = d.Category.Trim();
        if (name.Length == 0) return;
        EnsureCategory(name);
        using var cmd = Cmd("""
            UPDATE task_categories SET draft_project=$p, draft_steps=$s, draft_task_apps=$a, draft_first_step=$f, last_used_at=$t, updated_at=$t
            WHERE name=$n;
            """,
            ("$n", name), ("$p", d.Project), ("$s", StepList.Format(d.Steps)), ("$a", d.TaskApps), ("$f", d.FirstStep), ("$t", d.SavedAt.ToString("O")));
        cmd.ExecuteNonQuery();
    }

    /// <summary>下書き。無ければ null。</summary>
    public TaskDraft? LoadDraft(string category)
    {
        using var cmd = Cmd("""
            SELECT draft_project, draft_steps, draft_task_apps, draft_first_step, last_used_at FROM task_categories WHERE name=$n AND last_used_at IS NOT NULL;
            """, ("$n", category.Trim()));
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        string S(int i) => rd.IsDBNull(i) ? "" : rd.GetString(i);
        return new TaskDraft(category.Trim(), S(0), StepList.Parse(S(1)), S(2), S(3), DateTimeOffset.Parse(rd.GetString(4), CultureInfo.InvariantCulture));
    }

    /// <summary>プルダウンの一覧から外す(「一覧から外す」)。実行の記録・手順・下書きは消さない。同じ名前を使えば一覧に戻る。</summary>
    public void Archive(string category)
    {
        using var cmd = Cmd("UPDATE task_categories SET archived=1, updated_at=$t WHERE name=$n;", ("$n", category.Trim()), ("$t", DateTimeOffset.Now.ToString("O")));
        cmd.ExecuteNonQuery();
    }

    /// <summary>登録した手順(手順エディタで保存したもの)。無ければ空。</summary>
    public List<StepItem> LoadTemplate(string category)
    {
        var res = new List<StepItem>();
        using var cmd = Cmd("""
            SELECT t.name, t.target_min FROM subtask_templates t JOIN task_categories c ON c.id=t.category_id
            WHERE c.name=$n AND t.active=1 ORDER BY t.seq;
            """, ("$n", category));
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) res.Add(new StepItem(rd.GetString(0), rd.IsDBNull(1) ? null : rd.GetDouble(1)));
        return res;
    }

    /// <summary>手順を登録する(そのタスクの手順を丸ごと置き換える)。名前の重複は呼び出し側(StepList.Normalize)で弾いておく。</summary>
    public void SaveTemplate(string category, IReadOnlyList<StepItem> steps)
    {
        var catId = EnsureCategory(category);
        using var tx = _c.BeginTransaction();
        using (var del = Cmd("DELETE FROM subtask_templates WHERE category_id=$c;", ("$c", catId))) del.ExecuteNonQuery();
        for (var i = 0; i < steps.Count; i++)
            using (var ins = Cmd("INSERT INTO subtask_templates(category_id,seq,name,target_min) VALUES($c,$q,$n,$m);",
                ("$c", catId), ("$q", i), ("$n", steps[i].Name), ("$m", steps[i].TargetMin)))
                ins.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>そのタスクで最後に終了した実行の手順(名前と、そのとき設定した目安)。無ければ空。</summary>
    public List<StepItem> LoadLastRunSteps(string category)
    {
        var res = new List<StepItem>();
        using var cmd = Cmd("""
            SELECT s.name, s.target_sec FROM subtask_runs s
            WHERE s.task_run_id=(SELECT r.id FROM task_runs r JOIN task_categories c ON c.id=r.category_id
                                 WHERE c.name=$n AND r.status='completed' ORDER BY r.started_at DESC LIMIT 1)
            ORDER BY s.seq;
            """, ("$n", category));
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) res.Add(new StepItem(rd.GetString(0), rd.IsDBNull(1) ? null : rd.GetDouble(1) / 60));
        return res;
    }

    // ---------------- 割り込みのカテゴリ・記録・あとでやること(v6。design-0.12A §6.2) ----------------

    /// <summary>出口カードのカテゴリのプルダウン。最近使った順(使っていないものは作った順)。一覧から外したものは出さない。</summary>
    public List<string> ListInterruptCategories()
    {
        var res = new List<string>();
        using var cmd = Cmd("SELECT name FROM interrupt_categories WHERE archived=0 ORDER BY COALESCE(last_used_at, '') DESC, id;");
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) res.Add(rd.GetString(0));
        return res;
    }

    /// <summary>カテゴリを探し、無ければ作る。使われたら一覧に戻し、最終使用日時を更新する。空なら既定のカテゴリ。</summary>
    public long UseInterruptCategory(string name, DateTimeOffset at)
    {
        name = name.Trim() is { Length: > 0 } n ? n : InterruptCategories.Default;
        using (var ins = Cmd("INSERT INTO interrupt_categories(name, created_at) VALUES($n,$t) ON CONFLICT(name) DO NOTHING;", ("$n", name), ("$t", at.ToString("O"))))
            ins.ExecuteNonQuery();
        using (var up = Cmd("UPDATE interrupt_categories SET archived=0, last_used_at=$t WHERE name=$n;", ("$n", name), ("$t", at.ToString("O"))))
            up.ExecuteNonQuery();
        return (long)Scalar("SELECT id FROM interrupt_categories WHERE name=$n;", ("$n", name))!;
    }

    /// <summary>プルダウンの一覧から外す(記録は消さない。同じ名前を使えば戻る)。</summary>
    public void ArchiveInterruptCategory(string name)
    {
        using var cmd = Cmd("UPDATE interrupt_categories SET archived=1 WHERE name=$n;", ("$n", name.Trim()));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 閉じた割り込みを記録し、「あとでやること」があれば一緒に保存する(1つのトランザクション)。
    /// 出口カードを閉じた直後に呼ぶ(タスクの終了を待たないので、異常終了しても残る)。
    /// </summary>
    public (long InterruptId, long? FollowupId) SaveInterruptClose(InterruptCloseRecord r)
    {
        using var tx = _c.BeginTransaction();
        var catId = UseInterruptCategory(r.Category, r.EndedAt);
        long id;
        using (var ins = Cmd("""
            INSERT INTO interrupt_records(run_id,started_at,ended_at,category_id,title,outcome) VALUES($r,$s,$e,$c,$t,$o);
            SELECT last_insert_rowid();
            """, ("$r", r.RunId), ("$s", r.StartedAt.ToString("O")), ("$e", r.EndedAt.ToString("O")), ("$c", catId), ("$t", r.Title), ("$o", r.Outcome)))
            id = (long)ins.ExecuteScalar()!;
        long? fid = null;
        if (r.Followup is { Length: > 0 } text)
        {
            using var f = Cmd("""
                INSERT INTO followups(created_at,source,text,due_date,category_id,interrupt_id,title,task_name,windows_json,updated_at)
                VALUES($at,'interrupt',$x,$d,$c,$i,$t,$tn,$w,$at); SELECT last_insert_rowid();
                """, ("$at", r.EndedAt.ToString("O")), ("$x", text), ("$d", r.Due is { } d ? Followups.FormatDue(d) : null), ("$c", catId),
                ("$i", id), ("$t", r.Title), ("$tn", r.TaskName), ("$w", r.WindowsJson));
            fid = (long)f.ExecuteScalar()!;
        }
        tx.Commit();
        return (id, fid);
    }

    /// <summary>再入場ガイドの閉じ方と表示秒数を、割り込みの記録に付ける。</summary>
    public void SetReentry(long interruptId, string how, double shownSec)
    {
        using var cmd = Cmd("UPDATE interrupt_records SET reentry_how=$h, reentry_sec=$s WHERE id=$i;", ("$i", interruptId), ("$h", how), ("$s", shownSec));
        cmd.ExecuteNonQuery();
    }

    /// <summary>あとでやることを手で追加する(タスクを始めていなくてもよい)。</summary>
    public long AddFollowup(string text, DateOnly? due, string source, DateTimeOffset at, string? taskName = null)
    {
        using var cmd = Cmd("""
            INSERT INTO followups(created_at,source,text,due_date,task_name,updated_at) VALUES($at,$s,$x,$d,$tn,$at); SELECT last_insert_rowid();
            """, ("$at", at.ToString("O")), ("$s", source), ("$x", text.Trim()), ("$d", due is { } d ? Followups.FormatDue(d) : null), ("$tn", taskName));
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>未完了のもの全部と、doneDays 日以内に完了したもの(取り消しできるように)。削除したものは出さない。</summary>
    public List<FollowupItem> ListFollowups(DateTimeOffset now, int doneDays = 7)
    {
        var res = new List<FollowupItem>();
        using var cmd = Cmd("""
            SELECT f.id, f.text, f.due_date, COALESCE(c.name,''), f.title, COALESCE(f.task_name,''), f.created_at, f.status, f.done_at
            FROM followups f LEFT JOIN interrupt_categories c ON c.id=f.category_id
            WHERE f.status='open' OR (f.status='done' AND f.done_at >= $since);
            """, ("$since", now.AddDays(-doneDays).ToString("O")));
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            DateOnly? due = rd.IsDBNull(2) ? null : DateOnly.ParseExact(rd.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            res.Add(new FollowupItem(rd.GetInt64(0), rd.GetString(1), due, rd.GetString(3), rd.GetString(4), rd.GetString(5),
                DateTimeOffset.Parse(rd.GetString(6), CultureInfo.InvariantCulture), rd.GetString(7) == "done",
                rd.IsDBNull(8) ? null : DateTimeOffset.Parse(rd.GetString(8), CultureInfo.InvariantCulture)));
        }
        return res;
    }

    /// <summary>状態を変える: open(取り消し)/ done(完了)/ deleted(削除。論理削除)。</summary>
    public void SetFollowupStatus(long id, string status, DateTimeOffset at)
    {
        if (status is not ("open" or "done" or "deleted")) throw new ArgumentException($"bad status: {status}");
        using var cmd = Cmd("UPDATE followups SET status=$s, done_at=$d, updated_at=$t WHERE id=$i;",
            ("$i", id), ("$s", status), ("$d", status == "done" ? at.ToString("O") : null), ("$t", at.ToString("O")));
        cmd.ExecuteNonQuery();
    }

    public int CountRuns() => (int)(long)Scalar("SELECT COUNT(*) FROM task_runs;")!;

    public int CountResources(string runId) => (int)(long)Scalar("SELECT COUNT(*) FROM resource_accesses WHERE task_run_id=$r;", ("$r", runId))!;

    private void Exec(string sql)
    {
        using var cmd = _c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private object? Scalar(string sql, params (string, object?)[] ps)
    {
        using var cmd = Cmd(sql, ps);
        return cmd.ExecuteScalar();
    }

    private SqliteCommand Cmd(string sql, params (string Name, object? Value)[] ps)
    {
        var cmd = _c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }

    public void Dispose() => _c.Dispose();
}

/// <summary>
/// 開始準備のプルダウンの1行。LastCompleted は最後に終了した実行の開始時刻(終了した実行が無ければ null)。
/// LastUsed は最後に使った日時(実行の開始か下書きの保存の新しい方)。並びと表示の「最終」に使う。
/// </summary>
public sealed record CategoryInfo(string Name, int CompletedRuns, DateTimeOffset? LastCompleted, bool HasTemplate,
    bool HasDraft = false, DateTimeOffset? LastUsed = null);

/// <summary>出口カードで閉じた割り込みの保存内容(design-0.12A §6.2)。Followup が空なら「あとでやること」は作らない。</summary>
public sealed record InterruptCloseRecord(string? RunId, DateTimeOffset StartedAt, DateTimeOffset EndedAt, string Category, string Title,
    string Outcome, string Followup, DateOnly? Due, string? TaskName, string? WindowsJson);

/// <summary>開始準備の入力の下書き(タスクごとに1つ)。Steps は手順(名前と目安の分)。</summary>
public sealed record TaskDraft(string Category, string Project, List<StepItem> Steps, string TaskApps, string FirstStep, DateTimeOffset SavedAt);
