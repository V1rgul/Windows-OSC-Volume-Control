using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace WindowsOscVolumeControl.Misc;

static class WindowsAutostart {
	public readonly record struct UiFeedbackDetail(
		UiTextFeedback feedback,
		string? pathOrNull);

	/// <summary>Outcome of <see cref="tryDeregisterAllCopiesFromRun"/>.</summary>
	public readonly record struct DeregisterAllResult(
		bool registryFailure,
		string? failureOrNoneMessage,
		int removedCount);

	public const string VALUE_NAME = "Windows-OSC-Volume-Control";
	const string RUN_KEY_PATH = @"Software\Microsoft\Windows\CurrentVersion\Run";
	const string FALLBACK_EXE_FILE_NAME = "Windows-OSC-Volume-Control.exe";

	static string truncateForAutostartMessage(string text, int maxLen) {
		if (string.IsNullOrEmpty(text) || text.Length <= maxLen)
			return text ?? "";
		int half = (maxLen - 1) / 2;
		return text.Substring(0, half) + "\u2026" + text.Substring(text.Length - half);
	}

	public static UiFeedbackDetail getCurrentUiFeedback() {
		List<(string valueName, string? parsedExePath, string rawCommand)> entries = listRunEntriesForThisAppExe();
		string? current = Environment.ProcessPath;

		if (entries.Count > 1) {
			var sb = new StringBuilder();
			sb.Append("Multiple autostart entries (");
			sb.Append(entries.Count);
			sb.Append(") point to this application. Use Deregister All to remove them.");
			int n = Math.Min(3, entries.Count);
			for (int i = 0; i < n; i++) {
				(string valueName, string? parsedExePath, string rawCommand) = entries[i];
				string path = parsedExePath ?? truncateForAutostartMessage(rawCommand, 80);
				sb.AppendLine();
				sb.Append("\u2022 ");
				sb.Append(valueName);
				sb.Append(": ");
				sb.Append(truncateForAutostartMessage(path, 100));
			}
			if (entries.Count > n) {
				sb.AppendLine();
				sb.Append("\u2026");
			}
			return new UiFeedbackDetail(new UiTextFeedback(sb.ToString(), UiTextFeedbackKind.ERROR), null);
		}

		if (entries.Count == 1) {
			(_, string? parsedExePath, string rawCommand) = entries[0];
			if (pathsEqualForAutostart(parsedExePath, current))
				return new UiFeedbackDetail(new UiTextFeedback("Autostart is registered for this executable.", UiTextFeedbackKind.DEFAULT), null);
			if (!string.IsNullOrEmpty(parsedExePath)) {
				return new UiFeedbackDetail(
					new UiTextFeedback("Autostart is registered but points to a different location:", UiTextFeedbackKind.WARNING),
					parsedExePath);
			}
			string line = truncateForAutostartMessage(rawCommand, 200);
			return new UiFeedbackDetail(
				new UiTextFeedback(
					"Autostart is registered but points to a different location:" + Environment.NewLine + line,
					UiTextFeedbackKind.WARNING),
				null);
		}

		return new UiFeedbackDetail(new UiTextFeedback("Autostart is currently not registered.", UiTextFeedbackKind.DEFAULT), null);
	}

	public static UiTextFeedback uiFeedbackForDeregisterAll(DeregisterAllResult r) {
		if (r.registryFailure)
			return new UiTextFeedback(r.failureOrNoneMessage ?? "Could not remove autostart entries.", UiTextFeedbackKind.ERROR);
		if (r.removedCount == 0)
			return new UiTextFeedback(r.failureOrNoneMessage!, UiTextFeedbackKind.SUCCESS);
		UiTextFeedback tail = getCurrentUiFeedback().feedback;
		string word = r.removedCount == 1 ? "entry" : "entries";
		string prefix = "Removed " + r.removedCount + " autostart " + word + ". ";
		return new UiTextFeedback(prefix + tail.text, UiTextFeedbackKind.SUCCESS);
	}

	public static bool IsRegistered() {
		string? current = Environment.ProcessPath;
		if (string.IsNullOrEmpty(current))
			return false;
		foreach ((_, string? parsedExePath, _) in listRunEntriesForThisAppExe()) {
			if (pathsEqualForAutostart(parsedExePath, current))
				return true;
		}
		return false;
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

	static string stablePathHashSuffixForRunValueName(string exe) {
		string norm;
		try {
			norm = Path.GetFullPath(exe);
		} catch {
			norm = exe;
		}
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(norm));
		return Convert.ToHexString(hash.AsSpan(0, 8));
	}

	/// <summary>HKCU Run value name: <see cref="VALUE_NAME"/> plus SHA-256 suffix from the exe path; disambiguates with -2, -3, … if needed.</summary>
	static string runValueNameForExePath(RegistryKey key, string exe) {
		string stem = VALUE_NAME + "-" + stablePathHashSuffixForRunValueName(exe);
		for (int i = 0; ; i++) {
			string candidate = i == 0 ? stem : stem + "-" + i;
			if (key.GetValue(candidate) is not string raw || string.IsNullOrWhiteSpace(raw))
				return candidate;
			if (tryParseRunCommandFirstExecutable(raw, out string? parsed)
			    && pathsEqualForAutostart(parsed, exe))
				return candidate;
		}
	}

	public static UiTextFeedback tryRegister() {
		string? exe = Environment.ProcessPath;
		if (string.IsNullOrEmpty(exe))
			return new UiTextFeedback("Could not resolve executable path.", UiTextFeedbackKind.ERROR);
		foreach ((_, string? parsedExePath, _) in listRunEntriesForThisAppExe()) {
			if (pathsEqualForAutostart(parsedExePath, exe))
				return new UiTextFeedback("Autostart already registered for this executable.", UiTextFeedbackKind.SUCCESS);
		}
		try {
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RUN_KEY_PATH, true);
			if (key == null)
				return new UiTextFeedback("Could not open the Run registry key.", UiTextFeedbackKind.ERROR);
			string value = exe.Contains(' ') ? "\"" + exe + "\"" : exe;
			string name = runValueNameForExePath(key, exe);
			key.SetValue(name, value, RegistryValueKind.String);
			return new UiTextFeedback("Autostart registered.", UiTextFeedbackKind.SUCCESS);
		} catch (Exception ex) {
			return new UiTextFeedback(ex.Message ?? "Could not register autostart.", UiTextFeedbackKind.ERROR);
		}
	}

	/// <summary>Removes only HKCU Run values whose command resolves to the current process executable path.</summary>
	public static UiTextFeedback tryDeregister() {
		string? current = Environment.ProcessPath;
		if (string.IsNullOrEmpty(current))
			return new UiTextFeedback("Could not resolve executable path.", UiTextFeedbackKind.ERROR);
		int removedCount = 0;
		try {
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RUN_KEY_PATH, true);
			if (key == null) {
				// No key: nothing to remove for this path.
			} else {
				foreach ((string valueName, string? parsedExePath, _) in listRunEntriesForThisAppExe()) {
					if (!pathsEqualForAutostart(parsedExePath, current))
						continue;
					key.DeleteValue(valueName, false);
					removedCount++;
				}
			}
		} catch (Exception ex) {
			return new UiTextFeedback(ex.Message ?? "Could not deregister autostart.", UiTextFeedbackKind.ERROR);
		}
		if (removedCount > 0)
			return new UiTextFeedback("Autostart removed for this executable.", UiTextFeedbackKind.SUCCESS);
		return new UiTextFeedback(
			"No autostart entry for this executable was removed. If another copy is registered, use Deregister All.",
			UiTextFeedbackKind.ERROR);
	}

	public static DeregisterAllResult tryDeregisterAllCopiesFromRun() {
		int removedCount = 0;
		try {
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RUN_KEY_PATH, true);
			if (key == null)
				return new DeregisterAllResult(false, "No matching autostart entries were removed.", 0);
			List<(string valueName, string? parsedExePath, string rawCommand)> entries = listRunEntriesForThisAppExe();
			foreach ((string valueName, _, _) in entries) {
				key.DeleteValue(valueName, false);
				removedCount++;
			}
			if (removedCount == 0)
				return new DeregisterAllResult(false, "No matching autostart entries were removed.", 0);
			return new DeregisterAllResult(false, null, removedCount);
		} catch (Exception ex) {
			return new DeregisterAllResult(true, ex.Message ?? "Could not remove autostart entries.", removedCount);
		}
	}
}


