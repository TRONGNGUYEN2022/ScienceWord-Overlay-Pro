using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ScienceWordOverlay.Models;
using ScienceWordOverlay.Utils;

namespace ScienceWordOverlay
{
    public partial class ToolbarWindow : Window
    {
        private readonly OverlayWindow _overlay;
        private readonly DispatcherTimer _appPollTimer;
        private readonly DispatcherTimer _revAnimTimer;
        private TextMathWindow? _textMathWin;
        private bool _isAnimatingRev = false;
        private readonly OfficeDoubleClickHook _dblClickHook = new();
        private readonly DispatcherTimer _dblClickDelayTimer;
        private string _pendingDblClickProc = "";
        private IntPtr _pendingDblClickHwnd = IntPtr.Zero;
        private int _pendingDblClickX = 0, _pendingDblClickY = 0;

        private Point _fabDownPos;
        private bool _fabPressed = false, _fabDragged = false;

        public ToolbarWindow(OverlayWindow overlay)
        {
            InitializeComponent();
            _overlay = overlay;
            PreviewKeyDown += (_, e) => _overlay.Surface.HandleKeyDown(e);

            _overlay.Surface.MetricsUpdated += txt =>
            {
                TxtMetrics.Text = txt;
                if (_overlay.Surface.RevScene != null && _overlay.Surface.RevScene.IsValid && ExpandedToolbarPanel.Visibility == Visibility.Visible)
                    Row3Panel.Visibility = Visibility.Visible;
            };
            _overlay.Surface.ActiveToolChanged += t =>
            {
                TxtActiveTool.Text = t switch
                {
                    "select" => "Chọn / Di chuyển",
                    "orbit_3d" => "Xoay 3D",
                    "add_bound" => "+ Cận/Miền",
                    "point_edge_3d" => "Điểm cạnh 3D",
                    "parallel_3d" => "Song song 3D",
                    "section_3d" => "Thiết diện 3D",
                    "plane_3pts_3d" => "Mp qua 3 điểm",
                    "plane_para_3d" => "Mp 1đ // mặt",
                    "pen" => "Bút viết mịn",
                    "highlighter" => "Dạ quang",
                    "eraser" => "Tẩy nét",
                    _ => TxtActiveTool.Text
                };
            };
            _overlay.Surface.MathFormulaEditRequested += mf =>
            {
                EnsureTextMathWindow().OpenForEdit(mf);
            };
            _overlay.Surface.TextEditRequested += txt =>
            {
                EnsureTextMathWindow().OpenForEditText(txt);
            };
            _overlay.Surface.HandwritingEditRequested += cluster =>
            {
                EnsureTextMathWindow().OpenForEditHandwriting(cluster);
            };
            _overlay.Surface.SpinAxisStateChanged += axis =>
            {
                if (BtnAutoSpin3D != null) BtnAutoSpin3D.Content = axis == SpinAxisMode.Vertical ? "⏸ Đứng" : "⟳ Đứng";
                if (BtnAutoSpinH3D != null) BtnAutoSpinH3D.Content = axis == SpinAxisMode.Horizontal ? "⏸ Ngang" : "⇵ Ngang";
                if (BtnRevVertSpin != null) BtnRevVertSpin.Content = axis == SpinAxisMode.Vertical ? "⏸ Đứng" : "⟳ Đứng";
                if (BtnRevHorizSpin != null) BtnRevHorizSpin.Content = axis == SpinAxisMode.Horizontal ? "⏸ Ngang" : "⇵ Ngang";
            };
            _overlay.Surface.RevHandlesVisibilityChanged += visible =>
            {
                if (ChkRevHandles != null && ChkRevHandles.IsChecked != visible)
                    ChkRevHandles.IsChecked = visible;
            };
            _overlay.Surface.NetProgressUpdated += pct =>
            {
                if (SlNet3D != null && Math.Abs(SlNet3D.Value - pct) > 0.5)
                    SlNet3D.Value = pct;
            };

            _appPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _appPollTimer.Tick += OnPollForegroundApp; _appPollTimer.Start();

            _revAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            _revAnimTimer.Tick += OnRevAnimTick;

            Loaded += (_, _) => ClampToScreenBounds();
            SizeChanged += (_, _) => ClampToScreenBounds();

            _dblClickDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _dblClickDelayTimer.Tick += (_, _) =>
            {
                _dblClickDelayTimer.Stop();
                bool ok = false; string up = _pendingDblClickProc.ToUpperInvariant();
                if (up is "WINWORD" or "POWERPNT" or "EXCEL") ok = _overlay.Surface.TryRestoreFromOfficeDoubleClick(_pendingDblClickProc);
                else if (up.Contains("SCIENCEWORD") || up == "SW") ok = _overlay.Surface.TryRestoreFromScienceWordDoubleClick(_pendingDblClickHwnd, _pendingDblClickX, _pendingDblClickY);
                if (ok && BtnPassthrough.IsChecked == true) BtnPassthrough.IsChecked = false;
            };
            _dblClickHook.TargetAppDoubleClicked += (proc, hwnd, px, py) =>
            {
                if (BtnPassthrough.IsChecked != true) return;
                _pendingDblClickProc = proc; _pendingDblClickHwnd = hwnd; _pendingDblClickX = px; _pendingDblClickY = py;
                Dispatcher.BeginInvoke(() => { _dblClickDelayTimer.Stop(); _dblClickDelayTimer.Start(); });
            };
            _dblClickHook.Install();
        }

        private void ToggleFabMode(bool collapseToCircle)
        {
            FabCirclePanel.Visibility = collapseToCircle ? Visibility.Visible : Visibility.Collapsed;
            ExpandedToolbarPanel.Visibility = collapseToCircle ? Visibility.Collapsed : Visibility.Visible;
            Dispatcher.BeginInvoke(ClampToScreenBounds, DispatcherPriority.Loaded);
        }

        private void BtnToCircle_Click(object sender, RoutedEventArgs e) => ToggleFabMode(true);
        private void Fab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { _fabPressed = true; _fabDragged = false; _fabDownPos = e.GetPosition(this); ((UIElement)sender).CaptureMouse(); }
        private void Fab_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_fabPressed || e.LeftButton != MouseButtonState.Pressed) return;
            Point cur = e.GetPosition(this);
            if (!_fabDragged && (Math.Abs(cur.X - _fabDownPos.X) > 5 || Math.Abs(cur.Y - _fabDownPos.Y) > 5)) { _fabDragged = true; _fabPressed = false; ((UIElement)sender).ReleaseMouseCapture(); try { DragMove(); } catch { } }
        }
        private void Fab_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { if (_fabPressed) { _fabPressed = false; ((UIElement)sender).ReleaseMouseCapture(); if (!_fabDragged) ToggleFabMode(false); } }

        private void Fab_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var menu = new ContextMenu();
            var m1 = new MenuItem { Header = "📂 Bung thanh công cụ" }; m1.Click += (_, _) => ToggleFabMode(false); menu.Items.Add(m1);
            var m2 = new MenuItem { Header = BtnPassthrough.IsChecked == true ? "✏️ Chuyển về chế độ Vẽ" : "🖱️ Bật Xuyên chuột" }; m2.Click += (_, _) => BtnPassthrough.IsChecked = !(BtnPassthrough.IsChecked == true); menu.Items.Add(m2);
            var m3 = new MenuItem { Header = "📥 Chèn hình vào App" }; m3.Click += (_, _) => _overlay.Surface.InsertIntoTargetApp(); menu.Items.Add(m3);
            menu.Items.Add(new Separator());
            var m4 = new MenuItem { Header = "❌ Thoát ứng dụng" }; m4.Click += BtnClose_Click; menu.Items.Add(m4);
            menu.PlacementTarget = FabCirclePanel; menu.IsOpen = true;
        }

        private void ClampToScreenBounds()
        {
            var wa = SystemParameters.WorkArea; if (ActualWidth <= 0) return;
            if (Left + ActualWidth > wa.Right) Left = Math.Max(wa.Left + 2, wa.Right - ActualWidth - 4);
            if (Left < wa.Left) Left = wa.Left + 2; if (Top < wa.Top) Top = wa.Top + 2;
        }

        private void OnPollForegroundApp(object? sender, EventArgs e)
        {
            var (hwnd, procName, _) = Win32Helper.GetForegroundAppInfo(); if (hwnd == IntPtr.Zero) return;
            var myHwnd = new WindowInteropHelper(this).Handle; var ovHwnd = new WindowInteropHelper(_overlay).Handle;
            if (hwnd == myHwnd || hwnd == ovHwnd || string.Equals(procName, "ScienceWord_Overlay_Pro", StringComparison.OrdinalIgnoreCase)) return;
            _overlay.Surface.LastTargetHwnd = hwnd; _overlay.Surface.LastTargetProcName = procName;
            TxtTargetApp.Text = "Đích: " + Win32Helper.FriendlyAppName(procName);
        }

        private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
        private void ActivateTool(string tag, string displayName) { if (BtnPassthrough.IsChecked == true) BtnPassthrough.IsChecked = false; _overlay.Surface.SetTool(tag); TxtActiveTool.Text = displayName; }
        private TextMathWindow EnsureTextMathWindow()
        {
            if (_textMathWin == null || !_textMathWin.IsLoaded && _textMathWin.AllowRealClose)
            {
                _textMathWin = new TextMathWindow(_overlay.Surface);
                try { if (_overlay.IsVisible) _textMathWin.Owner = _overlay; } catch { }
            }
            return _textMathWin;
        }

        private void QuickSelect_Click(object sender, RoutedEventArgs e) => ActivateTool("select", "Chọn / Di chuyển");

        private void BtnTextMath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (BtnPassthrough.IsChecked == true) BtnPassthrough.IsChecked = false;
                var w = EnsureTextMathWindow();
                if (w.IsVisible) w.Hide(); else w.OpenForCreate(penMode: false);
            }
            catch (Exception ex)
            {
                _overlay.Surface.ShowToast("Lỗi mở Text-Math: " + ex.Message, 5000);
            }
        }

        private void BtnQuickPen_Click(object sender, RoutedEventArgs e)
        {
            ActivateTool(_overlay.Surface.PenTipStyle == "highlighter" ? "highlighter" : (_overlay.Surface.PenTipStyle == "eraser" ? "eraser" : "pen"), "Bút viết mịn");
        }

        private void BtnMenu2D_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            void AddItem(string header, string tag, string shortName) { var mi = new MenuItem { Header = header }; mi.Click += (_, _) => ActivateTool(tag, shortName); menu.Items.Add(mi); }
            AddItem("✥ Chọn & Di chuyển hình 2D/3D", "select", "Chọn / Di chuyển");
            menu.Items.Add(new Separator());
            AddItem("• Điểm (A, B, C...)", "point", "Điểm");
            AddItem("📏 Đoạn thẳng (Thước ảo)", "segment", "Đoạn thẳng");
            AddItem("┄ Đoạn nét đứt", "dashed", "Nét đứt");
            AddItem("→ Vectơ", "vector", "Vectơ");
            AddItem("△ Tam giác (3 đỉnh)", "triangle", "Tam giác");
            AddItem("▭ Hình chữ nhật (Êke)", "rect", "Hình chữ nhật");
            AddItem("⭕ Đường tròn (Compa ảo)", "circle", "Đường tròn");
            menu.Items.Add(new Separator());
            AddItem("✛ Trung điểm cạnh", "midpoint", "Trung điểm");
            AddItem("⊥ Hạ đường cao (Êke chuẩn 360°)", "altitude", "Đường cao ⊥");
            AddItem("∥ Đường song song qua 1 điểm", "parallel_2d", "Song song //");
            AddItem("✕ Giao điểm 2 đoạn thẳng", "intersect_2d", "Giao điểm");
            AddItem("∟ Ký hiệu góc vuông", "right_angle", "Góc vuông");
            menu.Items.Add(new Separator());
            AddItem("🎨 Tô miền đa giác", "fill_area", "Tô miền");
            AddItem("∠ Tô màu góc (Thước đo độ)", "fill_angle", "Tô góc");
            AddItem("🪄 Đũa thần (Tô 1-Click hình kín)", "magic_wand", "Đũa thần");
            var miFillOff = new MenuItem { Header = "📄 Tô màu hình Office đang chọn" }; miFillOff.Click += (_, _) => _overlay.Surface.FillSelectedOfficeShape(); menu.Items.Add(miFillOff);
            menu.Items.Add(new Separator());
            AddItem("✏️ Bút viết tự do", "pen", "Bút viết");
            AddItem("🖍️ Bút dạ quang", "highlighter", "Dạ quang");
            AddItem("🔤 Chèn chữ / Ký hiệu toán", "text", "Chữ");
            menu.Items.Add(new Separator());
            AddItem("✂️ Chọn vùng HCN (Chụp/Kéo khối)", "snip_rect", "Chọn HCN");
            AddItem("〰️ Khoanh vùng tự do (Lasso)", "snip_lasso", "Chọn tự do");
            var miSnipCap = new MenuItem { Header = "📷 Chụp vùng vừa chọn vào Clipboard" }; miSnipCap.Click += (_, _) => _overlay.Surface.CaptureSnipToClipboard(); menu.Items.Add(miSnipCap);
            var miSnipFill = new MenuItem { Header = "🪣 Tô màu vùng vừa khoanh" }; miSnipFill.Click += (_, _) => _overlay.Surface.FillSnipRegion(); menu.Items.Add(miSnipFill);
            menu.PlacementTarget = BtnMenu2D; menu.IsOpen = true;
        }

        private void BtnMenu3D_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            void AddSolid(string header, string presetKey) { var mi = new MenuItem { Header = header }; mi.Click += (_, _) => { if (BtnPassthrough.IsChecked == true) BtnPassthrough.IsChecked = false; _overlay.Surface.SpawnInteractiveSolid3D(presetKey); SlSplit3D.Value = 0; }; menu.Items.Add(mi); }
            void Add3DTool(string header, string tag, string shortName) { var mi = new MenuItem { Header = header }; mi.Click += (_, _) => ActivateTool(tag, shortName); menu.Items.Add(mi); }

            var miKeep = new MenuItem
            {
                Header = _overlay.Surface.KeepPreviousSolids3D ? "📌 Giữ khối cũ khi chọn khối mới: BẬT (Click để tắt)" : "📌 Giữ khối cũ khi chọn khối mới: TẮT (Click để bật)",
                IsCheckable = true,
                IsChecked = _overlay.Surface.KeepPreviousSolids3D
            };
            miKeep.Click += (_, _) => { _overlay.Surface.KeepPreviousSolids3D = !_overlay.Surface.KeepPreviousSolids3D; _overlay.Surface.ShowToast(_overlay.Surface.KeepPreviousSolids3D ? "Đã bật GIỮ khối 3D cũ khi tạo khối mới" : "Đã bật THAY thế khối 3D cũ khi tạo khối mới"); };
            menu.Items.Add(miKeep);
            var miClearAll3D = new MenuItem { Header = "🧹 Xóa sạch mọi khối 3D trên màn hình" };
            miClearAll3D.Click += (_, _) => _overlay.Surface.ClearAllSolids3D();
            menu.Items.Add(miClearAll3D);
            menu.Items.Add(new Separator());
            AddSolid("🍦 Hình nón 3D (Nét đứt chuẩn + Khối đặc)", "cone");
            AddSolid("🥫 Hình trụ 3D (Nét đứt chuẩn + Khối đặc)", "cylinder");
            AddSolid("🔮 Hình cầu 3D (Nét đứt chuẩn + Khối đặc)", "sphere");
            menu.Items.Add(new Separator());
            AddSolid("🔺 Chóp S.ABCD (đáy hình bình hành)", "pyramid_para");
            AddSolid("📐 Chóp S.ABCD (SA ⊥ đáy)", "pyramid_perp");
            AddSolid("⟁ Tứ diện S.ABC", "tetra");
            AddSolid("🧊 Hình lập phương ABCD.A'B'C'D'", "cube");
            AddSolid("📦 Hình hộp chữ nhật", "cuboid");
            AddSolid("⬡ Lăng trụ tam giác đứng ABC.A'B'C'", "prism_tri");
            menu.Items.Add(new Separator());
            Add3DTool("✥ Di chuyển khối 3D (Kéo trái)", "select", "Di chuyển 3D");
            Add3DTool("🔄 Xoay góc nhìn 3D (Kéo chuột)", "orbit_3d", "Xoay 3D");
            var miSpinV = new MenuItem { Header = _overlay.Surface.IsSpinningVertical3D ? "⏸ Dừng quay quanh trục thẳng đứng" : "⟳ Quay quanh trục thẳng đứng (360°)" };
            miSpinV.Click += (_, _) => { if (BtnPassthrough.IsChecked == true) BtnPassthrough.IsChecked = false; _overlay.Surface.ToggleVerticalSpin3D(); };
            menu.Items.Add(miSpinV);
            var miSpinH = new MenuItem { Header = _overlay.Surface.IsSpinningHorizontal3D ? "⏸ Dừng quay quanh trục nằm ngang" : "⇵ Quay quanh trục nằm ngang (360°)" };
            miSpinH.Click += (_, _) => { if (BtnPassthrough.IsChecked == true) BtnPassthrough.IsChecked = false; _overlay.Surface.ToggleHorizontalSpin3D(); };
            menu.Items.Add(miSpinH);
            Add3DTool("• Chấm điểm trên cạnh 3D (M ∈ SC)", "point_edge_3d", "Điểm cạnh 3D");
            Add3DTool("╱ Nối 2 đỉnh thành đoạn 3D", "edge_3d", "Nối cạnh 3D");
            Add3DTool("∥ Dựng song song 3D (MN // AB)", "parallel_3d", "Song song 3D");
            Add3DTool("🔶 Tô Thiết diện 3D (chọn các đỉnh)", "section_3d", "Thiết diện 3D");
            menu.Items.Add(new Separator());
            Add3DTool("▱ Mp qua 3 điểm bất kỳ (kể cả ngoài khối)", "plane_3pts_3d", "Mp 3đ bất kỳ");
            AddSolid("▱ Mặt phẳng (P) độc lập (3 điểm tự do)", "plane_only");
            Add3DTool("▱ Mp qua 1 điểm // mặt (tự dựng thiết diện)", "plane_para_3d", "Mp 1đ // mặt");
            var miCutPlane = new MenuItem { Header = "✂️ Mp cắt tương tác (Nón / Trụ / Cầu / Đa diện)" };
            miCutPlane.Click += (_, _) => { if (BtnPassthrough.IsChecked == true) BtnPassthrough.IsChecked = false; _overlay.Surface.SpawnInteractiveCutPlane3D(); };
            menu.Items.Add(miCutPlane);
            var miIntersectSolids = new MenuItem { Header = "⋈ Giao của 2 khối 3D (tính & tô điểm giao)" };
            miIntersectSolids.Click += (_, _) => { if (BtnPassthrough.IsChecked == true) BtnPassthrough.IsChecked = false; _overlay.Surface.IntersectActiveSolids3D(); };
            menu.Items.Add(miIntersectSolids);
            var miNetUnfold = new MenuItem { Header = (_overlay.Surface.Solid3D?.IsNetUnfolded == true) ? "📦 Gấp lại khối 3D (Đóng Net)" : "📦 Net — Khai triển trải phẳng khối 3D (GeoGebra)" };
            miNetUnfold.Click += (_, _) => { if (BtnPassthrough.IsChecked == true) BtnPassthrough.IsChecked = false; _overlay.Surface.ToggleNetUnfold3D(); };
            menu.Items.Add(miNetUnfold);
            var miToggleOxyz = new MenuItem { Header = _overlay.Surface.ShowCoordSystem3D ? "🌐 Ẩn Hệ tọa độ không gian Oxyz" : "🌐 Hiện Hệ tọa độ không gian Oxyz" };
            miToggleOxyz.Click += (_, _) => _overlay.Surface.ToggleCoordSystem3D();
            menu.Items.Add(miToggleOxyz);
            menu.Items.Add(new Separator());
            var miShowRev = new MenuItem { Header = "📊 Bật thanh Tích phân S & Quay tròn xoay V (Word)" };
            miShowRev.Click += (_, _) => { Row3Panel.Visibility = Visibility.Visible; _overlay.Surface.SyncFromOffice(); };
            menu.Items.Add(miShowRev);
            menu.PlacementTarget = BtnMenu3D; menu.IsOpen = true;
        }

        private void BtnMenuBg_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            void AddBg(string label, string btnShort, CanvasBackgroundMode mode) { var mi = new MenuItem { Header = label }; mi.Click += (_, _) => { _overlay.Surface.SetCanvasBackground(mode); BtnMenuBg.Content = $"🎨 Nền: {btnShort} ▾"; }; menu.Items.Add(mi); }
            AddBg("🔍 Trong suốt — vẽ đè mọi App (Mặc định)", "Trong suốt", CanvasBackgroundMode.Transparent);
            AddBg("🟩 Bảng xanh kẻ ô (giống clip)", "Bảng xanh", CanvasBackgroundMode.ChalkboardGrid);
            AddBg("⬜ Bảng trắng trơn", "Trắng", CanvasBackgroundMode.WhiteboardPlain);
            AddBg("📑 Giấy ô ly / kẻ ô vuông", "Ô ly", CanvasBackgroundMode.WhiteboardGrid);
            AddBg("⬛ Bảng đen phấn trắng", "Bảng đen", CanvasBackgroundMode.Blackboard);
            menu.PlacementTarget = BtnMenuBg; menu.IsOpen = true;
        }

        private void BtnPassthrough_Checked(object sender, RoutedEventArgs e) { BtnPassthrough.Background = new SolidColorBrush(Color.FromRgb(234, 88, 12)); FabStatusDot.Fill = new SolidColorBrush(Color.FromRgb(249, 115, 22)); _overlay.SetPassthrough(true); Topmost = true; }
        private void BtnPassthrough_Unchecked(object sender, RoutedEventArgs e) { BtnPassthrough.Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)); FabStatusDot.Fill = new SolidColorBrush(Color.FromRgb(34, 197, 94)); _overlay.SetPassthrough(false); Topmost = true; }
        private void StrokeColor_Click(object sender, RoutedEventArgs e) { if (sender is Button btn && btn.Tag is string hex) { _overlay.Surface.StrokeColorHex = hex; _overlay.Surface.InvalidateVisual(); _overlay.Surface.ShowToast($"Màu nét: {hex}"); } }

        private void FillColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                _overlay.Surface.FillColorHex = hex; _overlay.Surface.Area2DColorHex = hex;
                if (_overlay.Surface.Solid3D != null) { _overlay.Surface.Solid3D.SolidColorHex = hex; _overlay.Surface.Solid3D.IsSolidShaded = true; }
                if (_overlay.Surface.RevScene != null && _overlay.Surface.RevScene.IsValid && _overlay.Surface.RevMode == MathRenderMode.Area2D) _overlay.Surface.RevScene.SetActiveRegionColor(hex);
                if (BtnColorArea != null) BtnColorArea.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                _overlay.Surface.InvalidateVisual(); _overlay.Surface.ShowToast($"Màu tô / Khối đặc 3D: {hex}");
            }
        }

        private void CbPenType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_overlay != null && CbPenType.SelectedItem is ComboBoxItem item && item.Tag is string ps)
            {
                _overlay.Surface.PenTipStyle = ps;
                if (ps == "eraser") ActivateTool("eraser", "Tẩy nét");
                else if (ps == "highlighter") ActivateTool("highlighter", "Dạ quang");
                else if (_overlay.Surface.Tool is "pen" or "highlighter" or "eraser") ActivateTool("pen", "Bút viết mịn");
                _overlay.Surface.InvalidateVisual();
            }
        }
        private void CbWidth_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_overlay != null && CbWidth.SelectedItem is ComboBoxItem item && item.Tag is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double w)) _overlay.Surface.StrokeWidth = w; }
        private void CbPattern_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_overlay != null && CbPattern.SelectedItem is ComboBoxItem item && item.Tag is string pat) { _overlay.Surface.FillPattern = pat; _overlay.Surface.InvalidateVisual(); } }
        private void SlOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_overlay == null) return; double v = e.NewValue / 100.0; _overlay.Surface.FillOpacity = v; if (_overlay.Surface.Solid3D != null) _overlay.Surface.Solid3D.SolidOpacity = v; _overlay.Surface.InvalidateVisual(); }
        private void BtnToggleShaded3D_Click(object sender, RoutedEventArgs e) => _overlay.Surface.ToggleSolid3DShaded();
        private void BtnToggleNet3D_Click(object sender, RoutedEventArgs e) => _overlay.Surface.ToggleNetUnfold3D();
        private void SlNet3D_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_overlay != null) _overlay.Surface.SetNetUnfoldProgress3D(e.NewValue); }
        private void BtnToggleOxyz3D_Click(object sender, RoutedEventArgs e) => _overlay.Surface.ToggleCoordSystem3D();

        private void BtnAutoSpin3D_Click(object sender, RoutedEventArgs e)
        {
            _overlay.Surface.ToggleVerticalSpin3D();
        }

        private void BtnAutoSpinH3D_Click(object sender, RoutedEventArgs e)
        {
            _overlay.Surface.ToggleHorizontalSpin3D();
        }

        private void BtnRevVertSpin_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay.Surface.RevScene == null) _overlay.Surface.SyncFromOffice();
            if (_overlay.Surface.RevScene == null) return;
            _overlay.Surface.RevMode = MathRenderMode.Solid3D;
            BtnRev3D.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
            BtnRevArea.Background = new SolidColorBrush(Color.FromArgb(220, 30, 41, 59));
            _overlay.Surface.ToggleVerticalSpin3D();
        }

        private void BtnRevHorizSpin_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay.Surface.RevScene == null) _overlay.Surface.SyncFromOffice();
            if (_overlay.Surface.RevScene == null) return;
            _overlay.Surface.RevMode = MathRenderMode.Solid3D;
            BtnRev3D.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
            BtnRevArea.Background = new SolidColorBrush(Color.FromArgb(220, 30, 41, 59));
            _overlay.Surface.ToggleHorizontalSpin3D();
        }

        private void BtnRevResetSpin_Click(object sender, RoutedEventArgs e) => _overlay.Surface.ResetRevTurntable();

        private void SlSplit3D_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_overlay == null || _overlay.Surface.Solid3D == null) return; _overlay.Surface.Solid3D.SplitAmount = e.NewValue / 100.0; if (TxtSplit3D != null) TxtSplit3D.Text = $"Tách:{e.NewValue:0}%"; _overlay.Surface.InvalidateVisual(); }
        private void BtnClearSolid3D_Click(object sender, RoutedEventArgs e) { _overlay.Surface.DeleteSolid3D(); SlSplit3D.Value = 0; }
        private void BtnUndo_Click(object sender, RoutedEventArgs e) => _overlay.Surface.Undo();
        private void BtnRedo_Click(object sender, RoutedEventArgs e) => _overlay.Surface.Redo();
        private void BtnClear_Click(object sender, RoutedEventArgs e) { BtnClearSolid3D_Click(sender, e); _overlay.Surface.ClearAll(); Row3Panel.Visibility = Visibility.Collapsed; }
        private void BtnSyncOffice_Click(object sender, RoutedEventArgs e) { _overlay.Surface.SyncFromOffice(); if (_overlay.Surface.RevScene != null) Row3Panel.Visibility = Visibility.Visible; }
        private void ChkWhiteBg_Changed(object sender, RoutedEventArgs e) { if (_overlay != null) _overlay.Surface.ExportWhiteBg = (sender as CheckBox)?.IsChecked == true; }
        private void BtnInsertApp_Click(object sender, RoutedEventArgs e) => _overlay.Surface.InsertIntoTargetApp();
        private void BtnCopyPng_Click(object sender, RoutedEventArgs e) => _overlay.Surface.CopyToClipboard();
        private void BtnSavePng_Click(object sender, RoutedEventArgs e) => _overlay.Surface.SavePng();

        private void BtnHistoryEdit_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu(); var recent = SceneHistoryStore.GetRecent(10);
            if (recent.Count == 0) menu.Items.Add(new MenuItem { Header = "(Chưa có hình nào được chèn gần đây)", IsEnabled = false });
            else foreach (var entry in recent) { var item = new MenuItem { Header = $"{entry.Label} ({entry.TargetApp})" }; var captured = entry; item.Click += (_, _) => { if (BtnPassthrough.IsChecked == true) BtnPassthrough.IsChecked = false; _overlay.Surface.RestoreFromHistoryEntry(captured); }; menu.Items.Add(item); }
            menu.PlacementTarget = BtnHistoryEdit; menu.IsOpen = true;
        }

        private void BtnRevArea_Click(object sender, RoutedEventArgs e) { if (_overlay.Surface.RevScene == null) _overlay.Surface.SyncFromOffice(); _overlay.Surface.RevMode = MathRenderMode.Area2D; BtnRevArea.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)); BtnRev3D.Background = new SolidColorBrush(Color.FromArgb(220, 30, 41, 59)); _overlay.Surface.NotifyMetrics(); _overlay.Surface.InvalidateVisual(); }
        private void BtnRev3D_Click(object sender, RoutedEventArgs e) { if (_overlay.Surface.RevScene == null) _overlay.Surface.SyncFromOffice(); _overlay.Surface.RevMode = MathRenderMode.Solid3D; BtnRev3D.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)); BtnRevArea.Background = new SolidColorBrush(Color.FromArgb(220, 30, 41, 59)); _overlay.Surface.NotifyMetrics(); _overlay.Surface.InvalidateVisual(); }
        private void BtnClickAddBound_Click(object sender, RoutedEventArgs e) { if (_overlay.Surface.RevScene == null) _overlay.Surface.SyncFromOffice(); ActivateTool("add_bound", "+ Cận/Miền"); _overlay.Surface.ShowToast("Click lên vùng trống để tạo miền rời mới, hoặc click vào miền cũ để tách đôi!", 4200); }
        private void BtnRemoveRegion_Click(object sender, RoutedEventArgs e) => _overlay.Surface.RemoveRevolutionRegion();
        private void BtnColorArea_Click(object sender, RoutedEventArgs e) { string hex = _overlay.Surface.CycleArea2DColor(); BtnColorArea.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        private void BtnColorVol_Click(object sender, RoutedEventArgs e) { string hex = _overlay.Surface.CycleVolume3DColor(); BtnColorVol.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }

        private void BtnRevAnim_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay.Surface.RevScene == null) _overlay.Surface.SyncFromOffice(); if (_overlay.Surface.RevScene == null) return;
            if (_isAnimatingRev) { _isAnimatingRev = false; _revAnimTimer.Stop(); BtnRevAnim.Content = "▶ 360°"; }
            else { _overlay.Surface.RevMode = MathRenderMode.Solid3D; BtnRev3D.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)); BtnRevArea.Background = new SolidColorBrush(Color.FromArgb(220, 30, 41, 59)); SlRevAngle.Value = 0; _isAnimatingRev = true; BtnRevAnim.Content = "⏸ Dừng"; _revAnimTimer.Start(); }
        }
        private void OnRevAnimTick(object? sender, EventArgs e) { double next = SlRevAngle.Value + 5.0; if (next >= 360.0) { next = 360.0; _isAnimatingRev = false; _revAnimTimer.Stop(); BtnRevAnim.Content = "▶ 360°"; } SlRevAngle.Value = next; }
        private void SlRevAngle_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_overlay != null) { _overlay.Surface.RevSweepDeg = e.NewValue; if (TxtRevAngle != null) TxtRevAngle.Text = $"{e.NewValue:0}°"; _overlay.Surface.InvalidateVisual(); } }
        private void ChkRevCaps_Changed(object sender, RoutedEventArgs e) { if (_overlay != null) { _overlay.Surface.RevShowCaps = (sender as CheckBox)?.IsChecked == true; _overlay.Surface.InvalidateVisual(); } }
        private void ChkRevHandles_Changed(object sender, RoutedEventArgs e) { if (_overlay != null) { _overlay.Surface.SetRevBoundsHandlesVisible((sender as CheckBox)?.IsChecked == true); } }
        private void BtnRevSwap_Click(object sender, RoutedEventArgs e) { if (_overlay.Surface.RevScene != null && _overlay.Surface.RevScene.SwapAxisAndCurve()) { _overlay.Surface.NotifyMetrics(); _overlay.Surface.InvalidateVisual(); _overlay.Surface.ShowToast("Đã đổi trục quay & đường cong sinh!"); } }
        private void BtnRevClear_Click(object sender, RoutedEventArgs e) { _overlay.Surface.RevScene = null; Row3Panel.Visibility = Visibility.Collapsed; _overlay.Surface.NotifyMetrics(); _overlay.Surface.InvalidateVisual(); }

        private void BtnClose_Click(object sender, RoutedEventArgs e) { _appPollTimer.Stop(); _revAnimTimer.Stop(); _overlay.Surface.StopAllTimers(); _dblClickDelayTimer.Stop(); _dblClickHook.Dispose(); if (_textMathWin != null) { _textMathWin.AllowRealClose = true; _textMathWin.Close(); } Application.Current.Shutdown(); }
    }
}