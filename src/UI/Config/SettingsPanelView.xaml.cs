using System.ComponentModel;
using System.Windows;
using WindowsOscVolumeControl.UI.Config.ViewModels;
using Button = System.Windows.Controls.Button;
using UserControl = System.Windows.Controls.UserControl;
using PlacementMode = System.Windows.Controls.Primitives.PlacementMode;

namespace WindowsOscVolumeControl.UI.Config;

public partial class SettingsPanelView : UserControl {
	public SettingsPanelView() {
		InitializeComponent();
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

	void buttonDeregisterAutostartSplitMenu_Click(object sender, RoutedEventArgs e) {
		if (sender is not Button b || b.ContextMenu == null)
			return;
		b.ContextMenu.PlacementTarget = b;
		b.ContextMenu.Placement = PlacementMode.Bottom;
		b.ContextMenu.IsOpen = true;
		e.Handled = true;
	}
}

