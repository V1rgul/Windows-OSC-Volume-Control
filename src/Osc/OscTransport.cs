using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SharpOSC;

namespace WindowsOscVolumeControl;

public sealed class OscTransport : IDisposable {
	public sealed class Config {
		public IPEndPoint endPoint { get; set; } = new(IPAddress.Parse("127.0.0.1"), 10023);

		public Config() { }

		public Config(Config other) {
			ArgumentNullException.ThrowIfNull(other);
			endPoint = new IPEndPoint(other.endPoint.Address, other.endPoint.Port);
		}
	}

	readonly object _lock = new();
	UdpClient? _udp;
	IPEndPoint? _remote;
	CancellationTokenSource? _loopCts;
	Task? _receiveLoop;
	bool _disposed;

	public event Action<OscMessage>? messageReceived;

	public OscTransport(Config config) {
		applyConfig(config);
	}

	public void applyConfig(Config config) {
		ArgumentNullException.ThrowIfNull(config);

		Config nextConfig = new(config);
		UdpClient? oldUdp;
		CancellationTokenSource? oldCts;
		Task? oldLoop;

		lock (_lock) {
			throwIfDisposed();

			oldUdp = _udp;
			oldCts = _loopCts;
			oldLoop = _receiveLoop;

			_udp = null;
			_remote = null;
			_loopCts = null;
			_receiveLoop = null;
		}

		oldCts?.Cancel();
		wakeReceiveLoop(oldUdp);
		if (oldLoop != null) {
			try {
				oldLoop.Wait();
			} catch (AggregateException ex) when (ex.InnerExceptions.Count == 1 && ex.InnerException is OperationCanceledException) {
			}
		}
		oldCts?.Dispose();
		oldUdp?.Dispose();

		UdpClient nextUdp = createUdpClient(nextConfig.endPoint.Port);
		IPEndPoint nextRemote = new(nextConfig.endPoint.Address, nextConfig.endPoint.Port);
		var nextCts = new CancellationTokenSource();

		lock (_lock) {
			throwIfDisposed();
			_udp = nextUdp;
			_remote = nextRemote;
			_loopCts = nextCts;
			_receiveLoop = Task.Run(() => receiveLoopAsync(nextUdp, nextCts.Token));
		}

		AppTrace.OscTransport.TraceEvent(
			TraceEventType.Information,
			0,
			$"OSC socket local={nextUdp.Client.LocalEndPoint} remote={nextRemote}");
	}

	public async Task sendAsync(string address, params object[] args) {
		ArgumentException.ThrowIfNullOrWhiteSpace(address);

		UdpClient udp;
		IPEndPoint remote;
		lock (_lock) {
			throwIfDisposed();
			udp = _udp ?? throw new InvalidOperationException("OSC transport is not configured.");
			remote = _remote ?? throw new InvalidOperationException("OSC transport is not configured.");
		}

		var message = new OscMessage(address, args);
		byte[] bytes = message.GetBytes();
		string argText = args.Length == 0
			? ""
			: " args=[" + string.Join(", ", args.Select(static a => a == null ? "null" : (a.GetType().Name + ":" + a))) + "]";
		string line = $"Sending {address}, {bytes.Length} B{argText}";
		AppTrace.OscTransport.TraceEvent(TraceEventType.Information, 0, line);
		await udp.SendAsync(bytes, bytes.Length, remote).ConfigureAwait(false);
	}

	async Task receiveLoopAsync(UdpClient udp, CancellationToken cancellationToken) {
		while (!cancellationToken.IsCancellationRequested) {
			try {
				UdpReceiveResult result = await udp.ReceiveAsync().ConfigureAwait(false);
				if (cancellationToken.IsCancellationRequested)
					break;

				OscPacket packet;
				try {
					packet = OscPacket.GetPacket(result.Buffer);
				} catch (Exception ex) {
					AppTrace.OscTransport.TraceEvent(
						TraceEventType.Warning,
						0,
						$"OSC parse error, skipping datagram: {ex.Message}");
					continue;
				}

				foreach (OscMessage message in unpackMessages(packet)) {
					AppTrace.OscTransport.TraceEvent(
						TraceEventType.Verbose,
						0,
						$"Received OSC address '{message.Address}'");
					try {
						messageReceived?.Invoke(message);
					} catch (Exception ex) {
						AppTrace.OscTransport.TraceEvent(
							TraceEventType.Error,
							0,
							$"OSC message handler failed: {ex}");
					}
				}
			} catch (ObjectDisposedException) {
				break;
			} catch (SocketException ex) {
				if (_disposed || cancellationToken.IsCancellationRequested)
					break;
				AppTrace.OscTransport.TraceEvent(TraceEventType.Error, 0, ex.ToString());
			}
		}
	}

	static void wakeReceiveLoop(UdpClient? udp) {
		if (udp?.Client.LocalEndPoint is not IPEndPoint localEndPoint)
			return;

		using var wakeSender = new UdpClient(AddressFamily.InterNetwork);
		wakeSender.Send([0], 1, new IPEndPoint(IPAddress.Loopback, localEndPoint.Port));
	}

	static IEnumerable<OscMessage> unpackMessages(OscPacket packet) {
		if (packet is OscMessage message) {
			yield return message;
			yield break;
		}

		if (packet is OscBundle bundle) {
			foreach (OscMessage bundledMessage in bundle.Messages)
				yield return bundledMessage;
		}
	}

	static UdpClient createUdpClient(int oscPort) {
		var udp = new UdpClient(AddressFamily.InterNetwork);
		try {
			udp.Client.Bind(new IPEndPoint(IPAddress.Any, oscPort));
		} catch (SocketException ex) {
			AppTrace.OscTransport.TraceEvent(
				TraceEventType.Warning,
				0,
				$"Bind local UDP {oscPort} failed (another app may use it): {ex.Message}");
			udp.Dispose();
			udp = new UdpClient(AddressFamily.InterNetwork);
		}

		udp.Client.IOControl((IOControlCode)(-1744830452), [0], null);
		return udp;
	}

	void throwIfDisposed() {
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	public void Dispose() {
		UdpClient? udp;
		CancellationTokenSource? cts;
		Task? loop;

		lock (_lock) {
			if (_disposed)
				return;

			_disposed = true;
			udp = _udp;
			cts = _loopCts;
			loop = _receiveLoop;
			_udp = null;
			_remote = null;
			_loopCts = null;
			_receiveLoop = null;
		}

		cts?.Cancel();
		wakeReceiveLoop(udp);
		if (loop != null) {
			try {
				loop.Wait();
			} catch (AggregateException ex) when (ex.InnerExceptions.Count == 1 && ex.InnerException is OperationCanceledException) {
			}
		}
		cts?.Dispose();
		udp?.Dispose();
	}
}
