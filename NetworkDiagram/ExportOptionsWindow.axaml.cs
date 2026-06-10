using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NetworkDiagram
{
    public enum ExportOption
    {
        Cancel,
        Transparent,
        White,
        BlackAndWhite
    }

    public partial class ExportOptionsWindow : Window
    {
        public ExportOptionsWindow()
        {
            InitializeComponent();
        }

        private void White_Click(object? sender, RoutedEventArgs e)
        {
            Close(ExportOption.White);
        }

        private void Transparent_Click(object? sender, RoutedEventArgs e)
        {
            Close(ExportOption.Transparent);
        }

        private void BlackAndWhite_Click(object? sender, RoutedEventArgs e)
        {
            Close(ExportOption.BlackAndWhite);
        }
    }
}
