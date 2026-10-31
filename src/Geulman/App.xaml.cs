using Microsoft.UI.Xaml;
namespace Geulman;
public partial class App : Application
{
    public App() { this.InitializeComponent(); }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        m_window ??= new MainWindow();
        m_window.Activate();
    }
    Window? m_window;
}
