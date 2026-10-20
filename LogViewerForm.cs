using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WhoIsOnMyWiFi_Reborn
{
    /// <summary>View the log files (scan.log, block.log, error.log ...) produced by the app.</summary>
    public class LogViewerForm : Form
    {
        private readonly string _folder;
        private ComboBox _combo;
        private TextBox _txt;

        public LogViewerForm(string folder)
        {
            _folder = folder;
            Text = "Logs";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 560);
            Font = new Font("Segoe UI", 9.25F);

            _combo = new ComboBox { Location = new Point(12, 12), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            _combo.SelectedIndexChanged += (s, e) => LoadLog();
            Controls.Add(_combo);

            var btnRefresh = new Button { Text = "Refresh", Location = new Point(280, 10), Size = new Size(80, 26) };
            btnRefresh.Click += (s, e) => { FillList(); LoadLog(); };
            Controls.Add(btnRefresh);

            var btnFolder = new Button { Text = "Open folder", Location = new Point(366, 10), Size = new Size(100, 26) };
            btnFolder.Click += (s, e) =>
            {
                try
                {
                    if (Directory.Exists(_folder))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + _folder + "\"") { UseShellExecute = true });
                }
                catch { }
            };
            Controls.Add(btnFolder);

            var btnClear = new Button { Text = "Clear this log", Location = new Point(472, 10), Size = new Size(100, 26) };
            btnClear.Click += (s, e) => ClearCurrent();
            Controls.Add(btnClear);

            _txt = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9F)
            };
            Controls.Add(_txt);
            _txt.BringToFront();

            FillList();
            if (_combo.Items.Count > 0) _combo.SelectedIndex = 0;
        }

        private void FillList()
        {
            _combo.Items.Clear();
            try
            {
                if (Directory.Exists(_folder))
                {
                    foreach (string f in Directory.GetFiles(_folder, "*.log").OrderBy(f => f))
                    {
                        _combo.Items.Add(Path.GetFileName(f));
                    }
                }
                if (File.Exists(Program.CrashLogPath)) _combo.Items.Add(Path.GetFileName(Program.CrashLogPath));
            }
            catch { }
        }

        private string SelectedPath()
        {
            if (_combo.SelectedItem == null) return null;
            string name = _combo.SelectedItem.ToString();
            if (name == Path.GetFileName(Program.CrashLogPath)) return Program.CrashLogPath;
            return Path.Combine(_folder, name);
        }

        private void LoadLog()
        {
            string path = SelectedPath();
            _txt.Clear();
            if (path == null) return;
            try
            {
                if (!File.Exists(path)) return;
                var fi = new FileInfo(path);
                if (fi.Length > 2_000_000)
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    fs.Seek(-1_000_000, SeekOrigin.End);
                    using var sr = new StreamReader(fs);
                    _txt.Text = "[showing the last ~1 MB of " + fi.Length.ToString("N0") + " bytes]\r\n" + sr.ReadToEnd();
                }
                else
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);
                    _txt.Text = sr.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                _txt.Text = "Could not read the log: " + ex.Message;
            }
        }

        private void ClearCurrent()
        {
            string path = SelectedPath();
            if (path == null) return;
            if (MessageBox.Show(this, "Delete the contents of " + Path.GetFileName(path) + "?", "Clear log",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { File.WriteAllText(path, ""); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Clear log", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            LoadLog();
        }
    }
}