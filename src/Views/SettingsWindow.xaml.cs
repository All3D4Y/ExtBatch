using System;
using System.Windows;
using System.Windows.Controls;
using CmOxeProvisioner.Models;
using CmOxeProvisioner.Services;
using Microsoft.Win32;

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
            c.CmExePath = src.CmExePath;
            c.CmUserId = src.CmUserId;
            c.CmPasswordEnc = src.CmPasswordEnc;
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
            c.DialPlanBlurPercent = src.DialPlanBlurPercent ?? 55;
            return c;
        }

        private void LoadFields(AppSettings s)
        {
            TxtCmExePath.Text = s.CmExePath ?? "";
            TxtCmUserId.Text = s.CmUserId ?? "";
            PwdCmPassword.Password = SettingsService.UnprotectString(s.CmPasswordEnc);

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
            int blur = Math.Max(0, Math.Min(100, s.DialPlanBlurPercent ?? 55));
            SldDialPlanBlur.Value = blur;
            UpdateBlurLabel(blur);
        }

        private void SldDialPlanBlur_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDialPlanBlurValue == null) return;
            UpdateBlurLabel((int)Math.Round(e.NewValue));
        }

        private void UpdateBlurLabel(int percent)
        {
            TxtDialPlanBlurValue.Text = percent <= 0 ? "0% (미사용)" : (percent + "%");
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

        private void BtnBrowseCm_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Configuration Manager (sce.exe)|sce.exe|Executable Files (*.exe)|*.exe|All Files (*.*)|*.*";
            ofd.Title = "Configuration Manager (sce.exe) 위치 선택";
            if (ofd.ShowDialog(this) == true)
            {
                TxtCmExePath.Text = ofd.FileName;
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

            _workingSettings.CmExePath = TxtCmExePath.Text.Trim();
            _workingSettings.CmUserId = TxtCmUserId.Text.Trim();
            _workingSettings.CmPasswordEnc = SettingsService.ProtectString(PwdCmPassword.Password);

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
            _workingSettings.DialPlanBlurPercent = Math.Max(0, Math.Min(100, (int)Math.Round(SldDialPlanBlur.Value)));

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
