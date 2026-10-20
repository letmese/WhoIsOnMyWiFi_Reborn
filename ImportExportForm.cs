using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WhoIsOnMyWiFi_Reborn
{
    /// <summary>
    /// Import data from the ORIGINAL "Who Is On My WiFi" app (its _netlib2.dl2 / manu.db /
    /// .wif files), from Reborn backups (.wif / .json), and export backups of this app.
    /// </summary>
    public class ImportExportForm : Form
    {
        private readonly Database _db;
        private readonly Action<string> _log;
        private TextBox _txtImportPath;
        private CheckBox _chkSettings, _chkManu;
        private TextBox _txtExportPath;
        private RadioButton _radJson, _radWif;
        private Button _btnImport, _btnExport;
        private Label _lblStatus;

        public ImportExportForm(Database db, Action<string> log)
        {
            _db = db;
            _log = log;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Import / Export Data";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 470);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9.25F);

            // ---------------- import ----------------
            var grpIn = new GroupBox { Text = "Import (old app data or a Reborn backup)", Location = new Point(12, 10), Size = new Size(596, 170) };
            grpIn.Controls.Add(new Label
            {
                Text = "Source file: original app's _netlib2.dl2 / manu.db / exported .wif,\r\nor a Reborn backup (.wif / .json).",
                Location = new Point(12, 22),
                Size = new Size(570, 34),
                ForeColor = Color.DimGray
            });
            _txtImportPath = new TextBox { Location = new Point(12, 60), Width = 500 };
            grpIn.Controls.Add(_txtImportPath);
            var btnBrowseIn = new Button { Text = "Browse...", Location = new Point(516, 58), Size = new Size(70, 25) };
            btnBrowseIn.Click += (s, e) => BrowseOpen();
            grpIn.Controls.Add(btnBrowseIn);

            _chkSettings = new CheckBox { Text = "Also import settings (ranges, alerts, email config)", Location = new Point(12, 92), AutoSize = true, Checked = true };
            _chkManu = new CheckBox { Text = "Import MAC vendor table (recommended)", Location = new Point(12, 118), AutoSize = true, Checked = true };
            grpIn.Controls.Add(_chkSettings);
            grpIn.Controls.Add(_chkManu);

            _btnImport = new Button { Text = "Start Import", Location = new Point(486, 140), Size = new Size(100, 26) };
            _btnImport.Click += async (s, e) => await RunImportAsync();
            grpIn.Controls.Add(_btnImport);
            Controls.Add(grpIn);

            // ---------------- export ----------------
            var grpOut = new GroupBox { Text = "Export (backup this app)", Location = new Point(12, 190), Size = new Size(596, 120) };
            _txtExportPath = new TextBox { Location = new Point(12, 30), Width = 500 };
            grpOut.Controls.Add(_txtExportPath);
            var btnBrowseOut = new Button { Text = "Browse...", Location = new Point(516, 28), Size = new Size(70, 25) };
            btnBrowseOut.Click += (s, e) => BrowseSave();
            grpOut.Controls.Add(btnBrowseOut);

            _radJson = new RadioButton { Text = "JSON (readable, devices + settings + ranges)", Location = new Point(12, 64), AutoSize = true, Checked = true };
            _radWif = new RadioButton { Text = ".wif (exact SQLite copy)", Location = new Point(300, 64), AutoSize = true };
            grpOut.Controls.Add(_radJson);
            grpOut.Controls.Add(_radWif);

            _btnExport = new Button { Text = "Export Now", Location = new Point(486, 88), Size = new Size(100, 26) };
            _btnExport.Click += (s, e) => RunExport();
            grpOut.Controls.Add(_btnExport);
            Controls.Add(grpOut);

            _lblStatus = new Label { Location = new Point(14, 322), Size = new Size(590, 100), ForeColor = Color.DimGray, Text = "Ready." };
            Controls.Add(_lblStatus);

            try
            {
                _txtImportPath.Text = SuggestLegacyPath();
            }
            catch { }
        }

        private static string SuggestLegacyPath()
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Who Is On My WiFi", "data", "_netlib2.dl2"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Who Is On My WiFi", "data", "_netlib2.dl2"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Who Is On My WiFi", "data", "_netlib2.dl2"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Who Is On My WiFi", "data", "_netlib2.dl2")
            };
            foreach (string p in candidates)
            {
                if (File.Exists(p)) return p;
            }
            return candidates[0];
        }

        private void BrowseOpen()
        {
            using var d = new OpenFileDialog
            {
                Title = "Choose the data file to import",
                Filter = "Who Is On My WiFi data (*.dl2;*.wif;*.db;*.json;*.dat)|*.dl2;*.wif;*.db;*.json;*.dat|All files (*.*)|*.*"
            };
            if (File.Exists(_txtImportPath.Text))
            {
                try
                {
                    d.InitialDirectory = Path.GetDirectoryName(_txtImportPath.Text);
                    d.FileName = Path.GetFileName(_txtImportPath.Text);
                }
                catch { }
            }
            if (d.ShowDialog(this) == DialogResult.OK) _txtImportPath.Text = d.FileName;
        }

        private void BrowseSave()
        {
            using var d = new SaveFileDialog
            {
                Title = "Choose where to save the backup",
                Filter = _radWif.Checked ? "Reborn backup (*.wif)|*.wif" : "Reborn JSON backup (*.json)|*.json",
                FileName = _radWif.Checked
                    ? "reborn-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".wif"
                    : "reborn-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json"
            };
            if (d.ShowDialog(this) == DialogResult.OK) _txtExportPath.Text = d.FileName;
        }

        private async Task RunImportAsync()
        {
            string path = _txtImportPath.Text.Trim();
            if (path.Length == 0 || !File.Exists(path))
            {
                MessageBox.Show(this, "Pick an existing file first.", "Import", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _btnImport.Enabled = false;
            _btnExport.Enabled = false;
            _lblStatus.Text = "Importing " + Path.GetFileName(path) + " ...";
            _lblStatus.ForeColor = Color.DimGray;
            try
            {
                var result = await Task.Run(() => LegacyImporter.ImportFile(path, _db, _chkSettings.Checked));
                string msg = "Import finished.\r\n\r\n" +
                             "Devices: " + result.Devices + "\r\n" +
                             "IP ranges: " + result.Subnets + "\r\n" +
                             "Manufacturers: " + result.Manufacturers + "\r\n" +
                             "Settings: " + (result.SettingsImported ? "imported" : "not imported");
                if (!_chkManu.Checked && result.Manufacturers > 0) msg += " (manufacturer data came along in the file)";
                _log?.Invoke("Imported from " + path + ": " + result.Devices + " devices, " + result.Manufacturers + " manufacturers.");
                _lblStatus.Text = "Import OK - " + result.Devices + " devices, " + result.Manufacturers + " manufacturers.";
                _lblStatus.ForeColor = Color.Green;
                MessageBox.Show(this, msg, "Import", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Program.Report(ex, "IMPORT");
                _lblStatus.Text = "Import FAILED: " + ex.Message;
                _lblStatus.ForeColor = Color.Firebrick;
                MessageBox.Show(this,
                    "Import failed:\r\n\r\n" + ex.Message +
                    "\r\n\r\nIf this is the old app's file, make sure the original app is closed " +
                    "(its database is locked while running) and try again.\r\nDetails: " + Program.CrashLogPath,
                    "Import", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnImport.Enabled = true;
                _btnExport.Enabled = true;
            }
        }

        private void RunExport()
        {
            string path = _txtExportPath.Text.Trim();
            if (path.Length == 0)
            {
                MessageBox.Show(this, "Choose a destination file first.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                if (File.Exists(path))
                {
                    if (MessageBox.Show(this, "Overwrite " + Path.GetFileName(path) + "?", "Export",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    File.Delete(path);
                }
                if (_radWif.Checked)
                {
                    _db.BackupTo(path);
                }
                else
                {
                    LegacyImporter.ExportJson(_db, path);
                }
                _lblStatus.Text = "Export OK: " + path;
                _lblStatus.ForeColor = Color.Green;
                _log?.Invoke("Exported backup to " + path + ".");
                MessageBox.Show(this, "Backup created:\r\n" + path, "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Program.Report(ex, "EXPORT");
                _lblStatus.Text = "Export FAILED: " + ex.Message;
                _lblStatus.ForeColor = Color.Firebrick;
                MessageBox.Show(this, "Export failed:\r\n" + ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}