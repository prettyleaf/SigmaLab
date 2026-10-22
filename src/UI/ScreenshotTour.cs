using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using SigmaLab.Core;

namespace SigmaLab.UI
{
    /// <summary>
    /// Служебный режим «--shots папка»: программа сама проходит основные шаги работы на синтетических данных
    /// и сохраняет снимки окон. Используется для проверки интерфейса.
    /// </summary>
    internal static class ScreenshotTour
    {
        public static void Attach(MainForm main, string dir)
        {
            Directory.CreateDirectory(dir);
            var steps = new List<Action>();
            var timer = new Timer { Interval = 1500 };
            int k = 0;
            Form current = null;
            Action<Form, string> shot = (f, name) =>
            {
                try
                {
                    f.Activate();
                    f.Refresh();
                    Application.DoEvents();
                    using (var bmp = new Bitmap(f.Width, f.Height))
                    {
                        try
                        {
                            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(f.Location, Point.Empty, f.Size);
                        }
                        catch
                        {
                            f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                        }
                        bmp.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
                    }
                    File.AppendAllText(Path.Combine(dir, "log.txt"), "ok " + name + "\r\n");
                }
                catch (Exception ex)
                {
                    File.AppendAllText(Path.Combine(dir, "log.txt"), "fail " + name + ": " + ex + "\r\n");
                }
            };
            Action<string> log = s => File.AppendAllText(Path.Combine(dir, "log.txt"), s + "\r\n");

            ImageData capture = null;
            steps.Add(() =>
            {
                main.WindowState = FormWindowState.Normal;
                main.Bounds = new Rectangle(0, 0, 1360, 860);
                var chart = TestCharts.StepChart(new StepChartSpec { Steps = 20, DensityStep = 0.1, FirstDensity = 0, Width = 720, Height = 200, Margin = 20 });
                main.AddImage(chart);
                capture = TestCharts.SimulateCamera(chart, 2.2, 0.92, 2.2, 5, 0.12, 0.8, true, 7);
                capture.Name = "снимок_таблицы.bmp";
                var doc = main.AddImage(capture);
                var ws = main.Workspace;
                ws.Rois.Add(new NamedRoi { Name = "1", Rect = new Rectangle(40, 70, 40, 60) });
                ws.Rois.Add(new NamedRoi { Name = "2", Rect = new Rectangle(640, 70, 40, 60) });
                ws.NotifyRois();
                doc.Roi = new Rectangle(300, 60, 60, 80);
                main.TourRefresh();
                main.TourMeasureRois();
            });
            steps.Add(() => shot(main, "01_main"));
            steps.Add(() =>
            {
                var f = new StepchartForm(main);
                f.Show(main);
                f.Bounds = new Rectangle(20, 20, 1240, 800);
                f.TourSetRoi(new Rectangle(22, 22, 676, 156));
                current = f;
            });
            steps.Add(() => shot(current, "02_stepchart_settings"));
            steps.Add(() =>
            {
                var sf = (StepchartForm)current;
                var res = sf.TourRun();
                current.Close();
                current = res;
                if (res != null) res.Bounds = new Rectangle(10, 10, 1180, 900);
            });
            steps.Add(() => { if (current != null) shot(current, "03_stepchart_fig1"); });
            steps.Add(() => { var r = current as StepchartResultsForm; if (r != null) { r.TourTab(1); } });
            steps.Add(() => { if (current != null) shot(current, "04_stepchart_fig2"); });
            steps.Add(() => { var r = current as StepchartResultsForm; if (r != null) { r.TourTab(2); } });
            steps.Add(() => { if (current != null) shot(current, "05_stepchart_fig3"); });
            steps.Add(() => { var r = current as StepchartResultsForm; if (r != null) { r.TourTab(3); } });
            steps.Add(() => { if (current != null) shot(current, "05b_stepchart_table"); current.Close(); });
            steps.Add(() =>
            {
                var noisy = Noise.AddGaussian(capture, 15, 3);
                noisy.Name = "снимок + шум σ=15";
                main.AddImage(noisy);
                var f = new PsnrForm(main);
                f.Show(main);
                f.Bounds = new Rectangle(20, 20, 1180, 780);
                f.TourCompute(capture.Name, "снимок + шум σ=15");
                current = f;
            });
            steps.Add(() => shot(current, "08_psnr"));
            steps.Add(() =>
            {
                current.Close();
                var f = new HistogramForm(capture, new Rectangle(640, 70, 40, 60), -1, "область 2", main);
                f.Show(main);
                current = f;
            });
            steps.Add(() => shot(current, "09_histogram"));
            steps.Add(() =>
            {
                current.Close();
                var f = new AboutForm();
                f.Show(main);
                current = f;
            });
            steps.Add(() => shot(current, "11_about"));
            steps.Add(() =>
            {
                current.Close();
                var f = new ParamDialog("Добавить шум (Add Specified Noise)", "Аддитивный гауссов шум с нулевым средним и заданным стандартным отклонением.");
                f.AddNumber("Стандартное отклонение (σ):", 25, 2, 0);
                f.AddCheck("Фиксировать генератор (одинаковый шум при повторе)", false);
                f.EnablePreview();
                f.Show(main);
                current = f;
            });
            steps.Add(() => { shot(current, "12_dialog"); current.Close(); });
            Func<Form> topDialog = () =>
            {
                Form found = null;
                foreach (Form f in Application.OpenForms) if (f is ParamDialog && f.Visible) found = f;
                return found;
            };
            Action<string, string, Action<Form>, bool> modal = (cmd, name, prepare, ok) =>
            {
                var t = new Timer { Interval = 1000 };
                int phase = 0;
                t.Tick += (s, e) =>
                {
                    var f = topDialog();
                    if (f == null) { if (++phase > 6) { t.Stop(); log("no dialog for " + cmd); } return; }
                    if (phase < 100 && prepare != null) { prepare(f); phase = 100; return; }
                    t.Stop();
                    t.Dispose();
                    shot(f, name);
                    if (ok) ((ParamDialog)f).OkButton.PerformClick();
                    else { f.DialogResult = DialogResult.Cancel; f.Close(); }
                };
                t.Start();
                main.TourCommand(cmd);
            };
            Action<Control, string> check = null;
            check = (c, text) =>
            {
                foreach (Control cc in c.Controls)
                {
                    var cb = cc as CheckBox;
                    if (cb != null && cb.Text == text) cb.Checked = true;
                    check(cc, text);
                }
            };
            steps.Add(() => main.TourActivate("снимок_таблицы.bmp"));
            steps.Add(() => modal("convolve", "13_convolve_preview", f => check(f, "Предпросмотр"), false));
            Action<Control, int, int, int> prepareSize = null;
            prepareSize = (c, choice, w, h) =>
            {
                foreach (Control cc in c.Controls)
                {
                    var cb = cc as ComboBox;
                    if (cb != null) cb.SelectedIndex = choice;
                    var nb = cc as NumBox;
                    if (nb != null && nb.Caption == "Ширина") nb.Value = w;
                    if (nb != null && nb.Caption == "Высота") nb.Value = h;
                    prepareSize(cc, choice, w, h);
                }
            };
            steps.Add(() => modal("convolve", "13b_convolve_template", f => prepareSize(f, 1 + (int)MaskKind.SmallDetails, 5, 3), false));
            steps.Add(() => modal("rank", "13c_rank_rect", f => { prepareSize(f, 1, 3, 5); check(f, "Предпросмотр"); }, false));
            steps.Add(() => modal("calc", "14_calculator", null, false));
            steps.Add(() => modal("chart", "15_chart_generator", null, true));
            steps.Add(() => shot(main, "16_main_after_chart"));
            steps.Add(() => modal("new", "17_new_image", null, true));
            steps.Add(() => shot(main, "19_main_new"));
            steps.Add(() =>
            {
                // ввод-вывод: сохранение и повторное чтение во всех форматах
                string tmp = Path.Combine(dir, "io");
                Directory.CreateDirectory(tmp);
                var src = capture;
                foreach (string ext in new[] { ".tif", ".png", ".bmp", ".jpg" })
                {
                    try
                    {
                        string p = Path.Combine(tmp, "rt" + ext);
                        IO.ImageIO.Save(src, p);
                        var back = IO.ImageIO.Load(p);
                        double maxd = 0;
                        for (int c = 0; c < 3; c++)
                            for (int i = 0; i < src.PixelCount; i++) maxd = Math.Max(maxd, Math.Abs(src.Planes[c][i] - back.Planes[c][i]));
                        log("io " + ext + ": " + back.Description + " maxdiff=" + maxd);
                    }
                    catch (Exception ex) { log("io " + ext + " FAIL " + ex.Message); }
                }
                try
                {
                    var g8 = ImageOps.ConvertTo(src, PixelType.Gray8);
                    foreach (string ext in new[] { ".tif", ".png", ".bmp" })
                    {
                        string p = Path.Combine(tmp, "g8" + ext);
                        IO.ImageIO.Save(g8, p);
                        var back = IO.ImageIO.Load(p);
                        double maxd = 0;
                        for (int i = 0; i < g8.PixelCount; i++) maxd = Math.Max(maxd, Math.Abs(g8.Planes[0][i] - back.Planes[0][i]));
                        log("io gray8 " + ext + ": " + back.Description + " maxdiff=" + maxd);
                    }
                    var f32 = ImageOps.Calculate(src, Noise.AddGaussian(src, 5, 1), CalcOp.Subtract, true);
                    string pf = Path.Combine(tmp, "f32.tif");
                    IO.ImageIO.Save(f32, pf);
                    var bf = IO.ImageIO.Load(pf);
                    double md = 0;
                    for (int c = 0; c < f32.Channels; c++)
                        for (int i = 0; i < f32.PixelCount; i++) md = Math.Max(md, Math.Abs(f32.Planes[c][i] - bf.Planes[c][i]));
                    log("io float tif: " + bf.Description + " maxdiff=" + md);
                }
                catch (Exception ex) { log("io gray/float FAIL " + ex); }
                // отмена
                var doc = main.Workspace.Active;
                var before = doc.Data;
                doc.Replace(Noise.AddGaussian(before, 10, 2), "шум");
                main.TourCommand("undo");
                log("undo restores: " + ReferenceEquals(main.Workspace.Active.Data, before));
                // видео
                string avi = Environment.GetEnvironmentVariable("SIGMALAB_TEST_AVI");
                if (!string.IsNullOrEmpty(avi))
                {
                    try
                    {
                        string method;
                        var frames = IO.VideoFrames.Extract(avi, 2, 3, 1, out method);
                        var s0 = Stats.Compute(frames[0], Rectangle.Empty);
                        for (int fi = 0; fi < frames.Count; fi++) IO.ImageIO.Save(frames[fi], Path.Combine(Path.Combine(dir, "io"), "avi_" + fi + ".png"));
                        log("avi: " + frames.Count + " frames " + frames[0].Description + " via " + method + " name=" + frames[0].Name + " mean=" + s0.Mean.ToString("0.0"));
                    }
                    catch (Exception ex) { log("avi FAIL " + ex.Message); }
                }
            });
            ImageData beforeNoise = null;
            Rectangle noiseRoi = new Rectangle(100, 50, 200, 100);
            steps.Add(() =>
            {
                main.TourActivate("снимок_таблицы.bmp");
                var d = main.Workspace.Active;
                d.Roi = noiseRoi;
                main.TourRefresh();
                beforeNoise = d.Data;
                modal("noise", "20_noise_ok", null, true);
            });
            steps.Add(() =>
            {
                var after = main.Workspace.Active.Data;
                int changedIn = 0, changedOut = 0;
                for (int y = 0; y < after.Height; y++)
                    for (int x = 0; x < after.Width; x++)
                    {
                        int i = y * after.Width + x;
                        bool diff = after.Planes[0][i] != beforeNoise.Planes[0][i];
                        if (noiseRoi.Contains(x, y)) { if (diff) changedIn++; }
                        else if (diff) changedOut++;
                    }
                log("noise in ROI: changed inside=" + changedIn + " outside=" + changedOut);
            });
            steps.Add(() => { log("done"); Application.Exit(); });

            main.Shown += (s, e) => timer.Start();
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (k < steps.Count)
                {
                    try { steps[k](); }
                    catch (Exception ex) { log("step " + k + " error: " + ex); }
                    k++;
                    timer.Start();
                }
            };
        }
    }
}
