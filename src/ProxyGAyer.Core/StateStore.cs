using System.Text.Json;

namespace ProxyGAyer.Core;

public sealed class StateStore
{
    private readonly string path;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string DirectoryPath { get; }
    public StateStore(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        path = Path.Combine(DirectoryPath, "state.json");
    }

    public AppState Load()
    {
        if (!File.Exists(path)) return new();
        var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path))
            ?? throw new InvalidDataException("فایل تنظیمات برنامه خالی یا خراب است.");
        if (state.Version != 1) throw new InvalidDataException("نسخه فایل تنظیمات پشتیبانی نمی‌شود.");
        return state;
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temporary, path, true);
    }
}

public interface IResourceStore
{
    string? Read(string key);
    void Write(string key, string? value);
}

// The journal is saved BEFORE touching external configuration. A crash leaves a
// recoverable pending change. Reset never overwrites a resource edited externally.
public sealed class ChangeManager(StateStore store, IResourceStore resources, AppState state)
{
    public AppState State { get; } = state;

    public void Apply(string id, ProxyProfile profile, IReadOnlyDictionary<string, string?> desired)
    {
        profile.Validate();
        var hadOld = State.Changes.TryGetValue(id, out var old);
        if (old?.Pending == true) throw new InvalidOperationException("تغییر قبلی کامل نشده است. ابتدا Reset را اجرا کنید.");
        if (old is not null)
            foreach (var item in old.Resources)
                if (resources.Read(item.Key) != item.After)
                    throw Conflict(item.Key);

        foreach (var pair in State.Changes.Where(p => p.Key != id))
            if (pair.Value.Resources.Any(r => desired.ContainsKey(r.Key)))
                throw new InvalidOperationException($"این تنظیمات با بخش {pair.Key} مشترک است. ابتدا آن بخش را Reset کنید.");

        var changes = desired.Select(p => new ResourceChange(p.Key,
            old?.Resources.FirstOrDefault(r => r.Key == p.Key)?.Before ?? resources.Read(p.Key), p.Value)).ToList();
        // A null Before means the resource did not exist. Do not replace it with
        // the current value when applying a second time.
        if (old is not null)
            changes = changes.Select(r => old.Resources.FirstOrDefault(o => o.Key == r.Key) is { } original
                ? r with { Before = original.Before } : r).ToList();

        if (old is not null && old.Resources.Any(r => !desired.ContainsKey(r.Key)))
            throw new InvalidOperationException("مسیر تنظیمات از زمان Set قبلی تغییر کرده است. ابتدا Reset کنید.");

        var previous = changes.ToDictionary(r => r.Key, r => resources.Read(r.Key));
        changes = changes.Select(r => r with { Previous = previous[r.Key] }).ToList();
        var journal = new ChangeSet { Pending = true, Resources = changes };
        State.Changes[id] = journal;
        store.Save(State);
        try
        {
            foreach (var item in changes) resources.Write(item.Key, item.After);
        }
        catch
        {
            var rollbackSucceeded = true;
            foreach (var item in changes.AsEnumerable().Reverse())
            {
                try { resources.Write(item.Key, previous[item.Key]); }
                catch { rollbackSucceeded = false; }
            }
            if (rollbackSucceeded)
            {
                if (hadOld) State.Changes[id] = old!;
                else State.Changes.Remove(id);
            }
            store.Save(State);
            throw;
        }
        journal.Pending = false;
        State.Profiles[id] = profile;
        store.Save(State);
    }

    public void Reset(string id)
    {
        if (!State.Changes.TryGetValue(id, out var journal)) return;
        // Preflight all resources before restoring any of them.
        foreach (var item in journal.Resources)
        {
            var current = resources.Read(item.Key);
            if (current != item.After && current != item.Before && !(journal.Pending && current == item.Previous))
                throw Conflict(item.Key);
        }
        journal.Pending = true;
        store.Save(State);
        foreach (var item in journal.Resources.AsEnumerable().Reverse())
            if (resources.Read(item.Key) != item.Before) resources.Write(item.Key, item.Before);
        State.Changes.Remove(id);
        store.Save(State);
    }

    private static InvalidOperationException Conflict(string key) => new(
        $"تنظیمات خارج از برنامه تغییر کرده است. برای حفظ تغییرات جدید، عملیات متوقف شد. فایل پشتیبان در پوشه داده برنامه موجود است.\n{key}");
}
