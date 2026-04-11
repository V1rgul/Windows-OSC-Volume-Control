using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WindowsOscVolumeControl {
	internal enum FeedbackTone {
		NORMAL,
		SUCCESS,
		WARNING,
		ERROR,
	}

	internal static class NetworkPingTest {
		/// <summary>PingReply.RoundtripTime is whole milliseconds only; 0 means &lt;1 ms, not a sub-ms measurement.</summary>
		static string FormatPingSampleMs(long roundtripMs) => roundtripMs == 0 ? "<1 ms" : $"{roundtripMs} ms";

		static (string text, FeedbackTone tone) FormatPingFeedback(int ok, int denom, int totalProbes, long sumMs, long minMs, long maxMs, int timeoutMs, bool stillRunning) {
			int lostInDenom = denom - ok;
			double lossPct = denom > 0 ? 100.0 * lostInDenom / denom : 0;
			FeedbackTone tone = ok == 0 || lostInDenom > 0 ? FeedbackTone.WARNING : FeedbackTone.SUCCESS;
			string tail = stillRunning ? $" — {denom}/{totalProbes} probes…" : "";
			if (ok == 0) {
				if (!stillRunning)
					return ($"Ping test: {lossPct:0}% packet loss — no replies ({totalProbes} probes, timeout {timeoutMs} ms)", tone);
				return ($"Ping test: {lossPct:0}% loss, no replies yet ({denom}/{totalProbes} probes){tail}", tone);
			}
			double avg = (double)sumMs / ok;
			string spread = ok >= 2 ? $", min {FormatPingSampleMs(minMs)}, max {FormatPingSampleMs(maxMs)}" : "";
			string replyCount = stillRunning ? $"{ok}/{denom} replies so far" : $"{ok}/{totalProbes} replies";
			string core = $"Ping test: {lossPct:0}% loss, avg latency {avg:0} ms{spread} ({replyCount})";
			return (core + tail, tone);
		}

		internal static async Task<(string text, FeedbackTone tone)> PingFeedbackAsync(IPAddress address, int probes = 4, int timeoutMs = 750,
			IProgress<(string text, FeedbackTone tone)>? probeProgress = null) {
			using var ping = new Ping();
			int ok = 0;
			long sumMs = 0;
			long minMs = long.MaxValue;
			long maxMs = 0;
			for (int i = 0; i < probes; i++) {
				try {
					PingReply reply = await ping.SendPingAsync(address, timeoutMs).ConfigureAwait(false);
					if (reply.Status == IPStatus.Success) {
						ok++;
						long rt = reply.RoundtripTime;
						sumMs += rt;
						minMs = Math.Min(minMs, rt);
						maxMs = Math.Max(maxMs, rt);
					}
				} catch (PingException) { }
				catch (SocketException) { }
				int completed = i + 1;
				bool stillRunning = completed < probes;
				var line = FormatPingFeedback(ok, completed, probes, sumMs, minMs, maxMs, timeoutMs, stillRunning);
				probeProgress?.Report(line);
			}
			return FormatPingFeedback(ok, probes, probes, sumMs, minMs, maxMs, timeoutMs, stillRunning: false);
		}
	}
}
