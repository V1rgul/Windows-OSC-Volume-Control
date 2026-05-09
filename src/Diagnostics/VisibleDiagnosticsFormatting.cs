namespace WindowsOscVolumeControl.Diagnostics;

public static class VisibleDiagnosticsFormatting {
	public static string formatVisibleErrors(IReadOnlyCollection<Error> errors) {
		if (errors.Count == 0)
			return "";

		return string.Join("; ", errors.Select(static error => error switch {
			Error.Generic.Starting => "Starting",
			Error.MixerController.Network => "Mixer network error",
			Error.MixerController.InvalidReply => "Mixer invalid reply",
			Error.KeyboardHook.InstallFailed => "Keyboard hook install failed",
			Error.Application.StartupHealthFault => "Startup health fault",
			Error.OscTransport.BindFailed => "OSC UDP listen port in use (another copy or app); incoming OSC may be lost",
			_ => error.GetType().Name,
		}));
	}
}
