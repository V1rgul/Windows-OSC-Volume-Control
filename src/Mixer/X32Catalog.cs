using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WindowsOscVolumeControl.Mixer;

public enum X32CatalogKind {
	Linf,
	Logf,
	Level,
	Toggle,
}

public sealed class X32CatalogEntry {
	public X32CatalogKind kind { get; init; }
	public float minimum { get; init; }
	public float maximum { get; init; }
	public string? unit { get; init; }
}

/// <summary>Loads <c>Assets/x32-catalog.config</c> next to the executable; bracket patterns become regex matchers.</summary>
public static class X32Catalog {
	static readonly Lock _initLock = new();
	static IReadOnlyList<(Regex pattern, string addressPattern, X32CatalogEntry entry)> _compiled = [];
	static IReadOnlyList<string> _addressPatterns = [];

	public static void ensureLoaded() {
		lock (_initLock) {
			if (_compiled.Count > 0)
				return;
			string path = Path.Combine(AppContext.BaseDirectory, "Assets", "x32-catalog.config");
			if (!File.Exists(path)) {
				AppTrace.Application.TraceEvent(System.Diagnostics.TraceEventType.Warning, 0, "X32 catalog missing at " + path);
				return;
			}
			try {
				string text = File.ReadAllText(path);
				_compiled = parseAndCompile(text, out IReadOnlyList<string> loadedAddressPatterns);
				_addressPatterns = loadedAddressPatterns;
			} catch (Exception ex) {
				AppTrace.Application.TraceEvent(System.Diagnostics.TraceEventType.Error, 0, "X32 catalog load failed: " + ex.Message);
			}
		}
	}

	public static IReadOnlyList<string> addressPatterns {
		get {
			ensureLoaded();
			return _addressPatterns;
		}
	}

	public static bool tryResolve(string address, out X32CatalogEntry entry) {
		ensureLoaded();
		foreach ((Regex re, _, X32CatalogEntry e) in _compiled) {
			if (re.IsMatch(address)) {
				entry = e;
				return true;
			}
		}
		entry = null!;
		return false;
	}

	static IReadOnlyList<(Regex, string, X32CatalogEntry)> parseAndCompile(string text, out IReadOnlyList<string> compiledAddressPatterns) {
		// Preferred format: one record per line, semicolon-delimited key=value fields:
		// address=/ch/[01..32]/mix/fader;   kind=level; min=-90; max=10
		// Spaces are allowed only after ';' for visual alignment; delimiter terminates values.
		List<Row> semicolonRows = parseSemicolonRows(text);

		// Backward compatible: legacy entry.N.field=value format (used by earlier versions).
		// If any semicolon rows exist, prefer them and ignore legacy rows to avoid accidental mixing.
		var legacyRows = new Dictionary<int, Row>();
		if (semicolonRows.Count == 0) {
			Dictionary<string, string> map = parseKeyValueLines(text);
			foreach ((string key, string value) in map) {
				if (!tryParseEntryKey(key, out int idx, out string field))
					continue;
				if (!legacyRows.TryGetValue(idx, out Row? row))
					legacyRows[idx] = row = new Row();
				switch (field.ToLowerInvariant()) {
					case "address":
						row.addressPattern = value.Trim();
						break;
					case "kind":
						row.kindText = value.Trim();
						break;
					case "min":
						if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float mn) && float.IsFinite(mn))
							row.minText = value.Trim();
						break;
					case "max":
						if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float mx) && float.IsFinite(mx))
							row.maxText = value.Trim();
						break;
					case "unit":
						row.unit = value.Trim();
						break;
				}
			}
		}
		var list = new List<(Regex, string, X32CatalogEntry)>();
		var patterns = new List<string>();
		IEnumerable<Row> rows = semicolonRows.Count > 0
			? semicolonRows
			: legacyRows.Keys.OrderBy(x => x).Select(i => legacyRows[i]);
		foreach (Row r in rows) {
			if (string.IsNullOrEmpty(r.addressPattern))
				continue;
			if (!tryParseKind(r.kindText, out X32CatalogKind k))
				continue;
			float minV = 0f;
			float maxV = 1f;
			if (k != X32CatalogKind.Toggle) {
				if (string.IsNullOrEmpty(r.minText) || string.IsNullOrEmpty(r.maxText))
					continue;
				if (!float.TryParse(r.minText, NumberStyles.Float, CultureInfo.InvariantCulture, out minV) || !float.IsFinite(minV))
					continue;
				if (!float.TryParse(r.maxText, NumberStyles.Float, CultureInfo.InvariantCulture, out maxV) || !float.IsFinite(maxV))
					continue;
			}
			string? u = string.IsNullOrWhiteSpace(r.unit) ? null : r.unit.Trim();
			try {
				Regex re = compileAddressPattern(r.addressPattern);
				list.Add((re, r.addressPattern, new X32CatalogEntry {
					kind = k,
					minimum = minV,
					maximum = maxV,
					unit = u,
				}));
				patterns.Add(r.addressPattern);
			} catch {
				// skip bad pattern
			}
		}
		compiledAddressPatterns = patterns;
		return list;
	}

	static List<Row> parseSemicolonRows(string text) {
		var list = new List<Row>();
		foreach (string raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) {
			if (raw.Length == 0)
				continue;
			string line = raw.TrimStart();
			if (line.Length == 0 || line[0] == '#')
				continue;

			// If it looks like the legacy format, bail out early.
			if (line.StartsWith("entry.", StringComparison.OrdinalIgnoreCase))
				continue;

			var row = new Row();
			bool anyField = false;
			foreach (string segRaw in line.Split(';')) {
				string seg = segRaw.TrimStart();
				if (seg.Length == 0)
					continue;
				int eq = seg.IndexOf('=');
				if (eq <= 0)
					continue;
				string k = seg[..eq].Trim();
				string v = seg[(eq + 1)..];
				anyField = true;
				switch (k.ToLowerInvariant()) {
					case "address":
						row.addressPattern = v.Trim();
						break;
					case "kind":
						row.kindText = v.Trim();
						break;
					case "min":
						row.minText = v.Trim();
						break;
					case "max":
						row.maxText = v.Trim();
						break;
					case "unit":
						row.unit = v.Trim();
						break;
				}
			}
			if (anyField)
				list.Add(row);
		}
		return list;
	}

	sealed class Row {
		public string addressPattern = "";
		public string kindText = "";
		public string minText = "";
		public string maxText = "";
		public string? unit;
	}

	static bool tryParseEntryKey(string key, out int index, out string field) {
		index = -1;
		field = "";
		if (!key.StartsWith("entry.", StringComparison.OrdinalIgnoreCase))
			return false;
		string rest = key["entry.".Length..];
		int dot = rest.IndexOf('.');
		if (dot <= 0)
			return false;
		if (!int.TryParse(rest[..dot], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx) || idx < 0)
			return false;
		index = idx;
		field = rest[(dot + 1)..];
		return true;
	}

	static bool tryParseKind(string text, out X32CatalogKind kind) {
		kind = default;
		switch (text.Trim().ToLowerInvariant()) {
			case "linf":
				kind = X32CatalogKind.Linf;
				return true;
			case "logf":
				kind = X32CatalogKind.Logf;
				return true;
			case "level":
				kind = X32CatalogKind.Level;
				return true;
			case "toggle":
				kind = X32CatalogKind.Toggle;
				return true;
			default:
				return false;
		}
	}

	static Dictionary<string, string> parseKeyValueLines(string text) {
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) {
			string line = raw.Trim();
			if (line.Length == 0 || line[0] == '#')
				continue;
			int eq = line.IndexOf('=');
			if (eq <= 0)
				continue;
			string k = line[..eq].Trim();
			string v = line[(eq + 1)..].Trim();
			if (k.Length > 0)
				map[k] = v;
		}
		return map;
	}

	/// <summary>Turns <c>/ch/[01..32]/trim</c> into a regex; <c>[a..b]</c> expands to numeric runs.</summary>
	internal static Regex compileAddressPattern(string pattern) {
		var sb = new StringBuilder();
		sb.Append('^');
		for (int i = 0; i < pattern.Length; i++) {
			char c = pattern[i];
			if (c == '[') {
				int close = pattern.IndexOf(']', i + 1);
				if (close < 0) {
					sb.Append(Regex.Escape(c.ToString()));
					continue;
				}
				string inner = pattern.AsSpan(i + 1, close - i - 1).ToString();
				i = close;
				sb.Append(tryParseBracketRange(inner, out string regexFragment)
					? regexFragment
					: Regex.Escape("[" + inner + "]"));
			} else if (c == '.' || c == '^' || c == '$' || c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}' || c == '|' || c == '\\' || c == '*' || c == '+' || c == '?')
				sb.Append('\\').Append(c);
			else
				sb.Append(c);
		}
		sb.Append('$');
		return new Regex(sb.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline);
	}

	static bool tryParseBracketRange(string inner, out string regexFragment) {
		regexFragment = "";
		int dots = inner.IndexOf("..", StringComparison.Ordinal);
		if (dots <= 0)
			return false;
		string a = inner[..dots].Trim();
		string b = inner[(dots + 2)..].Trim();
		if (!int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out int start)
		    || !int.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out int end))
			return false;
		if (end < start)
			(start, end) = (end, start);
		var parts = new List<string>();
		int width = Math.Max(a.TrimStart('+').Length, b.TrimStart('+').Length);
		bool zeroPad = a.Length > 1 && a[0] == '0' || b.Length > 1 && b[0] == '0';
		for (int v = start; v <= end; v++) {
			string s = v.ToString(CultureInfo.InvariantCulture);
			if (zeroPad && width > 0)
				s = s.PadLeft(width, '0');
			parts.Add(Regex.Escape(s));
		}
		regexFragment = "(?:" + string.Join("|", parts) + ")";
		return true;
	}
}
