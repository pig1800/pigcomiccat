using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PigComic.Core.Project;

namespace PigComic.App.ViewModels;

/// <summary>One master-character row with editable fields (SPEC §6.3).</summary>
public partial class CharacterMasterRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    /// <summary>Project-folder-relative image path, e.g. "characters/a3f09c12.png".</summary>
    [ObservableProperty]
    private string _image = "";

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

    /// <summary>Absolute path of the current image, for the preview thumbnail.</summary>
    [ObservableProperty]
    private string _imageAbsolute = "";

    /// <summary>Thumbnail shown in the image cell (loaded from <see cref="ImageAbsolute"/>).</summary>
    [ObservableProperty]
    private Avalonia.Media.IImage? _imagePreview;

    public string ImageRelative => Image;

    public CharacterList.MasterCharacter ToMaster()
        => new(Name, Image, Gender, Age, FirstChapter, Pronoun, Comments);
}

/// <summary>
/// M7.1 master character editor (SPEC §16): a grid of §6.3 rows over characters.json.
/// The window code-behind drives clipboard-paste and the file picker; this VM owns the
/// rows, add/delete, uniqueness feedback and persistence. A prefill name seeds a new row.
/// </summary>
public partial class CharacterMasterViewModel : ObservableObject
{
    private readonly string _projectFolder;
    private readonly CharacterList _list;
    private readonly HashSet<string> _originalNames = new(StringComparer.Ordinal);

    public ObservableCollection<CharacterMasterRowViewModel> Rows { get; } = [];

    /// <summary>Set when a row's name collides with a DIFFERENT row (inline rejection).</summary>
    [ObservableProperty]
    private string _duplicateMessage = "";

    public bool HasDuplicate => DuplicateMessage.Length > 0;

    partial void OnDuplicateMessageChanged(string value) => OnPropertyChanged(nameof(HasDuplicate));

    public CharacterMasterViewModel(string projectFolder, string? prefillName = null)
    {
        _projectFolder = projectFolder;
        Directory.CreateDirectory(Path.Combine(projectFolder, "characters"));

        var path = Path.Combine(projectFolder, CharacterList.FileName);
        _list = File.Exists(path) ? CharacterList.Load(path) : CharacterList.CreateNew(path);

        foreach (var c in _list.Characters)
        {
            var row = FromMaster(c);
            Rows.Add(row);
            _originalNames.Add(c.Name);
            LoadPreview(row);
        }

        if (!string.IsNullOrEmpty(prefillName) && !_originalNames.Contains(prefillName))
        {
            Rows.Add(FromMaster(new CharacterList.MasterCharacter(prefillName, "", "", "", "", "", "")));
        }

        Rows.CollectionChanged += (_, _) => DuplicateMessage = "";
    }

    private static CharacterMasterRowViewModel FromMaster(CharacterList.MasterCharacter c) => new()
    {
        Name = c.Name,
        Image = c.Image,
        Gender = c.Gender,
        Age = c.Age,
        FirstChapter = c.FirstChapter,
        Pronoun = c.Pronoun,
        Comments = c.Comments,
    };

    /// <summary>Loads the thumbnail for an existing row's stored image (best-effort).</summary>
    private void LoadPreview(CharacterMasterRowViewModel row)
    {
        if (string.IsNullOrEmpty(row.Image))
        {
            return;
        }

        var abs = Path.Combine(_projectFolder, row.Image);
        row.ImageAbsolute = abs;
        if (File.Exists(abs))
        {
            try
            {
                row.ImagePreview = new Avalonia.Media.Imaging.Bitmap(abs);
            }
            catch
            {
                row.ImagePreview = null;
            }
        }
    }

    public void AddRow()
    {
        Rows.Add(new CharacterMasterRowViewModel());
    }

    /// <summary>Removes a row (the window confirms first).</summary>
    public void DeleteRow(CharacterMasterRowViewModel row)
    {
        Rows.Remove(row);
    }

    /// <summary>Persists every row to characters.json (SPEC §6.3: name required and unique).</summary>
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

        // Names removed from the grid are gone from the master list.
        foreach (var removed in _originalNames.Where(n => !names.Contains(n)))
        {
            _list.Remove(removed);
        }

        foreach (var row in Rows)
        {
            var name = row.Name.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            _list.AddOrUpdate(new CharacterList.MasterCharacter(
                name, row.Image.Trim(), row.Gender.Trim(), row.Age.Trim(),
                row.FirstChapter.Trim(), row.Pronoun.Trim(), row.Comments.Trim()));
        }

        return true;
    }

    /// <summary>Records a picked/browsed image for a row (re-encoded to PNG, §6.3).</summary>
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

    /// <summary>Stores a clipboard-pasted image as PNG in characters/ (SPEC §6.3).</summary>
    public string? ApplyBitmap(CharacterMasterRowViewModel row, Avalonia.Media.Imaging.Bitmap bitmap)
    {
        var dir = Path.Combine(_projectFolder, "characters");
        Directory.CreateDirectory(dir);
        var name = string.IsNullOrWhiteSpace(row.Name) ? "c" : Sanitize(row.Name);
        var slug = name.Length > 20 ? name[..20] : name;
        var fileName = $"{slug}-{Guid.NewGuid().ToString("N")[..8]}.png";
        var target = Path.Combine(dir, fileName);

        try
        {
            bitmap.Save(target);
        }
        catch
        {
            return null;
        }

        row.Image = Path.Combine("characters", fileName);
        row.ImageAbsolute = target;
        row.ImagePreview = bitmap;
        return target;
    }

    private static string Sanitize(string s)
    {
        var chars = s.Where(c => char.IsLetterOrDigit(c)).ToArray();
        return chars.Length == 0 ? "c" : new string(chars);
    }
}
