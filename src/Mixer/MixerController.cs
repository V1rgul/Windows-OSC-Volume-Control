using System.Diagnostics;
using System.Globalization;
using System.Text;
using SharpOSC;

namespace WindowsOscVolumeControl.Diagnostics {
	public abstract partial record Error {
		public abstract record MixerController : Error {
			public sealed record Network : MixerController;
			public sealed record InvalidReply : MixerController;
		}
	}
}

namespace WindowsOscVolumeControl.Mixer {

/// <summary>High-level mixer operations (continuous float paths, OSC toggles, <c>/info</c>) with optional per-address value cache.</summary>
public sealed class MixerController {
	const string INFO_ADDRESS = "/info";

	public sealed class Config {
		public const float MIN_CONTINUOUS_STEP = 0.001f;
		public const uint MIN_TIMEOUT_MS = 1;
		public const uint MAX_TIMEOUT_MS = 10_000;
		public const uint MIN_VALUE_CACHE_TTL_MS = 0;
		public const uint MAX_VALUE_CACHE_TTL_MS = 10_000;

		public uint timeoutMs { get; set; } = 200;
		public uint ValueCacheTtlMs { get; set; } = 1000;

		public Config() { }

		public Config(Config other) {
			ArgumentNullException.ThrowIfNull(other);
			timeoutMs = other.timeoutMs;
			ValueCacheTtlMs = other.ValueCacheTtlMs;
		}
	}

	public abstract class Event {
		public required string address { get; init; }

		public sealed class FaderChanged : Event {
			public required float newLevel { get; init; }
			public required bool volumeIncreased { get; init; }
		}

		public sealed class ToggleChanged : Event {
			public required bool nowOn { get; init; }
		}

		public sealed class OperationFailed : Event { }
	}

	readonly OscTransport _transport;
	readonly object _lock = new();
	readonly Dictionary<string, MixerAddressState> _stateByAddress = new(StringComparer.Ordinal);
	Task<(bool Ok, string Detail)>? _pendingInfoTask;
	TaskCompletionSource<OscMessage>? _pendingInfoReply;
	CancellationTokenSource? _pendingInfoCancellation;
	int _pendingInfoVersion;
	Config _config;

	public ErrorList<Error.MixerController> errors { get; } = new();
	public event Action<Event>? eventReceived;

	public MixerController(OscTransport transport) : this(transport, new Config()) { }

	public MixerController(OscTransport transport, Config initialConfig) {
		_transport = transport ?? throw new ArgumentNullException(nameof(transport));
		ArgumentNullException.ThrowIfNull(initialConfig);
		_config = new Config(initialConfig);
		_transport.messageReceived += onMessage;
	}

	public void ApplyConfig(Config config) {
		ArgumentNullException.ThrowIfNull(config);
		CancellationTokenSource? pendingInfoCancellation;
		lock (_lock) {
			_config = new Config(config);
			_stateByAddress.Clear();
			_pendingInfoVersion++;
			pendingInfoCancellation = _pendingInfoCancellation;
			_pendingInfoCancellation = null;
			_pendingInfoTask = null;
			_pendingInfoReply = null;
		}
		pendingInfoCancellation?.Cancel();
		errors.clearAll();
	}

	public bool HasFreshContinuousSample(string address) {
		lock (_lock) {
			return _stateByAddress.TryGetValue(address, out MixerAddressState? state)
				&& state is MixerAddressState.Continuous cont
				&& cont.isCacheFresh(_config.ValueCacheTtlMs);
		}
	}

	public bool tryGetMeasuredLatency(string address, out TimeSpan latency) {
		lock (_lock) {
			if (_stateByAddress.TryGetValue(address, out MixerAddressState? state)) {
				TimeSpan? measuredLatency = state.tryGetLastQueryLatency();
				if (measuredLatency != null) {
					latency = measuredLatency.Value;
					return true;
				}
			}
		}

		latency = default;
		return false;
	}

	public void enqueueContinuousAction(string path, ControlActionContinuousAbstract action, BindingFloatAbstract binding) {
		ArgumentNullException.ThrowIfNull(path);
		ArgumentNullException.ThrowIfNull(action);
		ArgumentNullException.ThrowIfNull(binding);
		if (!float.IsFinite(binding.minimum) || !float.IsFinite(binding.maximum) || binding.minimum > binding.maximum)
			return;

		lock (_lock) {
			MixerAddressState.Continuous cont = getOrAddContinuousState(path);
			if (!action.needsCurrentWire) {
				cont.prepareImmediateSet(binding, DateTime.UtcNow);
				float wire = binding.applyContinuousAction(action, 0f);
				float? prev = cont.tryGetCachedValueTyped(_config.ValueCacheTtlMs);
				bool increased = prev == null || wire >= prev.Value;
				cont.updateCache(wire);
				sendAndEmit(
					path,
					wire,
					new Event.FaderChanged {
						address = path,
						newLevel = wire,
						volumeIncreased = increased,
					});
				return;
			}

			cont.enqueue(action, binding, DateTime.UtcNow);
			if (cont.tryGetCachedValueTyped(_config.ValueCacheTtlMs) != null)
				applyPending(path, cont);
			else
				refreshCache(path, cont);
		}
	}

	public void toggle(string address) {
		lock (_lock) {
			MixerAddressState.Toggle toggleState = getOrAddToggleState(address);
			toggleState.markPending(DateTime.UtcNow);

			if (toggleState.tryGetCachedValueTyped(_config.ValueCacheTtlMs) != null) {
				applyPending(address, toggleState);
				return;
			}

			refreshCache(address, toggleState);
		}
	}

	/// <summary>Sets toggle to an explicit on/off state; clears pending flip and updates cache.</summary>
	public void setToggle(string address, bool on) {
		lock (_lock) {
			MixerAddressState.Toggle toggleState = getOrAddToggleState(address);
			toggleState.clearPending();
			toggleState.updateCache(on);
			sendAndEmit(
				address,
				on ? 1f : 0f,
				new Event.ToggleChanged {
					address = address,
					nowOn = on,
				});
		}
	}

	public async Task<float?> QueryContinuousWireAsync(string address) {
		TaskCompletionSource<OscMessage> reply = createPendingReply();
		lock (_lock)
			getOrAddContinuousState(address).markQuerySent(DateTime.UtcNow);

		void handler(OscMessage message) {
			if (StringComparer.Ordinal.Equals(message.Address, address))
				reply.TrySetResult(message);
		}

		_transport.messageReceived += handler;
		try {
			if (!await trySendAsync(address).ConfigureAwait(false))
				return null;
			OscMessage message = await reply.Task.WaitAsync(getTimeout()).ConfigureAwait(false);
			if (message.Arguments.Count == 0 || message.Arguments[0] is not float f) {
				errors.setError(new Error.MixerController.InvalidReply(), true);
				return null;
			}

			clearMixerErrors();
			return f;
		} catch (TimeoutException) {
			errors.setError(new Error.MixerController.Network(), true);
			return null;
		} finally {
			_transport.messageReceived -= handler;
		}
	}

	public async Task<bool?> QueryToggleAsync(string address) {
		TaskCompletionSource<OscMessage> reply = createPendingReply();
		lock (_lock)
			getOrAddToggleState(address).markQuerySent(DateTime.UtcNow);

		void handler(OscMessage message) {
			if (StringComparer.Ordinal.Equals(message.Address, address))
				reply.TrySetResult(message);
		}

		_transport.messageReceived += handler;
		try {
			if (!await trySendAsync(address).ConfigureAwait(false))
				return null;
			OscMessage message = await reply.Task.WaitAsync(getTimeout()).ConfigureAwait(false);
			if (message.Arguments.Count == 0 || message.Arguments[0] is not int i) {
				errors.setError(new Error.MixerController.InvalidReply(), true);
				return null;
			}

			clearMixerErrors();
			return i != 0;
		} catch (TimeoutException) {
			errors.setError(new Error.MixerController.Network(), true);
			return null;
		} finally {
			_transport.messageReceived -= handler;
		}
	}

	public Task<(bool Ok, string Detail)> QueryInfoAsync() {
		lock (_lock) {
			if (_pendingInfoTask != null)
				return _pendingInfoTask;

			_pendingInfoVersion++;
			_pendingInfoReply = createPendingReply();
			_pendingInfoCancellation = new CancellationTokenSource();
			_pendingInfoTask = queryInfoCoreAsync(_pendingInfoReply, _pendingInfoVersion, _pendingInfoCancellation.Token);
			return _pendingInfoTask;
		}
	}

	public async Task<bool> TestConnectionAsync() {
		var (ok, _) = await QueryInfoAsync().ConfigureAwait(false);
		return ok;
	}

	async Task<(bool Ok, string Detail)> queryInfoCoreAsync(TaskCompletionSource<OscMessage> reply, int version, CancellationToken cancellationToken) {
		try {
			lock (_lock)
				getOrAddInfoState().markQuerySent(DateTime.UtcNow);

			if (!await trySendAsync(INFO_ADDRESS).ConfigureAwait(false))
				return (false, "OSC send failed (check IP, port, and network).");
			OscMessage message = await reply.Task.WaitAsync(getTimeout(), cancellationToken).ConfigureAwait(false);
			clearMixerErrors();
			return (true, formatInfoArguments(message));
		} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			return (false, "Request canceled due to config change.");
		} catch (TimeoutException) {
			errors.setError(new Error.MixerController.Network(), true);
			return (false, "No reply to /info within timeout (check IP, port, and network).");
		} finally {
			lock (_lock) {
				if (version == _pendingInfoVersion) {
					_pendingInfoTask = null;
					_pendingInfoReply = null;
					_pendingInfoCancellation = null;
				}
			}
		}
	}

	void onMessage(OscMessage msg) {
		lock (_lock) {
			if (!_stateByAddress.TryGetValue(msg.Address, out MixerAddressState? state))
				return;

			state.tryRecordQueryLatency(DateTime.UtcNow);

			switch (state) {
				case MixerAddressState.Continuous cont:
					if (!cont.tryUpdateCacheFromReply(msg)) {
						if (cont.hasPending) {
							cont.clearPending();
							errors.setError(new Error.MixerController.InvalidReply(), true);
							emitFailure(msg.Address);
						}
						return;
					}
					errors.setError(new Error.MixerController.InvalidReply(), false);
					applyPending(msg.Address, cont);
					return;

				case MixerAddressState.Toggle toggleState:
					if (!toggleState.tryUpdateCacheFromReply(msg)) {
						if (toggleState.hasPending) {
							toggleState.clearPending();
							errors.setError(new Error.MixerController.InvalidReply(), true);
							emitFailure(msg.Address);
						}
						return;
					}
					errors.setError(new Error.MixerController.InvalidReply(), false);
					applyPending(msg.Address, toggleState);
					return;

				case MixerAddressState.Info:
					_pendingInfoReply?.TrySetResult(msg);
					return;
			}
		}
	}

	void refreshCache(string address, MixerAddressState state) {
		state.markQuerySent(DateTime.UtcNow);
		_ = refreshCacheAsync(address);
	}

	async Task refreshCacheAsync(string address) {
		if (!await trySendAsync(address).ConfigureAwait(false))
			emitFailure(address);
	}

	void applyPending(string address, MixerAddressState.Continuous cont) {
		if (!cont.hasPending)
			return;

		if (!cont.tryApplyPending(_config.timeoutMs, out float newVal, out bool requestedIncrease)) {
			errors.setError(new Error.MixerController.Network(), true);
			emitFailure(address);
			return;
		}

		sendAndEmit(
			address,
			newVal,
			new Event.FaderChanged {
				address = address,
				newLevel = newVal,
				volumeIncreased = requestedIncrease,
			});
	}

	void applyPending(string address, MixerAddressState.Toggle toggleState) {
		if (!toggleState.hasPending)
			return;

		if (!toggleState.tryApplyPending(_config.timeoutMs, out bool nowOn)) {
			errors.setError(new Error.MixerController.Network(), true);
			emitFailure(address);
			return;
		}

		sendAndEmit(
			address,
			nowOn ? 1f : 0f,
			new Event.ToggleChanged {
				address = address,
				nowOn = nowOn,
			});
	}

	void sendAndEmit(string address, object arg, Event evt) {
		_ = sendAndEmitAsync(address, arg, evt);
	}

	async Task sendAndEmitAsync(string address, object arg, Event evt) {
		if (!await trySendAsync(address, arg).ConfigureAwait(false)) {
			emitFailure(address);
			return;
		}

		clearMixerErrors();
		emit(evt);
	}

	void emitFailure(string address) =>
		emit(new Event.OperationFailed { address = address });

	void emit(Event evt) {
		try {
			eventReceived?.Invoke(evt);
		} catch (Exception ex) {
			AppTrace.Application.TraceEvent(
				TraceEventType.Error,
				0,
				$"Mixer event handler failed: {ex}");
		}
	}

	async Task<bool> trySendAsync(string address, params object[] args) {
		try {
			await _transport.sendAsync(address, args).ConfigureAwait(false);
			return true;
		} catch (Exception ex) {
			AppTrace.Application.TraceEvent(
				TraceEventType.Error,
				0,
				$"OSC send failed for '{address}': {ex}");
			errors.setError(new Error.MixerController.Network(), true);
			return false;
		}
	}

	void clearMixerErrors() {
		errors.setError(new Error.MixerController.Network(), false);
		errors.setError(new Error.MixerController.InvalidReply(), false);
	}

	TaskCompletionSource<OscMessage> createPendingReply() =>
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	TimeSpan getTimeout() => TimeSpan.FromMilliseconds(_config.timeoutMs);

	MixerAddressState.Continuous getOrAddContinuousState(string address) {
		if (!_stateByAddress.TryGetValue(address, out MixerAddressState? state)) {
			state = new MixerAddressState.Continuous();
			_stateByAddress[address] = state;
		}
		return (MixerAddressState.Continuous)state;
	}

	MixerAddressState.Toggle getOrAddToggleState(string address) {
		if (!_stateByAddress.TryGetValue(address, out MixerAddressState? state)) {
			state = new MixerAddressState.Toggle();
			_stateByAddress[address] = state;
		}
		return (MixerAddressState.Toggle)state;
	}

	MixerAddressState.Info getOrAddInfoState() {
		if (!_stateByAddress.TryGetValue(INFO_ADDRESS, out MixerAddressState? state)) {
			state = new MixerAddressState.Info();
			_stateByAddress[INFO_ADDRESS] = state;
		}
		return (MixerAddressState.Info)state;
	}

	internal static string FaderPathToMixOnPath(string faderPath) {
		const string faderPathSuffix = "/mix/fader";
		if (faderPath.EndsWith(faderPathSuffix, StringComparison.Ordinal))
			return faderPath[..^faderPathSuffix.Length] + "/mix/on";
		throw new InvalidOperationException("FaderAddress must end with /mix/fader for mute (e.g. /main/st/mix/fader).");
	}

	internal static string formatInfoArguments(OscMessage message) {
		var sb = new StringBuilder();
		foreach (object? arg in message.Arguments) {
			if (sb.Length > 0)
				sb.AppendLine();
			sb.Append(formatOscArg(arg));
		}
		return sb.Length > 0 ? sb.ToString() : "(empty /info reply)";
	}

	internal static string formatOscArg(object? arg) {
		if (arg == null)
			return "null";
		return arg switch {
			float f => f.ToString(CultureInfo.InvariantCulture),
			double d => d.ToString(CultureInfo.InvariantCulture),
			int i => i.ToString(CultureInfo.InvariantCulture),
			long l => l.ToString(CultureInfo.InvariantCulture),
			string s => s,
			byte[] bytes => Convert.ToBase64String(bytes),
			_ => arg.ToString() ?? "",
		};
	}

	public static UiTextFeedback infoQueryDetailFeedback(bool ok, string detail) =>
		new(detail, ok ? UiTextFeedbackKind.DEFAULT : UiTextFeedbackKind.ERROR);

	public static UiTextFeedback settingsApplyMixerSummaryFeedback(bool mixerInfoOk) =>
		new(
			mixerInfoOk
				? "Settings applied and mixer responded."
				: "Settings saved, but the mixer did not respond cleanly.",
			mixerInfoOk ? UiTextFeedbackKind.SUCCESS : UiTextFeedbackKind.ERROR);

	public static UiTextFeedback exceptionMessageFeedback(Exception ex) =>
		new(ex.Message, UiTextFeedbackKind.ERROR);
}
}
