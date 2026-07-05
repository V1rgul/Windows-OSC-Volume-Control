using WindowsOscVolumeControl.Diagnostics;

namespace WindowsOscVolumeControl.App;

public partial class AppApplication {
	AppCoordinator? _coordinator;
	AppShutdownIpc? _shutdownIpc;

	protected override void OnStartup(System.Windows.StartupEventArgs e) {
		base.OnStartup(e);
		if (AppShutdownIpc.tryRunCommand(e.Args, out int exitCode)) {
			Shutdown(exitCode);
			return;
		}

		_shutdownIpc = AppShutdownIpc.startForCurrentProcess(requestStop);
		AppTrace.initializeFileLogging();
		_coordinator = new AppCoordinator();
	}

	protected override void OnExit(System.Windows.ExitEventArgs e) {
		_shutdownIpc?.Dispose();
		_coordinator?.Dispose();
		base.OnExit(e);
	}

	void requestStop() {
		if (!Dispatcher.CheckAccess()) {
			_ = Dispatcher.BeginInvoke(requestStop);
			return;
		}
		if (_coordinator == null)
			Shutdown();
		else
			_coordinator.Dispose();
	}
}
