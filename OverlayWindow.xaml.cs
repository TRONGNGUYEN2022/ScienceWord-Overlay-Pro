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

        private Button MakeCtxBtn(string text, Action onClick, string bgHex = "#1E293B", string fgHex = "#F8FAFC")
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
                Cursor = System.Windows.Input.Cursors.Hand
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

            if (kind == "3d" && Surface.Solid3D != null)
            {
                var s3 = Surface.Solid3D;
                ContextMenuItems.Children.Add(MakeCtxBtn("✥ Di chuyển", () => Surface.SetTool("select"), "#1D4ED8"));
                ContextMenuItems.Children.Add(MakeCtxBtn("🔄 Xoay 3D", () => Surface.SetTool("orbit_3d"), "#7C3AED"));
                ContextMenuItems.Children.Add(MakeCtxBtn(s3.IsSolidShaded ? "🧊 Khối đặc ✓" : "📐 Khung dây", () => Surface.ToggleSolid3DShaded(), s3.IsSolidShaded ? "#0F766E" : "#334155"));
                ContextMenuItems.Children.Add(MakeCtxBtn("🎨 Màu khối", () => Surface.CycleSolid3DColor(), s3.SolidColorHex, "#0F172A"));
                ContextMenuItems.Children.Add(MakeCtxBtn("＋", () => Surface.ScaleSolid3D(1.12)));
                ContextMenuItems.Children.Add(MakeCtxBtn("－", () => Surface.ScaleSolid3D(0.89)));
                if (s3.Kind == "poly")
                {
                    ContextMenuItems.Children.Add(MakeCtxBtn("• Điểm cạnh", () => Surface.SetTool("point_edge_3d")));
                    ContextMenuItems.Children.Add(MakeCtxBtn("∥ Song song", () => Surface.SetTool("parallel_3d")));
                    ContextMenuItems.Children.Add(MakeCtxBtn("🔶 Thiết diện", () => Surface.SetTool("section_3d")));
                }
                ContextMenuItems.Children.Add(MakeCtxBtn("🗑", () => { Surface.DeleteSolid3D(); ContextMenuBar.Visibility = Visibility.Collapsed; }, "#DC2626"));
            }
            else if (kind == "2d")
            {
                ContextMenuItems.Children.Add(MakeCtxBtn("🎨 Màu nét", () => Surface.CycleSelected2DStrokeColor(), "#1D4ED8"));
                ContextMenuItems.Children.Add(MakeCtxBtn("┄ Đứt/Liền", () => Surface.ToggleSelected2DDash()));
                ContextMenuItems.Children.Add(MakeCtxBtn("🪣 Tô miền", () => Surface.FillSelected2DLoop(), "#D97706"));
                ContextMenuItems.Children.Add(MakeCtxBtn("📋 Nhân bản", () => Surface.DuplicateSelected2D(), "#0F766E"));
                ContextMenuItems.Children.Add(MakeCtxBtn("🗑 Xóa", () => { Surface.DeleteSelected(); ContextMenuBar.Visibility = Visibility.Collapsed; }, "#DC2626"));
            }
            else if (kind == "rev" && Surface.RevScene != null)
            {
                ContextMenuItems.Children.Add(MakeCtxBtn("＋ Cận (Click)", () => Surface.SetTool("add_bound"), "#0EA5E9"));
                ContextMenuItems.Children.Add(MakeCtxBtn("🎨 Màu S", () => Surface.CycleArea2DColor(), Surface.Area2DColorHex));
                ContextMenuItems.Children.Add(MakeCtxBtn("🌀 Quay 3D (V)", () => { Surface.RevMode = MathRenderMode.Solid3D; Surface.InvalidateVisual(); }, "#7C3AED"));
                ContextMenuItems.Children.Add(MakeCtxBtn("🎨 Màu V", () => Surface.CycleVolume3DColor(), Surface.Volume3DColorHex));
                ContextMenuItems.Children.Add(MakeCtxBtn("🗑", () => { Surface.RevScene = null; Surface.NotifyMetrics(); Surface.InvalidateVisual(); ContextMenuBar.Visibility = Visibility.Collapsed; }, "#DC2626"));
            }
            else
            {
                ContextMenuBar.Visibility = Visibility.Collapsed;
                return;
            }

            ContextMenuBar.Visibility = Visibility.Visible;
            ContextMenuBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double bw = ContextMenuBar.DesiredSize.Width;
            double bh = ContextMenuBar.DesiredSize.Height;

            double x = Math.Clamp(anchor.Value.X, 12, Math.Max(12, ActualWidth - bw - 16));
            double y = anchor.Value.Y - bh - 12;
            if (y < 92) y = Math.Min(ActualHeight - bh - 20, anchor.Value.Y + 18);
            Canvas.SetLeft(ContextMenuBar, x);
            Canvas.SetTop(ContextMenuBar, y);
        }
    }
}