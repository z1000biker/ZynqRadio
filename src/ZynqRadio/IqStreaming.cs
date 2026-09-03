namespace ZynqRadio.Radio;

public sealed class IqStreaming : IDisposable
{
    private readonly string _role;
    private IntPtr _ctx;
    private bool _ownsContext;

    private IntPtr _rxDev;
    private IntPtr _rxI;
    private IntPtr _rxQ;
    private IntPtr _rxBuffer;
    private bool _rxIOnly;

    private IntPtr _txDev;
    private IntPtr _txI;
    private IntPtr _txQ;
    private IntPtr _txBuffer;

    public IqStreaming(string uri, string role)
    {
        _role = role;

        _ctx = NativeIio.iio_create_context_from_uri(uri);

        if (_ctx == IntPtr.Zero)
        {
            throw new IOException(
                $"Could not create independent IIO {_role} context for {uri}");
        }

        _ownsContext = true;

        Console.WriteLine(
            $"Independent libiio {_role} context connected.");
    }

    public void OpenRx(
        nuint samples = 32_768,
        bool iOnly = false)
    {
        _rxDev = NativeIio.iio_context_find_device(_ctx, "cf-ad9361-lpc");

        if (_rxDev == IntPtr.Zero)
            throw new IOException("cf-ad9361-lpc not found");

        _rxI = NativeIio.iio_device_find_channel(_rxDev, "voltage0", false);
        _rxQ = NativeIio.iio_device_find_channel(_rxDev, "voltage1", false);

        if (_rxI == IntPtr.Zero)
            throw new IOException("RX I channel not found");

        if (!iOnly && _rxQ == IntPtr.Zero)
            throw new IOException("RX Q channel not found");

        _rxIOnly = iOnly;
        NativeIio.iio_channel_enable(_rxI);

        if (!_rxIOnly)
            NativeIio.iio_channel_enable(_rxQ);

        _rxBuffer = NativeIio.iio_device_create_buffer(_rxDev, samples, false);

        if (_rxBuffer == IntPtr.Zero)
            throw new IOException("RX buffer create failed");

        nint rxStep = NativeIio.iio_buffer_step(_rxBuffer);

        Console.WriteLine($"IIO RX buffer opened: {samples:N0} complex-time samples");
        Console.WriteLine(
            _rxIOnly
                ? $"IIO RX transport: I-ONLY, step={rxStep} bytes/sample (50% RX bandwidth)"
                : $"IIO RX transport: FULL I/Q, step={rxStep} bytes/sample");
    }

    public int ReadRx(Span<short> interleavedIq)
    {
        if (_rxBuffer == IntPtr.Zero)
            throw new InvalidOperationException("RX buffer is not open.");

        NativeIio.Check(NativeIio.iio_buffer_refill(_rxBuffer), "RX refill");

        IntPtr first = NativeIio.iio_buffer_first(_rxBuffer, _rxI);
        IntPtr end = NativeIio.iio_buffer_end(_rxBuffer);
        nint step = NativeIio.iio_buffer_step(_rxBuffer);

        if (first == IntPtr.Zero || end == IntPtr.Zero || step <= 0)
            throw new IOException("Invalid IIO RX buffer pointers.");

        int maxComplex = interleavedIq.Length / 2;
        int complexCount = 0;

        unsafe
        {
            byte* p = (byte*)first.ToPointer();
            byte* pEnd = (byte*)end.ToPointer();

            while (p < pEnd && complexCount < maxComplex)
            {
                short* sample = (short*)p;
                interleavedIq[complexCount * 2] = sample[0];

                if (_rxIOnly)
                {
                    interleavedIq[complexCount * 2 + 1] = 0;
                }
                else
                {
                    interleavedIq[complexCount * 2 + 1] = sample[1];
                }

                complexCount++;
                p += (int)step;
            }
        }

        return complexCount * 2;
    }

    public void OpenTx(nuint samples = 32_768)
    {
        _txDev = NativeIio.iio_context_find_device(_ctx, "cf-ad9361-dds-core-lpc");

        if (_txDev == IntPtr.Zero)
            throw new IOException("cf-ad9361-dds-core-lpc not found");

        _txI = NativeIio.iio_device_find_channel(_txDev, "voltage0", true);
        _txQ = NativeIio.iio_device_find_channel(_txDev, "voltage1", true);

        if (_txI == IntPtr.Zero || _txQ == IntPtr.Zero)
            throw new IOException("TX I/Q channels not found");

        NativeIio.iio_channel_enable(_txI);
        NativeIio.iio_channel_enable(_txQ);

        _txBuffer = NativeIio.iio_device_create_buffer(_txDev, samples, false);

        if (_txBuffer == IntPtr.Zero)
            throw new IOException("TX buffer create failed");

        Console.WriteLine($"IIO TX buffer opened: {samples:N0} complex samples");

        nint txStep = NativeIio.iio_buffer_step(_txBuffer);
        Console.WriteLine($"IIO TX layout: step={txStep} bytes/sample");
    }

    public int WriteTx(ReadOnlySpan<short> interleavedIq)
    {
        if (_txBuffer == IntPtr.Zero)
            throw new InvalidOperationException("TX buffer is not open.");

        IntPtr first = NativeIio.iio_buffer_first(_txBuffer, _txI);
        IntPtr end = NativeIio.iio_buffer_end(_txBuffer);
        nint step = NativeIio.iio_buffer_step(_txBuffer);

        if (first == IntPtr.Zero || end == IntPtr.Zero || step <= 0)
            throw new IOException("Invalid IIO TX buffer pointers.");

        int requestedComplex = interleavedIq.Length / 2;
        int writtenComplex = 0;

        unsafe
        {
            byte* p = (byte*)first.ToPointer();
            byte* pEnd = (byte*)end.ToPointer();

            while (p < pEnd && writtenComplex < requestedComplex)
            {
                short* sample = (short*)p;
                sample[0] = interleavedIq[writtenComplex * 2];
                sample[1] = interleavedIq[writtenComplex * 2 + 1];
                writtenComplex++;
                p += (int)step;
            }
        }

        if (writtenComplex != requestedComplex)
        {
            throw new IOException(
                $"TX IIO buffer accepted only {writtenComplex:N0} of {requestedComplex:N0} complex samples.");
        }

        return checked((int)NativeIio.Check(NativeIio.iio_buffer_push(_txBuffer), "TX push"));
    }

    public void Dispose()
    {
        if (_rxBuffer != IntPtr.Zero)
        {
            NativeIio.iio_buffer_destroy(_rxBuffer);
            _rxBuffer = IntPtr.Zero;
        }

        if (_txBuffer != IntPtr.Zero)
        {
            NativeIio.iio_buffer_destroy(_txBuffer);
            _txBuffer = IntPtr.Zero;
        }

        if (_rxI != IntPtr.Zero)
            NativeIio.iio_channel_disable(_rxI);
        if (_rxQ != IntPtr.Zero)
            NativeIio.iio_channel_disable(_rxQ);
        if (_txI != IntPtr.Zero)
            NativeIio.iio_channel_disable(_txI);
        if (_txQ != IntPtr.Zero)
            NativeIio.iio_channel_disable(_txQ);

        if (_ownsContext && _ctx != IntPtr.Zero)
        {
            NativeIio.iio_context_destroy(_ctx);
            _ctx = IntPtr.Zero;
            _ownsContext = false;
            Console.WriteLine($"Independent libiio {_role} context closed.");
        }
    }
}
