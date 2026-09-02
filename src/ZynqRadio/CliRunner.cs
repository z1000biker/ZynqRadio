using ZynqRadio.Audio;
using ZynqRadio.Cat;
using ZynqRadio.Radio;

namespace ZynqRadio;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        Console.WriteLine("ZynqRadio .NET 8");
        Console.WriteLine("Direct libiio RX/TX. No SoapySDR.");
        Console.WriteLine();

        RadioConfig cfg = RadioConfig.Load(args);

        if (cfg.ListAudioDevices)
        {
            WindowsAudioSink.PrintDevices();
            return 0;
        }

        if (cfg.ListCaptureDevices)
        {
            TxAudioCapture.PrintDevices();
            return 0;
        }

        if ((cfg.EnableRxAudio || cfg.EnableTxAudio) && cfg.NoRadio)
        {
            Console.Error.WriteLine(
                "--rx-audio/--tx-audio cannot be used with --no-radio.");

            return 2;
        }

        using var radio = new IioRadio(cfg);

        if (!cfg.NoRadio)
        {
            try
            {
                radio.Connect();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    "IIO connection failed: " +
                    ex.Message);

                return 2;
            }
        }
        else
        {
            Console.WriteLine(
                "Radio hardware disabled by --no-radio.");
        }

        var cat = new RigCtlServer(
            cfg.CatHost,
            cfg.CatPort,
            radio,
            verboseLogging: true);

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress +=
            (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

        RxEngine? rx = null;
        TxEngine? tx = null;
        var tasks = new List<Task>();

        try
        {
            if (cfg.EnableRxAudio)
            {
                rx = new RxEngine(radio, cfg);
                tasks.Add(rx.RunAsync(cts.Token));
            }

            if (cfg.EnableTxAudio)
            {
                tx = new TxEngine(radio, cfg);
                tasks.Add(tx.RunAsync(cts.Token));
            }

            tasks.Add(cat.RunAsync(cts.Token));

            Task first = await Task.WhenAny(tasks);

            if (first.IsFaulted)
            {
                Exception error =
                    first.Exception?.GetBaseException() ??
                    new Exception("Background task failed.");

                Console.Error.WriteLine(
                    "Runtime failure: " +
                    error.Message);

                cts.Cancel();
            }

            if (first.IsCompleted &&
                !cts.IsCancellationRequested)
            {
                cts.Cancel();
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    "Runtime failure: " +
                    ex.GetBaseException().Message);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Cancel();

            tx?.Dispose();
            rx?.Dispose();

            radio.SetPtt(false);
        }

        return 0;
    }
}
