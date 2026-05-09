using WindowsOscVolumeControl.Diagnostics;

namespace WindowsOscVolumeControl.App;

public partial class AppApplication {
	AppCoordinator? _coordinator;

	protected override void OnStartup(System.Windows.StartupEventArgs e) {
		base.OnStartup(e);
		AppTrace.initializeFileLogging();
		_coordinator = new AppCoordinator();
	}

	protected override void OnExit(System.Windows.ExitEventArgs e) {
		_coordinator?.Dispose();
		base.OnExit(e);
	}
}
