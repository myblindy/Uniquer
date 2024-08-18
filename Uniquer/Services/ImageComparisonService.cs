using Nito.AsyncEx;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks.Dataflow;
using Uniquer.Models;

namespace Uniquer.Services;

public class ImageComparisonService(DbService dbService)
{
    static async Task<(byte[]? hash, int width, int height)> CalculateImageHash(string imagePath)
    {
        try
        {
            // open image in gray scale
            using var image = await Image.LoadAsync<Rgb24>(imagePath);
            var (width, height) = (image.Width, image.Height);

            // clone the image into a new smaller image with the correct aspect ratio and one gray scale channel
            const int hashImageSize = 32;
            image.Mutate(x => x.Resize(hashImageSize, hashImageSize));

            // the hash is the image bytes
            var result = new byte[hashImageSize * hashImageSize];
            image.ProcessPixelRows(data =>
            {
                for (int y = 0; y < data.Height; ++y)
                {
                    var rowSpan = data.GetRowSpan(y);
                    for (int x = 0; x < rowSpan.Length; ++x)
                        result[y * data.Width + x] = (byte)((rowSpan[x].R + rowSpan[x].G + rowSpan[x].B) / 3);
                }
            });
            return (result, width, height);
        }
        catch { return default; }
    }

    static bool AreHashesClose(byte[] hash1, byte[] hash2)
    {
        if (hash1.Length != hash2.Length) return false;

        const float percentage = 2.5f;

        Vector256<ushort> diffV = default;
        var remaining = hash1.Length / Vector256<byte>.Count;
        for (int i = 0; i < hash1.Length - remaining; i += Vector256<byte>.Count)
        {
            var v1 = Vector256.Create(hash1, i);
            var v2 = Vector256.Create(hash2, i);
            var _diffV = Avx2.SumAbsoluteDifferences(v1, v2);
            diffV += _diffV;
        }

        var diffV64 = diffV.AsUInt64();

        var diffV64Low = Avx2.ExtractVector128(diffV64, 0);
        var diffV64High = Avx2.ExtractVector128(diffV64, 1);
        var diffV128 = diffV64High + diffV64Low;

        var diff = diffV128[0] + diffV128[1];
        var isDifferent = diff < hash1.Length * percentage;
        return isDifferent;
    }

    public async Task<IList<ImagesDifference>> Start(string basePath, Action<double> percentageUpdate, CancellationToken ct)
    {
        // step 1: copy all the file data from the database to a local copy
        var allFileData = await Task.Run(async () => (await dbService.GetAllFileDataAsync().ConfigureAwait(false))
            .Select(w => (w.path, w.hash, w.width, w.height, @new: false))
            .ToList()).ConfigureAwait(false);
        var allHashesMonitor = new AsyncMonitor();

        // step 2: process any new images and add them to the local db
        var newFiles = await Task.Run(() => Directory.EnumerateFiles(basePath, "*", SearchOption.AllDirectories)
            .Except(allFileData.Select(w => w.path)).ToList(), ct).ConfigureAwait(false);

        var loadNewImagesBlock = new TransformBlock<string, (string imagePath, Image<Rgb24>? image)>(async imagePath =>
        {
            try
            {
                return (imagePath, await Image.LoadAsync<Rgb24>(imagePath));
            }
            catch { return (imagePath, null); }
        }, new() { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount * 8 });

        var index = 0;
        var hashNewImagesBlock = new ActionBlock<(string imagePath, Image<Rgb24>? image)>(async w =>
        {
            percentageUpdate((double)Interlocked.Increment(ref index) / newFiles.Count / 2);

            if (w.image is null) return;

            using (w.image)
            {
                var (width, height) = (w.image.Width, w.image.Height);

                // clone the image into a new smaller image with the correct aspect ratio and one gray scale channel
                const int hashImageSize = 32;
                w.image.Mutate(x => x.Resize(hashImageSize, hashImageSize));

                // the hash is the image bytes
                var hash = new byte[hashImageSize * hashImageSize];
                w.image.ProcessPixelRows(data =>
                {
                    for (int y = 0; y < data.Height; ++y)
                    {
                        var rowSpan = data.GetRowSpan(y);
                        for (int x = 0; x < rowSpan.Length; ++x)
                            hash[y * data.Width + x] = (byte)((rowSpan[x].R + rowSpan[x].G + rowSpan[x].B) / 3);
                    }
                });

                using (await allHashesMonitor.EnterAsync().ConfigureAwait(false))
                    allFileData.Insert(0, (w.imagePath, hash, width, height, true));
                await dbService.SetFileDataAsync(w.imagePath, hash, width, height).ConfigureAwait(false);
            }
        }, new() { CancellationToken = ct });

        var s2LinkOptions = new DataflowLinkOptions { PropagateCompletion = true };
        loadNewImagesBlock.LinkTo(hashNewImagesBlock, s2LinkOptions);

        foreach (var newFile in newFiles)
            await loadNewImagesBlock.SendAsync(newFile, ct).ConfigureAwait(false);
        loadNewImagesBlock.Complete();
        await Task.WhenAll(loadNewImagesBlock.Completion, hashNewImagesBlock.Completion).ConfigureAwait(false);

        // step 3: find any duplicates (conflicts) in the local database, thus including old and new entries alike
        var results = new ConcurrentBag<ImagesDifference>();
        await Parallel.ForEachAsync(Enumerable.Range(0, allFileData.Count).Where(idx => allFileData[idx].@new && allFileData[idx].path.StartsWith(basePath)).Chunk(32), ct, async (i1s, ct) =>
        {
            foreach (var i1 in i1s)
            {
                percentageUpdate(.5 + (double)i1 / allFileData.Count / 2);

                for (int i2 = i1 + 1; i2 < allFileData.Count; ++i2)
                    if (allFileData[i2].path.StartsWith(basePath) && AreHashesClose(allFileData[i1].hash, allFileData[i2].hash))
                        results.Add(new ImagesDifference(
                            allFileData[i1].path, allFileData[i1].width, allFileData[i1].height,
                            allFileData[i2].path, allFileData[i2].width, allFileData[i2].height,
                            allFileData[i1].width * allFileData[i1].height < allFileData[i2].width * allFileData[i2].height ? ImagesDifferenceType.RightBetter
                                : allFileData[i1].width * allFileData[i1].height > allFileData[i2].width * allFileData[i2].height ? ImagesDifferenceType.LeftBetter : ImagesDifferenceType.Similar));
            }
        }).ConfigureAwait(false);

        return results.ToArray();
    }
}
