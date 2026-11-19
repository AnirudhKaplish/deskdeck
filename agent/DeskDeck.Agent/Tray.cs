using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DeskDeck.Agent;

/// <summary>System tray icon: status, settings, logs, start with Windows, quit.</summary>
public sealed class Tray : IHostedService
{
    private readonly string SettingsUrl;

    private readonly IHostApplicationLifetime _life;
    private readonly AdbReverse _adb;
    private Thread? _thread;
    private ApplicationContext? _ctx;
    private Control? _invoker;
    private NotifyIcon? _icon;

    public Tray(IHostApplicationLifetime life, AdbReverse adb, AgentEndpoint endpoint)
    {
        SettingsUrl = endpoint.SettingsUrl;
        _life = life;
        _adb = adb;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "tray" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        try { _invoker?.BeginInvoke(() => { if (_icon != null) _icon.Visible = false; _ctx?.ExitThread(); }); } catch { }
        return Task.CompletedTask;
    }

    private void Run()
    {
        Application.EnableVisualStyles();
        // Invisible control used to marshal calls onto this UI thread (its handle is created here).
        var host = new Control();
        _ = host.Handle;
        _invoker = host;
        _ctx = new ApplicationContext();

        using var iconStream = AppPaths.Resource("deskdeck.ico");
        var status = new ToolStripMenuItem { Enabled = false };
        var autostart = new ToolStripMenuItem("Start with Windows");
        autostart.Click += (_, _) => { ToggleAutostart(); autostart.Checked = File.Exists(AppPaths.StartupShortcut); };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"DeskDeck {AppPaths.Version}") { Enabled = false });
        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OpenUrl(SettingsUrl)) { Font = new Font(menu.Font, FontStyle.Bold) });
        // cancelled while Windows is ending the session (no adb is started then, for at most a minute): nothing to show
        menu.Items.Add("Open panel on phone", null, async (_, _) => { try { await _adb.OpenPanelAsync(); } catch (OperationCanceledException) { } });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Edit config.json", null, (_, _) => Open("notepad.exe", AppPaths.ConfigPath));
        menu.Items.Add("Open log", null, (_, _) => Open("notepad.exe", AppPaths.LogPath));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(autostart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => _life.StopApplication());
        menu.Opening += (_, _) =>
        {
            status.Text = _adb.Connected ? $"Phone connected ({_adb.Devices})" : "No phone connected";
            autostart.Checked = File.Exists(AppPaths.StartupShortcut);
        };

        _icon = new NotifyIcon
        {
            Icon = iconStream is null ? SystemIcons.Application : new Icon(iconStream, 32, 32),
            Text = "DeskDeck",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OpenUrl(SettingsUrl);
        _adb.StateChanged += () =>
        {
            try { host.BeginInvoke(() => _icon.Text = _adb.Connected ? "DeskDeck — phone connected" : "DeskDeck — waiting for phone"); } catch { }
        };

        // Ask for a GitHub star once the phone is connected (over USB or Wi-Fi), unless it is starred already or was answered.
        var starTimer = new System.Windows.Forms.Timer { Interval = 8000 };
        starTimer.Tick += async (_, _) =>
        {
            if (!(_adb.Connected || ApprovalStore.PhoneConnected) || !StarPrompt.Due()) return;
            starTimer.Stop();
            try
            {
                if (await StarPrompt.IsStarredAsync() == true) { StarPrompt.Remember("starred"); return; }
                StarPrompt.Ask(_icon);
            }
            catch { }
        };
        starTimer.Start();

        Application.Run(_ctx);
        _icon.Visible = false;
        _icon.Dispose();
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private static void Open(string app, string path)
    {
        try { Process.Start(new ProcessStartInfo(app, $"\"{path}\"") { UseShellExecute = true }); } catch { }
    }

    /// <summary>Adds/removes a Startup-folder shortcut (only when the user clicks the menu item).</summary>
    private static void ToggleAutostart()
    {
        try
        {
            if (File.Exists(AppPaths.StartupShortcut)) { File.Delete(AppPaths.StartupShortcut); return; }
            var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic lnk = shell.CreateShortcut(AppPaths.StartupShortcut);
            lnk.TargetPath = Environment.ProcessPath;
            lnk.WorkingDirectory = Path.GetDirectoryName(Environment.ProcessPath);
            lnk.Description = "DeskDeck agent";
            lnk.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not change the startup shortcut: " + ex.Message, "DeskDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

/// <summary>One-time "star DeskDeck on GitHub" popup. The star state is read through the user's own GitHub CLI (<c>gh</c>) when it is signed in; DeskDeck itself sends nothing.</summary>
internal static class StarPrompt
{
    private const string Repo = "AnirudhKaplish/deskdeck";
    private static string StateFile => Path.Combine(AppPaths.ConfigDir, "star-prompt.txt");

    /// <summary>False once the user starred, clicked "Already starred" or opened the page; after "Not now", true again after 14 days.</summary>
    public static bool Due()
    {
        try
        {
            if (!File.Exists(StateFile)) return true;
            var parts = File.ReadAllText(StateFile).Split(' ');
            return parts[0] == "later" && long.TryParse(parts.ElementAtOrDefault(1), out var until) && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= until;
        }
        catch { return false; }
    }

    public static void Remember(string what)
    {
        try { File.WriteAllText(StateFile, what == "later" ? $"later {DateTimeOffset.UtcNow.AddDays(14).ToUnixTimeSeconds()}" : what); } catch { }
    }

    /// <summary>true = starred, false = not starred, null = unknown (gh missing or signed out).</summary>
    public static async Task<bool?> IsStarredAsync()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("gh", $"api -i user/starred/{Repo}")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return null; }
            var text = await output;
            if (text.StartsWith("HTTP/") && text.Contains(" 204")) return true;
            if (text.StartsWith("HTTP/") && text.Contains(" 404")) return false;
            return null;
        }
        catch { return null; }
    }

    public static void Ask(NotifyIcon? icon)
    {
        using var form = new Form
        {
            Text = "DeskDeck", StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, TopMost = true, ClientSize = new Size(420, 170), ShowInTaskbar = true,
        };
        if (icon?.Icon is { } ic) form.Icon = ic;
        form.Controls.Add(new Label
        {
            Text = "Your phone is connected. \u2728\n\nIf DeskDeck is useful to you, a star on GitHub helps other people find it. It takes one click.",
            Left = 20, Top = 16, Width = 380, Height = 90, Font = new Font("Segoe UI", 10f),
        });
        var star = new Button { Text = "\u2605 Star on GitHub", Left = 20, Top = 120, Width = 150, Height = 32, DialogResult = DialogResult.Yes };
        var done = new Button { Text = "Already starred", Left = 180, Top = 120, Width = 120, Height = 32, DialogResult = DialogResult.OK };
        var later = new Button { Text = "Not now", Left = 310, Top = 120, Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
        form.Controls.AddRange(new Control[] { star, done, later });
        form.AcceptButton = star; form.CancelButton = later;
        switch (form.ShowDialog())
        {
            case DialogResult.Yes:
                Remember("opened");
                try { Process.Start(new ProcessStartInfo($"https://github.com/{Repo}") { UseShellExecute = true }); } catch { }
                break;
            case DialogResult.OK: Remember("starred"); break;
            default: Remember("later"); break;
        }
    }
}
