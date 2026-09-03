using ZynqRadio.Cat;
using ZynqRadio.Diagnostics;
using ZynqRadio.Radio;

namespace ZynqRadio.Gui;

public sealed class RadioSession : IAsyncDisposable
{
    private CancellationTokenSource? _cts;
    private readonly List<Task> _tasks = new();
    private RxEngine? _rx;
    private TxEngine? _tx;
    private IioRadio? _radio;

    public RuntimeMetrics Metrics { get; } = new();

    public bool IsRunning =>
        _radio is not null &&
        _cts is not null &&
        !_cts.IsCancellationRequested;

    public IioRadio? Radio => _radio;

    public async Task StartAsync(RadioConfig cfg)
    {
        if (IsRunning)
            return;

        AppLog.Info("Starting radio session.");

        _cts = new CancellationTokenSource();
        _radio = new IioRadio(cfg);

        try
        {
            _radio.Connect();

            if (cfg.EnableRxAudio)
            {
                _rx = new RxEngine(
                    _radio,
                    cfg,
                    Metrics);

                _tasks.Add(
                    Task.Run(
                        () => _rx.RunAsync(_cts.Token),
                        _cts.Token));
            }

            if (cfg.EnableTxAudio)
            {
                _tx = new TxEngine(
                    _radio,
                    cfg,
                    Metrics);

                _tasks.Add(
                    Task.Run(
                        () => _tx.RunAsync(_cts.Token),
                        _cts.Token));
            }

            var cat = new RigCtlServer(
                cfg.CatHost,
                cfg.CatPort,
                _radio,
                cfg.VerboseCatLogging,
                Metrics);

            _tasks.Add(
                Task.Run(
                    () => cat.RunAsync(_cts.Token),
                    _cts.Token));

            _ = Task.Run(MonitorTasksAsync);
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    private async Task MonitorTasksAsync()
    {
        try
        {
            Task first =
                await Task.WhenAny(_tasks);

            if (_cts is null ||
                _cts.IsCancellationRequested)
            {
                return;
            }

            if (first.IsFaulted)
            {
                Exception ex =
                    first.Exception?
                        .GetBaseException() ??
                    new Exception(
                        "Background radio task failed.");

                AppLog.Error(
                    "Background task failed: " +
                    ex);
            }
            else
            {
                AppLog.Error(
                    "A background radio task exited unexpectedly.");
            }

            _cts.Cancel();
        }
        catch (Exception ex)
        {
            AppLog.Error(
                "Session monitor failed: " +
                ex);
        }
    }

    public void SetFrequency(long hz)
    {
        if (_radio is null)
            throw new InvalidOperationException(
                "Radio is not running.");

        _radio.SetFrequency(hz);
        _radio.SetTxFrequency(hz);
    }

    public void ApplyGains(
        double rxGain,
        double txGain)
    {
        if (_radio is null)
            throw new InvalidOperationException(
                "Radio is not running.");

        _radio.SetRxGain(rxGain);
        _radio.SetTxGain(txGain);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts =
            _cts;

        if (cts is null)
            return;

        AppLog.Info("Stopping radio session.");

        try
        {
            cts.Cancel();

            if (_tasks.Count > 0)
            {
                Task all =
                    Task.WhenAll(
                        _tasks);

                Task finished =
                    await Task.WhenAny(
                        all,
                        Task.Delay(3000));

                if (finished != all)
                {
                    AppLog.Error(
                        "Timed out waiting for one or more radio tasks to stop.");
                }
            }
        }
        finally
        {
            try
            {
                _radio?.SetPtt(false);
            }
            catch
            {
            }

            _tx?.Dispose();
            _tx = null;

            _rx?.Dispose();
            _rx = null;

            _radio?.Dispose();
            _radio = null;

            _tasks.Clear();

            cts.Dispose();
            _cts = null;

            AppLog.Info("Radio session stopped.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
