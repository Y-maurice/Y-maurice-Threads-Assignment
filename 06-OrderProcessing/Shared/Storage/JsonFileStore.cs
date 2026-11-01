using _06_OrderProcessing.Shared.Exceptions;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace _06_OrderProcessing.Shared.Storage;

public sealed class JsonFileStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };


    private static readonly UTF8Encoding Utf8 = new(false);

    // One lock per file, shared by every store instance and by sync and async callers (two callers cannot have the lock at the same time)
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _directory;

    public JsonFileStore() : this(DataFiles.DataDirectory) { }

    public JsonFileStore(string directory) => _directory = directory;

    public async Task<List<T>> ReadAllAsync<T>(string fileName, CancellationToken ct)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = LockFor(path);
        await gate.WaitAsync(ct);
        try
        {
            return await ReadCoreAsync<T>(path, fileName, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task WriteAllAsync<T>(string fileName, List<T> items, CancellationToken ct)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = LockFor(path);
        await gate.WaitAsync(ct);
        try
        {
            await WriteCoreAsync(path, items, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    // Read, change and write back as ONE step so no update is ever lost.
    public async Task<TResult> UpdateAsync<T, TResult>(
        string fileName, Func<List<T>, TResult> change, CancellationToken ct)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = LockFor(path);
        await gate.WaitAsync(ct);
        try
        {
            List<T> items = await ReadCoreAsync<T>(path, fileName, ct);
            TResult result = change(items);
            await WriteCoreAsync(path, items, ct);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    // ---------- Sync API (only for the sync comparison versions) ----------

    public List<T> ReadAll<T>(string fileName)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = LockFor(path);
        gate.Wait();
        try
        {
            if (!File.Exists(path))
                return new List<T>();
            return Parse<T>(File.ReadAllText(path), fileName);
        }
        finally
        {
            gate.Release();
        }
    }

    public void WriteAll<T>(string fileName, List<T> items)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = LockFor(path);
        gate.Wait();
        try
        {
            WriteCore(path, items);
        }
        finally
        {
            gate.Release();
        }
    }

    public TResult Update<T, TResult>(string fileName, Func<List<T>, TResult> change)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = LockFor(path);
        gate.Wait();
        try
        {
            List<T> items = File.Exists(path)
                ? Parse<T>(File.ReadAllText(path), fileName)
                : new List<T>();
            TResult result = change(items);
            WriteCore(path, items);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    // ---------- Internals (always called while holding the file's lock) ----------

    private string PathOf(string fileName) => Path.Combine(_directory, fileName);

    private static SemaphoreSlim LockFor(string path) =>
        Locks.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));

    private static List<T> Parse<T>(string json, string fileName)
    {
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, Options)
                ?? throw new DataFileCorruptedException(fileName, "the file contains null instead of an array");
        }
        catch (JsonException ex)
        {
            throw new DataFileCorruptedException(fileName, ex);
        }
    }

    private static string Serialize<T>(List<T> items) =>
        JsonSerializer.Serialize(items, Options);

    private static async Task<List<T>> ReadCoreAsync<T>(string path, string fileName, CancellationToken ct)
    {
        if (!File.Exists(path))
            return new List<T>();

        string json = await File.ReadAllTextAsync(path, ct);
        return Parse<T>(json, fileName);
    }

    private async Task WriteCoreAsync<T>(string path, List<T> items, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        string temp = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, Serialize(items), Utf8, ct);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private void WriteCore<T>(string path, List<T> items)
    {
        Directory.CreateDirectory(_directory);
        string temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, Serialize(items), Utf8);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

}
