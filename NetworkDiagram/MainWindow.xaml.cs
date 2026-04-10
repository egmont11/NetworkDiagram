using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using NetworkDiagram.Models;
using Path = System.IO.Path;

namespace NetworkDiagram
{
    public partial class MainWindow
    {
        private List<DeviceTemplate> _deviceTemplates = [];
        private Diagram _currentDiagram = new();
        
        // Selection & Dragging
        private readonly HashSet<FrameworkElement> _selectedElements = [];
        private Point _dragStartPoint;
        private bool _isDraggingDevices;
        private bool _isSelectingArea;
        private Point _selectionStartPoint;

        private ConnectionType? _activeTool;
        private PlacedDevice? _firstDeviceForConnection;

        private readonly Dictionary<Connection, Line> _connectionLines = new();
        private readonly Dictionary<PlacedDevice, FrameworkElement> _deviceElements = new();

        // Panning and Zooming
        private Point _lastPanPoint;
        private bool _isPanning;

        public MainWindow()
        {
            InitializeComponent();
            LoadTemplates();
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CenterView();
        }

        private void CenterView()
        {
            // Center logically on (20000, 20000) minus half the viewport size to put 0,0 in the middle
            CanvasTranslate.X = -20000 + (CanvasViewport.ActualWidth / 2);
            CanvasTranslate.Y = -20000 + (CanvasViewport.ActualHeight / 2);
            CanvasScale.ScaleX = 1.0;
            CanvasScale.ScaleY = 1.0;
        }

        #region Panning and Zooming
        private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _isPanning = true;
                _lastPanPoint = e.GetPosition(CanvasViewport);
                CanvasViewport.CaptureMouse();
            }
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
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

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _isPanning = false;
                CanvasViewport.ReleaseMouseCapture();
            }
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoom = e.Delta > 0 ? 1.1 : 0.9;
            
            double newScale = CanvasScale.ScaleX * zoom;
            if (newScale < 0.1 || newScale > 5) return;

            Point mousePos = e.GetPosition(DiagramCanvas);

            // Zoom centered on mouse
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
                
                foreach (var t in _deviceTemplates.Where(t => !string.IsNullOrEmpty(t.IconPath)))
                {
                    t.IconPath = Path.GetFullPath(t.IconPath);
                }
                
                ToolboxList.ItemsSource = _deviceTemplates;
            }
            catch (Exception ex) { MessageBox.Show($"Error loading templates: {ex.Message}"); }
        }

        private static string GetLocalizedString(string key) => Application.Current.Resources[key] as string ?? key;

        private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LangCombo.SelectedItem is not ComboBoxItem { Tag: string lang }) return;
            
            var dict = new ResourceDictionary
            {
                Source = new Uri($"Localization/Strings.{lang}.xaml", UriKind.Relative)
            };

            // Replace the existing localization dictionary
            var oldDict = Application.Current.Resources.MergedDictionaries.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Localization/Strings."));
            if (oldDict != null) Application.Current.Resources.MergedDictionaries.Remove(oldDict);
            Application.Current.Resources.MergedDictionaries.Add(dict);
        }

        #region Drag and Drop (Toolbox to Canvas)
        private void Toolbox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox)
            {
                // Find the item under the mouse instead of relying on SelectedItem
                var element = e.OriginalSource as DependencyObject;
                while (element != null && !(element is ListBoxItem))
                    element = VisualTreeHelper.GetParent(element);

                if (element is ListBoxItem item && item.Content is DeviceTemplate template)
                {
                    DragDrop.DoDragDrop(listBox, template, DragDropEffects.Copy);
                }
            }
        }


        private void DiagramCanvas_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(DeviceTemplate)) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void DiagramCanvas_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(DeviceTemplate))) return;
            
            var template = (DeviceTemplate)e.Data.GetData(typeof(DeviceTemplate));
            var dropPoint = e.GetPosition(DiagramCanvas);
            AddDeviceToCanvas(template, dropPoint.X, dropPoint.Y);
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
            var template = (DataTemplate)DiagramCanvas.Resources["DeviceTemplate"];
            var contentPresenter = new ContentPresenter { Content = device, ContentTemplate = template };
            var container = new Border { 
                Child = contentPresenter, 
                Tag = device,
                Style = (Style)Application.Current.Resources["DeviceContainerStyle"]
            };

            Canvas.SetLeft(container, device.X);
            Canvas.SetTop(container, device.Y);

            device.PropertyChanged += (s, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(PlacedDevice.X):
                        Canvas.SetLeft(container, device.X);
                        break;
                    case nameof(PlacedDevice.Y):
                        Canvas.SetTop(container, device.Y);
                        break;
                }
            };

            container.MouseDown += Device_MouseDown;
            container.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount != 2) return;
                StopDragging();
                EditDevice(device);
                e.Handled = true;
            };

            _deviceElements[device] = container;
            DiagramCanvas.Children.Add(container);
        }

        private void StopDragging()
        {
            _isDraggingDevices = false;
            _isSelectingArea = false;
            SelectionRect.Visibility = Visibility.Collapsed;
            DiagramCanvas.ReleaseMouseCapture();
        }

        private void Device_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: PlacedDevice device } element) return;
            
            if (_activeTool != null)
            {
                HandleConnectionTool(device, element);
                e.Handled = true;
                return;
            }

            // Handle Selection
            if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
            {
                if (!_selectedElements.Contains(element))
                {
                    ClearSelection();
                    SelectElement(element);
                }
            }
            else
            {
                if (_selectedElements.Contains(element)) DeselectElement(element);
                else SelectElement(element);
            }

            // Start Dragging
            _isDraggingDevices = true;
            _dragStartPoint = e.GetPosition(DiagramCanvas);
            DiagramCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void SelectElement(FrameworkElement element)
        {
            _selectedElements.Add(element);
            if (element is Border b)
            {
                b.BorderBrush = (SolidColorBrush)Application.Current.Resources["AccentBlue"];
                b.Background = new SolidColorBrush(Color.FromArgb(30, 0, 120, 215)); // Light blue tint
            }
            else if (element is Line l)
            {
                l.StrokeThickness = 5;
                l.Opacity = 0.8;
            }
        }

        private void DeselectElement(FrameworkElement element)
        {
            _selectedElements.Remove(element);
            switch (element)
            {
                case Border b:
                    b.BorderBrush = Brushes.Transparent;
                    b.Background = Brushes.Transparent;
                    break;
                case Line l:
                    l.StrokeThickness = 3;
                    l.Opacity = 1.0;
                    break;
            }
        }

        private void ClearSelection()
        {
            foreach (var el in _selectedElements.ToList()) DeselectElement(el);
        }

        #region Canvas Interaction (Selection Area & Dragging)
        private void DiagramCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == DiagramCanvas)
            {
                ClearSelection();
                _isSelectingArea = true;
                _selectionStartPoint = e.GetPosition(DiagramCanvas);
                
                Canvas.SetLeft(SelectionRect, _selectionStartPoint.X);
                Canvas.SetTop(SelectionRect, _selectionStartPoint.Y);
                SelectionRect.Width = 0;
                SelectionRect.Height = 0;
                SelectionRect.Visibility = Visibility.Visible;
                DiagramCanvas.CaptureMouse();
            }
        }

        private void DiagramCanvas_MouseMove(object sender, MouseEventArgs e)
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

                // Real-time selection preview
                var selectionBounds = new Rect(x, y, w, h);
                foreach (var border in _deviceElements.Values)
                {
                    var elementBounds = new Rect(Canvas.GetLeft(border), Canvas.GetTop(border), border.ActualWidth, border.ActualHeight);
                    if (selectionBounds.IntersectsWith(elementBounds))
                    {
                        if (!_selectedElements.Contains(border)) SelectElement(border);
                    }
                    else
                    {
                        // Only deselect if we are in the middle of a selection area drag
                        if (_selectedElements.Contains(border)) DeselectElement(border);
                    }
                }
                
                // Also preview for lines
                foreach (var line in _connectionLines.Values)
                {
                    var lineBounds = new Rect(
                        Math.Min(line.X1, line.X2),
                        Math.Min(line.Y1, line.Y2),
                        Math.Abs(line.X1 - line.X2),
                        Math.Abs(line.Y1 - line.Y2));
                    
                    if (selectionBounds.IntersectsWith(lineBounds))
                    {
                        if (!_selectedElements.Contains(line)) SelectElement(line);
                    }
                    else
                    {
                        if (_selectedElements.Contains(line)) DeselectElement(line);
                    }
                }
            }
            else if (_isDraggingDevices && e.LeftButton == MouseButtonState.Pressed)
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

        private void DiagramCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSelectingArea)
            {
                var selectionBounds = new Rect(
                    Canvas.GetLeft(SelectionRect),
                    Canvas.GetTop(SelectionRect),
                    SelectionRect.Width,
                    SelectionRect.Height);

                foreach (var border in _deviceElements.Values)
                {
                    var elementBounds = new Rect(Canvas.GetLeft(border), Canvas.GetTop(border), border.ActualWidth, border.ActualHeight);
                    if (selectionBounds.IntersectsWith(elementBounds)) SelectElement(border);
                }
            }
            StopDragging();
        }
        #endregion

        private void HandleConnectionTool(PlacedDevice device, FrameworkElement element)
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
            foreach (var child in DiagramCanvas.Children.OfType<Border>()) child.Opacity = 1.0;
        }

        private void RenderConnection(Connection conn)
        {
            var line = new Line
            {
                Stroke = conn.Type == ConnectionType.Wifi ? Brushes.DeepSkyBlue : Brushes.SlateGray,
                StrokeThickness = 3,
                Tag = conn
            };
            if (conn.Type == ConnectionType.Wifi) line.StrokeDashArray = [2, 2];

            UpdateLinePosition(conn, line);
            
            conn.StartDevice.PropertyChanged += (s, e) => { if (e.PropertyName == "X" || e.PropertyName == "Y") UpdateLinePosition(conn, line); };
            conn.EndDevice.PropertyChanged += (s, e) => { if (e.PropertyName == "X" || e.PropertyName == "Y") UpdateLinePosition(conn, line); };

            line.MouseDown += (s, e) => {
                if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl)) ClearSelection();
                SelectElement(line);
                e.Handled = true;
            };

            _connectionLines[conn] = line;
            DiagramCanvas.Children.Insert(0, line);
        }

        private void UpdateLinePosition(Connection conn, Line line)
        {
            if (!_deviceElements.TryGetValue(conn.StartDevice, out var startElem) ||
                !_deviceElements.TryGetValue(conn.EndDevice, out var endElem)) return;
            
            if (startElem.ActualWidth == 0 || startElem.ActualHeight == 0) startElem.UpdateLayout();
            if (endElem.ActualWidth == 0 || endElem.ActualHeight == 0) endElem.UpdateLayout();

            line.X1 = conn.StartDevice.X + (startElem.ActualWidth / 2);
            line.Y1 = conn.StartDevice.Y + (startElem.ActualHeight / 2);
            line.X2 = conn.EndDevice.X + (endElem.ActualWidth / 2);
            line.Y2 = conn.EndDevice.Y + (endElem.ActualHeight / 2);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Delete when _selectedElements.Count > 0:
                {
                    foreach (var el in _selectedElements.ToList())
                    {
                        switch (el)
                        {
                            case Line line when el.Tag is Connection conn:
                                _currentDiagram.Connections.Remove(conn);
                                _connectionLines.Remove(conn);
                                DiagramCanvas.Children.Remove(line);
                                break;
                            case Border { Tag: PlacedDevice device } border:
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
                                DiagramCanvas.Children.Remove(border);
                                break;
                            }
                        }
                    }
                    _selectedElements.Clear();
                    break;
                }
                case Key.Escape:
                    ResetConnectionTool(); StopDragging(); ClearSelection();
                    break;
            }
        }

        private void EditDevice(PlacedDevice device)
        {
            var dialog = new EditDeviceWindow(device);
            dialog.ShowDialog();
            
            if (!_deviceElements.TryGetValue(device, out var elem)) return;
            
            elem.UpdateLayout();
            var attached = _currentDiagram.Connections.Where(c => c.StartDevice == device || c.EndDevice == device).ToList();
            foreach(var c in attached) if (_connectionLines.TryGetValue(c, out var l)) UpdateLinePosition(c, l);
        }

        #region Toolbar Events
        private void NewDiagram_Click(object sender, RoutedEventArgs e)
        {
            _currentDiagram = new Diagram();
            DiagramCanvas.Children.Clear();
            // Re-add selection rectangle after clearing
            if (SelectionRect != null) DiagramCanvas.Children.Add(SelectionRect);
            
            _connectionLines.Clear();
            _deviceElements.Clear();
            ClearSelection();
            CenterView();
        }

        private void SaveDiagram_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog { Filter = "Network Diagram (*.ndjson)|*.ndjson" };
            if (sfd.ShowDialog() != true) return;
            
            var model = new DiagramSaveModel {
                Devices = _currentDiagram.Devices,
                Connections = _currentDiagram.Connections.Select(c => new ConnectionSaveModel {
                    StartIndex = _currentDiagram.Devices.IndexOf(c.StartDevice),
                    EndIndex = _currentDiagram.Devices.IndexOf(c.EndDevice),
                    Type = c.Type
                }).ToList()
            };
            
            var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sfd.FileName, json);
        }

        private void LoadDiagram_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog { Filter = "Network Diagram (*.ndjson)|*.ndjson" };
            if (ofd.ShowDialog() != true) return;
            
            var json = File.ReadAllText(ofd.FileName);
            var model = JsonSerializer.Deserialize<DiagramSaveModel>(json);
            if (model == null) return;
            _currentDiagram = new Diagram { Devices = model.Devices };

            foreach (var device in _currentDiagram.Devices)
            {
                var template = _deviceTemplates.FirstOrDefault(t => t.Name == device.TemplateName);
                if (template != null) device.IconPath = template.IconPath;
            }

            DiagramCanvas.Children.Clear();
            // Re-add selection rectangle after clearing
            if (SelectionRect != null) DiagramCanvas.Children.Add(SelectionRect);
            
            _connectionLines.Clear();
            _deviceElements.Clear();
            ClearSelection();

            foreach (var device in _currentDiagram.Devices) RenderDevice(device);
            foreach (var conn in model.Connections.Select(cModel => new Connection { StartDevice = _currentDiagram.Devices[cModel.StartIndex], EndDevice = _currentDiagram.Devices[cModel.EndIndex], Type = cModel.Type }))
            {
                _currentDiagram.Connections.Add(conn);
                RenderConnection(conn);
            }

            // Center on loaded content
            if (_currentDiagram.Devices.Count > 0)
            {
                double minX = _currentDiagram.Devices.Min(d => d.X);
                double minY = _currentDiagram.Devices.Min(d => d.Y);
                double maxX = _currentDiagram.Devices.Max(d => d.X);
                double maxY = _currentDiagram.Devices.Max(d => d.Y);

                CanvasTranslate.X = -((minX + maxX) / 2) * CanvasScale.ScaleX + (CanvasViewport.ActualWidth / 2);
                CanvasTranslate.Y = -((minY + maxY) / 2) * CanvasScale.ScaleY + (CanvasViewport.ActualHeight / 2);
            }
            else
            {
                CenterView();
            }
        }

        private void WireTool_Click(object sender, RoutedEventArgs e) => _activeTool = ConnectionType.Wire;
        private void WifiTool_Click(object sender, RoutedEventArgs e) => _activeTool = ConnectionType.Wifi;
        private void AddText_Click(object sender, RoutedEventArgs e)
        {
            // Center of the current viewport in canvas coordinates
            double centerX = (CanvasViewport.ActualWidth / 2 - CanvasTranslate.X) / CanvasScale.ScaleX;
            double centerY = (CanvasViewport.ActualHeight / 2 - CanvasTranslate.Y) / CanvasScale.ScaleY;
            
            AddDeviceToCanvas(new DeviceTemplate { Name = "Note", IconPath = "" }, centerX, centerY);
        }

        private void ExportPng_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDiagram.Devices.Count == 0)
            {
                MessageBox.Show(GetLocalizedString("EmptyDiagramMsg"));
                return;
            }

            var sfd = new SaveFileDialog { Filter = "PNG Image (*.png)|*.png", Title = GetLocalizedString("ExportOptionsTitle") };
            if (sfd.ShowDialog() != true) return;

            var result = MessageBox.Show(GetLocalizedString("ExportWhiteBgMsg"), GetLocalizedString("ExportOptionsTitle"), MessageBoxButton.YesNoCancel);
            if (result == MessageBoxResult.Cancel) return;

            var whiteBg = result == MessageBoxResult.Yes;

            // 1. Find content bounds
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var border in _deviceElements.Values)
            {
                var x = Canvas.GetLeft(border);
                var y = Canvas.GetTop(border);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x + border.ActualWidth);
                maxY = Math.Max(maxY, y + border.ActualHeight);
            }
            foreach (var line in _connectionLines.Values)
            {
                minX = Math.Min(minX, Math.Min(line.X1, line.X2));
                minY = Math.Min(minY, Math.Min(line.Y1, line.Y2));
                maxX = Math.Max(maxX, Math.Max(line.X1, line.X2));
                maxY = Math.Max(maxY, Math.Max(line.Y1, line.Y2));
            }

            const double margin = 20;
            minX -= margin; minY -= margin; maxX += margin; maxY += margin;
            var width = Math.Max(1, maxX - minX);
            var height = Math.Max(1, maxY - minY);

            try
            {
                var rtb = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    if (whiteBg) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                    dc.PushTransform(new TranslateTransform(-minX, -minY));

                    foreach (var child in DiagramCanvas.Children)
                    {
                        if (child == SelectionRect || !(child is Visual v) || ((UIElement)child).Visibility != Visibility.Visible) continue;
                        
                        var left = Canvas.GetLeft((UIElement)child);
                        var top = Canvas.GetTop((UIElement)child);
                        
                        switch (child)
                        {
                            case Line line:
                                dc.DrawLine(new Pen(line.Stroke, line.StrokeThickness) { DashStyle = new DashStyle(line.StrokeDashArray, 0) }, 
                                    new Point(line.X1, line.Y1), new Point(line.X2, line.Y2));
                                break;
                            case FrameworkElement fe:
                            {
                                var vb = new VisualBrush(fe) { Stretch = Stretch.None };
                                dc.DrawRectangle(vb, null, new Rect(left, top, fe.ActualWidth, fe.ActualHeight));
                                break;
                            }
                        }
                    }
                }
                rtb.Render(dv);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var stream = File.Create(sfd.FileName)) encoder.Save(stream);
                MessageBox.Show(GetLocalizedString("ExportSuccess"));
            }
            catch (Exception ex) { MessageBox.Show($"Export failed: {ex.Message}"); }
        }
        #endregion
    }

    public class DiagramSaveModel {
        public List<PlacedDevice> Devices { get; set; } = [];
        public List<ConnectionSaveModel> Connections { get; set; } = [];
    }
    public class ConnectionSaveModel {
        public int StartIndex { get; init; }
        public int EndIndex { get; set; }
        public ConnectionType Type { get; set; }
    }
}
