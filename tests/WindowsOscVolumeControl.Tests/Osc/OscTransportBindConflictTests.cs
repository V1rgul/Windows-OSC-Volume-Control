using System.Net;
using System.Net.Sockets;
using System.Reflection;
using WindowsOscVolumeControl.Diagnostics;

namespace WindowsOscVolumeControl.Tests;

public class OscTransportBindConflictTests {
	[Fact]
	public void BindFailed_whenPortAlreadyBound() {
		using var portBlocker = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
		int port = ((IPEndPoint)portBlocker.Client.LocalEndPoint!).Port;

		using var transport = new OscTransport(new OscTransport.Config {
			address = IPAddress.Loopback,
			port = port,
		});

		Assert.Contains(typeof(StatusError.OscTransport.BindFailed), transport.statusRegister.activeStatusErrorTypes);
		var receiveLoopField = typeof(OscTransport).GetField("_receiveLoop", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.Null(receiveLoopField!.GetValue(transport));
	}

	[Fact]
	public async Task applyConfigAsync_afterPortFreed_clearsBindFailed() {
		using var portBlocker = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
		int takenPort = ((IPEndPoint)portBlocker.Client.LocalEndPoint!).Port;

		using var transport = new OscTransport(new OscTransport.Config {
			address = IPAddress.Loopback,
			port = takenPort,
		});
		Assert.Contains(typeof(StatusError.OscTransport.BindFailed), transport.statusRegister.activeStatusErrorTypes);

		portBlocker.Dispose();
		await transport.applyConfigAsync(new OscTransport.Config {
			address = IPAddress.Loopback,
			port = takenPort,
		});

		Assert.DoesNotContain(typeof(StatusError.OscTransport.BindFailed), transport.statusRegister.activeStatusErrorTypes);
		var receiveLoopField = typeof(OscTransport).GetField("_receiveLoop", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(receiveLoopField!.GetValue(transport));
	}
}
