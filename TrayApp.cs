using System.Drawing.Drawing2D;

namespace MediaController;

public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly AdbClient _adb = new();
    private readonly HotkeyManager _hotkeys = new();
    private readonly SynchronizationContext _ui;
    private AppSettings _settings;
    private SettingsForm? _settingsForm;
    private readonly ToolStripMenuItem _statusItem;

    public TrayApp()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settings = AppSettings.Load();
        _adb.PreferredDevice = _settings.PreferredDevice;

        _statusItem = new ToolStripMenuItem("Iniciando…") { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        foreach (var a in new[] { MediaAction.PlayPause, MediaAction.Previous, MediaAction.Next,
                                  MediaAction.VolumeDown, MediaAction.VolumeUp })
        {
            var act = a;
            menu.Items.Add(MediaActionInfo.Label(a), null, (_, _) => Trigger(act));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Configurações…", null, (_, _) => ShowSettings());
        menu.Items.Add("Sair", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = MakeIcon(Color.Gray),
            Text = "MediaController",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _hotkeys.HotkeyPressed += Trigger;
        _adb.StateChanged += () => _ui.Post(_ => UpdateStatus(), null);
        ApplyHotkeys();

        _ = InitAdbAsync();
    }

    private async Task InitAdbAsync()
    {
        try
        {
            await _adb.EnsureAdbAsync(new Progress<string>(msg =>
            {
                _statusItem.Text = msg;
                _tray.ShowBalloonTip(3000, "MediaController", msg, ToolTipIcon.Info);
            }));
            _adb.StartMonitoring();
        }
        catch (Exception ex)
        {
            _statusItem.Text = "Erro ao obter adb";
            MessageBox.Show("Não foi possível obter o adb:\n" + ex.Message, "MediaController",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Trigger(MediaAction action)
    {
        Task.Run(() =>
        {
            bool ok = _adb.Send(action);
            if (!ok)
                _ui.Post(_ => _tray.ShowBalloonTip(2000, "MediaController",
                    "Nenhum celular conectado/autorizado.", ToolTipIcon.Warning), null);
            else if (_settings.ShowNotifications)
                _ui.Post(_ => _tray.ShowBalloonTip(800, "MediaController",
                    MediaActionInfo.Label(action), ToolTipIcon.None), null);
        });
    }

    private void UpdateStatus()
    {
        (string text, Color color) = _adb.State switch
        {
            DeviceState.Connected => ($"Conectado: {_adb.CurrentModel}", Color.FromArgb(46, 204, 113)),
            DeviceState.Unauthorized => ("Autorize a depuração USB no celular", Color.FromArgb(241, 196, 15)),
            DeviceState.Disconnected => ("Nenhum celular conectado", Color.FromArgb(231, 76, 60)),
            _ => ("adb indisponível", Color.Gray),
        };
        _statusItem.Text = text;
        _tray.Text = ("MediaController — " + text) is var t && t.Length > 63 ? t[..63] : t;
        var old = _tray.Icon;
        _tray.Icon = MakeIcon(color);
        old?.Dispose();
        _settingsForm?.UpdateDeviceStatus(text, color);

        if (_adb.State == DeviceState.Unauthorized)
            _tray.ShowBalloonTip(4000, "MediaController",
                "Aceite o pedido de \"Permitir depuração USB\" na tela do celular.", ToolTipIcon.Info);
    }

    private void ApplyHotkeys()
    {
        var failed = _hotkeys.RegisterAll(_settings.Hotkeys);
        if (failed.Count > 0)
            _tray.ShowBalloonTip(4000, "Conflito de atalhos",
                "Não foi possível registrar: " + string.Join(", ", failed.Select(MediaActionInfo.Label)) +
                ". Outro programa já usa essa combinação.", ToolTipIcon.Warning);
    }

    private void ShowSettings()
    {
        if (_settingsForm != null) { _settingsForm.Activate(); return; }

        _hotkeys.UnregisterAll(); // permite capturar as combinações livremente
        _settingsForm = new SettingsForm(_settings, _adb);
        _settingsForm.FormClosed += (_, _) =>
        {
            if (_settingsForm.Saved != null)
            {
                _settings = _settingsForm.Saved;
                _settings.Save();
                _adb.PreferredDevice = _settings.PreferredDevice;
                _adb.ForceRefresh();
            }
            _settingsForm = null;
            ApplyHotkeys();
        };
        _settingsForm.Show();
        UpdateStatus();
    }

    /// <summary>Gera um ícone circular com símbolo de play e indicador de status.</summary>
    private static Icon MakeIcon(Color status)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var bg = new LinearGradientBrush(new Rectangle(0, 0, 32, 32),
                Color.FromArgb(124, 92, 255), Color.FromArgb(0, 200, 255), 45f);
            g.FillEllipse(bg, 1, 1, 30, 30);
            g.FillPolygon(Brushes.White, new[] { new Point(12, 9), new Point(12, 23), new Point(23, 16) });
            using var sb = new SolidBrush(status);
            g.FillEllipse(Brushes.White, 20, 20, 12, 12);
            g.FillEllipse(sb, 22, 22, 8, 8);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _hotkeys.Dispose();
        _adb.Dispose();
        base.ExitThreadCore();
    }
}
