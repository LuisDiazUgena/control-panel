using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace SensorPanel
{
    public class DriveSnapshot
    {
        public string Letter = "";
        public double TotalGb;
        public double FreeGb;
        public double? Temperature;
        public double UsedFraction => TotalGb > 0 ? (TotalGb - FreeGb) / TotalGb : 0;
    }

    public class Snapshot
    {
        public Dictionary<string, double?> Values = new();
        public List<DriveSnapshot> Drives = new();
        public double? Fps;
        public double? this[string key] => Values.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>Wraps LibreHardwareMonitor: opens the hardware, refreshes it and resolves the configured selectors.</summary>
    public sealed class SensorService : IDisposable
    {
        readonly PanelConfig _config;
        readonly Computer _computer;
        readonly RtssReader _rtss = new();
        readonly Dictionary<string, Regex> _regexCache = new();
        Dictionary<string, int> _driveToDiskIndex = new(StringComparer.OrdinalIgnoreCase);

        public string CpuName { get; private set; } = "CPU";
        public string GpuName { get; private set; } = "GPU";

        public SensorService(PanelConfig config)
        {
            _config = config;
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsStorageEnabled = true,
                IsPsuEnabled = true,
            };
        }

        public void Open()
        {
            _computer.Open();
            CpuName = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu)?.Name ?? "CPU";
            var gpus = _computer.Hardware.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel).ToList();
            // Prefer a discrete card over the integrated one.
            GpuName = (gpus.FirstOrDefault(h => h.HardwareType != HardwareType.GpuIntel) ?? gpus.FirstOrDefault())?.Name ?? "GPU";
            _driveToDiskIndex = MapDrivesToDisks();
        }

        IEnumerable<IHardware> AllHardware()
        {
            foreach (var hw in _computer.Hardware)
            {
                yield return hw;
                foreach (var sub in hw.SubHardware)
                    yield return sub;
            }
        }

        IEnumerable<ISensor> AllSensors() => AllHardware().SelectMany(h => h.Sensors);

        readonly object _lock = new();

        public Snapshot Read()
        {
            lock (_lock)
                return ReadLocked();
        }

        Snapshot ReadLocked()
        {
            foreach (var hw in _computer.Hardware)
            {
                hw.Update();
                foreach (var sub in hw.SubHardware)
                    sub.Update();
            }

            var sensors = AllSensors().ToList();
            var snap = new Snapshot();
            foreach (var (key, selector) in _config.Sensors)
                snap.Values[key] = Resolve(sensors, selector);

            snap.Drives = ReadDrives(sensors);
            if (_config.ShowFps)
                snap.Fps = _rtss.ReadForegroundFps();
            return snap;
        }

        double? Resolve(List<ISensor> sensors, SensorSelector sel)
        {
            if (!string.IsNullOrWhiteSpace(sel.Id))
            {
                var exact = sensors.FirstOrDefault(s => s.Identifier.ToString().Equals(sel.Id, StringComparison.OrdinalIgnoreCase));
                return exact?.Value;
            }

            var candidates = sensors.Where(s =>
                Matches(sel.Hardware, s.Hardware.HardwareType.ToString()) &&
                Matches(sel.Device, s.Hardware.Name) &&
                Matches(sel.Type, s.SensorType.ToString()) &&
                s.Value.HasValue).ToList();

            foreach (var pattern in sel.Name.Split('|'))
            {
                var hits = candidates.Where(s => Matches(pattern.Trim(), s.Name)).Select(s => (double)s.Value!.Value).ToList();
                if (hits.Count == 0)
                    continue;
                return sel.Agg.ToLowerInvariant() switch
                {
                    "max" => hits.Max(),
                    "min" => hits.Min(),
                    "avg" => hits.Average(),
                    "sum" => hits.Sum(),
                    _ => hits[0],
                };
            }
            return null;
        }

        bool Matches(string pattern, string text)
        {
            if (pattern.Contains('|'))
                return pattern.Split('|').Any(p => Matches(p.Trim(), text));

            if (!_regexCache.TryGetValue(pattern, out var regex))
            {
                var expr = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                regex = new Regex(expr, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                _regexCache[pattern] = regex;
            }
            return regex.IsMatch(text);
        }

        List<DriveSnapshot> ReadDrives(List<ISensor> sensors)
        {
            var result = new List<DriveSnapshot>();
            foreach (var raw in _config.Drives)
            {
                var letter = raw.TrimEnd('\\', '/');
                var snap = new DriveSnapshot { Letter = letter };
                try
                {
                    var info = new DriveInfo(letter + "\\");
                    if (info.IsReady)
                    {
                        snap.TotalGb = info.TotalSize / 1e9;
                        snap.FreeGb = info.AvailableFreeSpace / 1e9;
                    }
                }
                catch { /* drive missing */ }

                if (_config.DriveTempSensors.TryGetValue(letter, out var sel))
                    snap.Temperature = Resolve(sensors, sel);
                else if (_driveToDiskIndex.TryGetValue(letter, out var index))
                    snap.Temperature = StorageTemperature(index);

                result.Add(snap);
            }
            return result;
        }

        double? StorageTemperature(int diskIndex)
        {
            // LibreHardwareMonitor names storage hardware "/hdd/{n}", "/nvme/{n}", "/ssd/{n}" where n is the physical drive index.
            var disk = _computer.Hardware.FirstOrDefault(h =>
                h.HardwareType == HardwareType.Storage &&
                h.Identifier.ToString().EndsWith("/" + diskIndex, StringComparison.Ordinal));
            var temp = disk?.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Value.HasValue);
            return temp?.Value;
        }

        static Dictionary<string, int> MapDrivesToDisks()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Antecedent: ...Win32_DiskPartition.DeviceID="Disk #1, Partition #1"   Dependent: ...Win32_LogicalDisk.DeviceID="C:"
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDiskToPartition");
                foreach (ManagementObject link in searcher.Get())
                {
                    var disk = Regex.Match(link["Antecedent"]?.ToString() ?? "", @"Disk #(\d+)");
                    var letter = Regex.Match(link["Dependent"]?.ToString() ?? "", @"DeviceID=""([A-Z]:)""");
                    if (disk.Success && letter.Success)
                        map[letter.Groups[1].Value] = int.Parse(disk.Groups[1].Value);
                }
            }
            catch { /* WMI unavailable: drive temperatures stay empty */ }
            return map;
        }

        /// <summary>Text listing of every sensor with its identifier, to help writing config.json.</summary>
        public string DumpSensors()
        {
            lock (_lock)
                return DumpLocked();
        }

        string DumpLocked()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Sensores detectados por LibreHardwareMonitor");
            sb.AppendLine("Usa 'hardware' (tipo), 'type' y 'name', o directamente 'id', en config.json");
            sb.AppendLine();
            foreach (var hw in AllHardware())
            {
                sb.AppendLine($"[{hw.HardwareType}] {hw.Name}   ({hw.Identifier})");
                foreach (var s in hw.Sensors.OrderBy(s => s.SensorType).ThenBy(s => s.Index))
                    sb.AppendLine($"    {s.SensorType,-12} {s.Name,-32} {Format(s.Value),10}   id: {s.Identifier}");
                sb.AppendLine();
            }
            sb.AppendLine("Unidades lógicas -> disco físico: " +
                string.Join(", ", _driveToDiskIndex.Select(kv => $"{kv.Key}=#{kv.Value}")));
            return sb.ToString();
        }

        static string Format(float? v) => v.HasValue ? v.Value.ToString("0.###") : "-";

        public void Dispose()
        {
            lock (_lock)
            {
                _rtss.Dispose();
                _computer.Close();
            }
        }
    }
}
