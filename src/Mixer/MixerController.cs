using System.Diagnostics;
using System.Globalization;
using System.Text;
using Result;
using SharpOSC;
using WindowsOscVolumeControl.Diagnostics;

namespace WindowsOscVolumeControl.Diagnostics {
	public abstract partial record StatusError {
		public abstract record MixerController : StatusError {
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

		public static Result<uint> parseTimeoutMs(string? text) =>
			parseBoundedUInt(text, MIN_TIMEOUT_MS, MAX_TIMEOUT_MS);

		public static Result<uint> parseValueCacheTtlMs(string? text) =>
			parseBoundedUInt(text, MIN_VALUE_CACHE_TTL_MS, MAX_VALUE_CACHE_TTL_MS);

		static Result<uint> parseBoundedUInt(string? text, uint min, uint max) {
			if (!uint.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed))
				return new ResultError.Generic.Parsing { message = "Must be an integer." };
			if (parsed < min || parsed > max)
				return new ResultError.Generic.Parsing { message = $"Must be between {min} and {max}." };
			return parsed;
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

	public StatusRegister<StatusError.MixerController> statusRegister { get; } = new();
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
		statusRegister.clearAll();
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

		ActionPlan plan;
		lock (_lock) {
			MixerAddressState.Continuous cont = getOrAddContinuousState(path);
			if (!action.needsCurrentWire) {
				cont.prepareImmediateSet(binding, DateTime.UtcNow);
				float wire = binding.applyContinuousAction(action, 0f);
				float? prev = cont.tryGetCachedValueTyped(_config.ValueCacheTtlMs);
				bool increased = prev == null || wire >= prev.Value;
				cont.updateCache(wire);
				plan = new ActionPlan { effect = ActionEffect.SEND_FADER, faderValue = wire, faderIncreased = increased };
			} else {
				cont.enqueue(action, binding, DateTime.UtcNow);
				plan = cont.tryGetCachedValueTyped(_config.ValueCacheTtlMs) != null
					? planApplyContinuousLocked(cont)
					: planRefreshQueryLocked(cont);
			}
		}
		executePlan(path, plan);
	}

	public void toggle(string address) {
		ActionPlan plan;
		lock (_lock) {
			MixerAddressState.Toggle toggleState = getOrAddToggleState(address);
			toggleState.markPending(DateTime.UtcNow);

			plan = toggleState.tryGetCachedValueTyped(_config.ValueCacheTtlMs) != null
				? planApplyToggleLocked(toggleState)
				: planRefreshQueryLocked(toggleState);
		}
		executePlan(address, plan);
	}

	/// <summary>Sets toggle to an explicit on/off state; clears pending flip and updates cache.</summary>
	public void setToggle(string address, bool on) {
		lock (_lock) {
			MixerAddressState.Toggle toggleState = getOrAddToggleState(address);
			toggleState.clearPending();
			toggleState.updateCache(on);
		}
		executePlan(address, new ActionPlan { effect = ActionEffect.SEND_TOGGLE, toggleOn = on });
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
				statusRegister.setStatusError<StatusError.MixerController.InvalidReply>(true);
				return null;
			}

			clearMixerErrors();
			return f;
		} catch (TimeoutException) {
			statusRegister.setStatusError<StatusError.MixerController.Network>(true);
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
				statusRegister.setStatusError<StatusError.MixerController.InvalidReply>(true);
				return null;
			}

			clearMixerErrors();
			return i != 0;
		} catch (TimeoutException) {
			statusRegister.setStatusError<StatusError.MixerController.Network>(true);
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
			statusRegister.setStatusError<StatusError.MixerController.Network>(true);
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

	/// <summary>What to do for an address once <see cref="_lock"/> is released; state mutation happens under the lock, sends/error/event fan-out outside it.</summary>
	enum ActionEffect {
		NONE,
		/// <summary>Reply did not parse while actions were pending.</summary>
		FAIL_INVALID_REPLY,
		/// <summary>Pending actions could not be applied (expired or no cached value).</summary>
		FAIL_APPLY,
		SEND_FADER,
		SEND_TOGGLE,
		/// <summary>Cache is stale and no query is in flight: send a bare query for the address.</summary>
		REFRESH_QUERY,
	}

	struct ActionPlan {
		public ActionEffect effect;
		/// <summary>Set on reply-driven plans only; hotkey-driven plans leave the error untouched (as before the refactor).</summary>
		public bool clearInvalidReply;
		public float faderValue;
		public bool faderIncreased;
		public bool toggleOn;
	}

	void onMessage(OscMessage msg) {
		TaskCompletionSource<OscMessage>? infoReply = null;
		var plan = default(ActionPlan);

		lock (_lock) {
			if (!_stateByAddress.TryGetValue(msg.Address, out MixerAddressState? state))
				return;

			state.tryRecordQueryLatency(DateTime.UtcNow);

			switch (state) {
				case MixerAddressState.Continuous cont:
					plan = planContinuousReplyLocked(cont, msg);
					break;
				case MixerAddressState.Toggle toggleState:
					plan = planToggleReplyLocked(toggleState, msg);
					break;
				case MixerAddressState.Info:
					infoReply = _pendingInfoReply;
					break;
			}
		}

		if (infoReply != null) {
			infoReply.TrySetResult(msg);
			return;
		}
		executePlan(msg.Address, plan);
	}

	ActionPlan planContinuousReplyLocked(MixerAddressState.Continuous cont, OscMessage msg) {
		if (!cont.tryUpdateCacheFromReply(msg)) {
			if (!cont.hasPending)
				return default;
			cont.clearPending();
			return new ActionPlan { effect = ActionEffect.FAIL_INVALID_REPLY };
		}
		ActionPlan plan = planApplyContinuousLocked(cont);
		plan.clearInvalidReply = true;
		return plan;
	}

	ActionPlan planToggleReplyLocked(MixerAddressState.Toggle toggleState, OscMessage msg) {
		if (!toggleState.tryUpdateCacheFromReply(msg)) {
			if (!toggleState.hasPending)
				return default;
			toggleState.clearPending();
			return new ActionPlan { effect = ActionEffect.FAIL_INVALID_REPLY };
		}
		ActionPlan plan = planApplyToggleLocked(toggleState);
		plan.clearInvalidReply = true;
		return plan;
	}

	ActionPlan planApplyContinuousLocked(MixerAddressState.Continuous cont) {
		if (!cont.hasPending)
			return default;
		if (!cont.tryApplyPending(_config.timeoutMs, out float newVal, out bool requestedIncrease))
			return new ActionPlan { effect = ActionEffect.FAIL_APPLY };
		return new ActionPlan { effect = ActionEffect.SEND_FADER, faderValue = newVal, faderIncreased = requestedIncrease };
	}

	ActionPlan planApplyToggleLocked(MixerAddressState.Toggle toggleState) {
		if (!toggleState.hasPending)
			return default;
		if (!toggleState.tryApplyPending(_config.timeoutMs, out bool nowOn))
			return new ActionPlan { effect = ActionEffect.FAIL_APPLY };
		return new ActionPlan { effect = ActionEffect.SEND_TOGGLE, toggleOn = nowOn };
	}

	ActionPlan planRefreshQueryLocked(MixerAddressState state) {
		// Dedupe: an outstanding query's reply (or its timeout) already drives the pending work.
		if (state.isQueryInFlight(_config.timeoutMs))
			return default;
		state.markQuerySent(DateTime.UtcNow);
		return new ActionPlan { effect = ActionEffect.REFRESH_QUERY };
	}

	void executePlan(string address, in ActionPlan plan) {
		if (plan.clearInvalidReply)
			statusRegister.setStatusError<StatusError.MixerController.InvalidReply>(false);

		switch (plan.effect) {
			case ActionEffect.NONE:
				return;
			case ActionEffect.FAIL_INVALID_REPLY:
				statusRegister.setStatusError<StatusError.MixerController.InvalidReply>(true);
				emitFailure(address);
				return;
			case ActionEffect.FAIL_APPLY:
				statusRegister.setStatusError<StatusError.MixerController.Network>(true);
				emitFailure(address);
				return;
			case ActionEffect.SEND_FADER:
				sendAndEmit(
					address,
					plan.faderValue,
					new Event.FaderChanged {
						address = address,
						newLevel = plan.faderValue,
						volumeIncreased = plan.faderIncreased,
					});
				return;
			case ActionEffect.SEND_TOGGLE:
				sendAndEmit(
					address,
					plan.toggleOn ? 1f : 0f,
					new Event.ToggleChanged {
						address = address,
						nowOn = plan.toggleOn,
					});
				return;
			case ActionEffect.REFRESH_QUERY:
				_ = refreshCacheAsync(address);
				return;
		}
	}

	async Task refreshCacheAsync(string address) {
		if (!await trySendAsync(address).ConfigureAwait(false))
			emitFailure(address);
	}

	void sendAndEmit(string address, object arg, Event evt) {
		_ = sendAndEmitAsync(address, arg, evt);
	}

	async Task sendAndEmitAsync(string address, object arg, Event evt) {
		if (!await trySendAsync(address, arg).ConfigureAwait(false)) {
			// The optimistic cache update no longer matches the mixer; force a re-query on the next action.
			lock (_lock) {
				if (_stateByAddress.TryGetValue(address, out MixerAddressState? state))
					state.invalidateCache();
			}
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
			statusRegister.setStatusError<StatusError.MixerController.Network>(true);
			return false;
		}
	}

	void clearMixerErrors() {
		statusRegister.setStatusError<StatusError.MixerController.Network>(false);
		statusRegister.setStatusError<StatusError.MixerController.InvalidReply>(false);
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
}
}
