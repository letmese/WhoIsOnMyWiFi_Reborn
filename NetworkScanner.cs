using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WhoIsOnMyWiFi_Reborn
{
    /// <summary>
    /// Network discovery engine (complete rewrite).
    ///
    /// Why the old version "found nothing" / "did not work":
    ///  1. It relied on the TEXT output of "arp -a", whose headers/keywords are localized
    ///     (French/German/Arabic Windows prints completely different headers), so on any
    ///     non-English Windows the parser returned zero rows => empty device list.
    ///     Now the ARP table is read with the GetIpNetTable Windows API - locale independent.
    ///  2. It never ping-swept first, so the ARP cache was usually empty (Windows only adds
    ///     entries after traffic). Now a fast parallel ping sweep wakes devices up, THEN the
    ///     ARP table is read.
    ///  3. Adapter detection used Dns.GetHostEntry(hostName), which throws SocketException
    ///     on many machines (VPNs, no DNS, unusual host names) and crashed the caller.
    ///     Now adapters are enumerated safely via NetworkInterface.GetAllNetworkInterfaces().
    ///  4. Everything runs fully async with a SemaphoreSlim throttle - the UI never freezes.
    /// </summary>
    public class ScanProgressReport
    {
        public string CurrentIp { get; set; } = "";
        public int Probed { get; set; }
        public int Total { get; set; }
        public int Found { get; set; }
        public int Percent { get; set; }
        public string Message { get; set; } = "";
    }

    public static class NetworkScanner
    {
        public class AdapterInfo
        {
            public string Ip = "";
            public string Mask = "255.255.255.0";
            public string Gateway = "";
            public string Name = "";
            public string Description = "";
            public string MacAddress = "";
            public NetworkInterfaceType InterfaceType = NetworkInterfaceType.Unknown;
        }

        public class ScanOptions
        {
            public bool Ping = true;
            public bool NetBios = true;
            public bool Randomize = false;
            public int DelayMs = 0;          // stealth delay between pings
            public bool IgnoreGateway = false;
            public string GatewayIp = "";
            public int PingTimeoutMs = 1000;
        }

        // ------------------------------------------------------------------ adapters

        /// <summary>Safe enumeration of up IPv4 adapters (never throws).</summary>
        public static List<AdapterInfo> GetAdapters()
        {
            var list = new List<AdapterInfo>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    try
                    {
                        if (nic.OperationalStatus != OperationalStatus.Up) continue;
                        if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                        if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                        var props = nic.GetIPProperties();
                        if (props.GetIPv4Properties() == null) continue;

                        string mac = "";
                        try
                        {
                            var phys = nic.GetPhysicalAddress();
                            if (phys != null)
                            {
                                byte[] b = phys.GetAddressBytes();
                                if (b != null && b.Length == 6)
                                {
                                    var sb = new StringBuilder(17);
                                    for (int i = 0; i < 6; i++)
                                    {
                                        if (i > 0) sb.Append(':');
                                        sb.Append(b[i].ToString("X2"));
                                    }
                                    mac = sb.ToString();
                                }
                            }
                        }
                        catch { }

                        foreach (var ua in props.UnicastAddresses)
                        {
                            if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                            string gw = "";
                            try
                            {
                                foreach (var g in props.GatewayAddresses)
                                {
                                    if (g.Address.AddressFamily == AddressFamily.InterNetwork &&
                                        !string.IsNullOrEmpty(g.Address.ToString()) &&
                                        g.Address.ToString() != "0.0.0.0")
                                    {
                                        gw = g.Address.ToString();
                                        break;
                                    }
                                }
                            }
                            catch { }
                            list.Add(new AdapterInfo
                            {
                                Ip = ua.Address.ToString(),
                                Mask = ua.IPv4Mask == null ? "255.255.255.0" : ua.IPv4Mask.ToString(),
                                Gateway = gw,
                                Name = nic.Name,
                                Description = nic.Description,
                                MacAddress = mac,
                                InterfaceType = nic.NetworkInterfaceType
                            });
                            break; // one IPv4 address per adapter is enough
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }

        /// <summary>The adapter most likely to be the real network (physical Wi-Fi/Ethernet with gateway).</summary>
        public static AdapterInfo GetBestAdapter()
        {
            var all = GetAdapters();
            // 1. Wi-Fi or Ethernet adapter with an active gateway
            foreach (var a in all)
            {
                if (!string.IsNullOrEmpty(a.Gateway) &&
                   (a.InterfaceType == NetworkInterfaceType.Wireless80211 || a.InterfaceType == NetworkInterfaceType.Ethernet))
                {
                    return a;
                }
            }
            // 2. Any adapter with an active gateway
            foreach (var a in all)
            {
                if (!string.IsNullOrEmpty(a.Gateway)) return a;
            }
            // 3. Fallback to first available
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>
        /// Detects the first usable subnet and returns it in the ORIGINAL app's
        /// "start-end" format (e.g. "192.168.1.1-192.168.1.254"). Never throws.
        /// </summary>
        public static string DetectFirstSubnet()
        {
            try
            {
                var a = GetBestAdapter();
                if (a == null || string.IsNullOrEmpty(a.Ip)) return "";
                if (!ParseIp(a.Ip, out uint ip)) return "";
                if (!ParseIp(a.Mask, out uint mask)) mask = 0xFFFFFF00u;
                uint net = ip & mask;
                uint start = net + 1;
                uint end = (net | (~mask)) - 1;
                if (end < start) end = start;
                return IpToString(start) + "-" + IpToString(end);
            }
            catch { }
            return "";
        }

        // ------------------------------------------------------------------ range parsing

        /// <summary>
        /// Accepts every format the original app (and humans) use:
        /// "192.168.1.1-192.168.1.254", "192.168.1.1-254", "192.168.1.0/24",
        /// "192.168.1" / "192.168.1.", "192.168.1.1".
        /// </summary>
        public static bool ParseRange(string text, out uint start, out uint end)
        {
            start = 0; end = 0;
            text = (text ?? "").Trim();
            if (text.Length == 0) return false;
            try
            {
                int slash = text.IndexOf('/');
                if (slash > 0)
                {
                    string ipPart = text.Substring(0, slash).Trim();
                    if (!ParseIp(ipPart, out uint baseIp)) return false;
                    if (!int.TryParse(text.Substring(slash + 1).Trim(), out int prefix)) return false;
                    if (prefix < 0 || prefix > 32) return false;
                    uint mask = prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
                    uint net = baseIp & mask;
                    if (prefix >= 31)
                    {
                        start = net; end = net;                    // no usable host range
                        return true;
                    }
                    start = net + 1;
                    end = (net | ~mask) - 1;                      // skip the broadcast address
                    if (end < start) end = start;
                    return end >= start;
                }

                int dash = text.IndexOf('-');
                if (dash > 0)
                {
                    string left = text.Substring(0, dash).Trim();
                    string right = text.Substring(dash + 1).Trim();
                    if (!ParseIp(left, out uint l)) return false;
                    uint r;
                    if (right.IndexOf('.') >= 0)
                    {
                        if (!ParseIp(right, out r)) return false;
                    }
                    else
                    {
                        // "192.168.1.1-254" style: replace the last octet
                        if (!int.TryParse(right, out int last) || last < 0 || last > 255) return false;
                        r = (l & 0xFFFFFF00u) | (uint)last;
                    }
                    start = l; end = r;
                    return end >= start;
                }

                string[] parts = text.Split('.');
                if (parts.Length == 4)
                {
                    if (!ParseIp(text, out uint single)) return false;
                    start = single; end = single;
                    return true;
                }
                if (parts.Length == 2 || parts.Length == 3)
                {
                    string prefix = string.Join(".", parts);
                    if (!ParseIp(prefix + ".0", out uint net)) return false;
                    start = net + 1;
                    end = net + 254;
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static bool ParseIp(string text, out uint ip)
        {
            ip = 0;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Trim().Split('.');
            if (parts.Length != 4) return false;
            uint v = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i].Trim(), out int b) || b < 0 || b > 255) return false;
                v |= (uint)b << (24 - i * 8);
            }
            ip = v;
            return true;
        }

        public static string IpToString(uint ip)
        {
            return ((ip >> 24) & 255) + "." + ((ip >> 16) & 255) + "." + ((ip >> 8) & 255) + "." + (ip & 255);
        }

        public static uint IpToUint(string ip)
        {
            return ParseIp(ip, out uint v) ? v : 0;
        }

        // ------------------------------------------------------------------ scanning

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(uint destIp, uint srcIp, byte[] pMacAddr, ref int phyAddrLen);

        /// <summary>
        /// Validates that a MAC address is a genuine physical 48-bit hardware address
        /// (not empty, not all zeros, not broadcast, not multicast).
        /// </summary>
        public static bool IsValidHardwareMac(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac) || mac.Length < 17) return false;
            mac = mac.ToUpper();
            if (mac == "00:00:00:00:00:00" || mac == "FF:FF:FF:FF:FF:FF") return false;
            if (mac.StartsWith("01:00:5E") || mac.StartsWith("33:33")) return false; // multicast
            return true;
        }

        /// <summary>
        /// Full scan pipeline:
        ///  1. Active parallel SendARP sweep bound directly to physical adapter IPv4.
        ///     - Bypasses ICMP/ping firewall drops.
        ///     - Immune to VPN/tun interface leakage.
        ///     - Instant 48-bit hardware MAC resolution.
        ///  2. Host PC detection (records physical adapter MAC and machine hostname).
        ///  3. OS ARP cache fallback for verified entries.
        ///  4. Name resolution (NetBIOS UDP 137, reverse DNS) for live hosts.
        ///  5. Strict MAC guarantee (no phantom/no-MAC records allowed).
        /// </summary>
        public static Task<List<DeviceRecord>> ScanAsync(string iprange, ScanOptions opt, IProgress<string> progress, CancellationToken ct)
        {
            var rep = new Progress<ScanProgressReport>(r => progress?.Report(r.Message));
            return ScanAsync(iprange, opt, rep, ct);
        }

        /// <summary>
        /// Full scan pipeline with high-fidelity real-time progress reporting:
        ///  1. Active parallel SendARP sweep bound directly to physical adapter IPv4.
        ///  2. Host PC detection (records physical adapter MAC and machine hostname).
        ///  3. OS ARP cache fallback for verified entries.
        ///  4. Name resolution (NetBIOS UDP 137, reverse DNS) for live hosts.
        ///  5. Strict MAC guarantee (no phantom/no-MAC records allowed).
        /// </summary>
        public static async Task<List<DeviceRecord>> ScanAsync(string iprange, ScanOptions opt, IProgress<ScanProgressReport> progress, CancellationToken ct)
        {
            var result = new List<DeviceRecord>();
            if (opt == null) opt = new ScanOptions();

            string range = iprange;
            if (!ParseRange(range, out uint start, out uint end))
            {
                range = DetectFirstSubnet();
                if (!ParseRange(range, out start, out end))
                {
                    progress?.Report(new ScanProgressReport { Message = "No valid IP range - check Settings > IP Ranges." });
                    return result;
                }
            }

            List<uint> ips = EnumerateRange(start, end, 2048);
            if (opt.Randomize) Shuffle(ips);

            var bestAdapter = GetBestAdapter();
            string localIpStr = bestAdapter?.Ip ?? "";
            string localMacStr = bestAdapter?.MacAddress ?? "";
            uint win32Src = 0;
            if (!string.IsNullOrEmpty(localIpStr) && ParseIp(localIpStr, out uint localU))
            {
                byte[] srcBytes = new byte[] { (byte)((localU >> 24) & 255), (byte)((localU >> 16) & 255), (byte)((localU >> 8) & 255), (byte)(localU & 255) };
                win32Src = BitConverter.ToUInt32(srcBytes, 0);
            }

            // uint IP -> verified hardware MAC string
            var found = new ConcurrentDictionary<uint, string>();

            int total = ips.Count;
            int probed = 0;

            progress?.Report(new ScanProgressReport
            {
                CurrentIp = ips.Count > 0 ? IpToString(ips[0]) : "",
                Probed = 0,
                Total = total,
                Found = 0,
                Percent = 0,
                Message = $"Scanning {total} hosts on subnet..."
            });

            // ---- 1. Active parallel SendARP sweep ----
            using (var gate = new SemaphoreSlim(64))
            {
                var tasks = new List<Task>();
                foreach (uint u in ips)
                {
                    ct.ThrowIfCancellationRequested();
                    uint target = u;
                    string targetIpStr = IpToString(target);

                    tasks.Add(Task.Run(async () =>
                    {
                        await gate.WaitAsync(ct);
                        try
                        {
                            if (opt.DelayMs > 0) await Task.Delay(opt.DelayMs, ct);

                            // Check if this IP is the local host PC
                            if (!string.IsNullOrEmpty(localIpStr) && targetIpStr.Equals(localIpStr, StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrEmpty(localMacStr) && IsValidHardwareMac(localMacStr))
                                {
                                    found[target] = localMacStr;
                                }
                                return;
                            }

                            byte[] destBytes = new byte[] { (byte)((target >> 24) & 255), (byte)((target >> 16) & 255), (byte)((target >> 8) & 255), (byte)(target & 255) };
                            uint win32Dest = BitConverter.ToUInt32(destBytes, 0);
                            byte[] macBuf = new byte[6];
                            int macLen = 6;

                            int rc = SendARP(win32Dest, win32Src, macBuf, ref macLen);
                            if (rc == 0 && macLen == 6)
                            {
                                string mac = MacBytesToString(macBuf, macLen);
                                if (IsValidHardwareMac(mac))
                                {
                                    found[target] = mac;
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            gate.Release();
                            int n = Interlocked.Increment(ref probed);
                            if (n % 2 == 0 || n == total)
                            {
                                int pct = total > 0 ? (n * 85) / total : 0;
                                progress?.Report(new ScanProgressReport
                                {
                                    CurrentIp = targetIpStr,
                                    Probed = n,
                                    Total = total,
                                    Found = found.Count,
                                    Percent = pct,
                                    Message = $"Probing {targetIpStr} ({n}/{total}) — {found.Count} device(s) found"
                                });
                            }
                        }
                    }, ct));
                }

                try { await Task.WhenAll(tasks); }
                catch (OperationCanceledException) { }
            }

            // ---- 2. Supplement with active OS ARP cache entries ----
            try
            {
                List<ArpRow> arp = ReadArpTable();
                foreach (ArpRow row in arp)
                {
                    if (row.Ip < start || row.Ip > end) continue;
                    if (row.Type == 1 || row.Type == 2) continue; // invalid
                    if (IsValidHardwareMac(row.Mac))
                    {
                        found.TryAdd(row.Ip, row.Mac);
                    }
                }
            }
            catch { }

            // Ensure host PC is present if on this subnet
            if (!string.IsNullOrEmpty(localIpStr) && ParseIp(localIpStr, out uint hostU))
            {
                if (hostU >= start && hostU <= end && !string.IsNullOrEmpty(localMacStr) && IsValidHardwareMac(localMacStr))
                {
                    found[hostU] = localMacStr;
                }
            }

            // Optionally hide the gateway if user requested
            if (opt.IgnoreGateway && !string.IsNullOrEmpty(opt.GatewayIp) && ParseIp(opt.GatewayIp, out uint gw))
            {
                found.TryRemove(gw, out _);
            }

            // ---- 3. Name resolution for verified live devices ONLY ----
            List<uint> ordered = new List<uint>(found.Keys);
            ordered.Sort();
            if (ordered.Count > 0)
            {
                progress?.Report(new ScanProgressReport
                {
                    CurrentIp = "Hostnames",
                    Probed = total,
                    Total = total,
                    Found = ordered.Count,
                    Percent = 90,
                    Message = $"Resolving names for {ordered.Count} live device(s)..."
                });
            }

            var names = new ConcurrentDictionary<uint, string>();
            using (var nameGate = new SemaphoreSlim(16))
            {
                var nameTasks = new List<Task>();
                foreach (uint u in ordered)
                {
                    uint target = u;
                    string ipStr = IpToString(target);

                    nameTasks.Add(Task.Run(async () =>
                    {
                        await nameGate.WaitAsync(ct);
                        try
                        {
                            // Host PC name
                            if (!string.IsNullOrEmpty(localIpStr) && ipStr.Equals(localIpStr, StringComparison.OrdinalIgnoreCase))
                            {
                                names[target] = Environment.MachineName;
                                return;
                            }

                            string nm = "";
                            byte[] targetBytes = new byte[] { (byte)((target >> 24) & 255), (byte)((target >> 16) & 255), (byte)((target >> 8) & 255), (byte)(target & 255) };

                            if (opt.NetBios)
                            {
                                try
                                {
                                    nm = await Task.Run(() => NetBiosName(new IPAddress(targetBytes), 450), ct);
                                }
                                catch { }
                            }
                            if (string.IsNullOrEmpty(nm))
                            {
                                try
                                {
                                    nm = await ResolveDnsAsync(new IPAddress(targetBytes));
                                }
                                catch { }
                            }
                            names[target] = nm ?? "";
                        }
                        catch { }
                        finally { nameGate.Release(); }
                    }, ct));
                }

                try { await Task.WhenAll(nameTasks); }
                catch (OperationCanceledException) { }
            }

            // ---- 4. Build records (STRICT: valid MAC address required) ----
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            foreach (uint u in ordered)
            {
                ct.ThrowIfCancellationRequested();
                string mac = found[u];
                if (!IsValidHardwareMac(mac)) continue;

                string ipStr = IpToString(u);
                string name = names.TryGetValue(u, out string nm) ? (nm ?? "") : "";
                bool isHost = !string.IsNullOrEmpty(localIpStr) && ipStr.Equals(localIpStr, StringComparison.OrdinalIgnoreCase);
                bool isGateway = !string.IsNullOrEmpty(opt.GatewayIp) && opt.GatewayIp == ipStr;

                string desc = "";
                if (isHost) desc = "This PC (Host)";
                else if (isGateway) desc = "Gateway/Router";

                result.Add(new DeviceRecord
                {
                    DigiSig = Digisig(mac),
                    MacAddress = mac,
                    ComputerName = name,
                    Description = desc,
                    Company = "",
                    LastIp = ipStr,
                    Connected = true,
                    Known = isHost,
                    LastFound = now,
                    FirstFound = now,
                    SubnetId = 0
                });
            }

            progress?.Report(new ScanProgressReport
            {
                CurrentIp = "Complete",
                Probed = total,
                Total = total,
                Found = result.Count,
                Percent = 100,
                Message = $"Scan complete: {result.Count} verified device(s) found."
            });
            return result;
        }

        // ------------------------------------------------------------------ NetBIOS

        /// <summary>NetBIOS Node Status query (UDP 137). Returns "" when the device does not answer.</summary>
        public static string NetBiosName(IPAddress ip, int timeoutMs)
        {
            try
            {
                using var udp = new UdpClient();
                var ep = new IPEndPoint(ip, 137);
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, timeoutMs);
                byte[] req = BuildNbstatRequest();
                udp.Send(req, req.Length, ep);
                IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
                byte[] resp = udp.Receive(ref any);   // throws on timeout
                return ParseNbnsResponse(resp);
            }
            catch { }
            return "";
        }

        /// <summary>NBNS Node Status Request: header(12) + question "*\0...\0\0" in the additional section, QTYPE=NBSTAT.</summary>
        private static byte[] BuildNbstatRequest()
        {
            byte[] pkt = new byte[50];
            pkt[0] = 0x41; pkt[1] = 0x41;              // transaction id (arbitrary)
            pkt[2] = 0x00; pkt[3] = 0x10;              // flags
            pkt[4] = 0x00; pkt[5] = 0x00;              // QDCOUNT = 0
            pkt[6] = 0x00; pkt[7] = 0x00;              // ANCOUNT = 0
            pkt[8] = 0x00; pkt[9] = 0x00;              // NSCOUNT = 0
            pkt[10] = 0x00; pkt[11] = 0x01;            // ARCOUNT = 1
            pkt[12] = 0x20;                            // label length 32
            pkt[13] = (byte)'C'; pkt[14] = (byte)'K';  // '*' = 0x2A -> 'C''K'
            for (int i = 15; i <= 44; i++) pkt[i] = (byte)'A';  // 15 zero bytes + suffix 0x00 -> "AA" each
            pkt[45] = 0x00;                            // root label
            pkt[46] = 0x00; pkt[47] = 0x21;            // QTYPE  = NBSTAT (0x21)
            pkt[48] = 0x00; pkt[49] = 0x01;            // QCLASS = IN (1)
            return pkt;
        }

        private static string ParseNbnsResponse(byte[] resp)
        {
            if (resp == null || resp.Length < 64) return "";
            try
            {
                // header(12) + echoed question(38) + RR header(12) -> NUMNAMES at offset 62
                if (!(resp[46] == 0x00 && resp[47] == 0x21)) return "";
                int num = resp[62];
                if (num <= 0 || num > 64) return "";
                string best = "";
                for (int i = 0; i < num; i++)
                {
                    int off = 63 + i * 18;
                    if (off + 18 > resp.Length) break;
                    byte suffix = resp[off + 15];
                    string name = Encoding.ASCII.GetString(resp, off, 15).TrimEnd(' ', '\0');
                    bool printable = name.Length > 0;
                    foreach (char ch in name) { if (ch < 32 || ch > 126) { printable = false; break; } }
                    if (!printable) continue;
                    if (suffix == 0x00 && !name.StartsWith("__") && !name.Equals("WORKGROUP", StringComparison.OrdinalIgnoreCase))
                        return name;                                    // the computer name
                    if (best.Length == 0 && suffix == 0x20) best = name;
                }
                return best;
            }
            catch { }
            return "";
        }

        // ------------------------------------------------------------------ reverse DNS

        private static async Task<string> ResolveDnsAsync(IPAddress ip)
        {
            try
            {
                Task<IPHostEntry> t = Dns.GetHostEntryAsync(ip);
                Task done = await Task.WhenAny(t, Task.Delay(450));
                if (done == t) return t.Result.HostName ?? "";
            }
            catch { }
            return "";
        }

        // ------------------------------------------------------------------ ARP table (Windows API + fallback)

        private struct ArpRow
        {
            public uint Ip;
            public string Mac;
            public uint Type;
        }

        [DllImport("iphlpapi.dll")]
        private static extern int GetIpNetTable(IntPtr pIpNetTable, ref int pdwSize, bool bOrder);

        private static List<ArpRow> ReadArpTable()
        {
            var list = new List<ArpRow>();
            try
            {
                int size = 0;
                int rc = GetIpNetTable(IntPtr.Zero, ref size, false); // returns 122 (insufficient buffer)
                if (rc == 122 && size >= 4)
                {
                    IntPtr buf = Marshal.AllocHGlobal(size);
                    try
                    {
                        rc = GetIpNetTable(buf, ref size, false);
                        if (rc == 0)
                        {
                            int count = Marshal.ReadInt32(buf, 0);
                            for (int i = 0; i < count; i++)
                            {
                                IntPtr row = IntPtr.Add(buf, 4 + i * 24);   // MIB_IPNETROW = 24 bytes
                                int physLen = Marshal.ReadInt32(row, 4);
                                if (physLen < 1) physLen = 0;
                                if (physLen > 8) physLen = 8;
                                var macBytes = new byte[6];
                                for (int b = 0; b < 6; b++) macBytes[b] = Marshal.ReadByte(row, 8 + b);
                                uint addr = (uint)Marshal.ReadInt32(row, 16);
                                uint type = (uint)Marshal.ReadInt32(row, 20);
                                string mac = MacBytesToString(macBytes, physLen);
                                if (string.IsNullOrEmpty(mac)) continue;
                                list.Add(new ArpRow { Ip = addr, Mac = mac, Type = type });
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                    if (list.Count > 0) return list;
                }
            }
            catch { }
            return ReadArpTableViaCommand();
        }

        /// <summary>Locale-independent fallback: match IP + MAC shapes instead of localized headers.</summary>
        private static List<ArpRow> ReadArpTableViaCommand()
        {
            var list = new List<ArpRow>();
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("arp", "-a")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);

                var re = new System.Text.RegularExpressions.Regex(
                    @"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s+((?:[0-9A-Fa-f]{2}[-:]){5}[0-9A-Fa-f]{2})");
                foreach (System.Text.RegularExpressions.Match m in re.Matches(output))
                {
                    if (!ParseIp(m.Groups[1].Value, out uint ip)) continue;
                    string mac = m.Groups[2].Value.ToUpper().Replace('-', ':');
                    list.Add(new ArpRow { Ip = ip, Mac = mac, Type = 3 });
                }
            }
            catch { }
            return list;
        }

        private static string MacBytesToString(byte[] b, int len)
        {
            if (len < 6) return "";
            var sb = new StringBuilder(17);
            for (int i = 0; i < 6; i++)
            {
                if (i > 0) sb.Append(':');
                sb.Append(b[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private static uint LocalIpAsUint()
        {
            try
            {
                var a = GetBestAdapter();
                if (a != null && ParseIp(a.Ip, out uint v)) return v;
            }
            catch { }
            return 0;
        }

        // ------------------------------------------------------------------ helpers

        public static string Digisig(string key)
        {
            try
            {
                byte[] hash = MD5.HashData(Encoding.ASCII.GetBytes((key ?? "").ToUpper().Replace('-', ':')));
                return Convert.ToHexString(hash);       // 32 uppercase hex chars, like the original app
            }
            catch { return key ?? ""; }
        }

        private static List<uint> EnumerateRange(uint start, uint end, int cap)
        {
            var list = new List<uint>();
            for (uint u = start; u <= end && list.Count < cap; u++)
            {
                list.Add(u);
                if (u == 0xFFFFFFFFu) break;
            }
            return list;
        }

        private static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}