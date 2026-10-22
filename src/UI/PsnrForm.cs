using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using VideoMeter.Core;
using VideoMeter.IO;

namespace VideoMeter.UI
{
    /// <summary>Расчёт ОСШ: по введённым M и σ, а также ОСШ и ПОСШ (PSNR) изображения относительно эталона.</summary>
    public sealed class PsnrForm : Form
    {
        private readonly MainForm _main;
        private readonly Workspace _ws;
        private readonly ComboBox _ref, _test, _channel;
        private readonly NumBox _peak;
        private readonly CheckBox _useRoi;
        private readonly DataGridView _metrics;
        private readonly DataGridView _history;
        private readonly NumBox _calcM, _calcS;
        private readonly Label _calcOut;
        private readonly Label _formula;

        public PsnrForm(MainForm main)
        {
            _main = main;
            _ws = main.Workspace;
            Theme.ApplyForm(this);
            Text = "Расчёт ОСШ и ПОСШ";
            Size = new Size(Theme.S(1180), Theme.S(780));
            MinimumSize = new Size(Theme.S(900), Theme.S(600));
            StartPosition = FormStartPosition.CenterParent;
            Theme.FitToScreen(this);

            // --- левая панель
            var left = new Panel { Dock = DockStyle.Left, Width = Theme.S(490), BackColor = Color.White, AutoScroll = true };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(Theme.S(10)) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Func<string, Label> lab = s => new Label { Text = s, AutoSize = true, Margin = new Padding(0, Theme.S(7), Theme.S(8), 0) };
            int w = Theme.S(210);

            _ref = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w };
            _test = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w };
            var bRefOpen = Theme.MakeButton("Файл…");
            var bTestOpen = Theme.MakeButton("Файл…");
            bRefOpen.MinimumSize = bTestOpen.MinimumSize = new Size(Theme.S(60), Theme.S(24));
            bRefOpen.Click += (s, e) => OpenInto(_ref);
            bTestOpen.Click += (s, e) => OpenInto(_test);
            t.Controls.Add(lab("Эталон (без шума):"));
            t.Controls.Add(Row(_ref, bRefOpen));
            t.Controls.Add(lab("Сравниваемое:"));
            t.Controls.Add(Row(_test, bTestOpen));

            _channel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w };
            _channel.Items.AddRange(new object[] { "Яркость", "R", "G", "B", "R, G, B (все каналы)" });
            _channel.SelectedIndex = 0;
            t.Controls.Add(lab("Канал:"));
            t.Controls.Add(_channel);

            _peak = new NumBox { Decimals = 3, Caption = "Пиковое значение", Width = Theme.S(90) };
            _peak.Value = 255;
            t.Controls.Add(lab("Пик MAX (для ПОСШ):"));
            t.Controls.Add(_peak);
            _useRoi = new CheckBox { Text = "Только в выделении эталона (ROI)", AutoSize = true };
            t.Controls.Add(new Label());
            t.Controls.Add(_useRoi);

            var calc = Theme.MakeButton("Вычислить", true);
            calc.Click += (s, e) => Compute();
            var err = Theme.MakeButton("Изображение ошибки");
            err.Click += (s, e) => ErrorImage();
            var btns = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(Theme.S(400), 0), Margin = new Padding(0, Theme.S(10), 0, 0) };
            btns.Controls.AddRange(new Control[] { calc, err });
            t.Controls.Add(btns);
            t.SetColumnSpan(btns, 2);

            _formula = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(Theme.S(390), 0),
                ForeColor = Theme.Muted,
                Margin = new Padding(0, Theme.S(12), 0, 0),
                Text = "MSE = (1/N)·Σ(x̂ − x)²,  RMSE = √MSE\n" +
                       "ПОСШ (PSNR) = 10·lg(MAX² / MSE), дБ\n" +
                       "ОСШ (SNR) = 10·lg(Σx² / Σ(x̂ − x)²), дБ\n" +
                       "ОСШ по дисперсии = 10·lg(σ²эт / MSE), дБ\n" +
                       "SSIM — индекс структурного сходства (Wang, 2004), окно Гаусса σ = 1,5\n\n" +
                       "x — эталон, x̂ — сравниваемое изображение. MAX = 255 для 8 бит."
            };
            t.Controls.Add(_formula);
            t.SetColumnSpan(_formula, 2);

            // калькулятор ОСШ по введённым среднему и СКО
            var hdr = Theme.MakeHeader("Калькулятор ОСШ по M и σ");
            hdr.Dock = DockStyle.None;
            hdr.Width = Theme.S(400);
            hdr.Margin = new Padding(0, Theme.S(16), 0, Theme.S(6));
            t.Controls.Add(hdr);
            t.SetColumnSpan(hdr, 2);
            var calcNote = new Label
            {
                Text = "Введите среднее значение яркости (Mean) и стандартное отклонение (StdDev), полученные по гистограмме области.",
                AutoSize = true, MaximumSize = new Size(Theme.S(390), 0), ForeColor = Theme.Muted
            };
            t.Controls.Add(calcNote);
            t.SetColumnSpan(calcNote, 2);
            _calcM = new NumBox { Decimals = 4, Caption = "M", Width = Theme.S(110), Text = "" };
            _calcS = new NumBox { Decimals = 4, Caption = "σ", Width = Theme.S(110), Text = "" };
            t.Controls.Add(lab("Среднее M:"));
            t.Controls.Add(_calcM);
            t.Controls.Add(lab("СКО σ:"));
            t.Controls.Add(_calcS);
            _calcOut = new Label { AutoSize = true, Font = Theme.BoldFont, ForeColor = Theme.Primary, Margin = new Padding(0, Theme.S(6), 0, 0) };
            t.Controls.Add(_calcOut);
            t.SetColumnSpan(_calcOut, 2);
            _calcM.TextChanged += (s, e) => UpdateCalc();
            _calcS.TextChanged += (s, e) => UpdateCalc();
            UpdateCalc();

            left.AutoScroll = true;
            left.Controls.Add(t);
            left.Controls.Add(Theme.MakeHeader("ОСШ и ПОСШ относительно эталона"));

            // --- правая часть
            var top = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = Theme.S(5), BackColor = Theme.Tint };
            _metrics = Grid();
            _metrics.Columns.Add("m", "Метрика");
            _metrics.Columns.Add("v", "Значение");
            _metrics.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _history = Grid();
            foreach (string c in new[] { "Эталон", "Сравниваемое", "Канал", "MSE", "RMSE", "ПОСШ, дБ", "ОСШ, дБ", "SSIM" }) _history.Columns.Add(c, c);
            var mp = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            mp.Controls.Add(_metrics);
            mp.Controls.Add(Theme.MakeHeader("Результат"));
            var hp = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            var hbar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(40), BackColor = Theme.SurfaceAlt, Padding = new Padding(Theme.S(4)) };
            var hc = Theme.MakeButton("Копировать");
            hc.Click += (s, e) => CopyGrid(_history);
            var hs = Theme.MakeButton("CSV…");
            hs.Click += (s, e) => Ui.SaveText(this, GridText(_history, ';'), "ОСШ и ПОСШ.csv");
            hbar.Controls.AddRange(new Control[] { hc, hs });
            hp.Controls.Add(_history);
            hp.Controls.Add(hbar);
            hp.Controls.Add(Theme.MakeHeader("История сравнений"));
            top.Panel1.Controls.Add(mp);
            top.Panel2.Controls.Add(hp);

            Controls.Add(top);
            Controls.Add(left);
            Load += (s, e) =>
            {
                try { top.SplitterDistance = (int)(top.Width * 0.45); }
                catch { }
            };

            _ws.DocsChanged += WsChanged;
            FormClosed += (s, e) => _ws.DocsChanged -= WsChanged;
            Fill();
            _ref.SelectedIndexChanged += (s, e) => UpdatePeak();
            UpdatePeak();
        }

        private static Control Row(Control a, Control b)
        {
            var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            p.Controls.Add(a);
            p.Controls.Add(b);
            return p;
        }

        private static DataGridView Grid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
                EnableHeadersVisualStyles = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
            g.ColumnHeadersDefaultCellStyle.Font = Theme.BoldFont;
            return g;
        }

        private void WsChanged(object s, EventArgs e) { if (!IsDisposed) Fill(); }

        private void Fill()
        {
            string r = _ref.SelectedItem as string, t = _test.SelectedItem as string;
            _ref.Items.Clear();
            _test.Items.Clear();
            foreach (var d in _ws.Docs) { _ref.Items.Add(d.Name); _test.Items.Add(d.Name); }
            if (_ref.Items.Count == 0) return;
            int ri = r != null ? _ref.Items.IndexOf(r) : -1;
            int ti = t != null ? _test.Items.IndexOf(t) : -1;
            if (ri < 0) ri = 0;
            if (ti < 0) ti = _ws.Active != null ? _ws.Docs.IndexOf(_ws.Active) : Math.Min(1, _test.Items.Count - 1);
            if (ti == ri && _test.Items.Count > 1) ti = (ri + 1) % _test.Items.Count;
            _ref.SelectedIndex = ri;
            _test.SelectedIndex = ti;
        }

        private void UpdatePeak()
        {
            var d = _ws.Find(_ref.SelectedItem as string);
            if (d != null) _peak.Value = Quality.DefaultPeak(d.Data);
        }

        private void OpenInto(ComboBox target)
        {
            using (var dlg = new OpenFileDialog { Filter = ImageIO.OpenFilter, Title = "Открыть изображение" })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir) && Directory.Exists(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var docs = _main.OpenFiles(new[] { dlg.FileName });
                if (docs.Count > 0)
                {
                    Fill();
                    target.SelectedItem = docs[0].Name;
                }
            }
        }

        private bool Get(out ImageDoc r, out ImageDoc t)
        {
            r = _ws.Find(_ref.SelectedItem as string);
            t = _ws.Find(_test.SelectedItem as string);
            if (r == null || t == null) { Ui.Info(this, "Откройте эталонное и сравниваемое изображения (в главном окне или кнопками «Файл…»)."); return false; }
            if (double.IsNaN(_peak.Value) || _peak.Value <= 0) { Ui.Error(this, "Пиковое значение должно быть положительным числом."); return false; }
            return true;
        }

        private CompareChannel Ch
        {
            get
            {
                switch (_channel.SelectedIndex)
                {
                    case 1: return CompareChannel.R;
                    case 2: return CompareChannel.G;
                    case 3: return CompareChannel.B;
                    case 4: return CompareChannel.AllRgb;
                    default: return CompareChannel.Luma;
                }
            }
        }

        internal void TourCompute(string refName, string testName)
        {
            _ref.SelectedItem = refName;
            _test.SelectedItem = testName;
            Compute();
        }

        private void Compute()
        {
            ImageDoc r, t;
            if (!Get(out r, out t)) return;
            Rectangle roi = _useRoi.Checked ? r.Roi : Rectangle.Empty;
            QualityResult q = null;
            if (!Ui.Busy(this, () => q = Quality.Compare(r.Data, t.Data, roi, Ch, _peak.Value))) return;
            _metrics.Rows.Clear();
            Action<string, string> add = (k, v) => _metrics.Rows.Add(k, v);
            add("ПОСШ (PSNR), дБ", Ui.F(q.Psnr, 3));
            add("ОСШ (SNR), дБ", Ui.F(q.SnrDb, 3));
            add("ОСШ по дисперсии, дБ", Ui.F(q.SnrVarDb, 3));
            add("ОСШ, раз (√(Σx²/Σe²))", Ui.G(Math.Pow(10, q.SnrDb / 20)));
            add("MSE", Ui.G(q.Mse));
            add("RMSE (СКО ошибки)", Ui.G(q.Rmse));
            add("Средняя абсолютная ошибка", Ui.G(q.Mae));
            add("Макс. абсолютная ошибка", Ui.G(q.MaxAbsError));
            add("Среднее ошибки (смещение)", Ui.G(q.MeanError));
            add("СКО ошибки (без смещения)", Ui.G(q.StdError));
            add("SSIM", Ui.F(q.Ssim, 4));
            add("M / σ эталона", Ui.G(q.MeanRef) + " / " + Ui.G(q.StdRef));
            add("M / σ сравниваемого", Ui.G(q.MeanTest) + " / " + Ui.G(q.StdTest));
            add("Пикселей (N)", q.N.ToString("N0"));
            add("MAX", Ui.G(q.Peak));
            _metrics.Rows[0].DefaultCellStyle.Font = Theme.BoldFont;
            _metrics.Rows[1].DefaultCellStyle.Font = Theme.BoldFont;
            _history.Rows.Add(r.Name, t.Name, _channel.SelectedItem, Ui.G(q.Mse), Ui.G(q.Rmse), Ui.F(q.Psnr, 2), Ui.F(q.SnrDb, 2), Ui.F(q.Ssim, 4));
        }

        private void UpdateCalc()
        {
            double m = _calcM.Value, sd = _calcS.Value;
            if (double.IsNaN(m) || double.IsNaN(sd)) { _calcOut.Text = "ОСШ = —"; return; }
            if (sd <= 0) { _calcOut.Text = "σ должно быть больше нуля"; return; }
            double ratio = m / sd;
            _calcOut.Text = "ОСШ = M/σ = " + Ui.G(ratio) + "\nОСШ = 20·lg(M/σ) = " + (ratio > 0 ? Ui.F(20 * Math.Log10(ratio), 2) + " дБ" : "—");
        }

        private void ErrorImage()
        {
            ImageDoc r, t;
            if (!Get(out r, out t)) return;
            Ui.Busy(this, () =>
            {
                var e = Quality.ErrorImage(r.Data, t.Data);
                _main.AddImage(e).Modified = true;
            });
        }

        private static string GridText(DataGridView g, char sep)
        {
            var sb = new StringBuilder();
            for (int c = 0; c < g.Columns.Count; c++) sb.Append(c > 0 ? sep.ToString() : "").Append(g.Columns[c].HeaderText);
            sb.AppendLine();
            foreach (DataGridViewRow row in g.Rows)
            {
                for (int c = 0; c < g.Columns.Count; c++)
                {
                    string v = Convert.ToString(row.Cells[c].Value);
                    sb.Append(c > 0 ? sep.ToString() : "").Append(sep == ';' ? Csv.Esc(v) : v);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static void CopyGrid(DataGridView g)
        {
            if (g.Rows.Count > 0) Clipboard.SetText(GridText(g, '\t'));
        }
    }
}
