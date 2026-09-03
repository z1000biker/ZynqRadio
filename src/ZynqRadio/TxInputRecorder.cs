using NAudio.Wave;

namespace ZynqRadio.Diagnostics;

public sealed class TxInputRecorder : IDisposable
{
    private WaveFileWriter? _writer;
    private string? _path;

    public void Start(long dialFrequencyHz)
    {
        Stop();

        Directory.CreateDirectory(AppLog.LogDirectory);

        _path = Path.Combine(
            AppLog.LogDirectory,
            $"tx_input_{dialFrequencyHz}_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

        _writer = new WaveFileWriter(
            _path,
            new WaveFormat(48_000, 16, 1));

        AppLog.Info(
            "TX input diagnostic recording started: " + _path);
    }

    public void Write(float sample)
    {
        if (_writer is null)
            return;

        float x = Math.Clamp(sample, -1f, 1f);
        short pcm = (short)Math.Round(x * 32767f);

        byte[] bytes =
        {
            (byte)(pcm & 0xff),
            (byte)((pcm >> 8) & 0xff)
        };

        _writer.Write(bytes, 0, 2);
    }

    public void Stop()
    {
        if (_writer is null)
            return;

        _writer.Dispose();
        _writer = null;

        AppLog.Info(
            "TX input diagnostic recording completed: " + _path);

        _path = null;
    }

    public void Dispose() => Stop();
}
