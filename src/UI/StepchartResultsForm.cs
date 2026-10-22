using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;
using SigmaLab.Core;
using SigmaLab.IO;

namespace SigmaLab.UI
{
    /// <summary>Результаты Stepchart: рисунки 1 и 2 (как в Imatest) и таблица по зонам.</summary>
    public sealed class StepchartResultsForm : Form
    {
        private readonly StepchartResult _r;
        private readonly TabControl _tabs;
        private readonly List<KeyValuePair<string, Figure>> _figures = new List<KeyValuePair<string, Figure>>();
        private readonly DataGridView _grid;

        private static readonly Color CR = Color.FromArgb(0xD0, 0x20, 0x20);
        private static readonly Color CG = Color.FromArgb(0x10, 0x90, 0x30);
        private static readonly Color CB = Color.FromArgb(0x20, 0x40, 0xD0);
        private static readonly Color CY = Color.FromArgb(0x20, 0x20, 0x20);
        private static readonly Color[] ChColors = { CR, CG, CB, CY };

        public StepchartResultsForm(StepchartResult r, MainForm main)
        {
            _r = r;
            Theme.ApplyForm(this);
            Text = "Stepchart — результаты: " + r.FileName;
            Size = new Size(Theme.S(1180), Theme.S(900));
            MinimumSize = new Size(Theme.S(800), Theme.S(600));
            StartPosition = FormStartPosition.CenterScreen;
            Theme.FitToScreen(this);

            _tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(Theme.S(12), Theme.S(4)) };
            if (r.Settings.PlotPixelsNoise) AddFigure("Рис. 1 — Pixels, noise", BuildFig1(r));
            if (r.Settings.PlotDensityNoise) AddFigure("Рис. 2 — Density, noise detail", BuildFig2(r));
            if (r.Settings.PlotDensityAll) AddFigure("Рис. 3 — Density, all channels + WB", BuildFig3(r));

            _grid = BuildTable(r);
            var tp = new TabPage("Таблица по зонам") { BackColor = Color.White };
            tp.Controls.Add(_grid);
            _tabs.TabPages.Add(tp);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(46), Padding = new Padding(Theme.S(6)), BackColor = Theme.SurfaceAlt, WrapContents = false };
            var bSave = Theme.MakeButton("Сохранить рисунок (TIFF)…", true);
            bSave.Click += (s, e) => SaveCurrent();
            var bAll = Theme.MakeButton("Сохранить всё в папку…");
            bAll.Click += (s, e) => SaveAll();
            var bCopy = Theme.MakeButton("Копировать рисунок");
            bCopy.Click += (s, e) => CopyCurrent();
            var bCsv = Theme.MakeButton("Таблица CSV…");
            bCsv.Click += (s, e) => Ui.SaveText(this, TableCsv(), PlotView.Safe("Stepchart " + r.FileName) + ".csv");
            bar.Controls.AddRange(new Control[] { bSave, bAll, bCopy, bCsv });

            Controls.Add(_tabs);
            Controls.Add(bar);
        }

        internal void TourTab(int i) { if (i < _tabs.TabPages.Count) _tabs.SelectedIndex = i; }

        private void AddFigure(string title, Figure f)
        {
            _figures.Add(new KeyValuePair<string, Figure>(title, f));
            var tp = new TabPage(title) { BackColor = Color.White };
            var pv = new PlotView { Dock = DockStyle.Fill, ExportName = "Stepchart " + title.Replace("Рис. ", "рис") + " " + _r.FileName };
            pv.Figure = f;
            tp.Controls.Add(pv);
            _tabs.TabPages.Add(tp);
        }

        private static string Fig1Unit(StepchartResult r) { return r.Settings.Fig1Lower == Fig1Lower.NoisePixels ? " пикс." : " %"; }

        // ------------------------------------------------------------------ рисунок 1

        private static Bitmap StripImage(StepchartResult r)
        {
            Bitmap b = ImageIO.ToBitmap(r.Crop);
            using (var g = Graphics.FromImage(b))
            using (var p = new Pen(Color.FromArgb(220, 255, 60, 60), Math.Max(1f, b.Width / 600f)))
            using (var f = Theme.SafeFont(Theme.SansNames, Math.Max(7f, Math.Min(b.Height * 0.25f, (r.Horizontal ? b.Width : b.Height) / (r.Zones.Count * 1.6f))), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var z in r.Zones)
                {
                    Rectangle m = z.Measure;
                    m.Offset(-r.Roi.X, -r.Roi.Y);
                    g.DrawRectangle(p, m);
                    string n = (z.Index + 1).ToString();
                    var c = z.Mean[3] > 110 ? Brushes.Black : Brushes.White;
                    g.DrawString(n, f, c, m.X + 1, m.Y + 1);
                }
            }
            return b;
        }

        private static double[] ZoneX(StepchartResult r)
        {
            var x = new double[r.Zones.Count];
            for (int i = 0; i < x.Length; i++) x[i] = i + 1;
            return x;
        }

        private static double[] LogExpX(StepchartResult r)
        {
            var x = new double[r.Zones.Count];
            for (int i = 0; i < x.Length; i++) x[i] = r.Zones[i].LogExposure;
            return x;
        }

        private static int[] Channels(StepchartResult r) { return r.IsColor ? new[] { 0, 1, 2, 3 } : new[] { 3 }; }

        private static string ChName(int ch) { return ch == 3 ? "Y (яркость)" : StepchartResult.ChannelNames[ch]; }

        private static Marker ChMarker(int ch) { return ch == 3 ? Marker.Square : (ch == 0 ? Marker.Circle : (ch == 1 ? Marker.Diamond : Marker.Triangle)); }

        public static Figure BuildFig1(StepchartResult r)
        {
            var fig = new Figure { Title = "Stepchart: " + r.FileName + " — 1. Pixels, noise", Size = new Size(1200, 920) };
            fig.Add(0, 0, 1, 0.13f, new Figure.ImagePanel { Image = StripImage(r), Caption = "Область анализа " + r.Roi.Width + "×" + r.Roi.Height + " из " + r.ImageWidth + "×" + r.ImageHeight + " пикс.; зоны пронумерованы от светлой" });

            double[] x = ZoneX(r);
            var pa = new Plot { Title = "Stepchart analysis — уровень сигнала по зонам", XLabel = "Зона (Zone)", YLabel = "Pixel level / " + r.Settings.Saturation };
            var y = new double[x.Length];
            var f1 = new double[x.Length];
            var f2 = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                var z = r.Zones[i];
                y[i] = z.Mean[3] / r.Settings.Saturation;
                double le = z.LogExposure;
                f1[i] = Math.Pow(10, r.GammaIntercept + r.Gamma * le);
                f2[i] = Math.Pow(10, r.Quad0 + r.Quad1 * le + r.Quad2 * le * le);
            }
            if (r.IsColor)
                for (int ch = 0; ch < 3; ch++)
                {
                    var yc = new double[x.Length];
                    for (int i = 0; i < x.Length; i++) yc[i] = r.Zones[i].Mean[ch] / r.Settings.Saturation;
                    var sc = pa.Add(StepchartResult.ChannelNames[ch], x, yc, ChColors[ch]);
                    sc.Width = 1f;
                }
            pa.Add("Y (измерено)", x, y, CY, Marker.Square).Width = 1.4f;
            var s1 = pa.Add("1-й порядок (γ)", x, f1, Theme.Primary);
            s1.Dash = DashStyle.Dash;
            var s2 = pa.Add("2-й порядок", x, f2, Theme.AccentDark);
            s2.Dash = DashStyle.Dot;
            pa.YMin = 0;
            pa.YMax = 1.05;
            pa.XMin = 0.5; pa.XMax = x.Length + 0.5;
            pa.Notes.Add("Гамма (Gamma) = " + Ui.F(r.Gamma, 3) + " = 1/" + Ui.F(1 / r.Gamma, 2));
            pa.Notes.Add("Зон: " + r.Zones.Count + "  (границ по перепадам: " + (r.DetectedZones - 1) + ")");
            pa.Notes.Add("Density step = " + Ui.F(r.Settings.DensityStep, 2) + ";  D первой зоны = " + Ui.F(r.Settings.FirstDensity, 2));
            pa.XFormat = "0";
            fig.Add(0, 0.13f, 1, 0.43f, new Figure.PlotPanel { Plot = pa });

            var pb = new Plot { Title = Fig1Title(r), XLabel = "Зона (Zone)", YLabel = Fig1YLabel(r) };
            foreach (int ch in Channels(r))
            {
                var yy = new double[x.Length];
                for (int i = 0; i < x.Length; i++) yy[i] = r.Fig1Noise(r.Zones[i], ch);
                var s = pb.Add(ChName(ch), x, yy, ChColors[ch], ChMarker(ch));
                s.Dash = ch == 3 ? DashStyle.Solid : DashStyle.Dot;
                s.Width = ch == 3 ? 1.8f : 1.2f;
            }
            pb.XMin = 0.5; pb.XMax = x.Length + 0.5;
            pb.XFormat = "0";
            if (r.Settings.Fig1Lower == Fig1Lower.NoiseNorm15)
                pb.Notes.Add("Нормировано на диапазон плотностей 1,5 (D = 0,1…1,6): " + Ui.F(r.Level01, 1) + " − " + Ui.F(r.Level16, 1) + " = " + Ui.F(r.Range15, 1) + " пикс.");
            pb.Notes.Add("Среднее (зоны " + r.AvgFrom + "–" + r.AvgTo + ")" + AvgText(r, r.AvgNoise, 2) + Fig1Unit(r));
            fig.Add(0, 0.56f, 1, 0.44f, new Figure.PlotPanel { Plot = pb });
            return fig;
        }

        private static string AvgText(StepchartResult r, double[] v, int dec)
        {
            if (!r.IsColor) return ": Y = " + Ui.F(v[3], dec);
            return " R, G, B, Y = " + Ui.F(v[0], dec) + "; " + Ui.F(v[1], dec) + "; " + Ui.F(v[2], dec) + "; " + Ui.F(v[3], dec);
        }

        private static string Fig1Title(StepchartResult r)
        {
            switch (r.Settings.Fig1Lower)
            {
                case Fig1Lower.NoiseNorm15: return "Pixel noise (%), нормированный на диапазон плотностей 1,5";
                case Fig1Lower.NoiseNorm255: return "Pixel noise (%), нормированный на 255";
                case Fig1Lower.NoiseNormWhiteBlack: return "Pixel noise (%), нормированный на разность белое−чёрное";
                default: return "Pixel noise (σ), уровни";
            }
        }

        private static string Fig1YLabel(StepchartResult r)
        {
            switch (r.Settings.Fig1Lower)
            {
                case Fig1Lower.NoiseNorm15: return "Шум, % от диапазона D 1,5";
                case Fig1Lower.NoiseNorm255: return "Шум, % от 255";
                case Fig1Lower.NoiseNormWhiteBlack: return "Шум, % от (белое−чёрное)";
                default: return "Шум σ, уровни";
            }
        }

        // ------------------------------------------------------------------ рисунок 2

        public static Figure BuildFig2(StepchartResult r)
        {
            var fig = new Figure { Title = "Stepchart: " + r.FileName + " — 2. Density, noise detail", Size = new Size(1200, 1000) };
            double[] x = LogExpX(r);

            // плотностная характеристика
            var pc = new Plot { Title = "Density response — тональный отклик", XLabel = "Log Exposure (−Target density)", YLabel = "lg(Pixel level / " + r.Settings.Saturation + ")" };
            var y = new double[x.Length];
            var fit = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                y[i] = r.Zones[i].Mean[3] > 0 ? Math.Log10(r.Zones[i].Mean[3] / r.Settings.Saturation) : double.NaN;
                fit[i] = r.GammaIntercept + r.Gamma * x[i];
            }
            pc.Add("Y (измерено)", x, y, CY, Marker.Square).Width = 1f;
            var sf = pc.Add("Аппроксимация γ", x, fit, Theme.Primary);
            sf.Dash = DashStyle.Dash;
            pc.Notes.Add("Gamma = " + Ui.F(r.Gamma, 3) + " = 1/" + Ui.F(1 / r.Gamma, 2));
            pc.Notes.Add("Total detected range = " + Ui.F(r.TotalRangeFStops, 2) + " f-stops (EV)");
            pc.Notes.Add("Density step = " + Ui.F(r.Settings.DensityStep, 2) + ", " + r.Zones.Count + " зон");
            fig.Add(0, 0, 0.66f, 0.33f, new Figure.PlotPanel { Plot = pc });

            // шум в f-stop или ОСШ
            bool snr = r.Settings.Fig2Middle == Fig2Middle.FStopSnr;
            var pd = new Plot
            {
                Title = snr ? "SNR (S/N) = 1/(шум в f-stop): R, G, B, Y" : "RMS noise: R, G, B, Y (яркость) в f-stop (EV)",
                XLabel = "Log Exposure (−Target density)",
                YLabel = snr ? "ОСШ (1/f-stop noise)" : "Шум, f-stop (EV)",
                LogY = true
            };
            foreach (int ch in Channels(r))
            {
                var yy = new double[x.Length];
                for (int i = 0; i < x.Length; i++)
                {
                    double v = r.Fig2Middle(r.Zones[i], ch);
                    yy[i] = r.Zones[i].Saturated || v <= 0 || v >= 99 ? double.NaN : v;
                }
                var s = pd.Add(ChName(ch), x, yy, ChColors[ch], ChMarker(ch));
                s.Dash = ch == 3 ? DashStyle.Solid : DashStyle.Dot;
                s.Width = ch == 3 ? 1.8f : 1.1f;
            }
            Color[] lc = { Color.FromArgb(0xE0, 0x40, 0xE0), Color.FromArgb(0xF0, 0x90, 0x20), Color.FromArgb(0xE0, 0xC0, 0x00), Color.FromArgb(0x20, 0xA0, 0x40) };
            string[] qn = { "Low", "Medium", "M-High", "High" };
            for (int k = 0; k < 4; k++)
            {
                double t = StepchartResult.DrThresholds[k];
                pd.Lines.Add(new RefLine { Value = snr ? 1 / t : t, Color = lc[k], Label = (snr ? "ОСШ " + (1 / t).ToString("0.##") : t.ToString("0.##") + " f-stop") + " (" + qn[k] + ")" });
            }
            pd.ShowLegend = true;
            fig.Add(0, 0.33f, 0.66f, 0.34f, new Figure.PlotPanel { Plot = pd });

            // шум в пикселях или ОСШ в дБ
            var pe = new Plot { Title = Fig2LowerTitle(r), XLabel = "Log Exposure (−Target density)", YLabel = Fig2LowerY(r) };
            var avg = new double[4];
            foreach (int ch in Channels(r))
            {
                var yy = new double[x.Length];
                double sum = 0;
                int n = 0;
                for (int i = 0; i < x.Length; i++)
                {
                    yy[i] = r.Fig2Lower(r.Zones[i], ch);
                    if (i + 1 >= r.AvgFrom && i + 1 <= r.AvgTo && !double.IsNaN(yy[i]) && !double.IsInfinity(yy[i])) { sum += yy[i]; n++; }
                }
                avg[ch] = n > 0 ? sum / n : double.NaN;
                var s = pe.Add(ChName(ch), x, yy, ChColors[ch], ChMarker(ch));
                s.Dash = ch == 3 ? DashStyle.Solid : DashStyle.Dot;
                s.Width = ch == 3 ? 1.8f : 1.1f;
            }
            pe.Notes.Add("Среднее (зоны " + r.AvgFrom + "–" + r.AvgTo + ")" + AvgText(r, avg, 2) + Fig2LowerUnit(r));
            fig.Add(0, 0.67f, 0.66f, 0.33f, new Figure.PlotPanel { Plot = pe });

            // таблица динамического диапазона
            var tp = new Figure.TextPanel { Title = "Динамический диапазон (Dynamic range), f-stop" };
            tp.Rows.Add(new KeyValuePair<string, string>("Полный (Total)", Ui.F(r.TotalRangeFStops, 2)));
            string[] q = { "Low", "Medium", "Med-High", "High" };
            for (int k = 0; k < 4; k++)
            {
                double t = StepchartResult.DrThresholds[k];
                string key = (snr ? "ОСШ ≥ " + (1 / t).ToString("0.##") : "шум ≤ " + t.ToString("0.##") + " f-stop") + " (" + q[k] + ")";
                tp.Rows.Add(new KeyValuePair<string, string>(key, Ui.F(r.DynamicRange[k], 2) + (r.DynamicRangeIsTotal[k] ? " (≥ Total)" : "")));
            }
            tp.Rows.Add(new KeyValuePair<string, string>("", null));
            tp.Rows.Add(new KeyValuePair<string, string>("Качество: Low — низкое, Medium — среднее, Med-High — выше среднего, High — высокое.", null));
            tp.Rows.Add(new KeyValuePair<string, string>("", null));
            tp.Rows.Add(new KeyValuePair<string, string>("1 f-stop = 0,301 единицы оптической плотности (D = f-stops · lg 2).", null));
            fig.Add(0.66f, 0, 0.34f, 0.26f, tp);

            var info = new Figure.TextPanel { Title = "Сведения" };
            info.Rows.Add(new KeyValuePair<string, string>("Файл", r.FileName));
            info.Rows.Add(new KeyValuePair<string, string>("Изображение", r.ImageWidth + "×" + r.ImageHeight + (r.IsColor ? ", RGB" : ", полутоновое")));
            info.Rows.Add(new KeyValuePair<string, string>("Область (crop)", r.Roi.Width + "×" + r.Roi.Height + " @ " + r.Roi.X + "," + r.Roi.Y));
            info.Rows.Add(new KeyValuePair<string, string>("Зон", r.Zones.Count.ToString()));
            info.Rows.Add(new KeyValuePair<string, string>("Гамма", Ui.F(r.Gamma, 3)));
            info.Rows.Add(new KeyValuePair<string, string>("Уровни D=0,1 / 1,6", Ui.F(r.Level01, 1) + " / " + Ui.F(r.Level16, 1)));
            info.Rows.Add(new KeyValuePair<string, string>("Белое − чёрное", Ui.F(r.WhiteBlack, 1)));
            info.Rows.Add(new KeyValuePair<string, string>("Дата анализа", DateTime.Now.ToString("dd.MM.yyyy HH:mm")));
            fig.Add(0.66f, 0.26f, 0.34f, 0.32f, info);

            var pf = new Plot { Title = "Noise spectrum — спектр шума (Y)", XLabel = "Частота, циклов/пиксель", YLabel = "Отн. мощность", ShowLegend = false };
            pf.Add("Спектр", r.SpectrumFreq, r.SpectrumPower, Theme.Primary);
            pf.XMin = 0; pf.XMax = 0.5; pf.YMin = 0; pf.YMax = 1.05;
            fig.Add(0.66f, 0.58f, 0.34f, 0.42f, new Figure.PlotPanel { Plot = pf });
            return fig;
        }

        // ------------------------------------------------------------------ рисунок 3

        public static Figure BuildFig3(StepchartResult r)
        {
            var fig = new Figure { Title = "Stepchart: " + r.FileName + " — 3. Density, all channels + White Balance", Size = new Size(1200, 900) };
            double[] x = LogExpX(r);
            var pa = new Plot { Title = "Density response — все каналы", XLabel = "Log Exposure (−Target density)", YLabel = "lg(Pixel level / " + r.Settings.Saturation + ")" };
            var gammas = new double[4];
            foreach (int ch in Channels(r))
            {
                var y = new double[x.Length];
                var fx = new List<double>();
                var fy = new List<double>();
                for (int i = 0; i < x.Length; i++)
                {
                    var z = r.Zones[i];
                    y[i] = z.Mean[ch] > 0 ? Math.Log10(z.Mean[ch] / r.Settings.Saturation) : double.NaN;
                    if (!z.Saturated && z.Mean[ch] > 0.5 && z.Density >= 0.09 && z.Density <= 0.91) { fx.Add(x[i]); fy.Add(y[i]); }
                }
                double a, b;
                Stepchart.LinFit(fx, fy, out a, out b);
                gammas[ch] = b;
                var s = pa.Add(ChName(ch), x, y, ChColors[ch], ChMarker(ch));
                s.Dash = ch == 3 ? DashStyle.Solid : DashStyle.Dot;
                s.Width = ch == 3 ? 1.8f : 1.2f;
            }
            pa.Notes.Add(r.IsColor
                ? "Гамма R, G, B, Y = " + Ui.F(gammas[0], 3) + "; " + Ui.F(gammas[1], 3) + "; " + Ui.F(gammas[2], 3) + "; " + Ui.F(gammas[3], 3)
                : "Гамма = " + Ui.F(gammas[3], 3));
            fig.Add(0, 0, 1, 0.5f, new Figure.PlotPanel { Plot = pa });

            var pb = new Plot { Title = "White balance — отклонение от нейтрального (CIELAB, D65)", XLabel = "Log Exposure (−Target density)", YLabel = "a*, b*, ΔC*ab" };
            var dc = new double[x.Length];
            var aa = new double[x.Length];
            var bb = new double[x.Length];
            double sum = 0;
            int cnt = 0;
            for (int i = 0; i < x.Length; i++)
            {
                var z = r.Zones[i];
                dc[i] = z.DeltaC; aa[i] = z.LabA; bb[i] = z.LabB;
                if (i + 1 >= r.AvgFrom && i + 1 <= r.AvgTo) { sum += z.DeltaC; cnt++; }
            }
            pb.Add("ΔC*ab", x, dc, CY, Marker.Square).Width = 1.8f;
            var sa = pb.Add("a* (зелёный − / красный +)", x, aa, CR, Marker.Circle);
            sa.Dash = DashStyle.Dot;
            var sb2 = pb.Add("b* (синий − / жёлтый +)", x, bb, CB, Marker.Triangle);
            sb2.Dash = DashStyle.Dot;
            pb.Lines.Add(new RefLine { Value = 0, Color = Theme.Muted });
            pb.Notes.Add("Среднее ΔC*ab (зоны " + r.AvgFrom + "–" + r.AvgTo + ") = " + Ui.F(cnt > 0 ? sum / cnt : double.NaN, 2));
            fig.Add(0, 0.5f, 0.66f, 0.5f, new Figure.PlotPanel { Plot = pb });

            var tp = new Figure.TextPanel { Title = "Баланс белого (White Balance)" };
            tp.Rows.Add(new KeyValuePair<string, string>("ΔC*ab — расстояние от цвета зоны до нейтрального серого той же светлоты в пространстве CIELAB.", null));
            tp.Rows.Add(new KeyValuePair<string, string>("", null));
            tp.Rows.Add(new KeyValuePair<string, string>("a* > 0 — сдвиг в красный, a* < 0 — в зелёный.", null));
            tp.Rows.Add(new KeyValuePair<string, string>("b* > 0 — сдвиг в жёлтый, b* < 0 — в синий.", null));
            tp.Rows.Add(new KeyValuePair<string, string>("", null));
            tp.Rows.Add(new KeyValuePair<string, string>("Изображение", r.IsColor ? "RGB" : "полутоновое (ΔC = 0)"));
            fig.Add(0.66f, 0.5f, 0.34f, 0.5f, tp);
            return fig;
        }

        private static string Fig2LowerTitle(StepchartResult r)
        {
            switch (r.Settings.Fig2Lower)
            {
                case Fig2Lower.Noise255: return "Pixel noise — шум, нормированный к макс. уровню (255), %";
                case Fig2Lower.PixelSnrDb: return "Pixel SNR (dB) = 20·lg(S/N)";
                case Fig2Lower.PixelSnrRatio: return "Pixel SNR = S/N, раз";
                default: return "Pixel noise σ, уровни";
            }
        }

        private static string Fig2LowerY(StepchartResult r)
        {
            switch (r.Settings.Fig2Lower)
            {
                case Fig2Lower.Noise255: return "Шум, % от 255";
                case Fig2Lower.PixelSnrDb: return "ОСШ, дБ";
                case Fig2Lower.PixelSnrRatio: return "ОСШ, раз";
                default: return "Шум σ, уровни";
            }
        }

        private static string Fig2LowerUnit(StepchartResult r)
        {
            switch (r.Settings.Fig2Lower)
            {
                case Fig2Lower.Noise255: return " %";
                case Fig2Lower.PixelSnrDb: return " дБ";
                case Fig2Lower.PixelSnrRatio: return "";
                default: return " уровней";
            }
        }

        // ------------------------------------------------------------------ таблица

        private static readonly string[] Cols =
        {
            "Зона", "D", "lg E (−D)", "EV (f-stop)", "R", "G", "B", "Y", "σR", "σG", "σB", "σY",
            "Шум Y (рис. 1)", "Шум Y, % от 255", "Шум Y, f-stop", "ОСШ Y (1/f-stop)", "ОСШ Y, раз", "ОСШ Y, дБ", "ΔC*ab", "Насыщение"
        };

        private DataGridView BuildTable(StepchartResult r)
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
                EnableHeadersVisualStyles = false
            };
            g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
            g.ColumnHeadersDefaultCellStyle.Font = Theme.BoldFont;
            g.AlternatingRowsDefaultCellStyle.BackColor = Theme.SurfaceAlt;
            foreach (string c in Cols) g.Columns.Add(c, c);
            foreach (var row in TableRows(r)) g.Rows.Add(row);
            return g;
        }

        private static List<object[]> TableRows(StepchartResult r)
        {
            var list = new List<object[]>();
            foreach (var z in r.Zones)
            {
                list.Add(new object[]
                {
                    z.Index + 1, Ui.F(z.Density, 2), Ui.F(z.LogExposure, 2), Ui.F(z.Ev, 2),
                    Ui.F(z.Mean[0], 2), Ui.F(z.Mean[1], 2), Ui.F(z.Mean[2], 2), Ui.F(z.Mean[3], 2),
                    Ui.F(z.Noise[0], 3), Ui.F(z.Noise[1], 3), Ui.F(z.Noise[2], 3), Ui.F(z.Noise[3], 3),
                    Ui.F(r.Fig1Noise(z, 3), 3), Ui.F(100 * z.Noise[3] / r.Settings.Saturation, 3),
                    Ui.F(z.FStopNoise[3], 4), Ui.F(z.FStopSnr(3), 2), Ui.F(z.SnrRatio(3), 2), Ui.F(z.SnrDb(3), 2),
                    Ui.F(z.DeltaC, 2), z.Saturated ? "да" : ""
                });
            }
            return list;
        }

        private string TableCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Stepchart;" + Csv.Esc(_r.FileName));
            sb.AppendLine("Гамма;" + Csv.Num(_r.Gamma));
            sb.AppendLine("Полный диапазон, f-stop;" + Csv.Num(_r.TotalRangeFStops));
            for (int k = 0; k < 4; k++) sb.AppendLine("Дин. диапазон при шуме " + Csv.Num(StepchartResult.DrThresholds[k]) + " f-stop;" + Csv.Num(_r.DynamicRange[k]));
            sb.AppendLine();
            sb.AppendLine(string.Join(";", Cols));
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var cells = new string[row.Cells.Count];
                for (int i = 0; i < cells.Length; i++) cells[i] = Csv.Esc(Convert.ToString(row.Cells[i].Value));
                sb.AppendLine(string.Join(";", cells));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ сохранение

        private Figure CurrentFigure()
        {
            int i = _tabs.SelectedIndex;
            if (i >= 0 && i < _figures.Count) return _figures[i].Value;
            return _figures.Count > 0 ? _figures[0].Value : null;
        }

        private void SaveCurrent()
        {
            var f = CurrentFigure();
            if (f == null) return;
            int i = Math.Max(0, Math.Min(_figures.Count - 1, _tabs.SelectedIndex));
            using (var dlg = new SaveFileDialog { Filter = ImageIO.BitmapSaveFilter, FileName = PlotView.Safe("Stepchart " + _r.FileName + " рис" + (i + 1)), Title = "Сохранить рисунок" })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string path = ImageIO.EnsureExtension(dlg.FileName, dlg.FilterIndex);
                Settings.LastDir = Path.GetDirectoryName(path);
                Ui.Busy(this, () => { using (var b = f.Render()) ImageIO.SaveBitmap(b, path); });
            }
        }

        private void SaveAll()
        {
            using (var fb = new FolderBrowserDialog { Description = "Папка для рисунков (TIFF без сжатия) и таблицы (CSV)" })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir) && Directory.Exists(Settings.LastDir)) fb.SelectedPath = Settings.LastDir;
                if (fb.ShowDialog(this) != DialogResult.OK) return;
                Ui.Busy(this, () =>
                {
                    string baseName = PlotView.Safe("Stepchart " + _r.FileName);
                    for (int i = 0; i < _figures.Count; i++)
                        using (var b = _figures[i].Value.Render())
                            ImageIO.SaveBitmap(b, Path.Combine(fb.SelectedPath, baseName + " рис" + (i + 1) + ".tif"));
                    File.WriteAllText(Path.Combine(fb.SelectedPath, baseName + ".csv"), TableCsv(), new UTF8Encoding(true));
                    Settings.LastDir = fb.SelectedPath;
                });
                Ui.Info(this, "Сохранено в папку:\n" + fb.SelectedPath);
            }
        }

        private void CopyCurrent()
        {
            var f = CurrentFigure();
            if (f == null) return;
            using (var b = f.Render()) Clipboard.SetImage(b);
        }
    }
}
