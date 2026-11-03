using Xunit;

namespace FocusCoach.Core.Tests;

/// <summary>コンパクト表示の透過・クリック透過の決め方(design-0.10A-usability.md R4)。</summary>
public class HudPolicyTests
{
    private static HudLook D(bool hotkeyOk = true, bool setting = true, bool over = false, bool ctrl = false, double c = 0.55, double h = 0.2)
        => HudPolicy.Decide(hotkeyOk, setting, over, ctrl, c, h);

    [Fact]
    public void Normal_state_is_semi_transparent_and_lets_clicks_through()
        => Assert.Equal(new HudLook(true, 0.55), D());

    [Fact]
    public void Hovering_makes_it_thinner_but_still_click_through()
        => Assert.Equal(new HudLook(true, 0.2), D(over: true));

    [Fact]
    public void Ctrl_while_hovering_makes_it_solid_and_clickable()
        => Assert.Equal(new HudLook(false, 1.0), D(over: true, ctrl: true));

    [Fact]
    public void Ctrl_without_hovering_changes_nothing()
        => Assert.Equal(D(), D(ctrl: true));

    [Fact]
    public void Without_the_restore_hotkey_it_never_blocks_clicks_so_the_user_can_always_get_back()
    {
        Assert.Equal(new HudLook(false, 0.55), D(hotkeyOk: false));
        Assert.Equal(new HudLook(false, 1.0), D(hotkeyOk: false, over: true));   // 触れる状態では見やすく濃くする
    }

    [Fact]
    public void Click_through_can_be_turned_off_in_settings()
        => Assert.Equal(new HudLook(false, 0.55), D(setting: false));

    [Fact]
    public void Opacity_is_clamped_so_it_never_disappears()
    {
        Assert.Equal(new HudLook(true, 0.1), D(c: 0.0, h: 0.0));
        Assert.Equal(new HudLook(true, 1.0), D(c: 5.0, h: 5.0));
        Assert.Equal(new HudLook(true, 0.55), D(c: double.NaN));
    }

    [Fact]
    public void Hover_opacity_is_never_denser_than_the_normal_opacity()
        => Assert.Equal(new HudLook(true, 0.3), D(over: true, c: 0.3, h: 0.8));
}
