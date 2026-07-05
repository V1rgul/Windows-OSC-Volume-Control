using System.Net;
using WindowsOscVolumeControl.UI.Config;
using WindowsOscVolumeControl.UI.Osd;

namespace WindowsOscVolumeControl.Tests;

public class ConfigStoreScalarLoadTests {
	[Fact]
	public void loadAppConfigFromKeyValueText_outOfRangeTimeout_usesDefaultAndRepairNote() {
		const string text = """
			ip=127.0.0.1
			port=10023
			timeoutMs=50000
			osc.0.name=T
			osc.0.address=/t
			osc.0.type=toggle
			""";
		AppConfig cfg = ConfigStore.loadAppConfigFromKeyValueTextForTests(text, out List<string> repairNotes);
		Assert.Equal(200u, cfg.mixer.timeoutMs);
		Assert.Contains(repairNotes, n => n.Contains("timeoutMs", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void loadAppConfigFromKeyValueText_outOfRangeOsdHeight_usesDefaultAndRepairNote() {
		const string text = """
			ip=127.0.0.1
			port=10023
			osdHeightDip=10
			osc.0.name=T
			osc.0.address=/t
			osc.0.type=toggle
			""";
		AppConfig cfg = ConfigStore.loadAppConfigFromKeyValueTextForTests(text, out List<string> repairNotes);
		Assert.Equal(80, cfg.osd.heightDip);
		Assert.Contains(repairNotes, n => n.Contains("osdHeightDip", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void loadAppConfigFromKeyValueText_invalidIp_usesOscDefaultsAndRepairNote() {
		const string text = """
			ip=not-valid
			port=10023
			osc.0.name=T
			osc.0.address=/t
			osc.0.type=toggle
			""";
		AppConfig cfg = ConfigStore.loadAppConfigFromKeyValueTextForTests(text, out List<string> repairNotes);
		Assert.Equal(IPAddress.Parse("127.0.0.1"), cfg.oscTransport.endPoint.Address);
		Assert.Equal(10023, cfg.oscTransport.endPoint.Port);
		Assert.Contains(repairNotes, n => n.Contains("OSC IP/port", StringComparison.OrdinalIgnoreCase));
	}
}

public class SettingsFormDraftScalarTests {
	[Fact]
	public void tryBuild_materializedScalars_buildsWithoutReparse() {
		var scalars = new SettingsScalarsMaterialized(
			new IPEndPoint(IPAddress.Loopback, 10023),
			200,
			1000,
			80,
			1000,
			450);
		(bool ok, AppConfig? config, UiTextFeedback? error) = SettingsFormDraft.tryBuild(
			scalars,
			OSDController.Config.OsdScreenAnchor.BOTTOM_CENTER,
			true,
			false,
			true,
			[]);
		Assert.True(ok);
		Assert.Null(error);
		Assert.NotNull(config);
		Assert.Equal(200u, config!.mixer.timeoutMs);
		Assert.Equal(1000u, config.mixer.ValueCacheTtlMs);
		Assert.Equal(80, config.osd.heightDip);
		Assert.Equal(450u, config.keyboardHook.longPressDurationMs);
	}
}
