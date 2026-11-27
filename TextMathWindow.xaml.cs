using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ScienceWordOverlay.Models;
using ScienceWordOverlay.Utils;

namespace ScienceWordOverlay
{
    public partial class TextMathWindow : Window
    {
        private readonly OverlaySurface _surface;
        private bool _isReady = false;
        public bool AllowRealClose { get; set; } = false;

        // Editing state: "" (create) | "math" | "text" | "handwriting"
        private string _editingKind = "";
        private string? _editingFormulaId;
        private string? _editingTextId;
        private List<string> _editingStrokeIds = new();
        private string _editingStrokeGroupId = "";
        private double _editOrigMinX, _editOrigMinY, _editMapPadX, _editMapPadY, _editMapScale = 1.0;

        private string _mathColorHex = "#0F172A";
        private double _mathFontSize = 24.0;
        private string _padPenStyle = "fountain";
        private string _padColorHex = "#1D4ED8";
        private double _padWidth = 2.4;

        private readonly List<FreeStroke> _padStrokes = new();
        private FreeStroke? _activePadStroke;
        private Point _lastSmoothedPadPt;
        private long _lastPadTick;

        private static readonly Dictionary<string, (string lbl, string tex, string tip)[]> VkKeys = new()
        {
            ["basic"] = new[]
            {
                ("a/b", "\\frac{}{}", "Phân số"), ("x²", "^{2}", "Bình phương"), ("xⁿ", "^{}", "Số mũ"),
                ("xₙ", "_{}", "Chỉ số dưới"), ("√□", "\\sqrt{}", "Căn bậc hai"), ("ⁿ√□", "\\sqrt[]{}", "Căn bậc n"),
                ("|x|", "\\left|\\right|", "Trị tuyệt đối"), ("( )", "\\left(\\right)", "Ngoặc đơn"),
                ("±", "\\pm ", "Cộng trừ"), ("≠", "\\neq ", "Khác"), ("≤", "\\le ", "Nhỏ hơn bằng"),
                ("≥", "\\ge ", "Lớn hơn bằng"), ("≈", "\\approx ", "Xấp xỉ"), ("∞", "\\infty ", "Vô cực"),
                ("∈", "\\in ", "Thuộc"), ("∉", "\\notin ", "Không thuộc"), ("⊂", "\\subset ", "Tập con"),
                ("∪", "\\cup ", "Hợp"), ("∩", "\\cap ", "Giao"), ("∅", "\\emptyset ", "Tập rỗng"),
                ("∀", "\\forall ", "Với mọi"), ("∃", "\\exists ", "Tồn tại"), ("⇒", "\\Rightarrow ", "Suy ra"), ("⇔", "\\Leftrightarrow ", "Tương đương")
            },
            ["calc"] = new[]
            {
                ("lim", "\\lim_{x \\to +\\infty} ", "Giới hạn"), ("∫ₐᵇ", "\\int_{a}^{b}  \\, dx", "Tích phân xác định"),
                ("∫", "\\int  \\, dx", "Nguyên hàm"), ("∑", "\\sum_{i=1}^{n} ", "Tổng Sigma"),
                ("dy/dx", "\\frac{dy}{dx}", "Đạo hàm"), ("f'(x)", "f'(x)", "Đạo hàm cấp 1"),
                ("sin", "\\sin ", "Sin"), ("cos", "\\cos ", "Cos"), ("tan", "\\tan ", "Tan"), ("cot", "\\cot ", "Cot"),
                ("logₐb", "\\log_{a} b", "Logarit"), ("ln", "\\ln ", "Logarit tự nhiên"), ("eˣ", "e^{x}", "Hàm mũ"), ("π", "\\pi ", "Số Pi")
            },
            ["geom"] = new[]
            {
                ("△ABC", "\\triangle ABC", "Tam giác"), ("∠ABC", "\\widehat{ABC}", "Góc ABC"), ("⊥", "\\perp ", "Vuông góc"),
                ("∥", "\\parallel ", "Song song"), ("≅", "\\cong ", "Bằng nhau"), ("∼", "\\sim ", "Đồng dạng"),
                ("AB⃗", "\\overrightarrow{AB}", "Vectơ AB"), ("u⃗", "\\vec{u}", "Vectơ u"), ("°", "^{\\circ}", "Độ"),
                ("(O;R)", "(O; R)", "Đường tròn"), ("α", "\\alpha ", "Alpha"), ("β", "\\beta ", "Beta"),
                ("γ", "\\gamma ", "Gamma"), ("Δ", "\\Delta ", "Delta"), ("φ", "\\varphi ", "Phi"), ("θ", "\\theta ", "Theta"), ("Ω", "\\Omega ", "Omega")
            },
            ["sys"] = new[]
            {
                ("{ Hệ 2 PT", "\\begin{cases} x + y = 5 \\\\ 2x - y = 1 \\end{cases}", "Hệ phương trình"),
                ("(Ma trận)", "\\begin{pmatrix} a & b \\\\ c & d \\end{pmatrix}", "Ma trận 2x2"),
                ("Cₙᵏ", "C_{n}^{k}", "Tổ hợp"), ("Aₙᵏ", "A_{n}^{k}", "Chỉnh hợp"),
                ("Pₙ", "P_{n} = n!", "Hoán vị"), ("x̄", "\\overline{x}", "Trung bình"), ("σ", "\\sigma ", "Độ lệch chuẩn")
            }
        };

        public TextMathWindow(OverlaySurface surface)
        {
            _surface = surface ?? throw new ArgumentNullException(nameof(surface));
            InitializeComponent();
            _isReady = true;
            BuildVkButtons("basic");
            RefreshPreview();
            Closing += (_, e) =>
            {
                if (!AllowRealClose)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
        }

        private void ResetEditingState()
        {
            _editingKind = "";
            _editingFormulaId = null;
            _editingTextId = null;
            _editingStrokeIds.Clear();
            _editingStrokeGroupId = "";
            _editMapScale = 1.0;
            TxtEditBadge.Text = "Tạo mới";
            BtnInsertOverlay.Content = "＋ Chèn lên bảng vẽ";
            BtnInsertApp.Content = "📥 Chèn vào App";
            BtnInsertPadOverlay.Content = "＋ Chèn lên bảng vẽ";
            BtnInsertPadApp.Content = "📥 Chèn vào App";
            BtnDeleteMathItem.Visibility = Visibility.Collapsed;
            BtnDeletePadItem.Visibility = Visibility.Collapsed;
        }

        private void SelectFontSizeCombo(double fontSize)
        {
            bool prev = _isReady;
            _isReady = false;
            try
            {
                int bestIdx = 1;
                double bestDiff = double.MaxValue;
                for (int i = 0; i < CbMathFontSize.Items.Count; i++)
                {
                    if (CbMathFontSize.Items[i] is ComboBoxItem it && it.Tag is string s &&
                        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                    {
                        double d = Math.Abs(v - fontSize);
                        if (d < bestDiff) { bestDiff = d; bestIdx = i; }
                    }
                }
                CbMathFontSize.SelectedIndex = bestIdx;
            }
            finally { _isReady = prev; }
        }

        public void OpenForCreate(bool penMode = false)
        {
            ResetEditingState();
            if (_surface.CanvasBg is CanvasBackgroundMode.ChalkboardGrid or CanvasBackgroundMode.Blackboard)
            {
                _mathColorHex = "#FFFFFF";
                _padColorHex = "#FFFFFF";
            }
            SetMode(penMode);
            Show();
            Activate();
            RefreshPreview();
        }

        public void OpenForEdit(GeoMathFormula f)
        {
            ResetEditingState();
            _editingKind = "math";
            _editingFormulaId = f.Id;
            TxtEditBadge.Text = "Đang sửa Công thức";
            BtnInsertOverlay.Content = "✓ Cập nhật công thức";
            BtnInsertApp.Content = "📥 Cập nhật & Chèn App";
            BtnDeleteMathItem.Visibility = Visibility.Visible;

            bool prev = _isReady;
            _isReady = false;
            TxtFormulaInput.Text = f.Latex;
            _mathColorHex = f.ColorHex;
            _mathFontSize = f.FontSize;
            ChkForceMath.IsChecked = f.ForceMath;
            _isReady = prev;
            SelectFontSizeCombo(f.FontSize);

            SetMode(false);
            Show();
            Activate();
            TxtFormulaInput.Focus();
            TxtFormulaInput.SelectAll();
            RefreshPreview();
        }

        public void OpenForEditText(GeoText t)
        {
            ResetEditingState();
            _editingKind = "text";
            _editingTextId = t.Id;
            TxtEditBadge.Text = "Đang sửa Văn bản (Text)";
            BtnInsertOverlay.Content = "✓ Cập nhật chữ";
            BtnInsertApp.Content = "📥 Cập nhật & Chèn App";
            BtnDeleteMathItem.Visibility = Visibility.Visible;

            bool prev = _isReady;
            _isReady = false;
            TxtFormulaInput.Text = t.Text;
            _mathColorHex = t.ColorHex;
            _mathFontSize = t.FontSize;
            ChkForceMath.IsChecked = false;
            _isReady = prev;
            SelectFontSizeCombo(t.FontSize);

            SetMode(false);
            Show();
            Activate();
            TxtFormulaInput.Focus();
            TxtFormulaInput.SelectAll();
            RefreshPreview();
        }

        public void OpenForEditHandwriting(List<FreeStroke> cluster)
        {
            ResetEditingState();
            if (cluster == null || cluster.Count == 0) { OpenForCreate(penMode: true); return; }

            _editingKind = "handwriting";
            _editingStrokeIds = cluster.Select(s => s.Id).ToList();
            _editingStrokeGroupId = cluster.FirstOrDefault(s => !string.IsNullOrEmpty(s.GroupId))?.GroupId ?? "";
            TxtEditBadge.Text = $"Đang sửa Chữ viết tay ({cluster.Count} nét)";
            BtnInsertPadOverlay.Content = "✓ Cập nhật chữ viết";
            BtnInsertPadApp.Content = "📥 Cập nhật & Chèn App";
            BtnDeletePadItem.Visibility = Visibility.Visible;

            // Tinh khung bao goc tren man hinh va map gon vao khung viet tay 556x215
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var st in cluster)
                foreach (var pt in st.Points)
                {
                    if (pt[0] < minX) minX = pt[0];
                    if (pt[1] < minY) minY = pt[1];
                    if (pt[0] > maxX) maxX = pt[0];
                    if (pt[1] > maxY) maxY = pt[1];
                }
            if (minX > maxX) { OpenForCreate(penMode: true); return; }

            double origW = Math.Max(12.0, maxX - minX);
            double origH = Math.Max(12.0, maxY - minY);
            double padW = HandwritingPad.ActualWidth > 60 ? HandwritingPad.ActualWidth : 556.0;
            double padH = HandwritingPad.ActualHeight > 40 ? HandwritingPad.ActualHeight : 210.0;
            double margin = 18.0;
            double scale = Math.Min(1.0, Math.Min((padW - 2 * margin) / origW, (padH - 2 * margin) / origH));
            scale = Math.Clamp(scale, 0.25, 1.0);
            double drawW = origW * scale, drawH = origH * scale;
            double offX = (padW - drawW) * 0.5;
            double offY = (padH - drawH) * 0.5;

            _editOrigMinX = minX;
            _editOrigMinY = minY;
            _editMapPadX = offX;
            _editMapPadY = offY;
            _editMapScale = scale;

            _padStrokes.Clear();
            foreach (var st in cluster)
            {
                var mapped = new FreeStroke
                {
                    Id = st.Id,
                    GroupId = st.GroupId,
                    ColorHex = st.ColorHex,
                    Width = Math.Max(1.0, st.Width * scale),
                    Opacity = st.Opacity,
                    IsHighlighter = st.IsHighlighter,
                    PenStyle = st.PenStyle,
                    PointWidths = st.PointWidths.Select(w => Math.Max(0.9, w * scale)).ToList(),
                    Points = st.Points.Select(p => new[] { offX + (p[0] - minX) * scale, offY + (p[1] - minY) * scale }).ToList()
                };
                _padStrokes.Add(mapped);
            }

            // Dong bo mau & loai but theo net dau tien cua cum dang sua
            var first = cluster[0];
            _padColorHex = first.ColorHex;
            _padWidth = Math.Clamp(first.Width, 1.2, 16.0);
            _padPenStyle = string.IsNullOrEmpty(first.PenStyle) ? "fountain" : first.PenStyle;

            SetMode(true);
            Show();
            Activate();
            RedrawPad();
        }

        private List<FreeStroke> MapPadStrokesBackToScreen()
        {
            double inv = _editMapScale > 1e-4 ? (1.0 / _editMapScale) : 1.0;
            var result = new List<FreeStroke>(_padStrokes.Count);
            foreach (var st in _padStrokes)
            {
                result.Add(new FreeStroke
                {
                    Id = st.Id,
                    GroupId = _editingStrokeGroupId,
                    ColorHex = st.ColorHex,
                    Width = Math.Clamp(st.Width * inv, 1.0, 32.0),
                    Opacity = st.Opacity,
                    IsHighlighter = st.IsHighlighter,
                    PenStyle = st.PenStyle,
                    PointWidths = st.PointWidths.Select(w => Math.Clamp(w * inv, 0.9, 36.0)).ToList(),
                    Points = st.Points.Select(p => new[] { _editOrigMinX + (p[0] - _editMapPadX) * inv, _editOrigMinY + (p[1] - _editMapPadY) * inv }).ToList()
                });
            }
            return result;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) try { DragMove(); } catch { }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Hide();

        private void SetMode(bool penMode)
        {
            PanelMath.Visibility = penMode ? Visibility.Collapsed : Visibility.Visible;
            PanelPen.Visibility = penMode ? Visibility.Visible : Visibility.Collapsed;
            BtnTabMath.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(penMode ? "#FFFFFF" : "#D97706"));
            BtnTabMath.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(penMode ? "#334155" : "#FFFFFF"));
            BtnTabPen.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(penMode ? "#D97706" : "#FFFFFF"));
            BtnTabPen.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(penMode ? "#FFFFFF" : "#334155"));
        }

        private void BtnTabMath_Click(object sender, RoutedEventArgs e) => SetMode(false);
        private void BtnTabPen_Click(object sender, RoutedEventArgs e) => SetMode(true);

        private void BuildVkButtons(string tab)
        {
            VkKeysWrap.Children.Clear();
            foreach (var btn in new[] { BtnVkBasic, BtnVkCalc, BtnVkGeom, BtnVkSys })
            {
                bool active = (btn.Tag as string) == tab;
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#F59E0B" : "#FFFFFF"));
                btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#0F172A" : "#334155"));
            }
            if (!VkKeys.TryGetValue(tab, out var list)) return;
            foreach (var (lbl, tex, tip) in list)
            {
                var b = new Button { Content = lbl, ToolTip = tip, Style = (Style)FindResource("VkKeyBtn") };
                string snippet = tex;
                b.Click += (_, _) => InsertSnippet(snippet);
                VkKeysWrap.Children.Add(b);
            }
        }

        private void VkTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string tag) BuildVkButtons(tag);
        }

        private void InsertSnippet(string tex)
        {
            int idx = TxtFormulaInput.SelectionStart;
            string cur = TxtFormulaInput.Text ?? "";
            int selLen = TxtFormulaInput.SelectionLength;
            TxtFormulaInput.Text = cur.Substring(0, idx) + tex + cur.Substring(idx + selLen);
            int braceIdx = tex.IndexOf("{}", StringComparison.Ordinal);
            TxtFormulaInput.SelectionStart = braceIdx >= 0 ? idx + braceIdx + 1 : idx + tex.Length;
            TxtFormulaInput.Focus();
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (!_isReady || ImgMathPreview == null || TxtFormulaInput == null || ChkForceMath == null) return;
            string txt = (TxtFormulaInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(txt)) { ImgMathPreview.Source = null; return; }
            try
            {
                var col = (Color)ColorConverter.ConvertFromString(_mathColorHex);
                if (col.R > 230 && col.G > 230 && col.B > 230) col = Color.FromRgb(15, 23, 42);
                ImgMathPreview.Source = LatexMathRenderer.RenderPreviewBitmap(txt, col, Math.Min(28, _mathFontSize), 1.35, ChkForceMath.IsChecked == true);
            }
            catch { }
        }

        private void TxtFormulaInput_TextChanged(object sender, TextChangedEventArgs e) { if (_isReady) RefreshPreview(); }
        private void ChkForceMath_Changed(object sender, RoutedEventArgs e) { if (_isReady) RefreshPreview(); }
        private void BtnClearFormula_Click(object sender, RoutedEventArgs e) { TxtFormulaInput.Text = ""; TxtFormulaInput.Focus(); RefreshPreview(); }

        private void MathColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string hex) { _mathColorHex = hex; RefreshPreview(); }
        }

        private void CbMathFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isReady) return;
            if (CbMathFontSize?.SelectedItem is ComboBoxItem it && it.Tag is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double sz))
            {
                _mathFontSize = sz;
                RefreshPreview();
            }
        }

        private bool CommitFormulaOrTextToOverlay()
        {
            string tex = (TxtFormulaInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(tex)) { _surface.ShowToast("Hãy nhập nội dung công thức hoặc chữ trước khi cập nhật!"); return false; }
            bool forceMath = ChkForceMath.IsChecked == true;
            if (_editingKind == "text" && !string.IsNullOrEmpty(_editingTextId))
            {
                _surface.UpdateTextItem(_editingTextId, tex, _mathColorHex, _mathFontSize, upgradeToMath: forceMath);
            }
            else
            {
                _surface.AddOrUpdateMathFormula(_editingFormulaId, tex, _mathColorHex, _mathFontSize, forceMath);
            }
            return true;
        }

        private void BtnInsertFormulaToOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (!CommitFormulaOrTextToOverlay()) return;
            ResetEditingState();
            Hide();
        }

        private void BtnInsertFormulaToApp_Click(object sender, RoutedEventArgs e)
        {
            if (!CommitFormulaOrTextToOverlay()) return;
            ResetEditingState();
            Hide();
            _surface.InsertIntoTargetApp();
        }

        private void BtnDeleteEditing_Click(object sender, RoutedEventArgs e)
        {
            if (_editingKind == "math" && !string.IsNullOrEmpty(_editingFormulaId))
                _surface.DeleteMathFormulaById(_editingFormulaId);
            else if (_editingKind == "text" && !string.IsNullOrEmpty(_editingTextId))
                _surface.DeleteTextById(_editingTextId);
            else if (_editingKind == "handwriting" && _editingStrokeIds.Count > 0)
                _surface.DeleteStrokesByIds(_editingStrokeIds);
            _padStrokes.Clear();
            HandwritingPad.Children.Clear();
            ResetEditingState();
            Hide();
        }

        // ================= HANDWRITING PAD (SMOOTH BEZIER) =================
        private void CbPadPenType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isReady || _surface == null) return;
            if (CbPadPenType?.SelectedItem is ComboBoxItem it && it.Tag is string tag)
            {
                _padPenStyle = tag;
                _surface.PenTipStyle = tag;
            }
        }

        private void CbPadPenWidth_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isReady || _surface == null) return;
            if (CbPadPenWidth?.SelectedItem is ComboBoxItem it && it.Tag is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double w))
            {
                _padWidth = w;
                _surface.StrokeWidth = w;
            }
        }

        private void PenColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string hex)
            {
                _padColorHex = hex;
                _surface.StrokeColorHex = hex;
                if (_padPenStyle == "eraser") { _padPenStyle = "fountain"; CbPadPenType.SelectedIndex = 0; }
            }
        }

        private void BtnPadRecolorAll_Click(object sender, RoutedEventArgs e)
        {
            if (_padStrokes.Count == 0) return;
            foreach (var st in _padStrokes) st.ColorHex = _padColorHex;
            RedrawPad();
        }

        private void BtnPadUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_padStrokes.Count > 0) { _padStrokes.RemoveAt(_padStrokes.Count - 1); RedrawPad(); }
        }

        private void BtnPadClear_Click(object sender, RoutedEventArgs e)
        {
            _padStrokes.Clear();
            HandwritingPad.Children.Clear();
        }

        private void Pad_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(HandwritingPad);
            if (_padPenStyle == "eraser")
            {
                ErasePadNear(p);
                HandwritingPad.CaptureMouse();
                return;
            }
            bool isHl = _padPenStyle == "highlighter";
            double scaleW = _editingKind == "handwriting" ? _editMapScale : 1.0;
            double baseW = (isHl ? Math.Max(14.0, _padWidth * 3.5) : _padWidth) * scaleW;
            _activePadStroke = new FreeStroke
            {
                Id = "pad_" + DateTime.Now.Ticks,
                ColorHex = _padColorHex,
                Width = Math.Max(1.0, baseW),
                Opacity = isHl ? 0.36 : (_padPenStyle == "pencil" ? 0.80 : 1.0),
                IsHighlighter = isHl,
                PenStyle = _padPenStyle
            };
            _activePadStroke.Points.Add(new[] { p.X, p.Y });
            _activePadStroke.PointWidths.Add(_activePadStroke.Width);
            _padStrokes.Add(_activePadStroke);
            _lastSmoothedPadPt = p;
            _lastPadTick = Environment.TickCount64;
            HandwritingPad.CaptureMouse();
            RedrawPad();
        }

        private void Pad_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            Point raw = e.GetPosition(HandwritingPad);
            if (_padPenStyle == "eraser") { ErasePadNear(raw); return; }
            if (_activePadStroke == null) return;

            Point p = new(_lastSmoothedPadPt.X * 0.42 + raw.X * 0.58, _lastSmoothedPadPt.Y * 0.42 + raw.Y * 0.58);
            double d = Math.Sqrt(Math.Pow(p.X - _lastSmoothedPadPt.X, 2) + Math.Pow(p.Y - _lastSmoothedPadPt.Y, 2));
            if (d < 1.3) return;

            long now = Environment.TickCount64;
            double dt = Math.Max(1.0, now - _lastPadTick);
            double vel = d / dt;
            double w = _activePadStroke.Width;
            double prevW = _activePadStroke.PointWidths.Count > 0 ? _activePadStroke.PointWidths[^1] : w;
            if (_activePadStroke.PenStyle == "brush")
            {
                double targetW = Math.Clamp(w * (1.85 - Math.Min(1.25, vel * 0.72)), Math.Max(1.0, w * 0.38), w * 2.05);
                w = prevW * 0.65 + targetW * 0.35;
            }
            else if (_activePadStroke.PenStyle == "fountain")
            {
                double targetW = Math.Clamp(w * (1.16 - Math.Min(0.36, vel * 0.24)), Math.Max(0.9, w * 0.74), w * 1.22);
                w = prevW * 0.7 + targetW * 0.3;
            }
            _activePadStroke.Points.Add(new[] { p.X, p.Y });
            _activePadStroke.PointWidths.Add(w);
            _lastSmoothedPadPt = p;
            _lastPadTick = now;
            RedrawPad();
        }

        private void Pad_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _activePadStroke = null;
            if (HandwritingPad.IsMouseCaptured) HandwritingPad.ReleaseMouseCapture();
        }

        private void ErasePadNear(Point p)
        {
            int removed = _padStrokes.RemoveAll(st => st.Points.Any(pt => Math.Abs(pt[0] - p.X) < 12 && Math.Abs(pt[1] - p.Y) < 12));
            if (removed > 0) RedrawPad();
        }

        private void RedrawPad()
        {
            HandwritingPad.Children.Clear();
            foreach (var st in _padStrokes)
            {
                if (st.Points.Count == 0) continue;
                var col = (Color)ColorConverter.ConvertFromString(st.ColorHex);
                if (col.R > 235 && col.G > 235 && col.B > 235) col = Color.FromRgb(15, 23, 42);
                col.A = (byte)Math.Clamp(st.Opacity * 255.0, 20, 255);
                var br = new SolidColorBrush(col);

                if (st.Points.Count == 1)
                {
                    double r = Math.Max(1.5, st.Width * 0.55);
                    var dot = new Ellipse { Width = r * 2, Height = r * 2, Fill = br };
                    Canvas.SetLeft(dot, st.Points[0][0] - r);
                    Canvas.SetTop(dot, st.Points[0][1] - r);
                    HandwritingPad.Children.Add(dot);
                    continue;
                }

                if ((st.PenStyle is "brush" or "fountain") && st.PointWidths.Count == st.Points.Count)
                {
                    for (int i = 0; i < st.Points.Count - 1; i++)
                    {
                        var ln = new Line
                        {
                            X1 = st.Points[i][0], Y1 = st.Points[i][1],
                            X2 = st.Points[i + 1][0], Y2 = st.Points[i + 1][1],
                            Stroke = br, StrokeThickness = Math.Max(1.0, st.PointWidths[i]),
                            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                        };
                        HandwritingPad.Children.Add(ln);
                    }
                }
                else
                {
                    var pf = new PathFigure { StartPoint = new Point(st.Points[0][0], st.Points[0][1]), IsClosed = false };
                    for (int i = 1; i < st.Points.Count - 1; i++)
                    {
                        var p1 = new Point(st.Points[i][0], st.Points[i][1]);
                        var mid = new Point((st.Points[i][0] + st.Points[i + 1][0]) * 0.5, (st.Points[i][1] + st.Points[i + 1][1]) * 0.5);
                        pf.Segments.Add(new QuadraticBezierSegment(p1, mid, true));
                    }
                    pf.Segments.Add(new LineSegment(new Point(st.Points[^1][0], st.Points[^1][1]), true));
                    var path = new Path
                    {
                        Data = new PathGeometry(new[] { pf }),
                        Stroke = br,
                        StrokeThickness = st.Width,
                        StrokeStartLineCap = st.IsHighlighter ? PenLineCap.Flat : PenLineCap.Round,
                        StrokeEndLineCap = st.IsHighlighter ? PenLineCap.Flat : PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round
                    };
                    if (st.PenStyle == "dash") { path.StrokeDashArray = new DoubleCollection { 4.5, 3.5 }; path.StrokeDashCap = PenLineCap.Flat; }
                    HandwritingPad.Children.Add(path);
                }
            }
        }

        private void BtnWriteOnScreen_Click(object sender, RoutedEventArgs e)
        {
            _surface.PenTipStyle = _padPenStyle;
            _surface.StrokeColorHex = _padColorHex;
            _surface.StrokeWidth = _padWidth;
            _surface.SetTool(_padPenStyle == "highlighter" ? "highlighter" : (_padPenStyle == "eraser" ? "eraser" : "pen"));
            Hide();
            _surface.ShowToast($"Đã bật bút viết mịn ({_padPenStyle}) trực tiếp trên màn hình!", 3500);
        }

        private bool CommitPadToOverlay()
        {
            if (_editingKind == "handwriting" && _editingStrokeIds.Count > 0)
            {
                if (_padStrokes.Count == 0)
                {
                    _surface.DeleteStrokesByIds(_editingStrokeIds);
                    return true;
                }
                var mapped = MapPadStrokesBackToScreen();
                _surface.ReplaceHandwritingCluster(_editingStrokeIds, mapped);
                return true;
            }
            if (_padStrokes.Count == 0) { _surface.ShowToast("Bảng viết đang trống!"); return false; }
            _surface.AddHandwritingStrokes(_padStrokes);
            return true;
        }

        private void BtnInsertPadToOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (!CommitPadToOverlay()) return;
            _padStrokes.Clear();
            HandwritingPad.Children.Clear();
            ResetEditingState();
            Hide();
        }

        private void BtnInsertPadToApp_Click(object sender, RoutedEventArgs e)
        {
            if (!CommitPadToOverlay()) return;
            _padStrokes.Clear();
            HandwritingPad.Children.Clear();
            ResetEditingState();
            Hide();
            _surface.InsertIntoTargetApp();
        }
    }
}
