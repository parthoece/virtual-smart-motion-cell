using Avalonia.Controls;

namespace VirtualSmartMotionCell.Hmi;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += async (_, _) =>
        {
            if (DataContext is IAsyncDisposable disposable) await disposable.DisposeAsync();
        };
    }
}
