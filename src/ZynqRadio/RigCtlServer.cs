using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ZynqRadio.Radio;
using ZynqRadio.Diagnostics;

namespace ZynqRadio.Cat;

public sealed class RigCtlServer
{
    private readonly IPAddress _address;
    private readonly int _port;
    private readonly IRadio _radio;
    private readonly bool _verboseLogging;
    private readonly RuntimeMetrics? _metrics;

    private string _vfo = "VFOA";
    private bool _split;
    private string _splitMode = "USB";
    private int _splitPassband = 3000;
    private int _powerState = 1;

    public RigCtlServer(
        string host,
        int port,
        IRadio radio,
        bool verboseLogging = true,
        RuntimeMetrics? metrics = null)
    {
        _address = IPAddress.Parse(host);
        _port = port;
        _radio = radio;
        _verboseLogging = verboseLogging;
        _metrics = metrics;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(_address, _port);
        listener.Start();

        Console.WriteLine($"CAT listening on {_address}:{_port}");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var ep = client.Client.RemoteEndPoint;
        _metrics?.CatClientConnected();
        Console.WriteLine($"CAT client connected: {ep}");

        try
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();

                using (stream)
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true))
                using (var writer = new StreamWriter(stream, Encoding.ASCII, 4096, true))
                {
                    writer.AutoFlush = true;
                    writer.NewLine = "\n";

                    while (!ct.IsCancellationRequested)
                    {
                        string? line = await reader.ReadLineAsync(ct);

                        if (line is null)
                            break;

                        line = line.Trim();

                        if (line.Length == 0)
                            continue;

                        bool importantCommand =
                            line.StartsWith("F ", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("T ", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("M ", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("I ", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("S ", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("\\set_", StringComparison.OrdinalIgnoreCase);

                        if (_verboseLogging || importantCommand)
                            Console.WriteLine($"CAT <= {line}");

                        string response;

                        try
                        {
                            response = Process(line);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"CAT command failed: {ex.Message}");
                            response = "RPRT -1\n";
                        }

                        if (response.Length > 0)
                        {
                            if (_verboseLogging || importantCommand)
                                Console.WriteLine($"CAT => {response.Replace("\n", "\\n")}");

                            await writer.WriteAsync(response.AsMemory(), ct);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException ex)
        {
            Console.WriteLine($"CAT I/O error: {ex.Message}");
        }
        finally
        {
            _metrics?.CatClientDisconnected();
            Console.WriteLine($"CAT client disconnected: {ep}");
        }
    }

    private string Process(string command)
    {
        if (command.Equals("\\get_powerstat", StringComparison.OrdinalIgnoreCase))
            return $"{_powerState}\n";

        if (command.StartsWith("\\set_powerstat ", StringComparison.OrdinalIgnoreCase))
        {
            string value = command["\\set_powerstat ".Length..].Trim();
            _powerState = value == "0" ? 0 : 1;
            return "RPRT 0\n";
        }

        if (command.Equals("\\chk_vfo", StringComparison.OrdinalIgnoreCase))
            return "0\n";

        if (command.Equals("\\dump_state", StringComparison.OrdinalIgnoreCase))
            return DumpStateProtocol0();

        if (command.Equals("\\get_info", StringComparison.OrdinalIgnoreCase) || command == "_")
            return "ZynqRadio direct-libiio\n";

        command = NormalizeLongCommand(command);

        string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
            return "RPRT -1\n";

        switch (parts[0])
        {
            case "F":
            {
                if (parts.Length < 2)
                    return "RPRT -1\n";

                long frequency = ParseFrequencyHz(parts[^1]);
                _radio.SetFrequency(frequency);

                if (!_split)
                    _radio.SetTxFrequency(frequency);

                return "RPRT 0\n";
            }

            case "f":
                return $"{_radio.RxFrequencyHz}\n";

            case "M":
            {
                if (parts.Length < 2)
                    return "RPRT -1\n";

                string mode = parts[1];
                int passband = _radio.PassbandHz;

                if (parts.Length >= 3 &&
                    int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPassband))
                {
                    passband = parsedPassband;
                }

                _radio.SetMode(mode, passband);
                return "RPRT 0\n";
            }

            case "m":
                return $"{_radio.Mode}\n{_radio.PassbandHz}\n";

            case "T":
            {
                if (parts.Length < 2)
                    return "RPRT -1\n";

                _radio.SetPtt(parts[^1] != "0");
                return "RPRT 0\n";
            }

            case "t":
                return _radio.Ptt ? "1\n" : "0\n";

            case "V":
            {
                if (parts.Length < 2)
                    return "RPRT -1\n";

                _vfo = parts[^1];
                return "RPRT 0\n";
            }

            case "v":
                return $"{_vfo}\n";

            case "I":
            {
                if (parts.Length < 2)
                    return "RPRT -1\n";

                long txFrequency = ParseFrequencyHz(parts[^1]);
                _radio.SetTxFrequency(txFrequency);
                return "RPRT 0\n";
            }

            case "i":
                return $"{_radio.TxFrequencyHz}\n";

            case "X":
            {
                if (parts.Length < 2)
                    return "RPRT -1\n";

                _splitMode = parts[1];

                if (parts.Length >= 3 &&
                    int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width))
                {
                    _splitPassband = width;
                }

                return "RPRT 0\n";
            }

            case "x":
                return $"{_splitMode}\n{_splitPassband}\n";

            case "S":
            {
                if (parts.Length < 2)
                    return "RPRT -1\n";

                _split = parts[1] != "0";

                if (parts.Length >= 3)
                    _vfo = parts[2];

                if (!_split)
                    _radio.SetTxFrequency(_radio.RxFrequencyHz);

                return "RPRT 0\n";
            }

            case "s":
                return $"{(_split ? 1 : 0)}\n{_vfo}\n";

            case "q":
            case "Q":
                return "";

            default:
                return "RPRT -4\n";
        }
    }

    private static long ParseFrequencyHz(string text)
    {
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value))
            throw new FormatException($"Invalid frequency '{text}'");

        if (value < 0 || value > long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(text));

        return decimal.ToInt64(decimal.Round(value, 0, MidpointRounding.AwayFromZero));
    }

    private static string NormalizeLongCommand(string command)
    {
        if (command.StartsWith("\\set_freq ", StringComparison.OrdinalIgnoreCase))
            return "F " + command["\\set_freq ".Length..];
        if (command.Equals("\\get_freq", StringComparison.OrdinalIgnoreCase))
            return "f";
        if (command.StartsWith("\\set_mode ", StringComparison.OrdinalIgnoreCase))
            return "M " + command["\\set_mode ".Length..];
        if (command.Equals("\\get_mode", StringComparison.OrdinalIgnoreCase))
            return "m";
        if (command.StartsWith("\\set_ptt ", StringComparison.OrdinalIgnoreCase))
            return "T " + command["\\set_ptt ".Length..];
        if (command.Equals("\\get_ptt", StringComparison.OrdinalIgnoreCase))
            return "t";
        if (command.StartsWith("\\set_vfo ", StringComparison.OrdinalIgnoreCase))
            return "V " + command["\\set_vfo ".Length..];
        if (command.Equals("\\get_vfo", StringComparison.OrdinalIgnoreCase))
            return "v";
        if (command.StartsWith("\\set_split_freq ", StringComparison.OrdinalIgnoreCase))
            return "I " + command["\\set_split_freq ".Length..];
        if (command.Equals("\\get_split_freq", StringComparison.OrdinalIgnoreCase))
            return "i";
        if (command.StartsWith("\\set_split_mode ", StringComparison.OrdinalIgnoreCase))
            return "X " + command["\\set_split_mode ".Length..];
        if (command.Equals("\\get_split_mode", StringComparison.OrdinalIgnoreCase))
            return "x";
        if (command.StartsWith("\\set_split_vfo ", StringComparison.OrdinalIgnoreCase))
            return "S " + command["\\set_split_vfo ".Length..];
        if (command.Equals("\\get_split_vfo", StringComparison.OrdinalIgnoreCase))
            return "s";

        return command;
    }

    private static string DumpStateProtocol0()
    {
        var sb = new StringBuilder();

        sb.AppendLine("0");
        sb.AppendLine("2");
        sb.AppendLine("0");
        sb.AppendLine("0 0 0 0 0 0 0");
        sb.AppendLine("0 0 0 0 0 0 0");
        sb.AppendLine("0 0");
        sb.AppendLine("0 0");
        sb.AppendLine("0");
        sb.AppendLine("0");
        sb.AppendLine("0");
        sb.AppendLine("0");
        sb.AppendLine("0");
        sb.AppendLine("0");
        sb.AppendLine("0x0");
        sb.AppendLine("0x0");
        sb.AppendLine("0x0");
        sb.AppendLine("0x0");
        sb.AppendLine("0x0");
        sb.AppendLine("0x0");

        return sb.ToString();
    }
}
