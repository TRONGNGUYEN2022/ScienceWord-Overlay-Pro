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
        private bool _collapsed = false;
        private readonly DispatcherTimer _appPollTimer;
        private readonly DispatcherTimer _revAnimTimer;
        private DispatcherTimer? _spin3DTimer;
        private bool _isAnimatingRev = false;
        private bool _isSpinning3D = false;
        private readonly OfficeDoubleClickHook _dblClickHook = new();
        private readonly DispatcherTimer _dblClickDelayTimer;
        private string _pendingDblClickProc = "";
        private IntPtr _pendingDblClickHwnd = IntPtr.Zero;
        private int _pendingDblClickX = 0;
        private int _pendingDblClickY = 0;

        public ToolbarWindow(OverlayWindow overlay)
        {
            InitializeComponent();
            _overlay = overlay;
            PreviewKeyDown += (_, e) => _overlay.Surface.HandleKeyDown(e);

            _overlay.Surface.MetricsUpdated += txt =>
            {
                TxtMetrics.Text = txt;
                if (_overlay.Surface.RevScene != null && _overlay.Surface.RevScene.IsValid && !_collapsed)
                    Row3Panel.Visibility = Visibility.Visible;
            };

            _appPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _appPollTimer.Tick += OnPollForegroundApp;
            _appPollTimer.Start();

            _revAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            _revAnimTimer.Tick += OnRevAnimTick;

            Loaded += (_, _) => ClampToScreenBounds();
            SizeChanged += (_, _) => ClampToScreenBounds();

            _dblClickDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _dblClickDelayTimer.Tick += (_, _) =>
            {
                _dblClickDelayTimer.Stop();
                bool ok = false;
                string up = _pendingDblClickProc.ToUpperInvariant();
                if (up is "WINWORD" or "POWERPNT" or "EXCEL")
                    ok = _overlay.Surface.TryRestoreFromOfficeDoubleClick(_pendingDblClickProc);
                else if (up.Contains("SCIENCEWORD") || up == "SW")
                    ok = _overlay.Surface.TryRestoreFromScienceWordDoubleClick(_pendingDblClickHwnd, _pendingDblClickX, _pendingDblClickY);
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

        private void ClampToScreenBounds()
        {
            var wa = SystemParameters.WorkArea;
            if (ActualWidth <= 0) return;
            if (Left + ActualWidth > wa.Right) Left = Math.Max(wa.Left + 2, wa.Right - ActualWidth - 4);
            if (Left < wa.Left) Left = wa.Left + 2;
            if (Top < wa.Top) Top = wa.Top + 2;
        }

        private void OnPollForegroundApp(object? sender, EventArgs e)
        {
            var (hwnd, procName, _) = Win32Helper.GetForegroundAppInfo();
            if (hwnd == IntPtr.Zero) return;
            var myHwnd = new WindowInteropHelper(this).Handle;
            var ovHwnd = new WindowInteropHelper(_overlay).Handle;
            if (hwnd == myHwnd || hwnd == ovHwnd) return;
            if (string.Equals(procName, "ScienceWord_Overlay_Pro", StringComparison.OrdinalIgnoreCase)) return;
            _overlay.Surface.LastTargetHwnd = hwnd;
            _overlay.Surface.LastTargetProcName = procName;
            TxtTargetApp.Text = "Đích: " + Win32Helper.FriendlyAppName(procName);
        }

        private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }

        private void ActivateTool(string tag, string displayName)
        {
            if (BtnPassthrough.IsChecked == true) BtnPassthrough.IsChecked = false;
            _overlay.Surface.SetTool(tag);
            TxtActiveTool.Text = displayName;
        }

        private void QuickSelect_Click(object sender, RoutedEventArgs e) => ActivateTool("select", "Chọn / Kéo");

        private void BtnMenu2D_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            void AddItem(string header, string tag, string shortName)
            {
                var mi = new MenuItem { Header = header };
                mi.Click += (_, _) => ActivateTool(tag, shortName);
                menu.Items.Add(mi);
            }
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
            var miFillOff = new MenuItem { Header = "📄 Tô màu hình Office đang chọn" };
            miFillOff.Click += (_, _) => _overlay.Surface.FillSelectedOfficeShape();
            menu.Items.Add(miFillOff);
            menu.Items.Add(new Separator());
            AddItem("✏️ Bút viết tự do", "pen", "Bút viết");
            AddItem("🖍️ Bút dạ quang", "highlighter", "Dạ quang");
            AddItem("🔤 Chèn chữ / Ký hiệu toán", "text", "Chữ");
            menu.Items.Add(new Separator());
            AddItem("✂️ Chọn vùng HCN (Chụp/Kéo khối)", "snip_rect", "Chọn HCN");
            AddItem("〰️ Khoanh vùng tự do (Lasso)", "snip_lasso", "Chọn tự do");
            var miSnipCap = new MenuItem { Header = "📷 Chụp vùng vừa chọn vào Clipboard" };
            miSnipCap.Click += (_, _) => _overlay.Surface.CaptureSnipToClipboard();
            menu.Items.Add(miSnipCap);
            var miSnipFill = new MenuItem { Header = "🪣 Tô màu vùng vừa khoanh" };
            miSnipFill.Click += (_, _) => _overlay.Surface.FillSnipRegion();
            menu.Items.Add(miSnipFill);

            menu.PlacementTarget = BtnMenu2D;
            menu.IsOpen = true;
        }

        private void BtnMenu3D_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            void AddSolid(string header, string presetKey)
            {
                var mi = new MenuItem { Header = header };
                mi.Click += (_, _) =>
                {
                    if (BtnPassthrough.IsChecked == true) BtnPassthrough.IsChecked = false;
                    _overlay.Surface.SpawnInteractiveSolid3D(presetKey);
                    SlSplit3D.Value = 0;
                    TxtActiveTool.Text = "3D: Xoay khối";
                };
                menu.Items.Add(mi);
            }
            void Add3DTool(string header, string tag, string shortName)
            {
                var mi = new MenuItem { Header = header };
                mi.Click += (_, _) => ActivateTool(tag, shortName);
                menu.Items.Add(mi);
            }

            AddSolid("🔺 Chóp S.ABCD (đáy hình bình hành)", "pyramid_para");
            AddSolid("📐 Chóp S.ABCD (SA ⊥ đáy)", "pyramid_perp");
            AddSolid("⟁ Tứ diện S.ABC", "tetra");
            AddSolid("🧊 Hình lập phương ABCD.A'B'C'D'", "cube");
            AddSolid("📦 Hình hộp chữ nhật", "cuboid");
            AddSolid("⬡ Lăng trụ tam giác đứng ABC.A'B'C'", "prism_tri");
            AddSolid("🍦 Hình nón 3D", "cone");
            AddSolid("🥫 Hình trụ 3D", "cylinder");
            menu.Items.Add(new Separator());
            Add3DTool("🔄 Xoay góc nhìn 3D (Kéo chuột)", "orbit_3d", "Xoay 3D");
            Add3DTool("• Chấm điểm trên cạnh 3D (M ∈ SC)", "point_edge_3d", "Điểm trên cạnh 3D");
            Add3DTool("╱ Nối 2 đỉnh thành đoạn 3D", "edge_3d", "Nối cạnh 3D");
            Add3DTool("∥ Dựng song song 3D (MN // AB)", "parallel_3d", "Song song 3D");
            Add3DTool("🔶 Tô Thiết diện 3D (chọn các đỉnh)", "section_3d", "Thiết diện 3D");
            menu.Items.Add(new Separator());
            var miShowRev = new MenuItem { Header = "📊 Bật thanh Tích phân S & Quay tròn xoay V (Word)" };
            miShowRev.Click += (_, _) => { Row3Panel.Visibility = Visibility.Visible; _overlay.Surface.SyncFromOffice(); };
            menu.Items.Add(miShowRev);
            var miPyr2d = new MenuItem { Header = "✏️ Vẽ mô phỏng Thước+Bút: Chóp S.ABCD (2D)" };
            miPyr2d.Click += (_, _) => _overlay.Surface.InsertPreset3D("pyramid");
            menu.Items.Add(miPyr2d);
            var miCube2d = new MenuItem { Header = "✏️ Vẽ mô phỏng Thước+Bút: Lập phương (2D)" };
            miCube2d.Click += (_, _) => _overlay.Surface.InsertPreset3D("cube");
            menu.Items.Add(miCube2d);

            menu.PlacementTarget = BtnMenu3D;
            menu.IsOpen = true;
        }

        private void BtnMenuBg_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            void AddBg(string label, string btnShort, CanvasBackgroundMode mode)
            {
                var mi = new MenuItem { Header = label };
                mi.Click += (_, _) => { _overlay.Surface.SetCanvasBackground(mode); BtnMenuBg.Content = $"🎨 Nền: {btnShort} ▾"; };
                menu.Items.Add(mi);
            }
            AddBg("🔍 Trong suốt — vẽ đè mọi App (Mặc định)", "Trong suốt", CanvasBackgroundMode.Transparent);
            AddBg("🟩 Bảng xanh kẻ ô (giống clip)", "Bảng xanh", CanvasBackgroundMode.ChalkboardGrid);
            AddBg("⬜ Bảng trắng trơn", "Trắng", CanvasBackgroundMode.WhiteboardPlain);
            AddBg("📑 Giấy ô ly / kẻ ô vuông", "Ô ly", CanvasBackgroundMode.WhiteboardGrid);
            AddBg("⬛ Bảng đen phấn trắng", "Bảng đen", CanvasBackgroundMode.Blackboard);
            menu.PlacementTarget = BtnMenuBg;
            menu.IsOpen = true;
        }

        private void BtnPassthrough_Checked(object sender, RoutedEventArgs e) { BtnPassthrough.Background = new SolidColorBrush(Color.FromRgb(234, 88, 12)); _overlay.SetPassthrough(true); Topmost = true; }
        private void BtnPassthrough_Unchecked(object sender, RoutedEventArgs e) { BtnPassthrough.Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)); _overlay.SetPassthrough(false); Topmost = true; }

        private void StrokeColor_Click(object sender, RoutedEventArgs e) { if (sender is Button btn && btn.Tag is string hex) { _overlay.Surface.StrokeColorHex = hex; _overlay.Surface.InvalidateVisual(); _overlay.Surface.ShowToast($"Màu nét: {hex}"); } }

        private void FillColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                _overlay.Surface.FillColorHex = hex;
                _overlay.Surface.Area2DColorHex = hex;
                if (_overlay.Surface.RevScene != null && _overlay.Surface.RevScene.IsValid && _overlay.Surface.RevMode == MathRenderMode.Area2D)
                    _overlay.Surface.RevScene.SetActiveRegionColor(hex);
                if (BtnColorArea != null) BtnColorArea.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                _overlay.Surface.InvalidateVisual();
                _overlay.Surface.ShowToast($"Màu tô / Thiết diện / Diện tích S: {hex}");
            }
        }

        private void CbPenType_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_overlay != null && CbPenType.SelectedItem is ComboBoxItem item && item.Tag is string ps) { _overlay.Surface.PenTipStyle = ps; _overlay.Surface.InvalidateVisual(); } }
        private void CbWidth_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_overlay != null && CbWidth.SelectedItem is ComboBoxItem item && item.Tag is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double w)) _overlay.Surface.StrokeWidth = w; }
        private void CbPattern_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_overlay != null && CbPattern.SelectedItem is ComboBoxItem item && item.Tag is string pat) { _overlay.Surface.FillPattern = pat; _overlay.Surface.InvalidateVisual(); } }
        private void SlOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_overlay != null) { _overlay.Surface.FillOpacity = e.NewValue / 100.0; _overlay.Surface.InvalidateVisual(); } }

        private void BtnAutoSpin3D_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay.Surface.Solid3D == null) { _overlay.Surface.ShowToast("Hãy chọn 1 khối từ menu [Vẽ 3D ▾] trước!"); return; }
            if (_spin3DTimer == null)
            {
                _spin3DTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
                _spin3DTimer.Tick += (_, _) => { if (_overlay.Surface.Solid3D == null) return; _overlay.Surface.Solid3D.YawDeg = (_overlay.Surface.Solid3D.YawDeg + 1.4) % 360.0; _overlay.Surface.InvalidateVisual(); };
            }
            _isSpinning3D = !_isSpinning3D;
            if (_isSpinning3D) { _spin3DTimer.Start(); BtnAutoSpin3D.Content = "⏸ Dừng"; } else { _spin3DTimer.Stop(); BtnAutoSpin3D.Content = "▶ Quay 3D"; }
        }

        private void SlSplit3D_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_overlay == null || _overlay.Surface.Solid3D == null) return;
            _overlay.Surface.Solid3D.SplitAmount = e.NewValue / 100.0;
            if (TxtSplit3D != null) TxtSplit3D.Text = $"Tách: {e.NewValue:0}%";
            _overlay.Surface.InvalidateVisual();
        }

        private void BtnClearSolid3D_Click(object sender, RoutedEventArgs e)
        {
            _isSpinning3D = false; _spin3DTimer?.Stop(); BtnAutoSpin3D.Content = "▶ Quay 3D";
            _overlay.Surface.Solid3D = null; SlSplit3D.Value = 0; _overlay.Surface.InvalidateVisual();
        }

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
            var menu = new ContextMenu();
            var recent = SceneHistoryStore.GetRecent(10);
            if (recent.Count == 0) menu.Items.Add(new MenuItem { Header = "(Chưa có hình nào được chèn gần đây)", IsEnabled = false });
            else foreach (var entry in recent)
            {
                var item = new MenuItem { Header = $"{entry.Label} ({entry.TargetApp})" };
                var captured = entry;
                item.Click += (_, _) => { if (BtnPassthrough.IsChecked == true) BtnPassthrough.IsChecked = false; _overlay.Surface.RestoreFromHistoryEntry(captured); };
                menu.Items.Add(item);
            }
            menu.PlacementTarget = BtnHistoryEdit; menu.IsOpen = true;
        }

        private void BtnRevArea_Click(object sender, RoutedEventArgs e) { if (_overlay.Surface.RevScene == null) _overlay.Surface.SyncFromOffice(); _overlay.Surface.RevMode = MathRenderMode.Area2D; BtnRevArea.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)); BtnRev3D.Background = new SolidColorBrush(Color.FromArgb(220, 30, 41, 59)); _overlay.Surface.NotifyMetrics(); _overlay.Surface.InvalidateVisual(); }
        private void BtnRev3D_Click(object sender, RoutedEventArgs e) { if (_overlay.Surface.RevScene == null) _overlay.Surface.SyncFromOffice(); _overlay.Surface.RevMode = MathRenderMode.Solid3D; BtnRev3D.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)); BtnRevArea.Background = new SolidColorBrush(Color.FromArgb(220, 30, 41, 59)); _overlay.Surface.NotifyMetrics(); _overlay.Surface.InvalidateVisual(); }
        private void BtnAddBound_Click(object sender, RoutedEventArgs e) => _overlay.Surface.AddRevolutionBound();
        private void BtnRemoveBound_Click(object sender, RoutedEventArgs e) => _overlay.Surface.RemoveRevolutionBound();
        private void BtnColorArea_Click(object sender, RoutedEventArgs e) { string hex = _overlay.Surface.CycleArea2DColor(); BtnColorArea.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        private void BtnColorVol_Click(object sender, RoutedEventArgs e) { string hex = _overlay.Surface.CycleVolume3DColor(); BtnColorVol.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }

        private void BtnRevAnim_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay.Surface.RevScene == null) _overlay.Surface.SyncFromOffice();
            if (_overlay.Surface.RevScene == null) return;
            if (_isAnimatingRev) { _isAnimatingRev = false; _revAnimTimer.Stop(); BtnRevAnim.Content = "▶ 360°"; }
            else { _overlay.Surface.RevMode = MathRenderMode.Solid3D; BtnRev3D.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)); BtnRevArea.Background = new SolidColorBrush(Color.FromArgb(220, 30, 41, 59)); SlRevAngle.Value = 0; _isAnimatingRev = true; BtnRevAnim.Content = "⏸ Dừng"; _revAnimTimer.Start(); }
        }

        private void OnRevAnimTick(object? sender, EventArgs e)
        {
            double next = SlRevAngle.Value + 5.0;
            if (next >= 360.0) { next = 360.0; _isAnimatingRev = false; _revAnimTimer.Stop(); BtnRevAnim.Content = "▶ 360°"; }
            SlRevAngle.Value = next;
        }

        private void SlRevAngle_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_overlay != null) { _overlay.Surface.RevSweepDeg = e.NewValue; if (TxtRevAngle != null) TxtRevAngle.Text = $"{e.NewValue:0}°"; _overlay.Surface.InvalidateVisual(); } }
        private void ChkRevCaps_Changed(object sender, RoutedEventArgs e) { if (_overlay != null) { _overlay.Surface.RevShowCaps = (sender as CheckBox)?.IsChecked == true; _overlay.Surface.InvalidateVisual(); } }
        private void BtnRevSwap_Click(object sender, RoutedEventArgs e) { if (_overlay.Surface.RevScene != null && _overlay.Surface.RevScene.SwapAxisAndCurve()) { _overlay.Surface.NotifyMetrics(); _overlay.Surface.InvalidateVisual(); _overlay.Surface.ShowToast("Đã đổi trục quay & đường cong sinh!"); } }
        private void BtnRevClear_Click(object sender, RoutedEventArgs e) { _overlay.Surface.RevScene = null; Row3Panel.Visibility = Visibility.Collapsed; _overlay.Surface.NotifyMetrics(); _overlay.Surface.InvalidateVisual(); }

        private void BtnCollapse_Click(object sender, RoutedEventArgs e)
        {
            _collapsed = !_collapsed;
            Row2Panel.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
            if (_collapsed) Row3Panel.Visibility = Visibility.Collapsed;
            else if (_overlay.Surface.RevScene != null && _overlay.Surface.RevScene.IsValid) Row3Panel.Visibility = Visibility.Visible;
            BtnCollapse.Content = _collapsed ? "▼" : "▲";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _appPollTimer.Stop(); _revAnimTimer.Stop(); _spin3DTimer?.Stop(); _dblClickDelayTimer.Stop(); _dblClickHook.Dispose();
            Application.Current.Shutdown();
        }
    }
}