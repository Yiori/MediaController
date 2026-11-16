using System.Runtime.InteropServices;

namespace MediaController;

/// <summary>Registra atalhos globais via Win32 RegisterHotKey.</summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    private readonly Dictionary<int, MediaAction> _registered = new();
    public event Action<MediaAction>? HotkeyPressed;

    public HotkeyManager() => CreateHandle(new CreateParams());

    /// <summary>Registra todos os atalhos. Retorna a lista de ações que falharam (conflito).</summary>
    public List<MediaAction> RegisterAll(Dictionary<MediaAction, Hotkey> map)
    {
        UnregisterAll();
        var failed = new List<MediaAction>();
        int id = 1;
        foreach (var (action, hk) in map)
        {
            if (hk.IsEmpty) continue;
            // Volume permite repetição ao segurar; demais não.
            bool repeat = action is MediaAction.VolumeUp or MediaAction.VolumeDown;
            uint mods = (hk.Ctrl ? MOD_CONTROL : 0) | (hk.Alt ? MOD_ALT : 0) |
                        (hk.Shift ? MOD_SHIFT : 0) | (hk.Win ? MOD_WIN : 0) |
                        (repeat ? 0 : MOD_NOREPEAT);
            if (RegisterHotKey(Handle, id, mods, (uint)hk.Key))
                _registered[id] = action;
            else
                failed.Add(action);
            id++;
        }
        return failed;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.Keys) UnregisterHotKey(Handle, id);
        _registered.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && _registered.TryGetValue((int)m.WParam, out var action))
            HotkeyPressed?.Invoke(action);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
