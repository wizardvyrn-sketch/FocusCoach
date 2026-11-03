// ui_render_probe - 使い捨ての見た目確認ツール(Windows 専用)。
// アプリの画面(開始準備・作業中の各状態・復帰カード・離席の問いかけ・メモ・ウィンドウ整理)を、
// 画面に出さずに WPF の RenderTargetBitmap で PNG にする。画面のキャプチャは、画面共有からの除外(WDA_EXCLUDEFROMCAPTURE)や
// ロック中の画面では黒くなるため、この方法を使う。保存先は隔離(FOCUSCOACH_DATA)。
// 実行: dotnet run --project prototype/ui_render_probe -- <出力フォルダ>
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusCoach.App;
using FocusCoach.Core;
using FocusCoach.Core.Workspace;

static class P
{
    static string Out = AppContext.BaseDirectory;
    /// <summary>画面の例に出す架空のサンプルを、言語に合わせて選ぶ(ja / en)。</summary>
    static bool En;
    static string S(string ja, string en) => En ? en : ja;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    [STAThread]
    static void Main(string[] args)
    {
        // 使い方: ui_render_probe <出力フォルダ> [ja|en]  (言語を省略すると ja。0.13A)
        Out = args.Length > 0 ? args[0] : Out;
        var lang = args.Length > 1 ? args[1] : "ja";
        En = lang == "en";
        var data = Path.Combine(Out, "data");
        if (Directory.Exists(data)) Directory.Delete(data, true);
        Environment.SetEnvironmentVariable("FOCUSCOACH_DATA", data);
        SeedHistory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), $"{{ \"Language\": \"{lang}\" }}");   // MainWindow が起動時に読む
        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 起動時のロゴ画面(SPIRE。0.14A)。飛ばした状態(進捗線が伸びきり READY)を描く
        var splash = new SplashWindow { Left = -4000, Top = -4000, WindowStartupLocation = WindowStartupLocation.Manual };
        splash.Show();
        typeof(SplashWindow).GetMethod("Skip", F)!.Invoke(splash, null);
        Save(splash, "0_splash.png");
        splash.Close();

        var w = new MainWindow { Left = -4000, Top = -4000 };
        w.Show();
        Save(w, "1_start.png");   // 既定は空から(0.10A)
        Find<ComboBox>(w, "CategoryBox").Text = S("見積もり依頼", "Estimate request");   // 選ぶと、下書きの手順・プロジェクト・最初の一手が入る
        Save(w, "1a_start_selected.png");
        Find<ComboBox>(w, "CategoryBox").Text = "";

        // タスクのプルダウンを開いた状態(ポップアップは別ウィンドウなので、その中身を描く)
        var combo = Find<ComboBox>(w, "CategoryBox");
        combo.IsDropDownOpen = true;
        w.UpdateLayout();
        if (combo.Template.FindName("PART_Popup", combo) is System.Windows.Controls.Primitives.Popup pop && pop.Child is FrameworkElement child)
        {
            child.UpdateLayout();
            SaveElement(child, "1b_dropdown.png", (Brush)w.FindResource("Bg"));
        }
        combo.IsDropDownOpen = false;

        // 手順エディタ
        var steps = new List<StepItem> { new(S("見積確認", "Review estimate"), 25), new(S("概略仕様確認", "Check outline spec"), 25), new(S("執筆", "Write draft"), 40) };
        var ed = (Window)typeof(StepsEditor).GetConstructors(F)[0].Invoke([S("開発計画書作成", "Write design document"), steps]);
        ed.Left = -4000; ed.Top = -4000; ed.WindowStartupLocation = WindowStartupLocation.Manual;
        ed.Show(); Save(ed, "1c_steps_editor.png");
        ed.Close();
        // 検証エラーの表示(重複と目安0)。エラーのときは DialogResult を設定しないので、Show のままでも Commit を呼べる
        var ed2 = (Window)typeof(StepsEditor).GetConstructors(F)[0].Invoke([S("開発計画書作成", "Write design document"), new List<StepItem> { new(S("見積確認", "Review estimate"), 25), new(S("見積確認", "Review estimate"), 0) }]);
        ed2.Left = -4000; ed2.Top = -4000; ed2.WindowStartupLocation = WindowStartupLocation.Manual;
        ed2.Show();
        typeof(StepsEditor).GetMethod("Commit", F)!.Invoke(ed2, null);
        Save(ed2, "1d_steps_editor_error.png");
        ed2.Close();

        typeof(MainWindow).GetMethod("EnterRunMode", F)!.Invoke(w, null);
        var render = typeof(MainWindow).GetMethod("Render", F, [typeof(StateSnapshot)])!;
        void St(string file, PanelEdge edge, string reason, bool paused, double sub, double? target, string first = "")
        {
            render.Invoke(w, [new StateSnapshot(RunStatus.Running, S("概略仕様確認", "Check outline spec"), 2, 3, 1870, sub, target, 5400, edge, reason, paused, DateTimeOffset.Now, first)]);
            Save(w, file);
        }
        St("2_run_normal.png", PanelEdge.Normal, "", false, 1104, 1500, S("見積書を開いて合計欄を見る", "Open the estimate and check the total"));
        St("3_run_overrun.png", PanelEdge.Overrun, "", false, 1720, 1500);
        St("4_run_interruptapp.png", PanelEdge.InterruptApp, "", false, 900, 1500);
        St("5_run_paused.png", PanelEdge.Paused, "interrupt", true, 900, 1500);
        // コンパクト表示: 通常 / 目安超過 / 割り込みアプリ / 中断中 / 待機。背景は下の画面に見立てた明るい色にして、透過時の見え方も確認する
        var settings = new FocusCoach.Storage.AppSettings();
        var hud = new CompactHud(settings, hotkeyOk: true, onRestore: () => { }, onInterrupt: () => { });
        hud.Left = -4000; hud.Top = -4000; hud.Show();
        void Hud(string file, PanelEdge edge, string reason, bool paused, double sub, string edgeKey, double opacity)
        {
            hud.Update(new StateSnapshot(RunStatus.Running, S("概略仕様確認", "Check outline spec"), 2, 3, 1870, sub, 1500, 5400, edge, reason, paused, DateTimeOffset.Now, ""), edgeKey, Loc.T("status.working"));
            hud.UpdateLayout();
            SaveElement((FrameworkElement)hud.Content, file, Brushes.WhiteSmoke, opacity);
        }
        Hud("h1_compact_normal.png", PanelEdge.Normal, "", false, 1104, "Line", 0.55);
        Hud("h2_compact_overrun.png", PanelEdge.Overrun, "", false, 1720, "Warn", 0.55);
        Hud("h3_compact_interruptapp.png", PanelEdge.InterruptApp, "", false, 900, "Accent2", 0.55);
        Hud("h4_compact_paused.png", PanelEdge.Paused, "interrupt", true, 900, "Paused", 0.55);
        Hud("h5_compact_hover.png", PanelEdge.Normal, "", false, 1104, "Line", 0.2);
        hud.ShowIdle(); hud.UpdateLayout();
        SaveElement((FrameworkElement)hud.Content, "h6_compact_idle.png", Brushes.WhiteSmoke, 0.55);
        hud.Close();

        // 復帰カード
        St("6_resume_card.png", PanelEdge.Normal, "", false, 905, 1500);
        Find<TextBlock>(w, "NextStepText").Text = Loc.T("rc.next", S("4章の性能要件を読む", "Read chapter 4"));
        Find<TextBlock>(w, "ResumeText").Text = Loc.T("rc.before", S("概略仕様確認", "Check outline spec"), "0:23:00")
            + Loc.T("rc.progress", S("3章の非機能要件まで確認", "Reviewed up to chapter 3"))
            + Loc.T("rc.resources", S("概略仕様書.docx, 見積書.xlsx", "outline-spec.docx, estimate.xlsx"));
        Find<Border>(w, "ResumeCardPanel").Visibility = Visibility.Visible;
        Save(w, "6_resume_card.png");
        Find<Border>(w, "ResumeCardPanel").Visibility = Visibility.Collapsed;
        Find<TextBlock>(w, "AwayText").Text = Loc.T("away.prompt", Loc.T("reason.idle"), 7.0);
        Find<Border>(w, "AwayPanel").Visibility = Visibility.Visible;
        Save(w, "7_away.png");

        // ダイアログ
        var memo = (Window)typeof(MemoWindow).GetConstructors(F)[0].Invoke(["再開メモ", S("3章の非機能要件まで確認", "Reviewed up to chapter 3"), S("4章の性能要件を読む", "Read chapter 4"), new Action<string, string>((_, _) => { })]);
        memo.Left = -4000; memo.Top = -4000; memo.WindowStartupLocation = WindowStartupLocation.Manual;
        memo.Show(); Save(memo, "8_memo.png"); 
        var cands = new List<WsStep>
        {
            new(new WindowInfo("0x1", "EXCEL.EXE", S("マスタスケジュール.xlsx - Excel", "master-schedule.xlsx - Excel")), WsAction.Close, "opened"),
            new(new WindowInfo("0x2", "explorer.exe", S("見積", "estimate")), WsAction.Close, "opened"),
            new(new WindowInfo("0x3", "WINWORD.EXE", S("開発計画書_XX.docx - Word", "design-document_XX.docx - Word")), WsAction.Keep, "deliverable"),
        };
        var cd = (Window)typeof(CloseDialog).GetConstructors(F)[0].Invoke([cands]);
        cd.Left = -4000; cd.Top = -4000; cd.WindowStartupLocation = WindowStartupLocation.Manual;
        cd.Show(); Save(cd, "9_close_dialog.png");
        cd.Close();

        // 出口カード(S-11a。design-0.12A §3): 完了 / あとでやることが残る
        var cats = new List<string> { S("営業からの問い合わせ", "Sales inquiry"), "割込み作業", S("社内の相談", "Internal question") };
        var em = new ExitCardModel(cats, cats[0], 12 * 60, 5, true, true, new Point(-4000, -4000));
        var ec = new ExitCardWindow(em, _ => { }, () => { }, _ => { });
        ec.Show(); Save(ec, "10_exit_card.png");
        ((TextBox)typeof(ExitCardWindow).GetField("_title", F)!.GetValue(ec)!).Text = S("A社 旧版の設定方法", "Old-version setup (Customer A)");
        ((RadioButton)typeof(ExitCardWindow).GetField("_follow", F)!.GetValue(ec)!).IsChecked = true;
        ((TextBox)typeof(ExitCardWindow).GetField("_followText", F)!.GetValue(ec)!).Text = S("来週の版で直るか確認して返信", "Reply after checking whether the next version fixes it");
        ((RadioButton)typeof(ExitCardWindow).GetField("_dueTomorrow", F)!.GetValue(ec)!).IsChecked = true;
        Save(ec, "10a_exit_card_followup.png");
        ((RadioButton)typeof(ExitCardWindow).GetField("_dueToday", F)!.GetValue(ec)!).IsChecked = true;
        ((TextBox)typeof(ExitCardWindow).GetField("_followText", F)!.GetValue(ec)!).Text = "";
        ec.Commit();   // 空のまま → エラーの表示
        Save(ec, "10b_exit_card_error.png");

        // 再入場ガイド(S-11b。§4)。初回の案内あり
        var gm = new ReentryGuideModel(S("開発計画書作成", "Write design document"), S("概略仕様確認", "Check outline spec"), S("3章の非機能要件から読む", "Start from chapter 3"), null, 40, ReentryPolicy.Message(null, 40), true, null);
        var gw = new ReentryGuideWindow(gm, (_, _, _) => { });
        gw.Show(); gw.Left = -4000; gw.Top = -4000;
        System.Threading.Thread.Sleep(10);
        gw.UpdateLayout();
        SaveElement((FrameworkElement)gw.Content, "11_reentry_guide.png", new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)));
        var gm2 = gm with { NextStep = null, ResumeNote = null, ShowHint = false };
        var gw2 = new ReentryGuideWindow(gm2, (_, _, _) => { });
        gw2.Show(); gw2.Left = -4000; gw2.Top = -4000; gw2.UpdateLayout();
        SaveElement((FrameworkElement)gw2.Content, "11a_reentry_guide_plain.png", new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)));

        // あとでやること(S-12)と、ミニパネルの件数
        typeof(MainWindow).GetMethod("RefreshFollowups", F)!.Invoke(w, null);
        St("12_run_with_followups.png", PanelEdge.Normal, "", false, 1104, 1500);
        FollowupListWindow.ShowOrActivate(w.Host, () => { });
        var fl = Application.Current.Windows.OfType<FollowupListWindow>().First();
        fl.Left = -4000; fl.Top = -4000;
        Save(fl, "13_followup_list.png");

        // メッセージ・確認のダイアログ(Windows 標準の MessageBox の代わり。0.12A)
        Window Dlg(string text, string title, (string, MessageBoxResult, bool)[] buttons)
        {
            var d = (Window)typeof(FcDialog).GetConstructors(F)[0].Invoke([text, title, buttons.ToList(), MessageBoxResult.No, MessageBoxResult.Cancel]);
            d.Left = -4000; d.Top = -4000; d.WindowStartupLocation = WindowStartupLocation.Manual;
            d.Show();
            return d;
        }
        var close = Dlg(Loc.T("close.text"), Loc.T("close.title"),
            [(Loc.T("close.cancel"), MessageBoxResult.Cancel, false), (Loc.T("close.end"), MessageBoxResult.Yes, false), (Loc.T("close.suspend"), MessageBoxResult.No, true)]);
        Save(close, "14_dialog_close.png");
        var review = Dlg(Loc.T("review.line1", "1:20:00", "1:02:10", "0:14:30", "0:03:20") + "\n" + Loc.T("review.line2", 78.0, 41, 3, 1) + "\n" + Loc.T("review.sameday")
            + "\n\n" + Loc.T("review.followups_head") + "\n  □ " + S("来週の版で直るか確認して返信", "Reply after checking whether the next version fixes it") + Loc.T("review.due", Loc.Date(new DateOnly(2026, 10, 30))),
            Loc.T("review.title"), [(Loc.T("common.ok"), MessageBoxResult.OK, true)]);
        Save(review, "15_dialog_review.png");
        Environment.Exit(0);
    }

    static T Find<T>(Window w, string name) where T : class => (T)w.FindName(name);

    static void Save(Window w, string file)
    {
        w.UpdateLayout();
        var root = (FrameworkElement)w.Content;
        root.UpdateLayout();
        SaveElement(root, file, w.Background);
    }

    static void SaveElement(FrameworkElement root, string file, Brush background, double opacity = 1.0)
    {
        var width = (int)Math.Ceiling(root.ActualWidth); var height = (int)Math.Ceiling(root.ActualHeight);
        var rtb = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) { dc.DrawRectangle(background, null, new Rect(0, 0, width, height)); dc.PushOpacity(opacity); dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, width, height)); dc.Pop(); }
        rtb.Render(dv);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(Out, file)); enc.Save(fs);
        Console.WriteLine($"saved {file} {width}x{height}");
    }

    /// <summary>プルダウンに出る履歴を作る(架空のデータ): 終了した実行2種類と、手順の登録1件、下書き、あとでやること。</summary>
    static void SeedHistory(string data)
    {
        using var db = new FocusCoach.Storage.FocusCoachDb(Path.Combine(data, "focuscoach.db"));
        var task = S(S("開発計画書作成", "Write design document"), "Write design document");
        var steps = S("見積確認:25,概略仕様確認:25,執筆:40", "Review estimate:25,Check outline spec:25,Write draft:40");
        var a = FocusCoach.Core.Scenarios.ScenarioRunner.Run($"at 00:00 start_task category=\"{task}\" subtasks=\"{steps}\"\nat 01:20 end_task", runId: "seed-a");
        db.SaveRun(a.Session.State, a.Metrics);
        var weekly = S("週次報告", "Weekly report");
        var weeklySteps = S("集計:15,まとめ:20", "Collect figures:15,Summarize:20");
        var b = FocusCoach.Core.Scenarios.ScenarioRunner.Run($"at 00:00 start_task category=\"{weekly}\" subtasks=\"{weeklySteps}\"\nat 00:40 end_task", runId: "seed-b");
        db.SaveRun(b.Session.State, b.Metrics);
        db.SaveTemplate(S("要件定義レビュー", "Requirements review"), [new(S("指摘の確認", "Check comments"), 20), new(S("回答の作成", "Write replies"), 30)]);
        db.SaveDraft(new FocusCoach.Storage.TaskDraft(S(S("見積もり依頼", "Estimate request"), "Estimate request"), S("YY更改", "Project YY"),
            [new(S("条件整理", "Sort out conditions"), 15), new(S("依頼文作成", "Write the request"), 20)], "WINWORD.EXE", S("過去の依頼文を開く", "Open a past request"), DateTimeOffset.Now));
        // あとでやること(架空のデータ): 期限切れ・今日・期限なし・完了
        var now = DateTimeOffset.Now; var today = DateOnly.FromDateTime(DateTime.Now);
        var sales = S("営業からの問い合わせ", "Sales inquiry");
        var field = S("現場エンジニア", "Field engineer");
        db.SaveInterruptClose(new("seed-a", now.AddHours(-30), now.AddHours(-29.8), sales, S("A社 旧版", "Customer A, old version"), "followup",
            S("旧版の設定手順を送る", "Send the old-version setup steps"), today.AddDays(-1), task, null));
        db.SaveInterruptClose(new("seed-a", now.AddHours(-3), now.AddHours(-2.8), field, S("B工場 エラー", "Plant B error"), "followup",
            S("ログを見て原因を返信", "Check the log and reply with the cause"), today, task, null));
        db.AddFollowup(S("社内ツールの手順をまとめる", "Write up the internal tool procedure"), null, "manual", now.AddDays(-2));
        var done = db.AddFollowup(S("Cさんに PC の設定を案内", "Show Mr. C how to set up his PC"), null, "manual", now.AddDays(-1));
        db.SetFollowupStatus(done, "done", now.AddHours(-1));
    }
}
