using System.Globalization;

namespace ZynqRadio.Radio;

public sealed class IioRadio : IRadio
{
    private readonly RadioConfig _cfg;

    private IntPtr _ctx;
    private IntPtr _phy;
    private IntPtr _rxLo;
    private IntPtr _txLo;
    private IntPtr _rxPhy;
    private IntPtr _txPhy;

    public bool IsConnected => _ctx != IntPtr.Zero;
    public bool Ptt { get; private set; }

    public long RxFrequencyHz { get; private set; }
    public long TxFrequencyHz { get; private set; }

    public long HardwareRxLoHz { get; private set; }
    public long HardwareTxLoHz { get; private set; }

    public string Mode { get; private set; } = "USB";
    public int PassbandHz { get; private set; } = 3000;

    internal IntPtr ContextHandle => _ctx;

    public IioRadio(RadioConfig cfg)
    {
        _cfg = cfg;
        RxFrequencyHz = cfg.InitialFrequencyHz;
        TxFrequencyHz = cfg.InitialFrequencyHz;
        HardwareRxLoHz = cfg.InitialFrequencyHz;
        HardwareTxLoHz = cfg.InitialFrequencyHz;
    }

    public void Connect()
    {
        _ctx = NativeIio.iio_create_context_from_uri(_cfg.IioUri);

        if (_ctx == IntPtr.Zero)
            throw new IOException($"Could not create IIO context for {_cfg.IioUri}");

        _phy = FindDevice("ad9361-phy");

        _rxLo = FindChannel(_phy, "altvoltage0", true);
        _txLo = FindChannel(_phy, "altvoltage1", true);

        _rxPhy = FindChannel(_phy, "voltage0", false);
        _txPhy = FindChannel(_phy, "voltage0", true);

        WriteLong(_rxPhy, "sampling_frequency", _cfg.SampleRateHz);
        WriteLong(_txPhy, "sampling_frequency", _cfg.SampleRateHz);

        WriteLong(_rxPhy, "rf_bandwidth", _cfg.RfBandwidthHz);
        WriteLong(_txPhy, "rf_bandwidth", _cfg.RfBandwidthHz);

        TryWriteString(_rxPhy, "gain_control_mode", "manual");
        SetRxGain(_cfg.RxGainDb);
        SetTxGain(_cfg.TxGainDb);

        SetFrequency(RxFrequencyHz);
        SetTxFrequency(TxFrequencyHz);

        Console.WriteLine("Connected directly to ad9361-phy through libiio.");

        if (_cfg.AllowTransmit)
            Console.WriteLine("TX permission ENABLED by --allow-tx.");
        else
            Console.WriteLine("TX is inhibited unless --allow-tx is supplied.");
    }

    public void SetFrequency(long hz)
    {
        ValidateDialFrequency(hz);
        RxFrequencyHz = hz;

        long hardwareHz = _cfg.EnableRxAudio
            ? hz - _cfg.RxLoOffsetHz
            : hz;

        ValidateHardwareFrequency(hardwareHz);
        HardwareRxLoHz = hardwareHz;

        if (IsConnected)
            WriteLong(_rxLo, "frequency", HardwareRxLoHz);

        if (_cfg.EnableRxAudio)
        {
            Console.WriteLine(
                $"RX dial = {RxFrequencyHz:N0} Hz | HW LO = {HardwareRxLoHz:N0} Hz | DSP = +{_cfg.RxLoOffsetHz:N0} Hz");
        }
        else
        {
            Console.WriteLine($"RX LO = {HardwareRxLoHz:N0} Hz");
        }
    }

    public void SetTxFrequency(long hz)
    {
        ValidateDialFrequency(hz);
        TxFrequencyHz = hz;
        HardwareTxLoHz = hz;

        if (IsConnected)
            WriteLong(_txLo, "frequency", HardwareTxLoHz);

        Console.WriteLine($"TX LO = {HardwareTxLoHz:N0} Hz");
    }

    public void SetMode(string mode, int passbandHz)
    {
        Mode = mode.ToUpperInvariant();

        if (passbandHz > 0)
            PassbandHz = passbandHz;

        Console.WriteLine($"Mode = {Mode} {PassbandHz} Hz");
    }

    public void SetPtt(bool enabled)
    {
        if (enabled && !_cfg.AllowTransmit)
        {
            Ptt = false;
            Console.WriteLine("PTT requested but TX inhibited.");
            return;
        }

        bool changed = Ptt != enabled;
        Ptt = enabled;

        if (IsConnected && _cfg.EnableRxAudio)
        {
            if (enabled)
                SetRxGain(_cfg.TxMonitorRxGainDb);
            else
                SetRxGain(_cfg.RxGainDb);
        }

        if (changed)
        {
            Console.WriteLine(
                enabled
                    ? "PTT -> TX, RX remains active (full duplex)"
                    : "PTT -> RX only");
        }
    }

    public void SetRxGain(double db)
    {
        if (IsConnected)
            TryWriteDouble(_rxPhy, "hardwaregain", db);
    }

    public void SetTxGain(double db)
    {
        if (IsConnected)
            TryWriteDouble(_txPhy, "hardwaregain", db);
    }

    public IqStreaming CreateIqStreaming(string role)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Radio is not connected.");

        return new IqStreaming(_cfg.IioUri, role);
    }

    private IntPtr FindDevice(string name)
    {
        IntPtr p = NativeIio.iio_context_find_device(_ctx, name);

        if (p == IntPtr.Zero)
            throw new IOException($"IIO device {name} not found");

        return p;
    }

    private static IntPtr FindChannel(IntPtr device, string name, bool output)
    {
        IntPtr p = NativeIio.iio_device_find_channel(device, name, output);

        if (p == IntPtr.Zero)
            throw new IOException($"IIO channel {name} output={output} not found");

        return p;
    }

    private static void WriteLong(IntPtr channel, string attribute, long value)
    {
        NativeIio.Check(
            NativeIio.iio_channel_attr_write_longlong(channel, attribute, value),
            $"write {attribute}={value}");
    }

    private static void TryWriteDouble(IntPtr channel, string attribute, double value)
    {
        try
        {
            NativeIio.Check(
                NativeIio.iio_channel_attr_write_double(channel, attribute, value),
                $"write {attribute}=" + value.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Warning: " + ex.Message);
        }
    }

    private static void TryWriteString(IntPtr channel, string attribute, string value)
    {
        try
        {
            NativeIio.Check(
                NativeIio.iio_channel_attr_write(channel, attribute, value),
                $"write {attribute}={value}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Warning: " + ex.Message);
        }
    }

    private static void ValidateDialFrequency(long hz)
    {
        if (hz < 70_000_000 || hz > 6_000_000_000)
            throw new ArgumentOutOfRangeException(nameof(hz), "Dial frequency is outside the configured AD936x range.");
    }

    private static void ValidateHardwareFrequency(long hz)
    {
        if (hz < 70_000_000 || hz > 6_000_000_000)
            throw new ArgumentOutOfRangeException(nameof(hz), "Computed hardware LO is outside the configured AD936x range.");
    }

    public void Dispose()
    {
        try
        {
            SetPtt(false);
        }
        catch
        {
        }

        if (_ctx != IntPtr.Zero)
        {
            NativeIio.iio_context_destroy(_ctx);
            _ctx = IntPtr.Zero;
        }
    }
}
