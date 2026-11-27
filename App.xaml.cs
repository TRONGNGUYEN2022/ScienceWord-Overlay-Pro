using System;
using System.Windows;

namespace ScienceWordOverlay
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            OverlayWindow? overlay = null;
            DispatcherUnhandledException += (_, exArgs) =>
            {
                exArgs.Handled = true;
                try
                {
                    if (overlay != null)
                        overlay.Surface.ShowToast("Lỗi thao tác: " + exArgs.Exception.Message, 5000);
                    else
                        MessageBox.Show(exArgs.Exception.Message, "ScienceWord Overlay Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch { }
            };

            overlay = new OverlayWindow();
            overlay.Show();

            var toolbar = new ToolbarWindow(overlay);
            toolbar.Owner = overlay;
            toolbar.Show();

            overlay.Surface.ShowToast("ScienceWord Overlay Universal v10.9 (Giao 2 khối CSG chuẩn AutoCAD + Cắt tách 2 nửa chuẩn SGK (M,N,P,Q) cho mọi khối 3D + Net GeoGebra + Oxyz) sẵn sàng!");
        }
    }
}
