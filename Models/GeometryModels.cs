using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ScienceWordOverlay.Models
{
    public enum MathRenderMode { Area2D = 0, Solid3D = 1 }

    public class GeoPoint { public string Id { get; set; } = ""; public double X { get; set; } public double Y { get; set; } public string Label { get; set; } = ""; public double Lx { get; set; } = 10; public double Ly { get; set; } = -18; }
    public class GeoShape { public string Id { get; set; } = ""; public string Type { get; set; } = "segment"; public string P1Id { get; set; } = ""; public string P2Id { get; set; } = ""; public string ColorHex { get; set; } = "#0F172A"; public double StrokeWidth { get; set; } = 2.4; public string DashStyle { get; set; } = "solid"; }
    public class GeoRegion { public string Id { get; set; } = ""; public string Type { get; set; } = "polygon"; public List<string> PointIds { get; set; } = new(); public string ColorHex { get; set; } = "#3B82F6"; public double Opacity { get; set; } = 0.30; public string Pattern { get; set; } = "solid"; }
    public class GeoMarker { public string Id { get; set; } = ""; public string Type { get; set; } = "right_angle"; public string P1Id { get; set; } = ""; public string VertexId { get; set; } = ""; public string P2Id { get; set; } = ""; public string ColorHex { get; set; } = "#0F172A"; }
    public class FreeStroke { public string Id { get; set; } = ""; public List<double[]> Points { get; set; } = new(); public string ColorHex { get; set; } = "#0F172A"; public double Width { get; set; } = 2.5; public double Opacity { get; set; } = 1.0; public bool IsHighlighter { get; set; } = false; public string PenStyle { get; set; } = "solid"; public List<double> PointWidths { get; set; } = new(); }
    public class GeoText { public string Id { get; set; } = ""; public double X { get; set; } public double Y { get; set; } public string Text { get; set; } = ""; public string ColorHex { get; set; } = "#0F172A"; public double FontSize { get; set; } = 22; }
    public class MagicFillLayer { public string Id { get; set; } = ""; public BitmapSource Bitmap { get; set; } = null!; public Rect BoundsDip { get; set; } }

    public class SceneState
    {
        public List<GeoPoint> Points { get; set; } = new();
        public List<GeoShape> Shapes { get; set; } = new();
        public List<GeoRegion> Regions { get; set; } = new();
        public List<GeoMarker> Markers { get; set; } = new();
        public List<FreeStroke> Strokes { get; set; } = new();
        public List<GeoText> Texts { get; set; } = new();
        public int NextId { get; set; } = 1;
    }

    public class RevolutionScene
    {
        public static readonly string[] DefaultRegionPalette = { "#38BDF8", "#FACC15", "#FB923C", "#34D399", "#A78BFA", "#F472B6" };

        public Point AxisOrigin { get; set; }
        public Vector AxisU { get; set; }
        public Vector AxisV { get; set; }
        public bool HasTwoCurves { get; set; }
        public List<Point> Curve1TR { get; set; } = new();
        public List<Point> Curve2TR { get; set; } = new();
        public double TMin { get; set; }
        public double TMax { get; set; }
        public double LimitA { get; set; }
        public double LimitB { get; set; }
        public double DipPerCm { get; set; } = 37.795;

        public List<double> SplitBounds { get; set; } = new();
        public List<string> RegionColors { get; set; } = new();
        public List<bool> RegionEnabled { get; set; } = new();
        public int ActiveRegionIndex { get; set; } = 0;

        public bool IsValid => Curve1TR.Count >= 2 && (TMax - TMin) > 2.0;

        public void EnsureBoundsInitialized()
        {
            if (SplitBounds.Count < 2)
            {
                double a = Math.Min(LimitA, LimitB), b = Math.Max(LimitA, LimitB);
                if (b - a < 1.0) { a = TMin; b = TMax; }
                SplitBounds = new List<double> { a, b };
            }
            int regionCount = SplitBounds.Count - 1;
            while (RegionColors.Count < regionCount) RegionColors.Add(DefaultRegionPalette[RegionColors.Count % DefaultRegionPalette.Length]);
            while (RegionColors.Count > regionCount) RegionColors.RemoveAt(RegionColors.Count - 1);
            while (RegionEnabled.Count < regionCount) RegionEnabled.Add(true);
            while (RegionEnabled.Count > regionCount) RegionEnabled.RemoveAt(RegionEnabled.Count - 1);
            ActiveRegionIndex = Math.Clamp(ActiveRegionIndex, 0, Math.Max(0, regionCount - 1));
            LimitA = SplitBounds[0]; LimitB = SplitBounds[^1];
        }

        public static string GetBoundLabel(int index)
        {
            const string letters = "abcdefghkmnpq";
            return index >= 0 && index < letters.Length ? letters[index].ToString() : $"x{index + 1}";
        }

        public int AddSplitBound(double? customT = null)
        {
            EnsureBoundsInitialized();
            if (SplitBounds.Count >= 10) return -1;
            int insertIdx = -1; double newT;

            if (customT.HasValue)
            {
                newT = Math.Clamp(customT.Value, TMin + 2.0, TMax - 2.0);
                foreach (double b in SplitBounds) if (Math.Abs(b - newT) < 5.0) return -1;
                if (newT < SplitBounds[0]) { SplitBounds.Insert(0, newT); RegionColors.Insert(0, DefaultRegionPalette[RegionColors.Count % DefaultRegionPalette.Length]); RegionEnabled.Insert(0, true); ActiveRegionIndex = 0; LimitA = SplitBounds[0]; LimitB = SplitBounds[^1]; return 0; }
                if (newT > SplitBounds[^1]) { SplitBounds.Add(newT); RegionColors.Add(DefaultRegionPalette[RegionColors.Count % DefaultRegionPalette.Length]); RegionEnabled.Add(true); ActiveRegionIndex = RegionColors.Count - 1; LimitA = SplitBounds[0]; LimitB = SplitBounds[^1]; return SplitBounds.Count - 1; }
                for (int i = 0; i < SplitBounds.Count - 1; i++) if (newT > SplitBounds[i] && newT < SplitBounds[i + 1]) { insertIdx = i + 1; break; }
            }
            else
            {
                int targetSeg = Math.Clamp(ActiveRegionIndex, 0, SplitBounds.Count - 2);
                if (SplitBounds[targetSeg + 1] - SplitBounds[targetSeg] < 12.0)
                {
                    double maxW = -1;
                    for (int i = 0; i < SplitBounds.Count - 1; i++) { double w = SplitBounds[i + 1] - SplitBounds[i]; if (w > maxW) { maxW = w; targetSeg = i; } }
                }
                if (SplitBounds[targetSeg + 1] - SplitBounds[targetSeg] < 8.0) return -1;
                newT = 0.5 * (SplitBounds[targetSeg] + SplitBounds[targetSeg + 1]);
                insertIdx = targetSeg + 1;
            }
            if (insertIdx < 0) return -1;
            SplitBounds.Insert(insertIdx, newT);
            RegionColors.Insert(insertIdx, DefaultRegionPalette[RegionColors.Count % DefaultRegionPalette.Length]);
            RegionEnabled.Insert(insertIdx, true);
            ActiveRegionIndex = insertIdx; LimitA = SplitBounds[0]; LimitB = SplitBounds[^1];
            return insertIdx;
        }

        public bool RemoveSplitBound()
        {
            EnsureBoundsInitialized();
            if (SplitBounds.Count <= 2) return false;
            int removeIdx = Math.Clamp(ActiveRegionIndex, 1, SplitBounds.Count - 2);
            SplitBounds.RemoveAt(removeIdx);
            int removeRegion = Math.Min(removeIdx, RegionColors.Count - 1);
            RegionColors.RemoveAt(removeRegion); RegionEnabled.RemoveAt(removeRegion);
            ActiveRegionIndex = Math.Clamp(ActiveRegionIndex, 0, RegionColors.Count - 1);
            LimitA = SplitBounds[0]; LimitB = SplitBounds[^1];
            return true;
        }

        public void SetBoundAt(int handleIdx, double newT)
        {
            EnsureBoundsInitialized();
            if (handleIdx < 0 || handleIdx >= SplitBounds.Count) return;
            double minAllowed = handleIdx == 0 ? TMin : SplitBounds[handleIdx - 1] + 3.0;
            double maxAllowed = handleIdx == SplitBounds.Count - 1 ? TMax : SplitBounds[handleIdx + 1] - 3.0;
            if (minAllowed > maxAllowed) return;
            SplitBounds[handleIdx] = Math.Clamp(newT, minAllowed, maxAllowed);
            LimitA = SplitBounds[0]; LimitB = SplitBounds[^1];
        }

        public void SetActiveRegionColor(string hex)
        {
            EnsureBoundsInitialized();
            if (ActiveRegionIndex >= 0 && ActiveRegionIndex < RegionColors.Count) RegionColors[ActiveRegionIndex] = hex;
        }

        public Point ToScreen2D(double t, double r) => new(AxisOrigin.X + t * AxisU.X + r * AxisV.X, AxisOrigin.Y + t * AxisU.Y + r * AxisV.Y);

        public Point Project3D(double t, double r, double thetaRad, double xCompression)
        {
            double cosT = Math.Cos(thetaRad), sinT = Math.Sin(thetaRad);
            double obliqueU = xCompression * r * sinT * 0.90, obliqueV = r * (cosT - xCompression * sinT * 0.18);
            return new Point(AxisOrigin.X + (t + obliqueU) * AxisU.X + obliqueV * AxisV.X, AxisOrigin.Y + (t + obliqueU) * AxisU.Y + obliqueV * AxisV.Y);
        }

        public double ComputeDepth(double t, double r, double thetaRad) => r * Math.Sin(thetaRad) - (r * Math.Cos(thetaRad)) * (0.20 * AxisV.Y) + (t - TMin) * 0.0006;

        public static double SampleR(List<Point> trPts, double t)
        {
            int n = trPts.Count;
            if (n == 0) return 0; if (n == 1) return trPts[0].Y;
            for (int i = 0; i < n - 1; i++)
            {
                double t0 = trPts[i].X, t1 = trPts[i + 1].X;
                if ((t >= t0 && t <= t1) || (t >= t1 && t <= t0))
                {
                    double denom = t1 - t0;
                    return Math.Abs(denom) < 1e-5 ? trPts[i].Y : trPts[i].Y + (trPts[i + 1].Y - trPts[i].Y) * ((t - t0) / denom);
                }
            }
            double bestD = double.MaxValue, bestR = trPts[0].Y;
            for (int i = 0; i < n; i++) { double d = Math.Abs(trPts[i].X - t); if (d < bestD) { bestD = d; bestR = trPts[i].Y; } }
            return bestR;
        }

        public (double areaCm2, double volCm3, double volOverPiCm3) ComputeIntervalMetrics(double a, double b)
        {
            if (!IsValid || b - a < 1.0) return (0, 0, 0);
            int steps = 160; double dt = (b - a) / steps, areaDip2 = 0, volOverPiDip3 = 0;
            for (int i = 0; i < steps; i++)
            {
                double t0 = a + i * dt, t1 = t0 + dt;
                double r1_0 = SampleR(Curve1TR, t0), r1_1 = SampleR(Curve1TR, t1);
                double r2_0 = HasTwoCurves ? SampleR(Curve2TR, t0) : 0.0, r2_1 = HasTwoCurves ? SampleR(Curve2TR, t1) : 0.0;
                areaDip2 += 0.5 * (Math.Abs(r1_0 - r2_0) + Math.Abs(r1_1 - r2_1)) * dt;
                volOverPiDip3 += 0.5 * (Math.Abs(r1_0 * r1_0 - r2_0 * r2_0) + Math.Abs(r1_1 * r1_1 - r2_1 * r2_1)) * dt;
            }
            double k = Math.Max(1.0, DipPerCm);
            double areaCm2 = areaDip2 / (k * k), volOverPiCm3 = volOverPiDip3 / (k * k * k);
            return (areaCm2, Math.PI * volOverPiCm3, volOverPiCm3);
        }

        public List<(int index, double a, double b, double areaCm2, double volCm3, double volOverPiCm3)> ComputeRegionMetrics()
        {
            EnsureBoundsInitialized();
            var list = new List<(int, double, double, double, double, double)>();
            for (int i = 0; i < SplitBounds.Count - 1; i++)
            {
                if (!RegionEnabled[i]) continue;
                var m = ComputeIntervalMetrics(SplitBounds[i], SplitBounds[i + 1]);
                list.Add((i, SplitBounds[i], SplitBounds[i + 1], m.areaCm2, m.volCm3, m.volOverPiCm3));
            }
            return list;
        }

        public (double areaCm2, double volCm3, double volOverPiCm3) ComputeMetrics()
        {
            if (!IsValid) return (0, 0, 0);
            double sumS = 0, sumV = 0, sumVPi = 0;
            foreach (var r in ComputeRegionMetrics()) { sumS += r.areaCm2; sumV += r.volCm3; sumVPi += r.volOverPiCm3; }
            return (sumS, sumV, sumVPi);
        }

        public bool SwapAxisAndCurve()
        {
            if (Curve1TR.Count < 2) return false;
            var screenPts = Curve1TR.Select(tr => ToScreen2D(tr.X, tr.Y)).ToList();
            Vector newU = AxisV; if (newU.Length < 1e-4) return false;
            newU.Normalize(); if (newU.Y < -0.5) newU = -newU;
            Vector newV = new(-newU.Y, newU.X); if (newV.Y > 0.05) newV = -newV;
            Point newOrigin = screenPts[0];
            var newTR = new List<Point>(screenPts.Count);
            double tMin = double.MaxValue, tMax = double.MinValue;
            foreach (var p in screenPts)
            {
                double dx = p.X - newOrigin.X, dy = p.Y - newOrigin.Y;
                double nt = dx * newU.X + dy * newU.Y, nr = dx * newV.X + dy * newV.Y;
                newTR.Add(new Point(nt, nr));
                if (nt < tMin) tMin = nt; if (nt > tMax) tMax = nt;
            }
            if (tMax - tMin <= 2.0) return false;
            AxisOrigin = newOrigin; AxisU = newU; AxisV = newV; Curve1TR = newTR;
            TMin = tMin; TMax = tMax; LimitA = tMin; LimitB = tMax;
            SplitBounds.Clear(); RegionColors.Clear(); RegionEnabled.Clear(); ActiveRegionIndex = 0;
            EnsureBoundsInitialized();
            return true;
        }
    }
}