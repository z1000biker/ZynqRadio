using System.Reflection;
using System.Runtime.InteropServices;

namespace ZynqRadio.Radio;

internal static class NativeIio
{
    private const string Lib = "iio";

    private static readonly object LoaderLock = new();
    private static IntPtr _loadedHandle = IntPtr.Zero;

    static NativeIio()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(NativeIio).Assembly,
            Resolve);
    }

    private static IntPtr Resolve(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? path)
    {
        if (!string.Equals(
                libraryName,
                Lib,
                StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        lock (LoaderLock)
        {
            if (_loadedHandle != IntPtr.Zero)
                return _loadedHandle;

            var candidates = new List<string>();

            string? explicitPath =
                Environment.GetEnvironmentVariable("IIO_DLL");

            if (!string.IsNullOrWhiteSpace(explicitPath))
                candidates.Add(explicitPath);

            candidates.Add(Path.Combine(
                AppContext.BaseDirectory,
                "iio.dll"));

            candidates.Add(Path.Combine(
                AppContext.BaseDirectory,
                "libiio.dll"));

            candidates.Add("iio.dll");
            candidates.Add("libiio.dll");

            candidates.Add(@"C:\PothosSDR\bin\iio.dll");
            candidates.Add(@"C:\PothosSDR\bin\libiio.dll");
            candidates.Add(@"C:\Program Files\libiio\bin\iio.dll");
            candidates.Add(@"C:\Program Files\libiio\bin\libiio.dll");

            foreach (string candidate in candidates)
            {
                try
                {
                    if (NativeLibrary.TryLoad(
                            candidate,
                            out IntPtr handle))
                    {
                        _loadedHandle = handle;
                        Console.WriteLine(
                            $"Loaded libiio: {candidate}");
                        return _loadedHandle;
                    }
                }
                catch
                {
                }
            }

            throw new DllNotFoundException(
                "Could not load 64-bit iio.dll/libiio.dll. " +
                "Put it beside the EXE or set IIO_DLL.");
        }
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr iio_create_context_from_uri(
        [MarshalAs(UnmanagedType.LPStr)] string uri);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void iio_context_destroy(
        IntPtr ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr iio_context_find_device(
        IntPtr ctx,
        [MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr iio_device_find_channel(
        IntPtr dev,
        [MarshalAs(UnmanagedType.LPStr)] string name,
        [MarshalAs(UnmanagedType.I1)] bool output);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int iio_channel_attr_write_longlong(
        IntPtr chn,
        [MarshalAs(UnmanagedType.LPStr)] string attr,
        long value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int iio_channel_attr_write_double(
        IntPtr chn,
        [MarshalAs(UnmanagedType.LPStr)] string attr,
        double value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr iio_channel_attr_write(
        IntPtr chn,
        [MarshalAs(UnmanagedType.LPStr)] string attr,
        [MarshalAs(UnmanagedType.LPStr)] string value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void iio_channel_enable(
        IntPtr chn);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void iio_channel_disable(
        IntPtr chn);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr iio_device_create_buffer(
        IntPtr dev,
        nuint samplesCount,
        [MarshalAs(UnmanagedType.I1)] bool cyclic);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void iio_buffer_destroy(
        IntPtr buffer);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr iio_buffer_refill(
        IntPtr buffer);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr iio_buffer_push(
        IntPtr buffer);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr iio_buffer_start(
        IntPtr buffer);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr iio_buffer_end(
        IntPtr buffer);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr iio_buffer_first(
        IntPtr buffer,
        IntPtr channel);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint iio_buffer_step(
        IntPtr buffer);

    internal static void Check(
        int result,
        string operation)
    {
        if (result < 0)
        {
            throw new IOException(
                $"{operation} failed with libiio error {result}");
        }
    }

    internal static long Check(
        IntPtr result,
        string operation)
    {
        long value = result.ToInt64();

        if (value < 0)
        {
            throw new IOException(
                $"{operation} failed with libiio error {value}");
        }

        return value;
    }
}
