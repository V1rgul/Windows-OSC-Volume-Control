namespace X32VolumeHijacker
{
	partial class ConfigForm
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			labelIp = new Label();
			textBoxIP = new TextBox();
			labelPort = new Label();
			textBoxPort = new TextBox();
			buttonSaveAndTest = new Button();
			labelFaderTestResult = new Label();
			labelNetworkFeedback = new Label();
			labelOscBaseFeedback = new Label();
			textBoxInfoResult = new TextBox();
			groupBoxAutostart = new GroupBox();
			tableLayoutAutostartOuter = new TableLayoutPanel();
			tableLayoutPanelAutostart = new TableLayoutPanel();
			buttonAutostartRegister = new Button();
			buttonAutostartDeregister = new Button();
			labelAutostartFeedback = new Label();
			groupBoxConfigStore = new GroupBox();
			tableLayoutConfigStore = new TableLayoutPanel();
			textBoxConfigStorePath = new TextBox();
			buttonOpenConfigStoreFolder = new Button();
			labelConfigStoreFeedback = new Label();
			toolTipConfigStore = new ToolTip();
			groupBoxNetwork = new GroupBox();
			tableLayoutNetwork = new TableLayoutPanel();
			tableLayoutQueryTimeout = new TableLayoutPanel();
			labelQueryTimeoutMs = new Label();
			numericUpDownQueryTimeoutMs = new NumericUpDown();
			labelQueryTimeoutUnitMs = new Label();
			groupBoxOscBase = new GroupBox();
			tableLayoutOscBase = new TableLayoutPanel();
			groupBoxFader = new GroupBox();
			tableLayoutFader = new TableLayoutPanel();
			tableLayoutVolumeCache = new TableLayoutPanel();
			dataGridViewOscFaders = new DataGridView();
			columnOscFaderName = new DataGridViewTextBoxColumn();
			columnOscFaderAddress = new DataGridViewTextBoxColumn();
			columnOscFaderStep = new DataGridViewTextBoxColumn();
			columnOscFaderMinimum = new DataGridViewTextBoxColumn();
			columnOscFaderMaximum = new DataGridViewTextBoxColumn();
			columnOscFaderHotkeyMinus = new DataGridViewTextBoxColumn();
			columnOscFaderClearMinus = new DataGridViewImageColumn();
			columnOscFaderHotkeyPlus = new DataGridViewTextBoxColumn();
			columnOscFaderClearPlus = new DataGridViewImageColumn();
			columnOscFaderRemove = new DataGridViewImageColumn();
			labelFaderVolumeCacheTtlMs = new Label();
			numericUpDownFaderVolumeCacheTtlMs = new NumericUpDown();
			labelFaderVolumeCacheUnitMs = new Label();
			groupBoxOscToggles = new GroupBox();
			tableLayoutOscToggles = new TableLayoutPanel();
			dataGridViewOscToggles = new DataGridView();
			columnOscToggleName = new DataGridViewTextBoxColumn();
			columnOscToggleAddress = new DataGridViewTextBoxColumn();
			columnOscToggleHotkey = new DataGridViewTextBoxColumn();
			columnOscToggleClearHotkey = new DataGridViewImageColumn();
			columnOscToggleRemove = new DataGridViewImageColumn();
			labelOscTogglesHint = new Label();
			tableLayoutMain = new TableLayoutPanel();
			groupBoxAutostart.SuspendLayout();
			tableLayoutAutostartOuter.SuspendLayout();
			tableLayoutPanelAutostart.SuspendLayout();
			groupBoxConfigStore.SuspendLayout();
			tableLayoutConfigStore.SuspendLayout();
			groupBoxNetwork.SuspendLayout();
			tableLayoutNetwork.SuspendLayout();
			tableLayoutQueryTimeout.SuspendLayout();
			groupBoxOscBase.SuspendLayout();
			tableLayoutOscBase.SuspendLayout();
			groupBoxFader.SuspendLayout();
			tableLayoutFader.SuspendLayout();
			tableLayoutVolumeCache.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)dataGridViewOscFaders).BeginInit();
			groupBoxOscToggles.SuspendLayout();
			tableLayoutOscToggles.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)dataGridViewOscToggles).BeginInit();
			tableLayoutMain.SuspendLayout();
			SuspendLayout();
			// 
			// labelIp
			// 
			labelIp.AutoSize = true;
			labelIp.Dock = DockStyle.Fill;
			labelIp.Location = new Point(0, 0);
			labelIp.Margin = new Padding(0, 0, 6, 0);
			labelIp.Name = "labelIp";
			labelIp.Size = new Size(24, 27);
			labelIp.TabIndex = 0;
			labelIp.Text = "IP:";
			labelIp.TextAlign = ContentAlignment.MiddleLeft;
			// 
			// textBoxIP
			// 
			textBoxIP.Dock = DockStyle.Fill;
			textBoxIP.Location = new Point(30, 3);
			textBoxIP.Margin = new Padding(0, 3, 0, 3);
			textBoxIP.Name = "textBoxIP";
			textBoxIP.Size = new Size(359, 27);
			textBoxIP.TabIndex = 1;
			textBoxIP.Text = "192.168.2.3";
			textBoxIP.TextChanged += textBoxIP_TextChanged;
			// 
			// labelPort
			// 
			labelPort.AutoSize = true;
			labelPort.Dock = DockStyle.Fill;
			labelPort.Location = new Point(0, 0);
			labelPort.Margin = new Padding(0, 0, 6, 0);
			labelPort.Name = "labelPort";
			labelPort.Size = new Size(40, 27);
			labelPort.TabIndex = 0;
			labelPort.Text = "Port:";
			labelPort.TextAlign = ContentAlignment.MiddleLeft;
			// 
			// textBoxPort
			// 
			textBoxPort.Dock = DockStyle.Fill;
			textBoxPort.Location = new Point(46, 3);
			textBoxPort.Margin = new Padding(0, 3, 0, 3);
			textBoxPort.Name = "textBoxPort";
			textBoxPort.Size = new Size(343, 27);
			textBoxPort.TabIndex = 1;
			textBoxPort.Text = "10023";
			textBoxPort.TextChanged += textBoxPort_TextChanged;
			// 
			// buttonSaveAndTest
			// 
			buttonSaveAndTest.Dock = DockStyle.Fill;
			buttonSaveAndTest.Location = new Point(3, 3);
			buttonSaveAndTest.Margin = new Padding(0, 0, 0, 0);
			buttonSaveAndTest.Name = "buttonSaveAndTest";
			buttonSaveAndTest.Size = new Size(407, 29);
			buttonSaveAndTest.TabIndex = 4;
			buttonSaveAndTest.Text = "Apply, save and test";
			buttonSaveAndTest.UseVisualStyleBackColor = true;
			buttonSaveAndTest.Click += buttonSaveAndTest_Click;
			// 
			// labelFaderTestResult
			// 
			labelFaderTestResult.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			labelFaderTestResult.AutoSize = true;
			labelFaderTestResult.Location = new Point(3, 36);
			labelFaderTestResult.Margin = new Padding(3, 6, 3, 0);
			labelFaderTestResult.Name = "labelFaderTestResult";
			labelFaderTestResult.Size = new Size(389, 20);
			labelFaderTestResult.TabIndex = 5;
			labelFaderTestResult.Text = "";
			labelFaderTestResult.TextAlign = ContentAlignment.MiddleLeft;
			// 
			// labelNetworkFeedback
			// 
			labelNetworkFeedback.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			labelNetworkFeedback.AutoSize = true;
			labelNetworkFeedback.Location = new Point(3, 33);
			labelNetworkFeedback.Margin = new Padding(3, 0, 3, 0);
			labelNetworkFeedback.Name = "labelNetworkFeedback";
			labelNetworkFeedback.Size = new Size(389, 20);
			labelNetworkFeedback.TabIndex = 2;
			labelNetworkFeedback.Text = "";
			labelNetworkFeedback.TextAlign = ContentAlignment.MiddleLeft;
			// 
			// labelOscBaseFeedback
			// 
			labelOscBaseFeedback.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			labelOscBaseFeedback.AutoSize = true;
			labelOscBaseFeedback.Location = new Point(3, 33);
			labelOscBaseFeedback.Margin = new Padding(3, 0, 3, 0);
			labelOscBaseFeedback.Name = "labelOscBaseFeedback";
			labelOscBaseFeedback.Size = new Size(389, 20);
			labelOscBaseFeedback.TabIndex = 2;
			labelOscBaseFeedback.Text = "";
			labelOscBaseFeedback.TextAlign = ContentAlignment.MiddleLeft;
			// 
			// textBoxInfoResult
			// 
			textBoxInfoResult.Dock = DockStyle.Fill;
			textBoxInfoResult.Location = new Point(3, 56);
			textBoxInfoResult.Margin = new Padding(3, 3, 3, 0);
			textBoxInfoResult.MinimumSize = new Size(0, 72);
			textBoxInfoResult.Multiline = true;
			textBoxInfoResult.Name = "textBoxInfoResult";
			textBoxInfoResult.ReadOnly = true;
			textBoxInfoResult.ScrollBars = ScrollBars.Vertical;
			textBoxInfoResult.Size = new Size(389, 96);
			textBoxInfoResult.TabIndex = 3;
			textBoxInfoResult.TabStop = false;
			// 
			// groupBoxAutostart
			// 
			groupBoxAutostart.Controls.Add(tableLayoutAutostartOuter);
			groupBoxAutostart.Dock = DockStyle.Fill;
			groupBoxAutostart.Location = new Point(3, 3);
			groupBoxAutostart.Margin = new Padding(3, 3, 3, 6);
			groupBoxAutostart.Name = "groupBoxAutostart";
			groupBoxAutostart.Padding = new Padding(3, 3, 3, 3);
			groupBoxAutostart.Size = new Size(413, 100);
			groupBoxAutostart.TabIndex = 6;
			groupBoxAutostart.TabStop = false;
			groupBoxAutostart.Text = "Autostart";
			// 
			// tableLayoutAutostartOuter
			// 
			tableLayoutAutostartOuter.ColumnCount = 1;
			tableLayoutAutostartOuter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			tableLayoutAutostartOuter.Controls.Add(tableLayoutPanelAutostart, 0, 0);
			tableLayoutAutostartOuter.Controls.Add(labelAutostartFeedback, 0, 1);
			tableLayoutAutostartOuter.Dock = DockStyle.Fill;
			tableLayoutAutostartOuter.Location = new Point(3, 23);
			tableLayoutAutostartOuter.Name = "tableLayoutAutostartOuter";
			tableLayoutAutostartOuter.RowCount = 2;
			tableLayoutAutostartOuter.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutAutostartOuter.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutAutostartOuter.Size = new Size(407, 74);
			tableLayoutAutostartOuter.TabIndex = 0;
			// 
			// tableLayoutPanelAutostart
			// 
			tableLayoutPanelAutostart.ColumnCount = 2;
			tableLayoutPanelAutostart.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
			tableLayoutPanelAutostart.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
			tableLayoutPanelAutostart.Controls.Add(buttonAutostartRegister, 0, 0);
			tableLayoutPanelAutostart.Controls.Add(buttonAutostartDeregister, 1, 0);
			tableLayoutPanelAutostart.Dock = DockStyle.Fill;
			tableLayoutPanelAutostart.Location = new Point(0, 0);
			tableLayoutPanelAutostart.Margin = new Padding(0, 0, 0, 3);
			tableLayoutPanelAutostart.Name = "tableLayoutPanelAutostart";
			tableLayoutPanelAutostart.RowCount = 1;
			tableLayoutPanelAutostart.RowStyles.Add(new RowStyle(SizeType.Absolute, 33F));
			tableLayoutPanelAutostart.Size = new Size(407, 33);
			tableLayoutPanelAutostart.TabIndex = 0;
			// 
			// buttonAutostartRegister
			// 
			buttonAutostartRegister.Dock = DockStyle.Fill;
			buttonAutostartRegister.Margin = new Padding(0, 4, 3, 0);
			buttonAutostartRegister.Name = "buttonAutostartRegister";
			buttonAutostartRegister.TabIndex = 0;
			buttonAutostartRegister.Text = "Register";
			buttonAutostartRegister.UseVisualStyleBackColor = true;
			buttonAutostartRegister.Click += buttonAutostartRegister_Click;
			// 
			// buttonAutostartDeregister
			// 
			buttonAutostartDeregister.Dock = DockStyle.Fill;
			buttonAutostartDeregister.Margin = new Padding(3, 4, 0, 0);
			buttonAutostartDeregister.Name = "buttonAutostartDeregister";
			buttonAutostartDeregister.TabIndex = 1;
			buttonAutostartDeregister.Text = "Deregister";
			buttonAutostartDeregister.UseVisualStyleBackColor = true;
			buttonAutostartDeregister.Click += buttonAutostartDeregister_Click;
			// 
			// labelAutostartFeedback
			// 
			labelAutostartFeedback.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			labelAutostartFeedback.AutoSize = true;
			labelAutostartFeedback.Location = new Point(3, 36);
			labelAutostartFeedback.Margin = new Padding(3, 0, 3, 0);
			labelAutostartFeedback.Name = "labelAutostartFeedback";
			labelAutostartFeedback.Size = new Size(401, 20);
			labelAutostartFeedback.TabIndex = 2;
			labelAutostartFeedback.Text = "";
			labelAutostartFeedback.TextAlign = ContentAlignment.MiddleLeft;
			// 
			// groupBoxConfigStore
			// 
			groupBoxConfigStore.Controls.Add(tableLayoutConfigStore);
			groupBoxConfigStore.Dock = DockStyle.Fill;
			groupBoxConfigStore.Location = new Point(3, 109);
			groupBoxConfigStore.Margin = new Padding(3, 3, 3, 6);
			groupBoxConfigStore.Name = "groupBoxConfigStore";
			groupBoxConfigStore.Padding = new Padding(3, 3, 3, 3);
			groupBoxConfigStore.Size = new Size(413, 100);
			groupBoxConfigStore.TabIndex = 10;
			groupBoxConfigStore.TabStop = false;
			groupBoxConfigStore.Text = "Configuration Store";
			// 
			// tableLayoutConfigStore
			// 
			tableLayoutConfigStore.ColumnCount = 2;
			tableLayoutConfigStore.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			tableLayoutConfigStore.ColumnStyles.Add(new ColumnStyle());
			tableLayoutConfigStore.Controls.Add(textBoxConfigStorePath, 0, 0);
			tableLayoutConfigStore.Controls.Add(buttonOpenConfigStoreFolder, 1, 0);
			tableLayoutConfigStore.Controls.Add(labelConfigStoreFeedback, 0, 1);
			tableLayoutConfigStore.Dock = DockStyle.Fill;
			tableLayoutConfigStore.Location = new Point(3, 23);
			tableLayoutConfigStore.Name = "tableLayoutConfigStore";
			tableLayoutConfigStore.RowCount = 2;
			tableLayoutConfigStore.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutConfigStore.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutConfigStore.Size = new Size(407, 74);
			tableLayoutConfigStore.TabIndex = 0;
			tableLayoutConfigStore.SetColumnSpan(labelConfigStoreFeedback, 2);
			// 
			// textBoxConfigStorePath
			// 
			textBoxConfigStorePath.Dock = DockStyle.Fill;
			textBoxConfigStorePath.Location = new Point(3, 3);
			textBoxConfigStorePath.Margin = new Padding(0, 3, 3, 3);
			textBoxConfigStorePath.Name = "textBoxConfigStorePath";
			textBoxConfigStorePath.ReadOnly = true;
			textBoxConfigStorePath.Size = new Size(368, 27);
			textBoxConfigStorePath.TabIndex = 0;
			textBoxConfigStorePath.TabStop = false;
			// 
			// buttonOpenConfigStoreFolder
			// 
			buttonOpenConfigStoreFolder.Anchor = AnchorStyles.None;
			buttonOpenConfigStoreFolder.AutoSize = false;
			buttonOpenConfigStoreFolder.ImageAlign = ContentAlignment.MiddleCenter;
			buttonOpenConfigStoreFolder.Location = new Point(374, 3);
			buttonOpenConfigStoreFolder.Margin = new Padding(0, 3, 0, 3);
			buttonOpenConfigStoreFolder.Name = "buttonOpenConfigStoreFolder";
			buttonOpenConfigStoreFolder.Size = new Size(27, 27);
			buttonOpenConfigStoreFolder.TabIndex = 1;
			buttonOpenConfigStoreFolder.TextImageRelation = TextImageRelation.ImageBeforeText;
			buttonOpenConfigStoreFolder.UseVisualStyleBackColor = true;
			buttonOpenConfigStoreFolder.Click += buttonOpenConfigStoreFolder_Click;
			toolTipConfigStore.SetToolTip(buttonOpenConfigStoreFolder, "Open folder in File Explorer");
			// 
			// labelConfigStoreFeedback
			// 
			labelConfigStoreFeedback.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			labelConfigStoreFeedback.AutoSize = true;
			labelConfigStoreFeedback.MaximumSize = new Size(401, 0);
			labelConfigStoreFeedback.Location = new Point(3, 36);
			labelConfigStoreFeedback.Margin = new Padding(3, 3, 3, 0);
			labelConfigStoreFeedback.Name = "labelConfigStoreFeedback";
			labelConfigStoreFeedback.Size = new Size(401, 40);
			labelConfigStoreFeedback.TabIndex = 2;
			labelConfigStoreFeedback.Text = "";
			labelConfigStoreFeedback.TextAlign = ContentAlignment.MiddleLeft;
			// 
			// toolTipConfigStore
			// 
			toolTipConfigStore.AutoPopDelay = 10000;
			toolTipConfigStore.InitialDelay = 400;
			toolTipConfigStore.ReshowDelay = 200;
			// 
			// groupBoxNetwork
			// 
			groupBoxNetwork.Controls.Add(tableLayoutNetwork);
			groupBoxNetwork.Dock = DockStyle.Fill;
			groupBoxNetwork.Location = new Point(3, 115);
			groupBoxNetwork.Margin = new Padding(3, 3, 3, 6);
			groupBoxNetwork.Name = "groupBoxNetwork";
			groupBoxNetwork.Padding = new Padding(3, 3, 3, 3);
			groupBoxNetwork.Size = new Size(413, 144);
			groupBoxNetwork.TabIndex = 7;
			groupBoxNetwork.TabStop = false;
			groupBoxNetwork.Text = "Network";
			// 
			// tableLayoutNetwork
			// 
			tableLayoutNetwork.ColumnCount = 2;
			tableLayoutNetwork.ColumnStyles.Add(new ColumnStyle());
			tableLayoutNetwork.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			tableLayoutNetwork.Controls.Add(labelIp, 0, 0);
			tableLayoutNetwork.Controls.Add(textBoxIP, 1, 0);
			tableLayoutNetwork.Controls.Add(tableLayoutQueryTimeout, 0, 1);
			tableLayoutNetwork.Controls.Add(labelNetworkFeedback, 0, 2);
			tableLayoutNetwork.Dock = DockStyle.Fill;
			tableLayoutNetwork.Location = new Point(3, 23);
			tableLayoutNetwork.Name = "tableLayoutNetwork";
			tableLayoutNetwork.RowCount = 3;
			tableLayoutNetwork.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutNetwork.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutNetwork.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutNetwork.Size = new Size(407, 118);
			tableLayoutNetwork.TabIndex = 0;
			tableLayoutNetwork.SetColumnSpan(tableLayoutQueryTimeout, 2);
			tableLayoutNetwork.SetColumnSpan(labelNetworkFeedback, 2);
			// 
			// tableLayoutQueryTimeout
			// 
			tableLayoutQueryTimeout.AutoSize = true;
			tableLayoutQueryTimeout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			tableLayoutQueryTimeout.ColumnCount = 3;
			tableLayoutQueryTimeout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			tableLayoutQueryTimeout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			tableLayoutQueryTimeout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			tableLayoutQueryTimeout.Controls.Add(labelQueryTimeoutMs, 0, 0);
			tableLayoutQueryTimeout.Controls.Add(numericUpDownQueryTimeoutMs, 1, 0);
			tableLayoutQueryTimeout.Controls.Add(labelQueryTimeoutUnitMs, 2, 0);
			tableLayoutQueryTimeout.Dock = DockStyle.Fill;
			tableLayoutQueryTimeout.Location = new Point(0, 33);
			tableLayoutQueryTimeout.Margin = new Padding(0);
			tableLayoutQueryTimeout.Name = "tableLayoutQueryTimeout";
			tableLayoutQueryTimeout.RowCount = 1;
			tableLayoutQueryTimeout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutQueryTimeout.Size = new Size(407, 33);
			tableLayoutQueryTimeout.TabIndex = 2;
			// 
			// labelQueryTimeoutMs
			// 
			labelQueryTimeoutMs.AutoSize = true;
			labelQueryTimeoutMs.Dock = DockStyle.Fill;
			labelQueryTimeoutMs.Location = new Point(0, 0);
			labelQueryTimeoutMs.Margin = new Padding(0, 0, 6, 0);
			labelQueryTimeoutMs.Name = "labelQueryTimeoutMs";
			labelQueryTimeoutMs.Size = new Size(106, 27);
			labelQueryTimeoutMs.TabIndex = 0;
			labelQueryTimeoutMs.Text = "Query timeout:";
			labelQueryTimeoutMs.TextAlign = ContentAlignment.MiddleLeft;
			// 
			// numericUpDownQueryTimeoutMs
			// 
			numericUpDownQueryTimeoutMs.DecimalPlaces = 0;
			numericUpDownQueryTimeoutMs.Dock = DockStyle.Fill;
			numericUpDownQueryTimeoutMs.Increment = 10;
			numericUpDownQueryTimeoutMs.Location = new Point(112, 3);
			numericUpDownQueryTimeoutMs.Margin = new Padding(0, 3, 0, 3);
			numericUpDownQueryTimeoutMs.Maximum = 10000;
			numericUpDownQueryTimeoutMs.Minimum = 1;
			numericUpDownQueryTimeoutMs.Name = "numericUpDownQueryTimeoutMs";
			numericUpDownQueryTimeoutMs.Size = new Size(271, 27);
			numericUpDownQueryTimeoutMs.TabIndex = 1;
			numericUpDownQueryTimeoutMs.TextAlign = HorizontalAlignment.Right;
			numericUpDownQueryTimeoutMs.ThousandsSeparator = true;
			numericUpDownQueryTimeoutMs.Value = 500;
			numericUpDownQueryTimeoutMs.ValueChanged += numericUpDownQueryTimeoutMs_ValueChanged;
			// 
			// labelQueryTimeoutUnitMs
			// 
			labelQueryTimeoutUnitMs.AutoSize = true;
			labelQueryTimeoutUnitMs.Dock = DockStyle.Fill;
			labelQueryTimeoutUnitMs.Location = new Point(383, 3);
			labelQueryTimeoutUnitMs.Margin = new Padding(6, 3, 0, 3);
			labelQueryTimeoutUnitMs.Name = "labelQueryTimeoutUnitMs";
			labelQueryTimeoutUnitMs.Size = new Size(28, 27);
			labelQueryTimeoutUnitMs.TabIndex = 2;
			labelQueryTimeoutUnitMs.Text = "ms";
			labelQueryTimeoutUnitMs.TextAlign = ContentAlignment.MiddleLeft;
			// 
			// groupBoxOscBase
			// 
			groupBoxOscBase.Controls.Add(tableLayoutOscBase);
			groupBoxOscBase.Dock = DockStyle.Fill;
			groupBoxOscBase.Location = new Point(3, 321);
			groupBoxOscBase.Margin = new Padding(3, 3, 3, 6);
			groupBoxOscBase.MinimumSize = new Size(280, 132);
			groupBoxOscBase.Name = "groupBoxOscBase";
			groupBoxOscBase.Padding = new Padding(3, 3, 3, 3);
			groupBoxOscBase.Size = new Size(413, 208);
			groupBoxOscBase.TabIndex = 9;
			groupBoxOscBase.TabStop = false;
			groupBoxOscBase.Text = "OSC Base";
			// 
			// tableLayoutOscBase
			// 
			tableLayoutOscBase.ColumnCount = 2;
			tableLayoutOscBase.ColumnStyles.Add(new ColumnStyle());
			tableLayoutOscBase.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			tableLayoutOscBase.Controls.Add(labelPort, 0, 0);
			tableLayoutOscBase.Controls.Add(textBoxPort, 1, 0);
			tableLayoutOscBase.Controls.Add(labelOscBaseFeedback, 0, 1);
			tableLayoutOscBase.Controls.Add(textBoxInfoResult, 0, 2);
			tableLayoutOscBase.Dock = DockStyle.Fill;
			tableLayoutOscBase.Location = new Point(3, 23);
			tableLayoutOscBase.Name = "tableLayoutOscBase";
			tableLayoutOscBase.RowCount = 3;
			tableLayoutOscBase.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutOscBase.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutOscBase.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			tableLayoutOscBase.Size = new Size(407, 182);
			tableLayoutOscBase.TabIndex = 0;
			tableLayoutOscBase.SetColumnSpan(labelOscBaseFeedback, 2);
			tableLayoutOscBase.SetColumnSpan(textBoxInfoResult, 2);
			// 
			// groupBoxFader
			// 
			groupBoxFader.AutoSize = true;
			groupBoxFader.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			groupBoxFader.Controls.Add(tableLayoutFader);
			groupBoxFader.Dock = DockStyle.Fill;
			groupBoxFader.Location = new Point(3, 541);
			groupBoxFader.Margin = new Padding(3, 3, 3, 6);
			groupBoxFader.Name = "groupBoxFader";
			groupBoxFader.Padding = new Padding(3, 3, 3, 3);
			groupBoxFader.Size = new Size(413, 140);
			groupBoxFader.TabIndex = 8;
			groupBoxFader.TabStop = false;
			groupBoxFader.Text = "OSC Faders";
			// 
			// tableLayoutVolumeCache
			// 
			tableLayoutVolumeCache.AutoSize = true;
			tableLayoutVolumeCache.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			tableLayoutVolumeCache.ColumnCount = 3;
			tableLayoutVolumeCache.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			tableLayoutVolumeCache.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			tableLayoutVolumeCache.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			tableLayoutVolumeCache.Controls.Add(labelFaderVolumeCacheTtlMs, 0, 0);
			tableLayoutVolumeCache.Controls.Add(numericUpDownFaderVolumeCacheTtlMs, 1, 0);
			tableLayoutVolumeCache.Controls.Add(labelFaderVolumeCacheUnitMs, 2, 0);
			tableLayoutVolumeCache.Dock = DockStyle.Fill;
			tableLayoutVolumeCache.Location = new Point(0, 0);
			tableLayoutVolumeCache.Margin = new Padding(0);
			tableLayoutVolumeCache.Name = "tableLayoutVolumeCache";
			tableLayoutVolumeCache.RowCount = 1;
			tableLayoutVolumeCache.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutVolumeCache.Size = new Size(407, 33);
			tableLayoutVolumeCache.TabIndex = 0;
			// 
			// tableLayoutFader
			// 
			tableLayoutFader.AutoSize = true;
			tableLayoutFader.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			tableLayoutFader.ColumnCount = 1;
			tableLayoutFader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			tableLayoutFader.Controls.Add(tableLayoutVolumeCache, 0, 0);
			tableLayoutFader.Controls.Add(dataGridViewOscFaders, 0, 1);
			tableLayoutFader.Controls.Add(labelFaderTestResult, 0, 2);
			tableLayoutFader.Dock = DockStyle.Top;
			tableLayoutFader.Location = new Point(3, 23);
			tableLayoutFader.Name = "tableLayoutFader";
			tableLayoutFader.RowCount = 3;
			tableLayoutFader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutFader.RowStyles.Add(new RowStyle(SizeType.Absolute, 140F));
			tableLayoutFader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutFader.Size = new Size(407, 200);
			tableLayoutFader.TabIndex = 0;
			// 
			// dataGridViewOscFaders
			// 
			dataGridViewOscFaders.AllowUserToAddRows = false;
			dataGridViewOscFaders.AllowUserToDeleteRows = false;
			dataGridViewOscFaders.AllowUserToResizeRows = false;
			dataGridViewOscFaders.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
			dataGridViewOscFaders.BackgroundColor = SystemColors.Window;
			dataGridViewOscFaders.BorderStyle = BorderStyle.FixedSingle;
			dataGridViewOscFaders.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
			dataGridViewOscFaders.Columns.AddRange(new DataGridViewColumn[] {
				columnOscFaderName, columnOscFaderAddress, columnOscFaderStep, columnOscFaderMinimum, columnOscFaderMaximum,
				columnOscFaderHotkeyMinus, columnOscFaderClearMinus, columnOscFaderHotkeyPlus, columnOscFaderClearPlus, columnOscFaderRemove });
			dataGridViewOscFaders.Dock = DockStyle.Fill;
			dataGridViewOscFaders.EditMode = DataGridViewEditMode.EditOnEnter;
			dataGridViewOscFaders.Location = new Point(0, 33);
			dataGridViewOscFaders.Margin = new Padding(0);
			dataGridViewOscFaders.MinimumSize = new Size(0, 120);
			dataGridViewOscFaders.MultiSelect = false;
			dataGridViewOscFaders.Name = "dataGridViewOscFaders";
			dataGridViewOscFaders.RowHeadersVisible = false;
			dataGridViewOscFaders.RowTemplate.Height = 29;
			dataGridViewOscFaders.SelectionMode = DataGridViewSelectionMode.CellSelect;
			dataGridViewOscFaders.Size = new Size(407, 140);
			dataGridViewOscFaders.TabIndex = 1;
			dataGridViewOscFaders.CellBeginEdit += dataGridViewOscFaders_CellBeginEdit;
			dataGridViewOscFaders.CellClick += dataGridViewOscFaders_CellClick;
			dataGridViewOscFaders.CellEndEdit += dataGridViewOscFaders_CellEndEdit;
			dataGridViewOscFaders.CurrentCellDirtyStateChanged += dataGridViewOscFaders_CurrentCellDirtyStateChanged;
			dataGridViewOscFaders.EditingControlShowing += dataGridViewOscFaders_EditingControlShowing;
			dataGridViewOscFaders.KeyDown += dataGridViewOscFaders_KeyDown;
			// 
			// columnOscFaderName
			// 
			columnOscFaderName.FillWeight = 18F;
			columnOscFaderName.HeaderText = "Name";
			columnOscFaderName.Name = "columnOscFaderName";
			columnOscFaderName.SortMode = DataGridViewColumnSortMode.NotSortable;
			// 
			// columnOscFaderAddress
			// 
			columnOscFaderAddress.FillWeight = 28F;
			columnOscFaderAddress.HeaderText = "Address";
			columnOscFaderAddress.Name = "columnOscFaderAddress";
			columnOscFaderAddress.SortMode = DataGridViewColumnSortMode.NotSortable;
			// 
			// columnOscFaderStep
			// 
			columnOscFaderStep.FillWeight = 10F;
			columnOscFaderStep.HeaderText = "Step";
			columnOscFaderStep.Name = "columnOscFaderStep";
			columnOscFaderStep.SortMode = DataGridViewColumnSortMode.NotSortable;
			// 
			// columnOscFaderMinimum
			// 
			columnOscFaderMinimum.FillWeight = 10F;
			columnOscFaderMinimum.HeaderText = "Minimum";
			columnOscFaderMinimum.Name = "columnOscFaderMinimum";
			columnOscFaderMinimum.SortMode = DataGridViewColumnSortMode.NotSortable;
			// 
			// columnOscFaderMaximum
			// 
			columnOscFaderMaximum.FillWeight = 10F;
			columnOscFaderMaximum.HeaderText = "Maximum";
			columnOscFaderMaximum.Name = "columnOscFaderMaximum";
			columnOscFaderMaximum.SortMode = DataGridViewColumnSortMode.NotSortable;
			// 
			// columnOscFaderHotkeyMinus
			// 
			columnOscFaderHotkeyMinus.FillWeight = 14F;
			columnOscFaderHotkeyMinus.HeaderText = "Hotkey −";
			columnOscFaderHotkeyMinus.Name = "columnOscFaderHotkeyMinus";
			columnOscFaderHotkeyMinus.SortMode = DataGridViewColumnSortMode.NotSortable;
			// 
			// columnOscFaderClearMinus
			// 
			columnOscFaderClearMinus.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
			columnOscFaderClearMinus.FillWeight = 8F;
			columnOscFaderClearMinus.HeaderText = "";
			columnOscFaderClearMinus.ImageLayout = DataGridViewImageCellLayout.Zoom;
			columnOscFaderClearMinus.Name = "columnOscFaderClearMinus";
			columnOscFaderClearMinus.Resizable = DataGridViewTriState.False;
			columnOscFaderClearMinus.SortMode = DataGridViewColumnSortMode.NotSortable;
			columnOscFaderClearMinus.Width = 28;
			// 
			// columnOscFaderHotkeyPlus
			// 
			columnOscFaderHotkeyPlus.FillWeight = 14F;
			columnOscFaderHotkeyPlus.HeaderText = "Hotkey +";
			columnOscFaderHotkeyPlus.Name = "columnOscFaderHotkeyPlus";
			columnOscFaderHotkeyPlus.SortMode = DataGridViewColumnSortMode.NotSortable;
			// 
			// columnOscFaderClearPlus
			// 
			columnOscFaderClearPlus.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
			columnOscFaderClearPlus.FillWeight = 8F;
			columnOscFaderClearPlus.HeaderText = "";
			columnOscFaderClearPlus.ImageLayout = DataGridViewImageCellLayout.Zoom;
			columnOscFaderClearPlus.Name = "columnOscFaderClearPlus";
			columnOscFaderClearPlus.Resizable = DataGridViewTriState.False;
			columnOscFaderClearPlus.SortMode = DataGridViewColumnSortMode.NotSortable;
			columnOscFaderClearPlus.Width = 28;
			// 
			// columnOscFaderRemove
			// 
			columnOscFaderRemove.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
			columnOscFaderRemove.FillWeight = 8F;
			columnOscFaderRemove.HeaderText = "";
			columnOscFaderRemove.ImageLayout = DataGridViewImageCellLayout.Zoom;
			columnOscFaderRemove.Name = "columnOscFaderRemove";
			columnOscFaderRemove.Resizable = DataGridViewTriState.False;
			columnOscFaderRemove.SortMode = DataGridViewColumnSortMode.NotSortable;
			columnOscFaderRemove.Width = 28;
			// 
			// labelFaderVolumeCacheTtlMs
			// 
			labelFaderVolumeCacheTtlMs.AutoSize = true;
			labelFaderVolumeCacheTtlMs.Dock = DockStyle.Fill;
			labelFaderVolumeCacheTtlMs.Location = new Point(0, 0);
			labelFaderVolumeCacheTtlMs.Margin = new Padding(0, 0, 6, 0);
			labelFaderVolumeCacheTtlMs.Name = "labelFaderVolumeCacheTtlMs";
			labelFaderVolumeCacheTtlMs.Size = new Size(160, 27);
			labelFaderVolumeCacheTtlMs.TabIndex = 0;
			labelFaderVolumeCacheTtlMs.Text = "Value cache duration:";
			labelFaderVolumeCacheTtlMs.TextAlign = ContentAlignment.MiddleLeft;
			// 
			// numericUpDownFaderVolumeCacheTtlMs
			// 
			numericUpDownFaderVolumeCacheTtlMs.DecimalPlaces = 0;
			numericUpDownFaderVolumeCacheTtlMs.Dock = DockStyle.Fill;
			numericUpDownFaderVolumeCacheTtlMs.Increment = 10;
			numericUpDownFaderVolumeCacheTtlMs.Location = new Point(166, 3);
			numericUpDownFaderVolumeCacheTtlMs.Margin = new Padding(0, 3, 0, 3);
			numericUpDownFaderVolumeCacheTtlMs.Maximum = 10000;
			numericUpDownFaderVolumeCacheTtlMs.Minimum = 0;
			numericUpDownFaderVolumeCacheTtlMs.Name = "numericUpDownFaderVolumeCacheTtlMs";
			numericUpDownFaderVolumeCacheTtlMs.Size = new Size(213, 27);
			numericUpDownFaderVolumeCacheTtlMs.TabIndex = 1;
			numericUpDownFaderVolumeCacheTtlMs.TextAlign = HorizontalAlignment.Right;
			numericUpDownFaderVolumeCacheTtlMs.ThousandsSeparator = true;
			numericUpDownFaderVolumeCacheTtlMs.Value = 1000;
			numericUpDownFaderVolumeCacheTtlMs.ValueChanged += numericUpDownFaderVolumeCacheTtlMs_ValueChanged;
			// 
			// labelFaderVolumeCacheUnitMs
			// 
			labelFaderVolumeCacheUnitMs.AutoSize = true;
			labelFaderVolumeCacheUnitMs.Dock = DockStyle.Fill;
			labelFaderVolumeCacheUnitMs.Location = new Point(379, 3);
			labelFaderVolumeCacheUnitMs.Margin = new Padding(6, 3, 0, 3);
			labelFaderVolumeCacheUnitMs.Name = "labelFaderVolumeCacheUnitMs";
			labelFaderVolumeCacheUnitMs.Size = new Size(28, 27);
			labelFaderVolumeCacheUnitMs.TabIndex = 2;
			labelFaderVolumeCacheUnitMs.Text = "ms";
			labelFaderVolumeCacheUnitMs.TextAlign = ContentAlignment.MiddleLeft;
			// 
			// groupBoxOscToggles
			// 
			groupBoxOscToggles.AutoSize = true;
			groupBoxOscToggles.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			groupBoxOscToggles.Controls.Add(tableLayoutOscToggles);
			groupBoxOscToggles.Dock = DockStyle.Fill;
			groupBoxOscToggles.Location = new Point(3, 690);
			groupBoxOscToggles.Margin = new Padding(3, 3, 3, 6);
			groupBoxOscToggles.Name = "groupBoxOscToggles";
			groupBoxOscToggles.Padding = new Padding(3);
			groupBoxOscToggles.Size = new Size(413, 236);
			groupBoxOscToggles.TabIndex = 10;
			groupBoxOscToggles.TabStop = false;
			groupBoxOscToggles.Text = "OSC Toggles";
			// 
			// tableLayoutOscToggles
			// 
			tableLayoutOscToggles.AutoSize = true;
			tableLayoutOscToggles.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			tableLayoutOscToggles.ColumnCount = 1;
			tableLayoutOscToggles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			tableLayoutOscToggles.Controls.Add(dataGridViewOscToggles, 0, 0);
			tableLayoutOscToggles.Controls.Add(labelOscTogglesHint, 0, 1);
			tableLayoutOscToggles.Dock = DockStyle.Fill;
			tableLayoutOscToggles.Location = new Point(3, 23);
			tableLayoutOscToggles.Name = "tableLayoutOscToggles";
			tableLayoutOscToggles.RowCount = 2;
			tableLayoutOscToggles.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutOscToggles.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutOscToggles.Size = new Size(407, 210);
			tableLayoutOscToggles.TabIndex = 0;
			// 
			// dataGridViewOscToggles
			// 
			dataGridViewOscToggles.AllowUserToAddRows = false;
			dataGridViewOscToggles.AllowUserToDeleteRows = false;
			dataGridViewOscToggles.AllowUserToResizeRows = false;
			dataGridViewOscToggles.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
			dataGridViewOscToggles.BackgroundColor = SystemColors.Window;
			dataGridViewOscToggles.BorderStyle = BorderStyle.FixedSingle;
			dataGridViewOscToggles.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
			dataGridViewOscToggles.Columns.AddRange(new DataGridViewColumn[] { columnOscToggleName, columnOscToggleAddress, columnOscToggleHotkey, columnOscToggleClearHotkey, columnOscToggleRemove });
			dataGridViewOscToggles.Dock = DockStyle.Fill;
			dataGridViewOscToggles.EditMode = DataGridViewEditMode.EditOnEnter;
			dataGridViewOscToggles.Location = new Point(0, 0);
			dataGridViewOscToggles.Margin = new Padding(0);
			dataGridViewOscToggles.MultiSelect = false;
			dataGridViewOscToggles.Name = "dataGridViewOscToggles";
			dataGridViewOscToggles.RowHeadersVisible = false;
			dataGridViewOscToggles.RowTemplate.Height = 29;
			dataGridViewOscToggles.SelectionMode = DataGridViewSelectionMode.CellSelect;
			dataGridViewOscToggles.Size = new Size(407, 152);
			dataGridViewOscToggles.TabIndex = 0;
			dataGridViewOscToggles.CellBeginEdit += dataGridViewOscToggles_CellBeginEdit;
			dataGridViewOscToggles.CellClick += dataGridViewOscToggles_CellClick;
			dataGridViewOscToggles.CellEndEdit += dataGridViewOscToggles_CellEndEdit;
			dataGridViewOscToggles.CurrentCellDirtyStateChanged += dataGridViewOscToggles_CurrentCellDirtyStateChanged;
			dataGridViewOscToggles.EditingControlShowing += dataGridViewOscToggles_EditingControlShowing;
			dataGridViewOscToggles.KeyDown += dataGridViewOscToggles_KeyDown;
			// 
			// columnOscToggleName
			// 
			columnOscToggleName.FillWeight = 30F;
			columnOscToggleName.HeaderText = "Name";
			columnOscToggleName.Name = "columnOscToggleName";
			columnOscToggleName.SortMode = DataGridViewColumnSortMode.NotSortable;
			// 
			// columnOscToggleAddress
			// 
			columnOscToggleAddress.FillWeight = 45F;
			columnOscToggleAddress.HeaderText = "Address";
			columnOscToggleAddress.Name = "columnOscToggleAddress";
			columnOscToggleAddress.SortMode = DataGridViewColumnSortMode.NotSortable;
			// 
			// columnOscToggleHotkey
			// 
			columnOscToggleHotkey.FillWeight = 25F;
			columnOscToggleHotkey.HeaderText = "Hotkey";
			columnOscToggleHotkey.Name = "columnOscToggleHotkey";
			columnOscToggleHotkey.SortMode = DataGridViewColumnSortMode.NotSortable;
			// 
			// columnOscToggleClearHotkey
			// 
			columnOscToggleClearHotkey.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
			columnOscToggleClearHotkey.FillWeight = 8F;
			columnOscToggleClearHotkey.HeaderText = "";
			columnOscToggleClearHotkey.ImageLayout = DataGridViewImageCellLayout.Zoom;
			columnOscToggleClearHotkey.Name = "columnOscToggleClearHotkey";
			columnOscToggleClearHotkey.Resizable = DataGridViewTriState.False;
			columnOscToggleClearHotkey.SortMode = DataGridViewColumnSortMode.NotSortable;
			columnOscToggleClearHotkey.Width = 28;
			// 
			// columnOscToggleRemove
			// 
			columnOscToggleRemove.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
			columnOscToggleRemove.FillWeight = 8F;
			columnOscToggleRemove.HeaderText = "";
			columnOscToggleRemove.ImageLayout = DataGridViewImageCellLayout.Zoom;
			columnOscToggleRemove.Name = "columnOscToggleRemove";
			columnOscToggleRemove.Resizable = DataGridViewTriState.False;
			columnOscToggleRemove.SortMode = DataGridViewColumnSortMode.NotSortable;
			columnOscToggleRemove.Width = 28;
			// 
			// labelOscTogglesHint
			// 
			labelOscTogglesHint.AutoSize = true;
			labelOscTogglesHint.Dock = DockStyle.Fill;
			labelOscTogglesHint.Location = new Point(0, 158);
			labelOscTogglesHint.Margin = new Padding(0, 6, 0, 0);
			labelOscTogglesHint.Name = "labelOscTogglesHint";
			labelOscTogglesHint.Size = new Size(407, 20);
			labelOscTogglesHint.TabIndex = 1;
			labelOscTogglesHint.Text = "";
			labelOscTogglesHint.TextAlign = ContentAlignment.MiddleLeft;
			// 
			// tableLayoutMain
			// 
			tableLayoutMain.ColumnCount = 1;
			tableLayoutMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			tableLayoutMain.Controls.Add(groupBoxAutostart, 0, 0);
			tableLayoutMain.Controls.Add(groupBoxConfigStore, 0, 1);
			tableLayoutMain.Controls.Add(groupBoxNetwork, 0, 2);
			tableLayoutMain.Controls.Add(groupBoxOscBase, 0, 3);
			tableLayoutMain.Controls.Add(groupBoxFader, 0, 4);
			tableLayoutMain.Controls.Add(groupBoxOscToggles, 0, 5);
			tableLayoutMain.Controls.Add(buttonSaveAndTest, 0, 6);
			tableLayoutMain.Dock = DockStyle.Fill;
			tableLayoutMain.Location = new Point(0, 0);
			tableLayoutMain.Name = "tableLayoutMain";
			tableLayoutMain.RowCount = 7;
			tableLayoutMain.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutMain.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutMain.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			tableLayoutMain.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutMain.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutMain.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutMain.Size = new Size(430, 1046);
			tableLayoutMain.TabIndex = 0;
			// 
			// ConfigForm
			// 
			AutoScaleDimensions = new SizeF(8F, 20F);
			AutoScaleMode = AutoScaleMode.Font;
			ClientSize = new Size(430, 1046);
			Controls.Add(tableLayoutMain);
			MinimumSize = new Size(360, 760);
			Name = "ConfigForm";
			Text = "OSC Volume hijacker";
			Load += ConfigForm_Load;
			tableLayoutAutostartOuter.ResumeLayout(false);
			tableLayoutAutostartOuter.PerformLayout();
			tableLayoutPanelAutostart.ResumeLayout(false);
			tableLayoutConfigStore.ResumeLayout(false);
			tableLayoutConfigStore.PerformLayout();
			groupBoxConfigStore.ResumeLayout(false);
			tableLayoutQueryTimeout.ResumeLayout(false);
			tableLayoutQueryTimeout.PerformLayout();
			tableLayoutNetwork.ResumeLayout(false);
			tableLayoutNetwork.PerformLayout();
			groupBoxAutostart.ResumeLayout(false);
			tableLayoutOscBase.ResumeLayout(false);
			tableLayoutOscBase.PerformLayout();
			groupBoxOscBase.ResumeLayout(false);
			tableLayoutVolumeCache.ResumeLayout(false);
			tableLayoutVolumeCache.PerformLayout();
			tableLayoutFader.ResumeLayout(false);
			tableLayoutFader.PerformLayout();
			((System.ComponentModel.ISupportInitialize)dataGridViewOscFaders).EndInit();
			groupBoxFader.ResumeLayout(false);
			groupBoxOscToggles.ResumeLayout(false);
			groupBoxOscToggles.PerformLayout();
			tableLayoutOscToggles.ResumeLayout(false);
			tableLayoutOscToggles.PerformLayout();
			((System.ComponentModel.ISupportInitialize)dataGridViewOscToggles).EndInit();
			groupBoxNetwork.ResumeLayout(false);
			tableLayoutMain.ResumeLayout(false);
			tableLayoutMain.PerformLayout();
			ResumeLayout(false);
		}

		#endregion

		private Label labelIp;
		private TextBox textBoxIP;
		private Label labelPort;
		private Button buttonSaveAndTest;
		private Label labelFaderTestResult;
		private Label labelNetworkFeedback;
		private Label labelOscBaseFeedback;
		private TextBox textBoxInfoResult;
		private GroupBox groupBoxAutostart;
		private TableLayoutPanel tableLayoutAutostartOuter;
		private TableLayoutPanel tableLayoutPanelAutostart;
		private Button buttonAutostartRegister;
		private Button buttonAutostartDeregister;
		private Label labelAutostartFeedback;
		private GroupBox groupBoxConfigStore;
		private TableLayoutPanel tableLayoutConfigStore;
		private TextBox textBoxConfigStorePath;
		private Button buttonOpenConfigStoreFolder;
		private Label labelConfigStoreFeedback;
		private ToolTip toolTipConfigStore;
		private GroupBox groupBoxNetwork;
		private TableLayoutPanel tableLayoutNetwork;
		private TableLayoutPanel tableLayoutQueryTimeout;
		private Label labelQueryTimeoutMs;
		private NumericUpDown numericUpDownQueryTimeoutMs;
		private Label labelQueryTimeoutUnitMs;
		private GroupBox groupBoxOscBase;
		private TableLayoutPanel tableLayoutOscBase;
		private GroupBox groupBoxFader;
		private TableLayoutPanel tableLayoutFader;
		private TableLayoutPanel tableLayoutVolumeCache;
		private DataGridView dataGridViewOscFaders;
		private Label labelFaderVolumeCacheTtlMs;
		private NumericUpDown numericUpDownFaderVolumeCacheTtlMs;
		private Label labelFaderVolumeCacheUnitMs;
		private TextBox textBoxPort;
		private TableLayoutPanel tableLayoutMain;
		private GroupBox groupBoxOscToggles;
		private TableLayoutPanel tableLayoutOscToggles;
		private DataGridView dataGridViewOscToggles;
		private Label labelOscTogglesHint;
		private DataGridViewTextBoxColumn columnOscToggleName;
		private DataGridViewTextBoxColumn columnOscToggleAddress;
		private DataGridViewTextBoxColumn columnOscToggleHotkey;
		private DataGridViewImageColumn columnOscToggleClearHotkey;
		private DataGridViewImageColumn columnOscToggleRemove;
		private DataGridViewTextBoxColumn columnOscFaderName;
		private DataGridViewTextBoxColumn columnOscFaderAddress;
		private DataGridViewTextBoxColumn columnOscFaderStep;
		private DataGridViewTextBoxColumn columnOscFaderMinimum;
		private DataGridViewTextBoxColumn columnOscFaderMaximum;
		private DataGridViewTextBoxColumn columnOscFaderHotkeyMinus;
		private DataGridViewImageColumn columnOscFaderClearMinus;
		private DataGridViewTextBoxColumn columnOscFaderHotkeyPlus;
		private DataGridViewImageColumn columnOscFaderClearPlus;
		private DataGridViewImageColumn columnOscFaderRemove;
	}
}
