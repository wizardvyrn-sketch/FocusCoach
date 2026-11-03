using System.Text;
using System.Text.Json;

namespace FocusCoach.Storage;

/// <summary>
/// アプリログ(08章 §8.3)。logs/app-YYYYMMDD.log に1行ずつ追記する。
/// [仮案-26] Serilog は導入せず、最小限の自前実装にした(依存を増やさないため)。ローテーションは日付単位、14日より古いものは起動時に削除する。
/// タイトル・パス・URL は呼び出し側で LogMask を通してから渡す(08章 §8.4)。
/// </summary>
public sealed class AppLog
{
    private readonly string _dir;
    private readonly object _lock = new();

    public AppLog(string dataRoot)
    {
        _dir = Path.Combine(dataRoot, "logs");
        Directory.CreateDirectory(_dir);
    }

    public void Info(string category, string message) => Write("INF", category, message);
    public void Warn(string category, string message) => Write("WRN", category, message);
    public void Error(string category, string message, Exception? ex = null) =>
        // スタックトレースも残す(実機で NullReferenceException が出ても場所が分からなかった)。内側の例外も辿る
        Write("ERR", category, ex is null ? message : $"{message} | {Describe(ex)}");

    private static string Describe(Exception ex)
    {
        var sb = new StringBuilder($"{ex.GetType().Name}: {ex.Message}");
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (!ReferenceEquals(e, ex)) sb.Append($"{Environment.NewLine}  caused by {e.GetType().Name}: {e.Message}");
            if (e.StackTrace is { } st) sb.Append(Environment.NewLine).Append(st);
        }
        return sb.ToString();
    }

    private void Write(string level, string category, string message)
    {
        var now = DateTimeOffset.Now;
        var line = $"{now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {category}: {message}{Environment.NewLine}";
        lock (_lock)
        {
            try { File.AppendAllText(Path.Combine(_dir, $"app-{now:yyyyMMdd}.log"), line, Encoding.UTF8); }
            catch (IOException) { /* ログの失敗でアプリを止めない */ }
        }
    }

    public int DeleteOlderThan(int days)
    {
        var n = 0;
        foreach (var f in Directory.GetFiles(_dir, "app-*.log").Concat(Directory.GetFiles(_dir, "crash-*.txt")))
        {
            var limit = Path.GetFileName(f).StartsWith("crash-", StringComparison.Ordinal) ? Math.Max(days, 30) : days;
            if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-limit)) { try { File.Delete(f); n++; } catch (IOException) { } }
        }
        return n;
    }

    /// <summary>クラッシュログ(08章 §8.7)。例外のスタックと、直前の状態の要約(個人データを含まない)を書く。</summary>
    public string WriteCrash(Exception ex, string stateSummary)
    {
        var path = Path.Combine(_dir, $"crash-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt");
        try { File.WriteAllText(path, $"{DateTimeOffset.Now:O}\n{stateSummary}\n\n{ex}\n", Encoding.UTF8); }
        catch (IOException) { }
        return path;
    }
}

/// <summary>利用者の設定(data root の settings.json)。DB の settings テーブルは未実装のため、当面はファイルに置く [仮案-27]。</summary>
public sealed class AppSettings
{
    public double? PanelLeft { get; set; }
    public double? PanelTop { get; set; }
    /// <summary>割り込み ⇄ 再開(09章 §9.4.1)。仮想キーコード。既定 F9。</summary>
    public int HotkeyInterrupt { get; set; } = 0x78;
    /// <summary>メモ(作業中は棚上げ、割り込み中はどこまで)。既定 F10。</summary>
    public int HotkeyMemo { get; set; } = 0x79;
    /// <summary>コンパクト表示の切り替え(03 S-04)。既定 F8。</summary>
    public int HotkeyCompact { get; set; } = 0x77;
    /// <summary>コンパクト表示の不透明度(通常時)。0.1〜1。</summary>
    public double CompactOpacity { get; set; } = 0.55;
    /// <summary>コンパクト表示にマウスを重ねたときの不透明度(下の画面が見えるよう薄くする)。</summary>
    public double CompactHoverOpacity { get; set; } = 0.2;
    /// <summary>コンパクト表示の上のクリックを、下のアプリに通すか。Ctrl を押している間だけ触れる。</summary>
    public bool CompactClickThrough { get; set; } = true;
    /// <summary>最小化でタスクトレイに格納したことを、初回だけ吹き出しで知らせたか(0.11A)。</summary>
    public bool TrayHintShown { get; set; }
    /// <summary>再入場ガイド(割り込みを閉じたあと、元の作業に戻るまでの秒数。0 で出さない。design-0.12A §6.3)。</summary>
    public int ReentryGuideSec { get; set; } = 40;
    /// <summary>これより短い割り込みでは、再入場ガイドを出さない(秒)。</summary>
    public int ReentryGuideMinInterruptSec { get; set; } = 60;
    /// <summary>
    /// 再入場ガイドの誘いかけの一言。{sec} は秒数。未設定(null)なら、表示言語ごとの既定文。空にすると出さない。
    /// (0.12A で保存された日本語の既定文は、未設定として扱う: ReentryPolicy.Message)
    /// </summary>
    public string? ReentryGuideMessage { get; set; }
    /// <summary>表示言語: auto(OS に合わせる。日本語の OS なら日本語、それ以外は英語)/ ja / en(0.13A)。</summary>
    public string Language { get; set; } = "auto";
    /// <summary>言語を切り替えるホットキー(仮想キーコード。0 で無効)。既定は Ctrl + Alt + F8。</summary>
    public int HotkeyLanguage { get; set; } = 0x77;
    /// <summary>言語のホットキーの修飾キー(Alt=1, Ctrl=2, Shift=4)。既定は Ctrl + Alt。</summary>
    public int HotkeyLanguageModifiers { get; set; } = 3;
    /// <summary>再入場ガイドの初回だけの案内(時間の使い方は自由)を出したか。</summary>
    public bool ReentryGuideHintShown { get; set; }
    /// <summary>出口カードの「割り込みで開いたウィンドウを片付ける」の初期値。</summary>
    public bool ExitCardTidyDefault { get; set; } = true;
    /// <summary>出口カードで最後に選んだカテゴリ(次の初期値)。</summary>
    public string LastInterruptCategory { get; set; } = "";
    /// <summary>タスクで使うアプリの初期値(05 F-02)。</summary>
    public string DefaultTaskApps { get; set; } = "WINWORD.EXE,EXCEL.EXE";
    /// <summary>ワークスペース操作をドライランにするか(実機確認が済むまで既定 true。[仮案-21])。</summary>
    public bool WorkspaceDryRun { get; set; } = true;

    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    public static string PathFor(string dataRoot) => Path.Combine(dataRoot, "settings.json");

    public static AppSettings Load(string dataRoot)
    {
        try
        {
            var p = PathFor(dataRoot);
            return File.Exists(p) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(p)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(); } // 壊れていたら既定値で起動する
    }

    public void Save(string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        var p = PathFor(dataRoot);
        var tmp = p + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opt));
        File.Move(tmp, p, overwrite: true); // 書きかけで壊さない
    }
}
