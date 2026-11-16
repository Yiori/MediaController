namespace MediaController;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "MediaController_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("O MediaController já está em execução (veja a bandeja do sistema).",
                "MediaController", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
