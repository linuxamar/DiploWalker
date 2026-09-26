namespace DiploWalker.Gui.Tests

open System
open System.Collections.Generic
open System.IO
open System.Threading.Tasks
open Avalonia.Platform.Storage

/// Fakes des interfaces Avalonia.Platform.Storage (Avalonia.Base) : les
/// ViewModels ne consomment que Path.LocalPath (et OpenWriteAsync pour
/// l'export du journal). Les autres membres retournent des valeurs bénignes
/// ou null.

/// Fake IStorageItem : seul Path (et Name) est réellement utilisé par les
/// ViewModels.
type FakeStorageItem(path: string, name: string) =

    interface IStorageItem with
        member _.Name = name
        member _.Path = Uri path
        member _.CanBookmark = false

        member _.GetBasicPropertiesAsync() =
            Task.FromResult(
                StorageItemProperties(
                    Nullable<uint64>(0UL),
                    Nullable<DateTimeOffset>(),
                    Nullable<DateTimeOffset>()
                )
            )

        member _.SaveBookmarkAsync() = Task.FromResult<string>(null)
        member _.GetParentAsync() = Task.FromResult<IStorageFolder>(null)
        member _.DeleteAsync() = Task.CompletedTask
        member _.MoveAsync(_folder) = Task.FromResult<IStorageItem>(null)

    interface IDisposable with
        member _.Dispose() = ()

/// Fake IStorageFile : OpenWriteAsync retourne un vrai flux sur le fichier,
/// ce qui permet de vérifier le contenu écrit par l'export du journal.
type FakeStorageFile(path: string) =
    inherit FakeStorageItem(path, Path.GetFileName(path))

    interface IStorageFile with
        member _.OpenReadAsync() = Task.FromResult<Stream>(File.OpenRead path)
        member _.OpenWriteAsync() = Task.FromResult<Stream>(File.OpenWrite path)

/// Fake IStorageFolder : le ViewModel ne lit que Path.
type FakeStorageFolder(path: string) =
    inherit FakeStorageItem(path, Path.GetFileName(path))

    interface IStorageFolder with
        member _.GetItemsAsync() =
            { new IAsyncEnumerable<IStorageItem> with
                member _.GetAsyncEnumerator(_ct) =
                    { new IAsyncEnumerator<IStorageItem> with
                        member _.Current = null
                        member _.MoveNextAsync() = ValueTask<bool>(false)
                        member _.DisposeAsync() = ValueTask() } }

        member _.GetFolderAsync(_name) = Task.FromResult<IStorageFolder>(null)
        member _.GetFileAsync(_name) = Task.FromResult<IStorageFile>(null)
        member _.CreateFileAsync(_name) = Task.FromResult<IStorageFile>(null)
        member _.CreateFolderAsync(_name) = Task.FromResult<IStorageFolder>(null)

/// Fake IStorageProvider : les pickers retournent au plus un élément injecté.
/// Sans élément, SaveFilePickerAsync retourne null (annulation de l'utilisateur)
/// et les pickers d'ouverture retournent une liste vide.
type FakeStorageProvider(?file: IStorageFile, ?folder: IStorageFolder, ?saveFile: IStorageFile) =

    interface IStorageProvider with
        member _.CanOpen = true
        member _.CanSave = true
        member _.CanPickFolder = true

        member _.OpenFilePickerAsync(_options) =
            let files: IReadOnlyList<IStorageFile> =
                ResizeArray(Option.toList file) :> IReadOnlyList<IStorageFile>

            Task.FromResult(files)

        member _.OpenFilePickerWithResultAsync(_options) =
            Task.FromResult(Unchecked.defaultof<OpenFilePickerResult>)

        member _.SaveFilePickerAsync(_options) =
            Task.FromResult(saveFile |> Option.toObj)

        member _.SaveFilePickerWithResultAsync(_options) =
            Task.FromResult(Unchecked.defaultof<SaveFilePickerResult>)

        member _.OpenFolderPickerAsync(_options) =
            let folders: IReadOnlyList<IStorageFolder> =
                ResizeArray(Option.toList folder) :> IReadOnlyList<IStorageFolder>

            Task.FromResult(folders)

        member _.OpenFileBookmarkAsync(_bookmark) = Task.FromResult<IStorageBookmarkFile>(null)
        member _.OpenFolderBookmarkAsync(_bookmark) = Task.FromResult<IStorageBookmarkFolder>(null)
        member _.TryGetFileFromPathAsync(_uri) = Task.FromResult<IStorageFile>(null)
        member _.TryGetFolderFromPathAsync(_uri) = Task.FromResult<IStorageFolder>(null)
        member _.TryGetWellKnownFolderAsync(_kind) = Task.FromResult<IStorageFolder>(null)
