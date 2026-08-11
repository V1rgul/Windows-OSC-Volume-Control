using System.Globalization;
using System.Net;
using Result;
using WindowsOscVolumeControl.Input;
using WindowsOscVolumeControl.Osc;
using WindowsOscVolumeControl.UI.Config.ViewModels;
using WindowsOscVolumeControl.UI.Osd;

namespace WindowsOscVolumeControl.UI.Config;

delegate ref T ConfigPath<T>(AppConfig cfg);

abstract class ResultBridge {
	public abstract string text { get; }
	public abstract IResult get();
	public abstract void parse(string? text);
	public abstract void take(AppConfig cfg);
	public abstract void put(AppConfig cfg);
}

sealed class ResultBridge<T> : ResultBridge {
	readonly ConfigPath<T> _path;
	readonly Func<string?, Result<T>> _parse;
	Result<T> _result;
	string _text;

	public ResultBridge(ConfigPath<T> path, Func<string?, Result<T>> parse) {
		_path = path;
		_parse = parse;
		_result = _parse(null);
		_text = "";
	}

	public override string text => _text;

	public override IResult get() => _result;

	public override void parse(string? text) {
		_text = text ?? "";
		_result = _parse(text);
	}

	public override void take(AppConfig cfg) {
		T value = _path(cfg);
		_result = value;
		_text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
	}

	public override void put(AppConfig cfg) {
		if (_result.isSuccess)
			_path(cfg) = _result.value;
	}
}

internal sealed class ResultsRegistry {
	static readonly (string name, Func<ResultBridge> create)[] fieldFactories = [
		(nameof(ConfigWindowViewModel.oscIpText),
			static () => new ResultBridge<IPAddress>(
				static c => ref c.oscTransport.address, OscTransport.Config.parseIpField)),
		(nameof(ConfigWindowViewModel.oscPortText),
			static () => new ResultBridge<int>(
				static c => ref c.oscTransport.port, OscTransport.Config.parsePortField)),
		(nameof(ConfigWindowViewModel.queryTimeoutText),
			static () => new ResultBridge<uint>(
				static c => ref c.mixer.timeoutMs, MixerController.Config.parseTimeoutMs)),
		(nameof(ConfigWindowViewModel.valueCacheTtlText),
			static () => new ResultBridge<uint>(
				static c => ref c.mixer.ValueCacheTtlMs, MixerController.Config.parseValueCacheTtlMs)),
		(nameof(ConfigWindowViewModel.osdHeightText),
			static () => new ResultBridge<int>(
				static c => ref c.osd.heightDip, OSDController.Config.parseHeightDip)),
		(nameof(ConfigWindowViewModel.osdDurationText),
			static () => new ResultBridge<uint>(
				static c => ref c.osd.DisplayDurationMs, OSDController.Config.parseDisplayDurationMs)),
		(nameof(ConfigWindowViewModel.hotkeyLongPressMsText),
			static () => new ResultBridge<uint>(
				static c => ref c.keyboardHook.longPressDurationMs, KeyboardHook.Config.parseLongPressMs)),
	];

	readonly Dictionary<string, ResultBridge> _fields;

	public ResultsRegistry() {
		_fields = new Dictionary<string, ResultBridge>(fieldFactories.Length);
		foreach ((string name, Func<ResultBridge> create) in fieldFactories)
			_fields[name] = create();
	}

	public IReadOnlyCollection<string> propertyNames => _fields.Keys;

	public string text(string propertyName) => _fields[propertyName].text;

	public void parse(string propertyName, string? text) => _fields[propertyName].parse(text);

	public bool tryGet(string propertyName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ResultBridge? bridge) =>
		_fields.TryGetValue(propertyName, out bridge);

	public bool anyError() {
		foreach (ResultBridge bridge in _fields.Values) {
			if (bridge.get().isError)
				return true;
		}
		return false;
	}

	public void take(AppConfig cfg) {
		foreach (ResultBridge bridge in _fields.Values)
			bridge.take(cfg);
	}

	public void put(AppConfig cfg) {
		foreach (ResultBridge bridge in _fields.Values)
			bridge.put(cfg);
	}
}
