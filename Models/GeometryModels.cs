using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ScienceWordOverlay.Models
{
    public enum MathRenderMode
    {
        Area2D = 0,
        Solid3D = 1
    }

    public class GeoPoint
    {
        public string Id { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public string Label { get; set; } = "";
        public double Lx { get; set; } = 10;
        public double Ly { get; set; } = -18;
    }

    public class GeoShape
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "segment";
        public string P1Id { get; set; } = "";
        public string P2Id { get; set; } = "";
        public string ColorHex { get; set; } = "#0F172A";
        public double StrokeWidth { get; set; } = 2.4;
        public string DashStyle { get; set; } = "solid";
    }

    public class GeoRegion
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "polygon";
        public List<string> PointIds { get; set; } = new();
        public string ColorHex { get; set; } = "#3B82F6";
        public double Opacity { get; set; } = 0.30;
        public string Pattern { get; set; } = "solid";
    }

    public class GeoMarker
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "right_angle";
        public string P1Id { get; set; } = "";
        public string VertexId { get; set; } = "";
        public string P2Id { get; set; } = "";
        public string ColorHex { get; set; } = "#0F172A";
    }

    public class FreeStroke
    {
        public string Id { get; set; } = "";
        public List<double[]> Points { get; set; } = new();
        public string ColorHex { get; set; } = "#0F172A";
        public double Width { get; set; } = 2.5;
        public double Opacity { get; set; } = 1.0;
        public bool IsHighlighter { get; set; } = false;
    }

    public class GeoText
    {
        public string Id { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public string Text { get; set; } = "";
        public string ColorHex { get; set; } = "#0F172A";
        public double FontSize { get; set; } = 22;
    }

    public class MagicFillLayer
    {
        public string Id { get; set; } = "";
        public BitmapSource Bitmap { get; set; } = null!;
        public Rect BoundsDip { get; set; }
    }

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
        public double DipPerCm { get; set; } = 37.795; // 96 DIP / 2.54 cm

        public bool IsValid => Curve1TR.Count >= 2 && (TMax - TMin) > 2.0;

        public Point ToScreen2D(double t, double r) =>
            new(AxisOrigin.X + t * AxisU.X + r * AxisV.X,
                AxisOrigin.Y + t * AxisU.Y + r * AxisV.Y);

        public Point Project3D(double t, double r, double thetaRad, double xCompression)
        {
            double cosT = Math.Cos(thetaRad);
            double sinT = Math.Sin(thetaRad);
            double obliqueU = xCompression * r * sinT * 0.90;
            double obliqueV = r * (cosT - xCompression * sinT * 0.18);
            return new Point(
                AxisOrigin.X + (t + obliqueU) * AxisU.X + obliqueV * AxisV.X,
                AxisOrigin.Y + (t + obliqueU) * AxisU.Y + obliqueV * AxisV.Y);
        }

        public double ComputeDepth(double t, double r, double thetaRad)
        {
            double cosT = Math.Cos(thetaRad);
            double sinT = Math.Sin(thetaRad);
            return r * sinT - (r * cosT) * (0.20 * AxisV.Y) + (t - TMin) * 0.0006;
        }

        public static double SampleR(List<Point> trPts, double t)
        {
            int n = trPts.Count;
            if (n == 0) return 0;
            if (n == 1) return trPts[0].Y;
            for (int i = 0; i < n - 1; i++)
            {
                double t0 = trPts[i].X, t1 = trPts[i + 1].X;
                if ((t >= t0 && t <= t1) || (t >= t1 && t <= t0))
                {
                    double denom = t1 - t0;
                    if (Math.Abs(denom) < 1e-5) return trPts[i].Y;
                    return trPts[i].Y + (trPts[i + 1].Y - trPts[i].Y) * ((t - t0) / denom);
                }
            }
            double bestD = double.MaxValue, bestR = trPts[0].Y;
            for (int i = 0; i < n; i++)
            {
                double d = Math.Abs(trPts[i].X - t);
                if (d < bestD) { bestD = d; bestR = trPts[i].Y; }
            }
            return bestR;
        }

        // Tính diện tích S (cm^2) và thể tích khối tròn xoay V = pi * int_a^b r^2 dt (cm^3)
        public (double areaCm2, double volCm3, double volOverPiCm3) ComputeMetrics()
        {
            if (!IsValid) return (0, 0, 0);
            double a = Math.Min(LimitA, LimitB);
            double b = Math.Max(LimitA, LimitB);
            if (b - a < 1.0) return (0, 0, 0);

            int steps = 200;
            double dt = (b - a) / steps;
            double areaDip2 = 0;
            double volOverPiDip3 = 0;

            for (int i = 0; i < steps; i++)
            {
                double t0 = a + i * dt;
                double t1 = t0 + dt;
                double r1_0 = SampleR(Curve1TR, t0);
                double r1_1 = SampleR(Curve1TR, t1);
                double r2_0 = HasTwoCurves ? SampleR(Curve2TR, t0) : 0.0;
                double r2_1 = HasTwoCurves ? SampleR(Curve2TR, t1) : 0.0;

                areaDip2 += 0.5 * (Math.Abs(r1_0 - r2_0) + Math.Abs(r1_1 - r2_1)) * dt;
                double sq0 = Math.Abs(r1_0 * r1_0 - r2_0 * r2_0);
                double sq1 = Math.Abs(r1_1 * r1_1 - r2_1 * r2_1);
                volOverPiDip3 += 0.5 * (sq0 + sq1) * dt;
            }

            double k = Math.Max(1.0, DipPerCm);
            double areaCm2 = areaDip2 / (k * k);
            double volOverPiCm3 = volOverPiDip3 / (k * k * k);
            return (areaCm2, Math.PI * volOverPiCm3, volOverPiCm3);
        }

        public bool SwapAxisAndCurve()
        {
            if (Curve1TR.Count < 2) return false;
            var screenPts = new List<Point>(Curve1TR.Count);
            foreach (var tr in Curve1TR) screenPts.Add(ToScreen2D(tr.X, tr.Y));

            Vector newU = AxisV;
            if (newU.Length < 1e-4) return false;
            newU.Normalize();
            if (newU.Y < -0.5) newU = -newU;
            Vector newV = new(-newU.Y, newU.X);
            if (newV.Y > 0.05) newV = -newV;

            Point newOrigin = screenPts[0];
            var newTR = new List<Point>(screenPts.Count);
            double tMin = double.MaxValue, tMax = double.MinValue;
            foreach (var p in screenPts)
            {
                double dx = p.X - newOrigin.X, dy = p.Y - newOrigin.Y;
                double nt = dx * newU.X + dy * newU.Y;
                double nr = dx * newV.X + dy * newV.Y;
                newTR.Add(new Point(nt, nr));
                if (nt < tMin) tMin = nt;
                if (nt > tMax) tMax = nt;
            }
            if (tMax - tMin <= 2.0) return false;

            AxisOrigin = newOrigin;
            AxisU = newU;
            AxisV = newV;
            Curve1TR = newTR;
            TMin = tMin; TMax = tMax;
            LimitA = tMin; LimitB = tMax;
            return true;
        }
    }
}
