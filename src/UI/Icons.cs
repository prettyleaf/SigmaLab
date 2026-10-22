using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace VideoMeter.UI
{
    /// <summary>Векторные иконки 16×16 (рисуются кодом, масштабируются под DPI).</summary>
    public static class Icons
    {
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

        public static Bitmap Get(string name)
        {
            Bitmap b;
            if (Cache.TryGetValue(name, out b)) return b;
            int size = Theme.S(16);
            b = new Bitmap(size, size);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(size / 16f, size / 16f);
                Draw(g, name);
            }
            Cache[name] = b;
            return b;
        }

        private static void Draw(Graphics g, string name)
        {
            Color p = Theme.Primary, a = Theme.AccentDark;
            using (var pen = new Pen(p, 1.4f))
            using (var thin = new Pen(p, 1f))
            using (var fill = new SolidBrush(p))
            using (var acc = new SolidBrush(a))
            using (var accPen = new Pen(a, 1.6f))
            using (var light = new SolidBrush(Theme.Tint))
            {
                switch (name)
                {
                    case "open":
                        g.FillPolygon(light, new[] { new PointF(1, 4), new PointF(6, 4), new PointF(7.5f, 5.5f), new PointF(15, 5.5f), new PointF(15, 14), new PointF(1, 14) });
                        g.DrawPolygon(pen, new[] { new PointF(1, 3), new PointF(6, 3), new PointF(7.5f, 4.5f), new PointF(14, 4.5f), new PointF(14, 13.5f), new PointF(1, 13.5f) });
                        g.FillPolygon(acc, new[] { new PointF(3, 8), new PointF(15.5f, 8), new PointF(13.5f, 13.5f), new PointF(1.2f, 13.5f) });
                        break;
                    case "save":
                        g.FillRectangle(light, 2, 2, 12, 12);
                        g.DrawRectangle(pen, 2, 2, 12, 12);
                        g.FillRectangle(fill, 4.5f, 2, 7, 4.5f);
                        g.FillRectangle(Brushes.White, 9, 2.8f, 1.6f, 2.8f);
                        g.FillRectangle(acc, 4, 9, 8, 5);
                        break;
                    case "rect":
                        using (var dash = new Pen(p, 1.5f) { DashStyle = DashStyle.Dash })
                            g.DrawRectangle(dash, 2, 3, 12, 10);
                        g.FillRectangle(acc, 1, 2, 3, 3);
                        g.FillRectangle(acc, 12, 11, 3, 3);
                        break;
                    case "hand":
                        g.FillEllipse(light, 3, 5, 10, 10);
                        g.DrawLine(pen, 5, 3, 5, 10);
                        g.DrawLine(pen, 7.5f, 1.5f, 7.5f, 9);
                        g.DrawLine(pen, 10, 2.5f, 10, 9);
                        g.DrawLine(pen, 12.5f, 4, 12.5f, 10);
                        g.DrawArc(pen, 3, 6, 11, 9, 0, 180);
                        g.DrawLine(pen, 3, 9, 1.5f, 7.5f);
                        break;
                    case "zoomin":
                    case "zoomout":
                        g.FillEllipse(light, 1.5f, 1.5f, 10, 10);
                        g.DrawEllipse(pen, 1.5f, 1.5f, 10, 10);
                        using (var h = new Pen(p, 2.6f)) g.DrawLine(h, 10, 10, 14.5f, 14.5f);
                        g.DrawLine(accPen, 4, 6.5f, 9, 6.5f);
                        if (name == "zoomin") g.DrawLine(accPen, 6.5f, 4, 6.5f, 9);
                        break;
                    case "fit":
                        g.DrawRectangle(thin, 3.5f, 3.5f, 9, 9);
                        g.FillRectangle(light, 4, 4, 8, 8);
                        g.DrawLines(pen, new[] { new PointF(1, 5), new PointF(1, 1), new PointF(5, 1) });
                        g.DrawLines(pen, new[] { new PointF(11, 1), new PointF(15, 1), new PointF(15, 5) });
                        g.DrawLines(pen, new[] { new PointF(15, 11), new PointF(15, 15), new PointF(11, 15) });
                        g.DrawLines(pen, new[] { new PointF(5, 15), new PointF(1, 15), new PointF(1, 11) });
                        break;
                    case "hist":
                        float[] hs = { 2, 4, 7, 11, 13, 10, 6, 3, 1.5f };
                        for (int i = 0; i < hs.Length; i++) g.FillRectangle(i == 4 ? acc : fill, 1 + i * 1.6f, 15 - hs[i], 1.3f, hs[i]);
                        g.DrawLine(thin, 0.5f, 15.3f, 15.5f, 15.3f);
                        break;
                    case "measure":
                        using (var f = Theme.SafeFont(Theme.SerifNames, 11f, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel))
                            g.DrawString("σ", f, fill, -0.5f, -1);
                        using (var f = Theme.SafeFont(Theme.SansNames, 7f, FontStyle.Bold, GraphicsUnit.Pixel)) g.DrawString("M", f, acc, 8, 8);
                        break;
                    case "profile":
                        g.DrawLine(thin, 1, 15, 15, 15);
                        g.DrawLine(thin, 1, 1, 1, 15);
                        g.DrawCurve(accPen, new[] { new PointF(2, 12), new PointF(5, 5), new PointF(8, 10), new PointF(11, 3), new PointF(14.5f, 8) });
                        break;
                    case "stepchart":
                        for (int i = 0; i < 6; i++)
                        {
                            int v = 255 - i * 45;
                            using (var b = new SolidBrush(Color.FromArgb(v, v, v))) g.FillRectangle(b, 1 + i * 2.4f, 4, 2.4f, 9);
                        }
                        g.DrawRectangle(pen, 1, 4, 14.4f, 9);
                        g.DrawLine(accPen, 1, 2, 15, 2);
                        break;
                    case "series":
                        g.FillRectangle(Brushes.White, 1, 2, 14, 12);
                        g.DrawRectangle(thin, 1, 2, 14, 12);
                        g.FillRectangle(fill, 1, 2, 14, 3);
                        g.DrawLine(thin, 1, 8.5f, 15, 8.5f);
                        g.DrawLine(thin, 6, 5, 6, 14);
                        g.DrawLine(thin, 10.5f, 5, 10.5f, 14);
                        g.FillRectangle(acc, 7, 10, 3, 3);
                        break;
                    case "psnr":
                        g.FillRectangle(light, 1, 1, 9, 9);
                        g.DrawRectangle(pen, 1, 1, 9, 9);
                        g.FillRectangle(acc, 6, 6, 9, 9);
                        g.DrawRectangle(pen, 6, 6, 9, 9);
                        break;
                    case "frames":
                        g.FillRectangle(light, 5, 1, 10, 8);
                        g.DrawRectangle(thin, 5, 1, 10, 8);
                        g.FillRectangle(light, 3, 3.5f, 10, 8);
                        g.DrawRectangle(thin, 3, 3.5f, 10, 8);
                        g.FillRectangle(Brushes.White, 1, 6, 10, 8);
                        g.DrawRectangle(pen, 1, 6, 10, 8);
                        using (var f = Theme.SafeFont(Theme.SansNames, 7.5f, FontStyle.Bold, GraphicsUnit.Pixel)) g.DrawString("Σ", f, acc, 2.5f, 6.5f);
                        break;
                    case "undo":
                        g.DrawArc(pen, 3, 4, 11, 9, 200, 230);
                        g.FillPolygon(fill, new[] { new PointF(1, 4), new PointF(6.5f, 3), new PointF(4, 8.5f) });
                        break;
                    case "calc":
                        g.FillRectangle(light, 2, 1, 12, 14);
                        g.DrawRectangle(pen, 2, 1, 12, 14);
                        g.FillRectangle(fill, 4, 3, 8, 3);
                        for (int i = 0; i < 3; i++) for (int j = 0; j < 2; j++) g.FillRectangle(j == 1 && i == 2 ? acc : fill, 4 + i * 3, 8 + j * 3, 2, 2);
                        break;
                    case "noise":
                        var rnd = new Random(3);
                        for (int i = 0; i < 40; i++) g.FillRectangle(i % 4 == 0 ? acc : fill, (float)rnd.NextDouble() * 14 + 1, (float)rnd.NextDouble() * 14 + 1, 1.4f, 1.4f);
                        break;
                    case "filter":
                        g.FillPolygon(light, new[] { new PointF(1, 2), new PointF(15, 2), new PointF(9.5f, 8.5f), new PointF(9.5f, 14), new PointF(6.5f, 12.5f), new PointF(6.5f, 8.5f) });
                        g.DrawPolygon(pen, new[] { new PointF(1, 2), new PointF(15, 2), new PointF(9.5f, 8.5f), new PointF(9.5f, 14), new PointF(6.5f, 12.5f), new PointF(6.5f, 8.5f) });
                        break;
                    case "new":
                        g.FillPolygon(Brushes.White, new[] { new PointF(3, 1), new PointF(10, 1), new PointF(13, 4), new PointF(13, 15), new PointF(3, 15) });
                        g.DrawPolygon(pen, new[] { new PointF(3, 1), new PointF(10, 1), new PointF(13, 4), new PointF(13, 15), new PointF(3, 15) });
                        g.DrawLine(accPen, 8, 7, 8, 13);
                        g.DrawLine(accPen, 5, 10, 11, 10);
                        break;
                    case "help":
                        g.FillEllipse(fill, 1, 1, 14, 14);
                        using (var f = Theme.SafeFont(Theme.SansNames, 11f, FontStyle.Bold, GraphicsUnit.Pixel)) g.DrawString("?", f, Brushes.White, 4, 0.5f);
                        break;
                    case "video":
                        g.FillRectangle(light, 1, 3, 14, 10);
                        g.DrawRectangle(pen, 1, 3, 14, 10);
                        g.FillPolygon(acc, new[] { new PointF(6, 5.5f), new PointF(11, 8), new PointF(6, 10.5f) });
                        break;
                    case "lab":
                        g.FillPolygon(light, new[] { new PointF(6, 1), new PointF(10, 1), new PointF(10, 6), new PointF(14.5f, 14), new PointF(1.5f, 14), new PointF(6, 6) });
                        g.DrawPolygon(pen, new[] { new PointF(6, 1), new PointF(10, 1), new PointF(10, 6), new PointF(14.5f, 14), new PointF(1.5f, 14), new PointF(6, 6) });
                        g.FillPolygon(acc, new[] { new PointF(4, 10), new PointF(12, 10), new PointF(13.5f, 13), new PointF(2.5f, 13) });
                        break;
                }
            }
        }
    }
}
