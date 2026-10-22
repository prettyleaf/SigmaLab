using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using VideoMeter.Core;

namespace VideoMeter.UI
{
    public sealed partial class MainForm
    {
        private readonly List<ToolStripItem> _needImage = new List<ToolStripItem>();
        private ToolStripMenuItem _miUndo, _miRecent;

        private ToolStripMenuItem Item(string text, Action a, Keys keys = Keys.None, string icon = null, bool needsImage = true)
        {
            var mi = new ToolStripMenuItem(text);
            if (icon != null) mi.Image = Icons.Get(icon);
            if (keys != Keys.None) mi.ShortcutKeys = keys;
            mi.Click += (s, e) => a();
            if (needsImage) _needImage.Add(mi);
            return mi;
        }

        private static ToolStripMenuItem Sub(string text, params ToolStripItem[] items)
        {
            var mi = new ToolStripMenuItem(text);
            mi.DropDownItems.AddRange(items);
            return mi;
        }

        private static ToolStripSeparator Sep() { return new ToolStripSeparator(); }

        private MenuStrip BuildMenu()
        {
            var ms = new MenuStrip { Renderer = Theme.Renderer, Font = Theme.UiFont, BackColor = Color.White, Padding = new Padding(Theme.S(4), Theme.S(2), 0, Theme.S(2)) };

            _miRecent = new ToolStripMenuItem("Последние файлы");
            _miRecent.DropDownOpening += (s, e) => FillRecent();
            _miRecent.DropDownItems.Add("(пусто)");

            var file = Sub("Файл",
                Item("Открыть…", OpenDialog, Keys.Control | Keys.O, "open", false),
                _miRecent,
                Item("Импорт кадров из видео…", ImportVideo, Keys.None, "video", false),
                Item("Создать изображение…", NewImageDialog, Keys.Control | Keys.N, "new", false),
                Sep(),
                Item("Сохранить", Save, Keys.Control | Keys.S, "save"),
                Item("Сохранить как…", SaveAs, Keys.Control | Keys.Shift | Keys.S),
                Item("Сохранить все изображения в папку…", SaveAllToFolder, Keys.None, null, false),
                Sep(),
                Item("Закрыть", CloseActive, Keys.Control | Keys.W),
                Item("Закрыть все", CloseAll, Keys.None, null, false),
                Sep(),
                Item("Печать…", PrintActive, Keys.Control | Keys.P),
                Sep(),
                new ToolStripMenuItem("Выход", null, (s, e) => Close()) { ShortcutKeyDisplayString = "Alt+F4" });

            _miUndo = Item("Отменить", Undo, Keys.Control | Keys.Z, "undo");
            var edit = Sub("Правка",
                _miUndo,
                Sep(),
                Item("Дублировать", Duplicate, Keys.Control | Keys.Shift | Keys.D),
                Item("Переименовать…", Rename),
                Sep(),
                Item("Выделить всё", SelectAll, Keys.Control | Keys.A),
                Item("Снять выделение", SelectNone, Keys.Control | Keys.Shift | Keys.A),
                Item("Задать выделение точно…", SpecifyRoi, Keys.Control | Keys.Shift | Keys.E),
                Sep(),
                Item("Настройки…", OptionsDialog, Keys.None, null, false));

            var image = Sub("Изображение",
                Sub("Тип",
                    Item("8 бит", () => ConvertType(PixelType.Gray8)),
                    Item("16 бит", () => ConvertType(PixelType.Gray16)),
                    Item("32 бита (float)", () => ConvertType(PixelType.Gray32)),
                    Item("RGB", () => ConvertType(PixelType.Rgb24))),
                Sep(),
                Item("Обрезать по выделению", Crop, Keys.Control | Keys.Shift | Keys.X),
                Item("Инвертировать", () => ApplyToActive("Инвертирование", ImageOps.Invert), Keys.Control | Keys.Shift | Keys.I),
                Sub("Отразить / повернуть",
                    Item("Отразить по горизонтали", () => ApplyToActive("Отражение", ImageOps.FlipHorizontal, false)),
                    Item("Отразить по вертикали", () => ApplyToActive("Отражение", ImageOps.FlipVertical, false)),
                    Item("Повернуть на 90° вправо", () => ApplyToActive("Поворот", d => ImageOps.Rotate90(d, true), false)),
                    Item("Повернуть на 90° влево", () => ApplyToActive("Поворот", d => ImageOps.Rotate90(d, false), false))),
                Sep(),
                Sub("Масштаб просмотра",
                    Item("Увеличить\t+", () => _viewer.ZoomIn(), Keys.None, "zoomin"),
                    Item("Уменьшить\t−", () => _viewer.ZoomOut(), Keys.None, "zoomout"),
                    Item("Исходный размер (100%)", () => _viewer.ActualSize()),
                    Item("Вписать в окно", () => _viewer.FitToWindow(), Keys.Control | Keys.D0, "fit")),
                Item("Сведения об изображении", ShowImageInfo, Keys.Control | Keys.I));

            var process = Sub("Обработка",
                Item("Калькулятор изображений (Image Calculator)…", CalculatorDialog, Keys.None, "calc"),
                Sub("Математика (Math)",
                    MathItem(MathOp.Add), MathItem(MathOp.Subtract), MathItem(MathOp.Multiply), MathItem(MathOp.Divide),
                    Sep(), MathItem(MathOp.Min), MathItem(MathOp.Max), MathItem(MathOp.Gamma), MathItem(MathOp.Set),
                    Sep(), MathItem(MathOp.Log), MathItem(MathOp.Exp), MathItem(MathOp.Square), MathItem(MathOp.Sqrt), MathItem(MathOp.Abs), MathItem(MathOp.Reciprocal)),
                Sub("Шум (Noise)",
                    Item("Добавить шум (Add Noise, σ = 25)", () => ApplyToActive("Шум σ=25", d => Noise.AddGaussian(d, 25)), Keys.None, "noise"),
                    Item("Добавить шум с заданным σ (Add Specified Noise)…", () => NoiseDialog(NoiseKind.Gaussian)),
                    Sep(),
                    Item("Импульсный «соль и перец» (Salt and Pepper)…", () => NoiseDialog(NoiseKind.SaltPepper)),
                    Item("Равномерный аддитивный…", () => NoiseDialog(NoiseKind.Uniform)),
                    Item("Мультипликативный (спекл)…", () => NoiseDialog(NoiseKind.Speckle))),
                Sub("Фильтры (Filters)",
                    Item("Свёртка (Convolve)…", ConvolveDialog, Keys.None, "filter"),
                    Sep(),
                    Item("Размытие по Гауссу (Gaussian Blur)…", GaussianDialog),
                    Item("Медианный (Median)…", () => RankDialog(RankType.Median)),
                    Item("Среднее (Mean)…", () => RankDialog(RankType.Mean)),
                    Item("Минимум (Minimum)…", () => RankDialog(RankType.Minimum)),
                    Item("Максимум (Maximum)…", () => RankDialog(RankType.Maximum)),
                    Item("Дисперсия (Variance)…", () => RankDialog(RankType.Variance))),
                Sep(),
                Item("Сгладить (Smooth)", () => ApplyToActive("Сглаживание", Filters.Smooth), Keys.Control | Keys.Shift | Keys.L),
                Item("Повысить резкость (Sharpen)", () => ApplyToActive("Резкость", Filters.Sharpen)),
                Item("Найти края (Find Edges)", () => ApplyToActive("Края", Filters.FindEdges)));

            var analyze = Sub("Анализ",
                Item("Измерить (Measure)", Measure, Keys.Control | Keys.M, "measure"),
                Item("Гистограмма (Histogram)", ShowHistogram, Keys.Control | Keys.H, "hist"),
                Item("Профиль (Plot Profile)", ShowProfile, Keys.Control | Keys.K, "profile"),
                Sep(),
                Sub("Менеджер областей (ROI Manager)",
                    Item("Добавить выделение (Add)", AddRoi, Keys.Control | Keys.T),
                    Item("Измерить все области (Measure)", MeasureRois)),
                Sep(),
                Item("Расчёт ОСШ и ПОСШ…", () => PsnrWindow(), Keys.Control | Keys.Shift | Keys.P, "psnr", false));

            var imatest = Sub("Imatest",
                Item("Stepchart…", StepchartWindow, Keys.None, "stepchart", false),
                Item("Генератор тестовых таблиц (Test Charts)…", ChartGeneratorDialog, Keys.None, null, false));

            var help = Sub("Справка",
                Item("Руководство пользователя", ShowGuide, Keys.F1, "help", false),
                Item("Соответствие команд ImageJ и Imatest", () => ShowGuide("map"), Keys.None, null, false),
                Sep(),
                Item("Лицензионное соглашение", () => LicenseForm.ShowLicense(this), Keys.None, null, false),
                Item("О программе", ShowAbout, Keys.None, null, false));

            ms.Items.AddRange(new ToolStripItem[] { file, edit, image, process, analyze, imatest, help });
            return ms;
        }

        private ToolStripMenuItem MathItem(MathOp op)
        {
            string name = ImageOps.MathName(op) + (ImageOps.MathNeedsValue(op) ? "…" : "");
            return Item(name, () => MathDialog(op));
        }

        private ToolStrip BuildToolbar()
        {
            var ts = new ToolStrip
            {
                Renderer = Theme.Renderer,
                GripStyle = ToolStripGripStyle.Hidden,
                ImageScalingSize = new Size(Theme.S(16), Theme.S(16)),
                Padding = new Padding(Theme.S(6), Theme.S(3), 0, Theme.S(3)),
                Font = Theme.UiFont
            };
            _tbSelect = new ToolStripButton(null, Icons.Get("rect")) { ToolTipText = "Прямоугольное выделение", Checked = true, DisplayStyle = ToolStripItemDisplayStyle.Image };
            _tbPan = new ToolStripButton(null, Icons.Get("hand")) { ToolTipText = "Перемещение (рука). Также: правая кнопка мыши или пробел", DisplayStyle = ToolStripItemDisplayStyle.Image };
            _tbSelect.Click += (s, e) => SetTool(ImageViewer.ToolMode.Select);
            _tbPan.Click += (s, e) => SetTool(ImageViewer.ToolMode.Pan);

            ts.Items.AddRange(new ToolStripItem[]
            {
                btn(null, "open", "Открыть (Ctrl+O)", OpenDialog, false),
                btn(null, "save", "Сохранить (Ctrl+S)", Save, true),
                btn(null, "undo", "Отменить (Ctrl+Z)", Undo, true),
                new ToolStripSeparator(),
                _tbSelect, _tbPan,
                btn(null, "zoomin", "Увеличить (+)", () => _viewer.ZoomIn(), true),
                btn(null, "zoomout", "Уменьшить (−)", () => _viewer.ZoomOut(), true),
                btn(null, "fit", "Вписать в окно (Ctrl+0)", () => _viewer.FitToWindow(), true),
                new ToolStripSeparator(),
                btn("Гистограмма", "hist", "Гистограмма выделения (Ctrl+H)", ShowHistogram, true),
                btn("Измерить", "measure", "Измерить: N, Mean, StdDev, Min, Max (Ctrl+M)", Measure, true),
                btn("Профиль", "profile", "Профиль яркости (Ctrl+K)", ShowProfile, true),
                new ToolStripSeparator(),
                btn("Калькулятор", "calc", "Калькулятор изображений", CalculatorDialog, true),
                btn("Свёртка", "filter", "Свёртка с маской (Convolve)", ConvolveDialog, true),
                btn("Шум", "noise", "Добавить шум с заданным σ", () => NoiseDialog(NoiseKind.Gaussian), true),
                new ToolStripSeparator(),
                btn("Stepchart", "stepchart", "Модуль Stepchart (Imatest)", StepchartWindow, false),
                btn("ОСШ/ПОСШ", "psnr", "Расчёт ОСШ и ПОСШ", () => PsnrWindow(), false),
            });
            return ts;
        }

        private ToolStripButton btn(string text, string icon, string tip, Action a, bool needs)
        {
            var b = new ToolStripButton(text, Icons.Get(icon)) { ToolTipText = tip, DisplayStyle = text == null ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText };
            b.Click += (s, e) => a();
            if (needs) _needImage.Add(b);
            return b;
        }

        private void SetTool(ImageViewer.ToolMode m)
        {
            _viewer.Tool = m;
            _tbSelect.Checked = m == ImageViewer.ToolMode.Select;
            _tbPan.Checked = m == ImageViewer.ToolMode.Pan;
        }

        private void UpdateMenuState()
        {
            bool has = _ws.Active != null;
            foreach (var it in _needImage) it.Enabled = has;
            _miUndo.Enabled = has && _ws.Active.CanUndo;
            _miUndo.Text = has && _ws.Active.CanUndo && !string.IsNullOrEmpty(_ws.Active.UndoName) ? "Отменить: " + _ws.Active.UndoName : "Отменить";
        }

        private void FillRecent()
        {
            _miRecent.DropDownItems.Clear();
            if (Settings.Recent.Count == 0)
            {
                _miRecent.DropDownItems.Add(new ToolStripMenuItem("(пусто)") { Enabled = false });
                return;
            }
            foreach (string p in Settings.Recent)
            {
                string path = p;
                _miRecent.DropDownItems.Add(new ToolStripMenuItem(path, null, (s, e) => OpenFiles(new[] { path })));
            }
        }
    }
}
