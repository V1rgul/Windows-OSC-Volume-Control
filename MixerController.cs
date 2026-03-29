using SharpOSC;

namespace X32VolumeHijacker;

/// <summary>High-level mixer operations (fader, mute, toggles, <c>/info</c>) and volume-key nudging with optional fader level cache.</summary>
public sealed class MixerController {
	/// <summary>Volume step and fader sample cache TTL persisted via <see cref="ConfigStore"/>.</summary>
	public sealed class Config {
		public const float MinVolumeStep = 0.001f;
		public const float MaxVolumeStep = 0.5f;
		public const uint MinFaderVolumeCacheTtlMs = 0;
		public const uint MaxFaderVolumeCacheTtlMs = 10_000;

		public float VolumeStep { get; set; } = 0.02f;
		/// <summary>How long a cached fader level is reused before <see cref="QueryFaderAsync"/> runs again (milliseconds). 0 = always query.</summary>
		public uint FaderVolumeCacheTtlMs { get; set; } = 1000;

		public Config() { }

		public Config(Config other) {
			ArgumentNullException.ThrowIfNull(other);
			VolumeStep = other.VolumeStep;
			FaderVolumeCacheTtlMs = other.FaderVolumeCacheTtlMs;
		}
	}

	readonly OscController _osc;
	Config _config;
	DateTime? _lastSampleUtc;
	float _lastLevel;

	public OscController Osc => _osc;

	public MixerController(OscController osc) : this(osc, new Config()) { }

	public MixerController(OscController osc, Config initialConfig) {
		_osc = osc ?? throw new ArgumentNullException(nameof(osc));
		ArgumentNullException.ThrowIfNull(initialConfig);
		_config = new Config(initialConfig);
	}

	/// <summary>Replaces persisted fader settings with a copy of <paramref name="c"/>.</summary>
	public void ApplyConfig(Config c) {
		ArgumentNullException.ThrowIfNull(c);
		_config = new Config(c);
	}

	/// <summary>Clears the last fader sample so the next nudge queries the desk. Call after OSC connection settings change.</summary>
	public void ClearFaderSampleCache() => _lastSampleUtc = null;

	/// <summary>True when <see cref="NudgeAsync"/> will use the cached level and skip <see cref="QueryFaderAsync"/>.</summary>
	public bool HasFreshFaderSample => IsFaderSampleFreshAt(DateTime.UtcNow);

	bool IsFaderSampleFreshAt(DateTime utcNow) {
		if (_lastSampleUtc == null)
			return false;
		TimeSpan ttl = TimeSpan.FromMilliseconds(_config.FaderVolumeCacheTtlMs);
		if (ttl <= TimeSpan.Zero)
			return false;
		return (utcNow - _lastSampleUtc.Value) < ttl;
	}

	/// <returns>New fader level, or <c>null</c> if the desk did not respond.</returns>
	public async Task<float?> NudgeAsync(KeyboardHook.VolumeKey key) {
		bool volumeUp = key switch {
			KeyboardHook.VolumeKey.UP => true,
			KeyboardHook.VolumeKey.DOWN => false,
			_ => throw new ArgumentOutOfRangeException(nameof(key), key, "Use UP or DOWN."),
		};
		DateTime utcNow = DateTime.UtcNow;
		float current;
		if (IsFaderSampleFreshAt(utcNow)) {
			current = _lastLevel;
		} else {
			float? queried = await QueryFaderAsync().ConfigureAwait(false);
			if (queried == null)
				return null;
			current = queried.Value;
			_lastLevel = current;
			_lastSampleUtc = utcNow;
		}

		float step = _config.VolumeStep;
		float delta = volumeUp ? step : -step;
		float newVal = current + delta;
		if (newVal < 0f) newVal = 0f;
		if (newVal > 1f) newVal = 1f;

		await SetFaderAsync(newVal).ConfigureAwait(false);
		_lastLevel = newVal;
		_lastSampleUtc = DateTime.UtcNow;

		return newVal;
	}

	public async Task<bool?> QueryMuteAsync() {
		bool? on = await QueryToggleAsync(MixOnOscAddress()).ConfigureAwait(false);
		return on == null ? null : !on.Value;
	}

	public async Task SetMuteAsync(bool muted) {
		await SetToggleAsync(MixOnOscAddress(), !muted).ConfigureAwait(false);
	}

	string MixOnOscAddress() => _osc.CachedMixOnPath ?? OscController.FaderPathToMixOnPath(_osc.RawFaderAddress);

	public async Task<bool?> QueryToggleAsync(string address) {
		address = OscController.NormalizeOscAddress(address);
		float? v = await _osc.QueryFloatAsync(address, logUnmatchedArgs: false).ConfigureAwait(false);
		return v == null ? null : v.Value >= 0.5f;
	}

	public async Task SetToggleAsync(string address, bool enabled) {
		address = OscController.NormalizeOscAddress(address);
		await _osc.SendMessageAsync(new OscMessage(address, enabled ? 1f : 0f)).ConfigureAwait(false);
	}

	public async Task<float?> QueryFaderAsync() {
		return await _osc.QueryFloatAsync(_osc.NormalizedFaderPath, logUnmatchedArgs: true).ConfigureAwait(false);
	}

	public async Task SetFaderAsync(float value) {
		await _osc.SendMessageAsync(new OscMessage(_osc.NormalizedFaderPath, value)).ConfigureAwait(false);
	}

	/// <summary>Sends <c>/info</c> and returns the first matching reply (X32 desk identity / firmware strings).</summary>
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
