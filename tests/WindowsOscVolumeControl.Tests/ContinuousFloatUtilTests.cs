namespace WindowsOscVolumeControl.Tests;

public class ContinuousFloatUtilTests {
	[Fact]
	public void RoundToBindingDecimals_UsesProjectBindingPrecision() {
		float value = 0.1236f;

		float rounded = ContinuousFloatUtil.RoundToBindingDecimals(value);

		Assert.Equal(0.124f, rounded);
	}

	[Theory]
	[InlineData(0.02f, 2)]
	[InlineData(0.2f, 1)]
	[InlineData(1f, 0)]
	public void GetOsdFractionalDigitsFromStep_MatchesRoundedStep(float step, int expectedDigits) {
		int digits = ContinuousFloatUtil.GetOsdFractionalDigitsFromStep(step);

		Assert.Equal(expectedDigits, digits);
	}

	[Fact]
	public void FormatOsdLevelValue_RespectsRequestedPrecision() {
		string formatted = ContinuousFloatUtil.FormatOsdLevelValue(0.1256f, 2);

		Assert.Equal("0.13", formatted);
	}
}
