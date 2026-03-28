using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace NetworkDiagram.Models
{
    public enum ConnectionType
    {
        Wire,
        Wifi
    }

    public class DeviceTemplate
    {
        public string Name { get; init; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;
    }

    public class PlacedDevice : INotifyPropertyChanged
    {
        public string Name
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
            }
        } = string.Empty;

        public string TemplateName { get; set; } = string.Empty;

        [JsonIgnore]
        public string IconPath { get; set; } = string.Empty;

        public double X
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CenterX));
            }
        }

        public double Y
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CenterY));
            }
        }

        public List<string> IpAddresses
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
            }
        } = [];

        public double CenterX => X + 50; 
        public double CenterY => Y + 40;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class Connection
    {
        public PlacedDevice StartDevice { get; set; } = null!;
        public PlacedDevice EndDevice { get; set; } = null!;
        public ConnectionType Type { get; set; }
    }

    public class Diagram
    {
        public List<PlacedDevice> Devices { get; set; } = [];
        public List<Connection> Connections { get; set; } = [];
    }
}
