using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace VirtualSmartMotionCell.Hmi;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var endpoint = Environment.GetEnvironmentVariable("VSMC_ENDPOINT") ?? "http://localhost:8080";
            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel(endpoint) };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
