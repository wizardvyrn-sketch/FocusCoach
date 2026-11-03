using Xunit;

namespace FocusCoach.Core.Tests;

/// <summary>コンパクト表示の置き場所(左上を起点に縮み、左上に戻る)。</summary>
public class HudPlacementTests
{
    private static readonly ScreenArea One = new(0, 0, 1920, 1080);
    private static readonly ScreenArea Two = new(-1280, 0, 3200, 1080);   // 左にもう1枚モニタがある(原点が負)

    [Fact]
    public void Shrinking_keeps_the_top_left_corner_of_the_original_window()
        => Assert.Equal((1200.0, 16.0), HudPlacement.ShrinkToTopLeft(1200, 16, 170, 36, One));

    [Fact]
    public void Restoring_puts_the_window_top_left_where_the_compact_view_is()
        => Assert.Equal((300.0, 200.0), HudPlacement.RestoreToTopLeft(300, 200, 420, 320, One));

    [Fact]
    public void Shrink_then_restore_without_moving_returns_to_the_same_place()
    {
        var (hl, ht) = HudPlacement.ShrinkToTopLeft(640, 120, 170, 36, One);
        Assert.Equal((640.0, 120.0), HudPlacement.RestoreToTopLeft(hl, ht, 420, 320, One));
    }

    [Fact]
    public void Compact_view_stays_inside_the_screen_when_the_original_was_partly_outside()
        => Assert.Equal((0.0, 0.0), HudPlacement.ShrinkToTopLeft(-50, -20, 170, 36, One));

    [Fact]
    public void A_big_window_restored_from_a_compact_view_near_the_right_bottom_is_pulled_back_inside()
        => Assert.Equal((1500.0, 760.0), HudPlacement.RestoreToTopLeft(1850, 1060, 420, 320, One));

    [Fact]
    public void Negative_origin_monitors_are_handled()
    {
        Assert.Equal((-1200.0, 40.0), HudPlacement.ShrinkToTopLeft(-1200, 40, 170, 36, Two));
        Assert.Equal((-1280.0, 40.0), HudPlacement.ShrinkToTopLeft(-1500, 40, 170, 36, Two));
    }

    [Fact]
    public void Non_finite_positions_fall_back_to_the_screen_origin()
    {
        Assert.Equal((0.0, 0.0), HudPlacement.ShrinkToTopLeft(double.NaN, double.PositiveInfinity, 170, 36, One));
        Assert.Equal((0.0, 0.0), HudPlacement.RestoreToTopLeft(double.NaN, double.NaN, 420, 320, One));
    }

    [Fact]
    public void A_window_larger_than_the_screen_is_pinned_to_the_top_left()
        => Assert.Equal((0.0, 0.0), HudPlacement.RestoreToTopLeft(500, 500, 3000, 2000, One));
}
