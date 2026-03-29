using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Windows.Forms;

namespace X32VolumeHijacker {
	public partial class ConfigForm : Form {
		const int OSC_TOGGLE_NAME_COLUMN = 0;
		const int OSC_TOGGLE_ADDRESS_COLUMN = 1;
		const int OSC_TOGGLE_HOTKEY_COLUMN = 2;
		const int OSC_TOGGLE_CLEAR_COLUMN = 3;
		const int OSC_TOGGLE_REMOVE_COLUMN = 4;
		const string OSC_TOGGLES_HINT_TEXT = "Select a Hotkey cell, then press the desired key combination.";

		static readonly double VolumeStepSpanLog = Math.Log((double)(MixerController.Config.MaxVolumeStep / MixerController.Config.MinVolumeStep));

		readonly MixerController _mixer;
		readonly AppIconController _icons;
		readonly TrayApp _tray;
		readonly ConfigStore _configStore;
		readonly ResourceLoader _resources;
		bool _volumeStepUiSync;
		TextBox? _hotkeyEditingTextBox;

		public ConfigForm(MixerController mixer, AppIconController icons, TrayApp tray, ConfigStore configStore) {
			InitializeComponent();
			ApplyConfigNumericBoundsFromPolicy();
			this._mixer = mixer;
			this._icons = icons;
			this._tray = tray;
			this._configStore = configStore;
			_resources = tray.Resources;
			HookNumericUpDownEditTextChanged(numericUpDownQueryTimeoutMs, QueryTimeoutNudEdit_TextChanged);
			HookNumericUpDownEditTextChanged(numericUpDownFaderVolumeCacheTtlMs, VolumeCacheNudEdit_TextChanged);
			SetupConfigStoreUi();
			SetupOscToggleGridUi();
			LoadFieldsFromOsc();
			SyncTitlebarIconFromTray();
			RefreshApplyButtonEnabled();
		}

		void SetupOscToggleGridUi() {
			dataGridViewOscToggles.Visible = true;
			dataGridViewOscToggles.Enabled = true;
			dataGridViewOscToggles.ShowCellToolTips = true;
			EnsureOscToggleAddRow();
		}

		void ResetOscToggleHint() {
			labelOscTogglesHint.Text = OSC_TOGGLES_HINT_TEXT;
			labelOscTogglesHint.ForeColor = Color.Black;
		}

		void ShowOscToggleError(string text) {
			labelOscTogglesHint.Text = text;
			labelOscTogglesHint.ForeColor = Color.Red;
		}

		void DetachHotkeyEditingControl() {
			if (_hotkeyEditingTextBox == null)
				return;
			_hotkeyEditingTextBox.KeyDown -= HotkeyTextBox_KeyDown;
			_hotkeyEditingTextBox.KeyPress -= HotkeyTextBox_KeyPress;
			_hotkeyEditingTextBox.ReadOnly = false;
			_hotkeyEditingTextBox.ShortcutsEnabled = true;
			_hotkeyEditingTextBox = null;
			_tray.SetOscToggleHotkeysEnabled(true);
		}

		static void HookNumericUpDownEditTextChanged(NumericUpDown nud, EventHandler handler) {
			foreach (Control c in nud.Controls) {
				if (c is TextBox) {
					c.TextChanged += handler;
					return;
				}
			}
		}

		void ApplyConfigNumericBoundsFromPolicy() {
			numericUpDownQueryTimeoutMs.Minimum = OscController.Config.MinQueryTimeoutMs;
			numericUpDownQueryTimeoutMs.Maximum = OscController.Config.MaxQueryTimeoutMs;
			numericUpDownFaderVolumeCacheTtlMs.Minimum = MixerController.Config.MinFaderVolumeCacheTtlMs;
			numericUpDownFaderVolumeCacheTtlMs.Maximum = MixerController.Config.MaxFaderVolumeCacheTtlMs;
			numericUpDownVolumeStep.Minimum = (decimal)MixerController.Config.MinVolumeStep;
			numericUpDownVolumeStep.Maximum = (decimal)MixerController.Config.MaxVolumeStep;
		}

		void SetupConfigStoreUi() {
			textBoxConfigStorePath.Text = _configStore.ConfigPath;
			buttonOpenConfigStoreFolder.Text = "";
			buttonOpenConfigStoreFolder.AutoSize = false;
			int boxH = textBoxConfigStorePath.PreferredHeight;
			buttonOpenConfigStoreFolder.Size = new Size(boxH, boxH);
			buttonOpenConfigStoreFolder.Padding = Padding.Empty;
			buttonOpenConfigStoreFolder.Margin = new Padding(0, 3, 0, 3);
			int iconSide = Math.Max(1, boxH - LogicalToDeviceUnits(6));
			try {
				string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
				using Icon? ico = Icon.ExtractAssociatedIcon(explorerPath);
				if (ico != null) {
					using Bitmap src = ico.ToBitmap();
					buttonOpenConfigStoreFolder.Image?.Dispose();
					buttonOpenConfigStoreFolder.Image = new Bitmap(src, new Size(iconSide, iconSide));
				}
			} catch {
				// keep text-free button; tooltip still explains the action
			}
		}

		void RefreshConfigStoreDiskFeedback() {
			labelConfigStoreFeedback.Text = _configStore.LastDiskFeedback;
			labelConfigStoreFeedback.ForeColor = _configStore.LastDiskOutcome switch {
				AppConfigDiskOutcome.None => SystemColors.GrayText,
				AppConfigDiskOutcome.NoFileUsingDefaults => Color.SteelBlue,
				AppConfigDiskOutcome.LoadedOk => Color.Green,
				AppConfigDiskOutcome.InvalidOrIncompleteFile => Color.Red,
				AppConfigDiskOutcome.LoadIoError => Color.Red,
				AppConfigDiskOutcome.SavedOk => Color.Green,
				AppConfigDiskOutcome.SaveFailed => Color.Red,
			};
		}

		void buttonOpenConfigStoreFolder_Click(object? sender, EventArgs e) {
			string path = _configStore.ConfigPath;
			string? dir = Path.GetDirectoryName(path);
			if (string.IsNullOrEmpty(dir))
				return;
			try {
				Process.Start(new ProcessStartInfo {
					FileName = "explorer.exe",
					Arguments = "\"" + dir + "\"",
					UseShellExecute = true,
				});
			} catch (Exception ex) {
				MessageBox.Show(this, ex.Message, "Could not open folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		void SyncTitlebarIconFromTray() => Icon = _icons.TrayIconSnapshot;

		/// <summary>Linear track position t in [0,1] -> step = min*(max/min)^t (dense control near min).</summary>
		static float TrackBarToVolumeStep(int value, int trackMin, int trackMax) {
			int span = trackMax - trackMin;
			double t = span <= 0 ? 0 : (value - trackMin) / (double)span;
			float lo = MixerController.Config.MinVolumeStep;
			float hi = MixerController.Config.MaxVolumeStep;
			return (float)(lo * Math.Pow(hi / lo, t));
		}

		static int VolumeStepToTrackBar(float step, int trackMin, int trackMax) {
			int span = trackMax - trackMin;
			double ratio = step / MixerController.Config.MinVolumeStep;
			if (ratio <= 0)
				return trackMin;
			double t = Math.Log(ratio) / VolumeStepSpanLog;
			int pos = trackMin + (int)Math.Round(t * span);
			return Math.Clamp(pos, trackMin, trackMax);
		}

		void SyncVolumeStepNumericFromTrackBar() {
			if (_volumeStepUiSync) return;
			_volumeStepUiSync = true;
			try {
				float s = TrackBarToVolumeStep(trackBarVolumeStep.Value, trackBarVolumeStep.Minimum, trackBarVolumeStep.Maximum);
				decimal d = Math.Round((decimal)s, 4, MidpointRounding.AwayFromZero);
				if (d < numericUpDownVolumeStep.Minimum) d = numericUpDownVolumeStep.Minimum;
				if (d > numericUpDownVolumeStep.Maximum) d = numericUpDownVolumeStep.Maximum;
				numericUpDownVolumeStep.Value = d;
			} finally {
				_volumeStepUiSync = false;
			}
		}

		void SyncVolumeStepTrackBarFromNumeric() {
			if (_volumeStepUiSync) return;
			_volumeStepUiSync = true;
			try {
				float s = (float)numericUpDownVolumeStep.Value;
				trackBarVolumeStep.Value = VolumeStepToTrackBar(s, trackBarVolumeStep.Minimum, trackBarVolumeStep.Maximum);
			} finally {
				_volumeStepUiSync = false;
			}
		}

		static void SetNumericUpDownClamped(NumericUpDown nud, uint value) {
			if (value < (uint)nud.Minimum) value = (uint)nud.Minimum;
			if (value > (uint)nud.Maximum) value = (uint)nud.Maximum;
			nud.Value = value;
		}

		static void ClearFeedbackLabel(Label label) {
			label.Text = "";
			label.ForeColor = Color.Black;
		}

		void LoadFieldsFromOsc() {
			OscController.Config c = new(_configStore.AppConfig.OscController);
			textBoxIP.Text = c.EndPoint.Address.ToString();
			textBoxPort.Text = c.EndPoint.Port.ToString();
			textBoxFaderAddress.Text = c.faderAddress ?? "";
			SetNumericUpDownClamped(numericUpDownQueryTimeoutMs, c.timeoutMs);
			_volumeStepUiSync = true;
			try {
				float s = _configStore.AppConfig.Mixer.VolumeStep;
				trackBarVolumeStep.Value = VolumeStepToTrackBar(s, trackBarVolumeStep.Minimum, trackBarVolumeStep.Maximum);
				float aligned = TrackBarToVolumeStep(trackBarVolumeStep.Value, trackBarVolumeStep.Minimum, trackBarVolumeStep.Maximum);
				numericUpDownVolumeStep.Value = Math.Round((decimal)aligned, 4, MidpointRounding.AwayFromZero);
				uint ttl = Math.Min(_configStore.AppConfig.Mixer.FaderVolumeCacheTtlMs, MixerController.Config.MaxFaderVolumeCacheTtlMs);
				SetNumericUpDownClamped(numericUpDownFaderVolumeCacheTtlMs, ttl);
			} finally {
				_volumeStepUiSync = false;
			}
			RefreshQueryTimeoutAndVolumeCacheColors();
			LoadOscToggleBindings();
		}

		void LoadOscToggleBindings() {
			dataGridViewOscToggles.Rows.Clear();
			foreach (OscToggleBinding binding in _configStore.AppConfig.TrayApp.Bindings)
				AddOscToggleRow(new OscToggleBinding(binding));
			EnsureOscToggleAddRow();
			ResetOscToggleHint();
		}

		static bool IsOscToggleAddRow(DataGridViewRow row) => row.Tag is true;

		void ConfigureOscToggleDataRow(DataGridViewRow row) {
			row.Tag = false;
			row.Cells[OSC_TOGGLE_NAME_COLUMN].ReadOnly = false;
			row.Cells[OSC_TOGGLE_ADDRESS_COLUMN].ReadOnly = false;
			row.Cells[OSC_TOGGLE_HOTKEY_COLUMN].ReadOnly = false;
			row.Cells[OSC_TOGGLE_CLEAR_COLUMN].ReadOnly = true;
			row.Cells[OSC_TOGGLE_REMOVE_COLUMN].ReadOnly = true;
			var clearCell = (DataGridViewImageCell)row.Cells[OSC_TOGGLE_CLEAR_COLUMN];
			clearCell.Value = _resources.ButtonClose;
			clearCell.ToolTipText = "Clear hotkey";
			var delCell = (DataGridViewImageCell)row.Cells[OSC_TOGGLE_REMOVE_COLUMN];
			delCell.Value = _resources.ButtonDelete;
			delCell.ToolTipText = "Remove row";
		}

		void ConfigureOscToggleAddRow(DataGridViewRow row) {
			row.Tag = true;
			row.Cells[OSC_TOGGLE_NAME_COLUMN].Value = "";
			row.Cells[OSC_TOGGLE_ADDRESS_COLUMN].Value = "";
			row.Cells[OSC_TOGGLE_HOTKEY_COLUMN].Value = "";
			row.Cells[OSC_TOGGLE_CLEAR_COLUMN] = new DataGridViewTextBoxCell {
				Value = "",
			};
			var addCell = (DataGridViewImageCell)row.Cells[OSC_TOGGLE_REMOVE_COLUMN];
			addCell.Value = _resources.ButtonAdd;
			addCell.ToolTipText = "Add toggle";
			row.Cells[OSC_TOGGLE_NAME_COLUMN].ReadOnly = true;
			row.Cells[OSC_TOGGLE_ADDRESS_COLUMN].ReadOnly = true;
			row.Cells[OSC_TOGGLE_HOTKEY_COLUMN].ReadOnly = true;
			row.Cells[OSC_TOGGLE_CLEAR_COLUMN].ReadOnly = true;
			row.Cells[OSC_TOGGLE_REMOVE_COLUMN].ReadOnly = true;
		}

		void EnsureOscToggleAddRow() {
			if (dataGridViewOscToggles.Rows.Count > 0 && IsOscToggleAddRow(dataGridViewOscToggles.Rows[^1]))
				return;
			int rowIndex = dataGridViewOscToggles.Rows.Add();
			ConfigureOscToggleAddRow(dataGridViewOscToggles.Rows[rowIndex]);
		}

		/// <summary>Lightweight field syntax only (does not reflect applied <see cref="OscController.Config"/>).</summary>
		bool IpFieldOk() => OscConnectionConfigParse.IsIpFieldSyntaxOk(textBoxIP.Text);

		bool PortFieldOk() => OscConnectionConfigParse.IsPortFieldSyntaxOk(textBoxPort.Text);

		bool FaderFieldOk() => !string.IsNullOrWhiteSpace(textBoxFaderAddress.Text);

		static bool NumericUpDownTextInRange(NumericUpDown nud) {
			string s = nud.Text.Trim();
			if (string.IsNullOrEmpty(s))
				return false;
			if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal v))
				return false;
			if (v != decimal.Truncate(v))
				return false;
			return v >= nud.Minimum && v <= nud.Maximum;
		}

		bool QueryTimeoutFieldOk() => NumericUpDownTextInRange(numericUpDownQueryTimeoutMs);

		bool VolumeCacheFieldOk() => NumericUpDownTextInRange(numericUpDownFaderVolumeCacheTtlMs);

		static string GridCellText(DataGridViewRow row, int columnIndex) => Convert.ToString(row.Cells[columnIndex].Value, CultureInfo.InvariantCulture)?.Trim() ?? "";

		void RefreshQueryTimeoutAndVolumeCacheColors() {
			numericUpDownQueryTimeoutMs.ForeColor = QueryTimeoutFieldOk() ? Color.Black : Color.Red;
			numericUpDownFaderVolumeCacheTtlMs.ForeColor = VolumeCacheFieldOk() ? Color.Black : Color.Red;
		}

		bool TryReadOscToggleBindings(out List<OscToggleBinding> bindings, out string? error) {
			bindings = [];
			error = null;
			var seenHotkeys = new Dictionary<Keys, int>();
			for (int rowIndex = 0; rowIndex < dataGridViewOscToggles.Rows.Count; rowIndex++) {
				DataGridViewRow row = dataGridViewOscToggles.Rows[rowIndex];
				if (row.IsNewRow || IsOscToggleAddRow(row))
					continue;
				string name = GridCellText(row, OSC_TOGGLE_NAME_COLUMN);
				string address = GridCellText(row, OSC_TOGGLE_ADDRESS_COLUMN);
				string hotkeyText = GridCellText(row, OSC_TOGGLE_HOTKEY_COLUMN);
				if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(address) && string.IsNullOrWhiteSpace(hotkeyText))
					continue;
				if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(hotkeyText)) {
					error = $"OSC toggle row {rowIndex + 1} must have Name, Address, and Hotkey.";
					return false;
				}
				if (!OscHotkey.TryParse(hotkeyText, out Keys hotkey)) {
					error = $"OSC toggle row {rowIndex + 1} has an invalid hotkey.";
					return false;
				}
				if (!OscHotkey.TryValidate(hotkey, out string hotkeyError)) {
					error = $"OSC toggle row {rowIndex + 1}: {hotkeyError}";
					return false;
				}
				hotkey = OscHotkey.Normalize(hotkey);
				if (seenHotkeys.TryGetValue(hotkey, out int firstRow)) {
					error = $"OSC toggle rows {firstRow + 1} and {rowIndex + 1} use the same hotkey ({OscHotkey.Format(hotkey)}).";
					return false;
				}
				seenHotkeys.Add(hotkey, rowIndex);
				bindings.Add(new OscToggleBinding {
					Name = name,
					Address = address,
					Hotkey = hotkey,
				});
			}
			return true;
		}

		bool OscToggleListOk() => TryReadOscToggleBindings(out _, out _);

		/// <summary>True when connection fields meet the same rules as <see cref="TryReadUiForApply"/>.</summary>
		bool ApplyFormOk() => IpFieldOk() && PortFieldOk() && FaderFieldOk()
			&& QueryTimeoutFieldOk() && VolumeCacheFieldOk() && OscToggleListOk();

		void RefreshApplyButtonEnabled() => buttonSaveAndTest.Enabled = ApplyFormOk();

		void SetConnectionInputsEnabled(bool enabled) {
			textBoxIP.Enabled = enabled;
			textBoxPort.Enabled = enabled;
			textBoxFaderAddress.Enabled = enabled;
			trackBarVolumeStep.Enabled = enabled;
			numericUpDownVolumeStep.Enabled = enabled;
			numericUpDownFaderVolumeCacheTtlMs.Enabled = enabled;
			numericUpDownQueryTimeoutMs.Enabled = enabled;
			dataGridViewOscToggles.Enabled = enabled;
		}

		void ClearNetworkTestFeedback() => ClearFeedbackLabel(labelNetworkFeedback);

		void ClearOscBaseTestFeedback() {
			ClearFeedbackLabel(labelOscBaseFeedback);
			textBoxInfoResult.Text = "";
		}

		void ClearFaderTestFeedback() => ClearFeedbackLabel(labelFaderTestResult);

		void ClearAllTestFeedback() {
			ClearNetworkTestFeedback();
			ClearOscBaseTestFeedback();
			ClearFaderTestFeedback();
		}

		/// <summary>Port change invalidates /info and fader checks (same OSC endpoint).</summary>
		void ClearOscBaseAndFaderTestFeedback() {
			ClearOscBaseTestFeedback();
			ClearFaderTestFeedback();
		}

		bool TryReadUiForApply(out OscController.Config cfg, out string? ipError, out string? portError, out string? oscError) {
			cfg = default!;
			if (!OscConnectionConfigParse.TryParse(textBoxIP.Text, textBoxPort.Text, textBoxFaderAddress.Text,
				    out IPAddress ip, out int port, out string fader, out ipError, out portError, out oscError))
				return false;
			cfg = new OscController.Config {
				EndPoint = new IPEndPoint(ip, port),
				faderAddress = fader,
				timeoutMs = (uint)numericUpDownQueryTimeoutMs.Value,
			};
			return true;
		}

		private void textBoxIP_TextChanged(object sender, EventArgs e) {
			if (IpFieldOk())
				textBoxIP.ForeColor = Color.Black;
			else
				textBoxIP.ForeColor = Color.Red;
			ClearAllTestFeedback();
			RefreshApplyButtonEnabled();
		}

		private void textBoxPort_TextChanged(object sender, EventArgs e) {
			if (PortFieldOk())
				textBoxPort.ForeColor = Color.Black;
			else
				textBoxPort.ForeColor = Color.Red;
			ClearOscBaseAndFaderTestFeedback();
			RefreshApplyButtonEnabled();
		}

		void QueryTimeoutNudEdit_TextChanged(object? sender, EventArgs e) {
			RefreshQueryTimeoutAndVolumeCacheColors();
			ClearNetworkTestFeedback();
			RefreshApplyButtonEnabled();
		}

		void VolumeCacheNudEdit_TextChanged(object? sender, EventArgs e) {
			RefreshQueryTimeoutAndVolumeCacheColors();
			ClearFaderTestFeedback();
			RefreshApplyButtonEnabled();
		}

		private void textBoxFaderAddress_TextChanged(object? sender, EventArgs e) {
			ClearFaderTestFeedback();
			RefreshApplyButtonEnabled();
		}

		void dataGridViewOscToggles_CurrentCellDirtyStateChanged(object? sender, EventArgs e) {
			if (dataGridViewOscToggles.IsCurrentCellDirty)
				dataGridViewOscToggles.CommitEdit(DataGridViewDataErrorContexts.Commit);
		}

		void dataGridViewOscToggles_CellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e) {
			if (e.RowIndex < 0 || e.ColumnIndex < 0)
				return;
			DataGridViewRow row = dataGridViewOscToggles.Rows[e.RowIndex];
			if (IsOscToggleAddRow(row) || e.ColumnIndex is OSC_TOGGLE_CLEAR_COLUMN or OSC_TOGGLE_REMOVE_COLUMN)
				e.Cancel = true;
		}

		void dataGridViewOscToggles_CellClick(object? sender, DataGridViewCellEventArgs e) {
			if (e.RowIndex < 0 || e.ColumnIndex < 0)
				return;
			DataGridViewRow row = dataGridViewOscToggles.Rows[e.RowIndex];
			if (IsOscToggleAddRow(row)) {
				if (e.ColumnIndex == OSC_TOGGLE_REMOVE_COLUMN)
					AddOscToggleRow();
				return;
			}
			if (e.ColumnIndex == OSC_TOGGLE_CLEAR_COLUMN) {
				dataGridViewOscToggles.CurrentCell = row.Cells[OSC_TOGGLE_HOTKEY_COLUMN];
				ClearCurrentOscToggleHotkey();
				return;
			}
			if (e.ColumnIndex == OSC_TOGGLE_REMOVE_COLUMN) {
				dataGridViewOscToggles.Rows.RemoveAt(e.RowIndex);
				EnsureOscToggleAddRow();
				ResetOscToggleHint();
				RefreshApplyButtonEnabled();
			}
		}

		void dataGridViewOscToggles_CellEndEdit(object? sender, DataGridViewCellEventArgs e) {
			DetachHotkeyEditingControl();
			if (e.RowIndex < 0 || e.ColumnIndex < 0)
				return;
			DataGridViewCell cell = dataGridViewOscToggles.Rows[e.RowIndex].Cells[e.ColumnIndex];
			string text = Convert.ToString(cell.Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
			if (e.ColumnIndex == OSC_TOGGLE_HOTKEY_COLUMN && OscHotkey.TryParse(text, out Keys hotkey))
				text = OscHotkey.Format(hotkey);
			cell.Value = text;
			ResetOscToggleHint();
			RefreshApplyButtonEnabled();
		}

		void AddOscToggleRow(OscToggleBinding? binding = null) {
			int insertIndex = dataGridViewOscToggles.Rows.Count;
			if (insertIndex > 0 && IsOscToggleAddRow(dataGridViewOscToggles.Rows[insertIndex - 1]))
				insertIndex--;
			dataGridViewOscToggles.Rows.Insert(insertIndex, binding?.Name ?? "", binding?.Address ?? "", binding != null ? OscHotkey.Format(binding.Hotkey) : "", "", "");
			ConfigureOscToggleDataRow(dataGridViewOscToggles.Rows[insertIndex]);
			EnsureOscToggleAddRow();
			if (binding == null) {
				dataGridViewOscToggles.CurrentCell = dataGridViewOscToggles.Rows[insertIndex].Cells[OSC_TOGGLE_NAME_COLUMN];
				dataGridViewOscToggles.BeginEdit(true);
			}
			ResetOscToggleHint();
			RefreshApplyButtonEnabled();
		}

		void ClearCurrentOscToggleHotkey() {
			if (dataGridViewOscToggles.CurrentCell == null)
				return;
			int rowIndex = dataGridViewOscToggles.CurrentCell.RowIndex;
			if (rowIndex < 0 || rowIndex >= dataGridViewOscToggles.Rows.Count)
				return;
			if (IsOscToggleAddRow(dataGridViewOscToggles.Rows[rowIndex]))
				return;
			SetCurrentHotkeyCellText("");
			ResetOscToggleHint();
			RefreshApplyButtonEnabled();
		}

		void SetCurrentHotkeyCellText(string text) {
			if (dataGridViewOscToggles.CurrentCell == null || dataGridViewOscToggles.CurrentCell.ColumnIndex != OSC_TOGGLE_HOTKEY_COLUMN)
				return;
			dataGridViewOscToggles.CurrentCell.Value = text;
			if (_hotkeyEditingTextBox != null) {
				_hotkeyEditingTextBox.Text = text;
				_hotkeyEditingTextBox.SelectionStart = text.Length;
				_hotkeyEditingTextBox.SelectionLength = 0;
			}
		}

		void SetCurrentHotkeyCell(Keys hotkey) {
			if (!OscHotkey.TryValidate(hotkey, out string error)) {
				ShowOscToggleError(error);
				RefreshApplyButtonEnabled();
				return;
			}
			SetCurrentHotkeyCellText(OscHotkey.Format(hotkey));
			ResetOscToggleHint();
			RefreshApplyButtonEnabled();
		}

		void dataGridViewOscToggles_KeyDown(object? sender, KeyEventArgs e) {
			if (dataGridViewOscToggles.CurrentCell == null || dataGridViewOscToggles.IsCurrentCellInEditMode)
				return;
			if (IsOscToggleAddRow(dataGridViewOscToggles.Rows[dataGridViewOscToggles.CurrentCell.RowIndex]))
				return;
			if (dataGridViewOscToggles.CurrentCell.ColumnIndex != OSC_TOGGLE_HOTKEY_COLUMN)
				return;
			if (e.KeyCode is Keys.Delete or Keys.Back) {
				ClearCurrentOscToggleHotkey();
				e.SuppressKeyPress = true;
				return;
			}
			if (OscHotkey.IsModifierKey(e.KeyCode)) {
				e.SuppressKeyPress = true;
				return;
			}
			SetCurrentHotkeyCell(OscHotkey.Normalize(e.KeyData));
			e.SuppressKeyPress = true;
		}

		void dataGridViewOscToggles_EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e) {
			DetachHotkeyEditingControl();
			if (dataGridViewOscToggles.CurrentCell?.ColumnIndex != OSC_TOGGLE_HOTKEY_COLUMN)
				return;
			if (e.Control is not TextBox tb)
				return;
			_hotkeyEditingTextBox = tb;
			_tray.SetOscToggleHotkeysEnabled(false);
			tb.ReadOnly = true;
			tb.ShortcutsEnabled = false;
			tb.KeyDown += HotkeyTextBox_KeyDown;
			tb.KeyPress += HotkeyTextBox_KeyPress;
		}

		protected override void OnFormClosed(FormClosedEventArgs e) {
			_tray.SetOscToggleHotkeysEnabled(true);
			DetachHotkeyEditingControl();
			base.OnFormClosed(e);
		}

		void HotkeyTextBox_KeyDown(object? sender, KeyEventArgs e) {
			if (e.KeyCode is Keys.Delete or Keys.Back) {
				ClearCurrentOscToggleHotkey();
				e.SuppressKeyPress = true;
				return;
			}
			if (OscHotkey.IsModifierKey(e.KeyCode)) {
				e.SuppressKeyPress = true;
				return;
			}
			SetCurrentHotkeyCell(OscHotkey.Normalize(e.KeyData));
			e.SuppressKeyPress = true;
		}

		static void HotkeyTextBox_KeyPress(object? sender, KeyPressEventArgs e) => e.Handled = true;

		private void trackBarVolumeStep_ValueChanged(object? sender, EventArgs e) {
			if (_volumeStepUiSync) return;
			SyncVolumeStepNumericFromTrackBar();
			ClearFaderTestFeedback();
		}

		private void numericUpDownVolumeStep_ValueChanged(object? sender, EventArgs e) {
			if (_volumeStepUiSync) return;
			SyncVolumeStepTrackBarFromNumeric();
			ClearFaderTestFeedback();
		}

		private void numericUpDownFaderVolumeCacheTtlMs_ValueChanged(object? sender, EventArgs e) {
			RefreshQueryTimeoutAndVolumeCacheColors();
			ClearFaderTestFeedback();
			RefreshApplyButtonEnabled();
		}

		private void numericUpDownQueryTimeoutMs_ValueChanged(object? sender, EventArgs e) {
			RefreshQueryTimeoutAndVolumeCacheColors();
			ClearNetworkTestFeedback();
			RefreshApplyButtonEnabled();
		}

		private void ConfigForm_Load(object sender, EventArgs e) {
			PositionNearBottomRightOfWorkingArea();
			SyncTitlebarIconFromTray();
			RefreshAutostartFeedback();
			RefreshConfigStoreDiskFeedback();
		}

		/// <summary>
		/// Places the form just inside the bottom-right of the monitor under the cursor (typical when opened from the tray menu),
		/// using <see cref="Screen.WorkingArea"/> so the taskbar and reserved edges are respected.
		/// </summary>
		void PositionNearBottomRightOfWorkingArea() {
			const int marginPx = 12;
			Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
			StartPosition = FormStartPosition.Manual;
			int x = wa.Right - Width - marginPx;
			int y = wa.Bottom - Height - marginPx;
			if (x < wa.Left) x = wa.Left;
			if (y < wa.Top) y = wa.Top;
			Location = new Point(x, y);
		}

		void RefreshAutostartFeedback() {
			labelAutostartFeedback.ForeColor = Color.Black;
			labelAutostartFeedback.Text = WindowsAutostart.IsRegistered()
				? "Currently registered to start with Windows."
				: "Not registered for Windows startup.";
		}

		void buttonAutostartRegister_Click(object? sender, EventArgs e) {
			if (WindowsAutostart.TryRegister(out string? err)) {
				labelAutostartFeedback.Text = "Registered.";
				labelAutostartFeedback.ForeColor = Color.Green;
			} else {
				labelAutostartFeedback.Text = err ?? "Registration failed.";
				labelAutostartFeedback.ForeColor = Color.Red;
			}
		}

		void buttonAutostartDeregister_Click(object? sender, EventArgs e) {
			if (WindowsAutostart.TryDeregister(out string? err)) {
				labelAutostartFeedback.Text = "Removed from startup.";
				labelAutostartFeedback.ForeColor = Color.Green;
			} else {
				labelAutostartFeedback.Text = err ?? "Could not remove startup entry.";
				labelAutostartFeedback.ForeColor = Color.Red;
			}
		}

		private async void buttonSaveAndTest_Click(object sender, EventArgs e) {
			labelNetworkFeedback.Text = "";
			labelOscBaseFeedback.Text = "";
			textBoxInfoResult.Text = "";
			labelFaderTestResult.Text = "";
			if (!TryReadUiForApply(out OscController.Config newCfg, out string? ipErr, out string? portErr, out string? oscErr)) {
				if (ipErr != null) {
					labelNetworkFeedback.Text = ipErr;
					labelNetworkFeedback.ForeColor = Color.Red;
				}
				if (portErr != null) {
					labelOscBaseFeedback.Text = portErr;
					labelOscBaseFeedback.ForeColor = Color.Red;
				}
				if (oscErr != null) {
					labelFaderTestResult.Text = oscErr;
					labelFaderTestResult.ForeColor = Color.Red;
				}
				return;
			}
			if (!TryReadOscToggleBindings(out List<OscToggleBinding> oscToggles, out string? toggleError)) {
				ShowOscToggleError(toggleError ?? "OSC toggles are invalid.");
				return;
			}
			buttonSaveAndTest.Enabled = false;
			SetConnectionInputsEnabled(false);
			try {
				var appConfig = new AppConfig {
					OscController = newCfg,
					Mixer = new MixerController.Config {
						VolumeStep = (float)numericUpDownVolumeStep.Value,
						FaderVolumeCacheTtlMs = (uint)numericUpDownFaderVolumeCacheTtlMs.Value,
					},
					TrayApp = new TrayApp.Config {
						Bindings = oscToggles.Select(b => new OscToggleBinding(b)).ToList(),
					},
				};
				_tray.CommitConfigFromSettingsForm(appConfig);
				ResetOscToggleHint();
				RefreshConfigStoreDiskFeedback();
				Icon = _tray.ApplyTrayIconState(AppTrayIconState.StartingOrInvalidConfig);
				labelNetworkFeedback.Text = "Ping test: running…";
				labelNetworkFeedback.ForeColor = Color.Black;
				labelOscBaseFeedback.Text = "Testing /info ...";
				labelOscBaseFeedback.ForeColor = Color.Black;
				labelFaderTestResult.Text = "Testing fader ...";
				labelFaderTestResult.ForeColor = Color.Black;

				var pingUi = new Progress<(string text, Color color)>(v => {
					labelNetworkFeedback.Text = v.text;
					labelNetworkFeedback.ForeColor = v.color;
				});
				int pingTimeoutMs = (int)Math.Min(newCfg.timeoutMs, int.MaxValue);
				Task<(string text, Color color)> pingTask = NetworkPingTest.PingFeedbackAsync(newCfg.EndPoint.Address, timeoutMs: pingTimeoutMs, probeProgress: pingUi);

				async Task<(bool infoOk, TimeSpan infoElapsed, float? fader, TimeSpan faderQueryElapsed)> runOscTestsAsync() {
					void postUi(Action action) {
						if (IsDisposed) return;
						BeginInvoke(action);
					}
					var sw = Stopwatch.StartNew();
					var info = await _mixer.QueryInfoAsync().ConfigureAwait(false);
					sw.Stop();
					TimeSpan elapsed = sw.Elapsed;
					postUi(() => {
						if (IsDisposed) return;
						if (info.Ok) {
							textBoxInfoResult.Text = info.Detail;
							labelOscBaseFeedback.Text = $"/info latency: {elapsed.TotalMilliseconds:0} ms";
							labelOscBaseFeedback.ForeColor = Color.Green;
						} else {
							textBoxInfoResult.Text = "";
							labelOscBaseFeedback.Text = info.Detail;
							labelOscBaseFeedback.ForeColor = Color.Red;
						}
					});
					var swFader = Stopwatch.StartNew();
					float? fader = await _mixer.QueryFaderAsync().ConfigureAwait(false);
					swFader.Stop();
					TimeSpan faderElapsed = swFader.Elapsed;
					postUi(() => {
						if (IsDisposed) return;
						if (fader != null) {
							string lat = $"{faderElapsed.TotalMilliseconds:0} ms";
							string val = fader.Value.ToString("0.###", CultureInfo.InvariantCulture);
							labelFaderTestResult.Text = $"Fader OK — latency {lat}, current value {val}";
							labelFaderTestResult.ForeColor = Color.Green;
						} else {
							labelFaderTestResult.Text = "Couldn't get fader.";
							labelFaderTestResult.ForeColor = Color.Red;
						}
					});
					return (info.Ok, elapsed, fader, faderElapsed);
				}

				Task<(bool infoOk, TimeSpan infoElapsed, float? fader, TimeSpan faderQueryElapsed)> oscTask = runOscTestsAsync();
				await Task.WhenAll(pingTask, oscTask).ConfigureAwait(true);
				var ping = await pingTask.ConfigureAwait(true);
				var (infoOk, _, fader, _) = await oscTask.ConfigureAwait(true);
				labelNetworkFeedback.Text = ping.text;
				labelNetworkFeedback.ForeColor = ping.color;

				bool faderOk = fader != null;
				if (infoOk && faderOk)
					Icon = _tray.ApplyTrayIconState(AppTrayIconState.Ok);
				else
					Icon = _tray.ApplyTrayIconState(AppTrayIconState.NetworkError, showErrorOsdIfNotOk: false);
			} catch (Exception ex) {
				textBoxInfoResult.Text = "";
				labelOscBaseFeedback.Text = "Error: " + ex.Message;
				labelOscBaseFeedback.ForeColor = Color.Red;
				labelFaderTestResult.Text = "";
				Icon = _tray.ApplyTrayIconState(AppTrayIconState.NetworkError, showErrorOsdIfNotOk: false);
			} finally {
				SetConnectionInputsEnabled(true);
				RefreshApplyButtonEnabled();
				RefreshConfigStoreDiskFeedback();
			}
		}
	}
}
