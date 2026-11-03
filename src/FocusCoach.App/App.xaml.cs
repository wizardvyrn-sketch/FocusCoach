using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FocusCoach.Core;

namespace FocusCoach.App;

public partial class App : Application
{
    private Mutex? _single;
    private const string ShowEventName = @"Local\FocusCoach.Show";
    private EventWaitHandle? _show;
    private Thread? _showThread;
    private volatile bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 単一起動(R-26)。2つ目の起動は、起動中の FocusCoach に「開いて」と合図して終了する(タスクトレイに格納中でも出てくる)
        _single = new Mutex(true, @"Local\FocusCoach.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            try { using var ev = EventWaitHandle.OpenExisting(ShowEventName); ev.Set(); }
            catch
            {
                Loc.SetLanguage(Loc.Resolve(LoadLanguageSetting()));   // 保存した表示言語(無ければ OS の言語)
                var text = Loc.T("app.already");
                try { FcDialog.Info(null, text); } catch { MessageBox.Show(text, "FocusCoach"); }
            }
            Shutdown();
            return;
        }
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showThread = new Thread(() =>
        {
            // 2つ目の起動からの合図を待つ。UI スレッドで、格納前の表示に戻す
            while (_show.WaitOne()) { if (_exiting) return; Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.RestoreFromTray()); }
        }) { IsBackground = true, Name = "fc-show-signal" };
        _showThread.Start();

        // 未処理例外(08章 §8.7、R-12): クラッシュログを書き、実行を interrupted にしてから終了する。次回の起動で再開を提案する(F-16)
        DispatcherUnhandledException += (_, ev) => { Crash(ev.Exception); ev.Handled = true; Shutdown(1); };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => { if (ev.ExceptionObject is Exception ex) Crash(ex); };
        TaskScheduler.UnobservedTaskException += (_, ev) => { (MainWindow as MainWindow)?.Host.Log.Error("App", "unobserved task exception", ev.Exception); ev.SetObserved(); };
        base.OnStartup(e);

        // 起動時のロゴ画面(03 §3.8。Secrypt と同じ SPIRE デザイン)。終わったら本体を出し、その上でロゴ画面を消す。
        // 最初に開いたウィンドウが Application.MainWindow になるため、本体を出したら差し替える(トレイからの復帰などが本体を参照する)
        var splash = new SplashWindow();
        splash.BootCompleted += (_, _) =>
        {
            var main = new MainWindow();
            MainWindow = main;
            // 本体を閉じたら終了する(あとでやることの一覧など、ほかのウィンドウが開いていてもプロセスを残さない)
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
            splash.FadeOutAndClose();
        };
        splash.Show();
    }

    /// <summary>設定の表示言語(CoreHost と同じ保存先の規則: FOCUSCOACH_DATA、無ければ %LOCALAPPDATA%\FocusCoach)。読めなければ auto。</summary>
    private static string LoadLanguageSetting()
    {
        try
        {
            var root = Environment.GetEnvironmentVariable("FOCUSCOACH_DATA") is { Length: > 0 } env
                ? env : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusCoach");
            return FocusCoach.Storage.AppSettings.Load(root).Language;
        }
        catch { return "auto"; }
    }

    private void Crash(Exception ex)
    {
        try
        {
            var path = (MainWindow as MainWindow)?.Host.OnCrash(ex) ?? "";
            var text = Loc.T("app.crash", path);
            try { FcDialog.Info(null, text); } catch { MessageBox.Show(text, "FocusCoach"); }   // テーマが使えない状態でも、知らせは必ず出す
        }
        catch { /* 異常終了中 */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        try { _show?.Set(); } catch { }   // 待っているスレッドを終わらせる
        _show?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
