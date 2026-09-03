namespace ZynqRadio.Radio;

public interface IRadio : IDisposable
{
    bool IsConnected { get; }
    bool Ptt { get; }
    long RxFrequencyHz { get; }
    long TxFrequencyHz { get; }
    string Mode { get; }
    int PassbandHz { get; }
    void Connect();
    void SetFrequency(long frequencyHz);
    void SetTxFrequency(long frequencyHz);
    void SetMode(string mode, int passbandHz);
    void SetPtt(bool enabled);
}
