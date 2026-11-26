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
        private string? _editingFormulaId;
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
            InitializeComponent();
            _surface = surface;
            Loaded += (_, _) =>
            {
                BuildVkButtons("basic");
                RefreshPreview();
            };
        }

        public void OpenForCreate(bool penMode = false)
        {
            _editingFormulaId = null;
            TxtEditBadge.Text = "Tạo mới";
            BtnInsertOverlay.Content = "＋ Chèn lên bảng vẽ";
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
            _editingFormulaId = f.Id;
            TxtEditBadge.Text = "Đang sửa công thức";
            BtnInsertOverlay.Content = "✓ Cập nhật công thức";
            TxtFormulaInput.Text = f.Latex;
            _mathColorHex = f.ColorHex;
            _mathFontSize = f.FontSize;
            ChkForceMath.IsChecked = f.ForceMath;
            SetMode(false);
            Show();
            Activate();
            RefreshPreview();
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
            if (ImgMathPreview == null || TxtFormulaInput == null) return;
            string txt = (TxtFormulaInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(txt)) { ImgMathPreview.Source = null; return; }
            try
            {
                var col = (Color)ColorConverter.ConvertFromString(_mathColorHex);
                // Neu chon chu trang tren nen preview sang, hien mau toi trong khung xem truoc de de doc
                if (col.R > 230 && col.G > 230 && col.B > 230) col = Color.FromRgb(15, 23, 42);
                ImgMathPreview.Source = LatexMathRenderer.RenderPreviewBitmap(txt, col, Math.Min(28, _mathFontSize), 1.35, ChkForceMath.IsChecked == true);
            }
            catch { }
        }

        private void TxtFormulaInput_TextChanged(object sender, TextChangedEventArgs e) => RefreshPreview();
        private void ChkForceMath_Changed(object sender, RoutedEventArgs e) => RefreshPreview();
        private void BtnClearFormula_Click(object sender, RoutedEventArgs e) { TxtFormulaInput.Text = ""; TxtFormulaInput.Focus(); RefreshPreview(); }

        private void MathColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string hex) { _mathColorHex = hex; RefreshPreview(); }
        }

        private void CbMathFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbMathFontSize?.SelectedItem is ComboBoxItem it && it.Tag is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double sz))
            {
                _mathFontSize = sz;
                RefreshPreview();
            }
        }

        private void BtnInsertFormulaToOverlay_Click(object sender, RoutedEventArgs e)
        {
            string tex = (TxtFormulaInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(tex)) { _surface.ShowToast("Hãy nhập nội dung công thức hoặc chữ trước khi chèn!"); return; }
            _surface.AddOrUpdateMathFormula(_editingFormulaId, tex, _mathColorHex, _mathFontSize, ChkForceMath.IsChecked == true);
            _editingFormulaId = null;
            TxtEditBadge.Text = "Tạo mới";
            BtnInsertOverlay.Content = "＋ Chèn lên bảng vẽ";
            Hide();
        }

        private void BtnInsertFormulaToApp_Click(object sender, RoutedEventArgs e)
        {
            string tex = (TxtFormulaInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(tex)) { _surface.ShowToast("Hãy nhập công thức hoặc chữ trước khi chèn vào App!"); return; }
            Hide();
            _surface.InsertFormulaDirectlyToApp(tex, _mathColorHex, _mathFontSize, ChkForceMath.IsChecked == true);
        }

        // ================= HANDWRITING PAD (SMOOTH BEZIER) =================
        private void CbPadPenType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbPadPenType?.SelectedItem is ComboBoxItem it && it.Tag is string tag)
            {
                _padPenStyle = tag;
                _surface.PenTipStyle = tag;
            }
        }

        private void CbPadPenWidth_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
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
            double baseW = isHl ? Math.Max(14.0, _padWidth * 3.5) : _padWidth;
            _activePadStroke = new FreeStroke
            {
                Id = "pad_" + DateTime.Now.Ticks,
                ColorHex = _padColorHex,
                Width = baseW,
                Opacity = isHl ? 0.36 : (_padPenStyle == "pencil" ? 0.80 : 1.0),
                IsHighlighter = isHl,
                PenStyle = _padPenStyle
            };
            _activePadStroke.Points.Add(new[] { p.X, p.Y });
            _activePadStroke.PointWidths.Add(baseW);
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

            // EMA smoothing cho chu viet tay min mang khong rang cua
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
                if (st.Points.Count < 2) continue;
                var col = (Color)ColorConverter.ConvertFromString(st.ColorHex);
                if (col.R > 235 && col.G > 235 && col.B > 235) col = Color.FromRgb(15, 23, 42);
                col.A = (byte)Math.Clamp(st.Opacity * 255.0, 20, 255);
                var br = new SolidColorBrush(col);

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
            _surface.SetTool(_padPenStyle == "highlighter" ? "highlighter" : "pen");
            Hide();
            _surface.ShowToast($"Đã bật bút viết mịn ({_padPenStyle}) trực tiếp trên màn hình!", 3500);
        }

        private void BtnInsertPadToOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (_padStrokes.Count == 0) { _surface.ShowToast("Bảng viết đang trống!"); return; }
            _surface.AddHandwritingStrokes(_padStrokes);
            _padStrokes.Clear();
            HandwritingPad.Children.Clear();
            Hide();
        }

        private void BtnInsertPadToApp_Click(object sender, RoutedEventArgs e)
        {
            if (_padStrokes.Count == 0) { _surface.ShowToast("Bảng viết đang trống!"); return; }
            var copy = _padStrokes.ToList();
            _padStrokes.Clear();
            HandwritingPad.Children.Clear();
            Hide();
            _surface.InsertStrokesDirectlyToApp(copy);
        }
    }
}
