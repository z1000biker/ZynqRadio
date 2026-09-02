using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ZynqRadio.Audio;
using ZynqRadio.Diagnostics;

namespace ZynqRadio.Gui;

public static class DiagnosticBundle
{
    public static void Create(
        string destinationZip,
        AppSettings settings)
    {
        string temp =
            Path.Combine(
                Path.GetTempPath(),
                "ZynqRadioDiag_" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temp);

        try
        {
            File.WriteAllText(
                Path.Combine(temp, "settings.json"),
                JsonSerializer.Serialize(
                    settings,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));

            var system = new StringBuilder();

            system.AppendLine(
                "ZynqRadio diagnostic bundle");

            system.AppendLine(
                "Created: " +
                DateTime.Now.ToString("O"));

            system.AppendLine(
                "OS: " +
                Environment.OSVersion);

            system.AppendLine(
                ".NET: " +
                Environment.Version);

            system.AppendLine(
                "64-bit process: " +
                Environment.Is64BitProcess);

            system.AppendLine(
                "Machine: " +
                Environment.MachineName);

            system.AppendLine();
            system.AppendLine(
                "WASAPI playback endpoints:");

            foreach (AudioDeviceDescriptor d in
                     WindowsAudioSink.GetRenderDevices())
            {
                system.AppendLine(
                    $"  {d.Index}: {d.Name}");
            }

            system.AppendLine();
            system.AppendLine(
                "WASAPI capture endpoints:");

            foreach (AudioDeviceDescriptor d in
                     TxAudioCapture.GetCaptureDevices())
            {
                system.AppendLine(
                    $"  {d.Index}: {d.Name}");
            }

            File.WriteAllText(
                Path.Combine(temp, "system.txt"),
                system.ToString());

            if (File.Exists(
                    AppLog.CurrentLogPath))
            {
                File.Copy(
                    AppLog.CurrentLogPath,
                    Path.Combine(
                        temp,
                        Path.GetFileName(
                            AppLog.CurrentLogPath)),
                    true);
            }

            if (File.Exists(destinationZip))
                File.Delete(destinationZip);

            ZipFile.CreateFromDirectory(
                temp,
                destinationZip,
                CompressionLevel.Optimal,
                false);
        }
        finally
        {
            try
            {
                Directory.Delete(
                    temp,
                    true);
            }
            catch
            {
            }
        }
    }
}
