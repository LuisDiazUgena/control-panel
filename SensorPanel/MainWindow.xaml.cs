using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;

namespace SensorPanel
{
    public partial class MainWindow : Window
    {
        PanelConfig _config;
        SensorService? _sensors;
        bool _closing;
        readonly List<(TextBlock size, Rectangle bar, TextBlock temp)> _driveRows = new();

        const double DriveBarWidth = 270;
        static readonly CultureInfo Num = CultureInfo.InvariantCulture;

        public MainWindow(PanelConfig config)
        {
            _config = config;
            InitializeComponent();
            BuildBackdrop();
            ApplyConfig();

            SourceInitialized += (_, _) => PlaceWindow();
            MouseLeftButtonDown += (_, e) =>
            {
                if (_config.Monitor < 0 && e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };
            Loaded += async (_, _) => await RunAsync();
            Closing += (_, _) => SaveWindowBounds();
        }

        // ---------------------------------------------------------------- setup

        void ApplyConfig()
        {
            Topmost = _config.Topmost;
            FpsCaption.Visibility = Fps.Visibility = _config.ShowFps ? Visibility.Visible : Visibility.Hidden;
            SetBrandImage(CpuImage, CpuIcon, _config.CpuImage);
            SetBrandImage(GpuImage, GpuIcon, _config.GpuImage);
            BuildDriveRows();
            if (_sensors != null)
                UpdateTitles();
        }

        static void SetBrandImage(Image image, System.Windows.Shapes.Path fallback, string file)
        {
            image.Visibility = Visibility.Collapsed;
            fallback.Visibility = Visibility.Visible;
            if (string.IsNullOrWhiteSpace(file))
                return;
            var path = System.IO.Path.IsPathRooted(file) ? file : System.IO.Path.Combine(AppContext.BaseDirectory, file);
            if (!File.Exists(path))
                return;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            image.Source = bmp;
            image.Visibility = Visibility.Visible;
            fallback.Visibility = Visibility.Collapsed;
        }

        void UpdateTitles()
        {
            CpuTitle.Text = string.IsNullOrWhiteSpace(_config.CpuTitle) ? ShortName(_sensors!.CpuName) : _config.CpuTitle;
            GpuTitle.Text = string.IsNullOrWhiteSpace(_config.GpuTitle) ? ShortName(_sensors!.GpuName) : _config.GpuTitle;
        }

        /// <summary>"AMD Ryzen 9 5900X 12-Core Processor" -> "AMD RYZEN 9 5900X", "NVIDIA GeForce RTX 3080" -> "RTX 3080".</summary>
        static string ShortName(string name)
        {
            name = Regex.Replace(name, @"\(R\)|\(TM\)|\d+-Core|Processor|CPU|NVIDIA|GeForce|Radeon\(TM\)", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\s+", " ").Trim();
            return name.ToUpperInvariant();
        }

        void BuildDriveRows()
        {
            DriveRows.Children.Clear();
            _driveRows.Clear();
            int count = Math.Min(_config.Drives.Count, 3);
            double spacing = count <= 2 ? 72 : 50;
            double top = count <= 2 ? 40 : 30;
            double barHeight = count <= 2 ? 22 : 16;
            var dim = (Brush)FindResource("TextDim");
            var main = (Brush)FindResource("TextMain");
            var labelFont = (FontFamily)FindResource("LabelFont");

            for (int i = 0; i < count; i++)
            {
                double y = top + i * spacing;
                var letter = new TextBlock { Text = _config.Drives[i].TrimEnd('\\'), FontFamily = labelFont, FontSize = 22, Foreground = main };
                Canvas.SetLeft(letter, 142); Canvas.SetTop(letter, y - 5);

                var size = new TextBlock { FontFamily = labelFont, FontSize = 19, Foreground = dim, Width = DriveBarWidth - 30, TextAlignment = TextAlignment.Right };
                Canvas.SetLeft(size, 172); Canvas.SetTop(size, y - 2);

                var track = new Rectangle { Width = DriveBarWidth, Height = barHeight, Fill = new SolidColorBrush(Color.FromArgb(0xC0, 0x55, 0x57, 0x5E)) };
                Canvas.SetLeft(track, 142); Canvas.SetTop(track, y + 24);
                var bar = new Rectangle { Width = 0, Height = barHeight, Fill = new SolidColorBrush(Color.FromRgb(0xDA, 0xDA, 0xDC)) };
                Canvas.SetLeft(bar, 142); Canvas.SetTop(bar, y + 24);

                var temp = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = main, Text = "--" };
                Canvas.SetLeft(temp, 422); Canvas.SetTop(temp, y + 24 + barHeight / 2 - 16);

                DriveRows.Children.Add(letter);
                DriveRows.Children.Add(size);
                DriveRows.Children.Add(track);
                DriveRows.Children.Add(bar);
                DriveRows.Children.Add(temp);
                _driveRows.Add((size, bar, temp));
            }
        }

        /// <summary>Dark slate on the left fading into green/cyan on the right, crossed by diagonal stripes.</summary>
        void BuildBackdrop()
        {
            var baseGradient = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            baseGradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x24, 0x27, 0x30), 0.0));
            baseGradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x26, 0x2A, 0x33), 0.30));
            baseGradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x0E, 0x7A, 0x5C), 0.55));
            baseGradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x08, 0xC8, 0x8C), 0.78));
            baseGradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x18, 0xE2, 0xD2), 1.0));
            Backdrop.Children.Add(new Rectangle { Width = 1024, Height = 600, Fill = baseGradient });

            var rnd = new Random(5900);
            double x = -500;
            while (x < 1300)
            {
                double width = rnd.Next(18, 90);
                double t = Math.Clamp((x + 300) / 1300, 0, 1); // stripes get greener to the right
                Color color = rnd.Next(3) switch
                {
                    0 => Color.FromArgb((byte)rnd.Next(40, 110), 0x14, 0x17, 0x1E),
                    1 => Color.FromArgb((byte)(30 + 90 * t), 0x10, 0xE0, 0x9A),
                    _ => Color.FromArgb((byte)(20 + 70 * t), 0x40, 0xF0, 0xE0),
                };
                var stripe = new Rectangle
                {
                    Width = width,
                    Height = 1600,
                    Fill = new SolidColorBrush(color),
                    RenderTransform = new RotateTransform(38),
                };
                Canvas.SetLeft(stripe, x);
                Canvas.SetTop(stripe, -300);
                Backdrop.Children.Add(stripe);
                x += width + rnd.Next(0, 60);
            }
            // keep the left half calm so the panels stay readable
            var shade = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0x22, 0x25, 0x2D), 0.0));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 0x22, 0x25, 0x2D), 0.35));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x22, 0x25, 0x2D), 0.7));
            Backdrop.Children.Add(new Rectangle { Width = 1024, Height = 600, Fill = shade });
        }

        // ---------------------------------------------------------------- window placement

        void PlaceWindow()
        {
            var screens = Forms.Screen.AllScreens;
            if (_config.Monitor >= 0 && _config.Monitor < screens.Length)
            {
                ResizeMode = ResizeMode.NoResize;
                var b = screens[_config.Monitor].Bounds;
                var hwnd = new WindowInteropHelper(this).Handle;
                SetWindowPos(hwnd, IntPtr.Zero, b.Left, b.Top, b.Width, b.Height, SWP_NOZORDER | SWP_NOACTIVATE);
            }
            else
            {
                ResizeMode = ResizeMode.CanResizeWithGrip;
                Left = _config.WindowLeft;
                Top = _config.WindowTop;
                Width = _config.WindowWidth;
                Height = _config.WindowHeight;
            }
        }

        void SaveWindowBounds()
        {
            if (_config.Monitor >= 0 || WindowState != WindowState.Normal)
                return;
            _config.WindowLeft = Left;
            _config.WindowTop = Top;
            _config.WindowWidth = Width;
            _config.WindowHeight = Height;
            _config.Save();
        }

        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // ---------------------------------------------------------------- update loop

        async Task RunAsync()
        {
            UpdateClock();
            if (!App.IsAdmin)
                ShowStatus("Sin permisos de administrador: los sensores de CPU y placa base pueden no estar disponibles.");

            try
            {
                _sensors = new SensorService(_config);
                await Task.Run(_sensors.Open);
                UpdateTitles();
            }
            catch (Exception ex)
            {
                ShowStatus("No se pudieron abrir los sensores: " + ex.Message);
                _sensors = null;
            }

            while (!_closing)
            {
                UpdateClock();
                if (_sensors != null)
                {
                    try
                    {
                        var sensors = _sensors;
                        var snap = await Task.Run(sensors.Read);
                        Show(snap);
                    }
                    catch (Exception ex)
                    {
                        ShowStatus("Error leyendo sensores: " + ex.Message);
                    }
                }
                await Task.Delay(Math.Max(250, _config.UpdateIntervalMs));
            }
        }

        void ShowStatus(string text)
        {
            Status.Text = text;
            Status.Visibility = Visibility.Visible;
        }

        void UpdateClock()
        {
            var now = DateTime.Now;
            Clock.Text = now.ToString("HH:mm");
            Date.Text = now.ToString("dd/MM/yyyy");
        }

        void Show(Snapshot s)
        {
            CpuClock.Text = Fmt(s["cpuClock"], "0", " MHz");
            CpuVoltage.Text = Fmt(s["cpuVoltage"], "0.000", " V");
            CpuClockGraph.Push(s["cpuClock"]);
            CpuVoltageGraph.Push(s["cpuVoltage"]);

            GpuClock.Text = Fmt(s["gpuClock"], "0", " MHz");
            GpuMemClock.Text = Fmt(s["gpuMemClock"], "0", " MHz");
            GpuClockGraph.Push(s["gpuClock"]);
            GpuMemClockGraph.Push(s["gpuMemClock"]);
            GpuMemTemp.Text = Fmt(s["gpuMemTemp"], "0", "°C");
            GpuHotSpot.Text = Fmt(s["gpuHotSpot"], "0", "°C");
            Fps.Text = Fmt(s.Fps ?? 0, "0");

            CpuTempTile.Value = Fmt(s["cpuTemp"], "0");
            GpuTempTile.Value = Fmt(s["gpuTemp"], "0");
            LiquidTempTile.Value = Fmt(s["liquidTemp"], "0");
            CpuLoadTile.Value = Fmt(s["cpuLoad"], "0");
            GpuLoadTile.Value = Fmt(s["gpuLoad"], "0");
            RamLoadTile.Value = Fmt(s["ramLoad"], "0");
            CpuPowerTile.Value = Fmt(s["cpuPower"], "0.0");
            GpuPowerTile.Value = Fmt(s["gpuPower"], "0.0");
            var tdp = s["gpuTdp"] ?? (s["gpuPower"] is double w && _config.GpuTdpWatts > 0 ? w / _config.GpuTdpWatts * 100 : null);
            GpuTdpTile.Value = Fmt(tdp, "0");
            FanTile.Value = Fmt(s["fan"], "0");

            for (int i = 0; i < _driveRows.Count && i < s.Drives.Count; i++)
            {
                var d = s.Drives[i];
                var row = _driveRows[i];
                row.size.Text = d.TotalGb > 0 ? $"{d.FreeGb:0} / {d.TotalGb:0} GB libres" : "no disponible";
                row.bar.Width = DriveBarWidth * Math.Clamp(d.UsedFraction, 0, 1);
                row.temp.Text = Fmt(d.Temperature, "0", "°C");
            }
        }

        static string Fmt(double? value, string format, string unit = "") =>
            value.HasValue && !double.IsNaN(value.Value) ? value.Value.ToString(format, Num) + unit : "--";

        // ---------------------------------------------------------------- context menu

        void Menu_Opened(object sender, RoutedEventArgs e)
        {
            MonitorMenu.Items.Clear();
            var windowed = new MenuItem { Header = "Ventana normal", IsCheckable = true, IsChecked = _config.Monitor < 0 };
            windowed.Click += (_, _) => SetMonitor(-1);
            MonitorMenu.Items.Add(windowed);
            var screens = Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                int index = i;
                var b = screens[i].Bounds;
                var item = new MenuItem
                {
                    Header = $"Pantalla {i + 1}: {b.Width}x{b.Height}{(screens[i].Primary ? " (principal)" : "")}",
                    IsCheckable = true,
                    IsChecked = _config.Monitor == i,
                };
                item.Click += (_, _) => SetMonitor(index);
                MonitorMenu.Items.Add(item);
            }
            TopmostItem.IsChecked = _config.Topmost;
            AutostartItem.IsChecked = Autostart.IsEnabled();
        }

        void SetMonitor(int index)
        {
            SaveWindowBounds();
            _config.Monitor = index;
            _config.Save();
            PlaceWindow();
        }

        void Topmost_Click(object sender, RoutedEventArgs e)
        {
            _config.Topmost = TopmostItem.IsChecked;
            _config.Save();
            Topmost = _config.Topmost;
        }

        void Autostart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Autostart.Set(AutostartItem.IsChecked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo cambiar el inicio automático:\n" + ex.Message, "Sensor Panel");
            }
        }

        void Dump_Click(object sender, RoutedEventArgs e)
        {
            if (_sensors == null)
                return;
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "sensores.txt");
            File.WriteAllText(path, _sensors.DumpSensors(), System.Text.Encoding.UTF8);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        void OpenConfig_Click(object sender, RoutedEventArgs e)
        {
            _config.Save();
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{PanelConfig.FilePath}\"") { UseShellExecute = true });
        }

        void Reload_Click(object sender, RoutedEventArgs e)
        {
            _config = PanelConfig.Load();
            if (_sensors != null)
            {
                // The service holds the selectors, so recreate it with the new config.
                var old = _sensors;
                _sensors = null;
                Task.Run(() =>
                {
                    old.Dispose();
                    var fresh = new SensorService(_config);
                    fresh.Open();
                    return fresh;
                }).ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        _sensors = t.Result;
                        UpdateTitles();
                    }
                    else
                        ShowStatus("No se pudieron abrir los sensores: " + t.Exception?.GetBaseException().Message);
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            ApplyConfig();
            PlaceWindow();
        }

        void Exit_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            _closing = true;
            _sensors?.Dispose();
            base.OnClosed(e);
        }
    }
}
