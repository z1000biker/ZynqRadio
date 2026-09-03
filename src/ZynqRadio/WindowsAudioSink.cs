using System.Buffers;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ZynqRadio.Audio;

public sealed class WindowsAudioSink : IDisposable
{
    public const int SampleRate = 48_000;

    private readonly MMDevice _device;
    private readonly WasapiOut _output;
    private readonly BufferedWaveProvider _provider;

    private WindowsAudioSink(MMDevice device)
    {
        _device = device;

        var format = new WaveFormat(SampleRate, 16, 1);

        _provider = new BufferedWaveProvider(format)
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true
        };

        _output = new WasapiOut(
            _device,
            AudioClientShareMode.Shared,
            useEventSync: true,
            latency: 100);

        _output.Init(_provider);
        _output.Play();
    }

    public static void PrintDevices()
    {
        List<AudioDeviceDescriptor> devices = GetRenderDevices();

        Console.WriteLine("Active Windows WASAPI playback endpoints:");

        if (devices.Count == 0)
        {
            Console.WriteLine("  No active render endpoints found.");
            return;
        }

        foreach (AudioDeviceDescriptor device in devices)
            Console.WriteLine($"  {device.Index}: {device.Name}");

        Console.WriteLine();
        Console.WriteLine("For VB-CABLE choose the playback endpoint named CABLE Input.");
    }

    public static WindowsAudioSink Open(Radio.RadioConfig cfg)
    {
        List<AudioDeviceDescriptor> devices = GetRenderDevices();

        if (devices.Count == 0)
            throw new InvalidOperationException("No active Windows playback endpoints were found.");

        AudioDeviceDescriptor? selected = null;

        if (cfg.AudioOutputDeviceNumber is int requestedIndex)
        {
            selected = devices.FirstOrDefault(d => d.Index == requestedIndex);

            if (selected is null)
                throw new ArgumentOutOfRangeException(
                    "--audio-device",
                    $"WASAPI playback endpoint {requestedIndex} does not exist.");
        }
        else if (!string.IsNullOrWhiteSpace(cfg.AudioOutputName))
        {
            string wanted = cfg.AudioOutputName;

            selected = devices.FirstOrDefault(
                d => d.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase));

            if (selected is null)
                throw new InvalidOperationException(
                    $"No active playback endpoint contains '{wanted}'. " +
                    "Run with --list-audio to see the exact names.");
        }
        else
        {
            selected = devices.FirstOrDefault(
                d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));

            if (selected is null)
                throw new InvalidOperationException(
                    "No playback endpoint containing 'CABLE Input' was found. " +
                    "Run with --list-audio and select one with " +
                    "--audio-device N or --audio-out \"name\".");
        }

        using var enumerator = new MMDeviceEnumerator();
        MMDevice liveDevice = enumerator.GetDevice(selected.Id);

        Console.WriteLine($"RX audio output: {selected.Index}: {selected.Name}");
        Console.WriteLine("Audio source format: 48000 Hz, mono, 16-bit PCM");
        Console.WriteLine("Audio backend: WASAPI shared mode");

        return new WindowsAudioSink(liveDevice);
    }

    public void Write(
        float[] samples,
        int count)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (count <= 0)
            return;

        if (count > samples.Length)
            throw new ArgumentOutOfRangeException(nameof(count));

        int byteCount = checked(count * sizeof(short));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            for (int i = 0; i < count; i++)
            {
                float x = Math.Clamp(samples[i], -1.0f, 1.0f);
                short pcm = (short)Math.Round(x * short.MaxValue);
                int offset = i * 2;
                buffer[offset] = (byte)(pcm & 0xff);
                buffer[offset + 1] = (byte)((pcm >> 8) & 0xff);
            }

            _provider.AddSamples(buffer, 0, byteCount);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Dispose()
    {
        try
        {
            _output.Stop();
        }
        catch
        {
        }

        _output.Dispose();
        _device.Dispose();
    }

    public static List<AudioDeviceDescriptor> GetRenderDevices()
    {
        var result = new List<AudioDeviceDescriptor>();
        using var enumerator = new MMDeviceEnumerator();

        MMDeviceCollection collection = enumerator.EnumerateAudioEndPoints(
            DataFlow.Render,
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
