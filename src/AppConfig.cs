namespace WindowsOscVolumeControl;

/// <summary>Aggregate settings owned by <see cref="ConfigStore"/>; composed from per-component DTOs.</summary>
public sealed class AppConfig {
	public AppConfig() { }

	public AppConfig(AppConfig from) {
		ArgumentNullException.ThrowIfNull(from);
		oscController = new OscController.Config(from.oscController);
		mixer = new MixerController.Config(from.mixer);
		trayApp = new BindingManager.Config(from.trayApp);
		osd = new OSDController.Config(from.osd);
	}

	public OscController.Config oscController { get; set; } = new();
	public MixerController.Config mixer { get; set; } = new();
	public BindingManager.Config trayApp { get; set; } = new();
	public OSDController.Config osd { get; set; } = new();
}
