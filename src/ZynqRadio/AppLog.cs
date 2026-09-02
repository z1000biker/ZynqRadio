using System.Text;

namespace ZynqRadio.Diagnostics;

public static class AppLog
{
    private static readonly object Gate = new();
    private static StreamWriter? _file;
    private static TextWriter? _originalOut;
    private static TextWriter? _originalError;

    public static event Action<string>? LineWritten;

    public static string LogDirectory { get; private set; } = "";
    public static string CurrentLogPath { get; private set; } = "";

    public static void Initialize()
    {
        if (_file is not null)
            return;

        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZynqRadio");

        LogDirectory = Path.Combine(root, "logs");
        Directory.CreateDirectory(LogDirectory);

        CurrentLogPath = Path.Combine(
            LogDirectory,
            $"zynqradio_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        _file = new StreamWriter(
            new FileStream(
                CurrentLogPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite),
            new UTF8Encoding(false))
        {
            AutoFlush = true
        };

        _originalOut = Console.Out;
        _originalError = Console.Error;

        Console.SetOut(new LogTextWriter("INFO", _originalOut));
        Console.SetError(new LogTextWriter("ERROR", _originalError));

        WriteInternal("INFO", "Logging initialized.");
        WriteInternal("INFO", "Log file: " + CurrentLogPath);
    }

    internal static void WriteInternal(string level, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        string line =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";

        lock (Gate)
        {
            _file?.WriteLine(line);
        }

        try
        {
            LineWritten?.Invoke(line);
        }
        catch
        {
        }
    }

    public static void Info(string message) =>
        WriteInternal("INFO", message);

    public static void Debug(string message) =>
        WriteInternal("DEBUG", message);

    public static void Error(string message) =>
        WriteInternal("ERROR", message);

    public static void Shutdown()
    {
        lock (Gate)
        {
            _file?.Flush();
            _file?.Dispose();
            _file = null;
        }
    }

    private sealed class LogTextWriter : TextWriter
    {
        private readonly string _level;
        private readonly TextWriter? _mirror;
        private readonly StringBuilder _buffer = new();
        private readonly object _gate = new();

        public LogTextWriter(string level, TextWriter? mirror)
        {
            _level = level;
            _mirror = mirror;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_gate)
            {
                if (value == '\r')
                    return;

                if (value == '\n')
                {
                    FlushLine();
                    _mirror?.WriteLine();
                    return;
                }

                _buffer.Append(value);
                _mirror?.Write(value);
            }
        }

        public override void Write(string? value)
        {
            if (value is null)
                return;

            foreach (char ch in value)
                Write(ch);
        }

        public override void WriteLine(string? value)
        {
            lock (_gate)
            {
                if (!string.IsNullOrEmpty(value))
                    _buffer.Append(value);

                FlushLine();
                _mirror?.WriteLine(value);
            }
        }

        private void FlushLine()
        {
            string text = _buffer.ToString();
            _buffer.Clear();

            if (text.Length > 0)
                AppLog.WriteInternal(_level, text);
        }
    }
}
