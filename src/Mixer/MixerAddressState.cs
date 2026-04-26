using System.Collections.Generic;
using SharpOSC;

namespace WindowsOscVolumeControl;

abstract class MixerAddressState {
	protected DateTime cachedValueUtc;
	protected DateTime lastPendingUpdateUtc;
	DateTime? lastQuerySentUtc;
	TimeSpan? lastQueryLatency;

	public object? tryGetCachedValue(uint valueCacheTtlMs) =>
		isCacheFresh(valueCacheTtlMs) ? getCachedValue() : null;

	public bool isPendingExpired(uint timeoutMs) =>
		(DateTime.UtcNow - lastPendingUpdateUtc) > TimeSpan.FromMilliseconds(timeoutMs);

	public bool isCacheFresh(uint valueCacheTtlMs) =>
		(DateTime.UtcNow - cachedValueUtc) < TimeSpan.FromMilliseconds(valueCacheTtlMs);

	public void markQuerySent(DateTime nowUtc) => lastQuerySentUtc = nowUtc;

	public bool tryRecordQueryLatency(DateTime nowUtc) {
		if (lastQuerySentUtc == null)
			return false;

		DateTime sentUtc = lastQuerySentUtc.Value;
		lastQueryLatency = nowUtc >= sentUtc ? nowUtc - sentUtc : TimeSpan.Zero;
		lastQuerySentUtc = null;
		return true;
	}

	public TimeSpan? tryGetLastQueryLatency() => lastQueryLatency;

	public abstract bool hasPending { get; }
	public abstract void clearPending();
	protected abstract object? getCachedValue();

	public sealed class Continuous : MixerAddressState {
		float? cachedWire;
		readonly Queue<ControlActionContinuousAbstract> _pending = new();
		BindingFloatAbstract? _applier;

		public override bool hasPending => _pending.Count > 0;
		public override void clearPending() {
			_pending.Clear();
			_applier = null;
		}

		protected override object? getCachedValue() => cachedWire;

		public float? tryGetCachedValueTyped(uint valueCacheTtlMs) =>
			(float?)tryGetCachedValue(valueCacheTtlMs);

		public float getCachedWireTyped() => (float)getCachedValue()!;

		public void updateCache(float wire) {
			cachedWire = wire;
			cachedValueUtc = DateTime.UtcNow;
		}

		public bool tryUpdateCacheFromReply(OscMessage msg) {
			if (msg.Arguments.Count == 0 || msg.Arguments[0] is not float f)
				return false;
			updateCache(f);
			return true;
		}

		public void enqueue(ControlActionContinuousAbstract action, BindingFloatAbstract applier, DateTime nowUtc) {
			_applier = applier;
			_pending.Enqueue(action);
			lastPendingUpdateUtc = nowUtc;
		}

		public void prepareImmediateSet(BindingFloatAbstract applier, DateTime nowUtc) {
			clearPending();
			_applier = applier;
			lastPendingUpdateUtc = nowUtc;
		}

		public bool tryApplyPending(uint timeoutMs, out float newWire, out bool requestedIncrease) {
			newWire = 0f;
			requestedIncrease = true;
			if (!hasPending || _applier == null)
				return false;
			if (isPendingExpired(timeoutMs)) {
				clearPending();
				return false;
			}
			if (cachedWire == null)
				return false;

			float start = cachedWire.Value;
			float wire = start;
			BindingFloatAbstract applier = _applier;
			foreach (ControlActionContinuousAbstract a in _pending) {
				wire = applier.applyContinuousAction(a, wire);
			}
			requestedIncrease = wire >= start;
			newWire = wire;
			_pending.Clear();
			_applier = null;
			updateCache(newWire);
			return true;
		}
	}

	public sealed class Toggle : MixerAddressState {
		bool? cachedValue;
		bool isPending;

		public override bool hasPending => isPending;
		public override void clearPending() => isPending = false;
		protected override object? getCachedValue() => cachedValue;

		public bool? tryGetCachedValueTyped(uint valueCacheTtlMs) =>
			(bool?)tryGetCachedValue(valueCacheTtlMs);

		public void updateCache(bool value) {
			cachedValue = value;
			cachedValueUtc = DateTime.UtcNow;
		}

		public bool tryUpdateCacheFromReply(OscMessage msg) {
			if (msg.Arguments.Count == 0 || msg.Arguments[0] is not int i)
				return false;
			updateCache(i != 0);
			return true;
		}

		public bool tryApplyPending(uint timeoutMs, out bool nowOn) {
			nowOn = false;
			if (!hasPending)
				return false;
			if (isPendingExpired(timeoutMs)) {
				clearPending();
				return false;
			}
			if (cachedValue == null)
				return false;

			bool state = cachedValue.Value;
			bool newState = !state;
			updateCache(newState);
			clearPending();
			nowOn = newState;
			return true;
		}

		public void markPending(DateTime nowUtc) {
			isPending = true;
			lastPendingUpdateUtc = nowUtc;
		}
	}

	public sealed class Info : MixerAddressState {
		public override bool hasPending => false;
		public override void clearPending() { }
		protected override object? getCachedValue() => null;
	}
}
