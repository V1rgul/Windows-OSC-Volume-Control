using System.Globalization;
using System.Net;

namespace WindowsOscVolumeControl.Config;

/// <summary>Parses and validates IP, UDP port, and OSC fader address strings (UI and config file).</summary>
static class OscConnectionConfigParse {
	public static bool isIpFieldSyntaxOk(string? ipRaw) =>
		!string.IsNullOrWhiteSpace(ipRaw) && IPAddress.TryParse(ipRaw.Trim(), out _);

	public static bool isPortFieldSyntaxOk(string? portRaw) =>
		int.TryParse(portRaw?.Trim() ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out int p)
		&& isPortInRange(p);

	/// <summary>IP and UDP port only (OSC addresses come from fader/toggle grids).</summary>
	public static bool tryParseIpPort(
		string? ipRaw, string? portRaw,
		out IPAddress ip, out int port, out string? ipError, out string? portError
	) {
		ip = default!;
		port = 0;
		ipError = null;
		portError = null;
		if (string.IsNullOrWhiteSpace(ipRaw) || !IPAddress.TryParse(ipRaw.Trim(), out IPAddress? parsedIp)) {
			ipError = "Invalid IP address.";
			return false;
		}
		ip = parsedIp;
		if (string.IsNullOrWhiteSpace(portRaw)
		    || !int.TryParse(portRaw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port)
		    || !isPortInRange(port)) {
			portError = "Port must be between 1 and 65535.";
			return false;
		}
		return true;
	}

	public static bool isPortInRange(int port) => port is >= 1 and <= 65535;
}
