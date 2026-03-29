using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace X32VolumeHijacker {
	public partial class KeyboardHook : IDisposable {
		const int WM_KEYDOWN = 0x0100;
		const int WM_SYSKEYDOWN = 0x0104;
		const int WM_KEYUP = 0x0101;
		const int WM_SYSKEYUP = 0x0105;
		const int VK_CONTROL = 0x11;
		const int VK_SHIFT = 0x10;
		const int VK_MENU = 0x12;
		const int VK_VOLUME_UP = 0xAF;
		const int VK_VOLUME_DOWN = 0xAE;
		const int VK_VOLUME_MUTE = 0xAD;

		delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

		public Action<Keys> OnConfiguredHotkeyPressed = _ => { };

		readonly IntPtr _hookId;
		readonly LowLevelKeyboardProc _proc;
		readonly object _configuredHotkeysSync = new();
		HashSet<Keys> _configuredHotkeys = [];
		Dictionary<int, Keys> _pressedConfiguredHotkeys = [];
		bool _configuredHotkeysEnabled = true;
		bool _disposed;

		public KeyboardHook() {
			_proc = HookCallback;
			_hookId = SetHook(_proc);
		}

		static IntPtr SetHook(LowLevelKeyboardProc proc) {
			using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
			string moduleName = curProcess.MainModule?.ModuleName
				?? throw new InvalidOperationException("Could not resolve the current process module name.");
			return SetWindowsHookEx(13, proc, GetModuleHandle(moduleName), 0);
		}

		void QueueConfiguredHotkey(Keys hotkey) {
			var handler = OnConfiguredHotkeyPressed;
			ThreadPool.QueueUserWorkItem(_ => handler(hotkey));
		}

		static bool IsKeyDown(IntPtr wParam) => wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
		static bool IsKeyUp(IntPtr wParam) => wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;

		public void SetConfiguredHotkeys(IEnumerable<Keys> hotkeys) {
			ArgumentNullException.ThrowIfNull(hotkeys);
			lock (_configuredHotkeysSync) {
				_configuredHotkeys = hotkeys
					.Select(OscHotkey.Normalize)
					.Where(hotkey => hotkey != Keys.None)
					.ToHashSet();
				_pressedConfiguredHotkeys.Clear();
			}
		}

		public void SetConfiguredHotkeysEnabled(bool enabled) {
			lock (_configuredHotkeysSync) {
				_configuredHotkeysEnabled = enabled;
				if (!enabled)
					_pressedConfiguredHotkeys.Clear();
			}
		}

		static bool IsModifierVirtualKey(int vkCode) => OscHotkey.IsModifierKey((Keys)vkCode);

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

		bool TryHandleConfiguredHotkey(IntPtr wParam, int vkCode, out Keys firedHotkey) {
			firedHotkey = Keys.None;
			if (IsModifierVirtualKey(vkCode))
				return false;
			lock (_configuredHotkeysSync) {
				if (!_configuredHotkeysEnabled)
					return false;
				if (IsKeyUp(wParam))
					return _pressedConfiguredHotkeys.Remove(vkCode);
				if (!IsKeyDown(wParam))
					return false;
				Keys candidate = OscHotkey.Normalize((Keys)vkCode | GetActiveModifiers());
				if (!_configuredHotkeys.Contains(candidate))
					return false;
				if (_pressedConfiguredHotkeys.ContainsKey(vkCode))
					return true;
				_pressedConfiguredHotkeys[vkCode] = candidate;
				firedHotkey = candidate;
				return true;
			}
		}

		IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
			// Key-up (and key-up repeat) would run the handler twice, e.g. mute toggles then toggles back.
			if (nCode >= 0) {
				int vkCode = Marshal.ReadInt32(lParam);
				if (TryHandleConfiguredHotkey(wParam, vkCode, out Keys firedHotkey)) {
					if (firedHotkey != Keys.None)
						QueueConfiguredHotkey(firedHotkey);
					return (IntPtr)1;
				}
				if (vkCode is VK_VOLUME_UP or VK_VOLUME_DOWN or VK_VOLUME_MUTE)
					return CallNextHookEx(_hookId, nCode, wParam, lParam);
			}
			return CallNextHookEx(_hookId, nCode, wParam, lParam);
		}

		public void Dispose() {
			if (_disposed) {
				GC.SuppressFinalize(this);
				return;
			}

			UnhookWindowsHookEx(_hookId);
			_disposed = true;
			GC.SuppressFinalize(this);
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
}