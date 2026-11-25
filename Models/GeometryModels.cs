using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ScienceWordOverlay.Models
{
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
}
