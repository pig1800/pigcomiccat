using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PigComic.Core.Project;
using SkiaSharp;

namespace PigComic.App.ViewModels;

/// <summary>One master-character row with editable fields (SPEC §6.3, SQLite-backed).</summary>
public partial class CharacterMasterRowViewModel : ObservableObject
{
    /// <summary>The "original" name — the key, the value @character references.</summary>
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _localized = "";

    [ObservableProperty]
    private string _pronunciation = "";

    [ObservableProperty]
    private string _gender = "";

    [ObservableProperty]
    private string _age = "";

    [ObservableProperty]
    private string _firstChapter = "";

    [ObservableProperty]
    private string _pronoun = "";

    [ObservableProperty]
    private string _comments = "";

    /// <summary>Portrait thumbnail (decoded from the stored PNG bytes by the VM).</summary>
    [ObservableProperty]
    private Avalonia.Media.IImage? _imagePreview;

    /// <summary>Builds the Core record from the row (image bytes supplied separately by the VM).</summary>
    public CharacterStore.MasterCharacter ToMaster(byte[]? image)
        => new(Name.Trim(), Localized.Trim(), Pronunciation.Trim(), image,
               Gender.Trim(), Age.Trim(), FirstChapter.Trim(), Pronoun.Trim(), Comments.Trim());
}

/// <summary>
/// M7.1 master character editor (SPEC §16) over the SQLite character store. Rows carry
/// the §6.3 fields incl. the new Original/Localized/Pronunciation name triplet; the portrait
/// is a PNG BLOB downscaled to ≤256×256 on ingest (Skia, App-side — Core never touches Skia).
/// </summary>
public partial class CharacterMasterViewModel : ObservableObject
{
    private const int MaxImageDimension = 256;

    private readonly string _projectFolder;
    private readonly CharacterStore _store;
    private readonly Dictionary<CharacterMasterRowViewModel, byte[]?> _rowImages = new();

    public ObservableCollection<CharacterMasterRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private string _duplicateMessage = "";

    public bool HasDuplicate => DuplicateMessage.Length > 0;

    partial void OnDuplicateMessageChanged(string value) => OnPropertyChanged(nameof(HasDuplicate));

    public CharacterMasterViewModel(string projectFolder, string? prefillName = null)
    {
        _projectFolder = projectFolder;
        _store = new CharacterStore(Path.Combine(projectFolder, CharacterStore.FileName));

        foreach (var c in _store.LoadAll())
        {
            var row = FromMaster(c);
            Rows.Add(row);
            _rowImages[row] = c.Image;
        }

        if (!string.IsNullOrEmpty(prefillName) && _store.Find(prefillName) is null)
        {
            var row = new CharacterMasterRowViewModel { Name = prefillName };
            Rows.Add(row);
            _rowImages[row] = null;
        }

        Rows.CollectionChanged += (_, _) => DuplicateMessage = "";
    }

    private static CharacterMasterRowViewModel FromMaster(CharacterStore.MasterCharacter c)
    {
        var row = new CharacterMasterRowViewModel
        {
            Name = c.Name,
            Localized = c.Localized,
            Pronunciation = c.Pronunciation,
            Gender = c.Gender,
            Age = c.Age,
            FirstChapter = c.FirstChapter,
            Pronoun = c.Pronoun,
            Comments = c.Comments,
        };
        SetPreview(row, c.Image);
        return row;
    }

    private static void SetPreview(CharacterMasterRowViewModel row, byte[]? png)
    {
        if (png is null || png.Length == 0)
        {
            row.ImagePreview = null;
            return;
        }

        try
        {
            row.ImagePreview = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(png));
        }
        catch
        {
            row.ImagePreview = null;
        }
    }

    public void AddRow()
    {
        var row = new CharacterMasterRowViewModel();
        Rows.Add(row);
        _rowImages[row] = null;
    }

    public void DeleteRow(CharacterMasterRowViewModel row)
    {
        _rowImages.Remove(row);
        Rows.Remove(row);
    }

    /// <summary>Persists every row to characters.db (SPEC §6.3: name required and unique).</summary>
    public bool Save()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Rows)
        {
            var name = row.Name.Trim();
            if (name.Length == 0)
            {
                continue; // an unnamed row is a draft — not persisted
            }

            if (!names.Add(name))
            {
                DuplicateMessage = $"Duplicate name: {name}";
                return false;
            }
        }

        var storedNames = _store.LoadAll().Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var row in Rows)
        {
            var name = row.Name.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            storedNames.Remove(name);
            _store.AddOrUpdate(row.ToMaster(_rowImages.GetValueOrDefault(row)));
        }

        // Names removed from the grid are gone from the store.
        foreach (var removed in storedNames)
        {
            _store.Remove(removed);
        }

        return true;
    }

    /// <summary>Ingests a browsed image: downscales to ≤256×256 (aspect-preserving), stores PNG bytes.</summary>
    public string? ApplyImage(CharacterMasterRowViewModel row, string absolutePath)
    {
        if (!File.Exists(absolutePath))
        {
            return null;
        }

        try
        {
            using var bitmap = new Avalonia.Media.Imaging.Bitmap(absolutePath);
            return ApplyBitmap(row, bitmap);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Ingests a clipboard bitmap: downscales to ≤256×256, stores PNG bytes (§6.3).</summary>
    public string? ApplyBitmap(CharacterMasterRowViewModel row, Avalonia.Media.Imaging.Bitmap bitmap)
    {
        var png = DownscaleAndEncode(bitmap);
        if (png is null)
        {
            return null;
        }

        _rowImages[row] = png;
        SetPreview(row, png);
        return $"png blob {png.Length} bytes";
    }

    /// <summary>Downscales to ≤256×256 preserving aspect ratio; returns PNG bytes (or null on failure).</summary>
    private static byte[]? DownscaleAndEncode(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        try
        {
            var srcW = bitmap.PixelSize.Width;
            var srcH = bitmap.PixelSize.Height;
            var scale = Math.Min(1.0, (double)MaxImageDimension / Math.Max(srcW, srcH));
            var dstW = Math.Max(1, (int)Math.Round(srcW * scale));
            var dstH = Math.Max(1, (int)Math.Round(srcH * scale));

            // Decode through Skia (the App is the only project that may reference SkiaSharp).
            // Avalonia Bitmap → PNG bytes → SKBitmap → canvas-resize → PNG.
            using var enc = new MemoryStream();
            bitmap.Save(enc);
            enc.Position = 0;
            using var src = SKBitmap.Decode(enc);
            if (src is null)
            {
                return null;
            }

            using var dst = new SKBitmap(dstW, dstH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(dst);
            canvas.DrawBitmap(src, new SKRect(0, 0, dstW, dstH));

            using var img = SKImage.FromBitmap(dst);
            using var data = img.Encode(SKEncodedImageFormat.Png, 90);
            using var pngStream = new MemoryStream();
            data.SaveTo(pngStream);
            return pngStream.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
