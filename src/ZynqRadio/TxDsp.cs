namespace ZynqRadio.Dsp;

public sealed class TxDsp
{
    private const int AudioRate = 48_000;

    private readonly int _rfRate;
    private readonly double _sourceStep;
    private readonly double _txLevel;
    private readonly int _qSign;

    private readonly HilbertAnalytic _hilbert = new(129);

    private ComplexSample _a;
    private ComplexSample _b;
    private bool _havePair;
    private double _fraction;

    public TxDsp(long rfRate, double txLevel, int qSign)
    {
        if (rfRate <= 0 || rfRate > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(rfRate));

        _rfRate = (int)rfRate;
        _sourceStep = (double)AudioRate / _rfRate;
        _txLevel = Math.Clamp(txLevel, 0.0, 1.0);
        _qSign = qSign < 0 ? -1 : 1;
    }

    public void Reset()
    {
        _hilbert.Reset();
        _havePair = false;
        _fraction = 0.0;
        _a = default;
        _b = default;
    }

    public int Generate(
        Span<short> interleavedIq,
        Func<float> nextAudioSample)
    {
        int complexCount = interleavedIq.Length / 2;

        if (!_havePair)
        {
            _a = NextAnalytic(nextAudioSample);
            _b = NextAnalytic(nextAudioSample);
            _havePair = true;
        }

        for (int n = 0; n < complexCount; n++)
        {
            double i = _a.I + (_b.I - _a.I) * _fraction;
            double q = _a.Q + (_b.Q - _a.Q) * _fraction;

            i *= _txLevel;
            q *= _txLevel * _qSign;

            int iv = (int)Math.Round(Math.Clamp(i, -1.0, 1.0) * 2047.0);
            int qv = (int)Math.Round(Math.Clamp(q, -1.0, 1.0) * 2047.0);

            interleavedIq[n * 2] = (short)(iv << 4);
            interleavedIq[n * 2 + 1] = (short)(qv << 4);

            _fraction += _sourceStep;

            while (_fraction >= 1.0)
            {
                _fraction -= 1.0;
                _a = _b;
                _b = NextAnalytic(nextAudioSample);
            }
        }

        return complexCount;
    }

    private ComplexSample NextAnalytic(Func<float> nextAudioSample)
    {
        float x = nextAudioSample();
        return _hilbert.Process(x);
    }

    private readonly record struct ComplexSample(double I, double Q);

    private sealed class HilbertAnalytic
    {
        private readonly double[] _h;
        private readonly double[] _history;
        private int _index;
        private readonly int _delay;

        public HilbertAnalytic(int taps)
        {
            if ((taps & 1) == 0)
                throw new ArgumentException("Hilbert tap count must be odd.");

            _h = Design(taps);
            _history = new double[taps];
            _delay = (taps - 1) / 2;
        }

        public ComplexSample Process(float sample)
        {
            _history[_index] = sample;

            double q = 0.0;
            int hidx = _index;

            for (int k = 0; k < _h.Length; k++)
            {
                q += _h[k] * _history[hidx];

                hidx--;
                if (hidx < 0)
                    hidx = _history.Length - 1;
            }

            int delayedIndex = _index - _delay;
            while (delayedIndex < 0)
                delayedIndex += _history.Length;

            double i = _history[delayedIndex];

            _index++;
            if (_index == _history.Length)
                _index = 0;

            return new ComplexSample(i, q);
        }

        public void Reset()
        {
            Array.Clear(_history);
            _index = 0;
        }

        private static double[] Design(int taps)
        {
            var h = new double[taps];
            int mid = (taps - 1) / 2;

            for (int n = 0; n < taps; n++)
            {
                int k = n - mid;

                double ideal;

                if (k == 0 || (k & 1) == 0)
                {
                    ideal = 0.0;
                }
                else
                {
                    ideal = 2.0 / (Math.PI * k);
                }

                double window =
                    0.54 -
                    0.46 *
                    Math.Cos(
                        2.0 * Math.PI * n / (taps - 1));

                h[n] = ideal * window;
            }

            return h;
        }
    }
}
