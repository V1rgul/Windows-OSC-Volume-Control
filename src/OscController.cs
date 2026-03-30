using SharpOSC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WindowsOscVolumeControl {
public partial class OscController {

	public class Config {
		public const uint MIN_QUERY_TIMEOUT_MS = 1;
		public const uint MAX_QUERY_TIMEOUT_MS = 10_000;

		public IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 10023);
		public uint timeoutMs = 200;

		public Config() {}
		public Config(Config other) {
			ArgumentNullException.ThrowIfNull(other);
			endPoint = new IPEndPoint(other.endPoint.Address, other.endPoint.Port);
			timeoutMs = other.timeoutMs;
		}
	}

	UdpClient udpClient = null!;
	IPEndPoint? _mixerEndPoint;

	/// <summary>Owns a clone of <paramref name="fromApp"/>; mutating the app snapshot does not affect the socket.</summary>
	public OscController(Config fromApp) {
		Connection = fromApp;
	}

	Config _config = null!;
	/// <summary>Mixer endpoint and query timeout only; fader paths come from <see cref="OscFaderBinding"/> rows.</summary>
	public Config Connection {
		get { return new Config(_config); }
		set {
			udpClient?.Dispose();
			int oscPort = value.endPoint.Port;
			// X32 replies to the UDP source port used by the client; binding to the configured OSC port
			// matches the desk default and keeps custom-port setups consistent with saved config.
			// See e.g. https://github.com/mike-steciuk/X32Client/blob/master/X32Client/X32/X32Client.cs
			udpClient = new UdpClient(AddressFamily.InterNetwork);
			try {
				udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, oscPort));
			} catch (SocketException ex) {
				Trace.WriteLine("Bind local UDP " + oscPort + " failed (another app may use it): " + ex.Message);
				udpClient.Dispose();
				udpClient = new UdpClient(AddressFamily.InterNetwork);
			}
			udpClient.Client.IOControl((IOControlCode)(-1744830452), [0], null);
			_mixerEndPoint = new IPEndPoint(value.endPoint.Address, value.endPoint.Port);
			Trace.WriteLine("OSC socket local=" + udpClient.Client.LocalEndPoint + " remote=" + _mixerEndPoint);
			_config = new Config(value);
		}
	}

	/// <summary>Applies mixer connection settings (same as assigning <see cref="Connection"/>).</summary>
	public void ApplyConfig(Config c) => Connection = c;

	async Task SendToMixerAsync(byte[] bytes) {
		if (udpClient == null || _mixerEndPoint == null) throw new InvalidOperationException("OSC not configured");
		await udpClient.SendAsync(bytes, bytes.Length, _mixerEndPoint);
	}

	public async Task SendMessageAsync(OscMessage message) {
		string addr = NormalizeBindingAddress(message.Address);
		var toSend = new OscMessage(addr, message.Arguments.ToArray());
		byte[] bytes = toSend.GetBytes();
		Trace.WriteLine("Sending " + message.Address + "," + bytes.Length);
		await SendToMixerAsync(bytes);
	}

	static List<OscMessage> UnpackMessages(OscPacket packet) {
		if (packet is OscMessage m) return [m];
		if (packet is OscBundle b) return b.Messages;
		return [];
	}

	async Task<IReadOnlyList<OscMessage>?> ReceiveBatchAsync(CancellationToken cancellationToken) {
		if (udpClient == null) return null;
		UdpReceiveResult result;
		try {
			result = await udpClient.ReceiveAsync(cancellationToken);
		} catch (OperationCanceledException) {
			return null;
		} catch (SocketException ex) {
			Trace.WriteLine("SocketException");
			Trace.TraceError(ex.ToString());
			return null;
		} catch (ObjectDisposedException) {
			return null;
		}
		IReadOnlyList<OscMessage> list;
		try {
			list = UnpackMessages(OscPacket.GetPacket(result.Buffer));
		} catch (Exception ex) {
			Trace.WriteLine("OSC parse error, skipping datagram: " + ex.Message);
			return [];
		}
		foreach (OscMessage msg in list)
			Trace.WriteLine("received OSC address:'" + msg.Address + "'");
		return list;
	}

	/// <summary>Sends a query message for <paramref name="address"/> and polls replies until <paramref name="tryExtract"/> returns non-null or timeout.</summary>
	internal async Task<T?> QueryAsync<T>(string address, Func<OscMessage, T?> tryExtract) where T : class {
		address = NormalizeBindingAddress(address);
		await SendMessageAsync(new OscMessage(address));
		int budgetMs = (int)Math.Max(1, _config.timeoutMs);
		var deadline = DateTime.UtcNow.AddMilliseconds(budgetMs);
		while (DateTime.UtcNow < deadline) {
			int remainingMs = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
			int waitMs = Math.Min(remainingMs, budgetMs);
			using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(waitMs));
			IReadOnlyList<OscMessage>? batch = await ReceiveBatchAsync(cts.Token);
			if (batch == null || batch.Count == 0) continue;
			foreach (OscMessage message in batch) {
				if (!OscAddressMatches(message.Address, address)) continue;
				T? extracted = tryExtract(message);
				if (extracted != null) return extracted;
			}
		}
		return null;
	}

	/// <summary>Value-type wrapper so <see cref="QueryAsync{T}"/> can use <c>null</c> as "no match".</summary>
	sealed class Boxed<T>(T value) {
		public readonly T Value = value;
	}

	internal async Task<float?> QueryFloatAsync(string address, bool logUnmatchedArgs) {
		var result = await QueryAsync(NormalizeBindingAddress(address), msg => {
			if (TryCoerceFirstNumericFromMessage(msg, out float f))
				return new Boxed<float>(f);
			if (logUnmatchedArgs)
				Trace.WriteLine("query: matched path but no numeric arg, types=" + string.Join(",", msg.Arguments.Select(a => a?.GetType().Name ?? "null")));
			return null;
		});
		return result?.Value;
	}

	/// <summary>Normalize X32 channel indices and trim path (same as queries use).</summary>
	public static string NormalizeBindingAddress(string path) =>
		NormalizeOscAddress(NormalizeX32ChannelPath(path));

	/// <summary>Maps e.g. /main/st/mix/fader -> /main/st/mix/on for X32 mute (0 = muted, 1 = on).</summary>
	internal static string FaderPathToMixOnPath(string faderPath) {
		faderPath = NormalizeOscAddress(NormalizeX32ChannelPath(faderPath));
		const string FADER_PATH_SUFFIX = "/mix/fader";
		if (faderPath.EndsWith(FADER_PATH_SUFFIX, StringComparison.Ordinal))
			return faderPath[..^FADER_PATH_SUFFIX.Length] + "/mix/on";
		throw new InvalidOperationException("FaderAddress must end with /mix/fader for mute (e.g. /main/st/mix/fader).");
	}

	/// <summary>Trim; remove trailing slash so UI path matches desk replies.</summary>
	internal static string NormalizeOscAddress(string path) {
		path = path.Trim();
		while (path.Length > 1 && path[^1] == '/')
			path = path[..^1];
		return path;
	}

	static bool OscAddressMatches(string reply, string want) {
		reply = NormalizeOscAddress(reply);
		want = NormalizeOscAddress(want);
		return string.Equals(reply, want, StringComparison.Ordinal);
	}

	static bool TryCoerceFirstNumericFromMessage(OscMessage message, out float value) {
		value = 0;
		foreach (object? a in message.Arguments) {
			if (a == null) continue;
			switch (a) {
				case float f:
					value = f;
					return true;
				case double d:
					value = (float)d;
					return true;
				case int i:
					value = i;
					return true;
				case long l:
					value = l;
					return true;
				case string s when float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fs):
					value = fs;
					return true;
			}
		}
		return false;
	}

	internal static string FormatInfoArguments(OscMessage message) {
		var sb = new StringBuilder();
		foreach (object? a in message.Arguments) {
			if (sb.Length > 0) sb.AppendLine();
			sb.Append(FormatOscArg(a));
		}
		return sb.Length > 0 ? sb.ToString() : "(empty /info reply)";
	}

	internal static string FormatOscArg(object? a) {
		if (a == null) return "null";
		return a switch {
			float f => f.ToString(CultureInfo.InvariantCulture),
			double d => d.ToString(CultureInfo.InvariantCulture),
			int i => i.ToString(CultureInfo.InvariantCulture),
			long l => l.ToString(CultureInfo.InvariantCulture),
			string s => s,
			byte[] bytes => Convert.ToBase64String(bytes),
			_ => a.ToString() ?? "",
		};
	}

	/// <summary>X32 requires two-digit channel indices (/ch/01/..., not /ch/1/...).</summary>
	static string NormalizeX32ChannelPath(string path) {
		if (string.IsNullOrEmpty(path)) return path;
		path = path.Trim();
		return SingleDigitChannelRegex().Replace(path, m => m.Groups[1].Value + "0" + m.Groups[2].Value);
	}

	[GeneratedRegex(@"^(/ch/)(\d)(?=/)", RegexOptions.CultureInvariant)]
	private static partial Regex SingleDigitChannelRegex();

}
}
