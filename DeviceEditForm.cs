using System;
using System.Drawing;
using System.Windows.Forms;

namespace WhoIsOnMyWiFi_Reborn
{
    /// <summary>Edit one device: friendly name, description and known state.</summary>
    public class DeviceEditForm : Form
    {
        public string DeviceName;
        public string Description;
        public bool IsKnown;

        private TextBox _txtName;
        private TextBox _txtDesc;
        private CheckBox _chkKnown;

        public DeviceEditForm(DeviceRecord d)
        {
            DeviceName = d.ComputerName;
            Description = d.Description;
            IsKnown = d.Known;

            Text = "Device " + (string.IsNullOrEmpty(d.LastIp) ? d.MacAddress : d.LastIp);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(430, 330);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9.25F);

            var info = new Label
            {
                Text =
                    "IP:        " + (string.IsNullOrEmpty(d.LastIp) ? "-" : d.LastIp) + "\r\n" +
                    "MAC:      " + (string.IsNullOrEmpty(d.MacAddress) ? "-" : d.MacAddress) + "\r\n" +
                    "Vendor:  " + (string.IsNullOrEmpty(d.Company) ? "-" : d.Company) + "\r\n" +
                    "First seen: " + d.FirstFound + "\r\n" +
                    "Last seen:  " + d.LastFound + "\r\n" +
                    (d.Block ? "Firewall block: ACTIVE" : "Firewall block: none"),
                Location = new Point(16, 16),
                Size = new Size(400, 100),
                Font = new Font("Consolas", 9F)
            };
            Controls.Add(info);

            int y = 130;
            Controls.Add(new Label { Text = "Name:", Location = new Point(16, y), AutoSize = true });
            _txtName = new TextBox { Location = new Point(110, y - 3), Width = 290, Text = DeviceName };
            Controls.Add(_txtName);
            y += 34;
            Controls.Add(new Label { Text = "Description:", Location = new Point(16, y), AutoSize = true });
            _txtDesc = new TextBox { Location = new Point(110, y - 3), Width = 290, Text = Description };
            Controls.Add(_txtDesc);
            y += 38;
            _chkKnown = new CheckBox
            {
                Text = "This is one of MY devices (mark as known)",
                Location = new Point(16, y),
                AutoSize = true,
                Checked = IsKnown
            };
            Controls.Add(_chkKnown);

            var btnOk = new Button { Text = "OK", Size = new Size(90, 30), Location = new Point(224, 268) };
            btnOk.Click += (s, e) =>
            {
                DeviceName = _txtName.Text.Trim();
                Description = _txtDesc.Text.Trim();
                IsKnown = _chkKnown.Checked;
                DialogResult = DialogResult.OK;
            };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(90, 30), Location = new Point(320, 268) };
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }
    }
}