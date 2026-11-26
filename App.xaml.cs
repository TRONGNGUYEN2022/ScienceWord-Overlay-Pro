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
            toolbar.Owner = overlay;
            toolbar.Show();

            overlay.Surface.ShowToast("ScienceWord Overlay Universal v9 (2D/3D tương tác + Chọn nền + 2 Menu gọn) (Tọa độ tương đối theo màn hình) (Mô phỏng Thước, Êke, Compa, Bút 2D & 3D) (Di chuyển khối + Chèn App + Double-Click sửa) sẵn sàng!");
        }
    }
}
