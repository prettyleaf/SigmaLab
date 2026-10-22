using System;
using System.Collections.Generic;
using System.Drawing;

namespace SigmaLab.Core
{
    public enum MathOp
    {
        Add, Subtract, Multiply, Divide, Min, Max, Gamma, Set,
        Log, Exp, Square, Sqrt, Abs, Reciprocal
    }

    public enum CalcOp
    {
        Add, Subtract, Multiply, Divide, And, Or, Xor, Min, Max, Average, Difference, Copy, TransparentZero
    }

    /// <summary>Арифметика и преобразования (Process › Math, Process › Image Calculator, Image › Type).</summary>
    public static class ImageOps
    {
        public static readonly string[] CalcOpNames =
        {
            "Сложение (Add)", "Вычитание (Subtract)", "Умножение (Multiply)", "Деление (Divide)",
            "И (AND)", "ИЛИ (OR)", "Исключающее ИЛИ (XOR)", "Минимум (Min)", "Максимум (Max)",
            "Среднее (Average)", "Модуль разности (Difference)", "Копирование (Copy)", "Прозрачный ноль (Transparent-zero)"
        };

        public static bool MathNeedsValue(MathOp op)
        {
            switch (op)
            {
                case MathOp.Log:
                case MathOp.Exp:
                case MathOp.Square:
                case MathOp.Sqrt:
                case MathOp.Abs:
                case MathOp.Reciprocal:
                    return false;
            }
            return true;
        }

        public static string MathName(MathOp op)
        {
            switch (op)
            {
                case MathOp.Add: return "Прибавить (Add)";
                case MathOp.Subtract: return "Вычесть (Subtract)";
                case MathOp.Multiply: return "Умножить (Multiply)";
                case MathOp.Divide: return "Разделить (Divide)";
                case MathOp.Min: return "Минимум (Min)";
                case MathOp.Max: return "Максимум (Max)";
                case MathOp.Gamma: return "Гамма (Gamma)";
                case MathOp.Set: return "Установить (Set)";
                case MathOp.Log: return "Логарифм (Log)";
                case MathOp.Exp: return "Экспонента (Exp)";
                case MathOp.Square: return "Квадрат (Square)";
                case MathOp.Sqrt: return "Корень (Square Root)";
                case MathOp.Abs: return "Модуль (Abs)";
                case MathOp.Reciprocal: return "Обратное 1/x (Reciprocal)";
            }
            return op.ToString();
        }

        /// <summary>Process › Math. Возвращает новое изображение.</summary>
        public static ImageData ApplyMath(ImageData src, MathOp op, double value)
        {
            var r = src.Clone();
            bool integer = src.IsInteger;
            double max = integer ? src.MaxValue : 0;
            for (int c = 0; c < r.Channels; c++)
            {
                float[] p = r.Planes[c];
                for (int i = 0; i < p.Length; i++)
                {
                    double v = p[i];
                    switch (op)
                    {
                        case MathOp.Add: v += value; break;
                        case MathOp.Subtract: v -= value; break;
                        case MathOp.Multiply: v *= value; break;
                        case MathOp.Divide:
                            if (value == 0) v = integer ? v : LabOptions.DivideByZeroValue;
                            else v /= value;
                            break;
                        case MathOp.Min: if (v < value) v = value; break; // как в ImageJ: «Min» ограничивает снизу
                        case MathOp.Max: if (v > value) v = value; break; // «Max» ограничивает сверху
                        case MathOp.Gamma:
                            if (integer)
                            {
                                // ImageJ: exp(log(v/range)*gamma)*range
                                v = v <= 0 ? 0 : Math.Exp(Math.Log(v / max) * value) * max;
                            }
                            else v = v <= 0 ? 0 : Math.Pow(v, value);
                            break;
                        case MathOp.Set: v = value; break;
                        case MathOp.Log:
                            // ImageJ для 8/16 бит: f(p) = ln(p)·max/ln(max)
                            if (integer) v = v <= 0 ? 0 : Math.Log(v) * (max / Math.Log(max));
                            else v = v <= 0 ? double.NaN : Math.Log(v);
                            break;
                        case MathOp.Exp:
                            if (integer) v = Math.Exp(v * (Math.Log(max) / max));
                            else v = Math.Exp(v);
                            break;
                        case MathOp.Square: v = v * v; break;
                        case MathOp.Sqrt: v = v <= 0 ? 0 : Math.Sqrt(v); break;
                        case MathOp.Abs: v = Math.Abs(v); break;
                        case MathOp.Reciprocal:
                            v = v == 0 ? (integer ? 0 : LabOptions.DivideByZeroValue) : 1.0 / v;
                            break;
                    }
                    p[i] = (float)v;
                }
            }
            r.Conform();
            return r;
        }

        /// <summary>
        /// Process › Image Calculator: результат = img1 (op) img2.
        /// Обрабатывается область пересечения, начиная с левого верхнего угла (как в ImageJ).
        /// </summary>
        public static ImageData Calculate(ImageData a, ImageData b, CalcOp op, bool floatResult)
        {
            PixelType rt;
            bool rgb = a.IsRgb || b.IsRgb;
            if (floatResult) rt = rgb ? PixelType.Rgb32 : PixelType.Gray32;
            else rt = a.Type;

            ImageData A = ConvertTo(a, rt, false);
            ImageData B = ConvertTo(b, rt, false);
            var r = A.Clone();
            r.Name = a.Name;
            int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
            bool integer = r.IsInteger;
            float max = rt == PixelType.Gray16 ? 65535f : 255f;
            for (int c = 0; c < r.Channels; c++)
            {
                float[] pa = A.Planes[c], pb = B.Planes[c], pr = r.Planes[c];
                for (int y = 0; y < h; y++)
                {
                    int ia = y * A.Width, ib = y * B.Width;
                    for (int x = 0; x < w; x++, ia++, ib++)
                    {
                        float v1 = pa[ia], v2 = pb[ib];
                        float v;
                        switch (op)
                        {
                            case CalcOp.Add: v = v1 + v2; break;
                            case CalcOp.Subtract: v = v1 - v2; break;
                            case CalcOp.Multiply: v = v1 * v2; break;
                            case CalcOp.Divide:
                                if (v2 == 0)
                                {
                                    if (integer) v = max; // ImageJ (ByteBlitter): деление на 0 даёт максимум
                                    else v = v1 == 0 ? float.NaN : (v1 > 0 ? LabOptions.DivideByZeroValue : -LabOptions.DivideByZeroValue);
                                }
                                else v = integer ? (float)Math.Floor(v1 / v2) : v1 / v2;
                                break;
                            case CalcOp.And: v = (int)v1 & (int)v2; break;
                            case CalcOp.Or: v = (int)v1 | (int)v2; break;
                            case CalcOp.Xor: v = (int)v1 ^ (int)v2; break;
                            case CalcOp.Min: v = Math.Min(v1, v2); break;
                            case CalcOp.Max: v = Math.Max(v1, v2); break;
                            case CalcOp.Average: v = integer ? (float)Math.Floor((v1 + v2) / 2f) : (v1 + v2) / 2f; break;
                            case CalcOp.Difference: v = Math.Abs(v1 - v2); break;
                            case CalcOp.Copy: v = v2; break;
                            case CalcOp.TransparentZero: v = v2 == 0 ? v1 : v2; break;
                            default: v = v1; break;
                        }
                        pr[ia] = v;
                    }
                }
            }
            r.Conform();
            return r;
        }

        /// <summary>Сумма набора изображений (по одному типу). floatResult=false повторяет последовательное
        /// 8-битное сложение ImageJ с отсечением на 255.</summary>
        public static ImageData Sum(IList<ImageData> images, bool floatResult)
        {
            if (images == null || images.Count == 0) throw new ArgumentException("Нет изображений.");
            ImageData acc = floatResult ? ConvertTo(images[0], images[0].IsRgb ? PixelType.Rgb32 : PixelType.Gray32, false) : images[0].Clone();
            for (int k = 1; k < images.Count; k++)
                acc = Calculate(acc, images[k], CalcOp.Add, floatResult);
            return acc;
        }

        /// <summary>Тип, в который переводится изображение (Image › Type).</summary>
        public static ImageData ConvertTo(ImageData src, PixelType type, bool scale = true)
        {
            if (src.Type == type) return src.Clone();
            ImageData r = new ImageData(src.Width, src.Height, type);
            r.Name = src.Name;
            r.FilePath = src.FilePath;
            int n = src.PixelCount;
            bool dstRgb = ImageData.IsRgbType(type);

            if (src.IsRgb && !dstRgb)
            {
                // RGB → полутоновое: яркость
                float[] l = src.LumaPlane();
                Array.Copy(l, r.Planes[0], n);
                if (type == PixelType.Gray8 && src.Type == PixelType.Rgb32 && scale)
                    ScaleInto(src, l, r.Planes[0], 255);
            }
            else if (!src.IsRgb && dstRgb)
            {
                float[] g = src.Planes[0];
                float[] tmp = g;
                if (type == PixelType.Rgb24 && (src.Type == PixelType.Gray32 || src.Type == PixelType.Gray16) && scale)
                {
                    tmp = new float[n];
                    ScaleInto(src, g, tmp, 255);
                }
                for (int c = 0; c < 3; c++) Array.Copy(tmp, r.Planes[c], n);
            }
            else if (src.IsRgb && dstRgb)
            {
                for (int c = 0; c < 3; c++)
                {
                    if (type == PixelType.Rgb24 && scale && src.Type == PixelType.Rgb32) ScaleInto(src, src.Planes[c], r.Planes[c], 255);
                    else Array.Copy(src.Planes[c], r.Planes[c], n);
                }
            }
            else
            {
                // полутоновое → полутоновое
                if ((type == PixelType.Gray8 && src.Type != PixelType.Gray8) && scale)
                    ScaleInto(src, src.Planes[0], r.Planes[0], 255);
                else if (type == PixelType.Gray16 && src.Type == PixelType.Gray32 && scale)
                    ScaleInto(src, src.Planes[0], r.Planes[0], 65535);
                else
                    Array.Copy(src.Planes[0], r.Planes[0], n);
            }
            r.Conform();
            return r;
        }

        /// <summary>Линейное масштабирование диапазона отображения в 0..max (как ImageJ при «Scale when converting»).</summary>
        private static void ScaleInto(ImageData src, float[] from, float[] to, float max)
        {
            double min = src.DisplayMin, dmax = src.DisplayMax;
            double k = dmax > min ? max / (dmax - min) : 1;
            for (int i = 0; i < from.Length; i++)
            {
                double v = (from[i] - min) * k;
                if (double.IsNaN(v)) v = 0;
                to[i] = (float)v;
            }
        }

        public static ImageData Crop(ImageData src, Rectangle rect)
        {
            rect = Rectangle.Intersect(rect, new Rectangle(0, 0, src.Width, src.Height));
            if (rect.Width <= 0 || rect.Height <= 0) throw new ArgumentException("Пустая область обрезки.");
            var r = new ImageData(rect.Width, rect.Height, src.Type);
            r.Name = src.Name;
            for (int c = 0; c < src.Channels; c++)
                for (int y = 0; y < rect.Height; y++)
                    Array.Copy(src.Planes[c], (rect.Y + y) * src.Width + rect.X, r.Planes[c], y * rect.Width, rect.Width);
            r.DisplayMin = src.DisplayMin;
            r.DisplayMax = src.DisplayMax;
            return r;
        }

        public static ImageData Invert(ImageData src)
        {
            var r = src.Clone();
            double hi = src.IsInteger ? src.MaxValue : src.DisplayMax, lo = src.IsInteger ? 0 : src.DisplayMin;
            for (int c = 0; c < r.Channels; c++)
            {
                float[] p = r.Planes[c];
                for (int i = 0; i < p.Length; i++) p[i] = (float)(hi + lo - p[i]);
            }
            r.Conform();
            return r;
        }

        public static ImageData FlipHorizontal(ImageData src)
        {
            var r = src.CreateEmpty();
            for (int c = 0; c < src.Channels; c++)
                for (int y = 0; y < src.Height; y++)
                {
                    int row = y * src.Width;
                    for (int x = 0; x < src.Width; x++) r.Planes[c][row + x] = src.Planes[c][row + src.Width - 1 - x];
                }
            r.DisplayMin = src.DisplayMin; r.DisplayMax = src.DisplayMax;
            return r;
        }

        public static ImageData FlipVertical(ImageData src)
        {
            var r = src.CreateEmpty();
            for (int c = 0; c < src.Channels; c++)
                for (int y = 0; y < src.Height; y++)
                    Array.Copy(src.Planes[c], (src.Height - 1 - y) * src.Width, r.Planes[c], y * src.Width, src.Width);
            r.DisplayMin = src.DisplayMin; r.DisplayMax = src.DisplayMax;
            return r;
        }

        public static ImageData Rotate90(ImageData src, bool clockwise)
        {
            var r = new ImageData(src.Height, src.Width, src.Type);
            r.Name = src.Name;
            int W = src.Width, H = src.Height;
            for (int c = 0; c < src.Channels; c++)
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int nx, ny;
                        if (clockwise) { nx = H - 1 - y; ny = x; }
                        else { nx = y; ny = W - 1 - x; }
                        r.Planes[c][ny * H + nx] = src.Planes[c][y * W + x];
                    }
            r.DisplayMin = src.DisplayMin; r.DisplayMax = src.DisplayMax;
            return r;
        }

        /// <summary>Переносит результат обработки только в прямоугольник roi (поведение ImageJ при наличии выделения).</summary>
        public static ImageData MergeInRoi(ImageData original, ImageData processed, Rectangle roi)
        {
            if (roi.IsEmpty || processed.Type != original.Type || processed.Width != original.Width || processed.Height != original.Height)
                return processed;
            roi = Rectangle.Intersect(roi, new Rectangle(0, 0, original.Width, original.Height));
            if (roi.Width == original.Width && roi.Height == original.Height) return processed;
            var r = original.Clone();
            for (int c = 0; c < r.Channels; c++)
                for (int y = roi.Top; y < roi.Bottom; y++)
                    Array.Copy(processed.Planes[c], y * r.Width + roi.X, r.Planes[c], y * r.Width + roi.X, roi.Width);
            r.ResetDisplayRange();
            return r;
        }

        /// <summary>Новое изображение (File › New › Image).</summary>
        public static ImageData NewImage(int w, int h, PixelType type, NewFill fill, double value = 0)
        {
            var r = new ImageData(w, h, type);
            double max = ImageData.IsFloatType(type) ? 1.0 : (type == PixelType.Gray16 ? 65535 : 255);
            for (int c = 0; c < r.Channels; c++)
            {
                float[] p = r.Planes[c];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        double v;
                        switch (fill)
                        {
                            case NewFill.White: v = max; break;
                            case NewFill.Black: v = 0; break;
                            case NewFill.Ramp: v = w > 1 ? max * x / (w - 1.0) : 0; break;
                            default: v = value; break;
                        }
                        p[y * w + x] = (float)v;
                    }
            }
            r.Conform();
            if (r.IsFloat && fill != NewFill.Ramp) { r.DisplayMin = 0; r.DisplayMax = Math.Max(1, max); }
            return r;
        }
    }

    public enum NewFill { White, Black, Ramp, Value }
}
