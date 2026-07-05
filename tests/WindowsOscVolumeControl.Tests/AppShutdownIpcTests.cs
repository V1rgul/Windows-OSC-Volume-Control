using WindowsOscVolumeControl.App;

namespace WindowsOscVolumeControl.Tests;

public class AppShutdownIpcTests {
	[Fact]
	public void parseCommand_noArgs_runsApp() {
		AppShutdownCommand command = AppShutdownIpc.parseCommand([]);

		Assert.Equal(AppShutdownCommandKind.RUN_APP, command.kind);
	}

	[Fact]
	public void parseCommand_stopAll_stopsAll() {
		AppShutdownCommand command = AppShutdownIpc.parseCommand(["--stop-all"]);

		Assert.Equal(AppShutdownCommandKind.STOP_ALL, command.kind);
	}

	[Fact]
	public void parseCommand_stopPid_stopsProcess() {
		AppShutdownCommand command = AppShutdownIpc.parseCommand(["--stop", "123"]);

		Assert.Equal(AppShutdownCommandKind.STOP_PROCESS, command.kind);
		Assert.Equal(123, command.processId);
	}

	[Theory]
	[InlineData("--stop")]
	[InlineData("--stop", "0")]
	[InlineData("--stop", "abc")]
	[InlineData("--stop-all", "extra")]
	[InlineData("--unknown")]
	public void parseCommand_invalidArgs_areInvalid(params string[] args) {
		AppShutdownCommand command = AppShutdownIpc.parseCommand(args);

		Assert.Equal(AppShutdownCommandKind.INVALID, command.kind);
	}

	[Fact]
	public void stopEventNameForProcess_containsProcessId() {
		string name = AppShutdownIpc.stopEventNameForProcess(123);

		Assert.Contains(".123.", name, StringComparison.Ordinal);
		Assert.StartsWith(@"Local\WindowsOscVolumeControl.", name, StringComparison.Ordinal);
		Assert.EndsWith(".Stop", name, StringComparison.Ordinal);
	}
}
