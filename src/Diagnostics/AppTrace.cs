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

	public static string traceLogFilePathForUi => Path.GetFullPath(Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"Windows-OSC-Volume-Control",
		"Windows-OSC-Volume-Control.log"));

	public static void initializeFileLogging() {
		lock (_fileLogInitLock) {
			if (_fileLogInitialized)
				return;
			_fileLogInitialized = true;
		}

		try {
			string path = traceLogFilePathForUi;
			string? dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir))
				Directory.CreateDirectory(dir);

			var listener = RotatingFileTraceListener.createForNewSession(path, TraceLogFileRotation.MAX_TRACE_LOG_FILE_BYTES);
			listener.Filter = new EventTypeFilter(FILE_LOG_LISTENER_MIN_LEVEL);

			foreach (TraceSource source in allTraceSources()) {
				source.Listeners.Add(listener);
				// Release: gate at the switch so hot paths can skip building trace strings via ShouldTrace.
				// Debug: keep everything flowing to the default (debugger) listener; the file filter still applies.
#if DEBUG
				source.Switch.Level = SourceLevels.All;
#else
				source.Switch.Level = FILE_LOG_LISTENER_MIN_LEVEL;
#endif
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
