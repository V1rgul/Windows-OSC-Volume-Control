namespace WindowsOscVolumeControl;

public partial class App : System.Windows.Application {
	AppCoordinator? _coordinator;

	protected override void OnStartup(System.Windows.StartupEventArgs e) {
		base.OnStartup(e);
		_coordinator = new AppCoordinator();
	}

	protected override void OnExit(System.Windows.ExitEventArgs e) {
		_coordinator?.Dispose();
		base.OnExit(e);
	}
}
