using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;

namespace WindowsOscVolumeControl;

public abstract partial record Error {
	public abstract partial record KeyboardHook : Error {
		public sealed record InstallFailed : KeyboardHook;
	}
}

public partial class KeyboardHook : IDisposable {
	const int WM_KEYDOWN = 0x0100;
	const int WM_SYSKEYDOWN = 0x0104;
	const int WM_KEYUP = 0x0101;
	const int WM_SYSKEYUP = 0x0105;
	const int VK_CONTROL = 0x11;
	const int VK_SHIFT = 0x10;
	const int VK_MENU = 0x12;

	/// <summary>Wall-clock slack so a slightly early <see cref="System.Threading.Timer"/> or hook tick still counts as past the long-press duration.</summary>
	const int LONG_PRESS_DEADLINE_SLACK_MS = 45;

	delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

	sealed class ActiveHotkeyPress {
		public required HotkeyGesture gesture;
		public required HotkeyDispatchTargets targets;
		public DateTime keyDownUtc;
		public System.Threading.Timer? longTimer;
		public System.Threading.Timer? shortDeadlineTimer;
		public bool longFired;
		public bool shortFired;

		public void disposeTimers() {
			longTimer?.Dispose();
			longTimer = null;
			shortDeadlineTimer?.Dispose();
			shortDeadlineTimer = null;
		}
	}

	readonly IntPtr _hookId;
	readonly LowLevelKeyboardProc _proc;
	readonly object _configuredHotkeysSync = new();
	volatile Func<HotkeyGesture, HotkeyDispatchTargets?> _getTargets = static _ => null;
	Action<IReadOnlyList<BindingManager.Slot>> _dispatchSlots = static _ => { };
	int _longPressDurationMs = (int)BindingManager.Config.DEFAULT_LONG_PRESS_MS;
	bool _optimizeNonLongPressKeyDown = true;
	readonly Dictionary<HotkeyGesture, ActiveHotkeyPress> _activePresses = [];
	bool _configuredHotkeysEnabled = true;
	bool _disposed;

	public ErrorList<Error.KeyboardHook> errors { get; } = new();

	public KeyboardHook() {
		_proc = HookCallback;
		_hookId = SetHook(_proc);
		if (_hookId == IntPtr.Zero) {
			errors.setError(new Error.KeyboardHook.InstallFailed(), true);
			AppTrace.KeyboardHook.TraceEvent(TraceEventType.Error, 0, "SetWindowsHookEx failed.");
		}
	}

	static IntPtr SetHook(LowLevelKeyboardProc proc) {
		using var curProcess = Process.GetCurrentProcess();
		string moduleName = curProcess.MainModule?.ModuleName
			?? throw new InvalidOperationException("Could not resolve the current process module name.");
		return SetWindowsHookEx(13, proc, GetModuleHandle(moduleName), 0);
	}

	void queueDispatch(Action dispatch) {
		ThreadPool.QueueUserWorkItem(_ => dispatch());
	}

	static bool IsKeyDown(IntPtr wParam) => wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
	static bool IsKeyUp(IntPtr wParam) => wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;

	/// <summary>Replaces hotkey resolution and timing. Invoked from the hook thread.</summary>
	public void setHotkeyDispatch(
		Func<HotkeyGesture, HotkeyDispatchTargets?> getTargets,
		Action<IReadOnlyList<BindingManager.Slot>> dispatchSlots,
		uint longPressDurationMs,
		bool optimizeNonLongPressKeyDown) {
		ArgumentNullException.ThrowIfNull(getTargets);
		ArgumentNullException.ThrowIfNull(dispatchSlots);
		lock (_configuredHotkeysSync) {
			cancelAllActivePressesLocked();
			_getTargets = getTargets;
			_dispatchSlots = dispatchSlots;
			_longPressDurationMs = (int)Math.Clamp(longPressDurationMs, 1u, int.MaxValue);
			_optimizeNonLongPressKeyDown = optimizeNonLongPressKeyDown;
		}
	}

	public void SetConfiguredHotkeysEnabled(bool enabled) {
		lock (_configuredHotkeysSync) {
			_configuredHotkeysEnabled = enabled;
			if (!enabled)
				cancelAllActivePressesLocked();
		}
	}

	void cancelAllActivePressesLocked() {
		foreach (ActiveHotkeyPress p in _activePresses.Values)
			p.disposeTimers();
		_activePresses.Clear();
	}

	static bool IsModifierVirtualKey(int vkCode) =>
		HotkeyUtil.isModifierKey(KeyInterop.KeyFromVirtualKey(vkCode));

	static HotkeyModifiers GetActiveModifiers() {
		HotkeyModifiers modifiers = HotkeyModifiers.NONE;
		if (IsVirtualKeyDown(VK_CONTROL))
			modifiers |= HotkeyModifiers.CONTROL;
		if (IsVirtualKeyDown(VK_SHIFT))
			modifiers |= HotkeyModifiers.SHIFT;
		if (IsVirtualKeyDown(VK_MENU))
			modifiers |= HotkeyModifiers.ALT;
		return modifiers;
	}

	static bool IsVirtualKeyDown(int vkCode) => (GetAsyncKeyState(vkCode) & 0x8000) != 0;

	static bool gestureMainKeyHeld(HotkeyGesture g) => IsVirtualKeyDown(g.keyCode);

	static bool gestureModifiersMatch(HotkeyGesture g) =>
		(GetActiveModifiers() & (HotkeyModifiers.CONTROL | HotkeyModifiers.SHIFT | HotkeyModifiers.ALT)) == g.modifiers;

	static bool gestureAppearsHeld(HotkeyGesture g) => gestureMainKeyHeld(g) && gestureModifiersMatch(g);

	/// <summary>Completes long-press using <see cref="ActiveHotkeyPress.keyDownUtc"/> only (no GetAsyncKeyState), matching keyup semantics.</summary>
	bool tryTakeLongPressIfDueLocked(ActiveHotkeyPress press, DateTime nowUtc, out IReadOnlyList<BindingManager.Slot>? longSlots) {
		longSlots = null;
		if (press.longFired || press.targets.longPressSlots.Count == 0)
			return false;
		double heldMs = (nowUtc - press.keyDownUtc).TotalMilliseconds;
		if (heldMs + LONG_PRESS_DEADLINE_SLACK_MS < _longPressDurationMs)
			return false;
		press.longFired = true;
		press.disposeTimers();
		longSlots = press.targets.longPressSlots;
		return true;
	}

	void processLongPressDeadlinesLocked() {
		if (_activePresses.Count == 0)
			return;
		DateTime nowUtc = DateTime.UtcNow;
		foreach (KeyValuePair<HotkeyGesture, ActiveHotkeyPress> kv in _activePresses) {
			if (!tryTakeLongPressIfDueLocked(kv.Value, nowUtc, out IReadOnlyList<BindingManager.Slot>? slots) || slots == null || slots.Count == 0)
				continue;
			IReadOnlyList<BindingManager.Slot> captured = slots;
			queueDispatch(() => _dispatchSlots(captured));
		}
	}

	bool tryHandleConfiguredHotkey(IntPtr wParam, int vkCode) {
		lock (_configuredHotkeysSync) {
			if (!_configuredHotkeysEnabled || _hookId == IntPtr.Zero)
				return false;

			processLongPressDeadlinesLocked();

			if (IsModifierVirtualKey(vkCode))
				return false;

			if (IsKeyUp(wParam))
				return tryHandleKeyUpLocked(vkCode);

			if (!IsKeyDown(wParam))
				return false;

			return tryHandleKeyDownLocked(vkCode);
		}
	}

	bool tryHandleKeyDownLocked(int vkCode) {
		HotkeyGesture candidate = HotkeyUtil.normalize(new HotkeyGesture {
			keyCode = vkCode,
			modifiers = GetActiveModifiers(),
		});
		if (candidate.isNone)
			return false;

		if (_activePresses.ContainsKey(candidate))
			return true;

		HotkeyDispatchTargets? targetsNullable = _getTargets(candidate);
		if (targetsNullable is not { } targets || !targets.hasAny)
			return false;

		bool hasLong = targets.longPressSlots.Count > 0;
		bool hasShort = targets.shortPressSlots.Count > 0;
		bool effectiveOptimize = _optimizeNonLongPressKeyDown && !hasLong;

		var press = new ActiveHotkeyPress {
			gesture = candidate,
			targets = targets,
			keyDownUtc = DateTime.UtcNow,
		};
		_activePresses[candidate] = press;

		if (hasLong) {
			ActiveHotkeyPress pressForTimer = press;
			press.longTimer = new System.Threading.Timer(_ => onLongTimerFired(pressForTimer), null, _longPressDurationMs, Timeout.Infinite);
		}

		if (hasShort) {
			if (effectiveOptimize) {
				press.shortFired = true;
				IReadOnlyList<BindingManager.Slot> shortList = targets.shortPressSlots;
				queueDispatch(() => _dispatchSlots(shortList));
			} else if (hasLong) {
				// Short fires on keyup if released before long timer.
			} else {
				HotkeyGesture gCap = candidate;
				press.shortDeadlineTimer = new System.Threading.Timer(_ => onShortDeadlineFired(gCap), null, _longPressDurationMs, Timeout.Infinite);
			}
		}

		return true;
	}

	void onLongTimerFired(ActiveHotkeyPress expected) {
		IReadOnlyList<BindingManager.Slot>? longList = null;
		lock (_configuredHotkeysSync) {
			if (!_activePresses.Values.Contains(expected))
				return;
			_ = tryTakeLongPressIfDueLocked(expected, DateTime.UtcNow, out longList);
		}
		if (longList != null && longList.Count > 0)
			queueDispatch(() => _dispatchSlots(longList));
	}

	void onShortDeadlineFired(HotkeyGesture g) {
		IReadOnlyList<BindingManager.Slot>? shortList = null;
		lock (_configuredHotkeysSync) {
			if (!_activePresses.TryGetValue(g, out ActiveHotkeyPress? press))
				return;
			if (press.shortFired)
				return;
			if (!gestureAppearsHeld(g))
				return;
			press.shortFired = true;
			press.shortDeadlineTimer?.Dispose();
			press.shortDeadlineTimer = null;
			shortList = press.targets.shortPressSlots;
		}
		if (shortList != null && shortList.Count > 0)
			queueDispatch(() => _dispatchSlots(shortList));
	}

	bool tryHandleKeyUpLocked(int vkCode) {
		HotkeyGesture candidate = HotkeyUtil.normalize(new HotkeyGesture {
			keyCode = vkCode,
			modifiers = GetActiveModifiers(),
		});

		if (!_activePresses.TryGetValue(candidate, out ActiveHotkeyPress? press))
			return false;

		press.disposeTimers();

		bool hasLongBucket = press.targets.longPressSlots.Count > 0;
		double heldMs = (DateTime.UtcNow - press.keyDownUtc).TotalMilliseconds;

		IReadOnlyList<BindingManager.Slot>? shortList = null;
		IReadOnlyList<BindingManager.Slot>? longListFromKeyUp = null;

		if (!press.longFired && !press.shortFired) {
			if (hasLongBucket) {
				// Avoid racing WM_KEYUP ahead of the timer thread: hold past duration ⇒ never short.
				if (heldMs >= _longPressDurationMs)
					longListFromKeyUp = press.targets.longPressSlots;
				else if (press.targets.shortPressSlots.Count > 0)
					shortList = press.targets.shortPressSlots;
			} else if (press.targets.shortPressSlots.Count > 0) {
				shortList = press.targets.shortPressSlots;
			}
		}

		_activePresses.Remove(candidate);

		if (longListFromKeyUp != null && longListFromKeyUp.Count > 0)
			queueDispatch(() => _dispatchSlots(longListFromKeyUp));
		else if (shortList != null && shortList.Count > 0)
			queueDispatch(() => _dispatchSlots(shortList));

		return true;
	}

	IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
		if (nCode >= 0) {
			int vkCode = Marshal.ReadInt32(lParam);
			if (tryHandleConfiguredHotkey(wParam, vkCode))
				return (IntPtr)1;
		}
		return CallNextHookEx(_hookId, nCode, wParam, lParam);
	}

	public void Dispose() {
		if (_disposed)
			return;

		lock (_configuredHotkeysSync)
			cancelAllActivePressesLocked();

		if (_hookId != IntPtr.Zero)
			UnhookWindowsHookEx(_hookId);
		_disposed = true;
	}

	[LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
	private static partial IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool UnhookWindowsHookEx(IntPtr hhk);

	[LibraryImport("user32.dll")]
	private static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

	[LibraryImport("user32.dll")]
	private static partial short GetAsyncKeyState(int vKey);

	[LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
	private static partial IntPtr GetModuleHandle(string lpModuleName);
}
