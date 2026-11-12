using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SensorPanel
{
    /// <summary>
    /// Selects one sensor (or an aggregate of several) from LibreHardwareMonitor.
    /// "id" wins if set; otherwise hardware/type/name are matched with * and ? wildcards.
    /// "name" may hold several patterns separated by '|': the first pattern that matches anything is used.
    /// </summary>
    public class SensorSelector
    {
        public string? Id { get; set; }
        public string Hardware { get; set; } = "*";
        /// <summary>Hardware name, e.g. "Total Memory" (there is also a "Virtual Memory").</summary>
        public string Device { get; set; } = "*";
        public string Type { get; set; } = "*";
        public string Name { get; set; } = "*";
        /// <summary>first | max | min | avg | sum</summary>
        public string Agg { get; set; } = "first";

        public SensorSelector() { }

        public SensorSelector(string hardware, string type, string name, string agg = "first")
        {
            Hardware = hardware;
            Type = type;
            Name = name;
            Agg = agg;
        }
    }

    public class PanelConfig
    {
        /// <summary>Index of the monitor (0 = first). -1 = normal movable window.</summary>
        public int Monitor { get; set; } = -1;
        public bool Topmost { get; set; } = false;
        public int UpdateIntervalMs { get; set; } = 1000;

        /// <summary>Empty = taken from the hardware name.</summary>
        public string CpuTitle { get; set; } = "";
        public string GpuTitle { get; set; } = "";

        /// <summary>Optional image files (PNG with transparency) shown instead of the generic chip / card icons.</summary>
        public string CpuImage { get; set; } = "";
        public string GpuImage { get; set; } = "";

        /// <summary>Position and size of the window when Monitor is -1, in device-independent pixels.</summary>
        public double WindowLeft { get; set; } = 100;
        public double WindowTop { get; set; } = 100;
        public double WindowWidth { get; set; } = 1024;
        public double WindowHeight { get; set; } = 600;

        /// <summary>Used for the GPU TDP tile when the card does not report a power-limit percentage.</summary>
        public double GpuTdpWatts { get; set; } = 320;

        public List<string> Drives { get; set; } = new() { "C:", "D:" };

        /// <summary>Optional per-drive temperature sensor; by default the disk holding the drive letter is used.</summary>
        public Dictionary<string, SensorSelector> DriveTempSensors { get; set; } = new();

        public bool ShowFps { get; set; } = true;

        public Dictionary<string, SensorSelector> Sensors { get; set; } = DefaultSensors();

        public static Dictionary<string, SensorSelector> DefaultSensors() => new()
        {
            ["cpuClock"] = new("Cpu", "Clock", "Cores (Average)|Core #1"),
            ["cpuVoltage"] = new("Cpu", "Voltage", "Core (SVI2 TFN)|Core (SVI3 TFN)|CPU Core|Core #1 VID|Core*"),
            ["cpuTemp"] = new("Cpu", "Temperature", "Core (Tctl/Tdie)|Core (Tctl)|CPU Package|Core Max|*"),
            ["cpuLoad"] = new("Cpu", "Load", "CPU Total"),
            ["cpuPower"] = new("Cpu", "Power", "Package|CPU Package"),

            ["gpuClock"] = new("Gpu*", "Clock", "GPU Core"),
            ["gpuMemClock"] = new("Gpu*", "Clock", "GPU Memory"),
            ["gpuTemp"] = new("Gpu*", "Temperature", "GPU Core"),
            ["gpuMemTemp"] = new("Gpu*", "Temperature", "GPU Memory Junction|GPU Memory*"),
            ["gpuHotSpot"] = new("Gpu*", "Temperature", "GPU Hot Spot"),
            ["gpuLoad"] = new("Gpu*", "Load", "GPU Core"),
            ["gpuPower"] = new("Gpu*", "Power", "GPU Package|GPU Power|GPU Board Power"),
            ["gpuTdp"] = new("Gpu*", "Load", "GPU Power|GPU Board Power"),

            ["ramLoad"] = new("Memory", "Load", "Memory") { Device = "Total Memory|Generic Memory" },
            ["liquidTemp"] = new("*", "Temperature", "*Liquid*|*Coolant*|*Water*"),
            ["fan"] = new("*", "Fan", "CPU Fan|*Pump*|Fan #1|*"),
        };

        [JsonIgnore]
        public static string FilePath => Path.Combine(AppContext.BaseDirectory, "config.json");

        static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
        };

        public static PanelConfig Load()
        {
            PanelConfig config;
            try
            {
                config = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<PanelConfig>(File.ReadAllText(FilePath), JsonOptions) ?? new PanelConfig()
                    : new PanelConfig();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"No se pudo leer config.json:\n{ex.Message}\n\nSe usará la configuración por defecto.",
                    "Sensor Panel");
                return new PanelConfig();
            }

            // Keys missing from an older config file fall back to the defaults.
            foreach (var (key, selector) in DefaultSensors())
                config.Sensors.TryAdd(key, selector);

            config.Save();
            return config;
        }

        public void Save()
        {
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions)); }
            catch { /* read-only folder: keep running with in-memory config */ }
        }
    }
}
