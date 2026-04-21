using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using NetworkDiagram.Models;
using Avalonia.Controls.Templates;

namespace NetworkDiagram
{
    public partial class MainWindow : Window
    {
        private List<DeviceTemplate> _deviceTemplates = [];
        private Diagram _currentDiagram = new();
        
        // Selection & Dragging
        private readonly HashSet<Control> _selectedElements = [];
        private Point _dragStartPoint;
        private bool _isDraggingDevices;
        private bool _isSelectingArea;
        private Point _selectionStartPoint;

        private ConnectionType? _activeTool;
        private PlacedDevice? _firstDeviceForConnection;

        private readonly Dictionary<Connection, Line> _connectionLines = new();
        private readonly Dictionary<PlacedDevice, Control> _deviceElements = new();

        // Panning and Zooming
        private Point _lastPanPoint;
        private bool _isPanning;

        private TranslateTransform CanvasTranslate = null!;
        private ScaleTransform CanvasScale = null!;

        public MainWindow()
        {
            InitializeComponent();
            LoadTemplates();
            this.Opened += (s, e) => {
                if (DiagramCanvas.RenderTransform is TransformGroup group)
                {
                    CanvasScale = group.Children.OfType<ScaleTransform>().First();
                    CanvasTranslate = group.Children.OfType<TranslateTransform>().First();
                }
                CenterView();
            };
            
            AddHandler(DragDrop.DragOverEvent, DiagramCanvas_DragOver);
            AddHandler(DragDrop.DropEvent, DiagramCanvas_Drop);
        }

        private void CenterView()
        {
            if (CanvasTranslate == null || CanvasScale == null) return;
            CanvasTranslate.X = -20000 + (CanvasViewport.Bounds.Width / 2);
            CanvasTranslate.Y = -20000 + (CanvasViewport.Bounds.Height / 2);
            CanvasScale.ScaleX = 1.0;
            CanvasScale.ScaleY = 1.0;
        }

        #region Panning and Zooming
        private void Viewport_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var properties = e.GetCurrentPoint(CanvasViewport).Properties;
            if (properties.IsMiddleButtonPressed)
            {
                _isPanning = true;
                _lastPanPoint = e.GetPosition(CanvasViewport);
                e.Pointer.Capture(CanvasViewport);
            }
        }

        private void Viewport_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isPanning)
            {
                Point currentPoint = e.GetPosition(CanvasViewport);
                double deltaX = currentPoint.X - _lastPanPoint.X;
                double deltaY = currentPoint.Y - _lastPanPoint.Y;

                CanvasTranslate.X += deltaX;
                CanvasTranslate.Y += deltaY;

                _lastPanPoint = currentPoint;
            }
        }

        private void Viewport_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == MouseButton.Middle)
            {
                _isPanning = false;
                e.Pointer.Capture(null);
            }
        }

        private void Viewport_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (CanvasScale == null || CanvasTranslate == null) return;
            double zoom = e.Delta.Y > 0 ? 1.1 : 0.9;
            
            double newScale = CanvasScale.ScaleX * zoom;
            if (newScale < 0.1 || newScale > 5) return;

            Point mousePos = e.GetPosition(DiagramCanvas);

            CanvasScale.ScaleX = newScale;
            CanvasScale.ScaleY = newScale;

            Point newMousePos = e.GetPosition(DiagramCanvas);
            CanvasTranslate.X += (newMousePos.X - mousePos.X) * CanvasScale.ScaleX;
            CanvasTranslate.Y += (newMousePos.Y - mousePos.Y) * CanvasScale.ScaleY;
        }
        #endregion

        private void LoadTemplates()
        {
            try
            {
                if (!File.Exists("devices.json")) return;
                
                var json = File.ReadAllText("devices.json");
                _deviceTemplates = JsonSerializer.Deserialize<List<DeviceTemplate>>(json) ?? [];
                
                ToolboxList.ItemsSource = _deviceTemplates;
            }
            catch (Exception ex) { Console.WriteLine($"Error loading templates: {ex.Message}"); }
        }

        private void LangCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (LangCombo.SelectedItem is ComboBoxItem { Tag: string lang })
            {
                var app = Application.Current;
                if (app == null) return;

                var mergedDicts = app.Resources.MergedDictionaries;
                var oldDict = mergedDicts.OfType<ResourceInclude>().FirstOrDefault(ri => ri.Source?.OriginalString.Contains("Localization/Strings.") == true);
                if (oldDict != null) mergedDicts.Remove(oldDict);
                
                mergedDicts.Add(new ResourceInclude(new Uri("avares://NetworkDiagram/App.axaml")) 
                { 
                    Source = new Uri($"avares://NetworkDiagram/Localization/Strings.{lang}.axaml") 
                });
            }
        }

        #region Drag and Drop (Toolbox to Canvas)
        private async void Toolbox_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var item = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>();
            if (item?.Content is DeviceTemplate template)
            {
                var data = new DataObject();
                data.Set("DeviceTemplate", template);
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
            }
        }

        private void DiagramCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Source == DiagramCanvas)
            {
                ClearSelection();
                _isSelectingArea = true;
                _selectionStartPoint = e.GetPosition(DiagramCanvas);
                
                Canvas.SetLeft(SelectionRect, _selectionStartPoint.X);
                Canvas.SetTop(SelectionRect, _selectionStartPoint.Y);
                SelectionRect.Width = 0;
                SelectionRect.Height = 0;
                SelectionRect.IsVisible = true;
                e.Pointer.Capture(DiagramCanvas);
            }
        }

        private void DiagramCanvas_PointerMoved(object? sender, PointerEventArgs e)
        {
            var currentPoint = e.GetPosition(DiagramCanvas);

            if (_isSelectingArea)
            {
                var x = Math.Min(_selectionStartPoint.X, currentPoint.X);
                var y = Math.Min(_selectionStartPoint.Y, currentPoint.Y);
                var w = Math.Abs(_selectionStartPoint.X - currentPoint.X);
                var h = Math.Abs(_selectionStartPoint.Y - currentPoint.Y);

                Canvas.SetLeft(SelectionRect, x);
                Canvas.SetTop(SelectionRect, y);
                SelectionRect.Width = w;
                SelectionRect.Height = h;

                var selectionBounds = new Rect(x, y, w, h);
                foreach (var entry in _deviceElements)
                {
                    var border = entry.Value;
                    var elementBounds = new Rect(Canvas.GetLeft(border), Canvas.GetTop(border), border.Bounds.Width, border.Bounds.Height);
                    if (selectionBounds.Intersects(elementBounds))
                    {
                        if (!_selectedElements.Contains(border)) SelectElement(border);
                    }
                    else
                    {
                        if (_selectedElements.Contains(border)) DeselectElement(border);
                    }
                }
            }
            else if (_isDraggingDevices)
            {
                var deltaX = currentPoint.X - _dragStartPoint.X;
                var deltaY = currentPoint.Y - _dragStartPoint.Y;

                foreach (var element in _selectedElements)
                {
                    if (element.Tag is PlacedDevice device)
                    {
                        device.X += deltaX;
                        device.Y += deltaY;
                    }
                }
                _dragStartPoint = currentPoint;
            }
        }

        private void DiagramCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            StopDragging();
            e.Pointer.Capture(null);
        }

        // Implementation of DragDrop for Canvas
        private void DiagramCanvas_DragOver(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains("DeviceTemplate"))
                e.DragEffects = DragDropEffects.Copy;
            else
                e.DragEffects = DragDropEffects.None;
        }

        private void DiagramCanvas_Drop(object? sender, DragEventArgs e)
        {
            if (e.Data.Get("DeviceTemplate") is DeviceTemplate template)
            {
                var dropPoint = e.GetPosition(DiagramCanvas);
                AddDeviceToCanvas(template, dropPoint.X, dropPoint.Y);
            }
        }
        #endregion

        private void AddDeviceToCanvas(DeviceTemplate template, double x, double y)
        {
            var placed = new PlacedDevice { Name = template.Name, TemplateName = template.Name, IconPath = template.IconPath, X = x, Y = y };
            _currentDiagram.Devices.Add(placed);
            RenderDevice(placed);
        }

        private void RenderDevice(PlacedDevice device)
        {
            var container = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                MaxWidth = 160,
                Tag = device
            };

            var stack = new StackPanel();
            
            var image = new Image { MaxWidth = 64, MaxHeight = 64, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            image.Bind(Image.SourceProperty, new Avalonia.Data.Binding(nameof(device.IconPath)) { Converter = ImageConverter.Instance });
            stack.Children.Add(image);

            var primaryBlue = Application.Current!.FindResource("PrimaryBlue");
            var nameText = new TextBlock { FontWeight = FontWeight.Bold, FontSize = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0,4,0,0) };
            if (primaryBlue is IBrush brush) nameText.Foreground = brush;
            nameText.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(device.Name)));
            stack.Children.Add(nameText);

            var ips = new ItemsControl();
            ips.Bind(ItemsControl.ItemsSourceProperty, new Avalonia.Data.Binding(nameof(device.IpAddresses)));
            
            var accentBlue = Application.Current!.FindResource("AccentBlue");
            ips.ItemTemplate = new FuncDataTemplate<string>((val, _) => {
                var tb = new TextBlock { Text = val, FontSize = 10, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
                if (accentBlue is IBrush b) tb.Foreground = b;
                return tb;
            });
            
            stack.Children.Add(ips);

            container.Child = stack;

            Canvas.SetLeft(container, device.X);
            Canvas.SetTop(container, device.Y);

            device.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PlacedDevice.X)) Canvas.SetLeft(container, device.X);
                if (e.PropertyName == nameof(PlacedDevice.Y)) Canvas.SetTop(container, device.Y);
            };

            container.PointerPressed += (s, e) =>
            {
                if (_activeTool != null)
                {
                    HandleConnectionTool(device, container);
                    e.Handled = true;
                    return;
                }

                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    if (!_selectedElements.Contains(container))
                    {
                        ClearSelection();
                        SelectElement(container);
                    }
                }
                else
                {
                    if (_selectedElements.Contains(container)) DeselectElement(container);
                    else SelectElement(container);
                }

                _isDraggingDevices = true;
                _dragStartPoint = e.GetPosition(DiagramCanvas);
                e.Pointer.Capture(DiagramCanvas);
                e.Handled = true;

                if (e.ClickCount == 2)
                {
                    StopDragging();
                    EditDevice(device);
                }
            };

            _deviceElements[device] = container;
            DiagramCanvas.Children.Add(container);
        }

        private void StopDragging()
        {
            _isDraggingDevices = false;
            _isSelectingArea = false;
            SelectionRect.IsVisible = false;
        }

        private void SelectElement(Control element)
        {
            _selectedElements.Add(element);
            if (element is Border b)
            {
                var accentBlue = Application.Current!.FindResource("AccentBlue");
                if (accentBlue is IBrush brush) b.BorderBrush = brush;
                b.Background = new SolidColorBrush(Color.FromArgb(30, 0, 120, 215));
            }
            else if (element is Line l)
            {
                l.StrokeThickness = 5;
                l.Opacity = 0.8;
            }
        }

        private void DeselectElement(Control element)
        {
            _selectedElements.Remove(element);
            if (element is Border b)
            {
                b.BorderBrush = Brushes.Transparent;
                b.Background = Brushes.Transparent;
            }
            else if (element is Line l)
            {
                l.StrokeThickness = 3;
                l.Opacity = 1.0;
            }
        }

        private void ClearSelection()
        {
            foreach (var el in _selectedElements.ToList()) DeselectElement(el);
        }

        private void HandleConnectionTool(PlacedDevice device, Control element)
        {
            if (_firstDeviceForConnection == null)
            {
                _firstDeviceForConnection = device;
                element.Opacity = 0.5;
            }
            else
            {
                if (_firstDeviceForConnection != device)
                {
                    var conn = new Connection { StartDevice = _firstDeviceForConnection, EndDevice = device, Type = _activeTool.Value };
                    _currentDiagram.Connections.Add(conn);
                    RenderConnection(conn);
                }
                ResetConnectionTool();
            }
        }

        private void ResetConnectionTool()
        {
            _firstDeviceForConnection = null;
            _activeTool = null;
            foreach (var child in _deviceElements.Values) child.Opacity = 1.0;
        }

        private void RenderConnection(Connection conn)
        {
            var line = new Line
            {
                Stroke = conn.Type == ConnectionType.Wifi ? Brushes.DeepSkyBlue : Brushes.SlateGray,
                StrokeThickness = 3,
                Tag = conn,
                ZIndex = 0
            };
            if (conn.Type == ConnectionType.Wifi) line.StrokeDashArray = new AvaloniaList<double>(new[] { 2.0, 2.0 });

            void UpdatePos()
            {
                if (!_deviceElements.TryGetValue(conn.StartDevice, out var s) || !_deviceElements.TryGetValue(conn.EndDevice, out var e)) return;
                line.StartPoint = new Point(conn.StartDevice.X + s.Bounds.Width / 2, conn.StartDevice.Y + s.Bounds.Height / 2);
                line.EndPoint = new Point(conn.EndDevice.X + e.Bounds.Width / 2, conn.EndDevice.Y + e.Bounds.Height / 2);
            }

            UpdatePos();
            conn.StartDevice.PropertyChanged += (s, e) => { if (e.PropertyName == "X" || e.PropertyName == "Y") UpdatePos(); };
            conn.EndDevice.PropertyChanged += (s, e) => { if (e.PropertyName == "X" || e.PropertyName == "Y") UpdatePos(); };

            line.PointerPressed += (s, e) => {
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) ClearSelection();
                SelectElement(line);
                e.Handled = true;
            };

            _connectionLines[conn] = line;
            DiagramCanvas.Children.Insert(0, line);
            
            // Initial position update after layout
            LayoutUpdated += (s, e) => UpdatePos();
        }

        private async void EditDevice(PlacedDevice device)
        {
            var dialog = new EditDeviceWindow(device);
            await dialog.ShowDialog(this);
            
            if (_deviceElements.TryGetValue(device, out var elem))
            {
                // Refresh layout and connections
                var attached = _currentDiagram.Connections.Where(c => c.StartDevice == device || c.EndDevice == device).ToList();
                foreach(var c in attached) if (_connectionLines.TryGetValue(c, out var l)) 
                {
                    l.StartPoint = new Point(c.StartDevice.X + _deviceElements[c.StartDevice].Bounds.Width / 2, c.StartDevice.Y + _deviceElements[c.StartDevice].Bounds.Height / 2);
                    l.EndPoint = new Point(c.EndDevice.X + _deviceElements[c.EndDevice].Bounds.Width / 2, c.EndDevice.Y + _deviceElements[c.EndDevice].Bounds.Height / 2);
                }
            }
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && _selectedElements.Count > 0)
            {
                foreach (var el in _selectedElements.ToList())
                {
                    if (el.Tag is Connection conn && el is Line line)
                    {
                        _currentDiagram.Connections.Remove(conn);
                        _connectionLines.Remove(conn);
                        DiagramCanvas.Children.Remove(line);
                    }
                    else if (el.Tag is PlacedDevice device)
                    {
                        _currentDiagram.Devices.Remove(device);
                        _deviceElements.Remove(device);
                        var toRemove = _currentDiagram.Connections.Where(c => c.StartDevice == device || c.EndDevice == device).ToList();
                        foreach (var c in toRemove)
                        {
                            if (_connectionLines.TryGetValue(c, out var l)) DiagramCanvas.Children.Remove(l);
                            _currentDiagram.Connections.Remove(c);
                            _connectionLines.Remove(c);
                        }
                        DiagramCanvas.Children.Remove(el);
                    }
                }
                _selectedElements.Clear();
            }
            else if (e.Key == Key.Escape)
            {
                ResetConnectionTool(); StopDragging(); ClearSelection();
            }
        }

        #region Toolbar Events
        private void NewDiagram_Click(object? sender, RoutedEventArgs e)
        {
            _currentDiagram = new Diagram();
            DiagramCanvas.Children.Clear();
            DiagramCanvas.Children.Add(SelectionRect);
            _connectionLines.Clear();
            _deviceElements.Clear();
            ClearSelection();
            CenterView();
        }

        private async void SaveDiagram_Click(object? sender, RoutedEventArgs e)
        {
            var topLevel = GetTopLevel(this);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Network Diagram",
                FileTypeChoices = new[] { new FilePickerFileType("Network Diagram") { Patterns = new[] { "*.ndjson" } } }
            });

            if (file != null)
            {
                var model = new DiagramSaveModel {
                    Devices = _currentDiagram.Devices,
                    Connections = _currentDiagram.Connections.Select(c => new ConnectionSaveModel {
                        StartIndex = _currentDiagram.Devices.IndexOf(c.StartDevice),
                        EndIndex = _currentDiagram.Devices.IndexOf(c.EndDevice),
                        Type = c.Type
                    }).ToList()
                };
                
                using var stream = await file.OpenWriteAsync();
                await JsonSerializer.SerializeAsync(stream, model, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        private async void LoadDiagram_Click(object? sender, RoutedEventArgs e)
        {
            var topLevel = GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Network Diagram",
                FileTypeFilter = new[] { new FilePickerFileType("Network Diagram") { Patterns = new[] { "*.ndjson" } } }
            });

            if (files.Count > 0)
            {
                using var stream = await files[0].OpenReadAsync();
                var model = await JsonSerializer.DeserializeAsync<DiagramSaveModel>(stream);
                if (model == null) return;

                NewDiagram_Click(null, new RoutedEventArgs());
                _currentDiagram = new Diagram { Devices = model.Devices };

                foreach (var device in _currentDiagram.Devices)
                {
                    var template = _deviceTemplates.FirstOrDefault(t => t.Name == device.TemplateName);
                    if (template != null) device.IconPath = template.IconPath;
                    RenderDevice(device);
                }

                foreach (var cModel in model.Connections)
                {
                    var conn = new Connection { StartDevice = _currentDiagram.Devices[cModel.StartIndex], EndDevice = _currentDiagram.Devices[cModel.EndIndex], Type = cModel.Type };
                    _currentDiagram.Connections.Add(conn);
                    RenderConnection(conn);
                }
            }
        }

        private void WireTool_Click(object? sender, RoutedEventArgs e) => _activeTool = ConnectionType.Wire;
        private void WifiTool_Click(object? sender, RoutedEventArgs e) => _activeTool = ConnectionType.Wifi;
        private void AddText_Click(object? sender, RoutedEventArgs e)
        {
            if (CanvasTranslate == null || CanvasScale == null) return;
            double centerX = (CanvasViewport.Bounds.Width / 2 - CanvasTranslate.X) / CanvasScale.ScaleX;
            double centerY = (CanvasViewport.Bounds.Height / 2 - CanvasTranslate.Y) / CanvasScale.ScaleY;
            AddDeviceToCanvas(new DeviceTemplate { Name = "Note", IconPath = "" }, centerX, centerY);
        }

        private async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            if (_currentDiagram.Devices.Count == 0) return;

            var topLevel = GetTopLevel(this);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export PNG",
                FileTypeChoices = new[] { new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } } }
            });

            if (file != null)
            {
                var oldTranslate = new Point(CanvasTranslate.X, CanvasTranslate.Y);
                var oldScale = new Point(CanvasScale.ScaleX, CanvasScale.ScaleY);

                double minX = _currentDiagram.Devices.Min(d => d.X) - 20;
                double minY = _currentDiagram.Devices.Min(d => d.Y) - 20;
                double maxX = _currentDiagram.Devices.Max(d => d.X) + 180;
                double maxY = _currentDiagram.Devices.Max(d => d.Y) + 120;

                var pixelSize = new PixelSize((int)(maxX - minX), (int)(maxY - minY));
                var rtb = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
                
                CanvasTranslate.X = -minX;
                CanvasTranslate.Y = -minY;
                CanvasScale.ScaleX = 1.0;
                CanvasScale.ScaleY = 1.0;
                
                await Task.Delay(50);
                rtb.Render(DiagramCanvas);
                
                using var stream = await file.OpenWriteAsync();
                rtb.Save(stream);

                CanvasTranslate.X = oldTranslate.X;
                CanvasTranslate.Y = oldTranslate.Y;
                CanvasScale.ScaleX = oldScale.X;
                CanvasScale.ScaleY = oldScale.Y;
            }
        }
        #endregion
    }
}
