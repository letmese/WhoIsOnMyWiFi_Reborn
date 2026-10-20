using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WhoIsOnMyWiFi_Reborn
{
    /// <summary>All app settings in one tabbed dialog (General / Scanning / Notifications / Logging / Privacy / Blocking).</summary>
    public class SettingsForm : Form
    {
        private readonly AppSettings _s;
        private readonly Database _db;
        public int InitialTab;

        private TabControl _tabs;
        private CheckBox _chkAutoscan, _chkContinuous, _chkMinimize, _chkFilter, _chkIgnoreGw;
        private NumericUpDown _numFrequency;
        private CheckBox _chkPing, _chkRandomize, _chkDelay;
        private CheckBox _chkMsg, _chkSound, _chkShutdown, _chkEmail, _chkSsl;
        private NumericUpDown _numCooldown, _numPort;
        private TextBox _txtEmail, _txtFrom, _txtServer, _txtUser, _txtPass;
        private CheckBox _chkEnableLog, _chkAllowBlock, _chkLogBlock;
        private TextBox _txtLogFolder;
        private RadioButton _radPrivate, _radPublic;

        public SettingsForm(AppSettings s, Database db)
        {
            _s = s;
            _db = db;
            BuildUi();
            LoadValues();
        }

        private void BuildUi()
        {
            Text = "Settings";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 9.25F);

            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs.TabPages.Add(BuildGeneralTab());
            _tabs.TabPages.Add(BuildScanTab());
            _tabs.TabPages.Add(BuildNotifyTab());
            _tabs.TabPages.Add(BuildLogTab());
            _tabs.TabPages.Add(BuildPrivacyTab());
            _tabs.TabPages.Add(BuildBlockTab());
            _tabs.SelectedIndexChanged += (s, e) =>
            {
                if (_tabs.SelectedIndex >= 0 && _tabs.SelectedIndex != InitialTab) InitialTab = _tabs.SelectedIndex;
            };
            Controls.Add(_tabs);

            var panel = new Panel { Dock = DockStyle.Bottom, Height = 46 };
            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(90, 30), Location = new Point(360, 8) };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(90, 30), Location = new Point(458, 8) };
            btnOk.Click += (s, e) => SaveValues();
            panel.Controls.Add(btnOk);
            panel.Controls.Add(btnCancel);
            Controls.Add(panel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private TabPage BuildGeneralTab()
        {
            var t = new TabPage("General");
            int y = 20;
            _chkAutoscan = new CheckBox { Text = "Scan automatically", Location = new Point(20, y), AutoSize = true };
            t.Controls.Add(_chkAutoscan);
            y += 32;
            t.Controls.Add(new Label { Text = "Minutes between automatic scans:", Location = new Point(40, y), AutoSize = true });
            _numFrequency = new NumericUpDown { Location = new Point(280, y - 3), Width = 70, Minimum = 1, Maximum = 1440 };
            t.Controls.Add(_numFrequency);
            y += 34;
            _chkContinuous = new CheckBox { Text = "Scan continuously (back-to-back, constant watch)", Location = new Point(20, y), AutoSize = true };
            t.Controls.Add(_chkContinuous);
            y += 34;
            _chkMinimize = new CheckBox { Text = "Minimize to tray when closing the window", Location = new Point(20, y), AutoSize = true };
            t.Controls.Add(_chkMinimize);
            y += 34;
            _chkFilter = new CheckBox { Text = "Show connected devices only", Location = new Point(20, y), AutoSize = true };
            t.Controls.Add(_chkFilter);
            y += 34;
            _chkIgnoreGw = new CheckBox { Text = "Hide the gateway / router from results", Location = new Point(20, y), AutoSize = true };
            t.Controls.Add(_chkIgnoreGw);
            y += 40;
            t.Controls.Add(new Label
            {
                Text = "IP ranges and per-range options are managed in\r\nSettings > IP Ranges.",
                Location = new Point(20, y),
                AutoSize = true,
                ForeColor = Color.DimGray
            });
            return t;
        }

        private TabPage BuildScanTab()
        {
            var t = new TabPage("Scanning");
            int y = 20;
            _chkPing = new CheckBox { Text = "Ping sweep (wakes devices up so the ARP table fills - recommended)", Location = new Point(20, y), AutoSize = true, Checked = true };
            t.Controls.Add(_chkPing);
            y += 34;
            _chkRandomize = new CheckBox { Text = "Randomize scan order", Location = new Point(20, y), AutoSize = true };
            t.Controls.Add(_chkRandomize);
            y += 34;
            _chkDelay = new CheckBox { Text = "Add a small delay between pings (stealth)", Location = new Point(20, y), AutoSize = true };
            t.Controls.Add(_chkDelay);
            y += 44;
            t.Controls.Add(new Label
            {
                Text = "Device names are resolved with NetBIOS and reverse DNS.\r\n" +
                       "NetBIOS (UDP 137) can be enabled per IP range in Settings > IP Ranges.",
                Location = new Point(20, y),
                AutoSize = true,
                ForeColor = Color.DimGray
            });
            return t;
        }

        private TabPage BuildNotifyTab()
        {
            var t = new TabPage("Notifications");
            int y = 16;
            _chkMsg = new CheckBox { Text = "Show a message when an unknown device is found", Location = new Point(20, y), AutoSize = true };
            t.Controls.Add(_chkMsg);
            y += 30;
            _chkSound = new CheckBox { Text = "Play a sound", Location = new Point(20, y), AutoSize = true };
            t.Controls.Add(_chkSound);
            y += 30;
            _chkShutdown = new CheckBox { Text = "Offer to shut down the PC (with 60s cancel window)", Location = new Point(20, y), AutoSize = true };
            t.Controls.Add(_chkShutdown);
            y += 32;
            t.Controls.Add(new Label { Text = "Notify at most once every [minutes] (0 = every time):", Location = new Point(20, y), AutoSize = true });
            _numCooldown = new NumericUpDown { Location = new Point(340, y - 3), Width = 60, Minimum = 0, Maximum = 1440 };
            t.Controls.Add(_numCooldown);
            y += 36;

            var grp = new GroupBox { Text = "Email alerts", Location = new Point(16, y), Size = new Size(520, 226) };
            int gy = 24;
            _chkEmail = new CheckBox { Text = "Send email when an unknown device is found", Location = new Point(14, gy), AutoSize = true };
            grp.Controls.Add(_chkEmail);
            gy += 30;
            grp.Controls.Add(new Label { Text = "To:", Location = new Point(14, gy), AutoSize = true });
            _txtEmail = new TextBox { Location = new Point(150, gy - 3), Width = 340 };
            grp.Controls.Add(_txtEmail);
            gy += 30;
            grp.Controls.Add(new Label { Text = "From:", Location = new Point(14, gy), AutoSize = true });
            _txtFrom = new TextBox { Location = new Point(150, gy - 3), Width = 340 };
            grp.Controls.Add(_txtFrom);
            gy += 30;
            grp.Controls.Add(new Label { Text = "SMTP server:", Location = new Point(14, gy), AutoSize = true });
            _txtServer = new TextBox { Location = new Point(150, gy - 3), Width = 250 };
            grp.Controls.Add(_txtServer);
            grp.Controls.Add(new Label { Text = "Port:", Location = new Point(410, gy), AutoSize = true });
            _numPort = new NumericUpDown { Location = new Point(450, gy - 3), Width = 60, Minimum = 1, Maximum = 65535 };
            grp.Controls.Add(_numPort);
            gy += 30;
            grp.Controls.Add(new Label { Text = "User name:", Location = new Point(14, gy), AutoSize = true });
            _txtUser = new TextBox { Location = new Point(150, gy - 3), Width = 340 };
            grp.Controls.Add(_txtUser);
            gy += 30;
            grp.Controls.Add(new Label { Text = "Password:", Location = new Point(14, gy), AutoSize = true });
            _txtPass = new TextBox { Location = new Point(150, gy - 3), Width = 340, UseSystemPasswordChar = true };
            grp.Controls.Add(_txtPass);
            gy += 30;
            _chkSsl = new CheckBox { Text = "This server uses SSL/TLS (Gmail: smtp.gmail.com, port 587, app password)", Location = new Point(14, gy), AutoSize = true };
            grp.Controls.Add(_chkSsl);
            var btnTest = new Button { Text = "Send test email", Location = new Point(390, 196), Size = new Size(118, 26) };
            btnTest.Click += (s, e) => TestEmail();
            grp.Controls.Add(btnTest);
            t.Controls.Add(grp);
            return t;
        }

        private TabPage BuildLogTab()
        {
            var t = new TabPage("Logging");
            int y = 20;
            _chkEnableLog = new CheckBox { Text = "Enable logging (scan log, block log)", Location = new Point(20, y), AutoSize = true };
            t.Controls.Add(_chkEnableLog);
            y += 34;
            t.Controls.Add(new Label { Text = "Log folder:", Location = new Point(20, y), AutoSize = true });
            _txtLogFolder = new TextBox { Location = new Point(110, y - 3), Width = 360 };
            t.Controls.Add(_txtLogFolder);
            var btnBrowse = new Button { Text = "...", Location = new Point(478, y - 5), Size = new Size(34, 25) };
            btnBrowse.Click += (s, e) =>
            {
                using var d = new FolderBrowserDialog();
                if (Directory.Exists(_txtLogFolder.Text)) d.SelectedPath = _txtLogFolder.Text;
                if (d.ShowDialog(this) == DialogResult.OK) _txtLogFolder.Text = d.SelectedPath;
            };
            t.Controls.Add(btnBrowse);
            y += 40;
            var btnOpen = new Button { Text = "Open log folder", Location = new Point(20, y), Size = new Size(130, 28) };
            btnOpen.Click += (s, e) =>
            {
                try
                {
                    string f = _txtLogFolder.Text;
                    if (Directory.Exists(f))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + f + "\"") { UseShellExecute = true });
                    else MessageBox.Show(this, "Folder does not exist yet - it is created on the first log entry.", "Reborn", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch { }
            };
            t.Controls.Add(btnOpen);
            return t;
        }

        private TabPage BuildPrivacyTab()
        {
            var t = new TabPage("Privacy");
            int y = 20;
            _radPrivate = new RadioButton { Text = "Private - log full details (IP, MAC, names) on this PC", Location = new Point(20, y), AutoSize = true };
            _radPublic = new RadioButton { Text = "Public - anonymize MAC/IP in any exported data", Location = new Point(20, y + 30), AutoSize = true };
            t.Controls.Add(_radPrivate);
            t.Controls.Add(_radPublic);
            t.Controls.Add(new Label
            {
                Text = "Everything is stored locally. No data ever leaves this computer,\r\n" +
                       "except alert emails you explicitly configure yourself.",
                Location = new Point(20, y + 70),
                AutoSize = true,
                ForeColor = Color.DimGray
            });
            return t;
        }

        private TabPage BuildBlockTab()
        {
            var t = new TabPage("Blocking");
            int y = 20;
            _chkAllowBlock = new CheckBox { Text = "Allow blocking devices (Windows Firewall)", Location = new Point(20, y), AutoSize = true };
            t.Controls.Add(_chkAllowBlock);
            y += 32;
            _chkLogBlock = new CheckBox { Text = "Log every block / unblock action", Location = new Point(20, y), AutoSize = true };
            t.Controls.Add(_chkLogBlock);
            y += 40;
            t.Controls.Add(new Label
            {
                Text = "How blocking works\r\n" +
                       "Two Windows Firewall rules (in + out) stop ALL traffic between this PC and\r\n" +
                       "the device. A UAC prompt appears once per action because firewall rules\r\n" +
                       "require administrator rights. To remove a block, select the device and\r\n" +
                       "click Unblock. Note: this isolates the device from THIS PC - kicking a\r\n" +
                       "device off the whole WiFi is done in your router settings (MAC filter).",
                Location = new Point(20, y),
                Size = new Size(510, 130),
                ForeColor = Color.DimGray
            });
            return t;
        }

        private void LoadValues()
        {
            _chkAutoscan.Checked = _s.autoscan;
            _numFrequency.Value = Math.Max(1, Math.Min(1440, _s.frequency));
            _chkContinuous.Checked = _s.scan_continuous;
            _chkMinimize.Checked = _s.minimize;
            _chkFilter.Checked = _s.filt_connected;
            _chkIgnoreGw.Checked = _s.ignoregateway;

            _chkPing.Checked = _s.scan_ping;
            _chkRandomize.Checked = _s.scan_randomize;
            _chkDelay.Checked = _s.scan_delay;

            _chkMsg.Checked = _s.not_message;
            _chkSound.Checked = _s.not_sound;
            _chkShutdown.Checked = _s.not_shutdown;
            _numCooldown.Value = Math.Max(0, Math.Min(1440, _s.notifytimes));
            _chkEmail.Checked = _s.not_email;
            _txtEmail.Text = _s.email;
            _txtFrom.Text = _s.mailfrom;
            _txtServer.Text = _s.mail_server;
            _numPort.Value = _s.mail_port > 0 ? _s.mail_port : 25;
            _chkSsl.Checked = _s.enablessl;
            _txtUser.Text = _s.mail_username;
            _txtPass.Text = _s.mail_password;

            _chkEnableLog.Checked = _s.enablelog;
            _txtLogFolder.Text = _s.logfolder;

            _radPrivate.Checked = _s.privacy_pref == 0;
            _radPublic.Checked = _s.privacy_pref != 0;

            _chkAllowBlock.Checked = _s.allow_blocking;
            _chkLogBlock.Checked = _s.log_blocking;

            if (InitialTab > 0 && InitialTab < _tabs.TabCount) _tabs.SelectedIndex = InitialTab;
        }

        private void SaveValues()
        {
            _s.autoscan = _chkAutoscan.Checked;
            _s.frequency = (int)_numFrequency.Value;
            _s.scan_continuous = _chkContinuous.Checked;
            _s.minimize = _chkMinimize.Checked;
            _s.filt_connected = _chkFilter.Checked;
            _s.ignoregateway = _chkIgnoreGw.Checked;

            _s.scan_ping = _chkPing.Checked;
            _s.scan_randomize = _chkRandomize.Checked;
            _s.scan_delay = _chkDelay.Checked;

            _s.not_message = _chkMsg.Checked;
            _s.not_sound = _chkSound.Checked;
            _s.not_shutdown = _chkShutdown.Checked;
            _s.notifytimes = (int)_numCooldown.Value;
            _s.not_email = _chkEmail.Checked;
            _s.email = _txtEmail.Text.Trim();
            _s.mailfrom = _txtFrom.Text.Trim();
            _s.mail_server = _txtServer.Text.Trim();
            _s.mail_port = (int)_numPort.Value;
            _s.enablessl = _chkSsl.Checked;
            _s.mail_username = _txtUser.Text.Trim();
            _s.mail_password = _txtPass.Text;

            _s.enablelog = _chkEnableLog.Checked;
            _s.logfolder = _txtLogFolder.Text.Trim();
            try { System.IO.Directory.CreateDirectory(_s.logfolder); } catch { }

            _s.privacy_pref = _radPublic.Checked ? 1 : 0;

            _s.allow_blocking = _chkAllowBlock.Checked;
            _s.log_blocking = _chkLogBlock.Checked;

            try { _db.SaveSettings(_s); }
            catch (Exception ex) { Program.Report(ex, "SAVE-SETTINGS"); }
        }

        private void TestEmail()
        {
            var test = new AppSettings
            {
                email = _txtEmail.Text.Trim(),
                mailfrom = _txtFrom.Text.Trim(),
                mail_server = _txtServer.Text.Trim(),
                mail_port = (int)_numPort.Value,
                enablessl = _chkSsl.Checked,
                mail_username = _txtUser.Text.Trim(),
                mail_password = _txtPass.Text
            };
            bool ok = NotificationManager.SendEmail(test, "Who Is On My WiFi Reborn - test",
                "This is a test message. If you can read this, alert emails are configured correctly.");
            MessageBox.Show(this, ok ? "Test email sent - check the inbox of " + test.email + "."
                                     : "Sending FAILED. Common causes: wrong server/port, SSL checkbox,\r\n" +
                                       "missing app password (Gmail/Outlook), or antivirus blocking SMTP.\r\nDetails are in error.log.",
                "Test email", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }
}