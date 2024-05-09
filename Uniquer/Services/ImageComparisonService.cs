using Nito.AsyncEx;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Uniquer.Models;

namespace Uniquer.Services;

public class ImageComparisonService(DbService dbService)
{
    static (byte[]? hash, int width, int height) CalculateImageHash(string imagePath)
    {
        try
        {
            // open image in gray scale
            using var image = Image.Load<Rgb24>(imagePath);
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
        if (isDifferent && diff != 0) { }
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

        var index = 0;
        await Parallel.ForEachAsync(newFiles, new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = 64
        }, async (imagePath, ct) =>
        {
            percentageUpdate((double)Interlocked.Increment(ref index) / newFiles.Count / 2);

            // calculate the file's hash on the thread pool
            var (hash, width, height) = CalculateImageHash(imagePath);
            if (hash is null) return;

            // push the new data to the local copy of the file data
            using (await allHashesMonitor.EnterAsync(ct).ConfigureAwait(false))
                allFileData.Insert(0, (imagePath, hash, width, height, @new: true));
        }).ConfigureAwait(false);

        // step 3: find any duplicates (conflicts) in the local database, thus including old and new entries alike
        var results = new ConcurrentBag<ImagesDifference>();
        await Parallel.ForEachAsync(Enumerable.Range(0, allFileData.Count), ct, async (i1, ct) =>
        {
            if (!allFileData[i1].@new || !allFileData[i1].path.StartsWith(basePath)) return;

            percentageUpdate(.5 + (double)i1 / allFileData.Count / 2);

            for (int i2 = i1 + 1; i2 < allFileData.Count; ++i2)
                if (allFileData[i2].path.StartsWith(basePath) && AreHashesClose(allFileData[i1].hash, allFileData[i2].hash))
                    results.Add(new ImagesDifference(
                        allFileData[i1].path, allFileData[i1].width, allFileData[i1].height,
                        allFileData[i2].path, allFileData[i2].width, allFileData[i2].height,
                        allFileData[i1].width * allFileData[i1].height < allFileData[i2].width * allFileData[i2].height ? ImagesDifferenceType.RightBetter
                            : allFileData[i1].width * allFileData[i1].height > allFileData[i2].width * allFileData[i2].height ? ImagesDifferenceType.LeftBetter : ImagesDifferenceType.Similar));
        }).ConfigureAwait(false);

        // step 4: push all the new entries to the database
        // TODO: should be done at the same time as step 3, but honestly it should be fast either way
        await Task.WhenAll(allFileData.Where(w => w.@new)
            .Select(w => dbService.SetFileDataAsync(w.path, w.hash, w.width, w.height))).ConfigureAwait(false);

        return results.ToArray();
    }
}
