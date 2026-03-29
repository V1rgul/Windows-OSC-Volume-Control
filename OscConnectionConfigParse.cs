using System.Globalization;
using System.Net;

namespace X32VolumeHijacker;

/// <summary>Parses and validates IP, UDP port, and OSC fader address strings (UI and config file).</summary>
static class OscConnectionConfigParse {
	public static bool IsIpFieldSyntaxOk(string? ipRaw) =>
		!string.IsNullOrWhiteSpace(ipRaw) && IPAddress.TryParse(ipRaw.Trim(), out _);

	public static bool IsPortFieldSyntaxOk(string? portRaw) =>
		int.TryParse(portRaw?.Trim() ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out int p)
		&& p is >= 1 and <= 65535;

	/// <summary>
	/// Parses the three connection fields. On failure, sets <paramref name="ipError"/>, <paramref name="portError"/>, or <paramref name="oscError"/>.
	/// </summary>
	public static bool TryParse(
		string? ipRaw, string? portRaw, string? faderRaw,
		out IPAddress ip, out int port, out string fader,
		out string? ipError, out string? portError, out string? oscError) {
		ip = default!;
		port = 0;
		fader = "";
		ipError = null;
		portError = null;
		oscError = null;

		if (string.IsNullOrWhiteSpace(ipRaw) || !IPAddress.TryParse(ipRaw.Trim(), out IPAddress? parsedIp)) {
			ipError = "Invalid IP address.";
			return false;
		}
		ip = parsedIp;

		if (string.IsNullOrWhiteSpace(portRaw)
		    || !int.TryParse(portRaw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port)
		    || port is < 1 or > 65535) {
			portError = "Port must be between 1 and 65535.";
			return false;
		}

		if (faderRaw == null) {
			oscError = "Fader address is required.";
			return false;
		}
		fader = faderRaw.Trim();
		if (fader.Length == 0) {
			oscError = "Fader address is required.";
			return false;
		}
		return true;
	}
}
