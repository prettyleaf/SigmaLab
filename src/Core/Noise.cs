using System;

namespace VideoMeter.Core
{
    public enum NoiseKind
    {
        Gaussian,     // аддитивный гауссов (Add Specified Noise)
        Uniform,      // аддитивный равномерный ±A
        SaltPepper,   // импульсный «соль и перец»
        Speckle       // мультипликативный: v·(1 + n), n ~ N(0, σ)
    }

    /// <summary>Process › Noise.</summary>
    public static class Noise
    {
        private static readonly object Sync = new object();
        private static Random _shared = new Random();

        public static Random NewRandom(int? seed)
        {
            if (seed.HasValue) return new Random(seed.Value);
            lock (Sync) return new Random(_shared.Next());
        }

        /// <summary>Нормальная случайная величина N(0,1), полярный метод (как java.util.Random.nextGaussian).</summary>
        public sealed class Gauss
        {
            private readonly Random _r;
            private bool _has;
            private double _next;
            public Gauss(Random r) { _r = r; }
            public double Next()
            {
                if (_has) { _has = false; return _next; }
                double v1, v2, s;
                do
                {
                    v1 = 2 * _r.NextDouble() - 1;
                    v2 = 2 * _r.NextDouble() - 1;
                    s = v1 * v1 + v2 * v2;
                } while (s >= 1 || s == 0);
                double m = Math.Sqrt(-2 * Math.Log(s) / s);
                _next = v2 * m;
                _has = true;
                return v1 * m;
            }
        }

        /// <summary>
        /// Add Specified Noise: гауссов шум со СКО sd к каждому каналу независимо.
        /// Для 8 бит, как в ImageJ, приращение округляется, а значения вне 0..255 разыгрываются заново
        /// (если включено <see cref="LabOptions.NoiseResampleOutOfRange"/>).
        /// </summary>
        public static ImageData AddGaussian(ImageData src, double sd, int? seed = null)
        {
            var r = src.Clone();
            var g = new Gauss(NewRandom(seed));
            bool integer = src.IsInteger;
            double max = integer ? src.MaxValue : 0;
            bool resample = LabOptions.NoiseResampleOutOfRange;
            for (int c = 0; c < r.Channels; c++)
            {
                float[] p = r.Planes[c];
                for (int i = 0; i < p.Length; i++)
                {
                    if (integer)
                    {
                        double v0 = p[i];
                        double v;
                        int guard = 0;
                        do
                        {
                            v = v0 + Math.Round(g.Next() * sd);
                            guard++;
                        } while (resample && (v < 0 || v > max) && guard < 1000);
                        p[i] = (float)v;
                    }
                    else p[i] = (float)(p[i] + g.Next() * sd);
                }
            }
            r.Conform();
            return r;
        }

        /// <summary>Равномерный аддитивный шум в диапазоне [-a; +a].</summary>
        public static ImageData AddUniform(ImageData src, double a, int? seed = null)
        {
            var r = src.Clone();
            var rnd = NewRandom(seed);
            bool integer = src.IsInteger;
            for (int c = 0; c < r.Channels; c++)
            {
                float[] p = r.Planes[c];
                for (int i = 0; i < p.Length; i++)
                {
                    double n = (rnd.NextDouble() * 2 - 1) * a;
                    p[i] = (float)(p[i] + (integer ? Math.Round(n) : n));
                }
            }
            r.Conform();
            return r;
        }

        /// <summary>
        /// «Соль и перец»: доля fraction пикселей заменяется — половина на минимум, половина на максимум.
        /// В ImageJ по умолчанию 2,5% + 2,5% (fraction = 0.05). Для RGB одинаковое значение во всех каналах.
        /// </summary>
        public static ImageData AddSaltPepper(ImageData src, double fraction, int? seed = null)
        {
            var r = src.Clone();
            var rnd = NewRandom(seed);
            double hi = src.IsInteger ? src.MaxValue : src.DisplayMax;
            double lo = src.IsInteger ? 0 : src.DisplayMin;
            int n = r.PixelCount;
            int count = (int)Math.Round(n * fraction / 2.0);
            for (int pass = 0; pass < 2; pass++)
            {
                float v = (float)(pass == 0 ? hi : lo);
                for (int k = 0; k < count; k++)
                {
                    int i = rnd.Next(n);
                    for (int c = 0; c < r.Channels; c++) r.Planes[c][i] = v;
                }
            }
            r.Conform();
            return r;
        }

        /// <summary>Мультипликативный (спекл) шум: v·(1 + n), n ~ N(0, sd).</summary>
        public static ImageData AddSpeckle(ImageData src, double sd, int? seed = null)
        {
            var r = src.Clone();
            var g = new Gauss(NewRandom(seed));
            for (int c = 0; c < r.Channels; c++)
            {
                float[] p = r.Planes[c];
                for (int i = 0; i < p.Length; i++) p[i] = (float)(p[i] * (1 + g.Next() * sd));
            }
            r.Conform();
            return r;
        }

        public static ImageData Add(ImageData src, NoiseKind kind, double param, int? seed = null)
        {
            switch (kind)
            {
                case NoiseKind.Uniform: return AddUniform(src, param, seed);
                case NoiseKind.SaltPepper: return AddSaltPepper(src, param, seed);
                case NoiseKind.Speckle: return AddSpeckle(src, param, seed);
                default: return AddGaussian(src, param, seed);
            }
        }
    }
}
