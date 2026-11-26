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
        private bool _isAnimatingRev = false;
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

            _overlay.Surface.MetricsUpdated += txt => TxtMetrics.Text = txt;

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

                if (ok && BtnPassthrough.IsChecked == true)
                    BtnPassthrough.IsChecked = false;
            };
            _dblClickHook.TargetAppDoubleClicked += (proc, hwnd, px, py) =>
            {
                if (BtnPassthrough.IsChecked != true) return;
                _pendingDblClickProc = proc;
                _pendingDblClickHwnd = hwnd;
                _pendingDblClickX = px;
                _pendingDblClickY = py;
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

        private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Tool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                if (BtnPassthrough.IsChecked == true) BtnPassthrough.IsChecked = false;
                _overlay.Surface.SetTool(tag);
            }
        }

        private void BtnPassthrough_Checked(object sender, RoutedEventArgs e)
        {
            BtnPassthrough.Background = new SolidColorBrush(Color.FromRgb(234, 88, 12));
            _overlay.SetPassthrough(true);
            Topmost = true;
        }

        private void BtnPassthrough_Unchecked(object sender, RoutedEventArgs e)
        {
            BtnPassthrough.Background = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            _overlay.SetPassthrough(false);
            Topmost = true;
        }

        private void StrokeColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                _overlay.Surface.StrokeColorHex = hex;
                _overlay.Surface.ShowToast($"Đã chọn màu nét: {hex}");
            }
        }

        private void FillColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                _overlay.Surface.FillColorHex = hex;
                _overlay.Surface.InvalidateVisual();
                _overlay.Surface.ShowToast($"Đã chọn màu tô / 3D: {hex}");
            }
        }

        private void CbPenType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_overlay == null) return;
            if (CbPenType.SelectedItem is ComboBoxItem item && item.Tag is string ps)
            {
                _overlay.Surface.PenTipStyle = ps;
                _overlay.Surface.ShowToast($"Đã chọn loại bút: {item.Content}");
                _overlay.Surface.InvalidateVisual();
            }
        }

        private void CbWidth_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_overlay == null) return;
            if (CbWidth.SelectedItem is ComboBoxItem item && item.Tag is string s &&
                double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double w))
                _overlay.Surface.StrokeWidth = w;
        }

        private void CbPattern_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_overlay == null) return;
            if (CbPattern.SelectedItem is ComboBoxItem item && item.Tag is string pat)
            {
                _overlay.Surface.FillPattern = pat;
                _overlay.Surface.InvalidateVisual();
            }
        }

        private void SlOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_overlay == null) return;
            _overlay.Surface.FillOpacity = e.NewValue / 100.0;
            _overlay.Surface.InvalidateVisual();
        }

        private void BtnSnipCapture_Click(object sender, RoutedEventArgs e) => _overlay.Surface.CaptureSnipToClipboard();
        private void BtnSnipFill_Click(object sender, RoutedEventArgs e) => _overlay.Surface.FillSnipRegion();

        private void PresetPyramid_Click(object sender, RoutedEventArgs e) => _overlay.Surface.InsertPreset3D("pyramid");
        private void PresetCube_Click(object sender, RoutedEventArgs e) => _overlay.Surface.InsertPreset3D("cube");
        private void BtnUndo_Click(object sender, RoutedEventArgs e) => _overlay.Surface.Undo();
        private void BtnRedo_Click(object sender, RoutedEventArgs e) => _overlay.Surface.Redo();
        private void BtnClear_Click(object sender, RoutedEventArgs e) => _overlay.Surface.ClearAll();
        private void BtnSyncOffice_Click(object sender, RoutedEventArgs e) => _overlay.Surface.SyncFromOffice();
        private void BtnFillOffice_Click(object sender, RoutedEventArgs e) => _overlay.Surface.FillSelectedOfficeShape();
        private void BtnHistoryEdit_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            var recent = SceneHistoryStore.GetRecent(10);
            if (recent.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "(Chưa có hình nào được chèn gần đây)", IsEnabled = false });
            }
            else
            {
                foreach (var entry in recent)
                {
                    var item = new MenuItem { Header = $"{entry.Label} ({entry.TargetApp})" };
                    var captured = entry;
                    item.Click += (_, _) =>
                    {
                        if (BtnPassthrough.IsChecked == true) BtnPassthrough.IsChecked = false;
                        _overlay.Surface.RestoreFromHistoryEntry(captured);
                    };
                    menu.Items.Add(item);
                }
            }
            menu.PlacementTarget = BtnHistoryEdit;
            menu.IsOpen = true;
        }

        private void ChkWhiteBg_Changed(object sender, RoutedEventArgs e) { if (_overlay != null) _overlay.Surface.ExportWhiteBg = (sender as CheckBox)?.IsChecked == true; }
        private void BtnInsertApp_Click(object sender, RoutedEventArgs e) => _overlay.Surface.InsertIntoTargetApp();
        private void BtnCopyPng_Click(object sender, RoutedEventArgs e) => _overlay.Surface.CopyToClipboard();
        private void BtnSavePng_Click(object sender, RoutedEventArgs e) => _overlay.Surface.SavePng();

        // ================= KHOI TRON XOAY 3D & TINH THE TICH V =================
        private void BtnRevArea_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay.Surface.RevScene == null) _overlay.Surface.SyncFromOffice();
            _overlay.Surface.RevMode = MathRenderMode.Area2D;
            BtnRevArea.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
            BtnRev3D.Background = new SolidColorBrush(Color.FromArgb(220, 30, 41, 59));
            _overlay.Surface.NotifyMetrics();
            _overlay.Surface.InvalidateVisual();
        }

        private void BtnRev3D_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay.Surface.RevScene == null) _overlay.Surface.SyncFromOffice();
            _overlay.Surface.RevMode = MathRenderMode.Solid3D;
            BtnRev3D.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
            BtnRevArea.Background = new SolidColorBrush(Color.FromArgb(220, 30, 41, 59));
            _overlay.Surface.NotifyMetrics();
            _overlay.Surface.InvalidateVisual();
        }

        private void BtnRevAnim_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay.Surface.RevScene == null) _overlay.Surface.SyncFromOffice();
            if (_overlay.Surface.RevScene == null) return;

            if (_isAnimatingRev)
            {
                _isAnimatingRev = false;
                _revAnimTimer.Stop();
                BtnRevAnim.Content = "▶ Xoay 0°-360°";
            }
            else
            {
                _overlay.Surface.RevMode = MathRenderMode.Solid3D;
                BtnRev3D.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                BtnRevArea.Background = new SolidColorBrush(Color.FromArgb(220, 30, 41, 59));
                SlRevAngle.Value = 0;
                _isAnimatingRev = true;
                BtnRevAnim.Content = "⏸ Dừng xoay";
                _revAnimTimer.Start();
            }
        }

        private void OnRevAnimTick(object? sender, EventArgs e)
        {
            double next = SlRevAngle.Value + 5.0;
            if (next >= 360.0)
            {
                next = 360.0;
                _isAnimatingRev = false;
                _revAnimTimer.Stop();
                BtnRevAnim.Content = "▶ Xoay 0°-360°";
            }
            SlRevAngle.Value = next;
        }

        private void SlRevAngle_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_overlay == null) return;
            _overlay.Surface.RevSweepDeg = e.NewValue;
            if (TxtRevAngle != null) TxtRevAngle.Text = $"Góc: {e.NewValue:0}°";
            _overlay.Surface.InvalidateVisual();
        }

        private void ChkRevCaps_Changed(object sender, RoutedEventArgs e)
        {
            if (_overlay == null) return;
            _overlay.Surface.RevShowCaps = (sender as CheckBox)?.IsChecked == true;
            _overlay.Surface.InvalidateVisual();
        }

        private void BtnRevSwap_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay.Surface.RevScene != null && _overlay.Surface.RevScene.SwapAxisAndCurve())
            {
                _overlay.Surface.NotifyMetrics();
                _overlay.Surface.InvalidateVisual();
                _overlay.Surface.ShowToast("Đã đổi trục quay & đường cong sinh!");
            }
        }

        private void BtnRevClear_Click(object sender, RoutedEventArgs e)
        {
            _overlay.Surface.RevScene = null;
            _overlay.Surface.NotifyMetrics();
            _overlay.Surface.InvalidateVisual();
        }

        private void BtnCollapse_Click(object sender, RoutedEventArgs e)
        {
            _collapsed = !_collapsed;
            var vis = _collapsed ? Visibility.Collapsed : Visibility.Visible;
            Row2Panel.Visibility = vis;
            Row3Panel.Visibility = vis;
            BtnCollapse.Content = _collapsed ? "▼" : "▲";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _appPollTimer.Stop();
            _revAnimTimer.Stop();
            _dblClickDelayTimer.Stop();
            _dblClickHook.Dispose();
            Application.Current.Shutdown();
        }
    }
}
