using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WhoIsOnMyWiFi_Reborn
{
    /// <summary>Manage the IP ranges that get scanned (mirrors the original app's subnet list).</summary>
    public class SubnetEditorForm : Form
    {
        private readonly Database _db;
        private readonly AppSettings _s;
        private ListView _list;

        public SubnetEditorForm(Database db, AppSettings s)
        {
            _db = db;
            _s = s;
            BuildUi();
            Reload();
        }

        private void BuildUi()
        {
            Text = "IP Ranges";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9.25F);

            _list = new ListView
            {
                Location = new Point(14, 14),
                Size = new Size(610, 300),
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false
            };
            _list.Columns.Add("Scan", 48, HorizontalAlignment.Center);
            _list.Columns.Add("IP Range", 200);
            _list.Columns.Add("NetBIOS", 60, HorizontalAlignment.Center);
            _list.Columns.Add("Gateway", 110);
            _list.Columns.Add("Ping", 50, HorizontalAlignment.Center);
            _list.DoubleClick += (s, e) => EditSelected();
            Controls.Add(_list);

            var btnAdd = new Button { Text = "Add...", Location = new Point(14, 326), Size = new Size(85, 30) };
            btnAdd.Click += (s, e) => AddOrEdit(null);
            var btnEdit = new Button { Text = "Edit...", Location = new Point(105, 326), Size = new Size(85, 30) };
            btnEdit.Click += (s, e) => EditSelected();
            var btnDel = new Button { Text = "Delete", Location = new Point(196, 326), Size = new Size(85, 30) };
            btnDel.Click += (s, e) => DeleteSelected();
            var btnDetect = new Button { Text = "Add current network", Location = new Point(300, 326), Size = new Size(140, 30) };
            btnDetect.Click += (s, e) => DetectCurrent();
            var btnClose = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Location = new Point(539, 326), Size = new Size(85, 30) };
            CancelButton = btnClose;

            Controls.Add(btnAdd);
            Controls.Add(btnEdit);
            Controls.Add(btnDel);
            Controls.Add(btnDetect);
            Controls.Add(btnClose);
        }

        private void Reload()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            try
            {
                foreach (var sn in _db.LoadSubnets())
                {
                    var it = new ListViewItem(sn.Scan ? "YES" : "-");
                    it.SubItems.Add(sn.IpRange);
                    it.SubItems.Add(sn.CheckNetbios ? "on" : "off");
                    it.SubItems.Add(string.IsNullOrEmpty(sn.GatewayIp) ? "-" : sn.GatewayIp);
                    it.SubItems.Add(sn.CheckIp ? "on" : "off");
                    it.Tag = sn;
                    _list.Items.Add(it);
                }
            }
            catch { }
            _list.EndUpdate();
        }

        private void EditSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            if (_list.SelectedItems[0].Tag is SubnetRecord sn) AddOrEdit(sn);
        }

        private void AddOrEdit(SubnetRecord existing)
        {
            using var dlg = new SubnetEditDialog(existing);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _db.UpsertSubnet(dlg.Record);
                if (dlg.Record.Scan)
                {
                    // only one range can be "the active scan range" in the single-range setting
                    _s.iprange = dlg.Record.IpRange;
                }
                Reload();
            }
        }

        private void DeleteSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            if (_list.SelectedItems[0].Tag is SubnetRecord sn)
            {
                if (MessageBox.Show(this, "Delete range " + sn.IpRange + "?", "Delete",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _db.DeleteSubnet(sn.SubnetId);
                    Reload();
                }
            }
        }

        private void DetectCurrent()
        {
            var sn = new SubnetRecord();
            try
            {
                var a = NetworkScanner.GetBestAdapter();
                if (a == null)
                {
                    MessageBox.Show(this, "No active network adapter with an IP address was found.", "Reborn",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                sn.IpRange = NetworkScanner.DetectFirstSubnet();
                sn.GatewayIp = a.Gateway;
            }
            catch { }
            using var dlg = new SubnetEditDialog(sn);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _db.UpsertSubnet(dlg.Record);
                if (dlg.Record.Scan) _s.iprange = dlg.Record.IpRange;
                Reload();
            }
        }
    }

    /// <summary>Add / edit a single IP range.</summary>
    public class SubnetEditDialog : Form
    {
        public SubnetRecord Record;
        private TextBox _txtRange;
        private CheckBox _chkScan, _chkPing, _chkNetbios;
        private TextBox _txtGateway;
        private Label _lblInfo;

        public SubnetEditDialog(SubnetRecord existing)
        {
            Record = existing ?? new SubnetRecord();
            Text = existing == null ? "Add IP range" : "Edit IP range";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(470, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9.25F);

            Controls.Add(new Label { Text = "IP range:", Location = new Point(16, 18), AutoSize = true });
            _txtRange = new TextBox { Location = new Point(120, 15), Width = 250, Text = Record.IpRange };
            _txtRange.TextChanged += (s, e) => ValidateRange();
            Controls.Add(_txtRange);

            _lblInfo = new Label { Text = "Accepted: 192.168.1.1-192.168.1.254, 192.168.1.0/24, 192.168.1", Location = new Point(120, 42), AutoSize = true, ForeColor = Color.DimGray };
            Controls.Add(_lblInfo);

            _chkScan = new CheckBox { Text = "Scan this range", Location = new Point(16, 70), AutoSize = true, Checked = Record.Scan || existing == null };
            _chkPing = new CheckBox { Text = "Ping sweep (recommended)", Location = new Point(16, 98), AutoSize = true, Checked = Record.CheckIp || existing == null };
            _chkNetbios = new CheckBox { Text = "Resolve NetBIOS names", Location = new Point(16, 126), AutoSize = true, Checked = Record.CheckNetbios || existing == null };
            Controls.Add(_chkScan);
            Controls.Add(_chkPing);
            Controls.Add(_chkNetbios);

            Controls.Add(new Label { Text = "Gateway IP:", Location = new Point(16, 158), AutoSize = true });
            _txtGateway = new TextBox { Location = new Point(120, 155), Width = 150, Text = Record.GatewayIp };
            Controls.Add(_txtGateway);

            var btnOk = new Button { Text = "OK", Size = new Size(90, 30), Location = new Point(268, 224), Enabled = false };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(90, 30), Location = new Point(364, 224) };
            btnOk.Click += (s, e) =>
            {
                Record.IpRange = _txtRange.Text.Trim();
                Record.Scan = _chkScan.Checked;
                Record.CheckIp = _chkPing.Checked;
                Record.CheckNetbios = _chkNetbios.Checked;
                Record.CheckPort80 = false;
                Record.CheckPort137 = _chkNetbios.Checked;
                Record.GatewayIp = _txtGateway.Text.Trim();
                DialogResult = DialogResult.OK;
            };
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = null;
            CancelButton = btnCancel;
            ValidateRange();
        }

        private void ValidateRange()
        {
            bool ok = NetworkScanner.ParseRange(_txtRange.Text, out uint start, out uint end);
            if (ok)
            {
                _lblInfo.Text = "Valid: " + NetworkScanner.IpToString(start) + " to " + NetworkScanner.IpToString(end);
                _lblInfo.ForeColor = Color.Green;
            }
            else
            {
                _lblInfo.Text = "Accepted: 192.168.1.1-192.168.1.254, 192.168.1.0/24, 192.168.1";
                _lblInfo.ForeColor = Color.DimGray;
            }
            foreach (Control c in Controls)
            {
                if (c is Button b && b.Text == "OK") b.Enabled = ok;
            }
        }
    }
}