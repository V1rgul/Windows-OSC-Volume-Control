using System.Diagnostics;
using System.Threading;

namespace WindowsOscVolumeControl.App;

enum AppShutdownCommandKind {
	RUN_APP,
	STOP_ALL,
	INVALID,
}

readonly record struct AppShutdownCommand(AppShutdownCommandKind kind);

sealed class AppShutdownIpc : IDisposable {
	internal const string STOP_EVENT_NAME = @"Local\WindowsOscVolumeControl.Stop";
	const int STOP_TIMEOUT_MS = 5000;
	public const int EXIT_OK = 0;
	public const int EXIT_TIMEOUT = 2;
	public const int EXIT_USAGE = 64;

	readonly EventWaitHandle _stopEvent;
	readonly RegisteredWaitHandle _registeredWait;
	bool _disposed;

	AppShutdownIpc(EventWaitHandle stopEvent, RegisteredWaitHandle registeredWait) {
		_stopEvent = stopEvent;
		_registeredWait = registeredWait;
	}

	public static AppShutdownIpc startForCurrentProcess(Action onStopRequested) {
		ArgumentNullException.ThrowIfNull(onStopRequested);
		var stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, STOP_EVENT_NAME);
		// A prior --stop-all may have left the event signaled if it exited before Reset.
		_ = stopEvent.Reset();
		RegisteredWaitHandle registeredWait = ThreadPool.RegisterWaitForSingleObject(
			stopEvent,
			(_, _) => onStopRequested(),
			null,
			Timeout.InfiniteTimeSpan,
			executeOnlyOnce: true);
		return new AppShutdownIpc(stopEvent, registeredWait);
	}

	public static bool tryRunCommand(string[] args, out int exitCode) {
		AppShutdownCommand command = parseCommand(args);
		switch (command.kind) {
			case AppShutdownCommandKind.RUN_APP:
				exitCode = EXIT_OK;
				return false;
			case AppShutdownCommandKind.STOP_ALL:
				exitCode = signalStopAndWaitForPeers();
				return true;
			default:
				exitCode = EXIT_USAGE;
				return true;
		}
	}

	internal static AppShutdownCommand parseCommand(IReadOnlyList<string> args) {
		if (args.Count == 0)
			return new AppShutdownCommand(AppShutdownCommandKind.RUN_APP);
		if (args.Count == 1 && string.Equals(args[0], "--stop-all", StringComparison.OrdinalIgnoreCase))
			return new AppShutdownCommand(AppShutdownCommandKind.STOP_ALL);
		return new AppShutdownCommand(AppShutdownCommandKind.INVALID);
	}

	static int signalStopAndWaitForPeers() {
		using EventWaitHandle? stopEvent = tryOpenAndSetStopEvent();
		if (stopEvent is null)
			return EXIT_OK;

		int currentProcessId = Environment.ProcessId;
		string processName = Process.GetCurrentProcess().ProcessName;
		int worstExitCode = EXIT_OK;
		var deadline = Environment.TickCount64 + STOP_TIMEOUT_MS;

		foreach (Process process in Process.GetProcessesByName(processName)) {
			using (process) {
				if (process.Id == currentProcessId)
					continue;
				int remainingMs = (int)Math.Max(0, deadline - Environment.TickCount64);
				try {
					if (!process.WaitForExit(remainingMs))
						worstExitCode = EXIT_TIMEOUT;
				} catch (InvalidOperationException) {
					// already exited
				}
			}
		}

		_ = stopEvent.Reset();
		return worstExitCode;
	}

	static EventWaitHandle? tryOpenAndSetStopEvent() {
		try {
			var stopEvent = EventWaitHandle.OpenExisting(STOP_EVENT_NAME);
			_ = stopEvent.Set();
			return stopEvent;
		} catch (WaitHandleCannotBeOpenedException) {
			return null;
		} catch (UnauthorizedAccessException) {
			return null;
		}
	}

	public void Dispose() {
		if (_disposed)
			return;
		_disposed = true;
		_ = _registeredWait.Unregister(null);
		_stopEvent.Dispose();
	}
}
