using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VideoMeter.UI
{
    public sealed class ViewerOverlay
    {
        public Rectangle Rect;
        public Color Color;
        public string Label;
        public int FillAlpha;      // 0 — без заливки
        public bool Dashed;
    }

    public sealed class PixelEventArgs : EventArgs
    {
        public int X, Y;
        public bool Inside;
    }

    /// <summary>Просмотр изображения с масштабированием и прямоугольным выделением (ROI) в координатах изображения.</summary>
    public sealed class ImageViewer : Control
    {
        public enum ToolMode { Select, Pan }

        private static readonly double[] ZoomLevels =
        {
            1 / 32.0, 1 / 24.0, 1 / 16.0, 1 / 12.0, 1 / 8.0, 1 / 6.0, 1 / 4.0, 1 / 3.0, 1 / 2.0, 0.75,
            1, 1.5, 2, 3, 4, 6, 8, 12, 16, 24, 32
        };

        private Bitmap _bmp;
        private Size _size;
        private double _zoom = 1;
        private double _ox, _oy;   // координата изображения в левом верхнем углу элемента
        private Rectangle _roi;
        private bool _fitMode = true;

        private enum Drag { None, NewRoi, MoveRoi, Resize, Pan }
        private Drag _drag;
        private int _handle;       // 0..7 — маркеры изменения размера
        private Point _downScreen;
        private Point _downImg;
        private Rectangle _downRoi;
        private double _downOx, _downOy;
        private bool _space;

        public readonly List<ViewerOverlay> Overlays = new List<ViewerOverlay>();
        public ToolMode Tool = ToolMode.Select;
        public bool AllowRoi = true;
        public string EmptyText = "Откройте изображение: Файл › Открыть… (Ctrl+O)\nили перетащите файлы в это окно";

        public event EventHandler RoiChanged;
        public event EventHandler RoiChanging;
        public event EventHandler ZoomChanged;
        public event EventHandler<PixelEventArgs> PixelHover;

        public ImageViewer()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Canvas;
            TabStop = true;
        }

        public Bitmap Image { get { return _bmp; } }
        public double Zoom { get { return _zoom; } }
        public Size ImageSize { get { return _size; } }

        /// <summary>Выделение в координатах изображения; Rectangle.Empty — нет выделения.</summary>
        public Rectangle Roi
        {
            get { return _roi; }
            set
            {
                Rectangle r = Clip(value);
                if (r == _roi) return;
                _roi = r;
                Invalidate();
                RoiChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Устанавливает изображение. keepView — сохранить масштаб и положение (для того же документа).</summary>
        public void SetImage(Bitmap bmp, bool keepView)
        {
            Size newSize = bmp == null ? Size.Empty : bmp.Size;
            bool sizeChanged = _bmp == null || bmp == null || _size != newSize;
            _bmp = bmp;
            _size = newSize;
            if (!keepView || sizeChanged)
            {
                _fitMode = true;
                FitToWindow();
            }
            _roi = Clip(_roi);
            Invalidate();
        }

        public void SetView(double zoom, double ox, double oy)
        {
            _zoom = zoom; _ox = ox; _oy = oy; _fitMode = false;
            Invalidate();
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }

        public void GetView(out double zoom, out double ox, out double oy) { zoom = _zoom; ox = _ox; oy = _oy; }

        private Rectangle Clip(Rectangle r)
        {
            if (_bmp == null || r.Width <= 0 || r.Height <= 0) return Rectangle.Empty;
            r = Rectangle.Intersect(r, new Rectangle(0, 0, _size.Width, _size.Height));
            return r.Width > 0 && r.Height > 0 ? r : Rectangle.Empty;
        }

        // ------------------------------------------------------------------ масштаб

        public void FitToWindow()
        {
            _fitMode = true;
            if (_bmp == null || ClientSize.Width < 4 || ClientSize.Height < 4) return;
            double z = Math.Min((ClientSize.Width - 8.0) / _size.Width, (ClientSize.Height - 8.0) / _size.Height);
            if (z <= 0) z = 1;
            _zoom = z;
            Center();
            Invalidate();
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ActualSize()
        {
            ZoomAt(1, new Point(ClientSize.Width / 2, ClientSize.Height / 2));
            Center();
        }

        private void Center()
        {
            if (_bmp == null) return;
            _ox = _size.Width / 2.0 - ClientSize.Width / (2 * _zoom);
            _oy = _size.Height / 2.0 - ClientSize.Height / (2 * _zoom);
        }

        public void ZoomIn() { StepZoom(+1, new Point(ClientSize.Width / 2, ClientSize.Height / 2)); }
        public void ZoomOut() { StepZoom(-1, new Point(ClientSize.Width / 2, ClientSize.Height / 2)); }

        private void StepZoom(int dir, Point anchor)
        {
            double next = _zoom;
            if (dir > 0)
            {
                foreach (double z in ZoomLevels) if (z > _zoom * 1.001) { next = z; break; }
            }
            else
            {
                for (int i = ZoomLevels.Length - 1; i >= 0; i--) if (ZoomLevels[i] < _zoom * 0.999) { next = ZoomLevels[i]; break; }
            }
            ZoomAt(next, anchor);
        }

        private void ZoomAt(double z, Point anchor)
        {
            if (_bmp == null) return;
            double ix = _ox + anchor.X / _zoom, iy = _oy + anchor.Y / _zoom;
            _zoom = z;
            _ox = ix - anchor.X / _zoom;
            _oy = iy - anchor.Y / _zoom;
            _fitMode = false;
            ConstrainPan();
            Invalidate();
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ConstrainPan()
        {
            if (_bmp == null) return;
            double vw = ClientSize.Width / _zoom, vh = ClientSize.Height / _zoom;
            if (vw >= _size.Width) _ox = _size.Width / 2.0 - vw / 2;
            else _ox = Math.Max(-vw * 0.25, Math.Min(_size.Width - vw * 0.75, _ox));
            if (vh >= _size.Height) _oy = _size.Height / 2.0 - vh / 2;
            else _oy = Math.Max(-vh * 0.25, Math.Min(_size.Height - vh * 0.75, _oy));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_fitMode) FitToWindow();
            else ConstrainPan();
        }

        // ------------------------------------------------------------------ координаты

        public PointF ToScreen(double ix, double iy) { return new PointF((float)((ix - _ox) * _zoom), (float)((iy - _oy) * _zoom)); }

        public RectangleF ToScreen(Rectangle r)
        {
            PointF a = ToScreen(r.Left, r.Top), b = ToScreen(r.Right, r.Bottom);
            return RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y);
        }

        public Point ToImage(Point s) { return new Point((int)Math.Floor(_ox + s.X / _zoom), (int)Math.Floor(_oy + s.Y / _zoom)); }

        private Point ToImageRounded(Point s)
        {
            int x = (int)Math.Round(_ox + s.X / _zoom), y = (int)Math.Round(_oy + s.Y / _zoom);
            if (_bmp != null)
            {
                x = Math.Max(0, Math.Min(_size.Width, x));
                y = Math.Max(0, Math.Min(_size.Height, y));
            }
            return new Point(x, y);
        }

        // ------------------------------------------------------------------ отрисовка

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (_bmp == null)
            {
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var br = new SolidBrush(Color.FromArgb(220, 220, 235)))
                    g.DrawString(EmptyText, Theme.UiFont, br, ClientRectangle, sf);
                return;
            }
            RectangleF dest = ToScreen(new Rectangle(0, 0, _size.Width, _size.Height));
            g.InterpolationMode = _zoom >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            // рисуем только видимую часть (быстрее на больших увеличениях)
            RectangleF vis = RectangleF.Intersect(dest, ClientRectangle);
            if (vis.Width > 0 && vis.Height > 0)
            {
                double sx0 = Math.Max(0, Math.Floor(_ox + vis.Left / _zoom)), sy0 = Math.Max(0, Math.Floor(_oy + vis.Top / _zoom));
                double sx1 = Math.Min(_size.Width, Math.Ceiling(_ox + vis.Right / _zoom)), sy1 = Math.Min(_size.Height, Math.Ceiling(_oy + vis.Bottom / _zoom));
                var src = new RectangleF((float)sx0, (float)sy0, (float)(sx1 - sx0), (float)(sy1 - sy0));
                RectangleF dst = ToScreen(Rectangle.FromLTRB((int)sx0, (int)sy0, (int)sx1, (int)sy1));
                try
                {
                    using (var ia = new System.Drawing.Imaging.ImageAttributes())
                    {
                        ia.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(_bmp, new[] { dst.Location, new PointF(dst.Right, dst.Top), new PointF(dst.Left, dst.Bottom) }, src, GraphicsUnit.Pixel, ia);
                    }
                }
                catch (ArgumentException) { } // растр уже заменён — следующая перерисовка покажет новый
            }
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.InterpolationMode = InterpolationMode.Default;
            using (var border = new Pen(Color.FromArgb(40, 40, 50)))
                g.DrawRectangle(border, dest.X - 1, dest.Y - 1, dest.Width + 1, dest.Height + 1);

            g.SmoothingMode = SmoothingMode.None;
            foreach (var o in Overlays) DrawOverlay(g, o);
            if (!_roi.IsEmpty) DrawRoi(g);
        }

        private void DrawOverlay(Graphics g, ViewerOverlay o)
        {
            RectangleF r = ToScreen(o.Rect);
            if (o.FillAlpha > 0)
                using (var b = new SolidBrush(Color.FromArgb(o.FillAlpha, o.Color)))
                    g.FillRectangle(b, r);
            using (var p = new Pen(o.Color, Theme.S(1.5f)))
            {
                if (o.Dashed) p.DashStyle = DashStyle.Dash;
                g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            }
            if (!string.IsNullOrEmpty(o.Label))
            {
                SizeF sz = g.MeasureString(o.Label, Theme.SmallFont);
                float ly = r.Y - sz.Height - 1;
                if (ly < 0) ly = r.Y + 1;
                using (var b = new SolidBrush(Color.FromArgb(200, o.Color)))
                    g.FillRectangle(b, r.X, ly, sz.Width, sz.Height);
                g.DrawString(o.Label, Theme.SmallFont, Brushes.Black, r.X, ly);
            }
        }

        private void DrawRoi(Graphics g)
        {
            RectangleF r = ToScreen(_roi);
            using (var p = new Pen(Theme.RoiColor, Theme.S(1.5f)))
                g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            float hs = Theme.S(3f);
            foreach (PointF h in Handles(r))
            {
                g.FillRectangle(Brushes.White, h.X - hs, h.Y - hs, hs * 2, hs * 2);
                g.DrawRectangle(Pens.Black, h.X - hs, h.Y - hs, hs * 2, hs * 2);
            }
            string txt = _roi.Width + "×" + _roi.Height;
            g.DrawString(txt, Theme.SmallFont, Brushes.Black, r.X + 1, r.Bottom + 3);
            using (var b = new SolidBrush(Theme.RoiColor)) g.DrawString(txt, Theme.SmallFont, b, r.X, r.Bottom + 2);
        }

        private static PointF[] Handles(RectangleF r)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            return new[]
            {
                new PointF(r.Left, r.Top), new PointF(cx, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, cy),
                new PointF(r.Right, r.Bottom), new PointF(cx, r.Bottom), new PointF(r.Left, r.Bottom), new PointF(r.Left, cy)
            };
        }

        private int HitHandle(Point p)
        {
            if (_roi.IsEmpty) return -1;
            PointF[] hs = Handles(ToScreen(_roi));
            float tol = Theme.S(6f);
            for (int i = 0; i < hs.Length; i++)
                if (Math.Abs(hs[i].X - p.X) <= tol && Math.Abs(hs[i].Y - p.Y) <= tol) return i;
            return -1;
        }

        // ------------------------------------------------------------------ мышь и клавиатура

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (_bmp == null) return;
            _downScreen = e.Location;
            _downOx = _ox; _downOy = _oy;
            if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right || (e.Button == MouseButtons.Left && (Tool == ToolMode.Pan || _space)))
            {
                _drag = Drag.Pan;
                Cursor = Cursors.SizeAll;
                return;
            }
            if (e.Button != MouseButtons.Left || !AllowRoi) return;
            int h = HitHandle(e.Location);
            _downRoi = _roi;
            _downImg = ToImageRounded(e.Location);
            if (h >= 0) { _drag = Drag.Resize; _handle = h; }
            else if (!_roi.IsEmpty && ToScreen(_roi).Contains(e.Location)) _drag = Drag.MoveRoi;
            else
            {
                _drag = Drag.NewRoi;
                _roi = Rectangle.Empty;
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_bmp == null) return;
            Point ip = ToImage(e.Location);
            PixelHover?.Invoke(this, new PixelEventArgs { X = ip.X, Y = ip.Y, Inside = ip.X >= 0 && ip.Y >= 0 && ip.X < _size.Width && ip.Y < _size.Height });

            switch (_drag)
            {
                case Drag.Pan:
                    _ox = _downOx - (e.X - _downScreen.X) / _zoom;
                    _oy = _downOy - (e.Y - _downScreen.Y) / _zoom;
                    _fitMode = false;
                    ConstrainPan();
                    Invalidate();
                    return;
                case Drag.NewRoi:
                    {
                        Point a = _downImg, b = ToImageRounded(e.Location);
                        _roi = Clip(Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
                        Invalidate();
                        RoiChanging?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                case Drag.MoveRoi:
                    {
                        Point b = ToImageRounded(e.Location);
                        int dx = b.X - _downImg.X, dy = b.Y - _downImg.Y;
                        var r = _downRoi;
                        r.X = Math.Max(0, Math.Min(_size.Width - r.Width, r.X + dx));
                        r.Y = Math.Max(0, Math.Min(_size.Height - r.Height, r.Y + dy));
                        _roi = r;
                        Invalidate();
                        RoiChanging?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                case Drag.Resize:
                    {
                        Point b = ToImageRounded(e.Location);
                        int l = _downRoi.Left, t = _downRoi.Top, rr = _downRoi.Right, bb = _downRoi.Bottom;
                        switch (_handle)
                        {
                            case 0: l = b.X; t = b.Y; break;
                            case 1: t = b.Y; break;
                            case 2: rr = b.X; t = b.Y; break;
                            case 3: rr = b.X; break;
                            case 4: rr = b.X; bb = b.Y; break;
                            case 5: bb = b.Y; break;
                            case 6: l = b.X; bb = b.Y; break;
                            case 7: l = b.X; break;
                        }
                        _roi = Clip(Rectangle.FromLTRB(Math.Min(l, rr), Math.Min(t, bb), Math.Max(l, rr), Math.Max(t, bb)));
                        Invalidate();
                        RoiChanging?.Invoke(this, EventArgs.Empty);
                        return;
                    }
            }
            // курсор
            if (Tool == ToolMode.Pan || _space) Cursor = Cursors.Hand;
            else if (!AllowRoi) Cursor = Cursors.Default;
            else
            {
                int h = HitHandle(e.Location);
                if (h == 0 || h == 4) Cursor = Cursors.SizeNWSE;
                else if (h == 2 || h == 6) Cursor = Cursors.SizeNESW;
                else if (h == 1 || h == 5) Cursor = Cursors.SizeNS;
                else if (h == 3 || h == 7) Cursor = Cursors.SizeWE;
                else if (!_roi.IsEmpty && ToScreen(_roi).Contains(e.Location)) Cursor = Cursors.SizeAll;
                else Cursor = Cursors.Cross;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Drag d = _drag;
            _drag = Drag.None;
            if (d == Drag.NewRoi || d == Drag.MoveRoi || d == Drag.Resize)
            {
                if (_roi.Width < 2 || _roi.Height < 2)
                {
                    // щелчок без протягивания снимает выделение
                    if (d == Drag.NewRoi) _roi = Rectangle.Empty;
                    else _roi = _downRoi;
                }
                Invalidate();
                RoiChanged?.Invoke(this, EventArgs.Empty);
            }
            if (d == Drag.Pan) Cursor = Tool == ToolMode.Pan ? Cursors.Hand : Cursors.Cross;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            PixelHover?.Invoke(this, new PixelEventArgs { Inside = false });
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_bmp == null) return;
            StepZoom(e.Delta > 0 ? +1 : -1, e.Location);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right) FitToWindow();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space) { _space = true; Cursor = Cursors.Hand; }
            if (_roi.IsEmpty || _bmp == null) return;
            int step = e.Shift ? 10 : 1;
            int dx = 0, dy = 0;
            switch (e.KeyCode)
            {
                case Keys.Left: dx = -step; break;
                case Keys.Right: dx = step; break;
                case Keys.Up: dy = -step; break;
                case Keys.Down: dy = step; break;
                default: return;
            }
            var r = _roi;
            if (e.Alt)
            {
                // Alt + стрелки — изменение размера
                r.Width = Math.Max(1, Math.Min(_size.Width - r.X, r.Width + dx));
                r.Height = Math.Max(1, Math.Min(_size.Height - r.Y, r.Height + dy));
            }
            else
            {
                r.X = Math.Max(0, Math.Min(_size.Width - r.Width, r.X + dx));
                r.Y = Math.Max(0, Math.Min(_size.Height - r.Height, r.Y + dy));
            }
            Roi = r;
            e.Handled = true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space) { _space = false; Cursor = Tool == ToolMode.Pan ? Cursors.Hand : Cursors.Cross; }
        }
    }
}
