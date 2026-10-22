using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SigmaLab.Core;
using SigmaLab.IO;

namespace SigmaLab.UI
{
    /// <summary>Настройки пользователя (%APPDATA%\SigmaLab\settings.ini).</summary>
    public static class Settings
    {
        public static string LastDir = "";
        public static bool CalcFloatDefault;
        public static readonly List<string> Recent = new List<string>();

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SigmaLab");
                return Path.Combine(dir, "settings.ini");
            }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "LastDir": LastDir = v; break;
                        case "Luma": LabOptions.Luma = v == "Weighted" ? LumaMode.Weighted : LumaMode.Unweighted; break;
                        case "NoiseResample": LabOptions.NoiseResampleOutOfRange = v != "0"; break;
                        case "CalcFloat": CalcFloatDefault = v == "1"; break;
                        case "Ffmpeg": VideoFrames.FfmpegPathOverride = v; break;
                        case "Recent":
                            Recent.Clear();
                            foreach (string p in v.Split('|')) if (p.Length > 0) Recent.Add(p);
                            break;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var sb = new StringBuilder();
                sb.AppendLine("LastDir=" + LastDir);
                sb.AppendLine("Luma=" + LabOptions.Luma);
                sb.AppendLine("NoiseResample=" + (LabOptions.NoiseResampleOutOfRange ? "1" : "0"));
                sb.AppendLine("CalcFloat=" + (CalcFloatDefault ? "1" : "0"));
                sb.AppendLine("Ffmpeg=" + (VideoFrames.FfmpegPathOverride ?? ""));
                sb.AppendLine("Recent=" + string.Join("|", Recent.ToArray()));
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static void AddRecent(string path)
        {
            Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            Recent.Insert(0, path);
            while (Recent.Count > 10) Recent.RemoveAt(Recent.Count - 1);
        }
    }
}
