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
            toolbar.Show();

            overlay.Surface.ShowToast("ScienceWord Overlay Pro (C# WPF) đã sẵn sàng trên lớp trong suốt!");
        }
    }
}
