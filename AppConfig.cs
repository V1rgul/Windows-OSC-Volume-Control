using System.Linq;
using System.Net;

namespace X32VolumeHijacker;

/// <summary>Aggregate settings owned by <see cref="ConfigStore"/>; composed from per-component DTOs.</summary>
public sealed class AppConfig {
	public OscController.Config Osc { get; set; } = new();
	public FaderVolumeAdjuster.Config Fader { get; set; } = new();
	public TrayApp.Config OscToggles { get; set; } = new();

	public static AppConfig CreateDefaults() => new AppConfig {
		Osc = new OscController.Config {
			EndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 10023),
			faderAddress = "/main/st/mix/fader",
			timeoutMs = ConfigStore.DefaultQueryTimeoutMs,
		},
		Fader = new FaderVolumeAdjuster.Config(),
		OscToggles = new TrayApp.Config(),
	};

	/// <summary>Deep copy for store ownership (detaches from form-held instances).</summary>
	public AppConfig DeepClone() => new AppConfig {
		Osc = new OscController.Config(Osc),
		Fader = new FaderVolumeAdjuster.Config(Fader),
		OscToggles = new TrayApp.Config {
			Bindings = OscToggles.Bindings.Select(b => new OscToggleBinding(b)).ToList(),
		},
	};
}
