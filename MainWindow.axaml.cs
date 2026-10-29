using Avalonia.Controls;
using Mdhub.Components;

namespace Mdhub;

// Application shell. Full-screen pages live in Views; workspace pieces live in Components.
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        MainView.TitleChanged += title => Title = title;
        MainView.CloseRequested += Close;
        MainView.WindowActionRequested += HandleWindowAction;
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty) MainView.UpdateWindowState(WindowState);
        };
        MainView.UpdateWindowState(WindowState);
        Closing += (_, args) => MainView.HandleClosing(args);
        Closed += (_, _) => MainView.StopPreview();
        Opened += (_, _) => { Title = MainView.DocumentTitle; MainView.FocusEditor(); };
    }

    private void HandleWindowAction(WindowAction action)
    {
        switch (action)
        {
            case WindowAction.Minimize: WindowState = WindowState.Minimized; break;
            case WindowAction.ToggleMaximize:
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
            case WindowAction.Close: Close(); break; // Uses the existing unsaved-change checks.
        }
    }
}
