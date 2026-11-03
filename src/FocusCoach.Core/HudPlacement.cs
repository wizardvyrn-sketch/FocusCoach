namespace FocusCoach.Core;

/// <summary>仮想スクリーン(全モニタをつなげた領域)。座標は WPF の単位(DPI 補正後)。</summary>
public readonly record struct ScreenArea(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>
/// コンパクト表示(03 S-04b)の置き場所を決める純粋関数。OS に依存しないので、クラウドでもテストできる。
/// 縮むときは「元のウィンドウの左上」を起点にし(左上の角が動かない)、戻すときも「コンパクト表示の左上」に戻す。
/// どちらも、画面の外にはみ出さないよう、仮想スクリーンの中に収める。
/// </summary>
public static class HudPlacement
{
    /// <summary>通常 → コンパクト: 元のウィンドウの左上に、コンパクト表示の左上を合わせる。</summary>
    public static (double Left, double Top) ShrinkToTopLeft(double mainLeft, double mainTop, double hudWidth, double hudHeight, ScreenArea screen)
        => (Fit(mainLeft, hudWidth, screen.X, screen.Right), Fit(mainTop, hudHeight, screen.Y, screen.Bottom));

    /// <summary>コンパクト → 通常: コンパクト表示の左上に、元のウィンドウの左上を合わせる(大きい元のウィンドウが画面の外に出ないよう収める)。</summary>
    public static (double Left, double Top) RestoreToTopLeft(double hudLeft, double hudTop, double mainWidth, double mainHeight, ScreenArea screen)
        => (Fit(hudLeft, mainWidth, screen.X, screen.Right), Fit(hudTop, mainHeight, screen.Y, screen.Bottom));

    /// <summary>start から size だけ伸びる範囲が [min, max] に収まる開始位置。収まらないほど大きいときは min に寄せる。</summary>
    private static double Fit(double start, double size, double min, double max)
    {
        if (double.IsNaN(start) || double.IsInfinity(start)) return min;
        var hi = max - size;
        return hi < min ? min : Math.Clamp(start, min, hi);
    }
}
