using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VideoMeter.Core;

namespace VideoMeter.IO
{
    /// <summary>
    /// Извлечение кадров из видеозаписи: AVI — средствами Windows (Video for Windows, avifil32.dll),
    /// прочие форматы (MPEG, MP4, MKV, …) — через ffmpeg.exe, если он есть рядом с программой или в PATH.
    /// </summary>
    public static class VideoFrames
    {
        public const string Filter = "Видео (*.avi;*.mpg;*.mpeg;*.mp4;*.mkv;*.mov;*.wmv;*.ts;*.vob)|*.avi;*.mpg;*.mpeg;*.mp4;*.mkv;*.mov;*.wmv;*.ts;*.vob|Все файлы (*.*)|*.*";

        public static string FfmpegPathOverride;

        public static bool IsAvi(string path)
        {
            return Path.GetExtension(path).Equals(".avi", StringComparison.OrdinalIgnoreCase);
        }

        public static string FindFfmpeg()
        {
            if (!string.IsNullOrEmpty(FfmpegPathOverride) && File.Exists(FfmpegPathOverride)) return FfmpegPathOverride;
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            foreach (string p in new[] { Path.Combine(dir, "ffmpeg.exe"), Path.Combine(dir, @"ffmpeg\ffmpeg.exe"), Path.Combine(dir, @"ffmpeg\bin\ffmpeg.exe") })
                if (File.Exists(p)) return p;
            string env = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string part in env.Split(Path.PathSeparator))
            {
                try
                {
                    string p = Path.Combine(part.Trim(), "ffmpeg.exe");
                    if (File.Exists(p)) return p;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        /// <summary>Кадры start, start+step, … (count штук). Номера кадров с нуля.</summary>
        public static List<ImageData> Extract(string path, int start, int count, int step, out string method)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            Exception aviError = null;
            if (IsAvi(path))
            {
                try
                {
                    var frames = ExtractAvi(path, start, count, step);
                    method = "Video for Windows (avifil32)";
                    Name(frames, name, start, step);
                    return frames;
                }
                catch (Exception ex) { aviError = ex; }
            }
            string ff = FindFfmpeg();
            if (ff == null)
            {
                string msg = "Для этого видео нужен ffmpeg.exe (положите его в папку программы или в подпапку ffmpeg), " +
                             "либо сохраните кадры в видеоплеере (например, Media Player Classic: File › Save Image…) и откройте их как изображения.";
                if (aviError != null) msg = "Не удалось прочитать AVI средствами Windows: " + aviError.Message + "\n\n" + msg;
                throw new InvalidOperationException(msg);
            }
            var list = ExtractFfmpeg(ff, path, start, count, step);
            method = "ffmpeg";
            Name(list, name, start, step);
            return list;
        }

        private static void Name(List<ImageData> frames, string baseName, int start, int step)
        {
            for (int i = 0; i < frames.Count; i++)
            {
                frames[i].Name = baseName + "_кадр" + (start + i * step).ToString("0000");
                frames[i].FilePath = null;
            }
        }

        // ------------------------------------------------------------------ ffmpeg

        private static List<ImageData> ExtractFfmpeg(string ffmpeg, string path, int start, int count, int step)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "VideoMeter_frames_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            try
            {
                string sel = string.Format("select='gte(n\\,{0})*not(mod(n-{0}\\,{1}))'", start, Math.Max(1, step));
                // в ffmpeg 5.1+ ключ -vsync заменён на -fps_mode, а в новых версиях удалён; старые сборки (для Windows 7) знают только -vsync
                string err = RunFfmpeg(ffmpeg, path, sel, "-fps_mode passthrough", count, tmp);
                if (err.IndexOf("Unrecognized option", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    err.IndexOf("Option not found", StringComparison.OrdinalIgnoreCase) >= 0)
                    err = RunFfmpeg(ffmpeg, path, sel, "-vsync 0", count, tmp);
                string[] files = Directory.GetFiles(tmp, "f_*.bmp");
                Array.Sort(files, StringComparer.Ordinal);
                if (files.Length == 0) throw new InvalidOperationException("ffmpeg не вернул ни одного кадра. " + err);
                var res = new List<ImageData>();
                foreach (string f in files) res.Add(ImageIO.Load(f));
                return res;
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        private static string RunFfmpeg(string ffmpeg, string path, string sel, string syncOption, int count, string tmp)
        {
            foreach (string f in Directory.GetFiles(tmp)) File.Delete(f);
            var psi = new ProcessStartInfo(ffmpeg,
                "-hide_banner -loglevel error -i \"" + path + "\" -vf \"" + sel + "\" " + syncOption + " -frames:v " + count +
                " \"" + Path.Combine(tmp, "f_%04d.bmp") + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                string err = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } throw new TimeoutException("ffmpeg не завершился за 2 минуты."); }
                return err ?? "";
            }
        }

        // ------------------------------------------------------------------ Video for Windows

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [DllImport("avifil32.dll")] private static extern void AVIFileInit();
        [DllImport("avifil32.dll")] private static extern void AVIFileExit();
        [DllImport("avifil32.dll", CharSet = CharSet.Unicode, EntryPoint = "AVIStreamOpenFromFileW")]
        private static extern int AVIStreamOpenFromFile(out IntPtr ppavi, string szFile, int fccType, int lParam, int mode, IntPtr pclsidHandler);
        [DllImport("avifil32.dll")] private static extern int AVIStreamStart(IntPtr pavi);
        [DllImport("avifil32.dll")] private static extern int AVIStreamLength(IntPtr pavi);
        [DllImport("avifil32.dll")] private static extern int AVIStreamReadFormat(IntPtr pavi, int lPos, ref BITMAPINFOHEADER lpFormat, ref int cbFormat);
        [DllImport("avifil32.dll")] private static extern IntPtr AVIStreamGetFrameOpen(IntPtr pavi, ref BITMAPINFOHEADER lpbiWanted);
        [DllImport("avifil32.dll", EntryPoint = "AVIStreamGetFrameOpen")] private static extern IntPtr AVIStreamGetFrameOpenDefault(IntPtr pavi, IntPtr lpbiWanted);
        [DllImport("avifil32.dll")] private static extern IntPtr AVIStreamGetFrame(IntPtr pgf, int lPos);
        [DllImport("avifil32.dll")] private static extern int AVIStreamGetFrameClose(IntPtr pgf);
        [DllImport("avifil32.dll")] private static extern int AVIStreamRelease(IntPtr pavi);

        private const int StreamTypeVideo = 0x73646976; // 'vids'

        public static int AviFrameCount(string path)
        {
            AVIFileInit();
            IntPtr pavi = IntPtr.Zero;
            try
            {
                if (AVIStreamOpenFromFile(out pavi, path, StreamTypeVideo, 0, 0x20, IntPtr.Zero) != 0) return -1;
                return AVIStreamLength(pavi);
            }
            finally
            {
                if (pavi != IntPtr.Zero) AVIStreamRelease(pavi);
                AVIFileExit();
            }
        }

        private static List<ImageData> ExtractAvi(string path, int start, int count, int step)
        {
            AVIFileInit();
            IntPtr pavi = IntPtr.Zero, pgf = IntPtr.Zero;
            try
            {
                int hr = AVIStreamOpenFromFile(out pavi, path, StreamTypeVideo, 0, 0x20, IntPtr.Zero);
                if (hr != 0) throw new InvalidOperationException("видеопоток не открыт (код 0x" + hr.ToString("X8") + ")");
                int first = AVIStreamStart(pavi), len = AVIStreamLength(pavi);
                var fmt = new BITMAPINFOHEADER();
                int cb = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                AVIStreamReadFormat(pavi, first, ref fmt, ref cb);
                var want = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                    biWidth = fmt.biWidth,
                    biHeight = Math.Abs(fmt.biHeight),
                    biPlanes = 1,
                    biBitCount = 24,
                    biCompression = 0
                };
                pgf = AVIStreamGetFrameOpen(pavi, ref want);
                if (pgf == IntPtr.Zero) pgf = AVIStreamGetFrameOpenDefault(pavi, new IntPtr(1));
                if (pgf == IntPtr.Zero) throw new InvalidOperationException("в системе нет кодека для этого AVI");
                // Кадры читаются подряд от начала потока: при «прыжках» некоторые реализации
                // Video for Windows (например, wine) возвращают не тот кадр. Для коротких записей это быстро.
                var res = new List<ImageData>();
                int st = Math.Max(1, step);
                int lastWanted = Math.Min(first + len - 1, first + start + (count - 1) * st);
                for (int pos = first; pos <= lastWanted && res.Count < count; pos++)
                {
                    IntPtr dib = AVIStreamGetFrame(pgf, pos);
                    if (dib == IntPtr.Zero) break;
                    int rel = pos - first - start;
                    if (rel >= 0 && rel % st == 0) res.Add(FromDib(dib));
                }
                if (res.Count == 0) throw new InvalidOperationException("не удалось получить кадры (в файле " + len + " кадров)");
                return res;
            }
            finally
            {
                if (pgf != IntPtr.Zero) AVIStreamGetFrameClose(pgf);
                if (pavi != IntPtr.Zero) AVIStreamRelease(pavi);
                AVIFileExit();
            }
        }

        private static ImageData FromDib(IntPtr dib)
        {
            var h = (BITMAPINFOHEADER)Marshal.PtrToStructure(dib, typeof(BITMAPINFOHEADER));
            int w = h.biWidth, ht = Math.Abs(h.biHeight);
            int bpp = h.biBitCount;
            if (bpp != 24 && bpp != 32) throw new InvalidOperationException("неподдерживаемый формат кадра: " + bpp + " бит");
            int palette = (int)(h.biClrUsed * 4);
            long bits = dib.ToInt64() + h.biSize + palette;
            int bytesPP = bpp / 8;
            int stride = ((w * bpp + 31) / 32) * 4;
            var line = new byte[stride];
            var img = new ImageData(w, ht, PixelType.Rgb24);
            bool bottomUp = h.biHeight > 0;
            for (int y = 0; y < ht; y++)
            {
                int srcRow = bottomUp ? ht - 1 - y : y;
                Marshal.Copy(new IntPtr(bits + (long)srcRow * stride), line, 0, stride);
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    img.Planes[2][i] = line[x * bytesPP];
                    img.Planes[1][i] = line[x * bytesPP + 1];
                    img.Planes[0][i] = line[x * bytesPP + 2];
                }
            }
            return img;
        }
    }
}
