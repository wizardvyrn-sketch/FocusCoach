using System.IO;
using System.Windows;
using FocusCoach.Core;

namespace FocusCoach.App;

/// <summary>画面の文言に入れるホットキーの名前(設定のキーから作る)。Language が空なら、言語のホットキーは無効。</summary>
public sealed record HotkeyNames(string Interrupt, string Memo, string Compact, string Language)
{
    public static HotkeyNames Default { get; } = new("F9", "F10", "F8", "Ctrl+Alt+F8");
}

/// <summary>
/// タスクトレイのアイコン(03 画面一覧「タスクトレイ」)。常駐し、クリックで開く。最小化(–)するとここに格納される。
/// System.Windows.Forms.NotifyIcon を使う(WPF には標準の部品が無いため。ほかの WinForms の部品は使わない)。
/// ウィンドウ名・タスク名などの内容は、ツールチップに出さない(画面共有に映る可能性があるため)。
/// 文言は Loc から引き、言語が替わったら Refresh() で作り直す(メニューの Language / 言語 は、どの言語でも併記)。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Windows.Forms.ToolStripMenuItem _open;
    private readonly System.Windows.Forms.ToolStripMenuItem _compact;
    private readonly System.Windows.Forms.ToolStripMenuItem _interrupt;
    private readonly System.Windows.Forms.ToolStripMenuItem _followups;
    private readonly System.Windows.Forms.ToolStripMenuItem _exit;
    private readonly System.Windows.Forms.ToolStripMenuItem _language;
    private readonly System.Windows.Forms.ToolStripMenuItem _langJa;
    private readonly System.Windows.Forms.ToolStripMenuItem _langEn;
    private string _statusKey = "status.idle";
    private bool _compactOn, _running, _interrupted;

    /// <summary>メニュー・吹き出しに出すホットキーの名前。</summary>
    public HotkeyNames Keys { get; set; } = HotkeyNames.Default;

    public TrayIcon(Action onOpen, Action onToggleCompact, Action onExit, Action onInterrupt, Action onFollowups, Action<string> onLanguage)
    {
        _open = new System.Windows.Forms.ToolStripMenuItem();
        _open.Click += (_, _) => onOpen();
        // 割り込み対応(しおり)。トレイに格納中でも押せる(design-0.12A §1.2)。作業中でないときは押せない
        _interrupt = new System.Windows.Forms.ToolStripMenuItem { Enabled = false };
        _interrupt.Click += (_, _) => onInterrupt();
        _followups = new System.Windows.Forms.ToolStripMenuItem();
        _followups.Click += (_, _) => onFollowups();
        _compact = new System.Windows.Forms.ToolStripMenuItem();
        _compact.Click += (_, _) => onToggleCompact();
        _exit = new System.Windows.Forms.ToolStripMenuItem();
        _exit.Click += (_, _) => onExit();
        // 言語。間違えて切り替えても戻れるよう、見出しは日本語と英語の併記、各言語の名前はその言語の文字
        _langJa = new System.Windows.Forms.ToolStripMenuItem();
        _langJa.Click += (_, _) => onLanguage(Loc.Ja);
        _langEn = new System.Windows.Forms.ToolStripMenuItem();
        _langEn.Click += (_, _) => onLanguage(Loc.En);
        _language = new System.Windows.Forms.ToolStripMenuItem();
        _language.DropDownItems.AddRange([_langJa, _langEn]);

        var menu = new System.Windows.Forms.ContextMenuStrip { ShowImageMargin = false };
        menu.Items.AddRange([_open, _interrupt, _compact, _followups, new System.Windows.Forms.ToolStripSeparator(), _language,
            new System.Windows.Forms.ToolStripSeparator(), _exit]);
        // アプリの配色(03 §3.4.1)に合わせる
        menu.BackColor = System.Drawing.Color.FromArgb(0x14, 0x25, 0x2D);
        menu.ForeColor = System.Drawing.Color.FromArgb(0xDC, 0xEB, 0xEF);
        menu.Renderer = new DarkRenderer();
        menu.Font = new System.Drawing.Font("BIZ UDPGothic", 9.5f);
        foreach (System.Windows.Forms.ToolStripItem i in _language.DropDownItems) { i.BackColor = menu.BackColor; i.ForeColor = menu.ForeColor; }
        _language.DropDown.BackColor = menu.BackColor;

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "FocusCoach",
            ContextMenuStrip = menu,
            Visible = true,
        };
        // 左クリック・ダブルクリックで開く(右クリックはメニュー)
        _icon.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) onOpen(); };
        Refresh();
    }

    /// <summary>ツールチップ(状態だけ。タスク名や手順名は出さない)。NotifyIcon.Text は 127 文字まで。</summary>
    public void SetStatus(string statusKey)
    {
        _statusKey = statusKey;
        _icon.Text = Loc.T("tray.tooltip", Loc.T(statusKey));
    }

    /// <summary>メニューの「コンパクト表示」の文言を、今の状態に合わせる。</summary>
    public void SetCompactLabel(bool compact)
    {
        _compactOn = compact;
        _compact.Text = Loc.T(compact ? "tray.full" : "tray.compact", Keys.Compact);
    }

    /// <summary>メニューの「割り込み対応」の文言と、押せるかどうか(作業中だけ)。</summary>
    public void SetInterruptLabel(bool running, bool interrupted)
    {
        _running = running; _interrupted = interrupted;
        _interrupt.Enabled = running;
        _interrupt.Text = Loc.T(interrupted ? "tray.return" : "tray.interrupt", Keys.Interrupt);
    }

    /// <summary>言語・ホットキーの名前が替わったとき、すべての文言を作り直す。</summary>
    public void Refresh()
    {
        _open.Text = Loc.T("tray.open");
        _followups.Text = Loc.T("tray.followups");
        _exit.Text = Loc.T("tray.exit");
        _language.Text = Loc.T("lang.menu");
        // メニューの左の余白を出していない(ShowImageMargin=false)ので、チェックの印の代わりに ● を付ける
        _langJa.Text = (Loc.Language == Loc.Ja ? "● " : "    ") + Loc.T("lang.ja");
        _langEn.Text = (Loc.Language == Loc.En ? "● " : "    ") + Loc.T("lang.en");
        SetStatus(_statusKey);
        SetCompactLabel(_compactOn);
        SetInterruptLabel(_running, _interrupted);
    }

    /// <summary>初回だけ、格納されたことを知らせる吹き出し(設定で一度出したら二度と出さない)。</summary>
    public void ShowHint()
    {
        _icon.BalloonTipTitle = Loc.T("tray.hint.title");
        _icon.BalloonTipText = Loc.T("tray.hint.text", Keys.Compact);
        _icon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.None;
        _icon.ShowBalloonTip(4000);
    }

    /// <summary>埋め込みのアイコンから、トレイの大きさ(通常 16px。高 DPI では大きい)に合う画像を取る。</summary>
    private static System.Drawing.Icon LoadIcon()
    {
        var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        using var ms = new MemoryStream();
        res.Stream.CopyTo(ms);
        ms.Position = 0;
        return new System.Drawing.Icon(ms, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;   // 先に消さないと、プロセスが終わってもトレイに幽霊のアイコンが残る
        _icon.Dispose();
    }

    /// <summary>メニューの見た目(暗い面、ホバーで青緑がかる)。</summary>
    private sealed class DarkRenderer : System.Windows.Forms.ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { }
        private sealed class DarkColors : System.Windows.Forms.ProfessionalColorTable
        {
            public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(0x1F, 0x4F, 0x66);
            public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(0x4F, 0xD1, 0xC5);
            public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(0x28, 0x43, 0x4E);
            public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(0x14, 0x25, 0x2D);
            public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(0x28, 0x43, 0x4E);
            public override System.Drawing.Color SeparatorLight => System.Drawing.Color.FromArgb(0x28, 0x43, 0x4E);
            public override System.Drawing.Color CheckBackground => System.Drawing.Color.FromArgb(0x1F, 0x4F, 0x66);
            public override System.Drawing.Color CheckSelectedBackground => System.Drawing.Color.FromArgb(0x1F, 0x4F, 0x66);
            public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.FromArgb(0x14, 0x25, 0x2D);
            public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.FromArgb(0x14, 0x25, 0x2D);
            public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.FromArgb(0x14, 0x25, 0x2D);
        }
    }
}
