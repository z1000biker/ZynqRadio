using System.Diagnostics;
using ZynqRadio.Audio;
using ZynqRadio.Dsp;
using ZynqRadio.Diagnostics;

namespace ZynqRadio.Radio;

public sealed class RxEngine : IDisposable
{
    private readonly IioRadio _radio;
    private readonly RadioConfig _cfg;
    private IqStreaming? _stream;
    private WindowsAudioSink? _audio;
    private readonly RuntimeMetrics? _metrics;
    private readonly RxOutputRecorder _rxRecorder = new();
    private bool _lastPtt;
    private DateTime? _rxRecordStopAtUtc;

    public RxEngine(
        IioRadio radio,
        RadioConfig cfg,
        RuntimeMetrics? metrics = null)
    {
        _radio = radio;
        _cfg = cfg;
        _metrics = metrics;
    }

    public async Task RunAsync(
        CancellationToken ct)
    {
        if (!_radio.IsConnected)
            throw new InvalidOperationException("RX requires a connected IIO radio.");

        _audio = WindowsAudioSink.Open(_cfg);
        _stream = _radio.CreateIqStreaming("RX");

        if (_cfg.RxIOnlyTransport && _cfg.RxLoOffsetHz < 6_000)
        {
            throw new InvalidOperationException(
                "I-only RX transport requires an RX LO offset of at least 6 kHz. " +
                "Use the normal 10 kHz offset or disable I-only RX.");
        }

        _stream.OpenRx((nuint)_cfg.RxBufferSamples, _cfg.RxIOnlyTransport);

        var dsp = new RxDsp(
            _cfg.SampleRateHz,
            _cfg.RxLoOffsetHz,
            _cfg.AudioGain,
            _cfg.IqRightShift);

        short[] rawIq = new short[_cfg.RxBufferSamples * 2];

        int estimatedAudio = Math.Max(
            2048,
            _cfg.RxBufferSamples / dsp.DecimationFactor + 512);

        float[] audio = new float[estimatedAudio];

        Console.WriteLine();
        Console.WriteLine("RX DSP started");
        Console.WriteLine($"  IIO input     : {_cfg.SampleRateHz:N0} S/s");
        Console.WriteLine(
            _cfg.RxIOnlyTransport
                ? "  RX transport  : I-only (half bandwidth)"
                : "  RX transport  : full I/Q");
        Console.WriteLine($"  CAT dial      : {_radio.RxFrequencyHz:N0} Hz");
        Console.WriteLine($"  Hardware RX LO: {_radio.HardwareRxLoHz:N0} Hz");
        Console.WriteLine($"  DSP offset    : +{_cfg.RxLoOffsetHz:N0} Hz");
        Console.WriteLine("  Audio output  : 48,000 Hz mono");
        Console.WriteLine($"  Audio gain    : {_cfg.AudioGain:0.###}x");
        Console.WriteLine();

        long totalComplex = 0;
        var meterWatch = Stopwatch.StartNew();
        long meterStartComplex = 0;
        TimeSpan previousMeter = TimeSpan.Zero;

        while (!ct.IsCancellationRequested)
        {
            int shortsRead = _stream.ReadRx(rawIq);
            int complexRead = shortsRead / 2;
            totalComplex += complexRead;

            int audioSamples = dsp.Process(
                rawIq.AsSpan(0, shortsRead),
                audio);

            _audio.Write(audio, audioSamples);

            bool pttNow = _radio.Ptt;

            if (pttNow && !_lastPtt)
            {
                _rxRecorder.Start(_radio.RxFrequencyHz);
                _rxRecordStopAtUtc = null;
            }
            else if (!pttNow && _lastPtt)
            {
                _rxRecordStopAtUtc = DateTime.UtcNow.AddSeconds(2);
            }

            _lastPtt = pttNow;

            if (_rxRecorder.IsRecording)
            {
                _rxRecorder.Write(audio, audioSamples);

                if (!pttNow &&
                    _rxRecordStopAtUtc is DateTime stopAt &&
                    DateTime.UtcNow >= stopAt)
                {
                    _rxRecorder.Stop();
                    _rxRecordStopAtUtc = null;
                }
            }

            TimeSpan now = meterWatch.Elapsed;

            if ((now - previousMeter).TotalSeconds >= 1.0)
            {
                double seconds = (now - previousMeter).TotalSeconds;
                long delta = totalComplex - meterStartComplex;
                double rate = delta / seconds;

                Console.WriteLine(
                    $"RX {rate / 1e6:0.000} MS/s | " +
                    $"IQ {dsp.LastIqRmsDbfs,6:0.0} dBFS pk {dsp.LastIqPeakDbfs,6:0.0} | " +
                    $"Audio {dsp.LastAudioRmsDbfs,6:0.0} dBFS pk {dsp.LastAudioPeakDbfs,6:0.0} | " +
                    $"Dial {_radio.RxFrequencyHz / 1e6:0.000000} MHz");

                _metrics?.UpdateRx(
                    rate / 1e6,
                    dsp.LastIqRmsDbfs,
                    dsp.LastIqPeakDbfs,
                    dsp.LastAudioRmsDbfs,
                    dsp.LastAudioPeakDbfs);

                previousMeter = now;
                meterStartComplex = totalComplex;
            }

            await Task.Yield();
        }
    }

    public void Dispose()
    {
        _rxRecorder.Dispose();
        _stream?.Dispose();
        _stream = null;
        _audio?.Dispose();
        _audio = null;
    }
}
