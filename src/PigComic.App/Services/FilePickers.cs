using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PigComic.App.Services;

/// <summary>
/// File/folder pickers for the whole app. Avalonia 12 removed the legacy
/// OpenFileDialog/SaveFileDialog/OpenFolderDialog types; every picker must go through
/// <see cref="IStorageProvider"/>. Use these helpers instead of calling StorageProvider
/// directly so the local-path handling stays in one place.
///
/// All methods return a local filesystem path, or null when the user cancelled (or the
/// picked item has no local path, e.g. a cloud location).
/// </summary>
public static class FilePickers
{
    /// <summary>Pick one existing file. <paramref name="extension"/> is bare, e.g. "pcml".</summary>
    public static async Task<string?> OpenFileAsync(Visual owner, string title, string filterName, string extension)
    {
        var provider = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (provider is null)
        {
            return null;
        }

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [FileType(filterName, extension)],
        }).ConfigureAwait(true);

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>Pick a save location. <paramref name="extension"/> is bare, e.g. "tmx".</summary>
    public static async Task<string?> SaveFileAsync(
        Visual owner, string title, string filterName, string extension, string? suggestedName = null)
    {
        var provider = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (provider is null)
        {
            return null;
        }

        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = extension,
            SuggestedFileName = suggestedName,
            FileTypeChoices = [FileType(filterName, extension)],
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }

    /// <summary>Pick one existing folder.</summary>
    public static async Task<string?> OpenFolderAsync(Visual owner, string title)
    {
        var provider = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (provider is null)
        {
            return null;
        }

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private static FilePickerFileType FileType(string name, string extension) =>
        new(name) { Patterns = [$"*.{extension}"] };
}
