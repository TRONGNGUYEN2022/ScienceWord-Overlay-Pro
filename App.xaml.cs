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

            overlay.Surface.ShowToast("ScienceWord Overlay Universal v6 (Di chuyển khối + Chèn App + Double-Click sửa) sẵn sàng!");
        }
    }
}
