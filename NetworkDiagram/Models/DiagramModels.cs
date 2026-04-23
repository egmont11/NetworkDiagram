using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
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
        [JsonPropertyName("Name")]
        public string Name { get; init; } = string.Empty;
        
        [JsonPropertyName("IconPath")]
        public string IconPath { get; set; } = string.Empty;
    }

    public class PlacedDevice : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        
        [JsonPropertyName("Name")]
        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged();
            }
        }

        private string _description = string.Empty;

        [JsonPropertyName("Description")]
        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyName("TemplateName")]
        public string TemplateName { get; set; } = string.Empty;

        [JsonIgnore]
        public string IconPath { get; set; } = string.Empty;

        private double _x;
        [JsonPropertyName("X")]
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
        [JsonPropertyName("Y")]
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
        [JsonPropertyName("IpAddresses")]
        public List<string> IpAddresses
        {
            get => _ipAddresses;
            set
            {
                _ipAddresses = value;
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        public double CenterX => X + 50; 
        [JsonIgnore]
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
            if (value is string path && !string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    if (path.StartsWith("avares://"))
                        return new Bitmap(AssetLoader.Open(new Uri(path)));
                    
                    if (File.Exists(path))
                        return new Bitmap(path);

                    string uriPath = path.Replace("\\", "/");
                    if (!uriPath.StartsWith("Assets/")) uriPath = "Assets/" + uriPath.TrimStart('/');
                    
                    string escapedPath = uriPath.Replace(" ", "%20");
                    var uri = new Uri($"avares://NetworkDiagram/{escapedPath}");
                    
                    return new Bitmap(AssetLoader.Open(uri));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ImageConverter] Error loading '{path}': {ex.Message}");
                    return null;
                }
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class DiagramSaveModel {
        [JsonPropertyName("Devices")]
        public List<PlacedDevice> Devices { get; set; } = [];
        
        [JsonPropertyName("Connections")]
        public List<ConnectionSaveModel> Connections { get; set; } = [];
    }
    
    public class ConnectionSaveModel {
        [JsonPropertyName("StartIndex")]
        public int StartIndex { get; set; }
        
        [JsonPropertyName("EndIndex")]
        public int EndIndex { get; set; }
        
        [JsonPropertyName("Type")]
        public ConnectionType Type { get; set; }
    }
}
