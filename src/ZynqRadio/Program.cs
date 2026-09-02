using System.Windows.Forms;
using ZynqRadio.Diagnostics;
using ZynqRadio.Gui;

namespace ZynqRadio;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        AppLog.Initialize();

        try
        {
            // CLI remains available only as a troubleshooting fallback.
            if (args.Length > 0)
            {
                string[] cliArgs =
                    args[0].Equals(
                        "--cli",
                        StringComparison.OrdinalIgnoreCase)
                        ? args[1..]
                        : args;

                return CliRunner
                    .RunAsync(cliArgs)
                    .GetAwaiter()
                    .GetResult();
            }

            ApplicationConfiguration.Initialize();

            using var form =
                new MainForm();

            Application.Run(form);

            return 0;
        }
        catch (Exception ex)
        {
            AppLog.Error(
                "Fatal application error: " +
                ex);

            try
            {
                MessageBox.Show(
                    ex.ToString(),
                    "ZynqRadio fatal error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
            }

            return 1;
        }
        finally
        {
            AppLog.Shutdown();
        }
    }
}
