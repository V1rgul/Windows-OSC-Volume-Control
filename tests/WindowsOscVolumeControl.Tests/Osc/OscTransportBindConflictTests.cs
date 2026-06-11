using System.Net;
using System.Net.Sockets;
using System.Reflection;
using WindowsOscVolumeControl.Diagnostics;

namespace WindowsOscVolumeControl.Tests;

public class OscTransportBindConflictTests {
	[Fact]
	public void OscTransport_SetsBindFailed_WhenPortAlreadyBound() {
		using var portBlocker = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
		int port = ((IPEndPoint)portBlocker.Client.LocalEndPoint!).Port;

		using var transport = new OscTransport(new OscTransport.Config {
			endPoint = new IPEndPoint(IPAddress.Loopback, port),
		});

		Assert.Contains(transport.errors.activeErrors, static e => e is Error.OscTransport.BindFailed);
		var receiveLoopField = typeof(OscTransport).GetField("_receiveLoop", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.Null(receiveLoopField!.GetValue(transport));
	}

	[Fact]
	public async Task OscTransport_ClearsBindFailed_AfterSuccessfulRebind() {
		using var portBlocker = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
		int takenPort = ((IPEndPoint)portBlocker.Client.LocalEndPoint!).Port;

		using var transport = new OscTransport(new OscTransport.Config {
			endPoint = new IPEndPoint(IPAddress.Loopback, takenPort),
		});
		Assert.Contains(transport.errors.activeErrors, static e => e is Error.OscTransport.BindFailed);

		portBlocker.Dispose();
		await transport.applyConfigAsync(new OscTransport.Config {
			endPoint = new IPEndPoint(IPAddress.Loopback, takenPort),
		});

		Assert.DoesNotContain(transport.errors.activeErrors, static e => e is Error.OscTransport.BindFailed);
		var receiveLoopField = typeof(OscTransport).GetField("_receiveLoop", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(receiveLoopField!.GetValue(transport));
	}
}
