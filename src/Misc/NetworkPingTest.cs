using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WindowsOscVolumeControl;

internal static class NetworkPingTest {
	internal static async Task<int?> PingOnceAsync(IPAddress address, int timeoutMs) {
		using var ping = new Ping();
		try {
			PingReply reply = await ping.SendPingAsync(address, timeoutMs).ConfigureAwait(false);
			if (reply.Status != IPStatus.Success)
				return null;
			long ms = reply.RoundtripTime;
			if (ms < 0 || ms > int.MaxValue)
				return null;
			return (int)ms;
		} catch (PingException) {
			return null;
		} catch (SocketException) {
			return null;
		}
	}
}
