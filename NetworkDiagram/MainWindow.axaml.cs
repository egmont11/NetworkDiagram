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
using Avalonia.Threading;
using Avalonia.VisualTree;
using NetworkDiagram.Models;
using Avalonia.Controls.Templates;

namespace NetworkDiagram
{
    public partial class MainWindow : Window
    {
        private List<DeviceTemplate> _deviceTemplates = [];
        private Diagram _currentDiagram = new();
        private bool _langComboReady;

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
            LangCombo.SelectedIndex = 0;
            _langComboReady = true;
            LoadTemplates();
            this.Opened += (s, e) => {
                if (DiagramCanvas.RenderTransform is TransformGroup group)
                {
                    CanvasScale = group.Children.OfType<ScaleTransform>().First();
                    CanvasTranslate = group.Children.OfType<TranslateTransform>().First();
                }
                CenterView();
            };

            AddHandler(DragDrop.DragOverEvent, Viewport_DragOver);
            AddHandler(DragDrop.DropEvent, Viewport_Drop);

            // Use Tunneling for drag start to avoid ListBoxItem selection interference
            ToolboxList.AddHandler(PointerPressedEvent, Toolbox_PointerPressed, RoutingStrategies.Tunnel);
        }

        // The logical center of the 40000x40000 canvas
        private const double CanvasCenterX = 20000;
        private const double CanvasCenterY = 20000;

        private void CenterView()
        {
            if (CanvasTranslate == null || CanvasScale == null) return;
            CanvasScale.ScaleX = 1.0;
            CanvasScale.ScaleY = 1.0;
            // Změna na 0,0 aby levý horní roh plátna odpovídal levému hornímu rohu viewportu
            CanvasTranslate.X = 0;
            CanvasTranslate.Y = 0;
        }

        #region Panning and Zooming
        private void Viewport_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var properties = e.GetCurrentPoint(CanvasViewport).Properties;
            if (properties.IsMiddleButtonPressed || properties.IsRightButtonPressed)
            {
                _isPanning = true;
                _lastPanPoint = e.GetPosition(CanvasViewport);
                e.Pointer.Capture(CanvasViewport);
                e.Handled = true;
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
            var props = e.GetCurrentPoint(CanvasViewport).Properties;
            if (!props.IsMiddleButtonPressed && !props.IsRightButtonPressed)
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
            if (newScale < 0.05 || newScale > 10) return;

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
                _deviceTemplates = JsonSerializer.Deserialize<List<DeviceTemplate>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                ToolboxList.ItemsSource = _deviceTemplates;
            }
            catch (Exception ex) { Console.WriteLine($"Error loading templates: {ex.Message}"); }
        }

        private void LangCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_langComboReady) return;
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
        private Point _toolboxDragStart;
        private DeviceTemplate? _toolboxPendingTemplate;

        private void Toolbox_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var item = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>();
            if (item?.Content is DeviceTemplate template)
            {
                _toolboxDragStart = e.GetPosition(null);
                _toolboxPendingTemplate = template;
            }
        }

        private async void Toolbox_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_toolboxPendingTemplate != null && e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            {
                var pos = e.GetPosition(null);
                var diff = _toolboxDragStart - pos;
                if (Math.Abs(diff.X) > 5 || Math.Abs(diff.Y) > 5)
                {
                    var template = _toolboxPendingTemplate;
                    _toolboxPendingTemplate = null;

                    var data = new DataObject();
                    data.Set("DeviceTemplate", template);
                    await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
                }
            }
        }

        private void Viewport_DragOver(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains("DeviceTemplate"))
                e.DragEffects = DragDropEffects.Copy;
            else
                e.DragEffects = DragDropEffects.None;
        }

        private void Viewport_Drop(object? sender, DragEventArgs e)
        {
            if (e.Data.Get("DeviceTemplate") is DeviceTemplate template)
            {
                var dropPoint = e.GetPosition(DiagramCanvas);
                AddDeviceToCanvas(template, dropPoint.X, dropPoint.Y);
            }
        }

        private void DiagramCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(DiagramCanvas).Properties;
            if (e.Source == DiagramCanvas && props.IsLeftButtonPressed)
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
        #endregion

        private Point GetVisibleCanvasCenter()
        {
            if (CanvasTranslate == null || CanvasScale == null) return new Point(CanvasCenterX, CanvasCenterY);
            double vw = CanvasViewport.Bounds.Width > 0 ? CanvasViewport.Bounds.Width : 1000;
            double vh = CanvasViewport.Bounds.Height > 0 ? CanvasViewport.Bounds.Height : 700;
            return new Point(
                (vw / 2 - CanvasTranslate.X) / CanvasScale.ScaleX,
                (vh / 2 - CanvasTranslate.Y) / CanvasScale.ScaleY
            );
        }

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
                Padding = new Thickness(12),
                Background = Brushes.White,
                BorderBrush = (IBrush)Application.Current!.FindResource("GridBlue")!,
                BorderThickness = new Thickness(1),
                MinWidth = 100,
                MinHeight = 80,
                MaxWidth = 180,
                Tag = device,
                ZIndex = 10,
                DataContext = device
            };

            var stack = new StackPanel { Spacing = 4 };

            var image = new Image { Width = 64, Height = 64, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            image.Bind(Image.SourceProperty, new Avalonia.Data.Binding("IconPath") { Converter = ImageConverter.Instance });
            stack.Children.Add(image);

            var primaryBlue = Application.Current!.FindResource("PrimaryBlue");
            var nameText = new TextBlock { FontWeight = FontWeight.Bold, FontSize = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            if (primaryBlue is IBrush brush) nameText.Foreground = brush;
            nameText.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding("Name"));
            stack.Children.Add(nameText);

            var ips = new ItemsControl();
            ips.Bind(ItemsControl.ItemsSourceProperty, new Avalonia.Data.Binding("IpAddresses"));

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
                b.Background = new SolidColorBrush(Color.FromArgb(50, 0, 120, 215));
                b.BorderThickness = new Thickness(2);
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
                b.BorderBrush = (IBrush)Application.Current!.FindResource("GridBlue")!;
                b.Background = Brushes.White;
                b.BorderThickness = new Thickness(1);
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
                    var conn = new Connection { StartDevice = _firstDeviceForConnection, EndDevice = device, Type = _activeTool!.Value };
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
                if (!_deviceElements.TryGetValue(conn.StartDevice, out var startEl) || !_deviceElements.TryGetValue(conn.EndDevice, out var endEl)) return;
                double sw = startEl.Bounds.Width > 0 ? startEl.Bounds.Width : 120;
                double sh = startEl.Bounds.Height > 0 ? startEl.Bounds.Height : 100;
                double ew = endEl.Bounds.Width > 0 ? endEl.Bounds.Width : 120;
                double eh = endEl.Bounds.Height > 0 ? endEl.Bounds.Height : 100;

                line.StartPoint = new Point(conn.StartDevice.X + sw / 2, conn.StartDevice.Y + sh / 2);
                line.EndPoint = new Point(conn.EndDevice.X + ew / 2, conn.EndDevice.Y + eh / 2);
            }

            UpdatePos();
            conn.StartDevice.PropertyChanged += (s, e) => { if (e.PropertyName == "X" || e.PropertyName == "Y") UpdatePos(); };
            conn.EndDevice.PropertyChanged += (s, e) => { if (e.PropertyName == "X" || e.PropertyName == "Y") UpdatePos(); };

            // Re-calculate line endpoints once device bounds are known after first layout pass
            // Unsubscribe immediately after first call to avoid repeated triggers
            if (_deviceElements.TryGetValue(conn.StartDevice, out var startElem))
            {
                EventHandler? handler = null;
                handler = (s, e) => { startElem.LayoutUpdated -= handler; UpdatePos(); };
                startElem.LayoutUpdated += handler;
            }
            if (_deviceElements.TryGetValue(conn.EndDevice, out var endElem) && endElem != startElem)
            {
                EventHandler? handler = null;
                handler = (s, e) => { endElem.LayoutUpdated -= handler; UpdatePos(); };
                endElem.LayoutUpdated += handler;
            }

            line.PointerPressed += (s, e) => {
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) ClearSelection();
                SelectElement(line);
                e.Handled = true;
            };

            _connectionLines[conn] = line;
            DiagramCanvas.Children.Insert(0, line);
        }

        private async void EditDevice(PlacedDevice device)
        {
            var dialog = new EditDeviceWindow(device);
            await dialog.ShowDialog(this);

            if (_deviceElements.TryGetValue(device, out var elem))
            {
                var attached = _currentDiagram.Connections.Where(c => c.StartDevice == device || c.EndDevice == device).ToList();
                foreach(var c in attached) if (_connectionLines.TryGetValue(c, out var l))
                {
                    double sw = _deviceElements[c.StartDevice].Bounds.Width > 0 ? _deviceElements[c.StartDevice].Bounds.Width : 120;
                    double sh = _deviceElements[c.StartDevice].Bounds.Height > 0 ? _deviceElements[c.StartDevice].Bounds.Height : 100;
                    double ew = _deviceElements[c.EndDevice].Bounds.Width > 0 ? _deviceElements[c.EndDevice].Bounds.Width : 120;
                    double eh = _deviceElements[c.EndDevice].Bounds.Height > 0 ? _deviceElements[c.EndDevice].Bounds.Height : 100;
                    l.StartPoint = new Point(c.StartDevice.X + sw / 2, c.StartDevice.Y + sh / 2);
                    l.EndPoint = new Point(c.EndDevice.X + ew / 2, c.EndDevice.Y + eh / 2);
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
            else if (e.Key == Key.Home)
            {
                if (_currentDiagram.Devices.Count > 0) CenterOnContent();
                else CenterView();
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

        private void CenterOnContent()
        {
            if (CanvasTranslate == null || CanvasScale == null || _currentDiagram.Devices.Count == 0) return;

            var devices = _currentDiagram.Devices;
            double minX = devices.Min(d => d.X);
            double minY = devices.Min(d => d.Y);
            double maxX = devices.Max(d => d.X) + 160;
            double maxY = devices.Max(d => d.Y) + 120;

            double contentWidth  = maxX - minX;
            double contentHeight = maxY - minY;
            double contentCenterX = (minX + maxX) / 2.0;
            double contentCenterY = (minY + maxY) / 2.0;

            double vw = CanvasViewport.Bounds.Width  > 0 ? CanvasViewport.Bounds.Width  : (this.Bounds.Width  > 240 ? this.Bounds.Width  - 240 : 860);
            double vh = CanvasViewport.Bounds.Height > 0 ? CanvasViewport.Bounds.Height : (this.Bounds.Height >  60 ? this.Bounds.Height -  60 : 600);

            // Zoom-to-fit: scale content to fill 85% of viewport, capped between 0.05 and 2.0
            double scaleToFit = Math.Min((vw * 0.85) / contentWidth, (vh * 0.85) / contentHeight);
            double newScale = Math.Clamp(scaleToFit, 0.05, 2.0);

            CanvasScale.ScaleX = newScale;
            CanvasScale.ScaleY = newScale;

            CanvasTranslate.X = vw / 2.0 - contentCenterX * newScale;
            CanvasTranslate.Y = vh / 2.0 - contentCenterY * newScale;
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
                try {
                    using var stream = await files[0].OpenReadAsync();
                    var model = await JsonSerializer.DeserializeAsync<DiagramSaveModel>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (model == null || model.Devices == null) return;

                    // Clear canvas without calling CenterView (we'll center on content after load)
                    _currentDiagram = new Diagram();
                    DiagramCanvas.Children.Clear();
                    DiagramCanvas.Children.Add(SelectionRect);
                    _connectionLines.Clear();
                    _deviceElements.Clear();
                    ClearSelection();

                    // Load devices exactly as saved — no normalization
                    _currentDiagram = new Diagram { Devices = model.Devices };

                    foreach (var device in _currentDiagram.Devices)
                    {
                        var template = _deviceTemplates.FirstOrDefault(t => t.Name == device.TemplateName);
                        if (template != null) device.IconPath = template.IconPath;
                        else if (device.TemplateName == "Note") device.IconPath = "";
                        RenderDevice(device);
                    }

                    if (model.Connections != null)
                    {
                        foreach (var cModel in model.Connections)
                        {
                            if (cModel.StartIndex >= 0 && cModel.StartIndex < _currentDiagram.Devices.Count &&
                                cModel.EndIndex >= 0 && cModel.EndIndex < _currentDiagram.Devices.Count)
                            {
                                var conn = new Connection {
                                    StartDevice = _currentDiagram.Devices[cModel.StartIndex],
                                    EndDevice = _currentDiagram.Devices[cModel.EndIndex],
                                    Type = cModel.Type
                                };
                                _currentDiagram.Connections.Add(conn);
                                RenderConnection(conn);
                            }
                        }
                    }

                    // Wait for Avalonia to measure/arrange the newly added controls
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                } catch (Exception ex) {
                    Console.WriteLine("Load failed: " + ex.Message);
                }
            }
        }

        private void WireTool_Click(object? sender, RoutedEventArgs e) => _activeTool = ConnectionType.Wire;
        private void WifiTool_Click(object? sender, RoutedEventArgs e) => _activeTool = ConnectionType.Wifi;
        private void AddText_Click(object? sender, RoutedEventArgs e)
        {
            var center = GetVisibleCanvasCenter();
            AddDeviceToCanvas(new DeviceTemplate { Name = "Note", IconPath = "" }, center.X - 60, center.Y - 40);
        }

        private async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            if (_currentDiagram.Devices.Count == 0) return;

            var prompt = new ExportOptionsWindow();
            var result = await prompt.ShowDialog<bool?>(this);
            if (result == null) return;

            var topLevel = GetTopLevel(this);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export PNG",
                FileTypeChoices = new[] { new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } } }
            });

            if (file != null)
            {
                double minX = _currentDiagram.Devices.Min(d => d.X) - 50;
                double minY = _currentDiagram.Devices.Min(d => d.Y) - 50;
                double maxX = _currentDiagram.Devices.Max(d => d.X) + 210;
                double maxY = _currentDiagram.Devices.Max(d => d.Y) + 160;

                int width = (int)(maxX - minX);
                int height = (int)(maxY - minY);
                if (width <= 0 || height <= 0) return;

                // Create a temporary canvas for rendering to avoid live UI transform issues
                var tempCanvas = new Canvas {
                    Width = width,
                    Height = height,
                    Background = result.Value ? Brushes.White : Brushes.Transparent
                };

                // Create a container to host temp canvas for rendering
                var container = new Panel();
                container.Children.Add(tempCanvas);

                var rtb = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));

                // 1. Draw connections
                foreach (var conn in _currentDiagram.Connections)
                {
                    if (_connectionLines.TryGetValue(conn, out var line))
                    {
                        var tempLine = new Line {
                            Stroke = line.Stroke,
                            StrokeThickness = line.StrokeThickness,
                            StrokeDashArray = line.StrokeDashArray,
                            StartPoint = new Point(line.StartPoint.X - minX, line.StartPoint.Y - minY),
                            EndPoint = new Point(line.EndPoint.X - minX, line.EndPoint.Y - minY)
                        };
                        tempCanvas.Children.Add(tempLine);
                    }
                }

                // 2. Draw devices
                foreach (var device in _currentDiagram.Devices)
                {
                    if (_deviceElements.TryGetValue(device, out var original))
                    {
                        // Create a clone-like visual
                        var clone = new Border {
                            CornerRadius = new CornerRadius(8),
                            Padding = new Thickness(12),
                            Background = Brushes.White,
                            BorderBrush = (IBrush)Application.Current!.FindResource("GridBlue")!,
                            BorderThickness = new Thickness(1),
                            Width = original.Bounds.Width > 0 ? original.Bounds.Width : 120,
                            Height = original.Bounds.Height > 0 ? original.Bounds.Height : 100,
                            DataContext = device
                        };

                        var stack = new StackPanel { Spacing = 4 };
                        var img = new Image { Width = 64, Height = 64 };
                        img.Bind(Image.SourceProperty, new Avalonia.Data.Binding("IconPath") { Converter = ImageConverter.Instance });

                        var txt = new TextBlock {
                            Text = device.Name,
                            FontWeight = FontWeight.Bold,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            Foreground = (IBrush)Application.Current!.FindResource("PrimaryBlue")!
                        };

                        stack.Children.Add(img);
                        stack.Children.Add(txt);

                        var ips = new ItemsControl { ItemsSource = device.IpAddresses };
                        ips.ItemTemplate = new FuncDataTemplate<string>((val, _) => new TextBlock {
                            Text = val, FontSize = 10, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            Foreground = (IBrush)Application.Current!.FindResource("AccentBlue")!
                        });
                        stack.Children.Add(ips);

                        clone.Child = stack;
                        Canvas.SetLeft(clone, device.X - minX);
                        Canvas.SetTop(clone, device.Y - minY);
                        tempCanvas.Children.Add(clone);
                    }
                }

                // Important: Ensure layout of temp visual
                container.Measure(new Size(width, height));
                container.Arrange(new Rect(0, 0, width, height));

                await Task.Delay(100);
                rtb.Render(container);

                using var stream = await file.OpenWriteAsync();
                rtb.Save(stream);
            }
        }
        #endregion
    }
}
