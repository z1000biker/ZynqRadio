namespace ZynqRadio.Diagnostics;

public sealed class RuntimeMetrics
{
    private readonly object _gate = new();

    private double _rxRateMsps;
    private double _iqRmsDbfs = -120;
    private double _iqPeakDbfs = -120;
    private double _rxAudioRmsDbfs = -120;
    private double _rxAudioPeakDbfs = -120;
    private double _txAudioRmsDbfs = -120;
    private double _txAudioPeakDbfs = -120;
    private long _txUnderruns;
    private int _catClients;

    public void UpdateRx(
        double rateMsps,
        double iqRms,
        double iqPeak,
        double audioRms,
        double audioPeak)
    {
        lock (_gate)
        {
            _rxRateMsps = rateMsps;
            _iqRmsDbfs = iqRms;
            _iqPeakDbfs = iqPeak;
            _rxAudioRmsDbfs = audioRms;
            _rxAudioPeakDbfs = audioPeak;
        }
    }

    public void UpdateTx(
        double audioRms,
        double audioPeak,
        long underruns)
    {
        lock (_gate)
        {
            _txAudioRmsDbfs = audioRms;
            _txAudioPeakDbfs = audioPeak;
            _txUnderruns = underruns;
        }
    }

    public void CatClientConnected() =>
        Interlocked.Increment(ref _catClients);

    public void CatClientDisconnected()
    {
        int value = Interlocked.Decrement(ref _catClients);
        if (value < 0)
            Interlocked.Exchange(ref _catClients, 0);
    }

    public RuntimeSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new RuntimeSnapshot(
                _rxRateMsps,
                _iqRmsDbfs,
                _iqPeakDbfs,
                _rxAudioRmsDbfs,
                _rxAudioPeakDbfs,
                _txAudioRmsDbfs,
                _txAudioPeakDbfs,
                _txUnderruns,
                Volatile.Read(ref _catClients));
        }
    }
}

public sealed record RuntimeSnapshot(
    double RxRateMsps,
    double IqRmsDbfs,
    double IqPeakDbfs,
    double RxAudioRmsDbfs,
    double RxAudioPeakDbfs,
    double TxAudioRmsDbfs,
    double TxAudioPeakDbfs,
    long TxUnderruns,
    int CatClients);
