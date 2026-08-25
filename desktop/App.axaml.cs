using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PlaceContext.Desktop.Services;
using PlaceContext.Desktop.ViewModels;
using PlaceContext.Desktop.Views;

namespace PlaceContext.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(
                    new PlaceContextConnectionService(),
                    new EndpointSettingsStore()),
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
