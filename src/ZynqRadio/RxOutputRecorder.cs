using NAudio.Wave;

namespace ZynqRadio.Diagnostics;

public sealed class RxOutputRecorder : IDisposable
{
    private WaveFileWriter? _writer;
    private string? _path;

    public bool IsRecording => _writer is not null;

    public void Start(long dialFrequencyHz)
    {
        Stop();

        Directory.CreateDirectory(AppLog.LogDirectory);

        _path = Path.Combine(
            AppLog.LogDirectory,
            $"rx_output_{dialFrequencyHz}_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

        _writer = new WaveFileWriter(
            _path,
            new WaveFormat(48_000, 16, 1));

        AppLog.Info(
            "RX output diagnostic recording started: " + _path);
    }

    public void Write(
        float[] samples,
        int count)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (_writer is null ||
            count <= 0)
        {
            return;
        }

        if (count > samples.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count));
        }

        byte[] bytes =
            new byte[count * 2];

        for (int i = 0; i < count; i++)
        {
            float x =
                Math.Clamp(
                    samples[i],
                    -1f,
                    1f);

            short pcm =
                (short)Math.Round(
                    x * 32767f);

            int o =
                i * 2;

            bytes[o] =
                (byte)(pcm & 0xff);

            bytes[o + 1] =
                (byte)((pcm >> 8) & 0xff);
        }

        _writer.Write(
            bytes,
            0,
            bytes.Length);
    }

    public void Stop()
    {
        if (_writer is null)
            return;

        _writer.Dispose();
        _writer = null;

        AppLog.Info(
            "RX output diagnostic recording completed: " + _path);

        _path = null;
    }

    public void Dispose() => Stop();
}
