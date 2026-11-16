using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace MediaController;

public enum MediaAction
{
    PlayPause,
    Next,
    Previous,
    Stop,
    VolumeUp,
    VolumeDown,
    Mute,
}

public static class MediaActionInfo
{
    public static string Label(MediaAction a) => a switch
    {
        MediaAction.PlayPause => "Play / Pause",
        MediaAction.Next => "Próxima faixa",
        MediaAction.Previous => "Faixa anterior",
        MediaAction.Stop => "Parar",
        MediaAction.VolumeUp => "Volume +",
        MediaAction.VolumeDown => "Volume −",
        MediaAction.Mute => "Mudo",
        _ => a.ToString(),
    };

    public static int KeyCode(MediaAction a) => a switch
    {
        MediaAction.PlayPause => 85,
        MediaAction.Next => 87,
        MediaAction.Previous => 88,
        MediaAction.Stop => 86,
        MediaAction.VolumeUp => 24,
        MediaAction.VolumeDown => 25,
        MediaAction.Mute => 164,
        _ => 0,
    };
}

/// <summary>Combinação de teclas: modificadores + tecla principal.</summary>
public sealed class Hotkey
{
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public Keys Key { get; set; } = Keys.None;

    [JsonIgnore]
    public bool IsEmpty => Key == Keys.None;

    public override string ToString()
    {
        if (IsEmpty) return "(nenhum)";
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    private static string KeyName(Keys k) => k switch
    {
        Keys.Space => "Espaço",
        Keys.Left => "←",
        Keys.Right => "→",
        Keys.Up => "↑",
        Keys.Down => "↓",
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (k - Keys.D0))).ToString(),
        _ => k.ToString(),
    };

    public Hotkey Clone() => (Hotkey)MemberwiseClone();
}

public sealed class AppSettings
{
    public Dictionary<MediaAction, Hotkey> Hotkeys { get; set; } = new();
    public string? PreferredDevice { get; set; }
    public bool ShowNotifications { get; set; } = false;

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaController");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static string DataDir => Dir;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Default() => new()
    {
        Hotkeys = new()
        {
            [MediaAction.PlayPause] = new() { Ctrl = true, Alt = true, Key = Keys.Space },
            [MediaAction.Next] = new() { Ctrl = true, Alt = true, Key = Keys.Right },
            [MediaAction.Previous] = new() { Ctrl = true, Alt = true, Key = Keys.Left },
            [MediaAction.VolumeUp] = new() { Ctrl = true, Alt = true, Key = Keys.Up },
            [MediaAction.VolumeDown] = new() { Ctrl = true, Alt = true, Key = Keys.Down },
            [MediaAction.Stop] = new(),
            [MediaAction.Mute] = new(),
        },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts);
                if (s != null)
                {
                    foreach (MediaAction a in Enum.GetValues<MediaAction>())
                        s.Hotkeys.TryAdd(a, new Hotkey());
                    return s;
                }
            }
        }
        catch { /* arquivo corrompido -> padrão */ }
        return Default();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
    }

    // ---- Iniciar com o Windows ----
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "MediaController";

    public static bool StartWithWindows
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(RunName) != null;
        }
        set
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                          ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue(RunName, false);
        }
    }
}
