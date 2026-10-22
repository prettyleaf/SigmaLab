using System;

namespace SigmaLab.Core
{
    public sealed class StepChartSpec
    {
        public int Steps = 20;
        public double DensityStep = 0.10;
        public double FirstDensity = 0.0;
        public int Width = 1600;
        public int Height = 400;
        public bool Horizontal = true;
        public int Margin = 40;          // поле вокруг шкалы
        public double EncodingGamma = 2.2; // 2.2 — для печати (sRGB-подобно), 1.0 — линейно
        public double MarginLevel = 128;   // яркость поля
    }

    /// <summary>Генерация тестовых таблиц (аналог модуля Test Charts) и имитация съёмки.</summary>
    public static class TestCharts
    {
        /// <summary>Уровень пикселя для оптической плотности D: 255·(10^−D)^(1/γ).</summary>
        public static double LevelForDensity(double d, double gamma)
        {
            double refl = Math.Pow(10, -d);
            return 255 * Math.Pow(refl, 1.0 / Math.Max(0.1, gamma));
        }

        public static ImageData StepChart(StepChartSpec s)
        {
            var img = new ImageData(s.Width, s.Height, PixelType.Gray8);
            img.Name = "Тестовая таблица " + s.Steps + " градаций (ΔD=" + s.DensityStep.ToString("0.00") + ")";
            float[] p = img.Planes[0];
            for (int i = 0; i < p.Length; i++) p[i] = (float)s.MarginLevel;
            int m = Math.Max(0, Math.Min(s.Margin, Math.Min(s.Width, s.Height) / 3));
            int x0 = m, y0 = m, w = s.Width - 2 * m, h = s.Height - 2 * m;
            int len = s.Horizontal ? w : h;
            for (int k = 0; k < s.Steps; k++)
            {
                int a = (int)Math.Round((double)k * len / s.Steps), b = (int)Math.Round((double)(k + 1) * len / s.Steps);
                float v = (float)Math.Round(LevelForDensity(s.FirstDensity + k * s.DensityStep, s.EncodingGamma));
                for (int t = a; t < b; t++)
                {
                    if (s.Horizontal)
                        for (int y = 0; y < h; y++) p[(y0 + y) * s.Width + x0 + t] = v;
                    else
                        for (int x = 0; x < w; x++) p[(y0 + t) * s.Width + x0 + x] = v;
                }
            }
            img.Conform();
            return img;
        }

        public static ImageData Ramp(int w, int h, bool horizontal)
        {
            var img = new ImageData(w, h, PixelType.Gray8);
            img.Name = "Градиент";
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    img.Planes[0][y * w + x] = (float)(horizontal ? 255.0 * x / Math.Max(1, w - 1) : 255.0 * y / Math.Max(1, h - 1));
            img.Conform();
            return img;
        }

        public static ImageData Checkerboard(int w, int h, int cell, double dark, double light)
        {
            var img = new ImageData(w, h, PixelType.Gray8);
            img.Name = "Шахматное поле " + cell + " пикс.";
            cell = Math.Max(1, cell);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    img.Planes[0][y * w + x] = (float)((((x / cell) + (y / cell)) & 1) == 0 ? light : dark);
            img.Conform();
            return img;
        }

        public static ImageData Uniform(int w, int h, double level)
        {
            var img = new ImageData(w, h, PixelType.Gray8);
            img.Name = "Однородное поле " + level;
            for (int i = 0; i < img.PixelCount; i++) img.Planes[0][i] = (float)level;
            img.Conform();
            return img;
        }

        /// <summary>
        /// Имитация съёмки напечатанной таблицы ТВ-камерой: линеаризация печати (γпеч),
        /// экспозиция, неравномерность освещения, размытие, гамма камеры, шум (с ростом в тенях) и 8-битное квантование.
        /// </summary>
        public static ImageData SimulateCamera(ImageData chart, double printGamma, double exposure, double cameraGamma,
                                               double noiseSigma, double nonUniformity, double blurSigma, bool color, int? seed)
        {
            ImageData src = chart.IsRgb ? ImageOps.ConvertTo(chart, PixelType.Gray8) : chart;
            int W = src.Width, H = src.Height;
            var lin = new ImageData(W, H, PixelType.Gray32);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    double refl = Math.Pow(Math.Max(0, src.Planes[0][i]) / 255.0, printGamma);
                    double dx = (x - W / 2.0) / (W / 2.0), dy = (y - H / 2.0) / (H / 2.0);
                    double illum = 1 - nonUniformity * (dx * dx + dy * dy) / 2;
                    lin.Planes[0][i] = (float)(refl * illum * exposure);
                }
            if (blurSigma > 0) lin = Filters.GaussianBlur(lin, blurSigma);

            var g = new Noise.Gauss(Noise.NewRandom(seed));
            var outImg = new ImageData(W, H, color ? PixelType.Rgb24 : PixelType.Gray8);
            outImg.Name = "Снимок: " + chart.Name;
            double[] chGain = { 1.0, 1.0, 1.0 };
            for (int c = 0; c < outImg.Channels; c++)
            {
                float[] o = outImg.Planes[c];
                for (int i = 0; i < o.Length; i++)
                {
                    double e = Math.Max(0, lin.Planes[0][i] * chGain[c]);
                    // шум в линейной области: дробовой + шум считывания, затем гамма камеры
                    double sigmaLin = noiseSigma / 255.0 * (0.35 + 0.65 * Math.Sqrt(Math.Min(1, e)));
                    double en = Math.Max(0, e + g.Next() * sigmaLin * 0.5);
                    double v = 255 * Math.Pow(Math.Min(1.2, en), 1.0 / Math.Max(0.1, cameraGamma));
                    v += g.Next() * noiseSigma * 0.5;
                    o[i] = (float)v;
                }
            }
            outImg.Conform();
            return outImg;
        }
    }
}
