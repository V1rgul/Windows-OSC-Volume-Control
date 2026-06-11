using SharpOSC;
using WindowsOscVolumeControl.Osc;

namespace WindowsOscVolumeControl.Tests;

/// <summary>The fast-path encoder must stay byte-for-byte compatible with SharpOSC's <see cref="OscMessage.GetBytes"/>.</summary>
public class OscMessageFastPathEncodingTests {
	[Theory]
	[InlineData("/info")]
	[InlineData("/main/st/mix/fader")]
	[InlineData("/ch/01/mix/on")]
	[InlineData("/a")]
	[InlineData("/abc")]
	public void encodeOscMessage_noArgs_matchesSharpOsc(string address) {
		Assert.Equal(new OscMessage(address).GetBytes(), OscTransport.encodeOscMessage(address, []));
	}

	[Theory]
	[InlineData("/main/st/mix/fader", 0f)]
	[InlineData("/main/st/mix/fader", 0.75f)]
	[InlineData("/main/st/mix/fader", 1f)]
	[InlineData("/ch/01/mix/fader", -0.001f)]
	public void encodeOscMessage_singleFloat_matchesSharpOsc(string address, float value) {
		Assert.Equal(new OscMessage(address, value).GetBytes(), OscTransport.encodeOscMessage(address, [value]));
	}

	[Theory]
	[InlineData("/main/st/mix/on", 0)]
	[InlineData("/main/st/mix/on", 1)]
	[InlineData("/ch/01/mix/on", -42)]
	public void encodeOscMessage_singleInt_matchesSharpOsc(string address, int value) {
		Assert.Equal(new OscMessage(address, value).GetBytes(), OscTransport.encodeOscMessage(address, [value]));
	}

	[Fact]
	public void encodeOscMessage_otherShapes_fallsBackToSharpOsc() {
		object[] args = ["text", 3];
		Assert.Equal(new OscMessage("/x", args).GetBytes(), OscTransport.encodeOscMessage("/x", args));
	}
}
