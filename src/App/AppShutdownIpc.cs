using System.Diagnostics;
using System.Security.Principal;
using System.Threading;

namespace WindowsOscVolumeControl.App;

enum AppShutdownCommandKind {
	RUN_APP,
	STOP_ALL,
	STOP_PROCESS,
	INVALID,
}

readonly record struct AppShutdownCommand(AppShutdownCommandKind kind, int processId);

sealed class AppShutdownIpc : IDisposable {
	const string EVENT_PREFIX = @"Local\WindowsOscVolumeControl";
	const string EVENT_SUFFIX = "Stop";
	const int STOP_TIMEOUT_MS = 5000;
	public const int EXIT_OK = 0;
	public const int EXIT_SIGNAL_FAILED = 1;
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
		string eventName = stopEventNameForProcess(Environment.ProcessId);
		var stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
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
				exitCode = stopAll();
				return true;
			case AppShutdownCommandKind.STOP_PROCESS:
				exitCode = stopProcess(command.processId);
				return true;
			default:
				exitCode = EXIT_USAGE;
				return true;
		}
	}

	internal static AppShutdownCommand parseCommand(IReadOnlyList<string> args) {
		if (args.Count == 0)
			return new AppShutdownCommand(AppShutdownCommandKind.RUN_APP, 0);
		if (args.Count == 1 && string.Equals(args[0], "--stop-all", StringComparison.OrdinalIgnoreCase))
			return new AppShutdownCommand(AppShutdownCommandKind.STOP_ALL, 0);
		if (args.Count == 2 && string.Equals(args[0], "--stop", StringComparison.OrdinalIgnoreCase)) {
			if (int.TryParse(args[1], out int pid) && pid > 0)
				return new AppShutdownCommand(AppShutdownCommandKind.STOP_PROCESS, pid);
			return new AppShutdownCommand(AppShutdownCommandKind.INVALID, 0);
		}
		return new AppShutdownCommand(AppShutdownCommandKind.INVALID, 0);
	}

	static int stopAll() {
		Process current = Process.GetCurrentProcess();
		string processName = current.ProcessName;
		string? currentPath = getProcessPath(current);
		int worstExitCode = EXIT_OK;

		foreach (Process process in Process.GetProcessesByName(processName)) {
			using (process) {
				if (!isStopAllTarget(process, current.Id, current.SessionId, currentPath))
					continue;
				int exitCode = stopProcess(process);
				if (exitCode == EXIT_TIMEOUT)
					worstExitCode = EXIT_TIMEOUT;
				else if (exitCode != EXIT_OK && worstExitCode == EXIT_OK)
					worstExitCode = exitCode;
			}
		}

		return worstExitCode;
	}

	static int stopProcess(int processId) {
		try {
			using Process process = Process.GetProcessById(processId);
			Process current = Process.GetCurrentProcess();
			if (!isStopAllTarget(process, current.Id, current.SessionId, getProcessPath(current)))
				return EXIT_SIGNAL_FAILED;
			return stopProcess(process);
		} catch (ArgumentException) {
			return EXIT_OK;
		}
	}

	static int stopProcess(Process process) {
		if (process.HasExited)
			return EXIT_OK;

		try {
			using EventWaitHandle stopEvent = EventWaitHandle.OpenExisting(stopEventNameForProcess(process.Id));
			_ = stopEvent.Set();
		} catch (WaitHandleCannotBeOpenedException) {
			process.Refresh();
			return process.HasExited ? EXIT_OK : EXIT_SIGNAL_FAILED;
		} catch (UnauthorizedAccessException) {
			process.Refresh();
			return process.HasExited ? EXIT_OK : EXIT_SIGNAL_FAILED;
		}

		try {
			return process.WaitForExit(STOP_TIMEOUT_MS) ? EXIT_OK : EXIT_TIMEOUT;
		} catch (InvalidOperationException) {
			return EXIT_OK;
		}
	}

	static bool isStopAllTarget(Process process, int currentProcessId, int currentSessionId, string? currentPath) {
		try {
			if (process.Id == currentProcessId || process.HasExited)
				return false;
			if (process.SessionId != currentSessionId)
				return false;
			string? processPath = getProcessPath(process);
			return WindowsAutostart.pathsEqualForAutostart(processPath, currentPath);
		} catch {
			return false;
		}
	}

	static string? getProcessPath(Process process) {
		try {
			return process.MainModule?.FileName;
		} catch {
			return null;
		}
	}

	internal static string stopEventNameForProcess(int processId) =>
		$"{EVENT_PREFIX}.{currentUserSidForName()}.{processId}.{EVENT_SUFFIX}";

	static string currentUserSidForName() {
		try {
			return WindowsIdentity.GetCurrent().User?.Value ?? "UnknownUser";
		} catch {
			return "UnknownUser";
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
