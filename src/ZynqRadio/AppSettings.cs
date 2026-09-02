using System.Text.Json;
using ZynqRadio.Audio;
using ZynqRadio.Radio;

namespace ZynqRadio.Gui;

public sealed class AppSettings
{
    public string IioUri { get; set; } = "ip:pluto.local";
    public string CatHost { get; set; } = "127.0.0.1";
    public int CatPort { get; set; } = 4532;
    public long FrequencyHz { get; set; } = 144_174_000;
    public long SampleRateHz { get; set; } = 2_500_000;
    public long RfBandwidthHz { get; set; } = 700_000;
    public double RxGainDb { get; set; } = 40.0;
    public double TxGainDb { get; set; } = -80.0;
    public double TxMonitorRxGainDb { get; set; } = 0.0;
    public double AudioGain { get; set; } = 20.0;
    public double TxLevel { get; set; } = 0.05;
    public long RxLoOffsetHz { get; set; } = 10_000;
    public int RxBufferSamples { get; set; } = 32_768;
    public int TxBufferSamples { get; set; } = 32_768;
    public int IqRightShift { get; set; } = 0;
    public int TxQSign { get; set; } = 1;
    public bool RxIOnlyTransport { get; set; } = true;
    public bool EnableRxAudio { get; set; } = true;
    public bool EnableTxAudio { get; set; } = true;
    public bool AllowTransmit { get; set; } = false;
    public bool VerboseCatLogging { get; set; } = false;
    public string? RxAudioDeviceId { get; set; }
    public string? TxAudioDeviceId { get; set; }

    public static string SettingsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZynqRadio");
    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions()) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        string json = JsonSerializer.Serialize(this, JsonOptions());
        File.WriteAllText(SettingsPath, json);
    }

    public RadioConfig ToRadioConfig(AudioDeviceDescriptor? rxDevice, AudioDeviceDescriptor? txDevice)
    {
        return new RadioConfig
        {
            IioUri = IioUri,
            CatHost = CatHost,
            CatPort = CatPort,
            InitialFrequencyHz = FrequencyHz,
            SampleRateHz = SampleRateHz,
            RfBandwidthHz = RfBandwidthHz,
            RxGainDb = RxGainDb,
            TxGainDb = TxGainDb,
            AllowTransmit = AllowTransmit,
            NoRadio = false,
            EnableRxAudio = EnableRxAudio,
            EnableTxAudio = EnableTxAudio,
            ListAudioDevices = false,
            ListCaptureDevices = false,
            AudioOutputDeviceNumber = EnableRxAudio ? rxDevice?.Index : null,
            TxAudioInputDeviceNumber = EnableTxAudio ? txDevice?.Index : null,
            RxLoOffsetHz = RxLoOffsetHz,
            AudioGain = AudioGain,
            RxBufferSamples = RxBufferSamples,
            TxBufferSamples = TxBufferSamples,
            IqRightShift = IqRightShift,
            RxIOnlyTransport = RxIOnlyTransport,
            TxLevel = TxLevel,
            TxQSign = TxQSign,
            TxMonitorRxGainDb = TxMonitorRxGainDb,
            VerboseCatLogging = VerboseCatLogging
        };
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
}
