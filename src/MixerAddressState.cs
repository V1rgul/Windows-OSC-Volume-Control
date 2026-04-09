using System;
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

	public sealed class Fader : MixerAddressState {
		float? cachedValue;
		float accumulatedDelta;
		float min;
		float max;

		public override bool hasPending => accumulatedDelta != 0f;
		public override void clearPending() => accumulatedDelta = 0f;
		protected override object? getCachedValue() => cachedValue;

		public float? tryGetCachedValueTyped(uint valueCacheTtlMs) =>
			(float?)tryGetCachedValue(valueCacheTtlMs);

		public float getCachedValueTyped() => (float)getCachedValue()!;

		public void updateCache(float value) {
			cachedValue = value;
			cachedValueUtc = DateTime.UtcNow;
		}

		public bool tryUpdateCacheFromReply(OscMessage msg) {
			if (msg.Arguments.Count == 0 || msg.Arguments[0] is not float f)
				return false;
			updateCache(f);
			return true;
		}

		public bool tryApplyPending(uint timeoutMs, out float newVal) {
			newVal = 0f;
			if (!hasPending)
				return false;
			if (isPendingExpired(timeoutMs)) {
				clearPending();
				return false;
			}
			if (cachedValue == null)
				return false;

			float current = cachedValue.Value;
			newVal = clampAndRound(current + accumulatedDelta, min, max);
			updateCache(newVal);
			clearPending();
			return true;
		}

		public void addDelta(float delta, float minValue, float maxValue, DateTime nowUtc) {
			accumulatedDelta += delta;
			min = minValue;
			max = maxValue;
			lastPendingUpdateUtc = nowUtc;
		}

		static float clampAndRound(float value, float minValue, float maxValue) {
			float newVal = Math.Clamp(value, minValue, maxValue);
			newVal = FaderFloatUtil.RoundToBindingDecimals(newVal);
			return Math.Clamp(newVal, minValue, maxValue);
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
