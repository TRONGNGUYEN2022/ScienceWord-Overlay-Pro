using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using ScienceWordOverlay.Models;
using ScienceWordOverlay.Utils;

namespace ScienceWordOverlay
{
    public class OverlaySurface : FrameworkElement
    {
        public SceneState Scene { get; private set; } = new();
        public List<MagicFillLayer> MagicFills { get; } = new();
        public OfficeSelectionContext? OfficeContext { get; private set; }

        public IntPtr LastTargetHwnd { get; set; } = IntPtr.Zero;
        public string LastTargetProcName { get; set; } = "";

        private readonly Stack<string> _undoStack = new();
        private readonly Stack<string> _redoStack = new();

        public string Tool { get; private set; } = "segment";
        public string StrokeColorHex { get; set; } = "#0F172A";
        public double StrokeWidth { get; set; } = 2.4;
        public string StrokeDash { get; set; } = "solid";
        public string FillColorHex { get; set; } = "#3B82F6";
        public double FillOpacity { get; set; } = 0.35;
        public string FillPattern { get; set; } = "solid";
        public bool ShowRedHandles { get; set; } = true;
        public bool ShowLabels { get; set; } = true;
        public bool ShowVirtualTools { get; set; } = true;
        public bool ExportWhiteBg { get; set; } = false;
        public bool IsPassthrough { get; set; } = false;

        private Point _mousePos;
        private readonly List<string> _stepPoints = new();
        private FreeStroke? _activeStroke;
        private string? _draggingPointId;
        private string? _draggingTextId;
        private string? _selectedPointId;
        private string? _selectedShapeId;

        private string _toastMsg = "";
        private readonly DispatcherTimer _toastTimer;
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public OverlaySurface()
        {
            Focusable = true;
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3000) };
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                _toastMsg = "";
                InvalidateVisual();
            };
        }

        public void ShowToast(string msg, int ms = 3000)
        {
            _toastMsg = msg;
            _toastTimer.Stop();
            _toastTimer.Interval = TimeSpan.FromMilliseconds(ms);
            _toastTimer.Start();
            InvalidateVisual();
        }

        // ================= UNDO / REDO =================
        public void SaveHistory()
        {
            _undoStack.Push(JsonSerializer.Serialize(Scene));
            _redoStack.Clear();
        }

        public void Undo()
        {
            if (MagicFills.Count > 0 && _undoStack.Count == 0)
            {
                MagicFills.RemoveAt(MagicFills.Count - 1);
                InvalidateVisual();
                ShowToast("Đã hoàn tác lớp tô Đũa thần");
                return;
            }
            if (_undoStack.Count == 0) return;
            _redoStack.Push(JsonSerializer.Serialize(Scene));
            var prev = JsonSerializer.Deserialize<SceneState>(_undoStack.Pop());
            if (prev != null) Scene = prev;
            ClearActiveSteps();
            ShowToast("Đã hoàn tác (Undo)");
        }

        public void Redo()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Push(JsonSerializer.Serialize(Scene));
            var next = JsonSerializer.Deserialize<SceneState>(_redoStack.Pop());
            if (next != null) Scene = next;
            ClearActiveSteps();
            ShowToast("Đã làm lại (Redo)");
        }

        public void ClearAll()
        {
            SaveHistory();
            Scene = new SceneState();
            MagicFills.Clear();
            OfficeContext = null;
            ClearActiveSteps();
            ShowToast("Đã xóa trắng toàn bộ lớp vẽ trong suốt");
        }

        private void ClearActiveSteps()
        {
            _stepPoints.Clear();
            _activeStroke = null;
            _draggingPointId = null;
            _draggingTextId = null;
            _selectedPointId = null;
            _selectedShapeId = null;
            InvalidateVisual();
        }

        public void SetTool(string toolName)
        {
            Tool = toolName;
            _stepPoints.Clear();
            _activeStroke = null;
            Cursor = toolName == "select" ? Cursors.Arrow : Cursors.Cross;

            string hint = toolName switch
            {
                "select" => "Chọn/Kéo: Kéo đỉnh để biến đổi hình, Delete để xóa",
                "magic_wand" => "Đũa thần (Tô 1-Click): Click vào bên trong BẤT KỲ hình kín nào trên PDF, Web, Paint, Word để tô màu!",
                "point" => "Điểm: Click bất kỳ để chấm điểm A, B, C...",
                "segment" => "Đoạn thẳng (Thước ảo): Click Điểm 1 -> Click Điểm 2",
                "dashed" => "Cạnh khuất nét đứt (3D): Click Điểm 1 -> Click Điểm 2",
                "vector" => "Vectơ: Click Điểm đầu -> Click Điểm ngọn",
                "triangle" => "Tam giác: Click lần lượt 3 đỉnh A, B, C",
                "rect" => "Hình chữ nhật: Click 2 góc đối diện",
                "circle" => "Đường tròn (Compa ảo): Click Tâm O -> Kéo chọn bán kính R",
                "midpoint" => "Trung điểm: Click trực tiếp vào 1 cạnh",
                "altitude" => "Hạ đường cao (⊥): Click 1 Đỉnh -> Click 1 Cạnh đáy",
                "fill_area" => "Tô miền đa giác: Click các đỉnh -> Chuột phải / Enter để tô",
                "fill_angle" => "Tô màu góc: Click 3 đỉnh (Cạnh 1 -> Đỉnh góc -> Cạnh 2)",
                "right_angle" => "Góc vuông: Click 3 đỉnh (Cạnh 1 -> Đỉnh vuông -> Cạnh 2)",
                "pen" => "Bút vẽ tay: Giữ chuột trái để viết/vẽ trực tiếp",
                "highlighter" => "Bút dạ quang: Giữ chuột trái để tô sáng nội dung bên dưới",
                "text" => "Chữ / Ghi chú: Click vị trí muốn chèn chữ",
                _ => $"Công cụ: {toolName}"
            };
            ShowToast(hint, 3800);
        }

        // ================= TẦNG 1: OFFICE COM (WORD / POWERPOINT / EXCEL) =================
        public void SyncFromOffice()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var ctx = OfficeComBridge.ReadFromActiveOffice(LastTargetProcName, dpi.DpiScaleX, dpi.DpiScaleY, out string? err);
            if (ctx == null)
            {
                ShowToast(err ?? "Không đọc được đối tượng từ Office!", 4000);
                return;
            }
            OfficeContext = ctx;
            InvalidateVisual();
            ShowToast($"{ctx.Summary} — Bấm [Tô hình Office] hoặc vẽ thêm rồi bấm [Chèn vào App]!", 4200);
        }

        public void FillSelectedOfficeShape()
        {
            if (OfficeContext == null || OfficeContext.Contours.Count == 0)
            {
                SyncFromOffice();
                if (OfficeContext == null || OfficeContext.Contours.Count == 0) return;
            }

            SaveHistory();
            int added = 0;
            foreach (var c in OfficeContext.Contours)
            {
                if (c.ScreenPoints.Count < 3) continue;
                var ptIds = new List<string>();
                foreach (var sp in c.ScreenPoints)
                {
                    var gp = new GeoPoint { Id = GenId("opt"), X = sp.X, Y = sp.Y, Label = "" };
                    Scene.Points.Add(gp);
                    ptIds.Add(gp.Id);
                }
                Scene.Regions.Add(new GeoRegion
                {
                    Id = GenId("reg"),
                    Type = "polygon",
                    PointIds = ptIds,
                    ColorHex = FillColorHex,
                    Opacity = FillOpacity,
                    Pattern = FillPattern
                });
                added++;
            }

            if (added == 0 && OfficeContext.ScreenBoundsDip.Width > 4)
            {
                var r = OfficeContext.ScreenBoundsDip;
                var ptIds = new List<string>();
                foreach (var sp in new[] { r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft })
                {
                    var gp = new GeoPoint { Id = GenId("opt"), X = sp.X, Y = sp.Y, Label = "" };
                    Scene.Points.Add(gp);
                    ptIds.Add(gp.Id);
                }
                Scene.Regions.Add(new GeoRegion
                {
                    Id = GenId("reg"),
                    Type = "polygon",
                    PointIds = ptIds,
                    ColorHex = FillColorHex,
                    Opacity = FillOpacity,
                    Pattern = FillPattern
                });
                added = 1;
            }

            InvalidateVisual();
            ShowToast($"Đã tô màu {added} miền từ hình đang chọn trong Office!");
        }

        // ================= TẦNG 3: CHÈN NGƯỢC VÀO MỌI ỨNG DỤNG (OFFICE COM HOẶC CTRL+V) =================
        public void InsertIntoTargetApp()
        {
            var bbox = ComputeContentBBox();
            if (bbox == null)
            {
                ShowToast("Chưa có hình tô hay nét vẽ nào để chèn!");
                return;
            }
            var bmp = RenderExportBitmap(2.5);
            if (bmp == null) return;

            // Nếu đang gắn với Office (Word / PowerPoint / Excel) -> Chèn khớp tọa độ COM
            string procUpper = LastTargetProcName.ToUpperInvariant();
            if (OfficeContext != null || procUpper is "WINWORD" or "POWERPNT" or "EXCEL")
            {
                string tempPath = Path.Combine(Path.GetTempPath(), $"ScienceWordOverlay_{DateTime.Now.Ticks}.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(tempPath)) encoder.Save(fs);

                if (OfficeComBridge.InsertPngToOffice(OfficeContext, LastTargetProcName, tempPath, bbox.Value, out string? err))
                {
                    ShowToast($"Đã chèn khớp tọa độ vào {Win32Helper.FriendlyAppName(LastTargetProcName)}!");
                    return;
                }
            }

            // Các app khác (Paint, PDF Editor, Web, Scribble, Zalo...) -> Copy PNG + Gửi Ctrl+V
            CopyBitmapToClipboardInternal(bmp);
            if (Win32Helper.SendPasteToWindow(LastTargetHwnd))
                ShowToast($"Đã tự động dán (Ctrl+V) vào {Win32Helper.FriendlyAppName(LastTargetProcName)}!");
            else
                ShowToast("Đã Copy PNG vào Clipboard — hãy bấm Ctrl+V vào ứng dụng đích!");
        }

        // ================= HÀM HÌNH HỌC =================
        private static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);
        private static double Dist(Point a, Point b) => Hypot(b.X - a.X, b.Y - a.Y);

        private static double PointToSegmentDist(Point p, Point a, Point b)
        {
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double wx = p.X - a.X, wy = p.Y - a.Y;
            double c1 = wx * vx + wy * vy;
            if (c1 <= 0) return Dist(p, a);
            double c2 = vx * vx + vy * vy;
            if (c2 <= c1) return Dist(p, b);
            double t = c1 / c2;
            return Dist(p, new Point(a.X + t * vx, a.Y + t * vy));
        }

        private static Point ProjectPointOnLine(Point p, Point a, Point b)
        {
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double denom = vx * vx + vy * vy;
            if (denom < 1e-6) return a;
            double t = ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / denom;
            return new Point(a.X + t * vx, a.Y + t * vy);
        }

        private string GenId(string prefix) => $"{prefix}_{Scene.NextId++}";

        private string NextLabel()
        {
            var used = new HashSet<string>(Scene.Points.Select(p => p.Label));
            foreach (char ch in Alphabet)
            {
                string s = ch.ToString();
                if (!used.Contains(s)) return s;
            }
            for (int i = 1; ; i++)
                foreach (char ch in Alphabet)
                {
                    string s = $"{ch}{i}";
                    if (!used.Contains(s)) return s;
                }
        }

        private GeoPoint? GetPoint(string id) => Scene.Points.FirstOrDefault(p => p.Id == id);

        private GeoPoint? FindNearestPoint(Point pos, double radius = 14.0)
        {
            GeoPoint? best = null;
            double bestD = radius;
            foreach (var p in Scene.Points)
            {
                double d = Dist(pos, new Point(p.X, p.Y));
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        private GeoShape? FindNearestSegment(Point pos, double radius = 12.0)
        {
            GeoShape? best = null;
            double bestD = radius;
            foreach (var s in Scene.Shapes)
            {
                if (s.Type is "segment" or "vector")
                {
                    var p1 = GetPoint(s.P1Id);
                    var p2 = GetPoint(s.P2Id);
                    if (p1 == null || p2 == null) continue;
                    double d = PointToSegmentDist(pos, new Point(p1.X, p1.Y), new Point(p2.X, p2.Y));
                    if (d < bestD) { bestD = d; best = s; }
                }
            }
            return best;
        }

        private GeoText? FindNearestText(Point pos)
        {
            for (int i = Scene.Texts.Count - 1; i >= 0; i--)
            {
                var t = Scene.Texts[i];
                if (Math.Abs(pos.X - t.X) < 48 && Math.Abs(pos.Y - t.Y) < 24) return t;
            }
            return null;
        }

        private GeoPoint GetOrCreatePoint(Point pos, string? customLabel = null)
        {
            var existing = FindNearestPoint(pos, 14.0);
            if (existing != null) return existing;
            var pt = new GeoPoint { Id = GenId("pt"), X = pos.X, Y = pos.Y, Label = customLabel ?? NextLabel() };
            Scene.Points.Add(pt);
            return pt;
        }

        private void AddShape(string type, string p1Id, string p2Id, string? dashOverride = null)
        {
            Scene.Shapes.Add(new GeoShape
            {
                Id = GenId("sh"),
                Type = type,
                P1Id = p1Id,
                P2Id = p2Id,
                ColorHex = StrokeColorHex,
                StrokeWidth = StrokeWidth,
                DashStyle = dashOverride ?? StrokeDash
            });
        }

        public void InsertPreset3D(string kind)
        {
            SaveHistory();
            double cx = ActualWidth > 100 ? ActualWidth / 2.0 : SystemParameters.PrimaryScreenWidth / 2.0;
            double cy = ActualHeight > 100 ? ActualHeight / 2.0 : SystemParameters.PrimaryScreenHeight / 2.0;

            if (kind == "pyramid")
            {
                var pS = GetOrCreatePoint(new Point(cx - 20, cy - 170), "S");
                var pA = GetOrCreatePoint(new Point(cx - 110, cy + 30), "A");
                var pB = GetOrCreatePoint(new Point(cx - 190, cy + 120), "B");
                var pC = GetOrCreatePoint(new Point(cx + 90, cy + 120), "C");
                var pD = GetOrCreatePoint(new Point(cx + 170, cy + 30), "D");
                foreach (var (a, b) in new[] { (pS, pA), (pA, pB), (pA, pD), (pA, pC), (pB, pD) })
                    AddShape("segment", a.Id, b.Id, "dashed");
                foreach (var (a, b) in new[] { (pS, pB), (pS, pC), (pS, pD), (pB, pC), (pC, pD) })
                    AddShape("segment", a.Id, b.Id, "solid");
                ShowToast("Đã chèn Hình chóp tứ giác S.ABCD chuẩn nét đứt SGK");
            }
            else if (kind == "cube")
            {
                double s = 170, dx = 70, dy = -55;
                var pA = GetOrCreatePoint(new Point(cx - s / 2 + dx, cy + s / 2 + dy), "A");
                var pB = GetOrCreatePoint(new Point(cx - s / 2, cy + s / 2), "B");
                var pC = GetOrCreatePoint(new Point(cx + s / 2, cy + s / 2), "C");
                var pD = GetOrCreatePoint(new Point(cx + s / 2 + dx, cy + s / 2 + dy), "D");
                var pA1 = GetOrCreatePoint(new Point(pA.X, pA.Y - s), "A'");
                var pB1 = GetOrCreatePoint(new Point(pB.X, pB.Y - s), "B'");
                var pC1 = GetOrCreatePoint(new Point(pC.X, pC.Y - s), "C'");
                var pD1 = GetOrCreatePoint(new Point(pD.X, pD.Y - s), "D'");
                foreach (var (a, b) in new[] { (pA, pB), (pA, pD), (pA, pA1) })
                    AddShape("segment", a.Id, b.Id, "dashed");
                foreach (var (a, b) in new[] { (pB, pC), (pC, pD), (pB, pB1), (pC, pC1), (pD, pD1), (pA1, pB1), (pB1, pC1), (pC1, pD1), (pD1, pA1) })
                    AddShape("segment", a.Id, b.Id, "solid");
                ShowToast("Đã chèn Hình lập phương ABCD.A'B'C'D' chuẩn SGK");
            }
            InvalidateVisual();
        }

        // ================= SỰ KIỆN CHUỘT & PHÍM =================
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (IsPassthrough) return;

            Focus();
            Point pos = e.GetPosition(this);
            _mousePos = pos;

            if (e.ChangedButton == MouseButton.Right)
            {
                if (Tool == "fill_area" && _stepPoints.Count >= 3) FinishPolygonFill();
                else { _stepPoints.Clear(); InvalidateVisual(); }
                return;
            }

            if (e.ChangedButton != MouseButton.Left) return;

            // TẦNG 2: ĐŨA THẦN FLOOD-FILL TRÊN PDF / WEB / PAINT / SCRIBBLE / WORD
            if (Tool == "magic_wand")
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                var fillCol = ParseColor(FillColorHex, 1.0);
                var res = ScreenMagicWand.CaptureAndFloodFill(pos, dpi.DpiScaleX, dpi.DpiScaleY, fillCol, FillOpacity, FillPattern, 32);
                if (res != null)
                {
                    MagicFills.Add(new MagicFillLayer { Id = GenId("mf"), Bitmap = res.Bitmap, BoundsDip = res.BoundsDip });
                    InvalidateVisual();
                    ShowToast("Đũa thần đã dò viền và tô màu vùng kín thành công!");
                }
                else
                {
                    ShowToast("Vùng quá lớn hoặc không khép kín! Hãy click vào bên trong một hình kín.");
                }
                return;
            }

            if (Tool == "select")
            {
                var pt = FindNearestPoint(pos, 15.0);
                if (pt != null)
                {
                    SaveHistory();
                    _draggingPointId = pt.Id; _selectedPointId = pt.Id; _selectedShapeId = null;
                    CaptureMouse(); InvalidateVisual(); return;
                }
                var txt = FindNearestText(pos);
                if (txt != null)
                {
                    SaveHistory();
                    _draggingTextId = txt.Id;
                    CaptureMouse(); InvalidateVisual(); return;
                }
                var sh = FindNearestSegment(pos, 12.0);
                if (sh != null)
                {
                    _selectedShapeId = sh.Id; _selectedPointId = null;
                    InvalidateVisual(); return;
                }
                _selectedPointId = null; _selectedShapeId = null;
                InvalidateVisual(); return;
            }

            if (Tool is "pen" or "highlighter")
            {
                SaveHistory();
                bool isHl = Tool == "highlighter";
                _activeStroke = new FreeStroke
                {
                    Id = GenId("strk"),
                    ColorHex = isHl ? FillColorHex : StrokeColorHex,
                    Width = isHl ? 20.0 : Math.Max(2.0, StrokeWidth),
                    Opacity = isHl ? 0.35 : 1.0,
                    IsHighlighter = isHl
                };
                _activeStroke.Points.Add(new[] { pos.X, pos.Y });
                Scene.Strokes.Add(_activeStroke);
                CaptureMouse(); InvalidateVisual(); return;
            }

            if (Tool == "text")
            {
                var dlg = new QuickInputWindow("Chèn Chữ / Công thức Toán học", "Nhập nội dung:");
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResultText))
                {
                    SaveHistory();
                    Scene.Texts.Add(new GeoText { Id = GenId("txt"), X = pos.X, Y = pos.Y, Text = dlg.ResultText.Trim(), ColorHex = StrokeColorHex, FontSize = 22 });
                    InvalidateVisual();
                }
                return;
            }

            if (Tool == "point") { SaveHistory(); GetOrCreatePoint(pos); InvalidateVisual(); return; }

            if (Tool == "midpoint")
            {
                var sh = FindNearestSegment(pos, 16.0);
                if (sh != null)
                {
                    var p1 = GetPoint(sh.P1Id); var p2 = GetPoint(sh.P2Id);
                    if (p1 != null && p2 != null)
                    {
                        SaveHistory();
                        var mid = GetOrCreatePoint(new Point((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0));
                        ShowToast($"Đã dựng trung điểm {mid.Label}");
                        InvalidateVisual();
                    }
                }
                else ShowToast("Hãy click trực tiếp vào một đoạn thẳng để lấy trung điểm");
                return;
            }

            if (Tool == "altitude")
            {
                if (_stepPoints.Count == 0)
                {
                    var pt = FindNearestPoint(pos, 16.0);
                    if (pt != null) { _stepPoints.Add(pt.Id); ShowToast($"Đã chọn đỉnh {pt.Label}. Click vào cạnh đáy!"); InvalidateVisual(); }
                    else ShowToast("Bước 1: Hãy click chọn một Đỉnh có sẵn");
                }
                else
                {
                    var sh = FindNearestSegment(pos, 16.0);
                    if (sh != null)
                    {
                        var v = GetPoint(_stepPoints[0]); var p1 = GetPoint(sh.P1Id); var p2 = GetPoint(sh.P2Id);
                        if (v != null && p1 != null && p2 != null)
                        {
                            SaveHistory();
                            var footPos = ProjectPointOnLine(new Point(v.X, v.Y), new Point(p1.X, p1.Y), new Point(p2.X, p2.Y));
                            var foot = GetOrCreatePoint(footPos, Scene.Points.Any(x => x.Label == "H") ? null : "H");
                            AddShape("segment", v.Id, foot.Id);
                            Scene.Markers.Add(new GeoMarker
                            {
                                Id = GenId("mk"), Type = "right_angle", P1Id = v.Id, VertexId = foot.Id,
                                P2Id = Dist(footPos, new Point(p1.X, p1.Y)) > 8 ? p1.Id : p2.Id, ColorHex = StrokeColorHex
                            });
                            _stepPoints.Clear();
                            ShowToast($"Đã hạ đường cao {v.Label}{foot.Label} kèm góc vuông!");
                            InvalidateVisual();
                        }
                    }
                    else ShowToast("Bước 2: Hãy click vào một Cạnh đáy");
                }
                return;
            }

            if (Tool is "segment" or "dashed" or "vector" or "circle" or "rect")
            {
                if (_stepPoints.Count == 0)
                {
                    SaveHistory();
                    _stepPoints.Add(GetOrCreatePoint(pos).Id);
                    InvalidateVisual();
                }
                else
                {
                    var p1 = GetPoint(_stepPoints[0]);
                    if (Tool == "rect" && p1 != null)
                    {
                        var p2 = GetOrCreatePoint(new Point(pos.X, p1.Y));
                        var p3 = GetOrCreatePoint(pos);
                        var p4 = GetOrCreatePoint(new Point(p1.X, pos.Y));
                        AddShape("segment", p1.Id, p2.Id); AddShape("segment", p2.Id, p3.Id);
                        AddShape("segment", p3.Id, p4.Id); AddShape("segment", p4.Id, p1.Id);
                    }
                    else if (p1 != null)
                    {
                        var p2 = GetOrCreatePoint(pos);
                        if (p2.Id != p1.Id) AddShape(Tool == "dashed" ? "segment" : Tool, p1.Id, p2.Id, Tool == "dashed" ? "dashed" : null);
                    }
                    _stepPoints.Clear();
                    InvalidateVisual();
                }
                return;
            }

            if (Tool == "triangle")
            {
                if (_stepPoints.Count == 0) SaveHistory();
                _stepPoints.Add(GetOrCreatePoint(pos).Id);
                if (_stepPoints.Count == 3)
                {
                    AddShape("segment", _stepPoints[0], _stepPoints[1]);
                    AddShape("segment", _stepPoints[1], _stepPoints[2]);
                    AddShape("segment", _stepPoints[2], _stepPoints[0]);
                    _stepPoints.Clear();
                }
                InvalidateVisual();
                return;
            }

            if (Tool == "fill_area")
            {
                var p = GetOrCreatePoint(pos);
                if (_stepPoints.Count >= 3 && p.Id == _stepPoints[0]) { FinishPolygonFill(); return; }
                if (!_stepPoints.Contains(p.Id))
                {
                    _stepPoints.Add(p.Id);
                    ShowToast($"Đã chọn {_stepPoints.Count} đỉnh. Click đỉnh đầu hoặc Chuột phải / Enter để tô!");
                }
                InvalidateVisual();
                return;
            }

            if (Tool is "fill_angle" or "right_angle")
            {
                _stepPoints.Add(GetOrCreatePoint(pos).Id);
                if (_stepPoints.Count == 3)
                {
                    SaveHistory();
                    if (Tool == "right_angle")
                        Scene.Markers.Add(new GeoMarker { Id = GenId("mk"), Type = "right_angle", P1Id = _stepPoints[0], VertexId = _stepPoints[1], P2Id = _stepPoints[2], ColorHex = StrokeColorHex });
                    else
                        Scene.Regions.Add(new GeoRegion { Id = GenId("reg"), Type = "angle", PointIds = new List<string>(_stepPoints), ColorHex = FillColorHex, Opacity = FillOpacity, Pattern = FillPattern });
                    _stepPoints.Clear();
                }
                InvalidateVisual();
            }
        }

        public void FinishPolygonFill()
        {
            if (_stepPoints.Count < 3) return;
            SaveHistory();
            Scene.Regions.Add(new GeoRegion { Id = GenId("reg"), Type = "polygon", PointIds = new List<string>(_stepPoints), ColorHex = FillColorHex, Opacity = FillOpacity, Pattern = FillPattern });
            _stepPoints.Clear();
            ShowToast("Đã tô màu miền đa giác!");
            InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (IsPassthrough) return;
            Point pos = e.GetPosition(this);
            _mousePos = pos;

            if (_draggingPointId != null) { var pt = GetPoint(_draggingPointId); if (pt != null) { pt.X = pos.X; pt.Y = pos.Y; } InvalidateVisual(); return; }
            if (_draggingTextId != null) { var txt = Scene.Texts.FirstOrDefault(t => t.Id == _draggingTextId); if (txt != null) { txt.X = pos.X; txt.Y = pos.Y; } InvalidateVisual(); return; }
            if (_activeStroke != null)
            {
                var last = _activeStroke.Points[^1];
                if (Hypot(pos.X - last[0], pos.Y - last[1]) >= 2.0) _activeStroke.Points.Add(new[] { pos.X, pos.Y });
                InvalidateVisual();
                return;
            }
            if (_stepPoints.Count > 0) InvalidateVisual();
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            _draggingPointId = null; _draggingTextId = null; _activeStroke = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        public void HandleKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { _stepPoints.Clear(); SetTool("select"); }
            else if (e.Key == Key.Enter && Tool == "fill_area" && _stepPoints.Count >= 3) FinishPolygonFill();
            else if (e.Key is Key.Delete or Key.Back) DeleteSelected();
            else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Undo();
            else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Redo();
            else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0) CopyToClipboard();
        }

        public void DeleteSelected()
        {
            if (_selectedShapeId != null)
            {
                SaveHistory();
                Scene.Shapes.RemoveAll(s => s.Id == _selectedShapeId);
                _selectedShapeId = null;
                InvalidateVisual();
                ShowToast("Đã xóa đường đang chọn");
            }
            else if (_selectedPointId != null)
            {
                SaveHistory();
                string pid = _selectedPointId;
                Scene.Points.RemoveAll(p => p.Id == pid);
                Scene.Shapes.RemoveAll(s => s.P1Id == pid || s.P2Id == pid);
                Scene.Regions.RemoveAll(r => r.PointIds.Contains(pid));
                Scene.Markers.RemoveAll(m => m.P1Id == pid || m.VertexId == pid || m.P2Id == pid);
                _selectedPointId = null;
                InvalidateVisual();
                ShowToast("Đã xóa điểm và các cạnh liên quan");
            }
        }

        // ================= RENDERING =================
        private static Color ParseColor(string hex, double opacity = 1.0)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                c.A = (byte)Math.Clamp(opacity * 255.0, 0, 255);
                return c;
            }
            catch { return Color.FromArgb((byte)(opacity * 255), 15, 23, 42); }
        }

        private static Brush MakeRegionBrush(string hex, double opacity, string pattern)
        {
            Color c = ParseColor(hex, opacity);
            if (pattern == "solid") return new SolidColorBrush(c);

            var group = new DrawingGroup();
            if (pattern == "hatch")
                group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(ParseColor(hex, Math.Min(1.0, opacity + 0.35))), 1.5), new LineGeometry(new Point(0, 10), new Point(10, 0))));
            else if (pattern == "cross")
            {
                var pen = new Pen(new SolidColorBrush(ParseColor(hex, Math.Min(1.0, opacity + 0.35))), 1.3);
                group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 10), new Point(10, 0))));
                group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(10, 10))));
            }
            else if (pattern == "dots")
                group.Children.Add(new GeometryDrawing(new SolidColorBrush(ParseColor(hex, Math.Min(1.0, opacity + 0.40))), null, new EllipseGeometry(new Point(5, 5), 1.8, 1.8)));

            return new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 10, 10), ViewportUnits = BrushMappingMode.Absolute };
        }

        private static Pen MakePen(string hex, double width, string dash, bool highlight = false)
        {
            var pen = new Pen(new SolidColorBrush(highlight ? ParseColor("#2563EB") : ParseColor(hex)), width + (highlight ? 1.4 : 0.0))
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
            };
            if (dash == "dashed") pen.DashStyle = new DashStyle(new double[] { 5.5, 3.8 }, 0);
            else if (dash == "dotted") pen.DashStyle = DashStyles.Dot;
            return pen;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (!IsPassthrough)
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), null, new Rect(0, 0, ActualWidth, ActualHeight));

            RenderScene(dc, cleanExport: false);

            if (!string.IsNullOrEmpty(_toastMsg))
            {
                var ft = new FormattedText(_toastMsg, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    13, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                double tw = ft.Width + 34, th = 34;
                var rect = new Rect((ActualWidth - tw) / 2.0, ActualHeight - 64, tw, th);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(230, 15, 23, 42)),
                    new Pen(new SolidColorBrush(Color.FromArgb(190, 96, 165, 250)), 1.2), rect, 17, 17);
                dc.DrawText(ft, new Point(rect.X + 17, rect.Y + (th - ft.Height) / 2.0));
            }
        }

        private void RenderScene(DrawingContext dc, bool cleanExport)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            // 0. Các lớp tô Đũa thần (Magic Wand Flood-Fill)
            foreach (var mf in MagicFills)
                dc.DrawImage(mf.Bitmap, mf.BoundsDip);

            // 0.5 Khung nhận diện đối tượng Office (chỉ hiện khi không export)
            if (!cleanExport && OfficeContext != null && OfficeContext.ScreenBoundsDip.Width > 2)
                dc.DrawRectangle(null, new Pen(new SolidColorBrush(ParseColor("#10B981", 0.85)), 1.6) { DashStyle = DashStyles.Dash }, OfficeContext.ScreenBoundsDip);

            // 1. Miền tô màu & Tô góc
            foreach (var reg in Scene.Regions)
            {
                var brush = MakeRegionBrush(reg.ColorHex, reg.Opacity, reg.Pattern);
                var borderPen = new Pen(new SolidColorBrush(ParseColor(reg.ColorHex, 0.7)), 1.2);
                if (reg.Type == "polygon")
                {
                    var pts = reg.PointIds.Select(GetPoint).Where(p => p != null).Select(p => new Point(p!.X, p.Y)).ToList();
                    if (pts.Count >= 3)
                    {
                        var geo = new StreamGeometry();
                        using (var ctx = geo.Open()) { ctx.BeginFigure(pts[0], true, true); ctx.PolyLineTo(pts.Skip(1).ToList(), true, false); }
                        geo.Freeze();
                        dc.DrawGeometry(brush, borderPen, geo);
                    }
                }
                else if (reg.Type == "angle" && reg.PointIds.Count == 3)
                {
                    var p1 = GetPoint(reg.PointIds[0]); var v = GetPoint(reg.PointIds[1]); var p2 = GetPoint(reg.PointIds[2]);
                    if (p1 != null && v != null && p2 != null)
                    {
                        double r = 36.0, a1 = Math.Atan2(p1.Y - v.Y, p1.X - v.X), a2 = Math.Atan2(p2.Y - v.Y, p2.X - v.X);
                        double diff = a2 - a1;
                        while (diff <= -Math.PI) diff += 2 * Math.PI;
                        while (diff > Math.PI) diff -= 2 * Math.PI;
                        Point s1 = new(v.X + r * Math.Cos(a1), v.Y + r * Math.Sin(a1));
                        Point s2 = new(v.X + r * Math.Cos(a1 + diff), v.Y + r * Math.Sin(a1 + diff));
                        var geo = new StreamGeometry();
                        using (var ctx = geo.Open())
                        {
                            ctx.BeginFigure(new Point(v.X, v.Y), true, true);
                            ctx.LineTo(s1, true, false);
                            ctx.ArcTo(s2, new Size(r, r), 0, Math.Abs(diff) > Math.PI, diff >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
                        }
                        geo.Freeze();
                        dc.DrawGeometry(brush, borderPen, geo);
                    }
                }
            }

            // 2. Ký hiệu góc vuông
            foreach (var mk in Scene.Markers)
            {
                var p1 = GetPoint(mk.P1Id); var v = GetPoint(mk.VertexId); var p2 = GetPoint(mk.P2Id);
                if (p1 == null || v == null || p2 == null) continue;
                double u1x = p1.X - v.X, u1y = p1.Y - v.Y, u2x = p2.X - v.X, u2y = p2.Y - v.Y;
                double l1 = Hypot(u1x, u1y), l2 = Hypot(u2x, u2y);
                if (l1 < 1 || l2 < 1) continue;
                double sz = 14.0;
                Point e1 = new(v.X + u1x / l1 * sz, v.Y + u1y / l1 * sz);
                Point e2 = new(v.X + u2x / l2 * sz, v.Y + u2y / l2 * sz);
                Point c = new(e1.X + u2x / l2 * sz, e1.Y + u2y / l2 * sz);
                var pen = new Pen(new SolidColorBrush(ParseColor(mk.ColorHex)), 1.5);
                dc.DrawLine(pen, e1, c); dc.DrawLine(pen, c, e2);
            }

            // 3. Các hình học chính
            foreach (var sh in Scene.Shapes)
            {
                var p1 = GetPoint(sh.P1Id); var p2 = GetPoint(sh.P2Id);
                if (p1 == null || p2 == null) continue;
                var pen = MakePen(sh.ColorHex, sh.StrokeWidth, sh.DashStyle, !cleanExport && sh.Id == _selectedShapeId);
                Point pt1 = new(p1.X, p1.Y), pt2 = new(p2.X, p2.Y);

                if (sh.Type == "circle") dc.DrawEllipse(null, pen, pt1, Dist(pt1, pt2), Dist(pt1, pt2));
                else if (sh.Type == "vector")
                {
                    dc.DrawLine(pen, pt1, pt2);
                    double ang = Math.Atan2(pt2.Y - pt1.Y, pt2.X - pt1.X), ah = 13.0;
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        ctx.BeginFigure(pt2, true, true);
                        ctx.LineTo(new Point(pt2.X - ah * Math.Cos(ang - 0.42), pt2.Y - ah * Math.Sin(ang - 0.42)), true, false);
                        ctx.LineTo(new Point(pt2.X - ah * Math.Cos(ang + 0.42), pt2.Y - ah * Math.Sin(ang + 0.42)), true, false);
                    }
                    geo.Freeze();
                    dc.DrawGeometry(new SolidColorBrush(ParseColor(sh.ColorHex)), null, geo);
                }
                else dc.DrawLine(pen, pt1, pt2);
            }

            // 4. Nét vẽ tay & Dạ quang
            foreach (var st in Scene.Strokes)
            {
                if (st.Points.Count < 2) continue;
                var pen = new Pen(new SolidColorBrush(ParseColor(st.ColorHex, st.Opacity)), st.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(st.Points[0][0], st.Points[0][1]), false, false);
                    for (int i = 1; i < st.Points.Count - 1; i++)
                        ctx.QuadraticBezierTo(new Point(st.Points[i][0], st.Points[i][1]), new Point((st.Points[i][0] + st.Points[i + 1][0]) / 2.0, (st.Points[i][1] + st.Points[i + 1][1]) / 2.0), true, false);
                    ctx.LineTo(new Point(st.Points[^1][0], st.Points[^1][1]), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
            }

            // 5. Chữ
            var mathTypeface = new Typeface(new FontFamily("Times New Roman"), FontStyles.Italic, FontWeights.Bold, FontStretches.Normal);
            foreach (var t in Scene.Texts)
            {
                var ft = new FormattedText(t.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, mathTypeface, t.FontSize, new SolidColorBrush(ParseColor(t.ColorHex)), dpi);
                dc.DrawText(ft, new Point(t.X, t.Y - ft.Height / 2));
            }

            // 6. Điểm & Tên đỉnh
            var labelTypeface = new Typeface(new FontFamily("Times New Roman"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
            foreach (var p in Scene.Points)
            {
                if (string.IsNullOrEmpty(p.Label) && p.Id.StartsWith("opt_")) continue;
                Point pt = new(p.X, p.Y);
                if (!cleanExport && ShowRedHandles)
                {
                    bool isSel = p.Id == _selectedPointId;
                    dc.DrawEllipse(new SolidColorBrush(ParseColor(isSel ? "#2563EB" : "#DC2626")), new Pen(Brushes.White, 1.5), pt, isSel ? 5.8 : 4.2, isSel ? 5.8 : 4.2);
                }
                else dc.DrawEllipse(new SolidColorBrush(ParseColor("#0F172A")), null, pt, 2.5, 2.5);

                if (ShowLabels && !string.IsNullOrEmpty(p.Label))
                {
                    var ft = new FormattedText(p.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, labelTypeface, 20, new SolidColorBrush(ParseColor("#0F172A")), dpi);
                    dc.DrawText(ft, new Point(p.X + p.Lx, p.Y + p.Ly));
                }
            }

            // 7. Preview & Thước / Compa ảo
            if (!cleanExport && _stepPoints.Count > 0) DrawLivePreviewAndTools(dc, dpi);
        }

        private void DrawLivePreviewAndTools(DrawingContext dc, double dpi)
        {
            var p1 = GetPoint(_stepPoints[0]);
            if (p1 == null) return;
            Point pt1 = new(p1.X, p1.Y), pt2 = _mousePos;
            var prevPen = new Pen(new SolidColorBrush(ParseColor("#2563EB")), 1.8) { DashStyle = DashStyles.Dash };

            if (Tool is "segment" or "dashed" or "vector")
            {
                dc.DrawLine(prevPen, pt1, pt2);
                if (ShowVirtualTools) DrawVirtualRuler(dc, pt1, pt2, dpi);
            }
            else if (Tool == "circle")
            {
                dc.DrawEllipse(null, prevPen, pt1, Dist(pt1, pt2), Dist(pt1, pt2));
                if (ShowVirtualTools) DrawVirtualCompass(dc, pt1, pt2, dpi);
            }
            else if (Tool == "rect") dc.DrawRectangle(null, prevPen, new Rect(pt1, pt2));
            else if (Tool is "triangle" or "fill_area")
            {
                var pts = _stepPoints.Select(GetPoint).Where(p => p != null).Select(p => new Point(p!.X, p.Y)).ToList();
                pts.Add(pt2);
                var geo = new StreamGeometry();
                using (var ctx = geo.Open()) { ctx.BeginFigure(pts[0], Tool == "fill_area", Tool == "fill_area"); ctx.PolyLineTo(pts.Skip(1).ToList(), true, false); }
                geo.Freeze();
                dc.DrawGeometry((Tool == "fill_area" && pts.Count >= 3) ? MakeRegionBrush(FillColorHex, FillOpacity, FillPattern) : null, prevPen, geo);
            }
        }

        private static void DrawVirtualRuler(DrawingContext dc, Point a, Point b, double dpi)
        {
            double len = Dist(a, b);
            if (len < 15) return;
            double angle = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI;
            double rulerLen = Math.Max(len + 45, 230), rulerH = 34;

            dc.PushTransform(new TranslateTransform(a.X, a.Y));
            dc.PushTransform(new RotateTransform(angle));
            var grad = new LinearGradientBrush(Color.FromArgb(215, 254, 243, 199), Color.FromArgb(180, 251, 191, 36), 90);
            dc.DrawRoundedRectangle(grad, new Pen(new SolidColorBrush(Color.FromArgb(215, 180, 83, 9)), 1.3), new Rect(-15, 2, rulerLen, rulerH), 4, 4);

            var tickPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 120, 53, 15)), 1.0);
            var numTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            double cmPx = 38.0, mmPx = cmPx / 10.0;
            for (int i = 0; i <= (int)((rulerLen - 20) / mmPx); i++)
            {
                double x = i * mmPx;
                if (i % 10 == 0)
                {
                    dc.DrawLine(tickPen, new Point(x, 2), new Point(x, 15));
                    dc.DrawText(new FormattedText((i / 10).ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, numTypeface, 9.5, new SolidColorBrush(Color.FromRgb(120, 53, 15)), dpi), new Point(x - 3, 17));
                }
                else if (i % 5 == 0) dc.DrawLine(tickPen, new Point(x, 2), new Point(x, 11));
                else dc.DrawLine(tickPen, new Point(x, 2), new Point(x, 7));
            }
            dc.DrawText(new FormattedText($"{len / cmPx:0.0} cm", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, numTypeface, 12, new SolidColorBrush(Color.FromRgb(30, 58, 138)), dpi), new Point(len / 2 - 20, -20));
            dc.Pop(); dc.Pop();
        }

        private static void DrawVirtualCompass(DrawingContext dc, Point center, Point tip, double dpi)
        {
            double r = Dist(center, tip);
            if (r < 10) return;
            Point mid = new((center.X + tip.X) / 2.0, (center.Y + tip.Y) / 2.0);
            double ux = (tip.X - center.X) / r, uy = (tip.Y - center.Y) / r;
            Point pivot = new(mid.X + (-uy) * Math.Clamp(r * 1.15, 85.0, 185.0), mid.Y + ux * Math.Clamp(r * 1.15, 85.0, 185.0));

            var legPen = new Pen(new SolidColorBrush(Color.FromArgb(225, 51, 65, 85)), 4.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(legPen, pivot, center); dc.DrawLine(legPen, pivot, tip);
            dc.DrawEllipse(new SolidColorBrush(ParseColor("#F59E0B")), new Pen(new SolidColorBrush(ParseColor("#78350F")), 1.8), pivot, 7, 7);
            dc.DrawEllipse(new SolidColorBrush(ParseColor("#DC2626")), null, center, 3.8, 3.8);
            dc.DrawEllipse(new SolidColorBrush(ParseColor("#2563EB")), null, tip, 3.8, 3.8);
            dc.DrawText(new FormattedText($"R = {r / 38.0:0.0} cm", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 12, new SolidColorBrush(ParseColor("#BE123C")), dpi), new Point(mid.X + 8, mid.Y - 18));
        }

        // ================= XUẤT ẢNH =================
        private Rect? ComputeContentBBox()
        {
            var xs = new List<double>(); var ys = new List<double>();
            foreach (var mf in MagicFills) { xs.Add(mf.BoundsDip.Left); xs.Add(mf.BoundsDip.Right); ys.Add(mf.BoundsDip.Top); ys.Add(mf.BoundsDip.Bottom); }
            foreach (var p in Scene.Points) { xs.Add(p.X); ys.Add(p.Y); }
            foreach (var sh in Scene.Shapes.Where(s => s.Type == "circle"))
            {
                var p1 = GetPoint(sh.P1Id); var p2 = GetPoint(sh.P2Id);
                if (p1 != null && p2 != null) { double r = Dist(new Point(p1.X, p1.Y), new Point(p2.X, p2.Y)); xs.Add(p1.X - r); xs.Add(p1.X + r); ys.Add(p1.Y - r); ys.Add(p1.Y + r); }
            }
            foreach (var st in Scene.Strokes) foreach (var pt in st.Points) { xs.Add(pt[0]); ys.Add(pt[1]); }
            foreach (var t in Scene.Texts) { xs.Add(t.X); xs.Add(t.X + t.Text.Length * 14); ys.Add(t.Y - 24); ys.Add(t.Y + 14); }
            if (xs.Count == 0) return null;
            double pad = 28;
            return new Rect(xs.Min() - pad, ys.Min() - pad, (xs.Max() - xs.Min()) + 2 * pad, (ys.Max() - ys.Min()) + 2 * pad);
        }

        public RenderTargetBitmap? RenderExportBitmap(double scale = 2.0)
        {
            var bbox = ComputeContentBBox();
            if (bbox == null) return null;
            int w = Math.Max(16, (int)Math.Ceiling(bbox.Value.Width * scale));
            int h = Math.Max(16, (int)Math.Ceiling(bbox.Value.Height * scale));
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                if (ExportWhiteBg) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.PushTransform(new TranslateTransform(-bbox.Value.Left, -bbox.Value.Top));
                RenderScene(dc, cleanExport: true);
                dc.Pop(); dc.Pop();
            }
            var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(dv);
            return rtb;
        }

        private static void CopyBitmapToClipboardInternal(RenderTargetBitmap bmp)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;
            var dataObj = new DataObject();
            dataObj.SetData("PNG", ms, false);
            dataObj.SetImage(bmp);
            Clipboard.SetDataObject(dataObj, true);
        }

        public void CopyToClipboard()
        {
            var bmp = RenderExportBitmap(2.0);
            if (bmp == null) { ShowToast("Chưa có hình vẽ nào để Copy!"); return; }
            CopyBitmapToClipboardInternal(bmp);
            ShowToast("Đã Copy ảnh PNG nét cao vào Clipboard — nhấn Ctrl+V vào ứng dụng đích!");
        }

        public void SavePng()
        {
            var bmp = RenderExportBitmap(2.5);
            if (bmp == null) { ShowToast("Chưa có hình vẽ nào để lưu!"); return; }
            var dlg = new SaveFileDialog { Title = "Lưu ảnh PNG", FileName = "HinhVe_ScienceWord.png", Filter = "PNG Image (*.png)|*.png" };
            if (dlg.ShowDialog() == true)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(dlg.FileName);
                encoder.Save(fs);
                ShowToast($"Đã lưu ảnh PNG: {Path.GetFileName(dlg.FileName)}");
            }
        }
    }
}
