namespace ZynqRadio.Dsp;

public sealed class RxDsp
{
    private const int IntermediateRate = 50_000;
    private const int OutputRate = 48_000;

    private readonly int _inputRate;
    private readonly int _decimation;
    private readonly int _rightShift;
    private readonly double _audioGain;

    private readonly MovingAverageComplex _ma1;
    private readonly MovingAverageComplex _ma2;
    private readonly MovingAverageComplex _ma3;
    private readonly FirFilter _audioLowPass;
    private readonly LinearResampler _resampler;

    private int _decimationCounter;

    private double _oscCos = 1.0;
    private double _oscSin = 0.0;
    private readonly double _stepCos;
    private readonly double _stepSin;
    private int _oscCounter;

    private double _dcPrevX;
    private double _dcPrevY;

    public double LastIqRmsDbfs { get; private set; } = -120.0;
    public double LastIqPeakDbfs { get; private set; } = -120.0;
    public double LastAudioRmsDbfs { get; private set; } = -120.0;
    public double LastAudioPeakDbfs { get; private set; } = -120.0;

    public int DecimationFactor => _decimation;

    public RxDsp(
        long inputRate,
        long dialAboveHardwareLoHz,
        double audioGain,
        int rightShift)
    {
        if (inputRate <= 0 || inputRate > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(inputRate));

        _inputRate = (int)inputRate;

        if (_inputRate % IntermediateRate != 0)
        {
            throw new ArgumentException(
                $"RX sample rate {_inputRate} must be an exact multiple of {IntermediateRate} for RX1 DSP.");
        }

        _decimation = _inputRate / IntermediateRate;

        if (_decimation < 2)
            throw new ArgumentException("RX sample rate is too low for the RX1 decimator.");

        _rightShift = Math.Clamp(rightShift, 0, 8);
        _audioGain = audioGain;

        _ma1 = new MovingAverageComplex(_decimation);
        _ma2 = new MovingAverageComplex(_decimation);
        _ma3 = new MovingAverageComplex(_decimation);

        _audioLowPass = new FirFilter(
            DesignLowPass(
                taps: 129,
                sampleRate: IntermediateRate,
                cutoffHz: 4_600.0));

        _resampler = new LinearResampler(IntermediateRate, OutputRate);

        double angle = -2.0 * Math.PI * dialAboveHardwareLoHz / _inputRate;
        _stepCos = Math.Cos(angle);
        _stepSin = Math.Sin(angle);
    }

    public int Process(
        ReadOnlySpan<short> iq,
        Span<float> audioOut)
    {
        int complexCount = iq.Length / 2;

        if (complexCount == 0)
            return 0;

        double iqSumSq = 0.0;
        double iqPeakSq = 0.0;
        double audioSumSq = 0.0;
        double audioPeak = 0.0;
        int audioCount = 0;
        int outCount = 0;

        for (int n = 0; n < complexCount; n++)
        {
            int rawI = iq[n * 2] >> _rightShift;
            int rawQ = iq[n * 2 + 1] >> _rightShift;

            double i = rawI / 2048.0;
            double q = rawQ / 2048.0;

            double magSq = (i * i + q * q) * 0.5;
            iqSumSq += magSq;

            if (magSq > iqPeakSq)
                iqPeakSq = magSq;

            double mixedI = i * _oscCos - q * _oscSin;
            double mixedQ = i * _oscSin + q * _oscCos;

            AdvanceOscillator();

            (double s1I, double s1Q) = _ma1.Process(mixedI, mixedQ);
            (double s2I, double s2Q) = _ma2.Process(s1I, s1Q);
            (double s3I, _) = _ma3.Process(s2I, s2Q);

            _decimationCounter++;

            if (_decimationCounter < _decimation)
                continue;

            _decimationCounter = 0;

            double filtered = _audioLowPass.Process(s3I);
            double dcBlocked = filtered - _dcPrevX + 0.995 * _dcPrevY;
            _dcPrevX = filtered;
            _dcPrevY = dcBlocked;

            double gained = dcBlocked * _audioGain;
            Span<float> remaining = audioOut[outCount..];
            int emitted = _resampler.Push((float)gained, remaining);

            for (int k = 0; k < emitted; k++)
            {
                float a = remaining[k];
                audioSumSq += a * a;
                double aa = Math.Abs(a);
                if (aa > audioPeak)
                    audioPeak = aa;
            }

            outCount += emitted;
            audioCount += emitted;

            if (outCount >= audioOut.Length)
                break;
        }

        LastIqRmsDbfs = ToDb(Math.Sqrt(iqSumSq / complexCount));
        LastIqPeakDbfs = ToDb(Math.Sqrt(iqPeakSq));

        if (audioCount > 0)
        {
            LastAudioRmsDbfs = ToDb(Math.Sqrt(audioSumSq / audioCount));
            LastAudioPeakDbfs = ToDb(audioPeak);
        }

        return outCount;
    }

    private void AdvanceOscillator()
    {
        double nextCos = _oscCos * _stepCos - _oscSin * _stepSin;
        double nextSin = _oscSin * _stepCos + _oscCos * _stepSin;

        _oscCos = nextCos;
        _oscSin = nextSin;
        _oscCounter++;

        if ((_oscCounter & 0x3fff) == 0)
        {
            double magnitude = Math.Sqrt(_oscCos * _oscCos + _oscSin * _oscSin);

            if (magnitude > 0.0)
            {
                _oscCos /= magnitude;
                _oscSin /= magnitude;
            }
        }
    }

    private static double[] DesignLowPass(
        int taps,
        int sampleRate,
        double cutoffHz)
    {
        if ((taps & 1) == 0)
            throw new ArgumentException("FIR tap count must be odd.");

        var h = new double[taps];
        int middle = (taps - 1) / 2;
        double sum = 0.0;

        for (int n = 0; n < taps; n++)
        {
            int k = n - middle;
            double ideal;

            if (k == 0)
            {
                ideal = 2.0 * cutoffHz / sampleRate;
            }
            else
            {
                ideal = Math.Sin(2.0 * Math.PI * cutoffHz * k / sampleRate) / (Math.PI * k);
            }

            double window = 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * n / (taps - 1));
            h[n] = ideal * window;
            sum += h[n];
        }

        for (int n = 0; n < taps; n++)
            h[n] /= sum;

        return h;
    }

    private static double ToDb(double linear)
    {
        if (linear <= 1e-12)
            return -120.0;

        return 20.0 * Math.Log10(linear);
    }

    private sealed class MovingAverageComplex
    {
        private readonly double[] _i;
        private readonly double[] _q;
        private int _index;
        private double _sumI;
        private double _sumQ;

        public MovingAverageComplex(int length)
        {
            _i = new double[length];
            _q = new double[length];
        }

        public (double I, double Q) Process(double i, double q)
        {
            _sumI += i - _i[_index];
            _sumQ += q - _q[_index];
            _i[_index] = i;
            _q[_index] = q;
            _index++;

            if (_index == _i.Length)
                _index = 0;

            return (_sumI / _i.Length, _sumQ / _q.Length);
        }
    }

    private sealed class FirFilter
    {
        private readonly double[] _coeff;
        private readonly double[] _history;
        private int _index;

        public FirFilter(double[] coefficients)
        {
            _coeff = coefficients;
            _history = new double[coefficients.Length];
        }

        public double Process(double sample)
        {
            _history[_index] = sample;
            double y = 0.0;
            int h = _index;

            for (int k = 0; k < _coeff.Length; k++)
            {
                y += _coeff[k] * _history[h];
                h--;
                if (h < 0)
                    h = _history.Length - 1;
            }

            _index++;
            if (_index == _history.Length)
                _index = 0;

            return y;
        }
    }

    private sealed class LinearResampler
    {
        private readonly double _sourceSamplesPerOutput;
        private bool _havePrevious;
        private float _previous;
        private long _inputIndex;
        private double _nextOutputPosition;

        public LinearResampler(int sourceRate, int targetRate)
        {
            _sourceSamplesPerOutput = (double)sourceRate / targetRate;
        }

        public int Push(float current, Span<float> destination)
        {
            if (!_havePrevious)
            {
                _havePrevious = true;
                _previous = current;
                _inputIndex = 0;

                if (destination.Length == 0)
                    return 0;

                destination[0] = current;
                _nextOutputPosition = _sourceSamplesPerOutput;
                return 1;
            }

            _inputIndex++;
            int count = 0;

            while (_nextOutputPosition <= _inputIndex && count < destination.Length)
            {
                double leftIndex = _inputIndex - 1;
                double fraction = _nextOutputPosition - leftIndex;
                fraction = Math.Clamp(fraction, 0.0, 1.0);

                destination[count] =
                    _previous + (current - _previous) * (float)fraction;

                count++;
                _nextOutputPosition += _sourceSamplesPerOutput;
            }

            _previous = current;
            return count;
        }
    }
}
