using System.Linq;

namespace WindowsOscVolumeControl;

/// <summary>Aggregate settings owned by <see cref="ConfigStore"/>; composed from per-component DTOs.</summary>
public sealed class AppConfig {
	public OscController.Config oscController { get; set; } = new();
	public MixerController.Config mixer { get; set; } = new();
	public TrayApp.Config trayApp { get; set; } = new();
	public OSDController.Config osd { get; set; } = new();

	/// <summary>Deep copy for store ownership (detaches from form-held instances).</summary>
	public AppConfig deepClone() => new AppConfig {
		oscController = new OscController.Config(oscController),
		mixer = new MixerController.Config(mixer),
		trayApp = new TrayApp.Config {
			bindings = trayApp.bindings.Select(b => new OscBindingToggle(b)).ToList(),
			faderBindings = trayApp.faderBindings.Select(f => new OscBindingFader(f)).ToList(),
		},
		osd = new OSDController.Config(osd),
	};
}
