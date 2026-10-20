using System;
using System.Drawing;
using System.Windows.Forms;

namespace WhoIsOnMyWiFi_Reborn
{
    public class AboutForm : Form
    {
        public AboutForm()
        {
            Text = "About Who Is On My WiFi Reborn";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(480, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var lbl = new Label
            {
                Text = "Who Is On My WiFi Reborn\n" +
                       "Version 1.0.1 (local)\n\n" +
                       "A modern rebuild of Who Is On My WiFi 4.0.1\n" +
                       "(original by IO3O LLC).\n\n" +
                       "Includes every feature of the original app:\n" +
                       "ARP/NetBIOS scanning, device grid, notifications,\n" +
                       "email alerts, blocking, IP range settings, logs,\n" +
                       "plus an Import/Export system for migrating\n" +
                       "data from the old app (_netlib2.dl2 / .wif).\n\n" +
                       "Runs entirely offline. Blocking uses Windows Firewall.\n" +
                       "If anything ever goes wrong, the details are in:\n" +
                       "%LocalAppData%\\WhoIsOnMyWiFi_Reborn\\error.log",
                Location = new Point(24, 20),
                AutoSize = true,
                Font = new Font("Segoe UI", 10)
            };
            Controls.Add(lbl);

            var btn = new Button { Text = "OK", Location = new Point(180, 300), Size = new Size(100, 34) };
            btn.Click += (s, e) => Close();
            Controls.Add(btn);
            AcceptButton = btn;
        }
    }
}