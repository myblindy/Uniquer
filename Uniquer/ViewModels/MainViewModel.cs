using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Uniquer.Core.Helpers;
using Uniquer.Models;
using Uniquer.Services;
using Windows.Storage.Pickers;

namespace Uniquer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ImageComparisonService imageComparisonService;
    private readonly DbService dbService;

    [ObservableProperty]
    string? basePath;

    partial void OnBasePathChanged(string? value) =>
        _ = dbService.SetBasePathAsync(value);

    public ObservableCollection<ImagesDifference> ImageDifferences { get; } = [];
    [ObservableProperty]
    ImagesDifference? selectedImageDifference;

    [ObservableProperty]
    double percentageProcessing;

    public MainViewModel(ImageComparisonService imageComparisonService, DbService dbService)
    {
        this.imageComparisonService = imageComparisonService;
        this.dbService = dbService;

        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        IEnumerable<IAsyncRelayCommand> commands = [StartCommand, ProcessChangesCommand, SelectFolderCommand];
        void setupCommandRunningHandler(IAsyncRelayCommand command) =>
            command.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(AsyncRelayCommand.IsRunning))
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        foreach (var cmd in commands)
                            cmd.NotifyCanExecuteChanged();
                    });
            };
        foreach (var cmd in commands)
            setupCommandRunningHandler(cmd);

        async Task ReadSettings()
        {
            BasePath = await dbService.GetBasePathAsync();
        }
        _ = ReadSettings();
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanExecuteCommand))]
    async Task Start(CancellationToken ct)
    {
        PercentageProcessing = 0;

        ImageDifferences.Clear();
        SelectedImageDifference = null;

        if (BasePath is not null)
        {
            var dispatcher = DispatcherQueue.GetForCurrentThread();
            var idx = 0;
            ImageDifferences.AddRange(await imageComparisonService.Start(BasePath, percentage =>
            {
                if (Interlocked.Increment(ref idx) % 400 == 0)
                    dispatcher.TryEnqueue(() => PercentageProcessing = percentage);
            }, ct));
        }
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanExecuteCommand))]
    async Task ProcessChanges(CancellationToken ct)
    {
        PercentageProcessing = 0;

        var processed = new ConcurrentBag<ImagesDifference>();
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        var idx = 0;
        await Parallel.ForEachAsync(ImageDifferences, ct, async (w, ct) =>
        {
            if (Interlocked.Increment(ref idx) % 25 == 0)
                dispatcher.TryEnqueue(() => PercentageProcessing = (float)idx / ImageDifferences.Count);

            switch (w.Type)
            {
                case ImagesDifferenceType.Identical or ImagesDifferenceType.Similar or ImagesDifferenceType.LeftBetter:
                    try { File.Delete(w.Path1); await dbService.DeleteFileEntryAsync(w.Path1); processed.Add(w); } catch { }
                    break;
                case ImagesDifferenceType.RightBetter:
                    try { File.Delete(w.Path2); await dbService.DeleteFileEntryAsync(w.Path2); processed.Add(w); } catch { }
                    break;
                default:
                    throw new NotImplementedException();
            }
        });

        foreach (var processedItem in processed)
            ImageDifferences.Remove(processedItem);
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanExecuteCommand))]
    async Task SelectFolder(CancellationToken ct)
    {
        FolderPicker picker = new();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        if (await picker.PickSingleFolderAsync() is { } folder)
            BasePath = folder.Path;
    }

    bool CanExecuteCommand() => !StartCommand.IsRunning && !ProcessChangesCommand.IsRunning;
}
