using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Windows.Forms;

namespace WindowsOscVolumeControl {
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
		readonly TrayController _trayController;
		readonly global::Application _tray;
		readonly ConfigStore _configStore;
		readonly ResourceLoader _resources;
		TextBox? _hotkeyEditingTextBox;
		DataGridView? _hotkeyOwnerGrid;
		int _hotkeyOwnerColumn = -1;

		public ConfigForm(MixerController mixer, TrayController trayController, global::Application tray, ConfigStore configStore, ResourceLoader resources) {
			InitializeComponent();
			ApplyConfigNumericBoundsFromPolicy();
			this._mixer = mixer;
			this._trayController = trayController;
			this._tray = tray;
			this._configStore = configStore;
			_resources = resources;
			HookNumericUpDownEditTextChanged(numericUpDownQueryTimeoutMs, QueryTimeoutNudEdit_TextChanged);
			HookNumericUpDownEditTextChanged(numericUpDownFaderVolumeCacheTtlMs, VolumeCacheNudEdit_TextChanged);
			HookNumericUpDownEditTextChanged(numericUpDownOsdHeightPx, OsdHeightNudEdit_TextChanged);
			HookNumericUpDownEditTextChanged(numericUpDownOsdDisplayDurationMs, OsdDurationNudEdit_TextChanged);
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
			_tray.setOscToggleHotkeysEnabled(true);
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
			numericUpDownQueryTimeoutMs.Minimum = OscController.Config.MIN_QUERY_TIMEOUT_MS;
			numericUpDownQueryTimeoutMs.Maximum = OscController.Config.MAX_QUERY_TIMEOUT_MS;
			numericUpDownFaderVolumeCacheTtlMs.Minimum = MixerController.Config.MIN_VALUE_CACHE_TTL_MS;
			numericUpDownFaderVolumeCacheTtlMs.Maximum = MixerController.Config.MAX_VALUE_CACHE_TTL_MS;
			numericUpDownOsdHeightPx.Minimum = OSDController.Config.MIN_HEIGHT_PX;
			numericUpDownOsdHeightPx.Maximum = OSDController.Config.MAX_HEIGHT_PX;
			numericUpDownOsdDisplayDurationMs.Minimum = OSDController.Config.MIN_DISPLAY_DURATION_MS;
			numericUpDownOsdDisplayDurationMs.Maximum = OSDController.Config.MAX_DISPLAY_DURATION_MS;
		}

		void SetupConfigStoreUi() {
			textBoxConfigStorePath.Text = _configStore.configPath;
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
			labelConfigStoreFeedback.Text = _configStore.lastDiskFeedback;
			labelConfigStoreFeedback.ForeColor = _configStore.lastDiskOutcome switch {
				AppConfigDiskOutcome.NONE => SystemColors.GrayText,
				AppConfigDiskOutcome.NO_FILE_USING_DEFAULTS => Color.SteelBlue,
				AppConfigDiskOutcome.LOADED_OK => Color.Green,
				AppConfigDiskOutcome.LOADED_PARTIAL => Color.DarkOrange,
				AppConfigDiskOutcome.LOAD_IO_ERROR => Color.Red,
				AppConfigDiskOutcome.SAVED_OK => Color.Green,
				AppConfigDiskOutcome.SAVE_FAILED => Color.Red,
				_ => SystemColors.GrayText,
			};
		}

		void buttonOpenConfigStoreFolder_Click(object? sender, EventArgs e) {
			string path = _configStore.configPath;
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

		void SyncTitlebarIconFromTray() => Icon = _trayController.TrayIconSnapshot;

		static void SetNumericUpDownClamped(NumericUpDown nud, uint value) {
			if (value < (uint)nud.Minimum) value = (uint)nud.Minimum;
			if (value > (uint)nud.Maximum) value = (uint)nud.Maximum;
			nud.Value = value;
		}

		static void SetOsdHeightNudClamped(NumericUpDown nud, int value) {
			value = Math.Clamp(value, OSDController.Config.MIN_HEIGHT_PX, OSDController.Config.MAX_HEIGHT_PX);
			nud.Value = value;
		}

		static void ClearFeedbackLabel(Label label) {
			label.Text = "";
			label.ForeColor = Color.Black;
		}

		void LoadFieldsFromOsc() {
			OscController.Config c = new(_configStore.appConfig.oscController);
			textBoxIP.Text = c.endPoint.Address.ToString();
			textBoxPort.Text = c.endPoint.Port.ToString();
			SetNumericUpDownClamped(numericUpDownQueryTimeoutMs, c.timeoutMs);
			uint ttl = Math.Min(_configStore.appConfig.mixer.ValueCacheTtlMs, MixerController.Config.MAX_VALUE_CACHE_TTL_MS);
			SetNumericUpDownClamped(numericUpDownFaderVolumeCacheTtlMs, ttl);
			OSDController.Config osd = OSDController.Config.Clamped(_configStore.appConfig.osd);
			SetOsdHeightNudClamped(numericUpDownOsdHeightPx, osd.HeightPx);
			SetNumericUpDownClamped(numericUpDownOsdDisplayDurationMs, osd.DisplayDurationMs);
			RefreshQueryTimeoutAndVolumeCacheColors();
			LoadOscFaderBindings();
			LoadOscToggleBindings();
		}

		void LoadOscFaderBindings() {
			dataGridViewOscFaders.Rows.Clear();
			foreach (BindingFader binding in _configStore.appConfig.trayApp?.faderBindings ?? [])
				AddOscFaderRow(new BindingFader(binding));
			EnsureOscFaderAddRow();
		}

		void LoadOscToggleBindings() {
			dataGridViewOscToggles.Rows.Clear();
			foreach (BindingToggle binding in _configStore.appConfig.trayApp?.bindings ?? [])
				AddOscToggleRow(new BindingToggle(binding));
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

		bool IpFieldOk() => OscConnectionConfigParse.isIpFieldSyntaxOk(textBoxIP.Text);

		bool PortFieldOk() => OscConnectionConfigParse.isPortFieldSyntaxOk(textBoxPort.Text);

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

		bool OsdHeightFieldOk() => NumericUpDownTextInRange(numericUpDownOsdHeightPx);

		bool OsdDisplayDurationFieldOk() => NumericUpDownTextInRange(numericUpDownOsdDisplayDurationMs);

		static string GridCellText(DataGridViewRow row, int columnIndex) => Convert.ToString(row.Cells[columnIndex].Value, CultureInfo.InvariantCulture)?.Trim() ?? "";

		void RefreshQueryTimeoutAndVolumeCacheColors() {
			numericUpDownQueryTimeoutMs.ForeColor = QueryTimeoutFieldOk() ? Color.Black : Color.Red;
			numericUpDownFaderVolumeCacheTtlMs.ForeColor = VolumeCacheFieldOk() ? Color.Black : Color.Red;
			numericUpDownOsdHeightPx.ForeColor = OsdHeightFieldOk() ? Color.Black : Color.Red;
			numericUpDownOsdDisplayDurationMs.ForeColor = OsdDisplayDurationFieldOk() ? Color.Black : Color.Red;
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
			if (!KeysUtil.tryParse(text, out hotkey)) {
				error = "Invalid hotkey.";
				return false;
			}
			if (!KeysUtil.tryValidate(hotkey, out string hotkeyError)) {
				error = hotkeyError;
				return false;
			}
			hotkey = KeysUtil.normalize(hotkey);
			return true;
		}

		bool TryReadOscFaderBindings(out List<BindingFader> bindings, out string? error) {
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
				step = Math.Clamp(FaderFloatUtil.RoundToBindingDecimals(step), MixerController.Config.MIN_FADER_STEP, MixerController.Config.MAX_FADER_STEP);
				min = FaderFloatUtil.RoundToBindingDecimals(min);
				max = FaderFloatUtil.RoundToBindingDecimals(max);
				if (min > max) {
					error = $"OSC fader row {rowIndex + 1}: Minimum must be ≤ Maximum after rounding.";
					return false;
				}
				_ = OscController.NormalizeBindingAddress(address);
				if (!TryParseOptionalHotkey(hkMinusS, out Keys hkMinus, out string? em)) {
					error = $"OSC fader row {rowIndex + 1} (−): {em}";
					return false;
				}
				if (!TryParseOptionalHotkey(hkPlusS, out Keys hkPlus, out string? ep)) {
					error = $"OSC fader row {rowIndex + 1} (+): {ep}";
					return false;
				}
				bindings.Add(new BindingFader {
					name = name,
					address = address,
					step = step,
					minimum = min,
					maximum = max,
					hotkeyMinus = hkMinus,
					hotkeyPlus = hkPlus,
				});
			}
			if (bindings.Count == 0) {
				error = "Add at least one OSC fader row (Name, Address, Step, Minimum, Maximum).";
				return false;
			}
			return true;
		}

		bool TryReadOscToggleBindings(out List<BindingToggle> bindings, out string? error) {
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
				if (!KeysUtil.tryParse(hotkeyText, out Keys hotkey)) {
					error = $"OSC toggle row {rowIndex + 1} has an invalid hotkey.";
					return false;
				}
				if (!KeysUtil.tryValidate(hotkey, out string hotkeyError)) {
					error = $"OSC toggle row {rowIndex + 1}: {hotkeyError}";
					return false;
				}
				hotkey = KeysUtil.normalize(hotkey);
				bindings.Add(new BindingToggle {
					name = name,
					address = address,
					hotkey = hotkey,
				});
			}
			return true;
		}

		static bool TryValidateHotkeysGlobally(IReadOnlyList<BindingFader> faders, IReadOnlyList<BindingToggle> toggles, out string? error) {
			error = null;
			var claimed = new Dictionary<Keys, string>();
			for (int i = 0; i < faders.Count; i++) {
				BindingFader f = faders[i];
				string rowLabel = $"OSC fader row {i + 1}";
				if (f.hotkeyMinus != Keys.None) {
					Keys k = KeysUtil.normalize(f.hotkeyMinus);
					if (claimed.TryGetValue(k, out string? prev)) {
						error = $"{rowLabel} hotkey − ({KeysUtil.format(k)}) conflicts with {prev}.";
						return false;
					}
					claimed[k] = $"{rowLabel} (−)";
				}
				if (f.hotkeyPlus != Keys.None) {
					Keys k = KeysUtil.normalize(f.hotkeyPlus);
					if (claimed.TryGetValue(k, out string? prev)) {
						error = $"{rowLabel} hotkey + ({KeysUtil.format(k)}) conflicts with {prev}.";
						return false;
					}
					claimed[k] = $"{rowLabel} (+)";
				}
			}
			for (int i = 0; i < toggles.Count; i++) {
				BindingToggle t = toggles[i];
				Keys k = KeysUtil.normalize(t.hotkey);
				if (claimed.TryGetValue(k, out string? prev)) {
					error = $"OSC toggle \"{t.name}\" ({KeysUtil.format(k)}) conflicts with {prev}.";
					return false;
				}
				claimed[k] = $"OSC toggle \"{t.name}\"";
			}
			return true;
		}

		bool OscFaderListOk() => TryReadOscFaderBindings(out _, out _);

		bool OscToggleListOk() => TryReadOscToggleBindings(out _, out _);

		bool ApplyFormOk() {
			if (!IpFieldOk() || !PortFieldOk() || !QueryTimeoutFieldOk() || !VolumeCacheFieldOk()
			    || !OsdHeightFieldOk() || !OsdDisplayDurationFieldOk())
				return false;
			if (!TryReadOscFaderBindings(out List<BindingFader> faders, out _))
				return false;
			if (!TryReadOscToggleBindings(out List<BindingToggle> toggles, out _))
				return false;
			return TryValidateHotkeysGlobally(faders, toggles, out _);
		}

		void RefreshApplyButtonEnabled() => buttonSaveAndTest.Enabled = ApplyFormOk();

		void SetConnectionInputsEnabled(bool enabled) {
			textBoxIP.Enabled = enabled;
			textBoxPort.Enabled = enabled;
			numericUpDownFaderVolumeCacheTtlMs.Enabled = enabled;
			numericUpDownQueryTimeoutMs.Enabled = enabled;
			numericUpDownOsdHeightPx.Enabled = enabled;
			numericUpDownOsdDisplayDurationMs.Enabled = enabled;
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
			if (!OscConnectionConfigParse.tryParseIpPort(textBoxIP.Text, textBoxPort.Text,
				    out IPAddress ip, out int port, out ipError, out portError))
				return false;
			cfg = new OscController.Config {
				endPoint = new IPEndPoint(ip, port),
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
			if (e.ColumnIndex == OSC_TOGGLE_HOTKEY_COLUMN && KeysUtil.tryParse(text, out Keys hotkey))
				text = KeysUtil.format(hotkey);
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
			if (e.ColumnIndex is OSC_FADER_STEP_COLUMN or OSC_FADER_MIN_COLUMN or OSC_FADER_MAX_COLUMN) {
				if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv) && float.IsFinite(fv))
					text = FaderFloatUtil.FormatGridFloat(fv);
			} else if ((e.ColumnIndex == OSC_FADER_HOTKEY_MINUS_COLUMN || e.ColumnIndex == OSC_FADER_HOTKEY_PLUS_COLUMN)
			           && KeysUtil.tryParse(text, out Keys hk)) {
				text = KeysUtil.format(hk);
			}
			cell.Value = text;
			ClearFaderTestFeedback();
			RefreshApplyButtonEnabled();
		}

		void AddOscToggleRow(BindingToggle? binding = null) {
			int insertIndex = dataGridViewOscToggles.Rows.Count;
			if (insertIndex > 0 && IsOscToggleAddRow(dataGridViewOscToggles.Rows[insertIndex - 1]))
				insertIndex--;
			dataGridViewOscToggles.Rows.Insert(insertIndex, binding?.name ?? "", binding?.address ?? "", binding != null ? KeysUtil.format(binding.hotkey) : "", "", "");
			ConfigureOscToggleDataRow(dataGridViewOscToggles.Rows[insertIndex]);
			EnsureOscToggleAddRow();
			if (binding == null) {
				dataGridViewOscToggles.CurrentCell = dataGridViewOscToggles.Rows[insertIndex].Cells[OSC_TOGGLE_NAME_COLUMN];
				dataGridViewOscToggles.BeginEdit(true);
			}
			ResetOscToggleHint();
			RefreshApplyButtonEnabled();
		}

		void AddOscFaderRow(BindingFader? binding = null) {
			int insertIndex = dataGridViewOscFaders.Rows.Count;
			if (insertIndex > 0 && IsOscFaderAddRow(dataGridViewOscFaders.Rows[insertIndex - 1]))
				insertIndex--;
			string s(float v) => FaderFloatUtil.FormatGridFloat(v);
			object[] vals = binding == null
				? ["", "", "", "", "", "", "", "", "", ""]
				: [
					binding.name,
					binding.address,
					s(binding.step),
					s(binding.minimum),
					s(binding.maximum),
					KeysUtil.format(binding.hotkeyMinus),
					"",
					KeysUtil.format(binding.hotkeyPlus),
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
			if (!KeysUtil.tryValidate(hotkey, out string error)) {
				ShowOscToggleError(error);
				RefreshApplyButtonEnabled();
				return;
			}
			SetHotkeyCellDisplay(_hotkeyOwnerGrid, rowIndex, _hotkeyOwnerColumn, KeysUtil.format(KeysUtil.normalize(hotkey)));
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
			if (KeysUtil.isModifierKey(e.KeyCode)) {
				e.SuppressKeyPress = true;
				return;
			}
			Keys k = KeysUtil.normalize(e.KeyData);
			if (!KeysUtil.tryValidate(k, out string err)) {
				ShowOscToggleError(err);
				RefreshApplyButtonEnabled();
				e.SuppressKeyPress = true;
				return;
			}
			SetHotkeyCellDisplay(dataGridViewOscToggles, r, OSC_TOGGLE_HOTKEY_COLUMN, KeysUtil.format(k));
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
			if (KeysUtil.isModifierKey(e.KeyCode)) {
				e.SuppressKeyPress = true;
				return;
			}
			Keys k = KeysUtil.normalize(e.KeyData);
			if (!KeysUtil.tryValidate(k, out string err)) {
				ShowOscToggleError(err);
				RefreshApplyButtonEnabled();
				e.SuppressKeyPress = true;
				return;
			}
			SetHotkeyCellDisplay(dataGridViewOscFaders, r, c, KeysUtil.format(k));
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
			_tray.setOscToggleHotkeysEnabled(false);
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
			_tray.setOscToggleHotkeysEnabled(false);
			tb.ReadOnly = true;
			tb.ShortcutsEnabled = false;
			tb.KeyDown += HotkeyTextBox_KeyDown;
			tb.KeyPress += HotkeyTextBox_KeyPress;
		}

		protected override void OnFormClosed(FormClosedEventArgs e) {
			_tray.setOscToggleHotkeysEnabled(true);
			DetachHotkeyEditingControl();
			base.OnFormClosed(e);
		}

		void HotkeyTextBox_KeyDown(object? sender, KeyEventArgs e) {
			if (e.KeyCode is Keys.Delete or Keys.Back) {
				ClearHotkeyAtOwnerEditTarget();
				e.SuppressKeyPress = true;
				return;
			}
			if (KeysUtil.isModifierKey(e.KeyCode)) {
				e.SuppressKeyPress = true;
				return;
			}
			ApplyHotkeyToOwnerEditTarget(KeysUtil.normalize(e.KeyData));
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

		private void numericUpDownOsdHeightPx_ValueChanged(object? sender, EventArgs e) {
			RefreshQueryTimeoutAndVolumeCacheColors();
			RefreshApplyButtonEnabled();
		}

		private void numericUpDownOsdDisplayDurationMs_ValueChanged(object? sender, EventArgs e) {
			RefreshQueryTimeoutAndVolumeCacheColors();
			RefreshApplyButtonEnabled();
		}

		void OsdHeightNudEdit_TextChanged(object? sender, EventArgs e) {
			RefreshQueryTimeoutAndVolumeCacheColors();
			RefreshApplyButtonEnabled();
		}

		void OsdDurationNudEdit_TextChanged(object? sender, EventArgs e) {
			RefreshQueryTimeoutAndVolumeCacheColors();
			RefreshApplyButtonEnabled();
		}

		private void ConfigForm_Load(object sender, EventArgs e) {
			PositionNearBottomRightOfWorkingArea();
			SyncTitlebarIconFromTray();
			RefreshAutostartFeedback();
			RefreshConfigStoreDiskFeedback();
		}

		void PositionNearBottomRightOfWorkingArea() {
			const int MARGIN_PX = 12;
			Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
			StartPosition = FormStartPosition.Manual;
			int x = wa.Right - Width - MARGIN_PX;
			int y = wa.Bottom - Height - MARGIN_PX;
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
			if (!TryReadOscFaderBindings(out List<BindingFader> oscFaders, out string? faderGridError)) {
				labelFaderTestResult.Text = faderGridError ?? "OSC faders are invalid.";
				labelFaderTestResult.ForeColor = Color.Red;
				return;
			}
			if (!TryReadOscToggleBindings(out List<BindingToggle> oscToggles, out string? toggleError)) {
				ShowOscToggleError(toggleError ?? "OSC toggles are invalid.");
				return;
			}
			if (!TryValidateHotkeysGlobally(oscFaders, oscToggles, out string? hkError)) {
				ShowOscToggleError(hkError ?? "Duplicate hotkey.");
				return;
			}
			string testFaderPath = OscController.NormalizeBindingAddress(oscFaders[0].address);
			buttonSaveAndTest.Enabled = false;
			SetConnectionInputsEnabled(false);
			try {
				var appConfig = new AppConfig {
					oscController = newCfg,
					mixer = new MixerController.Config {
						ValueCacheTtlMs = (uint)numericUpDownFaderVolumeCacheTtlMs.Value,
					},
					trayApp = new BindingManager.Config {
						faderBindings = oscFaders.Select(f => new BindingFader(f)).ToList(),
						bindings = oscToggles.Select(b => new BindingToggle(b)).ToList(),
					},
					osd = OSDController.Config.Clamped(new OSDController.Config {
						HeightPx = (int)numericUpDownOsdHeightPx.Value,
						DisplayDurationMs = (uint)numericUpDownOsdDisplayDurationMs.Value,
					}),
				};
				_tray.commitConfigFromSettingsForm(appConfig);
				ResetOscToggleHint();
				RefreshConfigStoreDiskFeedback();
				Icon = _tray.applyTrayIconState(AppTrayIconState.STARTING_OR_INVALID_CONFIG);
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
				Task<(string text, Color color)> pingTask = NetworkPingTest.PingFeedbackAsync(newCfg.endPoint.Address, timeoutMs: pingTimeoutMs, probeProgress: pingUi);

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
							string val = FaderFloatUtil.FormatGridFloat(fader.Value);
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
					Icon = _tray.applyTrayIconState(AppTrayIconState.OK);
				else
					Icon = _tray.applyTrayIconState(AppTrayIconState.NETWORK_ERROR, showErrorOsdIfNotOk: false);
			} catch (Exception ex) {
				textBoxInfoResult.Text = "";
				labelOscBaseFeedback.Text = "Error: " + ex.Message;
				labelOscBaseFeedback.ForeColor = Color.Red;
				labelFaderTestResult.Text = "";
				Icon = _tray.applyTrayIconState(AppTrayIconState.NETWORK_ERROR, showErrorOsdIfNotOk: false);
			} finally {
				SetConnectionInputsEnabled(true);
				RefreshApplyButtonEnabled();
				RefreshConfigStoreDiskFeedback();
			}
		}
	}
}
