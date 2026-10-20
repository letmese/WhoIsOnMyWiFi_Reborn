using System;
using System.IO;
using System.Windows.Forms;

namespace WhoIsOnMyWiFi_Reborn
{
    /// <summary>
    /// Entry point.
    ///
    /// FIX (why the app could "silently not work"):
    ///  - Global exception handlers were missing. Any unhandled exception (bad network state,
    ///    old database file, missing runtime) killed the process with NO message and NO log,
    ///    which looks exactly like "the app does nothing".
    ///  - Now every exception is written to %LocalAppData%\WhoIsOnMyWiFi_Reborn\error.log
    ///    and reported with a visible dialog, so problems can actually be diagnosed.
    /// </summary>
    internal static class Program
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int RegisterWindowMessage(string lpString);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private const int HWND_BROADCAST = 0xffff;
        public static readonly int WM_SHOWME = RegisterWindowMessage("WhoIsOnMyWiFi_Reborn_Show");

        private static System.Threading.Mutex _singleInstanceMutex;

        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                AttachConsole(-1); // Attach to parent cmd/powershell console only when running CLI commands
            }

            // CLI commands should run directly without single-instance mutex
            bool isCli = args != null && args.Length > 0 && 
                (args[0] == "--test-scan" || args[0] == "--import-file" || args[0] == "--generate-icon" || args[0] == "--icon" || args[0] == "--help");
            if (!isCli)
            {
                try
                {
                    bool createdNew;
                    _singleInstanceMutex = new System.Threading.Mutex(true, @"Local\WhoIsOnMyWiFi_Reborn_AppMutex", out createdNew);
                    if (!createdNew)
                    {
                        // Existing instance is running: bring it to front and exit cleanly
                        PostMessage((IntPtr)HWND_BROADCAST, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                        return;
                    }
                }
                catch
                {
                    // If mutex creation encounters any permission boundary, safely proceed
                }
            }

            if (args != null && args.Length > 0 && (args[0] == "--generate-icon" || args[0] == "--icon"))
            {
                string target = args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                IconGenerator.GenerateAppIcon(target);
                Console.WriteLine("Icon generated at: " + target);
                return;
            }

            if (args != null && args.Length > 0 && args[0] == "--test-scan")
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Testing active SendARP scan engine...");
                var progress = new Progress<string>(msg =>
                {
                    Console.WriteLine("[SCAN PROGRESS] " + msg);
                    sb.AppendLine("[SCAN PROGRESS] " + msg);
                });
                var opt = new NetworkScanner.ScanOptions { Ping = true, NetBios = true };
                var devices = NetworkScanner.ScanAsync("", opt, progress, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                sb.AppendLine($"\nSCAN COMPLETE: Found {devices.Count} verified live device(s):");
                foreach (var d in devices)
                {
                    sb.AppendLine($" -> IP: {d.LastIp,-15} MAC: {d.MacAddress,-17} Name: {d.ComputerName,-20} Desc: {d.Description}");
                }
                string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scan_test_output.txt");
                File.WriteAllText(outPath, sb.ToString());
                Console.WriteLine(sb.ToString());
                return;
            }

            if (args != null && args.Length > 0 && args[0] == "--import-file")
            {
                if (args.Length <= 1)
                {
                    Console.WriteLine("Usage: WhoIsOnMyWiFi_Reborn.exe --import-file <path-to-backup.wif>");
                    return;
                }
                string filePath = args[1];
                var db = new Database();
                db.Initialize();
                var res = LegacyImporter.ImportFile(filePath, db, true);
                Console.WriteLine($"Import complete! Devices: {res.Devices}, Subnets: {res.Subnets}, Manufacturers: {res.Manufacturers}, Settings: {res.SettingsImported}");
                var devices = db.LoadDevices(false);
                Console.WriteLine($"Total devices now in database: {devices.Count}");
                foreach (var d in devices)
                {
                    Console.WriteLine($" -> IP: {d.LastIp} | MAC: {d.MacAddress} | Name: {d.ComputerName} | Known: {d.Known} | Desc: {d.Description}");
                }
                var s = db.LoadSettings();
                Console.WriteLine($"Settings: IPRange={s.iprange}, AutoScan={s.autoscan}, Freq={s.frequency}");
                return;
            }

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string icoPath = Path.Combine(baseDir, "app.ico");
                if (!File.Exists(icoPath))
                {
                    IconGenerator.GenerateAppIcon(icoPath);
                }
            }
            catch { }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (s, e) => Report(e.Exception, "UI thread");
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Report(e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject)), "Background thread");
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            MainForm main = null;
            try
            {
                main = new MainForm();   // constructor exceptions are caught here, not by the above handlers
                Application.Run(main);
            }
            catch (Exception ex)
            {
                Report(ex, "Startup");
            }
            finally
            {
                if (main != null) main.SafeDispose();
            }
        }

        internal static string CrashLogPath
        {
            get { return Path.Combine(AppSettings.DataDirectory(), "error.log"); }
        }

        /// <summary>Never throws. Logs the exception and shows it to the user.</summary>
        internal static void Report(Exception ex, string source)
        {
            if (ex == null) return;
            try
            {
                string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] (" + source + ") " +
                              ex.GetType().Name + ": " + ex.Message + Environment.NewLine +
                              (ex.StackTrace ?? "") + Environment.NewLine + "----" + Environment.NewLine;
                File.AppendAllText(CrashLogPath, line);
            }
            catch { }
            try
            {
                DialogResult r = MessageBox.Show(
                    "Who Is On My WiFi Reborn ran into a problem and recovered.\r\n\r\n" +
                    ex.Message + "\r\n\r\n" +
                    "Technical details were written to:\r\n" + CrashLogPath + "\r\n\r\n" +
                    "Open the error log folder now?",
                    "Reborn - error",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (r == DialogResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                            "explorer.exe", "/select,\"" + CrashLogPath + "\"")
                        { UseShellExecute = true });
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}