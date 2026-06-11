using WindowsOscVolumeControl.UI.Wpf.Controls;

namespace WindowsOscVolumeControl.Tests;

public class SmoothScrollMathTests {
	[Fact]
	public void computeVerticalWheelDeltaPixels_multiLine_scalesWithDeltaAndLines() {
		// delta +120 → scroll content up → negative offset delta
		double up = SmoothScrollMath.computeVerticalWheelDeltaPixels(120, 3, 16, 900, 120);
		Assert.Equal(-48, up, 5);

		double down = SmoothScrollMath.computeVerticalWheelDeltaPixels(-120, 3, 16, 900, 120);
		Assert.Equal(48, down, 5);
	}

	[Fact]
	public void computeVerticalWheelDeltaPixels_fractionalDelta_trackpadStyle() {
		double d = SmoothScrollMath.computeVerticalWheelDeltaPixels(30, 3, 16, 900, 120);
		Assert.Equal(-12, d, 5);
	}

	[Fact]
	public void computeVerticalWheelDeltaPixels_pageMode_usesViewportHeight() {
		double d = SmoothScrollMath.computeVerticalWheelDeltaPixels(120, -1, 16, 400, 120);
		Assert.Equal(-400, d, 5);

		double half = SmoothScrollMath.computeVerticalWheelDeltaPixels(60, -1, 16, 400, 120);
		Assert.Equal(-200, half, 5);
	}

	[Theory]
	[InlineData(0, 3, 16, 100, 120, 0)]
	[InlineData(120, 0, 16, 100, 120, 0)]
	[InlineData(120, 3, 16, 100, 0, 0)]
	public void computeVerticalWheelDeltaPixels_edgeCases_returnZero(
		int wheelDelta,
		int lines,
		double scrollH,
		double viewH,
		int deltaPerLine,
		double expected) {
		double d = SmoothScrollMath.computeVerticalWheelDeltaPixels(wheelDelta, lines, scrollH, viewH, deltaPerLine);
		Assert.Equal(expected, d, 5);
	}

	[Fact]
	public void clampVerticalOffset_clampsToRange() {
		Assert.Equal(0, SmoothScrollMath.clampVerticalOffset(-5, 100));
		Assert.Equal(100, SmoothScrollMath.clampVerticalOffset(150, 100));
		Assert.Equal(42, SmoothScrollMath.clampVerticalOffset(42, 100));
	}

	[Fact]
	public void clampVerticalOffset_negativeMax_returnsZero() {
		Assert.Equal(0, SmoothScrollMath.clampVerticalOffset(10, -1));
	}
}
