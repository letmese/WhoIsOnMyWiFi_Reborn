using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace WhoIsOnMyWiFi_Reborn
{
    /// <summary>
    /// Reads data from the ORIGINAL "Who Is On My WiFi" app (its _netlib2.dl2 / manu.db /
    /// exported .wif files, which are SQLite databases), from Reborn .wif backups
    /// (VACUUM INTO copies) and from Reborn JSON exports.
    /// </summary>
    public static class LegacyImporter
    {
        public class ImportResult
        {
            public int Devices;
            public int Subnets;
            public int Manufacturers;
            public bool SettingsImported;
            public List<string> Warnings = new List<string>();
        }

        /// <summary>Auto-detects the file type and imports everything. Throws with a readable message on failure.</summary>
        public static ImportResult ImportFile(string path, Database db, bool importSettings)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new FileNotFoundException("File not found: " + path);

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".json" || (ext != ".dl2" && ext != ".wif" && ext != ".db" && ext != ".dat" && LooksLikeJson(path)))
                return ImportJson(path, db, importSettings);

            // SQLite of any flavour: work on a COPY so locked / WAL originals are fine.
            string temp = Path.Combine(Path.GetTempPath(), "reborn_import_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                File.Copy(path, temp, true);
                return ImportSqlite(temp, db, importSettings);
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }

        private static bool LooksLikeJson(string path)
        {
            try
            {
                using var sr = new StreamReader(path);
                int first = sr.Read();
                while (first == ' ' || first == '\t' || first == '\r' || first == '\n') first = sr.Read();
                return first == '{';
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ SQLite (original app / .wif)

        private static ImportResult ImportSqlite(string path, Database db, bool importSettings)
        {
            var result = new ImportResult();
            var conn = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly");
            conn.Open();
            try
            {
                var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) tables.Add(Convert.ToString(r[0]));
                }

                if (!tables.Contains("computers") && !tables.Contains("manu"))
                    throw new Exception("This file is not a Who Is On My WiFi data file (no computers/manu table found).");

                // ---- devices ----
                if (tables.Contains("computers"))
                {
                    var cols = GetColumns(conn, "computers");
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT * FROM computers";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        string rawMac = Get(r, cols, "macaddress");
                        string rawIp = Get(r, cols, "lastip");
                        string normMac = NormalizeMac(rawMac);

                        // Ignore multicast, broadcast, and invalid/empty hardware MACs
                        if (!NetworkScanner.IsValidHardwareMac(normMac)) continue;

                        // Normalize IP address (e.g. 192.168.010.003 -> 192.168.10.3)
                        string normIp = rawIp;
                        if (NetworkScanner.ParseIp(rawIp, out uint ipU))
                        {
                            normIp = NetworkScanner.IpToString(ipU);
                        }

                        string digisig = NetworkScanner.Digisig(normMac);
                        string desc = Get(r, cols, "compdesc");
                        if (desc != null && desc.Equals("TYPE IN NAME", StringComparison.OrdinalIgnoreCase)) desc = "";

                        string compName = Get(r, cols, "computername");
                        string company = Get(r, cols, "company");
                        if (string.IsNullOrEmpty(company)) company = db.GetCompanyByMac(normMac);

                        bool isKnown = Database.ParseBool(GetValue(r, cols, "known"));
                        if (!string.IsNullOrEmpty(desc)) isKnown = true;

                        // Who Is On My WiFi backup stores user-given friendly names in `compdesc`.
                        // Assign it as ComputerName so it displays as the primary device name in the app!
                        if (!string.IsNullOrWhiteSpace(desc) && (string.IsNullOrWhiteSpace(compName) || compName.StartsWith("UNKNOWN", StringComparison.OrdinalIgnoreCase)))
                        {
                            compName = desc.Trim();
                        }
                        else if (string.IsNullOrWhiteSpace(compName) && !string.IsNullOrWhiteSpace(desc))
                        {
                            compName = desc.Trim();
                        }

                        var d = new DeviceRecord
                        {
                            DigiSig = digisig,
                            MacAddress = normMac,
                            ComputerName = compName,
                            Description = desc,
                            Company = company,
                            LastIp = normIp,
                            Connected = false,                       // start fresh - a scan decides
                            Known = isKnown,
                            LastFound = Get(r, cols, "lastfound"),
                            FirstFound = Get(r, cols, "firstfound"),
                            SubnetId = GetLong(r, cols, "subnetid")
                        };
                        db.UpsertDevice(d);
                        result.Devices++;
                    }
                }

                // ---- subnets ----
                if (tables.Contains("subnet"))
                {
                    var cols = GetColumns(conn, "subnet");
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT * FROM subnet";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        string snRange = Get(r, cols, "iprange");
                        if (string.IsNullOrEmpty(snRange) || !NetworkScanner.ParseRange(snRange, out _, out _)) continue;

                        var sn = new SubnetRecord
                        {
                            IpRange = snRange,
                            CheckIp = Database.ParseBool(GetValue(r, cols, "check_ip")),
                            CheckNetbios = Database.ParseBool(GetValue(r, cols, "check_netbios")),
                            CheckPort80 = Database.ParseBool(GetValue(r, cols, "check_port80")),
                            CheckPort137 = Database.ParseBool(GetValue(r, cols, "check_port137")),
                            GatewayIp = Get(r, cols, "gatewayip"),
                            GatewayMac = NormalizeMac(Get(r, cols, "gatewaymac")),
                            Scan = false                                   // never auto-scan imported ranges
                        };
                        db.UpsertSubnet(sn);
                        result.Subnets++;
                    }
                }

                // ---- MAC manufacturers (the original app ships the full IEEE OUI list) ----
                if (tables.Contains("manu"))
                {
                    var cols = GetColumns(conn, "manu");
                    string macCol = cols.FirstOrDefault(c => c.ToLowerInvariant().Contains("mac") || c.ToLowerInvariant().Contains("prefix") || c.ToLowerInvariant().Contains("oui"));
                    string compCol = cols.FirstOrDefault(c => c.ToLowerInvariant().Contains("comp") || c.ToLowerInvariant().Contains("vendor") || c.ToLowerInvariant().Contains("name"));
                    if (macCol != null && compCol != null)
                    {
                        var rows = new List<KeyValuePair<string, string>>();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT [" + macCol + "], [" + compCol + "] FROM manu";
                        using var r = cmd.ExecuteReader();
                        while (r.Read())
                        {
                            string key = Convert.ToString(r[0]);
                            string val = r[1] == DBNull.Value ? "" : Convert.ToString(r[1]);
                            if (!string.IsNullOrEmpty(key)) rows.Add(new KeyValuePair<string, string>(key, val));
                            if (rows.Count >= 100000) break;                 // safety cap
                        }
                        result.Manufacturers = db.ImportManuRows(rows);
                    }
                }

                // ---- settings ----
                if (importSettings && tables.Contains("settings"))
                {
                    var s = db.LoadSettings();
                    var cols = GetColumns(conn, "settings");
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT * FROM settings LIMIT 1";
                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                    {
                        string iprange = Get(r, cols, "iprange");
                        if (!string.IsNullOrEmpty(iprange) && NetworkScanner.ParseRange(iprange, out _, out _))
                        {
                            s.iprange = iprange;
                        }
                        if (cols.Contains("AUTOSCAN")) s.autoscan = Database.ParseBool(GetValue(r, cols, "autoscan"));
                        s.not_message = Database.ParseBool(GetValue(r, cols, "not_message"));
                        s.not_sound = Database.ParseBool(GetValue(r, cols, "not_sound"));
                        s.not_shutdown = Database.ParseBool(GetValue(r, cols, "not_shutdown"));
                        s.not_email = Database.ParseBool(GetValue(r, cols, "not_email"));
                        string email = Get(r, cols, "email"); if (!string.IsNullOrEmpty(email)) s.email = email;
                        string mailfrom = Get(r, cols, "mailfrom"); if (!string.IsNullOrEmpty(mailfrom)) s.mailfrom = mailfrom;
                        string server = Get(r, cols, "mail_server"); if (!string.IsNullOrEmpty(server)) s.mail_server = server;
                        if (cols.Contains("MAIL_PORT")) { int port = GetLong(r, cols, "mail_port"); if (port > 0) s.mail_port = port; }
                        s.enablessl = Database.ParseBool(GetValue(r, cols, "enablessl"));
                        string user = Get(r, cols, "mail_username"); if (!string.IsNullOrEmpty(user)) s.mail_username = user;
                        string pass = Get(r, cols, "mail_password"); if (!string.IsNullOrEmpty(pass)) s.mail_password = pass;
                        s.log_blocking = Database.ParseBool(GetValue(r, cols, "log_blocking"));
                        if (cols.Contains("FREQUENCY")) { int freq = GetLong(r, cols, "frequency"); if (freq > 0) s.frequency = freq; }
                        db.SaveSettings(s);
                        result.SettingsImported = true;
                    }
                }
            }
            finally
            {
                try { conn.Dispose(); } catch { }
            }
            return result;
        }

        // ------------------------------------------------------------------ JSON (Reborn backups)

        private static ImportResult ImportJson(string path, Database db, bool importSettings)
        {
            var result = new ImportResult();
            string json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("devices", out var devices) && devices.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in devices.EnumerateArray())
                {
                    var d = new DeviceRecord
                    {
                        DigiSig = Str(el, "DigiSig"),
                        MacAddress = NormalizeMac(Str(el, "MacAddress")),
                        ComputerName = Str(el, "ComputerName"),
                        Description = Str(el, "Description"),
                        Company = Str(el, "Company"),
                        LastIp = Str(el, "LastIp"),
                        Connected = false,
                        Known = Bool(el, "Known"),
                        LastFound = Str(el, "LastFound"),
                        FirstFound = Str(el, "FirstFound"),
                        SubnetId = L(el, "SubnetId")
                    };
                    if (!string.IsNullOrWhiteSpace(d.Description) && (string.IsNullOrWhiteSpace(d.ComputerName) || d.ComputerName.StartsWith("UNKNOWN", StringComparison.OrdinalIgnoreCase)))
                    {
                        d.ComputerName = d.Description.Trim();
                    }
                    if (string.IsNullOrEmpty(d.DigiSig) && !string.IsNullOrEmpty(d.MacAddress))
                        d.DigiSig = NetworkScanner.Digisig(d.MacAddress);
                    if (string.IsNullOrEmpty(d.DigiSig)) continue;
                    db.UpsertDevice(d);
                    result.Devices++;
                }
            }

            if (root.TryGetProperty("subnets", out var subnets) && subnets.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in subnets.EnumerateArray())
                {
                    var sn = new SubnetRecord
                    {
                        IpRange = Str(el, "IpRange"),
                        CheckIp = Bool(el, "CheckIp"),
                        CheckNetbios = Bool(el, "CheckNetbios"),
                        CheckPort80 = Bool(el, "CheckPort80"),
                        CheckPort137 = Bool(el, "CheckPort137"),
                        GatewayIp = Str(el, "GatewayIp"),
                        GatewayMac = NormalizeMac(Str(el, "GatewayMac")),
                        Scan = false
                    };
                    db.UpsertSubnet(sn);
                    result.Subnets++;
                }
            }

            if (importSettings && root.TryGetProperty("settings", out var settingsEl))
            {
                var s = db.LoadSettings();
                s.iprange = Str(settingsEl, "iprange");
                s.not_message = Bool(settingsEl, "not_message");
                s.not_sound = Bool(settingsEl, "not_sound");
                s.not_shutdown = Bool(settingsEl, "not_shutdown");
                s.not_email = Bool(settingsEl, "not_email");
                s.email = Str(settingsEl, "email");
                s.mailfrom = Str(settingsEl, "mailfrom");
                s.mail_server = Str(settingsEl, "mail_server");
                s.mail_port = (int)L(settingsEl, "mail_port");
                s.enablessl = Bool(settingsEl, "enablessl");
                s.mail_username = Str(settingsEl, "mail_username");
                s.mail_password = Str(settingsEl, "mail_password");
                s.frequency = (int)L(settingsEl, "frequency");
                s.filt_connected = Bool(settingsEl, "filt_connected");
                s.ignoregateway = Bool(settingsEl, "ignoregateway");
                db.SaveSettings(s);
                result.SettingsImported = true;
            }

            return result;
        }

        /// <summary>Serializes the whole Reborn database into a portable JSON backup.</summary>
        public static void ExportJson(Database db, string path)
        {
            var payload = new Dictionary<string, object>
            {
                ["format"] = "WhoIsOnMyWiFi_Reborn",
                ["version"] = 2,
                ["exported"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["settings"] = db.LoadSettings(),
                ["devices"] = db.LoadDevices(false),
                ["subnets"] = db.LoadSubnets()
            };
            var options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, options));
        }

        // ------------------------------------------------------------------ sqlite helpers

        private static HashSet<string> GetColumns(SqliteConnection conn, string table)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA table_info(" + table + ")";
            using var r = cmd.ExecuteReader();
            while (r.Read()) set.Add(Convert.ToString(r["name"]));
            return set;
        }

        private static object GetValue(SqliteDataReader r, HashSet<string> cols, string name)
        {
            if (!cols.Contains(name)) return null;
            try
            {
                int i = r.GetOrdinal(name);
                return r.IsDBNull(i) ? null : r[i];
            }
            catch { return null; }
        }

        private static string Get(SqliteDataReader r, HashSet<string> cols, string name)
        {
            object v = GetValue(r, cols, name);
            return v == null ? "" : Convert.ToString(v).Trim();
        }

        private static int GetLong(SqliteDataReader r, HashSet<string> cols, string name)
        {
            object v = GetValue(r, cols, name);
            if (v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        private static string NormalizeMac(string mac)
        {
            mac = (mac ?? "").Trim().ToUpper().Replace('-', ':');
            if (mac.Length != 17) return mac;
            return mac;
        }

        // json helpers
        private static string Str(JsonElement el, string name)
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? "";
            return "";
        }
        private static bool Bool(JsonElement el, string name)
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return false;
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            if (v.ValueKind == JsonValueKind.String) return Database.ParseBool(v.GetString());
            return false;
        }
        private static long L(JsonElement el, string name)
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt64(out long n) ? n : 0;
                if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out long p)) return p;
            }
            return 0;
        }
    }
}