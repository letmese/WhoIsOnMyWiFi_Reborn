using System;
using System.IO;

namespace WhoIsOnMyWiFi_Reborn
{
    /// <summary>
    /// Mirrors every field of the original Who Is On My WiFi clsSettings / settings table (v4.0.1).
    /// </summary>
    public class AppSettings
    {
        public string dbversion = "401";
        public string regkey = "";
        public string installdate = "";
        public string iprange = "";               // legacy single-range
        public bool not_message = false;
        public bool not_sound = false;
        public bool not_shutdown = false;
        public bool not_email = false;
        public string email = "";
        public string mailfrom = "";
        public string mail_server = "";
        public int mail_port = 0;
        public string logfolder = "";
        public string logfile = "";
        public bool enablelog = false;
        public bool minimize = false;
        public bool windowclosenotice = true;
        public bool enablessl = false;
        public string mail_username = "";
        public string mail_password = "";
        public int frequency = 5;                 // minutes between auto scans
        public bool scanning = false;
        public bool beta_version = false;
        public bool update_onstartup = true;
        public bool update_nightly = true;
        public int update_nightly_time = 3;
        public bool scan_ping = true;
        public bool scan_randomize = false;
        public bool scan_delay = false;
        public bool show_batch = false;
        public bool blocking = false;
        public bool winpcap_installed = false;
        public bool allow_blocking = false;
        public bool log_blocking = false;
        public int devices_to_block = 0;
        public bool firstrun = true;
        public int firstrun_step = 0;
        public int recommended = 0;
        public int notifytimes = 0;
        public string web_sessionid = "";
        public DateTime web_sessiontime = DateTime.Now;
        public string webusername = "";
        public string webpassword = "";
        public string pubtoken = "";
        public string privtoken = "";
        public string agentkey = "";
        public bool webblock = true;
        public bool log_web = false;
        public int startupdelay = 0;
        public bool log_ap = false;
        public bool autoscan = true;
        public bool scan_continuous = false;     // keep scanning back-to-back with no pause
        public bool filt_connected = false;
        public int privacy_pref = 0;              // 0 = private/log everything, 1 = public/anonymize
        public string privacy_salt = "";
        public bool ignoregateway = false;
        public string mainhwnd = "";

        public static string DataDirectory()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhoIsOnMyWiFi_Reborn");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string DatabasePath()
        {
            return Path.Combine(DataDirectory(), "data.db");
        }
    }
}