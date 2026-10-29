namespace Twentea.Core;

/// <summary>Owns the live settings, the active preset and the user's saved presets. Persists to settings.json.</summary>
public sealed class SettingsStore
{
    private sealed class Snapshot
    {
        public string ActivePresetID { get; set; } = Presets.DefaultId;
        public AppSettings Settings { get; set; } = new();
        public List<Preset> UserPresets { get; set; } = [];
        public bool? IsOn { get; set; }
    }

    private readonly string _file;
    private bool _isOn;

    public AppSettings Settings { get; private set; }
    public string ActivePresetID { get; private set; }
    public List<Preset> UserPresets { get; }
    public bool IsFirstLaunch { get; private set; }

    /// <summary>Raised after any change is saved.</summary>
    public event Action? Changed;

    /// <summary>Master power switch. Kept outside presets so switching presets never turns the app on or off.</summary>
    public bool IsOn
    {
        get => _isOn;
        set { _isOn = value; Save(); }
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Twentea");

    public SettingsStore(string? directory = null)
    {
        directory ??= DefaultDirectory;
        Directory.CreateDirectory(directory);
        _file = Path.Combine(directory, "settings.json");

        Snapshot? snap = null;
        try { if (File.Exists(_file)) snap = Json.Read<Snapshot>(File.ReadAllText(_file)); }
        catch (System.Text.Json.JsonException) { snap = null; }

        if (snap is not null)
        {
            Settings = snap.Settings;
            ActivePresetID = snap.ActivePresetID;
            UserPresets = snap.UserPresets;
            _isOn = snap.IsOn ?? true;
        }
        else
        {
            var preset = Presets.BuiltIns.First(p => p.Id == Presets.DefaultId);
            Settings = Json.Clone(preset.Settings);
            ActivePresetID = preset.Id;
            UserPresets = [];
            _isOn = true;
            IsFirstLaunch = true;
            Save();
        }
    }

    public IEnumerable<Preset> AllPresets => Presets.BuiltIns.Concat(UserPresets);

    public Preset? ActivePreset => AllPresets.FirstOrDefault(p => p.Id == ActivePresetID);

    /// <summary>Settings differ from the preset they came from.</summary>
    public bool IsCustom => ActivePreset is not { } p || !Json.Same(p.Settings, Settings);

    public string ActiveTitle => (ActivePreset?.Name ?? "Custom") + (IsCustom ? " · Custom" : "");

    /// <summary>Changes the live settings and saves them.</summary>
    public void Update(Action<AppSettings> change)
    {
        change(Settings);
        Save();
    }

    public void Apply(string presetId)
    {
        if (AllPresets.FirstOrDefault(p => p.Id == presetId) is not { } preset) return;
        ActivePresetID = preset.Id;
        Settings = Json.Clone(preset.Settings);
        Save();
    }

    public void ResetToPreset()
    {
        if (ActivePreset is { } preset) { Settings = Json.Clone(preset.Settings); Save(); }
    }

    public Preset SaveAsPreset(string name, string symbol = "star")
    {
        var preset = new Preset { Id = NewUserId(), Name = name, Symbol = symbol, Settings = Json.Clone(Settings) };
        UserPresets.Add(preset);
        ActivePresetID = preset.Id;
        Save();
        return preset;
    }

    /// <summary>Overwrites the active user preset with the current settings.</summary>
    public void UpdateActivePreset()
    {
        if (UserPresets.FirstOrDefault(p => p.Id == ActivePresetID) is not { } preset) return;
        preset.Settings = Json.Clone(Settings);
        Save();
    }

    public void RenamePreset(string id, string name)
    {
        if (UserPresets.FirstOrDefault(p => p.Id == id) is not { } preset) return;
        preset.Name = name;
        Save();
    }

    public void DuplicatePreset(string id)
    {
        if (AllPresets.FirstOrDefault(p => p.Id == id) is not { } source) return;
        var copy = Json.Clone(source);
        copy.Id = NewUserId();
        copy.Name = $"{source.Name} copy";
        copy.BuiltIn = false;
        UserPresets.Add(copy);
        Save();
    }

    public void DeletePreset(string id)
    {
        UserPresets.RemoveAll(p => p.Id == id);
        if (ActivePresetID == id) ActivePresetID = Presets.DefaultId;
        Save();
    }

    /// <summary>A .twentea file: one preset as JSON, readable by both apps.</summary>
    public string ExportPreset(string id) =>
        Json.Write(AllPresets.FirstOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException(id));

    public void ImportPreset(string json)
    {
        var preset = Json.Read<Preset>(json);
        preset.Id = NewUserId();
        preset.BuiltIn = false;
        UserPresets.Add(preset);
        Save();
    }

    public void MarkLaunched() => IsFirstLaunch = false;

    private static string NewUserId() => "user-" + Guid.NewGuid().ToString("N")[..8];

    private void Save()
    {
        var snap = new Snapshot { ActivePresetID = ActivePresetID, Settings = Settings, UserPresets = UserPresets, IsOn = _isOn };
        var temp = _file + ".tmp";
        File.WriteAllText(temp, Json.Write(snap));
        File.Move(temp, _file, overwrite: true);
        Changed?.Invoke();
    }
}
