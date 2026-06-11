namespace WindowsOscVolumeControl.UI.Config;

/// <summary>Status-bar feedback lines for the apply-save-and-test flow in <see cref="ConfigWindow"/>.</summary>
static class SettingsFeedback {
	public static UiTextFeedback infoQueryDetail(bool ok, string detail) =>
		new(detail, ok ? UiTextFeedbackKind.DEFAULT : UiTextFeedbackKind.ERROR);

	public static UiTextFeedback settingsApplyMixerSummary(bool mixerInfoOk) =>
		new(
			mixerInfoOk
				? "Settings applied and mixer responded."
				: "Settings saved, but the mixer did not respond cleanly.",
			mixerInfoOk ? UiTextFeedbackKind.SUCCESS : UiTextFeedbackKind.ERROR);

	public static UiTextFeedback exceptionMessage(Exception ex) =>
		new(ex.Message, UiTextFeedbackKind.ERROR);
}
