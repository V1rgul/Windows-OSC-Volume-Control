using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Result;
using WindowsOscVolumeControl.Diagnostics;

namespace WindowsOscVolumeControl.Diagnostics {
	public abstract partial record StatusError {
		public abstract record KeyboardHook : StatusError {
			public sealed record InstallFailed : KeyboardHook;
		}
	}
}

namespace WindowsOscVolumeControl.Input {

public partial class KeyboardHook : IDisposable {
	/// <summary>Low-level hotkey timing and key-delivery policy; persisted on <see cref="AppConfig.keyboardHook"/>.</summary>
	public sealed class Config {
		public const uint DEFAULT_LONG_PRESS_MS = 450;
		public const uint MIN_LONG_PRESS_MS = 50;
		public const uint MAX_LONG_PRESS_MS = 5000;

		public uint longPressDurationMs = DEFAULT_LONG_PRESS_MS;

		/// <summary>When true, short-press rows fire on keydown (unless long-press rows exist for the same gesture).</summary>
		public bool optimizeNonLongPressKeyDown { get; set; } = true;

		/// <summary>When true, key down/up for gestures with only long-press actions are not delivered to other applications.</summary>
		public bool suppressKeyForLongPressOnlyGestures { get; set; }

		/// <summary>
		/// When true, allow macro-like ordering where modifier key-up may arrive before the main key-up (e.g. RightCtrl↓ F11↓ RightCtrl↑ F11↑).
		/// </summary>
		public bool acceptMacroChordKeyOrder { get; set; } = true;

		public Config() { }

		public Config(Config other) {
			ArgumentNullException.ThrowIfNull(other);
			Config c = Clamped(other);
			longPressDurationMs = c.longPressDurationMs;
			optimizeNonLongPressKeyDown = c.optimizeNonLongPressKeyDown;
			suppressKeyForLongPressOnlyGestures = c.suppressKeyForLongPressOnlyGestures;
			acceptMacroChordKeyOrder = c.acceptMacroChordKeyOrder;
		}

		public static Config Clamped(Config? raw) {
			raw ??= new Config();
			return new Config {
				longPressDurationMs = Math.Clamp(raw.longPressDurationMs, MIN_LONG_PRESS_MS, MAX_LONG_PRESS_MS),
				optimizeNonLongPressKeyDown = raw.optimizeNonLongPressKeyDown,
				suppressKeyForLongPressOnlyGestures = raw.suppressKeyForLongPressOnlyGestures,
				acceptMacroChordKeyOrder = raw.acceptMacroChordKeyOrder,
			};
		}

		public static Result<uint> parseLongPressMs(string? text) {
			if (!uint.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed))
				return new ResultError.Generic.Parsing { message = "Must be an integer." };
			if (parsed < MIN_LONG_PRESS_MS || parsed > MAX_LONG_PRESS_MS)
				return new ResultError.Generic.Parsing { message = $"Must be between {MIN_LONG_PRESS_MS} and {MAX_LONG_PRESS_MS}." };
			return parsed;
		}
	}

	const int WM_KEYDOWN = 0x0100;
	const int WM_SYSKEYDOWN = 0x0104;
	const int WM_KEYUP = 0x0101;
	const int WM_SYSKEYUP = 0x0105;
	const uint WM_QUIT = 0x0012;
	const int VK_TABLE_SIZE = 256;
	const int HOOK_THREAD_WAIT_MS = 5000;
	const int VK_LSHIFT = 0xA0;
	const int VK_RSHIFT = 0xA1;
	const int VK_LCONTROL = 0xA2;
	const int VK_RCONTROL = 0xA3;
	const int VK_LMENU = 0xA4;
	const int VK_RMENU = 0xA5;

	/// <summary>Wall-clock slack so a slightly early <see cref="System.Threading.Timer"/> or hook tick still counts as past the long-press duration.</summary>
	const int LONG_PRESS_DEADLINE_SLACK_MS = 45;

	delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

	sealed class ActiveHotkeyPress {
		public required HotkeyDispatchTargets targets;
		public required bool swallowKeyEvents;
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

	// ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
	readonly LowLevelKeyboardProc _proc;
	readonly Thread _hookThread;
	readonly ManualResetEventSlim _hookInstalled = new(false);
	IntPtr _hookId;
	uint _hookThreadId;
	readonly object _configuredHotkeysSync = new();
	volatile Func<HotkeyGesture, HotkeyDispatchTargets?> _getTargets = static _ => null;
	Action<IReadOnlyList<BindingManager.Slot>> _dispatchSlots = static _ => { };
	int _longPressDurationMs = (int)Config.DEFAULT_LONG_PRESS_MS;
	bool _optimizeNonLongPressKeyDown = true;
	bool _suppressKeyForLongPressOnlyGestures;
	bool _acceptMacroChordKeyOrder = true;
	readonly Dictionary<HotkeyGesture, ActiveHotkeyPress> _activePresses = [];
	/// <summary>Lock-free fast-path table of VK codes bound as hotkey main keys; swapped wholesale in <see cref="setHotkeyDispatch"/>.</summary>
	volatile bool[] _boundMainKeyVkTable = new bool[VK_TABLE_SIZE];
	volatile bool _hasActivePresses;
	bool _configuredHotkeysEnabled = true;
	volatile bool _disposed;

	public StatusRegister<StatusError.KeyboardHook> statusRegister { get; } = new();

	public KeyboardHook() {
		_proc = HookCallback;
		// WH_KEYBOARD_LL callbacks run on the installing thread. A dedicated message-pump thread keeps
		// system-wide keyboard latency independent of WPF UI-thread stalls (layout, blocking GC, ...).
		_hookThread = new Thread(hookThreadProc) {
			IsBackground = true,
			Name = "KeyboardHookMessagePump",
		};
		_hookThread.Start();

		if (!_hookInstalled.Wait(HOOK_THREAD_WAIT_MS) || _hookId == IntPtr.Zero) {
			statusRegister.setStatusError<StatusError.KeyboardHook.InstallFailed>(true);
			AppTrace.KeyboardHook.TraceEvent(TraceEventType.Error, 0, "SetWindowsHookEx failed.");
		}
	}

	void hookThreadProc() {
		// Touch the message queue before signaling readiness so Dispose can reliably PostThreadMessage(WM_QUIT).
		_ = PeekMessage(out MSG _, IntPtr.Zero, 0, 0, 0);
		_hookThreadId = GetCurrentThreadId();
		_hookId = SetHook(_proc);
		_hookInstalled.Set();
		if (_hookId == IntPtr.Zero)
			return;

		while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0) {
			_ = TranslateMessage(ref msg);
			_ = DispatchMessage(ref msg);
		}

		_ = UnhookWindowsHookEx(_hookId);
	}

	static IntPtr SetHook(LowLevelKeyboardProc proc) {
		using var curProcess = Process.GetCurrentProcess();
		string moduleName = curProcess.MainModule?.ModuleName
			?? throw new InvalidOperationException("Could not resolve the current process module name.");
		return SetWindowsHookEx(13, proc, GetModuleHandle(moduleName), 0);
	}

	void queueDispatch(Action dispatch) {
		ThreadPool.QueueUserWorkItem(_ => {
			try {
				if (_disposed)
					return;
				dispatch();
			} catch (Exception ex) {
				AppTrace.KeyboardHook.TraceEvent(
					TraceEventType.Error,
					0,
					$"Hotkey dispatch failed: {ex}");
			}
		});
	}

	static bool IsKeyDown(IntPtr wParam) {
		int message = wParam.ToInt32();
		return message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
	}

	static bool IsKeyUp(IntPtr wParam) {
		int message = wParam.ToInt32();
		return message == WM_KEYUP || message == WM_SYSKEYUP;
	}

	/// <summary>Replaces hotkey resolution delegates. Invoked from the hook thread.</summary>
	public void setHotkeyDispatch(
		Func<HotkeyGesture, HotkeyDispatchTargets?> getTargets,
		Action<IReadOnlyList<BindingManager.Slot>> dispatchSlots,
		IReadOnlyCollection<int> boundMainKeyCodes) {
		ArgumentNullException.ThrowIfNull(getTargets);
		ArgumentNullException.ThrowIfNull(dispatchSlots);
		ArgumentNullException.ThrowIfNull(boundMainKeyCodes);
		var table = new bool[VK_TABLE_SIZE];
		foreach (int vk in boundMainKeyCodes) {
			if ((uint)vk < (uint)table.Length)
				table[vk] = true;
		}
		lock (_configuredHotkeysSync) {
			cancelAllActivePressesLocked();
			_getTargets = getTargets;
			_dispatchSlots = dispatchSlots;
			_boundMainKeyVkTable = table;
		}
	}

	public void applyConfig(Config config) {
		ArgumentNullException.ThrowIfNull(config);
		lock (_configuredHotkeysSync) {
			cancelAllActivePressesLocked();
			Config c = Config.Clamped(config);
			_longPressDurationMs = (int)Math.Clamp(c.longPressDurationMs, 1u, int.MaxValue);
			_optimizeNonLongPressKeyDown = c.optimizeNonLongPressKeyDown;
			_suppressKeyForLongPressOnlyGestures = c.suppressKeyForLongPressOnlyGestures;
			_acceptMacroChordKeyOrder = c.acceptMacroChordKeyOrder;
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
		_hasActivePresses = false;
	}

	static bool IsModifierVirtualKey(int vkCode) =>
		HotkeyUtil.isModifierKey(KeyInterop.KeyFromVirtualKey(vkCode));

	static HotkeyModifiers GetActiveModifierSides() {
		HotkeyModifiers modifiers = HotkeyModifiers.NONE;
		if (IsVirtualKeyDown(VK_LCONTROL))
			modifiers |= HotkeyModifiers.LEFT_CONTROL;
		if (IsVirtualKeyDown(VK_RCONTROL))
			modifiers |= HotkeyModifiers.RIGHT_CONTROL;
		if (IsVirtualKeyDown(VK_LSHIFT))
			modifiers |= HotkeyModifiers.LEFT_SHIFT;
		if (IsVirtualKeyDown(VK_RSHIFT))
			modifiers |= HotkeyModifiers.RIGHT_SHIFT;
		if (IsVirtualKeyDown(VK_LMENU))
			modifiers |= HotkeyModifiers.LEFT_ALT;
		if (IsVirtualKeyDown(VK_RMENU))
			modifiers |= HotkeyModifiers.RIGHT_ALT;
		return modifiers;
	}

	static bool IsVirtualKeyDown(int vkCode) => (GetAsyncKeyState(vkCode) & 0x8000) != 0;

	static bool gestureMainKeyHeld(HotkeyGesture g) => IsVirtualKeyDown(g.keyCode);

	internal static bool deadlineGestureStillHeld(
		bool mainKeyHeld,
		HotkeyModifiers requiredModifiers,
		HotkeyModifiers activeModifierSides,
		bool acceptMacroChordKeyOrder) {
		if (!mainKeyHeld)
			return false;
		if (acceptMacroChordKeyOrder)
			return true;
		return HotkeyUtil.activeSidesMatchGesture(requiredModifiers, activeModifierSides);
	}

	bool deadlineGestureStillHeld(HotkeyGesture g) =>
		deadlineGestureStillHeld(
			mainKeyHeld: gestureMainKeyHeld(g),
			requiredModifiers: g.modifiers,
			activeModifierSides: GetActiveModifierSides(),
			acceptMacroChordKeyOrder: _acceptMacroChordKeyOrder);

	internal static IReadOnlyList<HotkeyGesture> resolveKeyUpTargetsForTests(
		int vkCode,
		HotkeyModifiers modifierSidesAtKeyUp,
		IEnumerable<HotkeyGesture> activePressKeys,
		bool acceptMacroChordKeyOrder) {
		ArgumentNullException.ThrowIfNull(activePressKeys);
		IReadOnlyList<HotkeyGesture> activePressKeysList = activePressKeys as IReadOnlyList<HotkeyGesture> ?? activePressKeys.ToArray();
		var strictCandidate = HotkeyUtil.normalize(new HotkeyGesture { keyCode = vkCode, modifiers = modifierSidesAtKeyUp });
		if (!strictCandidate.isNone) {
			foreach (HotkeyGesture k in activePressKeysList) {
				if (k == strictCandidate)
					return new[] { strictCandidate };
			}
		}
		if (!acceptMacroChordKeyOrder)
			return Array.Empty<HotkeyGesture>();

		// Fallback: collect all active presses whose main key matches this vkCode.
		var list = new List<HotkeyGesture>();
		foreach (HotkeyGesture k in activePressKeysList) {
			if (k.keyCode == vkCode)
				list.Add(k);
		}
		list.Sort(static (a, b) => a.modifiers != b.modifiers ? ((int)a.modifiers).CompareTo((int)b.modifiers) : a.keyCode.CompareTo(b.keyCode));
		return list.Count == 0 ? Array.Empty<HotkeyGesture>() : list;
	}

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
			modifiers = GetActiveModifierSides(),
		});
		if (candidate.isNone)
			return false;

		if (_activePresses.TryGetValue(candidate, out ActiveHotkeyPress? existingPress))
			return existingPress.swallowKeyEvents;

		HotkeyDispatchTargets? targetsNullable = _getTargets(candidate);
		if (targetsNullable is not { } targets || !targets.hasAny)
			return false;

		bool hasLong = targets.longPressSlots.Count > 0;
		bool hasShort = targets.shortPressSlots.Count > 0;
		bool effectiveOptimize = _optimizeNonLongPressKeyDown && !hasLong;
		bool longOnly = hasLong && !hasShort;
		bool swallowKeyEvents = !longOnly || _suppressKeyForLongPressOnlyGestures;

		var press = new ActiveHotkeyPress {
			targets = targets,
			swallowKeyEvents = swallowKeyEvents,
			keyDownUtc = DateTime.UtcNow,
		};
		_activePresses[candidate] = press;
		_hasActivePresses = true;

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

		return press.swallowKeyEvents;
	}

	void onLongTimerFired(ActiveHotkeyPress expected) {
		if (_disposed)
			return;
		IReadOnlyList<BindingManager.Slot>? longList;
		bool tookLong;
		lock (_configuredHotkeysSync) {
			if (!_activePresses.Values.Contains(expected))
				return;
			tookLong = tryTakeLongPressIfDueLocked(expected, DateTime.UtcNow, out longList);
		}
		if (tookLong && longList != null && longList.Count > 0)
			queueDispatch(() => _dispatchSlots(longList));
	}

	void onShortDeadlineFired(HotkeyGesture g) {
		if (_disposed)
			return;
		IReadOnlyList<BindingManager.Slot> shortList;
		lock (_configuredHotkeysSync) {
			if (!_activePresses.TryGetValue(g, out ActiveHotkeyPress? press))
				return;
			if (press.shortFired)
				return;
			if (!deadlineGestureStillHeld(g))
				return;
			press.shortFired = true;
			press.shortDeadlineTimer?.Dispose();
			press.shortDeadlineTimer = null;
			shortList = press.targets.shortPressSlots;
		}
		if (shortList.Count > 0)
			queueDispatch(() => _dispatchSlots(shortList));
	}

	bool tryHandleKeyUpLocked(int vkCode) {
		HotkeyModifiers sidesAtKeyUp = GetActiveModifierSides();
		HotkeyGesture strictCandidate = HotkeyUtil.normalize(new HotkeyGesture { keyCode = vkCode, modifiers = sidesAtKeyUp });

		if (_activePresses.TryGetValue(strictCandidate, out ActiveHotkeyPress? strictPress))
			return completeKeyUpLocked(strictCandidate, strictPress);

		if (!_acceptMacroChordKeyOrder)
			return false;

		IReadOnlyList<HotkeyGesture> targets = resolveKeyUpTargetsForTests(vkCode, sidesAtKeyUp, _activePresses.Keys, acceptMacroChordKeyOrder: true);
		if (targets.Count == 0)
			return false;

		bool swallow = false;
		foreach (HotkeyGesture g in targets) {
			if (!_activePresses.TryGetValue(g, out ActiveHotkeyPress? press))
				continue;
			swallow |= completeKeyUpLocked(g, press);
		}
		return swallow;
	}

	bool completeKeyUpLocked(HotkeyGesture key, ActiveHotkeyPress press) {
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

		_activePresses.Remove(key);
		_hasActivePresses = _activePresses.Count > 0;

		if (longListFromKeyUp != null && longListFromKeyUp.Count > 0)
			queueDispatch(() => _dispatchSlots(longListFromKeyUp));
		else if (shortList != null && shortList.Count > 0)
			queueDispatch(() => _dispatchSlots(shortList));

		return press.swallowKeyEvents;
	}

	IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
		if (nCode >= 0 && !_disposed) {
			int vkCode = Marshal.ReadInt32(lParam);
			if (mayConcernConfiguredHotkeys(vkCode) && tryHandleConfiguredHotkey(wParam, vkCode))
				return new IntPtr(1);
		}
		return CallNextHookEx(_hookId, nCode, wParam, lParam);
	}

	/// <summary>Lock-free fast path: with no press in flight, only VK codes bound as hotkey main keys need the locked state machine.</summary>
	bool mayConcernConfiguredHotkeys(int vkCode) {
		if (_hasActivePresses)
			return true;
		bool[] bound = _boundMainKeyVkTable;
		return (uint)vkCode < (uint)bound.Length && bound[vkCode];
	}

	public void Dispose() {
		if (_disposed)
			return;
		_disposed = true;

		lock (_configuredHotkeysSync)
			cancelAllActivePressesLocked();

		// The hook is uninstalled on the pump thread itself after WM_QUIT drains, so no callback can race the unhook.
		if (_hookInstalled.Wait(HOOK_THREAD_WAIT_MS) && _hookId != IntPtr.Zero) {
			_ = PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
			if (!_hookThread.Join(HOOK_THREAD_WAIT_MS))
				AppTrace.KeyboardHook.TraceEvent(TraceEventType.Warning, 0, "Keyboard hook thread did not exit in time.");
		}
		_hookInstalled.Dispose();
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

	[StructLayout(LayoutKind.Sequential)]
	struct MSG {
		public IntPtr hwnd;
		public uint message;
		public IntPtr wParam;
		public IntPtr lParam;
		public uint time;
		public int ptX;
		public int ptY;
	}

	[LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
	private static partial int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool TranslateMessage(ref MSG lpMsg);

	[LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
	private static partial IntPtr DispatchMessage(ref MSG lpMsg);

	[LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

	[LibraryImport("kernel32.dll")]
	private static partial uint GetCurrentThreadId();
}
}
