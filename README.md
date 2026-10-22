# СигмаЛаб (SigmaLab)

Учебная программа кафедры телевидения и управления (ТУ) ТУСУР для лабораторной работы № 1
«Измерение характеристик сигналов и шумов в телевизионном изображении». Заменяет используемые в работе
ImageJ и Imatest (модуль Stepchart) и дополнительно позволяет рассчитать ОСШ и ПОСШ.

- Авторы лабораторной работы: Каменский А.В. (доцент кафедры ТУ), Курячий М.И. (профессор кафедры ТУ)
- Разработчик программы: Колесников И.Д.
- © 2026 ТУСУР, кафедра ТУ; учебная лицензия кафедры — `src/Resources/license.txt`

## Принцип

Программа даёт **инструменты**, а не выполняет лабораторную работу: студент сам открывает изображения, выделяет области,
вводит маски и параметры, строит гистограммы, считает ОСШ по формулам, заполняет таблицы и строит графики — по методичке.
Команды названы так же, как в ImageJ/Imatest (с русским переводом), чтобы их легко было найти по тексту методички.
Пресетов «под пункты методички», готовых масок из методички и автоматических серий измерений нет.

## Состав

- ImageJ: открытие/сохранение (TIFF без сжатия, PNG, BMP, JPEG), File › New › Image, Image Calculator (32-bit result),
  Process › Math, Noise (Add Specified Noise и др.), Filters (Convolve с вводом маски, Gaussian Blur, Median, Mean,
  Minimum, Maximum, Variance), Histogram, Measure, Plot Profile, ROI Manager, отмена, обрезка, типы 8/16/32 бит и RGB.
- Imatest: Stepchart с настройками Chart type, Zone detection, Results (Fig. 1/Fig. 2), Plot 1–3; рисунки сохраняются
  в TIFF без сжатия; Test Charts (генератор ступенчатой шкалы).
- Дополнительно: калькулятор ОСШ по введённым M и σ; ОСШ, ПОСШ, RMSE, SSIM изображения относительно эталона;
  импорт кадров из видео (AVI — средствами Windows, MPEG/MP4 — через ffmpeg.exe).

## Совместимость

Windows 7 SP1, 8.1, 10, 11 (32 и 64 бит). Написано на C# / WinForms под .NET Framework 3.5 с `App.config`, разрешающим
запуск на .NET 4.x: в Windows 7 есть 3.5, в Windows 10/11 — 4.8, поэтому ничего доустанавливать не нужно.
Интерфейс учитывает масштаб экрана 125–200%.

## Сборка

Нужен .NET SDK 8+ (на Linux или Windows). Установщик — Inno Setup 6 (на Linux через wine).

```sh
./build.sh          # Linux: тесты ядра, SigmaLab.exe, ../dist/*-portable.zip и ../dist/*-setup.exe
build.cmd           # Windows
```

Результат: `src/bin/Release/net35/SigmaLab.exe` (+ `SigmaLab.exe.config`, он обязателен рядом с exe).

Релиз собирает GitHub Actions (`.github/workflows/release.yml`): поднять `<Version>` в `src/SigmaLab.csproj`, закоммитить и
отправить тег той же версии — установщик и портативный архив появятся в файлах релиза.

```sh
git tag -a v1.0.2 -m "Что нового" && git push origin main v1.0.2
```

## Устройство

```
src/Core   обработка без UI: ImageData, ImageOps (Math, Calculator), Noise, Filters (Convolve, Gaussian, Rank),
           Masks (разбор ядра свёртки), Stats (Mean, StdDev, гистограмма), Quality (ПОСШ, SSIM), Stepchart, TestCharts
src/IO     TIFF без сжатия (включая 32-бит float), GDI+ для прочих форматов, кадры видео (avifil32 / ffmpeg)
src/UI     окна WinForms в цветах ТУСУР (#3C388D, #9ABF38)
tests      тесты ядра (.NET 8): сверка с прямым перебором, восстановление гаммы и шума Stepchart и т. д.
installer  скрипт Inno Setup
```

`SigmaLab.exe --shots <папка>` — служебный режим самопроверки интерфейса (снимки окон на синтетических данных).
