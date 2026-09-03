using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ZynqRadio.Audio;

public sealed class TxAudioCapture : IDisposable
{
    private readonly MMDevice _device;
    private readonly WasapiCapture _capture;
    private readonly ConcurrentQueue<float> _samples = new();

    private long _sampleCount;
    private double _sumSq;
    private double _peak;

    private long _channelFrameCount;
    private double _leftSumSq;
    private double _rightSumSq;
    private double _leftPeak;
    private double _rightPeak;

    private readonly object _meterLock = new();

    public int SampleRate => _capture.WaveFormat.SampleRate;
    public int Channels => _capture.WaveFormat.Channels;
    public int BufferedSamples => _samples.Count;

    private TxAudioCapture(MMDevice device)
    {
        _device = device;
        _capture = new WasapiCapture(_device);

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null)
                Console.WriteLine("TX audio capture stopped: " + e.Exception.Message);
        };
    }

    public static void PrintDevices()
    {
        List<AudioDeviceDescriptor> devices = GetCaptureDevices();

        Console.WriteLine("Active Windows WASAPI capture endpoints:");

        if (devices.Count == 0)
        {
            Console.WriteLine("  No active capture endpoints found.");
            return;
        }

        foreach (AudioDeviceDescriptor device in devices)
            Console.WriteLine($"  {device.Index}: {device.Name}");

        Console.WriteLine();
        Console.WriteLine(
            "For WSJT-X TX use a SECOND virtual cable. " +
            "WSJT-X plays to its playback side, ZynqRadio captures its recording side.");
    }

    public static TxAudioCapture Open(Radio.RadioConfig cfg)
    {
        List<AudioDeviceDescriptor> devices = GetCaptureDevices();

        if (devices.Count == 0)
            throw new InvalidOperationException("No active Windows capture endpoints were found.");

        AudioDeviceDescriptor? selected = null;

        if (cfg.TxAudioInputDeviceNumber is int requestedIndex)
        {
            selected = devices.FirstOrDefault(d => d.Index == requestedIndex);

            if (selected is null)
                throw new ArgumentOutOfRangeException(
                    "--tx-audio-device",
                    $"WASAPI capture endpoint {requestedIndex} does not exist.");
        }
        else if (!string.IsNullOrWhiteSpace(cfg.TxAudioInputName))
        {
            string wanted = cfg.TxAudioInputName;

            selected = devices.FirstOrDefault(
                d => d.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase));

            if (selected is null)
                throw new InvalidOperationException(
                    $"No active capture endpoint contains '{wanted}'. Run with --list-capture-audio.");
        }
        else
        {
            selected = devices.FirstOrDefault(
                d => d.Name.Contains("CABLE Out 16ch", StringComparison.OrdinalIgnoreCase))
                ?? devices.FirstOrDefault(
                    d => d.Name.Contains("CABLE Output 16ch", StringComparison.OrdinalIgnoreCase));

            if (selected is null)
                throw new InvalidOperationException(
                    "No obvious second virtual-cable capture endpoint was found. " +
                    "Run with --list-capture-audio and select one using " +
                    "--tx-audio-device N or --tx-audio-in \"name\".");
        }

        using var enumerator = new MMDeviceEnumerator();
        MMDevice live = enumerator.GetDevice(selected.Id);

        var result = new TxAudioCapture(live);

        Console.WriteLine($"TX audio capture: {selected.Index}: {selected.Name}");
        Console.WriteLine($"TX capture format: {result._capture.WaveFormat}");

        if (result.SampleRate != 48_000)
        {
            result.Dispose();
            throw new InvalidOperationException(
                $"TX capture endpoint is {result.SampleRate} Hz. " +
                "Set that Windows virtual cable to 48000 Hz before using TX1.");
        }

        result._capture.StartRecording();
        return result;
    }

    public bool TryRead(out float sample)
    {
        return _samples.TryDequeue(out sample);
    }

    public void Clear()
    {
        while (_samples.TryDequeue(out _))
        {
        }
    }

    public (double RmsDbfs, double PeakDbfs, long Samples) TakeMeter()
    {
        lock (_meterLock)
        {
            double rms = _sampleCount > 0
                ? Math.Sqrt(_sumSq / _sampleCount)
                : 0.0;

            double peak = _peak;
            long count = _sampleCount;

            _sampleCount = 0;
            _sumSq = 0.0;
            _peak = 0.0;

            return (ToDb(rms), ToDb(peak), count);
        }
    }

    public (double LeftRmsDbfs,
            double LeftPeakDbfs,
            double RightRmsDbfs,
            double RightPeakDbfs,
            long Frames) TakeChannelMeter()
    {
        lock (_meterLock)
        {
            double leftRms =
                _channelFrameCount > 0
                    ? Math.Sqrt(_leftSumSq / _channelFrameCount)
                    : 0.0;

            double rightRms =
                _channelFrameCount > 0
                    ? Math.Sqrt(_rightSumSq / _channelFrameCount)
                    : 0.0;

            double leftPeak = _leftPeak;
            double rightPeak = _rightPeak;
            long frames = _channelFrameCount;

            _channelFrameCount = 0;
            _leftSumSq = 0.0;
            _rightSumSq = 0.0;
            _leftPeak = 0.0;
            _rightPeak = 0.0;

            return (
                ToDb(leftRms),
                ToDb(leftPeak),
                ToDb(rightRms),
                ToDb(rightPeak),
                frames);
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        WaveFormat f = _capture.WaveFormat;

        int channels = Math.Max(1, f.Channels);
        int bytesPerSample = f.BitsPerSample / 8;
        int frameBytes = bytesPerSample * channels;

        if (frameBytes <= 0)
            return;

        for (int offset = 0;
             offset + frameBytes <= e.BytesRecorded;
             offset += frameBytes)
        {
            double sum = 0.0;
            float left = 0.0f;
            float right = 0.0f;

            for (int ch = 0; ch < channels; ch++)
            {
                int sampleOffset = offset + ch * bytesPerSample;
                float value = ReadSample(e.Buffer, sampleOffset, f);

                if (ch == 0)
                    left = value;
                if (ch == 1)
                    right = value;

                sum += value;
            }

            float mono = (float)(sum / channels);
            _samples.Enqueue(mono);

            lock (_meterLock)
            {
                double x = mono;
                _sumSq += x * x;
                _sampleCount++;

                double a = Math.Abs(x);
                if (a > _peak)
                    _peak = a;

                double l = left;
                double r = channels > 1 ? right : left;

                _leftSumSq += l * l;
                _rightSumSq += r * r;

                double la = Math.Abs(l);
                double ra = Math.Abs(r);

                if (la > _leftPeak)
                    _leftPeak = la;
                if (ra > _rightPeak)
                    _rightPeak = ra;

                _channelFrameCount++;
            }
        }

        while (_samples.Count > 48_000 * 5 && _samples.TryDequeue(out _))
        {
        }
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat f)
    {
        if (f.Encoding == WaveFormatEncoding.IeeeFloat && f.BitsPerSample == 32)
            return BitConverter.ToSingle(buffer, offset);

        if (f.Encoding == WaveFormatEncoding.Pcm)
        {
            if (f.BitsPerSample == 16)
            {
                short v = BitConverter.ToInt16(buffer, offset);
                return v / 32768.0f;
            }

            if (f.BitsPerSample == 24)
            {
                int v =
                    buffer[offset] |
                    (buffer[offset + 1] << 8) |
                    (buffer[offset + 2] << 16);

                if ((v & 0x00800000) != 0)
                    v |= unchecked((int)0xff000000);

                return v / 8388608.0f;
            }

            if (f.BitsPerSample == 32)
            {
                int v = BitConverter.ToInt32(buffer, offset);
                return v / 2147483648.0f;
            }
        }

        throw new NotSupportedException($"Unsupported TX audio capture format: {f}");
    }

    private static double ToDb(double x)
    {
        if (x <= 1e-12)
            return -120.0;

        return 20.0 * Math.Log10(x);
    }

    public void Dispose()
    {
        try
        {
            _capture.StopRecording();
        }
        catch
        {
        }

        _capture.Dispose();
        _device.Dispose();
    }

    public static List<AudioDeviceDescriptor> GetCaptureDevices()
    {
        var result = new List<AudioDeviceDescriptor>();
        using var enumerator = new MMDeviceEnumerator();

        MMDeviceCollection collection = enumerator.EnumerateAudioEndPoints(
            DataFlow.Capture,
            DeviceState.Active);

        for (int i = 0; i < collection.Count; i++)
        {
            MMDevice device = collection[i];

            try
            {
                result.Add(new AudioDeviceDescriptor(i, device.FriendlyName, device.ID));
            }
            finally
            {
                device.Dispose();
            }
        }

        return result;
    }
}
