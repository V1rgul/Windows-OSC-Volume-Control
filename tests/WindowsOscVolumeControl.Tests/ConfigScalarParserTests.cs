using Result;
using System.Net;
using WindowsOscVolumeControl.Diagnostics;
using WindowsOscVolumeControl.Input;
using WindowsOscVolumeControl.Osc;
using WindowsOscVolumeControl.UI.Osd;

namespace WindowsOscVolumeControl.Tests;

public class ConfigScalarParserTests {
	[Theory]
	[InlineData("127.0.0.1", "127.0.0.1")]
	[InlineData(" 192.168.0.1 ", "192.168.0.1")]
	public void parseIpField_valid_returnsAddress(string text, string expected) {
		Result<IPAddress> result = OscTransport.Config.parseIpField(text);
		Assert.True(result.isSuccess);
		Assert.Equal(expected, result.value.ToString());
	}

	[Theory]
	[InlineData("")]
	[InlineData("not-an-ip")]
	public void parseIpField_invalid_returnsParsingError(string text) {
		Result<IPAddress> result = OscTransport.Config.parseIpField(text);
		Assert.True(result.isError);
		Assert.IsType<ResultError.Generic.Parsing>(result.errors[0]);
		Assert.Equal("Invalid IP address.", ((ResultErrorWithMsg)result.errors[0]).message);
	}

	[Theory]
	[InlineData("10023", 10023)]
	[InlineData(" 1 ", 1)]
	[InlineData("65535", 65535)]
	public void parsePortField_valid_returnsPort(string text, int expected) {
		Result<int> result = OscTransport.Config.parsePortField(text);
		Assert.True(result.isSuccess);
		Assert.Equal(expected, result.value);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("65536")]
	[InlineData("abc")]
	public void parsePortField_invalid_returnsParsingError(string text) {
		Result<int> result = OscTransport.Config.parsePortField(text);
		Assert.True(result.isError);
		Assert.Equal("Port must be between 1 and 65535.", ((ResultErrorWithMsg)result.errors[0]).message);
	}

	[Theory]
	[InlineData("200", 200u)]
	[InlineData("10000", 10000u)]
	public void parseTimeoutMs_valid_returnsValue(string text, uint expected) {
		Result<uint> result = MixerController.Config.parseTimeoutMs(text);
		Assert.True(result.isSuccess);
		Assert.Equal(expected, result.value);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("10001")]
	[InlineData("x")]
	public void parseTimeoutMs_invalid_returnsParsingError(string text) {
		Result<uint> result = MixerController.Config.parseTimeoutMs(text);
		Assert.True(result.isError);
	}

	[Theory]
	[InlineData("80", 80)]
	[InlineData("600", 600)]
	public void parseHeightDip_valid_returnsValue(string text, int expected) {
		Result<int> result = OSDController.Config.parseHeightDip(text);
		Assert.True(result.isSuccess);
		Assert.Equal(expected, result.value);
	}

	[Theory]
	[InlineData("47")]
	[InlineData("601")]
	public void parseHeightDip_outOfRange_returnsParsingError(string text) {
		Result<int> result = OSDController.Config.parseHeightDip(text);
		Assert.True(result.isError);
	}

	[Theory]
	[InlineData("450", 450u)]
	public void parseLongPressMs_valid_returnsValue(string text, uint expected) {
		Result<uint> result = KeyboardHook.Config.parseLongPressMs(text);
		Assert.True(result.isSuccess);
		Assert.Equal(expected, result.value);
	}

	[Theory]
	[InlineData("49")]
	[InlineData("5001")]
	public void parseLongPressMs_outOfRange_returnsParsingError(string text) {
		Result<uint> result = KeyboardHook.Config.parseLongPressMs(text);
		Assert.True(result.isError);
	}
}
