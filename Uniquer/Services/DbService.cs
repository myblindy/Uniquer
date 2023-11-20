using LiteDB;
using Nito.AsyncEx;

namespace Uniquer.Services;

public class DbService
{
    readonly ILiteDatabase db;
    readonly AsyncReaderWriterLock monitor = new();

    class FileData
    {
        public int Id { get; set; }
        public required string FullPath { get; set; }
        public required byte[] Hash { get; set; }
        public required int Width { get; set; }
        public required int Height { get; set; }
    }
    readonly ILiteCollection<FileData> fileDataCollection;

    class SettingsData
    {
        public int Id { get; set; }
        public string? BasePath { get; set; }
    }
    readonly ILiteCollection<SettingsData> settingsCollection;

    public DbService()
    {
        var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uniquer");
        Directory.CreateDirectory(dbPath);
        db = new LiteDatabase(Path.Combine(dbPath, "storage.db"));
        fileDataCollection = db.GetCollection<FileData>("fileData");
        settingsCollection = db.GetCollection<SettingsData>("settingsData");
    }

    public async Task<bool> FileExists(string path)
    {
        using (await monitor.ReaderLockAsync())
            return fileDataCollection.Exists(x => x.FullPath == path);
    }

    public async Task<(byte[]? hash, int width, int height)> GetFileHashAsync(string path)
    {
        using (await monitor.ReaderLockAsync())
            return fileDataCollection.FindOne(x => x.FullPath == path) is { } fileData
                ? (fileData.Hash, fileData.Width, fileData.Height)
                : default;
    }

    public async Task<IEnumerable<(string path, int width, int height)>> GetFilesFromHashAsync(byte[] hash)
    {
        using (await monitor.ReaderLockAsync())
            return fileDataCollection.Find(x => x.Hash == hash)
                .Select(fileData => (fileData.FullPath, fileData.Width, fileData.Height))
                .ToList();
    }

    public async Task<byte[][]> GetAllFileHashesAsync()
    {
        using (await monitor.ReaderLockAsync())
            return fileDataCollection.FindAll()
                .Select(fileData => fileData.Hash)
                .ToArray();
    }

    public async Task<(string path, byte[] hash, int width, int height)[]> GetAllFileDataAsync()
    {
        using (await monitor.ReaderLockAsync())
            return fileDataCollection.FindAll()
                .Select(fileData => (fileData.FullPath, fileData.Hash, fileData.Width, fileData.Height))
                .ToArray();
    }

    public async Task SetFileDataAsync(string path, byte[] hash, int width, int height)
    {
        using (await monitor.WriterLockAsync())
        {
            var fileData = fileDataCollection.FindOne(x => x.FullPath == path);
            if (fileData == null)
                fileDataCollection.Insert(new FileData
                {
                    FullPath = path,
                    Hash = hash,
                    Width = width,
                    Height = height
                });
            else
            {
                fileData.Hash = hash;
                fileDataCollection.Update(fileData);
            }
            db.Commit();
            fileDataCollection.EnsureIndex(x => x.FullPath);
            fileDataCollection.EnsureIndex(x => x.Hash);
        }
    }

    public async Task<bool> DeleteFileEntryAsync(string path)
    {
        using (await monitor.WriterLockAsync())
        {
            var deletedAny = fileDataCollection.DeleteMany(x => x.FullPath == path) > 0;
            db.Commit();
            return deletedAny;
        }
    }

    public async Task<string?> GetBasePathAsync()
    {
        using (await monitor.ReaderLockAsync())
            return settingsCollection.Find(s => true, limit: 1).FirstOrDefault()?.BasePath;
    }

    public async Task SetBasePathAsync(string? newBasePath)
    {
        using (await monitor.WriterLockAsync())
        {
            var settings = settingsCollection.Find(s => true, limit: 1).FirstOrDefault();
            if (settings == null)
                settingsCollection.Insert(new SettingsData { BasePath = newBasePath });
            else
            {
                settings.BasePath = newBasePath;
                settingsCollection.Update(settings);
            }
            db.Commit();
        }
    }
}
