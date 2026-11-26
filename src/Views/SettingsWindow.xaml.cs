using System;
using System.Windows;
using System.Windows.Controls;
using CmOxeProvisioner.Models;
using CmOxeProvisioner.Services;

namespace CmOxeProvisioner.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly AppSettings _workingSettings;
        private readonly string _initialThemeKey;
        private bool _savedCompleted = false;

        public AppSettings SavedSettings { get; private set; }

        public SettingsWindow(SettingsService settingsService, AppSettings currentSettings)
        {
            InitializeComponent();
            _settingsService = settingsService;
            _workingSettings = CloneSettings(currentSettings);
            _initialThemeKey = _workingSettings.ThemeKey ?? "antigravity-light";

            CmbTheme.ItemsSource = ThemeService.Themes;
            CmbTheme.DisplayMemberPath = "Label";
            CmbTheme.SelectedValuePath = "Key";

            LoadFields(_workingSettings);
            TxtStoragePath.Text = "설정 파일 위치: " + _settingsService.SettingsFilePath;

            this.Closing += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (!_savedCompleted)
                {
                    ThemeService.ApplyTheme(_initialThemeKey);
                }
            };
        }

        private static AppSettings CloneSettings(AppSettings src)
        {
            if (src == null) return AppSettings.CreateDefault();
            AppSettings c = new AppSettings();
            c.ThemeKey = src.ThemeKey;
            c.CmPrimaryHost = src.CmPrimaryHost;
            c.CmPrimaryPort = src.CmPrimaryPort;
            c.CmBackupHost = src.CmBackupHost;
            c.CmBackupPort = src.CmBackupPort;
            c.CmClientName = src.CmClientName;
            c.CmUserId = src.CmUserId;
            c.CmPasswordEnc = src.CmPasswordEnc;
            c.BackendPort = src.BackendPort;
            c.EmsUrl = src.EmsUrl;
            c.EmsServerKeyword = src.EmsServerKeyword;
            c.EmsUserId = src.EmsUserId;
            c.EmsPasswordEnc = src.EmsPasswordEnc;
            c.EmsAutoLogin = src.EmsAutoLogin;
            c.EmsTerminalOnly = src.EmsTerminalOnly;
            c.OxeNodeNumber = src.OxeNodeNumber;
            c.DefaultProfileExt = src.DefaultProfileExt;
            c.DefaultProfileLogin = src.DefaultProfileLogin;
            c.DnWarnThreshold = src.DnWarnThreshold;
            return c;
        }

        private void LoadFields(AppSettings s)
        {
            TxtCmPrimaryHost.Text = s.CmPrimaryHost ?? "10.62.130.67";
            TxtCmPrimaryPort.Text = (s.CmPrimaryPort > 0 ? s.CmPrimaryPort : 2020).ToString();
            TxtCmBackupHost.Text = s.CmBackupHost ?? "10.62.130.68";
            TxtCmBackupPort.Text = (s.CmBackupPort > 0 ? s.CmBackupPort : 2020).ToString();
            TxtCmClientName.Text = string.IsNullOrEmpty(s.CmClientName) ? "default" : s.CmClientName;
            TxtCmUserId.Text = string.IsNullOrEmpty(s.CmUserId) ? "default" : s.CmUserId;
            string plainPass = SettingsService.UnprotectString(s.CmPasswordEnc);
            if (string.IsNullOrEmpty(s.CmPasswordEnc) && string.Equals(TxtCmUserId.Text, "default", StringComparison.OrdinalIgnoreCase))
            {
                plainPass = "password";
            }
            PwdCmPassword.Password = plainPass;
            TxtBackendPort.Text = (s.BackendPort > 0 ? s.BackendPort : 18080).ToString();

            TxtEmsUrl.Text = s.EmsUrl ?? "https://lightgate.lbucess.com/terminal";
            TxtEmsServerKeyword.Text = s.EmsServerKeyword ?? "9. PBX_A";
            TxtEmsUserId.Text = s.EmsUserId ?? "";
            PwdEmsPassword.Password = SettingsService.UnprotectString(s.EmsPasswordEnc);
            ChkEmsAutoLogin.IsChecked = s.EmsAutoLogin;
            ChkEmsTerminalOnly.IsChecked = s.EmsTerminalOnly;

            TxtNodeNumber.Text = s.OxeNodeNumber.ToString();
            TxtDefProfExt.Text = s.DefaultProfileExt ?? "31090";
            TxtDefProfLogin.Text = s.DefaultProfileLogin ?? "71090";
            TxtWarnThreshold.Text = s.DnWarnThreshold.ToString();
            CmbTheme.SelectedValue = s.ThemeKey ?? "antigravity-light";
        }

        private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsInitialized) return;
            string previewKey = CmbTheme.SelectedValue as string;
            if (!string.IsNullOrEmpty(previewKey))
            {
                ThemeService.ApplyTheme(previewKey);
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            AppSettings defs = AppSettings.CreateDefault();
            LoadFields(defs);
            ThemeService.ApplyTheme(defs.ThemeKey);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.ApplyTheme(_initialThemeKey);
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string primaryHost = TxtCmPrimaryHost.Text.Trim();
            if (string.IsNullOrWhiteSpace(primaryHost))
            {
                MessageBox.Show(this, "Primary Config Server Host를 입력하세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int primaryPort;
            if (!int.TryParse(TxtCmPrimaryPort.Text.Trim(), out primaryPort) || primaryPort <= 0 || primaryPort > 65535)
            {
                MessageBox.Show(this, "Primary Port는 1~65535 사이의 숫자여야 합니다.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string backupHost = TxtCmBackupHost.Text.Trim();
            int backupPort = 2020;
            if (!string.IsNullOrEmpty(backupHost))
            {
                if (!int.TryParse(TxtCmBackupPort.Text.Trim(), out backupPort) || backupPort <= 0 || backupPort > 65535)
                {
                    MessageBox.Show(this, "Backup Port는 1~65535 사이의 숫자여야 합니다.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (!int.TryParse(TxtCmBackupPort.Text.Trim(), out backupPort) || backupPort <= 0 || backupPort > 65535)
            {
                backupPort = 2020;
            }

            int backendPort;
            if (!int.TryParse(TxtBackendPort.Text.Trim(), out backendPort) || backendPort <= 0 || backendPort > 65535)
            {
                MessageBox.Show(this, "백엔드 Port는 1~65535 사이의 숫자여야 합니다.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int nodeNum;
            if (!int.TryParse(TxtNodeNumber.Text.Trim(), out nodeNum) || nodeNum <= 0)
            {
                MessageBox.Show(this, "OXE Node 번호는 1 이상의 숫자여야 합니다.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int warnThreshold;
            if (!int.TryParse(TxtWarnThreshold.Text.Trim(), out warnThreshold) || warnThreshold <= 0)
            {
                MessageBox.Show(this, "내선 경고 기준 개수는 1 이상의 숫자여야 합니다.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _workingSettings.CmPrimaryHost = primaryHost;
            _workingSettings.CmPrimaryPort = primaryPort;
            _workingSettings.CmBackupHost = backupHost;
            _workingSettings.CmBackupPort = backupPort;
            _workingSettings.CmClientName = string.IsNullOrWhiteSpace(TxtCmClientName.Text) ? "default" : TxtCmClientName.Text.Trim();
            _workingSettings.CmUserId = string.IsNullOrWhiteSpace(TxtCmUserId.Text) ? "default" : TxtCmUserId.Text.Trim();
            _workingSettings.CmPasswordEnc = SettingsService.ProtectString(PwdCmPassword.Password);
            _workingSettings.BackendPort = backendPort;

            _workingSettings.EmsUrl = TxtEmsUrl.Text.Trim();
            _workingSettings.EmsServerKeyword = TxtEmsServerKeyword.Text.Trim();
            _workingSettings.EmsUserId = TxtEmsUserId.Text.Trim();
            _workingSettings.EmsPasswordEnc = SettingsService.ProtectString(PwdEmsPassword.Password);
            _workingSettings.EmsAutoLogin = (ChkEmsAutoLogin.IsChecked == true);
            _workingSettings.EmsTerminalOnly = (ChkEmsTerminalOnly.IsChecked == true);

            _workingSettings.OxeNodeNumber = nodeNum;
            _workingSettings.DefaultProfileExt = TxtDefProfExt.Text.Trim();
            _workingSettings.DefaultProfileLogin = TxtDefProfLogin.Text.Trim();
            _workingSettings.DnWarnThreshold = warnThreshold;

            string selTheme = CmbTheme.SelectedValue as string;
            if (!string.IsNullOrEmpty(selTheme))
            {
                _workingSettings.ThemeKey = selTheme;
            }

            _settingsService.Save(_workingSettings);
            ThemeService.ApplyTheme(_workingSettings.ThemeKey);
            _savedCompleted = true;
            SavedSettings = _workingSettings;
            DialogResult = true;
            Close();
        }
    }
}
