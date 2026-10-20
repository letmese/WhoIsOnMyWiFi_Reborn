using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WhoIsOnMyWiFi_Reborn
{
    /// <summary>
    /// Modern Windows 11 Fluent Desktop Interface for Who Is On My WiFi Reborn.
    /// Powered by Microsoft.Web.WebView2 and styled after the Stitch PC Design.
    /// </summary>
    public class MainForm : Form
    {
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private Database _db;
        private AppSettings _settings;
        private string _dbError;

        private WebView2 _webView;
        private System.Windows.Forms.Timer _autoTimer;
        private System.Windows.Forms.Timer _clockTimer;
        private NotifyIcon _tray;

        private LogWriter _scanLog;
        private LogWriter _blockLog;

        private bool _scanning;
        private bool _reallyExit;
        private bool _baselineDone;
        private DateTime _lastScan = DateTime.MinValue;
        private DateTime _nextScan = DateTime.MinValue;
        private CancellationTokenSource _scanCts;
        private long _scanSessionId = 0;

        public MainForm()
        {
            try
            {
                InitializeDatabase();
            }
            catch (Exception ex)
            {
                _dbError = ex.Message;
                Program.Report(ex, "DB-INIT");
            }

            BuildUi();
            Load += MainForm_Load;
            FormClosing += MainForm_FormClosing;
        }

        private void InitializeDatabase()
        {
            _db = new Database();
            _db.Initialize();
            _settings = _db.LoadSettings();
            if (_settings == null) _settings = new AppSettings();
            if (string.IsNullOrEmpty(_settings.logfolder))
            {
                _settings.logfolder = Path.Combine(AppSettings.DataDirectory(), "logs");
                try { Directory.CreateDirectory(_settings.logfolder); } catch { }
            }
            _scanLog = new LogWriter(_settings.logfolder, "scan.log");
            _blockLog = new LogWriter(_settings.logfolder, "block.log");
        }

        private void BuildUi()
        {
            Text = "Who Is On My WiFi Reborn";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1240, 780);
            MinimumSize = new Size(960, 600);
            FormBorderStyle = FormBorderStyle.None;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(19, 19, 25);
            var appIcon = GetAppIcon();
            Icon = appIcon;

            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(19, 19, 25)
            };
            Controls.Add(_webView);

            _autoTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            _autoTimer.Tick += (s, e) => { _ = RunScanAsync(false); };

            _clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _clockTimer.Tick += (s, e) => BroadcastTelemetryTick();

            _tray = new NotifyIcon
            {
                Icon = appIcon,
                Text = "Who Is On My WiFi Reborn",
                Visible = true,
                ContextMenuStrip = BuildTrayMenu()
            };
            _tray.DoubleClick += (s, e) => ShowFromTray();
        }

        private Icon GetAppIcon()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(path)) return new Icon(path);
                string parentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "app.ico");
                if (File.Exists(parentPath)) return new Icon(parentPath);
            }
            catch { }
            return SystemIcons.Shield;
        }

        private ContextMenuStrip BuildTrayMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Scan Now", null, (s, e) => { _ = RunScanAsync(true); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open Dashboard", null, (s, e) => ShowFromTray());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => { _reallyExit = true; Close(); });
            return menu;
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            ScheduleNext(false);
            _clockTimer.Start();

            try
            {
                string dataFolder = Path.Combine(AppSettings.DataDirectory(), "webview2_data");
                Directory.CreateDirectory(dataFolder);
                CoreWebView2Environment env = null;
                try
                {
                    env = await CoreWebView2Environment.CreateAsync(null, dataFolder);
                    await _webView.EnsureCoreWebView2Async(env);
                }
                catch (COMException comEx) when (comEx.HResult == unchecked((int)0x800700AA) || comEx.ErrorCode == unchecked((int)0x800700AA))
                {
                    // Fallback to secondary process-isolated directory if default directory is locked
                    string altFolder = Path.Combine(AppSettings.DataDirectory(), "webview2_data_alt_" + Environment.ProcessId);
                    Directory.CreateDirectory(altFolder);
                    env = await CoreWebView2Environment.CreateAsync(null, altFolder);
                    await _webView.EnsureCoreWebView2Async(env);
                }

                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                _webView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;

                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "index.html");
                if (!File.Exists(htmlPath))
                {
                    htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "wwwroot", "index.html");
                    htmlPath = Path.GetFullPath(htmlPath);
                }

                if (File.Exists(htmlPath))
                {
                    _webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
                }
                else
                {
                    MessageBox.Show(this, "Could not find wwwroot/index.html at:\r\n" + htmlPath, "Missing Asset", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                Program.Report(ex, "WEBVIEW2-LOAD");
                MessageBox.Show(this, "Failed to initialize Microsoft Edge WebView2 runtime:\r\n" + ex.Message,
                    "WebView2 Required", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------------ Win32 Window Resizing
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.WM_SHOWME)
            {
                ShowFromTray();
                return;
            }

            const int WM_NCHITTEST = 0x84;
            const int HTLEFT = 10;
            const int HTRIGHT = 11;
            const int HTTOP = 12;
            const int HTTOPLEFT = 13;
            const int HTTOPRIGHT = 14;
            const int HTBOTTOM = 15;
            const int HTBOTTOMLEFT = 16;
            const int HTBOTTOMRIGHT = 17;

            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if (WindowState == FormWindowState.Normal)
                {
                    Point p = PointToClient(new Point(m.LParam.ToInt32()));
                    int b = 6;
                    if (p.X < b && p.Y < b) m.Result = (IntPtr)HTTOPLEFT;
                    else if (p.X > ClientSize.Width - b && p.Y < b) m.Result = (IntPtr)HTTOPRIGHT;
                    else if (p.X < b && p.Y > ClientSize.Height - b) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (p.X > ClientSize.Width - b && p.Y > ClientSize.Height - b) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else if (p.X < b) m.Result = (IntPtr)HTLEFT;
                    else if (p.X > ClientSize.Width - b) m.Result = (IntPtr)HTRIGHT;
                    else if (p.Y < b) m.Result = (IntPtr)HTTOP;
                    else if (p.Y > ClientSize.Height - b) m.Result = (IntPtr)HTBOTTOM;
                }
                return;
            }
            base.WndProc(ref m);
        }

        // ------------------------------------------------------------------ Message Bus with UI
        private void WebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json = e.WebMessageAsJson;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("action", out var actElem)) return;
                string action = actElem.GetString();

                switch (action)
                {
                    case "theme_changed":
                        if (root.TryGetProperty("isLight", out var ilElem))
                        {
                            bool isLight = ilElem.GetBoolean();
                            Color bg = isLight ? Color.FromArgb(241, 245, 249) : Color.FromArgb(15, 23, 42);
                            BackColor = bg;
                            _webView.DefaultBackgroundColor = bg;
                        }
                        break;
                    case "init":
                        SendFullState();
                        break;

                    case "scan_start":
                        _ = RunScanAsync(true);
                        break;

                    case "scan_stop":
                        CancelScan();
                        break;

                    case "autoscan_toggle":
                        if (root.TryGetProperty("enabled", out var enElem))
                        {
                            _settings.autoscan = enElem.GetBoolean();
                            SaveSettings();
                            ScheduleNext(false);
                            SendFullState();
                        }
                        break;

                    case "continuous_toggle":
                        if (root.TryGetProperty("enabled", out var ctElem))
                        {
                            _settings.scan_continuous = ctElem.GetBoolean();
                            SaveSettings();
                            ScheduleNext(false);
                            SendFullState();
                        }
                        break;

                    case "filt_connected_toggle":
                        if (root.TryGetProperty("enabled", out var fcElem))
                        {
                            _settings.filt_connected = fcElem.GetBoolean();
                            SaveSettings();
                        }
                        break;

                    case "device_save":
                        HandleDeviceSave(root);
                        break;

                    case "device_block":
                        HandleDeviceBlock(root);
                        break;

                    case "auto_block_all":
                        HandleAutoBlockAll();
                        break;

                    case "device_delete":
                        if (root.TryGetProperty("digisig", out var dsElem))
                        {
                            string ds = dsElem.GetString();
                            _db.DeleteDeviceByDigisig(ds);
                            _scanLog?.Write("DEVICE DELETED: " + ds);
                            SendFullState();
                        }
                        break;

                    case "devices_delete_batch":
                        if (root.TryGetProperty("digisigs", out var dssElem) && dssElem.ValueKind == JsonValueKind.Array)
                        {
                            var list = new List<string>();
                            foreach (var item in dssElem.EnumerateArray())
                            {
                                string s = item.GetString();
                                if (!string.IsNullOrEmpty(s)) list.Add(s);
                            }
                            _db.DeleteDevicesByDigisig(list);
                            _scanLog?.Write($"BATCH DELETED: {list.Count} devices removed.");
                            SendFullState();
                        }
                        break;

                    case "delete_all_intruders":
                        int delCount = _db.DeleteAllIntruders();
                        _scanLog?.Write($"INTRUDER CLEANUP: Deleted {delCount} intruder device(s) from database.");
                        SendFullState();
                        break;

                    case "device_port_scan":
                        if (root.TryGetProperty("ip", out var ipElem))
                        {
                            string targetIp = ipElem.GetString();
                            _ = RunPortScanAsync(targetIp);
                        }
                        break;

                    case "settings_save":
                        HandleSettingsSave(root);
                        break;

                    case "subnet_add":
                        if (root.TryGetProperty("subnet", out var snElem))
                        {
                            string snText = snElem.GetString();
                            if (NetworkScanner.ParseRange(snText, out _, out _))
                            {
                                _settings.iprange = snText;
                                SaveSettings();
                                SendFullState();
                            }
                        }
                        break;

                    case "get_logs":
                        SendLogs();
                        break;

                    case "export_data":
                        BackupDatabase();
                        break;

                    case "import_data":
                        HandleImportData(root);
                        break;

                    case "window_drag":
                        ReleaseCapture();
                        SendMessage(Handle, 0xA1, 2, 0);
                        break;

                    case "window_minimize":
                        WindowState = FormWindowState.Minimized;
                        break;

                    case "window_maximize":
                        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                        break;

                    case "window_close":
                        Close();
                        break;
                }
            }
            catch (Exception ex)
            {
                Program.Report(ex, "WEB-MESSAGE");
            }
        }

        private void SendFullState()
        {
            try
            {
                var snap = NetworkTelemetryService.CaptureSnapshot();
                var devList = GetDeviceDtos(snap.GatewayIp);

                var packet = new
                {
                    type = "init_state",
                    devices = devList,
                    settings = new
                    {
                        autoscan = _settings.autoscan,
                        frequency = _settings.frequency,
                        continuous = _settings.scan_continuous,
                        ignore_gateway = _settings.ignoregateway,
                        minimize_to_tray = _settings.minimize,
                        sound_alert = _settings.not_sound,
                        popup_alert = _settings.not_message,
                        filt_connected = _settings.filt_connected
                    },
                    telemetry = snap
                };

                PostToWeb(JsonSerializer.Serialize(packet));
            }
            catch (Exception ex)
            {
                Program.Report(ex, "SEND-STATE");
            }
        }

        private List<object> GetDeviceDtos(string gatewayIp)
        {
            var list = new List<object>();
            try
            {
                var devices = _db.LoadDevices(false);
                devices.Sort((a, b) => NetworkScanner.IpToUint(a.LastIp).CompareTo(NetworkScanner.IpToUint(b.LastIp)));

                foreach (var d in devices)
                {
                    list.Add(new
                    {
                        d.DigiSig,
                        d.MacAddress,
                        ComputerName = string.IsNullOrEmpty(d.ComputerName) ? "Unknown Device" : d.ComputerName,
                        d.Description,
                        Company = string.IsNullOrEmpty(d.Company) ? "Generic Hardware" : d.Company,
                        d.LastIp,
                        Active = d.Connected,
                        d.Known,
                        d.Block,
                        FirstFound = string.IsNullOrEmpty(d.FirstFound) ? "Today" : d.FirstFound,
                        LastFound = string.IsNullOrEmpty(d.LastFound) ? "Active now" : d.LastFound,
                        DeviceType = NetworkTelemetryService.InferDeviceType(d, gatewayIp)
                    });
                }
            }
            catch { }
            return list;
        }

        private void BroadcastTelemetryTick()
        {
            try
            {
                if (_webView == null || _webView.CoreWebView2 == null) return;

                var snap = NetworkTelemetryService.CaptureSnapshot();
                int nextSeconds = 0;
                if (_nextScan > DateTime.Now)
                {
                    nextSeconds = (int)(_nextScan - DateTime.Now).TotalSeconds;
                }

                var tick = new
                {
                    type = "telemetry_tick",
                    telemetry = snap,
                    nextScanSeconds = nextSeconds,
                    isContinuous = _settings.scan_continuous
                };

                PostToWeb(JsonSerializer.Serialize(tick));
            }
            catch { }
        }

        private async Task RunPortScanAsync(string ip)
        {
            try
            {
                var ports = await PortScanner.ScanPortsAsync(ip, 500);
                var dto = ports.Select(p => new
                {
                    port = p.Port,
                    name = p.Name,
                    service = p.Service,
                    isCritical = p.IsCritical
                }).ToList();

                var packet = new
                {
                    type = "port_scan_results",
                    ip = ip,
                    ports = dto
                };

                PostToWeb(JsonSerializer.Serialize(packet));
            }
            catch (Exception ex)
            {
                Program.Report(ex, "PORT-SCAN");
            }
        }

        private void HandleDeviceSave(JsonElement root)
        {
            try
            {
                string digi = root.GetProperty("digisig").GetString();
                string name = root.GetProperty("name").GetString();
                string desc = root.GetProperty("desc").GetString();
                bool isKnown = root.GetProperty("isKnown").GetBoolean();

                _db.UpdateDeviceName(digi, name);
                _db.UpdateDeviceDescription(digi, desc);
                _db.UpdateDeviceKnown(digi, isKnown);

                SendFullState();
            }
            catch (Exception ex)
            {
                Program.Report(ex, "DEVICE-SAVE");
            }
        }

        private void HandleDeviceBlock(JsonElement root)
        {
            try
            {
                string digi = root.GetProperty("digisig").GetString();
                bool block = root.GetProperty("block").GetBoolean();

                var devs = _db.LoadDevices(false);
                var d = devs.FirstOrDefault(x => x.DigiSig == digi);
                if (d != null)
                {
                    string err = "";
                    bool ok = block ? FirewallService.BlockDevice(d, out err) : FirewallService.UnblockDevice(d, out err);
                    _db.UpdateDeviceBlock(digi, block && ok);
                    _blockLog?.Write((block ? "BLOCK " : "UNBLOCK ") + d.LastIp + " " + d.MacAddress + (ok ? " OK" : " FAILED"));
                }

                SendFullState();
            }
            catch (Exception ex)
            {
                Program.Report(ex, "DEVICE-BLOCK");
            }
        }

        private void HandleAutoBlockAll()
        {
            try
            {
                var devs = _db.LoadDevices(false);
                var unknowns = devs.Where(d => !d.Known && !d.Block).ToList();

                foreach (var d in unknowns)
                {
                    bool ok = FirewallService.BlockDevice(d, out string err);
                    _db.UpdateDeviceBlock(d.DigiSig, ok);
                    _blockLog?.Write("AUTO-BLOCK INTRUDER: " + d.LastIp + " " + d.MacAddress + (ok ? " OK" : " ERR: " + err));
                }

                SendFullState();
            }
            catch (Exception ex)
            {
                Program.Report(ex, "AUTO-BLOCK");
            }
        }

        private void HandleSettingsSave(JsonElement root)
        {
            try
            {
                if (root.TryGetProperty("settings", out var sElem))
                {
                    if (sElem.TryGetProperty("autoscan", out var a)) _settings.autoscan = a.GetBoolean();
                    if (sElem.TryGetProperty("frequency", out var f)) _settings.frequency = f.GetInt32();
                    if (sElem.TryGetProperty("continuous", out var c)) _settings.scan_continuous = c.GetBoolean();
                    if (sElem.TryGetProperty("ignore_gateway", out var ig)) _settings.ignoregateway = ig.GetBoolean();
                    if (sElem.TryGetProperty("minimize_to_tray", out var m)) _settings.minimize = m.GetBoolean();
                    if (sElem.TryGetProperty("sound_alert", out var sa)) _settings.not_sound = sa.GetBoolean();
                    if (sElem.TryGetProperty("popup_alert", out var pa)) _settings.not_message = pa.GetBoolean();
                    if (sElem.TryGetProperty("filt_connected", out var fc)) _settings.filt_connected = fc.GetBoolean();

                    SaveSettings();
                    ScheduleNext(false);
                    SendFullState();
                }
            }
            catch (Exception ex)
            {
                Program.Report(ex, "SETTINGS-SAVE");
            }
        }

        private void SendLogs()
        {
            try
            {
                string text = "";
                string scanLogFile = Path.Combine(_settings.logfolder, "scan.log");
                string blockLogFile = Path.Combine(_settings.logfolder, "block.log");

                if (File.Exists(blockLogFile))
                {
                    text += "=== FIREWALL BLOCK LOGS ===" + Environment.NewLine + File.ReadAllText(blockLogFile) + Environment.NewLine + Environment.NewLine;
                }
                if (File.Exists(scanLogFile))
                {
                    text += "=== NETWORK SCAN LOGS ===" + Environment.NewLine + File.ReadAllText(scanLogFile);
                }

                if (string.IsNullOrWhiteSpace(text)) text = "No logs recorded yet. Run a network scan to generate logs.";

                var packet = new { type = "logs_data", logs = text };
                PostToWeb(JsonSerializer.Serialize(packet));
            }
            catch (Exception ex)
            {
                PostToWeb(JsonSerializer.Serialize(new { type = "logs_data", logs = "Error reading logs: " + ex.Message }));
            }
        }

        private void PostToWeb(string json)
        {
            try
            {
                if (_webView != null && _webView.CoreWebView2 != null)
                {
                    BeginInvoke(new Action(() =>
                    {
                        try { _webView.CoreWebView2.PostWebMessageAsJson(json); } catch { }
                    }));
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------ Scanning Pipeline
        private void CancelScan()
        {
            try { _scanCts?.Cancel(); } catch { }
        }

        private async Task RunScanAsync(bool manual)
        {
            if (_scanning) return;
            if (_db == null || !string.IsNullOrEmpty(_dbError)) return;

            long currentScanId = Interlocked.Increment(ref _scanSessionId);
            _scanning = true;
            _scanCts = new CancellationTokenSource();
            bool cancelled = false;

            PostToWeb(JsonSerializer.Serialize(new
            {
                type = "scan_progress",
                scanId = currentScanId,
                text = "Starting sweep...",
                percent = 5
            }));

            try
            {
                var found = new List<DeviceRecord>();
                var opt = new NetworkScanner.ScanOptions
                {
                    Ping = _settings.scan_ping,
                    Randomize = _settings.scan_randomize,
                    DelayMs = _settings.scan_delay ? 30 : 0,
                    IgnoreGateway = _settings.ignoregateway
                };

                var progress = new Progress<ScanProgressReport>(rep =>
                {
                    PostToWeb(JsonSerializer.Serialize(new
                    {
                        type = "scan_progress",
                        scanId = currentScanId,
                        text = rep.Message,
                        currentIp = rep.CurrentIp,
                        probed = rep.Probed,
                        total = rep.Total,
                        found = rep.Found,
                        percent = rep.Percent
                    }));
                });

                foreach ((long id, string range, bool netbios, string gw) in BuildRangeList())
                {
                    opt.NetBios = netbios;
                    opt.GatewayIp = gw;
                    var devs = await NetworkScanner.ScanAsync(range, opt, progress, _scanCts.Token);
                    foreach (var d in devs) d.SubnetId = id;
                    found.AddRange(devs);
                    _scanCts.Token.ThrowIfCancellationRequested();
                }

                ApplyResults(found);
            }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception ex)
            {
                Program.Report(ex, "SCAN");
            }
            finally
            {
                _scanning = false;
                _lastScan = DateTime.Now;
                ScheduleNext(manual);

                _scanLog?.Write(cancelled ? "Scan cancelled." : "Scan finished at " + _lastScan.ToString("yyyy-MM-dd HH:mm:ss") + ".");

                var snap = NetworkTelemetryService.CaptureSnapshot();
                var devList = GetDeviceDtos(snap.GatewayIp);

                PostToWeb(JsonSerializer.Serialize(new
                {
                    type = "scan_done",
                    scanId = currentScanId,
                    devices = devList
                }));
            }
        }

        private List<(long, string, bool, string)> BuildRangeList()
        {
            var list = new List<(long, string, bool, string)>();
            try
            {
                var subnets = _db.LoadSubnets();
                foreach (var sn in subnets)
                {
                    if (sn.Scan && !string.IsNullOrEmpty(sn.IpRange))
                    {
                        list.Add((sn.SubnetId, sn.IpRange, sn.CheckNetbios, sn.GatewayIp));
                    }
                }
            }
            catch { }
            if (list.Count == 0)
            {
                string range = _settings.iprange;
                if (string.IsNullOrEmpty(range))
                {
                    range = NetworkScanner.DetectFirstSubnet();
                    if (!string.IsNullOrEmpty(range))
                    {
                        _settings.iprange = range;
                        SaveSettings();
                    }
                }
                string gw = "";
                try { var a = NetworkScanner.GetBestAdapter(); if (a != null) gw = a.Gateway; } catch { }
                if (!string.IsNullOrEmpty(range)) list.Add((0, range, true, gw));
            }
            return list;
        }

        private void ApplyResults(List<DeviceRecord> found)
        {
            _db.SetAllConnected(false);

            var existing = new Dictionary<string, DeviceRecord>();
            foreach (var d in _db.LoadDevices(false)) existing[d.DigiSig] = d;

            var brandNew = new List<DeviceRecord>();
            foreach (var d in found)
            {
                if (!NetworkScanner.IsValidHardwareMac(d.MacAddress)) continue;

                if (string.IsNullOrEmpty(d.Company) && d.MacAddress.Length >= 17)
                    d.Company = _db.GetCompanyByMac(d.MacAddress);

                if (existing.TryGetValue(d.DigiSig, out DeviceRecord old))
                {
                    d.Known = old.Known;
                    d.Block = old.Block;
                    d.FirstFound = old.FirstFound;
                    // Preserve user's friendly name and description across scans
                    if (!string.IsNullOrEmpty(old.ComputerName) && !old.ComputerName.StartsWith("UNKNOWN", StringComparison.OrdinalIgnoreCase))
                    {
                        d.ComputerName = old.ComputerName;
                    }
                    else if (string.IsNullOrEmpty(d.ComputerName))
                    {
                        d.ComputerName = old.ComputerName;
                    }
                    if (string.IsNullOrEmpty(d.Description)) d.Description = old.Description;
                    if (string.IsNullOrEmpty(d.Company) && !string.IsNullOrEmpty(old.Company)) d.Company = old.Company;
                }
                else
                {
                    brandNew.Add(d);
                }
                _db.UpsertDevice(d);
            }

            // Baseline scan logic
            if (existing.Count == 0 && brandNew.Count > 0 && !_baselineDone)
            {
                _baselineDone = true;
                foreach (var d in brandNew)
                {
                    d.Known = true;
                    _db.UpsertDevice(d);
                }
                _scanLog?.Write("Baseline scan: " + brandNew.Count + " device(s) established as trusted baseline.");
                return;
            }

            var unknownNew = brandNew.Where(d => !d.Known).ToList();
            if (unknownNew.Count > 0)
            {
                _scanLog?.Write(unknownNew.Count + " NEW UNKNOWN device(s): " +
                    string.Join(", ", unknownNew.Select(d => d.LastIp + " (" + (string.IsNullOrEmpty(d.MacAddress) ? "no MAC" : d.MacAddress) + ")")));
                NotificationManager.HandleNewUnknownDevices(unknownNew, _settings, _db, _tray, this, _scanLog);
            }
        }

        private void ScheduleNext(bool manual)
        {
            if (_settings == null) return;
            if (_settings.scan_continuous)
            {
                _autoTimer.Interval = 2000;
                _autoTimer.Enabled = true;
                _nextScan = DateTime.Now.AddSeconds(2);
            }
            else if (_settings.autoscan && _settings.frequency > 0)
            {
                _autoTimer.Interval = Math.Max(1, _settings.frequency) * 60000;
                _autoTimer.Enabled = true;
                _nextScan = DateTime.Now.AddMilliseconds(_autoTimer.Interval);
            }
            else
            {
                _autoTimer.Enabled = false;
                _nextScan = DateTime.MinValue;
            }
        }

        private void SaveSettings()
        {
            try { _db.SaveSettings(_settings); }
            catch (Exception ex) { Program.Report(ex, "SAVE"); }
        }

        private void BackupDatabase()
        {
            using var dlg = new SaveFileDialog
            {
                Title = "Backup Reborn Database",
                Filter = "Reborn backup (*.wif)|*.wif",
                FileName = "reborn-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".wif"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                _db.BackupTo(dlg.FileName);
                MessageBox.Show(this, "Backup successfully created:\r\n" + dlg.FileName, "Backup Created", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Backup failed:\r\n" + ex.Message, "Backup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void HandleImportData(JsonElement root)
        {
            try
            {
                string filePath = "";
                if (root.TryGetProperty("path", out var pElem))
                {
                    filePath = pElem.GetString() ?? "";
                }

                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    string defaultDir = Directory.Exists(@"C:\Users\xbxga\Downloads")
                        ? @"C:\Users\xbxga\Downloads"
                        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                    using var dlg = new OpenFileDialog
                    {
                        Title = "Import Settings and Devices from Who Is On My WiFi Backup",
                        Filter = "Who Is On My WiFi Backup (*.wif;*.db;*.json;*.dat;*.dl2)|*.wif;*.db;*.json;*.dat;*.dl2|All Files (*.*)|*.*",
                        InitialDirectory = defaultDir,
                        FileName = "backup.wif"
                    };

                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    filePath = dlg.FileName;
                }

                if (File.Exists(filePath))
                {
                    var res = LegacyImporter.ImportFile(filePath, _db, true);
                    _settings = _db.LoadSettings();
                    ScheduleNext(false);
                    SendFullState();

                    string summary = $"Successfully imported {res.Devices} device(s), {res.Subnets} subnet(s), and settings from {Path.GetFileName(filePath)}!";
                    _scanLog?.Write("IMPORT: " + summary);

                    PostToWeb(JsonSerializer.Serialize(new
                    {
                        type = "import_result",
                        success = true,
                        message = summary,
                        devices = res.Devices,
                        subnets = res.Subnets,
                        settings = res.SettingsImported
                    }));
                }
            }
            catch (Exception ex)
            {
                Program.Report(ex, "IMPORT");
                PostToWeb(JsonSerializer.Serialize(new
                {
                    type = "import_result",
                    success = false,
                    message = "Import failed: " + ex.Message
                }));
            }
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _scanLog?.Write("FORM CLOSING: Reason=" + e.CloseReason + ", UserClosing=" + (e.CloseReason == CloseReason.UserClosing) + ", reallyExit=" + _reallyExit);
            if (!_reallyExit && _settings != null && _settings.minimize)
            {
                e.Cancel = true;
                Hide();
                if (_settings.windowclosenotice)
                {
                    _tray.BalloonTipTitle = "Running in Background";
                    _tray.BalloonTipText = "Who Is On My WiFi Reborn is actively watching your network in the background.\r\nRight-click tray icon to Exit.";
                    _tray.BalloonTipIcon = ToolTipIcon.Info;
                    _tray.ShowBalloonTip(4000);
                    _settings.windowclosenotice = false;
                    SaveSettings();
                }
                return;
            }
            _reallyExit = true;
            CancelScan();
            try { if (_tray != null) _tray.Visible = false; } catch { }
        }

        public void SafeDispose()
        {
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
        }
    }
}