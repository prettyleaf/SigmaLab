using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SigmaLab.Core
{
    /// <summary>Маски 3×3 из методички (рис. 4.18), часть из них — с произвольным размером.</summary>
    public enum MaskKind
    {
        MovingAverage, SmallDetails, Laplacian8, Laplacian4, DoubleDiff,
        VerticalLines, HorizontalLines, DiagonalLeft, DiagonalRight, East, SouthEast
    }

    /// <summary>Ядро свёртки, введённое пользователем (как в окне Convolver программы ImageJ).</summary>
    public static class Masks
    {
        /// <summary>Ядро, которое ImageJ показывает в окне Convolve по умолчанию.</summary>
        public const string ImageJDefault =
            "-1 -1 -1 -1 -1\r\n-1 -1 -1 -1 -1\r\n-1 -1 24 -1 -1\r\n-1 -1 -1 -1 -1\r\n-1 -1 -1 -1 -1";

        public const int MaxTemplateSize = 15;

        public static string KindName(MaskKind k)
        {
            switch (k)
            {
                case MaskKind.MovingAverage: return "Скользящее среднее";
                case MaskKind.SmallDetails: return "Выделение малоразмерных деталей из шумов";
                case MaskKind.Laplacian8: return "Лапласиан для восьми соседей";
                case MaskKind.Laplacian4: return "Лапласиан для четырёх соседей";
                case MaskKind.DoubleDiff: return "Двойное дифференцирование";
                case MaskKind.VerticalLines: return "Выделение вертикальных линий";
                case MaskKind.HorizontalLines: return "Выделение горизонтальных линий";
                case MaskKind.DiagonalLeft: return "Выделение «левой» диагонали";
                case MaskKind.DiagonalRight: return "Выделение «правой» диагонали";
                case MaskKind.East: return "Оператор «Восток»";
                case MaskKind.SouthEast: return "Оператор «Юго-Восток»";
            }
            return k.ToString();
        }

        /// <summary>Какие размеры шаблона можно менять: 0 — любой нечётный, иначе размер фиксирован.</summary>
        public static void FixedSize(MaskKind k, out int width, out int height)
        {
            switch (k)
            {
                case MaskKind.MovingAverage:
                case MaskKind.SmallDetails:
                case MaskKind.Laplacian8: width = height = 0; break;
                case MaskKind.VerticalLines: width = 3; height = 0; break;
                case MaskKind.HorizontalLines: width = 0; height = 3; break;
                default: width = height = 3; break;
            }
        }

        /// <summary>
        /// Шаблон маски width×height (построчно). Скользящее среднее — все единицы; малоразмерные детали —
        /// биномиальная [1 2 1]**[1 2 1]ᵀ, на большие размеры — строками треугольника Паскаля (1 4 6 4 1, …);
        /// лапласиан для восьми соседей — все −1, в центре их число; линии удлиняются вдоль направления линии.
        /// </summary>
        public static float[] Make(MaskKind kind, int width, int height)
        {
            int fw, fh;
            FixedSize(kind, out fw, out fh);
            if (fw > 0) width = fw;
            if (fh > 0) height = fh;
            if (width < 1 || height < 1 || width % 2 == 0 || height % 2 == 0)
                throw new ArgumentException("Ширина и высота маски должны быть нечётными (1, 3, 5…).");
            var k = new float[width * height];
            switch (kind)
            {
                case MaskKind.MovingAverage:
                    for (int i = 0; i < k.Length; i++) k[i] = 1;
                    break;
                case MaskKind.SmallDetails:
                    double[] bx = Binomial(width), by = Binomial(height);
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++) k[y * width + x] = (float)(by[y] * bx[x]);
                    break;
                case MaskKind.Laplacian8:
                    for (int i = 0; i < k.Length; i++) k[i] = -1;
                    k[k.Length / 2] = k.Length - 1;
                    break;
                case MaskKind.VerticalLines:
                    for (int y = 0; y < height; y++) { k[y * 3] = -1; k[y * 3 + 1] = 2; k[y * 3 + 2] = -1; }
                    break;
                case MaskKind.HorizontalLines:
                    for (int x = 0; x < width; x++) { k[x] = -1; k[width + x] = 2; k[2 * width + x] = -1; }
                    break;
                default:
                    Array.Copy(Fixed3x3(kind), k, 9);
                    break;
            }
            return k;
        }

        private static float[] Fixed3x3(MaskKind kind)
        {
            switch (kind)
            {
                case MaskKind.Laplacian4: return new float[] { 0, -1, 0, -1, 4, -1, 0, -1, 0 };
                case MaskKind.DoubleDiff: return new float[] { 1, -2, 1, -2, 4, -2, 1, -2, 1 };
                case MaskKind.DiagonalLeft: return new float[] { 2, -1, -1, -1, 2, -1, -1, -1, 2 };
                case MaskKind.DiagonalRight: return new float[] { -1, -1, 2, -1, 2, -1, 2, -1, -1 };
                case MaskKind.East: return new float[] { -1, 1, 1, -1, -2, 1, -1, 1, 1 };
                case MaskKind.SouthEast: return new float[] { -1, -1, 1, -1, -2, 1, 1, 1, 1 };
            }
            throw new ArgumentException("Нет маски 3×3 для " + kind);
        }

        /// <summary>Биномиальные коэффициенты C(n−1, i): 1; 1 2 1; 1 4 6 4 1; …</summary>
        private static double[] Binomial(int n)
        {
            var c = new double[n];
            c[0] = 1;
            for (int i = 1; i < n; i++) c[i] = c[i - 1] * (n - i) / i;
            return c;
        }

        public static float Sum(float[] k)
        {
            float s = 0;
            foreach (float v in k) s += v;
            return s;
        }

        /// <summary>Текст ядра (числа через пробел, строки — с новой строки).</summary>
        public static string ToText(float[] k, int rows, int cols)
        {
            var sb = new StringBuilder();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (c > 0) sb.Append(' ');
                    sb.Append(k[r * cols + c].ToString("0.####", CultureInfo.InvariantCulture));
                }
                if (r < rows - 1) sb.Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>Разбор ядра: числа через пробел, каждая строка ядра — с новой строки; размеры нечётные.</summary>
        public static bool TryParse(string text, out float[] kernel, out int rows, out int cols, out string error)
        {
            kernel = null; rows = cols = 0; error = null;
            var lines = new List<float[]>();
            foreach (string raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                var parts = line.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
                var row = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    double v;
                    if (!NumParse.TryParse(parts[i], out v))
                    {
                        error = "Не удалось прочитать число «" + parts[i] + "».";
                        return false;
                    }
                    row[i] = (float)v;
                }
                lines.Add(row);
            }
            if (lines.Count == 0) { error = "Ядро не задано."; return false; }
            cols = lines[0].Length;
            foreach (var l in lines)
                if (l.Length != cols) { error = "Во всех строках должно быть одинаковое число коэффициентов."; return false; }
            rows = lines.Count;
            if (rows % 2 == 0 || cols % 2 == 0) { error = "Число строк и столбцов ядра должно быть нечётным (3, 5, 7…)."; return false; }
            kernel = new float[rows * cols];
            for (int r = 0; r < rows; r++) Array.Copy(lines[r], 0, kernel, r * cols, cols);
            return true;
        }
    }

    /// <summary>Разбор чисел и с точкой, и с запятой.</summary>
    public static class NumParse
    {
        public static bool TryParse(string s, out double v)
        {
            v = 0;
            if (s == null) return false;
            s = s.Trim().Replace(',', '.').Replace('−', '-');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
