namespace MatrixRain;

internal static class Program
{
    private enum Mode { Fullscreen, Preview, Config }

    [STAThread]
    private static int Main(string[] args)
    {
        // Single call that pulls in high-DPI mode, default font, visual styles
        // and SetCompatibleTextRenderingDefault from the csproj application
        // properties (e.g. <ApplicationHighDpiMode>).
        ApplicationConfiguration.Initialize();

        var (mode, hwnd) = ParseArgs(args);

        switch (mode)
        {
            case Mode.Config:
                using (var dlg = new ConfigForm())
                    dlg.ShowDialog();
                return 0;

            case Mode.Preview:
                if (hwnd != IntPtr.Zero) RunPreview(hwnd);
                return 0;

            default:
                RunFullscreen();
                return 0;
        }
    }

    // Windows passes screensaver flags case-insensitively, with either '/' or '-' prefix,
    // and HWND can be a separate arg (`/p 12345`) or attached (`/p:12345`).
    private static (Mode mode, IntPtr hwnd) ParseArgs(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            return (Mode.Fullscreen, IntPtr.Zero);

        var first = args[0].Trim();
        if (first[0] != '/' && first[0] != '-')
            return (Mode.Fullscreen, IntPtr.Zero);

        var rest = first.Substring(1);
        var colonIdx = rest.IndexOf(':');
        var key = (colonIdx >= 0 ? rest.Substring(0, colonIdx) : rest).ToLowerInvariant();
        var inlineVal = colonIdx >= 0 ? rest.Substring(colonIdx + 1) : null;

        switch (key)
        {
            case "s":
                return (Mode.Fullscreen, IntPtr.Zero);
            case "c":
                return (Mode.Config, IntPtr.Zero);
            case "a":
                // Legacy "change password" flag — Windows still passes it on some setups.
                // We have no password concept, so just open settings instead.
                return (Mode.Config, IntPtr.Zero);
            case "p":
                var hwndStr = inlineVal ?? (args.Length >= 2 ? args[1] : null);
                if (long.TryParse(hwndStr, out var h))
                    return (Mode.Preview, new IntPtr(h));
                return (Mode.Preview, IntPtr.Zero);
            default:
                return (Mode.Fullscreen, IntPtr.Zero);
        }
    }

    private static void RunFullscreen()
    {
        var config = Config.Load();
        var forms = new List<MatrixRainForm>();
        foreach (var screen in Screen.AllScreens)
        {
            var f = new MatrixRainForm(screen.Bounds, isPreview: false, config);
            f.ExitRequested += (_, _) =>
            {
                if (Application.MessageLoop) Application.Exit();
            };
            forms.Add(f);
        }
        // The first form anchors the message loop. Application.Exit() (called
        // by any form's ExitRequested handler) closes every form, which makes
        // Application.Run return.
        for (int i = 1; i < forms.Count; i++) forms[i].Show();
        Application.Run(forms[0]);
    }

    private static void RunPreview(IntPtr parent)
    {
        if (!Native.IsWindow(parent)) return;
        if (!Native.GetClientRect(parent, out var rect)) return;

        var size = new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
        if (size.Width <= 0 || size.Height <= 0) return;

        var config = Config.Load();
        var f = new MatrixRainForm(new Rectangle(0, 0, size.Width, size.Height), isPreview: true, config);
        f.ParentHwndForCheck = parent;

        // Force handle creation before reparenting so SetParent has a real HWND to attach.
        var handle = f.Handle;
        Native.SetParent(handle, parent);
        int style = Native.GetWindowLong(handle, Native.GWL_STYLE);
        style = (style | Native.WS_CHILD) & ~Native.WS_POPUP;
        Native.SetWindowLong(handle, Native.GWL_STYLE, style);
        f.Location = Point.Empty;
        f.Size = size;

        Application.Run(f);
    }
}
