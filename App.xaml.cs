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

            overlay.Surface.ShowToast("ScienceWord Overlay Universal v10.2 (Mặt phẳng cắt (P) & Thiết diện 3D + Nét đứt chuẩn + Khớp tọa độ 0px + Miền S rời) (Tọa độ tương đối theo màn hình) (Mô phỏng Thước, Êke, Compa, Bút 2D & 3D) (Di chuyển khối + Chèn App + Double-Click sửa) sẵn sàng!");
        }
    }
}
