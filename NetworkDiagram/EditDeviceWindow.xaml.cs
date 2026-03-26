using System;
using System.Linq;
using System.Windows;
using NetworkDiagram.Models;

namespace NetworkDiagram
{
    public partial class EditDeviceWindow : Window
    {
        private PlacedDevice _device;

        public EditDeviceWindow(PlacedDevice device)
        {
            InitializeComponent();
            _device = device;
            NameBox.Text = device.Name;
            IpBox.Text = string.Join(Environment.NewLine, device.IpAddresses);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _device.Name = NameBox.Text;
            _device.IpAddresses = IpBox.Text.Split(new[] { Environment.NewLine, "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries).ToList();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
