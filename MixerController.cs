using System;
using System.Collections.Generic;
using SharpOSC;

namespace X32VolumeHijacker;

/// <summary>High-level mixer operations (per-path fader nudge, OSC toggles, <c>/info</c>) with optional per-address level cache.</summary>
public sealed class MixerController {
	public sealed class Config {
		/// <summary>Smallest allowed nudge step; also defines binding/grid decimal places via <see cref="FaderFloatUtil.BindingFractionalDigits"/>.</summary>
		public const float MinFaderStep = 0.001f;
		public const float MaxFaderStep = 10.0f;
		public const uint MinValueCacheTtlMs = 0;
		public const uint MaxValueCacheTtlMs = 10_000;

		/// <summary>How long a cached level per path is reused before querying again (ms). 0 = always query.</summary>
		public uint ValueCacheTtlMs { get; set; } = 1000;

		public Config() { }

		public Config(Config other) {
			ArgumentNullException.ThrowIfNull(other);
			ValueCacheTtlMs = other.ValueCacheTtlMs;
		}
	}

	readonly OscController _osc;
	Config _config;
	readonly Dictionary<string, (float level, DateTime sampleUtc)> _samples = new(StringComparer.Ordinal);

	public OscController Osc => _osc;

	public MixerController(OscController osc) : this(osc, new Config()) { }

	public MixerController(OscController osc, Config initialConfig) {
		_osc = osc ?? throw new ArgumentNullException(nameof(osc));
		ArgumentNullException.ThrowIfNull(initialConfig);
		_config = new Config(initialConfig);
	}

	public void ApplyConfig(Config c) {
		ArgumentNullException.ThrowIfNull(c);
		_config = new Config(c);
	}

	public void ClearFaderSampleCache() => _samples.Clear();

	public bool HasFreshFaderSample(string normalizedPath) => IsFaderSampleFreshAt(normalizedPath, DateTime.UtcNow);

	bool IsFaderSampleFreshAt(string normalizedPath, DateTime utcNow) {
		if (!_samples.TryGetValue(normalizedPath, out var entry))
			return false;
		TimeSpan ttl = TimeSpan.FromMilliseconds(_config.ValueCacheTtlMs);
		if (ttl <= TimeSpan.Zero)
			return false;
		return (utcNow - entry.sampleUtc) < ttl;
	}

	public async Task<float?> NudgeAsync(string normalizedPath, bool volumeUp, float step, float min, float max) {
		step = Math.Clamp(step, Config.MinFaderStep, Config.MaxFaderStep);
		if (!float.IsFinite(min) || !float.IsFinite(max) || min > max)
			return null;
		DateTime utcNow = DateTime.UtcNow;
		float current;
		if (IsFaderSampleFreshAt(normalizedPath, utcNow)) {
			current = _samples[normalizedPath].level;
		} else {
			float? queried = await QueryFaderAsync(normalizedPath).ConfigureAwait(false);
			if (queried == null)
				return null;
			current = queried.Value;
			_samples[normalizedPath] = (current, utcNow);
		}

		float delta = volumeUp ? step : -step;
		float newVal = current + delta;
		if (newVal < min) newVal = min;
		if (newVal > max) newVal = max;

		newVal = FaderFloatUtil.RoundToBindingDecimals(newVal);
		if (newVal < min) newVal = min;
		if (newVal > max) newVal = max;
		await SetFaderAsync(normalizedPath, newVal).ConfigureAwait(false);
		_samples[normalizedPath] = (newVal, DateTime.UtcNow);

		return newVal;
	}

	public async Task<bool?> QueryToggleAsync(string address) {
		address = OscController.NormalizeBindingAddress(address);
		float? v = await _osc.QueryFloatAsync(address, logUnmatchedArgs: false).ConfigureAwait(false);
		return v == null ? null : v.Value >= 0.5f;
	}

	public async Task SetToggleAsync(string address, bool enabled) {
		address = OscController.NormalizeBindingAddress(address);
		await _osc.SendMessageAsync(new OscMessage(address, enabled ? 1f : 0f)).ConfigureAwait(false);
	}

	public async Task<float?> QueryFaderAsync(string normalizedPath) {
		return await _osc.QueryFloatAsync(normalizedPath, logUnmatchedArgs: true).ConfigureAwait(false);
	}

	public async Task SetFaderAsync(string normalizedPath, float value) {
		await _osc.SendMessageAsync(new OscMessage(normalizedPath, value)).ConfigureAwait(false);
	}

	public async Task<(bool Ok, string Detail)> QueryInfoAsync() {
		var result = await _osc.QueryAsync("/info", OscController.FormatInfoArguments).ConfigureAwait(false);
		if (result != null)
			return (true, result);
		return (false, "No reply to /info within timeout (check IP, port, and network).");
	}

	public async Task<bool> TestConnectionAsync() {
		var (ok, _) = await QueryInfoAsync().ConfigureAwait(false);
		return ok;
	}
}
