using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

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

	delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

	readonly IntPtr _hookId;
	readonly LowLevelKeyboardProc _proc;
	readonly object _configuredHotkeysSync = new();
	volatile Func<Keys, Action?> _keyCallback = static _ => null;
	HashSet<int> _pressedConfiguredHotkeys = [];
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

	/// <summary>Called when hotkey bindings change; <paramref name="keyCallback"/> is invoked from the hook thread (keys are already normalized).</summary>
	public void setKeyCallback(Func<Keys, Action?> keyCallback) {
		ArgumentNullException.ThrowIfNull(keyCallback);
		_keyCallback = keyCallback;
		lock (_configuredHotkeysSync)
			_pressedConfiguredHotkeys.Clear();
	}

	public void SetConfiguredHotkeysEnabled(bool enabled) {
		lock (_configuredHotkeysSync) {
			_configuredHotkeysEnabled = enabled;
			if (!enabled)
				_pressedConfiguredHotkeys.Clear();
		}
	}

	static bool IsModifierVirtualKey(int vkCode) => KeysUtil.isModifierKey((Keys)vkCode);

	static Keys GetActiveModifiers() {
		Keys modifiers = Keys.None;
		if (IsVirtualKeyDown(VK_CONTROL))
			modifiers |= Keys.Control;
		if (IsVirtualKeyDown(VK_SHIFT))
			modifiers |= Keys.Shift;
		if (IsVirtualKeyDown(VK_MENU))
			modifiers |= Keys.Alt;
		return modifiers;
	}

	static bool IsVirtualKeyDown(int vkCode) => (GetAsyncKeyState(vkCode) & 0x8000) != 0;

	bool tryHandleConfiguredHotkey(IntPtr wParam, int vkCode) {
		if (IsModifierVirtualKey(vkCode))
			return false;
		Action? dispatch = null;
		lock (_configuredHotkeysSync) {
			if (!_configuredHotkeysEnabled || _hookId == IntPtr.Zero)
				return false;
			if (IsKeyUp(wParam))
				return _pressedConfiguredHotkeys.Remove(vkCode);
			if (!IsKeyDown(wParam))
				return false;
			Keys candidate = KeysUtil.normalize((Keys)vkCode | GetActiveModifiers());
			dispatch = _keyCallback(candidate);
			if (dispatch == null)
				return false;
			_pressedConfiguredHotkeys.Add(vkCode);
		}
		queueDispatch(dispatch);
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