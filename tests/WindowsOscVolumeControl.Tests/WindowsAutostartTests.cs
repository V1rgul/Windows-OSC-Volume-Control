namespace WindowsOscVolumeControl.Tests;

public class WindowsAutostartTests {
	[Theory]
	[InlineData(@"""C:\Program Files\App\a.exe"" --foo", @"C:\Program Files\App\a.exe")]
	[InlineData(@"C:\b\App.exe arg", @"C:\b\App.exe")]
	[InlineData(@"C:\b\App.exe", @"C:\b\App.exe")]
	[InlineData(@"""D:\x y\z.exe""", @"D:\x y\z.exe")]
	public void tryParseRunCommandFirstExecutable_parses(string raw, string expected) {
		bool ok = WindowsAutostart.tryParseRunCommandFirstExecutable(raw, out string? path);

		Assert.True(ok);
		Assert.Equal(expected, path);
	}

	[Theory]
	[InlineData("", false)]
	[InlineData("   ", false)]
	[InlineData(@"""C:\unclosed", false)]
	[InlineData(@"""", false)]
	public void tryParseRunCommandFirstExecutable_rejects(string raw, bool expectOk) {
		bool ok = WindowsAutostart.tryParseRunCommandFirstExecutable(raw, out _);

		Assert.Equal(expectOk, ok);
	}

	[Fact]
	public void pathsEqualForAutostart_isOrdinalIgnoreCaseOnWindows() {
		Assert.True(WindowsAutostart.pathsEqualForAutostart(
			@"C:\Windows\System32\notepad.exe",
			@"c:/Windows/System32/NOTEPAD.EXE"));
		Assert.False(WindowsAutostart.pathsEqualForAutostart(
			@"C:\Windows\System32\notepad.exe",
			@"C:\Windows\System32\write.exe"));
	}
}
