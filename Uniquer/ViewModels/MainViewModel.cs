using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Uniquer.Core.Helpers;
using Uniquer.Models;
using Uniquer.Services;

namespace Uniquer.ViewModels;

public partial class MainViewModel(ImageComparisonService imageComparisonService, DbService dbService) : ObservableRecipient
{
    [ObservableProperty]
    string? basePath;

    public ObservableCollection<ImagesDifference> ImageDifferences { get; } = [];
    [ObservableProperty]
    ImagesDifference? selectedImageDifference;

    [ObservableProperty]
    bool isProcessing;

    [ObservableProperty]
    double percentageProcessing;

    [ICommand]
    async Task Start()
    {
        try
        {
            IsProcessing = true;
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
                }, CancellationToken.None));
            }
        }
        finally { IsProcessing = false; }
    }

    [ICommand]
    async Task ProcessChanges()
    {
        try
        {
            IsProcessing = true;
            PercentageProcessing = 0;

            var processed = new ConcurrentBag<ImagesDifference>();
            var dispatcher = DispatcherQueue.GetForCurrentThread();
            var idx = 0;
            await Parallel.ForEachAsync(ImageDifferences, async (w, ct) =>
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
        finally { IsProcessing = false; }
    }
}
