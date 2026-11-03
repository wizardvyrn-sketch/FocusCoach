namespace FocusCoach.Core;

/// <summary>コンパクト表示の見た目: クリックを下のアプリに通すか、不透明度(0〜1)。</summary>
public sealed record HudLook(bool ClickThrough, double Opacity);

/// <summary>
/// コンパクト表示(03 S-04、prototype/review/design-0.10A-usability.md R4)の見た目を決める純粋関数。
/// OS に依存しないので、クラウドでもテストできる。
/// </summary>
public static class HudPolicy
{
    public const double DefaultOpacity = 0.55;
    public const double DefaultHoverOpacity = 0.2;

    /// <summary>
    /// hotkeyOk: 戻すためのホットキー(F8)を登録できたか。登録できないときは、クリックを通すと戻せなくなるので通さない。
    /// clickThroughSetting: 設定でクリック透過を使うか。
    /// cursorOver: マウスがコンパクト表示の上にあるか。ctrlDown: Ctrl を押しているか(押している間だけ触れる)。
    /// compactOpacity: 通常時の不透明度、hoverOpacity: マウスを重ねたとき(下の画面が見えるよう、さらに薄くする)。
    /// </summary>
    public static HudLook Decide(bool hotkeyOk, bool clickThroughSetting, bool cursorOver, bool ctrlDown,
                                 double compactOpacity, double hoverOpacity)
    {
        compactOpacity = Clamp(compactOpacity);
        hoverOpacity = Clamp(hoverOpacity);
        var canClickThrough = hotkeyOk && clickThroughSetting;
        if (!canClickThrough) return new HudLook(false, cursorOver ? 1.0 : compactOpacity);
        if (cursorOver && ctrlDown) return new HudLook(false, 1.0);                  // Ctrl を押して触る
        return new HudLook(true, cursorOver ? Math.Min(hoverOpacity, compactOpacity) : compactOpacity);
    }

    /// <summary>薄くしすぎて見えなくなるのを防ぐ(最小 0.1)。</summary>
    private static double Clamp(double v) => double.IsNaN(v) ? DefaultOpacity : Math.Clamp(v, 0.1, 1.0);
}
