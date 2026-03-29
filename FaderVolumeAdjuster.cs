using X32VolumeHijacker;

/// <summary>Applies volume-up/down steps to the mixer fader, skipping <see cref="OscController.QueryFaderAsync"/> when a cached level is still within <see cref="Config.FaderVolumeCacheTtlMs"/>.</summary>
public sealed class FaderVolumeAdjuster {
	/// <summary>Volume step and fader sample cache TTL persisted via <see cref="ConfigStore"/>.</summary>
	public sealed class Config {
		public float VolumeStep { get; set; } = ConfigStore.DefaultVolumeStep;
		/// <summary>How long a cached fader level is reused before <see cref="OscController.QueryFaderAsync"/> runs again (milliseconds). 0 = always query.</summary>
		public uint FaderVolumeCacheTtlMs { get; set; } = ConfigStore.DefaultFaderVolumeCacheTtlMs;

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

	public FaderVolumeAdjuster(OscController osc) : this(osc, new Config()) { }

	public FaderVolumeAdjuster(OscController osc, Config initialConfig) {
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

	/// <summary>True when <see cref="NudgeAsync"/> will use the cached level and skip <see cref="OscController.QueryFaderAsync"/>.</summary>
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
			float? queried = await _osc.QueryFaderAsync().ConfigureAwait(false);
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

		await _osc.SetFaderAsync(newVal).ConfigureAwait(false);
		_lastLevel = newVal;
		_lastSampleUtc = DateTime.UtcNow;

		return newVal;
	}
}
