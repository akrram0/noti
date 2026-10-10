using Microsoft.UI.Xaml;
using Noti.App.ViewModels;
using Noti.Core;
using Noti.Storage;

namespace Noti.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var store = new MarkdownNoteStore();
        var service = new NoteService(store);
        var viewModel = new MainViewModel(service);

        var mainWindow = new MainWindow(viewModel);
        _window = mainWindow;
        mainWindow.Activate();

        await viewModel.InitializeAsync();
    }
}
