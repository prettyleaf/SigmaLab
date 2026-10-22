using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using VideoMeter.Core;

namespace VideoMeter.UI
{
    /// <summary>Гистограмма области (Analyze › Histogram).</summary>
    public sealed class HistogramForm : Form
    {
        private readonly ImageData _img;
        private readonly Rectangle _roi;
        private readonly string _area;
        private readonly MainForm _main;
        private readonly PlotView _view;
        private readonly ComboBox _channel;
        private readonly CheckBox _log;
        private readonly Label _stats;
        private Histogram _hist;
        private RoiStats _s;

        public HistogramForm(ImageData img, Rectangle roi, int channel, string area, MainForm main)
        {
            _img = img;
            _roi = Stats.Normalize(img, roi);
            _area = area;
            _main = main;
            Theme.ApplyForm(this);
            Text = "Гистограмма — " + img.Name + " (" + area + ")";
            Size = new Size(Theme.S(720), Theme.S(500));
            MinimumSize = new Size(Theme.S(480), Theme.S(360));
            StartPosition = FormStartPosition.CenterParent;
            Theme.FitToScreen(this);
            ShowInTaskbar = false;

            _view = new PlotView { Dock = DockStyle.Fill, ExportName = "Гистограмма " + img.Name };

            var side = new TableLayoutPanel { Dock = DockStyle.Right, Width = Theme.S(230), ColumnCount = 1, Padding = new Padding(Theme.S(10)), BackColor = Theme.SurfaceAlt };
            _channel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(200) };
            _channel.Items.AddRange(new object[] { "Яркость", "Канал R", "Канал G", "Канал B" });
            _channel.SelectedIndex = channel < 0 ? 0 : channel + 1;
            _channel.Enabled = img.IsRgb;
            _channel.SelectedIndexChanged += (s, e) => Recalc();
            _log = new CheckBox { Text = "Логарифмическая шкала", AutoSize = true };
            _log.CheckedChanged += (s, e) => Redraw();
            _stats = new Label
            {
                AutoSize = true,
                Font = new Font(Theme.MonoFont.FontFamily, 8.5f),
                MaximumSize = new Size(Theme.S(210), 0),
                Margin = new Padding(0, Theme.S(8), 0, Theme.S(8))
            };
            side.Controls.Add(_channel);
            side.Controls.Add(_log);
            side.Controls.Add(_stats);
            foreach (var kv in new[]
            {
                new Tuple2("В таблицу результатов", AddToResults),
                new Tuple2("Копировать график", () => _view.CopyImage()),
                new Tuple2("Сохранить график…", () => _view.SaveImageDialog()),
                new Tuple2("Список значений (CSV)…", SaveList)
            })
            {
                var b = Theme.MakeButton(kv.Text);
                b.Width = Theme.S(200);
                b.AutoSize = false;
                b.Height = Theme.S(28);
                Action a = kv.Action;
                b.Click += (s, e) => a();
                side.Controls.Add(b);
            }
            Controls.Add(_view);
            Controls.Add(side);
            Recalc();
        }

        private sealed class Tuple2
        {
            public readonly string Text;
            public readonly Action Action;
            public Tuple2(string t, Action a) { Text = t; Action = a; }
        }

        private int Channel { get { return _img.IsRgb && _channel.SelectedIndex > 0 ? _channel.SelectedIndex - 1 : -1; } }

        private void Recalc()
        {
            _hist = Stats.ComputeHistogram(_img, _roi, Channel);
            _s = _hist.Stats;
            var sb = new StringBuilder();
            sb.AppendLine("Изображение: " + _img.Name);
            sb.AppendLine("Область: " + _area);
            sb.AppendLine("  x=" + _roi.X + ", y=" + _roi.Y + ", " + _roi.Width + "×" + _roi.Height);
            sb.AppendLine();
            sb.AppendLine("Count (N):   " + _s.N.ToString("N0"));
            sb.AppendLine("Mean (M):    " + Ui.G(_s.Mean));
            sb.AppendLine("StdDev (σ):  " + Ui.G(_s.StdDev));
            sb.AppendLine("Min:         " + Ui.G(_s.Min));
            sb.AppendLine("Max:         " + Ui.G(_s.Max));
            sb.AppendLine("Mode:        " + Ui.G(_s.Mode));
            sb.AppendLine("Бинов:       " + _hist.Bins + (_hist.IntegerBins ? "" : ", ширина " + Ui.G(_hist.BinWidth)));
            _stats.Text = sb.ToString();
            Redraw();
        }

        private void Redraw()
        {
            var p = new Plot
            {
                Title = _img.Name + " — " + _area + (Channel >= 0 ? " [" + "RGB"[Channel] + "]" : ""),
                XLabel = "Значение пикселя",
                YLabel = "Число пикселей",
                LogY = _log.Checked,
                ShowLegend = false
            };
            int n = _hist.Bins;
            var x = new double[n];
            var y = new double[n];
            for (int i = 0; i < n; i++) { x[i] = _hist.BinCenter(i); y[i] = _hist.Counts[i]; }
            var s = p.Add("Гистограмма", x, y, Theme.Primary);
            s.Bars = true;
            if (_hist.IntegerBins) { p.XMin = -0.5; p.XMax = n - 0.5; }
            p.Notes.Add("Count = " + _s.N + ";  Mean = " + Ui.G(_s.Mean) + ";  StdDev = " + Ui.G(_s.StdDev));
            p.Notes.Add("Min = " + Ui.G(_s.Min) + ";  Max = " + Ui.G(_s.Max) + ";  Mode = " + Ui.G(_s.Mode));
            p.Lines.Add(new RefLine { Value = _s.Mean, Horizontal = false, Color = Theme.AccentDark, Label = "M" });
            _view.Plot = p;
        }

        private void AddToResults()
        {
            if (_main != null) _main.AddResult(_img.Name + (Channel >= 0 ? " [" + "RGB"[Channel] + "]" : ""), _area, _roi, _s);
        }

        private void SaveList()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Значение;Число пикселей");
            for (int i = 0; i < _hist.Bins; i++) sb.AppendLine(Csv.Num(_hist.BinCenter(i)) + ";" + _hist.Counts[i].ToString(CultureInfo.InvariantCulture));
            Ui.SaveText(this, sb.ToString(), PlotView.Safe("Гистограмма " + _img.Name) + ".csv");
        }
    }

    /// <summary>Профиль яркости вдоль прямоугольного выделения (Analyze › Plot Profile).</summary>
    public sealed class ProfileForm : Form
    {
        private readonly ImageData _img;
        private readonly Rectangle _roi;
        private readonly int _channel;
        private readonly PlotView _view;
        private readonly CheckBox _vertical;

        public ProfileForm(ImageData img, Rectangle roi, int channel)
        {
            _img = img;
            _roi = Stats.Normalize(img, roi);
            _channel = channel;
            Theme.ApplyForm(this);
            Text = "Профиль — " + img.Name;
            Size = new Size(Theme.S(760), Theme.S(440));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            _view = new PlotView { Dock = DockStyle.Fill, ExportName = "Профиль " + img.Name };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(40), Padding = new Padding(Theme.S(6)), BackColor = Theme.SurfaceAlt };
            _vertical = new CheckBox { Text = "Вдоль вертикали (среднее по строкам)", AutoSize = true, Checked = _roi.Height > _roi.Width * 2, Margin = new Padding(Theme.S(4), Theme.S(6), Theme.S(16), 0) };
            _vertical.CheckedChanged += (s, e) => Redraw();
            bar.Controls.Add(_vertical);
            var save = Theme.MakeButton("Сохранить график…");
            save.Click += (s, e) => _view.SaveImageDialog();
            var csv = Theme.MakeButton("Данные (CSV)…");
            csv.Click += (s, e) => _view.SaveCsvDialog();
            bar.Controls.Add(save);
            bar.Controls.Add(csv);
            Controls.Add(_view);
            Controls.Add(bar);
            Redraw();
        }

        private void Redraw()
        {
            bool v = _vertical.Checked;
            double[] prof = Stats.Profile(_img, _roi, v, _channel);
            var x = new double[prof.Length];
            for (int i = 0; i < x.Length; i++) x[i] = (v ? _roi.Y : _roi.X) + i;
            var p = new Plot
            {
                Title = "Профиль: " + _img.Name + " (x=" + _roi.X + ", y=" + _roi.Y + ", " + _roi.Width + "×" + _roi.Height + ")",
                XLabel = v ? "Строка y, пикс." : "Столбец x, пикс.",
                YLabel = "Средний уровень",
                ShowLegend = false
            };
            p.Add("Профиль", x, prof, Theme.Primary);
            _view.Plot = p;
        }
    }
}
