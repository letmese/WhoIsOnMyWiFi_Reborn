using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WhoIsOnMyWiFi_Reborn
{
    public class PortResult
    {
        public int Port { get; set; }
        public string Name { get; set; }
        public string Service { get; set; }
        public bool IsOpen { get; set; }
        public bool IsCritical { get; set; }
    }

    public static class PortScanner
    {
        public static readonly (int Port, string Name, string Service, bool IsCritical)[] CommonPorts = new[]
        {
            (80, "HTTP", "Web Management Interface", false),
            (443, "HTTPS", "Secure Web Interface", false),
            (8080, "ALT-HTTP", "Secondary Telemetry Service", false),
            (23, "TELNET", "Telnet Service Exposed", true),
            (22, "SSH", "Secure Shell (Remote Terminal)", false),
            (445, "SMB", "Windows SMB File Sharing", false),
            (53, "DNS", "DNS Resolver Service", false),
            (554, "RTSP", "RTSP Video Stream / IP Camera", false),
            (3389, "RDP", "Remote Desktop Protocol", false),
            (1883, "MQTT", "IoT MQTT Broker Service", false),
            (21, "FTP", "FTP File Server", false),
            (8443, "ALT-HTTPS", "Alternative HTTPS Port", false)
        };

        public static async Task<List<PortResult>> ScanPortsAsync(string ip, int timeoutMs = 450, CancellationToken ct = default)
        {
            var results = new List<PortResult>();
            if (string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0") return results;

            var tasks = new List<Task<PortResult>>();
            foreach (var p in CommonPorts)
            {
                tasks.Add(CheckPortAsync(ip, p.Port, p.Name, p.Service, p.IsCritical, timeoutMs, ct));
            }

            var scanned = await Task.WhenAll(tasks);
            foreach (var r in scanned)
            {
                if (r != null && r.IsOpen)
                {
                    results.Add(r);
                }
            }
            return results;
        }

        private static async Task<PortResult> CheckPortAsync(string ip, int port, string name, string service, bool isCritical, int timeoutMs, CancellationToken ct)
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                using var timeoutCts = new CancellationTokenSource(timeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                var connectTask = socket.ConnectAsync(ip, port, linkedCts.Token).AsTask();
                await connectTask;

                if (socket.Connected)
                {
                    return new PortResult
                    {
                        Port = port,
                        Name = name,
                        Service = service,
                        IsOpen = true,
                        IsCritical = isCritical
                    };
                }
            }
            catch
            {
                // Port closed or timed out
            }

            return null;
        }
    }
}
