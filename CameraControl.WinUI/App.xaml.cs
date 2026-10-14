using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CameraControl_WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private Startup.ApplicationStartup? _startup;
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        var probe = CameraControl_WinUI.Video.VideoProbeOptions.Read(Environment.GetCommandLineArgs());
        if (probe is not null)
        {
            Directory.CreateDirectory(probe.OutputDirectory);
            File.AppendAllText(System.IO.Path.Combine(probe.OutputDirectory, "startup-stage.txt"), "App constructor\n");
            UnhandledException += (_, error) =>
            {
                try { File.WriteAllText(System.IO.Path.Combine(probe.OutputDirectory, "unhandled-exception.txt"), error.Exception.ToString()); }
                catch (IOException) { System.Diagnostics.Debug.WriteLine(error.Exception); }
            };
        }
        InitializeComponent();
        if (probe is not null) File.AppendAllText(System.IO.Path.Combine(probe.OutputDirectory, "startup-stage.txt"), "App XAML initialized\n");
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var probe = CameraControl_WinUI.Video.VideoProbeOptions.Read(Environment.GetCommandLineArgs());
        if (probe is not null)
        {
            Directory.CreateDirectory(probe.OutputDirectory);
            File.AppendAllText(System.IO.Path.Combine(probe.OutputDirectory, "startup-stage.txt"), "OnLaunched\n");
        }
        if (probe is not null)
        {
            _window = new VideoProbeWindow(probe);
            _window.Activate();
            return;
        }
        _startup = new Startup.ApplicationStartup();
        await _startup.StartAsync(Environment.GetCommandLineArgs().Skip(1).ToArray());
    }
}
