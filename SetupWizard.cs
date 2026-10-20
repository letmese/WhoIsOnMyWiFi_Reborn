using System;
using System.Drawing;
using System.Windows.Forms;

namespace WhoIsOnMyWiFi_Reborn
{
    /// <summary>
    /// First-run wizard (network range -> alerts -> blocking -> done),
    /// mirroring the original app's setup flow.
    /// </summary>
    public class SetupWizard : Form
    {
        private readonly AppSettings _s;
        private readonly bool _fresh;
        private int _step = 0;
        private Label _lblTitle;
        private Panel _panel;
        private Button _btnBack, _btnNext;

        // step 1 controls
        private TextBox _txtRange;
        private Label _lblAdapter;
        // step 2 controls
        private CheckBox _chkMsg, _chkSound, _chkShutdown, _chkEmail, _chkSsl;
        private NumericUpDown _numCooldown, _numPort;
        private TextBox _txtEmail, _txtFrom, _txtServer, _txtUser, _txtPass;
        // step 3 controls
        private CheckBox _chkAllowBlock, _chkLogBlock;
        // step 4 controls
        private CheckBox _chkRunScan;

        /// <summary>true when the user left the last page with "Finish"</summary>
        public bool RunScanAfter { get; private set; }

        public SetupWizard(AppSettings s)
        {
            _s = s;
            _fresh = s.firstrun;
            Text = "Who Is On My WiFi Reborn - Setup";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 470);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9.25F);

            _lblTitle = new Label
            {
                Location = new Point(16, 12),
                Size = new Size(580, 30),
                Font = new Font("Segoe UI", 12F, FontStyle.Bold)
            };
            Controls.Add(_lblTitle);

            _panel = new Panel { Location = new Point(16, 50), Size = new Size(580, 350), AutoScroll = true };
            Controls.Add(_panel);

            _btnBack = new Button { Text = "< Back", Size = new Size(90, 30), Location = new Point(320, 408), Enabled = false };
            _btnBack.Click += (s2, e) => { _step--; ShowStep(); };
            _btnNext = new Button { Text = "Next >", Size = new Size(90, 30), Location = new Point(416, 408) };
            _btnNext.Click += (s2, e) => Next();
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(90, 30), Location = new Point(512, 408) };
            Controls.Add(_btnBack);
            Controls.Add(_btnNext);
            Controls.Add(btnCancel);
            CancelButton = btnCancel;

            ShowStep();
        }

        private void Next()
        {
            if (_step == 1)
            {
                if (!NetworkScanner.ParseRange(_txtRange.Text.Trim(), out uint st, out uint en))
                {
                    MessageBox.Show(this, "The IP range is not valid.\r\nExamples: 192.168.1.1-192.168.1.254 or 192.168.1.0/24",
                        "Reborn", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _s.iprange = _txtRange.Text.Trim();
            }
            if (_step == 4)
            {
                Finish();
                return;
            }
            _step++;
            ShowStep();
        }

        private void ShowStep()
        {
            _panel.Controls.Clear();
            _panel.AutoScrollPosition = new Point(0, 0);
            _btnBack.Enabled = _step > 0;
            _btnNext.Text = _step == 4 ? "Finish" : "Next >";

            if (_step == 0)
            {
                _lblTitle.Text = "Welcome";
                _panel.Controls.Add(new Label
                {
                    Text =
                        "This wizard sets up Who Is On My WiFi Reborn in three quick steps.\r\n\r\n" +
                        "The app watches your network and lists every device that is using it.\r\n" +
                        "Devices you do not recognize can be blocked with one click.\r\n\r\n" +
                        "Everything runs locally on this PC. No accounts, no cloud,\r\n" +
                        "no data sent anywhere (unless you configure email alerts yourself).\r\n\r\n" +
                        "Click Next to continue.",
                    Location = new Point(10, 10),
                    Size = new Size(560, 280)
                });
            }
            else if (_step == 1)
            {
                _lblTitle.Text = "Step 1 of 3 - Your network";
                string detected = "";
                try { detected = NetworkScanner.DetectFirstSubnet(); } catch { }
                if (string.IsNullOrEmpty(detected)) detected = _s.iprange;
                if (string.IsNullOrEmpty(detected)) detected = "192.168.1.1-192.168.1.254";

                _panel.Controls.Add(new Label { Text = "IP range to scan:", Location = new Point(10, 10), AutoSize = true });
                _txtRange = new TextBox { Location = new Point(10, 36), Width = 300, Text = detected };
                _panel.Controls.Add(_txtRange);
                _panel.Controls.Add(new Label
                {
                    Text = "Format examples: 192.168.1.1-192.168.1.254   or   192.168.1.0/24   or   192.168.1",
                    Location = new Point(10, 64),
                    AutoSize = true,
                    ForeColor = Color.DimGray
                });

                _lblAdapter = new Label { Location = new Point(10, 96), Size = new Size(560, 70), ForeColor = Color.DimGray };
                _panel.Controls.Add(_lblAdapter);
                try
                {
                    var a = NetworkScanner.GetBestAdapter();
                    _lblAdapter.Text = a == null
                        ? "No connected network adapter was detected right now.\r\nYou can continue and fix the range later in Settings > IP Ranges."
                        : "Detected adapter: " + a.Description + "\r\n" +
                          "This PC: " + a.Ip + "   Mask: " + a.Mask + "   Gateway: " +
                          (string.IsNullOrEmpty(a.Gateway) ? "-" : a.Gateway);
                }
                catch { _lblAdapter.Text = ""; }
            }
            else if (_step == 2)
            {
                _lblTitle.Text = "Step 2 of 3 - Alerts";
                int y = 4;
                _chkMsg = new CheckBox { Text = "Show a message when an unknown device appears", Location = new Point(10, y), AutoSize = true, Checked = _s.not_message || _fresh }; y += 28;
                _chkSound = new CheckBox { Text = "Play a warning sound", Location = new Point(10, y), AutoSize = true, Checked = _s.not_sound }; y += 28;
                _chkShutdown = new CheckBox { Text = "Offer to shut down this PC (security mode, cancellable)", Location = new Point(10, y), AutoSize = true, Checked = _s.not_shutdown }; y += 28;
                _panel.Controls.Add(_chkMsg);
                _panel.Controls.Add(_chkSound);
                _panel.Controls.Add(_chkShutdown);

                var lblCd = new Label { Text = "Notify at most once every [min] (0 = every time):", Location = new Point(10, y), AutoSize = true };
                _numCooldown = new NumericUpDown { Location = new Point(430, y - 2), Width = 60, Minimum = 0, Maximum = 1440, Value = _s.notifytimes };
                _panel.Controls.Add(lblCd);
                _panel.Controls.Add(_numCooldown);
                y += 32;

                var grp = new GroupBox { Text = "Email alerts (optional)", Location = new Point(6, y), Size = new Size(564, 190) };
                int gy = 24;
                _chkEmail = new CheckBox { Text = "Send me an email", Location = new Point(10, gy), AutoSize = true, Checked = _s.not_email }; gy += 28;
                grp.Controls.Add(_chkEmail);
                grp.Controls.Add(new Label { Text = "To:", Location = new Point(10, gy), AutoSize = true });
                _txtEmail = new TextBox { Location = new Point(120, gy - 3), Width = 300, Text = _s.email }; gy += 28;
                grp.Controls.Add(_txtEmail);
                grp.Controls.Add(new Label { Text = "From:", Location = new Point(10, gy), AutoSize = true });
                _txtFrom = new TextBox { Location = new Point(120, gy - 3), Width = 300, Text = _s.mailfrom }; gy += 28;
                grp.Controls.Add(_txtFrom);
                grp.Controls.Add(new Label { Text = "SMTP server:", Location = new Point(10, gy), AutoSize = true });
                _txtServer = new TextBox { Location = new Point(120, gy - 3), Width = 200, Text = _s.mail_server };
                grp.Controls.Add(new Label { Text = "Port:", Location = new Point(330, gy), AutoSize = true });
                _numPort = new NumericUpDown { Location = new Point(380, gy - 3), Width = 55, Minimum = 1, Maximum = 65535, Value = _s.mail_port > 0 ? _s.mail_port : 587 }; gy += 28;
                grp.Controls.Add(_txtServer);
                grp.Controls.Add(_numPort);
                grp.Controls.Add(new Label { Text = "User:", Location = new Point(10, gy), AutoSize = true });
                _txtUser = new TextBox { Location = new Point(120, gy - 3), Width = 160, Text = _s.mail_username };
                grp.Controls.Add(new Label { Text = "Password:", Location = new Point(300, gy), AutoSize = true });
                _txtPass = new TextBox { Location = new Point(380, gy - 3), Width = 130, UseSystemPasswordChar = true, Text = _s.mail_password };
                grp.Controls.Add(_txtUser);
                grp.Controls.Add(_txtPass);
                _chkSsl = new CheckBox { Text = "SSL/TLS (Gmail: smtp.gmail.com, port 587, app password)", Location = new Point(10, 138), AutoSize = true, Checked = _s.enablessl };
                grp.Controls.Add(_chkSsl);
                _panel.Controls.Add(grp);
            }
            else if (_step == 3)
            {
                _lblTitle.Text = "Step 3 of 3 - Blocking";
                _chkAllowBlock = new CheckBox
                {
                    Text = "Allow blocking unknown devices (recommended)",
                    Location = new Point(10, 10),
                    AutoSize = true,
                    Checked = _s.allow_blocking || _fresh
                };
                _chkLogBlock = new CheckBox { Text = "Log every block / unblock", Location = new Point(10, 40), AutoSize = true, Checked = _s.log_blocking };
                var lbl = new Label
                {
                    Text =
                        "\r\nHow it works: two Windows Firewall rules (in + out) stop all traffic\r\n" +
                        "between this PC and the selected device. Each block shows one UAC\r\n" +
                        "prompt because firewall rules need administrator rights.\r\n\r\n" +
                        "Note: this isolates the device from THIS PC. To kick a device off\r\n" +
                        "your WiFi completely, use the MAC filter in your router settings.\r\n\r\n" +
                        "Blocking can be turned on/off anytime in Settings > General > Blocking.",
                    Location = new Point(10, 76),
                    Size = new Size(560, 220),
                    ForeColor = Color.DimGray
                };
                _panel.Controls.Add(_chkAllowBlock);
                _panel.Controls.Add(_chkLogBlock);
                _panel.Controls.Add(lbl);
            }
            else if (_step == 4)
            {
                _lblTitle.Text = "Setup complete";
                _chkRunScan = new CheckBox
                {
                    Text = "Run the first scan now (recommended)",
                    Location = new Point(10, 10),
                    AutoSize = true,
                    Checked = true
                };
                var lbl = new Label
                {
                    Text =
                        "\r\nEverything is set up.\r\n\r\n" +
                        "The first scan establishes your baseline: every device found is\r\n" +
                        "marked as known automatically. Afterwards, anything new that\r\n" +
                        "appears is treated as unknown and can trigger alerts.\r\n\r\n" +
                        "Tip: review the device list after the first scan (right-click >\r\n" +
                        "Mark as Unknown) so only YOUR devices stay known.",
                    Location = new Point(10, 40),
                    Size = new Size(560, 240)
                };
                _panel.Controls.Add(_chkRunScan);
                _panel.Controls.Add(lbl);
            }
        }

        private void Finish()
        {
            // step 2 values
            if (_chkMsg != null) _s.not_message = _chkMsg.Checked;
            if (_chkSound != null) _s.not_sound = _chkSound.Checked;
            if (_chkShutdown != null) _s.not_shutdown = _chkShutdown.Checked;
            if (_numCooldown != null) _s.notifytimes = (int)_numCooldown.Value;
            if (_chkEmail != null)
            {
                _s.not_email = _chkEmail.Checked;
                _s.email = _txtEmail.Text.Trim();
                _s.mailfrom = _txtFrom.Text.Trim();
                _s.mail_server = _txtServer.Text.Trim();
                _s.mail_port = (int)_numPort.Value;
                _s.mail_username = _txtUser.Text.Trim();
                _s.mail_password = _txtPass.Text;
                _s.enablessl = _chkSsl.Checked;
            }
            // step 3 values
            if (_chkAllowBlock != null) _s.allow_blocking = _chkAllowBlock.Checked;
            if (_chkLogBlock != null) _s.log_blocking = _chkLogBlock.Checked;

            _s.firstrun = false;
            _s.firstrun_step = 5;
            _s.autoscan = true;
            _s.windowclosenotice = true;
            RunScanAfter = _chkRunScan == null || _chkRunScan.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK && _step < 4)
            {
                if (MessageBox.Show(this, "Exit the setup wizard? Values already filled in are kept.",
                    "Reborn", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            base.OnFormClosing(e);
        }
    }
}