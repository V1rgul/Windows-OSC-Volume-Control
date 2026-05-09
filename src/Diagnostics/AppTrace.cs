using System.Diagnostics;
using System.IO;

namespace WindowsOscVolumeControl.Diagnostics;

static class AppTrace {
	const SourceLevels FILE_LOG_LISTENER_MIN_LEVEL = SourceLevels.Warning;
	public static readonly TraceSource Application = new(nameof(Application));
	public static readonly TraceSource BindingManager = new(nameof(BindingManager));
	public static readonly TraceSource KeyboardHook = new(nameof(KeyboardHook));
	public static readonly TraceSource OscTransport = new(nameof(OscTransport));
	public static readonly TraceSource StatusController = new(nameof(StatusController));

	static readonly object _fileLogInitLock = new();
	static bool _fileLogInitialized;

	public static void initializeFileLogging() {
		lock (_fileLogInitLock) {
			if (_fileLogInitialized)
				return;
			_fileLogInitialized = true;
		}

		try {
			string dir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"Windows-OSC-Volume-Control");
			Directory.CreateDirectory(dir);
			string path = Path.Combine(dir, "Windows-OSC-Volume-Control.log");

			var listener = RotatingFileTraceListener.createForNewSession(path, TraceLogFileRotation.MAX_TRACE_LOG_FILE_BYTES);
			listener.Filter = new EventTypeFilter(FILE_LOG_LISTENER_MIN_LEVEL);

			foreach (TraceSource source in allTraceSources()) {
				source.Listeners.Add(listener);
				source.Switch.Level = SourceLevels.All;
			}

			Trace.AutoFlush = true;
		} catch {
			// best-effort: startup must not fail on logging
		}
	}

	static IEnumerable<TraceSource> allTraceSources() {
		yield return Application;
		yield return BindingManager;
		yield return KeyboardHook;
		yield return OscTransport;
		yield return StatusController;
	}
}
