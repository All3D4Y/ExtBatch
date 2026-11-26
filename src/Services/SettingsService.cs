using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using CmOxeProvisioner.Models;

namespace CmOxeProvisioner.Services
{
    public class SettingsService
    {
        private readonly string _settingsDir;
        private readonly string _settingsPath;

        public SettingsService() : this(null)
        {
        }

        public SettingsService(string customSettingsDir)
        {
            if (!string.IsNullOrEmpty(customSettingsDir))
            {
                _settingsDir = customSettingsDir;
                _settingsPath = Path.Combine(_settingsDir, "settings.json");
                return;
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _settingsDir = Path.Combine(localAppData, "ExtBatch_v2");
            _settingsPath = Path.Combine(_settingsDir, "settings.json");

            try
            {
                if (!File.Exists(_settingsPath))
                {
                    AppSettings initial = CreateInitialSettings();
                    string v1Path = Path.Combine(localAppData, "ExtBatch", "settings.json");
                    if (File.Exists(v1Path))
                    {
                        MigrateSelectivelyFromV1(v1Path, initial);
                    }
                    Save(initial);
                }
            }
            catch
            {
            }
        }

        private static AppSettings CreateInitialSettings()
        {
            AppSettings s = AppSettings.CreateDefault();
            try
            {
                s.CmPasswordEnc = ProtectString("password");
            }
            catch
            {
                s.CmPasswordEnc = "";
            }
            return s;
        }

        private static void MigrateSelectivelyFromV1(string v1Path, AppSettings target)
        {
            try
            {
                string json = File.ReadAllText(v1Path, Encoding.UTF8);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                AppSettings v1 = ser.Deserialize<AppSettings>(json);
                if (v1 == null) return;

                if (!string.IsNullOrEmpty(v1.ThemeKey)) target.ThemeKey = v1.ThemeKey;
                if (!string.IsNullOrEmpty(v1.EmsUrl)) target.EmsUrl = v1.EmsUrl;
                if (!string.IsNullOrEmpty(v1.EmsServerKeyword)) target.EmsServerKeyword = v1.EmsServerKeyword;
                if (!string.IsNullOrEmpty(v1.EmsUserId)) target.EmsUserId = v1.EmsUserId;
                if (!string.IsNullOrEmpty(v1.EmsPasswordEnc)) target.EmsPasswordEnc = v1.EmsPasswordEnc;
                target.EmsAutoLogin = v1.EmsAutoLogin;
                target.EmsTerminalOnly = v1.EmsTerminalOnly;

                if (v1.OxeNodeNumber > 0) target.OxeNodeNumber = v1.OxeNodeNumber;
                if (!string.IsNullOrEmpty(v1.DefaultProfileExt)) target.DefaultProfileExt = v1.DefaultProfileExt;
                if (!string.IsNullOrEmpty(v1.DefaultProfileLogin)) target.DefaultProfileLogin = v1.DefaultProfileLogin;
                if (v1.DnWarnThreshold > 0) target.DnWarnThreshold = v1.DnWarnThreshold;

                if (!string.IsNullOrEmpty(v1.CmUserId))
                {
                    target.CmUserId = v1.CmUserId;
                    if (!string.IsNullOrEmpty(v1.CmPasswordEnc))
                    {
                        target.CmPasswordEnc = v1.CmPasswordEnc;
                    }
                }
            }
            catch
            {
            }
        }

        public string SettingsFilePath
        {
            get { return _settingsPath; }
        }

        public string WebViewDataDir
        {
            get { return Path.Combine(_settingsDir, "WebView2Data"); }
        }

        public AppSettings Load()
        {
            AppSettings defaults = CreateInitialSettings();
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    try { Save(defaults); } catch { }
                    return defaults;
                }
                string json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                AppSettings loaded = ser.Deserialize<AppSettings>(json);
                if (loaded == null) return defaults;

                if (string.IsNullOrEmpty(loaded.ThemeKey)) loaded.ThemeKey = defaults.ThemeKey;
                if (string.IsNullOrEmpty(loaded.CmPrimaryHost)) loaded.CmPrimaryHost = defaults.CmPrimaryHost;
                if (loaded.CmPrimaryPort <= 0 || loaded.CmPrimaryPort > 65535) loaded.CmPrimaryPort = defaults.CmPrimaryPort;
                if (loaded.CmBackupHost == null) loaded.CmBackupHost = defaults.CmBackupHost;
                if (loaded.CmBackupPort <= 0 || loaded.CmBackupPort > 65535) loaded.CmBackupPort = defaults.CmBackupPort;
                if (string.IsNullOrEmpty(loaded.CmClientName)) loaded.CmClientName = defaults.CmClientName;
                if (string.IsNullOrEmpty(loaded.CmUserId)) loaded.CmUserId = defaults.CmUserId;
                if (loaded.CmPasswordEnc == null)
                {
                    loaded.CmPasswordEnc = string.Equals(loaded.CmUserId, "default", StringComparison.OrdinalIgnoreCase)
                        ? defaults.CmPasswordEnc
                        : "";
                }
                if (loaded.BackendPort <= 0 || loaded.BackendPort > 65535) loaded.BackendPort = defaults.BackendPort;

                if (string.IsNullOrEmpty(loaded.EmsUrl)) loaded.EmsUrl = defaults.EmsUrl;
                if (string.IsNullOrEmpty(loaded.EmsServerKeyword)) loaded.EmsServerKeyword = defaults.EmsServerKeyword;
                if (loaded.EmsUserId == null) loaded.EmsUserId = "";
                if (loaded.EmsPasswordEnc == null) loaded.EmsPasswordEnc = "";
                if (loaded.OxeNodeNumber <= 0) loaded.OxeNodeNumber = 1;
                if (string.IsNullOrEmpty(loaded.DefaultProfileExt)) loaded.DefaultProfileExt = defaults.DefaultProfileExt;
                if (string.IsNullOrEmpty(loaded.DefaultProfileLogin)) loaded.DefaultProfileLogin = defaults.DefaultProfileLogin;
                if (loaded.DnWarnThreshold <= 0) loaded.DnWarnThreshold = 8;
                return loaded;
            }
            catch
            {
                return defaults;
            }
        }

        public void Save(AppSettings settings)
        {
            if (settings == null) return;
            Directory.CreateDirectory(_settingsDir);
            JavaScriptSerializer ser = new JavaScriptSerializer();
            string json = ser.Serialize(settings);
            File.WriteAllText(_settingsPath, json, new UTF8Encoding(false));
        }

        public static string ProtectString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipherBytes);
        }

        public static string UnprotectString(string encryptedBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64)) return "";
            try
            {
                byte[] cipherBytes = Convert.FromBase64String(encryptedBase64);
                byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return "";
            }
        }
    }
}
