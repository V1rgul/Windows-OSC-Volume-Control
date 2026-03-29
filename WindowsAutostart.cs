using Microsoft.Win32;

namespace X32VolumeHijacker;

static class WindowsAutostart {
	const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
	const string ValueName = "X32VolumeHijacker";

	public static bool IsRegistered() {
		using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
		if (key == null)
			return false;
		return key.GetValue(ValueName) is string s && s.Length > 0;
	}

	public static bool TryRegister(out string? error) {
		error = null;
		string? exe = Environment.ProcessPath;
		if (string.IsNullOrEmpty(exe)) {
			error = "Could not resolve executable path.";
			return false;
		}
		try {
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
			if (key == null) {
				error = "Could not open the Run registry key.";
				return false;
			}
			string value = exe.Contains(' ') ? "\"" + exe + "\"" : exe;
			key.SetValue(ValueName, value, RegistryValueKind.String);
			return true;
		} catch (Exception ex) {
			error = ex.Message;
			return false;
		}
	}

	public static bool TryDeregister(out string? error) {
		error = null;
		try {
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
			if (key == null)
				return true;
			key.DeleteValue(ValueName, false);
			return true;
		} catch (Exception ex) {
			error = ex.Message;
			return false;
		}
	}
}
