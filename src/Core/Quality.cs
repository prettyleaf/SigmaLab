using System;
using System.Drawing;

namespace SigmaLab.Core
{
    /// <summary>Режим сравнения каналов при расчёте ОСШ/ПОСШ.</summary>
    public enum CompareChannel { Luma = -1, R = 0, G = 1, B = 2, AllRgb = 3 }

    public sealed class QualityResult
    {
        public long N;
        public double Peak;
        public double Mse, Rmse, Mae, MaxAbsError;
        /// <summary>ПОСШ (PSNR), дБ = 10·lg(MAX²/MSE).</summary>
        public double Psnr;
        /// <summary>ОСШ, дБ = 10·lg(Σs² / Σ(s − ŝ)²).</summary>
        public double SnrDb;
        /// <summary>ОСШ по мощности переменной составляющей, дБ = 10·lg(σ²эт / MSE).</summary>
        public double SnrVarDb;
        public double MeanRef, MeanTest, StdRef, StdTest, StdError, MeanError;
        public double Ssim;
    }

    /// <summary>Сравнение изображения с эталоном: ОСШ, ПОСШ, СКО ошибки, SSIM.</summary>
    public static class Quality
    {
        public static string ChannelName(CompareChannel c)
        {
            switch (c)
            {
                case CompareChannel.Luma: return "Яркость";
                case CompareChannel.R: return "R";
                case CompareChannel.G: return "G";
                case CompareChannel.B: return "B";
                default: return "R, G, B (все каналы)";
            }
        }

        /// <summary>Пиковое значение по умолчанию: 255 для 8 бит/RGB, 65535 для 16 бит, максимум эталона для float.</summary>
        public static double DefaultPeak(ImageData reference)
        {
            if (reference.IsInteger) return reference.MaxValue;
            double mx = double.MinValue;
            foreach (var p in reference.Planes)
                foreach (float v in p)
                    if (!float.IsNaN(v) && !float.IsInfinity(v) && v > mx) mx = v;
            return mx > 0 ? mx : 1;
        }

        private static float[][] Planes(ImageData img, Rectangle roi, CompareChannel ch)
        {
            if (ch == CompareChannel.AllRgb)
            {
                if (!img.IsRgb)
                {
                    var g = Stats.GetValues(img, roi, -1);
                    return new[] { g, g, g };
                }
                return new[] { Stats.GetValues(img, roi, 0), Stats.GetValues(img, roi, 1), Stats.GetValues(img, roi, 2) };
            }
            int c = (int)ch;
            if (!img.IsRgb) c = -1;
            return new[] { Stats.GetValues(img, roi, c) };
        }

        public static QualityResult Compare(ImageData reference, ImageData test, Rectangle roi, CompareChannel ch, double peak)
        {
            if (reference.Width != test.Width || reference.Height != test.Height)
                throw new ArgumentException("Размеры изображений различаются: " + reference.Width + "×" + reference.Height +
                                            " и " + test.Width + "×" + test.Height + ".");
            Rectangle r = Stats.Normalize(reference, roi);
            float[][] a = Planes(reference, r, ch), b = Planes(test, r, ch);
            var q = new QualityResult { Peak = peak };
            double se = 0, ae = 0, maxAbs = 0, sumRef = 0, sumTest = 0, sumRef2 = 0, sumTest2 = 0, sumErr = 0;
            long n = 0;
            for (int c = 0; c < a.Length; c++)
            {
                float[] pa = a[c], pb = b[c];
                for (int i = 0; i < pa.Length; i++)
                {
                    double x = pa[i], y = pb[i];
                    if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)) continue;
                    double e = y - x;
                    se += e * e;
                    ae += Math.Abs(e);
                    if (Math.Abs(e) > maxAbs) maxAbs = Math.Abs(e);
                    sumRef += x; sumTest += y;
                    sumRef2 += x * x; sumTest2 += y * y;
                    sumErr += e;
                    n++;
                }
            }
            q.N = n;
            if (n == 0) throw new ArgumentException("В выбранной области нет пикселей для сравнения.");
            q.Mse = se / n;
            q.Rmse = Math.Sqrt(q.Mse);
            q.Mae = ae / n;
            q.MaxAbsError = maxAbs;
            q.MeanRef = sumRef / n;
            q.MeanTest = sumTest / n;
            q.MeanError = sumErr / n;
            q.StdRef = n > 1 ? Math.Sqrt(Math.Max(0, (sumRef2 - n * q.MeanRef * q.MeanRef) / (n - 1))) : 0;
            q.StdTest = n > 1 ? Math.Sqrt(Math.Max(0, (sumTest2 - n * q.MeanTest * q.MeanTest) / (n - 1))) : 0;
            double errVar = se / n - q.MeanError * q.MeanError;
            q.StdError = n > 1 ? Math.Sqrt(Math.Max(0, errVar * n / (n - 1))) : 0;

            q.Psnr = q.Mse > 0 ? 10 * Math.Log10(peak * peak / q.Mse) : double.PositiveInfinity;
            q.SnrDb = se > 0 ? 10 * Math.Log10(sumRef2 / se) : double.PositiveInfinity;
            double varRef = sumRef2 / n - q.MeanRef * q.MeanRef;
            q.SnrVarDb = q.Mse > 0 && varRef > 0 ? 10 * Math.Log10(varRef / q.Mse) : (q.Mse > 0 ? double.NegativeInfinity : double.PositiveInfinity);

            // SSIM (Wang et al., 2004): гауссово окно σ = 1.5, K1 = 0.01, K2 = 0.03
            double ssim = 0;
            for (int c = 0; c < a.Length; c++) ssim += Ssim(a[c], b[c], r.Width, r.Height, peak);
            q.Ssim = ssim / a.Length;
            return q;
        }

        public static double Ssim(float[] x, float[] y, int w, int h, double L)
        {
            double c1 = (0.01 * L) * (0.01 * L), c2 = (0.03 * L) * (0.03 * L);
            int n = w * h;
            var xx = new float[n];
            var yy = new float[n];
            var xy = new float[n];
            for (int i = 0; i < n; i++) { xx[i] = x[i] * x[i]; yy[i] = y[i] * y[i]; xy[i] = x[i] * y[i]; }
            float[] mx = Blur(x, w, h), my = Blur(y, w, h), sxx = Blur(xx, w, h), syy = Blur(yy, w, h), sxy = Blur(xy, w, h);
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                double ux = mx[i], uy = my[i];
                double vx = sxx[i] - ux * ux, vy = syy[i] - uy * uy, cxy = sxy[i] - ux * uy;
                sum += ((2 * ux * uy + c1) * (2 * cxy + c2)) / ((ux * ux + uy * uy + c1) * (vx + vy + c2));
            }
            return sum / n;
        }

        private static float[] Blur(float[] p, int w, int h)
        {
            const double sigma = 1.5;
            const int rad = 5;
            var k = new double[2 * rad + 1];
            double s = 0;
            for (int i = -rad; i <= rad; i++) { k[i + rad] = Math.Exp(-0.5 * i * i / (sigma * sigma)); s += k[i + rad]; }
            for (int i = 0; i < k.Length; i++) k[i] /= s;
            var t = new float[w * h];
            var o = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double a = 0;
                    for (int i = -rad; i <= rad; i++)
                    {
                        int xx = x + i;
                        if (xx < 0) xx = 0; else if (xx >= w) xx = w - 1;
                        a += k[i + rad] * p[y * w + xx];
                    }
                    t[y * w + x] = (float)a;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double a = 0;
                    for (int i = -rad; i <= rad; i++)
                    {
                        int yy = y + i;
                        if (yy < 0) yy = 0; else if (yy >= h) yy = h - 1;
                        a += k[i + rad] * t[yy * w + x];
                    }
                    o[y * w + x] = (float)a;
                }
            return o;
        }

        /// <summary>Изображение ошибки (тест − эталон), 32 бита.</summary>
        public static ImageData ErrorImage(ImageData reference, ImageData test)
        {
            var r = ImageOps.Calculate(test, reference, CalcOp.Subtract, true);
            r.Name = "Ошибка (" + test.Name + " − " + reference.Name + ")";
            return r;
        }
    }
}
