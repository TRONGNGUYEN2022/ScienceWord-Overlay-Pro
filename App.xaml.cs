using System.Windows;

namespace ScienceWordOverlay
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var overlay = new OverlayWindow();
            overlay.Show();

            var toolbar = new ToolbarWindow(overlay);
            toolbar.Owner = overlay; // Khóa thanh công cụ luôn nằm TRÊN lớp vẽ trong suốt, sửa dứt điểm lỗi dính lệnh!
            toolbar.Show();

            overlay.Surface.ShowToast("ScienceWord Overlay Universal (Office COM + Đũa thần PDF/Web/Paint) đã sẵn sàng!");
        }
    }
}
