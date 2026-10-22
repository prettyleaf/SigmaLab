using System;
using System.Collections.Generic;
using System.Drawing;

namespace SigmaLab.Core
{
    public enum ZoneDetection { Automatic, Equal }
    public enum DetrendMode { None, Plane, Quadratic }

    /// <summary>Results (Fig. 1), Lower plot: Pixel noise.</summary>
    public enum Fig1Lower { NoiseNorm15, NoiseNorm255, NoiseNormWhiteBlack, NoisePixels }

    /// <summary>Results (Fig. 2), Middle plot: f-stop (EV) noise or SNR.</summary>
    public enum Fig2Middle { FStopNoise, FStopSnr }

    /// <summary>Results (Fig. 2), Lower plot: Pixel noise or SNR.</summary>
    public enum Fig2Lower { Noise255, PixelSnrDb, NoisePixels, PixelSnrRatio }

    public sealed class ChartType
    {
        public string Title;
        public double Step;
        public double First;
        public int Zones;
        public override string ToString() { return Title; }
    }

    public sealed class StepchartSettings
    {
        public string Title = "";
        public int ChartIndex;
        public int Zones = 20;              // 0 — определить автоматически
        public double DensityStep = 0.10;
        public double FirstDensity = 0.05;
        public ZoneDetection Detection = ZoneDetection.Equal;
        public double ZoneFraction = 0.6;   // доля зоны (по длине и ширине), по которой ведётся измерение
        public DetrendMode Detrend = DetrendMode.Quadratic;
        public double Saturation = 255;
        public double PixelOffset = 0;
        // значения по умолчанию — как в окне настроек Imatest Stepchart
        public Fig1Lower Fig1Lower = Fig1Lower.NoiseNorm255;
        public Fig2Middle Fig2Middle = Fig2Middle.FStopSnr;
        public Fig2Lower Fig2Lower = Fig2Lower.PixelSnrDb;
        public bool PlotPixelsNoise = true;
        public bool PlotDensityNoise = true;
        public bool PlotDensityAll = true;

        public static readonly ChartType[] Charts =
        {
            new ChartType { Title = "Density step = 0.10 REFLECTIVE (Kodak Q-13, Q-14, …)", Step = 0.10, First = 0.05, Zones = 20 },
            new ChartType { Title = "Density step = 0.10 — таблица СигмаЛаб (20 градаций, D от 0)", Step = 0.10, First = 0.00, Zones = 20 },
            new ChartType { Title = "Density step = 0.15 TRANSMISSIVE (Stouffer T4110)", Step = 0.15, First = 0.05, Zones = 41 },
            new ChartType { Title = "Density step = 0.10 TRANSMISSIVE (Stouffer T2115)", Step = 0.10, First = 0.05, Zones = 21 },
            new ChartType { Title = "Density step = 0.05 TRANSMISSIVE (Stouffer T3110)", Step = 0.05, First = 0.05, Zones = 31 },
            new ChartType { Title = "Другая (задать шаг вручную)", Step = 0.10, First = 0.0, Zones = 20 },
        };

        public StepchartSettings Clone() { return (StepchartSettings)MemberwiseClone(); }

    }

    public sealed class StepZone
    {
        public int Index;            // 0 — самая светлая
        public double Density;
        public Rectangle Bounds;     // границы зоны в изображении
        public Rectangle Measure;    // область измерения (центральная часть зоны)
        public readonly double[] Mean = new double[4];       // R, G, B, Y
        public readonly double[] Noise = new double[4];      // СКО, пиксели
        public readonly double[] Slope = new double[4];      // d(уровень)/d(f-stop)
        public readonly double[] FStopNoise = new double[4]; // шум в f-stop
        public bool Saturated;
        /// <summary>Координаты CIELAB среднего цвета зоны (sRGB, D65) и отклонение от нейтрального ΔC*ab.</summary>
        public double LabL, LabA, LabB;
        public double DeltaC { get { return Math.Sqrt(LabA * LabA + LabB * LabB); } }
        internal float[] ResidualY;
        internal int MeasureW, MeasureH;

        public double LogExposure { get { return -Density; } }
        public double Ev { get { return -Density / Stepchart.Log2; } }

        public double SnrRatio(int ch) { return Noise[ch] > 0 ? Mean[ch] / Noise[ch] : double.PositiveInfinity; }
        public double SnrDb(int ch) { return Noise[ch] > 0 && Mean[ch] > 0 ? 20 * Math.Log10(Mean[ch] / Noise[ch]) : double.NaN; }
        public double FStopSnr(int ch) { return FStopNoise[ch] > 0 ? 1.0 / FStopNoise[ch] : double.PositiveInfinity; }
    }

    public sealed class StepchartResult
    {
        public StepchartSettings Settings;
        public string FileName;
        public int ImageWidth, ImageHeight;
        public Rectangle Roi;
        public bool Horizontal;
        public bool Reversed;
        public List<StepZone> Zones = new List<StepZone>();
        public int DetectedZones;
        public double Gamma, GammaIntercept;
        public double Quad0, Quad1, Quad2;
        public double Range15;           // разность уровней для плотностей 0.1 и 1.6
        public double Level01, Level16;
        public double WhiteBlack;
        public double TotalRangeFStops;
        public static readonly double[] DrThresholds = { 1.0, 0.5, 0.25, 0.1 };
        public static readonly string[] DrNames = { "Низкое (Low)", "Среднее (Medium)", "Выше среднего (Med-High)", "Высокое (High)" };
        public double[] DynamicRange = new double[4];
        public bool[] DynamicRangeIsTotal = new bool[4];
        public double[] SpectrumFreq = new double[0];
        public double[] SpectrumPower = new double[0];
        public int AvgFrom, AvgTo;              // номера зон (с 1) для усреднения
        public double[] AvgNoise = new double[4];
        public double[] AvgSnrDb = new double[4];
        public ImageData Crop;
        public bool IsColor;

        public static readonly string[] ChannelNames = { "R", "G", "B", "Y" };

        /// <summary>Шум зоны в единицах, выбранных для нижнего графика рис. 1.</summary>
        public double Fig1Noise(StepZone z, int ch)
        {
            switch (Settings.Fig1Lower)
            {
                case Fig1Lower.NoiseNorm15: return Range15 > 0 ? 100 * z.Noise[ch] / Range15 : double.NaN;
                case Fig1Lower.NoiseNorm255: return 100 * z.Noise[ch] / Settings.Saturation;
                case Fig1Lower.NoiseNormWhiteBlack: return WhiteBlack > 0 ? 100 * z.Noise[ch] / WhiteBlack : double.NaN;
                default: return z.Noise[ch];
            }
        }

        public double Fig2Lower(StepZone z, int ch)
        {
            switch (Settings.Fig2Lower)
            {
                case SigmaLab.Core.Fig2Lower.Noise255: return 100 * z.Noise[ch] / Settings.Saturation;
                case SigmaLab.Core.Fig2Lower.PixelSnrDb: return z.SnrDb(ch);
                case SigmaLab.Core.Fig2Lower.PixelSnrRatio: return z.SnrRatio(ch);
                default: return z.Noise[ch];
            }
        }

        public double Fig2Middle(StepZone z, int ch)
        {
            return Settings.Fig2Middle == SigmaLab.Core.Fig2Middle.FStopNoise ? z.FStopNoise[ch] : z.FStopSnr(ch);
        }
    }

    /// <summary>Аналог модуля Stepchart программы Imatest.</summary>
    public static class Stepchart
    {
        public const double Log2 = 0.30102999566398120; // lg 2: 1 f-stop = 0,301 единицы плотности

        public static double LumaY(double r, double g, double b) { return 0.3 * r + 0.59 * g + 0.11 * b; }

        /// <summary>sRGB (0…1) → CIELAB, опорный белый D65.</summary>
        public static void ToLab(double r, double g, double b, out double L, out double A, out double B)
        {
            Func<double, double> lin = v =>
            {
                v = Math.Max(0, Math.Min(1, v));
                return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            };
            double R = lin(r), G = lin(g), Bl = lin(b);
            double X = (0.4124 * R + 0.3576 * G + 0.1805 * Bl) / 0.95047;
            double Y = 0.2126 * R + 0.7152 * G + 0.0722 * Bl;
            double Z = (0.0193 * R + 0.1192 * G + 0.9505 * Bl) / 1.08883;
            Func<double, double> f = t => t > 0.008856 ? Math.Pow(t, 1.0 / 3) : 7.787 * t + 16.0 / 116;
            double fx = f(X), fy = f(Y), fz = f(Z);
            L = 116 * fy - 16;
            A = 500 * (fx - fy);
            B = 200 * (fy - fz);
        }

        // ------------------------------------------------------------------ поиск зон

        public static List<Rectangle> DetectZones(ImageData img, Rectangle roi, StepchartSettings s, out bool horizontal, out int detected)
        {
            roi = Stats.Normalize(img, roi);
            horizontal = roi.Width >= roi.Height;
            int L = horizontal ? roi.Width : roi.Height;
            int S = horizontal ? roi.Height : roi.Width;
            if (L < 8) throw new ArgumentException("Область слишком мала для анализа.");

            // профиль вдоль длинной стороны по центральным 60% короткой
            int s0 = (int)(S * 0.2), s1 = Math.Max(s0 + 1, (int)Math.Ceiling(S * 0.8));
            var prof = new double[L];
            for (int t = 0; t < L; t++)
            {
                double acc = 0;
                for (int q = s0; q < s1; q++)
                {
                    int x = horizontal ? roi.X + t : roi.X + q;
                    int y = horizontal ? roi.Y + q : roi.Y + t;
                    acc += PixelY(img, y * img.Width + x);
                }
                prof[t] = acc / (s1 - s0);
            }
            double[] sm = BoxSmooth(prof, Math.Max(1, L / 400));
            var grad = new double[L];
            for (int t = 1; t < L - 1; t++) grad[t] = Math.Abs(sm[t + 1] - sm[t - 1]) / 2;

            // порог значимости перепада: по медиане модуля градиента (оценка шума профиля)
            var sorted = (double[])grad.Clone();
            Array.Sort(sorted);
            double med = sorted[sorted.Length / 2];
            double gmax = sorted[sorted.Length - 1];
            double thr = Math.Max(4 * med, 0.02 * gmax);

            int N = s.Zones;
            if (N <= 0)
            {
                // автоматическое число зон: локальные максимумы градиента выше порога
                int minSep = Math.Max(3, L / 80);
                var peaks = FindPeaks(grad, thr, minSep);
                N = Math.Max(2, Math.Min(64, peaks.Count + 1));
            }
            if (L < 2 * N) throw new ArgumentException("Область слишком мала для " + N + " зон.");

            var b = new double[N + 1];
            for (int k = 0; k <= N; k++) b[k] = (double)k * L / N;
            detected = 1;
            if (s.Detection == ZoneDetection.Automatic)
            {
                double zw = (double)L / N;
                for (int k = 1; k < N; k++)
                {
                    int a0 = Math.Max(1, (int)(b[k] - 0.35 * zw)), a1 = Math.Min(L - 2, (int)(b[k] + 0.35 * zw));
                    int best = -1;
                    double bv = 0;
                    for (int t = a0; t <= a1; t++) if (grad[t] > bv) { bv = grad[t]; best = t; }
                    if (best >= 0 && bv > thr)
                    {
                        b[k] = best + 0.5;
                        detected++;
                    }
                }
                // монотонность границ
                for (int k = 1; k <= N; k++) if (b[k] < b[k - 1] + 2) b[k] = Math.Min(L, b[k - 1] + 2);
            }
            else detected = N;

            var zones = new List<Rectangle>();
            for (int k = 0; k < N; k++)
            {
                int t0 = (int)Math.Round(b[k]), t1 = (int)Math.Round(b[k + 1]);
                if (t1 <= t0) t1 = t0 + 1;
                zones.Add(horizontal
                    ? new Rectangle(roi.X + t0, roi.Y, t1 - t0, roi.Height)
                    : new Rectangle(roi.X, roi.Y + t0, roi.Width, t1 - t0));
            }
            return zones;
        }

        private static List<int> FindPeaks(double[] g, double thr, int minSep)
        {
            var idx = new List<int>();
            for (int t = 1; t < g.Length - 1; t++)
                if (g[t] > thr && g[t] >= g[t - 1] && g[t] >= g[t + 1]) idx.Add(t);
            // подавление соседних максимумов
            idx.Sort((p, q) => g[q].CompareTo(g[p]));
            var keep = new List<int>();
            foreach (int t in idx)
            {
                bool ok = true;
                foreach (int k in keep) if (Math.Abs(k - t) < minSep) { ok = false; break; }
                if (ok) keep.Add(t);
            }
            keep.Sort();
            return keep;
        }

        private static double[] BoxSmooth(double[] p, int r)
        {
            if (r <= 0) return (double[])p.Clone();
            var o = new double[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                double s = 0;
                int n = 0;
                for (int k = i - r; k <= i + r; k++)
                    if (k >= 0 && k < p.Length) { s += p[k]; n++; }
                o[i] = s / n;
            }
            return o;
        }

        private static double PixelY(ImageData img, int i)
        {
            if (!img.IsRgb) return img.Planes[0][i];
            return LumaY(img.Planes[0][i], img.Planes[1][i], img.Planes[2][i]);
        }

        public static Rectangle MeasureRect(Rectangle zone, bool horizontal, double fraction)
        {
            fraction = Math.Max(0.1, Math.Min(1.0, fraction));
            int w = Math.Max(1, (int)Math.Round(zone.Width * fraction));
            int h = Math.Max(1, (int)Math.Round(zone.Height * fraction));
            return new Rectangle(zone.X + (zone.Width - w) / 2, zone.Y + (zone.Height - h) / 2, w, h);
        }

        // ------------------------------------------------------------------ анализ

        public static StepchartResult Analyze(ImageData img, Rectangle roi, StepchartSettings s)
        {
            roi = Stats.Normalize(img, roi);
            bool horizontal;
            int detected;
            List<Rectangle> rects = DetectZones(img, roi, s, out horizontal, out detected);
            var res = new StepchartResult
            {
                Settings = s.Clone(),
                FileName = string.IsNullOrEmpty(s.Title) ? img.Name : s.Title,
                ImageWidth = img.Width,
                ImageHeight = img.Height,
                Roi = roi,
                Horizontal = horizontal,
                DetectedZones = detected,
                IsColor = img.IsRgb,
                Crop = ImageOps.Crop(img, roi)
            };

            foreach (Rectangle zr in rects)
            {
                var z = new StepZone { Bounds = zr, Measure = MeasureRect(zr, horizontal, s.ZoneFraction) };
                MeasureZone(img, z, s, horizontal);
                res.Zones.Add(z);
            }
            // зона 1 — самая светлая
            if (res.Zones.Count > 1 && res.Zones[0].Mean[3] < res.Zones[res.Zones.Count - 1].Mean[3])
            {
                res.Zones.Reverse();
                res.Reversed = true;
            }
            for (int i = 0; i < res.Zones.Count; i++)
            {
                var z = res.Zones[i];
                z.Index = i;
                z.Density = s.FirstDensity + i * s.DensityStep;
                z.Saturated = z.Mean[3] >= 0.98 * s.Saturation;
                ToLab(z.Mean[0] / s.Saturation, z.Mean[1] / s.Saturation, z.Mean[2] / s.Saturation, out z.LabL, out z.LabA, out z.LabB);
            }

            FitGamma(res);
            ComputeRanges(res);
            ComputeFStopNoise(res);
            ComputeDynamicRange(res);
            ComputeAverages(res);
            ComputeSpectrum(res);
            return res;
        }

        private static void MeasureZone(ImageData img, StepZone z, StepchartSettings s, bool horizontal)
        {
            Rectangle m = z.Measure;
            int n = m.Width * m.Height;
            var vals = new double[4][];
            for (int c = 0; c < 4; c++) vals[c] = new double[n];
            int k = 0;
            for (int y = m.Top; y < m.Bottom; y++)
                for (int x = m.Left; x < m.Right; x++)
                {
                    int i = y * img.Width + x;
                    double r, g, b;
                    if (img.IsRgb) { r = img.Planes[0][i]; g = img.Planes[1][i]; b = img.Planes[2][i]; }
                    else r = g = b = img.Planes[0][i];
                    r -= s.PixelOffset; g -= s.PixelOffset; b -= s.PixelOffset;
                    vals[0][k] = r; vals[1][k] = g; vals[2][k] = b; vals[3][k] = LumaY(r, g, b);
                    k++;
                }
            for (int c = 0; c < 4; c++)
            {
                double mean = 0;
                foreach (double v in vals[c]) mean += v;
                mean /= n;
                double[] resid = Detrend(vals[c], m.Width, m.Height, s.Detrend);
                double ss = 0;
                foreach (double v in resid) ss += v * v;
                z.Mean[c] = mean;
                z.Noise[c] = n > 1 ? Math.Sqrt(ss / (n - 1)) : 0;
                if (c == 3)
                {
                    // остаток вдоль длинной оси таблицы — для спектра шума
                    z.ResidualY = new float[n];
                    if (horizontal)
                    {
                        for (int i = 0; i < n; i++) z.ResidualY[i] = (float)resid[i];
                        z.MeasureW = m.Width; z.MeasureH = m.Height;
                    }
                    else
                    {
                        // транспонируем, чтобы строки шли вдоль таблицы
                        for (int y = 0; y < m.Height; y++)
                            for (int x = 0; x < m.Width; x++) z.ResidualY[x * m.Height + y] = (float)resid[y * m.Width + x];
                        z.MeasureW = m.Height; z.MeasureH = m.Width;
                    }
                }
            }
        }

        /// <summary>Остаток после вычитания полинома 0-го, 1-го или 2-го порядка по координатам (x, y).</summary>
        public static double[] Detrend(double[] v, int w, int h, DetrendMode mode)
        {
            int n = v.Length;
            int nb = mode == DetrendMode.None ? 1 : (mode == DetrendMode.Plane ? 3 : 6);
            if (n <= nb * 2) nb = 1;
            var ata = new double[nb, nb];
            var atb = new double[nb];
            var basis = new double[nb];
            double cx = (w - 1) / 2.0, cy = (h - 1) / 2.0, sx = Math.Max(1, w / 2.0), sy = Math.Max(1, h / 2.0);
            for (int y = 0, i = 0; y < h; y++)
                for (int x = 0; x < w; x++, i++)
                {
                    Basis((x - cx) / sx, (y - cy) / sy, basis, nb);
                    for (int p = 0; p < nb; p++)
                    {
                        atb[p] += basis[p] * v[i];
                        for (int q = 0; q < nb; q++) ata[p, q] += basis[p] * basis[q];
                    }
                }
            double[] coef = Solve(ata, atb, nb);
            var r = new double[n];
            for (int y = 0, i = 0; y < h; y++)
                for (int x = 0; x < w; x++, i++)
                {
                    Basis((x - cx) / sx, (y - cy) / sy, basis, nb);
                    double f = 0;
                    for (int p = 0; p < nb; p++) f += coef[p] * basis[p];
                    r[i] = v[i] - f;
                }
            return r;
        }

        private static void Basis(double x, double y, double[] b, int nb)
        {
            b[0] = 1;
            if (nb >= 3) { b[1] = x; b[2] = y; }
            if (nb >= 6) { b[3] = x * x; b[4] = x * y; b[5] = y * y; }
        }

        /// <summary>Решение системы методом Гаусса с выбором главного элемента.</summary>
        internal static double[] Solve(double[,] a, double[] b, int n)
        {
            var m = (double[,])a.Clone();
            var r = (double[])b.Clone();
            for (int col = 0; col < n; col++)
            {
                int piv = col;
                for (int row = col + 1; row < n; row++) if (Math.Abs(m[row, col]) > Math.Abs(m[piv, col])) piv = row;
                if (Math.Abs(m[piv, col]) < 1e-12) continue;
                if (piv != col)
                {
                    for (int k = 0; k < n; k++) { double t = m[col, k]; m[col, k] = m[piv, k]; m[piv, k] = t; }
                    double tb = r[col]; r[col] = r[piv]; r[piv] = tb;
                }
                for (int row = 0; row < n; row++)
                {
                    if (row == col) continue;
                    double f = m[row, col] / m[col, col];
                    if (f == 0) continue;
                    for (int k = col; k < n; k++) m[row, k] -= f * m[col, k];
                    r[row] -= f * r[col];
                }
            }
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = Math.Abs(m[i, i]) < 1e-12 ? 0 : r[i] / m[i, i];
            return x;
        }

        private static bool UsableForFit(StepZone z, StepchartSettings s)
        {
            return !z.Saturated && z.Mean[3] > 0.5;
        }

        private static void FitGamma(StepchartResult res)
        {
            var s = res.Settings;
            var xs = new List<double>();
            var ys = new List<double>();
            // гамма — средний наклон lg(уровня) от lg(экспозиции) для светлых и средних зон (D ≈ 0.1…0.9)
            foreach (var z in res.Zones)
                if (UsableForFit(z, s) && z.Density >= 0.09 && z.Density <= 0.91)
                {
                    xs.Add(z.LogExposure);
                    ys.Add(Math.Log10(z.Mean[3] / s.Saturation));
                }
            if (xs.Count < 3)
            {
                xs.Clear(); ys.Clear();
                foreach (var z in res.Zones)
                    if (UsableForFit(z, s)) { xs.Add(z.LogExposure); ys.Add(Math.Log10(z.Mean[3] / s.Saturation)); }
            }
            double a, b;
            LinFit(xs, ys, out a, out b);
            res.GammaIntercept = a;
            res.Gamma = b;

            xs.Clear(); ys.Clear();
            foreach (var z in res.Zones)
                if (UsableForFit(z, s)) { xs.Add(z.LogExposure); ys.Add(Math.Log10(z.Mean[3] / s.Saturation)); }
            double[] q = PolyFit(xs, ys, 2);
            res.Quad0 = q[0]; res.Quad1 = q[1]; res.Quad2 = q[2];
        }

        public static void LinFit(IList<double> x, IList<double> y, out double a, out double b)
        {
            int n = x.Count;
            if (n == 0) { a = b = 0; return; }
            if (n == 1) { a = y[0]; b = 0; return; }
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            for (int i = 0; i < n; i++) { sx += x[i]; sy += y[i]; sxx += x[i] * x[i]; sxy += x[i] * y[i]; }
            double d = n * sxx - sx * sx;
            b = Math.Abs(d) < 1e-15 ? 0 : (n * sxy - sx * sy) / d;
            a = (sy - b * sx) / n;
        }

        public static double[] PolyFit(IList<double> x, IList<double> y, int order)
        {
            int nb = order + 1;
            var res = new double[nb];
            if (x.Count < nb)
            {
                double a, b;
                LinFit(x, y, out a, out b);
                res[0] = a;
                if (nb > 1) res[1] = b;
                return res;
            }
            var ata = new double[nb, nb];
            var atb = new double[nb];
            for (int i = 0; i < x.Count; i++)
            {
                double xp = 1;
                var pw = new double[nb];
                for (int p = 0; p < nb; p++) { pw[p] = xp; xp *= x[i]; }
                for (int p = 0; p < nb; p++)
                {
                    atb[p] += pw[p] * y[i];
                    for (int q = 0; q < nb; q++) ata[p, q] += pw[p] * pw[q];
                }
            }
            return Solve(ata, atb, nb);
        }

        /// <summary>Уровень яркости Y, интерполированный для заданной плотности.</summary>
        public static double LevelAtDensity(StepchartResult res, double d, int ch = 3)
        {
            var zs = res.Zones;
            if (zs.Count == 0) return double.NaN;
            if (d <= zs[0].Density) return zs[0].Mean[ch];
            for (int i = 1; i < zs.Count; i++)
                if (d <= zs[i].Density)
                {
                    double t = (d - zs[i - 1].Density) / (zs[i].Density - zs[i - 1].Density);
                    return zs[i - 1].Mean[ch] + t * (zs[i].Mean[ch] - zs[i - 1].Mean[ch]);
                }
            return zs[zs.Count - 1].Mean[ch];
        }

        private static void ComputeRanges(StepchartResult res)
        {
            res.Level01 = LevelAtDensity(res, 0.1);
            res.Level16 = LevelAtDensity(res, 1.6);
            res.Range15 = res.Level01 - res.Level16;
            res.WhiteBlack = res.Zones[0].Mean[3] - res.Zones[res.Zones.Count - 1].Mean[3];
            if (res.Range15 <= 0) res.Range15 = res.WhiteBlack;
            var zs = res.Zones;
            res.TotalRangeFStops = Math.Abs(zs[zs.Count - 1].Density - zs[0].Density) / Log2;
        }

        /// <summary>Шум в f-stop = шум (пикс.) / |d(уровень)/d(f-stop)|; производная — по локальной регрессии на 5 зонах.</summary>
        private static void ComputeFStopNoise(StepchartResult res)
        {
            var zs = res.Zones;
            int n = zs.Count;
            for (int ch = 0; ch < 4; ch++)
            {
                for (int i = 0; i < n; i++)
                {
                    var xs = new List<double>();
                    var ys = new List<double>();
                    int lo = Math.Max(0, i - 2), hi = Math.Min(n - 1, i + 2);
                    if (hi - lo < 2) { lo = Math.Max(0, hi - 2); hi = Math.Min(n - 1, lo + 2); }
                    for (int j = lo; j <= hi; j++) { xs.Add(zs[j].Ev); ys.Add(zs[j].Mean[ch]); }
                    double a, b;
                    LinFit(xs, ys, out a, out b);
                    zs[i].Slope[ch] = b;
                    double slope = Math.Abs(b);
                    double fs = slope > 1e-9 ? zs[i].Noise[ch] / slope : double.PositiveInfinity;
                    if (fs > 100) fs = 100;
                    zs[i].FStopNoise[ch] = fs;
                }
            }
        }

        private static void ComputeDynamicRange(StepchartResult res)
        {
            var zs = res.Zones;
            int i0 = 0;
            while (i0 < zs.Count - 1 && zs[i0].Saturated) i0++;
            double evStart = zs[i0].Ev;
            double total = Math.Abs(zs[zs.Count - 1].Ev - evStart);
            for (int k = 0; k < StepchartResult.DrThresholds.Length; k++)
            {
                double t = StepchartResult.DrThresholds[k];
                double dr = total;
                bool isTotal = true;
                for (int i = i0; i < zs.Count; i++)
                {
                    double f = zs[i].FStopNoise[3];
                    if (f > t)
                    {
                        isTotal = false;
                        if (i == i0) { dr = 0; break; }
                        double f0 = Math.Max(1e-6, zs[i - 1].FStopNoise[3]), f1 = Math.Max(1e-6, f);
                        double frac = (Math.Log(t) - Math.Log(f0)) / (Math.Log(f1) - Math.Log(f0));
                        if (double.IsNaN(frac) || frac < 0) frac = 0;
                        if (frac > 1) frac = 1;
                        double ev = zs[i - 1].Ev + frac * (zs[i].Ev - zs[i - 1].Ev);
                        dr = Math.Abs(ev - evStart);
                        break;
                    }
                }
                res.DynamicRange[k] = dr;
                res.DynamicRangeIsTotal[k] = isTotal;
            }
        }

        private static void ComputeAverages(StepchartResult res)
        {
            var zs = res.Zones;
            int a = -1, b = -1;
            for (int i = 0; i < zs.Count; i++)
                if (zs[i].Density >= 0.1 - 1e-9 && zs[i].Density <= 1.5 + 1e-9)
                {
                    if (a < 0) a = i;
                    b = i;
                }
            if (a < 0) { a = 0; b = zs.Count - 1; }
            res.AvgFrom = a + 1;
            res.AvgTo = b + 1;
            for (int ch = 0; ch < 4; ch++)
            {
                double sn = 0, ss = 0;
                int cnt = 0, cs = 0;
                for (int i = a; i <= b; i++)
                {
                    double v = res.Fig1Noise(zs[i], ch);
                    if (!double.IsNaN(v)) { sn += v; cnt++; }
                    double d = zs[i].SnrDb(ch);
                    if (!double.IsNaN(d) && !double.IsInfinity(d)) { ss += d; cs++; }
                }
                res.AvgNoise[ch] = cnt > 0 ? sn / cnt : double.NaN;
                res.AvgSnrDb[ch] = cs > 0 ? ss / cs : double.NaN;
            }
        }

        /// <summary>Спектр шума (яркость Y) по средним зонам: усреднённый квадрат модуля ДПФ строк вдоль таблицы.</summary>
        private static void ComputeSpectrum(StepchartResult res)
        {
            var use = new List<StepZone>();
            foreach (var z in res.Zones)
                if (!z.Saturated && z.Density >= 0.2 && z.Density <= 1.3 && z.MeasureW >= 8) use.Add(z);
            if (use.Count == 0)
                foreach (var z in res.Zones) if (!z.Saturated && z.MeasureW >= 8) use.Add(z);
            if (use.Count == 0) return;
            int M = int.MaxValue;
            foreach (var z in use) M = Math.Min(M, z.MeasureW);
            int nf = M / 2 + 1;
            var power = new double[nf];
            int lines = 0;
            foreach (var z in use)
            {
                for (int row = 0; row < z.MeasureH; row++)
                {
                    int off = row * z.MeasureW;
                    double mean = 0;
                    for (int t = 0; t < M; t++) mean += z.ResidualY[off + t];
                    mean /= M;
                    for (int k = 0; k < nf; k++)
                    {
                        double re = 0, im = 0;
                        for (int t = 0; t < M; t++)
                        {
                            double ang = 2 * Math.PI * k * t / M;
                            double v = z.ResidualY[off + t] - mean;
                            re += v * Math.Cos(ang);
                            im -= v * Math.Sin(ang);
                        }
                        power[k] += (re * re + im * im) / M;
                    }
                    lines++;
                }
            }
            double mx = 0;
            for (int k = 1; k < nf; k++) { power[k] /= lines; if (power[k] > mx) mx = power[k]; }
            power[0] = 0;
            var freq = new double[nf - 1];
            var pw = new double[nf - 1];
            for (int k = 1; k < nf; k++)
            {
                freq[k - 1] = (double)k / M;
                pw[k - 1] = mx > 0 ? power[k] / mx : 0;
            }
            res.SpectrumFreq = freq;
            res.SpectrumPower = pw;
        }
    }
}
