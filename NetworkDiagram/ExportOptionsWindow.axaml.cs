using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NetworkDiagram
{
    public partial class ExportOptionsWindow : Window
    {
        public ExportOptionsWindow()
        {
            InitializeComponent();
        }

        private void White_Click(object? sender, RoutedEventArgs e)
        {
            Close(true);
        }

        private void Transparent_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
