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

namespace X32VolumeHijacker {
public partial class OscController {

	public class Config {
		public IPEndPoint EndPoint = null!;
		public string faderAddress = "";
		public uint timeoutMs = ConfigStore.DefaultQueryTimeoutMs;

		public Config() {}
		public Config(Config other) {
			ArgumentNullException.ThrowIfNull(other);
			EndPoint = new IPEndPoint(other.EndPoint.Address, other.EndPoint.Port);
			faderAddress = other.faderAddress;
			timeoutMs = other.timeoutMs;
		}
	}

	UdpClient udpClient = null!;
	IPEndPoint? _mixerEndPoint;

	/// <summary>NormalizeOscAddress(NormalizeX32ChannelPath(fader)); refreshed when <see cref="Connection"/> is set.</summary>
	string _oscFaderPath = "";
	/// <summary><c>FaderPathToMixOnPath(fader)</c> when valid; otherwise <c>null</c> (mute APIs resolve/throw on use).</summary>
	string? _oscMixOnPath;

	public OscController(Config config){
		Connection = config;
	}

	Config _config = null!;
	/// <summary>Mixer endpoint, fader OSC address, and query timeout (get returns a copy).</summary>
	public Config Connection {
		get { return new Config(_config); }
		set {
			udpClient?.Dispose();
			int oscPort = value.EndPoint.Port;
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
			_mixerEndPoint = new IPEndPoint(value.EndPoint.Address, value.EndPoint.Port);
			Trace.WriteLine("OSC socket local=" + udpClient.Client.LocalEndPoint + " remote=" + _mixerEndPoint);
			_config = new Config(value);
			_config.faderAddress = (_config.faderAddress ?? "").Trim();
			_oscFaderPath = NormalizeOscAddress(NormalizeX32ChannelPath(_config.faderAddress));
			try {
				_oscMixOnPath = FaderPathToMixOnPath(_config.faderAddress);
			} catch (InvalidOperationException) {
				_oscMixOnPath = null;
			}
		}
	}

	/// <summary>Applies mixer connection settings (same as assigning <see cref="Connection"/>).</summary>
	public void ApplyConfig(Config c) => Connection = c;

	async Task SendToMixerAsync(byte[] bytes) {
		if (udpClient == null || _mixerEndPoint == null) throw new InvalidOperationException("OSC not configured");
		await udpClient.SendAsync(bytes, bytes.Length, _mixerEndPoint);
	}

	public async Task SendMessageAsync(OscMessage message) {
		byte[] bytes = message.GetBytes();
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
	async Task<T?> QueryAsync<T>(string address, Func<OscMessage, T?> tryExtract) where T : class {
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

	async Task<float?> QueryFloatAsync(string address, bool logUnmatchedArgs) {
		var result = await QueryAsync(address, msg => {
			if (TryCoerceFirstNumericFromMessage(msg, out float f))
				return new Boxed<float>(f);
			if (logUnmatchedArgs)
				Trace.WriteLine("query: matched path but no numeric arg, types=" + string.Join(",", msg.Arguments.Select(a => a?.GetType().Name ?? "null")));
			return null;
		});
		return result?.Value;
	}

	public async Task SetFaderAsync(float value) {
		await SendMessageAsync(new OscMessage(_oscFaderPath, value));
	}

	/// <summary>Maps e.g. /main/st/mix/fader -> /main/st/mix/on for X32 mute (0 = muted, 1 = on).</summary>
	static string FaderPathToMixOnPath(string faderPath) {
		faderPath = NormalizeOscAddress(NormalizeX32ChannelPath(faderPath));
		const string suffix = "/mix/fader";
		if (faderPath.EndsWith(suffix, StringComparison.Ordinal))
			return faderPath[..^suffix.Length] + "/mix/on";
		throw new InvalidOperationException("FaderAddress must end with /mix/fader for mute (e.g. /main/st/mix/fader).");
	}

	public async Task<bool?> QueryMuteAsync() {
		string want = _oscMixOnPath ?? FaderPathToMixOnPath(_config.faderAddress);
		float? v = await QueryFloatAsync(want, logUnmatchedArgs: false);
		return v == null ? null : v.Value < 0.5f;
	}

	public async Task SetMuteAsync(bool muted) {
		string path = _oscMixOnPath ?? FaderPathToMixOnPath(_config.faderAddress);
		await SendMessageAsync(new OscMessage(path, muted ? 0f : 1f));
	}

	public async Task<bool?> QueryToggleAsync(string address) {
		address = NormalizeOscAddress(address);
		float? v = await QueryFloatAsync(address, logUnmatchedArgs: false);
		return v == null ? null : v.Value >= 0.5f;
	}

	public async Task SetToggleAsync(string address, bool enabled) {
		address = NormalizeOscAddress(address);
		await SendMessageAsync(new OscMessage(address, enabled ? 1f : 0f));
	}

	public async Task<float?> QueryFaderAsync() {
		return await QueryFloatAsync(_oscFaderPath, logUnmatchedArgs: true);
	}

	/// <summary>Trim; remove trailing slash so UI path matches desk replies.</summary>
	static string NormalizeOscAddress(string path) {
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

	/// <summary>Sends <c>/info</c> and returns the first matching reply (X32 desk identity / firmware strings).</summary>
	public async Task<(bool Ok, string Detail)> QueryInfoAsync() {
		var result = await QueryAsync("/info", msg => FormatInfoArguments(msg));
		if (result != null)
			return (true, result);
		return (false, "No reply to /info within timeout (check IP, port, and network).");
	}

	static string FormatInfoArguments(OscMessage message) {
		var sb = new StringBuilder();
		foreach (object? a in message.Arguments) {
			if (sb.Length > 0) sb.AppendLine();
			sb.Append(FormatOscArg(a));
		}
		return sb.Length > 0 ? sb.ToString() : "(empty /info reply)";
	}

	static string FormatOscArg(object? a) {
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

	public async Task<bool> TestConnectionAsync() {
		var (ok, _) = await QueryInfoAsync().ConfigureAwait(false);
		return ok;
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
