namespace WindowsOscVolumeControl;

/// <summary>Aggregate settings owned by <see cref="ConfigStore"/>; composed from per-component DTOs.</summary>
public sealed class AppConfig {
	public AppConfig() { }

	public AppConfig(AppConfig from) {
		ArgumentNullException.ThrowIfNull(from);
		oscTransport = new OscTransport.Config(from.oscTransport);
		mixer = new MixerController.Config(from.mixer);
		trayApp = new BindingManager.Config(from.trayApp);
		osd = new OSDController.Config(from.osd);
		keyboardHook = KeyboardHook.Config.Clamped(from.keyboardHook);
	}

	public OscTransport.Config oscTransport { get; set; } = new();
	public MixerController.Config mixer { get; set; } = new();
	public BindingManager.Config trayApp { get; set; } = new();
	public OSDController.Config osd { get; set; } = new();

	/// <summary>Low-level hotkey timing and key-delivery policy.</summary>
	public KeyboardHook.Config keyboardHook { get; set; } = KeyboardHook.Config.Clamped(null);

}
