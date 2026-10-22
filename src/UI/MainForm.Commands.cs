using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using VideoMeter.Core;
using VideoMeter.IO;

namespace VideoMeter.UI
{
    public sealed partial class MainForm
    {
        // ================================================================== Файл

        private void OpenDialog()
        {
            using (var dlg = new OpenFileDialog { Filter = ImageIO.OpenFilter, Multiselect = true, Title = "Открыть изображения" })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir) && Directory.Exists(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                OpenFiles(dlg.FileNames);
            }
        }

        public List<ImageDoc> OpenFiles(IEnumerable<string> files)
        {
            var opened = new List<ImageDoc>();
            var errors = new StringBuilder();
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (string f in files)
                {
                    try
                    {
                        if (IsVideo(f))
                        {
                            ImportVideo(f);
                            continue;
                        }
                        ImageData d = ImageIO.Load(f);
                        opened.Add(AddImage(d));
                        Settings.LastDir = Path.GetDirectoryName(f);
                        Settings.AddRecent(f);
                    }
                    catch (Exception ex)
                    {
                        errors.AppendLine(Path.GetFileName(f) + ": " + ex.Message);
                    }
                }
            }
            finally { Cursor = Cursors.Default; }
            if (errors.Length > 0) Ui.Error(this, "Не удалось открыть:\n" + errors);
            return opened;
        }

        private static bool IsVideo(string f)
        {
            string e = Path.GetExtension(f).ToLowerInvariant();
            return e == ".avi" || e == ".mpg" || e == ".mpeg" || e == ".mp4" || e == ".mkv" || e == ".mov" || e == ".wmv" || e == ".ts" || e == ".vob";
        }

        private void ImportVideo() { ImportVideo(null); }

        /// <summary>Импорт кадров из видео. Возвращает открытые кадры.</summary>
        public List<ImageDoc> ImportVideo(string path)
        {
            var res = new List<ImageDoc>();
            if (path == null)
            {
                using (var od = new OpenFileDialog { Filter = VideoFrames.Filter, Title = "Видеозапись" })
                {
                    if (!string.IsNullOrEmpty(Settings.LastDir) && Directory.Exists(Settings.LastDir)) od.InitialDirectory = Settings.LastDir;
                    if (od.ShowDialog(this) != DialogResult.OK) return res;
                    path = od.FileName;
                }
            }
            Settings.LastDir = Path.GetDirectoryName(path);
            string info = "";
            if (VideoFrames.IsAvi(path))
            {
                try
                {
                    int n = VideoFrames.AviFrameCount(path);
                    if (n > 0) info = "В файле " + n + " кадров. ";
                }
                catch { }
            }
            string ff = VideoFrames.FindFfmpeg();
            info += ff != null ? "Декодер: ffmpeg (" + ff + ")." : (VideoFrames.IsAvi(path) ? "Декодер: Video for Windows." : "ffmpeg не найден — поддерживаются только AVI.");
            var dlg = new ParamDialog("Импорт кадров из видео", Path.GetFileName(path) + "\n" + info);
            var start = dlg.AddNumber("Первый кадр (с нуля):", 0, 0, 0);
            var count = dlg.AddNumber("Число кадров:", 5, 0, 1, 500);
            var step = dlg.AddNumber("Шаг (через сколько кадров):", 1, 0, 1, 10000);
            if (dlg.ShowDialog(this) != DialogResult.OK) return res;
            Ui.Busy(this, () =>
            {
                string method;
                var frames = VideoFrames.Extract(path, start.IntValue, count.IntValue, step.IntValue, out method);
                foreach (var f in frames) res.Add(AddImage(f));
            });
            return res;
        }

        private void NewImageDialog()
        {
            var dlg = new ParamDialog("Создать изображение (File › New › Image)");
            var name = dlg.AddText("Имя:", _ws.UniqueName("Без имени"));
            var type = dlg.AddChoice("Тип:", new object[] { "8 бит", "16 бит", "32 бита (float)", "RGB" }, 0, 160);
            var fill = dlg.AddChoice("Заполнение:", new object[] { "Белое", "Чёрное", "Градиент", "Заданное значение" }, 0, 160);
            var val = dlg.AddNumber("Значение:", 185, 3);
            var w = dlg.AddNumber("Ширина:", 512, 0, 1, 20000, "пикс.");
            var h = dlg.AddNumber("Высота:", 512, 0, 1, 20000, "пикс.");
            val.Enabled = false;
            fill.SelectedIndexChanged += (s, e) => val.Enabled = fill.SelectedIndex == 3;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            PixelType[] types = { PixelType.Gray8, PixelType.Gray16, PixelType.Gray32, PixelType.Rgb24 };
            Ui.Busy(this, () =>
            {
                var d = ImageOps.NewImage(w.IntValue, h.IntValue, types[type.SelectedIndex], (NewFill)fill.SelectedIndex, val.Value);
                d.Name = string.IsNullOrEmpty(name.Text.Trim()) ? "Без имени" : name.Text.Trim();
                AddImage(d).Modified = true;
            });
        }

        private void Save()
        {
            var doc = RequireImage();
            if (doc == null) return;
            string p = doc.Data.FilePath;
            if (string.IsNullOrEmpty(p) || !(p.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)))
            {
                SaveAs();
                return;
            }
            SaveTo(doc, p);
        }

        private void SaveAs()
        {
            var doc = RequireImage();
            if (doc == null) return;
            using (var dlg = new SaveFileDialog { Filter = ImageIO.SaveFilter, Title = "Сохранить изображение" })
            {
                dlg.FileName = PlotView.Safe(Path.GetFileNameWithoutExtension(doc.Name));
                if (!string.IsNullOrEmpty(Settings.LastDir) && Directory.Exists(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (doc.Data.IsFloat || doc.Data.Type == PixelType.Gray16) dlg.FilterIndex = 1;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string path = ImageIO.EnsureExtension(dlg.FileName, dlg.FilterIndex);
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if ((doc.Data.IsFloat || doc.Data.Type == PixelType.Gray16) && ext != ".tif" && ext != ".tiff")
                {
                    if (!Ui.Ask(this, "Изображение " + doc.Data.TypeName + " без потерь сохраняется только в TIFF. " +
                                      "В выбранном формате оно будет преобразовано в 8 бит по текущему диапазону отображения. Продолжить?"))
                        return;
                }
                SaveTo(doc, path);
            }
        }

        private void SaveTo(ImageDoc doc, string path)
        {
            if (Ui.Busy(this, () => ImageIO.Save(doc.Data, path)))
            {
                doc.Data.FilePath = path;
                doc.Modified = false;
                Settings.LastDir = Path.GetDirectoryName(path);
                Settings.AddRecent(path);
                UpdateTitle();
                _list.Invalidate();
            }
        }

        private void SaveAllToFolder()
        {
            if (_ws.Docs.Count == 0) { Ui.Info(this, "Нет открытых изображений."); return; }
            using (var fb = new FolderBrowserDialog { Description = "Папка для сохранения всех изображений (TIFF без сжатия)" })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir) && Directory.Exists(Settings.LastDir)) fb.SelectedPath = Settings.LastDir;
                if (fb.ShowDialog(this) != DialogResult.OK) return;
                Ui.Busy(this, () =>
                {
                    foreach (var d in _ws.Docs)
                    {
                        string path = Path.Combine(fb.SelectedPath, PlotView.Safe(Path.GetFileNameWithoutExtension(d.Name)) + ".tif");
                        ImageIO.Save(d.Data, path);
                        d.Data.FilePath = path;
                        d.Modified = false;
                    }
                    Settings.LastDir = fb.SelectedPath;
                });
                _list.Invalidate();
                UpdateTitle();
            }
        }

        private void CloseActive()
        {
            var doc = _ws.Active;
            if (doc == null) return;
            if (doc.Modified)
            {
                var r = Ui.AskCancel(this, "Сохранить изменения в «" + doc.Name + "»?");
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes) { SaveAs(); if (doc.Modified) return; }
            }
            _ws.Close(doc);
        }

        private void CloseAll()
        {
            if (_ws.Docs.Exists(d => d.Modified) && !Ui.Ask(this, "Закрыть все изображения? Несохранённые изменения будут потеряны.")) return;
            while (_ws.Docs.Count > 0) _ws.Close(_ws.Docs[_ws.Docs.Count - 1]);
        }

        private void PrintActive()
        {
            var doc = RequireImage();
            if (doc == null) return;
            using (var pd = new PrintDocument())
            {
                pd.DocumentName = doc.Name;
                pd.PrintPage += (s, e) =>
                {
                    Rectangle m = e.MarginBounds;
                    Bitmap b = doc.Display;
                    using (var f = new Font(Theme.UiFont.FontFamily, 10f))
                        e.Graphics.DrawString(doc.Name, f, Brushes.Black, m.Left, m.Top - 30);
                    float k = Math.Min((float)m.Width / b.Width, (float)m.Height / b.Height);
                    e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    e.Graphics.DrawImage(b, m.Left, m.Top, b.Width * k, b.Height * k);
                };
                using (var dlg = new PrintDialog { Document = pd, UseEXDialog = true })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    try { pd.Print(); }
                    catch (Exception ex) { Ui.Error(this, "Ошибка печати: " + ex.Message); }
                }
            }
        }

        // ================================================================== Правка

        private void Undo()
        {
            var doc = _ws.Active;
            if (doc == null || !doc.CanUndo) return;
            doc.Undo();
        }

        private void Duplicate()
        {
            var doc = RequireImage();
            if (doc == null) return;
            ImageData d = doc.Roi.IsEmpty ? doc.Data.Clone() : ImageOps.Crop(doc.Data, doc.Roi);
            d.Name = Path.GetFileNameWithoutExtension(doc.Name) + "-копия";
            d.FilePath = null;
            AddImage(d).Modified = true;
        }

        private void Rename()
        {
            var doc = RequireImage();
            if (doc == null) return;
            var dlg = new ParamDialog("Переименовать");
            var t = dlg.AddText("Новое имя:", doc.Name, 1, 300);
            dlg.Validator = () => string.IsNullOrEmpty(t.Text.Trim()) ? "Имя не может быть пустым." :
                (_ws.Docs.Exists(d => d != doc && d.Name == t.Text.Trim()) ? "Изображение с таким именем уже открыто." : null);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            doc.Name = t.Text.Trim();
            _ws.NotifyDocs();
            UpdateTitle();
        }

        private void SelectAll()
        {
            var doc = RequireImage();
            if (doc == null) return;
            _viewer.Roi = new Rectangle(0, 0, doc.Data.Width, doc.Data.Height);
        }

        private void SelectNone()
        {
            if (_ws.Active == null) return;
            _viewer.Roi = Rectangle.Empty;
        }

        private void SpecifyRoi()
        {
            var doc = RequireImage();
            if (doc == null) return;
            Rectangle r = doc.Roi.IsEmpty ? new Rectangle(doc.Data.Width / 4, doc.Data.Height / 4, doc.Data.Width / 2, doc.Data.Height / 2) : doc.Roi;
            var dlg = new ParamDialog("Задать выделение", "Координаты левого верхнего угла и размер в пикселях.");
            var x = dlg.AddNumber("X:", r.X, 0, 0, doc.Data.Width - 1);
            var y = dlg.AddNumber("Y:", r.Y, 0, 0, doc.Data.Height - 1);
            var w = dlg.AddNumber("Ширина:", r.Width, 0, 1, doc.Data.Width);
            var h = dlg.AddNumber("Высота:", r.Height, 0, 1, doc.Data.Height);
            var centered = dlg.AddCheck("X, Y — центр области", false);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            int X = x.IntValue, Y = y.IntValue;
            if (centered.Checked) { X -= w.IntValue / 2; Y -= h.IntValue / 2; }
            _viewer.Roi = new Rectangle(X, Y, w.IntValue, h.IntValue);
        }

        private void OptionsDialog()
        {
            var dlg = new ParamDialog("Настройки");
            var luma = dlg.AddChoice("Яркость для RGB:", new object[] { "(R+G+B)/3 — как в ImageJ по умолчанию", "0,299R + 0,587G + 0,114B (взвешенная)" },
                LabOptions.Luma == LumaMode.Weighted ? 1 : 0, 300);
            var res = dlg.AddCheck("Шум для 8 бит: разыгрывать значения вне 0…255 заново (как ImageJ)", LabOptions.NoiseResampleOutOfRange);
            var calc = dlg.AddCheck("Калькулятор: по умолчанию 32-битный результат", Settings.CalcFloatDefault);
            var ff = dlg.AddText("Путь к ffmpeg.exe:", VideoFrames.FfmpegPathOverride ?? "", 1, 300);
            dlg.AddNote("ffmpeg нужен только для импорта кадров из MPEG/MP4 и т. п. AVI читается средствами Windows. " +
                        "Если путь не задан, ffmpeg.exe ищется в папке программы и в PATH. Найден: " + (VideoFrames.FindFfmpeg() ?? "нет"));
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            LabOptions.Luma = luma.SelectedIndex == 1 ? LumaMode.Weighted : LumaMode.Unweighted;
            LabOptions.NoiseResampleOutOfRange = res.Checked;
            Settings.CalcFloatDefault = calc.Checked;
            VideoFrames.FfmpegPathOverride = ff.Text.Trim();
            Settings.Save();
            UpdateRoiInfo();
        }

        // ================================================================== Изображение

        private void ConvertType(PixelType t)
        {
            var doc = RequireImage();
            if (doc == null) return;
            if (doc.Data.Type == t) return;
            ApplyToActive("Тип: " + t, d => ImageOps.ConvertTo(d, t), false);
        }

        private void Crop()
        {
            var doc = RequireImage();
            if (doc == null) return;
            if (doc.Roi.IsEmpty) { Ui.Info(this, "Сначала выделите область мышью."); return; }
            Rectangle r = doc.Roi;
            ApplyToActive("Обрезка", d => ImageOps.Crop(d, r), false);
        }

        private void ShowImageInfo()
        {
            var doc = RequireImage();
            if (doc == null) return;
            var d = doc.Data;
            var s = Stats.Compute(d, Rectangle.Empty, -1);
            Ui.Info(this,
                "Имя: " + d.Name + "\n" +
                "Файл: " + (d.FilePath ?? "(не сохранён)") + "\n" +
                "Размер: " + d.Width + " × " + d.Height + " пикс.\n" +
                "Тип: " + d.TypeName + "\n" +
                "Диапазон отображения: " + Ui.G(d.DisplayMin) + " … " + Ui.G(d.DisplayMax) + "\n\n" +
                "По всему изображению" + (d.IsRgb ? " (яркость)" : "") + ":\n" +
                "M = " + Ui.G(s.Mean) + ",  σ = " + Ui.G(s.StdDev) + ",  мин = " + Ui.G(s.Min) + ",  макс = " + Ui.G(s.Max));
        }

        // ================================================================== Обработка

        private void CalculatorDialog()
        {
            if (RequireImage() == null) return;
            object[] names = _ws.Docs.ConvertAll(d => (object)d.Name).ToArray();
            int ai = _ws.Docs.IndexOf(_ws.Active);
            var dlg = new ParamDialog("Калькулятор изображений (Image Calculator)", "Результат = Изображение 1 (операция) Изображение 2.");
            var i1 = dlg.AddChoice("Изображение 1:", names, ai, 300);
            var op = dlg.AddChoice("Операция:", ImageOps.CalcOpNames, 0, 300);
            var i2 = dlg.AddChoice("Изображение 2:", names, names.Length > 1 ? (ai + 1) % names.Length : 0, 300);
            var newWin = dlg.AddCheck("Создать новое изображение (Create new window)", true);
            var flt = dlg.AddCheck("32-битный результат (32-bit (float) result)", Settings.CalcFloatDefault);
            dlg.AddNote("Без 32-битного результата 8-битные значения ограничиваются диапазоном 0…255: при сложении нескольких кадров " +
                        "сумма «упирается» в 255, а при вычитании отрицательные значения становятся нулём (как в ImageJ).");
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var a = _ws.Docs[i1.SelectedIndex];
            var b = _ws.Docs[i2.SelectedIndex];
            var o = (CalcOp)op.SelectedIndex;
            Ui.Busy(this, () =>
            {
                ImageData r = ImageOps.Calculate(a.Data, b.Data, o, flt.Checked);
                if (newWin.Checked)
                {
                    string[] sym = { "+", "−", "×", "÷", "AND", "OR", "XOR", "min", "max", "ср.", "|−|", "copy", "tz" };
                    r.Name = "Результат: " + a.Name + " " + sym[(int)o] + " " + b.Name;
                    r.FilePath = null;
                    AddImage(r).Modified = true;
                }
                else a.Replace(r, "Калькулятор");
            });
        }

        private void MathDialog(MathOp op)
        {
            var doc = RequireImage();
            if (doc == null) return;
            if (!ImageOps.MathNeedsValue(op))
            {
                ApplyToActive(ImageOps.MathName(op), d => ImageOps.ApplyMath(d, op, 0));
                return;
            }
            double def;
            switch (op)
            {
                case MathOp.Multiply: case MathOp.Divide: def = 1.25; break;
                case MathOp.Min: def = 0; break;
                case MathOp.Max: def = 255; break;
                case MathOp.Gamma: def = 0.5; break;
                default: def = 25; break;
            }
            var dlg = new ParamDialog(ImageOps.MathName(op));
            if (op == MathOp.Min) dlg.AddNote("«Min»: значения меньше заданного заменяются им (ограничение снизу).");
            if (op == MathOp.Max) dlg.AddNote("«Max»: значения больше заданного заменяются им (ограничение сверху).");
            var v = dlg.AddNumber("Значение:", def, 4);
            dlg.EnablePreview();
            RunFilterDialog(dlg, ImageOps.MathName(op) + " " + Ui.G(v.Value), d => ImageOps.ApplyMath(d, op, v.Value));
        }

        private void NoiseDialog(NoiseKind kind)
        {
            if (RequireImage() == null) return;
            ParamDialog dlg;
            NumBox p;
            string action;
            switch (kind)
            {
                case NoiseKind.SaltPepper:
                    dlg = new ParamDialog("Импульсный шум «соль и перец»", "Заданная доля пикселей заменяется: половина — белыми, половина — чёрными. В ImageJ — 5% (2,5% + 2,5%).");
                    p = dlg.AddNumber("Доля пикселей:", 5, 2, 0, 100, "%");
                    action = "Соль и перец";
                    break;
                case NoiseKind.Uniform:
                    dlg = new ParamDialog("Равномерный аддитивный шум", "К каждому пикселю прибавляется случайное число, равномерно распределённое в [−A; +A]. СКО такого шума σ = A/√3.");
                    p = dlg.AddNumber("Амплитуда A:", 25, 2, 0);
                    action = "Равномерный шум";
                    break;
                case NoiseKind.Speckle:
                    dlg = new ParamDialog("Мультипликативный (спекл) шум", "Каждый пиксель умножается на (1 + n), где n ~ N(0, σ). Шум пропорционален сигналу.");
                    p = dlg.AddNumber("σ (доля сигнала):", 0.1, 3, 0, 10);
                    action = "Спекл-шум";
                    break;
                default:
                    dlg = new ParamDialog("Добавить шум (Add Specified Noise)", "Аддитивный гауссов шум с нулевым средним и заданным стандартным отклонением (как Process › Noise › Add Specified Noise в ImageJ).");
                    p = dlg.AddNumber("Стандартное отклонение (σ):", 25, 2, 0);
                    action = "Шум";
                    break;
            }
            var seedBox = dlg.AddCheck("Фиксировать генератор (одинаковый шум при повторе)", false);
            dlg.EnablePreview();
            RunFilterDialog(dlg, action + " " + Ui.G(p.Value), d =>
            {
                double v = kind == NoiseKind.SaltPepper ? p.Value / 100.0 : p.Value;
                return Noise.Add(d, kind, v, seedBox.Checked ? (int?)12345 : null);
            });
        }

        private void ConvolveDialog()
        {
            if (RequireImage() == null) return;
            var dlg = new ParamDialog("Свёртка (Convolve)", "Введите коэффициенты маски через пробел, каждую строку маски — с новой строки " +
                                                          "(как в Process › Filters › Convolve программы ImageJ). Число строк и столбцов — нечётное. " +
                                                          "Шаблон подставляет в поле маску из методички (рис. 4.18) заданного размера — её можно изменить.");
            var kinds = (MaskKind[])Enum.GetValues(typeof(MaskKind));
            var names = new object[kinds.Length + 1];
            names[0] = "— своя маска —";
            for (int i = 0; i < kinds.Length; i++) names[i + 1] = Masks.KindName(kinds[i]);
            var tpl = dlg.AddChoice("Шаблон:", names, _maskTemplate, 300);
            var size = dlg.AddSize("Размер шаблона:", _maskSize[0], _maskSize[1], 1, Masks.MaxTemplateSize);
            var weights = dlg.AddChoice("Веса:", new object[] { "биномиальные (1 4 6 4 1)", "треугольные (1 2 3 2 1)", "свои: строка ** столбец" }, _maskWeights, 300);
            var ownText = dlg.AddText("Свои веса:", _maskOwnWeights, 1, 300);
            var text = dlg.AddText("Маска:", _lastKernel, 9, 360);
            var info = dlg.AddNote("");
            var norm = dlg.AddCheck("Нормировать (Normalize Kernel)", true);
            var toNew = dlg.AddCheck("Результат — в новое изображение", false);
            Action describe = () =>
            {
                float[] k; int r, c; string err;
                if (!Masks.TryParse(text.Text, out k, out r, out c, out err))
                {
                    info.Text = err;
                    info.ForeColor = Color.Firebrick;
                    return;
                }
                info.ForeColor = Theme.Muted;
                info.Text = "Маска " + r + "×" + c + " (строки × столбцы), сумма коэффициентов " +
                            Masks.Sum(k).ToString("0.####", System.Globalization.CultureInfo.CurrentCulture);
            };
            text.TextChanged += (s, e) => describe();
            describe();

            // размер, выбранный пользователем; у шаблонов фиксированного размера и при своих весах в полях показывается их размер
            int[] want = { _maskSize[0], _maskSize[1] };
            string generated = null;
            bool setting = false;
            Func<MaskKind?> kind = () => tpl.SelectedIndex > 0 ? kinds[tpl.SelectedIndex - 1] : (MaskKind?)null;
            Func<bool> ownWeights = () => kind() == MaskKind.SmallDetails && weights.SelectedIndex == 2;
            Func<string> templateError = () =>
            {
                if (kind() == null) return null;
                double[] hz, vt; string err;
                if (ownWeights()) return Masks.TryParseProfiles(ownText.Text, out hz, out vt, out err) ? null : "Свои веса: " + err;
                return ParamDialog.IsOddSize(size) ? null : "Размер шаблона: число строк и столбцов — нечётное, от 1 до " + Masks.MaxTemplateSize + ".";
            };
            // текст маски по шаблону; вызывать, только если templateError() == null
            Func<string> build = () =>
            {
                if (ownWeights())
                {
                    double[] hz, vt; string err;
                    Masks.TryParseProfiles(ownText.Text, out hz, out vt, out err);
                    return Masks.ToText(Masks.Separable(hz, vt), vt.Length, hz.Length);
                }
                int h = size[0].IntValue, w = size[1].IntValue;
                var profile = weights.SelectedIndex == 1 ? WeightProfile.Triangular : WeightProfile.Binomial;
                return Masks.ToText(Masks.Make(kind().Value, w, h, profile), h, w);
            };
            Action showSize = () =>
            {
                int fw = 0, fh = 0;
                if (kind() != null) Masks.FixedSize(kind().Value, out fw, out fh);
                double[] hz, vt; string err;
                if (ownWeights() && Masks.TryParseProfiles(ownText.Text, out hz, out vt, out err)) { fw = hz.Length; fh = vt.Length; }
                setting = true;
                size[0].Enabled = kind() != null && fh == 0 && !ownWeights();
                size[1].Enabled = kind() != null && fw == 0 && !ownWeights();
                size[0].Value = fh > 0 ? fh : want[0];
                size[1].Value = fw > 0 ? fw : want[1];
                weights.Enabled = kind() == MaskKind.SmallDetails;
                ownText.Enabled = ownWeights();
                setting = false;
            };
            Action generate = () =>
            {
                if (kind() == null) return;
                string err = templateError();
                if (err != null) { info.Text = err; info.ForeColor = Color.Firebrick; return; }
                generated = build();
                text.Text = generated;
            };
            showSize();
            if (kind() != null && templateError() == null && build() == text.Text) generated = text.Text;
            tpl.SelectedIndexChanged += (s, e) => { showSize(); generate(); };
            weights.SelectedIndexChanged += (s, e) => { showSize(); generate(); };
            ownText.TextChanged += (s, e) =>
            {
                if (setting || !ownText.Enabled) return;
                showSize();
                generate();
            };
            for (int i = 0; i < 2; i++)
            {
                int j = i;
                size[j].TextChanged += (s, e) =>
                {
                    if (setting || !size[j].Enabled) return;
                    if (size[j].Validate() == null) want[j] = size[j].IntValue;
                    generate();
                };
            }
            dlg.Validator = () =>
            {
                float[] k; int r, c; string err;
                return templateError() ?? (Masks.TryParse(text.Text, out k, out r, out c, out err) ? null : err);
            };
            dlg.EnablePreview();
            Func<ImageData, ImageData> f = d =>
            {
                float[] k; int r, c; string err;
                if (!Masks.TryParse(text.Text, out k, out r, out c, out err)) throw new ArgumentException(err);
                return Filters.Convolve(d, k, c, r, norm.Checked);
            };
            dlg.FormClosed += (s, e) =>
            {
                if (dlg.DialogResult != DialogResult.OK) return;
                _lastKernel = text.Text;
                _maskTemplate = tpl.SelectedIndex;
                _maskSize = want;
                _maskWeights = weights.SelectedIndex;
                _maskOwnWeights = ownText.Text;
            };
            RunFilterDialog(dlg, "Свёртка", f, true, toNew, () =>
            {
                float[] k; int r, c; string err;
                Masks.TryParse(text.Text, out k, out r, out c, out err);
                if (text.Text != generated) return "свёртка " + r + "×" + c;
                string what = Masks.KindName(kind().Value) + " " + r + "×" + c;
                if (kind() == MaskKind.SmallDetails && weights.SelectedIndex > 0)
                    what += weights.SelectedIndex == 1 ? " (треугольные веса)" : " (свои веса)";
                return what;
            });
        }

        private string _lastKernel = Masks.ImageJDefault;
        private int _maskTemplate;
        private int[] _maskSize = { 3, 3 }; // строки, столбцы
        private int _maskWeights;
        private string _maskOwnWeights = "1 2 1 ** 1 2 1";

        private void GaussianDialog()
        {
            if (RequireImage() == null) return;
            var dlg = new ParamDialog("Размытие по Гауссу (Gaussian Blur)", "Параметр — стандартное отклонение гауссианы (в ImageJ поле «Sigma (Radius)»).");
            var s = dlg.AddNumber("Sigma (радиус):", 2, 2, 0.01, 200, "пикс.");
            var toNew = dlg.AddCheck("Результат — в новое изображение", false);
            dlg.EnablePreview();
            RunFilterDialog(dlg, "Gaussian Blur", d => Filters.GaussianBlur(d, s.Value), true, toNew, () => "Gaussian Blur σ=" + Ui.G(s.Value));
        }

        private void RankDialog(RankType t)
        {
            if (RequireImage() == null) return;
            var dlg = new ParamDialog(Filters.RankName(t), "Апертура фильтра — круг заданного радиуса (как Process › Filters в ImageJ) " +
                                                           "или прямоугольник строки × столбцы, например 3×5 — 3 строки по 5 пикселей.");
            var shape = dlg.AddChoice("Апертура:", new object[] { "круг заданного радиуса", "прямоугольник" }, _rankRect ? 1 : 0);
            var r = dlg.AddNumber("Радиус:", 2, 1, 0.5, 100, "пикс.");
            var size = dlg.AddSize("Размер:", _rankSize[0], _rankSize[1], 1, 101);
            var area = dlg.AddNote("");
            Func<bool> rect = () => shape.SelectedIndex == 1;
            Action upd = () =>
            {
                r.Enabled = !rect();
                size[0].Enabled = size[1].Enabled = rect();
                area.ForeColor = Theme.Muted;
                if (!rect())
                {
                    double v = r.Value;
                    area.Text = double.IsNaN(v) || v < 0.5 ? "" : "Апертура: " + Filters.CircleArea(v) + " пикселей";
                }
                else if (ParamDialog.IsOddSize(size))
                    area.Text = "Апертура: " + size[0].IntValue + "×" + size[1].IntValue + " = " + size[0].IntValue * size[1].IntValue + " пикселей";
                else
                {
                    area.Text = "Число строк и столбцов — нечётное (1, 3, 5…).";
                    area.ForeColor = Color.Firebrick;
                }
            };
            shape.SelectedIndexChanged += (s, e) => upd();
            r.TextChanged += (s, e) => upd();
            size[0].TextChanged += (s, e) => upd();
            size[1].TextChanged += (s, e) => upd();
            upd();
            dlg.Validator = () => rect() && !ParamDialog.IsOddSize(size) ? "Число строк и столбцов апертуры должно быть нечётным (1, 3, 5…)." : null;
            var toNew = dlg.AddCheck("Результат — в новое изображение", false);
            dlg.EnablePreview();
            dlg.FormClosed += (s, e) =>
            {
                if (dlg.DialogResult != DialogResult.OK) return;
                _rankRect = rect();
                if (_rankRect) _rankSize = new[] { size[0].IntValue, size[1].IntValue };
            };
            RunFilterDialog(dlg, Filters.RankName(t),
                d => rect() ? Filters.Rank(d, t, size[1].IntValue, size[0].IntValue) : Filters.Rank(d, t, r.Value), true, toNew,
                () => Filters.RankName(t) + (rect() ? " " + size[0].IntValue + "×" + size[1].IntValue : " r=" + Ui.G(r.Value)));
        }

        private bool _rankRect;
        private int[] _rankSize = { 3, 3 }; // строки, столбцы

        // ================================================================== Анализ

        private void Measure()
        {
            var doc = RequireImage();
            if (doc == null) return;
            Rectangle r = Stats.Normalize(doc.Data, doc.Roi);
            var s = Stats.Compute(doc.Data, r, Channel);
            AddResult(doc.Name + ChannelName, doc.Roi.IsEmpty ? "всё изображение" : "выделение", r, s);
        }

        private void ShowHistogram()
        {
            var doc = RequireImage();
            if (doc == null) return;
            new HistogramForm(doc.Data, doc.Roi, Channel, doc.Roi.IsEmpty ? "всё изображение" : "выделение", this).Show(this);
        }

        private void ShowProfile()
        {
            var doc = RequireImage();
            if (doc == null) return;
            new ProfileForm(doc.Data, doc.Roi, Channel).Show(this);
        }

        private void AddRoi()
        {
            var doc = RequireImage();
            if (doc == null) return;
            if (doc.Roi.IsEmpty) { Ui.Info(this, "Сначала выделите область мышью."); return; }
            _ws.Rois.Add(new NamedRoi { Name = (_ws.Rois.Count + 1).ToString(), Rect = doc.Roi });
            _ws.NotifyRois();
        }

        private void DeleteRoi()
        {
            int i = _roiList.SelectedIndex;
            if (i < 0 || i >= _ws.Rois.Count) { Ui.Info(this, "Выберите область в списке."); return; }
            _ws.Rois.RemoveAt(i);
            _ws.NotifyRois();
        }

        private void RenameRoi()
        {
            int i = _roiList.SelectedIndex;
            if (i < 0 || i >= _ws.Rois.Count) { Ui.Info(this, "Выберите область в списке."); return; }
            var dlg = new ParamDialog("Переименовать область");
            var t = dlg.AddText("Название:", _ws.Rois[i].Name, 1, 220);
            dlg.Validator = () => string.IsNullOrEmpty(t.Text.Trim()) ? "Название не может быть пустым." : null;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _ws.Rois[i].Name = t.Text.Trim();
            _ws.NotifyRois();
        }

        /// <summary>ROI Manager › Measure: измерение всех сохранённых областей на активном изображении.</summary>
        private void MeasureRois()
        {
            var doc = RequireImage();
            if (doc == null) return;
            if (_ws.Rois.Count == 0) { Ui.Info(this, "Список областей пуст: выделите область и нажмите «Добавить» (Ctrl+T)."); return; }
            foreach (var r in _ws.Rois)
            {
                if (r.Rect.Right > doc.Data.Width || r.Rect.Bottom > doc.Data.Height) continue;
                AddResult(doc.Name + ChannelName, r.Name, r.Rect, Stats.Compute(doc.Data, r.Rect, Channel));
            }
        }

        private void PsnrWindow() { new PsnrForm(this).Show(this); }

        public void StepchartWindow() { new StepchartForm(this).Show(this); }

        // ================================================================== Imatest

        public void ChartGeneratorDialog()
        {
            var dlg = new ParamDialog("Генератор тестовых таблиц (Test Charts)",
                "Ступенчатая шкала для модуля Stepchart: уровень пикселя для зоны с оптической плотностью D равен 255·(10^−D)^(1/γ). " +
                "γ = 2,2 — для печати на принтере, γ = 1 — линейное кодирование.");
            var type = dlg.AddChoice("Тип таблицы:", new object[] { "Ступенчатая шкала (Stepchart)", "Плавный градиент", "Шахматное поле (Distortion)", "Однородное поле" }, 0, 280);
            var steps = dlg.AddNumber("Число градаций:", 20, 0, 2, 100);
            var dstep = dlg.AddNumber("Шаг плотности ΔD:", 0.10, 3, 0.01, 1);
            var d0 = dlg.AddNumber("Плотность первой зоны:", 0.0, 3, 0, 4);
            var gamma = dlg.AddNumber("Гамма кодирования γ:", 2.2, 2, 0.1, 5);
            var vert = dlg.AddCheck("Вертикальная шкала", false);
            var w = dlg.AddNumber("Ширина:", 1600, 0, 16, 20000, "пикс.");
            var h = dlg.AddNumber("Высота:", 400, 0, 16, 20000, "пикс.");
            var margin = dlg.AddNumber("Поле вокруг шкалы:", 40, 0, 0, 2000, "пикс.");
            var cell = dlg.AddNumber("Клетка / уровень:", 64, 0, 1, 2000);
            var info = dlg.AddNote("");
            Action upd = () =>
            {
                int t = type.SelectedIndex;
                steps.Enabled = dstep.Enabled = d0.Enabled = gamma.Enabled = vert.Enabled = margin.Enabled = t == 0;
                cell.Enabled = t == 2 || t == 3;
                if (t == 0 && !double.IsNaN(dstep.Value) && !double.IsNaN(steps.Value))
                {
                    double dmax = d0.Value + (steps.Value - 1) * dstep.Value;
                    info.Text = "Плотность от " + Ui.G(d0.Value) + " до " + Ui.G(dmax) + " (" + Ui.F(dmax / Stepchart.Log2, 1) + " f-stop). Уровни: " +
                                Math.Round(TestCharts.LevelForDensity(d0.Value, gamma.Value)) + " … " + Math.Round(TestCharts.LevelForDensity(dmax, gamma.Value));
                }
                else if (t == 2) info.Text = "«Клетка» — размер клетки в пикселях.";
                else if (t == 3) info.Text = "«Клетка / уровень» — яркость поля (0…255).";
                else info.Text = "";
            };
            foreach (Control c in new Control[] { type, steps, dstep, d0, gamma, cell }) c.TextChanged += (s, e) => upd();
            type.SelectedIndexChanged += (s, e) => upd();
            upd();
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            Ui.Busy(this, () =>
            {
                ImageData img;
                switch (type.SelectedIndex)
                {
                    case 1: img = TestCharts.Ramp(w.IntValue, h.IntValue, true); break;
                    case 2: img = TestCharts.Checkerboard(w.IntValue, h.IntValue, cell.IntValue, 0, 255); break;
                    case 3: img = TestCharts.Uniform(w.IntValue, h.IntValue, Math.Max(0, Math.Min(255, cell.Value))); break;
                    default:
                        img = TestCharts.StepChart(new StepChartSpec
                        {
                            Steps = steps.IntValue, DensityStep = dstep.Value, FirstDensity = d0.Value, EncodingGamma = gamma.Value,
                            Horizontal = !vert.Checked, Width = w.IntValue, Height = h.IntValue, Margin = margin.IntValue
                        });
                        break;
                }
                AddImage(img).Modified = true;
            });
        }

        // ================================================================== Справка

        private void ShowGuide() { ShowGuide(null); }

        private void ShowGuide(string anchor) { HelpForm.ShowHelp(this, anchor); }

        private void ShowAbout()
        {
            using (var f = new AboutForm()) f.ShowDialog(this);
        }
    }
}
