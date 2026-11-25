using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ScienceWordOverlay.Utils;

namespace ScienceWordOverlay
{
    public partial class ToolbarWindow : Window
    {
        private readonly OverlayWindow _overlay;
        private bool _collapsed = false;
        private readonly DispatcherTimer _appPollTimer;

        public ToolbarWindow(OverlayWindow overlay)
        {
            InitializeComponent();
            _overlay = overlay;
            PreviewKeyDown += (_, e) => _overlay.Surface.HandleKeyDown(e);

            // Tự động theo dõi ứng dụng đang mở phía sau (Word, PowerPoint, Excel, Paint, Web, PDF...)
            _appPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _appPollTimer.Tick += OnPollForegroundApp;
            _appPollTimer.Start();
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
                _overlay.Surface.ShowToast($"Đã chọn màu tô / Đũa thần: {hex}");
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
                _overlay.Surface.FillPattern = pat;
        }

        private void SlOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_overlay == null) return;
            _overlay.Surface.FillOpacity = e.NewValue / 100.0;
        }

        private void PresetPyramid_Click(object sender, RoutedEventArgs e) => _overlay.Surface.InsertPreset3D("pyramid");
        private void PresetCube_Click(object sender, RoutedEventArgs e) => _overlay.Surface.InsertPreset3D("cube");

        private void ChkTools_Changed(object sender, RoutedEventArgs e)
        {
            if (_overlay == null) return;
            _overlay.Surface.ShowVirtualTools = (sender as CheckBox)?.IsChecked == true;
            _overlay.Surface.InvalidateVisual();
        }

        private void ChkLabels_Changed(object sender, RoutedEventArgs e)
        {
            if (_overlay == null) return;
            _overlay.Surface.ShowLabels = (sender as CheckBox)?.IsChecked == true;
            _overlay.Surface.InvalidateVisual();
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e) => _overlay.Surface.Undo();
        private void BtnRedo_Click(object sender, RoutedEventArgs e) => _overlay.Surface.Redo();
        private void BtnClear_Click(object sender, RoutedEventArgs e) => _overlay.Surface.ClearAll();
        private void BtnSyncOffice_Click(object sender, RoutedEventArgs e) => _overlay.Surface.SyncFromOffice();
        private void BtnFillOffice_Click(object sender, RoutedEventArgs e) => _overlay.Surface.FillSelectedOfficeShape();
        private void BtnInsertApp_Click(object sender, RoutedEventArgs e) => _overlay.Surface.InsertIntoTargetApp();
        private void BtnCopyPng_Click(object sender, RoutedEventArgs e) => _overlay.Surface.CopyToClipboard();
        private void BtnSavePng_Click(object sender, RoutedEventArgs e) => _overlay.Surface.SavePng();

        private void BtnCollapse_Click(object sender, RoutedEventArgs e)
        {
            _collapsed = !_collapsed;
            Row2Panel.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
            BtnCollapse.Content = _collapsed ? "▼" : "▲";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _appPollTimer.Stop();
            Application.Current.Shutdown();
        }
    }
}
