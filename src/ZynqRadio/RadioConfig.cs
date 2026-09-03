using System.Globalization;

namespace ZynqRadio.Radio;

public sealed class RadioConfig
{
    public string IioUri { get; init; } = "ip:pluto.local";
    public string CatHost { get; init; } = "127.0.0.1";
    public int CatPort { get; init; } = 4532;

    public long InitialFrequencyHz { get; init; } = 144_174_000;
    public long SampleRateHz { get; init; } = 2_500_000;
    public long RfBandwidthHz { get; init; } = 700_000;

    public double RxGainDb { get; init; } = 40.0;

    public double TxGainDb { get; init; } = -80.0;

    public bool AllowTransmit { get; init; }
    public bool NoRadio { get; init; }

    public bool EnableRxAudio { get; init; }
    public bool EnableTxAudio { get; init; }

    public bool ListAudioDevices { get; init; }
    public bool ListCaptureDevices { get; init; }

    public string? AudioOutputName { get; init; }
    public int? AudioOutputDeviceNumber { get; init; }

    public string? TxAudioInputName { get; init; }
    public int? TxAudioInputDeviceNumber { get; init; }

    public long RxLoOffsetHz { get; init; } = 10_000;

    public double AudioGain { get; init; } = 20.0;
    public int RxBufferSamples { get; init; } = 32_768;
    public int TxBufferSamples { get; init; } = 32_768;

    public int IqRightShift { get; init; } = 0;

    public bool RxIOnlyTransport { get; init; } = true;

    public double TxLevel { get; init; } = 0.10;

    public int TxQSign { get; init; } = 1;

    public double TxMonitorRxGainDb { get; init; } = 0.0;
    public bool VerboseCatLogging { get; init; }

    public static RadioConfig Load(string[] args)
    {
        string? Get(string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        bool Has(string key) =>
            args.Contains(key, StringComparer.OrdinalIgnoreCase);

        long L(string? s, long d) =>
            long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v)
                ? v : d;

        int I(string? s, int d) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v : d;

        double D(string? s, double d) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? v : d;

        int? ParseNullableInt(string? text)
        {
            if (text is not null &&
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                return v;
            return null;
        }

        int qSign = I(Get("--tx-q-sign"), 1);
        qSign = qSign < 0 ? -1 : 1;

        return new RadioConfig
        {
            IioUri =
                Get("--uri") ??
                Environment.GetEnvironmentVariable("ZYNQRADIO_IIO_URI") ??
                "ip:pluto.local",

            CatHost = Get("--cat-host") ?? "127.0.0.1",
            CatPort = I(Get("--cat-port"), 4532),

            InitialFrequencyHz = L(Get("--freq"), 144_174_000),
            SampleRateHz = L(Get("--rate"), 2_500_000),
            RfBandwidthHz = L(Get("--bw"), 700_000),

            RxGainDb = D(Get("--rx-gain"), 40.0),
            TxGainDb = D(Get("--tx-gain"), -80.0),

            AllowTransmit = Has("--allow-tx"),
            NoRadio = Has("--no-radio"),

            EnableRxAudio = Has("--rx-audio"),
            EnableTxAudio = Has("--tx-audio"),

            ListAudioDevices = Has("--list-audio"),
            ListCaptureDevices = Has("--list-capture-audio"),

            AudioOutputName = Get("--audio-out"),
            AudioOutputDeviceNumber = ParseNullableInt(Get("--audio-device")),

            TxAudioInputName = Get("--tx-audio-in"),
            TxAudioInputDeviceNumber = ParseNullableInt(Get("--tx-audio-device")),

            RxLoOffsetHz = L(Get("--rx-offset"), 10_000),
            AudioGain = D(Get("--audio-gain"), 20.0),

            RxBufferSamples = I(Get("--rx-buffer"), 32_768),
            TxBufferSamples = I(Get("--tx-buffer"), 32_768),

            IqRightShift = I(Get("--iq-shift"), 0),
            RxIOnlyTransport = !Has("--rx-full-iq"),

            TxLevel = D(Get("--tx-level"), 0.10),
            TxQSign = qSign,
            TxMonitorRxGainDb = D(Get("--tx-monitor-rx-gain"), 0.0),
            VerboseCatLogging = Has("--verbose-cat")
        };
    }
}
