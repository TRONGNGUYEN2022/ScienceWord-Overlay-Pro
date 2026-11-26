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

            overlay.Surface.ShowToast("ScienceWord Overlay Universal v10.3 (Quay 3D trục thẳng đứng + Bút viết mịn đa chất liệu + Text-Math LaTeX + Mặt phẳng cắt (P) + Chèn App 2 chiều) (Tọa độ tương đối theo màn hình) (Mô phỏng Thước, Êke, Compa, Bút 2D & 3D) (Di chuyển khối + Chèn App + Double-Click sửa) sẵn sàng!");
        }
    }
}
