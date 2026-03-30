using Microsoft.Win32;

namespace WindowsOscVolumeControl;

static class WindowsAutostart {
	const string RUN_KEY_PATH = @"Software\Microsoft\Windows\CurrentVersion\Run";
	const string VALUE_NAME = "Windows-OSC-Volume-Control";

	public static bool IsRegistered() {
		using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RUN_KEY_PATH, false);
		if (key == null)
			return false;
		return key.GetValue(VALUE_NAME) is string s && s.Length > 0;
	}

	public static bool TryRegister(out string? error) {
		error = null;
		string? exe = Environment.ProcessPath;
		if (string.IsNullOrEmpty(exe)) {
			error = "Could not resolve executable path.";
			return false;
		}
		try {
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RUN_KEY_PATH, true);
			if (key == null) {
				error = "Could not open the Run registry key.";
				return false;
			}
			string value = exe.Contains(' ') ? "\"" + exe + "\"" : exe;
			key.SetValue(VALUE_NAME, value, RegistryValueKind.String);
			return true;
		} catch (Exception ex) {
			error = ex.Message;
			return false;
		}
	}

	public static bool TryDeregister(out string? error) {
		error = null;
		try {
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RUN_KEY_PATH, true);
			if (key == null)
				return true;
			key.DeleteValue(VALUE_NAME, false);
			return true;
		} catch (Exception ex) {
			error = ex.Message;
			return false;
		}
	}
}
