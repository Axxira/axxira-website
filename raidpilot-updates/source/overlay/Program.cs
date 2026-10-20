using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace RaidPilot.Overlay;

internal static class Program
{
    private const string SingleInstanceMutexName =
        "Local\\RaidPilot.Overlay.v35.RAIDREADY.6DE13A94";

    internal static bool PreserveScreenshotsRequested(IEnumerable<string> arguments) =>
        arguments.Contains("--preserve-screenshots", StringComparer.Ordinal);

    [STAThread]
    private static void Main()
    {
        // An interface update must not incidentally purge the user's manual screenshots.
        // The installed shortcut opts out for this process; no system setting is changed.
        if (PreserveScreenshotsRequested(Environment.GetCommandLineArgs()))
            Environment.SetEnvironmentVariable("RAIDPILOT_PRESERVE_SCREENSHOTS_ONCE", "1");
        using var singleInstance = CreateSingleInstanceMutex(out var isFirstInstance);

        if (!isFirstInstance)
        {
            ShowExistingInstance();
            return;
        }

        StopLegacyInstances();

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new OverlayForm());
    }

    private static Mutex CreateSingleInstanceMutex(out bool isFirstInstance)
    {
        // The overlay may be started from the desktop or by an updater running
        // in a restricted Windows context. A default named-mutex ACL created
        // by one context can reject the other before any window is constructed.
        // The mutex contains no user data, so cross-context access is safe here.
        var security = new MutexSecurity();
        security.AddAccessRule(new MutexAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null),
            MutexRights.FullControl,
            AccessControlType.Allow));

        return MutexAcl.Create(
            initiallyOwned: true,
            name: SingleInstanceMutexName,
            createdNew: out isFirstInstance,
            mutexSecurity: security);
    }

    private static void ShowExistingInstance()
    {
        // A second shortcut launch normally means "show the overlay". The
        // first process can still be creating its handle, so briefly retry
        // before returning instead of leaving the user with no visible app.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            nint existingWindow = nint.Zero;
            NativeMethods.EnumWindows((window, _) =>
            {
                var title = new StringBuilder(128);
                if (NativeMethods.GetWindowText(window, title, title.Capacity) > 0
                    && string.Equals(title.ToString(), "RaidPilot Overlay", StringComparison.Ordinal))
                {
                    NativeMethods.GetWindowThreadProcessId(window, out var processId);
                    if (processId != 0 && processId != Environment.ProcessId)
                    {
                        existingWindow = window;
                        return false;
                    }
                }
                return true;
            }, nint.Zero);

            if (existingWindow != nint.Zero)
            {
                NativeMethods.ShowWindow(existingWindow, NativeMethods.SwShowNoActivate);
                NativeMethods.SetWindowPos(
                    existingWindow,
                    NativeMethods.HwndTopMost,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SwpNoMove
                    | NativeMethods.SwpNoSize
                    | NativeMethods.SwpNoActivate
                    | NativeMethods.SwpShowWindow);
                return;
            }

            Thread.Sleep(50);
        }
    }

    private static void StopLegacyInstances()
    {
        var currentProcessId = Environment.ProcessId;
        var legacyProcessIds = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("RaidPilot.Overlay"))
        {
            using (process)
            {
                if (process.Id == currentProcessId)
                {
                    continue;
                }

                legacyProcessIds.Add(process.Id);
            }
        }

        // Early development builds could be hosted by dotnet.exe, so the
        // process name alone is insufficient. Their private top-level window
        // title is stable and lets the updater close only RaidPilot instances.
        NativeMethods.EnumWindows((window, _) =>
        {
            var title = new StringBuilder(128);
            if (NativeMethods.GetWindowText(window, title, title.Capacity) > 0
                && string.Equals(title.ToString(), "RaidPilot Overlay", StringComparison.Ordinal))
            {
                NativeMethods.GetWindowThreadProcessId(window, out var processId);
                if (processId != 0 && processId != currentProcessId)
                {
                    legacyProcessIds.Add((int)processId);
                }
            }
            return true;
        }, nint.Zero);

        foreach (var processId in legacyProcessIds)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
            catch (Exception)
            {
                // A legacy process can exit between enumeration and cleanup.
                // The new instance still starts and remains independently usable.
            }
        }
    }
}
