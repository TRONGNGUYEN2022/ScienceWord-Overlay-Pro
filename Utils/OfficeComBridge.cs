using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

namespace ScienceWordOverlay.Utils
{
    public enum OfficeAppKind { None, Word, PowerPoint, Excel }

    public class OfficeExtractedContour
    {
        public List<Point> ScreenPoints { get; set; } = new();
        public bool IsLine { get; set; }
    }

    public class OfficeSelectionContext
    {
        public OfficeAppKind AppKind { get; set; } = OfficeAppKind.None;
        public Rect ScreenBoundsDip { get; set; }
        public double DipPerPt { get; set; } = 96.0 / 72.0;
        public double RefDipLeft { get; set; }
        public double RefDipTop { get; set; }
        public double RefDocLeftPt { get; set; }
        public double RefDocTopPt { get; set; }
        public int RefRelH { get; set; } = 1;
        public int RefRelV { get; set; } = 1;
        public object? RefAnchor { get; set; }
        public List<OfficeExtractedContour> Contours { get; set; } = new();
        public string Summary { get; set; } = "";
    }

    public static class OfficeComBridge
    {
        private const int MsoLine = 9;
        private const int MsoGroup = 6;
        private const int MsoSegmentCurve = 1;
        private const int MsoTrue = -1;

        [DllImport("ole32.dll")]
        private static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string lpszProgID, out Guid pclsid);

        [DllImport("oleaut32.dll")]
        private static extern int GetActiveObject(ref Guid rclsid, IntPtr pvReserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

        private static object? GetActiveComObject(string progId)
        {
            int hr = CLSIDFromProgID(progId, out Guid clsid);
            if (hr < 0) return null;
            hr = GetActiveObject(ref clsid, IntPtr.Zero, out object obj);
            return hr >= 0 ? obj : null;
        }

        public static OfficeSelectionContext? ReadFromActiveOffice(string targetProcName, double dpiScaleX, double dpiScaleY, out string? error)
        {
            string p = targetProcName.ToUpperInvariant();
            if (p == "POWERPNT") return ReadFromPowerPoint(dpiScaleX, dpiScaleY, out error);
            if (p == "EXCEL") return ReadFromExcel(dpiScaleX, dpiScaleY, out error);
            if (p == "WINWORD") return ReadFromWord(dpiScaleX, dpiScaleY, out error);

            // Tự thử lần lượt Word -> PowerPoint -> Excel
            var ctx = ReadFromWord(dpiScaleX, dpiScaleY, out error);
            if (ctx != null) return ctx;
            ctx = ReadFromPowerPoint(dpiScaleX, dpiScaleY, out error);
            if (ctx != null) return ctx;
            ctx = ReadFromExcel(dpiScaleX, dpiScaleY, out error);
            if (ctx != null) return ctx;

            error = "Không tìm thấy đối tượng đang chọn trong Word, PowerPoint hoặc Excel. Với PDF/Web/Paint hãy dùng Đũa thần!";
            return null;
        }

        public static bool InsertPngToOffice(OfficeSelectionContext? ctx, string targetProcName, string pngPath, Rect screenBoxDip, out string? error)
        {
            OfficeAppKind kind = ctx?.AppKind ?? targetProcName.ToUpperInvariant() switch
            {
                "WINWORD" => OfficeAppKind.Word,
                "POWERPNT" => OfficeAppKind.PowerPoint,
                "EXCEL" => OfficeAppKind.Excel,
                _ => OfficeAppKind.None
            };

            if (kind == OfficeAppKind.Word) return InsertPngToWord(ctx, pngPath, screenBoxDip, out error);
            if (kind == OfficeAppKind.PowerPoint) return InsertPngToPowerPoint(ctx, pngPath, screenBoxDip, out error);
            if (kind == OfficeAppKind.Excel) return InsertPngToExcel(ctx, pngPath, screenBoxDip, out error);

            error = "App đích không phải Office COM.";
            return false;
        }

        // ===================== 1. MICROSOFT WORD =====================
        private static OfficeSelectionContext? ReadFromWord(double dpiScaleX, double dpiScaleY, out string? error)
        {
            error = null;
            object? wordObj = GetActiveComObject("Word.Application");
            if (wordObj == null) { error = "Không tìm thấy Word đang mở."; return null; }

            try
            {
                dynamic wordApp = wordObj;
                if ((int)wordApp.Documents.Count == 0) { error = "Word chưa mở tài liệu."; return null; }
                dynamic win = wordApp.ActiveWindow;
                dynamic? sr = null;
                try { sr = wordApp.Selection.ShapeRange; } catch { }
                if (sr == null || (int)sr.Count == 0) { error = "Hãy chọn ít nhất 1 hình trong Word."; return null; }

                var shapes = CollectShapes(sr);
                if (shapes.Count == 0) { error = "Không có hình hợp lệ trong vùng chọn Word."; return null; }

                double zoomPct = 100.0;
                try { zoomPct = Convert.ToDouble(win.ActivePane.View.Zoom.Percentage); } catch { }
                double fallbackPxPerPt = (96.0 * dpiScaleX / 72.0) * (zoomPct / 100.0);

                bool hasGroupBox = TryWordGetPoint(win, sr, out Rect groupPx);
                var ctx = BuildContextFromShapes(OfficeAppKind.Word, shapes, hasGroupBox, groupPx,
                    sh => TryWordGetPoint(win, sh, out Rect r) ? r : Rect.Empty,
                    fallbackPxPerPt, dpiScaleX, dpiScaleY, zoomPct, "Word");

                if (ctx != null)
                {
                    dynamic refShape = shapes[0];
                    try { ctx.RefRelH = (int)refShape.RelativeHorizontalPosition; } catch { }
                    try { ctx.RefRelV = (int)refShape.RelativeVerticalPosition; } catch { }
                    try { ctx.RefAnchor = refShape.Anchor; } catch { }
                }
                return ctx;
            }
            catch (Exception ex) { error = "Lỗi đọc Word: " + ex.Message; return null; }
        }

        private static bool InsertPngToWord(OfficeSelectionContext? ctx, string pngPath, Rect screenBoxDip, out string? error)
        {
            error = null;
            object? wordObj = GetActiveComObject("Word.Application");
            if (wordObj == null) { error = "Không tìm thấy Word."; return false; }
            try
            {
                dynamic wordApp = wordObj;
                dynamic doc = wordApp.ActiveDocument;
                double dipPerPt = (ctx != null && ctx.DipPerPt > 0.1) ? ctx.DipPerPt : (96.0 / 72.0);
                double leftPt = ctx != null ? ctx.RefDocLeftPt + (screenBoxDip.Left - ctx.RefDipLeft) / dipPerPt : 72.0;
                double topPt = ctx != null ? ctx.RefDocTopPt + (screenBoxDip.Top - ctx.RefDipTop) / dipPerPt : 72.0;
                double wPt = screenBoxDip.Width / dipPerPt;
                double hPt = screenBoxDip.Height / dipPerPt;

                dynamic pic = ctx?.RefAnchor != null
                    ? doc.Shapes.AddPicture(pngPath, false, true, (float)leftPt, (float)topPt, (float)wPt, (float)hPt, ctx.RefAnchor)
                    : doc.Shapes.AddPicture(pngPath, false, true, (float)leftPt, (float)topPt, (float)wPt, (float)hPt);

                try { pic.LockAspectRatio = MsoTrue; } catch { }
                try { pic.WrapFormat.Type = 3; } catch { }
                if (ctx != null)
                {
                    try { pic.RelativeHorizontalPosition = ctx.RefRelH; } catch { }
                    try { pic.RelativeVerticalPosition = ctx.RefRelV; } catch { }
                }
                pic.Left = (float)leftPt; pic.Top = (float)topPt;
                pic.Width = (float)wPt; pic.Height = (float)hPt;
                return true;
            }
            catch (Exception ex) { error = "Lỗi chèn Word: " + ex.Message; return false; }
        }

        // ===================== 2. MICROSOFT POWERPOINT =====================
        private static OfficeSelectionContext? ReadFromPowerPoint(double dpiScaleX, double dpiScaleY, out string? error)
        {
            error = null;
            object? pptObj = GetActiveComObject("PowerPoint.Application");
            if (pptObj == null) { error = "Không tìm thấy PowerPoint đang mở."; return null; }

            try
            {
                dynamic pptApp = pptObj;
                if ((int)pptApp.Presentations.Count == 0) { error = "PowerPoint chưa mở bài trình chiếu."; return null; }
                dynamic win = pptApp.ActiveWindow;
                dynamic? sr = null;
                try { sr = win.Selection.ShapeRange; } catch { }
                if (sr == null || (int)sr.Count == 0) { error = "Hãy chọn ít nhất 1 hình trong PowerPoint."; return null; }

                var shapes = CollectShapes(sr);
                if (shapes.Count == 0) { error = "Không có hình hợp lệ trong vùng chọn PowerPoint."; return null; }

                double zoomPct = 100.0;
                try { zoomPct = Convert.ToDouble(win.View.Zoom); } catch { }

                // PowerPoint có sẵn PointsToScreenPixelsX / Y rất chính xác!
                Rect GetPptRectPx(dynamic sh)
                {
                    try
                    {
                        float lPt = Convert.ToSingle(sh.Left);
                        float tPt = Convert.ToSingle(sh.Top);
                        float wPt = Convert.ToSingle(sh.Width);
                        float hPt = Convert.ToSingle(sh.Height);
                        double x1 = Convert.ToDouble(win.PointsToScreenPixelsX(lPt));
                        double y1 = Convert.ToDouble(win.PointsToScreenPixelsY(tPt));
                        double x2 = Convert.ToDouble(win.PointsToScreenPixelsX(lPt + wPt));
                        double y2 = Convert.ToDouble(win.PointsToScreenPixelsY(tPt + hPt));
                        return new Rect(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
                    }
                    catch { return Rect.Empty; }
                }

                double fallbackPxPerPt = (96.0 * dpiScaleX / 72.0) * (zoomPct / 100.0);
                return BuildContextFromShapes(OfficeAppKind.PowerPoint, shapes, false, Rect.Empty,
                    GetPptRectPx, fallbackPxPerPt, dpiScaleX, dpiScaleY, zoomPct, "PowerPoint");
            }
            catch (Exception ex) { error = "Lỗi đọc PowerPoint: " + ex.Message; return null; }
        }

        private static bool InsertPngToPowerPoint(OfficeSelectionContext? ctx, string pngPath, Rect screenBoxDip, out string? error)
        {
            error = null;
            object? pptObj = GetActiveComObject("PowerPoint.Application");
            if (pptObj == null) { error = "Không tìm thấy PowerPoint."; return false; }
            try
            {
                dynamic pptApp = pptObj;
                dynamic slide = pptApp.ActiveWindow.View.Slide;
                double dipPerPt = (ctx != null && ctx.DipPerPt > 0.1) ? ctx.DipPerPt : (96.0 / 72.0);
                double leftPt = ctx != null ? ctx.RefDocLeftPt + (screenBoxDip.Left - ctx.RefDipLeft) / dipPerPt : 60.0;
                double topPt = ctx != null ? ctx.RefDocTopPt + (screenBoxDip.Top - ctx.RefDipTop) / dipPerPt : 60.0;
                double wPt = screenBoxDip.Width / dipPerPt;
                double hPt = screenBoxDip.Height / dipPerPt;

                slide.Shapes.AddPicture(pngPath, 0, -1, (float)leftPt, (float)topPt, (float)wPt, (float)hPt);
                return true;
            }
            catch (Exception ex) { error = "Lỗi chèn PowerPoint: " + ex.Message; return false; }
        }

        // ===================== 3. MICROSOFT EXCEL =====================
        private static OfficeSelectionContext? ReadFromExcel(double dpiScaleX, double dpiScaleY, out string? error)
        {
            error = null;
            object? xlObj = GetActiveComObject("Excel.Application");
            if (xlObj == null) { error = "Không tìm thấy Excel đang mở."; return null; }

            try
            {
                dynamic xlApp = xlObj;
                if ((int)xlApp.Workbooks.Count == 0) { error = "Excel chưa mở bảng tính."; return null; }
                dynamic win = xlApp.ActiveWindow;
                dynamic? sr = null;
                try { sr = xlApp.Selection.ShapeRange; } catch { }
                if (sr == null || (int)sr.Count == 0) { error = "Hãy chọn ít nhất 1 hình (Shape) trong Excel."; return null; }

                var shapes = CollectShapes(sr);
                if (shapes.Count == 0) { error = "Không có hình hợp lệ trong vùng chọn Excel."; return null; }

                double zoomPct = 100.0;
                try { zoomPct = Convert.ToDouble(win.Zoom); } catch { }

                Rect GetXlRectPx(dynamic sh)
                {
                    try
                    {
                        double lPt = Convert.ToDouble(sh.Left);
                        double tPt = Convert.ToDouble(sh.Top);
                        double wPt = Convert.ToDouble(sh.Width);
                        double hPt = Convert.ToDouble(sh.Height);
                        double x1 = Convert.ToDouble(win.PointsToScreenPixelsX(lPt));
                        double y1 = Convert.ToDouble(win.PointsToScreenPixelsY(tPt));
                        double x2 = Convert.ToDouble(win.PointsToScreenPixelsX(lPt + wPt));
                        double y2 = Convert.ToDouble(win.PointsToScreenPixelsY(tPt + hPt));
                        return new Rect(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
                    }
                    catch { return Rect.Empty; }
                }

                double fallbackPxPerPt = (96.0 * dpiScaleX / 72.0) * (zoomPct / 100.0);
                return BuildContextFromShapes(OfficeAppKind.Excel, shapes, false, Rect.Empty,
                    GetXlRectPx, fallbackPxPerPt, dpiScaleX, dpiScaleY, zoomPct, "Excel");
            }
            catch (Exception ex) { error = "Lỗi đọc Excel: " + ex.Message; return null; }
        }

        private static bool InsertPngToExcel(OfficeSelectionContext? ctx, string pngPath, Rect screenBoxDip, out string? error)
        {
            error = null;
            object? xlObj = GetActiveComObject("Excel.Application");
            if (xlObj == null) { error = "Không tìm thấy Excel."; return false; }
            try
            {
                dynamic xlApp = xlObj;
                dynamic sheet = xlApp.ActiveSheet;
                double dipPerPt = (ctx != null && ctx.DipPerPt > 0.1) ? ctx.DipPerPt : (96.0 / 72.0);
                double leftPt = ctx != null ? ctx.RefDocLeftPt + (screenBoxDip.Left - ctx.RefDipLeft) / dipPerPt : 60.0;
                double topPt = ctx != null ? ctx.RefDocTopPt + (screenBoxDip.Top - ctx.RefDipTop) / dipPerPt : 60.0;
                double wPt = screenBoxDip.Width / dipPerPt;
                double hPt = screenBoxDip.Height / dipPerPt;

                sheet.Shapes.AddPicture(pngPath, 0, -1, (float)leftPt, (float)topPt, (float)wPt, (float)hPt);
                return true;
            }
            catch (Exception ex) { error = "Lỗi chèn Excel: " + ex.Message; return false; }
        }

        // ===================== HELPERS CHUNG CHO OFFICE =====================
        private static List<dynamic> CollectShapes(dynamic shapeRange)
        {
            var list = new List<dynamic>();
            int count = (int)shapeRange.Count;
            for (int i = 1; i <= count; i++)
            {
                dynamic item = shapeRange.Item(i);
                int t = 0;
                try { t = (int)item.Type; } catch { }
                if (t == MsoGroup)
                {
                    try
                    {
                        dynamic gItems = item.GroupItems;
                        int gc = (int)gItems.Count;
                        for (int g = 1; g <= gc; g++) list.Add(gItems.Item(g));
                        continue;
                    }
                    catch { }
                }
                list.Add(item);
            }
            return list;
        }

        private static OfficeSelectionContext? BuildContextFromShapes(
            OfficeAppKind appKind,
            List<dynamic> shapes,
            bool hasGroupBox,
            Rect groupPx,
            Func<dynamic, Rect> getShapeRectPx,
            double fallbackPxPerPt,
            double dpiScaleX,
            double dpiScaleY,
            double zoomPct,
            string appLabel)
        {
            double minPtX = double.MaxValue, minPtY = double.MaxValue;
            double maxPtX = double.MinValue, maxPtY = double.MinValue;
            var rawContours = new List<(List<Point> pts, bool isLine)>();

            foreach (dynamic sh in shapes)
            {
                var ptsPt = ExtractShapePointsInPt(sh, out bool isLine);
                foreach (var p in ptsPt)
                {
                    if (p.X < minPtX) minPtX = p.X;
                    if (p.Y < minPtY) minPtY = p.Y;
                    if (p.X > maxPtX) maxPtX = p.X;
                    if (p.Y > maxPtY) maxPtY = p.Y;
                }
                rawContours.Add((ptsPt, isLine));
            }

            double ptW = Math.Max(1.0, maxPtX - minPtX);
            double ptH = Math.Max(1.0, maxPtY - minPtY);
            double pxPerPtX = fallbackPxPerPt, pxPerPtY = fallbackPxPerPt;
            double basePxX = 0, basePxY = 0;

            if (hasGroupBox && groupPx.Width > 4 && groupPx.Height > 4)
            {
                pxPerPtX = groupPx.Width / ptW;
                pxPerPtY = groupPx.Height / ptH;
                basePxX = groupPx.Left;
                basePxY = groupPx.Top;
            }
            else
            {
                Rect firstPx = getShapeRectPx(shapes[0]);
                if (firstPx.IsEmpty) return null;
                double fLeft = Convert.ToDouble(shapes[0].Left);
                double fTop = Convert.ToDouble(shapes[0].Top);
                double fW = Math.Max(1.0, Convert.ToDouble(shapes[0].Width));
                double fH = Math.Max(1.0, Convert.ToDouble(shapes[0].Height));
                pxPerPtX = firstPx.Width > 4 ? firstPx.Width / fW : fallbackPxPerPt;
                pxPerPtY = firstPx.Height > 4 ? firstPx.Height / fH : fallbackPxPerPt;
                basePxX = firstPx.Left + (minPtX - fLeft) * pxPerPtX;
                basePxY = firstPx.Top + (minPtY - fTop) * pxPerPtY;
            }

            var ctx = new OfficeSelectionContext
            {
                AppKind = appKind,
                DipPerPt = 0.5 * (pxPerPtX / dpiScaleX + pxPerPtY / dpiScaleY)
            };

            double minDipX = double.MaxValue, minDipY = double.MaxValue;
            double maxDipX = double.MinValue, maxDipY = double.MinValue;

            foreach (var (ptsPt, isLine) in rawContours)
            {
                var contour = new OfficeExtractedContour { IsLine = isLine };
                foreach (var p in ptsPt)
                {
                    double dipX = (basePxX + (p.X - minPtX) * pxPerPtX) / dpiScaleX;
                    double dipY = (basePxY + (p.Y - minPtY) * pxPerPtY) / dpiScaleY;
                    contour.ScreenPoints.Add(new Point(dipX, dipY));
                    if (dipX < minDipX) minDipX = dipX;
                    if (dipY < minDipY) minDipY = dipY;
                    if (dipX > maxDipX) maxDipX = dipX;
                    if (dipY > maxDipY) maxDipY = dipY;
                }
                if (contour.ScreenPoints.Count >= 2) ctx.Contours.Add(contour);
            }

            if (minDipX < maxDipX && minDipY < maxDipY)
                ctx.ScreenBoundsDip = new Rect(minDipX, minDipY, maxDipX - minDipX, maxDipY - minDipY);

            dynamic refShape = shapes[0];
            Rect refPx = getShapeRectPx(refShape);
            ctx.RefDipLeft = (!refPx.IsEmpty ? refPx.Left : basePxX) / dpiScaleX;
            ctx.RefDipTop = (!refPx.IsEmpty ? refPx.Top : basePxY) / dpiScaleY;
            ctx.RefDocLeftPt = Convert.ToDouble(refShape.Left);
            ctx.RefDocTopPt = Convert.ToDouble(refShape.Top);
            ctx.Summary = $"Đã đồng bộ {shapes.Count} đối tượng từ {appLabel} (Zoom {zoomPct:0}%)";
            return ctx;
        }

        private static bool TryWordGetPoint(object activeWindow, object targetObj, out Rect rect)
        {
            rect = Rect.Empty;
            try
            {
                object[] args = { 0, 0, 0, 0, targetObj };
                var pm = new ParameterModifier(5);
                pm[0] = true; pm[1] = true; pm[2] = true; pm[3] = true; pm[4] = false;

                activeWindow.GetType().InvokeMember(
                    "GetPoint", BindingFlags.InvokeMethod, null, activeWindow, args, new[] { pm }, null, null);

                rect = new Rect(Convert.ToInt32(args[0]), Convert.ToInt32(args[1]),
                    Math.Max(0, Convert.ToInt32(args[2])), Math.Max(0, Convert.ToInt32(args[3])));
                return true;
            }
            catch { return false; }
        }

        private static List<Point> ExtractShapePointsInPt(dynamic shape, out bool isLine)
        {
            var result = new List<Point>();
            int shapeType = 0;
            try { shapeType = (int)shape.Type; } catch { }

            double left = Convert.ToDouble(shape.Left);
            double top = Convert.ToDouble(shape.Top);
            double width = Convert.ToDouble(shape.Width);
            double height = Convert.ToDouble(shape.Height);
            double rotation = 0;
            try { rotation = Convert.ToDouble(shape.Rotation); } catch { }

            if (shapeType == MsoLine)
            {
                isLine = true;
                bool hFlip = false, vFlip = false;
                try { hFlip = (int)shape.HorizontalFlip == MsoTrue; } catch { }
                try { vFlip = (int)shape.VerticalFlip == MsoTrue; } catch { }

                Point p1 = new(hFlip ? left + width : left, vFlip ? top + height : top);
                Point p2 = new(hFlip ? left : left + width, vFlip ? top : top + height);
                if (Math.Abs(rotation) > 0.01)
                {
                    Point c = new(left + width * 0.5, top + height * 0.5);
                    p1 = RotatePoint(p1, c, rotation);
                    p2 = RotatePoint(p2, c, rotation);
                }
                result.Add(p1); result.Add(p2);
                return result;
            }

            isLine = false;
            try
            {
                dynamic nodes = shape.Nodes;
                int count = (int)nodes.Count;
                if (count >= 2)
                {
                    Point curr = GetNodePoint(nodes.Item(1));
                    result.Add(curr);
                    int i = 2;
                    while (i <= count)
                    {
                        dynamic node = nodes.Item(i);
                        int segType = (int)node.SegmentType;
                        if (segType == MsoSegmentCurve && i + 2 <= count)
                        {
                            Point c1 = GetNodePoint(node);
                            Point c2 = GetNodePoint(nodes.Item(i + 1));
                            Point p3 = GetNodePoint(nodes.Item(i + 2));
                            for (int s = 1; s <= 18; s++)
                                result.Add(EvalCubicBezier(curr, c1, c2, p3, s / 18.0));
                            curr = p3;
                            i += 3;
                        }
                        else
                        {
                            Point next = GetNodePoint(node);
                            result.Add(next);
                            curr = next;
                            i += 1;
                        }
                    }
                    if (Math.Abs(rotation) > 0.01)
                    {
                        Point c = new(left + width * 0.5, top + height * 0.5);
                        for (int k = 0; k < result.Count; k++) result[k] = RotatePoint(result[k], c, rotation);
                    }
                    return result;
                }
            }
            catch { }

            result.Add(new Point(left, top));
            result.Add(new Point(left + width, top));
            result.Add(new Point(left + width, top + height));
            result.Add(new Point(left, top + height));
            return result;
        }

        private static Point GetNodePoint(dynamic node)
        {
            Array pts = (Array)node.Points;
            int lb0 = pts.GetLowerBound(0);
            int lb1 = pts.GetLowerBound(1);
            return new Point(Convert.ToDouble(pts.GetValue(lb0, lb1)), Convert.ToDouble(pts.GetValue(lb0, lb1 + 1)));
        }

        private static Point EvalCubicBezier(Point p0, Point c1, Point c2, Point p3, double t)
        {
            double u = 1 - t;
            return new Point(
                u * u * u * p0.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * p3.X,
                u * u * u * p0.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * p3.Y);
        }

        private static Point RotatePoint(Point p, Point c, double deg)
        {
            double rad = deg * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            double dx = p.X - c.X, dy = p.Y - c.Y;
            return new Point(c.X + dx * cos - dy * sin, c.Y + dx * sin + dy * cos);
        }
    }
}
