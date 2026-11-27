# Settings and binary data

Part of the `spar-app` skill: what an app remembers between runs, its installation file, and binary data from the backend.

## Settings

`spar.Settings(defaults)` in `Program.cs` declares a settings record, a positional record under the same wire rules as a contract's types. Spar writes its serializer and registers `ISparSettings<T>`: `Current`, `UpdateAsync(change, ct)` (one at a time, each written at once) and `WatchAsync(ct)`. The file is `settings.json` in the data folder; a property it lacks takes the default's value. `s => s.Name("layout")` gives a second record its own file, `s.File(path)` moves one, `s.Version(2).Migrate(from: 1, json => …)` reads an older file, and a file on a network share needs `s.AllowNetworkFile()`. Never put a password in settings.

<!-- source: samples/Notebook/Program.cs -->
```csharp
    // The view mode and the last open page, remembered between runs in settings.json (docs/app-model.md).
    spar.Settings(new NotebookSettings(ViewMode.Split, LastPage: null));
```

<!-- source: samples/Notebook/Pages/Preferences.cs -->
```csharp
/// <summary>What the notebook remembers between runs, in <c>settings.json</c> in the app's data folder.</summary>
public sealed record NotebookSettings(ViewMode ViewMode, string? LastPage);

/// <summary>The notebook's remembered choices: the view mode, and the page to open at the next start.</summary>
[SparContract]
public sealed class PreferencesService(ISparSettings<NotebookSettings> settings)
{
    [SparCommand]
    public Task<NotebookSettings> GetAsync() => Task.FromResult(settings.Current);

    [SparCommand]
    public async Task SetViewModeAsync(ViewMode mode, CancellationToken cancellationToken) =>
        await settings.UpdateAsync(current => current with { ViewMode = mode }, cancellationToken);
    // …
}
```

`spar.InstallationFile()` lets an installation put `<app>.spar.json` beside the executable to move the folders and settings files, keep the browser's data in memory, remove folders at exit and set log levels, without a new build; it never sets security. `spar doctor` shows what it sets.

## Binary data

Above about 64 KB, do not return a `byte[]`: the page reads binary data from the app's own origin several times faster, and an `<img>` needs no change to the page's policy.
- **A handle**, for data that exists once: return a `SparBlob` (`SparBlob.FromBytes`, `SparBlob.FromFile`, or the injected `SparBlobs`' `FromStreamAsync`), alone or in a record. The page gets `url`, `contentType`, `size`, `name`, `bytes()`, `blob()` and `release()`; release what the page no longer shows. A document holds at most 10,000 handles and 512 MB of `byte[]` ones.
- **A resource**, for data the backend can fetch again by a key, such as an image by its id: `[SparResource]` on a command that takes a scalar or a record of scalars and returns `SparBlob?` (null is a 404). The page never calls it; it uses its address, and each load runs the command, keeping nothing:

<!-- source: samples/Notebook/Pages/PageImages.cs -->
```csharp
    /// <summary>The image itself, for the preview's <c>&lt;img&gt;</c>; none when the page or the image is gone.</summary>
    [SparCommand]
    [SparResource]
    public Task<SparBlob?> ImageAsync(ImageRef request) => Task.FromResult(files.Find(request.PageId, request.ImageId));
```

<!-- source: samples/Notebook/frontend/src/remembered.ts -->
```ts
  return images.image.url({ pageId, imageId: src.slice(imageScheme.length) });
```

A blob never goes in a request (send a `byte[]`, within the bridge's 4 MiB message), a stream's items or a declared error (SPAR026). Page tests reply with `spar.blob(bytes, contentType, name)`.

