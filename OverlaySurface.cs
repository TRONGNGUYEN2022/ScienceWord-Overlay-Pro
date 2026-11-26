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
        public event Action<string>? MetricsUpdated;

        public SceneState Scene { get; private set; } = new();
        public List<MagicFillLayer> MagicFills { get; } = new();
        public OfficeSelectionContext? OfficeContext { get; private set; }

        // Trạng thái Khối tròn xoay 3D & Diện tích tích phân
        public RevolutionScene? RevScene { get; set; }
        public MathRenderMode RevMode { get; set; } = MathRenderMode.Area2D;
        public double RevSweepDeg { get; set; } = 360.0;
        public double RevXCompression { get; set; } = 0.28;
        public bool RevShowCaps { get; set; } = true;
        private int _draggingRevHandle = 0; // 1 = a, 2 = b

        // Trạng thái Chọn vùng kiểu Snipping Tool (Chữ nhật & Tự do Lasso)
        public Rect? ActiveSnipRect { get; private set; }
        public List<Point> ActiveSnipLasso { get; } = new();
        private bool _isDraggingSnip = false;
        private Point _snipStartPt;

        public IntPtr LastTargetHwnd { get; set; } = IntPtr.Zero;
        public string LastTargetProcName { get; set; } = "";

        private readonly Stack<string> _undoStack = new();
        private readonly Stack<string> _redoStack = new();

        public string Tool { get; private set; } = "segment";
        public string StrokeColorHex { get; set; } = "#0F172A";
        public double StrokeWidth { get; set; } = 2.4;
        public string StrokeDash { get; set; } = "solid";
        public string PenTipStyle { get; set; } = "solid"; // solid | pencil | brush | dash | highlighter
        public string FillColorHex { get; set; } = "#3B82F6";
        public double FillOpacity { get; set; } = 0.40;
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
        // Chọn & Di chuyển toàn bộ nhóm đối tượng (Marquee / HCN / Tự do Lasso)
        private readonly HashSet<string> _selPointIds = new();
        private readonly HashSet<string> _selStrokeIds = new();
        private readonly HashSet<string> _selTextIds = new();
        private readonly HashSet<string> _selMagicIds = new();
        private bool _isDraggingGroup = false;
        private Point _groupLastPos;
        private Rect? _marqueeRect;
        private Point _marqueeStart;

        private string _toastMsg = "";
        private readonly DispatcherTimer _toastTimer;
        // Mô phỏng diễn hoạt Thước thẳng + Bút vẽ lần lượt từng cạnh cho Hình 3D (Chóp, Lập phương)
        private sealed class AnimEdgeTask
        {
            public string P1Id { get; set; } = "";
            public string P2Id { get; set; } = "";
            public string Dash { get; set; } = "solid";
        }
        private readonly Queue<AnimEdgeTask> _animEdgeQueue = new();
        private AnimEdgeTask? _currentAnimEdge;
        private double _animEdgeProgress = 0.0;
        private readonly DispatcherTimer _animDrawTimer;

        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public OverlaySurface()
        {
            Focusable = true;
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3200) };
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                _toastMsg = "";
                InvalidateVisual();
            };

            _animDrawTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _animDrawTimer.Tick += OnAnimDrawTick;
        }

        private void OnAnimDrawTick(object? sender, EventArgs e)
        {
            if (_currentAnimEdge == null)
            {
                if (_animEdgeQueue.Count == 0)
                {
                    _animDrawTimer.Stop();
                    InvalidateVisual();
                    return;
                }
                _currentAnimEdge = _animEdgeQueue.Dequeue();
                _animEdgeProgress = 0.0;
            }

            _animEdgeProgress += 0.14; // ~7 frames (~110ms) per edge for snappy, smooth ruler+pen motion
            if (_animEdgeProgress >= 1.0)
            {
                AddShape("segment", _currentAnimEdge.P1Id, _currentAnimEdge.P2Id, _currentAnimEdge.Dash);
                _currentAnimEdge = null;
                _animEdgeProgress = 0.0;
                if (_animEdgeQueue.Count == 0) _animDrawTimer.Stop();
            }
            InvalidateVisual();
        }

        public void ShowToast(string msg, int ms = 3200)
        {
            _toastMsg = msg;
            _toastTimer.Stop();
            _toastTimer.Interval = TimeSpan.FromMilliseconds(ms);
            _toastTimer.Start();
            InvalidateVisual();
        }

        public void NotifyMetrics()
        {
            if (RevScene != null && RevScene.IsValid)
            {
                var (s, v, vPi) = RevScene.ComputeMetrics();
                MetricsUpdated?.Invoke($"S = {s:0.00} cm²  |  V = {vPi:0.00}π ≈ {v:0.00} cm³");
            }
            else
            {
                MetricsUpdated?.Invoke("Chọn 1 Trục + 1 Đường cong trong Word rồi bấm [Đọc Office] để Quay 3D & tính V");
            }
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
            RevScene = null;
            ActiveSnipRect = null;
            ActiveSnipLasso.Clear();
            ClearActiveSteps();
            NotifyMetrics();
            ShowToast("Đã xóa trắng toàn bộ lớp vẽ trong suốt");
        }


        public bool TryRestoreFromOfficeDoubleClick(string procName)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var restored = OfficeComBridge.TryReadSelectedOverlayImage(procName, dpi.DpiScaleX, dpi.DpiScaleY);
            if (restored == null) return false;

            SaveHistory();
            Scene = restored.Value.scene;
            OfficeContext = restored.Value.ctx;
            ClearGroupSelection();
            InvalidateVisual();
            ShowToast("Đã mở lại hình để sửa / vẽ thêm! Sửa xong bấm [Chèn App] để cập nhật đè vào file.", 4500);
            return true;
        }

        public bool TryRestoreFromScienceWordDoubleClick(IntPtr swHwnd, int clickPxX, int clickPxY)
        {
            // Gửi Ctrl+C để lấy ảnh vừa được nháy đúp chọn trong ScienceWord rồi đọc mã nhận diện ẩn (LSB Stego)
            try
            {
                Clipboard.Clear();
                Win32Helper.SendCopyToWindow(swHwnd);
                System.Threading.Thread.Sleep(150);

                if (Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img != null && SceneHistoryStore.TryDecodeStegoId(img, out int sceneId))
                    {
                        var entry = SceneHistoryStore.GetById(sceneId);
                        if (entry != null)
                        {
                            var dpi = VisualTreeHelper.GetDpi(this);
                            double clickDipX = clickPxX / dpi.DpiScaleX;
                            double clickDipY = clickPxY / dpi.DpiScaleY;
                            double imgDipW = img.PixelWidth / dpi.DpiScaleX;
                            double imgDipH = img.PixelHeight / dpi.DpiScaleY;
                            double sx = entry.BoxW > 4 ? imgDipW / entry.BoxW : 1.0;
                            double sy = entry.BoxH > 4 ? imgDipH / entry.BoxH : 1.0;

                            var topLeft = new Point(Math.Max(20, clickDipX - imgDipW * 0.5), Math.Max(90, clickDipY - imgDipH * 0.5));
                            SaveHistory();
                            Scene = SceneHistoryStore.MaterializeSceneAt(entry, topLeft, sx, sy);
                            OfficeContext = null;
                            ClearGroupSelection();
                            InvalidateVisual();
                            ShowToast($"Đã mở lại hình #{entry.Id} từ ScienceWord! Sửa xong bấm [Chèn App] để thay thế hình đang chọn.", 4800);
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public void RestoreFromHistoryEntry(SavedSceneEntry entry)
        {
            SaveHistory();
            var targetPos = new Point(Math.Max(40, entry.OrigLeft), Math.Max(110, entry.OrigTop));
            Scene = SceneHistoryStore.MaterializeSceneAt(entry, targetPos, 1.0, 1.0);
            OfficeContext = null;
            ClearGroupSelection();
            InvalidateVisual();
            ShowToast($"Đã mở lại {entry.Label}! Chỉnh sửa xong bấm [Chèn App] để dán lại.", 4200);
        }

        private void ClearGroupSelection()
        {
            _selPointIds.Clear(); _selStrokeIds.Clear(); _selTextIds.Clear(); _selMagicIds.Clear();
            _isDraggingGroup = false;
            _marqueeRect = null;
        }

        private bool HasGroupSelection() =>
            _selPointIds.Count > 0 || _selStrokeIds.Count > 0 || _selTextIds.Count > 0 || _selMagicIds.Count > 0;

        private Rect? GetGroupSelectionBounds()
        {
            var xs = new List<double>(); var ys = new List<double>();
            foreach (var pid in _selPointIds) { var p = GetPoint(pid); if (p != null) { xs.Add(p.X); ys.Add(p.Y); } }
            foreach (var sid in _selStrokeIds)
            {
                var st = Scene.Strokes.FirstOrDefault(x => x.Id == sid);
                if (st != null) foreach (var pt in st.Points) { xs.Add(pt[0]); ys.Add(pt[1]); }
            }
            foreach (var tid in _selTextIds)
            {
                var t = Scene.Texts.FirstOrDefault(x => x.Id == tid);
                if (t != null) { xs.Add(t.X); xs.Add(t.X + Math.Max(20, t.Text.Length * 11)); ys.Add(t.Y - 16); ys.Add(t.Y + 16); }
            }
            foreach (var mid in _selMagicIds)
            {
                var mf = MagicFills.FirstOrDefault(x => x.Id == mid);
                if (mf != null) { xs.Add(mf.BoundsDip.Left); xs.Add(mf.BoundsDip.Right); ys.Add(mf.BoundsDip.Top); ys.Add(mf.BoundsDip.Bottom); }
            }
            if (xs.Count == 0) return null;
            return new Rect(xs.Min() - 8, ys.Min() - 8, Math.Max(16, xs.Max() - xs.Min() + 16), Math.Max(16, ys.Max() - ys.Min() + 16));
        }

        private static bool PointInPolygon(Point pt, List<Point> poly)
        {
            if (poly.Count < 3) return false;
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if (((poly[i].Y > pt.Y) != (poly[j].Y > pt.Y)) &&
                    (pt.X < (poly[j].X - poly[i].X) * (pt.Y - poly[i].Y) / (poly[j].Y - poly[i].Y + 1e-9) + poly[i].X))
                    inside = !inside;
            }
            return inside;
        }

        private void SelectObjectsInPredicate(Func<Point, bool> inside)
        {
            ClearGroupSelection();
            foreach (var p in Scene.Points) if (inside(new Point(p.X, p.Y))) _selPointIds.Add(p.Id);
            foreach (var st in Scene.Strokes) if (st.Points.Any(pt => inside(new Point(pt[0], pt[1])))) _selStrokeIds.Add(st.Id);
            foreach (var t in Scene.Texts) if (inside(new Point(t.X, t.Y))) _selTextIds.Add(t.Id);
            foreach (var mf in MagicFills)
            {
                Point c = new(mf.BoundsDip.Left + mf.BoundsDip.Width * 0.5, mf.BoundsDip.Top + mf.BoundsDip.Height * 0.5);
                if (inside(c)) _selMagicIds.Add(mf.Id);
            }
            int total = _selPointIds.Count + _selStrokeIds.Count + _selTextIds.Count + _selMagicIds.Count;
            if (total > 0)
                ShowToast($"Đã chọn {total} đối tượng! Kéo bên trong khung xanh để di chuyển toàn bộ, hoặc nhấn Delete để xóa.", 3800);
        }

        private void MoveSelectedGroupBy(double dx, double dy)
        {
            foreach (var pid in _selPointIds) { var p = GetPoint(pid); if (p != null) { p.X += dx; p.Y += dy; } }
            foreach (var sid in _selStrokeIds)
            {
                var st = Scene.Strokes.FirstOrDefault(x => x.Id == sid);
                if (st != null) for (int i = 0; i < st.Points.Count; i++) { st.Points[i][0] += dx; st.Points[i][1] += dy; }
            }
            foreach (var tid in _selTextIds)
            {
                var t = Scene.Texts.FirstOrDefault(x => x.Id == tid);
                if (t != null) { t.X += dx; t.Y += dy; }
            }
            foreach (var mid in _selMagicIds)
            {
                var mf = MagicFills.FirstOrDefault(x => x.Id == mid);
                if (mf != null) mf.BoundsDip = new Rect(mf.BoundsDip.X + dx, mf.BoundsDip.Y + dy, mf.BoundsDip.Width, mf.BoundsDip.Height);
            }
            if (ActiveSnipRect.HasValue)
                ActiveSnipRect = new Rect(ActiveSnipRect.Value.X + dx, ActiveSnipRect.Value.Y + dy, ActiveSnipRect.Value.Width, ActiveSnipRect.Value.Height);
            for (int i = 0; i < ActiveSnipLasso.Count; i++)
                ActiveSnipLasso[i] = new Point(ActiveSnipLasso[i].X + dx, ActiveSnipLasso[i].Y + dy);
        }

        private void ClearActiveSteps()
        {
            _animDrawTimer.Stop();
            _animEdgeQueue.Clear();
            _currentAnimEdge = null;
            ClearGroupSelection();
            _stepPoints.Clear();
            _activeStroke = null;
            _draggingPointId = null;
            _draggingTextId = null;
            _draggingRevHandle = 0;
            _selectedPointId = null;
            _selectedShapeId = null;
            InvalidateVisual();
        }

        public void SetTool(string toolName)
        {
            Tool = toolName;
            _stepPoints.Clear();
            _activeStroke = null;
            _isDraggingSnip = false;
            Cursor = toolName == "select" ? Cursors.Arrow : Cursors.Cross;

            string hint = toolName switch
            {
                "select" => "Chọn/Kéo: Kéo đỉnh hoặc kéo chốt đỏ [a], [b] trên trục để đổi cận tích phân!",
                "snip_rect" => "Chọn HCN (Snipping Tool): Kéo khung chữ nhật -> bấm [Chụp vùng] hoặc [Tô vùng] (Enter để chụp ngay)!",
                "snip_lasso" => "Chọn tự do (Snipping Tool): Giữ chuột khoanh vùng tự do -> bấm [Chụp vùng] hoặc [Tô vùng]!",
                "magic_wand" => "Đũa thần (Tô 1-Click): Click vào bên trong hình kín trên PDF, Web, Paint, Word để tô màu!",
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

        // ================= SNIPPING TOOL ACTIONS (CHỤP VÙNG / TÔ VÙNG) =================
        public void CaptureSnipToClipboard()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            BitmapSource? bmp = null;

            if (ActiveSnipLasso.Count >= 3)
                bmp = ScreenMagicWand.CaptureLassoSnip(ActiveSnipLasso, dpi.DpiScaleX, dpi.DpiScaleY);
            else if (ActiveSnipRect.HasValue && ActiveSnipRect.Value.Width > 3 && ActiveSnipRect.Value.Height > 3)
                bmp = ScreenMagicWand.CaptureRectSnip(ActiveSnipRect.Value, dpi.DpiScaleX, dpi.DpiScaleY);

            if (bmp == null)
            {
                ShowToast("Hãy dùng [Chọn HCN] hoặc [Chọn tự do] để khoanh một vùng trên màn hình trước!");
                return;
            }

            CopyBitmapSourceToClipboard(bmp);
            ActiveSnipRect = null;
            ActiveSnipLasso.Clear();
            InvalidateVisual();
            ShowToast("Đã chụp vùng chọn (Snip) vào Clipboard — nhấn Ctrl+V để dán vào Word/Paint/Zalo!");
        }

        public void FillSnipRegion()
        {
            if (ActiveSnipLasso.Count >= 3)
            {
                SaveHistory();
                var ptIds = new List<string>();
                foreach (var p in ActiveSnipLasso)
                {
                    var gp = new GeoPoint { Id = GenId("spt"), X = p.X, Y = p.Y, Label = "" };
                    Scene.Points.Add(gp);
                    ptIds.Add(gp.Id);
                }
                Scene.Regions.Add(new GeoRegion
                {
                    Id = GenId("reg"), Type = "polygon", PointIds = ptIds,
                    ColorHex = FillColorHex, Opacity = FillOpacity, Pattern = FillPattern
                });
                ActiveSnipLasso.Clear();
                InvalidateVisual();
                ShowToast("Đã tô màu vùng chọn tự do!");
                return;
            }

            if (ActiveSnipRect.HasValue && ActiveSnipRect.Value.Width > 3 && ActiveSnipRect.Value.Height > 3)
            {
                SaveHistory();
                var r = ActiveSnipRect.Value;
                var ptIds = new List<string>();
                foreach (var p in new[] { r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft })
                {
                    var gp = new GeoPoint { Id = GenId("spt"), X = p.X, Y = p.Y, Label = "" };
                    Scene.Points.Add(gp);
                    ptIds.Add(gp.Id);
                }
                Scene.Regions.Add(new GeoRegion
                {
                    Id = GenId("reg"), Type = "polygon", PointIds = ptIds,
                    ColorHex = FillColorHex, Opacity = FillOpacity, Pattern = FillPattern
                });
                ActiveSnipRect = null;
                InvalidateVisual();
                ShowToast("Đã tô màu vùng chọn chữ nhật!");
                return;
            }

            ShowToast("Hãy dùng [Chọn HCN] hoặc [Chọn tự do] khoanh vùng trước khi bấm [Tô vùng]!");
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
            if (ctx.Revolution != null && ctx.Revolution.IsValid)
            {
                RevScene = ctx.Revolution;
                NotifyMetrics();
            }
            InvalidateVisual();
            ShowToast($"{ctx.Summary}!", 4200);
        }

        public void FillSelectedOfficeShape()
        {
            if (OfficeContext == null || OfficeContext.Contours.Count == 0)
            {
                SyncFromOffice();
                if (OfficeContext == null || OfficeContext.Contours.Count == 0) return;
            }

            // Nếu đã nhận diện được cặp Trục + Đường cong (RevolutionScene) -> bật ngay Tô diện tích tích phân khớp khít!
            if (OfficeContext.Revolution != null && OfficeContext.Revolution.IsValid)
            {
                RevScene = OfficeContext.Revolution;
                RevMode = MathRenderMode.Area2D;
                NotifyMetrics();
                InvalidateVisual();
                var (s, _, _) = RevScene.ComputeMetrics();
                ShowToast($"Đã tô diện tích chuẩn khớp khít trục & đường cong (S ≈ {s:0.00} cm²)! Kéo chốt a, b để chỉnh cận.");
                return;
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
                    Id = GenId("reg"), Type = "polygon", PointIds = ptIds,
                    ColorHex = FillColorHex, Opacity = FillOpacity, Pattern = FillPattern
                });
                added++;
            }

            InvalidateVisual();
            ShowToast($"Đã tô màu {added} miền khớp tọa độ từ hình Office!");
        }

        // ================= TẦNG 3: CHÈN NGƯỢC VÀO MỌI ỨNG DỤNG =================
        private bool TargetNeedsOpaqueWhiteBg(string procUpper)
        {
            if (ExportWhiteBg) return true;
            // Các phần mềm GDI đời cũ (ScienceWord, Paint, WordPad) biến kênh trong suốt (Alpha=0) thành nền đen kịt khi dán Clipboard
            return procUpper.Contains("SCIENCEWORD")
                || procUpper is "SW" or "MSPAINT" or "WORDPAD";
        }

        public void InsertIntoTargetApp()
        {
            var bbox = ComputeContentBBox();
            if (bbox == null) { ShowToast("Chưa có hình tô hay nét vẽ nào để chèn!"); return; }

            var dpi = VisualTreeHelper.GetDpi(this);
            string procUpper = LastTargetProcName.ToUpperInvariant();

            if (OfficeContext != null || procUpper is "WINWORD" or "POWERPNT" or "EXCEL")
            {
                var officeBmp = RenderExportBitmap(2.5, forceWhiteBg: ExportWhiteBg);
                if (officeBmp == null) return;

                string tempPath = Path.Combine(Path.GetTempPath(), $"ScienceWordOverlay_{DateTime.Now.Ticks}.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(officeBmp));
                using (var fs = File.Create(tempPath)) encoder.Save(fs);

                if (OfficeComBridge.InsertPngToOffice(OfficeContext, LastTargetProcName, tempPath, bbox.Value, Scene, dpi.DpiScaleX, dpi.DpiScaleY, out _))
                {
                    SceneHistoryStore.SaveScene(Scene, bbox.Value, Win32Helper.FriendlyAppName(LastTargetProcName));
                    ShowToast($"Đã chèn khớp tọa độ vào {Win32Helper.FriendlyAppName(LastTargetProcName)}!");
                    return;
                }
            }

            // Lưu vào lịch sử để hỗ trợ Double-Click / Mở hình cũ sửa lại trên ScienceWord & các app khác
            var savedEntry = SceneHistoryStore.SaveScene(Scene, bbox.Value, Win32Helper.FriendlyAppName(LastTargetProcName));

            // Dán qua Clipboard (ScienceWord, Paint, Web, Zalo...): xuất đúng tỉ lệ 1:1 pixel vật lý màn hình (dpi.DpiScaleX)
            bool useWhite = TargetNeedsOpaqueWhiteBg(procUpper);
            var rawBmp = RenderExportBitmap(dpi.DpiScaleX, forceWhiteBg: useWhite);
            if (rawBmp == null) return;

            BitmapSource finalBmp = useWhite ? SceneHistoryStore.EmbedStegoId(rawBmp, savedEntry.Id) : rawBmp;
            CopyBitmapSourceToClipboard(finalBmp, opaqueWhite: useWhite);
            if (Win32Helper.SendPasteToWindow(LastTargetHwnd))
                ShowToast($"Đã dán 1:1 vào {Win32Helper.FriendlyAppName(LastTargetProcName)} (Lưu #{savedEntry.Id} — Double-Click hoặc bấm [Sửa cũ] để chỉnh lại)!", 4200);
            else
                ShowToast($"Đã Copy ảnh (#{savedEntry.Id}) vào Clipboard — nhấn Ctrl+V để dán!");
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
            foreach (char ch in Alphabet) { string s = ch.ToString(); if (!used.Contains(s)) return s; }
            for (int i = 1; ; i++)
                foreach (char ch in Alphabet) { string s = $"{ch}{i}"; if (!used.Contains(s)) return s; }
        }

        private GeoPoint? GetPoint(string id) => Scene.Points.FirstOrDefault(p => p.Id == id);

        private GeoPoint? FindNearestPoint(Point pos, double radius = 14.0)
        {
            GeoPoint? best = null; double bestD = radius;
            foreach (var p in Scene.Points)
            {
                if (string.IsNullOrEmpty(p.Label) && (p.Id.StartsWith("opt_") || p.Id.StartsWith("spt_"))) continue;
                double d = Dist(pos, new Point(p.X, p.Y));
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        private GeoShape? FindNearestSegment(Point pos, double radius = 12.0)
        {
            GeoShape? best = null; double bestD = radius;
            foreach (var s in Scene.Shapes)
            {
                if (s.Type is "segment" or "vector")
                {
                    var p1 = GetPoint(s.P1Id); var p2 = GetPoint(s.P2Id);
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
                if (Math.Abs(pos.X - Scene.Texts[i].X) < 48 && Math.Abs(pos.Y - Scene.Texts[i].Y) < 24) return Scene.Texts[i];
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
            Scene.Shapes.Add(new GeoShape { Id = GenId("sh"), Type = type, P1Id = p1Id, P2Id = p2Id, ColorHex = StrokeColorHex, StrokeWidth = StrokeWidth, DashStyle = dashOverride ?? StrokeDash });
        }

        public void InsertPreset3D(string kind)
        {
            SaveHistory();
            _animDrawTimer.Stop();
            _animEdgeQueue.Clear();
            _currentAnimEdge = null;

            double cx = ActualWidth > 100 ? ActualWidth / 2.0 : SystemParameters.PrimaryScreenWidth / 2.0;
            double cy = ActualHeight > 100 ? ActualHeight / 2.0 : SystemParameters.PrimaryScreenHeight / 2.0;
            var edgeTasks = new List<AnimEdgeTask>();

            if (kind == "pyramid")
            {
                var pS = GetOrCreatePoint(new Point(cx - 20, cy - 170), "S");
                var pA = GetOrCreatePoint(new Point(cx - 110, cy + 30), "A");
                var pB = GetOrCreatePoint(new Point(cx - 190, cy + 120), "B");
                var pC = GetOrCreatePoint(new Point(cx + 90, cy + 120), "C");
                var pD = GetOrCreatePoint(new Point(cx + 170, cy + 30), "D");
                foreach (var (a, b) in new[] { (pS, pA), (pA, pB), (pA, pD), (pA, pC), (pB, pD) })
                    edgeTasks.Add(new AnimEdgeTask { P1Id = a.Id, P2Id = b.Id, Dash = "dashed" });
                foreach (var (a, b) in new[] { (pS, pB), (pS, pC), (pS, pD), (pB, pC), (pC, pD) })
                    edgeTasks.Add(new AnimEdgeTask { P1Id = a.Id, P2Id = b.Id, Dash = "solid" });
                ShowToast("Đang dùng Thước & Bút mô phỏng vẽ Hình chóp S.ABCD chuẩn nét đứt SGK...");
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
                    edgeTasks.Add(new AnimEdgeTask { P1Id = a.Id, P2Id = b.Id, Dash = "dashed" });
                foreach (var (a, b) in new[] { (pB, pC), (pC, pD), (pB, pB1), (pC, pC1), (pD, pD1), (pA1, pB1), (pB1, pC1), (pC1, pD1), (pD1, pA1) })
                    edgeTasks.Add(new AnimEdgeTask { P1Id = a.Id, P2Id = b.Id, Dash = "solid" });
                ShowToast("Đang dùng Thước & Bút mô phỏng vẽ Hình lập phương ABCD.A'B'C'D'...");
            }

            if (ShowVirtualTools && edgeTasks.Count > 0)
            {
                foreach (var t in edgeTasks) _animEdgeQueue.Enqueue(t);
                _animDrawTimer.Start();
            }
            else
            {
                foreach (var t in edgeTasks) AddShape("segment", t.P1Id, t.P2Id, t.Dash);
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
                else { _stepPoints.Clear(); ActiveSnipRect = null; ActiveSnipLasso.Clear(); InvalidateVisual(); }
                return;
            }
            if (e.ChangedButton != MouseButton.Left) return;

            // 0. Ưu tiên kéo 2 chốt đỏ [a], [b] của khối tròn xoay 3D bất kỳ lúc nào đang hiện
            if (RevScene != null && RevScene.IsValid)
            {
                Point hA = RevScene.ToScreen2D(RevScene.LimitA, 0);
                Point hB = RevScene.ToScreen2D(RevScene.LimitB, 0);
                double dA = Dist(pos, hA), dB = Dist(pos, hB);
                if (dA <= 16.0 && dA <= dB) { _draggingRevHandle = 1; CaptureMouse(); return; }
                if (dB <= 16.0) { _draggingRevHandle = 2; CaptureMouse(); return; }
            }

            // 0.5 Nếu đang có nhóm đối tượng được chọn và click vào bên trong khung nhóm -> Kéo di chuyển TOÀN BỘ nhóm!
            if (Tool is "select" or "snip_rect" or "snip_lasso" && HasGroupSelection())
            {
                var grpBox = GetGroupSelectionBounds();
                if (grpBox.HasValue && grpBox.Value.Contains(pos))
                {
                    SaveHistory();
                    _isDraggingGroup = true;
                    _groupLastPos = pos;
                    CaptureMouse();
                    return;
                }
            }

            // 1. Snipping Tool: Chọn HCN & Chọn Tự Do (Lasso) -> Vừa chụp vừa chọn đối tượng bên trong
            if (Tool == "snip_rect")
            {
                ClearGroupSelection();
                ActiveSnipLasso.Clear();
                _snipStartPt = pos;
                ActiveSnipRect = new Rect(pos, pos);
                _isDraggingSnip = true;
                CaptureMouse();
                InvalidateVisual();
                return;
            }
            if (Tool == "snip_lasso")
            {
                ClearGroupSelection();
                ActiveSnipRect = null;
                ActiveSnipLasso.Clear();
                ActiveSnipLasso.Add(pos);
                _isDraggingSnip = true;
                CaptureMouse();
                InvalidateVisual();
                return;
            }

            // 2. Đũa thần Flood-Fill
            if (Tool == "magic_wand")
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                var res = ScreenMagicWand.CaptureAndFloodFill(pos, dpi.DpiScaleX, dpi.DpiScaleY, ParseColor(FillColorHex, 1.0), FillOpacity, FillPattern, 32);
                if (res != null)
                {
                    MagicFills.Add(new MagicFillLayer { Id = GenId("mf"), Bitmap = res.Bitmap, BoundsDip = res.BoundsDip });
                    InvalidateVisual();
                    ShowToast("Đũa thần đã dò viền và tô màu vùng kín thành công!");
                }
                else ShowToast("Vùng quá lớn hoặc không khép kín! Hãy click vào bên trong một hình kín.");
                return;
            }

            if (Tool == "select")
            {
                var pt = FindNearestPoint(pos, 15.0);
                if (pt != null) { ClearGroupSelection(); SaveHistory(); _draggingPointId = pt.Id; _selectedPointId = pt.Id; _selectedShapeId = null; CaptureMouse(); InvalidateVisual(); return; }
                var txt = FindNearestText(pos);
                if (txt != null) { ClearGroupSelection(); SaveHistory(); _draggingTextId = txt.Id; CaptureMouse(); InvalidateVisual(); return; }
                var sh = FindNearestSegment(pos, 12.0);
                if (sh != null) { ClearGroupSelection(); _selectedShapeId = sh.Id; _selectedPointId = null; InvalidateVisual(); return; }

                // Click vùng trống -> Kéo khung chọn nhiều đối tượng (Marquee Selection)
                ClearGroupSelection();
                _selectedPointId = null; _selectedShapeId = null;
                _marqueeStart = pos;
                _marqueeRect = new Rect(pos, pos);
                CaptureMouse();
                InvalidateVisual();
                return;
            }

            if (Tool is "pen" or "highlighter")
            {
                SaveHistory();
                string pStyle = Tool == "highlighter" ? "highlighter" : PenTipStyle;
                bool isHl = pStyle == "highlighter";
                double baseW = isHl ? Math.Max(16.0, StrokeWidth * 5.0) : Math.Max(1.6, StrokeWidth);
                _activeStroke = new FreeStroke
                {
                    Id = GenId("strk"),
                    ColorHex = isHl ? FillColorHex : StrokeColorHex,
                    Width = baseW,
                    Opacity = isHl ? 0.36 : (pStyle == "pencil" ? 0.82 : 1.0),
                    IsHighlighter = isHl,
                    PenStyle = pStyle
                };
                _activeStroke.Points.Add(new[] { pos.X, pos.Y });
                _activeStroke.PointWidths.Add(baseW);
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
                    if (p1 != null && p2 != null) { SaveHistory(); var mid = GetOrCreatePoint(new Point((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0)); ShowToast($"Đã dựng trung điểm {mid.Label}"); InvalidateVisual(); }
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
                            Scene.Markers.Add(new GeoMarker { Id = GenId("mk"), Type = "right_angle", P1Id = v.Id, VertexId = foot.Id, P2Id = Dist(footPos, new Point(p1.X, p1.Y)) > 8 ? p1.Id : p2.Id, ColorHex = StrokeColorHex });
                            _stepPoints.Clear(); ShowToast($"Đã hạ đường cao {v.Label}{foot.Label} kèm góc vuông!"); InvalidateVisual();
                        }
                    }
                    else ShowToast("Bước 2: Hãy click vào một Cạnh đáy");
                }
                return;
            }

            if (Tool is "segment" or "dashed" or "vector" or "circle" or "rect")
            {
                if (_stepPoints.Count == 0) { SaveHistory(); _stepPoints.Add(GetOrCreatePoint(pos).Id); InvalidateVisual(); }
                else
                {
                    var p1 = GetPoint(_stepPoints[0]);
                    if (Tool == "rect" && p1 != null)
                    {
                        var p2 = GetOrCreatePoint(new Point(pos.X, p1.Y)); var p3 = GetOrCreatePoint(pos); var p4 = GetOrCreatePoint(new Point(p1.X, pos.Y));
                        AddShape("segment", p1.Id, p2.Id); AddShape("segment", p2.Id, p3.Id); AddShape("segment", p3.Id, p4.Id); AddShape("segment", p4.Id, p1.Id);
                    }
                    else if (p1 != null)
                    {
                        var p2 = GetOrCreatePoint(pos);
                        if (p2.Id != p1.Id) AddShape(Tool == "dashed" ? "segment" : Tool, p1.Id, p2.Id, Tool == "dashed" ? "dashed" : null);
                    }
                    _stepPoints.Clear(); InvalidateVisual();
                }
                return;
            }

            if (Tool == "triangle")
            {
                if (_stepPoints.Count == 0) SaveHistory();
                _stepPoints.Add(GetOrCreatePoint(pos).Id);
                if (_stepPoints.Count == 3)
                {
                    AddShape("segment", _stepPoints[0], _stepPoints[1]); AddShape("segment", _stepPoints[1], _stepPoints[2]); AddShape("segment", _stepPoints[2], _stepPoints[0]);
                    _stepPoints.Clear();
                }
                InvalidateVisual(); return;
            }

            if (Tool == "fill_area")
            {
                var p = GetOrCreatePoint(pos);
                if (_stepPoints.Count >= 3 && p.Id == _stepPoints[0]) { FinishPolygonFill(); return; }
                if (!_stepPoints.Contains(p.Id)) { _stepPoints.Add(p.Id); ShowToast($"Đã chọn {_stepPoints.Count} đỉnh. Click đỉnh đầu hoặc Chuột phải / Enter để tô!"); }
                InvalidateVisual(); return;
            }

            if (Tool is "fill_angle" or "right_angle")
            {
                _stepPoints.Add(GetOrCreatePoint(pos).Id);
                if (_stepPoints.Count == 3)
                {
                    SaveHistory();
                    if (Tool == "right_angle") Scene.Markers.Add(new GeoMarker { Id = GenId("mk"), Type = "right_angle", P1Id = _stepPoints[0], VertexId = _stepPoints[1], P2Id = _stepPoints[2], ColorHex = StrokeColorHex });
                    else Scene.Regions.Add(new GeoRegion { Id = GenId("reg"), Type = "angle", PointIds = new List<string>(_stepPoints), ColorHex = FillColorHex, Opacity = FillOpacity, Pattern = FillPattern });
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
            _stepPoints.Clear(); ShowToast("Đã tô màu miền đa giác!"); InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (IsPassthrough) return;
            Point pos = e.GetPosition(this);
            _mousePos = pos;

            if (_draggingRevHandle != 0 && RevScene != null && RevScene.IsValid)
            {
                double dx = pos.X - RevScene.AxisOrigin.X, dy = pos.Y - RevScene.AxisOrigin.Y;
                double t = Math.Clamp(dx * RevScene.AxisU.X + dy * RevScene.AxisU.Y, RevScene.TMin, RevScene.TMax);
                if (_draggingRevHandle == 1) RevScene.LimitA = t;
                else RevScene.LimitB = t;
                NotifyMetrics();
                InvalidateVisual();
                return;
            }

            if (_isDraggingGroup)
            {
                MoveSelectedGroupBy(pos.X - _groupLastPos.X, pos.Y - _groupLastPos.Y);
                _groupLastPos = pos;
                InvalidateVisual();
                return;
            }

            if (_marqueeRect.HasValue)
            {
                _marqueeRect = new Rect(_marqueeStart, pos);
                InvalidateVisual();
                return;
            }

            if (_isDraggingSnip)
            {
                if (Tool == "snip_rect") ActiveSnipRect = new Rect(_snipStartPt, pos);
                else if (Tool == "snip_lasso" && (ActiveSnipLasso.Count == 0 || Dist(ActiveSnipLasso[^1], pos) >= 3.0)) ActiveSnipLasso.Add(pos);
                InvalidateVisual();
                return;
            }

            if (_draggingPointId != null) { var pt = GetPoint(_draggingPointId); if (pt != null) { pt.X = pos.X; pt.Y = pos.Y; } InvalidateVisual(); return; }
            if (_draggingTextId != null) { var txt = Scene.Texts.FirstOrDefault(t => t.Id == _draggingTextId); if (txt != null) { txt.X = pos.X; txt.Y = pos.Y; } InvalidateVisual(); return; }
            if (_activeStroke != null)
            {
                var last = _activeStroke.Points[^1];
                double d = Hypot(pos.X - last[0], pos.Y - last[1]);
                if (d >= 1.8)
                {
                    _activeStroke.Points.Add(new[] { pos.X, pos.Y });
                    double w = _activeStroke.Width;
                    if (_activeStroke.PenStyle == "brush")
                    {
                        double prevW = _activeStroke.PointWidths.Count > 0 ? _activeStroke.PointWidths[^1] : w;
                        double targetW = Math.Clamp(w * (1.75 - Math.Min(1.2, d / 18.0)), Math.Max(1.0, w * 0.35), w * 2.1);
                        w = prevW * 0.6 + targetW * 0.4;
                    }
                    _activeStroke.PointWidths.Add(w);
                }
                InvalidateVisual(); return;
            }
            if (_stepPoints.Count > 0 || (ShowVirtualTools && Tool is not "select" and not "snip_rect" and not "snip_lasso" and not "magic_wand")) InvalidateVisual();
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isDraggingGroup)
            {
                _isDraggingGroup = false;
            }
            if (_marqueeRect.HasValue)
            {
                var r = _marqueeRect.Value;
                _marqueeRect = null;
                if (r.Width > 5 || r.Height > 5)
                    SelectObjectsInPredicate(pt => r.Contains(pt));
                InvalidateVisual();
            }
            if (_isDraggingSnip)
            {
                _isDraggingSnip = false;
                if (Tool == "snip_rect" && ActiveSnipRect.HasValue && ActiveSnipRect.Value.Width > 4)
                {
                    var r = ActiveSnipRect.Value;
                    SelectObjectsInPredicate(pt => r.Contains(pt));
                }
                else if (Tool == "snip_lasso" && ActiveSnipLasso.Count >= 3)
                {
                    SelectObjectsInPredicate(pt => PointInPolygon(pt, ActiveSnipLasso));
                }
                if (!HasGroupSelection())
                    ShowToast("Đã khoanh vùng! Bấm [Chụp vùng] (Enter) để Copy ảnh, hoặc [Tô vùng] để tô màu!", 4000);
                InvalidateVisual();
            }
            _draggingRevHandle = 0;
            _draggingPointId = null; _draggingTextId = null; _activeStroke = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        public void HandleKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { _stepPoints.Clear(); ActiveSnipRect = null; ActiveSnipLasso.Clear(); SetTool("select"); }
            else if (e.Key == Key.Enter)
            {
                if (Tool == "fill_area" && _stepPoints.Count >= 3) FinishPolygonFill();
                else if (ActiveSnipRect.HasValue || ActiveSnipLasso.Count >= 3) CaptureSnipToClipboard();
            }
            else if (e.Key is Key.Delete or Key.Back) DeleteSelected();
            else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Undo();
            else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Redo();
            else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0) CopyToClipboard();
        }

        public void DeleteSelected()
        {
            if (HasGroupSelection())
            {
                SaveHistory();
                Scene.Points.RemoveAll(p => _selPointIds.Contains(p.Id));
                Scene.Shapes.RemoveAll(s => _selPointIds.Contains(s.P1Id) || _selPointIds.Contains(s.P2Id));
                Scene.Regions.RemoveAll(r => r.PointIds.Any(pid => _selPointIds.Contains(pid)));
                Scene.Markers.RemoveAll(m => _selPointIds.Contains(m.P1Id) || _selPointIds.Contains(m.VertexId) || _selPointIds.Contains(m.P2Id));
                Scene.Strokes.RemoveAll(st => _selStrokeIds.Contains(st.Id));
                Scene.Texts.RemoveAll(t => _selTextIds.Contains(t.Id));
                MagicFills.RemoveAll(mf => _selMagicIds.Contains(mf.Id));
                ClearGroupSelection();
                InvalidateVisual();
                ShowToast("Đã xóa toàn bộ nhóm đối tượng đang chọn!");
                return;
            }
            if (_selectedShapeId != null) { SaveHistory(); Scene.Shapes.RemoveAll(s => s.Id == _selectedShapeId); _selectedShapeId = null; InvalidateVisual(); ShowToast("Đã xóa đường đang chọn"); }
            else if (_selectedPointId != null)
            {
                SaveHistory(); string pid = _selectedPointId;
                Scene.Points.RemoveAll(p => p.Id == pid); Scene.Shapes.RemoveAll(s => s.P1Id == pid || s.P2Id == pid);
                Scene.Regions.RemoveAll(r => r.PointIds.Contains(pid)); Scene.Markers.RemoveAll(m => m.P1Id == pid || m.VertexId == pid || m.P2Id == pid);
                _selectedPointId = null; InvalidateVisual(); ShowToast("Đã xóa điểm và các cạnh liên quan");
            }
        }

        // ================= RENDERING =================
        private static Color ParseColor(string hex, double opacity = 1.0)
        {
            try { var c = (Color)ColorConverter.ConvertFromString(hex); c.A = (byte)Math.Clamp(opacity * 255.0, 0, 255); return c; }
            catch { return Color.FromArgb((byte)(opacity * 255), 15, 23, 42); }
        }

        private static Color ShadeColor(Color c, double factor, double opacity)
        {
            return Color.FromArgb((byte)Math.Clamp(opacity * 255.0, 20, 255),
                (byte)Math.Clamp(c.R * factor, 0, 255),
                (byte)Math.Clamp(c.G * factor, 0, 255),
                (byte)Math.Clamp(c.B * factor, 0, 255));
        }

        private static Brush MakeRegionBrush(string hex, double opacity, string pattern)
        {
            Color c = ParseColor(hex, opacity);
            if (pattern == "solid") return new SolidColorBrush(c);
            var group = new DrawingGroup();
            if (pattern == "hatch") group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(ParseColor(hex, Math.Min(1.0, opacity + 0.35))), 1.5), new LineGeometry(new Point(0, 10), new Point(10, 0))));
            else if (pattern == "cross") { var pen = new Pen(new SolidColorBrush(ParseColor(hex, Math.Min(1.0, opacity + 0.35))), 1.3); group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 10), new Point(10, 0)))); group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(10, 10)))); }
            else if (pattern == "dots") group.Children.Add(new GeometryDrawing(new SolidColorBrush(ParseColor(hex, Math.Min(1.0, opacity + 0.40))), null, new EllipseGeometry(new Point(5, 5), 1.8, 1.8)));
            return new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 10, 10), ViewportUnits = BrushMappingMode.Absolute };
        }

        private static Pen MakePen(string hex, double width, string dash, bool highlight = false)
        {
            var pen = new Pen(new SolidColorBrush(highlight ? ParseColor("#2563EB") : ParseColor(hex)), width + (highlight ? 1.4 : 0.0)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (dash == "dashed") pen.DashStyle = new DashStyle(new double[] { 5.5, 3.8 }, 0);
            else if (dash == "dotted") pen.DashStyle = DashStyles.Dot;
            return pen;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (!IsPassthrough) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), null, new Rect(0, 0, ActualWidth, ActualHeight));
            RenderScene(dc, cleanExport: false);

            if (!string.IsNullOrEmpty(_toastMsg))
            {
                var ft = new FormattedText(_toastMsg, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 13, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                double tw = ft.Width + 34, th = 34;
                var rect = new Rect((ActualWidth - tw) / 2.0, ActualHeight - 64, tw, th);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(230, 15, 23, 42)), new Pen(new SolidColorBrush(Color.FromArgb(190, 96, 165, 250)), 1.2), rect, 17, 17);
                dc.DrawText(ft, new Point(rect.X + 17, rect.Y + (th - ft.Height) / 2.0));
            }
        }

        private void RenderScene(DrawingContext dc, bool cleanExport)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            // 0. Các lớp tô Đũa thần
            foreach (var mf in MagicFills) dc.DrawImage(mf.Bitmap, mf.BoundsDip);

            // 0.4 Khối tròn xoay 3D / Diện tích tích phân [a, b]
            if (RevScene != null && RevScene.IsValid)
                RenderRevolutionScene(dc, RevScene, cleanExport, dpi);

            // 0.5 Khung nhận diện đối tượng Office (ẩn khi export)
            if (!cleanExport && OfficeContext != null && OfficeContext.ScreenBoundsDip.Width > 2)
                dc.DrawRectangle(null, new Pen(new SolidColorBrush(ParseColor("#10B981", 0.75)), 1.4) { DashStyle = DashStyles.Dash }, OfficeContext.ScreenBoundsDip);

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
                        geo.Freeze(); dc.DrawGeometry(brush, borderPen, geo);
                    }
                }
                else if (reg.Type == "angle" && reg.PointIds.Count == 3)
                {
                    var p1 = GetPoint(reg.PointIds[0]); var v = GetPoint(reg.PointIds[1]); var p2 = GetPoint(reg.PointIds[2]);
                    if (p1 != null && v != null && p2 != null)
                    {
                        double r = 36.0, a1 = Math.Atan2(p1.Y - v.Y, p1.X - v.X), a2 = Math.Atan2(p2.Y - v.Y, p2.X - v.X), diff = a2 - a1;
                        while (diff <= -Math.PI) diff += 2 * Math.PI; while (diff > Math.PI) diff -= 2 * Math.PI;
                        var geo = new StreamGeometry();
                        using (var ctx = geo.Open()) { ctx.BeginFigure(new Point(v.X, v.Y), true, true); ctx.LineTo(new Point(v.X + r * Math.Cos(a1), v.Y + r * Math.Sin(a1)), true, false); ctx.ArcTo(new Point(v.X + r * Math.Cos(a1 + diff), v.Y + r * Math.Sin(a1 + diff)), new Size(r, r), 0, Math.Abs(diff) > Math.PI, diff >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false); }
                        geo.Freeze(); dc.DrawGeometry(brush, borderPen, geo);
                    }
                }
            }

            // 2. Ký hiệu góc vuông
            foreach (var mk in Scene.Markers)
            {
                var p1 = GetPoint(mk.P1Id); var v = GetPoint(mk.VertexId); var p2 = GetPoint(mk.P2Id);
                if (p1 == null || v == null || p2 == null) continue;
                double u1x = p1.X - v.X, u1y = p1.Y - v.Y, u2x = p2.X - v.X, u2y = p2.Y - v.Y, l1 = Hypot(u1x, u1y), l2 = Hypot(u2x, u2y);
                if (l1 < 1 || l2 < 1) continue;
                double sz = 14.0; Point e1 = new(v.X + u1x / l1 * sz, v.Y + u1y / l1 * sz), e2 = new(v.X + u2x / l2 * sz, v.Y + u2y / l2 * sz), c = new(e1.X + u2x / l2 * sz, e1.Y + u2y / l2 * sz);
                var pen = new Pen(new SolidColorBrush(ParseColor(mk.ColorHex)), 1.5); dc.DrawLine(pen, e1, c); dc.DrawLine(pen, c, e2);
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
                    using (var ctx = geo.Open()) { ctx.BeginFigure(pt2, true, true); ctx.LineTo(new Point(pt2.X - ah * Math.Cos(ang - 0.42), pt2.Y - ah * Math.Sin(ang - 0.42)), true, false); ctx.LineTo(new Point(pt2.X - ah * Math.Cos(ang + 0.42), pt2.Y - ah * Math.Sin(ang + 0.42)), true, false); }
                    geo.Freeze(); dc.DrawGeometry(new SolidColorBrush(ParseColor(sh.ColorHex)), null, geo);
                }
                else dc.DrawLine(pen, pt1, pt2);
            }

            // 4. Nét vẽ tay đa chất liệu (Bút bi, Bút chì, Bút cọ thư pháp, Nét đứt, Dạ quang)
            foreach (var st in Scene.Strokes)
            {
                if (st.Points.Count < 2) continue;
                string pStyle = st.IsHighlighter ? "highlighter" : (st.PenStyle ?? "solid");

                if (pStyle == "brush" && st.PointWidths.Count == st.Points.Count)
                {
                    var br = new SolidColorBrush(ParseColor(st.ColorHex, st.Opacity));
                    for (int i = 0; i < st.Points.Count - 1; i++)
                    {
                        var segPen = new Pen(br, Math.Max(1.0, st.PointWidths[i])) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                        dc.DrawLine(segPen, new Point(st.Points[i][0], st.Points[i][1]), new Point(st.Points[i + 1][0], st.Points[i + 1][1]));
                    }
                    continue;
                }

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(st.Points[0][0], st.Points[0][1]), false, false);
                    for (int i = 1; i < st.Points.Count - 1; i++)
                        ctx.QuadraticBezierTo(new Point(st.Points[i][0], st.Points[i][1]), new Point((st.Points[i][0] + st.Points[i + 1][0]) / 2.0, (st.Points[i][1] + st.Points[i + 1][1]) / 2.0), true, false);
                    ctx.LineTo(new Point(st.Points[^1][0], st.Points[^1][1]), true, false);
                }
                geo.Freeze();

                if (pStyle == "pencil")
                {
                    var softPen = new Pen(new SolidColorBrush(ParseColor(st.ColorHex, 0.28)), st.Width * 1.65) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    var corePen = new Pen(new SolidColorBrush(ParseColor(st.ColorHex, 0.85)), Math.Max(1.1, st.Width * 0.82)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    dc.DrawGeometry(null, softPen, geo);
                    dc.DrawGeometry(null, corePen, geo);
                }
                else
                {
                    var pen = new Pen(new SolidColorBrush(ParseColor(st.ColorHex, st.Opacity)), st.Width)
                    {
                        StartLineCap = pStyle == "highlighter" ? PenLineCap.Flat : PenLineCap.Round,
                        EndLineCap = pStyle == "highlighter" ? PenLineCap.Flat : PenLineCap.Round,
                        LineJoin = PenLineJoin.Round
                    };
                    if (pStyle == "dash") pen.DashStyle = new DashStyle(new double[] { 4.8, 3.2 }, 0);
                    dc.DrawGeometry(null, pen, geo);
                }
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
                if (string.IsNullOrEmpty(p.Label) && (p.Id.StartsWith("opt_") || p.Id.StartsWith("spt_"))) continue;
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

            // 7. Vùng chọn Snipping Tool (Chữ nhật / Tự do Lasso)
            if (!cleanExport)
            {
                var snipPen = new Pen(new SolidColorBrush(Color.FromRgb(239, 68, 68)), 2.0) { DashStyle = DashStyles.Dash };
                var snipFill = new SolidColorBrush(Color.FromArgb(28, 59, 130, 246));
                if (_marqueeRect.HasValue && _marqueeRect.Value.Width > 2)
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(30, 37, 99, 235)), new Pen(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 1.6) { DashStyle = DashStyles.Dash }, _marqueeRect.Value);
                if (HasGroupSelection())
                {
                    var gb = GetGroupSelectionBounds();
                    if (gb.HasValue)
                        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(20, 37, 99, 235)), new Pen(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 1.8) { DashStyle = DashStyles.Dash }, gb.Value);
                }
                if (ActiveSnipRect.HasValue && ActiveSnipRect.Value.Width > 2)
                    dc.DrawRectangle(snipFill, snipPen, ActiveSnipRect.Value);
                if (ActiveSnipLasso.Count >= 2)
                {
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open()) { ctx.BeginFigure(ActiveSnipLasso[0], true, !_isDraggingSnip); ctx.PolyLineTo(ActiveSnipLasso.Skip(1).ToList(), true, false); }
                    geo.Freeze(); dc.DrawGeometry(snipFill, snipPen, geo);
                }
                if (_currentAnimEdge != null) DrawAnimated3DEdgeWithRulerAndPen(dc, dpi);
                if (_stepPoints.Count > 0) DrawLivePreviewAndTools(dc, dpi);
                else if (ShowVirtualTools && _mousePos.X > 1 && _mousePos.Y > 1)
                {
                    DrawIdleVirtualToolAtCursor(dc, dpi);
                }
            }
        }

        // ================= RENDER TÔ DIỆN TÍCH 2D & KHỐI TRÒN XOAY 3D =================
        private void RenderRevolutionScene(DrawingContext dc, RevolutionScene s, bool cleanExport, double dpi)
        {
            double a = Math.Min(s.LimitA, s.LimitB);
            double b = Math.Max(s.LimitA, s.LimitB);
            Color baseCol = ParseColor(FillColorHex, 1.0);

            if (b - a >= 1.5)
            {
                if (RevMode == MathRenderMode.Area2D)
                {
                    int steps = 72;
                    var poly = new List<Point>(steps * 2 + 2);
                    for (int i = 0; i <= steps; i++)
                    {
                        double t = a + (b - a) * i / steps;
                        poly.Add(s.ToScreen2D(t, RevolutionScene.SampleR(s.Curve1TR, t)));
                    }
                    for (int i = steps; i >= 0; i--)
                    {
                        double t = a + (b - a) * i / steps;
                        double r2 = s.HasTwoCurves ? RevolutionScene.SampleR(s.Curve2TR, t) : 0.0;
                        poly.Add(s.ToScreen2D(t, r2));
                    }
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open()) { ctx.BeginFigure(poly[0], true, true); ctx.PolyLineTo(poly.Skip(1).ToList(), true, false); }
                    geo.Freeze();
                    dc.DrawGeometry(MakeRegionBrush(FillColorHex, FillOpacity, FillPattern), null, geo);

                    var boundPen = new Pen(new SolidColorBrush(ShadeColor(baseCol, 0.7, Math.Min(1.0, FillOpacity + 0.35))), 1.5) { DashStyle = DashStyles.Dash };
                    dc.DrawLine(boundPen, s.ToScreen2D(a, RevolutionScene.SampleR(s.Curve1TR, a)), s.ToScreen2D(a, s.HasTwoCurves ? RevolutionScene.SampleR(s.Curve2TR, a) : 0));
                    dc.DrawLine(boundPen, s.ToScreen2D(b, RevolutionScene.SampleR(s.Curve1TR, b)), s.ToScreen2D(b, s.HasTwoCurves ? RevolutionScene.SampleR(s.Curve2TR, b) : 0));
                }
                else if (RevSweepDeg >= 1.0)
                {
                    int nT = 48;
                    int nAng = Math.Max(8, (int)Math.Ceiling(RevSweepDeg / 8.0));
                    double maxRad = RevSweepDeg * Math.PI / 180.0;

                    double[] ts = new double[nT + 1];
                    double[] rs = new double[nT + 1];
                    for (int i = 0; i <= nT; i++) { ts[i] = a + (b - a) * i / nT; rs[i] = RevolutionScene.SampleR(s.Curve1TR, ts[i]); }

                    var grid = new Point[nT + 1, nAng + 1];
                    for (int i = 0; i <= nT; i++)
                        for (int j = 0; j <= nAng; j++)
                            grid[i, j] = s.Project3D(ts[i], rs[i], maxRad * j / nAng, RevXCompression);

                    var quads = new List<(Point[] pts, double depth, Color col)>(nT * nAng + nAng * 2);
                    for (int i = 0; i < nT; i++)
                    {
                        double rAvg = 0.5 * (rs[i] + rs[i + 1]);
                        double dt = ts[i + 1] - ts[i], dr = rs[i + 1] - rs[i];
                        double sl = Hypot(dt, dr);
                        double slopeN = sl > 1e-4 ? -dr / sl : 0;
                        double signR = rAvg >= 0 ? 1.0 : -1.0;

                        for (int j = 0; j < nAng; j++)
                        {
                            double thMid = maxRad * (j + 0.5) / nAng;
                            double depth = 0.5 * (s.ComputeDepth(ts[i], rs[i], thMid) + s.ComputeDepth(ts[i + 1], rs[i + 1], thMid));
                            double nx = slopeN * 0.4, ny = Math.Cos(thMid) * signR, nz = Math.Sin(thMid) * signR;
                            double nLen = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                            if (nLen > 1e-5) { nx /= nLen; ny /= nLen; nz /= nLen; }
                            double sxN = nx * s.AxisU.X + ny * s.AxisV.X, syN = nx * s.AxisU.Y + ny * s.AxisV.Y;
                            double light = Math.Clamp(0.54 + 0.48 * Math.Max(-0.35, sxN * -0.25 + syN * -0.55 + nz * 0.78), 0.30, 1.25);

                            quads.Add((new[] { grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1] }, depth, ShadeColor(baseCol, light, Math.Max(0.45, FillOpacity + 0.25))));
                        }
                    }

                    if (RevShowCaps)
                    {
                        void AddCap(double tEnd, double rEnd, double sideSign)
                        {
                            if (Math.Abs(rEnd) < 1.0) return;
                            Point center = s.ToScreen2D(tEnd, 0);
                            Color capCol = ShadeColor(baseCol, sideSign > 0 ? 0.92 : 0.72, Math.Min(0.95, FillOpacity + 0.35));
                            for (int j = 0; j < nAng; j++)
                            {
                                double th0 = maxRad * j / nAng, th1 = maxRad * (j + 1) / nAng;
                                quads.Add((new[] { center, s.Project3D(tEnd, rEnd, th0, RevXCompression), s.Project3D(tEnd, rEnd, th1, RevXCompression) },
                                    s.ComputeDepth(tEnd, rEnd * 0.55, 0.5 * (th0 + th1)) + sideSign * 0.03, capCol));
                            }
                        }
                        AddCap(ts[0], rs[0], -1);
                        AddCap(ts[nT], rs[nT], 1);
                    }

                    quads.Sort((x, y) => x.depth.CompareTo(y.depth));
                    foreach (var q in quads)
                    {
                        var geo = new StreamGeometry();
                        using (var ctx = geo.Open()) { ctx.BeginFigure(q.pts[0], true, true); ctx.PolyLineTo(q.pts.Skip(1).ToList(), false, false); }
                        geo.Freeze();
                        dc.DrawGeometry(new SolidColorBrush(q.col), null, geo);
                    }

                    // Vẽ viền 2 miệng đầu mút
                    var rimPen = new Pen(new SolidColorBrush(ShadeColor(baseCol, 0.55, 0.85)), 1.4);
                    void DrawRing(int idx)
                    {
                        var gRing = new StreamGeometry();
                        using (var ctx = gRing.Open()) { ctx.BeginFigure(grid[idx, 0], false, false); for (int j = 1; j <= nAng; j++) ctx.LineTo(grid[idx, j], true, false); }
                        gRing.Freeze(); dc.DrawGeometry(null, rimPen, gRing);
                    }
                    DrawRing(0); DrawRing(nT);
                }
            }

            // Vẽ 2 chốt kéo cận [a], [b] màu đỏ trên trục (chỉ hiện khi không export)
            if (!cleanExport)
            {
                Point hA = s.ToScreen2D(s.LimitA, 0), hB = s.ToScreen2D(s.LimitB, 0);
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(180, 239, 68, 68)), 2.0) { DashStyle = DashStyles.Dot }, hA, hB);
                var tf = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                void DrawHandle(Point pt, string lbl)
                {
                    dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(220, 38, 38)), new Pen(Brushes.White, 2.0), pt, 9.5, 9.5);
                    var ft = new FormattedText(lbl, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 11.5, Brushes.White, dpi);
                    dc.DrawText(ft, new Point(pt.X - ft.Width / 2, pt.Y - ft.Height / 2));
                }
                DrawHandle(hA, "a"); DrawHandle(hB, "b");
            }
        }

        private void DrawIdleVirtualToolAtCursor(DrawingContext dc, double dpi)
        {
            if (Tool is "select" or "snip_rect" or "snip_lasso" or "magic_wand" or "text") return;

            if (Tool == "circle")
            {
                // Hiện Compa nhỏ sẵn sàng đặt tâm O tại con trỏ chuột
                DrawVirtualCompass(dc, _mousePos, new Point(_mousePos.X + 46, _mousePos.Y + 12), dpi, isIdle: true);
                return;
            }
            if (Tool is "altitude" or "right_angle")
            {
                // Hiện Êke vuông góc sẵn sàng tại con trỏ
                DrawVirtualSetSquare(dc, _mousePos, 0.0, 115.0, 75.0, dpi);
                DrawVirtualPen(dc, _mousePos, ParseColor(StrokeColorHex, 1.0), PenTipStyle, false);
                return;
            }
            if (Tool == "fill_angle")
            {
                // Hiện Thước đo độ nhỏ sẵn sàng tại con trỏ
                DrawVirtualProtractor(dc, _mousePos, 0.0, 68.0, dpi);
                DrawVirtualPen(dc, _mousePos, ParseColor(FillColorHex, 1.0), PenTipStyle, false);
                return;
            }

            string vStyle = Tool == "highlighter" ? "highlighter"
                : (Tool == "dashed" ? "dash" : PenTipStyle);
            Color vCol = ParseColor(vStyle == "highlighter" ? FillColorHex : StrokeColorHex, 1.0);
            DrawVirtualPen(dc, _mousePos, vCol, vStyle, _activeStroke != null);
        }

        private void DrawAnimated3DEdgeWithRulerAndPen(DrawingContext dc, double dpi)
        {
            if (_currentAnimEdge == null) return;
            var p1 = GetPoint(_currentAnimEdge.P1Id);
            var p2 = GetPoint(_currentAnimEdge.P2Id);
            if (p1 == null || p2 == null) return;

            Point a = new(p1.X, p1.Y), b = new(p2.X, p2.Y);
            double t = Math.Clamp(_animEdgeProgress, 0.0, 1.0);
            Point cur = new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

            var pen = MakePen(StrokeColorHex, StrokeWidth, _currentAnimEdge.Dash, false);
            dc.DrawLine(pen, a, cur);

            DrawVirtualRuler(dc, a, b, dpi);
            DrawVirtualPen(dc, cur, ParseColor(StrokeColorHex, 1.0), _currentAnimEdge.Dash == "dashed" ? "dash" : PenTipStyle, true);
        }

        private void DrawLivePreviewAndTools(DrawingContext dc, double dpi)
        {
            var p1 = GetPoint(_stepPoints[0]);
            if (p1 == null) return;
            Point pt1 = new(p1.X, p1.Y), pt2 = _mousePos;
            var prevPen = new Pen(new SolidColorBrush(ParseColor("#2563EB")), 1.8) { DashStyle = DashStyles.Dash };
            Color penCol = ParseColor(StrokeColorHex, 1.0);

            if (Tool is "segment" or "dashed" or "vector")
            {
                dc.DrawLine(prevPen, pt1, pt2);
                if (ShowVirtualTools)
                {
                    DrawVirtualRuler(dc, pt1, pt2, dpi);
                    DrawVirtualPen(dc, pt2, penCol, Tool == "dashed" ? "dash" : PenTipStyle, true);
                }
            }
            else if (Tool == "circle")
            {
                dc.DrawEllipse(null, prevPen, pt1, Dist(pt1, pt2), Dist(pt1, pt2));
                if (ShowVirtualTools) DrawVirtualCompass(dc, pt1, pt2, dpi, isIdle: false);
            }
            else if (Tool == "rect")
            {
                var r = new Rect(pt1, pt2);
                dc.DrawRectangle(null, prevPen, r);
                if (ShowVirtualTools)
                {
                    // Hiện Êke góc vuông bám tại góc đối diện + Bút vẽ ở đầu chuột
                    double ekeW = Math.Clamp(r.Width, 90, 220);
                    double ekeH = Math.Clamp(r.Height, 65, 150);
                    DrawVirtualSetSquare(dc, new Point(r.Left, r.Bottom), 0.0, ekeW, ekeH, dpi);
                    DrawVirtualPen(dc, pt2, penCol, PenTipStyle, true);
                }
            }
            else if (Tool is "triangle" or "fill_area")
            {
                var pts = _stepPoints.Select(GetPoint).Where(p => p != null).Select(p => new Point(p!.X, p.Y)).ToList();
                Point lastAnchor = pts[^1];
                pts.Add(pt2);
                var geo = new StreamGeometry();
                using (var ctx = geo.Open()) { ctx.BeginFigure(pts[0], Tool == "fill_area", Tool == "fill_area"); ctx.PolyLineTo(pts.Skip(1).ToList(), true, false); }
                geo.Freeze();
                dc.DrawGeometry((Tool == "fill_area" && pts.Count >= 3) ? MakeRegionBrush(FillColorHex, FillOpacity, FillPattern) : null, prevPen, geo);

                if (ShowVirtualTools)
                {
                    DrawVirtualRuler(dc, lastAnchor, pt2, dpi);
                    DrawVirtualPen(dc, pt2, Tool == "fill_area" ? ParseColor(FillColorHex, 1.0) : penCol, PenTipStyle, true);
                }
            }
            else if (Tool == "altitude")
            {
                // Đã chọn đỉnh, đang rê tìm cạnh đáy -> hiện Êke vuông góc + Bút vẽ
                var sh = FindNearestSegment(pt2, 22.0);
                if (sh != null)
                {
                    var s1 = GetPoint(sh.P1Id); var s2 = GetPoint(sh.P2Id);
                    if (s1 != null && s2 != null)
                    {
                        Point foot = ProjectPointOnLine(pt1, new Point(s1.X, s1.Y), new Point(s2.X, s2.Y));
                        dc.DrawLine(prevPen, pt1, foot);
                        if (ShowVirtualTools)
                        {
                            double ang = Math.Atan2(s2.Y - s1.Y, s2.X - s1.X) * 180.0 / Math.PI;
                            DrawVirtualSetSquare(dc, foot, ang, 150.0, Math.Clamp(Dist(pt1, foot), 75.0, 175.0), dpi);
                            DrawVirtualPen(dc, foot, penCol, PenTipStyle, true);
                        }
                        return;
                    }
                }
                dc.DrawLine(prevPen, pt1, pt2);
                if (ShowVirtualTools)
                {
                    DrawVirtualSetSquare(dc, pt2, 0.0, 130.0, 85.0, dpi);
                    DrawVirtualPen(dc, pt2, penCol, PenTipStyle, true);
                }
            }
            else if (Tool is "fill_angle" or "right_angle")
            {
                if (_stepPoints.Count == 1)
                {
                    dc.DrawLine(prevPen, pt1, pt2);
                    if (ShowVirtualTools)
                    {
                        DrawVirtualRuler(dc, pt1, pt2, dpi);
                        DrawVirtualPen(dc, pt2, penCol, PenTipStyle, true);
                    }
                }
                else if (_stepPoints.Count == 2)
                {
                    var pv = GetPoint(_stepPoints[1]);
                    if (pv != null)
                    {
                        Point vPt = new(pv.X, pv.Y);
                        dc.DrawLine(prevPen, pt1, vPt);
                        dc.DrawLine(prevPen, vPt, pt2);
                        if (ShowVirtualTools)
                        {
                            double baseAng = Math.Atan2(pt1.Y - vPt.Y, pt1.X - vPt.X) * 180.0 / Math.PI;
                            if (Tool == "right_angle")
                                DrawVirtualSetSquare(dc, vPt, baseAng, 130.0, 90.0, dpi);
                            else
                                DrawVirtualProtractor(dc, vPt, baseAng, 95.0, dpi);
                            DrawVirtualPen(dc, pt2, Tool == "fill_angle" ? ParseColor(FillColorHex, 1.0) : penCol, PenTipStyle, true);
                        }
                    }
                }
            }
        }

        private static void DrawVirtualSetSquare(DrawingContext dc, Point origin, double angleDeg, double w, double h, double dpi)
        {
            dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
            dc.PushTransform(new RotateTransform(angleDeg));

            var outer = new StreamGeometry();
            using (var ctx = outer.Open())
            {
                ctx.BeginFigure(new Point(0, 0), true, true);
                ctx.PolyLineTo(new[] { new Point(w, 0), new Point(0, -h) }, true, false);
            }
            outer.Freeze();

            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(145, 191, 219, 254)),
                new Pen(new SolidColorBrush(Color.FromArgb(225, 30, 64, 175)), 1.8), outer);

            // Tam giác rỗng bên trong Êke
            if (w > 85 && h > 55)
            {
                var inner = new StreamGeometry();
                using (var ctx = inner.Open())
                {
                    ctx.BeginFigure(new Point(22, -16), true, true);
                    ctx.PolyLineTo(new[] { new Point(w - 44, -16), new Point(22, -(h - 34)) }, true, false);
                }
                inner.Freeze();
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
                    new Pen(new SolidColorBrush(Color.FromArgb(190, 30, 64, 175)), 1.2), inner);
            }

            // Ký hiệu góc vuông 90° màu đỏ trên Êke
            var sqPen = new Pen(new SolidColorBrush(Color.FromArgb(230, 220, 38, 38)), 1.6);
            dc.DrawLine(sqPen, new Point(12, 0), new Point(12, -12));
            dc.DrawLine(sqPen, new Point(12, -12), new Point(0, -12));

            // Vạch chia cm trên 2 cạnh góc vuông
            var tickPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 30, 58, 138)), 0.9);
            for (double x = 8; x < w - 10; x += 8) dc.DrawLine(tickPen, new Point(x, 0), new Point(x, x % 16 == 0 ? -8 : -5));
            for (double y = 8; y < h - 10; y += 8) dc.DrawLine(tickPen, new Point(0, -y), new Point(y % 16 == 0 ? 8 : 5, -y));

            dc.Pop();
            dc.Pop();
        }

        private static void DrawVirtualProtractor(DrawingContext dc, Point center, double angleDeg, double radius, double dpi)
        {
            dc.PushTransform(new TranslateTransform(center.X, center.Y));
            dc.PushTransform(new RotateTransform(angleDeg));

            var pie = new StreamGeometry();
            using (var ctx = pie.Open())
            {
                ctx.BeginFigure(new Point(-radius, 0), true, true);
                ctx.ArcTo(new Point(radius, 0), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(-radius, 0), true, false);
            }
            pie.Freeze();

            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(145, 224, 242, 254)),
                new Pen(new SolidColorBrush(Color.FromArgb(225, 2, 132, 199)), 1.8), pie);

            var tickPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 12, 74, 110)), 1.0);
            for (int deg = 0; deg <= 180; deg += 15)
            {
                double rad = -deg * Math.PI / 180.0;
                double c = Math.Cos(rad), s = Math.Sin(rad);
                double len = (deg % 45 == 0) ? 12 : 7;
                dc.DrawLine(tickPen, new Point(c * radius, s * radius), new Point(c * (radius - len), s * (radius - len)));
            }

            // Tâm thước đo độ màu đỏ
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(220, 38, 38)), new Pen(Brushes.White, 1.2), new Point(0, 0), 3.2, 3.2);
            dc.Pop();
            dc.Pop();
        }


        private static void DrawVirtualPen(DrawingContext dc, Point tip, Color penCol, string style, bool isPressed)
        {
            dc.PushTransform(new TranslateTransform(tip.X, tip.Y));
            dc.PushTransform(new RotateTransform(isPressed ? -36.0 : -45.0));

            // Chấm sáng ngay đầu ngòi bút
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(isPressed ? 235 : 170), penCol.R, penCol.G, penCol.B)),
                new Pen(Brushes.White, 1.3), new Point(0, 0), 3.6, 3.6);

            if (style == "highlighter")
            {
                var nib = new StreamGeometry();
                using (var ctx = nib.Open()) { ctx.BeginFigure(new Point(0, 0), true, true); ctx.PolyLineTo(new[] { new Point(9, -6), new Point(13, 3), new Point(4, 5) }, true, false); }
                nib.Freeze();
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(230, penCol.R, penCol.G, penCol.B)), null, nib);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(240, 250, 204, 21)), new Pen(new SolidColorBrush(Color.FromRgb(51, 65, 85)), 1.3), new Rect(10, -8, 44, 15), 2, 2);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(230, penCol.R, penCol.G, penCol.B)), null, new Rect(16, -6, 26, 11));
            }
            else if (style == "pencil")
            {
                var wood = new StreamGeometry();
                using (var ctx = wood.Open()) { ctx.BeginFigure(new Point(0, 0), true, true); ctx.PolyLineTo(new[] { new Point(13, -5), new Point(13, 5) }, true, false); }
                wood.Freeze();
                var lead = new StreamGeometry();
                using (var ctx = lead.Open()) { ctx.BeginFigure(new Point(0, 0), true, true); ctx.PolyLineTo(new[] { new Point(5, -2), new Point(5, 2) }, true, false); }
                lead.Freeze();
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(245, 208, 155)), null, wood);
                dc.DrawGeometry(new SolidColorBrush(penCol), null, lead);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(234, 179, 8)), new Pen(new SolidColorBrush(Color.FromRgb(71, 85, 105)), 1.1), new Rect(13, -5, 42, 10));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(203, 213, 225)), null, new Rect(55, -5, 6, 10));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(244, 114, 182)), null, new Rect(61, -5, 8, 10));
            }
            else if (style == "brush")
            {
                var tipGeo = new StreamGeometry();
                using (var ctx = tipGeo.Open()) { ctx.BeginFigure(new Point(0, 0), true, true); ctx.QuadraticBezierTo(new Point(8, -6), new Point(16, -4), true, false); ctx.LineTo(new Point(16, 4), true, false); ctx.QuadraticBezierTo(new Point(8, 6), new Point(0, 0), true, false); }
                tipGeo.Freeze();
                dc.DrawGeometry(new SolidColorBrush(penCol), null, tipGeo);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(120, 53, 15)), new Pen(new SolidColorBrush(Color.FromRgb(69, 26, 3)), 1.2), new Rect(16, -4.5, 48, 9), 3, 3);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(251, 191, 36)), null, new Rect(16, -4.5, 5, 9));
            }
            else
            {
                var cone = new StreamGeometry();
                using (var ctx = cone.Open()) { ctx.BeginFigure(new Point(0, 0), true, true); ctx.PolyLineTo(new[] { new Point(12, -4.5), new Point(12, 4.5) }, true, false); }
                cone.Freeze();
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(226, 232, 240)), new Pen(new SolidColorBrush(Color.FromRgb(30, 41, 59)), 1.1), cone);
                dc.DrawRoundedRectangle(new SolidColorBrush(penCol), new Pen(new SolidColorBrush(Color.FromRgb(15, 23, 42)), 1.2), new Rect(12, -5, 48, 10), 2, 2);
                if (style == "dash")
                    dc.DrawLine(new Pen(Brushes.White, 2.0) { DashStyle = DashStyles.Dash }, new Point(18, 0), new Point(52, 0));
                else
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(95, 255, 255, 255)), null, new Rect(14, -3.5, 44, 2.5));
            }

            dc.Pop();
            dc.Pop();
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
                if (i % 10 == 0) { dc.DrawLine(tickPen, new Point(x, 2), new Point(x, 15)); dc.DrawText(new FormattedText((i / 10).ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, numTypeface, 9.5, new SolidColorBrush(Color.FromRgb(120, 53, 15)), dpi), new Point(x - 3, 17)); }
                else if (i % 5 == 0) dc.DrawLine(tickPen, new Point(x, 2), new Point(x, 11));
                else dc.DrawLine(tickPen, new Point(x, 2), new Point(x, 7));
            }
            dc.DrawText(new FormattedText($"{len / cmPx:0.0} cm", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, numTypeface, 12, new SolidColorBrush(Color.FromRgb(30, 58, 138)), dpi), new Point(len / 2 - 20, -20));
            dc.Pop(); dc.Pop();
        }

        private static void DrawVirtualCompass(DrawingContext dc, Point center, Point tip, double dpi, bool isIdle = false)
        {
            double r = Dist(center, tip);
            if (r < 10) return;
            Point mid = new((center.X + tip.X) / 2.0, (center.Y + tip.Y) / 2.0);
            double ux = (tip.X - center.X) / r, uy = (tip.Y - center.Y) / r;
            double nx = -uy, ny = ux;
            if (ny > 0) { nx = -nx; ny = -ny; } // Luôn hướng khớp compa lên trên
            double h = isIdle ? 68.0 : Math.Clamp(r * 1.15, 85.0, 185.0);
            Point pivot = new(mid.X + nx * h, mid.Y + ny * h);

            byte a = (byte)(isIdle ? 175 : 235);
            var legPen = new Pen(new SolidColorBrush(Color.FromArgb(a, 51, 65, 85)), 4.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var shinePen = new Pen(new SolidColorBrush(Color.FromArgb(a, 203, 213, 225)), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(legPen, pivot, center);
            dc.DrawLine(legPen, pivot, tip);
            dc.DrawLine(shinePen, pivot, center);
            dc.DrawLine(shinePen, pivot, tip);

            // Đỉnh tay cầm compa
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(a, 30, 41, 59)), 5.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                pivot, new Point(pivot.X + nx * 18, pivot.Y + ny * 18));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, 245, 158, 11)), new Pen(new SolidColorBrush(Color.FromArgb(a, 120, 53, 15)), 1.8), pivot, 7, 7);

            // Kim nhọn tại tâm O & Ngòi bút chì gắn trên chân quay
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, 220, 38, 38)), new Pen(Brushes.White, 1.2), center, 3.8, 3.8);
            DrawVirtualPen(dc, tip, Color.FromArgb(a, 37, 99, 235), "pencil", !isIdle);

            if (!isIdle)
            {
                dc.DrawText(new FormattedText($"R = {r / 38.0:0.0} cm", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 12, new SolidColorBrush(ParseColor("#BE123C")), dpi),
                    new Point(mid.X + 8, mid.Y - 18));
            }
        }

        // ================= XUẤT ẢNH =================
        private Rect? ComputeContentBBox()
        {
            var xs = new List<double>(); var ys = new List<double>();
            if (RevScene != null && RevScene.IsValid)
            {
                double a = Math.Min(RevScene.LimitA, RevScene.LimitB), b = Math.Max(RevScene.LimitA, RevScene.LimitB);
                for (int i = 0; i <= 36; i++)
                {
                    double t = a + (b - a) * i / 36.0;
                    double r = RevolutionScene.SampleR(RevScene.Curve1TR, t);
                    if (RevMode == MathRenderMode.Area2D)
                    {
                        var p1 = RevScene.ToScreen2D(t, r); var p2 = RevScene.ToScreen2D(t, 0);
                        xs.Add(p1.X); xs.Add(p2.X); ys.Add(p1.Y); ys.Add(p2.Y);
                    }
                    else
                    {
                        for (int j = 0; j <= 12; j++)
                        {
                            var p = RevScene.Project3D(t, r, (RevSweepDeg * Math.PI / 180.0) * j / 12.0, RevXCompression);
                            xs.Add(p.X); ys.Add(p.Y);
                        }
                    }
                }
            }
            foreach (var mf in MagicFills) { xs.Add(mf.BoundsDip.Left); xs.Add(mf.BoundsDip.Right); ys.Add(mf.BoundsDip.Top); ys.Add(mf.BoundsDip.Bottom); }
            foreach (var p in Scene.Points)
            {
                xs.Add(p.X); ys.Add(p.Y);
                if (ShowLabels && !string.IsNullOrEmpty(p.Label))
                {
                    // Cộng đủ bề rộng và chiều cao thực tế của chữ nhãn (Times New Roman 20px nghiêng) để không bao giờ bị cắt chữ ở mép phải/trên!
                    double lblW = Math.Max(22.0, p.Label.Length * 15.0 + 8.0);
                    double lblH = 26.0;
                    xs.Add(p.X + p.Lx);
                    xs.Add(p.X + p.Lx + lblW);
                    ys.Add(p.Y + p.Ly - 2.0);
                    ys.Add(p.Y + p.Ly + lblH);
                }
            }
            foreach (var sh in Scene.Shapes.Where(s => s.Type == "circle"))
            {
                var p1 = GetPoint(sh.P1Id); var p2 = GetPoint(sh.P2Id);
                if (p1 != null && p2 != null) { double r = Dist(new Point(p1.X, p1.Y), new Point(p2.X, p2.Y)); xs.Add(p1.X - r); xs.Add(p1.X + r); ys.Add(p1.Y - r); ys.Add(p1.Y + r); }
            }
            foreach (var st in Scene.Strokes)
            {
                double halfW = Math.Max(4.0, st.Width * 0.6);
                foreach (var pt in st.Points)
                {
                    xs.Add(pt[0] - halfW); xs.Add(pt[0] + halfW);
                    ys.Add(pt[1] - halfW); ys.Add(pt[1] + halfW);
                }
            }
            foreach (var t in Scene.Texts)
            {
                xs.Add(t.X - 4); xs.Add(t.X + Math.Max(24, t.Text.Length * (t.FontSize * 0.68)) + 8);
                ys.Add(t.Y - t.FontSize * 0.8); ys.Add(t.Y + t.FontSize * 0.8);
            }
            if (xs.Count == 0) return null;
            double pad = 22;
            return new Rect(xs.Min() - pad, ys.Min() - pad, (xs.Max() - xs.Min()) + 2 * pad, (ys.Max() - ys.Min()) + 2 * pad);
        }

        public RenderTargetBitmap? RenderExportBitmap(double scale = 2.0, bool? forceWhiteBg = null)
        {
            var bbox = ComputeContentBBox();
            if (bbox == null) return null;
            bool whiteBg = forceWhiteBg ?? ExportWhiteBg;
            int w = Math.Max(16, (int)Math.Ceiling(bbox.Value.Width * scale));
            int h = Math.Max(16, (int)Math.Ceiling(bbox.Value.Height * scale));
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                if (whiteBg) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, bbox.Value.Width, bbox.Value.Height));
                dc.PushTransform(new TranslateTransform(-bbox.Value.Left, -bbox.Value.Top));
                RenderScene(dc, cleanExport: true);
                dc.Pop();
            }
            // RenderTargetBitmap với dpi = 96 * scale sẽ tự scale DrawingVisual đúng 1 lần theo tỉ lệ `scale`
            var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(dv);
            return rtb;
        }

        private static void CopyBitmapSourceToClipboard(BitmapSource bmp, bool opaqueWhite = false)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;
            var dataObj = new DataObject();
            if (!opaqueWhite)
                dataObj.SetData("PNG", ms, false);
            dataObj.SetImage(bmp);
            Clipboard.SetDataObject(dataObj, true);
        }

        public void CopyToClipboard()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            string procUpper = LastTargetProcName.ToUpperInvariant();
            bool useWhite = TargetNeedsOpaqueWhiteBg(procUpper);
            var bmp = RenderExportBitmap(useWhite ? dpi.DpiScaleX : 2.0, forceWhiteBg: useWhite);
            if (bmp == null) { ShowToast("Chưa có hình vẽ nào để Copy!"); return; }
            CopyBitmapSourceToClipboard(bmp, opaqueWhite: useWhite);
            ShowToast($"Đã Copy ảnh vào Clipboard{(useWhite ? " (nền trắng 1:1)" : " (PNG trong suốt)")} — nhấn Ctrl+V để dán!");
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
