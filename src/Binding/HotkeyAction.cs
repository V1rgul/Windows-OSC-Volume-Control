namespace WindowsOscVolumeControl;

public enum HotkeyActionValueKind {
	NONE,
	FLOAT,
	BOOL,
}

/// <summary>One hotkey assignment: key + polymorphic action payload.</summary>
public abstract class HotkeyAction {
	public HotkeyGesture hotkey { get; set; } = HotkeyGesture.None;

	/// <summary>When true, the action runs only after the configured long-press duration; when false, timing follows global short-press rules.</summary>
	public bool longPress { get; set; }

	public abstract string name { get; }
	public abstract HotkeyActionValueKind valueKind { get; }
	public abstract HotkeyAction clone();
}

public abstract class HotkeyActionFaderAbstract : HotkeyAction { }

public abstract class HotkeyActionToggleAbstract : HotkeyAction { }

public sealed class HotkeyActionFaderSet : HotkeyActionFaderAbstract {
	public float value { get; set; }

	public override string name => "Set value";
	public override HotkeyActionValueKind valueKind => HotkeyActionValueKind.FLOAT;

	public override HotkeyAction clone() => new HotkeyActionFaderSet {
		hotkey = hotkey,
		longPress = longPress,
		value = FaderFloatUtil.RoundToBindingDecimals(value),
	};
}

public sealed class HotkeyActionFaderDelta : HotkeyActionFaderAbstract {
	public float delta { get; set; }

	public override string name => "Apply delta";
	public override HotkeyActionValueKind valueKind => HotkeyActionValueKind.FLOAT;

	public override HotkeyAction clone() => new HotkeyActionFaderDelta {
		hotkey = hotkey,
		longPress = longPress,
		delta = FaderFloatUtil.RoundToBindingDecimals(delta),
	};
}

public sealed class HotkeyActionToggleSet : HotkeyActionToggleAbstract {
	public bool on { get; set; }

	public override string name => "Set state";
	public override HotkeyActionValueKind valueKind => HotkeyActionValueKind.BOOL;

	public override HotkeyAction clone() => new HotkeyActionToggleSet {
		hotkey = hotkey,
		longPress = longPress,
		on = on,
	};
}

public sealed class HotkeyActionToggleFlip : HotkeyActionToggleAbstract {
	public override string name => "Toggle";
	public override HotkeyActionValueKind valueKind => HotkeyActionValueKind.NONE;

	public override HotkeyAction clone() => new HotkeyActionToggleFlip { hotkey = hotkey, longPress = longPress };
}
