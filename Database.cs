using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace WhoIsOnMyWiFi_Reborn
{
    public class DeviceRecord
    {
        public long UniqInt;
        public string DigiSig = "";
        public string MacAddress = "";
        public string ComputerName = "";
        public string Description = "";
        public string Company = "";
        public string LastIp = "";
        public bool Connected = false;
        public bool Known = false;
        public string LastFound = "";
        public string FirstFound = "";
        public long SubnetId = 0;
        public bool Block = false;
        public string ByteIp = "";
        public int TcpPort1 = 0;
        public int TcpPort2 = 0;
        public int TcpPort3 = 0;
        public int TcpPort4 = 0;
    }

    public class SubnetRecord
    {
        public long SubnetId;
        public string IpRange = "";
        public bool CheckIp = false;
        public bool CheckNetbios = false;
        public bool CheckPort80 = false;
        public bool CheckPort137 = false;
        public string GatewayIp = "";
        public string GatewayMac = "";
        public bool Scan = false;
    }

    /// <summary>
    /// SQLite data store with the same tables as the original app (settings, computers, subnet,
    /// notification, manu) plus a metadata table for the Reborn app.
    ///
    /// FIXES in this file (v1.0.1 - the "app is still not working" round):
    ///  FIX A (CRITICAL - schema drift): earlier builds created the settings/computers/subnet
    ///       tables with FEWER columns than the current INSERT statements reference. On an
    ///       existing data.db, "INSERT INTO settings (... privacy_pref ...)" throws
    ///       "table settings has no column named privacy_pref", SaveSettings rethrows, and
    ///       every single save fails => setup wizard loops, autoscan never saves, app looks
    ///       completely broken even though the code "looks fine". Initialize() now runs
    ///       EnsureColumn() for EVERY column of EVERY table, so any old database is upgraded
    ///       in place.
    ///  FIX B: the DDL used to run as ONE batched statement. If any statement failed mid-batch
    ///       (e.g. the UNIQUE index on a polluted database with duplicate digisigs), the tables
    ///       after it were never created and Initialize() threw => dead app on every start.
    ///       Now every CREATE runs separately with its own try/catch.
    ///  FIX C: duplicate digisigs (produced by the earlier buggy build) are collapsed BEFORE
    ///       the unique index is created, so index creation can no longer fail.
    ///  FIX D: seeding (default settings row) moved OUT of the open connection, wrapped in
    ///       try/catch, and NetworkScanner.DetectFirstSubnet() is guarded - machines with no
    ///       network / exotic adapters can no longer crash the very first startup.
    ///  FIX E: WAL journal mode for reliable concurrent reads during a scan.
    ///  FIX F: VACUUM INTO (backup) no longer uses a bound parameter (fails on some SQLite
    ///       builds); the path is safely escaped/quoted instead.
    ///  FIX G: UpdateDeviceName added for the device editor dialog.
    ///
    /// Kept from the previous fix round (still valid):
    ///  1. SaveSettings rewrites the single row inside a transaction (old code appended rows
    ///     because the table has no primary key, so no setting ever persisted).
    ///  2. LoadSettings reads the newest row (ORDER BY rowid DESC).
    ///  3. Initialize() dedupes polluted settings tables from the buggy version.
    ///  4. No forced "UPDATE settings SET scan_ping='T'" on startup.
    ///  5. "manu" (MAC vendor / OUI) table for the Company column.
    ///  6. UpsertSubnet reads last_insert_rowid() separately.
    ///  7. ParseBool also accepts YES / NO style values used by the original app.
    /// </summary>
    public class Database
    {
        private readonly string _path;
        private readonly string _conn;

        public Database(string path = null)
        {
            _path = path ?? AppSettings.DatabasePath();
            _conn = "Data Source=" + _path;
        }

        public string Path => _path;
        public string ConnectionString => _conn;

        public SqliteConnection Open()
        {
            var c = new SqliteConnection(_conn);
            c.Open();
            return c;
        }

        public void Initialize()
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            long settingsCount;
            using (var c = Open())
            {
                // FIX E: WAL mode - readers no longer block while a scan writes.
                try
                {
                    using (var pr = c.CreateCommand())
                    {
                        pr.CommandText = "PRAGMA journal_mode=WAL;";
                        pr.ExecuteNonQuery();
                    }
                }
                catch { }

                // FIX B: every CREATE runs separately; one failure no longer kills the rest.
                string[] ddl = new string[]
                {
                    @"CREATE TABLE IF NOT EXISTS settings (
                        dbversion varchar(10) NOT NULL default '',
                        regkey varchar(24) NOT NULL default '',
                        installdate varchar(24) NOT NULL default '',
                        iprange varchar(31) NOT NULL default '',
                        not_message char(1) NOT NULL default 'F',
                        not_sound char(1) NOT NULL default 'F',
                        not_shutdown char(1) NOT NULL default 'F',
                        not_email char(1) NOT NULL default 'F',
                        email varchar(200) NOT NULL default '',
                        mailfrom varchar(200) NOT NULL default '',
                        mail_server varchar(100) NOT NULL default '',
                        mail_port int NOT NULL default 0,
                        logfolder varchar(100) NOT NULL default '',
                        logfile varchar(100) NOT NULL default '',
                        enablelog char(1) NOT NULL default 'F',
                        minimize char(1) NOT NULL default 'F',
                        windowclosenotice char(1) NOT NULL default 'T',
                        enablessl char(1) NOT NULL default 'F',
                        mail_username varchar(50) NOT NULL default '',
                        mail_password varchar(50) NOT NULL default '',
                        frequency int NOT NULL default 5,
                        scanning char(1) NOT NULL default 'F',
                        beta_version char(1) NOT NULL default 'F',
                        update_onstartup char(1) NOT NULL default 'T',
                        update_nightly char(1) NOT NULL default 'T',
                        update_nightly_time int NOT NULL default 3,
                        scan_ping char(1) NOT NULL default 'T',
                        scan_randomize char(1) NOT NULL default 'F',
                        scan_delay char(1) NOT NULL default 'F',
                        show_batch char(1) NOT NULL default 'F',
                        blocking char(1) NOT NULL default 'F',
                        winpcap_installed char(1) NOT NULL default 'F',
                        allow_blocking char(1) NOT NULL default 'F',
                        log_blocking char(1) NOT NULL default 'F',
                        devices_to_block int NOT NULL default 0,
                        firstrun char(1) NOT NULL default 'T',
                        firstrun_step int NOT NULL default 0,
                        recommended int NOT NULL default 0,
                        notifytimes int NOT NULL default 0,
                        web_sessionid varchar(64) NOT NULL default '',
                        web_sessiontime varchar(30) NOT NULL default '',
                        webusername varchar(200) NOT NULL default '',
                        webpassword varchar(200) NOT NULL default '',
                        pubtoken varchar(40) NOT NULL default '',
                        privtoken varchar(128) NOT NULL default '',
                        agentkey varchar(60) NOT NULL default '',
                        webblock char(1) NOT NULL default 'T',
                        log_web char(1) NOT NULL default 'F',
                        startupdelay int NOT NULL default 0,
                        log_ap char(1) NOT NULL default 'F',
                        autoscan char(1) NOT NULL default 'T',
                        scan_continuous char(1) NOT NULL default 'F',
                        filt_connected char(1) NOT NULL default 'F',
                        privacy_pref int NOT NULL default 0,
                        privacy_salt varchar(128) NOT NULL default '',
                        ignoregateway char(1) NOT NULL default 'F',
                        mainhwnd varchar(50) NOT NULL default ''
                    );",
                    @"CREATE TABLE IF NOT EXISTS computers (
                        uniqint INTEGER PRIMARY KEY AUTOINCREMENT,
                        digisig char(32) NOT NULL default '',
                        macaddress char(17) NOT NULL default '',
                        computername varchar(100) NOT NULL default '',
                        compdesc varchar(50) NOT NULL default '',
                        company varchar(50) NOT NULL default '',
                        lastip varchar(15) NOT NULL default '',
                        connected char(3) NOT NULL default '',
                        known char(7) NOT NULL default '',
                        lastfound varchar(17) NOT NULL default '',
                        firstfound varchar(17) NOT NULL default '',
                        subnetid INTEGER NOT NULL DEFAULT 0,
                        block char(1) NOT NULL DEFAULT 'F',
                        byteip varchar(15) NOT NULL default '',
                        tcpport1 int NOT NULL default 0,
                        tcpport2 int NOT NULL default 0,
                        tcpport3 int NOT NULL default 0,
                        tcpport4 int NOT NULL default 0
                    );",
                    @"CREATE TABLE IF NOT EXISTS subnet (
                        subnetid INTEGER PRIMARY KEY,
                        iprange varchar(31) NOT NULL default '',
                        check_ip char(1) NOT NULL default 'F',
                        check_netbios char(1) NOT NULL default 'F',
                        check_port80 char(1) NOT NULL default 'F',
                        check_port137 char(1) NOT NULL default 'F',
                        gatewayip varchar(15) NOT NULL default '',
                        gatewaymac varchar(17) NOT NULL default '',
                        scan char(1) NOT NULL default 'F'
                    );",
                    @"CREATE TABLE IF NOT EXISTS notification (
                        uniqint INTEGER PRIMARY KEY AUTOINCREMENT,
                        sentat varchar(17) NOT NULL default ''
                    );",
                    @"CREATE TABLE IF NOT EXISTS manu (
                        mac varchar(8) PRIMARY KEY,
                        company varchar(50) NOT NULL default ''
                    );",
                    @"CREATE TABLE IF NOT EXISTS meta (
                        key varchar(50) PRIMARY KEY,
                        value varchar(255) NOT NULL default ''
                    );"
                };
                foreach (string sql in ddl)
                {
                    try
                    {
                        using (var cmd = c.CreateCommand())
                        {
                            cmd.CommandText = sql;
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch { }
                }

                // Purge any phantom records from earlier buggy scans (empty MACs or IP digisigs)
                try
                {
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM computers WHERE macaddress IS NULL OR trim(macaddress) = '' OR macaddress LIKE '00:00:00%' OR macaddress LIKE '01:00:5E%' OR macaddress LIKE '33:33%' OR macaddress LIKE 'FF:FF:FF%' OR digisig LIKE 'IP:%';";
                        cmd.ExecuteNonQuery();
                    }
                }
                catch { }

                // ---- FIX A: upgrade OLD databases in place (schema drift) ----
                string[][] columns = new string[][]
                {
                    new string[] { "settings", "dbversion",        "varchar(10) NOT NULL default ''" },
                    new string[] { "settings", "regkey",            "varchar(24) NOT NULL default ''" },
                    new string[] { "settings", "installdate",       "varchar(24) NOT NULL default ''" },
                    new string[] { "settings", "iprange",           "varchar(31) NOT NULL default ''" },
                    new string[] { "settings", "not_message",       "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "not_sound",         "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "not_shutdown",      "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "not_email",         "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "email",             "varchar(200) NOT NULL default ''" },
                    new string[] { "settings", "mailfrom",          "varchar(200) NOT NULL default ''" },
                    new string[] { "settings", "mail_server",       "varchar(100) NOT NULL default ''" },
                    new string[] { "settings", "mail_port",         "int NOT NULL default 0" },
                    new string[] { "settings", "logfolder",         "varchar(100) NOT NULL default ''" },
                    new string[] { "settings", "logfile",           "varchar(100) NOT NULL default ''" },
                    new string[] { "settings", "enablelog",         "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "minimize",          "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "windowclosenotice", "char(1) NOT NULL default 'T'" },
                    new string[] { "settings", "enablessl",         "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "mail_username",      "varchar(50) NOT NULL default ''" },
                    new string[] { "settings", "mail_password",     "varchar(50) NOT NULL default ''" },
                    new string[] { "settings", "frequency",         "int NOT NULL default 5" },
                    new string[] { "settings", "scanning",          "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "beta_version",       "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "update_onstartup",  "char(1) NOT NULL default 'T'" },
                    new string[] { "settings", "update_nightly",    "char(1) NOT NULL default 'T'" },
                    new string[] { "settings", "update_nightly_time", "int NOT NULL default 3" },
                    new string[] { "settings", "scan_ping",         "char(1) NOT NULL default 'T'" },
                    new string[] { "settings", "scan_randomize",    "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "scan_delay",        "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "show_batch",        "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "blocking",         "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "winpcap_installed", "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "allow_blocking",    "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "log_blocking",      "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "devices_to_block",  "int NOT NULL default 0" },
                    new string[] { "settings", "firstrun",           "char(1) NOT NULL default 'T'" },
                    new string[] { "settings", "firstrun_step",     "int NOT NULL default 0" },
                    new string[] { "settings", "recommended",       "int NOT NULL default 0" },
                    new string[] { "settings", "notifytimes",       "int NOT NULL default 0" },
                    new string[] { "settings", "web_sessionid",    "varchar(64) NOT NULL default ''" },
                    new string[] { "settings", "web_sessiontime",  "varchar(30) NOT NULL default ''" },
                    new string[] { "settings", "webusername",       "varchar(200) NOT NULL default ''" },
                    new string[] { "settings", "webpassword",       "varchar(200) NOT NULL default ''" },
                    new string[] { "settings", "pubtoken",          "varchar(40) NOT NULL default ''" },
                    new string[] { "settings", "privtoken",          "varchar(128) NOT NULL default ''" },
                    new string[] { "settings", "agentkey",          "varchar(60) NOT NULL default ''" },
                    new string[] { "settings", "webblock",          "char(1) NOT NULL default 'T'" },
                    new string[] { "settings", "log_web",           "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "startupdelay",      "int NOT NULL default 0" },
                    new string[] { "settings", "log_ap",             "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "autoscan",          "char(1) NOT NULL default 'T'" },
                    new string[] { "settings", "scan_continuous",   "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "filt_connected",    "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "privacy_pref",      "int NOT NULL default 0" },
                    new string[] { "settings", "privacy_salt",     "varchar(128) NOT NULL default ''" },
                    new string[] { "settings", "ignoregateway",     "char(1) NOT NULL default 'F'" },
                    new string[] { "settings", "mainhwnd",         "varchar(50) NOT NULL default ''" },

                    new string[] { "computers", "digisig",      "char(32) NOT NULL default ''" },
                    new string[] { "computers", "macaddress",   "char(17) NOT NULL default ''" },
                    new string[] { "computers", "computername", "varchar(100) NOT NULL default ''" },
                    new string[] { "computers", "compdesc",     "varchar(50) NOT NULL default ''" },
                    new string[] { "computers", "company",      "varchar(50) NOT NULL default ''" },
                    new string[] { "computers", "lastip",       "varchar(15) NOT NULL default ''" },
                    new string[] { "computers", "connected",    "char(3) NOT NULL default ''" },
                    new string[] { "computers", "known",       "char(7) NOT NULL default ''" },
                    new string[] { "computers", "lastfound",   "varchar(17) NOT NULL default ''" },
                    new string[] { "computers", "firstfound",   "varchar(17) NOT NULL default ''" },
                    new string[] { "computers", "subnetid",     "INTEGER NOT NULL DEFAULT 0" },
                    new string[] { "computers", "block",        "char(1) NOT NULL DEFAULT 'F'" },
                    new string[] { "computers", "byteip",       "varchar(15) NOT NULL default ''" },
                    new string[] { "computers", "tcpport1",     "int NOT NULL default 0" },
                    new string[] { "computers", "tcpport2",     "int NOT NULL default 0" },
                    new string[] { "computers", "tcpport3",     "int NOT NULL default 0" },
                    new string[] { "computers", "tcpport4",     "int NOT NULL default 0" },

                    new string[] { "subnet", "iprange",        "varchar(31) NOT NULL default ''" },
                    new string[] { "subnet", "check_ip",       "char(1) NOT NULL default 'F'" },
                    new string[] { "subnet", "check_netbios",  "char(1) NOT NULL default 'F'" },
                    new string[] { "subnet", "check_port80",   "char(1) NOT NULL default 'F'" },
                    new string[] { "subnet", "check_port137",  "char(1) NOT NULL default 'F'" },
                    new string[] { "subnet", "gatewayip",      "varchar(15) NOT NULL default ''" },
                    new string[] { "subnet", "gatewaymac",     "varchar(17) NOT NULL default ''" },
                    new string[] { "subnet", "scan",           "char(1) NOT NULL default 'F'" },

                    new string[] { "notification", "sentat", "varchar(17) NOT NULL default ''" }
                };
                foreach (string[] col in columns)
                {
                    EnsureColumn(c, col[0], col[1], col[2]);
                }

                // Dedupe polluted settings table from the very first buggy build (kept from round 1)
                try
                {
                    using (var dedup = c.CreateCommand())
                    {
                        dedup.CommandText = "DELETE FROM settings WHERE rowid NOT IN (SELECT MAX(rowid) FROM settings)";
                        dedup.ExecuteNonQuery();
                    }
                }
                catch (Exception) { }

                // FIX C: collapse duplicate digisigs BEFORE creating the unique index.
                try
                {
                    using (var fix = c.CreateCommand())
                    {
                        fix.CommandText = "DELETE FROM computers WHERE uniqint NOT IN (SELECT MIN(uniqint) FROM computers GROUP BY digisig)";
                        fix.ExecuteNonQuery();
                    }
                }
                catch (Exception) { }

                try
                {
                    using (var idx = c.CreateCommand())
                    {
                        idx.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idxdigisig ON computers(digisig)";
                        idx.ExecuteNonQuery();
                    }
                }
                catch (Exception) { }

                // Promote custom user names in compdesc to computername so they display properly
                try
                {
                    using (var up = c.CreateCommand())
                    {
                        up.CommandText = @"
                            UPDATE computers 
                            SET computername = TRIM(compdesc) 
                            WHERE (computername IS NULL OR computername = '' OR UPPER(computername) LIKE 'UNKNOWN%') 
                              AND compdesc IS NOT NULL 
                              AND TRIM(compdesc) != '' 
                              AND UPPER(TRIM(compdesc)) != 'TYPE IN NAME';";
                        up.ExecuteNonQuery();
                    }
                }
                catch (Exception) { }

                using (var chk = c.CreateCommand())
                {
                    chk.CommandText = "SELECT COUNT(*) FROM settings";
                    settingsCount = Convert.ToInt64(chk.ExecuteScalar());
                }
            }

            // FIX D: seed the default settings row AFTER the connection is closed,
            // fully guarded - a machine with no network or a weird adapter stack can
            // no longer crash the first startup.
            if (settingsCount == 0)
            {
                try
                {
                    var s = new AppSettings();
                    try
                    {
                        string subnet = NetworkScanner.DetectFirstSubnet();
                        if (!string.IsNullOrEmpty(subnet)) s.iprange = subnet;
                    }
                    catch { }
                    s.installdate = DateTime.Now.ToString("yyyy-MM-dd");
                    s.agentkey = "REBORN-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
                    s.logfolder = System.IO.Path.Combine(AppSettings.DataDirectory(), "logs");
                    try { Directory.CreateDirectory(s.logfolder); } catch { }
                    s.privacy_salt = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                    SaveSettings(s);
                }
                catch (Exception)
                {
                    // Never block startup because of seeding.
                }
            }
        }

        private static void EnsureColumn(SqliteConnection c, string table, string column, string ddl)
        {
            try
            {
                bool exists = false;
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info(" + table + ")";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        if (string.Equals(Convert.ToString(r["name"]), column, StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }
                }
                if (!exists)
                {
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "ALTER TABLE " + table + " ADD COLUMN " + column + " " + ddl;
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception) { }
        }

        public AppSettings LoadSettings()
        {
            var s = new AppSettings();
            try
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT * FROM settings ORDER BY rowid DESC LIMIT 1";
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    s.dbversion = GetStr(r, "dbversion", "401");
                    s.regkey = GetStr(r, "regkey", "");
                    s.installdate = GetStr(r, "installdate", "");
                    s.iprange = GetStr(r, "iprange", "");
                    s.not_message = GetBool(r, "not_message", false);
                    s.not_sound = GetBool(r, "not_sound", false);
                    s.not_shutdown = GetBool(r, "not_shutdown", false);
                    s.not_email = GetBool(r, "not_email", false);
                    s.email = GetStr(r, "email", "");
                    s.mailfrom = GetStr(r, "mailfrom", "");
                    s.mail_server = GetStr(r, "mail_server", "");
                    s.mail_port = GetInt(r, "mail_port", 0);
                    s.logfolder = GetStr(r, "logfolder", "");
                    s.logfile = GetStr(r, "logfile", "");
                    s.enablelog = GetBool(r, "enablelog", false);
                    s.minimize = GetBool(r, "minimize", false);
                    s.windowclosenotice = GetBool(r, "windowclosenotice", true);
                    s.enablessl = GetBool(r, "enablessl", false);
                    s.mail_username = GetStr(r, "mail_username", "");
                    s.mail_password = GetStr(r, "mail_password", "");
                    s.frequency = GetInt(r, "frequency", 5);
                    s.scanning = GetBool(r, "scanning", false);
                    s.beta_version = GetBool(r, "beta_version", false);
                    s.update_onstartup = GetBool(r, "update_onstartup", true);
                    s.update_nightly = GetBool(r, "update_nightly", true);
                    s.update_nightly_time = GetInt(r, "update_nightly_time", 3);
                    s.scan_ping = GetBool(r, "scan_ping", true);
                    s.scan_randomize = GetBool(r, "scan_randomize", false);
                    s.scan_delay = GetBool(r, "scan_delay", false);
                    s.show_batch = GetBool(r, "show_batch", false);
                    s.blocking = GetBool(r, "blocking", false);
                    s.winpcap_installed = GetBool(r, "winpcap_installed", false);
                    s.allow_blocking = GetBool(r, "allow_blocking", false);
                    s.log_blocking = GetBool(r, "log_blocking", false);
                    s.devices_to_block = GetInt(r, "devices_to_block", 0);
                    s.firstrun = GetBool(r, "firstrun", true);
                    s.firstrun_step = GetInt(r, "firstrun_step", 0);
                    s.recommended = GetInt(r, "recommended", 0);
                    s.notifytimes = GetInt(r, "notifytimes", 0);
                    s.web_sessionid = GetStr(r, "web_sessionid", "");
                    s.web_sessiontime = ParseDateTime(GetStr(r, "web_sessiontime", ""), DateTime.Now);
                    s.webusername = GetStr(r, "webusername", "");
                    s.webpassword = GetStr(r, "webpassword", "");
                    s.pubtoken = GetStr(r, "pubtoken", "");
                    s.privtoken = GetStr(r, "privtoken", "");
                    s.agentkey = GetStr(r, "agentkey", "");
                    s.webblock = GetBool(r, "webblock", true);
                    s.log_web = GetBool(r, "log_web", false);
                    s.startupdelay = GetInt(r, "startupdelay", 0);
                    s.log_ap = GetBool(r, "log_ap", false);
                    s.autoscan = GetBool(r, "autoscan", true);
                    s.scan_continuous = GetBool(r, "scan_continuous", false);
                    s.filt_connected = GetBool(r, "filt_connected", false);
                    s.privacy_pref = GetInt(r, "privacy_pref", 0);
                    s.privacy_salt = GetStr(r, "privacy_salt", "");
                    s.ignoregateway = GetBool(r, "ignoregateway", false);
                    s.mainhwnd = GetStr(r, "mainhwnd", "");
                }
            }
            catch (Exception) { }
            return s;
        }

        public void SaveSettings(AppSettings s)
        {
            using var c = Open();
            using var tx = c.BeginTransaction();
            try
            {
                using (var del = c.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM settings";
                    del.ExecuteNonQuery();
                }
                using (var cmd = c.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
INSERT INTO settings (
 dbversion, regkey, installdate, iprange, not_message, not_sound, not_shutdown, not_email,
 email, mailfrom, mail_server, mail_port, logfolder, logfile, enablelog, minimize, windowclosenotice,
 enablessl, mail_username, mail_password, frequency, scanning, beta_version, update_onstartup,
 update_nightly, update_nightly_time, scan_ping, scan_randomize, scan_delay, show_batch, blocking,
 winpcap_installed, allow_blocking, log_blocking, devices_to_block, firstrun, firstrun_step, recommended,
 notifytimes, web_sessionid, web_sessiontime, webusername, webpassword, pubtoken, privtoken, agentkey,
 webblock, log_web, startupdelay, log_ap, autoscan, filt_connected, privacy_pref, privacy_salt,
 ignoregateway, mainhwnd, scan_continuous)
VALUES (@dbversion, @regkey, @installdate, @iprange, @not_message, @not_sound, @not_shutdown, @not_email,
 @email, @mailfrom, @mail_server, @mail_port, @logfolder, @logfile, @enablelog, @minimize, @windowclosenotice,
 @enablessl, @mail_username, @mail_password, @frequency, @scanning, @beta_version, @update_onstartup,
 @update_nightly, @update_nightly_time, @scan_ping, @scan_randomize, @scan_delay, @show_batch, @blocking,
 @winpcap_installed, @allow_blocking, @log_blocking, @devices_to_block, @firstrun, @firstrun_step, @recommended,
 @notifytimes, @web_sessionid, @web_sessiontime, @webusername, @webpassword, @pubtoken, @privtoken, @agentkey,
 @webblock, @log_web, @startupdelay, @log_ap, @autoscan, @filt_connected, @privacy_pref, @privacy_salt,
 @ignoregateway, @mainhwnd, @scan_continuous)";
                    cmd.Parameters.AddWithValue("@dbversion", s.dbversion);
                    cmd.Parameters.AddWithValue("@regkey", s.regkey);
                    cmd.Parameters.AddWithValue("@installdate", s.installdate);
                    cmd.Parameters.AddWithValue("@iprange", s.iprange);
                    cmd.Parameters.AddWithValue("@not_message", Bool(s.not_message));
                    cmd.Parameters.AddWithValue("@not_sound", Bool(s.not_sound));
                    cmd.Parameters.AddWithValue("@not_shutdown", Bool(s.not_shutdown));
                    cmd.Parameters.AddWithValue("@not_email", Bool(s.not_email));
                    cmd.Parameters.AddWithValue("@email", s.email);
                    cmd.Parameters.AddWithValue("@mailfrom", s.mailfrom);
                    cmd.Parameters.AddWithValue("@mail_server", s.mail_server);
                    cmd.Parameters.AddWithValue("@mail_port", s.mail_port);
                    cmd.Parameters.AddWithValue("@logfolder", s.logfolder);
                    cmd.Parameters.AddWithValue("@logfile", s.logfile);
                    cmd.Parameters.AddWithValue("@enablelog", Bool(s.enablelog));
                    cmd.Parameters.AddWithValue("@minimize", Bool(s.minimize));
                    cmd.Parameters.AddWithValue("@windowclosenotice", Bool(s.windowclosenotice));
                    cmd.Parameters.AddWithValue("@enablessl", Bool(s.enablessl));
                    cmd.Parameters.AddWithValue("@mail_username", s.mail_username);
                    cmd.Parameters.AddWithValue("@mail_password", s.mail_password);
                    cmd.Parameters.AddWithValue("@frequency", s.frequency);
                    cmd.Parameters.AddWithValue("@scanning", Bool(s.scanning));
                    cmd.Parameters.AddWithValue("@beta_version", Bool(s.beta_version));
                    cmd.Parameters.AddWithValue("@update_onstartup", Bool(s.update_onstartup));
                    cmd.Parameters.AddWithValue("@update_nightly", Bool(s.update_nightly));
                    cmd.Parameters.AddWithValue("@update_nightly_time", s.update_nightly_time);
                    cmd.Parameters.AddWithValue("@scan_ping", Bool(s.scan_ping));
                    cmd.Parameters.AddWithValue("@scan_randomize", Bool(s.scan_randomize));
                    cmd.Parameters.AddWithValue("@scan_delay", Bool(s.scan_delay));
                    cmd.Parameters.AddWithValue("@show_batch", Bool(s.show_batch));
                    cmd.Parameters.AddWithValue("@blocking", Bool(s.blocking));
                    cmd.Parameters.AddWithValue("@winpcap_installed", Bool(s.winpcap_installed));
                    cmd.Parameters.AddWithValue("@allow_blocking", Bool(s.allow_blocking));
                    cmd.Parameters.AddWithValue("@log_blocking", Bool(s.log_blocking));
                    cmd.Parameters.AddWithValue("@devices_to_block", s.devices_to_block);
                    cmd.Parameters.AddWithValue("@firstrun", Bool(s.firstrun));
                    cmd.Parameters.AddWithValue("@firstrun_step", s.firstrun_step);
                    cmd.Parameters.AddWithValue("@recommended", s.recommended);
                    cmd.Parameters.AddWithValue("@notifytimes", s.notifytimes);
                    cmd.Parameters.AddWithValue("@web_sessionid", s.web_sessionid);
                    cmd.Parameters.AddWithValue("@web_sessiontime", s.web_sessiontime.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@webusername", s.webusername);
                    cmd.Parameters.AddWithValue("@webpassword", s.webpassword);
                    cmd.Parameters.AddWithValue("@pubtoken", s.pubtoken);
                    cmd.Parameters.AddWithValue("@privtoken", s.privtoken);
                    cmd.Parameters.AddWithValue("@agentkey", s.agentkey);
                    cmd.Parameters.AddWithValue("@webblock", Bool(s.webblock));
                    cmd.Parameters.AddWithValue("@log_web", Bool(s.log_web));
                    cmd.Parameters.AddWithValue("@startupdelay", s.startupdelay);
                    cmd.Parameters.AddWithValue("@log_ap", Bool(s.log_ap));
                    cmd.Parameters.AddWithValue("@autoscan", Bool(s.autoscan));
                    cmd.Parameters.AddWithValue("@scan_continuous", Bool(s.scan_continuous));
                    cmd.Parameters.AddWithValue("@filt_connected", Bool(s.filt_connected));
                    cmd.Parameters.AddWithValue("@privacy_pref", s.privacy_pref);
                    cmd.Parameters.AddWithValue("@privacy_salt", s.privacy_salt);
                    cmd.Parameters.AddWithValue("@ignoregateway", Bool(s.ignoregateway));
                    cmd.Parameters.AddWithValue("@mainhwnd", s.mainhwnd);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public List<DeviceRecord> LoadDevices(bool connectedOnly)
        {
            var list = new List<DeviceRecord>();
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT * FROM computers" + (connectedOnly ? " WHERE connected='T'" : "") + " ORDER BY known DESC, computername COLLATE NOCASE";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(ReadDevice(r));
            }
            return list;
        }

        private DeviceRecord ReadDevice(SqliteDataReader r)
        {
            var d = new DeviceRecord
            {
                UniqInt = (long)r["uniqint"],
                DigiSig = GetStr(r, "digisig", ""),
                MacAddress = GetStr(r, "macaddress", ""),
                ComputerName = GetStr(r, "computername", ""),
                Description = GetStr(r, "compdesc", ""),
                Company = GetStr(r, "company", ""),
                LastIp = GetStr(r, "lastip", ""),
                Connected = GetBool(r, "connected", false),
                Known = GetBool(r, "known", false),
                LastFound = GetStr(r, "lastfound", ""),
                FirstFound = GetStr(r, "firstfound", ""),
                SubnetId = GetLong(r, "subnetid", 0),
                Block = GetBool(r, "block", false),
                ByteIp = GetStr(r, "byteip", ""),
                TcpPort1 = GetInt(r, "tcpport1", 0),
                TcpPort2 = GetInt(r, "tcpport2", 0),
                TcpPort3 = GetInt(r, "tcpport3", 0),
                TcpPort4 = GetInt(r, "tcpport4", 0)
            };
            return d;
        }

        public void UpsertDevice(DeviceRecord d)
        {
            if (string.IsNullOrEmpty(d.DigiSig))
            {
                // Never insert an empty key - all empty-key rows would collapse into one
                // because of the unique index on digisig.
                return;
            }
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
INSERT INTO computers (digisig, macaddress, computername, compdesc, company, lastip, connected, known, lastfound, firstfound, subnetid, block, byteip, tcpport1, tcpport2, tcpport3, tcpport4)
VALUES (@digisig, @macaddress, @computername, @compdesc, @company, @lastip, @connected, @known, @lastfound, @firstfound, @subnetid, @block, @byteip, @tcpport1, @tcpport2, @tcpport3, @tcpport4)
ON CONFLICT(digisig) DO UPDATE SET
 macaddress=@macaddress, computername=@computername, compdesc=@compdesc, company=@company,
 lastip=@lastip, connected=@connected, known=@known, lastfound=@lastfound,
 subnetid=@subnetid, block=@block, byteip=@byteip,
 tcpport1=@tcpport1, tcpport2=@tcpport2, tcpport3=@tcpport3, tcpport4=@tcpport4";
            cmd.Parameters.AddWithValue("@digisig", d.DigiSig);
            cmd.Parameters.AddWithValue("@macaddress", d.MacAddress);
            cmd.Parameters.AddWithValue("@computername", d.ComputerName);
            cmd.Parameters.AddWithValue("@compdesc", d.Description);
            cmd.Parameters.AddWithValue("@company", d.Company);
            cmd.Parameters.AddWithValue("@lastip", d.LastIp);
            cmd.Parameters.AddWithValue("@connected", Bool(d.Connected));
            cmd.Parameters.AddWithValue("@known", Bool(d.Known));
            cmd.Parameters.AddWithValue("@lastfound", d.LastFound);
            cmd.Parameters.AddWithValue("@firstfound", d.FirstFound);
            cmd.Parameters.AddWithValue("@subnetid", d.SubnetId);
            cmd.Parameters.AddWithValue("@block", Bool(d.Block));
            cmd.Parameters.AddWithValue("@byteip", d.ByteIp);
            cmd.Parameters.AddWithValue("@tcpport1", d.TcpPort1);
            cmd.Parameters.AddWithValue("@tcpport2", d.TcpPort2);
            cmd.Parameters.AddWithValue("@tcpport3", d.TcpPort3);
            cmd.Parameters.AddWithValue("@tcpport4", d.TcpPort4);
            cmd.ExecuteNonQuery();

            // keep firstfound on conflict
            using var c2 = Open();
            using var up = c2.CreateCommand();
            up.CommandText = "UPDATE computers SET firstfound = COALESCE(NULLIF(firstfound,''), @firstfound) WHERE digisig=@digisig";
            up.Parameters.AddWithValue("@firstfound", d.FirstFound);
            up.Parameters.AddWithValue("@digisig", d.DigiSig);
            up.ExecuteNonQuery();
        }

        public void UpdateDeviceKnown(string digisig, bool known)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE computers SET known=@known WHERE digisig=@digisig";
            cmd.Parameters.AddWithValue("@known", Bool(known));
            cmd.Parameters.AddWithValue("@digisig", digisig);
            cmd.ExecuteNonQuery();
        }

        public void UpdateDeviceName(string digisig, string name)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE computers SET computername=@name WHERE digisig=@digisig";
            cmd.Parameters.AddWithValue("@name", name ?? "");
            cmd.Parameters.AddWithValue("@digisig", digisig);
            cmd.ExecuteNonQuery();
        }

        public void UpdateDeviceDescription(string digisig, string desc)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE computers SET compdesc=@desc WHERE digisig=@digisig";
            cmd.Parameters.AddWithValue("@desc", desc ?? "");
            cmd.Parameters.AddWithValue("@digisig", digisig);
            cmd.ExecuteNonQuery();
        }

        public void UpdateDeviceBlock(string digisig, bool block)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE computers SET block=@block WHERE digisig=@digisig";
            cmd.Parameters.AddWithValue("@block", Bool(block));
            cmd.Parameters.AddWithValue("@digisig", digisig);
            cmd.ExecuteNonQuery();
        }

        public void UpdateDeviceConnected(string digisig, bool connected)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE computers SET connected=@c WHERE digisig=@digisig";
            cmd.Parameters.AddWithValue("@c", Bool(connected));
            cmd.Parameters.AddWithValue("@digisig", digisig);
            cmd.ExecuteNonQuery();
        }

        public void DeleteDevice(long uniqint)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM computers WHERE uniqint=@id";
            cmd.Parameters.AddWithValue("@id", uniqint);
            cmd.ExecuteNonQuery();
        }

        public void DeleteDeviceByDigisig(string digisig)
        {
            if (string.IsNullOrEmpty(digisig)) return;
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM computers WHERE digisig=@ds";
            cmd.Parameters.AddWithValue("@ds", digisig);
            cmd.ExecuteNonQuery();
        }

        public void DeleteDevicesByDigisig(IEnumerable<string> digisigs)
        {
            if (digisigs == null) return;
            using var c = Open();
            using var trans = c.BeginTransaction();
            using var cmd = c.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "DELETE FROM computers WHERE digisig=@ds";
            var p = cmd.Parameters.Add("@ds", Microsoft.Data.Sqlite.SqliteType.Text);
            foreach (var ds in digisigs)
            {
                if (string.IsNullOrEmpty(ds)) continue;
                p.Value = ds;
                cmd.ExecuteNonQuery();
            }
            trans.Commit();
        }

        public int DeleteAllIntruders()
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM computers WHERE known=0 OR known='False' OR known='false'";
            return cmd.ExecuteNonQuery();
        }

        public void SetAllConnected(bool connected)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE computers SET connected=@c";
            cmd.Parameters.AddWithValue("@c", Bool(connected));
            cmd.ExecuteNonQuery();
        }

        public List<SubnetRecord> LoadSubnets()
        {
            var list = new List<SubnetRecord>();
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT * FROM subnet ORDER BY subnetid";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new SubnetRecord
                {
                    SubnetId = GetLong(r, "subnetid", 0),
                    IpRange = GetStr(r, "iprange", ""),
                    CheckIp = GetBool(r, "check_ip", false),
                    CheckNetbios = GetBool(r, "check_netbios", false),
                    CheckPort80 = GetBool(r, "check_port80", false),
                    CheckPort137 = GetBool(r, "check_port137", false),
                    GatewayIp = GetStr(r, "gatewayip", ""),
                    GatewayMac = GetStr(r, "gatewaymac", ""),
                    Scan = GetBool(r, "scan", false)
                });
            }
            return list;
        }

        public long UpsertSubnet(SubnetRecord s)
        {
            using var c = Open();
            if (s.SubnetId == 0)
            {
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO subnet (iprange, check_ip, check_netbios, check_port80, check_port137, gatewayip, gatewaymac, scan) VALUES (@iprange,@check_ip,@check_netbios,@check_port80,@check_port137,@gatewayip,@gatewaymac,@scan)";
                    BindSubnetParams(cmd, s);
                    cmd.ExecuteNonQuery();
                }
                using var last = c.CreateCommand();
                last.CommandText = "SELECT last_insert_rowid()";
                object id = last.ExecuteScalar();
                return id == null || id == DBNull.Value ? 0 : Convert.ToInt64(id);
            }
            else
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE subnet SET iprange=@iprange, check_ip=@check_ip, check_netbios=@check_netbios, check_port80=@check_port80, check_port137=@check_port137, gatewayip=@gatewayip, gatewaymac=@gatewaymac, scan=@scan WHERE subnetid=@subnetid";
                BindSubnetParams(cmd, s);
                cmd.Parameters.AddWithValue("@subnetid", s.SubnetId);
                cmd.ExecuteNonQuery();
                return s.SubnetId;
            }
        }

        private static void BindSubnetParams(SqliteCommand cmd, SubnetRecord s)
        {
            cmd.Parameters.AddWithValue("@iprange", s.IpRange);
            cmd.Parameters.AddWithValue("@check_ip", Bool(s.CheckIp));
            cmd.Parameters.AddWithValue("@check_netbios", Bool(s.CheckNetbios));
            cmd.Parameters.AddWithValue("@check_port80", Bool(s.CheckPort80));
            cmd.Parameters.AddWithValue("@check_port137", Bool(s.CheckPort137));
            cmd.Parameters.AddWithValue("@gatewayip", s.GatewayIp);
            cmd.Parameters.AddWithValue("@gatewaymac", s.GatewayMac);
            cmd.Parameters.AddWithValue("@scan", Bool(s.Scan));
        }

        public void DeleteSubnet(long subnetid)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM subnet WHERE subnetid=@id";
            cmd.Parameters.AddWithValue("@id", subnetid);
            cmd.ExecuteNonQuery();
        }

        public bool NotificationRecentlySent(int minutes)
        {
            if (minutes <= 0) return false;
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM notification WHERE sentat >= @since";
            cmd.Parameters.AddWithValue("@since", DateTime.Now.AddMinutes(-minutes).ToString("yyyy-MM-dd HH:mm:ss"));
            long n = (long)cmd.ExecuteScalar();
            return n > 0;
        }

        public void RecordNotification()
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO notification (sentat) VALUES (@now)";
            cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        public void BackupTo(string dest)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            // FIX F: VACUUM INTO with a bound parameter fails on some SQLite builds -
            // escape the path manually instead.
            string safe = (dest ?? "").Replace("'", "''");
            cmd.CommandText = "VACUUM INTO '" + safe + "'";
            cmd.ExecuteNonQuery();
        }

        // ---------------- MAC vendor (Company) lookup, mirrors the original manu table ----------------

        /// <summary>
        /// Look up the manufacturer for a MAC address, first in the imported "manu" table
        /// (filled automatically when importing the old app's database), then in a small
        /// built-in list of common vendors.
        /// </summary>
        public string GetCompanyByMac(string mac)
        {
            string prefix = MacPrefix(mac);
            if (prefix == null) return "";
            try
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT company FROM manu WHERE mac=@p LIMIT 1";
                cmd.Parameters.AddWithValue("@p", prefix);
                object o = cmd.ExecuteScalar();
                if (o != null && o != DBNull.Value)
                {
                    string s = Convert.ToString(o).Trim();
                    if (s.Length > 0) return s;
                }
            }
            catch { }
            return BuiltInOui.TryGetValue(prefix, out string company) ? company : "";
        }

        /// <summary>Normalize any MAC ("aa-bb-cc-dd-ee-ff" / "AA:BB:CC:DD:EE:FF") to the 8-char
        /// colon prefix "AA:BB:CC" used by the original app's manu table.</summary>
        private static string MacPrefix(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return null;
            string m = mac.Trim().ToUpper().Replace('-', ':');
            if (m.Length < 8) return null;
            return m.Substring(0, 8);
        }

        /// <summary>Bulk-insert manufacturer rows (called by the legacy importer).</summary>
        public int ImportManuRows(IEnumerable<KeyValuePair<string, string>> rows)
        {
            int count = 0;
            using var c = Open();
            using var tx = c.BeginTransaction();
            try
            {
                using (var cmd = c.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT OR REPLACE INTO manu (mac, company) VALUES (@m, @c)";
                    var pm = cmd.Parameters.Add("@m", SqliteType.Text);
                    var pc = cmd.Parameters.Add("@c", SqliteType.Text);
                    foreach (var row in rows)
                    {
                        // Accept "AA:BB:CC", "AA-BB-CC", "aabbcc" ... and normalize to "AA:BB:CC".
                        string raw = (row.Key ?? "").Trim().ToUpper().Replace('-', ':').Replace(' ', ':');
                        string hex = raw.Replace(":", "");
                        if (hex.Length != 6) continue;                       // OUI = exactly 6 hex digits
                        foreach (char ch in hex)
                        {
                            if (!Uri.IsHexDigit(ch)) { hex = null; break; }
                        }
                        if (string.IsNullOrEmpty(hex)) continue;
                        string key = hex.Substring(0, 2) + ":" + hex.Substring(2, 2) + ":" + hex.Substring(4, 2);
                        pm.Value = key;
                        pc.Value = row.Value ?? "";
                        cmd.ExecuteNonQuery();
                        count++;
                    }
                }
                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
            return count;
        }

        /// <summary>
        /// Small offline fallback list of common OUI prefixes, used until the full IEEE table
        /// is imported from the old app (File > Import / Export Data > Import).
        /// </summary>
        private static readonly Dictionary<string, string> BuiltInOui = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AC:DE:48"] = "Apple",
            ["00:03:93"] = "Apple",
            ["F0:18:98"] = "Apple",
            ["00:16:32"] = "Samsung Electronics",
            ["8C:77:12"] = "Samsung Electronics",
            ["00:25:9E"] = "Huawei",
            ["18:C5:8A"] = "Huawei",
            ["B4:5F:84"] = "Fiberhome Telecommunication",
            ["4C:EA:AE"] = "Bilian Electronic",
            ["64:09:80"] = "Xiaomi",
            ["00:E0:4C"] = "Realtek",
            ["00:02:B3"] = "Intel",
            ["24:77:03"] = "Intel Corporation",
            ["3C:A9:F4"] = "Intel",
            ["A0:36:9F"] = "Intel",
            ["00:00:0C"] = "Cisco",
            ["00:10:18"] = "Broadcom",
            ["4C:CC:6A"] = "Hon Hai Precision (Foxconn)",
            ["50:C7:BF"] = "TP-Link",
            ["14:CC:20"] = "TP-Link",
            ["60:32:B1"] = "TP-Link",
            ["00:05:5D"] = "D-Link",
            ["14:D6:4D"] = "D-Link",
            ["34:08:04"] = "D-Link",
            ["00:09:5B"] = "NETGEAR",
            ["A0:40:A0"] = "NETGEAR",
            ["04:D9:F5"] = "ASUSTek Computer",
            ["00:23:54"] = "ASUSTek Computer",
            ["00:14:22"] = "Dell",
            ["00:1F:29"] = "HP",
            ["3C:D9:2B"] = "HP",
            ["00:1A:4B"] = "HP",
            ["54:EE:75"] = "Lenovo",
            ["00:01:4A"] = "Sony",
            ["B8:27:EB"] = "Raspberry Pi Foundation",
            ["DC:A6:32"] = "Raspberry Pi Trading",
            ["24:6F:28"] = "Espressif (ESP32/ESP8266)",
            ["30:AE:A4"] = "Espressif (ESP32)",
            ["5C:CF:7F"] = "Espressif (ESP8266)",
            ["6C:56:97"] = "Amazon (Echo)",
            ["74:C2:46"] = "Amazon",
            ["F4:F5:D8"] = "Google",
            ["3C:5A:B4"] = "Google",
            ["04:18:D6"] = "Ubiquiti Networks",
            ["24:A4:3C"] = "Ubiquiti Networks",
            ["F0:9F:C2"] = "Ubiquiti Networks",
            ["48:8F:5A"] = "MikroTik",
            ["2C:C8:1B"] = "MikroTik",
            ["64:D1:54"] = "MikroTik",
            ["00:50:56"] = "VMware",
            ["00:0C:29"] = "VMware",
            ["00:05:69"] = "VMware",
            ["08:00:27"] = "VirtualBox (Oracle)",
            ["52:54:00"] = "QEMU/KVM",
            ["00:15:5D"] = "Microsoft (Hyper-V)",
            ["00:03:FF"] = "Microsoft"
        };

        // ---- helpers ----
        public static string Bool(bool b) => b ? "T" : "F";
        public static bool ParseBool(object o)
        {
            if (o == null || o == DBNull.Value) return false;
            string s = Convert.ToString(o).Trim().ToUpper();
            return s == "T" || s == "TRUE" || s == "1" || s == "Y" || s == "YES";
        }
        private static string GetStr(SqliteDataReader r, string col, string def)
        {
            try { return r.GetOrdinal(col) >= 0 && !r.IsDBNull(r.GetOrdinal(col)) ? Convert.ToString(r[col]) : def; }
            catch { return def; }
        }
        private static DateTime ParseDateTime(string s, DateTime def)
        {
            if (DateTime.TryParse(s, out var dt)) return dt;
            return def;
        }
        private static bool GetBool(SqliteDataReader r, string col, bool def)
        {
            try { return r.GetOrdinal(col) >= 0 && !r.IsDBNull(r.GetOrdinal(col)) ? ParseBool(r[col]) : def; }
            catch { return def; }
        }
        private static int GetInt(SqliteDataReader r, string col, int def)
        {
            try { return r.GetOrdinal(col) >= 0 && !r.IsDBNull(r.GetOrdinal(col)) ? Convert.ToInt32(r[col]) : def; }
            catch { return def; }
        }
        private static long GetLong(SqliteDataReader r, string col, long def)
        {
            try { return r.GetOrdinal(col) >= 0 && !r.IsDBNull(r.GetOrdinal(col)) ? Convert.ToInt64(r[col]) : def; }
            catch { return def; }
        }
    }
}