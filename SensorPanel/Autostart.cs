using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;

namespace SensorPanel
{
    /// <summary>
    /// Start at logon through a scheduled task with highest privileges, so the panel starts elevated without a UAC prompt.
    /// (A Run registry key cannot start an elevated program silently.)
    /// </summary>
    static class Autostart
    {
        const string TaskName = "SensorPanel";

        public static bool IsEnabled() => SchTasks($"/Query /TN \"{TaskName}\"") == 0;

        public static void Set(bool enabled)
        {
            if (!enabled)
            {
                SchTasks($"/Delete /TN \"{TaskName}\" /F");
                return;
            }

            var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            var exe = SecurityElement.Escape(Environment.ProcessPath!);
            var dir = SecurityElement.Escape(AppContext.BaseDirectory);
            // Written as XML so the task has no 3-day time limit and also runs on battery.
            var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{user}</UserId>
      <Delay>PT10S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{user}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exe}</Command>
      <WorkingDirectory>{dir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
";
            var file = Path.Combine(Path.GetTempPath(), "SensorPanelTask.xml");
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            try
            {
                if (SchTasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F") != 0)
                    throw new InvalidOperationException("schtasks devolvió un error (¿se está ejecutando como administrador?).");
            }
            finally
            {
                File.Delete(file);
            }
        }

        static int SchTasks(string args)
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            p.WaitForExit();
            return p.ExitCode;
        }
    }
}
