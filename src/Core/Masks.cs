using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SigmaLab.Core
{
    /// <summary>Ядро свёртки, введённое пользователем (как в окне Convolver программы ImageJ).</summary>
    public static class Masks
    {
        /// <summary>Ядро, которое ImageJ показывает в окне Convolve по умолчанию.</summary>
        public const string ImageJDefault =
            "-1 -1 -1 -1 -1\r\n-1 -1 -1 -1 -1\r\n-1 -1 24 -1 -1\r\n-1 -1 -1 -1 -1\r\n-1 -1 -1 -1 -1";

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
