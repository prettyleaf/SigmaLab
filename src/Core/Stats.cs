using System;
using System.Drawing;

namespace SigmaLab.Core
{
    /// <summary>Статистика области: те же величины, что Analyze › Measure / Histogram в ImageJ.</summary>
    public struct RoiStats
    {
        public long N;
        public double Mean, StdDev, Min, Max, Mode, Sum;
    }

    public sealed class Histogram
    {
        public double[] Counts;
        public double BinStart;
        public double BinWidth;
        public bool IntegerBins;
        public RoiStats Stats;
        public int Bins { get { return Counts.Length; } }
        public double BinCenter(int i) { return IntegerBins ? BinStart + i : BinStart + (i + 0.5) * BinWidth; }
    }

    public static class Stats
    {
        /// <summary>Канал: -1 — яркость (для RGB), 0..2 — R, G, B.</summary>
        public static float[] GetValues(ImageData img, Rectangle roi, int channel)
        {
            Rectangle r = Normalize(img, roi);
            var v = new float[r.Width * r.Height];
            int k = 0;
            for (int y = r.Top; y < r.Bottom; y++)
            {
                int row = y * img.Width;
                for (int x = r.Left; x < r.Right; x++)
                {
                    int i = row + x;
                    float val;
                    if (img.IsRgb && channel >= 0 && channel < 3) val = img.Planes[channel][i];
                    else if (img.IsRgb) val = img.Luma(i);
                    else val = img.Planes[0][i];
                    v[k++] = val;
                }
            }
            return v;
        }

        public static Rectangle Normalize(ImageData img, Rectangle roi)
        {
            var full = new Rectangle(0, 0, img.Width, img.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return full;
            var r = Rectangle.Intersect(roi, full);
            return r.Width > 0 && r.Height > 0 ? r : full;
        }

        public static RoiStats Compute(ImageData img, Rectangle roi, int channel = -1)
        {
            float[] v = GetValues(img, roi, channel);
            var s = Compute(v);
            s.Mode = ComputeHistogram(v, img.IsInteger, img.IsInteger ? 0 : double.NaN, img.IsInteger ? img.MaxValue : double.NaN, 256).Stats.Mode;
            return s;
        }

        /// <summary>Среднее, СКО (несмещённое, n−1, как в ImageJ), минимум, максимум. Нечисловые значения пропускаются.</summary>
        public static RoiStats Compute(float[] v)
        {
            var s = new RoiStats();
            double sum = 0, min = double.MaxValue, max = double.MinValue;
            long n = 0;
            foreach (float f in v)
            {
                if (float.IsNaN(f) || float.IsInfinity(f)) continue;
                sum += f;
                if (f < min) min = f;
                if (f > max) max = f;
                n++;
            }
            s.N = n;
            if (n == 0) { s.Mean = s.StdDev = s.Min = s.Max = double.NaN; return s; }
            double mean = sum / n;
            double ss = 0;
            foreach (float f in v)
            {
                if (float.IsNaN(f) || float.IsInfinity(f)) continue;
                double d = f - mean;
                ss += d * d;
            }
            s.Mean = mean;
            s.Sum = sum;
            s.StdDev = n > 1 ? Math.Sqrt(ss / (n - 1)) : 0;
            s.Min = min;
            s.Max = max;
            s.Mode = mean;
            return s;
        }

        public static Histogram ComputeHistogram(ImageData img, Rectangle roi, int channel)
        {
            float[] v = GetValues(img, roi, channel);
            double lo = double.NaN, hi = double.NaN;
            if (img.Type == PixelType.Gray8 || img.Type == PixelType.Rgb24) { lo = 0; hi = 255; }
            var h = ComputeHistogram(v, img.Type == PixelType.Gray8 || img.Type == PixelType.Rgb24, lo, hi, 256);
            return h;
        }

        /// <summary>
        /// Гистограмма: для 8-битных данных 256 целых бинов 0..255;
        /// иначе 256 бинов от минимума до максимума (как в ImageJ для 32-битных изображений).
        /// </summary>
        public static Histogram ComputeHistogram(float[] v, bool integerBins, double lo, double hi, int nBins)
        {
            var h = new Histogram();
            h.Stats = Compute(v);
            if (integerBins)
            {
                h.IntegerBins = true;
                h.BinStart = 0;
                h.BinWidth = 1;
                int nb = double.IsNaN(hi) ? 256 : (int)hi + 1;
                h.Counts = new double[nb];
                foreach (float f in v)
                {
                    int b = (int)f;
                    if (b < 0) b = 0; else if (b >= nb) b = nb - 1;
                    h.Counts[b]++;
                }
            }
            else
            {
                double mn = h.Stats.Min, mx = h.Stats.Max;
                if (double.IsNaN(mn)) { mn = 0; mx = 1; }
                if (mx <= mn) mx = mn + 1;
                h.BinStart = mn;
                h.BinWidth = (mx - mn) / nBins;
                h.Counts = new double[nBins];
                foreach (float f in v)
                {
                    if (float.IsNaN(f) || float.IsInfinity(f)) continue;
                    int b = (int)((f - mn) / h.BinWidth);
                    if (b < 0) b = 0; else if (b >= nBins) b = nBins - 1;
                    h.Counts[b]++;
                }
            }
            int mode = 0;
            for (int i = 1; i < h.Counts.Length; i++) if (h.Counts[i] > h.Counts[mode]) mode = i;
            var st = h.Stats;
            st.Mode = h.BinCenter(mode);
            h.Stats = st;
            return h;
        }

        /// <summary>Профиль: среднее по столбцам (vertical=false) или по строкам (vertical=true) прямоугольника.</summary>
        public static double[] Profile(ImageData img, Rectangle roi, bool vertical, int channel = -1)
        {
            Rectangle r = Normalize(img, roi);
            int len = vertical ? r.Height : r.Width;
            var p = new double[len];
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++)
                {
                    int i = y * img.Width + x;
                    float v = img.IsRgb ? (channel >= 0 ? img.Planes[channel][i] : img.Luma(i)) : img.Planes[0][i];
                    p[vertical ? y - r.Top : x - r.Left] += v;
                }
            double div = vertical ? r.Width : r.Height;
            for (int i = 0; i < len; i++) p[i] /= div;
            return p;
        }
    }
}
