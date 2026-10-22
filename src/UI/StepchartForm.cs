using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using VideoMeter.Core;
using VideoMeter.IO;

namespace VideoMeter.UI
{
    /// <summary>Модуль Stepchart: выбор изображения и области таблицы, настройки, запуск анализа.</summary>
    public sealed class StepchartForm : Form
    {
        private static Rectangle _lastRoi;
        private static Size _lastSize;
        private static StepchartSettings _lastSettings;

        private readonly MainForm _main;
        private readonly ImageViewer _viewer;
        private ImageData _img;
        private Bitmap _bmp;
        private readonly Label _status;
        private readonly Label _imgLabel;

        private ComboBox _chart, _detect, _detrend, _f1, _f2m, _f2l;
        private NumBox _zones, _step, _first, _fraction, _sat, _offset;
        private TextBox _title;
        private CheckBox _p1, _p2, _p3;
        private bool _loading;

        public StepchartForm(MainForm main)
        {
            _main = main;
            Theme.ApplyForm(this);
            Text = "Stepchart — анализ шумов по ступенчатой тестовой таблице";
            Size = new Size(Theme.S(1240), Theme.S(800));
            MinimumSize = new Size(Theme.S(900), Theme.S(600));
            StartPosition = FormStartPosition.CenterParent;
            Theme.FitToScreen(this);

            var left = BuildSettings();
            var center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            _viewer = new ImageViewer { Dock = DockStyle.Fill, EmptyText = "Откройте изображение тестовой таблицы\n(кнопки внизу) и выделите мышью область со всеми градациями" };
            _viewer.RoiChanged += (s, e) => UpdateZones();
            _viewer.RoiChanging += (s, e) => UpdateZones();

            var hint = new Label
            {
                Dock = DockStyle.Top,
                Height = Theme.S(46),
                Padding = new Padding(Theme.S(10), Theme.S(4), Theme.S(10), 0),
                BackColor = Theme.TintLight,
                ForeColor = Theme.Text,
                Text = "1) Откройте снимок таблицы.  2) Выделите мышью область, охватывающую все градации и не выходящую за границы таблицы " +
                       "(как в окне ROI Imatest).  3) Проверьте, что зоны (зелёные прямоугольники) совпали с градациями, и нажмите «Анализ»." +
                       "  Настройки сохраняются до следующего запуска модуля."
            };
            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(46), BackColor = Theme.SurfaceAlt, Padding = new Padding(Theme.S(6)) };
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.SurfaceAlt, WrapContents = false, Margin = Padding.Empty };
            var bOpen = Theme.MakeButton("Открыть файл…");
            bOpen.Image = Icons.Get("open");
            bOpen.TextImageRelation = TextImageRelation.ImageBeforeText;
            bOpen.Click += (s, e) => OpenFile();
            var bActive = Theme.MakeButton("Взять активное изображение");
            bActive.Click += (s, e) => TakeActive(true);
            var bAll = Theme.MakeButton("Выделить всё");
            bAll.Click += (s, e) => { if (_img != null) _viewer.Roi = new Rectangle(0, 0, _img.Width, _img.Height); };
            var bRun = Theme.MakeButton("Анализ  (Yes, Continue)", true);
            bRun.Click += (s, e) => Run();
            _imgLabel = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(Theme.S(8), Theme.S(9), 0, 0) };
            bottom.Controls.AddRange(new Control[] { bOpen, bActive, bAll, _imgLabel });
            bRun.Dock = DockStyle.Right;
            bRun.AutoSize = false;
            bRun.Width = Theme.S(210);
            bottomPanel.Controls.Add(bottom);
            bottomPanel.Controls.Add(bRun);
            _status = new Label { Dock = DockStyle.Bottom, Height = Theme.S(24), Padding = new Padding(Theme.S(10), Theme.S(4), 0, 0), ForeColor = Theme.Primary, Font = Theme.BoldFont, BackColor = Color.White };

            center.Controls.Add(_viewer);
            center.Controls.Add(hint);
            center.Controls.Add(_status);
            center.Controls.Add(bottomPanel);
            Controls.Add(center);
            Controls.Add(left);
            AcceptButton = bRun;

            ApplySettings(_lastSettings ?? new StepchartSettings());
            TakeActive(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _bmp != null) _bmp.Dispose();
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ панель настроек

        private Control BuildSettings()
        {
            var outer = new Panel { Dock = DockStyle.Left, Width = Theme.S(400), BackColor = Color.White };
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(Theme.S(10)), Dock = DockStyle.Top };
            Func<string, Label> cap = s => new Label { Text = s, AutoSize = true, ForeColor = Theme.Primary, Font = Theme.BoldFont, Margin = new Padding(0, Theme.S(10), 0, Theme.S(2)) };
            Func<string, Label> sub = s => new Label { Text = s, AutoSize = true, ForeColor = Theme.Muted, MaximumSize = new Size(Theme.S(360), 0), Margin = new Padding(0, Theme.S(4), 0, Theme.S(1)) };
            int w = Theme.S(360);

            t.Controls.Add(cap("Title"));
            _title = new TextBox { Width = w };
            _title.TextChanged += (s, e) => { if (!_settingTitle) _titleAuto = false; };
            t.Controls.Add(sub("Название (по умолчанию — имя файла)"));
            t.Controls.Add(_title);

            t.Controls.Add(cap("Chart type — тип тестовой таблицы"));
            _chart = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w };
            foreach (var c in StepchartSettings.Charts) _chart.Items.Add(c);
            _chart.SelectedIndexChanged += (s, e) =>
            {
                if (_loading) return;
                var ct = StepchartSettings.Charts[_chart.SelectedIndex];
                _loading = true;
                _step.Value = ct.Step; _first.Value = ct.First; _zones.Value = ct.Zones;
                _loading = false;
                Changed();
            };
            t.Controls.Add(_chart);
            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(250)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Func<string, double, int, NumBox> num = (label, v, dec) =>
            {
                var l = new Label { Text = label, AutoSize = true, Margin = new Padding(0, Theme.S(6), 0, 0) };
                var nb = new NumBox { Decimals = dec, Caption = label, Width = Theme.S(90) };
                nb.Value = v;
                nb.TextChanged += (s, e) => Changed();
                grid.Controls.Add(l);
                grid.Controls.Add(nb);
                return nb;
            };
            _zones = num("Число зон (0 — определить автоматически):", 20, 0);
            _step = num("Шаг плотности ΔD:", 0.1, 3);
            _first = num("Плотность первой (светлой) зоны:", 0.05, 3);
            t.Controls.Add(grid);

            t.Controls.Add(cap("Zone detection — определение зон"));
            _detect = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w };
            _detect.Items.AddRange(new object[] { "Automatic — границы зон по перепадам яркости", "Manual — равные интервалы по числу зон" });
            _detect.SelectedIndexChanged += (s, e) => Changed();
            t.Controls.Add(_detect);

            var grid2 = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
            grid2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(250)));
            grid2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Func<string, double, int, NumBox> num2 = (label, v, dec) =>
            {
                var l = new Label { Text = label, AutoSize = true, Margin = new Padding(0, Theme.S(6), 0, 0) };
                var nb = new NumBox { Decimals = dec, Caption = label, Width = Theme.S(90) };
                nb.Value = v;
                nb.TextChanged += (s, e) => Changed();
                grid2.Controls.Add(l);
                grid2.Controls.Add(nb);
                return nb;
            };
            _fraction = num2("Доля зоны для измерения, %:", 60, 0);
            _sat = num2("Saturation level — насыщение:", 255, 0);
            _offset = num2("Pixel offset — смещение уровня:", 0, 1);
            t.Controls.Add(grid2);
            t.Controls.Add(sub("Неравномерность освещения внутри зоны:"));
            _detrend = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w };
            _detrend.Items.AddRange(new object[] { "Не учитывать", "Вычитать плоскость (1-й порядок)", "Вычитать поверхность 2-го порядка (как Imatest)" });
            t.Controls.Add(_detrend);

            t.Controls.Add(cap("Results (Fig. 1), Lower plot: Pixel noise"));
            _f1 = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w };
            _f1.Items.AddRange(new object[]
            {
                "Noise (%) normalized to image density range = 1.5",
                "Noise (%) normalized to 255 pixels (max of 100)",
                "Noise (%) normalized to white−black difference",
                "Noise in pixels (σ)"
            });
            t.Controls.Add(_f1);

            t.Controls.Add(cap("Results (Fig. 2), Middle plot: f-stop (EV) noise or SNR"));
            _f2m = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w };
            _f2m.Items.AddRange(new object[] { "F-stop (EV) noise — шум в единицах f-stop", "SNR (1/f-stop (1/EV) noise) — ОСШ для f-stop" });
            t.Controls.Add(_f2m);

            t.Controls.Add(cap("Results (Fig. 2), Lower plot: Pixel noise or SNR"));
            _f2l = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w };
            _f2l.Items.AddRange(new object[]
            {
                "Noise (%) normalized to 255 pixels",
                "Pixel SNR (dB) = 20·lg(S/N)",
                "Noise in pixels (σ)",
                "Pixel SNR (раз) = S/N"
            });
            t.Controls.Add(_f2l);

            t.Controls.Add(cap("Plot — какие рисунки построить"));
            _p1 = new CheckBox { Text = "1. Pixels, noise", AutoSize = true, Checked = true };
            _p2 = new CheckBox { Text = "2. Density, noise detail", AutoSize = true, Checked = true };
            _p3 = new CheckBox { Text = "3. Density, all channels + White Balance", AutoSize = true, Checked = true };
            t.Controls.Add(_p1);
            t.Controls.Add(_p2);
            t.Controls.Add(_p3);

            scroll.Controls.Add(t);
            outer.Controls.Add(scroll);
            outer.Controls.Add(Theme.MakeHeader("Stepchart settings — настройки"));
            return outer;
        }

        private void ApplySettings(StepchartSettings s)
        {
            _loading = true;
            {
                _chart.SelectedIndex = Math.Max(0, Math.Min(StepchartSettings.Charts.Length - 1, s.ChartIndex));
                _zones.Value = s.Zones;
                _step.Value = s.DensityStep;
                _first.Value = s.FirstDensity;
                _detect.SelectedIndex = (int)s.Detection;
                _fraction.Value = s.ZoneFraction * 100;
                _sat.Value = s.Saturation;
                _offset.Value = s.PixelOffset;
                _detrend.SelectedIndex = (int)s.Detrend;
            }
            _f1.SelectedIndex = (int)s.Fig1Lower;
            _f2m.SelectedIndex = (int)s.Fig2Middle;
            _f2l.SelectedIndex = (int)s.Fig2Lower;
            _p1.Checked = s.PlotPixelsNoise;
            _p2.Checked = s.PlotDensityNoise;
            _p3.Checked = s.PlotDensityAll;
            _loading = false;
            Changed();
        }

        private StepchartSettings ReadSettings(out string error)
        {
            error = null;
            foreach (var nb in new[] { _zones, _step, _first, _fraction, _sat, _offset })
                if (double.IsNaN(nb.Value)) { error = "Поле «" + nb.Caption + "»: введите число."; return null; }
            var s = new StepchartSettings
            {
                Title = _title.Text.Trim(),
                ChartIndex = _chart.SelectedIndex,
                Zones = Math.Max(0, _zones.IntValue),
                DensityStep = _step.Value,
                FirstDensity = _first.Value,
                Detection = (ZoneDetection)_detect.SelectedIndex,
                ZoneFraction = Math.Max(10, Math.Min(100, _fraction.Value)) / 100.0,
                Detrend = (DetrendMode)_detrend.SelectedIndex,
                Saturation = _sat.Value > 0 ? _sat.Value : 255,
                PixelOffset = _offset.Value,
                Fig1Lower = (Fig1Lower)_f1.SelectedIndex,
                Fig2Middle = (Fig2Middle)_f2m.SelectedIndex,
                Fig2Lower = (Fig2Lower)_f2l.SelectedIndex,
                PlotPixelsNoise = _p1.Checked,
                PlotDensityNoise = _p2.Checked,
                PlotDensityAll = _p3.Checked
            };
            if (s.DensityStep <= 0) { error = "Шаг плотности должен быть положительным."; return null; }
            if (s.Zones == 1 || s.Zones > 100) { error = "Число зон: от 2 до 100 (или 0 — автоматически)."; return null; }
            return s;
        }

        private void Changed()
        {
            if (_loading) return;
            UpdateZones();
        }

        // ------------------------------------------------------------------ изображение и зоны

        private void SetImage(ImageData img)
        {
            _img = img;
            if (_bmp != null) _bmp.Dispose();
            _bmp = ImageIO.ToBitmap(img);
            _viewer.SetImage(_bmp, false);
            _imgLabel.Text = img.Name + " — " + img.Description;
            if (string.IsNullOrEmpty(_title.Text) || _titleAuto)
            {
                _settingTitle = true;
                _title.Text = img.Name;
                _settingTitle = false;
                _titleAuto = true;
            }
            Rectangle roi = Rectangle.Empty;
            if (!_lastRoi.IsEmpty && _lastSize == new Size(img.Width, img.Height))
            {
                if (Ui.Ask(this, "Использовать ту же область (ROI), что и для предыдущего изображения?\n(Do you want the same ROIs as the previous image?)"))
                    roi = _lastRoi;
            }
            _viewer.Roi = roi;
            UpdateZones();
        }

        private bool _titleAuto = true, _settingTitle;

        private void TakeActive(bool explicitClick)
        {
            var d = _main.Workspace.Active;
            if (d == null)
            {
                if (explicitClick) Ui.Info(this, "В главном окне нет активного изображения.");
                return;
            }
            SetImage(d.Data);
            if (!d.Roi.IsEmpty && _viewer.Roi.IsEmpty) _viewer.Roi = d.Roi;
        }

        private void OpenFile()
        {
            using (var dlg = new OpenFileDialog { Filter = ImageIO.OpenFilter, Title = "Изображение тестовой таблицы" })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir) && Directory.Exists(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var img = ImageIO.Load(dlg.FileName);
                    Settings.LastDir = Path.GetDirectoryName(dlg.FileName);
                    SetImage(img);
                }
                catch (Exception ex) { Ui.Error(this, "Не удалось открыть файл: " + ex.Message); }
            }
        }

        private void UpdateZones()
        {
            _viewer.Overlays.Clear();
            if (_img == null) { _status.Text = ""; _viewer.Invalidate(); return; }
            Rectangle roi = _viewer.Roi;
            if (roi.IsEmpty)
            {
                _status.Text = "Выделите мышью область таблицы со всеми градациями.";
                _viewer.Invalidate();
                return;
            }
            string err;
            var s = ReadSettings(out err);
            if (s == null) { _status.Text = err; return; }
            try
            {
                bool horizontal;
                int detected;
                List<Rectangle> zones = Stepchart.DetectZones(_img, roi, s, out horizontal, out detected);
                // нумерация от светлой зоны
                var first = Stats.Compute(_img, Stepchart.MeasureRect(zones[0], horizontal, s.ZoneFraction), -1);
                var last = Stats.Compute(_img, Stepchart.MeasureRect(zones[zones.Count - 1], horizontal, s.ZoneFraction), -1);
                bool rev = first.Mean < last.Mean;
                for (int i = 0; i < zones.Count; i++)
                {
                    int num = rev ? zones.Count - i : i + 1;
                    _viewer.Overlays.Add(new ViewerOverlay { Rect = zones[i], Color = Color.FromArgb(160, 160, 190), Dashed = true });
                    _viewer.Overlays.Add(new ViewerOverlay { Rect = Stepchart.MeasureRect(zones[i], horizontal, s.ZoneFraction), Color = Theme.Accent, FillAlpha = 70, Label = num.ToString() });
                }
                _status.Text = "Зон: " + zones.Count + (s.Detection == ZoneDetection.Automatic ? ", границ найдено по перепадам: " + (detected - 1) + " из " + (zones.Count - 1) : "") +
                               (horizontal ? ", таблица горизонтальная" : ", таблица вертикальная") + (rev ? ", светлая зона справа/снизу" : "") +
                               ".   Область: " + roi.Width + "×" + roi.Height + " из " + _img.Width + "×" + _img.Height;
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            _viewer.Invalidate();
        }

        // ------------------------------------------------------------------ анализ

        internal void TourSetRoi(Rectangle r) { _viewer.Roi = r; }
        internal StepchartResultsForm TourRun() { return Run(); }

        private StepchartResultsForm Run()
        {
            if (_img == null) { Ui.Info(this, "Откройте изображение тестовой таблицы."); return null; }
            if (_viewer.Roi.IsEmpty) { Ui.Info(this, "Выделите мышью область таблицы со всеми градациями."); return null; }
            string err;
            var s = ReadSettings(out err);
            if (s == null) { Ui.Error(this, err); return null; }
            if (!s.PlotPixelsNoise && !s.PlotDensityNoise && !s.PlotDensityAll) { Ui.Info(this, "Отметьте в поле Plot хотя бы один рисунок."); return null; }
            StepchartResult res = null;
            if (!Ui.Busy(this, () => res = Stepchart.Analyze(_img, _viewer.Roi, s))) return null;
            _lastRoi = _viewer.Roi;
            _lastSize = new Size(_img.Width, _img.Height);
            _lastSettings = s;
            var f = new StepchartResultsForm(res, _main);
            f.Show(Owner ?? this);
            return f;
        }
    }
}
