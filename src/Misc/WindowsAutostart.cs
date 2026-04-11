using System.IO;
using Microsoft.Win32;

namespace WindowsOscVolumeControl;

static class WindowsAutostart {
	public const string VALUE_NAME = "Windows-OSC-Volume-Control";
	const string RUN_KEY_PATH = @"Software\Microsoft\Windows\CurrentVersion\Run";
	const string FALLBACK_EXE_FILE_NAME = "Windows-OSC-Volume-Control.exe";

	public static bool IsRegistered() {
		using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RUN_KEY_PATH, false);
		if (key == null)
			return false;
		return key.GetValue(VALUE_NAME) is string s && s.Length > 0;
	}

	/// <summary>First executable path from a Run value (quoted or unquoted); ignores trailing arguments.</summary>
	internal static bool tryParseRunCommandFirstExecutable(string raw, out string? exePath) {
		exePath = null;
		if (string.IsNullOrWhiteSpace(raw))
			return false;
		ReadOnlySpan<char> t = raw.AsSpan().Trim();
		if (t.Length == 0)
			return false;
		if (t[0] == '"') {
			int end = t[1..].IndexOf('"');
			if (end < 0)
				return false;
			exePath = t.Slice(1, end).ToString();
			return exePath.Length > 0;
		}
		int space = t.IndexOf(' ');
		ReadOnlySpan<char> token = space < 0 ? t : t.Slice(0, space);
		exePath = token.ToString();
		return exePath.Length > 0;
	}

	internal static bool pathsEqualForAutostart(string? a, string? b) {
		if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
			return false;
		try {
			string fa = Path.GetFullPath(a);
			string fb = Path.GetFullPath(b);
			return string.Equals(fa, fb, StringComparison.OrdinalIgnoreCase);
		} catch {
			return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
		}
	}

	static string autostartExeFileNameForMatching() {
		string? p = Environment.ProcessPath;
		if (!string.IsNullOrEmpty(p)) {
			try {
				return Path.GetFileName(p);
			} catch {
				// fall through
			}
		}
		return FALLBACK_EXE_FILE_NAME;
	}

	/// <summary>HKCU Run values whose command resolves to this app's executable file name.</summary>
	public static List<(string valueName, string? parsedExePath, string rawCommand)> listRunEntriesForThisAppExe() {
		var list = new List<(string, string?, string)>();
		string targetName = autostartExeFileNameForMatching();
		using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RUN_KEY_PATH, false);
		if (key == null)
			return list;
		foreach (string name in key.GetValueNames()) {
			if (key.GetValue(name) is not string raw || raw.Length == 0)
				continue;
			if (!tryParseRunCommandFirstExecutable(raw, out string? parsed))
				continue;
			string? fileName;
			try {
				fileName = Path.GetFileName(parsed);
			} catch {
				continue;
			}
			if (string.IsNullOrEmpty(fileName))
				continue;
			if (!string.Equals(fileName, targetName, StringComparison.OrdinalIgnoreCase))
				continue;
			list.Add((name, parsed, raw));
		}
		return list;
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

	public static bool tryDeregisterAllCopiesFromRun(out int removedCount, out string? error) {
		removedCount = 0;
		error = null;
		try {
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RUN_KEY_PATH, true);
			if (key == null)
				return true;
			List<(string valueName, string? parsedExePath, string rawCommand)> entries = listRunEntriesForThisAppExe();
			foreach ((string valueName, _, _) in entries) {
				key.DeleteValue(valueName, false);
				removedCount++;
			}
			return true;
		} catch (Exception ex) {
			error = ex.Message;
			return false;
		}
	}
}
