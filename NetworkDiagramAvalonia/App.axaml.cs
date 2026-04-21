using Avalonia.Controls.ApplicationLifetimes;
using System.Net.Mime;
using Avalonia;
using Avalonia.Markup.Xaml;
using NetworkDiagramAvalonia.ViewModels;
using NetworkDiagramAvalonia.Views;

namespace NetworkDiagramAvalonia;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}