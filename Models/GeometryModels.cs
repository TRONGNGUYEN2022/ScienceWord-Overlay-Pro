using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ScienceWordOverlay.Models
{
    public enum MathRenderMode { Area2D = 0, Solid3D = 1 }
    public enum SpinAxisMode { None = 0, Vertical = 1, Horizontal = 2 }

    public class GeoPoint { public string Id { get; set; } = ""; public double X { get; set; } public double Y { get; set; } public string Label { get; set; } = ""; public double Lx { get; set; } = 10; public double Ly { get; set; } = -18; }
    public class GeoShape { public string Id { get; set; } = ""; public string Type { get; set; } = "segment"; public string P1Id { get; set; } = ""; public string P2Id { get; set; } = ""; public string ColorHex { get; set; } = "#0F172A"; public double StrokeWidth { get; set; } = 2.4; public string DashStyle { get; set; } = "solid"; }
    public class GeoRegion { public string Id { get; set; } = ""; public string Type { get; set; } = "polygon"; public List<string> PointIds { get; set; } = new(); public string ColorHex { get; set; } = "#3B82F6"; public double Opacity { get; set; } = 0.30; public string Pattern { get; set; } = "solid"; }
    public class GeoMarker { public string Id { get; set; } = ""; public string Type { get; set; } = "right_angle"; public string P1Id { get; set; } = ""; public string VertexId { get; set; } = ""; public string P2Id { get; set; } = ""; public string ColorHex { get; set; } = "#0F172A"; }
    public class FreeStroke { public string Id { get; set; } = ""; public string GroupId { get; set; } = ""; public List<double[]> Points { get; set; } = new(); public string ColorHex { get; set; } = "#0F172A"; public double Width { get; set; } = 2.5; public double Opacity { get; set; } = 1.0; public bool IsHighlighter { get; set; } = false; public string PenStyle { get; set; } = "solid"; public List<double> PointWidths { get; set; } = new(); }
    public class GeoText { public string Id { get; set; } = ""; public double X { get; set; } public double Y { get; set; } public string Text { get; set; } = ""; public string ColorHex { get; set; } = "#0F172A"; public double FontSize { get; set; } = 22; }
    public class GeoMathFormula { public string Id { get; set; } = ""; public double X { get; set; } public double Y { get; set; } public string Latex { get; set; } = ""; public string ColorHex { get; set; } = "#0F172A"; public double FontSize { get; set; } = 24; public bool ForceMath { get; set; } = true; public bool LayerBelow { get; set; } = false; }
    public class MagicFillLayer { public string Id { get; set; } = ""; public BitmapSource Bitmap { get; set; } = null!; public Rect BoundsDip { get; set; } }

    public class SceneState
    {
        public List<GeoPoint> Points { get; set; } = new();
        public List<GeoShape> Shapes { get; set; } = new();
        public List<GeoRegion> Regions { get; set; } = new();
        public List<GeoMarker> Markers { get; set; } = new();
        public List<FreeStroke> Strokes { get; set; } = new();
        public List<GeoText> Texts { get; set; } = new();
        public List<GeoMathFormula> MathFormulas { get; set; } = new();
        public int NextId { get; set; } = 1;
    }

    // Moi mien tich phan co cap can [StartT, EndT] DOC LAP -> keo can tao duoc vung trong khong to mau!
    public class RevRegion
    {
        public double StartT { get; set; }
        public double EndT { get; set; }
        public string ColorHex { get; set; } = "#38BDF8";
        public bool Enabled { get; set; } = true;
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

        public List<RevRegion> Regions { get; set; } = new();
        public int ActiveRegionIndex { get; set; } = 0;

        public bool IsValid => Curve1TR.Count >= 2 && (TMax - TMin) > 2.0;

        public void EnsureRegionsInitialized()
        {
            if (Regions.Count == 0)
            {
                double a = Math.Min(LimitA, LimitB), b = Math.Max(LimitA, LimitB);
                if (b - a < 1.0) { a = TMin; b = TMax; }
                Regions.Add(new RevRegion { StartT = a, EndT = b, ColorHex = DefaultRegionPalette[0], Enabled = true });
            }
            Regions.Sort((x, y) => x.StartT.CompareTo(y.StartT));
            ActiveRegionIndex = Math.Clamp(ActiveRegionIndex, 0, Regions.Count - 1);
            SyncOuterLimits();
        }

        private void SyncOuterLimits()
        {
            if (Regions.Count == 0) return;
            LimitA = Regions.Min(r => r.StartT);
            LimitB = Regions.Max(r => r.EndT);
        }

        public static string GetHandleLabel(int regionIdx, bool isEnd)
        {
            const string letters = "abcdefghkmnpqrstuv";
            int idx = regionIdx * 2 + (isEnd ? 1 : 0);
            return idx >= 0 && idx < letters.Length ? letters[idx].ToString() : $"x{idx + 1}";
        }

        // Click tren hinh de them can/mien:
        // - Neu click vao VUNG TRONG (chua to) -> tao 1 mien to DOC LAP moi ngay tai khoang trong do!
        // - Neu click vao BEN TRONG 1 mien dang to -> tach thanh 2 mien doc lap co khe ho nho (12px) de de keo tao vung trong hoac ghep khit!
        public int AddOrSplitAt(double? clickT = null)
        {
            EnsureRegionsInitialized();
            if (Regions.Count >= 8) return -1;

            double t;
            if (!clickT.HasValue)
            {
                // Tim khoang trong rong nhat truoc; neu khong co khoang trong lon thi tach doi mien dang chon
                var bestGap = FindLargestEmptyGap();
                if (bestGap.HasValue && (bestGap.Value.end - bestGap.Value.start) >= 24.0)
                {
                    double gs = bestGap.Value.start, ge = bestGap.Value.end;
                    double pad = Math.Min(10.0, (ge - gs) * 0.12);
                    Regions.Add(new RevRegion
                    {
                        StartT = gs + pad,
                        EndT = ge - pad,
                        ColorHex = DefaultRegionPalette[Regions.Count % DefaultRegionPalette.Length],
                        Enabled = true
                    });
                    Regions.Sort((x, y) => x.StartT.CompareTo(y.StartT));
                    ActiveRegionIndex = Regions.FindIndex(r => Math.Abs(r.StartT - (gs + pad)) < 0.1);
                    SyncOuterLimits();
                    return ActiveRegionIndex;
                }
                var cur = Regions[Math.Clamp(ActiveRegionIndex, 0, Regions.Count - 1)];
                t = 0.5 * (cur.StartT + cur.EndT);
            }
            else
            {
                t = Math.Clamp(clickT.Value, TMin + 2.0, TMax - 2.0);
            }

            // 1. Kiem tra co nam trong mien hien co khong -> tach thanh 2 mien doc lap
            for (int i = 0; i < Regions.Count; i++)
            {
                var r = Regions[i];
                if (t > r.StartT + 8.0 && t < r.EndT - 8.0)
                {
                    double gapHalf = Math.Min(7.0, Math.Min(t - r.StartT, r.EndT - t) * 0.28);
                    double origEnd = r.EndT;
                    r.EndT = t - gapHalf;
                    var nr = new RevRegion
                    {
                        StartT = t + gapHalf,
                        EndT = origEnd,
                        ColorHex = DefaultRegionPalette[Regions.Count % DefaultRegionPalette.Length],
                        Enabled = true
                    };
                    Regions.Insert(i + 1, nr);
                    ActiveRegionIndex = i + 1;
                    SyncOuterLimits();
                    return i + 1;
                }
            }

            // 2. Nam trong vung TRONG (khong to mau) -> tao mien moi doc lap trong khoang trong bao quanh t!
            double gapL = TMin, gapR = TMax;
            foreach (var r in Regions)
            {
                if (r.EndT <= t && r.EndT > gapL) gapL = r.EndT;
                if (r.StartT >= t && r.StartT < gapR) gapR = r.StartT;
            }
            if (gapR - gapL < 10.0) return -1;

            double halfW = Math.Min(Math.Max(22.0, (TMax - TMin) * 0.14), (gapR - gapL) * 0.45);
            double ns = Math.Max(gapL + 4.0, t - halfW);
            double ne = Math.Min(gapR - 4.0, t + halfW);
            if (ne - ns < 6.0) { ns = gapL + 2.0; ne = gapR - 2.0; }
            if (ne - ns < 4.0) return -1;

            var created = new RevRegion
            {
                StartT = ns,
                EndT = ne,
                ColorHex = DefaultRegionPalette[Regions.Count % DefaultRegionPalette.Length],
                Enabled = true
            };
            Regions.Add(created);
            Regions.Sort((x, y) => x.StartT.CompareTo(y.StartT));
            ActiveRegionIndex = Regions.IndexOf(created);
            SyncOuterLimits();
            return ActiveRegionIndex;
        }

        private (double start, double end)? FindLargestEmptyGap()
        {
            double bestS = 0, bestE = 0, bestW = 0;
            double prev = TMin;
            foreach (var r in Regions)
            {
                if (r.StartT - prev > bestW) { bestW = r.StartT - prev; bestS = prev; bestE = r.StartT; }
                prev = Math.Max(prev, r.EndT);
            }
            if (TMax - prev > bestW) { bestW = TMax - prev; bestS = prev; bestE = TMax; }
            return bestW > 8.0 ? (bestS, bestE) : null;
        }

        public bool RemoveActiveRegion()
        {
            EnsureRegionsInitialized();
            if (Regions.Count <= 1) return false;
            Regions.RemoveAt(Math.Clamp(ActiveRegionIndex, 0, Regions.Count - 1));
            ActiveRegionIndex = Math.Clamp(ActiveRegionIndex, 0, Regions.Count - 1);
            SyncOuterLimits();
            return true;
        }

        // Di chuyen can StartT hoac EndT cua mien regionIdx:
        // - Tu do thu hep/mo rong de tao vung TRONG khong to mau!
        // - Khong de chong len mien ben canh; tu hut khit khi sat trong 5px
        public void MoveHandle(int regionIdx, bool isEnd, double newT)
        {
            EnsureRegionsInitialized();
            if (regionIdx < 0 || regionIdx >= Regions.Count) return;
            var r = Regions[regionIdx];

            if (!isEnd)
            {
                double minT = regionIdx > 0 ? Regions[regionIdx - 1].EndT : TMin;
                double maxT = r.EndT - 4.0;
                if (minT > maxT) return;
                double clamped = Math.Clamp(newT, minT, maxT);
                if (regionIdx > 0 && Math.Abs(clamped - minT) < 5.0) clamped = minT; // Hut khit neu keo sat
                r.StartT = clamped;
            }
            else
            {
                double minT = r.StartT + 4.0;
                double maxT = regionIdx + 1 < Regions.Count ? Regions[regionIdx + 1].StartT : TMax;
                if (minT > maxT) return;
                double clamped = Math.Clamp(newT, minT, maxT);
                if (regionIdx + 1 < Regions.Count && Math.Abs(clamped - maxT) < 5.0) clamped = maxT;
                r.EndT = clamped;
            }
            SyncOuterLimits();
        }

        public void SetActiveRegionColor(string hex)
        {
            EnsureRegionsInitialized();
            if (ActiveRegionIndex >= 0 && ActiveRegionIndex < Regions.Count)
                Regions[ActiveRegionIndex].ColorHex = hex;
        }

        public double TurntableYawDeg { get; set; } = 0.0;
        public double TurntablePitchDeg { get; set; } = 0.0;
        public bool HasTurntableRotation => Math.Abs(TurntableYawDeg % 360.0) > 0.05 || Math.Abs(TurntablePitchDeg) > 0.05;

        public Point GetPivotScreenCenter()
        {
            double tMid = 0.5 * (LimitA + LimitB);
            if (Math.Abs(LimitB - LimitA) < 1.0) tMid = 0.5 * (TMin + TMax);
            return ToScreen2D(tMid, 0.0);
        }

        public Point ToScreen2D(double t, double r) => new(AxisOrigin.X + t * AxisU.X + r * AxisV.X, AxisOrigin.Y + t * AxisU.Y + r * AxisV.Y);

        private (double sx, double sy, double sz) EvalRotated3D(double t, double r, double thetaRad, double xCompression)
        {
            double cosT = Math.Cos(thetaRad), sinT = Math.Sin(thetaRad);
            if (!HasTurntableRotation)
            {
                double obliqueU = xCompression * r * sinT * 0.90;
                double obliqueV = r * (cosT - xCompression * sinT * 0.18);
                double px = AxisOrigin.X + (t + obliqueU) * AxisU.X + obliqueV * AxisV.X;
                double py = AxisOrigin.Y + (t + obliqueU) * AxisU.Y + obliqueV * AxisV.Y;
                double depth = r * sinT - (r * cosT) * (0.20 * AxisV.Y) + (t - TMin) * 0.0006;
                return (px, py, depth);
            }

            // Quay khoi tron xoay quanh TRUC THANG DUNG di qua tam khoi (giong clip 3D)
            Point pivot = GetPivotScreenCenter();
            double tMid = 0.5 * (LimitA + LimitB);
            if (Math.Abs(LimitB - LimitA) < 1.0) tMid = 0.5 * (TMin + TMax);
            double dt = t - tMid;
            double x0 = dt * AxisU.X + (r * cosT) * AxisV.X;
            double y0 = dt * AxisU.Y + (r * cosT) * AxisV.Y; // Y man hinh huong xuong
            double z0 = r * sinT;

            // 1. Quay quanh truc thang dung (truc Y man hinh) mot goc TurntableYawDeg
            double ry = TurntableYawDeg * Math.PI / 180.0;
            double cosY = Math.Cos(ry), sinY = Math.Sin(ry);
            double x1 = x0 * cosY + z0 * sinY;
            double y1 = y0;
            double z1 = -x0 * sinY + z0 * cosY;

            // 2. Quay quanh truc nam ngang (Pitch) — them do nghieng nhe 8 deg chi khi quay truc dung thuan tuy
            double tiltOffset = Math.Abs(TurntablePitchDeg) < 0.05 ? 8.0 * Math.Min(1.0, Math.Abs(TurntableYawDeg) / 15.0) : 0.0;
            double rx = (TurntablePitchDeg + tiltOffset) * Math.PI / 180.0;
            double cosX = Math.Cos(rx), sinX = Math.Sin(rx);
            double y2 = y1 * cosX - z1 * sinX;
            double z2 = y1 * sinX + z1 * cosX;

            double persp = 1.0 / Math.Max(0.45, 1.0 - z2 * 0.00055);
            return (pivot.X + x1 * persp, pivot.Y + y2 * persp, z2);
        }

        public Point Project3D(double t, double r, double thetaRad, double xCompression)
        {
            var p = EvalRotated3D(t, r, thetaRad, xCompression);
            return new Point(p.sx, p.sy);
        }

        public double ComputeDepth(double t, double r, double thetaRad, double xCompression = 0.28)
        {
            return EvalRotated3D(t, r, thetaRad, xCompression).sz;
        }

        public (double nx, double ny, double nz) RotateNormal3D(double nx, double ny, double nz)
        {
            if (!HasTurntableRotation) return (nx, ny, nz);
            double ry = TurntableYawDeg * Math.PI / 180.0;
            double cosY = Math.Cos(ry), sinY = Math.Sin(ry);
            double x1 = nx * cosY + nz * sinY;
            double y1 = ny;
            double z1 = -nx * sinY + nz * cosY;
            double tiltOffset = Math.Abs(TurntablePitchDeg) < 0.05 ? 8.0 * Math.Min(1.0, Math.Abs(TurntableYawDeg) / 15.0) : 0.0;
            double rx = (TurntablePitchDeg + tiltOffset) * Math.PI / 180.0;
            double cosX = Math.Cos(rx), sinX = Math.Sin(rx);
            return (x1, y1 * cosX - z1 * sinX, y1 * sinX + z1 * cosX);
        }

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
            EnsureRegionsInitialized();
            var list = new List<(int, double, double, double, double, double)>();
            for (int i = 0; i < Regions.Count; i++)
            {
                if (!Regions[i].Enabled) continue;
                var m = ComputeIntervalMetrics(Regions[i].StartT, Regions[i].EndT);
                list.Add((i, Regions[i].StartT, Regions[i].EndT, m.areaCm2, m.volCm3, m.volOverPiCm3));
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
            Regions.Clear(); ActiveRegionIndex = 0;
            EnsureRegionsInitialized();
            return true;
        }
    }
}