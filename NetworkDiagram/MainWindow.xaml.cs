using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public partial class MainWindow : Window
    {
        private List<DeviceTemplate> _deviceTemplates = new List<DeviceTemplate>();
        private Diagram _currentDiagram = new Diagram();
        
        // Selection & Dragging
        private HashSet<FrameworkElement> _selectedElements = new HashSet<FrameworkElement>();
        private Point _dragStartPoint;
        private bool _isDraggingDevices;
        private bool _isSelectingArea;
        private Point _selectionStartPoint;

        private ConnectionType? _activeTool;
        private PlacedDevice? _firstDeviceForConnection;

        private Dictionary<Connection, Line> _connectionLines = new Dictionary<Connection, Line>();
        private Dictionary<PlacedDevice, FrameworkElement> _deviceElements = new Dictionary<PlacedDevice, FrameworkElement>();

        public MainWindow()
        {
            InitializeComponent();
            LoadTemplates();
        }

        private void LoadTemplates()
        {
            try
            {
                if (File.Exists("devices.json"))
                {
                    string json = File.ReadAllText("devices.json");
                    _deviceTemplates = JsonSerializer.Deserialize<List<DeviceTemplate>>(json) ?? new List<DeviceTemplate>();
                    foreach(var t in _deviceTemplates)
                    {
                        if (!string.IsNullOrEmpty(t.IconPath)) t.IconPath = Path.GetFullPath(t.IconPath);
                    }
                    ToolboxList.ItemsSource = _deviceTemplates;
                }
            }
            catch (Exception ex) { MessageBox.Show($"Error loading templates: {ex.Message}"); }
        }

        private string GetLocalizedString(string key) => Application.Current.Resources[key] as string ?? key;

        private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LangCombo.SelectedItem is ComboBoxItem item && item.Tag is string lang)
            {
                var dict = new ResourceDictionary();
                dict.Source = new Uri($"Localization/Strings.{lang}.xaml", UriKind.Relative);

                // Replace the existing localization dictionary
                var oldDict = Application.Current.Resources.MergedDictionaries.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Localization/Strings."));
                if (oldDict != null) Application.Current.Resources.MergedDictionaries.Remove(oldDict);
                Application.Current.Resources.MergedDictionaries.Add(dict);
            }
        }

        #region Drag and Drop (Toolbox to Canvas)
        private void Toolbox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is DeviceTemplate template)
                DragDrop.DoDragDrop(listBox, template, DragDropEffects.Copy);
        }

        private void DiagramCanvas_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(DeviceTemplate)) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void DiagramCanvas_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(DeviceTemplate)))
            {
                var template = (DeviceTemplate)e.Data.GetData(typeof(DeviceTemplate));
                Point dropPoint = e.GetPosition(DiagramCanvas);
                AddDeviceToCanvas(template, dropPoint.X, dropPoint.Y);
            }
        }
        #endregion

        private void AddDeviceToCanvas(DeviceTemplate template, double x, double y)
        {
            var placed = new PlacedDevice { Name = template.Name, IconPath = template.IconPath, X = x, Y = y };
            _currentDiagram.Devices.Add(placed);
            RenderDevice(placed);
        }

        private void RenderDevice(PlacedDevice device)
        {
            var template = (DataTemplate)DiagramCanvas.Resources["DeviceTemplate"];
            var contentPresenter = new ContentPresenter { Content = device, ContentTemplate = template };
            var container = new Border { Child = contentPresenter, Tag = device, Background = Brushes.Transparent };

            Canvas.SetLeft(container, device.X);
            Canvas.SetTop(container, device.Y);

            device.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(PlacedDevice.X)) Canvas.SetLeft(container, device.X);
                if (e.PropertyName == nameof(PlacedDevice.Y)) Canvas.SetTop(container, device.Y);
            };

            container.MouseDown += Device_MouseDown;
            container.MouseLeftButtonDown += (s, e) => { 
                if (e.ClickCount == 2) {
                    StopDragging();
                    EditDevice(device);
                    e.Handled = true;
                }
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
            if (sender is FrameworkElement element && element.Tag is PlacedDevice device)
            {
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
        }

        private void SelectElement(FrameworkElement element)
        {
            _selectedElements.Add(element);
            element.Opacity = 0.7;
            if (element is Line l) l.StrokeThickness = 5;
        }

        private void DeselectElement(FrameworkElement element)
        {
            _selectedElements.Remove(element);
            element.Opacity = 1.0;
            if (element is Line l) l.StrokeThickness = 3;
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
            Point currentPoint = e.GetPosition(DiagramCanvas);

            if (_isSelectingArea)
            {
                double x = Math.Min(_selectionStartPoint.X, currentPoint.X);
                double y = Math.Min(_selectionStartPoint.Y, currentPoint.Y);
                double w = Math.Abs(_selectionStartPoint.X - currentPoint.X);
                double h = Math.Abs(_selectionStartPoint.Y - currentPoint.Y);

                Canvas.SetLeft(SelectionRect, x);
                Canvas.SetTop(SelectionRect, y);
                SelectionRect.Width = w;
                SelectionRect.Height = h;

                // Real-time selection preview
                Rect selectionBounds = new Rect(x, y, w, h);
                foreach (var border in _deviceElements.Values)
                {
                    Rect elementBounds = new Rect(Canvas.GetLeft(border), Canvas.GetTop(border), border.ActualWidth, border.ActualHeight);
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
                    Rect lineBounds = new Rect(
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
                double deltaX = currentPoint.X - _dragStartPoint.X;
                double deltaY = currentPoint.Y - _dragStartPoint.Y;

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
                Rect selectionBounds = new Rect(
                    Canvas.GetLeft(SelectionRect),
                    Canvas.GetTop(SelectionRect),
                    SelectionRect.Width,
                    SelectionRect.Height);

                foreach (var border in _deviceElements.Values)
                {
                    Rect elementBounds = new Rect(Canvas.GetLeft(border), Canvas.GetTop(border), border.ActualWidth, border.ActualHeight);
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
            if (conn.Type == ConnectionType.Wifi) line.StrokeDashArray = new DoubleCollection { 2, 2 };

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
            if (_deviceElements.TryGetValue(conn.StartDevice, out var startElem) && 
                _deviceElements.TryGetValue(conn.EndDevice, out var endElem))
            {
                if (startElem.ActualWidth == 0) startElem.UpdateLayout();
                if (endElem.ActualWidth == 0) endElem.UpdateLayout();

                line.X1 = conn.StartDevice.X + (startElem.ActualWidth / 2);
                line.Y1 = conn.StartDevice.Y + 24;
                line.X2 = conn.EndDevice.X + (endElem.ActualWidth / 2);
                line.Y2 = conn.EndDevice.Y + 24;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && _selectedElements.Count > 0)
            {
                foreach (var el in _selectedElements.ToList())
                {
                    if (el is Line line && el.Tag is Connection conn)
                    {
                        _currentDiagram.Connections.Remove(conn);
                        _connectionLines.Remove(conn);
                        DiagramCanvas.Children.Remove(line);
                    }
                    else if (el is Border border && border.Tag is PlacedDevice device)
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
                    }
                }
                _selectedElements.Clear();
            }
            else if (e.Key == Key.Escape) { ResetConnectionTool(); StopDragging(); ClearSelection(); }
        }

        private void EditDevice(PlacedDevice device)
        {
            var dialog = new EditDeviceWindow(device);
            dialog.ShowDialog();
            if (_deviceElements.TryGetValue(device, out var elem))
            {
                elem.UpdateLayout();
                var attached = _currentDiagram.Connections.Where(c => c.StartDevice == device || c.EndDevice == device).ToList();
                foreach(var c in attached) if (_connectionLines.TryGetValue(c, out var l)) UpdateLinePosition(c, l);
            }
        }

        #region Toolbar Events
        private void NewDiagram_Click(object sender, RoutedEventArgs e)
        {
            _currentDiagram = new Diagram();
            DiagramCanvas.Children.Clear();
            _connectionLines.Clear();
            _deviceElements.Clear();
            ClearSelection();
        }

        private void SaveDiagram_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog { Filter = "Network Diagram (*.ndjson)|*.ndjson" };
            if (sfd.ShowDialog() == true)
            {
                var model = new DiagramSaveModel {
                    Devices = _currentDiagram.Devices,
                    Connections = _currentDiagram.Connections.Select(c => new ConnectionSaveModel {
                        StartIndex = _currentDiagram.Devices.IndexOf(c.StartDevice),
                        EndIndex = _currentDiagram.Devices.IndexOf(c.EndDevice),
                        Type = c.Type
                    }).ToList()
                };
                string json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(sfd.FileName, json);
            }
        }

        private void LoadDiagram_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog { Filter = "Network Diagram (*.ndjson)|*.ndjson" };
            if (ofd.ShowDialog() == true)
            {
                string json = File.ReadAllText(ofd.FileName);
                var model = JsonSerializer.Deserialize<DiagramSaveModel>(json);
                if (model == null) return;
                _currentDiagram = new Diagram { Devices = model.Devices };
                DiagramCanvas.Children.Clear();
                _connectionLines.Clear();
                _deviceElements.Clear();
                ClearSelection();
                foreach (var device in _currentDiagram.Devices) RenderDevice(device);
                foreach (var cModel in model.Connections)
                {
                    var conn = new Connection { StartDevice = _currentDiagram.Devices[cModel.StartIndex], EndDevice = _currentDiagram.Devices[cModel.EndIndex], Type = cModel.Type };
                    _currentDiagram.Connections.Add(conn);
                    RenderConnection(conn);
                }
            }
        }

        private void WireTool_Click(object sender, RoutedEventArgs e) => _activeTool = ConnectionType.Wire;
        private void WifiTool_Click(object sender, RoutedEventArgs e) => _activeTool = ConnectionType.Wifi;
        private void AddText_Click(object sender, RoutedEventArgs e) => AddDeviceToCanvas(new DeviceTemplate { Name = "Note", IconPath = "" }, 100, 100);

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

            bool whiteBg = result == MessageBoxResult.Yes;

            // 1. Find content bounds
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var border in _deviceElements.Values)
            {
                double x = Canvas.GetLeft(border);
                double y = Canvas.GetTop(border);
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

            double margin = 20;
            minX -= margin; minY -= margin; maxX += margin; maxY += margin;
            double width = Math.Max(1, maxX - minX);
            double height = Math.Max(1, maxY - minY);

            try
            {
                RenderTargetBitmap rtb = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
                DrawingVisual dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                {
                    if (whiteBg) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                    dc.PushTransform(new TranslateTransform(-minX, -minY));

                    foreach (var child in DiagramCanvas.Children)
                    {
                        if (child == SelectionRect || !(child is Visual v) || ((UIElement)child).Visibility != Visibility.Visible) continue;
                        
                        double left = Canvas.GetLeft((UIElement)child);
                        double top = Canvas.GetTop((UIElement)child);
                        
                        if (child is Line line)
                        {
                            dc.DrawLine(new Pen(line.Stroke, line.StrokeThickness) { DashStyle = new DashStyle(line.StrokeDashArray, 0) }, 
                                        new Point(line.X1, line.Y1), new Point(line.X2, line.Y2));
                        }
                        else if (child is FrameworkElement fe)
                        {
                            VisualBrush vb = new VisualBrush(fe) { Stretch = Stretch.None };
                            dc.DrawRectangle(vb, null, new Rect(left, top, fe.ActualWidth, fe.ActualHeight));
                        }
                    }
                }
                rtb.Render(dv);
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var stream = File.Create(sfd.FileName)) encoder.Save(stream);
                MessageBox.Show(GetLocalizedString("ExportSuccess"));
            }
            catch (Exception ex) { MessageBox.Show($"Export failed: {ex.Message}"); }
        }
        #endregion
    }

    public class DiagramSaveModel {
        public List<PlacedDevice> Devices { get; set; } = new();
        public List<ConnectionSaveModel> Connections { get; set; } = new();
    }
    public class ConnectionSaveModel {
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public ConnectionType Type { get; set; }
    }
}
