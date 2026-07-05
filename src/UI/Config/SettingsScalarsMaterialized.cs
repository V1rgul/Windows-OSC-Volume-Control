using System.Net;
using WindowsOscVolumeControl.UI.Config.ViewModels;

namespace WindowsOscVolumeControl.UI.Config;

public readonly record struct SettingsScalarsMaterialized(
	IPEndPoint oscEndPoint,
	uint queryTimeoutMs,
	uint valueCacheTtlMs,
	int osdHeightDip,
	uint osdDisplayDurationMs,
	uint hotkeyLongPressMs);

internal static class ScalarPropertyNames {
	internal static readonly string[] all = [
		nameof(ConfigWindowViewModel.oscIpText),
		nameof(ConfigWindowViewModel.oscPortText),
		nameof(ConfigWindowViewModel.queryTimeoutText),
		nameof(ConfigWindowViewModel.valueCacheTtlText),
		nameof(ConfigWindowViewModel.osdHeightText),
		nameof(ConfigWindowViewModel.osdDurationText),
		nameof(ConfigWindowViewModel.hotkeyLongPressMsText),
	];

	internal static readonly IReadOnlyDictionary<string, string> humanLabels = new Dictionary<string, string> {
		[nameof(ConfigWindowViewModel.oscIpText)] = "OSC IP",
		[nameof(ConfigWindowViewModel.oscPortText)] = "OSC port",
		[nameof(ConfigWindowViewModel.queryTimeoutText)] = "Query timeout",
		[nameof(ConfigWindowViewModel.valueCacheTtlText)] = "Value cache TTL",
		[nameof(ConfigWindowViewModel.osdHeightText)] = "OSD height",
		[nameof(ConfigWindowViewModel.osdDurationText)] = "OSD display duration",
		[nameof(ConfigWindowViewModel.hotkeyLongPressMsText)] = "Long-press duration",
	};
}
