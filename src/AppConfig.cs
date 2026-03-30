using System.Linq;

namespace WindowsOscVolumeControl;

/// <summary>Aggregate settings owned by <see cref="ConfigStore"/>; composed from per-component DTOs.</summary>
public sealed class AppConfig {
	public OscController.Config OscController { get; set; } = new();
	public MixerController.Config Mixer { get; set; } = new();
	public TrayApp.Config TrayApp { get; set; } = new();
	public OSDController.Config Osd { get; set; } = new();

	/// <summary>Deep copy for store ownership (detaches from form-held instances).</summary>
	public AppConfig DeepClone() => new AppConfig {
		OscController = new OscController.Config(OscController),
		Mixer = new MixerController.Config(Mixer),
		TrayApp = new TrayApp.Config {
			Bindings = TrayApp.Bindings.Select(b => new OscToggleBinding(b)).ToList(),
			FaderBindings = TrayApp.FaderBindings.Select(f => new OscFaderBinding(f)).ToList(),
		},
		Osd = new OSDController.Config(Osd),
	};
}
