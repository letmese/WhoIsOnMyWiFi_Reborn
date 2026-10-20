using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace WhoIsOnMyWiFi_Reborn
{
    /// <summary>
    /// Blocking via Windows Firewall (netsh), mirroring the original "Block" feature.
    ///
    /// FIXES (why blocking silently "did not work" before):
    ///  1. netsh add-rule REQUIRES elevation. The old code started netsh as a normal
    ///     process: Windows printed "The requested operation requires elevation",
    ///     the exit code was 1, the word "Ok" never appeared, and the method returned
    ///     false with an EMPTY error message - looked exactly like "the button does nothing".
    ///     Now the process is started with Verb="runas" (UAC prompt) whenever the app
    ///     itself is not elevated.
    ///  2. The old rule only blocked INBOUND traffic (dir=in). The device could still be
    ///     reached / keep talking on outbound flows. Now a rule pair (in + out) is created.
    ///  3. The real netsh output is now returned as the error text, so failures are
    ///     actually diagnosable.
    ///  4. Blocking first removes existing rules of the same name, so repeated clicks
    ///     do not pile up duplicate rules; unblock loops until all rules are gone.
    /// </summary>
    public static class FirewallService
    {
        private static string RuleName(DeviceRecord d, string dir)
        {
            string mac = (d.MacAddress ?? "").Replace(":", "").Replace("-", "");
            if (string.IsNullOrEmpty(mac)) mac = (d.LastIp ?? "").Replace(".", "");
            return "WOMW_Reborn_Block_" + dir + "_" + mac;
        }

        public static bool IsElevated()
        {
            try
            {
                using WindowsIdentity id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        /// <summary>Runs netsh (elevating via UAC when needed). Returns exit code; output only when elevated.</summary>
        private static int RunNetsh(string args, out string output)
        {
            output = "";
            try
            {
                if (IsElevated())
                {
                    var psi = new ProcessStartInfo("netsh", args)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var p = Process.Start(psi);
                    output = (p.StandardOutput.ReadToEnd() + " " + p.StandardError.ReadToEnd()).Trim();
                    p.WaitForExit(15000);
                    return p.ExitCode;
                }
                else
                {
                    var psi = new ProcessStartInfo("netsh", args)
                    {
                        UseShellExecute = true,
                        Verb = "runas",            // UAC prompt
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using var p = Process.Start(psi);
                    if (p == null || !p.WaitForExit(30000)) return -1;
                    return p.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                output = "Elevation was refused or failed: " + ex.Message;
                return -1;
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }

        public static bool BlockDevice(DeviceRecord d, out string error)
        {
            error = "";
            try
            {
                string ip = d.LastIp;
                if (string.IsNullOrEmpty(ip) || ip == "0.0.0.0")
                {
                    error = "Device has no last-seen IP to block. Re-scan to capture an IP first.";
                    return false;
                }

                bool ok = true;
                string messages = "";

                foreach (string dir in new string[] { "in", "out" })
                {
                    string name = RuleName(d, dir.ToUpper());

                    // remove any previous rules of the same name (ignore result)
                    RunNetsh("advfirewall firewall delete rule name=\"" + name + "\"", out _);

                    int rc = RunNetsh("advfirewall firewall add rule name=\"" + name + "\" dir=" + dir +
                                      " action=block remoteip=" + ip + " localip=any protocol=any enable=yes",
                                      out string outp);
                    if (rc != 0)
                    {
                        ok = false;
                        string msg = string.IsNullOrEmpty(outp)
                            ? "netsh exit code " + rc + " (run as administrator / allow the UAC prompt)"
                            : outp;
                        if (messages.IndexOf(msg) < 0) messages = messages.Length == 0 ? msg : messages + "\r\n" + msg;
                    }
                }

                if (!ok) error = messages;
                return ok;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool UnblockDevice(DeviceRecord d, out string error)
        {
            error = "";
            try
            {
                // netsh removes ONE matching rule per call - loop until they are all gone.
                for (int round = 0; round < 10; round++)
                {
                    int rcIn = RunNetsh("advfirewall firewall delete rule name=\"" + RuleName(d, "IN") + "\"", out _);
                    int rcOut = RunNetsh("advfirewall firewall delete rule name=\"" + RuleName(d, "OUT") + "\"", out _);

                    string probeIn = "", probeOut = "";
                    int rIn = RunNetsh("advfirewall firewall show rule name=\"" + RuleName(d, "IN") + "\"", out probeIn);
                    int rOut = RunNetsh("advfirewall firewall show rule name=\"" + RuleName(d, "OUT") + "\"", out probeOut);
                    bool noneLeft = rIn != 0 && rOut != 0;
                    if (rcIn != 0 && rcOut != 0 && round == 0)
                    {
                        // nothing to delete - fine
                    }
                    if (noneLeft) return true;
                    if (round == 9)
                    {
                        error = "Some rules could not be removed. Check Windows Defender Firewall > Advanced Security manually.";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    /// <summary>Simple append-only log (scan log / web log / block log) with timestamp.</summary>
    public class LogWriter
    {
        private readonly string _file;
        public LogWriter(string folder, string name)
        {
            try
            {
                Directory.CreateDirectory(folder);
                _file = Path.Combine(folder, name);
            }
            catch { _file = ""; }
        }

        public void Write(string message)
        {
            if (string.IsNullOrEmpty(_file)) return;
            try
            {
                File.AppendAllText(_file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}