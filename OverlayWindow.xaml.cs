using System;
using System.Windows;
using System.Windows.Input;
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
        }

        public void SetPassthrough(bool enabled)
        {
            Surface.IsPassthrough = enabled;
            Win32Helper.SetClickThrough(this, enabled);
            Surface.InvalidateVisual();
            Surface.ShowToast(enabled
                ? "Chế độ XUYÊN CHUỘT: Thao tác trực tiếp trên Word/PDF/Web bên dưới"
                : "Chế độ VẼ TRONG SUỐT: Sẵn sàng vẽ và tô màu trên màn hình");
        }
    }
}
