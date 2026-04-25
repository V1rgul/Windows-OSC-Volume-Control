using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WindowsOscVolumeControl;

/// <summary>Maps <see cref="UiTextFeedback"/> to WPF text controls.</summary>
static class UiTextFeedbackPresenter {
	/// <summary>Uses <paramref name="primary"/>.text; kind is the worst among <paramref name="primary"/> and <paramref name="alsoForKindOnly"/> (ERROR &gt; WARNING &gt; SUCCESS &gt; DEFAULT).</summary>
	public static UiTextFeedback mergeWithWorstKind(UiTextFeedback primary, params UiTextFeedback[] alsoForKindOnly) {
		UiTextFeedbackKind k = primary.kind;
		foreach (UiTextFeedback o in alsoForKindOnly)
			k = worstKind(k, o.kind);
		return new UiTextFeedback(primary.text, k);
	}

	static UiTextFeedbackKind worstKind(UiTextFeedbackKind a, UiTextFeedbackKind b) =>
		kindRank(a) >= kindRank(b) ? a : b;

	static int kindRank(UiTextFeedbackKind k) => k switch {
		UiTextFeedbackKind.ERROR => 3,
		UiTextFeedbackKind.WARNING => 2,
		UiTextFeedbackKind.SUCCESS => 1,
		_ => 0,
	};

	public static void apply(TextBlock block, UiTextFeedback feedback) {
		block.Text = feedback.text;
		applyKind(block, feedback.kind);
	}

	public static void apply(System.Windows.Controls.TextBox box, UiTextFeedback feedback) {
		box.Text = feedback.text;
		applyKind(box, feedback.kind);
	}

	static System.Windows.Media.Brush tryGetThemeBrush(FrameworkElement element, string key, System.Windows.Media.Brush fallback) =>
		element.TryFindResource(key) as System.Windows.Media.Brush ?? fallback;

	static void applyKind(FrameworkElement element, UiTextFeedbackKind kind) {
		switch (kind) {
			case UiTextFeedbackKind.SUCCESS:
				element.SetValue(
					TextElement.ForegroundProperty,
					tryGetThemeBrush(element, "SystemFillColorSuccessBrush", System.Windows.Media.Brushes.LimeGreen));
				break;
			case UiTextFeedbackKind.ERROR:
				element.SetValue(
					TextElement.ForegroundProperty,
					tryGetThemeBrush(element, "SystemFillColorCriticalBrush", System.Windows.Media.Brushes.IndianRed));
				break;
			case UiTextFeedbackKind.WARNING:
				element.SetValue(
					TextElement.ForegroundProperty,
					tryGetThemeBrush(element, "SystemFillColorCautionBrush", System.Windows.Media.Brushes.DarkOrange));
				break;
			default:
				element.ClearValue(TextElement.ForegroundProperty);
				break;
		}
	}
}

