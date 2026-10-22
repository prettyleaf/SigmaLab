using System;

namespace VideoMeter.Core
{
    public enum RankType { Mean, Median, Minimum, Maximum, Variance }

    /// <summary>Process › Filters. Края обрабатываются повторением крайних пикселей, как в ImageJ.</summary>
    public static class Filters
    {
        public static string RankName(RankType t)
        {
            switch (t)
            {
                case RankType.Mean: return "Среднее (Mean)";
                case RankType.Median: return "Медианный (Median)";
                case RankType.Minimum: return "Минимум (Minimum)";
                case RankType.Maximum: return "Максимум (Maximum)";
                case RankType.Variance: return "Дисперсия (Variance)";
            }
            return t.ToString();
        }

        // ------------------------------------------------------------------ свёртка

        /// <summary>
        /// Свёртка с ядром kw×kh (построчно, как вводится в Convolver ImageJ).
        /// Вычисляется корреляция (ядро не переворачивается) — так же, как в ImageJ.
        /// При normalize ядро делится на сумму коэффициентов, если она не равна нулю.
        /// </summary>
        public static ImageData Convolve(ImageData src, float[] kernel, int kw, int kh, bool normalize)
        {
            if (kw % 2 == 0 || kh % 2 == 0) throw new ArgumentException("Ширина и высота ядра должны быть нечётными.");
            if (kernel.Length != kw * kh) throw new ArgumentException("Размер ядра не совпадает с числом коэффициентов.");
            double sum = 0;
            for (int i = 0; i < kernel.Length; i++) sum += kernel[i];
            double scale = normalize && Math.Abs(sum) > 1e-12 ? 1.0 / sum : 1.0;

            var r = src.CreateEmpty();
            int W = src.Width, H = src.Height, rx = kw / 2, ry = kh / 2;
            int PW = W + 2 * rx;
            for (int c = 0; c < src.Channels; c++)
            {
                float[] pad = Pad(src.Planes[c], W, H, rx, ry);
                float[] o = r.Planes[c];
                for (int y = 0; y < H; y++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        double s = 0;
                        int ki = 0;
                        for (int v = 0; v < kh; v++)
                        {
                            int row = (y + v) * PW + x;
                            for (int u = 0; u < kw; u++, ki++)
                            {
                                float k = kernel[ki];
                                if (k != 0) s += k * pad[row + u];
                            }
                        }
                        o[y * W + x] = (float)(s * scale);
                    }
                }
            }
            r.Conform();
            return r;
        }

        /// <summary>Копия плоскости с полями rx/ry, заполненными крайними пикселями.</summary>
        internal static float[] Pad(float[] p, int W, int H, int rx, int ry)
        {
            int PW = W + 2 * rx, PH = H + 2 * ry;
            var pad = new float[PW * PH];
            for (int py = 0; py < PH; py++)
            {
                int sy = py - ry;
                if (sy < 0) sy = 0; else if (sy >= H) sy = H - 1;
                int srow = sy * W, drow = py * PW;
                float left = p[srow], right = p[srow + W - 1];
                for (int px = 0; px < rx; px++) pad[drow + px] = left;
                Array.Copy(p, srow, pad, drow + rx, W);
                for (int px = rx + W; px < PW; px++) pad[drow + px] = right;
            }
            return pad;
        }

        // ------------------------------------------------------------------ гауссово размытие

        /// <summary>Gaussian Blur с заданной сигмой (поле «Sigma (Radius)» в ImageJ).</summary>
        public static ImageData GaussianBlur(ImageData src, double sigma)
        {
            if (sigma <= 0) return src.Clone();
            double accuracy = src.IsInteger ? 0.002 : 0.0002;
            int radius = (int)Math.Ceiling(sigma * Math.Sqrt(-2 * Math.Log(accuracy))) + 1;
            var k = new double[2 * radius + 1];
            double sum = 0;
            for (int i = -radius; i <= radius; i++)
            {
                double v = Math.Exp(-0.5 * i * i / (sigma * sigma));
                k[i + radius] = v;
                sum += v;
            }
            for (int i = 0; i < k.Length; i++) k[i] /= sum;

            var r = src.CreateEmpty();
            int W = src.Width, H = src.Height;
            var tmp = new float[W * H];
            for (int c = 0; c < src.Channels; c++)
            {
                float[] p = src.Planes[c], o = r.Planes[c];
                // по строкам
                for (int y = 0; y < H; y++)
                {
                    int row = y * W;
                    for (int x = 0; x < W; x++)
                    {
                        double s = 0;
                        for (int i = -radius; i <= radius; i++)
                        {
                            int xx = x + i;
                            if (xx < 0) xx = 0; else if (xx >= W) xx = W - 1;
                            s += k[i + radius] * p[row + xx];
                        }
                        tmp[row + x] = (float)s;
                    }
                }
                // по столбцам
                for (int y = 0; y < H; y++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        double s = 0;
                        for (int i = -radius; i <= radius; i++)
                        {
                            int yy = y + i;
                            if (yy < 0) yy = 0; else if (yy >= H) yy = H - 1;
                            s += k[i + radius] * tmp[yy * W + x];
                        }
                        o[y * W + x] = (float)s;
                    }
                }
            }
            r.Conform();
            return r;
        }

        // ------------------------------------------------------------------ ранговые фильтры

        /// <summary>
        /// Круглая апертура ImageJ (RankFilters.makeLineRadii): r² = (int)(radius²) + 1,
        /// для каждой строки dy полуширина dx = (int)sqrt(r² − dy²).
        /// </summary>
        public static int[] CircleHalfWidths(double radius, out int kRadius)
        {
            if (radius < 0.5) radius = 0.5;
            int r2 = (int)(radius * radius) + 1;
            kRadius = (int)Math.Sqrt(r2 + 1e-10);
            var dx = new int[2 * kRadius + 1];
            for (int dy = -kRadius; dy <= kRadius; dy++)
                dx[dy + kRadius] = (int)Math.Sqrt(r2 - dy * dy + 1e-10);
            return dx;
        }

        /// <summary>Число пикселей в круглой апертуре заданного радиуса.</summary>
        public static int CircleArea(double radius)
        {
            int kr;
            int[] dx = CircleHalfWidths(radius, out kr);
            int n = 0;
            foreach (int d in dx) n += 2 * d + 1;
            return n;
        }

        /// <summary>Ранговый фильтр с круглой апертурой заданного радиуса (как в ImageJ).</summary>
        public static ImageData Rank(ImageData src, RankType type, double radius)
        {
            int kr;
            return Rank(src, type, CircleHalfWidths(radius, out kr));
        }

        /// <summary>Ранговый фильтр с прямоугольной апертурой width×height (нечётные); апертура 3×5 методички (3 строки, 5 столбцов) — width = 5, height = 3.</summary>
        public static ImageData Rank(ImageData src, RankType type, int width, int height)
        {
            if (width < 1 || height < 1 || width % 2 == 0 || height % 2 == 0)
                throw new ArgumentException("Ширина и высота апертуры должны быть нечётными (1, 3, 5…).");
            var hw = new int[height];
            for (int j = 0; j < height; j++) hw[j] = width / 2;
            return Rank(src, type, hw);
        }

        /// <summary>Апертура задана полуширинами строк hw (их нечётное число, центр — средняя строка).</summary>
        private static ImageData Rank(ImageData src, RankType type, int[] hw)
        {
            int rx = 0, ry = hw.Length / 2;
            foreach (int d in hw) rx = Math.Max(rx, d);
            int W = src.Width, H = src.Height, PW = W + 2 * rx;
            var r = src.CreateEmpty();
            bool byteData = src.Type == PixelType.Gray8 || src.Type == PixelType.Rgb24;
            int n = 0;
            foreach (int d in hw) n += 2 * d + 1;

            for (int c = 0; c < src.Channels; c++)
            {
                float[] pad = Pad(src.Planes[c], W, H, rx, ry);
                float[] o = r.Planes[c];
                switch (type)
                {
                    case RankType.Mean:
                    case RankType.Variance:
                        MeanVariance(pad, PW, W, H, rx, hw, n, o, type == RankType.Variance);
                        break;
                    default:
                        if (byteData) RankByte(pad, PW, W, H, rx, hw, n, o, type);
                        else RankFloat(pad, PW, W, H, rx, hw, n, o, type);
                        break;
                }
            }
            r.Conform();
            return r;
        }

        // во вспомогательных функциях kr — поле слева/справа (rx); строк апертуры hw.Length, поле сверху/снизу — hw.Length / 2
        private static void MeanVariance(float[] pad, int PW, int W, int H, int kr, int[] hw, int n, float[] o, bool variance)
        {
            int PH = H + hw.Length - 1;
            // префиксные суммы по строкам дополненного изображения
            var ps = new double[PH * (PW + 1)];
            double[] ps2 = variance ? new double[PH * (PW + 1)] : null;
            for (int y = 0; y < PH; y++)
            {
                int pr = y * (PW + 1), r = y * PW;
                double s = 0, s2 = 0;
                ps[pr] = 0;
                if (variance) ps2[pr] = 0;
                for (int x = 0; x < PW; x++)
                {
                    double v = pad[r + x];
                    s += v;
                    ps[pr + x + 1] = s;
                    if (variance) { s2 += v * v; ps2[pr + x + 1] = s2; }
                }
            }
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    double s = 0, s2 = 0;
                    int cx = x + kr;
                    for (int j = 0; j < hw.Length; j++)
                    {
                        int pr = (y + j) * (PW + 1);
                        int a = cx - hw[j], b = cx + hw[j] + 1;
                        s += ps[pr + b] - ps[pr + a];
                        if (variance) s2 += ps2[pr + b] - ps2[pr + a];
                    }
                    double mean = s / n;
                    if (variance)
                    {
                        double var = s2 / n - mean * mean;
                        o[y * W + x] = (float)(var < 0 ? 0 : var);
                    }
                    else o[y * W + x] = (float)mean;
                }
            }
        }

        /// <summary>Медиана/минимум/максимум для 8-битных данных: скользящая гистограмма.</summary>
        private static void RankByte(float[] pad, int PW, int W, int H, int kr, int[] hw, int n, float[] o, RankType type)
        {
            var hist = new int[256];
            int half = n / 2; // n всегда нечётное
            for (int y = 0; y < H; y++)
            {
                Array.Clear(hist, 0, 256);
                // начальное окно для x = 0
                for (int j = 0; j < hw.Length; j++)
                {
                    int row = (y + j) * PW + kr;
                    for (int dx = -hw[j]; dx <= hw[j]; dx++) hist[(int)pad[row + dx]]++;
                }
                for (int x = 0; x < W; x++)
                {
                    if (x > 0)
                    {
                        for (int j = 0; j < hw.Length; j++)
                        {
                            int row = (y + j) * PW + kr + x;
                            hist[(int)pad[row - hw[j] - 1]]--;
                            hist[(int)pad[row + hw[j]]]++;
                        }
                    }
                    int v;
                    if (type == RankType.Minimum)
                    {
                        v = 0;
                        while (hist[v] == 0) v++;
                    }
                    else if (type == RankType.Maximum)
                    {
                        v = 255;
                        while (hist[v] == 0) v--;
                    }
                    else
                    {
                        int acc = 0;
                        v = 0;
                        while (true)
                        {
                            acc += hist[v];
                            if (acc > half) break;
                            v++;
                        }
                    }
                    o[y * W + x] = v;
                }
            }
        }

        private static void RankFloat(float[] pad, int PW, int W, int H, int kr, int[] hw, int n, float[] o, RankType type)
        {
            var buf = new float[n];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int k = 0;
                    float mn = float.MaxValue, mx = float.MinValue;
                    for (int j = 0; j < hw.Length; j++)
                    {
                        int row = (y + j) * PW + kr + x;
                        for (int dx = -hw[j]; dx <= hw[j]; dx++)
                        {
                            float v = pad[row + dx];
                            buf[k++] = v;
                            if (v < mn) mn = v;
                            if (v > mx) mx = v;
                        }
                    }
                    float res;
                    if (type == RankType.Minimum) res = mn;
                    else if (type == RankType.Maximum) res = mx;
                    else res = QuickSelect(buf, n, n / 2);
                    o[y * W + x] = res;
                }
            }
        }

        internal static float QuickSelect(float[] a, int n, int k)
        {
            int lo = 0, hi = n - 1;
            while (lo < hi)
            {
                float pivot = a[(lo + hi) >> 1];
                int i = lo, j = hi;
                while (i <= j)
                {
                    while (a[i] < pivot) i++;
                    while (a[j] > pivot) j--;
                    if (i <= j)
                    {
                        float t = a[i]; a[i] = a[j]; a[j] = t;
                        i++; j--;
                    }
                }
                if (k <= j) hi = j;
                else if (k >= i) lo = i;
                else break;
            }
            return a[k];
        }

        // ------------------------------------------------------------------ прочее (Process › Smooth/Sharpen/Find Edges)

        public static ImageData Smooth(ImageData src)
        {
            return Convolve(src, new float[] { 1, 1, 1, 1, 1, 1, 1, 1, 1 }, 3, 3, true);
        }

        public static ImageData Sharpen(ImageData src)
        {
            return Convolve(src, new float[] { -1, -1, -1, -1, 12, -1, -1, -1, -1 }, 3, 3, true);
        }

        /// <summary>Find Edges: модуль градиента Собеля.</summary>
        public static ImageData FindEdges(ImageData src)
        {
            int W = src.Width, H = src.Height, PW = W + 2;
            var r = src.CreateEmpty();
            for (int c = 0; c < src.Channels; c++)
            {
                float[] p = Pad(src.Planes[c], W, H, 1, 1);
                float[] o = r.Planes[c];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = (y + 1) * PW + x + 1;
                        double p1 = p[i - PW - 1], p2 = p[i - PW], p3 = p[i - PW + 1];
                        double p4 = p[i - 1], p6 = p[i + 1];
                        double p7 = p[i + PW - 1], p8 = p[i + PW], p9 = p[i + PW + 1];
                        double sx = p1 + 2 * p2 + p3 - p7 - 2 * p8 - p9;
                        double sy = p1 + 2 * p4 + p7 - p3 - 2 * p6 - p9;
                        o[y * W + x] = (float)Math.Sqrt(sx * sx + sy * sy);
                    }
            }
            r.Conform();
            return r;
        }
    }
}
