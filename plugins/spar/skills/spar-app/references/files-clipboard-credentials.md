# Files, drag and drop, the clipboard and credentials

Part of the `spar-app` skill: files the user chooses or drops, the backend's clipboard, and secrets kept by the OS.

## Grants, never paths

A file or folder the user gives the app (through a dialog or a drop) becomes a grant: `FileGrant` or `FolderGrant` in C#, `PickedFile` or `PickedFolder` on the page. C# reads `grant.Path`; the page sees only the name, the size and whether it may write. A grant belongs to the window: it outlives a reload and is revoked when the window closes; a window holds at most 1,000. A command takes grants as parameters or inside its request record, like any other value; a settings record cannot hold one (SPAR028).

- **Dialogs** come from `IFileDialogs` in C# (`OpenAsync`, `OpenManyAsync`, `SaveAsync`, `OpenFolderAsync`, each with an options record: `Title`, `Filters`, `SuggestedName`, `Access` for a folder, `Expiry` for a shorter life) or from `files.open()`, `files.openMany()`, `files.save()` and `files.openFolder()` on the page. Each returns a `Result` whose errors are `Dismissed` (the user closed it), `AccessDenied`, `Unavailable` (on Linux without a file-chooser portal; its reason says so) and `Busy` (a dialog already open, or the window full of grants).
- **Reading and writing** return `Result`s with `FileAccessError`: `NotFound`, `AccessDenied`, `InUse`, `TooLarge`, `GrantExpired`, `OutsideFolder`, `ReadOnlyGrant`, `Failed`. Writes are safe writes (a temporary file beside the target, then a replace); only a save dialog's grant, or a read-write folder, can be written. On the page `readText`, `readBytes`, `writeText` and `writeBytes` carry at most 4 MiB; `blob()` reads any size on the app's own origin, and a larger write goes through a command that takes the grant.
- **A folder grant** lists, reads, writes, creates folders and deletes, with every path relative to it; an absolute path, `..` or a link that leaves it is `OutsideFolder`.

A backend command that asks for the file itself, then writes it:

<!-- source: samples/Notebook/Pages/PageTransfer.cs -->
```csharp
        var options = new SaveFileOptions
        {
            Title = $"Export {page.Title}",
            SuggestedName = $"{page.Id}.md",
            Filters = [new FileFilter("Markdown", "*.md")],
        };
        var target = await dialogs.SaveAsync(options, cancellationToken);
        if (!target.TryGetValue(out var file))
        {
            return target.Error switch
            {
                FileDialogError.Dismissed => new ExportError.Dismissed(),
                FileDialogError.AccessDenied e => new ExportError.NotSaved($"{e.Path} cannot be written."),
                FileDialogError.Unavailable e => new ExportError.NotSaved(e.Reason),
                FileDialogError.Busy => new ExportError.NotSaved("Another dialog is open."),
            };
        }

        var written = await file.WriteTextAsync(page.Markdown, cancellationToken);
```

The page choosing files, then handing the grants to a command:

<!-- source: samples/Notebook/frontend/src/transfer.ts -->
```ts
export async function chooseAndImport(): Promise<TransferOutcome> {
  return files.openMany({ title: "Import Markdown", filters: [{ name: "Markdown", extensions: [...markdownExtensions] }] }).match({
    success: picked => importFiles(picked),
    errors: {
      dismissed: (): TransferOutcome => ({}),
      accessDenied: (e): TransferOutcome => ({ notice: `${e.path} cannot be opened.` }),
      unavailable: (e): TransferOutcome => ({ notice: `No file dialog here: ${e.reason}` }),
      busy: (): TransferOutcome => ({}),
    },
  });
}
```

## Drop areas

Files dropped from the OS reach the app only on an area the page declares: `files.dropTarget(element, { accept, folders, limit }, handler)` returns the function that removes it. The handler gets a `Result<DroppedItems, DropError>`: the files as grants (and read-only folder grants with `folders: true`), or `notAccepted` (a file the area does not take) or `tooMany` (past `limit`, 100 by default). While a drag of files is over it, the area carries `data-spar-drop="over"` for its style. A drop anywhere else is refused, and drag and drop inside the page works as on the web.

<!-- source: samples/Notebook/frontend/src/components/Sidebar.tsx -->
```tsx
    const accept = markdownExtensions.map(extension => `.${extension}`);
    return files.dropTarget(list.current, { accept, limit: 50 }, result => result.match({
      success: dropped => onImport(dropped.files),
      errors: {
        notAccepted: e => onNotice(`${e.name} is not a Markdown file.`),
        tooMany: e => onNotice(`At most ${e.limit} files at a time.`),
      },
    }));
```

On Linux the page never gets the dropped `File` objects (WebKitGTK does not give them), so read a dropped file through its grant, never through the drop event's `dataTransfer`.

## The backend's clipboard

The page uses the web's `navigator.clipboard`: writing is allowed, reading needs `Permission.ClipboardRead`. The backend injects `IClipboard` (`SetTextAsync`, `GetTextAsync`), text only, at most 4 MiB, with the errors `Busy` (another program holds it), `TooLarge` and `Unavailable`.

<!-- source: samples/Notebook/Pages/PageTransfer.cs -->
```csharp
        var copied = await clipboard.SetTextAsync(page.Markdown, cancellationToken);
        return copied switch
        {
            Success => Result.Success,
            ClipboardError.Busy => new CopyError.Busy(),
            ClipboardError.TooLarge e => new CopyError.NotCopied($"The page is longer than the clipboard takes ({e.Limit / (1024 * 1024)} MB)."),
            ClipboardError.Unavailable e => new CopyError.NotCopied(e.Reason),
        };
```

## Credentials

Passwords and connection secrets go in `ICredentialStore`, never in settings or a file: `SetAsync(name, secret)`, `GetAsync(name)`, `ExistsAsync(name)` and `DeleteAsync(name)`, with the errors `NotFound`, `TooLarge` (past 2,560 UTF-8 bytes) and `Unavailable` (on Linux without a Secret Service, or with its keyring locked; the reason says which). Windows keeps them in Credential Manager under `<app id>/<name>` on this machine; `spar.Credentials(c => c.Roaming())` in `Program.cs` lets them travel with a roaming profile. Linux keeps them in the user's keyring through the Secret Service.

The page has no credential API. It sends a secret to one of the app's own commands, which checks it and stores it, and never reads one back; any script in the page could otherwise read or overwrite it.

## Tests

Under the test host each of these is a fake the test drives: `app.FileDialogs` answers the next dialog (`Choose(paths)`, `Dismiss()`, `Fail(error)`, and `Shown` lists what was asked), `app.Clipboard` holds `Text` (and `FailNext` makes the next call fail), `app.Credentials` keeps secrets in memory (`Stored`, `Store`, `Unavailable`). A window test drops real files on an area with its locator's `DropFilesAsync(paths)`. Page tests make a file with `spar.file(name, contents)`, read what the page wrote with `spar.contents(file)`, drop with `spar.drop(area, files)`, and script `files.open`, `openMany` and `save` like any command.
