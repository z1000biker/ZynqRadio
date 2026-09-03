using System.Diagnostics;
using ZynqRadio.Audio;
using ZynqRadio.Dsp;
using ZynqRadio.Diagnostics;

namespace ZynqRadio.Radio;

public sealed class TxEngine : IDisposable
{
    private const int AudioRate = 48_000;
    private const int StartReserveSamples = 16_800;
    private const int TailSilenceSamples = 5_760;

    private readonly IioRadio _radio;
    private readonly RadioConfig _cfg;

    private TxAudioCapture? _capture;
    private IqStreaming? _stream;
    private TxDsp? _dsp;

    private bool _wasPtt;
    private bool _rfActive;
    private bool _drainingTail;

    private long _underrunAudioSamples;
    private long _consumedAudioSamples;
    private long _tailSilenceRun;

    private readonly Stopwatch _meter = Stopwatch.StartNew();
    private TimeSpan _lastMeter;

    private readonly RuntimeMetrics? _metrics;
    private readonly TxInputRecorder _txInputRecorder = new();

    private int _minimumQueueDepth = int.MaxValue;

    private long _timingBuffers;
    private long _deadlineMisses;
    private double _sumGenerateMs;
    private double _sumPushMs;
    private double _maxGenerateMs;
    private double _maxPushMs;
    private double _maxCycleMs;
    private readonly Stopwatch _timingReport = Stopwatch.StartNew();
    private TimeSpan _lastTimingReport;

    public TxEngine(
        IioRadio radio,
        RadioConfig cfg,
        RuntimeMetrics? metrics = null)
    {
        _radio = radio;
        _cfg = cfg;
        _metrics = metrics;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _capture = TxAudioCapture.Open(_cfg);

        _dsp = new TxDsp(
            _cfg.SampleRateHz,
            _cfg.TxLevel,
            _cfg.TxQSign);

        if (_cfg.AllowTransmit)
        {
            _stream = _radio.CreateIqStreaming("TX");
            _stream.OpenTx((nuint)_cfg.TxBufferSamples);

            Console.WriteLine(
                $"TX IIO stream armed: {_cfg.TxBufferSamples:N0} complex samples/buffer");
        }
        else
        {
            Console.WriteLine(
                "TX audio is being captured, but RF TX remains disabled.");
        }

        short[] txIq = new short[_cfg.TxBufferSamples * 2];

        int sourceSamplesPerRfBuffer = checked(
            (int)Math.Ceiling(
                _cfg.TxBufferSamples *
                (double)AudioRate /
                _cfg.SampleRateHz) + 8);

        double rfBufferDurationMs =
            _cfg.TxBufferSamples * 1000.0 / _cfg.SampleRateHz;

        Console.WriteLine();
        Console.WriteLine("TX subsystem started");
        Console.WriteLine($"  RF rate       : {_cfg.SampleRateHz:N0} S/s");
        Console.WriteLine($"  TX digital lvl: {_cfg.TxLevel:0.###}");
        Console.WriteLine($"  TX Q sign     : {_cfg.TxQSign}");
        Console.WriteLine($"  TX HW gain    : {_cfg.TxGainDb:0.##} dB");
        Console.WriteLine("  RX during TX  : ACTIVE");
        Console.WriteLine($"  TX-monitor RX : {_cfg.TxMonitorRxGainDb:0.##} dB");
        Console.WriteLine($"  TX audio/block: ~{sourceSamplesPerRfBuffer:N0} samples");
        Console.WriteLine($"  RF buffer time : {rfBufferDurationMs:0.000} ms");
        Console.WriteLine($"  TX start reserve: {StartReserveSamples:N0} samples ({StartReserveSamples * 1000.0 / AudioRate:0} ms)");
        Console.WriteLine();

        while (!ct.IsCancellationRequested)
        {
            bool catPtt = _radio.Ptt;

            if (catPtt != _wasPtt)
            {
                if (catPtt)
                {
                    await BeginTransmissionAsync(ct);
                }
                else if (_rfActive)
                {
                    _drainingTail = true;
                    _tailSilenceRun = 0;
                    _radio.SetRxGain(_cfg.TxMonitorRxGainDb);

                    Console.WriteLine(
                        $"CAT PTT OFF: draining buffered TX tail; queue={_capture.BufferedSamples:N0}");
                }

                _wasPtt = catPtt;
            }

            bool shouldGenerateRf =
                _cfg.AllowTransmit &&
                _stream is not null &&
                _rfActive;

            if (shouldGenerateRf)
            {
                int required = sourceSamplesPerRfBuffer + 64;

                if (!_drainingTail)
                {
                    var wait = Stopwatch.StartNew();

                    while (!ct.IsCancellationRequested &&
                           _radio.Ptt &&
                           _capture.BufferedSamples < required)
                    {
                        await Task.Delay(1, ct).ConfigureAwait(false);

                        if (wait.ElapsedMilliseconds > 250)
                        {
                            Console.WriteLine(
                                $"TX FIFO warning: waiting for source audio; queue={_capture.BufferedSamples:N0}, need={required:N0}");
                            wait.Restart();
                        }
                    }
                }

                int depth = _capture.BufferedSamples;
                if (depth < _minimumQueueDepth)
                    _minimumQueueDepth = depth;

                var cycleWatch = Stopwatch.StartNew();
                var generateWatch = Stopwatch.StartNew();

                _dsp.Generate(txIq, NextAudioNoGaps);

                generateWatch.Stop();
                var pushWatch = Stopwatch.StartNew();

                _stream!.WriteTx(txIq);

                pushWatch.Stop();
                cycleWatch.Stop();

                double genMs = generateWatch.Elapsed.TotalMilliseconds;
                double pushMs = pushWatch.Elapsed.TotalMilliseconds;
                double cycleMs = cycleWatch.Elapsed.TotalMilliseconds;

                _timingBuffers++;
                _sumGenerateMs += genMs;
                _sumPushMs += pushMs;

                if (genMs > _maxGenerateMs)
                    _maxGenerateMs = genMs;
                if (pushMs > _maxPushMs)
                    _maxPushMs = pushMs;
                if (cycleMs > _maxCycleMs)
                    _maxCycleMs = cycleMs;

                if (cycleMs > rfBufferDurationMs * 1.02)
                    _deadlineMisses++;

                TimeSpan timingNow = _timingReport.Elapsed;

                if ((timingNow - _lastTimingReport).TotalSeconds >= 1.0)
                {
                    double avgGen = _timingBuffers > 0
                        ? _sumGenerateMs / _timingBuffers
                        : 0.0;

                    double avgPush = _timingBuffers > 0
                        ? _sumPushMs / _timingBuffers
                        : 0.0;

                    Console.WriteLine(
                        $"TX IIO timing | buf {rfBufferDurationMs:0.00} ms | " +
                        $"gen avg/max {avgGen:0.00}/{_maxGenerateMs:0.00} | " +
                        $"push avg/max {avgPush:0.00}/{_maxPushMs:0.00} | " +
                        $"cycle max {_maxCycleMs:0.00} | " +
                        $"deadline misses {_deadlineMisses:N0}/{_timingBuffers:N0}");

                    _lastTimingReport = timingNow;
                }

                if (_drainingTail && _tailSilenceRun >= TailSilenceSamples)
                {
                    EndTransmission();
                    await Task.Delay(5, ct).ConfigureAwait(false);
                }
                else
                {
                    await Task.Yield();
                }
            }
            else
            {
                if (!catPtt && !_drainingTail && !_rfActive)
                    _capture.Clear();

                await Task.Delay(5, ct).ConfigureAwait(false);
            }

            TimeSpan now = _meter.Elapsed;

            if ((now - _lastMeter).TotalSeconds >= 1.0)
            {
                var m = _capture.TakeMeter();
                var ch = _capture.TakeChannelMeter();

                _metrics?.UpdateTx(
                    m.RmsDbfs,
                    m.PeakDbfs,
                    _underrunAudioSamples);

                if (catPtt || _rfActive || m.Samples > 0)
                {
                    string phase = _drainingTail
                        ? "TAIL"
                        : catPtt
                            ? "PTT"
                            : "IDLE";

                    Console.WriteLine(
                        $"TX audio {m.RmsDbfs,6:0.0} dBFS pk {m.PeakDbfs,6:0.0} | " +
                        $"L {ch.LeftRmsDbfs,6:0.0}/{ch.LeftPeakDbfs,6:0.0} " +
                        $"R {ch.RightRmsDbfs,6:0.0}/{ch.RightPeakDbfs,6:0.0} | " +
                        $"{phase,-4} | queue {_capture.BufferedSamples,6:N0} | " +
                        $"synthetic zeros {_underrunAudioSamples:N0}");
                }

                _lastMeter = now;
            }
        }

        async Task BeginTransmissionAsync(CancellationToken token)
        {
            _dsp!.Reset();

            _underrunAudioSamples = 0;
            _consumedAudioSamples = 0;
            _tailSilenceRun = 0;
            _minimumQueueDepth = int.MaxValue;

            _timingBuffers = 0;
            _deadlineMisses = 0;
            _sumGenerateMs = 0.0;
            _sumPushMs = 0.0;
            _maxGenerateMs = 0.0;
            _maxPushMs = 0.0;
            _maxCycleMs = 0.0;
            _lastTimingReport = _timingReport.Elapsed;

            _drainingTail = false;
            _rfActive = false;

            _txInputRecorder.Start(_radio.TxFrequencyHz);

            var wait = Stopwatch.StartNew();

            while (_radio.Ptt &&
                   !token.IsCancellationRequested &&
                   _capture!.BufferedSamples < StartReserveSamples &&
                   wait.ElapsedMilliseconds < 700)
            {
                await Task.Delay(1, token).ConfigureAwait(false);
            }

            int buffered = _capture!.BufferedSamples;

            if (!_radio.Ptt)
            {
                Console.WriteLine(
                    "PTT ended before TX reserve was ready; transmission cancelled.");

                _txInputRecorder.Stop();
                _capture.Clear();
                return;
            }

            _rfActive = true;

            Console.WriteLine(
                $"TX reserve ready: {buffered:N0} samples " +
                $"({buffered * 1000.0 / AudioRate:0.0} ms) " +
                $"after {wait.ElapsedMilliseconds:N0} ms; RF stream START");
        }

        float NextAudioNoGaps()
        {
            var wait = new SpinWait();
            var sw = Stopwatch.StartNew();

            while (!ct.IsCancellationRequested)
            {
                if (_capture!.TryRead(out float sample))
                {
                    _txInputRecorder.Write(sample);
                    _consumedAudioSamples++;

                    if (_drainingTail)
                    {
                        if (Math.Abs(sample) < 1.0e-5f)
                            _tailSilenceRun++;
                        else
                            _tailSilenceRun = 0;
                    }

                    return sample;
                }

                if (sw.ElapsedMilliseconds < 2)
                    wait.SpinOnce();
                else
                    Thread.Sleep(1);

                if (sw.ElapsedMilliseconds > 100)
                {
                    _underrunAudioSamples++;

                    if ((_underrunAudioSamples % 100) == 1)
                    {
                        Console.WriteLine(
                            "TX source FIFO STARVED: waiting for real audio sample.");
                    }

                    sw.Restart();
                }
            }

            return 0.0f;
        }

        void EndTransmission()
        {
            Console.WriteLine(
                $"TX stream END: consumed {_consumedAudioSamples:N0} audio samples, " +
                $"minimum queue {_minimumQueueDepth:N0}, " +
                $"source starvation events {_underrunAudioSamples:N0}");

            double finalAvgGen = _timingBuffers > 0
                ? _sumGenerateMs / _timingBuffers
                : 0.0;

            double finalAvgPush = _timingBuffers > 0
                ? _sumPushMs / _timingBuffers
                : 0.0;

            Console.WriteLine(
                $"TX IIO END | buf {rfBufferDurationMs:0.00} ms | " +
                $"gen avg/max {finalAvgGen:0.00}/{_maxGenerateMs:0.00} | " +
                $"push avg/max {finalAvgPush:0.00}/{_maxPushMs:0.00} | " +
                $"cycle max {_maxCycleMs:0.00} | " +
                $"deadline misses {_deadlineMisses:N0}/{_timingBuffers:N0}");

            _rfActive = false;
            _drainingTail = false;

            _txInputRecorder.Stop();
            _capture!.Clear();
            _radio.SetRxGain(_cfg.RxGainDb);
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;

        _txInputRecorder.Dispose();

        _capture?.Dispose();
        _capture = null;
    }
}
