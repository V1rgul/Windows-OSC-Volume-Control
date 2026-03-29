using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Windows.Forms;

namespace X32VolumeHijacker {
	public partial class ConfigForm : Form {
		const int OSC_TOGGLE_NAME_COLUMN = 0;
		const int OSC_TOGGLE_ADDRESS_COLUMN = 1;
		const int OSC_TOGGLE_HOTKEY_COLUMN = 2;
		const int OSC_TOGGLE_CLEAR_COLUMN = 3;
		const int OSC_TOGGLE_REMOVE_COLUMN = 4;

		const int OSC_FADER_NAME_COLUMN = 0;
		const int OSC_FADER_ADDRESS_COLUMN = 1;
		const int OSC_FADER_STEP_COLUMN = 2;
		const int OSC_FADER_MIN_COLUMN = 3;
		const int OSC_FADER_MAX_COLUMN = 4;
		const int OSC_FADER_HOTKEY_MINUS_COLUMN = 5;
		const int OSC_FADER_CLEAR_MINUS_COLUMN = 6;
		const int OSC_FADER_HOTKEY_PLUS_COLUMN = 7;
		const int OSC_FADER_CLEAR_PLUS_COLUMN = 8;
		const int OSC_FADER_REMOVE_COLUMN = 9;

		readonly MixerController _mixer;
		readonly AppIconController _icons;
		readonly TrayApp _tray;
		readonly ConfigStore _configStore;
		readonly ResourceLoader _resources;
		TextBox? _hotkeyEditingTextBox;
		DataGridView? _hotkeyOwnerGrid;
		int _hotkeyOwnerColumn = -1;

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
			SetupOscFaderGridUi();
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

		void SetupOscFaderGridUi() {
			dataGridViewOscFaders.Visible = true;
			dataGridViewOscFaders.Enabled = true;
			dataGridViewOscFaders.ShowCellToolTips = true;
			EnsureOscFaderAddRow();
		}

		void ResetOscToggleHint() {
			labelOscTogglesHint.Text = "";
			labelOscTogglesHint.ForeColor = Color.Black;
		}

		void ShowOscToggleError(string text) {
			labelOscTogglesHint.Text = text;
			labelOscTogglesHint.ForeColor = Color.Red;
		}

		void DetachHotkeyEditingControl() {
			if (_hotkeyEditingTextBox != null) {
				_hotkeyEditingTextBox.KeyDown -= HotkeyTextBox_KeyDown;
				_hotkeyEditingTextBox.KeyPress -= HotkeyTextBox_KeyPress;
				_hotkeyEditingTextBox.ReadOnly = false;
				_hotkeyEditingTextBox.ShortcutsEnabled = true;
				_hotkeyEditingTextBox = null;
			}
			_hotkeyOwnerGrid = null;
			_hotkeyOwnerColumn = -1;
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
			numericUpDownFaderVolumeCacheTtlMs.Minimum = MixerController.Config.MinValueCacheTtlMs;
			numericUpDownFaderVolumeCacheTtlMs.Maximum = MixerController.Config.MaxValueCacheTtlMs;
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
			SetNumericUpDownClamped(numericUpDownQueryTimeoutMs, c.timeoutMs);
			uint ttl = Math.Min(_configStore.AppConfig.Mixer.ValueCacheTtlMs, MixerController.Config.MaxValueCacheTtlMs);
			SetNumericUpDownClamped(numericUpDownFaderVolumeCacheTtlMs, ttl);
			RefreshQueryTimeoutAndVolumeCacheColors();
			LoadOscFaderBindings();
			LoadOscToggleBindings();
		}

		void LoadOscFaderBindings() {
			dataGridViewOscFaders.Rows.Clear();
			foreach (OscFaderBinding binding in _configStore.AppConfig.TrayApp?.FaderBindings ?? [])
				AddOscFaderRow(new OscFaderBinding(binding));
			EnsureOscFaderAddRow();
		}

		void LoadOscToggleBindings() {
			dataGridViewOscToggles.Rows.Clear();
			foreach (OscToggleBinding binding in _configStore.AppConfig.TrayApp?.Bindings ?? [])
				AddOscToggleRow(new OscToggleBinding(binding));
			EnsureOscToggleAddRow();
			ResetOscToggleHint();
		}

		static bool IsOscToggleAddRow(DataGridViewRow row) => row.Tag is true;

		static bool IsOscFaderAddRow(DataGridViewRow row) => row.Tag is true;

		void ConfigureOscFaderDataRow(DataGridViewRow row) {
			row.Tag = false;
			for (int i = OSC_FADER_NAME_COLUMN; i <= OSC_FADER_HOTKEY_PLUS_COLUMN; i++)
				row.Cells[i].ReadOnly = false;
			row.Cells[OSC_FADER_CLEAR_MINUS_COLUMN].ReadOnly = true;
			row.Cells[OSC_FADER_CLEAR_PLUS_COLUMN].ReadOnly = true;
			row.Cells[OSC_FADER_REMOVE_COLUMN].ReadOnly = true;
			var clearM = (DataGridViewImageCell)row.Cells[OSC_FADER_CLEAR_MINUS_COLUMN];
			clearM.Value = _resources.ButtonClose;
			clearM.ToolTipText = "Clear hotkey −";
			var clearP = (DataGridViewImageCell)row.Cells[OSC_FADER_CLEAR_PLUS_COLUMN];
			clearP.Value = _resources.ButtonClose;
			clearP.ToolTipText = "Clear hotkey +";
			var delCell = (DataGridViewImageCell)row.Cells[OSC_FADER_REMOVE_COLUMN];
			delCell.Value = _resources.ButtonDelete;
			delCell.ToolTipText = "Remove row";
		}

		void ConfigureOscFaderAddRow(DataGridViewRow row) {
			row.Tag = true;
			for (int i = 0; i <= OSC_FADER_HOTKEY_PLUS_COLUMN; i++)
				row.Cells[i].Value = "";
			row.Cells[OSC_FADER_CLEAR_MINUS_COLUMN] = new DataGridViewTextBoxCell { Value = "" };
			row.Cells[OSC_FADER_CLEAR_PLUS_COLUMN] = new DataGridViewTextBoxCell { Value = "" };
			var addCell = (DataGridViewImageCell)row.Cells[OSC_FADER_REMOVE_COLUMN];
			addCell.Value = _resources.ButtonAdd;
			addCell.ToolTipText = "Add fader";
			for (int i = 0; i < row.Cells.Count; i++)
				row.Cells[i].ReadOnly = true;
		}

		void EnsureOscFaderAddRow() {
			if (dataGridViewOscFaders.Rows.Count > 0 && IsOscFaderAddRow(dataGridViewOscFaders.Rows[^1]))
				return;
			int rowIndex = dataGridViewOscFaders.Rows.Add();
			ConfigureOscFaderAddRow(dataGridViewOscFaders.Rows[rowIndex]);
		}

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

		bool IpFieldOk() => OscConnectionConfigParse.IsIpFieldSyntaxOk(textBoxIP.Text);

		bool PortFieldOk() => OscConnectionConfigParse.IsPortFieldSyntaxOk(textBoxPort.Text);

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

		static bool IsOscFaderRowAllEmpty(DataGridViewRow row) {
			if (IsOscFaderAddRow(row))
				return true;
			return string.IsNullOrWhiteSpace(GridCellText(row, OSC_FADER_NAME_COLUMN))
				&& string.IsNullOrWhiteSpace(GridCellText(row, OSC_FADER_ADDRESS_COLUMN))
				&& string.IsNullOrWhiteSpace(GridCellText(row, OSC_FADER_STEP_COLUMN))
				&& string.IsNullOrWhiteSpace(GridCellText(row, OSC_FADER_MIN_COLUMN))
				&& string.IsNullOrWhiteSpace(GridCellText(row, OSC_FADER_MAX_COLUMN))
				&& string.IsNullOrWhiteSpace(GridCellText(row, OSC_FADER_HOTKEY_MINUS_COLUMN))
				&& string.IsNullOrWhiteSpace(GridCellText(row, OSC_FADER_HOTKEY_PLUS_COLUMN));
		}

		static bool TryParseOptionalHotkey(string text, out Keys hotkey, out string? error) {
			hotkey = Keys.None;
			error = null;
			text = text.Trim();
			if (string.IsNullOrEmpty(text))
				return true;
			if (!OscHotkey.TryParse(text, out hotkey)) {
				error = "Invalid hotkey.";
				return false;
			}
			if (!OscHotkey.TryValidate(hotkey, out string hotkeyError)) {
				error = hotkeyError;
				return false;
			}
			hotkey = OscHotkey.Normalize(hotkey);
			return true;
		}

		bool TryReadOscFaderBindings(out List<OscFaderBinding> bindings, out string? error) {
			bindings = [];
			error = null;
			for (int rowIndex = 0; rowIndex < dataGridViewOscFaders.Rows.Count; rowIndex++) {
				DataGridViewRow row = dataGridViewOscFaders.Rows[rowIndex];
				if (row.IsNewRow || IsOscFaderAddRow(row))
					continue;
				if (IsOscFaderRowAllEmpty(row))
					continue;
				string name = GridCellText(row, OSC_FADER_NAME_COLUMN);
				string address = GridCellText(row, OSC_FADER_ADDRESS_COLUMN);
				string stepS = GridCellText(row, OSC_FADER_STEP_COLUMN);
				string minS = GridCellText(row, OSC_FADER_MIN_COLUMN);
				string maxS = GridCellText(row, OSC_FADER_MAX_COLUMN);
				string hkMinusS = GridCellText(row, OSC_FADER_HOTKEY_MINUS_COLUMN);
				string hkPlusS = GridCellText(row, OSC_FADER_HOTKEY_PLUS_COLUMN);
				bool any = !string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(address)
					|| !string.IsNullOrWhiteSpace(stepS) || !string.IsNullOrWhiteSpace(minS) || !string.IsNullOrWhiteSpace(maxS)
					|| !string.IsNullOrWhiteSpace(hkMinusS) || !string.IsNullOrWhiteSpace(hkPlusS);
				if (!any)
					continue;
				if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address)
				    || string.IsNullOrWhiteSpace(stepS) || string.IsNullOrWhiteSpace(minS) || string.IsNullOrWhiteSpace(maxS)) {
					error = $"OSC fader row {rowIndex + 1} must have Name, Address, Step, Minimum, and Maximum.";
					return false;
				}
				if (!float.TryParse(stepS, NumberStyles.Float, CultureInfo.InvariantCulture, out float step) || !float.IsFinite(step)
				    || !float.TryParse(minS, NumberStyles.Float, CultureInfo.InvariantCulture, out float min) || !float.IsFinite(min)
				    || !float.TryParse(maxS, NumberStyles.Float, CultureInfo.InvariantCulture, out float max) || !float.IsFinite(max)) {
					error = $"OSC fader row {rowIndex + 1}: Step, Minimum, and Maximum must be finite numbers.";
					return false;
				}
				if (min > max) {
					error = $"OSC fader row {rowIndex + 1}: Minimum must be ≤ Maximum.";
					return false;
				}
				step = Math.Clamp(step, MixerController.Config.MinFaderStep, MixerController.Config.MaxFaderStep);
				_ = OscController.NormalizeBindingAddress(address);
				if (!TryParseOptionalHotkey(hkMinusS, out Keys hkMinus, out string? em)) {
					error = $"OSC fader row {rowIndex + 1} (−): {em}";
					return false;
				}
				if (!TryParseOptionalHotkey(hkPlusS, out Keys hkPlus, out string? ep)) {
					error = $"OSC fader row {rowIndex + 1} (+): {ep}";
					return false;
				}
				bindings.Add(new OscFaderBinding {
					Name = name,
					Address = address,
					Step = step,
					Minimum = min,
					Maximum = max,
					HotkeyMinus = hkMinus,
					HotkeyPlus = hkPlus,
				});
			}
			if (bindings.Count == 0) {
				error = "Add at least one OSC fader row (Name, Address, Step, Minimum, Maximum).";
				return false;
			}
			return true;
		}

		bool TryReadOscToggleBindings(out List<OscToggleBinding> bindings, out string? error) {
			bindings = [];
			error = null;
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
				bindings.Add(new OscToggleBinding {
					Name = name,
					Address = address,
					Hotkey = hotkey,
				});
			}
			return true;
		}

		static bool TryValidateHotkeysGlobally(IReadOnlyList<OscFaderBinding> faders, IReadOnlyList<OscToggleBinding> toggles, out string? error) {
			error = null;
			var claimed = new Dictionary<Keys, string>();
			for (int i = 0; i < faders.Count; i++) {
				OscFaderBinding f = faders[i];
				string rowLabel = $"OSC fader row {i + 1}";
				if (f.HotkeyMinus != Keys.None) {
					Keys k = OscHotkey.Normalize(f.HotkeyMinus);
					if (claimed.TryGetValue(k, out string? prev)) {
						error = $"{rowLabel} hotkey − ({OscHotkey.Format(k)}) conflicts with {prev}.";
						return false;
					}
					claimed[k] = $"{rowLabel} (−)";
				}
				if (f.HotkeyPlus != Keys.None) {
					Keys k = OscHotkey.Normalize(f.HotkeyPlus);
					if (claimed.TryGetValue(k, out string? prev)) {
						error = $"{rowLabel} hotkey + ({OscHotkey.Format(k)}) conflicts with {prev}.";
						return false;
					}
					claimed[k] = $"{rowLabel} (+)";
				}
			}
			for (int i = 0; i < toggles.Count; i++) {
				OscToggleBinding t = toggles[i];
				Keys k = OscHotkey.Normalize(t.Hotkey);
				if (claimed.TryGetValue(k, out string? prev)) {
					error = $"OSC toggle \"{t.Name}\" ({OscHotkey.Format(k)}) conflicts with {prev}.";
					return false;
				}
				claimed[k] = $"OSC toggle \"{t.Name}\"";
			}
			return true;
		}

		bool OscFaderListOk() => TryReadOscFaderBindings(out _, out _);

		bool OscToggleListOk() => TryReadOscToggleBindings(out _, out _);

		bool ApplyFormOk() {
			if (!IpFieldOk() || !PortFieldOk() || !QueryTimeoutFieldOk() || !VolumeCacheFieldOk())
				return false;
			if (!TryReadOscFaderBindings(out List<OscFaderBinding> faders, out _))
				return false;
			if (!TryReadOscToggleBindings(out List<OscToggleBinding> toggles, out _))
				return false;
			return TryValidateHotkeysGlobally(faders, toggles, out _);
		}

		void RefreshApplyButtonEnabled() => buttonSaveAndTest.Enabled = ApplyFormOk();

		void SetConnectionInputsEnabled(bool enabled) {
			textBoxIP.Enabled = enabled;
			textBoxPort.Enabled = enabled;
			numericUpDownFaderVolumeCacheTtlMs.Enabled = enabled;
			numericUpDownQueryTimeoutMs.Enabled = enabled;
			dataGridViewOscToggles.Enabled = enabled;
			dataGridViewOscFaders.Enabled = enabled;
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

		void ClearOscBaseAndFaderTestFeedback() {
			ClearOscBaseTestFeedback();
			ClearFaderTestFeedback();
		}

		bool TryReadUiForApply(out OscController.Config cfg, out string? ipError, out string? portError) {
			cfg = default!;
			if (!OscConnectionConfigParse.TryParseIpPort(textBoxIP.Text, textBoxPort.Text,
				    out IPAddress ip, out int port, out ipError, out portError))
				return false;
			cfg = new OscController.Config {
				EndPoint = new IPEndPoint(ip, port),
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

		void dataGridViewOscToggles_CurrentCellDirtyStateChanged(object? sender, EventArgs e) {
			if (dataGridViewOscToggles.IsCurrentCellDirty)
				dataGridViewOscToggles.CommitEdit(DataGridViewDataErrorContexts.Commit);
		}

		void dataGridViewOscFaders_CurrentCellDirtyStateChanged(object? sender, EventArgs e) {
			if (dataGridViewOscFaders.IsCurrentCellDirty)
				dataGridViewOscFaders.CommitEdit(DataGridViewDataErrorContexts.Commit);
		}

		void dataGridViewOscToggles_CellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e) {
			if (e.RowIndex < 0 || e.ColumnIndex < 0)
				return;
			DataGridViewRow row = dataGridViewOscToggles.Rows[e.RowIndex];
			if (IsOscToggleAddRow(row) || e.ColumnIndex is OSC_TOGGLE_CLEAR_COLUMN or OSC_TOGGLE_REMOVE_COLUMN)
				e.Cancel = true;
		}

		void dataGridViewOscFaders_CellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e) {
			if (e.RowIndex < 0 || e.ColumnIndex < 0)
				return;
			DataGridViewRow row = dataGridViewOscFaders.Rows[e.RowIndex];
			if (IsOscFaderAddRow(row) || e.ColumnIndex is OSC_FADER_CLEAR_MINUS_COLUMN or OSC_FADER_CLEAR_PLUS_COLUMN or OSC_FADER_REMOVE_COLUMN)
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
				SetHotkeyCellDisplay(dataGridViewOscToggles, e.RowIndex, OSC_TOGGLE_HOTKEY_COLUMN, "");
				ResetOscToggleHint();
				RefreshApplyButtonEnabled();
				return;
			}
			if (e.ColumnIndex == OSC_TOGGLE_REMOVE_COLUMN) {
				dataGridViewOscToggles.Rows.RemoveAt(e.RowIndex);
				EnsureOscToggleAddRow();
				ResetOscToggleHint();
				RefreshApplyButtonEnabled();
			}
		}

		void dataGridViewOscFaders_CellClick(object? sender, DataGridViewCellEventArgs e) {
			if (e.RowIndex < 0 || e.ColumnIndex < 0)
				return;
			DataGridViewRow row = dataGridViewOscFaders.Rows[e.RowIndex];
			if (IsOscFaderAddRow(row)) {
				if (e.ColumnIndex == OSC_FADER_REMOVE_COLUMN)
					AddOscFaderRow();
				return;
			}
			if (e.ColumnIndex == OSC_FADER_CLEAR_MINUS_COLUMN) {
				dataGridViewOscFaders.CurrentCell = row.Cells[OSC_FADER_HOTKEY_MINUS_COLUMN];
				SetHotkeyCellDisplay(dataGridViewOscFaders, e.RowIndex, OSC_FADER_HOTKEY_MINUS_COLUMN, "");
				ClearFaderTestFeedback();
				RefreshApplyButtonEnabled();
				return;
			}
			if (e.ColumnIndex == OSC_FADER_CLEAR_PLUS_COLUMN) {
				dataGridViewOscFaders.CurrentCell = row.Cells[OSC_FADER_HOTKEY_PLUS_COLUMN];
				SetHotkeyCellDisplay(dataGridViewOscFaders, e.RowIndex, OSC_FADER_HOTKEY_PLUS_COLUMN, "");
				ClearFaderTestFeedback();
				RefreshApplyButtonEnabled();
				return;
			}
			if (e.ColumnIndex == OSC_FADER_REMOVE_COLUMN) {
				dataGridViewOscFaders.Rows.RemoveAt(e.RowIndex);
				EnsureOscFaderAddRow();
				ClearFaderTestFeedback();
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

		void dataGridViewOscFaders_CellEndEdit(object? sender, DataGridViewCellEventArgs e) {
			DetachHotkeyEditingControl();
			if (e.RowIndex < 0 || e.ColumnIndex < 0)
				return;
			DataGridViewCell cell = dataGridViewOscFaders.Rows[e.RowIndex].Cells[e.ColumnIndex];
			string text = Convert.ToString(cell.Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
			if ((e.ColumnIndex == OSC_FADER_HOTKEY_MINUS_COLUMN || e.ColumnIndex == OSC_FADER_HOTKEY_PLUS_COLUMN)
			    && OscHotkey.TryParse(text, out Keys hk))
				text = OscHotkey.Format(hk);
			cell.Value = text;
			ClearFaderTestFeedback();
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

		void AddOscFaderRow(OscFaderBinding? binding = null) {
			int insertIndex = dataGridViewOscFaders.Rows.Count;
			if (insertIndex > 0 && IsOscFaderAddRow(dataGridViewOscFaders.Rows[insertIndex - 1]))
				insertIndex--;
			string s(float v) => v.ToString("G9", CultureInfo.InvariantCulture);
			object[] vals = binding == null
				? ["", "", "", "", "", "", "", "", "", ""]
				: [
					binding.Name,
					binding.Address,
					s(binding.Step),
					s(binding.Minimum),
					s(binding.Maximum),
					OscHotkey.Format(binding.HotkeyMinus),
					"",
					OscHotkey.Format(binding.HotkeyPlus),
					"",
					"",
				];
			dataGridViewOscFaders.Rows.Insert(insertIndex, vals);
			ConfigureOscFaderDataRow(dataGridViewOscFaders.Rows[insertIndex]);
			EnsureOscFaderAddRow();
			if (binding == null) {
				dataGridViewOscFaders.CurrentCell = dataGridViewOscFaders.Rows[insertIndex].Cells[OSC_FADER_NAME_COLUMN];
				dataGridViewOscFaders.BeginEdit(true);
			}
			ClearFaderTestFeedback();
			RefreshApplyButtonEnabled();
		}

		void SetHotkeyCellDisplay(DataGridView dgv, int rowIndex, int columnIndex, string text) {
			dgv.Rows[rowIndex].Cells[columnIndex].Value = text;
			if (_hotkeyEditingTextBox != null && dgv.CurrentCell != null
			    && dgv.CurrentCell.RowIndex == rowIndex && dgv.CurrentCell.ColumnIndex == columnIndex) {
				_hotkeyEditingTextBox.Text = text;
				_hotkeyEditingTextBox.SelectionStart = text.Length;
				_hotkeyEditingTextBox.SelectionLength = 0;
			}
		}

		void ClearHotkeyAtOwnerEditTarget() {
			if (_hotkeyOwnerGrid?.CurrentCell == null || _hotkeyOwnerColumn < 0)
				return;
			if (_hotkeyOwnerGrid.CurrentCell.ColumnIndex != _hotkeyOwnerColumn)
				return;
			int rowIndex = _hotkeyOwnerGrid.CurrentCell.RowIndex;
			if (rowIndex < 0)
				return;
			if (_hotkeyOwnerGrid == dataGridViewOscToggles && IsOscToggleAddRow(dataGridViewOscToggles.Rows[rowIndex]))
				return;
			if (_hotkeyOwnerGrid == dataGridViewOscFaders && IsOscFaderAddRow(dataGridViewOscFaders.Rows[rowIndex]))
				return;
			SetHotkeyCellDisplay(_hotkeyOwnerGrid, rowIndex, _hotkeyOwnerColumn, "");
			ResetOscToggleHint();
			RefreshApplyButtonEnabled();
		}

		void ApplyHotkeyToOwnerEditTarget(Keys hotkey) {
			if (_hotkeyOwnerGrid?.CurrentCell == null || _hotkeyOwnerColumn < 0)
				return;
			if (_hotkeyOwnerGrid.CurrentCell.ColumnIndex != _hotkeyOwnerColumn)
				return;
			int rowIndex = _hotkeyOwnerGrid.CurrentCell.RowIndex;
			if (rowIndex < 0)
				return;
			if (!OscHotkey.TryValidate(hotkey, out string error)) {
				ShowOscToggleError(error);
				RefreshApplyButtonEnabled();
				return;
			}
			SetHotkeyCellDisplay(_hotkeyOwnerGrid, rowIndex, _hotkeyOwnerColumn, OscHotkey.Format(OscHotkey.Normalize(hotkey)));
			ResetOscToggleHint();
			ClearFaderTestFeedback();
			RefreshApplyButtonEnabled();
		}

		void dataGridViewOscToggles_KeyDown(object? sender, KeyEventArgs e) {
			if (dataGridViewOscToggles.CurrentCell == null || dataGridViewOscToggles.IsCurrentCellInEditMode)
				return;
			if (IsOscToggleAddRow(dataGridViewOscToggles.Rows[dataGridViewOscToggles.CurrentCell.RowIndex]))
				return;
			if (dataGridViewOscToggles.CurrentCell.ColumnIndex != OSC_TOGGLE_HOTKEY_COLUMN)
				return;
			int r = dataGridViewOscToggles.CurrentCell.RowIndex;
			if (e.KeyCode is Keys.Delete or Keys.Back) {
				SetHotkeyCellDisplay(dataGridViewOscToggles, r, OSC_TOGGLE_HOTKEY_COLUMN, "");
				ResetOscToggleHint();
				RefreshApplyButtonEnabled();
				e.SuppressKeyPress = true;
				return;
			}
			if (OscHotkey.IsModifierKey(e.KeyCode)) {
				e.SuppressKeyPress = true;
				return;
			}
			Keys k = OscHotkey.Normalize(e.KeyData);
			if (!OscHotkey.TryValidate(k, out string err)) {
				ShowOscToggleError(err);
				RefreshApplyButtonEnabled();
				e.SuppressKeyPress = true;
				return;
			}
			SetHotkeyCellDisplay(dataGridViewOscToggles, r, OSC_TOGGLE_HOTKEY_COLUMN, OscHotkey.Format(k));
			ResetOscToggleHint();
			RefreshApplyButtonEnabled();
			e.SuppressKeyPress = true;
		}

		void dataGridViewOscFaders_KeyDown(object? sender, KeyEventArgs e) {
			if (dataGridViewOscFaders.CurrentCell == null || dataGridViewOscFaders.IsCurrentCellInEditMode)
				return;
			int c = dataGridViewOscFaders.CurrentCell.ColumnIndex;
			if (c != OSC_FADER_HOTKEY_MINUS_COLUMN && c != OSC_FADER_HOTKEY_PLUS_COLUMN)
				return;
			int r = dataGridViewOscFaders.CurrentCell.RowIndex;
			if (IsOscFaderAddRow(dataGridViewOscFaders.Rows[r]))
				return;
			if (e.KeyCode is Keys.Delete or Keys.Back) {
				SetHotkeyCellDisplay(dataGridViewOscFaders, r, c, "");
				ClearFaderTestFeedback();
				RefreshApplyButtonEnabled();
				e.SuppressKeyPress = true;
				return;
			}
			if (OscHotkey.IsModifierKey(e.KeyCode)) {
				e.SuppressKeyPress = true;
				return;
			}
			Keys k = OscHotkey.Normalize(e.KeyData);
			if (!OscHotkey.TryValidate(k, out string err)) {
				ShowOscToggleError(err);
				RefreshApplyButtonEnabled();
				e.SuppressKeyPress = true;
				return;
			}
			SetHotkeyCellDisplay(dataGridViewOscFaders, r, c, OscHotkey.Format(k));
			ClearFaderTestFeedback();
			RefreshApplyButtonEnabled();
			e.SuppressKeyPress = true;
		}

		void dataGridViewOscToggles_EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e) {
			DetachHotkeyEditingControl();
			if (dataGridViewOscToggles.CurrentCell?.ColumnIndex != OSC_TOGGLE_HOTKEY_COLUMN)
				return;
			if (e.Control is not TextBox tb)
				return;
			_hotkeyOwnerGrid = dataGridViewOscToggles;
			_hotkeyOwnerColumn = OSC_TOGGLE_HOTKEY_COLUMN;
			_hotkeyEditingTextBox = tb;
			_tray.SetOscToggleHotkeysEnabled(false);
			tb.ReadOnly = true;
			tb.ShortcutsEnabled = false;
			tb.KeyDown += HotkeyTextBox_KeyDown;
			tb.KeyPress += HotkeyTextBox_KeyPress;
		}

		void dataGridViewOscFaders_EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e) {
			DetachHotkeyEditingControl();
			int col = dataGridViewOscFaders.CurrentCell?.ColumnIndex ?? -1;
			if (col != OSC_FADER_HOTKEY_MINUS_COLUMN && col != OSC_FADER_HOTKEY_PLUS_COLUMN)
				return;
			if (e.Control is not TextBox tb)
				return;
			_hotkeyOwnerGrid = dataGridViewOscFaders;
			_hotkeyOwnerColumn = col;
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
				ClearHotkeyAtOwnerEditTarget();
				e.SuppressKeyPress = true;
				return;
			}
			if (OscHotkey.IsModifierKey(e.KeyCode)) {
				e.SuppressKeyPress = true;
				return;
			}
			ApplyHotkeyToOwnerEditTarget(OscHotkey.Normalize(e.KeyData));
			e.SuppressKeyPress = true;
		}

		static void HotkeyTextBox_KeyPress(object? sender, KeyPressEventArgs e) => e.Handled = true;

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
			if (!TryReadUiForApply(out OscController.Config newCfg, out string? ipErr, out string? portErr)) {
				if (ipErr != null) {
					labelNetworkFeedback.Text = ipErr;
					labelNetworkFeedback.ForeColor = Color.Red;
				}
				if (portErr != null) {
					labelOscBaseFeedback.Text = portErr;
					labelOscBaseFeedback.ForeColor = Color.Red;
				}
				return;
			}
			if (!TryReadOscFaderBindings(out List<OscFaderBinding> oscFaders, out string? faderGridError)) {
				labelFaderTestResult.Text = faderGridError ?? "OSC faders are invalid.";
				labelFaderTestResult.ForeColor = Color.Red;
				return;
			}
			if (!TryReadOscToggleBindings(out List<OscToggleBinding> oscToggles, out string? toggleError)) {
				ShowOscToggleError(toggleError ?? "OSC toggles are invalid.");
				return;
			}
			if (!TryValidateHotkeysGlobally(oscFaders, oscToggles, out string? hkError)) {
				ShowOscToggleError(hkError ?? "Duplicate hotkey.");
				return;
			}
			string testFaderPath = OscController.NormalizeBindingAddress(oscFaders[0].Address);
			buttonSaveAndTest.Enabled = false;
			SetConnectionInputsEnabled(false);
			try {
				var appConfig = new AppConfig {
					OscController = newCfg,
					Mixer = new MixerController.Config {
						ValueCacheTtlMs = (uint)numericUpDownFaderVolumeCacheTtlMs.Value,
					},
					TrayApp = new TrayApp.Config {
						FaderBindings = oscFaders.Select(f => new OscFaderBinding(f)).ToList(),
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
				labelFaderTestResult.Text = $"Testing first fader row ({testFaderPath}) …";
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
					float? fader = await _mixer.QueryFaderAsync(testFaderPath).ConfigureAwait(false);
					swFader.Stop();
					TimeSpan faderElapsed = swFader.Elapsed;
					postUi(() => {
						if (IsDisposed) return;
						if (fader != null) {
							string lat = $"{faderElapsed.TotalMilliseconds:0} ms";
							string val = fader.Value.ToString("0.###", CultureInfo.InvariantCulture);
							labelFaderTestResult.Text = $"Fader OK (first row) — latency {lat}, current value {val}";
							labelFaderTestResult.ForeColor = Color.Green;
						} else {
							labelFaderTestResult.Text = "Couldn't query the first fader row.";
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
