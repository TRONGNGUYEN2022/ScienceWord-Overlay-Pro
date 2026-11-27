using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScienceWordOverlay.Models;
using ScienceWordOverlay.Utils;

namespace ScienceWordOverlay
{
    public partial class OverlayWindow : Window
    {
        public OverlayWindow()
        {
            InitializeComponent();
            // Dat khop 100% VirtualScreen, khong dung WindowState=Maximized de tranh lech -8px DWM cua Win10
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            SourceInitialized += (_, _) => Win32Helper.HideFromAltTab(this);
            PreviewKeyDown += (_, e) => Surface.HandleKeyDown(e);
            Surface.ContextMenuRequested += OnContextMenuRequested;
        }

        public void SetPassthrough(bool enabled)
        {
            Surface.IsPassthrough = enabled;
            Win32Helper.SetClickThrough(this, enabled);
            if (enabled) ContextMenuBar.Visibility = Visibility.Collapsed;
            Surface.InvalidateVisual();
            Surface.ShowToast(enabled
                ? "Chế độ XUYÊN CHUỘT: Thao tác trực tiếp trên App bên dưới"
                : "Chế độ VẼ TRONG SUỐT: Sẵn sàng vẽ, tô màu, 2D & 3D");
        }

        private Button MakeCtxBtn(string text, Action onClick, string bgHex = "#1E293B", string fgHex = "#F8FAFC", string? tooltip = null)
        {
            var btn = new Button
            {
                Content = text,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex)),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(140, 148, 163, 184)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(2, 0, 2, 0),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = tooltip
            };
            btn.Click += (_, _) => onClick();
            return btn;
        }

        private void OnContextMenuRequested(string kind, Point? anchor)
        {
            if (string.IsNullOrEmpty(kind) || !anchor.HasValue || Surface.IsPassthrough)
            {
                ContextMenuBar.Visibility = Visibility.Collapsed;
                return;
            }

            ContextMenuItems.Children.Clear();
            var row1 = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            if (kind == "3d" && Surface.Solid3D != null)
            {
                var s3 = Surface.Solid3D;
                row1.Children.Add(MakeCtxBtn("✥ Di chuyển", () => Surface.SetTool("select"), "#1D4ED8"));
                row1.Children.Add(MakeCtxBtn("🔄 Xoay tay", () => Surface.SetTool("orbit_3d"), "#7C3AED"));
                row1.Children.Add(MakeCtxBtn(Surface.IsSpinningVertical3D ? "⏸ Dừng đứng" : "⟳ Trục đứng", () => Surface.ToggleVerticalSpin3D(), Surface.IsSpinningVertical3D ? "#DC2626" : "#B45309", tooltip: "Tự động quay khối 3D quanh trục thẳng đứng"));
                row1.Children.Add(MakeCtxBtn(Surface.IsSpinningHorizontal3D ? "⏸ Dừng ngang" : "⇵ Trục ngang", () => Surface.ToggleHorizontalSpin3D(), Surface.IsSpinningHorizontal3D ? "#DC2626" : "#0369A1", tooltip: "Tự động quay khối 3D quanh trục nằm ngang"));
                row1.Children.Add(MakeCtxBtn(s3.IsSolidShaded ? "🧊 Khối đặc ✓" : "📐 Khung dây", () => Surface.ToggleSolid3DShaded(), s3.IsSolidShaded ? "#0F766E" : "#334155"));
                row1.Children.Add(MakeCtxBtn("🎨 Màu", () => Surface.CycleSolid3DColor(), s3.SolidColorHex, "#0F172A"));
                if (s3.Kind is "poly" or "cone" or "cylinder")
                {
                    row1.Children.Add(MakeCtxBtn(s3.IsNetUnfolded ? "📦 Gấp lại" : "📦 Net (Trải phẳng)", () => Surface.ToggleNetUnfold3D(), s3.IsNetUnfolded ? "#D97706" : "#0F766E", tooltip: "Khai triển trải phẳng Net các mặt khối 3D (giống GeoGebra)"));
                    if (s3.IsNetUnfolded && s3.Kind == "poly" && s3.Faces.Count >= 6)
                        row1.Children.Add(MakeCtxBtn("🔄 Bản lề nắp", () => Surface.CycleNetTopHinge3D(), "#4F46E5", tooltip: "Đổi cạnh bản lề để lật nắp trên"));
                }
                row1.Children.Add(MakeCtxBtn(Surface.ShowCoordSystem3D ? "🌐 Oxyz ✓" : "🌐 Oxyz", () => Surface.ToggleCoordSystem3D(), Surface.ShowCoordSystem3D ? "#1D4ED8" : "#334155", tooltip: "Bật / tắt Hệ tọa độ không gian Oxyz"));
                if (Surface.Solids3DList.Count >= 2)
                    row1.Children.Add(MakeCtxBtn("⋈ Giao 2 khối", () => Surface.IntersectActiveSolids3D(), "#9333EA", tooltip: "Tính giao giữa 2 khối 3D"));
                row1.Children.Add(MakeCtxBtn("＋", () => Surface.ScaleSolid3D(1.12), tooltip: "Phóng to khối 3D"));
                row1.Children.Add(MakeCtxBtn("－", () => Surface.ScaleSolid3D(0.89), tooltip: "Thu nhỏ khối 3D"));
                if (s3.Kind == "poly")
                {
                    row1.Children.Add(MakeCtxBtn("• Điểm cạnh", () => Surface.SetTool("point_edge_3d")));
                    row1.Children.Add(MakeCtxBtn("∥ Song song", () => Surface.SetTool("parallel_3d")));
                    row1.Children.Add(MakeCtxBtn("▱ Mp 3đ", () => Surface.SetTool("plane_3pts_3d"), "#0284C7", tooltip: "Dựng mặt phẳng (P) qua 3 điểm → tự tính thiết diện"));
                    row1.Children.Add(MakeCtxBtn("▱ Mp //", () => Surface.SetTool("plane_para_3d"), "#0369A1", tooltip: "Dựng mặt phẳng (P) qua 1 điểm // mặt phẳng chuẩn"));
                }
                row1.Children.Add(MakeCtxBtn("✂️ Mp cắt", () => Surface.SpawnInteractiveCutPlane3D(), "#B45309", tooltip: "Tạo mặt phẳng cắt (P) tương tác"));
                row1.Children.Add(MakeCtxBtn("🗑", () => { Surface.DeleteSolid3D(); ContextMenuBar.Visibility = Visibility.Collapsed; }, "#DC2626", tooltip: "Xóa khối 3D"));
                ContextMenuItems.Children.Add(row1);

                if (s3.CutPlane != null)
                {
                    var row2 = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
                    row2.Children.Add(new TextBlock
                    {
                        Text = "Mp (P):",
                        Foreground = new SolidColorBrush(Color.FromRgb(125, 211, 252)),
                        FontSize = 10.5,
                        FontWeight = FontWeights.Bold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(2, 0, 4, 0)
                    });
                    row2.Children.Add(MakeCtxBtn("⬆", () => Surface.NudgeCutPlane3D(0.14), "#1E3A8A", tooltip: "Nâng độ cao mặt phẳng (P)"));
                    row2.Children.Add(MakeCtxBtn("⬇", () => Surface.NudgeCutPlane3D(-0.14), "#1E3A8A", tooltip: "Hạ độ cao mặt phẳng (P)"));
                    row2.Children.Add(MakeCtxBtn("⟲", () => Surface.TiltCutPlane3D(-12.0), "#4C1D95", tooltip: "Xoay góc nghiêng mặt phẳng (P) trái"));
                    row2.Children.Add(MakeCtxBtn("⟳", () => Surface.TiltCutPlane3D(12.0), "#4C1D95", tooltip: "Xoay góc nghiêng mặt phẳng (P) phải"));
                    row2.Children.Add(MakeCtxBtn("⊥ Dọc", () => Surface.SetCutPlaneVertical90(), "#0F766E", tooltip: "Cắt dọc 90° qua trục (HCN / Tam giác cân / Đường tròn lớn)"));
                    row2.Children.Add(MakeCtxBtn(s3.CutPlane.Visible ? "👁 Ẩn (P)" : "👁 Hiện (P)", () => Surface.ToggleCutPlaneVisible(), "#334155", tooltip: "Bật / tắt hiển thị tấm mặt phẳng (P)"));
                    row2.Children.Add(MakeCtxBtn("↺ Ghép phần", () => Surface.ResetSolidPartOffsets(), "#047857", tooltip: "Ghép khít các phần đã kéo tách"));
                    row2.Children.Add(MakeCtxBtn("✕ Gỡ (P)", () => Surface.RemoveCutPlane3D(), "#991B1B", tooltip: "Xóa mặt phẳng cắt (P)"));
                    ContextMenuItems.Children.Add(row2);
                }
            }
            else if (kind == "2d")
            {
                row1.Children.Add(MakeCtxBtn("🎨 Màu nét", () => Surface.CycleSelected2DStrokeColor(), "#1D4ED8"));
                row1.Children.Add(MakeCtxBtn("┄ Đứt/Liền", () => Surface.ToggleSelected2DDash()));
                row1.Children.Add(MakeCtxBtn("🪣 Tô miền", () => Surface.FillSelected2DLoop(), "#D97706"));
                row1.Children.Add(MakeCtxBtn("📋 Nhân bản", () => Surface.DuplicateSelected2D(), "#0F766E"));
                row1.Children.Add(MakeCtxBtn("🗑 Xóa", () => { Surface.DeleteSelected(); ContextMenuBar.Visibility = Visibility.Collapsed; }, "#DC2626"));
                ContextMenuItems.Children.Add(row1);
            }
            else if (kind == "rev" && Surface.RevScene != null)
            {
                row1.Children.Add(MakeCtxBtn("＋ Cận/Miền", () => Surface.SetTool("add_bound"), "#0EA5E9"));
                row1.Children.Add(MakeCtxBtn(Surface.RevShowBoundsHandles ? "👁 Ẩn cận" : "👁 Hiện cận", () => Surface.ToggleRevBoundsHandles(), Surface.RevShowBoundsHandles ? "#475569" : "#0F766E", tooltip: "Ẩn / hiện các nút cận màu cam (a, b, c...) trên trục"));
                row1.Children.Add(MakeCtxBtn("🎨 Màu S", () => Surface.CycleArea2DColor(), Surface.Area2DColorHex));
                row1.Children.Add(MakeCtxBtn("🌀 Quay 3D (V)", () => { Surface.RevMode = MathRenderMode.Solid3D; Surface.InvalidateVisual(); }, "#7C3AED"));
                row1.Children.Add(MakeCtxBtn(Surface.IsSpinningVertical3D ? "⏸ Dừng đứng" : "⟳ Trục đứng", () => Surface.ToggleVerticalSpin3D(), Surface.IsSpinningVertical3D ? "#DC2626" : "#B45309", tooltip: "Quay khối tròn xoay 3D quanh trục thẳng đứng"));
                row1.Children.Add(MakeCtxBtn(Surface.IsSpinningHorizontal3D ? "⏸ Dừng ngang" : "⇵ Trục ngang", () => Surface.ToggleHorizontalSpin3D(), Surface.IsSpinningHorizontal3D ? "#DC2626" : "#0369A1", tooltip: "Quay khối tròn xoay 3D quanh trục nằm ngang"));
                row1.Children.Add(MakeCtxBtn("↺ 0°", () => Surface.ResetRevTurntable(), "#334155", tooltip: "Đưa về góc 0° khớp với hình gốc"));
                row1.Children.Add(MakeCtxBtn("🎨 Màu V", () => Surface.CycleVolume3DColor(), Surface.Volume3DColorHex));
                row1.Children.Add(MakeCtxBtn("－ Miền", () => Surface.RemoveRevolutionRegion(), "#475569"));
                row1.Children.Add(MakeCtxBtn("🗑", () => { Surface.SetSpinAxis3D(SpinAxisMode.None); Surface.RevScene = null; Surface.NotifyMetrics(); Surface.InvalidateVisual(); ContextMenuBar.Visibility = Visibility.Collapsed; }, "#DC2626"));
                ContextMenuItems.Children.Add(row1);
            }
            else if (kind == "math")
            {
                row1.Children.Add(MakeCtxBtn("✏️ Sửa công thức", () => Surface.EditSelectedMathFormula(), "#D97706"));
                row1.Children.Add(MakeCtxBtn("A－", () => Surface.ScaleSelectedMathFormula(-3)));
                row1.Children.Add(MakeCtxBtn("A＋", () => Surface.ScaleSelectedMathFormula(3)));
                row1.Children.Add(MakeCtxBtn("🎨 Màu", () => Surface.CycleSelectedMathColor(), "#1D4ED8"));
                row1.Children.Add(MakeCtxBtn("📋 Nhân bản", () => Surface.DuplicateSelected2D(), "#0F766E"));
                row1.Children.Add(MakeCtxBtn("📥 Chèn App", () => Surface.InsertIntoTargetApp(), "#059669"));
                row1.Children.Add(MakeCtxBtn("🗑 Xóa", () => { Surface.DeleteSelected(); ContextMenuBar.Visibility = Visibility.Collapsed; }, "#DC2626"));
                ContextMenuItems.Children.Add(row1);
            }
            else if (kind == "text")
            {
                row1.Children.Add(MakeCtxBtn("✏️ Sửa chữ", () => Surface.EditSelectedText(), "#D97706"));
                row1.Children.Add(MakeCtxBtn("A－", () => Surface.ScaleSelectedText(-3)));
                row1.Children.Add(MakeCtxBtn("A＋", () => Surface.ScaleSelectedText(3)));
                row1.Children.Add(MakeCtxBtn("🎨 Màu", () => Surface.CycleSelectedTextColor(), "#1D4ED8"));
                row1.Children.Add(MakeCtxBtn("📋 Nhân bản", () => Surface.DuplicateSelected2D(), "#0F766E"));
                row1.Children.Add(MakeCtxBtn("📥 Chèn App", () => Surface.InsertIntoTargetApp(), "#059669"));
                row1.Children.Add(MakeCtxBtn("🗑 Xóa", () => { Surface.DeleteSelected(); ContextMenuBar.Visibility = Visibility.Collapsed; }, "#DC2626"));
                ContextMenuItems.Children.Add(row1);
            }
            else if (kind == "handwriting")
            {
                row1.Children.Add(MakeCtxBtn("✏️ Sửa nét viết", () => Surface.EditSelectedHandwriting(), "#D97706"));
                row1.Children.Add(MakeCtxBtn("🎨 Màu nét", () => Surface.CycleSelected2DStrokeColor(), "#1D4ED8"));
                row1.Children.Add(MakeCtxBtn("📋 Nhân bản", () => Surface.DuplicateSelected2D(), "#0F766E"));
                row1.Children.Add(MakeCtxBtn("📥 Chèn App", () => Surface.InsertIntoTargetApp(), "#059669"));
                row1.Children.Add(MakeCtxBtn("🗑 Xóa", () => { Surface.DeleteSelected(); ContextMenuBar.Visibility = Visibility.Collapsed; }, "#DC2626"));
                ContextMenuItems.Children.Add(row1);
            }
            else
            {
                ContextMenuBar.Visibility = Visibility.Collapsed;
                return;
            }

            ContextMenuBar.Visibility = Visibility.Visible;
            ContextMenuBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double bw = ContextMenuBar.DesiredSize.Width, bh = ContextMenuBar.DesiredSize.Height;
            double x = Math.Clamp(anchor.Value.X, 12, Math.Max(12, ActualWidth - bw - 16));
            double y = anchor.Value.Y - bh - 12;
            if (y < 92) y = Math.Min(ActualHeight - bh - 20, anchor.Value.Y + 18);
            Canvas.SetLeft(ContextMenuBar, x);
            Canvas.SetTop(ContextMenuBar, y);
        }
    }
}
