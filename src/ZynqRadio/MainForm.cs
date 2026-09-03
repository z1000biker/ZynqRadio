using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using ZynqRadio.Audio;
using ZynqRadio.Diagnostics;
using ZynqRadio.Radio;

namespace ZynqRadio.Gui;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly RadioSession _session = new();

    private readonly Button _startStop = new();
    private readonly Label _stateLabel = new();
    private readonly Label _pttLabel = new();
    private readonly Label _catLabel = new();
    private readonly Label _frequencyStatus = new();

    private readonly TextBox _uri = new();
    private readonly NumericUpDown _frequency = new();
    private readonly ComboBox _sampleRate = new();
    private readonly NumericUpDown _bandwidth = new();

    private readonly NumericUpDown _rxGain = new();
    private readonly NumericUpDown _audioGain = new();
    private readonly NumericUpDown _rxOffset = new();
    private readonly NumericUpDown _rxBuffer = new();
    private readonly CheckBox _rxIOnly = new();

    private readonly CheckBox _enableRx = new();
    private readonly CheckBox _enableTx = new();
    private readonly CheckBox _allowTx = new();

    private readonly NumericUpDown _txGain = new();
    private readonly NumericUpDown _txLevel = new();
    private readonly NumericUpDown _txMonitorGain = new();
    private readonly NumericUpDown _txBuffer = new();
    private readonly ComboBox _txQSign = new();

    private readonly ComboBox _rxAudio = new();
    private readonly ComboBox _txAudio = new();
    private readonly Button _refreshAudio = new();

    private readonly TextBox _catHost = new();
    private readonly NumericUpDown _catPort = new();
    private readonly CheckBox _verboseCat = new();

    private readonly Label _rxRate = new();
    private readonly Label _iqLevel = new();
    private readonly Label _rxAudioLevel = new();
    private readonly Label _txAudioLevel = new();
    private readonly Label _txUnderruns = new();

    private readonly RichTextBox _log = new();
    private readonly System.Windows.Forms.Timer _timer = new();

    private bool _closing;
    private bool _lastRunning;

    public MainForm()
    {
        _settings = AppSettings.Load();

        Text = "ZynqRadio Control Center v1.0.0";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1050, 720);
        Size = new Size(1220, 820);
        Font = new Font("Segoe UI", 9F);

        BuildUi();
        LoadSettingsIntoControls();
        RefreshAudioDevices();

        AppLog.LineWritten += OnLogLine;

        _timer.Interval = 250;
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();

        FormClosing += OnFormClosingAsync;

        AppLog.Info("GUI initialized.");
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(10)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        Controls.Add(root);
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildTabs(), 0, 1);

        var footer = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text =
                "Settings are saved automatically in %LOCALAPPDATA%\\ZynqRadio. " +
                "Normal use requires no command-line switches."
        };

        root.Controls.Add(footer, 0, 2);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 2,
            Padding = new Padding(4)
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

        for (int i = 1; i < 6; i++)
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        _startStop.Text = "START";
        _startStop.Dock = DockStyle.Fill;
        _startStop.Font = new Font(Font, FontStyle.Bold);
        _startStop.Click += async (_, _) => await ToggleSessionAsync();

        panel.Controls.Add(_startStop, 0, 0);
        panel.SetRowSpan(_startStop, 2);

        AddHeaderStatus(panel, 1, "STATE", _stateLabel);
        AddHeaderStatus(panel, 2, "PTT", _pttLabel);
        AddHeaderStatus(panel, 3, "CAT", _catLabel);
        AddHeaderStatus(panel, 4, "DIAL", _frequencyStatus);

        var applyFreq = new Button
        {
            Text = "SET FREQUENCY",
            Dock = DockStyle.Fill
        };

        applyFreq.Click += (_, _) =>
        {
            try
            {
                long hz = Decimal.ToInt64(_frequency.Value);
                _session.SetFrequency(hz);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Frequency",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };

        panel.Controls.Add(applyFreq, 5, 0);

        var applyGain = new Button
        {
            Text = "APPLY GAINS",
            Dock = DockStyle.Fill
        };

        applyGain.Click += (_, _) =>
        {
            try
            {
                _session.ApplyGains(
                    (double)_rxGain.Value,
                    (double)_txGain.Value);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "RF gains",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };

        panel.Controls.Add(applyGain, 5, 1);

        return panel;
    }

    private static void AddHeaderStatus(
        TableLayoutPanel panel,
        int column,
        string caption,
        Label value)
    {
        panel.Controls.Add(
            new Label
            {
                Text = caption,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomCenter,
                ForeColor = SystemColors.GrayText
            },
            column,
            0);

        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.TopCenter;
        value.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

        panel.Controls.Add(value, column, 1);
    }

    private Control BuildTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill
        };

        tabs.TabPages.Add(BuildRadioTab());
        tabs.TabPages.Add(BuildAudioTab());
        tabs.TabPages.Add(BuildDiagnosticsTab());

        return tabs;
    }

    private TabPage BuildRadioTab()
    {
        var page = new TabPage("Radio && DSP");

        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10)
        };

        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        split.Controls.Add(BuildRxGroup(), 0, 0);
        split.Controls.Add(BuildTxGroup(), 1, 0);
        page.Controls.Add(split);

        return page;
    }

    private Control BuildRxGroup()
    {
        var group = new GroupBox
        {
            Text = "Radio / RX",
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };

        var grid = MakeSettingsGrid();

        AddRow(grid, "IIO URI", _uri);

        ConfigureNumeric(_frequency, 70_000_000, 6_000_000_000, 1, 0);
        _frequency.ThousandsSeparator = true;
        AddRow(grid, "Dial frequency (Hz)", _frequency);

        _sampleRate.DropDownStyle = ComboBoxStyle.DropDown;
        _sampleRate.Items.AddRange(new object[] { "2100000", "2500000" });
        AddRow(grid, "IIO sample rate (S/s)", _sampleRate);

        ConfigureNumeric(_bandwidth, 200_000, 5_000_000, 10_000, 0);
        _bandwidth.ThousandsSeparator = true;
        AddRow(grid, "RF bandwidth (Hz)", _bandwidth);

        ConfigureNumeric(_rxGain, -20, 80, 1, 1);
        AddRow(grid, "RX gain (dB)", _rxGain);

        ConfigureNumeric(_audioGain, 0, 200, 1, 1);
        AddRow(grid, "DSP audio gain (x)", _audioGain);

        ConfigureNumeric(_rxOffset, 0, 100_000, 100, 0);
        AddRow(grid, "RX LO offset (Hz)", _rxOffset);

        ConfigureNumeric(_rxBuffer, 1024, 262144, 1024, 0);
        _rxBuffer.ThousandsSeparator = true;
        AddRow(grid, "RX buffer (complex)", _rxBuffer);

        _rxIOnly.Text =
            "Low-bandwidth RX transport (I only, recommended for full duplex)";
        _rxIOnly.AutoSize = true;
        AddRow(grid, "", _rxIOnly);

        _enableRx.Text = "Enable RX audio";
        _enableRx.AutoSize = true;
        AddRow(grid, "", _enableRx);

        group.Controls.Add(grid);
        return group;
    }

    private Control BuildTxGroup()
    {
        var group = new GroupBox
        {
            Text = "TX / Full duplex",
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };

        var grid = MakeSettingsGrid();

        _enableTx.Text = "Enable TX audio path";
        _enableTx.AutoSize = true;
        AddRow(grid, "", _enableTx);

        _allowTx.Text = "ENABLE RF TRANSMISSION";
        _allowTx.AutoSize = true;
        _allowTx.Font = new Font(Font, FontStyle.Bold);
        _allowTx.ForeColor = Color.DarkRed;
        AddRow(grid, "", _allowTx);

        ConfigureNumeric(_txGain, -89, 0, 1, 1);
        AddRow(grid, "TX hardware gain (dB)", _txGain);

        ConfigureNumeric(_txLevel, 0, 1, 0.01M, 3);
        AddRow(grid, "TX digital level", _txLevel);

        ConfigureNumeric(_txMonitorGain, -20, 80, 1, 1);
        AddRow(grid, "RX gain during TX (dB)", _txMonitorGain);

        ConfigureNumeric(_txBuffer, 1024, 262144, 1024, 0);
        _txBuffer.ThousandsSeparator = true;
        AddRow(grid, "TX buffer (complex)", _txBuffer);

        _txQSign.DropDownStyle = ComboBoxStyle.DropDownList;
        _txQSign.Items.AddRange(new object[] { "+1", "-1" });
        AddRow(grid, "TX Q polarity", _txQSign);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Text =
                "RF transmission is controlled here instead of by a startup switch. " +
                "Your selection is saved. RX remains active during CAT PTT."
        };

        AddRow(grid, "", note);
        group.Controls.Add(grid);

        return group;
    }

    private TabPage BuildAudioTab()
    {
        var page = new TabPage("Audio && CAT");
        var grid = MakeSettingsGrid();

        _rxAudio.DropDownStyle = ComboBoxStyle.DropDownList;
        _txAudio.DropDownStyle = ComboBoxStyle.DropDownList;

        AddRow(grid, "RX output to WSJT-X", _rxAudio);
        AddRow(grid, "TX input from WSJT-X", _txAudio);

        _refreshAudio.Text = "Refresh Windows audio devices";
        _refreshAudio.AutoSize = true;
        _refreshAudio.Click += (_, _) => RefreshAudioDevices();
        AddRow(grid, "", _refreshAudio);

        AddRow(grid, "CAT bind address", _catHost);

        ConfigureNumeric(_catPort, 1, 65535, 1, 0);
        AddRow(grid, "CAT TCP port", _catPort);

        _verboseCat.Text = "Verbose CAT command logging";
        _verboseCat.AutoSize = true;
        AddRow(grid, "", _verboseCat);

        var routing = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            Height = 170,
            ScrollBars = ScrollBars.Vertical,
            Text =
                "Recommended current routing:\r\n\r\n" +
                "RX: ZynqRadio → VB-CABLE playback → CABLE Output → WSJT-X Input\r\n\r\n" +
                "TX: WSJT-X Output → Voicemeeter Input → B → Voicemeeter Out B1 → ZynqRadio\r\n\r\n" +
                "WSJT-X Radio: Hamlib NET rigctl, 127.0.0.1:4532, PTT Method CAT."
        };

        AddRow(grid, "WSJT-X routing", routing);
        page.Controls.Add(grid);

        return page;
    }

    private TabPage BuildDiagnosticsTab()
    {
        var page = new TabPage("Diagnostics && Logs");

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(8)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var meters = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 2
        };

        for (int i = 0; i < 5; i++)
            meters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        AddMeter(meters, 0, "RX RATE", _rxRate);
        AddMeter(meters, 1, "IQ", _iqLevel);
        AddMeter(meters, 2, "RX AUDIO", _rxAudioLevel);
        AddMeter(meters, 3, "TX AUDIO", _txAudioLevel);
        AddMeter(meters, 4, "TX UNDERRUNS", _txUnderruns);

        root.Controls.Add(meters, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        var clear = new Button
        {
            Text = "Clear log",
            AutoSize = true
        };
        clear.Click += (_, _) => _log.Clear();

        var copy = new Button
        {
            Text = "Copy log",
            AutoSize = true
        };
        copy.Click += (_, _) =>
        {
            if (_log.TextLength > 0)
                Clipboard.SetText(_log.Text);
        };

        var open = new Button
        {
            Text = "Open log folder",
            AutoSize = true
        };
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppLog.LogDirectory);
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = AppLog.LogDirectory,
                    UseShellExecute = true
                });
        };

        var export = new Button
        {
            Text = "Export diagnostic ZIP...",
            AutoSize = true
        };
        export.Click += (_, _) => ExportDiagnostics();

        buttons.Controls.Add(clear);
        buttons.Controls.Add(copy);
        buttons.Controls.Add(open);
        buttons.Controls.Add(export);

        root.Controls.Add(buttons, 0, 1);

        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.Font = new Font("Consolas", 9F);
        _log.BackColor = Color.FromArgb(20, 20, 20);
        _log.ForeColor = Color.Gainsboro;
        _log.WordWrap = false;

        root.Controls.Add(_log, 0, 2);
        page.Controls.Add(root);

        return page;
    }

    private static TableLayoutPanel MakeSettingsGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 2,
            RowCount = 0,
            Padding = new Padding(8)
        };

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        return grid;
    }

    private static void AddRow(
        TableLayoutPanel grid,
        string label,
        Control control)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var caption = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 8, 8)
        };

        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(3, 5, 3, 5);

        grid.Controls.Add(caption, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private static void ConfigureNumeric(
        NumericUpDown n,
        decimal min,
        decimal max,
        decimal increment,
        int decimals)
    {
        n.Minimum = min;
        n.Maximum = max;
        n.Increment = increment;
        n.DecimalPlaces = decimals;
        n.Width = 180;
    }

    private static void AddMeter(
        TableLayoutPanel table,
        int column,
        string caption,
        Label value)
    {
        table.Controls.Add(
            new Label
            {
                Text = caption,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomCenter,
                ForeColor = SystemColors.GrayText
            },
            column,
            0);

        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.TopCenter;
        value.Font = new Font("Segoe UI", 12F, FontStyle.Bold);

        table.Controls.Add(value, column, 1);
    }

    private void LoadSettingsIntoControls()
    {
        _uri.Text = _settings.IioUri;
        _frequency.Value = Clamp(_frequency, _settings.FrequencyHz);
        _sampleRate.Text = _settings.SampleRateHz.ToString();
        _bandwidth.Value = Clamp(_bandwidth, _settings.RfBandwidthHz);
        _rxGain.Value = Clamp(_rxGain, (decimal)_settings.RxGainDb);
        _audioGain.Value = Clamp(_audioGain, (decimal)_settings.AudioGain);
        _rxOffset.Value = Clamp(_rxOffset, _settings.RxLoOffsetHz);
        _rxBuffer.Value = Clamp(_rxBuffer, _settings.RxBufferSamples);
        _rxIOnly.Checked = _settings.RxIOnlyTransport;
        _enableRx.Checked = _settings.EnableRxAudio;
        _enableTx.Checked = _settings.EnableTxAudio;
        _allowTx.Checked = _settings.AllowTransmit;
        _txGain.Value = Clamp(_txGain, (decimal)_settings.TxGainDb);
        _txLevel.Value = Clamp(_txLevel, (decimal)_settings.TxLevel);
        _txMonitorGain.Value = Clamp(_txMonitorGain, (decimal)_settings.TxMonitorRxGainDb);
        _txBuffer.Value = Clamp(_txBuffer, _settings.TxBufferSamples);
        _txQSign.SelectedItem = _settings.TxQSign < 0 ? "-1" : "+1";
        _catHost.Text = _settings.CatHost;
        _catPort.Value = Clamp(_catPort, _settings.CatPort);
        _verboseCat.Checked = _settings.VerboseCatLogging;
    }

    private void SaveControlsIntoSettings()
    {
        _settings.IioUri = _uri.Text.Trim();
        _settings.FrequencyHz = Decimal.ToInt64(_frequency.Value);

        if (!long.TryParse(_sampleRate.Text.Trim(), out long rate))
            throw new InvalidOperationException("Sample rate must be an integer.");

        _settings.SampleRateHz = rate;
        _settings.RfBandwidthHz = Decimal.ToInt64(_bandwidth.Value);
        _settings.RxGainDb = (double)_rxGain.Value;
        _settings.AudioGain = (double)_audioGain.Value;
        _settings.RxLoOffsetHz = Decimal.ToInt64(_rxOffset.Value);
        _settings.RxBufferSamples = Decimal.ToInt32(_rxBuffer.Value);
        _settings.RxIOnlyTransport = _rxIOnly.Checked;
        _settings.EnableRxAudio = _enableRx.Checked;
        _settings.EnableTxAudio = _enableTx.Checked;
        _settings.AllowTransmit = _allowTx.Checked;
        _settings.TxGainDb = (double)_txGain.Value;
        _settings.TxLevel = (double)_txLevel.Value;
        _settings.TxMonitorRxGainDb = (double)_txMonitorGain.Value;
        _settings.TxBufferSamples = Decimal.ToInt32(_txBuffer.Value);
        _settings.TxQSign = _txQSign.SelectedItem?.ToString() == "-1" ? -1 : 1;
        _settings.CatHost = _catHost.Text.Trim();
        _settings.CatPort = Decimal.ToInt32(_catPort.Value);
        _settings.VerboseCatLogging = _verboseCat.Checked;

        if (_rxAudio.SelectedItem is AudioDeviceDescriptor rx)
            _settings.RxAudioDeviceId = rx.Id;

        if (_txAudio.SelectedItem is AudioDeviceDescriptor tx)
            _settings.TxAudioDeviceId = tx.Id;

        _settings.Save();
    }

    private void RefreshAudioDevices()
    {
        string? selectedRx =
            (_rxAudio.SelectedItem as AudioDeviceDescriptor)?.Id ??
            _settings.RxAudioDeviceId;

        string? selectedTx =
            (_txAudio.SelectedItem as AudioDeviceDescriptor)?.Id ??
            _settings.TxAudioDeviceId;

        List<AudioDeviceDescriptor> render = WindowsAudioSink.GetRenderDevices();
        List<AudioDeviceDescriptor> capture = TxAudioCapture.GetCaptureDevices();

        _rxAudio.BeginUpdate();
        _rxAudio.Items.Clear();
        foreach (AudioDeviceDescriptor d in render)
            _rxAudio.Items.Add(d);
        _rxAudio.EndUpdate();

        _txAudio.BeginUpdate();
        _txAudio.Items.Clear();
        foreach (AudioDeviceDescriptor d in capture)
            _txAudio.Items.Add(d);
        _txAudio.EndUpdate();

        SelectById(_rxAudio, selectedRx);
        SelectById(_txAudio, selectedTx);

        if (_rxAudio.SelectedIndex < 0)
        {
            AudioDeviceDescriptor? autoRx = render.FirstOrDefault(
                d =>
                    d.Name.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase) &&
                    !d.Name.Contains("16 Ch", StringComparison.OrdinalIgnoreCase));

            if (autoRx is not null)
                _rxAudio.SelectedItem = autoRx;
            else if (render.Count > 0)
                _rxAudio.SelectedIndex = 0;
        }

        if (_txAudio.SelectedIndex < 0)
        {
            AudioDeviceDescriptor? autoTx = capture.FirstOrDefault(
                d => d.Name.Contains("Voicemeeter Out B1", StringComparison.OrdinalIgnoreCase));

            if (autoTx is not null)
                _txAudio.SelectedItem = autoTx;
            else if (capture.Count > 0)
                _txAudio.SelectedIndex = 0;
        }

        AppLog.Info($"Audio devices refreshed: {render.Count} playback, {capture.Count} capture.");
    }

    private static void SelectById(ComboBox combo, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is AudioDeviceDescriptor d &&
                string.Equals(d.Id, id, StringComparison.Ordinal))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    private async Task ToggleSessionAsync()
    {
        if (_session.IsRunning)
        {
            await StopSessionAsync();
            return;
        }

        await StartSessionAsync();
    }

    private async Task StartSessionAsync()
    {
        try
        {
            SaveControlsIntoSettings();

            if (_settings.EnableRxAudio && _rxAudio.SelectedItem is not AudioDeviceDescriptor)
                throw new InvalidOperationException("Select an RX playback device.");

            if (_settings.EnableTxAudio && _txAudio.SelectedItem is not AudioDeviceDescriptor)
                throw new InvalidOperationException("Select a TX capture device.");

            if (_settings.AllowTransmit)
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    "RF transmission is enabled. CAT PTT from WSJT-X will generate real TX I/Q. Continue?",
                    "RF TX enabled",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (answer != DialogResult.Yes)
                    return;
            }

            RadioConfig cfg = _settings.ToRadioConfig(
                _rxAudio.SelectedItem as AudioDeviceDescriptor,
                _txAudio.SelectedItem as AudioDeviceDescriptor);

            SetUiEnabled(false);
            _startStop.Text = "STARTING...";

            await _session.StartAsync(cfg);

            _lastRunning = true;
            _startStop.Text = "STOP";
        }
        catch (Exception ex)
        {
            AppLog.Error("Start failed: " + ex);

            MessageBox.Show(
                this,
                ex.Message,
                "ZynqRadio could not start",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            SetUiEnabled(true);
            _startStop.Text = "START";
        }
    }

    private async Task StopSessionAsync()
    {
        _startStop.Enabled = false;
        _startStop.Text = "STOPPING...";

        try
        {
            await _session.StopAsync();
            _lastRunning = false;
        }
        finally
        {
            SetUiEnabled(true);
            _startStop.Text = "START";
            _startStop.Enabled = true;
        }
    }

    private void SetUiEnabled(bool enabled)
    {
        _uri.Enabled = enabled;
        _sampleRate.Enabled = enabled;
        _bandwidth.Enabled = enabled;
        _rxOffset.Enabled = enabled;
        _rxBuffer.Enabled = enabled;
        _rxIOnly.Enabled = enabled;
        _txBuffer.Enabled = enabled;
        _enableRx.Enabled = enabled;
        _enableTx.Enabled = enabled;
        _allowTx.Enabled = enabled;
        _txLevel.Enabled = enabled;
        _txMonitorGain.Enabled = enabled;
        _txQSign.Enabled = enabled;
        _rxAudio.Enabled = enabled;
        _txAudio.Enabled = enabled;
        _refreshAudio.Enabled = enabled;
        _catHost.Enabled = enabled;
        _catPort.Enabled = enabled;
        _verboseCat.Enabled = enabled;

        _frequency.Enabled = true;
        _rxGain.Enabled = true;
        _txGain.Enabled = true;
        _audioGain.Enabled = enabled;
    }

    private void RefreshStatus()
    {
        bool running = _session.IsRunning;

        if (_lastRunning && !running && !_closing)
        {
            SetUiEnabled(true);
            _startStop.Enabled = true;
            _startStop.Text = "START";
        }

        _lastRunning = running;

        _stateLabel.Text = running ? "RUNNING" : "STOPPED";
        _stateLabel.ForeColor = running ? Color.DarkGreen : Color.DarkRed;

        var radio = _session.Radio;

        if (radio is null)
        {
            _pttLabel.Text = "RX";
            _pttLabel.ForeColor = SystemColors.ControlText;
            _frequencyStatus.Text = $"{_frequency.Value / 1_000_000M:0.000000} MHz";
        }
        else
        {
            _pttLabel.Text = radio.Ptt ? "TX" : "RX";
            _pttLabel.ForeColor = radio.Ptt ? Color.Red : Color.DarkGreen;
            _frequencyStatus.Text = $"{radio.RxFrequencyHz / 1e6:0.000000} MHz";

            decimal liveFrequency = radio.RxFrequencyHz;

            if (liveFrequency >= _frequency.Minimum &&
                liveFrequency <= _frequency.Maximum &&
                _frequency.Value != liveFrequency)
            {
                _frequency.Value = liveFrequency;
            }
        }

        RuntimeSnapshot m = _session.Metrics.Snapshot();

        _catLabel.Text = m.CatClients > 0
            ? $"CONNECTED ({m.CatClients})"
            : "WAITING";

        _catLabel.ForeColor = m.CatClients > 0
            ? Color.DarkGreen
            : SystemColors.ControlText;

        _rxRate.Text = $"{m.RxRateMsps:0.000} MS/s";
        _iqLevel.Text = $"{m.IqRmsDbfs:0.0} / {m.IqPeakDbfs:0.0} dBFS";
        _rxAudioLevel.Text = $"{m.RxAudioRmsDbfs:0.0} / {m.RxAudioPeakDbfs:0.0} dBFS";
        _txAudioLevel.Text = $"{m.TxAudioRmsDbfs:0.0} / {m.TxAudioPeakDbfs:0.0} dBFS";
        _txUnderruns.Text = m.TxUnderruns.ToString("N0");
    }

    private void OnLogLine(string line)
    {
        if (_closing || IsDisposed)
            return;

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => AppendLogLine(line)));
            }
            catch
            {
            }

            return;
        }

        AppendLogLine(line);
    }

    private void AppendLogLine(string line)
    {
        _log.AppendText(line + Environment.NewLine);

        const int maxChars = 1_000_000;

        if (_log.TextLength > maxChars)
        {
            _log.Select(0, _log.TextLength - maxChars);
            _log.SelectedText = "";
        }

        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private void ExportDiagnostics()
    {
        try
        {
            SaveControlsIntoSettings();

            using var dialog = new SaveFileDialog
            {
                Filter = "ZIP files (*.zip)|*.zip",
                FileName = $"ZynqRadio_Diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            DiagnosticBundle.Create(dialog.FileName, _settings);

            MessageBox.Show(
                this,
                "Diagnostic bundle created:\r\n" + dialog.FileName,
                "Diagnostics",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Diagnostic export failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async void OnFormClosingAsync(object? sender, FormClosingEventArgs e)
    {
        if (_closing)
            return;

        _closing = true;
        e.Cancel = true;

        try
        {
            try
            {
                SaveControlsIntoSettings();
            }
            catch
            {
            }

            await _session.StopAsync();

            AppLog.LineWritten -= OnLogLine;
            _timer.Stop();

            e.Cancel = false;
            Close();
        }
        catch
        {
            e.Cancel = false;
        }
    }

    private static decimal Clamp(NumericUpDown control, decimal value)
    {
        if (value < control.Minimum)
            return control.Minimum;

        if (value > control.Maximum)
            return control.Maximum;

        return value;
    }
}
