namespace WindowsOscVolumeControl.Diagnostics;

public static class VisibleDiagnosticsFormatting {
	public static string formatVisibleStatusErrors(IReadOnlyCollection<Type> statusErrorTypes) {
		if (statusErrorTypes.Count == 0)
			return "";

		return string.Join("; ", statusErrorTypes.Select(static statusErrorType => statusErrorType switch {
			var t when t == typeof(StatusError.Generic.Starting) => "Starting",
			var t when t == typeof(StatusError.MixerController.Network) => "Mixer network error",
			var t when t == typeof(StatusError.MixerController.InvalidReply) => "Mixer invalid reply",
			var t when t == typeof(StatusError.KeyboardHook.InstallFailed) => "Keyboard hook install failed",
			var t when t == typeof(StatusError.Application.StartupHealthFault) => "Startup health fault",
			var t when t == typeof(StatusError.OscTransport.BindFailed) => "OSC UDP listen port in use (another copy or app); incoming OSC may be lost",
			_ => statusErrorType.Name,
		}));
	}
}
