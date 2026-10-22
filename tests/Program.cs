using System;
using System.Drawing;
using SigmaLab.Core;

static class T
{
    static int fails = 0, passes = 0;
    static void Check(bool ok, string what) { if (ok) passes++; else { fails++; Console.WriteLine("FAIL: " + what); } }
    static void Near(double a, double b, double tol, string what) { Check(Math.Abs(a - b) <= tol, what + $" (got {a:0.#####}, want {b:0.#####} ±{tol})"); }

    static ImageData Rand8(int w, int h, int seed) { var r = new Random(seed); var im = new ImageData(w, h, PixelType.Gray8); for (int i = 0; i < im.PixelCount; i++) im.Planes[0][i] = r.Next(256); return im; }
    static bool Throws(Action a) { try { a(); return false; } catch (ArgumentException) { return true; } }

    static int Main()
    {
        // ---- Stats (ImageJ: СКО с n-1)
        var a = new ImageData(2, 2, PixelType.Gray8); a.Planes[0][0]=10; a.Planes[0][1]=20; a.Planes[0][2]=30; a.Planes[0][3]=40;
        var st = Stats.Compute(a, Rectangle.Empty);
        Near(st.Mean, 25, 1e-9, "mean"); Near(st.StdDev, Math.Sqrt(500.0/3), 1e-9, "std n-1");

        // ---- Lab 4.3.2: white 512x512 - 70 = 185, + noise 20
        var img = ImageOps.NewImage(512, 512, PixelType.Gray8, NewFill.White);
        img = ImageOps.ApplyMath(img, MathOp.Subtract, 70);
        var s0 = Stats.Compute(img, Rectangle.Empty);
        Near(s0.Mean, 185, 0, "185"); Near(s0.StdDev, 0, 0, "ideal std 0");
        var noisy = Noise.AddGaussian(img, 20, 1);
        var s1 = Stats.Compute(noisy, Rectangle.Empty);
        Near(s1.Mean, 185, 0.2, "noisy mean"); Near(s1.StdDev, 20, 0.3, "noisy std");
        Console.WriteLine($"noise σ=20: M={s1.Mean:0.00} σ={s1.StdDev:0.00} mode={s1.Mode}");

        // ---- clipping semantics
        var c1 = ImageOps.Calculate(img, img, CalcOp.Add, false); Near(c1.Planes[0][0], 255, 0, "8bit add clamps");
        var c2 = ImageOps.Calculate(img, img, CalcOp.Add, true); Near(c2.Planes[0][0], 370, 0, "float add");
        Check(c2.Type == PixelType.Gray32, "float result type");
        var sub = ImageOps.Calculate(ImageOps.ApplyMath(img, MathOp.Set, 10), img, CalcOp.Subtract, false); Near(sub.Planes[0][5], 0, 0, "8bit sub clamps 0");

        // ---- convolution vs brute force (edge replicate, correlation)
        var r8 = Rand8(37, 23, 5);
        float[] k = { 1, 2, 3, 0, -1, 4, 2, 2, -5, 1, 0, 1, 3, 1, 1 }; // 3 rows x 5 cols
        var cv = Filters.Convolve(ImageOps.ConvertTo(r8, PixelType.Gray32), k, 5, 3, false);
        double maxd = 0;
        for (int y = 0; y < 23; y++) for (int x = 0; x < 37; x++) {
            double s = 0; for (int v = 0; v < 3; v++) for (int u = 0; u < 5; u++) {
                int xx = Math.Clamp(x + u - 2, 0, 36), yy = Math.Clamp(y + v - 1, 0, 22); s += k[v*5+u] * r8.Planes[0][yy*37+xx]; }
            maxd = Math.Max(maxd, Math.Abs(s - cv.Planes[0][y*37+x])); }
        Near(maxd, 0, 1e-3, "convolve brute force");
        var avg = Filters.Convolve(r8, new float[] { 1, 1, 1, 1, 1, 1, 1, 1, 1 }, 3, 3, true);
        double mx2 = 0; for (int y = 1; y < 22; y++) for (int x = 1; x < 36; x++) { double s=0; for(int v=-1;v<=1;v++) for(int u=-1;u<=1;u++) s+=r8.Planes[0][(y+v)*37+x+u]; mx2=Math.Max(mx2, Math.Abs(Math.Floor(s/9+0.5)-avg.Planes[0][y*37+x])); }
        Near(mx2, 0, 0, "moving average 8-bit rounding");

        // ---- rank filters
        Check(Filters.CircleArea(1) == 9, "r=1 area 9 (got " + Filters.CircleArea(1) + ")");
        Check(Filters.CircleArea(2) == 21, "r=2 area 21 (got " + Filters.CircleArea(2) + ")");
        Console.WriteLine($"circle areas r=2,4,6: {Filters.CircleArea(2)}, {Filters.CircleArea(4)}, {Filters.CircleArea(6)}");
        var big = Rand8(120, 90, 7);
        foreach (RankType rt in new[] { RankType.Median, RankType.Minimum, RankType.Maximum, RankType.Mean })
            foreach (double rad in new[] { 2.0, 4.0, 6.0 }) {
                var fb = Filters.Rank(big, rt, rad);
                var ff = Filters.Rank(ImageOps.ConvertTo(big, PixelType.Gray32), rt, rad);
                double d = 0; for (int i = 0; i < fb.PixelCount; i++) d = Math.Max(d, Math.Abs(Math.Floor(ff.Planes[0][i] + 0.5) - fb.Planes[0][i]));
                Near(d, 0, rt == RankType.Mean ? 1 : 0, $"rank byte==float {rt} r={rad}");
            }
        // naive median r=2 check at interior pixel
        { int kr; var hw = Filters.CircleHalfWidths(2, out kr); var med = Filters.Rank(big, RankType.Median, 2);
          var list = new System.Collections.Generic.List<float>(); int X=50, Y=40;
          for (int j=-kr;j<=kr;j++) for (int dx=-hw[j+kr]; dx<=hw[j+kr]; dx++) list.Add(big.Planes[0][(Y+j)*120+X+dx]);
          list.Sort(); Near(med.Planes[0][Y*120+X], list[list.Count/2], 0, "median naive"); }

        // прямоугольная апертура: перебор с повторением краёв; Mean w×h == свёртка со «скользящим средним» w×h
        foreach (var (aw, ah) in new[] { (3, 5), (5, 3), (7, 7), (1, 3) })
        {
            foreach (RankType rt in new[] { RankType.Median, RankType.Minimum, RankType.Maximum, RankType.Mean, RankType.Variance })
            {
                var rf = Filters.Rank(ImageOps.ConvertTo(big, PixelType.Gray32), rt, aw, ah);
                double md = 0;
                for (int y = 0; y < 90; y += 7) for (int x = 0; x < 120; x += 5)
                {
                    var win = new System.Collections.Generic.List<float>();
                    for (int v = -ah / 2; v <= ah / 2; v++) for (int u = -aw / 2; u <= aw / 2; u++)
                        win.Add(big.Planes[0][Math.Clamp(y + v, 0, 89) * 120 + Math.Clamp(x + u, 0, 119)]);
                    win.Sort();
                    double mean = 0, m2 = 0; foreach (float w in win) { mean += w; m2 += (double)w * w; }
                    mean /= win.Count;
                    double expect = rt == RankType.Median ? win[win.Count / 2] : rt == RankType.Minimum ? win[0] : rt == RankType.Maximum ? win[^1]
                                  : rt == RankType.Mean ? mean : m2 / win.Count - mean * mean;
                    md = Math.Max(md, Math.Abs(expect - rf.Planes[0][y * 120 + x]));
                }
                Near(md, 0, rt == RankType.Variance ? 0.05 : 1e-3, $"rank rect {rt} {aw}x{ah} brute force");
                if (rt != RankType.Mean && rt != RankType.Variance)
                {
                    var rb = Filters.Rank(big, rt, aw, ah);
                    double d = 0; for (int i = 0; i < rb.PixelCount; i++) d = Math.Max(d, Math.Abs(rb.Planes[0][i] - rf.Planes[0][i]));
                    Near(d, 0, 0, $"rank rect byte==float {rt} {aw}x{ah}");
                }
            }
            var mean8 = Filters.Rank(big, RankType.Mean, aw, ah);
            var conv8 = Filters.Convolve(big, Masks.Make(MaskKind.MovingAverage, aw, ah), aw, ah, true);
            double dm = 0; for (int i = 0; i < mean8.PixelCount; i++) dm = Math.Max(dm, Math.Abs(mean8.Planes[0][i] - conv8.Planes[0][i]));
            Near(dm, 0, 0, $"mean {aw}x{ah} == moving average mask");
        }
        Check(Throws(() => Filters.Rank(big, RankType.Median, 4, 3)), "rank rect rejects even width");

        // ---- маски методички (рис. 4.18) и их увеличение
        void Mask(MaskKind mk, int w, int h, string expect)
        {
            var m = Masks.Make(mk, w, h);
            int fw, fh; Masks.FixedSize(mk, out fw, out fh);
            int cols = fw > 0 ? fw : w, rows = fh > 0 ? fh : h;
            string got = Masks.ToText(m, rows, cols).Replace("\r\n", " / ");
            Check(got == expect, $"mask {mk} {w}x{h}: {got}");
            float[] pk2; int pr2, pc2; string e2;
            Check(Masks.TryParse(Masks.ToText(m, rows, cols), out pk2, out pr2, out pc2, out e2) && pr2 == rows && pc2 == cols, $"mask {mk} parses back");
        }
        Mask(MaskKind.MovingAverage, 3, 3, "1 1 1 / 1 1 1 / 1 1 1");
        Mask(MaskKind.MovingAverage, 3, 5, "1 1 1 / 1 1 1 / 1 1 1 / 1 1 1 / 1 1 1");
        Mask(MaskKind.SmallDetails, 3, 3, "1 2 1 / 2 4 2 / 1 2 1");
        Mask(MaskKind.SmallDetails, 5, 3, "1 4 6 4 1 / 2 8 12 8 2 / 1 4 6 4 1");
        Mask(MaskKind.SmallDetails, 3, 7, "1 2 1 / 6 12 6 / 15 30 15 / 20 40 20 / 15 30 15 / 6 12 6 / 1 2 1");
        Check(Masks.Sum(Masks.Make(MaskKind.SmallDetails, 7, 7)) == 4096, "small details 7x7 sum 64²");
        Check(Masks.ToText(Masks.Make(MaskKind.SmallDetails, 5, 3, WeightProfile.Triangular), 3, 5) == "1 2 3 2 1\r\n2 4 6 4 2\r\n1 2 3 2 1", "small details triangular 3x5 (rows x columns)");
        Check(string.Join(" ", Masks.Profile(WeightProfile.Triangular, 7)) == "1 2 3 4 3 2 1", "triangular profile 7");
        Check(string.Join(" ", Masks.Profile(WeightProfile.Binomial, 1)) == "1" && string.Join(" ", Masks.Profile(WeightProfile.Triangular, 1)) == "1", "profiles of 1");
        { double[] hz, vt; string pe;
          Check(Masks.TryParseProfiles("1 3 5 3 1 ** 1 2 1", out hz, out vt, out pe) && hz.Length == 5 && vt.Length == 3 && hz[2] == 5,
                "own weights row ** column");
          Check(Masks.ToText(Masks.Separable(hz, vt), 3, 5) == "1 3 5 3 1\r\n2 6 10 6 2\r\n1 3 5 3 1", "own weights mask");
          Check(Masks.TryParseProfiles(" 1 2 1 ", out hz, out vt, out pe) && hz.Length == 3 && vt.Length == 3, "own weights same both ways");
          Check(Masks.TryParseProfiles("0,5 1 0,5", out hz, out vt, out pe) && hz[0] == 0.5, "own weights comma decimals");
          Check(!Masks.TryParseProfiles("1 2 ** 1 2 1", out hz, out vt, out pe), "own weights reject even");
          Check(!Masks.TryParseProfiles("1 2 1 **", out hz, out vt, out pe), "own weights reject empty column");
          Check(!Masks.TryParseProfiles("1 x 1", out hz, out vt, out pe), "own weights reject text");
          Check(!Masks.TryParseProfiles("1 ** 1 ** 1", out hz, out vt, out pe), "own weights reject two **"); }
        Mask(MaskKind.Laplacian8, 3, 3, "-1 -1 -1 / -1 8 -1 / -1 -1 -1");
        Check(Masks.ToText(Masks.Make(MaskKind.Laplacian8, 5, 5), 5, 5) == Masks.ImageJDefault, "laplacian 8 5x5 == ImageJ default");
        Mask(MaskKind.Laplacian4, 7, 7, "0 -1 0 / -1 4 -1 / 0 -1 0");
        Mask(MaskKind.DoubleDiff, 3, 3, "1 -2 1 / -2 4 -2 / 1 -2 1");
        Mask(MaskKind.VerticalLines, 9, 5, "-1 2 -1 / -1 2 -1 / -1 2 -1 / -1 2 -1 / -1 2 -1");
        Mask(MaskKind.HorizontalLines, 7, 9, "-1 -1 -1 -1 -1 -1 -1 / 2 2 2 2 2 2 2 / -1 -1 -1 -1 -1 -1 -1");
        Mask(MaskKind.DiagonalLeft, 3, 3, "2 -1 -1 / -1 2 -1 / -1 -1 2");
        Mask(MaskKind.DiagonalRight, 3, 3, "-1 -1 2 / -1 2 -1 / 2 -1 -1");
        Mask(MaskKind.East, 3, 3, "-1 1 1 / -1 -2 1 / -1 1 1");
        Mask(MaskKind.SouthEast, 3, 3, "-1 -1 1 / -1 -2 1 / 1 1 1");
        foreach (MaskKind mk in Enum.GetValues(typeof(MaskKind)))
            if (mk != MaskKind.MovingAverage && mk != MaskKind.SmallDetails)
                Check(Masks.Sum(Masks.Make(mk, 5, 7)) == 0, $"mask {mk} sum 0");
        Check(Throws(() => Masks.Make(MaskKind.MovingAverage, 4, 3)), "mask rejects even size");

        // ---- gaussian blur keeps mean of constant & reduces noise
        var gb = Filters.GaussianBlur(ImageOps.ConvertTo(noisy, PixelType.Gray32), 2);
        var sg = Stats.Compute(gb, new Rectangle(50, 50, 400, 400));
        Console.WriteLine($"gauss σ=2 noise 20 -> {sg.StdDev:0.00} (theory ≈ {20/(2*Math.Sqrt(Math.PI)*2):0.00})");
        Near(sg.StdDev, 20/(2*Math.Sqrt(Math.PI)*2), 0.4, "gauss noise reduction");

        // ---- разбор ядра свёртки
        float[] pk; int pr, pc; string err;
        Check(Masks.TryParse("-1 -1 -1\n-1 8 -1\n-1 -1 -1", out pk, out pr, out pc, out err) && pr==3 && pc==3 && pk[4]==8, "parse kernel");
        Check(!Masks.TryParse("1 2\n3 4", out pk, out pr, out pc, out err), "reject even kernel");
        Check(Masks.TryParse("0,5 1,5 0,5", out pk, out pr, out pc, out err) && Math.Abs(pk[1]-1.5f)<1e-6, "comma decimals");
        Check(Masks.TryParse(Masks.ImageJDefault, out pk, out pr, out pc, out err) && pr==5 && pc==5 && pk[12]==24, "imagej default kernel");
        Check(!Masks.TryParse("1 2 3\n4 5", out pk, out pr, out pc, out err), "reject ragged kernel");
        // CIELAB: нейтральный серый → a*=b*=0
        { double L, A, B; Stepchart.ToLab(0.5, 0.5, 0.5, out L, out A, out B); Near(A, 0, 0.01, "lab a gray"); Near(B, 0, 0.01, "lab b gray"); Near(L, 53.39, 0.1, "lab L gray");
          Stepchart.ToLab(1, 0, 0, out L, out A, out B); Near(L, 53.24, 0.1, "lab L red"); Near(A, 80.09, 0.2, "lab a red"); }

        // ---- quality
        var ref32 = ImageOps.ConvertTo(img, PixelType.Gray32);
        var n32 = Noise.AddGaussian(ref32, 10, 3);
        var q = Quality.Compare(ref32, n32, Rectangle.Empty, CompareChannel.Luma, 255);
        Near(q.Psnr, 20*Math.Log10(255/10.0), 0.1, "psnr σ=10");
        Near(q.SnrDb, 10*Math.Log10(185.0*185/100), 0.1, "snr σ=10");
        var qq = Quality.Compare(ref32, ref32, Rectangle.Empty, CompareChannel.Luma, 255);
        Check(double.IsPositiveInfinity(qq.Psnr) && Math.Abs(qq.Ssim-1)<1e-6, "identical");
        Console.WriteLine($"PSNR={q.Psnr:0.00} SNR={q.SnrDb:0.00} SSIM={q.Ssim:0.0000}");

        // ---- Stepchart on simulated capture
        var spec = new StepChartSpec { Steps = 20, DensityStep = 0.1, FirstDensity = 0, Width = 1000, Height = 200, Margin = 0 };
        var chart = TestCharts.StepChart(spec);
        Near(chart.Planes[0][5], 255, 0, "chart first step white");
        var shot = TestCharts.SimulateCamera(chart, 2.2, 0.95, 2.2, 3, 0.0, 0.8, true, 11);
        var set = new StepchartSettings { FirstDensity = 0, Zones = 20, Detection = ZoneDetection.Automatic };
        var res = Stepchart.Analyze(shot, new Rectangle(0, 0, 1000, 200), set);
        Console.WriteLine($"Stepchart: zones={res.Zones.Count} detected={res.DetectedZones} gamma={res.Gamma:0.000} (1/{1/res.Gamma:0.00}) range15={res.Range15:0.0} total={res.TotalRangeFStops:0.00} reversed={res.Reversed}");
        Near(res.Gamma, 1/2.2, 0.03, "stepchart gamma");
        Check(res.DetectedZones >= 15, "detected zones");
        for (int i = 0; i < res.Zones.Count; i += 3) { var z = res.Zones[i]; Console.WriteLine($"  zone {i+1}: D={z.Density:0.00} Y={z.Mean[3]:0.0} σY={z.Noise[3]:0.00} f-stop={z.FStopNoise[3]:0.000} SNRdB={z.SnrDb(3):0.0}"); }
        Console.WriteLine("  DR: " + string.Join(", ", res.DynamicRange) + "  avg zones " + res.AvgFrom + "-" + res.AvgTo + " noise% Y=" + res.AvgNoise[3].ToString("0.00"));
        Check(res.SpectrumFreq.Length > 3, "spectrum");
        // reversed chart + vertical
        var shotV = ImageOps.Rotate90(shot, true);
        var resV = Stepchart.Analyze(shotV, new Rectangle(0,0,shotV.Width, shotV.Height), set);
        Near(resV.Gamma, res.Gamma, 0.01, "vertical same gamma");
        var shotR = ImageOps.FlipHorizontal(shot);
        var resR = Stepchart.Analyze(shotR, new Rectangle(0,0,1000,200), set);
        Check(resR.Reversed, "reversed detected"); Near(resR.Gamma, res.Gamma, 0.01, "reversed same gamma");
        // auto number of zones
        var setAuto = new StepchartSettings { FirstDensity = 0, Zones = 0, Detection = ZoneDetection.Automatic };
        var resA = Stepchart.Analyze(TestCharts.SimulateCamera(chart, 2.2, 0.95, 2.2, 0.5, 0, 0.5, false, 3), new Rectangle(0,0,1000,200), setAuto);
        Console.WriteLine("auto zones: " + resA.Zones.Count);

        // detrend check: plane gradient removed
        var grad = new double[20*10]; for (int y=0;y<10;y++) for (int x=0;x<20;x++) grad[y*20+x] = 3*x + 2*y + 0.5*x*x;
        var rr = Stepchart.Detrend(grad, 20, 10, DetrendMode.Quadratic); double mr=0; foreach(var v in rr) mr=Math.Max(mr,Math.Abs(v)); Near(mr,0,1e-6,"quadratic detrend");

        // frames average (lab 4.3.1) — float
        var frames = new System.Collections.Generic.List<ImageData>();
        for (int f = 0; f < 5; f++) frames.Add(Noise.AddGaussian(img, 5, 100+f));
        var sum = ImageOps.Sum(frames, true); var av = ImageOps.ApplyMath(sum, MathOp.Divide, 5);
        var diff = ImageOps.Calculate(frames[0], av, CalcOp.Subtract, true);
        var sd = Stats.Compute(diff, Rectangle.Empty);
        Console.WriteLine($"frames: avg mean={Stats.Compute(av, Rectangle.Empty).Mean:0.00} diff σ={sd.StdDev:0.00} (теор. {5*Math.Sqrt(1-1.0/5):0.00})");
        Near(sd.StdDev, 5*Math.Sqrt(0.8), 0.15, "temporal noise via averaging");

        // rotate/flip consistency
        var rot = ImageOps.Rotate90(ImageOps.Rotate90(ImageOps.Rotate90(ImageOps.Rotate90(r8, true), true), true), true);
        double rd=0; for(int i=0;i<r8.PixelCount;i++) rd=Math.Max(rd,Math.Abs(rot.Planes[0][i]-r8.Planes[0][i])); Near(rd,0,0,"rotate 4x");

        // TIFF: запись и чтение без потерь для всех типов
        foreach (PixelType pt in new[] { PixelType.Gray8, PixelType.Gray16, PixelType.Gray32, PixelType.Rgb24, PixelType.Rgb32 })
        {
            var src = new ImageData(31, 17, pt);
            var rng = new Random(9);
            for (int c = 0; c < src.Channels; c++)
                for (int i = 0; i < src.PixelCount; i++)
                    src.Planes[c][i] = pt == PixelType.Gray16 ? rng.Next(65536) : (ImageData.IsFloatType(pt) ? (float)(rng.NextDouble() * 1000 - 500) : rng.Next(256));
            var ms = new System.IO.MemoryStream();
            using (var bw = new System.IO.BinaryWriter(ms)) SigmaLab.IO.TiffIO.Write(src, bw);
            var back = SigmaLab.IO.TiffIO.TryRead(ms.ToArray());
            Check(back != null && back.Type == pt && back.Width == 31 && back.Height == 17, "tiff type " + pt);
            double md = 0;
            if (back != null) for (int c = 0; c < src.Channels; c++) for (int i = 0; i < src.PixelCount; i++) md = Math.Max(md, Math.Abs(src.Planes[c][i] - back.Planes[c][i]));
            Near(md, 0, 0, "tiff roundtrip " + pt);
        }

        Console.WriteLine($"\n{passes} passed, {fails} failed");
        return fails == 0 ? 0 : 1;
    }
}
