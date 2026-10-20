using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WhoIsOnMyWiFi_Reborn
{
    public class TelemetrySnapshot
    {
        public string Subnet { get; set; } = "192.168.1.0/24";
        public string LocalIp { get; set; } = "127.0.0.1";
        public string GatewayIp { get; set; } = "192.168.1.1";
        public string AdapterName { get; set; } = "Network Adapter";
        public string AdapterDescription { get; set; } = "Local Interface";
        public string ConnectionType { get; set; } = "Ethernet";
        public string LinkSpeed { get; set; } = "1000 Mbps";
        public string SignalStrength { get; set; } = "100%";
        public string SignalDbm { get; set; } = "-45 dBm";
        public int PingLatencyMs { get; set; } = 4;
        public double DownloadSpeedMbps { get; set; } = 0.0;
        public double UploadSpeedMbps { get; set; } = 0.0;
        public string DownloadSpeedStr { get; set; } = "0.0 KB/s";
        public string UploadSpeedStr { get; set; } = "0.0 KB/s";
        public bool IsOnline { get; set; } = true;
    }

    public static class NetworkTelemetryService
    {
        private static long _lastBytesRecv = 0;
        private static long _lastBytesSent = 0;
        private static DateTime _lastTimestamp = DateTime.UtcNow;
        private static NetworkInterface _activeNic = null;

        public static TelemetrySnapshot CaptureSnapshot()
        {
            var snap = new TelemetrySnapshot();
            try
            {
                NetworkInterface bestNic = null;
                IPInterfaceProperties bestProps = null;
                string gwIp = "";
                string localIp = "";
                string mask = "255.255.255.0";

                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var props = nic.GetIPProperties();
                    if (props.GetIPv4Properties() == null) continue;

                    foreach (var g in props.GatewayAddresses)
                    {
                        if (g.Address.AddressFamily == AddressFamily.InterNetwork && !string.IsNullOrEmpty(g.Address.ToString()) && g.Address.ToString() != "0.0.0.0")
                        {
                            gwIp = g.Address.ToString();
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(gwIp))
                    {
                        bestNic = nic;
                        bestProps = props;
                        foreach (var u in props.UnicastAddresses)
                        {
                            if (u.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                localIp = u.Address.ToString();
                                if (u.IPv4Mask != null) mask = u.IPv4Mask.ToString();
                                break;
                            }
                        }
                        break;
                    }
                }

                if (bestNic != null)
                {
                    _activeNic = bestNic;
                    snap.AdapterName = bestNic.Name;
                    snap.AdapterDescription = bestNic.Description;
                    snap.GatewayIp = gwIp;
                    snap.LocalIp = localIp;
                    snap.ConnectionType = bestNic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Gigabit Ethernet";

                    long speed = bestNic.Speed;
                    if (speed > 0)
                    {
                        if (speed >= 1_000_000_000) snap.LinkSpeed = (speed / 1_000_000_000.0).ToString("0.#") + " Gbps";
                        else snap.LinkSpeed = (speed / 1_000_000).ToString("0") + " Mbps";
                    }

                    // Subnet CIDR calculation
                    if (NetworkScanner.ParseIp(localIp, out uint ipVal) && NetworkScanner.ParseIp(mask, out uint maskVal))
                    {
                        int bits = 0;
                        for (int i = 0; i < 32; i++)
                        {
                            if (((maskVal >> (31 - i)) & 1) == 1) bits++;
                        }
                        uint net = ipVal & maskVal;
                        snap.Subnet = NetworkScanner.IpToString(net) + "/" + bits;
                    }

                    // Live traffic delta calculation
                    try
                    {
                        var stats = bestNic.GetIPv4Statistics();
                        long nowRecv = stats.BytesReceived;
                        long nowSent = stats.BytesSent;
                        DateTime nowTime = DateTime.UtcNow;

                        if (_lastBytesRecv > 0 && nowTime > _lastTimestamp)
                        {
                            double elapsedSec = (nowTime - _lastTimestamp).TotalSeconds;
                            if (elapsedSec > 0.1)
                            {
                                long diffRecv = Math.Max(0, nowRecv - _lastBytesRecv);
                                long diffSent = Math.Max(0, nowSent - _lastBytesSent);

                                double recvBps = diffRecv / elapsedSec;
                                double sentBps = diffSent / elapsedSec;

                                snap.DownloadSpeedMbps = (recvBps * 8) / 1_000_000.0;
                                snap.UploadSpeedMbps = (sentBps * 8) / 1_000_000.0;

                                snap.DownloadSpeedStr = FormatSpeed(recvBps);
                                snap.UploadSpeedStr = FormatSpeed(sentBps);
                            }
                        }

                        _lastBytesRecv = nowRecv;
                        _lastBytesSent = nowSent;
                        _lastTimestamp = nowTime;
                    }
                    catch { }

                    // Wi-Fi details if wireless
                    if (bestNic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        ReadWlanTelemetry(snap);
                    }
                }
            }
            catch { }

            return snap;
        }

        public static async Task<int> PingLatencyAsync(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return -1;
            try
            {
                using var p = new Ping();
                var reply = await p.SendPingAsync(ip, 1200);
                if (reply.Status == IPStatus.Success)
                {
                    return (int)reply.RoundtripTime;
                }
            }
            catch { }
            return -1;
        }

        private static void ReadWlanTelemetry(TelemetrySnapshot snap)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(2000);

                    var mRadio = Regex.Match(output, @"Radio type\s*:\s*(.+)", RegexOptions.IgnoreCase);
                    if (mRadio.Success)
                    {
                        string radio = mRadio.Groups[1].Value.Trim();
                        if (radio.IndexOf("802.11ax", StringComparison.OrdinalIgnoreCase) >= 0) snap.ConnectionType = "Wi-Fi 6 (802.11ax)";
                        else if (radio.IndexOf("802.11ac", StringComparison.OrdinalIgnoreCase) >= 0) snap.ConnectionType = "Wi-Fi 5 (802.11ac)";
                        else snap.ConnectionType = "Wi-Fi (" + radio + ")";
                    }

                    var mSignal = Regex.Match(output, @"Signal\s*:\s*(\d+)%", RegexOptions.IgnoreCase);
                    if (mSignal.Success)
                    {
                        snap.SignalStrength = mSignal.Groups[1].Value.Trim() + "%";
                        if (int.TryParse(mSignal.Groups[1].Value.Trim(), out int pct))
                        {
                            int dbm = -100 + (pct / 2);
                            snap.SignalDbm = dbm + " dBm";
                        }
                    }

                    var mRx = Regex.Match(output, @"Receive rate \(Mbps\)\s*:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (mRx.Success)
                    {
                        snap.LinkSpeed = mRx.Groups[1].Value.Trim() + " Mbps";
                    }
                }
            }
            catch { }
        }

        private static string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec >= 1_000_000)
            {
                return (bytesPerSec / 1_000_000.0).ToString("0.0") + " MB/s";
            }
            else if (bytesPerSec >= 1_000)
            {
                return (bytesPerSec / 1_000.0).ToString("0.#") + " KB/s";
            }
            return ((long)bytesPerSec) + " B/s";
        }

        public static string InferDeviceType(DeviceRecord d, string gatewayIp)
        {
            if (d == null) return "unknown";

            string ip = d.LastIp ?? "";
            string name = (d.ComputerName ?? "").ToLowerInvariant();
            string desc = (d.Description ?? "").ToLowerInvariant();
            string company = (d.Company ?? "").ToLowerInvariant();

            if (!string.IsNullOrEmpty(gatewayIp) && ip == gatewayIp) return "router";
            if (name.Contains("router") || name.Contains("gateway") || desc.Contains("router") ||
                company.Contains("cisco") || company.Contains("netgear") || company.Contains("tp-link") ||
                company.Contains("asus") || company.Contains("ubiquiti") || company.Contains("mikrotik") ||
                company.Contains("d-link") || company.Contains("zyxel") || company.Contains("arris"))
            {
                return "router";
            }

            if (name.Contains("printer") || desc.Contains("printer") || name.Contains("laserjet") ||
                name.Contains("deskjet") || company.Contains("brother") || company.Contains("epson") ||
                company.Contains("canon") || company.Contains("lexmark") || company.Contains("xerox"))
            {
                return "printer";
            }

            if (name.Contains("tv") || desc.Contains("tv") || name.Contains("roku") || name.Contains("bravia") ||
                name.Contains("tizen") || name.Contains("chromecast") || name.Contains("firetv") ||
                company.Contains("roku") || company.Contains("lg electronics") || (company.Contains("samsung") && name.Contains("tv")))
            {
                return "tv";
            }

            if (name.Contains("phone") || name.Contains("iphone") || name.Contains("galaxy") || name.Contains("pixel") ||
                name.Contains("android") || name.Contains("handset") || company.Contains("xiaomi") || company.Contains("huawei") ||
                company.Contains("oneplus") || company.Contains("oppo") || company.Contains("vivo"))
            {
                return "phone";
            }

            if (company.Contains("espressif") || company.Contains("raspberry") || company.Contains("tuya") ||
                company.Contains("sonoff") || name.Contains("esp32") || name.Contains("esp8266") ||
                name.Contains("sensor") || name.Contains("plug") || name.Contains("smart") || desc.Contains("iot"))
            {
                return "iot";
            }

            if (name.Contains("macbook") || name.Contains("laptop") || name.Contains("desktop") ||
                name.Contains("pc") || name.Contains("surface") || company.Contains("dell") ||
                company.Contains("lenovo") || company.Contains("hp inc") || company.Contains("intel") ||
                company.Contains("apple") || company.Contains("microsoft"))
            {
                return "laptop";
            }

            return "unknown";
        }
    }
}
