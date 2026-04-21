using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

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
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged();
            }
        }

        public string TemplateName { get; set; } = string.Empty;

        [JsonIgnore]
        public string IconPath { get; set; } = string.Empty;

        private double _x;
        public double X
        {
            get => _x;
            set
            {
                _x = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CenterX));
            }
        }

        private double _y;
        public double Y
        {
            get => _y;
            set
            {
                _y = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CenterY));
            }
        }

        private List<string> _ipAddresses = [];
        public List<string> IpAddresses
        {
            get => _ipAddresses;
            set
            {
                _ipAddresses = value;
                OnPropertyChanged();
            }
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
        public List<PlacedDevice> Devices { get; set; } = [];
        public List<Connection> Connections { get; set; } = [];
    }

    public class ImageConverter : IValueConverter
    {
        public static ImageConverter Instance { get; } = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                try
                {
                    if (path.StartsWith("avares://"))
                        return new Bitmap(AssetLoader.Open(new Uri(path)));
                    
                    if (File.Exists(path))
                        return new Bitmap(path);

                    // Try as relative avares
                    var uri = new Uri($"avares://NetworkDiagram/{path.Replace("\\", "/")}");
                    return new Bitmap(AssetLoader.Open(uri));
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class DiagramSaveModel {
        public List<PlacedDevice> Devices { get; set; } = [];
        public List<ConnectionSaveModel> Connections { get; set; } = [];
    }
    public class ConnectionSaveModel {
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public ConnectionType Type { get; set; }
    }
}
