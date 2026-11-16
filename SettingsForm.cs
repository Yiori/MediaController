using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MediaController;

/// <summary>Janela de configurações em tema escuro.</summary>
public sealed class SettingsForm : Form
{
    private static readonly Color Bg = Color.FromArgb(18, 18, 26);
    private static readonly Color Card = Color.FromArgb(30, 30, 42);
    private static readonly Color CardHover = Color.FromArgb(40, 40, 56);
    private static readonly Color Accent = Color.FromArgb(124, 92, 255);
    private static readonly Color Text1 = Color.FromArgb(235, 235, 245);
    private static readonly Color Text2 = Color.FromArgb(150, 150, 170);

    private readonly AppSettings _working;
    private readonly AdbClient _adb;
    private readonly Dictionary<MediaAction, HotkeyBox> _boxes = new();
    private readonly Label _statusLabel;
    private readonly Panel _statusDot;
    private readonly ComboBox _deviceCombo;
    private readonly CheckBox _startup;
    private readonly CheckBox _notify;

    public AppSettings? Saved { get; private set; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    public SettingsForm(AppSettings current, AdbClient adb)
    {
        _adb = adb;
        _working = new AppSettings
        {
            PreferredDevice = current.PreferredDevice,
            ShowNotifications = current.ShowNotifications,
            Hotkeys = current.Hotkeys.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
        };

        Text = "MediaController — Configurações";
        BackColor = Bg;
        ForeColor = Text1;
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 640);
        KeyPreview = true;

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(24, 20, 24, 20),
        };
        Controls.Add(root);

        // Cabeçalho
        root.Controls.Add(new Label
        {
            Text = "MediaController",
            Font = new Font("Segoe UI Semibold", 20f),
            ForeColor = Text1,
            AutoSize = true,
        });
        root.Controls.Add(new Label
        {
            Text = "Controle a mídia do seu Android via USB com atalhos globais.",
            ForeColor = Text2,
            AutoSize = true,
            Margin = new Padding(3, 0, 3, 16),
        });

        // Status do dispositivo
        var statusCard = MakeCard(56);
        _statusDot = new RoundPanel { Size = new Size(12, 12), Location = new Point(18, 22), BackColor = Color.Gray };
        _statusLabel = new Label { AutoSize = true, Location = new Point(40, 17), ForeColor = Text1, Text = "Verificando…" };
        statusCard.Controls.Add(_statusDot);
        statusCard.Controls.Add(_statusLabel);
        root.Controls.Add(statusCard);

        // Seção atalhos
        root.Controls.Add(SectionTitle("ATALHOS — clique e pressione a combinação"));
        foreach (MediaAction a in Enum.GetValues<MediaAction>())
        {
            var card = MakeCard(52);
            card.Controls.Add(new Label
            {
                Text = MediaActionInfo.Label(a),
                AutoSize = true,
                Location = new Point(18, 15),
                ForeColor = Text1,
            });
            var box = new HotkeyBox(_working.Hotkeys[a]) { Location = new Point(220, 10), Size = new Size(200, 32) };
            var clear = new Button
            {
                Text = "✕",
                FlatStyle = FlatStyle.Flat,
                ForeColor = Text2,
                BackColor = Card,
                Size = new Size(32, 32),
                Location = new Point(426, 10),
                Cursor = Cursors.Hand,
            };
            clear.FlatAppearance.BorderSize = 0;
            clear.FlatAppearance.MouseOverBackColor = CardHover;
            clear.Click += (_, _) => box.SetHotkey(new Hotkey());
            new ToolTip().SetToolTip(clear, "Remover atalho");
            card.Controls.Add(box);
            card.Controls.Add(clear);
            _boxes[a] = box;
            root.Controls.Add(card);
        }

        // Seção geral
        root.Controls.Add(SectionTitle("GERAL"));
        var devCard = MakeCard(52);
        devCard.Controls.Add(new Label { Text = "Dispositivo", AutoSize = true, Location = new Point(18, 15) });
        _deviceCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Bg,
            ForeColor = Text1,
            Location = new Point(220, 12),
            Width = 238,
        };
        devCard.Controls.Add(_deviceCombo);
        root.Controls.Add(devCard);
        LoadDevices();

        var optCard = MakeCard(76);
        _startup = MakeCheck("Iniciar com o Windows", AppSettings.StartWithWindows, 10);
        _notify = MakeCheck("Mostrar notificação a cada comando", _working.ShowNotifications, 40);
        optCard.Controls.Add(_startup);
        optCard.Controls.Add(_notify);
        root.Controls.Add(optCard);

        // Botões
        var btnRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Width = 456,
            Height = 50,
            Margin = new Padding(3, 12, 3, 0),
        };
        var save = MakeButton("Salvar", Accent, Color.White);
        save.Click += (_, _) => SaveAndClose();
        var defaults = MakeButton("Restaurar padrão", Card, Text1);
        defaults.Click += (_, _) =>
        {
            var d = AppSettings.Default();
            foreach (var (a, b) in _boxes) b.SetHotkey(d.Hotkeys[a].Clone());
        };
        btnRow.Controls.Add(save);
        btnRow.Controls.Add(defaults);
        root.Controls.Add(btnRow);

        AcceptButton = null;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = 1;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); // barra de título escura
    }

    public void UpdateDeviceStatus(string text, Color color)
    {
        if (IsDisposed) return;
        _statusLabel.Text = text;
        _statusDot.BackColor = color;
        LoadDevices();
    }

    private void LoadDevices()
    {
        var devs = _adb.AdbPath != null ? SafeList() : new();
        _deviceCombo.Items.Clear();
        _deviceCombo.Items.Add(new DeviceItem(null, "Automático (primeiro conectado)"));
        foreach (var d in devs) _deviceCombo.Items.Add(new DeviceItem(d.Serial, $"{d.Model} ({d.Serial})"));
        if (_working.PreferredDevice != null && devs.All(d => d.Serial != _working.PreferredDevice))
            _deviceCombo.Items.Add(new DeviceItem(_working.PreferredDevice, $"{_working.PreferredDevice} (desconectado)"));
        _deviceCombo.SelectedItem = _deviceCombo.Items.Cast<DeviceItem>()
            .FirstOrDefault(i => i.Serial == _working.PreferredDevice) ?? _deviceCombo.Items[0];
    }

    private List<(string Serial, string State, string Model)> SafeList()
    {
        try { return _adb.ListDevices(); } catch { return new(); }
    }

    private void SaveAndClose()
    {
        // Verifica duplicatas
        var dup = _boxes.Where(kv => !kv.Value.Hotkey.IsEmpty)
            .GroupBy(kv => kv.Value.Hotkey.ToString())
            .FirstOrDefault(g => g.Count() > 1);
        if (dup != null)
        {
            MessageBox.Show($"O atalho \"{dup.Key}\" está atribuído a mais de uma ação.",
                "Atalho duplicado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        foreach (var (a, b) in _boxes) _working.Hotkeys[a] = b.Hotkey;
        _working.PreferredDevice = (_deviceCombo.SelectedItem as DeviceItem)?.Serial;
        _working.ShowNotifications = _notify.Checked;
        try { AppSettings.StartWithWindows = _startup.Checked; } catch { }
        Saved = _working;
        Close();
    }

    // ---------------------------------------------------------------- helpers

    private static Panel MakeCard(int height)
    {
        var p = new RoundPanel { Width = 456, Height = height, BackColor = Card, Margin = new Padding(3, 3, 3, 5) };
        return p;
    }

    private static Label SectionTitle(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI Semibold", 8.5f),
        ForeColor = Accent,
        AutoSize = true,
        Margin = new Padding(3, 18, 3, 6),
    };

    private static CheckBox MakeCheck(string text, bool value, int y) => new()
    {
        Text = text,
        Checked = value,
        AutoSize = true,
        Location = new Point(18, y),
        ForeColor = Text1,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
    };

    private static Button MakeButton(string text, Color bg, Color fg)
    {
        var b = new Button
        {
            Text = text,
            BackColor = bg,
            ForeColor = fg,
            FlatStyle = FlatStyle.Flat,
            AutoSize = true,
            Padding = new Padding(14, 6, 14, 6),
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 10f),
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.2f);
        return b;
    }

    private sealed record DeviceItem(string? Serial, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>Painel com cantos arredondados.</summary>
    private class RoundPanel : Panel
    {
        public RoundPanel() { DoubleBuffered = true; }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int r = Math.Min(12, Math.Min(Width, Height) / 2) * 2;
            using var path = new GraphicsPath();
            path.AddArc(0, 0, r, r, 180, 90);
            path.AddArc(Width - r, 0, r, r, 270, 90);
            path.AddArc(Width - r, Height - r, r, r, 0, 90);
            path.AddArc(0, Height - r, r, r, 90, 90);
            path.CloseFigure();
            Region = new Region(path);
        }
    }

    /// <summary>Campo que captura uma combinação de teclas ao ser focado.</summary>
    private sealed class HotkeyBox : Label
    {
        public Hotkey Hotkey { get; private set; }
        private bool _capturing;

        public HotkeyBox(Hotkey hk)
        {
            Hotkey = hk;
            TextAlign = ContentAlignment.MiddleCenter;
            BackColor = Bg;
            ForeColor = Text1;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 10f);
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Refresh2();
        }

        public void SetHotkey(Hotkey hk) { Hotkey = hk; _capturing = false; Refresh2(); }

        private void Refresh2()
        {
            Text = _capturing ? "Pressione as teclas…" : Hotkey.ToString();
            ForeColor = _capturing ? Accent : (Hotkey.IsEmpty ? Text2 : Text1);
            Invalidate();
        }

        protected override void OnClick(EventArgs e) { Focus(); _capturing = true; Refresh2(); }
        protected override void OnLostFocus(EventArgs e) { _capturing = false; Refresh2(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData) => true;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!_capturing) return base.ProcessCmdKey(ref msg, keyData);
            var key = keyData & Keys.KeyCode;
            if (key == Keys.Escape) { _capturing = false; Refresh2(); return true; }
            if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
                or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey
                or Keys.LMenu or Keys.RMenu)
            {
                // só modificador: mostra prévia
                Text = Preview(keyData) + " …";
                return true;
            }
            bool win = (Control.ModifierKeys & Keys.LWin) != 0 || IsWinDown();
            Hotkey = new Hotkey
            {
                Ctrl = keyData.HasFlag(Keys.Control),
                Alt = keyData.HasFlag(Keys.Alt),
                Shift = keyData.HasFlag(Keys.Shift),
                Win = win,
                Key = key,
            };
            _capturing = false;
            Refresh2();
            return true;
        }

        [DllImport("user32.dll")] private static extern short GetKeyState(int vk);
        private static bool IsWinDown() => (GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0;

        private static string Preview(Keys k)
        {
            var p = new List<string>();
            if (k.HasFlag(Keys.Control)) p.Add("Ctrl");
            if (k.HasFlag(Keys.Alt)) p.Add("Alt");
            if (k.HasFlag(Keys.Shift)) p.Add("Shift");
            if (IsWinDown()) p.Add("Win");
            return string.Join(" + ", p);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(_capturing ? Accent : Color.FromArgb(60, 60, 80), 1.5f);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
