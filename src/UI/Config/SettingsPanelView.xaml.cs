using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WindowsOscVolumeControl.UI.Config.ViewModels;
using Button = System.Windows.Controls.Button;
using PlacementMode = System.Windows.Controls.Primitives.PlacementMode;
using TextBlock = System.Windows.Controls.TextBlock;

namespace WindowsOscVolumeControl.UI.Config;

public partial class SettingsPanelView {
	readonly Dictionary<string, TextBlock> _scalarLabels;

	public SettingsPanelView() {
		InitializeComponent();
		_scalarLabels = new() {
			[nameof(ConfigWindowViewModel.oscIpText)] = OscIpLabelTextBlock,
			[nameof(ConfigWindowViewModel.oscPortText)] = OscPortLabelTextBlock,
			[nameof(ConfigWindowViewModel.queryTimeoutText)] = QueryTimeoutLabelTextBlock,
			[nameof(ConfigWindowViewModel.valueCacheTtlText)] = ValueCacheTtlLabelTextBlock,
			[nameof(ConfigWindowViewModel.osdHeightText)] = OsdHeightLabelTextBlock,
			[nameof(ConfigWindowViewModel.osdDurationText)] = OsdDurationLabelTextBlock,
			[nameof(ConfigWindowViewModel.hotkeyLongPressMsText)] = HotkeyLongPressMsLabelTextBlock,
		};
		DataContextChanged += (_, _) => hookVm();
	}

	void hookVm() {
		if (DataContext is not ConfigWindowViewModel m)
			return;
		applyVmToUi(m);
		m.PropertyChanged -= vm_PropertyChanged;
		m.PropertyChanged += vm_PropertyChanged;
	}

	void vm_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (sender is not ConfigWindowViewModel m)
			return;
		applyVmToUi(m);
	}

	void applyVmToUi(ConfigWindowViewModel m) {
		UiTextFeedbackPresenter.apply(ConfigFeedbackTextBlock, m.configFeedback);
		UiTextFeedbackPresenter.apply(InfoResultTextBox, m.infoFeedback);
		applyAutostartFeedback(new WindowsAutostart.UiFeedbackDetail(m.autostartFeedback, m.autostartFeedbackPathOrNull));
	}

	void applyAutostartFeedback(WindowsAutostart.UiFeedbackDetail detail) {
		UiTextFeedbackPresenter.apply(AutostartFeedbackTextBlock, detail.feedback);
		if (string.IsNullOrEmpty(detail.pathOrNull)) {
			AutostartFeedbackPathTextBox.Text = "";
			AutostartFeedbackPathTextBox.Visibility = Visibility.Collapsed;
			return;
		}
		AutostartFeedbackPathTextBox.Text = detail.pathOrNull;
		AutostartFeedbackPathTextBox.Visibility = Visibility.Visible;
	}

	public string formatScalarErrorsForFooter(ConfigWindowViewModel vm) {
		var lines = new List<string>();
		foreach ((string propertyName, TextBlock label) in _scalarLabels) {
			string[] messages = vm.GetErrors(propertyName).Cast<string>().ToArray();
			if (messages.Length == 0)
				continue;
			lines.Add($"{label.Text}: {string.Join(ConfigWindowViewModel.FOOTER_ERROR_SEPARATOR, messages)}");
		}
		return string.Join(ConfigWindowViewModel.FOOTER_FIELD_SEPARATOR, lines);
	}

	void buttonDeregisterAutostartSplitMenu_Click(object sender, RoutedEventArgs e) {
		if (sender is not Button b || b.ContextMenu == null)
			return;
		b.ContextMenu.PlacementTarget = b;
		b.ContextMenu.Placement = PlacementMode.Bottom;
		b.ContextMenu.IsOpen = true;
		e.Handled = true;
	}
}
