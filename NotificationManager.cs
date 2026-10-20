using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WhoIsOnMyWiFi_Reborn
{
    /// <summary>
    /// Alert system for unknown devices, mirroring the original app's notification
    /// options (message / sound / email / shutdown) plus a sane cooldown.
    /// </summary>
    public static class NotificationManager
    {
        public static void HandleNewUnknownDevices(List<DeviceRecord> newUnknown, AppSettings s, Database db,
                                                   NotifyIcon tray, Form owner, LogWriter scanLog)
        {
            if (newUnknown == null || newUnknown.Count == 0 || s == null || db == null) return;

            try
            {
                // cooldown (notifytimes minutes, 0 = always notify)
                if (s.notifytimes > 0 && db.NotificationRecentlySent(s.notifytimes))
                {
                    scanLog?.Write("Unknown device(s) detected, but notification is in its cooldown window (" + s.notifytimes + " min).");
                    return;
                }

                var lines = newUnknown.Take(5).Select(d =>
                    " - " + (string.IsNullOrEmpty(d.ComputerName) ? "(unnamed)" : d.ComputerName) +
                    "  [" + d.LastIp + " " + (string.IsNullOrEmpty(d.MacAddress) ? "no MAC" : d.MacAddress) + "]").ToList();
                string text = "Unknown device" + (newUnknown.Count > 1 ? "s (" + newUnknown.Count + ")" : "") +
                             " joined your network:\r\n" + string.Join("\r\n", lines) +
                             (newUnknown.Count > 5 ? "\r\n ... and " + (newUnknown.Count - 5) + " more" : "");

                // popup (balloon when minimized, dialog when visible)
                if (s.not_message)
                {
                    bool windowVisible = owner != null && owner.Visible && owner.WindowState != FormWindowState.Minimized;
                    if (windowVisible)
                    {
                        MessageBox.Show(owner, text, "Who Is On My WiFi Reborn - new unknown device",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else if (tray != null)
                    {
                        tray.BalloonTipTitle = "New unknown device on your network";
                        tray.BalloonTipText = text;
                        tray.BalloonTipIcon = ToolTipIcon.Warning;
                        tray.ShowBalloonTip(8000);
                    }
                }

                if (s.not_sound)
                {
                    SystemSounds.Exclamation.Play();
                }

                if (s.not_email && !string.IsNullOrEmpty(s.email) && !string.IsNullOrEmpty(s.mail_server))
                {
                    string captured = text;
                    Task.Run(() =>
                    {
                        try
                        {
                            bool ok = SendEmail(s, "Who Is On My WiFi Reborn - unknown device alert", captured);
                            scanLog?.Write(ok ? "Alert email sent to " + s.email : "Alert email FAILED - check SMTP settings in Settings > Notifications.");
                        }
                        catch (Exception ex) { Program.Report(ex, "EMAIL"); }
                    });
                }

                if (s.not_shutdown)
                {
                    DialogResult r = MessageBox.Show(owner,
                        text + "\r\n\r\nYou enabled 'shut down PC when an unknown device is found'.\r\n" +
                        "Windows will shut down in 60 seconds - you can cancel with 'shutdown /a'.\r\n\r\nShut down now?",
                        "Reborn - intruder action", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (r == DialogResult.Yes)
                    {
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo("shutdown", "/s /t 60 /c \"Who Is On My WiFi Reborn: unknown device detected\"")
                            { UseShellExecute = false, CreateNoWindow = true };
                            System.Diagnostics.Process.Start(psi);
                        }
                        catch (Exception ex) { Program.Report(ex, "SHUTDOWN"); }
                    }
                }

                db.RecordNotification();
            }
            catch (Exception ex)
            {
                Program.Report(ex, "NOTIFY");
            }
        }

        /// <summary>Sends an email with the settings from the Notifications tab. Never throws.</summary>
        public static bool SendEmail(AppSettings s, string subject, string body)
        {
            try
            {
                if (string.IsNullOrEmpty(s.mail_server) || string.IsNullOrEmpty(s.email)) return false;
                string from = string.IsNullOrEmpty(s.mailfrom) ? s.email : s.mailfrom;
                using var msg = new MailMessage(from, s.email, subject, body);
                using var smtp = new SmtpClient(s.mail_server, s.mail_port <= 0 ? 25 : s.mail_port)
                {
                    EnableSsl = s.enablessl
                };
                if (!string.IsNullOrEmpty(s.mail_username))
                {
                    smtp.Credentials = new NetworkCredential(s.mail_username, s.mail_password ?? "");
                }
                smtp.Send(msg);
                return true;
            }
            catch (Exception ex)
            {
                Program.Report(ex, "EMAIL");
                return false;
            }
        }
    }
}