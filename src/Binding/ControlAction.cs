namespace WindowsOscVolumeControl;

public enum ControlActionValueKind {
	NONE,
	FLOAT,
	BOOL,
}

/// <summary>One control assignment: hotkey gesture + polymorphic action payload.</summary>
public abstract class ControlAction {
	public HotkeyGesture hotkey { get; set; } = HotkeyGesture.None;

	/// <summary>When true, the action runs only after the configured long-press duration; when false, timing follows global short-press rules.</summary>
	public bool longPress { get; set; }

	public abstract string name { get; }
	public abstract ControlActionValueKind valueKind { get; }
	public abstract ControlAction clone();
}

public abstract class ControlActionContinuousAbstract : ControlAction {
	public virtual bool needsCurrentWire => true;

	/// <summary>Captured from typed numeric string in UI; drives OSD fractional digits on the binding.</summary>
	public int fractionalDigits { get; set; }
}

public abstract class ControlActionToggleAbstract : ControlAction { }

public sealed class ControlActionContinuousSet : ControlActionContinuousAbstract {
	public float value { get; set; }

	public override string name => "Set value";
	public override ControlActionValueKind valueKind => ControlActionValueKind.FLOAT;
	public override bool needsCurrentWire => false;

	public override ControlAction clone() => new ControlActionContinuousSet {
		hotkey = hotkey,
		longPress = longPress,
		value = ContinuousFloatUtil.RoundToBindingDecimals(value),
		fractionalDigits = fractionalDigits,
	};
}

public sealed class ControlActionContinuousDelta : ControlActionContinuousAbstract {
	public float delta { get; set; }

	public override string name => "Apply delta";
	public override ControlActionValueKind valueKind => ControlActionValueKind.FLOAT;

	public override ControlAction clone() => new ControlActionContinuousDelta {
		hotkey = hotkey,
		longPress = longPress,
		delta = ContinuousFloatUtil.RoundToBindingDecimals(delta),
		fractionalDigits = fractionalDigits,
	};
}

public sealed class ControlActionContinuousRawDelta : ControlActionContinuousAbstract {
	public float delta { get; set; }

	public override string name => "Apply raw delta";
	public override ControlActionValueKind valueKind => ControlActionValueKind.FLOAT;

	public override ControlAction clone() => new ControlActionContinuousRawDelta {
		hotkey = hotkey,
		longPress = longPress,
		delta = ContinuousFloatUtil.RoundToBindingDecimals(delta),
		fractionalDigits = fractionalDigits,
	};
}

public sealed class ControlActionToggleSet : ControlActionToggleAbstract {
	public bool on { get; set; }

	public override string name => "Set state";
	public override ControlActionValueKind valueKind => ControlActionValueKind.BOOL;

	public override ControlAction clone() => new ControlActionToggleSet {
		hotkey = hotkey,
		longPress = longPress,
		on = on,
	};
}

public sealed class ControlActionToggleFlip : ControlActionToggleAbstract {
	public override string name => "Toggle";
	public override ControlActionValueKind valueKind => ControlActionValueKind.NONE;

	public override ControlAction clone() => new ControlActionToggleFlip { hotkey = hotkey, longPress = longPress };
}
