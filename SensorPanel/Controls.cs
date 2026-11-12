using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SensorPanel
{
    /// <summary>Small notched tile: label top-right, big value top-left, icon bottom-right.</summary>
    public class Tile : Control
    {
        static Tile()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(Tile), new FrameworkPropertyMetadata(typeof(Tile)));
        }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(Tile), new PropertyMetadata(""));
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(string), typeof(Tile), new PropertyMetadata("--"));
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(Tile), new PropertyMetadata(null));

        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public Geometry? Icon { get => (Geometry?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    }

    /// <summary>Filled history graph that auto-scales to the values it holds.</summary>
    public class Sparkline : FrameworkElement
    {
        readonly Queue<double> _values = new();

        public int Capacity { get; set; } = 60;
        /// <summary>Lowest top of the scale, so a flat idle line does not fill the whole graph.</summary>
        public double MinRange { get; set; } = 1;
        public Brush Fill { get; set; } = new SolidColorBrush(Color.FromArgb(0xC0, 0xC8, 0xC8, 0xCC));
        public Brush Line { get; set; } = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE4));

        public void Push(double? value)
        {
            if (!value.HasValue || double.IsNaN(value.Value))
                return;
            _values.Enqueue(value.Value);
            while (_values.Count > Capacity)
                _values.Dequeue();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (_values.Count < 2)
                return;

            var values = _values.ToArray();
            double max = Math.Max(values.Max(), MinRange);
            double min = Math.Min(values.Min(), max * 0.5);
            double range = Math.Max(max - min, 1e-6);
            double step = w / (Capacity - 1);
            double x0 = w - step * (values.Length - 1);

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(x0, h), true, true);
                for (int i = 0; i < values.Length; i++)
                {
                    double y = h - (values[i] - min) / range * (h - 2) - 1;
                    ctx.LineTo(new Point(x0 + i * step, y), true, true);
                }
                ctx.LineTo(new Point(w, h), true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(Fill, new Pen(Line, 1), geo);
        }
    }
}
