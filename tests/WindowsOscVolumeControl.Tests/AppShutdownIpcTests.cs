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

	[Theory]
	[InlineData("--stop-all", "extra")]
	[InlineData("--unknown")]
	public void parseCommand_invalidArgs_areInvalid(params string[] args) {
		AppShutdownCommand command = AppShutdownIpc.parseCommand(args);

		Assert.Equal(AppShutdownCommandKind.INVALID, command.kind);
	}

	[Fact]
	public void stopEventName_isSharedLocalName() {
		Assert.Equal(@"Local\WindowsOscVolumeControl.Stop", AppShutdownIpc.STOP_EVENT_NAME);
	}
}
