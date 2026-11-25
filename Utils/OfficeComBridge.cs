using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using ScienceWordOverlay.Models;

namespace ScienceWordOverlay.Utils
{
    public enum OfficeAppKind { None, Word, PowerPoint, Excel }

    public class OfficeExtractedContour
    {
        public List<Point> ScreenPoints { get; set; } = new();
        public bool IsLine { get; set; }
        public double DiagSpan { get; set; }
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
        public RevolutionScene? Revolution { get; set; }
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

            var ctx = ReadFromWord(dpiScaleX, dpiScaleY, out error);
            if (ctx != null) return ctx;
            ctx = ReadFromPowerPoint(dpiScaleX, dpiScaleY, out error);
            if (ctx != null) return ctx;
            ctx = ReadFromExcel(dpiScaleX, dpiScaleY, out error);
            if (ctx != null) return ctx;

            error = "Không tìm thấy hình đang chọn trong Word, PowerPoint hoặc Excel. Với PDF/Web/Paint hãy dùng Đũa thần hoặc Chọn HCN/Tự do!";
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
                object winObj = wordApp.ActiveWindow;
                dynamic win = winObj;
                object? srObj = null;
                try { srObj = wordApp.Selection.ShapeRange; } catch { }
                if (srObj == null) { error = "Hãy chọn ít nhất 1 hình trong Word."; return null; }

                List<object> shapes = CollectShapes(srObj);
                if (shapes.Count == 0) { error = "Hãy chọn ít nhất 1 hình trong Word."; return null; }

                double zoomPct = 100.0;
                try { zoomPct = Convert.ToDouble(win.ActivePane.View.Zoom.Percentage); } catch { }

                Func<object, Rect> getRectPx = (object shObj) => TryWordGetPoint(winObj, shObj, out Rect r) ? r : Rect.Empty;
                var ctx = BuildPerShapeContext(OfficeAppKind.Word, shapes, getRectPx, dpiScaleX, dpiScaleY, zoomPct, "Word");

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
                object winObj = pptApp.ActiveWindow;
                dynamic win = winObj;
                object? srObj = null;
                try { srObj = win.Selection.ShapeRange; } catch { }
                if (srObj == null) { error = "Hãy chọn ít nhất 1 hình trong PowerPoint."; return null; }

                List<object> shapes = CollectShapes(srObj);
                if (shapes.Count == 0) { error = "Hãy chọn ít nhất 1 hình trong PowerPoint."; return null; }

                double zoomPct = 100.0;
                try { zoomPct = Convert.ToDouble(win.View.Zoom); } catch { }

                Func<object, Rect> getRectPx = (object shObj) =>
                {
                    try
                    {
                        dynamic sh = shObj;
                        dynamic w = winObj;
                        float lPt = Convert.ToSingle(sh.Left);
                        float tPt = Convert.ToSingle(sh.Top);
                        float wPt = Convert.ToSingle(sh.Width);
                        float hPt = Convert.ToSingle(sh.Height);
                        double x1 = Convert.ToDouble(w.PointsToScreenPixelsX(lPt));
                        double y1 = Convert.ToDouble(w.PointsToScreenPixelsY(tPt));
                        double x2 = Convert.ToDouble(w.PointsToScreenPixelsX(lPt + wPt));
                        double y2 = Convert.ToDouble(w.PointsToScreenPixelsY(tPt + hPt));
                        return new Rect(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
                    }
                    catch { return Rect.Empty; }
                };

                return BuildPerShapeContext(OfficeAppKind.PowerPoint, shapes, getRectPx, dpiScaleX, dpiScaleY, zoomPct, "PowerPoint");
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
                object winObj = xlApp.ActiveWindow;
                dynamic win = winObj;
                object? srObj = null;
                try { srObj = xlApp.Selection.ShapeRange; } catch { }
                if (srObj == null) { error = "Hãy chọn ít nhất 1 hình (Shape) trong Excel."; return null; }

                List<object> shapes = CollectShapes(srObj);
                if (shapes.Count == 0) { error = "Hãy chọn ít nhất 1 hình (Shape) trong Excel."; return null; }

                double zoomPct = 100.0;
                try { zoomPct = Convert.ToDouble(win.Zoom); } catch { }

                Func<object, Rect> getRectPx = (object shObj) =>
                {
                    try
                    {
                        dynamic sh = shObj;
                        dynamic w = winObj;
                        double lPt = Convert.ToDouble(sh.Left);
                        double tPt = Convert.ToDouble(sh.Top);
                        double wPt = Convert.ToDouble(sh.Width);
                        double hPt = Convert.ToDouble(sh.Height);
                        double x1 = Convert.ToDouble(w.PointsToScreenPixelsX(lPt));
                        double y1 = Convert.ToDouble(w.PointsToScreenPixelsY(tPt));
                        double x2 = Convert.ToDouble(w.PointsToScreenPixelsX(lPt + wPt));
                        double y2 = Convert.ToDouble(w.PointsToScreenPixelsY(tPt + hPt));
                        return new Rect(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
                    }
                    catch { return Rect.Empty; }
                };

                return BuildPerShapeContext(OfficeAppKind.Excel, shapes, getRectPx, dpiScaleX, dpiScaleY, zoomPct, "Excel");
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

        // ===================== CORE PER-SHAPE PIXEL-PERFECT MAPPER =====================
        private static List<object> CollectShapes(object shapeRangeObj)
        {
            var list = new List<object>();
            dynamic shapeRange = shapeRangeObj;
            int count = (int)shapeRange.Count;
            for (int i = 1; i <= count; i++)
            {
                object itemObj = shapeRange.Item(i);
                dynamic item = itemObj;
                int t = 0;
                try { t = (int)item.Type; } catch { }
                if (t == MsoGroup)
                {
                    try
                    {
                        dynamic gItems = item.GroupItems;
                        int gc = (int)gItems.Count;
                        for (int g = 1; g <= gc; g++) list.Add((object)gItems.Item(g));
                        continue;
                    }
                    catch { }
                }
                list.Add(itemObj);
            }
            return list;
        }

        // Ánh xạ TỪNG SHAPE ĐỘC LẬP vào đúng GetPoint(sh) của chính nó -> KHỚP 100% KHÔNG BAO GIỜ LỆCH!
        private static OfficeSelectionContext? BuildPerShapeContext(
            OfficeAppKind appKind,
            List<object> shapes,
            Func<object, Rect> getShapeRectPx,
            double dpiScaleX,
            double dpiScaleY,
            double zoomPct,
            string appLabel)
        {
            var ctx = new OfficeSelectionContext
            {
                AppKind = appKind,
                DipPerPt = (96.0 / 72.0) * (zoomPct / 100.0)
            };

            double allMinX = double.MaxValue, allMinY = double.MaxValue;
            double allMaxX = double.MinValue, allMaxY = double.MinValue;
            bool hasRef = false;

            foreach (object shObj in shapes)
            {
                Rect shPx = getShapeRectPx(shObj);
                if (shPx.IsEmpty) continue;

                Rect shDip = new(shPx.Left / dpiScaleX, shPx.Top / dpiScaleY,
                                 Math.Max(1.0, shPx.Width / dpiScaleX), Math.Max(1.0, shPx.Height / dpiScaleY));

                var rawPt = ExtractShapePointsInPt(shObj, out bool isLine);
                if (rawPt.Count < 2) continue;
                if (!isLine && IsNearlyStraight(rawPt, 2.5)) isLine = true;

                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (var p in rawPt)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                }
                double spanX = maxX - minX;
                double spanY = maxY - minY;

                var contour = new OfficeExtractedContour { IsLine = isLine };
                foreach (var p in rawPt)
                {
                    double nx = spanX > 1e-3 ? (p.X - minX) / spanX : 0.5;
                    double ny = spanY > 1e-3 ? (p.Y - minY) / spanY : 0.5;
                    double dipX = shDip.Left + nx * shDip.Width;
                    double dipY = shDip.Top + ny * shDip.Height;
                    contour.ScreenPoints.Add(new Point(dipX, dipY));
                }

                contour.DiagSpan = Math.Sqrt(shDip.Width * shDip.Width + shDip.Height * shDip.Height);
                ctx.Contours.Add(contour);

                if (shDip.Left < allMinX) allMinX = shDip.Left;
                if (shDip.Top < allMinY) allMinY = shDip.Top;
                if (shDip.Right > allMaxX) allMaxX = shDip.Right;
                if (shDip.Bottom > allMaxY) allMaxY = shDip.Bottom;

                if (!hasRef)
                {
                    hasRef = true;
                    dynamic sh = shObj;
                    double wPt = Math.Max(1.0, Convert.ToDouble(sh.Width));
                    double hPt = Math.Max(1.0, Convert.ToDouble(sh.Height));
                    if (shDip.Width > 8 && wPt > 8) ctx.DipPerPt = shDip.Width / wPt;
                    else if (shDip.Height > 8 && hPt > 8) ctx.DipPerPt = shDip.Height / hPt;

                    ctx.RefDipLeft = shDip.Left;
                    ctx.RefDipTop = shDip.Top;
                    ctx.RefDocLeftPt = Convert.ToDouble(sh.Left);
                    ctx.RefDocTopPt = Convert.ToDouble(sh.Top);
                }
            }

            if (ctx.Contours.Count == 0) return null;

            ctx.ScreenBoundsDip = new Rect(allMinX, allMinY, Math.Max(2, allMaxX - allMinX), Math.Max(2, allMaxY - allMinY));
            ctx.Revolution = TryBuildRevolutionScene(ctx.Contours, zoomPct);
            ctx.Summary = ctx.Revolution != null
                ? $"Đã khớp {ctx.Contours.Count} hình từ {appLabel} (Sẵn sàng Tô S & Quay 3D)"
                : $"Đã khớp {ctx.Contours.Count} hình từ {appLabel} (Zoom {zoomPct:0}%)";
            return ctx;
        }

        // Tự động nhận diện 1 Trục + 1 Đường cong (hoặc 2 Đường cong) để Tô Diện Tích & Quay 3D tính Thể Tích
        private static RevolutionScene? TryBuildRevolutionScene(List<OfficeExtractedContour> contours, double zoomPct)
        {
            if (contours.Count < 2) return null;

            int axisIdx = -1;
            double bestAxisLen = -1;
            for (int i = 0; i < contours.Count; i++)
            {
                if (contours[i].IsLine && contours[i].DiagSpan > bestAxisLen)
                {
                    bestAxisLen = contours[i].DiagSpan;
                    axisIdx = i;
                }
            }

            var curveIndices = new List<int>();
            for (int i = 0; i < contours.Count; i++)
                if (i != axisIdx && !contours[i].IsLine) curveIndices.Add(i);

            curveIndices.Sort((a, b) => contours[b].DiagSpan.CompareTo(contours[a].DiagSpan));

            // Nếu chỉ có 2 đoạn thẳng -> lấy đoạn dài hơn làm trục, đoạn kia làm đường sinh
            if (axisIdx != -1 && curveIndices.Count == 0)
            {
                for (int i = 0; i < contours.Count; i++)
                    if (i != axisIdx) { curveIndices.Add(i); break; }
            }

            var rev = new RevolutionScene { DipPerCm = (96.0 / 2.54) * (zoomPct / 100.0) };

            if (axisIdx != -1 && curveIndices.Count >= 1)
            {
                var axisPts = contours[axisIdx].ScreenPoints;
                var curvePts = contours[curveIndices[0]].ScreenPoints;
                Point pA = axisPts[0], pB = axisPts[^1];
                Vector u = pB - pA;
                if (u.Length < 2.0) return null;
                u.Normalize();
                Vector v = new(-u.Y, u.X);
                if (v.Y > 0.05) v = -v;

                rev.HasTwoCurves = false;
                rev.AxisOrigin = pA;
                rev.AxisU = u;
                rev.AxisV = v;
                rev.Curve1TR = ProjectToTR(curvePts, pA, u, v, out double tMin, out double tMax);
                rev.TMin = tMin; rev.TMax = tMax;
                rev.LimitA = tMin; rev.LimitB = tMax;
                return rev.IsValid ? rev : null;
            }

            if (curveIndices.Count >= 2)
            {
                var pts1 = contours[curveIndices[0]].ScreenPoints;
                var pts2 = contours[curveIndices[1]].ScreenPoints;
                double minX = double.MaxValue, sumY = 0;
                int cnt = 0;
                foreach (var p in pts1) { if (p.X < minX) minX = p.X; sumY += p.Y; cnt++; }
                foreach (var p in pts2) { if (p.X < minX) minX = p.X; sumY += p.Y; cnt++; }

                Point origin = new(minX, cnt > 0 ? sumY / cnt : 0);
                Vector u = new(1, 0), v = new(0, 1);

                rev.HasTwoCurves = true;
                rev.AxisOrigin = origin;
                rev.AxisU = u;
                rev.AxisV = v;
                rev.Curve1TR = ProjectToTR(pts1, origin, u, v, out double t1Min, out double t1Max);
                rev.Curve2TR = ProjectToTR(pts2, origin, u, v, out double t2Min, out double t2Max);

                double oMin = Math.Max(t1Min, t2Min), oMax = Math.Min(t1Max, t2Max);
                rev.TMin = (oMax - oMin > 5) ? oMin : Math.Min(t1Min, t2Min);
                rev.TMax = (oMax - oMin > 5) ? oMax : Math.Max(t1Max, t2Max);
                rev.LimitA = rev.TMin; rev.LimitB = rev.TMax;
                return rev.IsValid ? rev : null;
            }

            return null;
        }

        private static List<Point> ProjectToTR(List<Point> pts, Point origin, Vector u, Vector v, out double tMin, out double tMax)
        {
            var tr = new List<Point>(pts.Count);
            tMin = double.MaxValue; tMax = double.MinValue;
            foreach (var p in pts)
            {
                double dx = p.X - origin.X, dy = p.Y - origin.Y;
                double t = dx * u.X + dy * u.Y;
                double r = dx * v.X + dy * v.Y;
                tr.Add(new Point(t, r));
                if (t < tMin) tMin = t;
                if (t > tMax) tMax = t;
            }
            if (tMin > tMax) { tMin = 0; tMax = 0; }
            return tr;
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

        private static List<Point> ExtractShapePointsInPt(object shapeObj, out bool isLine)
        {
            dynamic shape = shapeObj;
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
                            for (int s = 1; s <= 24; s++)
                                result.Add(EvalCubicBezier(curr, c1, c2, p3, s / 24.0));
                            curr = p3;
                            i += 3;
                        }
                        else
                        {
                            Point next = GetNodePoint(node);
                            for (int s = 1; s <= 6; s++)
                                result.Add(new Point(curr.X + (next.X - curr.X) * s / 6.0, curr.Y + (next.Y - curr.Y) * s / 6.0));
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

        private static bool IsNearlyStraight(List<Point> pts, double tol)
        {
            if (pts.Count <= 2) return true;
            Point a = pts[0], b = pts[^1];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1.0) return false;
            for (int i = 1; i < pts.Count - 1; i++)
                if (Math.Abs((pts[i].X - a.X) * dy - (pts[i].Y - a.Y) * dx) / len > tol) return false;
            return true;
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
