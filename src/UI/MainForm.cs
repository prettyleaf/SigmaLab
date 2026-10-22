using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SigmaLab.Core;
using SigmaLab.IO;

namespace SigmaLab.UI
{
    public sealed partial class MainForm : Form
    {
        private readonly Workspace _ws = new Workspace();
        private ImageViewer _viewer;
        private ListBox _list;
        private ToolStripStatusLabel _stPos, _stValue, _stInfo, _stZoom;
        private ToolStripButton _tbSelect, _tbPan;
        private ComboBox _channel;
        private Label _roiCaption;
        private readonly Dictionary<string, Label> _stat = new Dictionary<string, Label>();
        private ListBox _roiList;
        private CheckBox _showRois;
        private DataGridView _results;
        private SplitContainer _splitMain, _splitLeft, _splitRight;
        private Bitmap _previewBmp;
        private int _resultNo;

        public MainForm()
        {
            Theme.ApplyForm(this);
            Text = Ui.AppName;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Theme.S(1360), Theme.S(860));
            MinimumSize = new Size(Theme.S(900), Theme.S(600));
            if (Theme.FitToScreen(this)) WindowState = FormWindowState.Maximized;
            KeyPreview = true;
            AllowDrop = true;

            BuildLayout();
            var tool = BuildToolbar();
            var menu = BuildMenu();
            var status = BuildStatus();
            Controls.Add(_splitMain);
            Controls.Add(status);
            Controls.Add(tool);
            Controls.Add(menu);
            MainMenuStrip = menu;

            _ws.DocsChanged += (s, e) => RefreshList();
            _ws.ActiveChanged += (s, e) => OnActiveChanged();
            _ws.ActiveDataChanged += (s, e) => OnActiveDataChanged();
            _ws.RoisChanged += (s, e) => { UpdateOverlays(); UpdateRoiInfo(); };

            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null) OpenFiles(files);
            };
            OnActiveChanged();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                _splitMain.SplitterDistance = Math.Max(Theme.S(200), _splitMain.Height - Theme.S(190));
                _splitLeft.SplitterDistance = Theme.S(210);
                _splitRight.SplitterDistance = Math.Max(Theme.S(300), _splitRight.Width - Theme.S(318));
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.CloseReason == CloseReason.UserClosing && _ws.Docs.Exists(d => d.Modified))
            {
                if (!Ui.Ask(this, "Есть изменённые и несохранённые изображения. Выйти из программы?"))
                {
                    e.Cancel = true;
                    return;
                }
            }
            Settings.Save();
        }

        // ------------------------------------------------------------------ раскладка

        private void BuildLayout()
        {
            _splitMain = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2, SplitterWidth = Theme.S(5), BackColor = Theme.Tint };
            _splitLeft = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel1, SplitterWidth = Theme.S(4), BackColor = Theme.Tint };
            _splitRight = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2, SplitterWidth = Theme.S(4), BackColor = Theme.Tint };
            _splitMain.Panel1.Controls.Add(_splitLeft);
            _splitLeft.Panel2.Controls.Add(_splitRight);

            // --- список изображений
            _list = new ListBox
            {
                Dock = DockStyle.Fill,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Theme.S(54),
                IntegralHeight = false,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White
            };
            _list.DrawItem += DrawListItem;
            _list.SelectedIndexChanged += (s, e) =>
            {
                if (_list.SelectedIndex >= 0 && _list.SelectedIndex < _ws.Docs.Count) _ws.Active = _ws.Docs[_list.SelectedIndex];
            };
            _list.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    int i = _list.IndexFromPoint(e.Location);
                    if (i >= 0) _list.SelectedIndex = i;
                }
            };
            var lm = new ContextMenuStrip { Renderer = Theme.Renderer };
            lm.Items.Add("Переименовать…", null, (s, e) => Rename());
            lm.Items.Add("Дублировать", null, (s, e) => Duplicate());
            lm.Items.Add("Сохранить как…", Icons.Get("save"), (s, e) => SaveAs());
            lm.Items.Add("Гистограмма", Icons.Get("hist"), (s, e) => ShowHistogram());
            lm.Items.Add(new ToolStripSeparator());
            lm.Items.Add("Закрыть", null, (s, e) => CloseActive());
            _list.ContextMenuStrip = lm;
            var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            leftPanel.Controls.Add(_list);
            leftPanel.Controls.Add(Theme.MakeHeader("Изображения"));
            _splitLeft.Panel1.Controls.Add(leftPanel);

            // --- просмотр
            _viewer = new ImageViewer { Dock = DockStyle.Fill };
            _viewer.RoiChanged += (s, e) => { if (_ws.Active != null) _ws.Active.Roi = _viewer.Roi; UpdateRoiInfo(); };
            _viewer.RoiChanging += (s, e) => UpdateRoiInfo();
            _viewer.ZoomChanged += (s, e) => _stZoom.Text = "Масштаб: " + Math.Round(_viewer.Zoom * 100) + "%";
            _viewer.PixelHover += (s, e) =>
            {
                var d = _ws.Active;
                if (d == null || !e.Inside) { _stPos.Text = ""; _stValue.Text = ""; return; }
                _stPos.Text = "x=" + e.X + "  y=" + e.Y;
                _stValue.Text = "значение: " + d.Data.ValueString(e.X, e.Y);
            };
            _splitRight.Panel1.Controls.Add(_viewer);

            // --- правая панель
            _splitRight.Panel2.Controls.Add(BuildRightPanel());

            // --- результаты
            _splitMain.Panel2.Controls.Add(BuildResults());
        }

        private Control BuildRightPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, AutoScroll = true };
            var flow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(Theme.S(8), Theme.S(6), Theme.S(8), Theme.S(6)) };

            _roiCaption = new Label { AutoSize = true, ForeColor = Theme.Muted, MaximumSize = new Size(Theme.S(260), 0), Margin = new Padding(0, 0, 0, Theme.S(4)) };
            flow.Controls.Add(_roiCaption);

            _channel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(250), Margin = new Padding(0, 0, 0, Theme.S(6)) };
            _channel.Items.AddRange(new object[] { "Яркость", "Канал R", "Канал G", "Канал B" });
            _channel.SelectedIndex = 0;
            _channel.SelectedIndexChanged += (s, e) => UpdateRoiInfo();
            flow.Controls.Add(_channel);

            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(140)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            string[][] rows =
            {
                new[] { "n", "Пикселей (N)" }, new[] { "mean", "Среднее (Mean)" }, new[] { "sd", "СКО (StdDev)" },
                new[] { "min", "Минимум (Min)" }, new[] { "max", "Максимум (Max)" }
            };
            foreach (var r in rows)
            {
                var k = new Label { Text = r[1], AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, Theme.S(2), 0, Theme.S(2)) };
                var v = new Label { Text = "—", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, Theme.S(2), 0, Theme.S(2)) };
                grid.Controls.Add(k);
                grid.Controls.Add(v);
                _stat[r[0]] = v;
            }
            flow.Controls.Add(grid);

            var btns = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, Theme.S(6), 0, 0), MaximumSize = new Size(Theme.S(270), 0) };
            var bh = Theme.MakeButton("Гистограмма");
            bh.Image = Icons.Get("hist"); bh.TextImageRelation = TextImageRelation.ImageBeforeText;
            bh.Click += (s, e) => ShowHistogram();
            var bm = Theme.MakeButton("Измерить");
            bm.Image = Icons.Get("measure"); bm.TextImageRelation = TextImageRelation.ImageBeforeText;
            bm.Click += (s, e) => Measure();
            btns.Controls.Add(bh);
            btns.Controls.Add(bm);
            flow.Controls.Add(btns);

            // менеджер областей (аналог ROI Manager в ImageJ)
            var hdr2 = Theme.MakeHeader("Области (ROI Manager)");
            hdr2.Dock = DockStyle.None;
            hdr2.Width = Theme.S(270);
            hdr2.Margin = new Padding(0, Theme.S(12), 0, Theme.S(6));
            flow.Controls.Add(hdr2);
            _roiList = new ListBox { Width = Theme.S(270), Height = Theme.S(110), IntegralHeight = false };
            _roiList.SelectedIndexChanged += (s, e) =>
            {
                int i = _roiList.SelectedIndex;
                if (i >= 0 && i < _ws.Rois.Count && _ws.Active != null) _viewer.Roi = _ws.Rois[i].Rect;
            };
            flow.Controls.Add(_roiList);
            var rb = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(Theme.S(276), 0), Margin = Padding.Empty };
            Func<string, string, Action, Button> small = (text, tip, act) =>
            {
                var b = Theme.MakeButton(text);
                b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                b.MinimumSize = new Size(Theme.S(56), Theme.S(24));
                b.Padding = new Padding(Theme.S(2), 0, Theme.S(2), 0);
                b.Margin = new Padding(0, Theme.S(3), Theme.S(4), Theme.S(1));
                new ToolTip().SetToolTip(b, tip);
                b.Click += (s, e) => act();
                rb.Controls.Add(b);
                return b;
            };
            small("Добавить", "Добавить текущее выделение в список (Ctrl+T)", AddRoi);
            small("Удалить", "Удалить выбранную область из списка", DeleteRoi);
            small("Переименовать", "Переименовать выбранную область", RenameRoi);
            small("Измерить", "Измерить все области списка на активном изображении (Measure)", MeasureRois);
            flow.Controls.Add(rb);
            _showRois = new CheckBox { Text = "Показывать все области (Show All)", Checked = true, AutoSize = true, Margin = new Padding(0, Theme.S(4), 0, 0) };
            _showRois.CheckedChanged += (s, e) => UpdateOverlays();
            flow.Controls.Add(_showRois);

            var tip = new Label
            {
                Text = "Мышь: левая кнопка — выделение, колесо — масштаб, правая кнопка или пробел — перемещение. Стрелки сдвигают выделение (Shift — на 10 пикс., Alt — меняют размер). " +
                       "Области из списка можно выделять на любом открытом изображении.",
                AutoSize = true, MaximumSize = new Size(Theme.S(270), 0), ForeColor = Theme.Muted, Font = Theme.SmallFont,
                Margin = new Padding(0, Theme.S(14), 0, 0)
            };
            flow.Controls.Add(tip);

            panel.Controls.Add(flow);
            panel.Controls.Add(Theme.MakeHeader("Выделение (ROI)"));
            return panel;
        }

        private Control BuildResults()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            _results = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = true,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
                EnableHeadersVisualStyles = false,
                GridColor = Theme.TintLight
            };
            _results.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
            _results.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
            _results.ColumnHeadersDefaultCellStyle.Font = Theme.BoldFont;
            _results.DefaultCellStyle.SelectionBackColor = Theme.TintLight;
            _results.DefaultCellStyle.SelectionForeColor = Theme.Text;
            _results.AlternatingRowsDefaultCellStyle.BackColor = Theme.SurfaceAlt;
            foreach (string c in new[] { "№", "Изображение", "Область", "X", "Y", "Ширина", "Высота", "N", "Mean", "StdDev", "Min", "Max" })
                _results.Columns.Add(c, c);
            _results.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.S(32), BackColor = Theme.Surface, Padding = new Padding(Theme.S(4), 0, 0, 0), WrapContents = false };
            var title = new Label { Text = "Результаты измерений", Font = Theme.HeaderFont, ForeColor = Theme.Primary, AutoSize = true, Margin = new Padding(Theme.S(4), Theme.S(8), Theme.S(16), 0) };
            bar.Controls.Add(title);
            foreach (var t in new[]
            {
                new KeyValuePair<string, Action>("Копировать", CopyResults),
                new KeyValuePair<string, Action>("Сохранить CSV…", SaveResults),
                new KeyValuePair<string, Action>("Удалить строку", () => { foreach (DataGridViewRow r in _results.SelectedRows) _results.Rows.Remove(r); }),
                new KeyValuePair<string, Action>("Очистить", () => { _results.Rows.Clear(); _resultNo = 0; })
            })
            {
                var b = Theme.MakeButton(t.Key);
                b.MinimumSize = new Size(Theme.S(80), Theme.S(24));
                b.Margin = new Padding(Theme.S(2), Theme.S(4), Theme.S(2), 0);
                Action act = t.Value;
                b.Click += (s, e) => act();
                bar.Controls.Add(b);
            }
            panel.Controls.Add(_results);
            panel.Controls.Add(bar);
            return panel;
        }

        private StatusStrip BuildStatus()
        {
            var st = new StatusStrip { Renderer = Theme.Renderer, BackColor = Theme.Primary, SizingGrip = true };
            _stInfo = new ToolStripStatusLabel { ForeColor = Color.White, Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _stPos = new ToolStripStatusLabel { ForeColor = Color.White, AutoSize = false, Width = Theme.S(130) };
            _stValue = new ToolStripStatusLabel { ForeColor = Color.White, AutoSize = false, Width = Theme.S(240), TextAlign = ContentAlignment.MiddleLeft };
            _stZoom = new ToolStripStatusLabel { ForeColor = Color.White, AutoSize = false, Width = Theme.S(120) };
            st.Items.AddRange(new ToolStripItem[] { _stInfo, _stPos, _stValue, _stZoom });
            return st;
        }

        private void DrawListItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _ws.Docs.Count) return;
            var doc = _ws.Docs[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(sel ? Theme.TintLight : Color.White)) e.Graphics.FillRectangle(bg, e.Bounds);
            if (sel) using (var b = new SolidBrush(Theme.Primary)) e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y, Theme.S(4), e.Bounds.Height);
            int ts = Theme.S(44);
            int pad = (e.Bounds.Height - ts) / 2;
            var tr = new Rectangle(e.Bounds.X + Theme.S(8), e.Bounds.Y + pad, ts, ts);
            using (var cb = new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(cb, tr);
            try { e.Graphics.DrawImage(doc.Thumbnail(ts), tr); } catch { }
            int tx = tr.Right + Theme.S(8);
            var nameRect = new Rectangle(tx, e.Bounds.Y + pad, e.Bounds.Right - tx - Theme.S(4), Theme.S(20));
            TextRenderer.DrawText(e.Graphics, (doc.Modified ? "• " : "") + doc.Name, sel ? Theme.BoldFont : Theme.UiFont, nameRect, Theme.Text,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
            var infoRect = new Rectangle(tx, nameRect.Bottom, nameRect.Width, Theme.S(18));
            TextRenderer.DrawText(e.Graphics, doc.Data.Description, Theme.SmallFont, infoRect, Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.Left);
            using (var p = new Pen(Theme.TintLight)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        // ------------------------------------------------------------------ синхронизация

        private bool _refreshingList;

        private void RefreshList()
        {
            _refreshingList = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var d in _ws.Docs) _list.Items.Add(d.Name);
            int i = _ws.Active == null ? -1 : _ws.Docs.IndexOf(_ws.Active);
            if (i >= 0) _list.SelectedIndex = i;
            _list.EndUpdate();
            _refreshingList = false;
            _list.Invalidate();
        }

        private ImageDoc _shownDoc;

        private void OnActiveChanged()
        {
            if (_refreshingList) return;
            // запомнить вид прежнего изображения
            if (_shownDoc != null && _ws.Docs.Contains(_shownDoc))
            {
                double z, ox, oy;
                _viewer.GetView(out z, out ox, out oy);
                _shownDoc.ViewZoom = z; _shownDoc.ViewOx = ox; _shownDoc.ViewOy = oy;
            }
            var doc = _ws.Active;
            _shownDoc = doc;
            if (doc == null)
            {
                _viewer.SetImage(null, false);
                _viewer.Roi = Rectangle.Empty;
                Text = Ui.AppName;
                _stInfo.Text = "Нет открытых изображений";
            }
            else
            {
                _viewer.SetImage(doc.Display, false);
                if (doc.ViewZoom > 0) _viewer.SetView(doc.ViewZoom, doc.ViewOx, doc.ViewOy);
                _viewer.Roi = doc.Roi;
                UpdateTitle();
            }
            int i = doc == null ? -1 : _ws.Docs.IndexOf(doc);
            if (_list.SelectedIndex != i && i < _list.Items.Count) _list.SelectedIndex = i;
            _channel.Enabled = doc != null && doc.Data.IsRgb;
            UpdateOverlays();
            UpdateRoiInfo();
            UpdateMenuState();
        }

        private void OnActiveDataChanged()
        {
            var doc = _ws.Active;
            if (doc == null) return;
            _viewer.SetImage(doc.Display, true);
            _viewer.Roi = doc.Roi;
            _channel.Enabled = doc.Data.IsRgb;
            UpdateTitle();
            UpdateOverlays();
            UpdateRoiInfo();
            UpdateMenuState();
        }

        private void UpdateTitle()
        {
            var doc = _ws.Active;
            if (doc == null) return;
            Text = doc.Name + (doc.Modified ? " *" : "") + " — " + Ui.AppName;
            _stInfo.Text = doc.Name + ":  " + doc.Data.Description + (doc.Data.FilePath != null ? "   " + doc.Data.FilePath : "");
        }

        private void UpdateOverlays()
        {
            _viewer.Overlays.Clear();
            var doc = _ws.Active;
            if (doc != null && _showRois.Checked)
            {
                foreach (var r in _ws.Rois)
                    if (r.Rect.Right <= doc.Data.Width && r.Rect.Bottom <= doc.Data.Height)
                        _viewer.Overlays.Add(new ViewerOverlay { Rect = r.Rect, Color = Theme.DarkRoi, Label = r.Name });
            }
            _viewer.Invalidate();
            _roiList.BeginUpdate();
            _roiList.Items.Clear();
            foreach (var r in _ws.Rois) _roiList.Items.Add(r.Name + ":  x=" + r.Rect.X + ", y=" + r.Rect.Y + ", " + r.Rect.Width + "×" + r.Rect.Height);
            _roiList.EndUpdate();
        }

        private int Channel
        {
            get
            {
                var d = _ws.Active;
                if (d == null || !d.Data.IsRgb) return -1;
                return _channel.SelectedIndex <= 0 ? -1 : _channel.SelectedIndex - 1;
            }
        }

        private string ChannelName
        {
            get { return Channel < 0 ? "" : " [" + "RGB"[Channel] + "]"; }
        }

        private void UpdateRoiInfo()
        {
            var doc = _ws.Active;
            if (doc == null)
            {
                _roiCaption.Text = "Нет изображения";
                foreach (var l in _stat.Values) l.Text = "—";
                return;
            }
            Rectangle roi = _viewer.Roi;
            _roiCaption.Text = roi.IsEmpty
                ? "Выделения нет — статистика по всему изображению (" + doc.Data.Width + "×" + doc.Data.Height + ")"
                : "x=" + roi.X + ", y=" + roi.Y + ", размер " + roi.Width + "×" + roi.Height;
            RoiStats s;
            try { s = Stats.Compute(doc.Data, roi, Channel); }
            catch { return; }
            _stat["n"].Text = s.N.ToString("N0");
            _stat["mean"].Text = Ui.G(s.Mean);
            _stat["sd"].Text = Ui.G(s.StdDev);
            _stat["min"].Text = Ui.G(s.Min);
            _stat["max"].Text = Ui.G(s.Max);
        }

        // ------------------------------------------------------------------ результаты

        public void AddResult(string image, string area, Rectangle r, RoiStats s)
        {
            _resultNo++;
            _results.Rows.Add(_resultNo, image, area, r.X, r.Y, r.Width, r.Height, s.N, Ui.G(s.Mean), Ui.G(s.StdDev), Ui.G(s.Min), Ui.G(s.Max));
            if (_results.Rows.Count > 0) _results.FirstDisplayedScrollingRowIndex = _results.Rows.Count - 1;
        }

        private string ResultsText(char sep)
        {
            var sb = new System.Text.StringBuilder();
            for (int c = 0; c < _results.Columns.Count; c++) sb.Append(c > 0 ? sep.ToString() : "").Append(_results.Columns[c].HeaderText);
            sb.AppendLine();
            foreach (DataGridViewRow row in _results.Rows)
            {
                for (int c = 0; c < _results.Columns.Count; c++)
                {
                    object v = row.Cells[c].Value;
                    string s = v == null ? "" : v.ToString();
                    sb.Append(c > 0 ? sep.ToString() : "").Append(sep == ';' ? Csv.Esc(s) : s);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private void CopyResults()
        {
            if (_results.Rows.Count == 0) return;
            Clipboard.SetText(ResultsText('\t'));
        }

        private void SaveResults()
        {
            if (_results.Rows.Count == 0) { Ui.Info(this, "Таблица результатов пуста."); return; }
            Ui.SaveText(this, ResultsText(';'), "Результаты.csv");
        }

        // ------------------------------------------------------------------ общие помощники

        public Workspace Workspace { get { return _ws; } }

        private ImageDoc RequireImage()
        {
            if (_ws.Active == null)
            {
                Ui.Info(this, "Сначала откройте или создайте изображение (меню «Файл»).");
                return null;
            }
            return _ws.Active;
        }

        /// <summary>Применить обработку к активному изображению. При наличии выделения — только внутри него (как в ImageJ).</summary>
        private void ApplyToActive(string action, Func<ImageData, ImageData> f, bool respectRoi = true)
        {
            var doc = RequireImage();
            if (doc == null) return;
            Ui.Busy(this, () =>
            {
                ImageData res = f(doc.Data);
                if (respectRoi && !doc.Roi.IsEmpty) res = ImageOps.MergeInRoi(doc.Data, res, doc.Roi);
                doc.Replace(res, action);
            });
        }

        /// <summary>
        /// Показывает диалог с предпросмотром и по OK применяет обработку.
        /// Если отмечен toNew — результат открывается отдельным изображением с именем newName().
        /// </summary>
        private void RunFilterDialog(ParamDialog dlg, string action, Func<ImageData, ImageData> f, bool respectRoi = true,
                                     CheckBox toNew = null, Func<string> newName = null)
        {
            var doc = RequireImage();
            if (doc == null) return;
            if (respectRoi && !doc.Roi.IsEmpty)
                dlg.AddNote("Есть выделение: обработка будет применена только внутри него (как в ImageJ). Снять выделение — Ctrl+Shift+A.", true);
            dlg.Changed += (s, e) =>
            {
                if (!dlg.PreviewOn)
                {
                    _viewer.SetImage(doc.Display, true);
                    return;
                }
                try
                {
                    ImageData r = f(doc.Data);
                    if (respectRoi && !doc.Roi.IsEmpty) r = ImageOps.MergeInRoi(doc.Data, r, doc.Roi);
                    var bmp = ImageIO.ToBitmap(r);
                    _viewer.SetImage(bmp, true);
                    if (_previewBmp != null) _previewBmp.Dispose();
                    _previewBmp = bmp;
                    dlg.ShowError(null);
                }
                catch (Exception ex) { dlg.ShowError(ex.Message); }
            };
            bool ok = dlg.ShowDialog(this) == DialogResult.OK;
            _viewer.SetImage(doc.Display, true);
            if (_previewBmp != null) { _previewBmp.Dispose(); _previewBmp = null; }
            if (!ok) return;
            if (toNew != null && toNew.Checked)
            {
                Ui.Busy(this, () =>
                {
                    ImageData r = f(doc.Data);
                    if (respectRoi && !doc.Roi.IsEmpty) r = ImageOps.MergeInRoi(doc.Data, r, doc.Roi);
                    r.Name = System.IO.Path.GetFileNameWithoutExtension(doc.Name) + " — " + (newName != null ? newName() : action);
                    r.FilePath = null;
                    AddImage(r).Modified = true;
                });
            }
            else ApplyToActive(action, f, respectRoi);
        }

        public ImageDoc AddImage(ImageData d, bool activate = true)
        {
            var doc = _ws.Add(d, activate);
            return doc;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // + и − для масштаба, когда фокус не в поле ввода
            if (!(ActiveControl is TextBoxBase) && !(ActiveControl is ComboBox))
            {
                if (keyData == Keys.Oemplus || keyData == Keys.Add) { _viewer.ZoomIn(); return true; }
                if (keyData == Keys.OemMinus || keyData == Keys.Subtract) { _viewer.ZoomOut(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        internal void TourRefresh() { OnActiveChanged(); }
        internal void TourActivate(string name) { var d = _ws.Find(name); if (d != null) _ws.Active = d; }

        internal void TourCommand(string name)
        {
            switch (name)
            {
                case "calc": CalculatorDialog(); break;
                case "convolve": ConvolveDialog(); break;
                case "new": NewImageDialog(); break;
                case "chart": ChartGeneratorDialog(); break;
                case "noise": NoiseDialog(NoiseKind.Gaussian); break;
                case "rank": RankDialog(RankType.Median); break;
                case "undo": Undo(); break;
            }
        }
        internal void TourMeasureRois() { MeasureRois(); }

        public void OpenAtStartup(string[] files)
        {
            if (files != null && files.Length > 0) OpenFiles(files);
        }
    }
}
