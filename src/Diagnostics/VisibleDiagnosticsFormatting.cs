namespace WindowsOscVolumeControl.Diagnostics;

public static class VisibleDiagnosticsFormatting {
	public static string formatVisibleStatusErrors(IReadOnlyCollection<StatusError> statusErrors) {
		if (statusErrors.Count == 0)
			return "";

		return string.Join("; ", statusErrors.Select(static statusError => statusError switch {
			StatusError.Generic.Starting => "Starting",
			StatusError.MixerController.Network => "Mixer network error",
			StatusError.MixerController.InvalidReply => "Mixer invalid reply",
			StatusError.KeyboardHook.InstallFailed => "Keyboard hook install failed",
			StatusError.Application.StartupHealthFault => "Startup health fault",
			StatusError.OscTransport.BindFailed => "OSC UDP listen port in use (another copy or app); incoming OSC may be lost",
			_ => statusError.GetType().Name,
		}));
	}
}
