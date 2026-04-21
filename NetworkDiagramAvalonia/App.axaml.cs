using Avalonia.Controls.ApplicationLifetimes;
using System.Net.Mime;
using Avalonia.Markup.Xaml;
using NetworkDiagramAvalonia.ViewModels;
using NetworkDiagramAvalonia.Views;

namespace NetworkDiagramAvalonia;

public partial class App : MediaTypeNames.Application
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