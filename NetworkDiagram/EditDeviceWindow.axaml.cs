using Avalonia.Controls;
using Avalonia.Interactivity;
using NetworkDiagram.Models;

namespace NetworkDiagram
{
    public partial class EditDeviceWindow : Window
    {
        private readonly PlacedDevice _device;

        public EditDeviceWindow()
        {
            InitializeComponent();
            _device = new PlacedDevice(); // Should not be used but needed for designer
        }

        public EditDeviceWindow(PlacedDevice device)
        {
            InitializeComponent();
            _device = device;
            NameBox.Text = device.Name;
            IpBox.Text = string.Join(Environment.NewLine, device.IpAddresses);
        }

        private void Save_Click(object? sender, RoutedEventArgs e)
        {
            _device.Name = NameBox.Text ?? string.Empty;
            _device.IpAddresses = (IpBox.Text ?? string.Empty)
                .Split(new[] { Environment.NewLine, "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            Close(true);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
