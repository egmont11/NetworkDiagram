using System.Collections.Generic;
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
        public string Name { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;
    }

    public class PlacedDevice : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private List<string> _ipAddresses = new List<string>();
        private string _annotation = string.Empty;
        private double _x;
        private double _y;

        public string Name 
        { 
            get => _name; 
            set { _name = value; OnPropertyChanged(); } 
        }

        public string TemplateName { get; set; } = string.Empty;

        [JsonIgnore]
        public string IconPath { get; set; } = string.Empty;

        public double X 
        { 
            get => _x; 
            set { _x = value; OnPropertyChanged(); OnPropertyChanged(nameof(CenterX)); } 
        }

        public double Y 
        { 
            get => _y; 
            set { _y = value; OnPropertyChanged(); OnPropertyChanged(nameof(CenterY)); } 
        }

        public List<string> IpAddresses 
        { 
            get => _ipAddresses; 
            set { _ipAddresses = value; OnPropertyChanged(); } 
        }

        public string Annotation 
        { 
            get => _annotation; 
            set { _annotation = value; OnPropertyChanged(); } 
        }
        
        public double CenterX => X + 50; 
        public double CenterY => Y + 40;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
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
        public List<PlacedDevice> Devices { get; set; } = new List<PlacedDevice>();
        public List<Connection> Connections { get; set; } = new List<Connection>();
    }
}
