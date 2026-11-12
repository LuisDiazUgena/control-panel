using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace SensorPanel
{
    public partial class App : Application
    {
        Mutex? _singleInstance;

        public static bool IsAdmin { get; } =
            new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // CPU and motherboard sensors need the kernel driver, which only loads elevated.
            if (!IsAdmin && !e.Args.Contains("--no-elevate", StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
                    {
                        UseShellExecute = true,
                        Verb = "runas",
                        Arguments = string.Join(" ", e.Args.Select(a => $"\"{a}\"")),
                    });
                    Shutdown();
                    return;
                }
                catch
                {
                    // UAC prompt cancelled: run with the sensors available to a normal user.
                }
            }

            _singleInstance = new Mutex(true, "SensorPanel.SingleInstance", out bool isNew);
            if (!isNew)
            {
                Shutdown();
                return;
            }

            var config = PanelConfig.Load();

            int dumpIndex = Array.FindIndex(e.Args, a => a.Equals("--dump", StringComparison.OrdinalIgnoreCase));
            if (dumpIndex >= 0 && dumpIndex + 1 < e.Args.Length)
            {
                // Headless mode: write the sensor list and exit.
                using var sensors = new SensorService(config);
                sensors.Open();
                sensors.Read();
                System.IO.File.WriteAllText(e.Args[dumpIndex + 1], sensors.DumpSensors(), System.Text.Encoding.UTF8);
                Shutdown();
                return;
            }

            MainWindow = new MainWindow(config);
            MainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
