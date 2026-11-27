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
        public event Action<string, Point?>? ContextMenuRequested;
        public event Action<string>? ActiveToolChanged;
        public event Action<GeoMathFormula>? MathFormulaEditRequested;
        public event Action<GeoText>? TextEditRequested;
        public event Action<List<FreeStroke>>? HandwritingEditRequested;
        public event Action<SpinAxisMode>? SpinAxisStateChanged;
        public event Action<bool>? RevHandlesVisibilityChanged;

        public SceneState Scene { get; private set; } = new();
        public List<MagicFillLayer> MagicFills { get; } = new();
        public OfficeSelectionContext? OfficeContext { get; private set; }

        public CanvasBackgroundMode CanvasBg { get; set; } = CanvasBackgroundMode.Transparent;
        public List<Solid3DScene> Solids3DList { get; } = new();
        public Solid3DScene? Solid3D
        {
            get => Solids3DList.LastOrDefault();
            set
            {
                if (value == null)
                {
                    if (Solids3DList.Count > 0) Solids3DList.RemoveAt(Solids3DList.Count - 1);
                }
                else
                {
                    int idx = Solids3DList.IndexOf(value);
                    if (idx >= 0) { Solids3DList.RemoveAt(idx); Solids3DList.Add(value); }
                    else Solids3DList.Add(value);
                }
            }
        }
        public bool KeepPreviousSolids3D { get; set; } = true;
        public bool ShowCoordSystem3D { get; set; } = false;
        private readonly DispatcherTimer _netUnfoldTimer;
        private double _netTargetProgress = 0.0;
        public event Action<double>? NetProgressUpdated;
        private Solid3DScene? _activeOrbitSolid3D;
        private Solid3DScene? _activeMoveSolid3D;
        private int _draggingCutPartSide = 0; // +1 above plane, -1 below plane
        private readonly List<Vec3> _arbitraryPlanePoints = new();
        private Solid3DScene? _intersectSolidA;
        private Solid3DScene? _intersectSolidB;

        // Cached static Typefaces for zero-allocation 60fps rendering
        private static readonly Typeface TfMathItalicBold = new(new FontFamily("Times New Roman"), FontStyles.Italic, FontWeights.Bold, FontStretches.Normal);
        private static readonly Typeface TfMathItalic = new(new FontFamily("Times New Roman"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface TfUiBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Typeface TfUiSemiBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private bool _isOrbiting3D = false;
        private bool _isMoving3D = false;
        private Point _orbitLastPos;
        private readonly List<string> _step3DVertices = new();
        private string? _pending3DParallelVertex;

        // Trạng thái Khối tròn xoay 3D & Diện tích tích phân (hỗ trợ nhiều miền độc lập KHÔNG LIÊN TỤC)
        public RevolutionScene? RevScene { get; set; }
        public MathRenderMode RevMode { get; set; } = MathRenderMode.Area2D;
        public double RevSweepDeg { get; set; } = 360.0;
        public double RevXCompression { get; set; } = 0.28;
        public bool RevShowCaps { get; set; } = true;
        public bool RevShowBoundsHandles { get; set; } = true;
        public bool RevShowAreaIn3D { get; set; } = true;
        public string Area2DColorHex { get; set; } = "#EF4444";
        public string Volume3DColorHex { get; set; } = "#3B82F6";
        private int _dragRevRegionIdx = -1;
        private bool _dragRevIsEnd = false;
        private bool _isOrbitingRev3D = false;
        private readonly DispatcherTimer _spin3DAxisTimer;
        public SpinAxisMode ActiveSpinAxis { get; private set; } = SpinAxisMode.None;
        public bool IsSpinningVertical3D => ActiveSpinAxis == SpinAxisMode.Vertical;
        public bool IsSpinningHorizontal3D => ActiveSpinAxis == SpinAxisMode.Horizontal;

        private static readonly string[] VolumePalette = { "#3B82F6", "#06B6D4", "#10B981", "#8B5CF6", "#F59E0B" };
        private static readonly string[] AreaPalette   = { "#EF4444", "#38BDF8", "#FACC15", "#FB923C", "#34D399", "#EC4899" };
        private static readonly string[] SolidPalette  = { "#38BDF8", "#34D399", "#F59E0B", "#EC4899", "#A78BFA", "#EF4444" };
        private static readonly string[] StrokePalette = { "#0F172A", "#FFFFFF", "#FDE047", "#38BDF8", "#EF4444", "#10B981" };
        private int _volColorIdx = 0, _areaColorIdx = 0, _solidColorIdx = 0, _strokeCycleIdx = 0;

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
        public string PenTipStyle { get; set; } = "fountain"; // fountain | brush | chalk | pencil | dash | highlighter | eraser
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
        private string? _draggingMathId;
        private Point _dragMathOffset;
        private string? _selectedPointId;
        private string? _selectedShapeId;
        private string? _selectedMathId;
        private string? _selectedTextId;
        private long _lastDblClickTick = 0;
        private string? _lastDblClickKey;
        private Point _lastSmoothedPenPt;
        private long _lastPenTick = 0;
        // Chọn & Di chuyển toàn bộ nhóm đối tượng (Marquee / HCN / Tự do Lasso)
        private readonly HashSet<string> _selPointIds = new();
        private readonly HashSet<string> _selStrokeIds = new();
        private readonly HashSet<string> _selTextIds = new();
        private readonly HashSet<string> _selMathIds = new();
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

            _spin3DAxisTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _spin3DAxisTimer.Tick += OnSpin3DAxisTick;

            _netUnfoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _netUnfoldTimer.Tick += OnNetUnfoldTick;
        }

        private void OnNetUnfoldTick(object? sender, EventArgs e)
        {
            if (Solid3D == null) { _netUnfoldTimer.Stop(); return; }
            double cur = Solid3D.UnfoldProgress;
            double diff = _netTargetProgress - cur;
            if (Math.Abs(diff) <= 0.025)
            {
                Solid3D.UnfoldProgress = _netTargetProgress;
                _netUnfoldTimer.Stop();
            }
            else
            {
                Solid3D.UnfoldProgress = Math.Clamp(cur + Math.Sign(diff) * 0.032, 0.0, 1.0);
            }
            NetProgressUpdated?.Invoke(Solid3D.UnfoldProgress * 100.0);
            InvalidateVisual();
        }

        public void SetNetUnfoldProgress3D(double pct)
        {
            if (Solid3D == null) return;
            _netUnfoldTimer.Stop();
            Solid3D.UnfoldProgress = Math.Clamp(pct / 100.0, 0.0, 1.0);
            NetProgressUpdated?.Invoke(Solid3D.UnfoldProgress * 100.0);
            InvalidateVisual();
        }

        public void ToggleNetUnfold3D()
        {
            if (Solid3D == null)
            {
                ShowToast("Hãy tạo 1 khối 3D (Chóp, Lăng trụ, Lập phương, Hộp CN, Nón, Trụ) từ [Vẽ 3D ▾] trước!");
                return;
            }
            if (Solid3D.Kind is "sphere" or "plane_only")
            {
                ShowToast("Mặt cầu và Mặt phẳng đơn không có khai triển trải phẳng Net!");
                return;
            }
            Solid3D.CutPlane = null;
            Solid3D.ResetPartOffsets();
            _netTargetProgress = Solid3D.UnfoldProgress < 0.5 ? 1.0 : 0.0;
            _netUnfoldTimer.Start();
            RefreshSolid3DMenu();
            ShowToast(_netTargetProgress > 0.5
                ? "📦 Đang khai triển trải phẳng Net (giống GeoGebra) — kéo thanh [Net: %] để điều chỉnh góc mở!"
                : "📦 Đang gấp lại thành khối 3D ban đầu!");
        }

        public void CycleNetTopHinge3D()
        {
            if (Solid3D == null || Solid3D.Faces.Count == 0) return;
            int m = Math.Max(3, Solid3D.Faces[0].VertexIds.Count);
            Solid3D.TopCapHingeIdx = (Solid3D.TopCapHingeIdx + 1) % m;
            if (Solid3D.UnfoldProgress < 0.05) Solid3D.UnfoldProgress = 1.0;
            NetProgressUpdated?.Invoke(Solid3D.UnfoldProgress * 100.0);
            InvalidateVisual();
            ShowToast($"Đã đổi cạnh bản lề nắp trên sang cạnh #{Solid3D.TopCapHingeIdx + 1}");
        }

        private void OnSpin3DAxisTick(object? sender, EventArgs e)
        {
            if (ActiveSpinAxis == SpinAxisMode.None) { _spin3DAxisTimer.Stop(); return; }
            bool any = false;
            if (Solid3D != null)
            {
                if (ActiveSpinAxis == SpinAxisMode.Vertical)
                    Solid3D.YawDeg = (Solid3D.YawDeg + 1.35) % 360.0;
                else if (ActiveSpinAxis == SpinAxisMode.Horizontal)
                    Solid3D.PitchDeg = (Solid3D.PitchDeg + 1.35) % 360.0;
                any = true;
            }
            if (RevScene != null && RevScene.IsValid && RevMode == MathRenderMode.Solid3D)
            {
                if (ActiveSpinAxis == SpinAxisMode.Vertical)
                    RevScene.TurntableYawDeg = (RevScene.TurntableYawDeg + 1.45) % 360.0;
                else if (ActiveSpinAxis == SpinAxisMode.Horizontal)
                    RevScene.TurntablePitchDeg = (RevScene.TurntablePitchDeg + 1.45) % 360.0;
                any = true;
            }
            if (!any) { SetSpinAxis3D(SpinAxisMode.None); return; }
            InvalidateVisual();
        }

        public void SetSpinAxis3D(SpinAxisMode mode)
        {
            if (mode != SpinAxisMode.None && Solid3D == null && (RevScene == null || !RevScene.IsValid))
            {
                ShowToast("Hãy tạo 1 khối 3D từ [Vẽ 3D ▾] hoặc bật [Quay V] khối tròn xoay trước!");
                return;
            }
            if (mode != SpinAxisMode.None && Solid3D == null && RevScene != null && RevScene.IsValid && RevMode != MathRenderMode.Solid3D)
            {
                RevMode = MathRenderMode.Solid3D;
                if (RevSweepDeg < 30.0) RevSweepDeg = 360.0;
            }
            ActiveSpinAxis = mode;
            if (mode != SpinAxisMode.None) _spin3DAxisTimer.Start(); else _spin3DAxisTimer.Stop();
            SpinAxisStateChanged?.Invoke(mode);
            if (Solid3D != null) RefreshSolid3DMenu();
            else if (RevScene != null && RevScene.IsValid) RefreshRevMenu();
            InvalidateVisual();
            ShowToast(mode switch
            {
                SpinAxisMode.Vertical => "⟳ Đang quay khối 3D quanh TRỤC THẲNG ĐỨNG (bấm lại để dừng)!",
                SpinAxisMode.Horizontal => "⇵ Đang quay khối 3D quanh TRỤC NẰM NGANG (bấm lại để dừng)!",
                _ => "⏸ Đã dừng quay khối 3D."
            });
        }

        public void ToggleVerticalSpin3D() => SetSpinAxis3D(ActiveSpinAxis == SpinAxisMode.Vertical ? SpinAxisMode.None : SpinAxisMode.Vertical);
        public void ToggleHorizontalSpin3D() => SetSpinAxis3D(ActiveSpinAxis == SpinAxisMode.Horizontal ? SpinAxisMode.None : SpinAxisMode.Horizontal);
        public void SetVerticalSpin3D(bool enabled) => SetSpinAxis3D(enabled ? SpinAxisMode.Vertical : SpinAxisMode.None);

        public void SetRevBoundsHandlesVisible(bool visible)
        {
            RevShowBoundsHandles = visible;
            RevHandlesVisibilityChanged?.Invoke(visible);
            if (RevScene != null && RevScene.IsValid) RefreshRevMenu();
            InvalidateVisual();
            ShowToast(visible ? "👁 Đã hiện các nút cận (a, b, c...) trên trục" : "👁 Đã ẩn các nút cận (màu cam) trên trục!");
        }

        public void ToggleRevBoundsHandles() => SetRevBoundsHandlesVisible(!RevShowBoundsHandles);

        public void ResetRevTurntable()
        {
            if (RevScene == null) return;
            if (Solid3D == null && ActiveSpinAxis != SpinAxisMode.None) SetSpinAxis3D(SpinAxisMode.None);
            RevScene.TurntableYawDeg = 0.0;
            RevScene.TurntablePitchDeg = 0.0;
            InvalidateVisual();
            RefreshRevMenu();
            ShowToast("↺ Đã đưa khối tròn xoay về góc chuẩn 0° khớp với hình gốc!");
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

        public void UpdateOfficeOriginSync()
        {
            try { OfficeComBridge.SurfaceOriginScreenPx = PointToScreen(new Point(0, 0)); }
            catch { OfficeComBridge.SurfaceOriginScreenPx = new Point(0, 0); }
        }

        public void NotifyMetrics()
        {
            if (RevScene != null && RevScene.IsValid)
            {
                var regs = RevScene.ComputeRegionMetrics();
                var (s, v, vPi) = RevScene.ComputeMetrics();
                if (regs.Count > 1)
                {
                    string parts = string.Join(" | ", regs.Select(r => $"S{r.index + 1}={r.areaCm2:0.00}"));
                    MetricsUpdated?.Invoke($"{parts} | Tổng S={s:0.00} cm² | V={vPi:0.00}π");
                }
                else MetricsUpdated?.Invoke($"S = {s:0.00} cm²  |  V = {vPi:0.00}π ≈ {v:0.00} cm³");
            }
            else MetricsUpdated?.Invoke("Đọc Office -> [+ Cận/Miền] tạo nhiều miền (kể cả rời nhau)");
        }

        public void RemoveRevolutionRegion()
        {
            if (RevScene == null || !RevScene.IsValid) return;
            if (RevScene.RemoveActiveRegion()) { NotifyMetrics(); InvalidateVisual(); RefreshRevMenu(); ShowToast($"Đã xóa 1 miền (còn {RevScene.Regions.Count} miền)."); }
            else ShowToast("Cần giữ tối thiểu 1 miền tô!");
        }

        public string CycleArea2DColor()
        {
            _areaColorIdx = (_areaColorIdx + 1) % AreaPalette.Length;
            Area2DColorHex = AreaPalette[_areaColorIdx];
            if (RevScene != null && RevScene.IsValid) RevScene.SetActiveRegionColor(Area2DColorHex);
            InvalidateVisual(); RefreshRevMenu(); ShowToast($"Màu Diện tích (S): {Area2DColorHex}");
            return Area2DColorHex;
        }

        public string CycleVolume3DColor()
        {
            _volColorIdx = (_volColorIdx + 1) % VolumePalette.Length;
            Volume3DColorHex = VolumePalette[_volColorIdx];
            InvalidateVisual(); RefreshRevMenu(); ShowToast($"Màu Thể tích 3D (V): {Volume3DColorHex}");
            return Volume3DColorHex;
        }

        public void SetCanvasBackground(CanvasBackgroundMode mode)
        {
            CanvasBg = mode;
            if (mode is CanvasBackgroundMode.ChalkboardGrid or CanvasBackgroundMode.Blackboard)
            {
                if (StrokeColorHex.Equals("#0F172A", StringComparison.OrdinalIgnoreCase)) StrokeColorHex = "#F8FAFC";
            }
            else
            {
                if (StrokeColorHex.Equals("#F8FAFC", StringComparison.OrdinalIgnoreCase) || StrokeColorHex.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase)) StrokeColorHex = "#0F172A";
            }
            InvalidateVisual();
            ShowToast("Đã đổi nền: " + mode switch { CanvasBackgroundMode.ChalkboardGrid => "Bảng xanh kẻ ô", CanvasBackgroundMode.WhiteboardPlain => "Bảng trắng", CanvasBackgroundMode.WhiteboardGrid => "Giấy ô ly", CanvasBackgroundMode.Blackboard => "Bảng đen", _ => "Trong suốt (Mặc định)" });
        }

        public void SpawnInteractiveSolid3D(string presetKind)
        {
            double cx = ActualWidth > 100 ? ActualWidth * 0.45 : 480;
            double cy = ActualHeight > 100 ? ActualHeight * 0.54 : 420;
            int idx = Solids3DList.Count;
            double ox = KeepPreviousSolids3D ? ((idx % 4) - 1.5) * 145.0 : 0.0;
            double oy = KeepPreviousSolids3D ? ((idx / 4) % 2) * 35.0 : 0.0;
            if (!KeepPreviousSolids3D) Solids3DList.Clear();

            var created = Solid3DScene.CreatePreset(presetKind, new Point(cx + ox, cy + oy));
            Solids3DList.Add(created);
            NetProgressUpdated?.Invoke(0.0);
            _step3DVertices.Clear(); _pending3DParallelVertex = null; _arbitraryPlanePoints.Clear();
            SetTool("select"); InvalidateVisual(); RefreshSolid3DMenu();
            ShowToast(KeepPreviousSolids3D && Solids3DList.Count > 1
                ? $"Đã thêm {created.Name} (giữ {Solids3DList.Count} khối 3D trên màn hình)! Kéo di chuyển hoặc chọn [Giao 2 khối]."
                : $"Đã tạo {created.Name}! Kéo trái di chuyển, kéo phải xoay 3D, hoặc bấm nút trên menu nổi.", 4500);
        }

        public void StopAllTimers()
        {
            _toastTimer.Stop();
            _animDrawTimer.Stop();
            _spin3DAxisTimer.Stop();
            _netUnfoldTimer.Stop();
        }

        public void ClearAllSolids3D()
        {
            _netUnfoldTimer.Stop();
            Solids3DList.Clear();
            _intersectSolidA = null;
            _intersectSolidB = null;
            _arbitraryPlanePoints.Clear();
            _step3DVertices.Clear();
            _pending3DParallelVertex = null;
            if (RevScene == null) SetSpinAxis3D(SpinAxisMode.None);
            NetProgressUpdated?.Invoke(0.0);
            ContextMenuRequested?.Invoke("", null);
            InvalidateVisual();
            ShowToast("Đã xóa toàn bộ các khối 3D");
        }

        public void ToggleCoordSystem3D()
        {
            ShowCoordSystem3D = !ShowCoordSystem3D;
            InvalidateVisual();
            ShowToast(ShowCoordSystem3D ? "🌐 Đã bật Hệ tọa độ không gian Oxyz" : "🌐 Đã ẩn Hệ tọa độ không gian Oxyz");
        }



        public void IntersectActiveSolids3D()
        {
            if (Solids3DList.Count < 2)
            {
                ShowToast("Cần có ít nhất 2 khối 3D trên màn hình để tính giao khối! Hãy tạo thêm khối khác (giữ khối cũ).", 4200);
                return;
            }
            var a = Solids3DList[^1];
            var b = Solids3DList[^2];
            if (_intersectSolidA == a && _intersectSolidB == b)
            {
                _intersectSolidA = null; _intersectSolidB = null;
                InvalidateVisual(); RefreshSolid3DMenu();
                ShowToast("Đã tắt hiển thị vùng giao 2 khối 3D.");
                return;
            }
            _intersectSolidA = a;
            _intersectSolidB = b;
            var pts = Solid3DScene.ComputeSolidIntersectionPoints(a, b);
            InvalidateVisual(); RefreshSolid3DMenu();
            ShowToast(pts.Count > 0
                ? $"✓ Đang bật giao trực tiếp {a.Name} ∩ {b.Name} ({pts.Count} điểm giao — kéo/xoay khối để xem cập nhật theo thời gian thực)!"
                : $"Đã gắn theo dõi giao {a.Name} ∩ {b.Name} — hãy kéo 2 khối chồng lên nhau để thấy đường giao tự động xuất hiện!", 4800);
        }

        public void ToggleSolid3DShaded()
        {
            if (Solid3D == null) return;
            Solid3D.IsSolidShaded = !Solid3D.IsSolidShaded;
            InvalidateVisual(); RefreshSolid3DMenu();
            ShowToast(Solid3D.IsSolidShaded ? "Đã bật chế độ Khối đặc có màu 3D" : "Đã chuyển về chế độ Khung dây 3D");
        }

        public void CycleSolid3DColor()
        {
            if (Solid3D == null) return;
            _solidColorIdx = (_solidColorIdx + 1) % SolidPalette.Length;
            Solid3D.SolidColorHex = SolidPalette[_solidColorIdx];
            Solid3D.IsSolidShaded = true;
            InvalidateVisual(); RefreshSolid3DMenu();
            ShowToast($"Đã đổi màu khối 3D: {Solid3D.SolidColorHex}");
        }

        public void ScaleSolid3D(double factor) { if (Solid3D == null) return; Solid3D.Scale = Math.Clamp(Solid3D.Scale * factor, 55.0, 290.0); InvalidateVisual(); RefreshSolid3DMenu(); }
        public void DeleteSolid3D()
        {
            if (Solids3DList.Count == 0) return;
            var removed = Solids3DList[^1];
            Solids3DList.RemoveAt(Solids3DList.Count - 1);
            _netUnfoldTimer.Stop();
            if (_intersectSolidA == removed || _intersectSolidB == removed) { _intersectSolidA = null; _intersectSolidB = null; }
            if (Solids3DList.Count == 0 && RevScene == null) SetSpinAxis3D(SpinAxisMode.None);
            _step3DVertices.Clear(); _pending3DParallelVertex = null;
            NetProgressUpdated?.Invoke((Solid3D?.UnfoldProgress ?? 0.0) * 100.0);
            if (Solids3DList.Count > 0) RefreshSolid3DMenu();
            else ContextMenuRequested?.Invoke("", null);
            InvalidateVisual();
            ShowToast(Solids3DList.Count > 0 ? $"Đã xóa 1 khối 3D (còn {Solids3DList.Count} khối)" : "Đã xóa khối 3D");
        }

        public void SpawnInteractiveCutPlane3D()
        {
            if (Solid3D == null) { ShowToast("Hãy tạo 1 khối 3D (Nón, Trụ, Cầu hoặc Đa diện) từ [Vẽ 3D ▾] trước!"); return; }
            Solid3D.InitInteractiveCutPlane(FillColorHex);
            InvalidateVisual(); RefreshSolid3DMenu();
            ShowToast("Đã tạo Mặt phẳng cắt (P)! Dùng nút ⬆/⬇, ⟲/⟳, ⊥ Dọc trên menu nổi và thanh [Tách] để trình chiếu!", 4500);
        }

        public void NudgeCutPlane3D(double delta)
        {
            if (Solid3D?.CutPlane == null) return;
            Solid3D.NudgeCutPlane(delta);
            InvalidateVisual(); RefreshSolid3DMenu();
        }

        public void TiltCutPlane3D(double deltaDeg)
        {
            if (Solid3D?.CutPlane == null) return;
            Solid3D.TiltCutPlane(deltaDeg);
            InvalidateVisual(); RefreshSolid3DMenu();
        }

        public void SetCutPlaneVertical90()
        {
            if (Solid3D == null) return;
            Solid3D.SetCutPlaneVertical90();
            InvalidateVisual(); RefreshSolid3DMenu();
            string info = Solid3D.Kind switch
            {
                "cone" => "Mặt phẳng (P) cắt dọc 90° qua đỉnh S → Thiết diện Tam giác cân!",
                "cylinder" => "Mặt phẳng (P) cắt dọc 90° qua trục → Thiết diện Hình chữ nhật!",
                "sphere" => "Mặt phẳng (P) cắt dọc 90° qua tâm O → Thiết diện Đường tròn lớn!",
                _ => "Đã dựng Mặt phẳng (P) cắt dọc 90° qua tâm khối đa diện!"
            };
            ShowToast(info, 3800);
        }

        public void ToggleCutPlaneVisible()
        {
            if (Solid3D?.CutPlane == null) return;
            Solid3D.CutPlane.Visible = !Solid3D.CutPlane.Visible;
            InvalidateVisual(); RefreshSolid3DMenu();
            ShowToast(Solid3D.CutPlane.Visible ? "Đã hiện tấm Mặt phẳng (P)" : "Đã ẩn tấm Mặt phẳng (P) (vẫn giữ thiết diện)");
        }

        public void RemoveCutPlane3D()
        {
            if (Solid3D == null) return;
            Solid3D.CutPlane = null;
            Solid3D.ResetPartOffsets();
            InvalidateVisual(); RefreshSolid3DMenu();
            ShowToast("Đã gỡ Mặt phẳng cắt (P) & ghép khít khối");
        }

        public void ResetSolidPartOffsets()
        {
            if (Solid3D == null) return;
            Solid3D.ResetPartOffsets();
            InvalidateVisual(); RefreshSolid3DMenu();
            ShowToast("↺ Đã ghép khít các phần đã tách của khối 3D");
        }

        // ================= TEXT-MATH & CHỮ VIẾT TAY TƯƠNG TÁC =================
        public void AddOrUpdateMathFormula(string? existingId, string latex, string colorHex, double fontSize, bool forceMath)
        {
            SaveHistory();
            if (!string.IsNullOrEmpty(existingId))
            {
                var found = Scene.MathFormulas.FirstOrDefault(m => m.Id == existingId);
                if (found != null)
                {
                    found.Latex = latex;
                    found.ColorHex = colorHex;
                    found.FontSize = fontSize;
                    found.ForceMath = forceMath;
                    _selectedMathId = found.Id;
                    InvalidateVisual(); RefreshMathMenu();
                    ShowToast("✓ Đã cập nhật công thức toán!");
                    return;
                }
            }
            double cx = ActualWidth > 200 ? ActualWidth * 0.42 : 420;
            double cy = ActualHeight > 200 ? ActualHeight * 0.36 : 260;
            int offset = (Scene.MathFormulas.Count % 6) * 24;
            var mf = new GeoMathFormula
            {
                Id = GenId("math"),
                X = cx + offset,
                Y = cy + offset,
                Latex = latex,
                ColorHex = colorHex,
                FontSize = fontSize,
                ForceMath = forceMath
            };
            Scene.MathFormulas.Add(mf);
            ClearGroupSelection();
            _selectedMathId = mf.Id;
            _selMathIds.Add(mf.Id);
            SetTool("select");
            InvalidateVisual(); RefreshMathMenu();
            ShowToast("Đã chèn công thức lên bảng vẽ! Kéo để di chuyển, nháy đúp để sửa, bấm [Chèn App] để đưa vào Word/App.", 4200);
        }

        public void AddHandwritingStrokes(List<FreeStroke> padStrokes)
        {
            if (padStrokes.Count == 0) return;
            SaveHistory();
            double minX = double.MaxValue, minY = double.MaxValue;
            foreach (var st in padStrokes) foreach (var pt in st.Points) { if (pt[0] < minX) minX = pt[0]; if (pt[1] < minY) minY = pt[1]; }
            double baseX = ActualWidth > 200 ? ActualWidth * 0.38 : 360;
            double baseY = ActualHeight > 200 ? ActualHeight * 0.36 : 260;
            string grpId = GenId("hw");
            ClearGroupSelection();
            foreach (var st in padStrokes)
            {
                var clone = new FreeStroke
                {
                    Id = GenId("strk"),
                    GroupId = grpId,
                    ColorHex = st.ColorHex,
                    Width = st.Width,
                    Opacity = st.Opacity,
                    IsHighlighter = st.IsHighlighter,
                    PenStyle = st.PenStyle,
                    PointWidths = new List<double>(st.PointWidths),
                    Points = st.Points.Select(p => new[] { baseX + (p[0] - minX), baseY + (p[1] - minY) }).ToList()
                };
                Scene.Strokes.Add(clone);
                _selStrokeIds.Add(clone.Id);
            }
            SetTool("select");
            InvalidateVisual(); RefreshHandwritingMenu();
            ShowToast("Đã chèn chữ viết tay lên bảng vẽ! Kéo để đặt vị trí, nháy đúp để sửa nét, bấm [Chèn App] để dán.", 4200);
        }

        public void ReplaceHandwritingCluster(List<string> oldStrokeIds, List<FreeStroke> updatedStrokes)
        {
            SaveHistory();
            var oldSet = new HashSet<string>(oldStrokeIds);
            Scene.Strokes.RemoveAll(s => oldSet.Contains(s.Id));
            ClearGroupSelection();
            string grpId = updatedStrokes.FirstOrDefault(s => !string.IsNullOrEmpty(s.GroupId))?.GroupId ?? GenId("hw");
            if (string.IsNullOrEmpty(grpId)) grpId = GenId("hw");
            foreach (var st in updatedStrokes)
            {
                if (st.Points.Count == 0) continue;
                var clone = new FreeStroke
                {
                    Id = GenId("strk"),
                    GroupId = grpId,
                    ColorHex = st.ColorHex,
                    Width = st.Width,
                    Opacity = st.Opacity,
                    IsHighlighter = st.IsHighlighter,
                    PenStyle = st.PenStyle,
                    PointWidths = new List<double>(st.PointWidths),
                    Points = st.Points.Select(p => new[] { p[0], p[1] }).ToList()
                };
                Scene.Strokes.Add(clone);
                _selStrokeIds.Add(clone.Id);
            }
            SetTool("select");
            InvalidateVisual();
            if (_selStrokeIds.Count > 0) RefreshHandwritingMenu();
            else ContextMenuRequested?.Invoke("", null);
            ShowToast("✓ Đã cập nhật chữ viết tay tại đúng vị trí cũ!");
        }

        public void UpdateTextItem(string textId, string newText, string colorHex, double fontSize, bool upgradeToMath)
        {
            var found = Scene.Texts.FirstOrDefault(t => t.Id == textId);
            if (found == null)
            {
                AddOrUpdateMathFormula(null, newText, colorHex, fontSize, upgradeToMath);
                return;
            }
            SaveHistory();
            if (upgradeToMath)
            {
                double x = found.X, y = found.Y - fontSize * 0.6;
                Scene.Texts.Remove(found);
                var mf = new GeoMathFormula
                {
                    Id = GenId("math"),
                    X = x,
                    Y = y,
                    Latex = newText,
                    ColorHex = colorHex,
                    FontSize = fontSize,
                    ForceMath = true
                };
                Scene.MathFormulas.Add(mf);
                ClearGroupSelection();
                _selectedMathId = mf.Id;
                _selMathIds.Add(mf.Id);
                SetTool("select");
                InvalidateVisual(); RefreshMathMenu();
                ShowToast("✓ Đã nâng cấp văn bản thành Công thức Toán!");
                return;
            }
            found.Text = newText;
            found.ColorHex = colorHex;
            found.FontSize = fontSize;
            ClearGroupSelection();
            _selectedTextId = found.Id;
            _selTextIds.Add(found.Id);
            SetTool("select");
            InvalidateVisual(); RefreshTextMenu();
            ShowToast("✓ Đã cập nhật nội dung văn bản!");
        }

        public void DeleteMathFormulaById(string id)
        {
            SaveHistory();
            Scene.MathFormulas.RemoveAll(m => m.Id == id);
            ClearGroupSelection();
            ContextMenuRequested?.Invoke("", null);
            InvalidateVisual();
            ShowToast("Đã xóa công thức toán!");
        }

        public void DeleteTextById(string id)
        {
            SaveHistory();
            Scene.Texts.RemoveAll(t => t.Id == id);
            ClearGroupSelection();
            ContextMenuRequested?.Invoke("", null);
            InvalidateVisual();
            ShowToast("Đã xóa dòng chữ!");
        }

        public void DeleteStrokesByIds(List<string> ids)
        {
            SaveHistory();
            var set = new HashSet<string>(ids);
            Scene.Strokes.RemoveAll(s => set.Contains(s.Id));
            ClearGroupSelection();
            ContextMenuRequested?.Invoke("", null);
            InvalidateVisual();
            ShowToast("Đã xóa cụm chữ viết tay!");
        }

        public void InsertFormulaDirectlyToApp(string latex, string colorHex, double fontSize, bool forceMath)
        {
            AddOrUpdateMathFormula(null, latex, colorHex, fontSize, forceMath);
            InsertIntoTargetApp();
        }

        public void InsertStrokesDirectlyToApp(List<FreeStroke> padStrokes)
        {
            AddHandwritingStrokes(padStrokes);
            InsertIntoTargetApp();
        }

        public void EditSelectedMathFormula()
        {
            var mf = Scene.MathFormulas.FirstOrDefault(m => m.Id == _selectedMathId || _selMathIds.Contains(m.Id));
            if (mf != null) MathFormulaEditRequested?.Invoke(mf);
        }

        public void ScaleSelectedMathFormula(double deltaPx)
        {
            var mf = Scene.MathFormulas.FirstOrDefault(m => m.Id == _selectedMathId || _selMathIds.Contains(m.Id));
            if (mf == null) return;
            SaveHistory();
            mf.FontSize = Math.Clamp(mf.FontSize + deltaPx, 12.0, 72.0);
            InvalidateVisual(); RefreshMathMenu();
        }

        public void CycleSelectedMathColor()
        {
            var mf = Scene.MathFormulas.FirstOrDefault(m => m.Id == _selectedMathId || _selMathIds.Contains(m.Id));
            if (mf == null) return;
            _strokeCycleIdx = (_strokeCycleIdx + 1) % StrokePalette.Length;
            SaveHistory();
            mf.ColorHex = StrokePalette[_strokeCycleIdx];
            InvalidateVisual(); RefreshMathMenu();
        }

        private Rect GetMathFormulaRect(GeoMathFormula mf)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var (w, h) = LatexMathRenderer.Measure(mf.Latex, mf.FontSize, dpi, mf.ForceMath);
            return new Rect(mf.X, mf.Y, w, h);
        }

        private GeoMathFormula? FindNearestMathFormula(Point pos)
        {
            for (int i = Scene.MathFormulas.Count - 1; i >= 0; i--)
            {
                var r = GetMathFormulaRect(Scene.MathFormulas[i]);
                r.Inflate(6, 6);
                if (r.Contains(pos)) return Scene.MathFormulas[i];
            }
            return null;
        }

        private void RefreshMathMenu()
        {
            var mf = Scene.MathFormulas.FirstOrDefault(m => m.Id == _selectedMathId || _selMathIds.Contains(m.Id));
            if (mf == null) return;
            var r = GetMathFormulaRect(mf);
            ContextMenuRequested?.Invoke("math", new Point(r.Left, r.Top));
        }

        public void EditSelectedText()
        {
            var txt = Scene.Texts.FirstOrDefault(t => t.Id == _selectedTextId || _selTextIds.Contains(t.Id));
            if (txt != null) TextEditRequested?.Invoke(txt);
        }

        public void ScaleSelectedText(double deltaPx)
        {
            var txt = Scene.Texts.FirstOrDefault(t => t.Id == _selectedTextId || _selTextIds.Contains(t.Id));
            if (txt == null) return;
            SaveHistory();
            txt.FontSize = Math.Clamp(txt.FontSize + deltaPx, 12.0, 72.0);
            InvalidateVisual(); RefreshTextMenu();
        }

        public void CycleSelectedTextColor()
        {
            var txt = Scene.Texts.FirstOrDefault(t => t.Id == _selectedTextId || _selTextIds.Contains(t.Id));
            if (txt == null) return;
            _strokeCycleIdx = (_strokeCycleIdx + 1) % StrokePalette.Length;
            SaveHistory();
            txt.ColorHex = StrokePalette[_strokeCycleIdx];
            InvalidateVisual(); RefreshTextMenu();
        }

        public void EditSelectedHandwriting()
        {
            var strokes = Scene.Strokes.Where(s => _selStrokeIds.Contains(s.Id)).ToList();
            if (strokes.Count > 0) HandwritingEditRequested?.Invoke(strokes);
        }

        private Rect GetTextRect(GeoText t)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var tf = new Typeface(new FontFamily("Times New Roman"), FontStyles.Italic, FontWeights.Bold, FontStretches.Normal);
            var ft = new FormattedText(t.Text ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, t.FontSize, Brushes.Black, dpi);
            return new Rect(t.X, t.Y - ft.Height * 0.5, Math.Max(22.0, ft.Width), Math.Max(18.0, ft.Height));
        }

        private static Rect GetStrokeBounds(FreeStroke st)
        {
            if (st.Points.Count == 0) return Rect.Empty;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in st.Points)
            {
                if (p[0] < minX) minX = p[0]; if (p[1] < minY) minY = p[1];
                if (p[0] > maxX) maxX = p[0]; if (p[1] > maxY) maxY = p[1];
            }
            double pad = Math.Max(4.0, st.Width * 0.6);
            return new Rect(minX - pad, minY - pad, (maxX - minX) + 2 * pad, (maxY - minY) + 2 * pad);
        }

        private FreeStroke? FindNearestStroke(Point pos, double radius = 14.0)
        {
            FreeStroke? best = null;
            double bestD = radius;
            for (int i = Scene.Strokes.Count - 1; i >= 0; i--)
            {
                var st = Scene.Strokes[i];
                if (st.Points.Count == 0) continue;
                double tol = Math.Max(bestD, st.Width * 0.65 + 6.0);
                double d = double.MaxValue;
                if (st.Points.Count == 1)
                {
                    d = Dist(pos, new Point(st.Points[0][0], st.Points[0][1]));
                }
                else
                {
                    for (int k = 0; k < st.Points.Count - 1; k++)
                    {
                        double sd = PointToSegmentDist(pos, new Point(st.Points[k][0], st.Points[k][1]), new Point(st.Points[k + 1][0], st.Points[k + 1][1]));
                        if (sd < d) d = sd;
                    }
                }
                if (d <= tol && d < bestD) { bestD = d; best = st; }
            }
            return best;
        }

        private List<FreeStroke> GetStrokeCluster(FreeStroke seed)
        {
            if (!string.IsNullOrEmpty(seed.GroupId))
                return Scene.Strokes.Where(s => s.GroupId == seed.GroupId).ToList();

            var visited = new HashSet<string> { seed.Id };
            var queue = new Queue<FreeStroke>();
            queue.Enqueue(seed);
            var result = new List<FreeStroke> { seed };

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                var curB = GetStrokeBounds(cur);
                if (curB.IsEmpty) continue;
                curB.Inflate(24.0, 16.0);
                foreach (var other in Scene.Strokes)
                {
                    if (visited.Contains(other.Id) || !string.IsNullOrEmpty(other.GroupId)) continue;
                    var ob = GetStrokeBounds(other);
                    if (!ob.IsEmpty && curB.IntersectsWith(ob))
                    {
                        visited.Add(other.Id);
                        queue.Enqueue(other);
                        result.Add(other);
                    }
                }
            }
            return result;
        }

        private void RefreshTextMenu()
        {
            var txt = Scene.Texts.FirstOrDefault(t => t.Id == _selectedTextId || _selTextIds.Contains(t.Id));
            if (txt == null) return;
            var r = GetTextRect(txt);
            ContextMenuRequested?.Invoke("text", new Point(r.Left, r.Top));
        }

        private void RefreshHandwritingMenu()
        {
            var b = GetGroupSelectionBounds();
            if (b.HasValue) ContextMenuRequested?.Invoke("handwriting", new Point(b.Value.Left, b.Value.Top));
        }

        private void RefreshActiveSelectionMenu()
        {
            if (_selMathIds.Count == 1 && _selPointIds.Count == 0 && _selStrokeIds.Count == 0 && _selTextIds.Count == 0) RefreshMathMenu();
            else if (_selTextIds.Count == 1 && _selPointIds.Count == 0 && _selStrokeIds.Count == 0 && _selMathIds.Count == 0) RefreshTextMenu();
            else if (_selStrokeIds.Count > 0 && _selPointIds.Count == 0 && _selTextIds.Count == 0 && _selMathIds.Count == 0) RefreshHandwritingMenu();
            else Refresh2DMenu();
        }

        public void CycleSelected2DStrokeColor()
        {
            _strokeCycleIdx = (_strokeCycleIdx + 1) % StrokePalette.Length;
            string hex = StrokePalette[_strokeCycleIdx];
            SaveHistory();
            foreach (var sh in Scene.Shapes) if (sh.Id == _selectedShapeId || (_selPointIds.Contains(sh.P1Id) && _selPointIds.Contains(sh.P2Id))) sh.ColorHex = hex;
            foreach (var st in Scene.Strokes) if (_selStrokeIds.Contains(st.Id)) st.ColorHex = hex;
            foreach (var t in Scene.Texts) if (_selTextIds.Contains(t.Id)) t.ColorHex = hex;
            InvalidateVisual();
        }

        public void ToggleSelected2DDash()
        {
            SaveHistory();
            foreach (var sh in Scene.Shapes) if (sh.Id == _selectedShapeId || (_selPointIds.Contains(sh.P1Id) && _selPointIds.Contains(sh.P2Id))) sh.DashStyle = sh.DashStyle == "dashed" ? "solid" : "dashed";
            InvalidateVisual();
        }

        public void FillSelected2DLoop()
        {
            if (_selPointIds.Count < 3) { ShowToast("Chọn hình đa giác (ít nhất 3 đỉnh) để tô màu!"); return; }
            var pts = _selPointIds.Select(GetPoint).Where(p => p != null).ToList(); if (pts.Count < 3) return;
            double cx = pts.Average(p => p!.X), cy = pts.Average(p => p!.Y);
            var orderedIds = pts.OrderBy(p => Math.Atan2(p!.Y - cy, p!.X - cx)).Select(p => p!.Id).ToList();
            SaveHistory();
            Scene.Regions.Add(new GeoRegion { Id = GenId("reg"), Type = "polygon", PointIds = orderedIds, ColorHex = FillColorHex, Opacity = FillOpacity, Pattern = FillPattern });
            InvalidateVisual(); ShowToast("Đã tô màu hình đang chọn!");
        }

        public void DuplicateSelected2D()
        {
            if (!HasGroupSelection()) return;
            SaveHistory();
            var idMap = new Dictionary<string, string>(); var newSelPts = new HashSet<string>();
            foreach (var pid in _selPointIds.ToList())
            {
                var p = GetPoint(pid); if (p == null) continue;
                var np = new GeoPoint { Id = GenId("pt"), X = p.X + 28, Y = p.Y + 24, Label = string.IsNullOrEmpty(p.Label) ? "" : NextLabel(), Lx = p.Lx, Ly = p.Ly };
                Scene.Points.Add(np); idMap[pid] = np.Id; newSelPts.Add(np.Id);
            }
            foreach (var sh in Scene.Shapes.ToList()) if (idMap.ContainsKey(sh.P1Id) && idMap.ContainsKey(sh.P2Id)) Scene.Shapes.Add(new GeoShape { Id = GenId("sh"), Type = sh.Type, P1Id = idMap[sh.P1Id], P2Id = idMap[sh.P2Id], ColorHex = sh.ColorHex, StrokeWidth = sh.StrokeWidth, DashStyle = sh.DashStyle });
            foreach (var reg in Scene.Regions.ToList()) if (reg.PointIds.All(pid => idMap.ContainsKey(pid))) Scene.Regions.Add(new GeoRegion { Id = GenId("reg"), Type = reg.Type, PointIds = reg.PointIds.Select(pid => idMap[pid]).ToList(), ColorHex = reg.ColorHex, Opacity = reg.Opacity, Pattern = reg.Pattern });
            var newSelTexts = new HashSet<string>();
            foreach (var tid in _selTextIds.ToList())
            {
                var t = Scene.Texts.FirstOrDefault(x => x.Id == tid); if (t == null) continue;
                var nt = new GeoText { Id = GenId("txt"), X = t.X + 24, Y = t.Y + 24, Text = t.Text, ColorHex = t.ColorHex, FontSize = t.FontSize };
                Scene.Texts.Add(nt); newSelTexts.Add(nt.Id);
            }
            var newSelMath = new HashSet<string>();
            foreach (var mid in _selMathIds.ToList())
            {
                var m = Scene.MathFormulas.FirstOrDefault(x => x.Id == mid); if (m == null) continue;
                var nm = new GeoMathFormula { Id = GenId("math"), X = m.X + 24, Y = m.Y + 24, Latex = m.Latex, ColorHex = m.ColorHex, FontSize = m.FontSize, ForceMath = m.ForceMath, LayerBelow = m.LayerBelow };
                Scene.MathFormulas.Add(nm); newSelMath.Add(nm.Id);
            }
            var newSelStrokes = new HashSet<string>();
            string newHwGrp = _selStrokeIds.Count > 0 ? GenId("hw") : "";
            foreach (var sid in _selStrokeIds.ToList())
            {
                var st = Scene.Strokes.FirstOrDefault(x => x.Id == sid); if (st == null) continue;
                var ns = new FreeStroke
                {
                    Id = GenId("strk"), GroupId = newHwGrp, ColorHex = st.ColorHex, Width = st.Width, Opacity = st.Opacity,
                    IsHighlighter = st.IsHighlighter, PenStyle = st.PenStyle, PointWidths = new List<double>(st.PointWidths),
                    Points = st.Points.Select(p => new[] { p[0] + 24, p[1] + 24 }).ToList()
                };
                Scene.Strokes.Add(ns); newSelStrokes.Add(ns.Id);
            }
            _selPointIds.Clear(); foreach (var id in newSelPts) _selPointIds.Add(id);
            _selTextIds.Clear(); foreach (var id in newSelTexts) _selTextIds.Add(id);
            _selMathIds.Clear(); foreach (var id in newSelMath) _selMathIds.Add(id);
            _selStrokeIds.Clear(); foreach (var id in newSelStrokes) _selStrokeIds.Add(id);
            _selectedMathId = newSelMath.FirstOrDefault();
            _selectedTextId = newSelTexts.FirstOrDefault();
            InvalidateVisual(); RefreshActiveSelectionMenu(); ShowToast("Đã nhân bản đối tượng đang chọn!");
        }

        private Rect GetSolid3DBoundsFor(Solid3DScene? sc)
        {
            if (sc == null) return Rect.Empty;
            var xs = new List<double>(); var ys = new List<double>();
            if (sc.IsNetUnfolded)
            {
                foreach (var (pts3d, _, _, _) in sc.ComputeNetFacePolygons3D())
                    foreach (var p3 in pts3d) { Point p = sc.Project(p3, out _); xs.Add(p.X); ys.Add(p.Y); }
            }
            else if (sc.Kind == "sphere")
            {
                Vec3 offSph = sc.GetPartOffset(sc.GetPartSideSign(new Vec3(0, 0, 0)));
                Point c = sc.Project(offSph, out _);
                double r = sc.Radius * sc.Scale;
                xs.Add(c.X - r); xs.Add(c.X + r); ys.Add(c.Y - r); ys.Add(c.Y + r);
            }
            else
            {
                if (sc.Kind == "poly" && sc.HasActiveSplit)
                {
                    foreach (var (pts3d, _, _, _) in sc.ComputeSplitPolyHalves3D())
                        foreach (var p3 in pts3d) { Point p = sc.Project(p3, out _); xs.Add(p.X); ys.Add(p.Y); }
                }
                foreach (var v in sc.Vertices) { Point p = sc.Project(sc.GetEffectivePos(v, true), out _); xs.Add(p.X); ys.Add(p.Y); }
                if (sc.Kind is "cone" or "cylinder")
                {
                    double h2 = sc.Height * 0.5;
                    for (int i = 0; i < 24; i++)
                    {
                        double th = 2 * Math.PI * i / 24.0, cx = sc.Radius * Math.Cos(th), cz = sc.Radius * Math.Sin(th);
                        Vec3 pb3 = new(cx, -h2, cz);
                        Point pb = sc.Project(pb3 + sc.GetPartOffset(sc.GetPartSideSign(pb3)), out _); xs.Add(pb.X); ys.Add(pb.Y);
                        if (sc.Kind == "cylinder")
                        {
                            Vec3 pt3 = new(cx, h2, cz);
                            Point pt = sc.Project(pt3 + sc.GetPartOffset(sc.GetPartSideSign(pt3)), out _); xs.Add(pt.X); ys.Add(pt.Y);
                        }
                    }
                }
            }
            if (sc.CutPlane != null && sc.CutPlane.Visible && !sc.IsNetUnfolded)
            {
                foreach (var corner in sc.CutPlane.GetQuadCorners())
                {
                    Point pc = sc.Project(corner, out _);
                    xs.Add(pc.X); ys.Add(pc.Y);
                }
            }
            return xs.Count == 0 ? Rect.Empty : new Rect(xs.Min() - 12, ys.Min() - 12, xs.Max() - xs.Min() + 24, ys.Max() - ys.Min() + 24);
        }

        private Rect GetSolid3DBounds() => GetSolid3DBoundsFor(Solid3D);

        private Solid3DScene? FindSolid3DAt(Point pos)
        {
            for (int i = Solids3DList.Count - 1; i >= 0; i--)
                if (GetSolid3DBoundsFor(Solids3DList[i]).Contains(pos)) return Solids3DList[i];
            return null;
        }

        private int HitTestCutPartSide(Solid3DScene sc, Point pos)
        {
            if (sc.CutPlane == null || !sc.TryGetCutPlane(out Vec3 o, out Vec3 n)) return 0;
            bool hasSplit = sc.SplitAmount > 0.01 || sc.PartOffsetAbove.Length > 0.01 || sc.PartOffsetBelow.Length > 0.01;
            if (!hasSplit) return 0;

            if (sc.Kind == "poly" && sc.Vertices.Count > 0)
            {
                int bestSide = 0;
                double bestDist = double.MaxValue;
                foreach (var v in sc.Vertices)
                {
                    int side = sc.GetPartSideSign(v.Pos);
                    if (side == 0) continue;
                    Point sp = sc.Project(sc.GetEffectivePos(v, true), out _);
                    double d = Dist(pos, sp);
                    if (d < bestDist) { bestDist = d; bestSide = side; }
                }
                return bestSide;
            }
            // Voi Non / Tru / Cau: so sanh vi tri click voi mat phang cat da dich chuyen
            Point pCutScreen = sc.Project(o, out _);
            Vec3 rotN = sc.RotatePoint(n);
            double signScreen = (pos.X - pCutScreen.X) * rotN.X - (pos.Y - pCutScreen.Y) * rotN.Y;
            return signScreen >= 0 ? 1 : -1;
        }

        private void RefreshSolid3DMenu() { var b = GetSolid3DBounds(); if (!b.IsEmpty) ContextMenuRequested?.Invoke("3d", new Point(b.Left, b.Top)); }
        private void Refresh2DMenu() { var b = GetGroupSelectionBounds(); if (b.HasValue) ContextMenuRequested?.Invoke("2d", new Point(b.Value.Left, b.Value.Top)); }
        private void RefreshRevMenu()
        {
            if (RevScene == null || !RevScene.IsValid) return;
            RevScene.EnsureRegionsInitialized();
            var r0 = RevScene.Regions[Math.Clamp(RevScene.ActiveRegionIndex, 0, RevScene.Regions.Count - 1)];
            Point p = RevScene.ToScreen2D(r0.StartT, RevolutionScene.SampleR(RevScene.Curve1TR, r0.StartT));
            ContextMenuRequested?.Invoke("rev", new Point(p.X, Math.Min(p.Y, RevScene.AxisOrigin.Y) - 10));
        }

        private void SelectConnected2DComponent(string seedPointId)
        {
            ClearGroupSelection();
            var q = new Queue<string>(); q.Enqueue(seedPointId); _selPointIds.Add(seedPointId);
            while (q.Count > 0)
            {
                string cur = q.Dequeue();
                foreach (var sh in Scene.Shapes) { if (sh.P1Id == cur && _selPointIds.Add(sh.P2Id)) q.Enqueue(sh.P2Id); else if (sh.P2Id == cur && _selPointIds.Add(sh.P1Id)) q.Enqueue(sh.P1Id); }
                foreach (var reg in Scene.Regions) if (reg.PointIds.Contains(cur)) foreach (var pid in reg.PointIds) if (_selPointIds.Add(pid)) q.Enqueue(pid);
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
            _netUnfoldTimer.Stop();
            RevScene = null;
            Solids3DList.Clear();
            _intersectSolidA = null;
            _intersectSolidB = null;
            _arbitraryPlanePoints.Clear();
            _step3DVertices.Clear();
            NetProgressUpdated?.Invoke(0.0);
            _pending3DParallelVertex = null;
            ContextMenuRequested?.Invoke("", null);
            ActiveSnipRect = null;
            ActiveSnipLasso.Clear();
            ClearActiveSteps();
            NotifyMetrics();
            ShowToast("Đã xóa trắng toàn bộ lớp vẽ trong suốt");
        }


        public bool TryRestoreFromOfficeDoubleClick(string procName)
        {
            UpdateOfficeOriginSync();
            var dpi = VisualTreeHelper.GetDpi(this);
            var restored = OfficeComBridge.TryReadSelectedOverlayImage(procName, dpi.DpiScaleX, dpi.DpiScaleY);
            if (restored == null) return false;

            SaveHistory();
            Scene = restored.Value.scene;
            OfficeContext = restored.Value.ctx;
            OnSceneRestoredForEditing("Office");
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
                            UpdateOfficeOriginSync();
                            var dpi = VisualTreeHelper.GetDpi(this);
                            double clickDipX = (clickPxX - OfficeComBridge.SurfaceOriginScreenPx.X) / dpi.DpiScaleX;
                            double clickDipY = (clickPxY - OfficeComBridge.SurfaceOriginScreenPx.Y) / dpi.DpiScaleY;
                            double imgDipW = img.PixelWidth / dpi.DpiScaleX;
                            double imgDipH = img.PixelHeight / dpi.DpiScaleY;
                            double sx = entry.BoxW > 4 ? imgDipW / entry.BoxW : 1.0;
                            double sy = entry.BoxH > 4 ? imgDipH / entry.BoxH : 1.0;

                            var topLeft = new Point(Math.Max(20, clickDipX - imgDipW * 0.5), Math.Max(90, clickDipY - imgDipH * 0.5));
                            SaveHistory();
                            Scene = SceneHistoryStore.MaterializeSceneAt(entry, topLeft, sx, sy);
                            OfficeContext = null;
                            OnSceneRestoredForEditing($"ScienceWord #{entry.Id}");
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
            OnSceneRestoredForEditing(entry.Label);
        }

        private void OnSceneRestoredForEditing(string sourceLabel)
        {
            ClearGroupSelection();
            SetTool("select");
            InvalidateVisual();

            bool hasGeo = Scene.Points.Count > 0 || Scene.Shapes.Count > 0 || Scene.Regions.Count > 0;
            if (!hasGeo && Scene.MathFormulas.Count == 1 && Scene.Texts.Count == 0 && Scene.Strokes.Count == 0)
            {
                var mf = Scene.MathFormulas[0];
                _selectedMathId = mf.Id; _selMathIds.Add(mf.Id);
                InvalidateVisual(); RefreshMathMenu();
                MathFormulaEditRequested?.Invoke(mf);
                ShowToast($"Đã mở lại Công thức từ {sourceLabel}! Sửa xong bấm [Cập nhật & Chèn App].", 4600);
                return;
            }
            if (!hasGeo && Scene.Texts.Count == 1 && Scene.MathFormulas.Count == 0 && Scene.Strokes.Count == 0)
            {
                var t = Scene.Texts[0];
                _selectedTextId = t.Id; _selTextIds.Add(t.Id);
                InvalidateVisual(); RefreshTextMenu();
                TextEditRequested?.Invoke(t);
                ShowToast($"Đã mở lại Văn bản từ {sourceLabel}! Sửa xong bấm [Cập nhật & Chèn App].", 4600);
                return;
            }
            if (!hasGeo && Scene.Strokes.Count > 0 && Scene.MathFormulas.Count == 0 && Scene.Texts.Count == 0)
            {
                foreach (var st in Scene.Strokes) _selStrokeIds.Add(st.Id);
                InvalidateVisual(); RefreshHandwritingMenu();
                HandwritingEditRequested?.Invoke(Scene.Strokes.ToList());
                ShowToast($"Đã mở lại Chữ viết tay từ {sourceLabel}! Chỉnh sửa xong bấm [Cập nhật & Chèn App].", 4600);
                return;
            }
            ShowToast($"Đã mở lại từ {sourceLabel}! Nháy đúp lên Công thức, Văn bản hoặc Chữ viết tay để sửa — xong bấm [Chèn App].", 4800);
        }

        private void ClearGroupSelection()
        {
            _selPointIds.Clear(); _selStrokeIds.Clear(); _selTextIds.Clear(); _selMathIds.Clear(); _selMagicIds.Clear();
            _selectedMathId = null;
            _selectedTextId = null;
            _isDraggingGroup = false;
            _marqueeRect = null;
        }

        private bool HasGroupSelection() =>
            _selPointIds.Count > 0 || _selStrokeIds.Count > 0 || _selTextIds.Count > 0 || _selMathIds.Count > 0 || _selMagicIds.Count > 0;

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
                if (t != null) { var r = GetTextRect(t); xs.Add(r.Left); xs.Add(r.Right); ys.Add(r.Top); ys.Add(r.Bottom); }
            }
            foreach (var mfid in _selMathIds)
            {
                var mf = Scene.MathFormulas.FirstOrDefault(x => x.Id == mfid);
                if (mf != null) { var r = GetMathFormulaRect(mf); xs.Add(r.Left); xs.Add(r.Right); ys.Add(r.Top); ys.Add(r.Bottom); }
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
            foreach (var t in Scene.Texts) { var r = GetTextRect(t); if (inside(new Point(r.Left + r.Width * 0.5, r.Top + r.Height * 0.5))) _selTextIds.Add(t.Id); }
            foreach (var mf in Scene.MathFormulas) { var r = GetMathFormulaRect(mf); if (inside(new Point(r.Left + r.Width * 0.5, r.Top + r.Height * 0.5))) _selMathIds.Add(mf.Id); }
            foreach (var mf in MagicFills)
            {
                Point c = new(mf.BoundsDip.Left + mf.BoundsDip.Width * 0.5, mf.BoundsDip.Top + mf.BoundsDip.Height * 0.5);
                if (inside(c)) _selMagicIds.Add(mf.Id);
            }
            int total = _selPointIds.Count + _selStrokeIds.Count + _selTextIds.Count + _selMathIds.Count + _selMagicIds.Count;
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
            foreach (var mfid in _selMathIds)
            {
                var mf = Scene.MathFormulas.FirstOrDefault(x => x.Id == mfid);
                if (mf != null) { mf.X += dx; mf.Y += dy; }
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
            _dragRevRegionIdx = -1;
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
                "plane_3pts_3d" => "Mp (P) qua 3 điểm bất kỳ: Click 3 đỉnh, cạnh hoặc điểm tự do bất kỳ (trong/ngoài khối) để dựng (P)!",
                "plane_para_3d" => "Mp (P) qua 1 điểm // mặt: Click 1 điểm đi qua, sau đó click 3 đỉnh xác định mặt phẳng chuẩn!",
                "intersect_solids_3d" => "Giao 2 khối 3D: Click 2 khối 3D để tính & làm nổi bật giao điểm / giao tuyến!",
                _ => $"Công cụ: {toolName}"
            };
            _step3DVertices.Clear(); _pending3DParallelVertex = null; _arbitraryPlanePoints.Clear();
            if (toolName is not "select" and not "orbit_3d" and not "add_bound" and not "point_edge_3d" and not "edge_3d" and not "parallel_3d" and not "section_3d" and not "plane_3pts_3d" and not "plane_para_3d")
                ContextMenuRequested?.Invoke("", null);
            ActiveToolChanged?.Invoke(toolName);
            ShowToast(hint, 3600);
        }

        // ================= SNIPPING TOOL ACTIONS (CHỤP VÙNG / TÔ VÙNG) =================
        public void CaptureSnipToClipboard()
        {
            UpdateOfficeOriginSync();
            var dpi = VisualTreeHelper.GetDpi(this);
            double oxDip = OfficeComBridge.SurfaceOriginScreenPx.X / dpi.DpiScaleX;
            double oyDip = OfficeComBridge.SurfaceOriginScreenPx.Y / dpi.DpiScaleY;
            BitmapSource? bmp = null;

            if (ActiveSnipLasso.Count >= 3)
                bmp = ScreenMagicWand.CaptureLassoSnip(ActiveSnipLasso.Select(p => new Point(p.X + oxDip, p.Y + oyDip)).ToList(), dpi.DpiScaleX, dpi.DpiScaleY);
            else if (ActiveSnipRect.HasValue && ActiveSnipRect.Value.Width > 3 && ActiveSnipRect.Value.Height > 3)
            {
                var r = ActiveSnipRect.Value;
                bmp = ScreenMagicWand.CaptureRectSnip(new Rect(r.X + oxDip, r.Y + oyDip, r.Width, r.Height), dpi.DpiScaleX, dpi.DpiScaleY);
            }

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
            UpdateOfficeOriginSync();
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

                UpdateOfficeOriginSync();
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
            {
                var r = GetTextRect(Scene.Texts[i]);
                r.Inflate(8, 6);
                if (r.Contains(pos)) return Scene.Texts[i];
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
                var hitS3 = FindSolid3DAt(pos);
                if (hitS3 != null)
                {
                    Solids3DList.Remove(hitS3); Solids3DList.Add(hitS3);
                    _activeOrbitSolid3D = hitS3;
                    _isOrbiting3D = true; _orbitLastPos = pos; CaptureMouse(); RefreshSolid3DMenu(); return;
                }
                if (RevScene != null && RevScene.IsValid && RevMode == MathRenderMode.Solid3D)
                {
                    _isOrbitingRev3D = true; _orbitLastPos = pos; CaptureMouse(); RefreshRevMenu(); return;
                }
                if (RevScene != null && RevScene.IsValid && TryHitRevRegion(pos, out int rIdx))
                {
                    RevScene.Regions[rIdx].Enabled = !RevScene.Regions[rIdx].Enabled;
                    NotifyMetrics(); InvalidateVisual();
                    ShowToast(RevScene.Regions[rIdx].Enabled ? $"Đã bật miền S{rIdx + 1}" : $"Đã ẩn miền S{rIdx + 1} (tạo vùng trống)");
                    return;
                }
                if (Tool == "fill_area" && _stepPoints.Count >= 3) FinishPolygonFill();
                else { _stepPoints.Clear(); _step3DVertices.Clear(); _pending3DParallelVertex = null; ActiveSnipRect = null; ActiveSnipLasso.Clear(); InvalidateVisual(); }
                return;
            }
            if (e.ChangedButton != MouseButton.Left) return;

            // 0. Tích phân nhiều miền độc lập (có thể KHÔNG LIÊN TỤC): Click đặt thêm miền/cận hoặc kéo từng chốt cận
            if (RevScene != null && RevScene.IsValid)
            {
                RevScene.EnsureRegionsInitialized();
                if (Tool == "add_bound")
                {
                    if (!RevShowBoundsHandles) SetRevBoundsHandlesVisible(true);
                    double dx = pos.X - RevScene.AxisOrigin.X, dy = pos.Y - RevScene.AxisOrigin.Y;
                    double t = dx * RevScene.AxisU.X + dy * RevScene.AxisU.Y;
                    int idx = RevScene.AddOrSplitAt(t);
                    if (idx >= 0) { NotifyMetrics(); InvalidateVisual(); RefreshRevMenu(); ShowToast($"Đã tạo/tách miền S{idx + 1}! Kéo từng chốt đỏ để tạo vùng trống không tô màu.", 3800); }
                    else ShowToast("Khoảng này quá hẹp để chèn thêm miền!");
                    return;
                }

                // Kiểm tra kéo chốt cận trái/phải của từng miền (chỉ bắt khi đang hiện nút cận)
                int bestReg = -1; bool bestIsEnd = false; double bestD = 15.0;
                if (RevShowBoundsHandles)
                for (int i = 0; i < RevScene.Regions.Count; i++)
                {
                    var rg = RevScene.Regions[i];
                    double dS = Dist(pos, RevScene.ToScreen2D(rg.StartT, 0));
                    if (dS <= bestD) { bestD = dS; bestReg = i; bestIsEnd = false; }
                    double dE = Dist(pos, RevScene.ToScreen2D(rg.EndT, 0));
                    if (dE <= bestD) { bestD = dE; bestReg = i; bestIsEnd = true; }
                }
                if (bestReg >= 0)
                {
                    _dragRevRegionIdx = bestReg; _dragRevIsEnd = bestIsEnd;
                    RevScene.ActiveRegionIndex = bestReg;
                    CaptureMouse(); RefreshRevMenu();
                    return;
                }

                if (Tool == "select" && TryHitRevRegion(pos, out int hitReg))
                {
                    RevScene.ActiveRegionIndex = hitReg; InvalidateVisual(); RefreshRevMenu();
                    ShowToast($"Đang chọn S{hitReg + 1} — kéo chốt đỏ để đổi cận (tạo vùng trống), chuột phải để ẩn/hiện!", 3800);
                    return;
                }
            }

            // 0.2 Tương tác Khối 3D thực & Mặt phẳng bất kỳ
            if (Tool == "plane_3pts_3d")
            {
                var targetSc = FindSolid3DAt(pos) ?? Solid3D;
                Vec3 p3d;
                string descPt;
                if (targetSc != null && targetSc.Kind == "poly")
                {
                    var v = FindNearest3DVertex(pos, 18.0);
                    if (v != null) { p3d = v.Pos; descPt = v.Label; }
                    else
                    {
                        var hitEdge = FindNearest3DEdge(pos, 15.0);
                        if (hitEdge != null)
                        {
                            var (edge, tParam) = hitEdge.Value; var v1 = targetSc.GetV(edge.V1Id)!; var v2 = targetSc.GetV(edge.V2Id)!;
                            p3d = v1.Pos + (v2.Pos - v1.Pos) * tParam;
                            var nv = new Vertex3D { Id = $"v3_{targetSc.NextIdx++}", Label = Next3DLabel(), X = p3d.X, Y = p3d.Y, Z = p3d.Z, HostEdgeV1 = edge.V1Id, HostEdgeV2 = edge.V2Id, HostT = tParam };
                            targetSc.Vertices.Add(nv);
                            descPt = nv.Label;
                        }
                        else
                        {
                            p3d = targetSc.ScreenToLocalApprox(pos, (_arbitraryPlanePoints.Count - 1) * 0.35);
                            descPt = $"M{_arbitraryPlanePoints.Count + 1}";
                        }
                    }
                }
                else if (targetSc != null)
                {
                    p3d = targetSc.ScreenToLocalApprox(pos, (_arbitraryPlanePoints.Count - 1) * 0.35);
                    descPt = $"M{_arbitraryPlanePoints.Count + 1}";
                }
                else
                {
                    double cx = ActualWidth > 100 ? ActualWidth * 0.45 : 480;
                    double cy = ActualHeight > 100 ? ActualHeight * 0.54 : 420;
                    var tmp = new Solid3DScene { CenterScreen = new Point(cx, cy), Scale = 135.0 };
                    p3d = tmp.ScreenToLocalApprox(pos, (_arbitraryPlanePoints.Count - 1) * 0.35);
                    descPt = $"M{_arbitraryPlanePoints.Count + 1}";
                }

                _arbitraryPlanePoints.Add(p3d);
                if (_arbitraryPlanePoints.Count < 3)
                {
                    ShowToast($"Mp (P) qua 3 điểm bất kỳ: đã chọn {_arbitraryPlanePoints.Count}/3 điểm ({descPt}). Click tiếp điểm thứ {_arbitraryPlanePoints.Count + 1}!");
                }
                else
                {
                    var pA = _arbitraryPlanePoints[0]; var pB = _arbitraryPlanePoints[1]; var pC = _arbitraryPlanePoints[2];
                    _arbitraryPlanePoints.Clear();
                    if (targetSc == null)
                    {
                        double cx = ActualWidth > 100 ? ActualWidth * 0.45 : 480;
                        double cy = ActualHeight > 100 ? ActualHeight * 0.54 : 420;
                        targetSc = Solid3DScene.CreatePreset("plane_only", new Point(cx, cy));
                        if (!KeepPreviousSolids3D) Solids3DList.Clear();
                        Solids3DList.Add(targetSc);
                    }
                    var (_, msg) = targetSc.CreatePlaneFromArbitrary3Points(pA, pB, pC, FillColorHex);
                    SetTool("select");
                    RefreshSolid3DMenu();
                    ShowToast(msg, 4800);
                }
                InvalidateVisual();
                return;
            }

            if (Tool == "intersect_solids_3d")
            {
                var hitS = FindSolid3DAt(pos);
                if (hitS != null)
                {
                    if (!_step3DVertices.Contains(hitS.Id)) _step3DVertices.Add(hitS.Id);
                    if (_step3DVertices.Count == 1)
                    {
                        ShowToast($"Đã chọn khối 1: {hitS.Name}. Click chọn khối 2 để tính giao!");
                    }
                    else
                    {
                        var s1 = Solids3DList.FirstOrDefault(x => x.Id == _step3DVertices[0]) ?? Solid3D;
                        var s2 = hitS;
                        _step3DVertices.Clear();
                        if (s1 != null && s2 != null && s1 != s2)
                        {
                            _intersectSolidA = s1;
                            _intersectSolidB = s2;
                            var pts = Solid3DScene.ComputeSolidIntersectionPoints(s1, s2);
                            ShowToast(pts.Count > 0 ? $"✓ Đang bật giao trực tiếp {s1.Name} ∩ {s2.Name} ({pts.Count} điểm giao)!" : "Đã bật theo dõi giao 2 khối — hãy kéo 2 khối chồng lên nhau!", 4500);
                        }
                        else ShowToast("Hãy chọn 2 khối 3D khác nhau!");
                    }
                }
                else ShowToast("Click vào 1 khối 3D để chọn giao khối!");
                InvalidateVisual();
                return;
            }

            if (Solid3D != null && Tool is "orbit_3d" or "point_edge_3d" or "edge_3d" or "parallel_3d" or "section_3d" or "plane_para_3d")
            {
                if (Tool == "orbit_3d") { _activeOrbitSolid3D = FindSolid3DAt(pos) ?? Solid3D; _isOrbiting3D = true; _orbitLastPos = pos; CaptureMouse(); RefreshSolid3DMenu(); return; }
                if (Tool == "point_edge_3d")
                {
                    var hit = FindNearest3DEdge(pos, 16.0);
                    if (hit != null)
                    {
                        var (edge, tParam) = hit.Value; var v1 = Solid3D.GetV(edge.V1Id)!; var v2 = Solid3D.GetV(edge.V2Id)!;
                        Vec3 p3 = v1.Pos + (v2.Pos - v1.Pos) * tParam; string lbl = Next3DLabel();
                        Solid3D.Vertices.Add(new Vertex3D { Id = $"v3_{Solid3D.NextIdx++}", Label = lbl, X = p3.X, Y = p3.Y, Z = p3.Z, HostEdgeV1 = edge.V1Id, HostEdgeV2 = edge.V2Id, HostT = tParam });
                        InvalidateVisual(); ShowToast($"Đã chấm điểm {lbl} thuộc cạnh {v1.Label}{v2.Label}!");
                    }
                    else ShowToast("Hãy click trực tiếp vào 1 cạnh của khối đa diện 3D!");
                    return;
                }
                if (Tool == "edge_3d")
                {
                    var v = FindNearest3DVertex(pos, 18.0);
                    if (v != null)
                    {
                        _step3DVertices.Add(v.Id);
                        if (_step3DVertices.Count == 2) { if (_step3DVertices[0] != _step3DVertices[1]) Solid3D.Edges.Add(new Edge3D { V1Id = _step3DVertices[0], V2Id = _step3DVertices[1], IsConstruction = true }); _step3DVertices.Clear(); }
                        InvalidateVisual();
                    }
                    return;
                }
                if (Tool == "parallel_3d")
                {
                    if (_pending3DParallelVertex == null) { var v = FindNearest3DVertex(pos, 18.0); if (v != null) { _pending3DParallelVertex = v.Id; ShowToast($"Đã chọn {v.Label}. Click cạnh chuẩn song song!"); InvalidateVisual(); } else ShowToast("Bước 1: Click điểm đi qua trên khối 3D"); }
                    else { var hit = FindNearest3DEdge(pos, 18.0); if (hit != null) { var newV = Solid3D.ConstructParallelEdge3D(_pending3DParallelVertex, hit.Value.edge, Next3DLabel); _pending3DParallelVertex = null; InvalidateVisual(); ShowToast(newV != null ? $"Đã dựng song song 3D cắt tại {newV.Label}!" : "Không tìm thấy cạnh đối diện đồng phẳng!"); } }
                    return;
                }
                if (Tool == "section_3d")
                {
                    var v = FindNearest3DVertex(pos, 18.0);
                    if (v != null)
                    {
                        if (_step3DVertices.Count >= 3 && v.Id == _step3DVertices[0]) { Solid3D.Sections.Add(new Section3D { VertexIds = new List<string>(_step3DVertices), ColorHex = FillColorHex, Opacity = FillOpacity }); _step3DVertices.Clear(); ShowToast("Đã tô Thiết diện 3D! Kéo thanh [Tách] để tách khối!", 4000); }
                        else if (!_step3DVertices.Contains(v.Id)) { _step3DVertices.Add(v.Id); ShowToast($"Thiết diện: đã chọn {_step3DVertices.Count} đỉnh. Click lại đỉnh đầu để khép!"); }
                        InvalidateVisual();
                    }
                    return;
                }

                if (Tool == "plane_para_3d")
                {
                    if (Solid3D.Kind != "poly") { ShowToast("Với Nón/Trụ/Cầu hãy bấm [✂️ Mp cắt] để tạo mặt phẳng cắt tương tác!"); return; }
                    var v = FindNearest3DVertex(pos, 18.0);
                    if (v == null && _step3DVertices.Count == 0)
                    {
                        var hit = FindNearest3DEdge(pos, 15.0);
                        if (hit != null)
                        {
                            var (edge, tParam) = hit.Value; var v1 = Solid3D.GetV(edge.V1Id)!; var v2 = Solid3D.GetV(edge.V2Id)!;
                            Vec3 p3 = v1.Pos + (v2.Pos - v1.Pos) * tParam;
                            v = new Vertex3D { Id = $"v3_{Solid3D.NextIdx++}", Label = Next3DLabel(), X = p3.X, Y = p3.Y, Z = p3.Z, HostEdgeV1 = edge.V1Id, HostEdgeV2 = edge.V2Id, HostT = tParam };
                            Solid3D.Vertices.Add(v);
                        }
                    }
                    if (v == null) { ShowToast(_step3DVertices.Count == 0 ? "Bước 1: Click điểm đi qua (đỉnh hoặc cạnh khối 3D)!" : "Click chọn 3 đỉnh xác định mặt phẳng chuẩn (vd A, B, C)!"); return; }
                    if (_step3DVertices.Count == 0)
                    {
                        _step3DVertices.Add(v.Id);
                        ShowToast($"Đã chọn điểm đi qua {v.Label}. Tiếp theo click 3 đỉnh xác định mặt phẳng chuẩn!");
                    }
                    else
                    {
                        var facePicked = _step3DVertices.Skip(1).ToList();
                        if (!facePicked.Contains(v.Id)) _step3DVertices.Add(v.Id);
                        if (_step3DVertices.Count < 4)
                        {
                            var fNames = string.Join(", ", _step3DVertices.Skip(1).Select(id => Solid3D.GetV(id)?.Label));
                            ShowToast($"Mặt phẳng chuẩn: đã chọn {_step3DVertices.Count - 1}/3 đỉnh ({fNames}).");
                        }
                        else
                        {
                            var (_, msg) = Solid3D.CreatePlaneFromPointParallelFace(_step3DVertices[0], _step3DVertices[1], _step3DVertices[2], _step3DVertices[3], Next3DLabel, FillColorHex);
                            _step3DVertices.Clear();
                            RefreshSolid3DMenu();
                            ShowToast(msg, 4800);
                        }
                    }
                    InvalidateVisual();
                    return;
                }
            }

            // 0.3 Dựng hình 2D mới: Song song (//) & Giao điểm
            if (Tool == "parallel_2d")
            {
                if (_stepPoints.Count == 0) { var pt = FindNearestPoint(pos, 16.0) ?? GetOrCreatePoint(pos); _stepPoints.Add(pt.Id); ShowToast($"Đã chọn {pt.Label}. Click đoạn thẳng mẫu!"); InvalidateVisual(); }
                else
                {
                    var sh = FindNearestSegment(pos, 18.0); var v = GetPoint(_stepPoints[0]);
                    if (sh != null && v != null) { var s1 = GetPoint(sh.P1Id); var s2 = GetPoint(sh.P2Id); if (s1 != null && s2 != null) { SaveHistory(); Vector dir = new(s2.X - s1.X, s2.Y - s1.Y); double len = Math.Max(120.0, dir.Length); dir.Normalize(); var pEnd = GetOrCreatePoint(new Point(v.X + dir.X * len, v.Y + dir.Y * len)); AddShape("segment", v.Id, pEnd.Id); _stepPoints.Clear(); ShowToast($"Đã dựng song song {v.Label}{pEnd.Label}!"); InvalidateVisual(); } }
                }
                return;
            }
            if (Tool == "intersect_2d")
            {
                var sh = FindNearestSegment(pos, 16.0);
                if (sh != null)
                {
                    if (_selectedShapeId == null) { _selectedShapeId = sh.Id; ShowToast("Đã chọn đoạn 1. Click đoạn 2 lấy giao điểm!"); InvalidateVisual(); }
                    else { var sh1 = Scene.Shapes.FirstOrDefault(x => x.Id == _selectedShapeId); _selectedShapeId = null; if (sh1 != null && sh1.Id != sh.Id && TryIntersect2DSegments(sh1, sh, out Point inter)) { SaveHistory(); var pt = GetOrCreatePoint(inter); ShowToast($"Đã chấm giao điểm {pt.Label}!"); } else ShowToast("Hai đoạn không cắt nhau!"); InvalidateVisual(); }
                }
                return;
            }

            // 0.4 NHÁY ĐÚP CHỈNH SỬA TRỰC TIẾP: Công thức Toán, Văn bản (Text) & Chữ viết tay (chạy TRƯỚC kéo nhóm!)
            if (Tool is "select" or "text" or "segment" or "dashed" or "point")
            {
                var mDbl = FindNearestMathFormula(pos);
                var tDbl = mDbl == null ? FindNearestText(pos) : null;
                var sDbl = (mDbl == null && tDbl == null && Tool == "select") ? FindNearestStroke(pos, 14.0) : null;
                string? hitKey = mDbl != null ? ("m:" + mDbl.Id) : (tDbl != null ? ("t:" + tDbl.Id) : (sDbl != null ? ("s:" + (string.IsNullOrEmpty(sDbl.GroupId) ? sDbl.Id : sDbl.GroupId)) : null));
                long nowTick = Environment.TickCount64;
                bool isDbl = hitKey != null && (e.ClickCount >= 2 || (_lastDblClickKey == hitKey && (nowTick - _lastDblClickTick) < 420));
                if (isDbl)
                {
                    _lastDblClickKey = null;
                    _lastDblClickTick = 0;
                    if (mDbl != null)
                    {
                        ClearGroupSelection();
                        _selectedMathId = mDbl.Id; _selMathIds.Add(mDbl.Id);
                        InvalidateVisual(); RefreshMathMenu();
                        MathFormulaEditRequested?.Invoke(mDbl);
                        return;
                    }
                    if (tDbl != null)
                    {
                        ClearGroupSelection();
                        _selectedTextId = tDbl.Id; _selTextIds.Add(tDbl.Id);
                        InvalidateVisual(); RefreshTextMenu();
                        TextEditRequested?.Invoke(tDbl);
                        return;
                    }
                    if (sDbl != null)
                    {
                        var cluster = GetStrokeCluster(sDbl);
                        ClearGroupSelection();
                        foreach (var cs in cluster) _selStrokeIds.Add(cs.Id);
                        InvalidateVisual(); RefreshHandwritingMenu();
                        HandwritingEditRequested?.Invoke(cluster);
                        return;
                    }
                }
                if (hitKey != null) { _lastDblClickKey = hitKey; _lastDblClickTick = nowTick; }
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
                UpdateOfficeOriginSync();
                var dpi = VisualTreeHelper.GetDpi(this);
                double oxDip = OfficeComBridge.SurfaceOriginScreenPx.X / dpi.DpiScaleX;
                double oyDip = OfficeComBridge.SurfaceOriginScreenPx.Y / dpi.DpiScaleY;
                var screenPosDip = new Point(pos.X + oxDip, pos.Y + oyDip);
                var res = ScreenMagicWand.CaptureAndFloodFill(screenPosDip, dpi.DpiScaleX, dpi.DpiScaleY, ParseColor(FillColorHex, 1.0), FillOpacity, FillPattern, 32);
                if (res != null)
                {
                    var localBounds = new Rect(res.BoundsDip.X - oxDip, res.BoundsDip.Y - oyDip, res.BoundsDip.Width, res.BoundsDip.Height);
                    MagicFills.Add(new MagicFillLayer { Id = GenId("mf"), Bitmap = res.Bitmap, BoundsDip = localBounds });
                    InvalidateVisual();
                    ShowToast("Đũa thần đã dò viền và tô màu vùng kín thành công!");
                }
                else ShowToast("Vùng quá lớn hoặc không khép kín! Hãy click vào bên trong một hình kín.");
                return;
            }

            if (Tool == "select")
            {
                var mHit = FindNearestMathFormula(pos);
                if (mHit != null)
                {
                    ClearGroupSelection(); SaveHistory();
                    _selectedMathId = mHit.Id; _selMathIds.Add(mHit.Id);
                    _draggingMathId = mHit.Id; _dragMathOffset = new Point(pos.X - mHit.X, pos.Y - mHit.Y);
                    CaptureMouse(); InvalidateVisual(); RefreshMathMenu(); return;
                }
                var txt = FindNearestText(pos);
                if (txt != null)
                {
                    ClearGroupSelection(); SaveHistory();
                    _selectedTextId = txt.Id; _draggingTextId = txt.Id; _selTextIds.Add(txt.Id);
                    CaptureMouse(); InvalidateVisual(); RefreshTextMenu(); return;
                }
                var pt = FindNearestPoint(pos, 13.0);
                if (pt != null) { ClearGroupSelection(); SaveHistory(); _draggingPointId = pt.Id; _selectedPointId = pt.Id; _selectedShapeId = null; _selPointIds.Add(pt.Id); CaptureMouse(); InvalidateVisual(); Refresh2DMenu(); return; }
                var sh = FindNearestSegment(pos, 12.0) ?? FindNearestCircle(pos, 12.0);
                if (sh != null) { SaveHistory(); _selectedShapeId = sh.Id; _selectedPointId = null; SelectConnected2DComponent(sh.P1Id); _isDraggingGroup = true; _groupLastPos = pos; CaptureMouse(); InvalidateVisual(); Refresh2DMenu(); return; }
                var reg = FindRegionContainingPoint(pos);
                if (reg != null && reg.PointIds.Count > 0) { SaveHistory(); SelectConnected2DComponent(reg.PointIds[0]); _isDraggingGroup = true; _groupLastPos = pos; CaptureMouse(); InvalidateVisual(); Refresh2DMenu(); return; }
                var strk = FindNearestStroke(pos, 14.0);
                if (strk != null)
                {
                    var cluster = GetStrokeCluster(strk);
                    ClearGroupSelection(); SaveHistory();
                    foreach (var cs in cluster) _selStrokeIds.Add(cs.Id);
                    _isDraggingGroup = true; _groupLastPos = pos;
                    CaptureMouse(); InvalidateVisual(); RefreshHandwritingMenu(); return;
                }
                var hitSolid = FindSolid3DAt(pos);
                if (hitSolid != null)
                {
                    ClearGroupSelection();
                    Solids3DList.Remove(hitSolid); Solids3DList.Add(hitSolid);
                    _activeMoveSolid3D = hitSolid;
                    int partSide = HitTestCutPartSide(hitSolid, pos);
                    if (partSide != 0 && hitSolid.CutPlane != null)
                    {
                        _draggingCutPartSide = partSide;
                        _orbitLastPos = pos;
                        CaptureMouse(); InvalidateVisual(); RefreshSolid3DMenu();
                        ShowToast(partSide > 0 ? "✂️ Đang kéo dời phần TRÊN mặt phẳng cắt (P)!" : "✂️ Đang kéo dời phần DƯỚI mặt phẳng cắt (P)!", 2200);
                        return;
                    }
                    _isMoving3D = true; _orbitLastPos = pos; CaptureMouse(); InvalidateVisual(); RefreshSolid3DMenu(); return;
                }

                ClearGroupSelection(); ContextMenuRequested?.Invoke("", null);
                _selectedPointId = null; _selectedShapeId = null;
                _marqueeStart = pos; _marqueeRect = new Rect(pos, pos);
                CaptureMouse(); InvalidateVisual();
                return;
            }

            if (Tool is "pen" or "highlighter" or "eraser")
            {
                string pStyle = Tool == "eraser" ? "eraser" : (Tool == "highlighter" ? "highlighter" : PenTipStyle);
                if (pStyle == "eraser")
                {
                    SaveHistory();
                    EraseStrokesNear(pos);
                    CaptureMouse(); InvalidateVisual(); return;
                }
                SaveHistory();
                bool isHl = pStyle == "highlighter";
                double baseW = isHl ? Math.Max(16.0, StrokeWidth * 4.5) : (pStyle == "chalk" ? Math.Max(3.2, StrokeWidth * 1.4) : Math.Max(1.4, StrokeWidth));
                _activeStroke = new FreeStroke
                {
                    Id = GenId("strk"),
                    ColorHex = isHl ? FillColorHex : StrokeColorHex,
                    Width = baseW,
                    Opacity = isHl ? 0.36 : (pStyle == "pencil" ? 0.80 : (pStyle == "chalk" ? 0.90 : 1.0)),
                    IsHighlighter = isHl,
                    PenStyle = pStyle
                };
                _activeStroke.Points.Add(new[] { pos.X, pos.Y });
                _activeStroke.PointWidths.Add(baseW);
                _lastSmoothedPenPt = pos;
                _lastPenTick = Environment.TickCount64;
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

            if (_draggingCutPartSide != 0 && (_activeMoveSolid3D ?? Solid3D) != null)
            {
                var sc = _activeMoveSolid3D ?? Solid3D!;
                sc.NudgePartByScreenDelta(_draggingCutPartSide, pos.X - _orbitLastPos.X, pos.Y - _orbitLastPos.Y);
                _orbitLastPos = pos; InvalidateVisual(); return;
            }
            if (_isMoving3D && (_activeMoveSolid3D ?? Solid3D) != null)
            {
                var sc = _activeMoveSolid3D ?? Solid3D!;
                sc.CenterScreen = new Point(sc.CenterScreen.X + (pos.X - _orbitLastPos.X), sc.CenterScreen.Y + (pos.Y - _orbitLastPos.Y));
                _orbitLastPos = pos; InvalidateVisual(); RefreshSolid3DMenu(); return;
            }
            if (_isOrbiting3D && (_activeOrbitSolid3D ?? Solid3D) != null)
            {
                var sc = _activeOrbitSolid3D ?? Solid3D!;
                sc.YawDeg += (pos.X - _orbitLastPos.X) * 0.55;
                sc.PitchDeg = Math.Clamp(sc.PitchDeg + (pos.Y - _orbitLastPos.Y) * 0.45, -75, 75);
                _orbitLastPos = pos; InvalidateVisual(); return;
            }
            if (_isOrbitingRev3D && RevScene != null && RevScene.IsValid)
            {
                RevScene.TurntableYawDeg = (RevScene.TurntableYawDeg + (pos.X - _orbitLastPos.X) * 0.65) % 360.0;
                RevScene.TurntablePitchDeg = Math.Clamp(RevScene.TurntablePitchDeg + (pos.Y - _orbitLastPos.Y) * 0.35, -55, 55);
                _orbitLastPos = pos; InvalidateVisual(); return;
            }
            if (_dragRevRegionIdx >= 0 && RevScene != null && RevScene.IsValid)
            {
                double dx = pos.X - RevScene.AxisOrigin.X, dy = pos.Y - RevScene.AxisOrigin.Y;
                RevScene.MoveHandle(_dragRevRegionIdx, _dragRevIsEnd, dx * RevScene.AxisU.X + dy * RevScene.AxisU.Y);
                NotifyMetrics(); InvalidateVisual(); return;
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

            if (_draggingMathId != null)
            {
                var mf = Scene.MathFormulas.FirstOrDefault(m => m.Id == _draggingMathId);
                if (mf != null) { mf.X = pos.X - _dragMathOffset.X; mf.Y = pos.Y - _dragMathOffset.Y; }
                InvalidateVisual(); RefreshMathMenu(); return;
            }
            if (_draggingPointId != null) { var pt = GetPoint(_draggingPointId); if (pt != null) { pt.X = pos.X; pt.Y = pos.Y; } InvalidateVisual(); return; }
            if (_draggingTextId != null) { var txt = Scene.Texts.FirstOrDefault(t => t.Id == _draggingTextId); if (txt != null) { txt.X = pos.X; txt.Y = pos.Y; } InvalidateVisual(); RefreshTextMenu(); return; }
            if (e.LeftButton == MouseButtonState.Pressed && (Tool == "eraser" || (Tool == "pen" && PenTipStyle == "eraser")))
            {
                if (EraseStrokesNear(pos)) InvalidateVisual();
                return;
            }
            if (_activeStroke != null)
            {
                // Loc lam muot EMA giup net chu viet tay mem mai, khong gap khuc nhu clip
                Point sm = new(_lastSmoothedPenPt.X * 0.42 + pos.X * 0.58, _lastSmoothedPenPt.Y * 0.42 + pos.Y * 0.58);
                double d = Hypot(sm.X - _lastSmoothedPenPt.X, sm.Y - _lastSmoothedPenPt.Y);
                if (d >= 1.35)
                {
                    long now = Environment.TickCount64;
                    double dt = Math.Max(1.0, now - _lastPenTick);
                    double vel = d / dt;
                    _activeStroke.Points.Add(new[] { sm.X, sm.Y });
                    double w = _activeStroke.Width;
                    double prevW = _activeStroke.PointWidths.Count > 0 ? _activeStroke.PointWidths[^1] : w;
                    if (_activeStroke.PenStyle == "brush")
                    {
                        double targetW = Math.Clamp(w * (1.85 - Math.Min(1.25, vel * 0.72)), Math.Max(1.0, w * 0.36), w * 2.15);
                        w = prevW * 0.64 + targetW * 0.36;
                    }
                    else if (_activeStroke.PenStyle is "fountain" or "solid")
                    {
                        double targetW = Math.Clamp(w * (1.16 - Math.Min(0.36, vel * 0.24)), Math.Max(0.9, w * 0.74), w * 1.22);
                        w = prevW * 0.68 + targetW * 0.32;
                    }
                    _activeStroke.PointWidths.Add(w);
                    _lastSmoothedPenPt = sm;
                    _lastPenTick = now;
                }
                InvalidateVisual(); return;
            }
            if (Tool == "add_bound" || _stepPoints.Count > 0 || (ShowVirtualTools && Tool is not "select" and not "snip_rect" and not "snip_lasso" and not "magic_wand")) InvalidateVisual();
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            bool wasDraggingGroup = _isDraggingGroup;
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
            if (wasDraggingGroup) RefreshActiveSelectionMenu();
            _dragRevRegionIdx = -1;
            _isOrbiting3D = false; _isMoving3D = false; _isOrbitingRev3D = false;
            _draggingCutPartSide = 0; _activeOrbitSolid3D = null; _activeMoveSolid3D = null;
            _draggingPointId = null; _draggingTextId = null; _draggingMathId = null; _activeStroke = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        public void HandleKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { _stepPoints.Clear(); _step3DVertices.Clear(); _pending3DParallelVertex = null; ActiveSnipRect = null; ActiveSnipLasso.Clear(); SetTool("select"); }
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
                Scene.MathFormulas.RemoveAll(m => _selMathIds.Contains(m.Id));
                MagicFills.RemoveAll(mf => _selMagicIds.Contains(mf.Id));
                ContextMenuRequested?.Invoke("", null);
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
            else if (Solids3DList.Count > 0)
            {
                DeleteSolid3D();
            }
            else if (RevScene != null)
            {
                SetSpinAxis3D(SpinAxisMode.None);
                RevScene = null;
                NotifyMetrics();
                ContextMenuRequested?.Invoke("", null);
                InvalidateVisual();
                ShowToast("Đã xóa miền tích phân / khối tròn xoay");
            }
        }

        private bool EraseStrokesNear(Point pos)
        {
            int removed = Scene.Strokes.RemoveAll(st => st.Points.Any(pt => Hypot(pt[0] - pos.X, pt[1] - pos.Y) <= 14.0));
            return removed > 0;
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

        private static SolidColorBrush FrozenBrush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private static Brush MakeRegionBrush(string hex, double opacity, string pattern)
        {
            Color c = ParseColor(hex, opacity);
            if (pattern == "solid") return FrozenBrush(c);
            var group = new DrawingGroup();
            if (pattern == "hatch") group.Children.Add(new GeometryDrawing(null, new Pen(FrozenBrush(ParseColor(hex, Math.Min(1.0, opacity + 0.35))), 1.5), new LineGeometry(new Point(0, 10), new Point(10, 0))));
            else if (pattern == "cross") { var pen = new Pen(FrozenBrush(ParseColor(hex, Math.Min(1.0, opacity + 0.35))), 1.3); group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 10), new Point(10, 0)))); group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(10, 10)))); }
            else if (pattern == "dots") group.Children.Add(new GeometryDrawing(FrozenBrush(ParseColor(hex, Math.Min(1.0, opacity + 0.40))), null, new EllipseGeometry(new Point(5, 5), 1.8, 1.8)));
            var db = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 10, 10), ViewportUnits = BrushMappingMode.Absolute };
            db.Freeze();
            return db;
        }

        private static Pen MakePen(string hex, double width, string dash, bool highlight = false)
        {
            var pen = new Pen(FrozenBrush(highlight ? ParseColor("#2563EB") : ParseColor(hex)), width + (highlight ? 1.4 : 0.0))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                DashCap = PenLineCap.Flat,
                LineJoin = PenLineJoin.Round
            };
            if (dash == "dashed") pen.DashStyle = new DashStyle(new double[] { 5.0, 4.2 }, 0);
            else if (dash == "dotted") pen.DashStyle = DashStyles.Dot;
            pen.Freeze();
            return pen;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            DrawCanvasBackground(dc);
            RenderScene(dc, cleanExport: false);

            if (!string.IsNullOrEmpty(_toastMsg))
            {
                var ft = new FormattedText(_toastMsg, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    TfUiSemiBold, 13, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
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

            // 0.43 Hệ tọa độ không gian Oxyz (khi bật sẽ hiển thị cả lúc xuất/chèn ảnh!)
            if (ShowCoordSystem3D) RenderSpatialCoordinateSystem3D(dc, dpi);

            // 0.45 Các khối 3D & Mặt phẳng (Nón/Trụ/Cầu nét đứt chuẩn + Chóp/Lăng trụ + Khai triển Net GeoGebra)
            foreach (var sc3 in Solids3DList)
                RenderSolid3D(dc, sc3, cleanExport, dpi);

            // 0.46 Giao trực tiếp giữa 2 khối 3D (tự cập nhật khi kéo/xoay 1 trong 2 khối)
            if (_intersectSolidA != null && _intersectSolidB != null && Solids3DList.Contains(_intersectSolidA) && Solids3DList.Contains(_intersectSolidB))
            {
                var liveInterPts = Solid3DScene.ComputeSolidIntersectionPoints(_intersectSolidA, _intersectSolidB);
                if (liveInterPts.Count > 0)
                {
                    var interPts2d = liveInterPts.Select(p => _intersectSolidA.Project(p, out _)).ToList();
                    if (interPts2d.Count >= 2)
                    {
                        var gInt = new StreamGeometry();
                        using (var ctx = gInt.Open()) { ctx.BeginFigure(interPts2d[0], true, interPts2d.Count >= 3); ctx.PolyLineTo(interPts2d.Skip(1).ToList(), true, false); }
                        gInt.Freeze();
                        dc.DrawGeometry(FrozenBrush(Color.FromArgb(72, 239, 68, 68)), new Pen(FrozenBrush(Color.FromRgb(220, 38, 38)), 2.4), gInt);
                    }
                    foreach (var ip in interPts2d)
                        dc.DrawEllipse(FrozenBrush(Color.FromRgb(239, 68, 68)), new Pen(Brushes.White, 1.4), ip, 4.2, 4.2);
                }
            }

            // 0.47 Đang chọn điểm dựng mặt phẳng (P) tự do (hiện rõ kể cả khi chưa có khối 3D nào!)
            if (!cleanExport && _arbitraryPlanePoints.Count > 0)
            {
                var refProj = Solid3D ?? GetDefault3DReferenceScene();
                var p2d = _arbitraryPlanePoints.Select(p => refProj.Project(p, out _)).ToList();
                var prevPen = new Pen(FrozenBrush(Color.FromRgb(2, 132, 199)), 1.8) { DashStyle = DashStyles.Dash };
                for (int i = 0; i < p2d.Count; i++)
                {
                    if (i > 0) dc.DrawLine(prevPen, p2d[i - 1], p2d[i]);
                    dc.DrawEllipse(FrozenBrush(Color.FromRgb(2, 132, 199)), new Pen(Brushes.White, 1.5), p2d[i], 5.0, 5.0);
                    var ftP = new FormattedText($"M{i + 1}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TfUiBold, 12, FrozenBrush(Color.FromRgb(2, 132, 199)), dpi);
                    dc.DrawText(ftP, new Point(p2d[i].X + 8, p2d[i].Y - 14));
                }
            }

            // 0.5 Khung nhận diện đối tượng Office (ẩn khi export hoặc khi người dùng chọn ẩn nút cận)
            if (!cleanExport && OfficeContext != null && OfficeContext.ScreenBoundsDip.Width > 2 && (RevScene == null || RevShowBoundsHandles))
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

            // 4. Nét vẽ tay đa chất liệu mượt mà (Bút máy, Bút cọ thanh đậm, Phấn bảng, Bút chì, Nét đứt, Dạ quang)
            foreach (var st in Scene.Strokes)
            {
                if (st.Points.Count == 0) continue;
                if (st.Points.Count == 1)
                {
                    double rDot = Math.Max(1.4, st.Width * 0.55);
                    dc.DrawEllipse(new SolidColorBrush(ParseColor(st.ColorHex, st.Opacity)), null, new Point(st.Points[0][0], st.Points[0][1]), rDot, rDot);
                    continue;
                }
                string pStyle = st.IsHighlighter ? "highlighter" : (st.PenStyle ?? "fountain");

                if ((pStyle is "brush" or "fountain") && st.PointWidths.Count == st.Points.Count)
                {
                    var br = new SolidColorBrush(ParseColor(st.ColorHex, st.Opacity));
                    for (int i = 0; i < st.Points.Count - 1; i++)
                    {
                        Point p0 = new(st.Points[i][0], st.Points[i][1]);
                        Point p1 = new(st.Points[i + 1][0], st.Points[i + 1][1]);
                        double segW = Math.Max(0.9, 0.5 * (st.PointWidths[i] + st.PointWidths[i + 1]));
                        var segPen = new Pen(br, segW) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                        dc.DrawLine(segPen, p0, p1);
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

                if (pStyle == "chalk")
                {
                    var dustPen = new Pen(new SolidColorBrush(ParseColor(st.ColorHex, 0.24)), st.Width * 1.95) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    var midPen = new Pen(new SolidColorBrush(ParseColor(st.ColorHex, 0.58)), st.Width * 1.25) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    var corePen = new Pen(new SolidColorBrush(ParseColor(st.ColorHex, 0.94)), Math.Max(1.2, st.Width * 0.78)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    dc.DrawGeometry(null, dustPen, geo);
                    dc.DrawGeometry(null, midPen, geo);
                    dc.DrawGeometry(null, corePen, geo);
                }
                else if (pStyle == "pencil")
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
                        DashCap = PenLineCap.Flat,
                        LineJoin = PenLineJoin.Round
                    };
                    if (pStyle == "dash") pen.DashStyle = new DashStyle(new double[] { 5.0, 3.8 }, 0);
                    dc.DrawGeometry(null, pen, geo);
                }
            }

            // 5. Chữ & Công thức Toán học (Text-Math LaTeX)
            var mathTypeface = new Typeface(new FontFamily("Times New Roman"), FontStyles.Italic, FontWeights.Bold, FontStretches.Normal);
            foreach (var t in Scene.Texts)
            {
                var ft = new FormattedText(t.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, mathTypeface, t.FontSize, new SolidColorBrush(ParseColor(t.ColorHex)), dpi);
                double topY = t.Y - ft.Height / 2;
                dc.DrawText(ft, new Point(t.X, topY));
                if (!cleanExport && (t.Id == _selectedTextId || _selTextIds.Contains(t.Id)))
                {
                    var selRect = new Rect(t.X - 4, topY - 3, Math.Max(20, ft.Width) + 8, Math.Max(16, ft.Height) + 6);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(18, 37, 99, 235)),
                        new Pen(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 1.5) { DashStyle = DashStyles.Dash }, selRect, 5, 5);
                }
            }
            foreach (var mf in Scene.MathFormulas)
            {
                Rect r = LatexMathRenderer.Draw(dc, mf.Latex, new Point(mf.X, mf.Y), ParseColor(mf.ColorHex, 1.0), mf.FontSize, dpi, mf.ForceMath);
                if (!cleanExport && (mf.Id == _selectedMathId || _selMathIds.Contains(mf.Id)))
                {
                    var selRect = new Rect(r.X - 4, r.Y - 4, r.Width + 8, r.Height + 8);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(18, 37, 99, 235)),
                        new Pen(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 1.5) { DashStyle = DashStyles.Dash }, selRect, 5, 5);
                }
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
        private GeoShape? FindNearestCircle(Point pos, double radius)
        {
            foreach (var s in Scene.Shapes)
            {
                if (s.Type != "circle") continue;
                var p1 = GetPoint(s.P1Id); var p2 = GetPoint(s.P2Id);
                if (p1 == null || p2 == null) continue;
                double r = Dist(new Point(p1.X, p1.Y), new Point(p2.X, p2.Y)), d = Dist(pos, new Point(p1.X, p1.Y));
                if (Math.Abs(d - r) <= radius || d <= r) return s;
            }
            return null;
        }

        private GeoRegion? FindRegionContainingPoint(Point pos)
        {
            for (int i = Scene.Regions.Count - 1; i >= 0; i--)
            {
                var reg = Scene.Regions[i]; if (reg.Type != "polygon") continue;
                var pts = reg.PointIds.Select(GetPoint).Where(p => p != null).Select(p => new Point(p!.X, p.Y)).ToList();
                if (PointInPolygon(pos, pts)) return reg;
            }
            return null;
        }

        private bool TryHitRevRegion(Point pos, out int regionIdx)
        {
            regionIdx = -1;
            if (RevScene == null || !RevScene.IsValid) return false;
            RevScene.EnsureRegionsInitialized();
            double dx = pos.X - RevScene.AxisOrigin.X, dy = pos.Y - RevScene.AxisOrigin.Y;
            double t = dx * RevScene.AxisU.X + dy * RevScene.AxisU.Y, r = dx * RevScene.AxisV.X + dy * RevScene.AxisV.Y;
            for (int i = 0; i < RevScene.Regions.Count; i++)
            {
                var rg = RevScene.Regions[i];
                if (t >= rg.StartT && t <= rg.EndT)
                {
                    double r1 = RevolutionScene.SampleR(RevScene.Curve1TR, t);
                    double r2 = RevScene.HasTwoCurves ? RevolutionScene.SampleR(RevScene.Curve2TR, t) : 0.0;
                    if (r >= Math.Min(r1, r2) - 8.0 && r <= Math.Max(r1, r2) + 8.0) { regionIdx = i; return true; }
                }
            }
            return false;
        }

        private string Next3DLabel()
        {
            const string pref = "MNPQEFGHIJK";
            var used = new HashSet<string>(Solid3D!.Vertices.Select(v => v.Label));
            foreach (char c in pref) if (!used.Contains(c.ToString())) return c.ToString();
            return $"P{Solid3D.NextIdx}";
        }

        private Vertex3D? FindNearest3DVertex(Point pos, double radius)
        {
            if (Solid3D == null) return null;
            Vertex3D? best = null; double bestD = radius;
            foreach (var v in Solid3D.Vertices)
            {
                if (string.IsNullOrEmpty(v.Label)) continue;
                double d = Dist(pos, Solid3D.Project(Solid3D.GetEffectivePos(v, true), out _));
                if (d < bestD) { bestD = d; best = v; }
            }
            return best;
        }

        private (Edge3D edge, double t)? FindNearest3DEdge(Point pos, double radius)
        {
            if (Solid3D == null) return null;
            Edge3D? bestE = null; double bestD = radius, bestT = 0.5;
            foreach (var e in Solid3D.Edges)
            {
                var v1 = Solid3D.GetV(e.V1Id); var v2 = Solid3D.GetV(e.V2Id);
                if (v1 == null || v2 == null) continue;
                Point a = Solid3D.Project(Solid3D.GetEffectivePos(v1, true), out _);
                Point b = Solid3D.Project(Solid3D.GetEffectivePos(v2, true), out _);
                double d = PointToSegmentDist(pos, a, b);
                if (d < bestD) { bestD = d; bestE = e; double vx = b.X - a.X, vy = b.Y - a.Y, den = vx * vx + vy * vy; bestT = den > 1e-5 ? Math.Clamp(((pos.X - a.X) * vx + (pos.Y - a.Y) * vy) / den, 0.05, 0.95) : 0.5; }
            }
            return bestE != null ? (bestE, bestT) : null;
        }

        private bool TryIntersect2DSegments(GeoShape s1, GeoShape s2, out Point inter)
        {
            inter = default;
            var a = GetPoint(s1.P1Id); var b = GetPoint(s1.P2Id); var c = GetPoint(s2.P1Id); var d = GetPoint(s2.P2Id);
            if (a == null || b == null || c == null || d == null) return false;
            double x1 = a.X, y1 = a.Y, x2 = b.X, y2 = b.Y, x3 = c.X, y3 = c.Y, x4 = d.X, y4 = d.Y;
            double den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4); if (Math.Abs(den) < 1e-6) return false;
            inter = new Point(((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / den, ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / den);
            return true;
        }

        private void DrawCanvasBackground(DrawingContext dc)
        {
            Rect full = new(0, 0, ActualWidth, ActualHeight);
            if (CanvasBg == CanvasBackgroundMode.Transparent) { if (!IsPassthrough) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), null, full); return; }
            if (CanvasBg == CanvasBackgroundMode.ChalkboardGrid) { dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(27, 73, 58)), null, full); DrawGridLines(dc, 26, Color.FromArgb(55, 167, 243, 208), Color.FromArgb(95, 167, 243, 208)); }
            else if (CanvasBg == CanvasBackgroundMode.WhiteboardPlain) dc.DrawRectangle(Brushes.White, null, full);
            else if (CanvasBg == CanvasBackgroundMode.WhiteboardGrid) { dc.DrawRectangle(Brushes.White, null, full); DrawGridLines(dc, 26, Color.FromArgb(140, 219, 234, 254), Color.FromArgb(200, 147, 197, 253)); }
            else if (CanvasBg == CanvasBackgroundMode.Blackboard) { dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(17, 24, 39)), null, full); DrawGridLines(dc, 28, Color.FromArgb(40, 148, 163, 184), Color.FromArgb(75, 148, 163, 184)); }
        }

        private void DrawGridLines(DrawingContext dc, double step, Color minorCol, Color majorCol)
        {
            var minorPen = new Pen(new SolidColorBrush(minorCol), 0.7); var majorPen = new Pen(new SolidColorBrush(majorCol), 1.1);
            int idx = 0; for (double x = 0; x <= ActualWidth; x += step, idx++) dc.DrawLine(idx % 5 == 0 ? majorPen : minorPen, new Point(x, 0), new Point(x, ActualHeight));
            idx = 0; for (double y = 0; y <= ActualHeight; y += step, idx++) dc.DrawLine(idx % 5 == 0 ? majorPen : minorPen, new Point(0, y), new Point(ActualWidth, y));
        }

        // Ve duong cong khep kin bang StreamGeometry lien tuc theo tung cum thay/khuat -> NET DUT DEU & CHUAN 100%!
        private static void DrawClosedCurveWithVisibility(DrawingContext dc, Point[] pts, bool[] segVisible, Pen solidPen, Pen dashedPen)
        {
            int n = segVisible.Length;
            if (n < 3) return;
            bool allSame = true;
            for (int i = 1; i < n; i++) if (segVisible[i] != segVisible[0]) { allSame = false; break; }

            if (allSame)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open()) { ctx.BeginFigure(pts[0], false, true); for (int i = 1; i < n; i++) ctx.LineTo(pts[i], true, true); }
                g.Freeze(); dc.DrawGeometry(null, segVisible[0] ? solidPen : dashedPen, g);
                return;
            }

            // Tim diem chuyen trang thai dau tien de moi cum (lien / dut) khong bi cat doi o chi so 0
            int start = 0;
            for (int i = 0; i < n; i++) if (segVisible[i] != segVisible[(i - 1 + n) % n]) { start = i; break; }

            int cur = start, drawn = 0;
            while (drawn < n)
            {
                bool vis = segVisible[cur];
                var run = new List<Point> { pts[cur] };
                while (drawn < n && segVisible[cur] == vis)
                {
                    int nxt = (cur + 1) % n;
                    run.Add(pts[nxt]);
                    cur = nxt;
                    drawn++;
                }
                var g = new StreamGeometry();
                using (var ctx = g.Open()) { ctx.BeginFigure(run[0], false, false); ctx.PolyLineTo(run.Skip(1).ToList(), true, true); }
                g.Freeze();
                dc.DrawGeometry(null, vis ? solidPen : dashedPen, g);
            }
        }

        private void RenderSolid3D(DrawingContext dc, Solid3DScene s, bool cleanExport, double dpi)
        {
            string lineHex = (CanvasBg is CanvasBackgroundMode.ChalkboardGrid or CanvasBackgroundMode.Blackboard) ? "#F8FAFC" : StrokeColorHex;
            var solidPen = MakePen(lineHex, StrokeWidth, "solid", false);
            var dashedPen = MakePen(lineHex, Math.Max(1.6, StrokeWidth * 0.90), "dashed", false);
            Color baseSolidCol = ParseColor(s.SolidColorHex, 1.0);
            Vec3 vCam = s.GetCameraLocalDir();
            Vec3 lightDir = new Vec3(-0.32, 0.62, 0.72).Normalized();
            var lblTf = new Typeface(new FontFamily("Times New Roman"), FontStyles.Italic, FontWeights.Bold, FontStretches.Normal);
            Color lblCol = (CanvasBg is CanvasBackgroundMode.ChalkboardGrid or CanvasBackgroundMode.Blackboard) ? Colors.White : Color.FromRgb(15, 23, 42);

            if (!cleanExport && ActiveSpinAxis != SpinAxisMode.None)
            {
                var spinTf = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                if (ActiveSpinAxis == SpinAxisMode.Vertical)
                {
                    Point pTopAxis = new(s.CenterScreen.X, s.CenterScreen.Y - s.Scale * 1.45);
                    Point pBotAxis = new(s.CenterScreen.X, s.CenterScreen.Y + s.Scale * 1.35);
                    dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(165, 56, 189, 248)), 1.4) { DashStyle = DashStyles.Dash }, pBotAxis, pTopAxis);
                    double yawNorm = ((s.YawDeg % 360.0) + 360.0) % 360.0;
                    var ftSpin = new FormattedText($"⟳ Trục đứng: {yawNorm:0}°", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, spinTf, 11.5, new SolidColorBrush(Color.FromRgb(2, 132, 199)), dpi);
                    dc.DrawText(ftSpin, new Point(pTopAxis.X + 8, pTopAxis.Y - 6));
                }
                else if (ActiveSpinAxis == SpinAxisMode.Horizontal)
                {
                    Point pLeftAxis = new(s.CenterScreen.X - s.Scale * 1.55, s.CenterScreen.Y);
                    Point pRightAxis = new(s.CenterScreen.X + s.Scale * 1.55, s.CenterScreen.Y);
                    dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(175, 249, 115, 22)), 1.4) { DashStyle = DashStyles.Dash }, pLeftAxis, pRightAxis);
                    double pitchNorm = ((s.PitchDeg % 360.0) + 360.0) % 360.0;
                    var ftSpin = new FormattedText($"⇵ Trục ngang: {pitchNorm:0}°", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, spinTf, 11.5, new SolidColorBrush(Color.FromRgb(234, 88, 12)), dpi);
                    dc.DrawText(ftSpin, new Point(pRightAxis.X - ftSpin.Width, pRightAxis.Y - 20));
                }
            }

            void Draw3DLabel(Vec3 p3, string lbl, bool dot = true)
            {
                Point pt = s.Project(p3, out _);
                if (dot) dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(239, 68, 68)), new Pen(Brushes.White, 1.3), pt, 3.6, 3.6);
                var ft = new FormattedText(lbl, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, lblTf, 19, new SolidColorBrush(lblCol), dpi);
                dc.DrawText(ft, new Point(pt.X + 8, pt.Y - 18));
            }

            if (s.Kind == "plane_only")
            {
                if (s.CutPlane != null && s.CutPlane.Visible)
                    DrawCutPlaneSheet3D(dc, s, s.CutPlane, lblTf, dpi);
                return;
            }

            if (s.IsNetUnfolded)
            {
                RenderNetUnfolded3D(dc, s, cleanExport, solidPen, baseSolidCol, lightDir, lblTf, lblCol, dpi);
                return;
            }

            if (s.Kind == "sphere")
            {
                Vec3 offSph = s.GetPartOffset(s.GetPartSideSign(new Vec3(0, 0, 0)));
                Point cPt = s.Project(offSph, out _);
                double rPx = s.Radius * s.Scale;

                if (s.IsSolidShaded)
                {
                    Color cHi = Color.FromArgb((byte)(Math.Clamp(s.SolidOpacity + 0.25, 0.3, 0.95) * 255),
                        (byte)Math.Min(255, baseSolidCol.R + (255 - baseSolidCol.R) * 0.62),
                        (byte)Math.Min(255, baseSolidCol.G + (255 - baseSolidCol.G) * 0.62),
                        (byte)Math.Min(255, baseSolidCol.B + (255 - baseSolidCol.B) * 0.62));
                    Color cMid = ShadeColor(baseSolidCol, 0.95, Math.Clamp(s.SolidOpacity + 0.15, 0.25, 0.90));
                    Color cDark = ShadeColor(baseSolidCol, 0.42, Math.Clamp(s.SolidOpacity + 0.28, 0.35, 0.95));
                    var rgb = new RadialGradientBrush { GradientOrigin = new Point(0.32, 0.28), Center = new Point(0.45, 0.45), RadiusX = 0.68, RadiusY = 0.68 };
                    rgb.GradientStops.Add(new GradientStop(cHi, 0.0)); rgb.GradientStops.Add(new GradientStop(cMid, 0.55)); rgb.GradientStops.Add(new GradientStop(cDark, 1.0));
                    dc.DrawEllipse(rgb, null, cPt, rPx, rPx);
                }

                // Vong xich dao 96 diem: nua phia truoc (Z >= 0) 1 net lien, nua bi che phia sau (Z < 0) 1 net dut ro rang!
                int n = 96;
                Point[] eqPts = new Point[n]; bool[] eqVis = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    double t0 = 2 * Math.PI * i / n, tm = 2 * Math.PI * (i + 0.5) / n;
                    eqPts[i] = s.Project(offSph + new Vec3(s.Radius * Math.Cos(t0), 0, s.Radius * Math.Sin(t0)), out _);
                    eqVis[i] = s.RotatePoint(new Vec3(s.Radius * Math.Cos(tm), 0, s.Radius * Math.Sin(tm))).Z >= 0.0;
                }
                DrawClosedCurveWithVisibility(dc, eqPts, eqVis, solidPen, dashedPen);
                dc.DrawEllipse(null, solidPen, cPt, rPx, rPx);

                Point rimPt = new(cPt.X + rPx * Math.Cos(-0.35), cPt.Y + rPx * Math.Sin(-0.35));
                dc.DrawLine(dashedPen, cPt, rimPt);
                Draw3DLabel(new Vec3(0, 0, 0), "O");
                var ftR = new FormattedText("R", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, lblTf, 16, new SolidColorBrush(lblCol), dpi);
                dc.DrawText(ftR, new Point(0.5 * (cPt.X + rimPt.X) - 6, 0.5 * (cPt.Y + rimPt.Y) - 20));
                if (s.CutPlane != null) RenderCutPlaneAndAnalyticalSection(dc, s, s.CutPlane, vCam, lblTf, lblCol, dpi);
                return;
            }

            if (s.Kind is "cylinder" or "cone")
            {
                bool isCone = s.Kind == "cone";
                int n = 96;
                double R = s.Radius, h2 = s.Height * 0.5;
                Vec3 offBot = s.GetPartOffset(s.GetPartSideSign(new Vec3(0, -h2, 0)));
                Vec3 offTop = s.GetPartOffset(s.GetPartSideSign(new Vec3(0, h2, 0)));
                Vec3 pBotC = new Vec3(0, -h2, 0) + offBot, pTopC = new Vec3(0, h2, 0) + offTop;
                Point ptBotC = s.Project(pBotC, out double zBotC), ptTopC = s.Project(pTopC, out double zTopC);

                Point[] botPts = new Point[n], topPts = new Point[n];
                double[] botZ = new double[n], topZ = new double[n];
                bool[] botVis = new bool[n], topVis = new bool[n];

                for (int i = 0; i < n; i++)
                {
                    double th = 2 * Math.PI * i / n, tm = 2 * Math.PI * (i + 0.5) / n;
                    Vec3 pb3 = new(R * Math.Cos(th), -h2, R * Math.Sin(th));
                    botPts[i] = s.Project(pb3 + s.GetPartOffset(s.GetPartSideSign(pb3)), out botZ[i]);
                    if (!isCone)
                    {
                        Vec3 pt3 = new(R * Math.Cos(th), h2, R * Math.Sin(th));
                        topPts[i] = s.Project(pt3 + s.GetPartOffset(s.GetPartSideSign(pt3)), out topZ[i]);
                    }

                    Vec3 rad = new(Math.Cos(tm), 0, Math.Sin(tm));
                    if (isCone)
                    {
                        Vec3 latN = new Vec3(s.Height * rad.X, R, s.Height * rad.Z).Normalized();
                        botVis[i] = (vCam.Y < 0.0) || (Vec3.Dot(latN, vCam) > 0.0);
                    }
                    else
                    {
                        botVis[i] = (vCam.Y <= 0.02) || (Vec3.Dot(rad, vCam) >= 0.0);
                        topVis[i] = (vCam.Y >= -0.02) || (Vec3.Dot(rad, vCam) >= 0.0);
                    }
                }

                if (s.IsSolidShaded)
                {
                    var quads = new List<(Point[] pts, double depth, Color col)>(n * 3);
                    for (int i = 0; i < n; i++)
                    {
                        int nxt = (i + 1) % n;
                        double tm = 2 * Math.PI * (i + 0.5) / n;
                        Vec3 nLoc = isCone ? new Vec3(s.Height * Math.Cos(tm), R, s.Height * Math.Sin(tm)).Normalized() : new Vec3(Math.Cos(tm), 0, Math.Sin(tm));
                        double light = Math.Clamp(0.48 + 0.54 * Math.Max(-0.25, Vec3.Dot(s.RotatePoint(nLoc), lightDir)), 0.28, 1.24);
                        Color col = ShadeColor(baseSolidCol, light, s.SolidOpacity);

                        if (isCone) quads.Add((new[] { ptTopC, botPts[i], botPts[nxt] }, (zTopC + botZ[i] + botZ[nxt]) / 3.0, col));
                        else quads.Add((new[] { botPts[i], botPts[nxt], topPts[nxt], topPts[i] }, 0.25 * (botZ[i] + botZ[nxt] + topZ[i] + topZ[nxt]), col));

                        double lBot = Math.Clamp(0.46 + 0.45 * Math.Max(-0.2, Vec3.Dot(s.RotatePoint(new Vec3(0, -1, 0)), lightDir)), 0.30, 1.10);
                        quads.Add((new[] { ptBotC, botPts[i], botPts[nxt] }, (zBotC + botZ[i] + botZ[nxt]) / 3.0 - 0.02, ShadeColor(baseSolidCol, lBot, s.SolidOpacity * 0.95)));
                        if (!isCone)
                        {
                            double lTop = Math.Clamp(0.55 + 0.45 * Math.Max(-0.2, Vec3.Dot(s.RotatePoint(new Vec3(0, 1, 0)), lightDir)), 0.35, 1.22);
                            quads.Add((new[] { ptTopC, topPts[i], topPts[nxt] }, (zTopC + topZ[i] + topZ[nxt]) / 3.0 + 0.02, ShadeColor(baseSolidCol, lTop, Math.Min(0.9, s.SolidOpacity + 0.08))));
                        }
                    }
                    quads.Sort((a, b) => a.depth.CompareTo(b.depth));
                    foreach (var q in quads)
                    {
                        var g = new StreamGeometry(); using (var ctx = g.Open()) { ctx.BeginFigure(q.pts[0], true, true); ctx.PolyLineTo(q.pts.Skip(1).ToList(), false, false); }
                        g.Freeze(); dc.DrawGeometry(new SolidColorBrush(q.col), null, g);
                    }
                }

                // Ve day bang StreamGeometry lien mach -> cung khuat phia sau NET DUT CHUAN 100%!
                DrawClosedCurveWithVisibility(dc, botPts, botVis, solidPen, dashedPen);
                if (!isCone) DrawClosedCurveWithVisibility(dc, topPts, topVis, solidPen, dashedPen);

                // Dung 2 duong sinh tiep tuyen bien
                double hLen = Math.Sqrt(vCam.X * vCam.X + vCam.Z * vCam.Z);
                if (hLen > 1e-4)
                {
                    if (isCone)
                    {
                        double rhs = -(R / Math.Max(0.1, s.Height)) * vCam.Y / hLen;
                        if (Math.Abs(rhs) < 0.995)
                        {
                            double phi = Math.Atan2(vCam.Z, vCam.X), delta = Math.Acos(rhs);
                            foreach (double th in new[] { phi + delta, phi - delta })
                            {
                                Vec3 pb = new(R * Math.Cos(th), -h2, R * Math.Sin(th));
                                dc.DrawLine(solidPen, ptTopC, s.Project(pb + s.GetPartOffset(s.GetPartSideSign(pb)), out _));
                            }
                        }
                    }
                    else
                    {
                        double th1 = Math.Atan2(-vCam.X, vCam.Z);
                        foreach (double th in new[] { th1, th1 + Math.PI })
                        {
                            double x = R * Math.Cos(th), z = R * Math.Sin(th);
                            Vec3 pb = new(x, -h2, z), pt = new(x, h2, z);
                            dc.DrawLine(solidPen, s.Project(pb + s.GetPartOffset(s.GetPartSideSign(pb)), out _), s.Project(pt + s.GetPartOffset(s.GetPartSideSign(pt)), out _));
                        }
                    }
                }

                dc.DrawLine(dashedPen, ptBotC, ptTopC);
                Draw3DLabel(pBotC, "O");
                Draw3DLabel(pTopC, isCone ? "S" : "O'");
                if (s.CutPlane != null) RenderCutPlaneAndAnalyticalSection(dc, s, s.CutPlane, vCam, lblTf, lblCol, dpi);
                return;
            }

            // Da dien 3D (Chop, Tu dien, Lap phuong, Hop CN, Lang tru)
            // Khi da cat tach khoi bang mat phang (P) -> dung Sutherland-Hodgman cat thuc thanh 2 nua khep kin + nap thiet dien o ca 2 nua!
            if (s.HasActiveSplit)
            {
                if (s.CutPlane != null && s.CutPlane.Visible)
                    DrawCutPlaneSheet3D(dc, s, s.CutPlane, lblTf, dpi);

                var splitPolys = s.ComputeSplitPolyHalves3D();
                var drawList = new List<(Point[] pts, double depth, Color fillCol, Pen edgePen, bool isCap)>();
                Color upperCol = ParseColor(s.SolidColorHex, 1.0);
                Color lowerCol = ParseColor("#34D399", 1.0);
                Color capCol = ParseColor(s.CutPlane?.SectionColorHex ?? "#FACC15", 1.0);
                var capBorderPen = MakePen("#D97706", Math.Max(1.8, StrokeWidth * 0.9), "solid", false);

                foreach (var (pts3d, outwardN, partSide, isCap) in splitPolys)
                {
                    if (pts3d.Count < 3) continue;
                    var pts2d = new Point[pts3d.Count];
                    double sumZ = 0;
                    for (int i = 0; i < pts3d.Count; i++) { pts2d[i] = s.Project(pts3d[i], out double z); sumZ += z; }
                    double light = Math.Clamp(0.50 + 0.50 * Math.Max(-0.2, Vec3.Dot(s.RotatePoint(outwardN), lightDir)), 0.32, 1.22);
                    Color baseC = isCap ? capCol : (partSide > 0 ? upperCol : lowerCol);
                    double op = isCap ? 0.72 : (s.IsSolidShaded ? Math.Max(0.45, s.SolidOpacity * 0.92) : 0.22);
                    bool frontFacing = s.RotatePoint(outwardN).Z >= -0.05;
                    Pen ep = isCap ? capBorderPen : (frontFacing ? solidPen : dashedPen);
                    drawList.Add((pts2d, sumZ / pts3d.Count, ShadeColor(baseC, light, op), ep, isCap));
                }

                drawList.Sort((a, b) => a.depth.CompareTo(b.depth));
                foreach (var q in drawList)
                {
                    var g = new StreamGeometry();
                    using (var ctx = g.Open()) { ctx.BeginFigure(q.pts[0], true, true); ctx.PolyLineTo(q.pts.Skip(1).ToList(), true, false); }
                    g.Freeze();
                    dc.DrawGeometry(FrozenBrush(q.fillCol), q.edgePen, g);
                }

                foreach (var v in s.Vertices)
                {
                    if (string.IsNullOrEmpty(v.Label)) continue;
                    Point pv = s.Project(s.GetEffectivePos(v, true), out _);
                    dc.DrawEllipse(FrozenBrush(Color.FromRgb(239, 68, 68)), new Pen(Brushes.White, 1.3), pv, 3.4, 3.4);
                    var ft = new FormattedText(v.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, lblTf, 18, FrozenBrush(lblCol), dpi);
                    dc.DrawText(ft, new Point(pv.X + 7, pv.Y - 17));
                }
                return;
            }

            var proj = new Dictionary<string, (Point pt, double z)>();
            foreach (var v in s.Vertices) { Point p = s.Project(s.GetEffectivePos(v, true), out double z); proj[v.Id] = (p, z); }

            if (s.IsSolidShaded)
            {
                var faceList = new List<(Point[] pts, double depth, Color col)>();
                foreach (var f in s.Faces)
                {
                    var pts = f.VertexIds.Where(id => proj.ContainsKey(id)).Select(id => proj[id].pt).ToArray(); if (pts.Length < 3) continue;
                    double avgZ = f.VertexIds.Where(id => proj.ContainsKey(id)).Average(id => proj[id].z);
                    double light = Math.Clamp(0.48 + 0.52 * Math.Max(-0.2, Vec3.Dot(s.RotatePoint(s.GetOutwardFaceNormal(f)), lightDir)), 0.30, 1.22);
                    faceList.Add((pts, avgZ, ShadeColor(baseSolidCol, light, s.SolidOpacity * 0.85)));
                }
                faceList.Sort((a, b) => a.depth.CompareTo(b.depth));
                foreach (var q in faceList) { var g = new StreamGeometry(); using (var ctx = g.Open()) { ctx.BeginFigure(q.pts[0], true, true); ctx.PolyLineTo(q.pts.Skip(1).ToList(), false, false); } g.Freeze(); dc.DrawGeometry(new SolidColorBrush(q.col), null, g); }
            }

            if (s.CutPlane != null && s.CutPlane.Visible)
                DrawCutPlaneSheet3D(dc, s, s.CutPlane, lblTf, dpi);

            foreach (var sec in s.Sections)
            {
                var pts = sec.VertexIds.Where(id => proj.ContainsKey(id)).Select(id => proj[id].pt).ToList(); if (pts.Count < 3) continue;
                var geo = new StreamGeometry(); using (var ctx = geo.Open()) { ctx.BeginFigure(pts[0], true, true); ctx.PolyLineTo(pts.Skip(1).ToList(), true, false); }
                geo.Freeze(); dc.DrawGeometry(new SolidColorBrush(ParseColor(sec.ColorHex, sec.Opacity)), new Pen(new SolidColorBrush(ParseColor(sec.ColorHex, 0.95)), 1.6), geo);
            }

            if (s.CutPlane != null && s.CutPlane.IsInteractive)
            {
                var live = s.ComputeLivePolySection(s.CutPlane);
                if (live.Points3D.Count >= 3)
                {
                    var pts2d = live.Points3D.Select(p => s.Project(p, out _)).ToArray();
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open()) { ctx.BeginFigure(pts2d[0], true, true); ctx.PolyLineTo(pts2d.Skip(1).ToList(), true, false); }
                    geo.Freeze();
                    dc.DrawGeometry(new SolidColorBrush(ParseColor(s.CutPlane.SectionColorHex, 0.45)), null, geo);
                    var secSolid = MakePen(s.CutPlane.SectionColorHex, Math.Max(2.2, StrokeWidth), "solid", false);
                    var secDash = MakePen(s.CutPlane.SectionColorHex, Math.Max(1.8, StrokeWidth * 0.9), "dashed", false);
                    for (int i = 0; i < pts2d.Length; i++)
                        dc.DrawLine(live.EdgeVisible[i] ? secSolid : secDash, pts2d[i], pts2d[(i + 1) % pts2d.Length]);
                }
            }

            foreach (var e in s.Edges)
            {
                if (!proj.TryGetValue(e.V1Id, out var a) || !proj.TryGetValue(e.V2Id, out var b)) continue;
                string hex = e.CustomColorHex ?? (e.IsConstruction ? "#FDE047" : lineHex);
                dc.DrawLine(MakePen(hex, StrokeWidth, s.IsEdgeHidden(e) ? "dashed" : "solid", false), a.pt, b.pt);
            }

            foreach (var v in s.Vertices)
            {
                if (!proj.TryGetValue(v.Id, out var pv) || string.IsNullOrEmpty(v.Label)) continue;
                bool isPicked = !cleanExport && (_step3DVertices.Contains(v.Id) || _pending3DParallelVertex == v.Id);
                dc.DrawEllipse(new SolidColorBrush(isPicked ? Color.FromRgb(245, 158, 11) : Color.FromRgb(239, 68, 68)), new Pen(Brushes.White, 1.4), pv.pt, isPicked ? 5.5 : 3.6, isPicked ? 5.5 : 3.6);
                var ft = new FormattedText(v.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, lblTf, 19, new SolidColorBrush(lblCol), dpi);
                dc.DrawText(ft, new Point(pv.pt.X + 8, pv.pt.Y - 18));
            }
        }

        private void RenderNetUnfolded3D(DrawingContext dc, Solid3DScene s, bool cleanExport, Pen borderPen, Color baseCol, Vec3 lightDir, Typeface lblTf, Color lblCol, double dpi)
        {
            var netFaces = s.ComputeNetFacePolygons3D();
            if (netFaces.Count == 0) return;

            var rendered = new List<(Point[] pts, double depth, Color fillCol, string label, bool isBase, bool isTopCap)>();
            foreach (var (pts3d, label, isBase, isTopCap) in netFaces)
            {
                if (pts3d.Count < 3) continue;
                var pts2d = new Point[pts3d.Count];
                double sumZ = 0;
                for (int i = 0; i < pts3d.Count; i++)
                {
                    pts2d[i] = s.Project(pts3d[i], out double z);
                    sumZ += z;
                }
                Vec3 n = Vec3.Cross(pts3d[1] - pts3d[0], pts3d[2] - pts3d[0]).Normalized();
                double light = Math.Clamp(0.55 + 0.45 * Math.Abs(Vec3.Dot(s.RotatePoint(n), lightDir)), 0.35, 1.2);
                Color c = isBase ? ParseColor("#F59E0B", 0.58) : (isTopCap ? ParseColor("#10B981", 0.55) : ShadeColor(baseCol, light, 0.52));
                rendered.Add((pts2d, sumZ / pts3d.Count, c, label, isBase, isTopCap));
            }

            rendered.Sort((a, b) => a.depth.CompareTo(b.depth));
            var netBorderPen = new Pen(new SolidColorBrush(Color.FromRgb(29, 78, 216)), Math.Max(1.6, StrokeWidth * 0.85)) { LineJoin = PenLineJoin.Round };

            foreach (var item in rendered)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(item.pts[0], true, true);
                    ctx.PolyLineTo(item.pts.Skip(1).ToList(), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(new SolidColorBrush(item.fillCol), netBorderPen, g);

                if (s.Kind == "poly")
                {
                    foreach (var p in item.pts)
                        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(239, 68, 68)), new Pen(Brushes.White, 1.2), p, 3.2, 3.2);
                }
                if (!cleanExport && s.UnfoldProgress > 0.35 && !string.IsNullOrEmpty(item.label))
                {
                    double cx = item.pts.Average(p => p.X), cy = item.pts.Average(p => p.Y);
                    var ft = new FormattedText(item.label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                        11.5, new SolidColorBrush(lblCol), dpi);
                    dc.DrawText(ft, new Point(cx - ft.Width * 0.5, cy - ft.Height * 0.5));
                }
            }

            if (!cleanExport)
            {
                Point topP = new(s.CenterScreen.X, s.CenterScreen.Y - s.Scale * 1.45);
                var ftHud = new FormattedText($"📦 Khai triển Net: {s.UnfoldProgress * 100.0:0}%", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    12, new SolidColorBrush(Color.FromRgb(180, 83, 9)), dpi);
                dc.DrawText(ftHud, new Point(topP.X - ftHud.Width * 0.5, topP.Y));
            }
        }

        private Solid3DScene GetDefault3DReferenceScene() => new()
        {
            CenterScreen = new Point(ActualWidth > 100 ? ActualWidth * 0.48 : 480, ActualHeight > 100 ? ActualHeight * 0.55 : 430),
            Scale = 135.0,
            YawDeg = -28.0,
            PitchDeg = 22.0
        };

        private void RenderSpatialCoordinateSystem3D(DrawingContext dc, double dpi)
        {
            var refSc = Solid3D ?? GetDefault3DReferenceScene();

            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(65, 100, 116, 139)), 0.85);
            for (int i = -2; i <= 2; i++)
            {
                double off = i * 0.65;
                dc.DrawLine(gridPen, refSc.Project(new Vec3(-1.3, 0, off), out _), refSc.Project(new Vec3(1.3, 0, off), out _));
                dc.DrawLine(gridPen, refSc.Project(new Vec3(off, 0, -1.3), out _), refSc.Project(new Vec3(off, 0, 1.3), out _));
            }

            Point pO = refSc.Project(new Vec3(0, 0, 0), out _);
            Point pX = refSc.Project(new Vec3(1.65, 0, 0), out _);
            Point pY = refSc.Project(new Vec3(0, 0, 1.65), out _);
            Point pZ = refSc.Project(new Vec3(0, 1.65, 0), out _);

            void DrawAxisArrow(Point from, Point to, Color col, string name)
            {
                var pen = new Pen(new SolidColorBrush(col), 2.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(pen, from, to);
                double ang = Math.Atan2(to.Y - from.Y, to.X - from.X);
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(to, true, true);
                    ctx.LineTo(new Point(to.X - 10 * Math.Cos(ang - 0.4), to.Y - 10 * Math.Sin(ang - 0.4)), true, false);
                    ctx.LineTo(new Point(to.X - 10 * Math.Cos(ang + 0.4), to.Y - 10 * Math.Sin(ang + 0.4)), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(new SolidColorBrush(col), null, g);
                var ft = new FormattedText(name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Times New Roman"), FontStyles.Italic, FontWeights.Bold, FontStretches.Normal),
                    17, new SolidColorBrush(col), dpi);
                dc.DrawText(ft, new Point(to.X + 6, to.Y - 10));
            }

            DrawAxisArrow(pO, pX, Color.FromRgb(220, 38, 38), "x");
            DrawAxisArrow(pO, pY, Color.FromRgb(5, 150, 105), "y");
            DrawAxisArrow(pO, pZ, Color.FromRgb(37, 99, 235), "z");
            var ftO = new FormattedText("O", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Times New Roman"), FontStyles.Italic, FontWeights.Bold, FontStretches.Normal),
                16, new SolidColorBrush(Color.FromRgb(15, 23, 42)), dpi);
            dc.DrawText(ftO, new Point(pO.X - 14, pO.Y + 4));
        }

        private static void DrawCutPlaneSheet3D(DrawingContext dc, Solid3DScene s, CutPlane3D plane, Typeface lblTf, double dpi)
        {
            var corners3d = plane.GetQuadCorners();
            var pts = corners3d.Select(c => s.Project(c, out _)).ToArray();
            var geo = new StreamGeometry();
            using (var ctx = geo.Open()) { ctx.BeginFigure(pts[0], true, true); ctx.PolyLineTo(pts.Skip(1).ToList(), true, false); }
            geo.Freeze();

            Color fillCol = ParseColor(plane.PlaneColorHex, 0.20);
            Color borderCol = ParseColor(plane.PlaneColorHex, 0.85);
            dc.DrawGeometry(new SolidColorBrush(fillCol), new Pen(new SolidColorBrush(borderCol), 1.6), geo);

            // Ve nhan mat phang (P) o goc trai-duoi tam mat phang
            Point cornerP = pts[0];
            Point insideP = new(cornerP.X + (pts[2].X - cornerP.X) * 0.14, cornerP.Y + (pts[2].Y - cornerP.Y) * 0.14);
            var ftP = new FormattedText(plane.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, lblTf, 16, new SolidColorBrush(Color.FromRgb(2, 132, 199)), dpi);
            dc.DrawText(ftP, new Point(insideP.X - ftP.Width * 0.5, insideP.Y - ftP.Height * 0.5));
        }

        private void RenderCutPlaneAndAnalyticalSection(DrawingContext dc, Solid3DScene s, CutPlane3D plane, Vec3 vCam, Typeface lblTf, Color lblCol, double dpi)
        {
            if (plane.Visible) DrawCutPlaneSheet3D(dc, s, plane, lblTf, dpi);

            var sec = s.ComputeAnalyticalRevolutionSection(plane);
            if (!sec.HasIntersection || sec.Boundary3D.Count < 3) return;

            int n = sec.Boundary3D.Count;
            Point[] pts2d = new Point[n];
            bool[] segVis = new bool[n];
            double h2 = s.Height * 0.5;

            for (int i = 0; i < n; i++)
                pts2d[i] = s.Project(sec.Boundary3D[i], out _);

            for (int i = 0; i < n; i++)
            {
                Vec3 a = sec.Boundary3D[i], b = sec.Boundary3D[(i + 1) % n];
                Vec3 mid = (a + b) * 0.5;
                if (s.Kind == "sphere")
                {
                    segVis[i] = s.RotatePoint(mid).Z >= -0.02;
                }
                else if (s.Kind == "cylinder")
                {
                    if (Math.Abs(mid.Y - h2) < 0.025) segVis[i] = vCam.Y >= -0.02;
                    else if (Math.Abs(mid.Y + h2) < 0.025) segVis[i] = vCam.Y <= 0.02;
                    else segVis[i] = Vec3.Dot(new Vec3(mid.X, 0, mid.Z).Normalized(), vCam) >= -0.02;
                }
                else // cone
                {
                    if (Math.Abs(mid.Y + h2) < 0.025) segVis[i] = vCam.Y <= 0.02;
                    else
                    {
                        double rho = Math.Sqrt(mid.X * mid.X + mid.Z * mid.Z);
                        Vec3 latN = rho > 1e-4 ? new Vec3(s.Height * mid.X / rho, s.Radius, s.Height * mid.Z / rho).Normalized() : new Vec3(0, 1, 0);
                        segVis[i] = Vec3.Dot(latN, vCam) >= -0.02;
                    }
                }
            }

            var fillGeo = new StreamGeometry();
            using (var ctx = fillGeo.Open()) { ctx.BeginFigure(pts2d[0], true, true); ctx.PolyLineTo(pts2d.Skip(1).ToList(), true, false); }
            fillGeo.Freeze();
            dc.DrawGeometry(new SolidColorBrush(ParseColor(plane.SectionColorHex, 0.44)), null, fillGeo);

            var secSolid = MakePen("#D97706", Math.Max(2.2, StrokeWidth), "solid", false);
            var secDash = MakePen("#D97706", Math.Max(1.8, StrokeWidth * 0.9), "dashed", false);
            DrawClosedCurveWithVisibility(dc, pts2d, segVis, secSolid, secDash);

            // Hinh cau: ve tam H, doan vuong goc OH, ky hieu goc vuong & ban kinh r = sqrt(R^2 - d^2)
            if (sec.IsSphereCircle)
            {
                Point ptO = s.Project(new Vec3(0, 0, 0), out _);
                Point ptH = s.Project(sec.SphereFootH, out _);
                var perpPen = MakePen("#DC2626", 1.9, "dashed", false);
                if (sec.SphereDistD > 0.03)
                {
                    dc.DrawLine(perpPen, ptO, ptH);
                    // Ky hieu goc vuong nho tai H
                    plane.GetBasis(out Vec3 u, out _);
                    Vec3 dirHO = (new Vec3(0, 0, 0) - sec.SphereFootH).Normalized();
                    double sq = Math.Min(0.14, Math.Min(sec.SphereDistD * 0.45, sec.SphereSectionR * 0.45));
                    if (sq > 0.03)
                    {
                        Point q1 = s.Project(sec.SphereFootH + dirHO * sq, out _);
                        Point q2 = s.Project(sec.SphereFootH + dirHO * sq + u * sq, out _);
                        Point q3 = s.Project(sec.SphereFootH + u * sq, out _);
                        var sqPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 38, 38)), 1.3);
                        dc.DrawLine(sqPen, q1, q2);
                        dc.DrawLine(sqPen, q2, q3);
                    }
                }
                // Ve ban kinh r cua duong tron thiet dien
                plane.GetBasis(out Vec3 uRad, out _);
                Point ptM = s.Project(sec.SphereFootH + uRad * sec.SphereSectionR, out _);
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(180, 83, 9)), 1.7) { DashStyle = DashStyles.Dash }, ptH, ptM);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(220, 38, 38)), new Pen(Brushes.White, 1.3), ptH, 3.8, 3.8);

                var ftH = new FormattedText("H", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, lblTf, 17, new SolidColorBrush(lblCol), dpi);
                dc.DrawText(ftH, new Point(ptH.X - 18, ptH.Y - 16));
                var ftRad = new FormattedText("r = √(R² - d²)", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 11.5, new SolidColorBrush(Color.FromRgb(180, 83, 9)), dpi);
                dc.DrawText(ftRad, new Point(0.5 * (ptH.X + ptM.X) + 4, 0.5 * (ptH.Y + ptM.Y) - 16));
            }
            else if (!string.IsNullOrEmpty(sec.ShapeName))
            {
                // Hien thi ten thiet dien giai tich gon gang canh tam mat phang
                Point tagPt = s.Project(plane.GetQuadCorners()[1], out _);
                var ftTag = new FormattedText($"Thiết diện: {sec.ShapeName}", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 11.5, new SolidColorBrush(Color.FromRgb(180, 83, 9)), dpi);
                dc.DrawText(ftTag, new Point(tagPt.X - ftTag.Width - 6, tagPt.Y - ftTag.Height - 2));
            }
        }

        private void RenderRevolutionScene(DrawingContext dc, RevolutionScene s, bool cleanExport, double dpi)
        {
            s.EnsureRegionsInitialized();
            Color volBaseCol = ParseColor(Volume3DColorHex, 1.0);
            var lblTf = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

            Point MapSlicePt(double t, double r, bool use3D) => use3D ? s.Project3D(t, r, 0.0, RevXCompression) : s.ToScreen2D(t, r);

            void DrawPlanarSlice(double a, double b, string hexCol, double opacity, bool isSelectedBorder, bool use3D = false)
            {
                if (b - a < 1.5) return;
                int steps = 64;
                var poly = new List<Point>(steps * 2 + 2);
                for (int i = 0; i <= steps; i++) { double t = a + (b - a) * i / steps; poly.Add(MapSlicePt(t, RevolutionScene.SampleR(s.Curve1TR, t), use3D)); }
                for (int i = steps; i >= 0; i--) { double t = a + (b - a) * i / steps; poly.Add(MapSlicePt(t, s.HasTwoCurves ? RevolutionScene.SampleR(s.Curve2TR, t) : 0.0, use3D)); }
                var geo = new StreamGeometry(); using (var ctx = geo.Open()) { ctx.BeginFigure(poly[0], true, true); ctx.PolyLineTo(poly.Skip(1).ToList(), true, false); }
                geo.Freeze();
                Color c = ParseColor(hexCol, 1.0);
                Pen borderPen = isSelectedBorder ? new Pen(new SolidColorBrush(ShadeColor(c, 0.55, 0.95)), 2.2) : new Pen(new SolidColorBrush(ShadeColor(c, 0.72, 0.75)), 1.2);
                dc.DrawGeometry(MakeRegionBrush(hexCol, opacity, FillPattern), borderPen, geo);
                var boundPen = new Pen(new SolidColorBrush(ShadeColor(c, 0.65, Math.Min(1.0, opacity + 0.35))), 1.5) { DashStyle = DashStyles.Dash };
                dc.DrawLine(boundPen, MapSlicePt(a, RevolutionScene.SampleR(s.Curve1TR, a), use3D), MapSlicePt(a, s.HasTwoCurves ? RevolutionScene.SampleR(s.Curve2TR, a) : 0, use3D));
                dc.DrawLine(boundPen, MapSlicePt(b, RevolutionScene.SampleR(s.Curve1TR, b), use3D), MapSlicePt(b, s.HasTwoCurves ? RevolutionScene.SampleR(s.Curve2TR, b) : 0, use3D));
            }

            if (RevMode == MathRenderMode.Area2D)
            {
                for (int k = 0; k < s.Regions.Count; k++)
                {
                    var rg = s.Regions[k];
                    if (!rg.Enabled) continue;
                    DrawPlanarSlice(rg.StartT, rg.EndT, rg.ColorHex, FillOpacity, !cleanExport && s.Regions.Count > 1 && k == s.ActiveRegionIndex);
                    if (s.Regions.Count > 1 && rg.EndT - rg.StartT >= 18.0)
                    {
                        double tMid = 0.5 * (rg.StartT + rg.EndT), rMid = 0.5 * (RevolutionScene.SampleR(s.Curve1TR, tMid) + (s.HasTwoCurves ? RevolutionScene.SampleR(s.Curve2TR, tMid) : 0.0));
                        Point cPt = s.ToScreen2D(tMid, rMid);
                        var ft = new FormattedText($"S{k + 1}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, lblTf, 13, new SolidColorBrush(Color.FromRgb(15, 23, 42)), dpi);
                        dc.DrawText(ft, new Point(cPt.X - ft.Width / 2, cPt.Y - ft.Height / 2));
                    }
                }
            }
            else
            {
                if (RevSweepDeg >= 1.0)
                {
                    int nAng = Math.Max(8, (int)Math.Ceiling(RevSweepDeg / 8.0));
                    double maxRad = RevSweepDeg * Math.PI / 180.0;
                    var quads = new List<(Point[] pts, double depth, Color col)>();
                    var rings = new List<Point[]>();
                    for (int k = 0; k < s.Regions.Count; k++)
                    {
                        var rg = s.Regions[k];
                        if (!rg.Enabled || rg.EndT - rg.StartT < 1.5) continue;
                        double a = rg.StartT, b = rg.EndT;
                        int nT = Math.Max(20, (int)Math.Ceiling(48.0 * (b - a) / Math.Max(1.0, s.TMax - s.TMin)));
                        double[] ts = new double[nT + 1], rs = new double[nT + 1];
                        for (int i = 0; i <= nT; i++) { ts[i] = a + (b - a) * i / nT; rs[i] = RevolutionScene.SampleR(s.Curve1TR, ts[i]); }
                        var grid = new Point[nT + 1, nAng + 1];
                        for (int i = 0; i <= nT; i++) for (int j = 0; j <= nAng; j++) grid[i, j] = s.Project3D(ts[i], rs[i], maxRad * j / nAng, RevXCompression);
                        for (int i = 0; i < nT; i++)
                        {
                            double rAvg = 0.5 * (rs[i] + rs[i + 1]), dt = ts[i + 1] - ts[i], dr = rs[i + 1] - rs[i], sl = Hypot(dt, dr);
                            double slopeN = sl > 1e-4 ? -dr / sl : 0, signR = rAvg >= 0 ? 1.0 : -1.0;
                            for (int j = 0; j < nAng; j++)
                            {
                                double thMid = maxRad * (j + 0.5) / nAng;
                                double depth = 0.5 * (s.ComputeDepth(ts[i], rs[i], thMid) + s.ComputeDepth(ts[i + 1], rs[i + 1], thMid));
                                double nx = slopeN * 0.4, ny = Math.Cos(thMid) * signR, nz = Math.Sin(thMid) * signR;
                                double nLen = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (nLen > 1e-5) { nx /= nLen; ny /= nLen; nz /= nLen; }
                                double sxN = nx * s.AxisU.X + ny * s.AxisV.X, syN = nx * s.AxisU.Y + ny * s.AxisV.Y;
                                var rotN = s.RotateNormal3D(sxN, syN, nz);
                                double light = Math.Clamp(0.54 + 0.48 * Math.Max(-0.35, rotN.nx * -0.25 + rotN.ny * -0.55 + rotN.nz * 0.78), 0.30, 1.25);
                                quads.Add((new[] { grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1] }, depth, ShadeColor(volBaseCol, light, Math.Max(0.42, FillOpacity + 0.22))));
                            }
                        }
                        if (RevShowCaps)
                        {
                            void AddCap(double tEnd, double rEnd, double sideSign)
                            {
                                if (Math.Abs(rEnd) < 1.0) return;
                                Point center = s.Project3D(tEnd, 0, 0, RevXCompression);
                                Color capCol = ShadeColor(volBaseCol, sideSign > 0 ? 0.92 : 0.72, Math.Min(0.92, FillOpacity + 0.32));
                                for (int j = 0; j < nAng; j++)
                                {
                                    double th0 = maxRad * j / nAng, th1 = maxRad * (j + 1) / nAng;
                                    quads.Add((new[] { center, s.Project3D(tEnd, rEnd, th0, RevXCompression), s.Project3D(tEnd, rEnd, th1, RevXCompression) }, s.ComputeDepth(tEnd, rEnd * 0.55, 0.5 * (th0 + th1), RevXCompression) + sideSign * 0.03, capCol));
                                }
                            }
                            AddCap(ts[0], rs[0], -1); AddCap(ts[nT], rs[nT], 1);
                        }
                        Point[] r0 = new Point[nAng + 1], rN = new Point[nAng + 1];
                        for (int j = 0; j <= nAng; j++) { r0[j] = grid[0, j]; rN[j] = grid[nT, j]; }
                        rings.Add(r0); rings.Add(rN);
                    }
                    quads.Sort((x, y) => x.depth.CompareTo(y.depth));
                    foreach (var q in quads) { var g = new StreamGeometry(); using (var ctx = g.Open()) { ctx.BeginFigure(q.pts[0], true, true); ctx.PolyLineTo(q.pts.Skip(1).ToList(), false, false); } g.Freeze(); dc.DrawGeometry(new SolidColorBrush(q.col), null, g); }
                    var rimPen = new Pen(new SolidColorBrush(ShadeColor(volBaseCol, 0.55, 0.85)), 1.4);
                    foreach (var ring in rings) { var gRing = new StreamGeometry(); using (var ctx = gRing.Open()) { ctx.BeginFigure(ring[0], false, false); for (int j = 1; j < ring.Length; j++) ctx.LineTo(ring[j], true, false); } gRing.Freeze(); dc.DrawGeometry(null, rimPen, gRing); }
                }
                if (RevShowAreaIn3D || RevSweepDeg < 1.0)
                    foreach (var rg in s.Regions) if (rg.Enabled) DrawPlanarSlice(rg.StartT, rg.EndT, rg.ColorHex, Math.Clamp(FillOpacity + 0.05, 0.25, 0.65), false, use3D: s.HasTurntableRotation);
                if (!cleanExport && (s.HasTurntableRotation || ActiveSpinAxis != SpinAxisMode.None))
                {
                    Point pv = s.GetPivotScreenCenter();
                    if (ActiveSpinAxis == SpinAxisMode.Horizontal || (ActiveSpinAxis == SpinAxisMode.None && Math.Abs(s.TurntablePitchDeg % 360.0) > 0.1 && Math.Abs(s.TurntableYawDeg % 360.0) < 0.1))
                    {
                        var hPen = new Pen(new SolidColorBrush(Color.FromArgb(165, 249, 115, 22)), 1.35) { DashStyle = DashStyles.Dash };
                        dc.DrawLine(hPen, new Point(pv.X - 135, pv.Y), new Point(pv.X + 135, pv.Y));
                        double angNorm = ((s.TurntablePitchDeg % 360.0) + 360.0) % 360.0;
                        var ftP = new FormattedText($"⇵ Trục ngang: {angNorm:0}°", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, lblTf, 11.5, new SolidColorBrush(Color.FromRgb(234, 88, 12)), dpi);
                        dc.DrawText(ftP, new Point(pv.X + 14, pv.Y - 22));
                    }
                    else
                    {
                        var axisPen = new Pen(new SolidColorBrush(Color.FromArgb(145, 56, 189, 248)), 1.3) { DashStyle = DashStyles.Dash };
                        dc.DrawLine(axisPen, new Point(pv.X, pv.Y - 115), new Point(pv.X, pv.Y + 115));
                        double angNorm = ((s.TurntableYawDeg % 360.0) + 360.0) % 360.0;
                        var ftYaw = new FormattedText($"⟳ Trục đứng: {angNorm:0}°", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, lblTf, 11.5, new SolidColorBrush(Color.FromRgb(2, 132, 199)), dpi);
                        dc.DrawText(ftYaw, new Point(pv.X + 8, pv.Y - 112));
                    }
                }
            }

            if (!cleanExport && Tool == "add_bound")
            {
                double dx = _mousePos.X - s.AxisOrigin.X, dy = _mousePos.Y - s.AxisOrigin.Y;
                double tHover = Math.Clamp(dx * s.AxisU.X + dy * s.AxisU.Y, s.TMin, s.TMax);
                Point pAxis = s.ToScreen2D(tHover, 0), pCurve = s.ToScreen2D(tHover, RevolutionScene.SampleR(s.Curve1TR, tHover));
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(14, 165, 233)), 2.2) { DashStyle = DashStyles.Dash }, pAxis, pCurve);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(14, 165, 233)), new Pen(Brushes.White, 1.5), pAxis, 6.5, 6.5);
            }

            // Ve cap chot can DOC LAP [a, b], [c, d]... cho tung mien (cho phep an gon khi trinh chieu)
            if (!cleanExport && RevShowBoundsHandles)
            {
                for (int i = 0; i < s.Regions.Count; i++)
                {
                    var rg = s.Regions[i];
                    Point pS = s.ToScreen2D(rg.StartT, 0), pE = s.ToScreen2D(rg.EndT, 0);
                    dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(190, 239, 68, 68)), 2.0) { DashStyle = DashStyles.Dot }, pS, pE);

                    void DrawH(Point pt, string lbl, bool isActive)
                    {
                        var br = new SolidColorBrush(isActive ? Color.FromRgb(220, 38, 38) : Color.FromRgb(234, 88, 12));
                        dc.DrawEllipse(br, new Pen(Brushes.White, 2.0), pt, 9.0, 9.0);
                        var ft = new FormattedText(lbl, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, lblTf, 11.0, Brushes.White, dpi);
                        dc.DrawText(ft, new Point(pt.X - ft.Width / 2, pt.Y - ft.Height / 2));
                    }
                    DrawH(pS, RevolutionScene.GetHandleLabel(i, false), i == s.ActiveRegionIndex);
                    DrawH(pE, RevolutionScene.GetHandleLabel(i, true), i == s.ActiveRegionIndex);
                }
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
            var p1 = GetPoint(_stepPoints[0]); if (p1 == null) return;
            Point pt1 = new(p1.X, p1.Y), pt2 = _mousePos;
            var prevPen = new Pen(new SolidColorBrush(ParseColor("#2563EB")), 1.8) { DashStyle = DashStyles.Dash };
            Color penCol = ParseColor(StrokeColorHex, 1.0);

            if (Tool is "segment" or "dashed" or "vector")
            {
                dc.DrawLine(prevPen, pt1, pt2);
                if (ShowVirtualTools) { DrawVirtualRuler(dc, pt1, pt2, dpi); DrawVirtualPen(dc, pt2, penCol, Tool == "dashed" ? "dash" : PenTipStyle, true); }
            }
            else if (Tool == "circle") { dc.DrawEllipse(null, prevPen, pt1, Dist(pt1, pt2), Dist(pt1, pt2)); if (ShowVirtualTools) DrawVirtualCompass(dc, pt1, pt2, dpi, isIdle: false); }
            else if (Tool == "rect")
            {
                var r = new Rect(pt1, pt2); dc.DrawRectangle(null, prevPen, r);
                if (ShowVirtualTools) { DrawVirtualSetSquare(dc, new Point(r.Left, r.Bottom), 0.0, Math.Clamp(r.Width, 90, 220), Math.Clamp(r.Height, 65, 150), dpi); DrawVirtualPen(dc, pt2, penCol, PenTipStyle, true); }
            }
            else if (Tool is "triangle" or "fill_area")
            {
                var pts = _stepPoints.Select(GetPoint).Where(p => p != null).Select(p => new Point(p!.X, p.Y)).ToList();
                Point lastAnchor = pts[^1]; pts.Add(pt2);
                var geo = new StreamGeometry(); using (var ctx = geo.Open()) { ctx.BeginFigure(pts[0], Tool == "fill_area", Tool == "fill_area"); ctx.PolyLineTo(pts.Skip(1).ToList(), true, false); }
                geo.Freeze(); dc.DrawGeometry((Tool == "fill_area" && pts.Count >= 3) ? MakeRegionBrush(FillColorHex, FillOpacity, FillPattern) : null, prevPen, geo);
                if (ShowVirtualTools) { DrawVirtualRuler(dc, lastAnchor, pt2, dpi); DrawVirtualPen(dc, pt2, Tool == "fill_area" ? ParseColor(FillColorHex, 1.0) : penCol, PenTipStyle, true); }
            }
            else if (Tool == "altitude")
            {
                var sh = FindNearestSegment(pt2, 22.0);
                if (sh != null)
                {
                    var s1 = GetPoint(sh.P1Id); var s2 = GetPoint(sh.P2Id);
                    if (s1 != null && s2 != null)
                    {
                        Point sp1 = new(s1.X, s1.Y), sp2 = new(s2.X, s2.Y), foot = ProjectPointOnLine(pt1, sp1, sp2);
                        dc.DrawLine(prevPen, pt1, foot);
                        if (ShowVirtualTools)
                        {
                            Vector eBase = Dist(foot, sp2) >= Dist(foot, sp1) ? (sp2 - foot) : (sp1 - foot); if (eBase.Length < 1e-4) eBase = sp2 - sp1;
                            Vector eVert = pt1 - foot; if (eVert.Length < 1e-4) eVert = new Vector(-eBase.Y, eBase.X);
                            DrawVirtualSetSquareByAxes(dc, foot, eBase, eVert, 145.0, Math.Clamp(Dist(pt1, foot), 75.0, 185.0));
                            DrawVirtualPen(dc, foot, penCol, PenTipStyle, true);
                        }
                        return;
                    }
                }
                dc.DrawLine(prevPen, pt1, pt2);
                if (ShowVirtualTools)
                {
                    Vector vDir = pt1 - pt2;
                    if (vDir.Length > 4) { Vector bDir = new(vDir.Y, -vDir.X); if (bDir.X < 0) bDir = -bDir; DrawVirtualSetSquareByAxes(dc, pt2, bDir, vDir, 130.0, 85.0); }
                    else DrawVirtualSetSquare(dc, pt2, 0.0, 130.0, 85.0, dpi);
                    DrawVirtualPen(dc, pt2, penCol, PenTipStyle, true);
                }
            }
            else if (Tool is "fill_angle" or "right_angle")
            {
                if (_stepPoints.Count == 1) { dc.DrawLine(prevPen, pt1, pt2); if (ShowVirtualTools) { DrawVirtualRuler(dc, pt1, pt2, dpi); DrawVirtualPen(dc, pt2, penCol, PenTipStyle, true); } }
                else if (_stepPoints.Count == 2)
                {
                    var pv = GetPoint(_stepPoints[1]);
                    if (pv != null)
                    {
                        Point vPt = new(pv.X, pv.Y); dc.DrawLine(prevPen, pt1, vPt); dc.DrawLine(prevPen, vPt, pt2);
                        if (ShowVirtualTools)
                        {
                            if (Tool == "right_angle") { Vector eBase = pt1 - vPt, eVert = new(-eBase.Y, eBase.X); if (eVert.X * (pt2.X - vPt.X) + eVert.Y * (pt2.Y - vPt.Y) < 0) eVert = -eVert; DrawVirtualSetSquareByAxes(dc, vPt, eBase, eVert, 130.0, 90.0); }
                            else DrawVirtualProtractor(dc, vPt, Math.Atan2(pt1.Y - vPt.Y, pt1.X - vPt.X) * 180.0 / Math.PI, 95.0, dpi);
                            DrawVirtualPen(dc, pt2, Tool == "fill_angle" ? ParseColor(FillColorHex, 1.0) : penCol, PenTipStyle, true);
                        }
                    }
                }
            }
        }

        private static void DrawVirtualSetSquareByAxes(DrawingContext dc, Point origin, Vector eBase, Vector eVert, double w, double h)
        {
            if (eBase.Length < 1e-4 || eVert.Length < 1e-4) return;
            eBase.Normalize(); eVert.Normalize();
            Point Map(double lx, double ly) => new(origin.X + lx * eBase.X + ly * eVert.X, origin.Y + lx * eBase.Y + ly * eVert.Y);
            var outer = new StreamGeometry(); using (var ctx = outer.Open()) { ctx.BeginFigure(Map(0, 0), true, true); ctx.PolyLineTo(new[] { Map(w, 0), Map(0, h) }, true, false); } outer.Freeze();
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(145, 191, 219, 254)), new Pen(new SolidColorBrush(Color.FromArgb(225, 30, 64, 175)), 1.8), outer);
            if (w > 85 && h > 55) { var inner = new StreamGeometry(); using (var ctx = inner.Open()) { ctx.BeginFigure(Map(22, 16), true, true); ctx.PolyLineTo(new[] { Map(w - 44, 16), Map(22, h - 34) }, true, false); } inner.Freeze(); dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), new Pen(new SolidColorBrush(Color.FromArgb(190, 30, 64, 175)), 1.2), inner); }
            var sqPen = new Pen(new SolidColorBrush(Color.FromArgb(230, 220, 38, 38)), 1.6); dc.DrawLine(sqPen, Map(12, 0), Map(12, 12)); dc.DrawLine(sqPen, Map(12, 12), Map(0, 12));
            var tickPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 30, 58, 138)), 0.9);
            for (double x = 8; x < w - 10; x += 8) dc.DrawLine(tickPen, Map(x, 0), Map(x, x % 16 == 0 ? 8 : 5));
            for (double y = 8; y < h - 10; y += 8) dc.DrawLine(tickPen, Map(0, y), Map(y % 16 == 0 ? 8 : 5, y));
        }

        private static void DrawVirtualSetSquare(DrawingContext dc, Point origin, double angleDeg, double w, double h, double dpi)
        {
            double rad = angleDeg * Math.PI / 180.0;
            DrawVirtualSetSquareByAxes(dc, origin, new Vector(Math.Cos(rad), Math.Sin(rad)), new Vector(Math.Sin(rad), -Math.Cos(rad)), w, h);
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
            double len = Dist(a, b); if (len < 15) return;
            bool reverse = (b.X < a.X - 0.5) || (Math.Abs(b.X - a.X) <= 0.5 && b.Y < a.Y);
            Point p0 = reverse ? b : a, p1 = reverse ? a : b;
            double angle = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X) * 180.0 / Math.PI;
            double rulerLen = Math.Max(len + 45, 230), rulerH = 34;
            dc.PushTransform(new TranslateTransform(p0.X, p0.Y)); dc.PushTransform(new RotateTransform(angle));
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
                RevScene.EnsureRegionsInitialized();
                foreach (var rg in RevScene.Regions)
                {
                    if (!rg.Enabled || rg.EndT - rg.StartT < 1.0) continue;
                    double a = rg.StartT, b = rg.EndT;
                    for (int i = 0; i <= 36; i++)
                    {
                        double t = a + (b - a) * i / 36.0;
                        double r1 = RevolutionScene.SampleR(RevScene.Curve1TR, t);
                        double r2 = RevScene.HasTwoCurves ? RevolutionScene.SampleR(RevScene.Curve2TR, t) : 0.0;
                        if (RevMode == MathRenderMode.Area2D)
                        {
                            var p1 = RevScene.ToScreen2D(t, r1); var p2 = RevScene.ToScreen2D(t, r2);
                            xs.Add(p1.X); xs.Add(p2.X); ys.Add(p1.Y); ys.Add(p2.Y);
                        }
                        else
                        {
                            for (int j = 0; j <= 12; j++)
                            {
                                double ang = (RevSweepDeg * Math.PI / 180.0) * j / 12.0;
                                var p = RevScene.Project3D(t, r1, ang, RevXCompression);
                                xs.Add(p.X); ys.Add(p.Y);
                                if (RevScene.HasTwoCurves)
                                {
                                    var p2 = RevScene.ToScreen2D(t, r2);
                                    xs.Add(p2.X); ys.Add(p2.Y);
                                }
                            }
                        }
                    }
                }
            }
            if (ShowCoordSystem3D)
            {
                var refSc = Solid3D ?? GetDefault3DReferenceScene();
                foreach (var axPt in new[] { new Vec3(0, 0, 0), new Vec3(1.75, 0, 0), new Vec3(0, 0, 1.75), new Vec3(0, 1.75, 0), new Vec3(-1.3, 0, -1.3) })
                {
                    Point ap = refSc.Project(axPt, out _);
                    xs.Add(ap.X); ys.Add(ap.Y);
                }
            }
            foreach (var sc3 in Solids3DList) { var sb = GetSolid3DBoundsFor(sc3); if (!sb.IsEmpty) { xs.Add(sb.Left); xs.Add(sb.Right); ys.Add(sb.Top); ys.Add(sb.Bottom); } }
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
                var r = GetTextRect(t);
                xs.Add(r.Left - 4); xs.Add(r.Right + 4);
                ys.Add(r.Top - 4); ys.Add(r.Bottom + 4);
            }
            foreach (var mf in Scene.MathFormulas)
            {
                var r = GetMathFormulaRect(mf);
                xs.Add(r.Left - 4); xs.Add(r.Right + 4);
                ys.Add(r.Top - 4); ys.Add(r.Bottom + 4);
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
