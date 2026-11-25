using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ScienceWordOverlay
{
    public partial class ToolbarWindow : Window
    {
        private readonly OverlayWindow _overlay;
        private bool _collapsed = false;

        public ToolbarWindow(OverlayWindow overlay)
        {
            InitializeComponent();
            _overlay = overlay;
            PreviewKeyDown += (_, e) => _overlay.Surface.HandleKeyDown(e);
        }

        private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Tool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                if (BtnPassthrough.IsChecked == true)
                    BtnPassthrough.IsChecked = false;
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
                _overlay.Surface.ShowToast($"Đã chọn màu tô / dạ quang: {hex}");
            }
        }

        private void CbWidth_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_overlay == null) return;
            if (CbWidth.SelectedItem is ComboBoxItem item && item.Tag is string s &&
                double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double w))
            {
                _overlay.Surface.StrokeWidth = w;
            }
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

        private void ChkWhiteBg_Changed(object sender, RoutedEventArgs e)
        {
            if (_overlay == null) return;
            _overlay.Surface.ExportWhiteBg = (sender as CheckBox)?.IsChecked == true;
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e) => _overlay.Surface.Undo();
        private void BtnRedo_Click(object sender, RoutedEventArgs e) => _overlay.Surface.Redo();
        private void BtnClear_Click(object sender, RoutedEventArgs e) => _overlay.Surface.ClearAll();
        private void BtnCopyWord_Click(object sender, RoutedEventArgs e) => _overlay.Surface.CopyToClipboard();
        private void BtnSavePng_Click(object sender, RoutedEventArgs e) => _overlay.Surface.SavePng();

        private void BtnCollapse_Click(object sender, RoutedEventArgs e)
        {
            _collapsed = !_collapsed;
            Row2Panel.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
            BtnCollapse.Content = _collapsed ? "▼" : "▲";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
