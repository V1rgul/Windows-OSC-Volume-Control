using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WindowsOscVolumeControl;

public abstract class ObservableObject : INotifyPropertyChanged {
	public event PropertyChangedEventHandler? PropertyChanged;

	protected bool setProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
		if (EqualityComparer<T>.Default.Equals(field, value))
			return false;

		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		return true;
	}

	protected void raisePropertyChanged([CallerMemberName] string? propertyName = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class FaderBindingEditor : ObservableObject {
	string _name = "";
	string _address = "";
	string _step = "0.02";
	string _minimum = "0";
	string _maximum = "1";
	HotkeyGesture _hotkeyMinus = HotkeyGesture.None;
	HotkeyGesture _hotkeyPlus = HotkeyGesture.None;

	public string name {
		get => _name;
		set {
			if (setProperty(ref _name, value ?? "")) {
				raisePropertyChanged(nameof(headerText));
			}
		}
	}

	public string address {
		get => _address;
		set {
			if (setProperty(ref _address, value ?? "")) {
				raisePropertyChanged(nameof(headerText));
			}
		}
	}

	public string step {
		get => _step;
		set => setProperty(ref _step, value ?? "");
	}

	public string minimum {
		get => _minimum;
		set => setProperty(ref _minimum, value ?? "");
	}

	public string maximum {
		get => _maximum;
		set => setProperty(ref _maximum, value ?? "");
	}

	public HotkeyGesture hotkeyMinus {
		get => _hotkeyMinus;
		set {
			if (setProperty(ref _hotkeyMinus, HotkeyUtil.normalize(value)))
				raisePropertyChanged(nameof(hotkeyMinusText));
		}
	}

	public HotkeyGesture hotkeyPlus {
		get => _hotkeyPlus;
		set {
			if (setProperty(ref _hotkeyPlus, HotkeyUtil.normalize(value)))
				raisePropertyChanged(nameof(hotkeyPlusText));
		}
	}

	public string hotkeyMinusText => hotkeyMinus.isNone ? "Set hotkey −" : HotkeyUtil.format(hotkeyMinus);
	public string hotkeyPlusText => hotkeyPlus.isNone ? "Set hotkey +" : HotkeyUtil.format(hotkeyPlus);
	public string headerText => string.IsNullOrWhiteSpace(name) ? (string.IsNullOrWhiteSpace(address) ? "New fader binding" : address.Trim()) : name.Trim();

	public static FaderBindingEditor fromBinding(BindingFader binding) => new() {
		name = binding.name,
		address = binding.address,
		step = FaderFloatUtil.FormatGridFloat(binding.step),
		minimum = FaderFloatUtil.FormatGridFloat(binding.minimum),
		maximum = FaderFloatUtil.FormatGridFloat(binding.maximum),
		hotkeyMinus = binding.hotkeyMinus,
		hotkeyPlus = binding.hotkeyPlus,
	};
}

public sealed class ToggleBindingEditor : ObservableObject {
	string _name = "";
	string _address = "";
	HotkeyGesture _hotkey = HotkeyGesture.None;

	public string name {
		get => _name;
		set {
			if (setProperty(ref _name, value ?? "")) {
				raisePropertyChanged(nameof(headerText));
			}
		}
	}

	public string address {
		get => _address;
		set {
			if (setProperty(ref _address, value ?? "")) {
				raisePropertyChanged(nameof(headerText));
			}
		}
	}

	public HotkeyGesture hotkey {
		get => _hotkey;
		set {
			if (setProperty(ref _hotkey, HotkeyUtil.normalize(value)))
				raisePropertyChanged(nameof(hotkeyText));
		}
	}

	public string hotkeyText => hotkey.isNone ? "Set hotkey" : HotkeyUtil.format(hotkey);
	public string headerText => string.IsNullOrWhiteSpace(name) ? (string.IsNullOrWhiteSpace(address) ? "New toggle binding" : address.Trim()) : name.Trim();

	public static ToggleBindingEditor fromBinding(BindingToggle binding) => new() {
		name = binding.name,
		address = binding.address,
		hotkey = binding.hotkey,
	};
}
