using System.Windows;

namespace WindowsOscVolumeControl.UI.Config;

public partial class ScalarFieldToolTip {
	public static readonly DependencyProperty helpTextProperty = DependencyProperty.Register(
		nameof(helpText),
		typeof(string),
		typeof(ScalarFieldToolTip),
		new PropertyMetadata(""));

	public string helpText {
		get => (string)GetValue(helpTextProperty);
		set => SetValue(helpTextProperty, value);
	}

	public ScalarFieldToolTip() => InitializeComponent();
}
