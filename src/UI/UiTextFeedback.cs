namespace WindowsOscVolumeControl.UI;

/// <summary>Visual role for a line of config-window feedback text.</summary>
public enum UiTextFeedbackKind {
	DEFAULT,
	SUCCESS,
	ERROR,
	WARNING,
}

/// <summary>Text plus <see cref="UiTextFeedbackKind"/> for applying to a config feedback <see cref="System.Windows.Controls.TextBlock"/>.</summary>
public readonly record struct UiTextFeedback(string text, UiTextFeedbackKind kind);
