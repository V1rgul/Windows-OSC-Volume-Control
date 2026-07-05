using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Result;
using WindowsOscVolumeControl.Diagnostics;
using WindowsOscVolumeControl.Config;

namespace WindowsOscVolumeControl.Binding;

/// <summary>Short- vs long-press slot lists for one <see cref="HotkeyGesture"/>.</summary>
public readonly struct HotkeyDispatchTargets {
	public IReadOnlyList<BindingManager.Slot> shortPressSlots { get; init; }
	public IReadOnlyList<BindingManager.Slot> longPressSlots { get; init; }

	public bool hasAny => shortPressSlots.Count > 0 || longPressSlots.Count > 0;
}

/// <summary>Runtime OSC bindings and hotkey → slot map built from tray configuration.</summary>
public sealed class BindingManager {
	internal static BindingAbstract cloneBinding(BindingAbstract b) => b switch {
		BindingLinear f => new BindingLinear(f),
		BindingLinf x => new BindingLinf(x),
		BindingLogf g => new BindingLogf(g),
		BindingLevel l => new BindingLevel(l),
		BindingToggle t => new BindingToggle(t),
		_ => throw new InvalidOperationException("Unknown binding type: " + b.GetType().Name),
	};

	/// <summary>OSC bindings; persisted via <see cref="ConfigStore"/>.</summary>
	public sealed class Config {
		static readonly char[] _oscNameForbiddenChars = [' ', '#', '*', ',', '?', '[', ']', '{', '}'];

		public readonly record struct FloatFieldValue(float value, int fractionalDigits);

		public Config() { }

		public Config(Config from) {
			ArgumentNullException.ThrowIfNull(from);
			bindings = from.bindings.Select(cloneBinding).ToList();
		}

		public static BindingLinear createDefaultLinearBinding() => new() {
			name = "MAIN",
			address = "/main/st/mix/fader",
			minimum = 0f,
			maximum = 1f,
			actions = [
				new ControlActionContinuousDelta {
					hotkey = new HotkeyGesture { keyCode = HotkeyGesture.VK_VOLUME_DOWN },
					delta = -0.02f,
				},
				new ControlActionContinuousDelta {
					hotkey = new HotkeyGesture { keyCode = HotkeyGesture.VK_VOLUME_UP },
					delta = 0.02f,
				},
			],
		};

		public static BindingToggle createDefaultToggleBinding() => new() {
			name = "MAIN",
			address = "/main/st/mix/on",
			actions = [
				new ControlActionToggleFlip {
					hotkey = new HotkeyGesture { keyCode = HotkeyGesture.VK_VOLUME_MUTE },
				},
			],
		};

		public List<BindingAbstract> bindings { get; set; } = [createDefaultLinearBinding(), createDefaultToggleBinding()];

		public static Result<string> parseBindingNameField(string? text) {
			return ConfigParseUtil.parseRequiredText(text);
		}

		public static Result<string> parseOscAddressField(string? text) {
			string address = (text ?? "").Trim();
			if (address.Length == 0)
				return new ResultError.Generic.Parsing { message = "OSC address is required." };
			if (!address.StartsWith('/'))
				return new ResultError.Generic.Parsing { message = "OSC address must start with '/'." };
			if (address.Length == 1)
				return new ResultError.Generic.Parsing { message = "OSC address must include at least one path part after '/'." };
			if (address.EndsWith('/'))
				return new ResultError.Generic.Parsing { message = "OSC address must not end with '/'." };
			if (address.Contains("//", StringComparison.Ordinal))
				return new ResultError.Generic.Parsing { message = "OSC address must not contain empty path parts." };

			foreach (char c in address) {
				if (c == '/')
					continue;
				if (c < 0x21 || c > 0x7e)
					return new ResultError.Generic.Parsing { message = "OSC address parts must use printable ASCII characters." };
				if (_oscNameForbiddenChars.Contains(c))
					return new ResultError.Generic.Parsing { message = "OSC address parts must not contain space, #, *, comma, ?, [], or {}." };
			}
			return address;
		}

		public static Result<string?> parseUnitField(string? text) {
			return ConfigParseUtil.parseOptionalText(text);
		}

		public static Result<FloatFieldValue> parseContinuousFloatField(string? text) {
			Result<ConfigFloatParseValue> parsed = ConfigParseUtil.parseFiniteFloatWithDigits(text);
			if (parsed.isError)
				return parsed.errors;
			return new FloatFieldValue(parsed.value.value, parsed.value.fractionalDigits);
		}
	}

	/// <summary>One hotkey’s target binding and action.</summary>
	public readonly struct Slot {
		public BindingAbstract binding { get; }
		public ControlAction action { get; }

		public Slot(BindingAbstract binding, ControlAction action) {
			this.binding = binding;
			this.action = action;
		}
	}

	sealed class GestureBuckets {
		public readonly List<Slot> shortPress = [];
		public readonly List<Slot> longPress = [];
	}

	volatile FrozenDictionary<HotkeyGesture, HotkeyDispatchTargets> _byGesture = FrozenDictionary<HotkeyGesture, HotkeyDispatchTargets>.Empty;
	volatile FrozenDictionary<string, BindingFloatAbstract> _floatByAddress = FrozenDictionary<string, BindingFloatAbstract>.Empty;
	volatile FrozenDictionary<string, BindingToggle> _toggleByAddress = FrozenDictionary<string, BindingToggle>.Empty;
	volatile int[] _boundMainKeyCodes = [];

	/// <summary>Distinct main-key VK codes of all bound gestures; feeds the keyboard hook's lock-free fast path.</summary>
	internal IReadOnlyCollection<int> boundMainKeyCodes => _boundMainKeyCodes;

	/// <summary>Rebuilds the snapshot from config. Same gesture may appear in multiple rows and in both short and long buckets.</summary>
	internal void rebuildFromConfig(IEnumerable<BindingAbstract> bindings) {
		var merge = new Dictionary<HotkeyGesture, GestureBuckets>();
		var floatByAddress = new Dictionary<string, BindingFloatAbstract>(StringComparer.Ordinal);
		var toggleByAddress = new Dictionary<string, BindingToggle>(StringComparer.Ordinal);
		foreach (BindingAbstract b in bindings) {
			BindingAbstract row = cloneBinding(b);
			switch (row) {
				case BindingFloatAbstract bf:
					_ = floatByAddress.TryAdd(bf.address, bf);
					break;
				case BindingToggle bt:
					_ = toggleByAddress.TryAdd(bt.address, bt);
					break;
			}
			foreach (ControlAction ha in row.actions) {
				if (ha.hotkey.isNone)
					continue;
				HotkeyGesture k = HotkeyUtil.normalize(ha.hotkey);
				if (k.isNone)
					continue;
				if (!merge.TryGetValue(k, out GestureBuckets? buckets)) {
					buckets = new GestureBuckets();
					merge[k] = buckets;
				}
				var slot = new Slot(row, ha.clone());
				if (ha.longPress)
					buckets.longPress.Add(slot);
				else
					buckets.shortPress.Add(slot);
			}
		}

		var frozenMap = new Dictionary<HotkeyGesture, HotkeyDispatchTargets>();
		foreach ((HotkeyGesture g, GestureBuckets b) in merge) {
			HotkeyDispatchTargets t = new() {
				shortPressSlots = b.shortPress.Count > 0 ? b.shortPress.ToArray() : Array.Empty<Slot>(),
				longPressSlots = b.longPress.Count > 0 ? b.longPress.ToArray() : Array.Empty<Slot>(),
			};
			if (t.hasAny)
				frozenMap[g] = t;
		}

		_boundMainKeyCodes = frozenMap.Keys.Select(static g => g.keyCode).Distinct().ToArray();
		_floatByAddress = floatByAddress.ToFrozenDictionary(StringComparer.Ordinal);
		_toggleByAddress = toggleByAddress.ToFrozenDictionary(StringComparer.Ordinal);
		_byGesture = frozenMap.ToFrozenDictionary();
	}

	internal bool tryGetDispatchTargets(HotkeyGesture hotkey, out HotkeyDispatchTargets targets) {
		hotkey = HotkeyUtil.normalize(hotkey);
		return _byGesture.TryGetValue(hotkey, out targets);
	}

	internal bool tryGetFloatBindingByAddress(string address, [NotNullWhen(true)] out BindingFloatAbstract? binding) =>
		_floatByAddress.TryGetValue(address, out binding);

	internal bool tryGetToggleBindingByAddress(string address, [NotNullWhen(true)] out BindingToggle? binding) =>
		_toggleByAddress.TryGetValue(address, out binding);
}
