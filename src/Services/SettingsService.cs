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
            _settingsDir = Path.Combine(localAppData, "ExtBatch");
            _settingsPath = Path.Combine(_settingsDir, "settings.json");

            try
            {
                if (!File.Exists(_settingsPath))
                {
                    string legacyPath = Path.Combine(localAppData, "CmOxeProvisioner", "settings.json");
                    if (File.Exists(legacyPath))
                    {
                        Directory.CreateDirectory(_settingsDir);
                        File.Copy(legacyPath, _settingsPath, false);
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
            AppSettings defaults = AppSettings.CreateDefault();
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
                if (string.IsNullOrEmpty(loaded.CmExePath)) loaded.CmExePath = defaults.CmExePath;
                if (loaded.CmUserId == null) loaded.CmUserId = "";
                if (loaded.CmPasswordEnc == null) loaded.CmPasswordEnc = "";
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
