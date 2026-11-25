using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScienceWordOverlay
{
    public class QuickInputWindow : Window
    {
        private readonly TextBox _input;
        public string ResultText => _input.Text;

        public QuickInputWindow(string title, string prompt)
        {
            Title = title;
            Width = 380;
            Height = 155;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));

            var sp = new StackPanel { Margin = new Thickness(14) };
            sp.Children.Add(new TextBlock { Text = prompt, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });

            _input = new TextBox { FontSize = 14, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 12) };
            sp.Children.Add(_input);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new Button { Content = "Chèn", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var btnCancel = new Button { Content = "Hủy", Width = 72, Height = 28, IsCancel = true };
            btnOk.Click += (_, _) => { DialogResult = true; Close(); };
            btnRow.Children.Add(btnOk);
            btnRow.Children.Add(btnCancel);
            sp.Children.Add(btnRow);

            Content = sp;
            Loaded += (_, _) => _input.Focus();
        }
    }
}
