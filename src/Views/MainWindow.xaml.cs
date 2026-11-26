using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CmOxeProvisioner.Models;
using CmOxeProvisioner.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace CmOxeProvisioner.Views
{
    public partial class MainWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly string _baseDir;
        private readonly string _auditLogDir;
        private readonly BackendProcessService _backendService;
        private readonly CmWorkerService _cmWorker;

        private AppSettings _settings;
        private List<CustomerInfo> _allCustomers = new List<CustomerInfo>();
        private CenterAddTabView _activeCenterAddPopupView = null;
        private List<CustomerInfo> _filteredCustomers = new List<CustomerInfo>();
        private CustomerInfo _selectedCustomer;

        private string _lastOxeScript = "";
        private string _lastOxeAction = "create";
        private string _viewMode = "Split";
        private bool _pathsExpanded = false;
        private bool _isInitializingUi = true;
        private bool _isSyncingCheckboxes = false;
        private bool _prefAgentDn = true;
        private bool _prefPlace = true;
        private bool _prefAgentLogin = true;
        private bool _prefAcdPos = true;
        private bool _isCmBusy = false;
        private bool _isPbxCheckBusy = false;
        private bool _startupAutoConnectDone = false;
        private bool? _cmOptRealVq = null;
        private bool? _cmOptRealIvr = null;
        private bool _cmOptUserTouchedVq = false;
        private bool _cmOptUserTouchedIvr = false;
        private bool _isSyncingCenterOptChecks = false;

        private bool _webViewReady = false;
        private bool _webViewInitStarted = false;
        private string _lastNavigatedUrl = "";
        private bool _autoLoginInjectedForUrl = false;
        private bool _autoSetupLoopRunning = false;
        private bool _autoOpenedServerDone = false;
        private bool _loggedLightGateLogin = false;
        private bool _loggedTerminalReady = false;
        private bool _isEmsTerminalConnected = false;
        private bool _isEmsActionBusy = false;
        private int _emsLoopGen = 0;

        private NumberParseResult _lastDnParse = new NumberParseResult();
        private NumberParseResult _lastCtiParse = new NumberParseResult();
        private TokenErrorUnderlineAdorner _dnErrorAdorner;
        private TokenErrorUnderlineAdorner _ctiErrorAdorner;

        private readonly List<string> _openTabs = new List<string>();
        private string _activeTabKey = "extcti";
        private bool _isSidebarCollapsed = false;

        public MainWindow(SettingsService settingsService, AppSettings initialSettings)
        {
            InitializeComponent();
            Version appVer = typeof(MainWindow).Assembly.GetName().Version;
            if (appVer != null && TxtAppVersion != null)
            {
                TxtAppVersion.Text = string.Format("v{0}.{1}.{2}", appVer.Major, appVer.Minor, appVer.Build);
            }
            _settingsService = settingsService;
            _settings = initialSettings ?? AppSettings.CreateDefault();
            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!File.Exists(Path.Combine(_baseDir, "cm_customers.json")))
            {
                string asmDir = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location);
                if (!string.IsNullOrEmpty(asmDir) && File.Exists(Path.Combine(asmDir, "cm_customers.json")))
                {
                    _baseDir = asmDir;
                }
            }
            _auditLogDir = Path.Combine(_baseDir, "logs");
            _backendService = new BackendProcessService(_baseDir, _auditLogDir, delegate() { return _settings; });
            _cmWorker = new CmWorkerService(_backendService, delegate() { return _settings; });

            LoadCustomers();
            PersonTabControl.SetCustomers(_allCustomers);
            PersonTabControl.SetExtCtiContextProvider(GetExtCtiImportContext);
            PersonTabControl.FetchLoginPoolAsyncHandler = HandlePersonFetchLoginPoolAsync;
            PersonTabControl.DuplicateCheckAsyncHandler = HandlePersonDuplicateCheckAsync;
            PersonTabControl.CreatePersonsAsyncHandler = HandlePersonCreateAsync;
            PersonTabControl.FetchManagePersonsAsyncHandler = HandlePersonFetchManageAsync;
            PersonTabControl.UpdateManagePersonsAsyncHandler = HandlePersonUpdateManageAsync;
            PersonTabControl.DeleteManagePersonsAsyncHandler = HandlePersonDeleteManageAsync;

            DialPlanTabControl.Initialize(
                Path.Combine(_baseDir, "dialplan_db.json"),
                Path.Combine(_baseDir, "cm_customers.json"),
                () => _allCustomers,
                HandleCenterPrecheckAsync,
                HandleCenterCreateAsync,
                SaveLogTextToFile,
                delegate(CenterAddTabView popupView) { _activeCenterAddPopupView = popupView; },
                HandleCenterFullDeleteAsync,
                NavigateToExtCtiFromDialPlan,
                delegate(string msg)
                {
                    AuditLogService.AppendLine(_auditLogDir, msg);
                    AppendLog("INFO", msg);
                },
                RefreshCustomersFromDialPlan);

            if (UnifiedLookupTabControl != null)
            {
                UnifiedLookupTabControl.Initialize(
                    _cmWorker,
                    () => _allCustomers,
                    () => Path.Combine(_baseDir, "dialplan_db.json"),
                    NavigateToExtCtiFromLookup,
                    NavigateToPersonFromLookup,
                    delegate(string level, string msg)
                    {
                        AppendLog(level, msg);
                    });
            }

            if (SnapshotDiffTabControl != null)
            {
                SnapshotDiffTabControl.Initialize(
                    _cmWorker,
                    () => _allCustomers,
                    delegate(string level, string msg)
                    {
                        AppendLog(level, msg);
                    },
                    delegate(List<AuditEventDto> events)
                    {
                        if (events == null) return;
                        for (int i = 0; i < events.Count; i++)
                        {
                            string line = AuditLogService.FormatAuditEvent(events[i]);
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                AuditLogService.AppendLine(_auditLogDir, line);
                            }
                        }
                    });
            }

            ChkEmsTerminalOnly.IsChecked = _settings.EmsTerminalOnly;
            SetActiveViewMode("Split");
            OpenOrActivateTab("extcti");
            UpdatePreviewCounts();

            _isInitializingUi = false;
            AppendLog("INFO", "준비 완료");

            this.Loaded += MainWindow_Loaded;
            this.StateChanged += MainWindow_StateChanged;
            this.Closing += MainWindow_Closing;
            this.Closed += MainWindow_Closed;
        }

        private ExtCtiImportContext GetExtCtiImportContext()
        {
            NumberParseResult ctiParsed = OxeScriptService.ParseNumbersStrict(TxtCti.Text ?? "");
            ExtCtiImportContext ctx = new ExtCtiImportContext();
            ctx.Customer = _selectedCustomer;
            ctx.CtiNumbers = ctiParsed.Items;
            return ctx;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureInputAdorners();
            UpdatePreviewCounts();
            if (_viewMode != "Work")
            {
                EnsureWebView2InitializedAsync();
            }

            if (_startupAutoConnectDone) return;
            _startupAutoConnectDone = true;

            if (string.IsNullOrWhiteSpace(_settings.CmPrimaryHost)) return;

            await Task.Delay(250);
            await ConnectConfigBackgroundAsync(true);
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            bool isMax = (this.WindowState == WindowState.Maximized);
            RootFrame.Padding = isMax ? new Thickness(7) : new Thickness(0);
            MaxRectPath.Visibility = isMax ? Visibility.Collapsed : Visibility.Visible;
            RestoreRectPath.Visibility = isMax ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnMin_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnMaxRestore_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = (this.WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DialPlanTabControl != null && !DialPlanTabControl.ConfirmDiscardOrSaveUnsaved("프로그램 종료 전 선번장 저장 확인"))
            {
                e.Cancel = true;
                return;
            }
            if (_isCmBusy)
            {
                MessageBoxResult res = MessageBox.Show(
                    this,
                    "Config 작업이 현재 실행 중입니다. 그래도 창을 닫으시겠습니까?",
                    "작업 진행 중",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                }
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            ShutdownBackend();
        }

        internal void ShutdownBackend()
        {
            try
            {
                _backendService.Dispose();
            }
            catch
            {
            }
        }

        private bool HasSavedEmsCredentials()
        {
            string u = (_settings.EmsUserId ?? "").Trim();
            string p = SettingsService.UnprotectString(_settings.EmsPasswordEnc);
            return u.Length > 0 && p.Length > 0;
        }

        private void LoadCustomers()
        {
            try
            {
                CustomerRepository repo = new CustomerRepository();
                string jsonPath = Path.Combine(_baseDir, "cm_customers.json");
                _allCustomers = repo.LoadAll(jsonPath);
                FilterCustomers("");
            }
            catch (Exception ex)
            {
                AppendLog("ERR", "고객사 목록 로드 실패: " + ex.Message);
                AuditLogService.AppendError(_auditLogDir, "APP", "LoadCustomers", ex.Message);
            }
        }

        private void FilterCustomers(string query)
        {
            string q = (query ?? "").Trim().ToLowerInvariant();
            string prevName = _selectedCustomer != null ? _selectedCustomer.name : null;

            _filteredCustomers = new List<CustomerInfo>();
            for (int i = 0; i < _allCustomers.Count; i++)
            {
                CustomerInfo c = _allCustomers[i];
                string n = (c.name ?? "").ToLowerInvariant();
                string f = (c.floor ?? "").ToLowerInvariant();
                if (q.Length == 0 || n.Contains(q) || f.Contains(q))
                {
                    _filteredCustomers.Add(c);
                }
            }

            CmbCustomer.ItemsSource = null;
            CmbCustomer.DisplayMemberPath = "DisplayName";
            CmbCustomer.ItemsSource = _filteredCustomers;
            LblCustHeader.Text = string.Format("({0}개)", _filteredCustomers.Count);

            if (_filteredCustomers.Count > 0 && (q.Length > 0 || !string.IsNullOrEmpty(prevName)))
            {
                int restoreIdx = q.Length > 0 ? 0 : -1;
                if (!string.IsNullOrEmpty(prevName))
                {
                    for (int i = 0; i < _filteredCustomers.Count; i++)
                    {
                        if (_filteredCustomers[i].name == prevName)
                        {
                            restoreIdx = i;
                            break;
                        }
                    }
                }
                CmbCustomer.SelectedIndex = restoreIdx;
                if (restoreIdx < 0)
                {
                    _selectedCustomer = null;
                    UpdateCustomerDetails();
                }
            }
            else
            {
                CmbCustomer.SelectedIndex = -1;
                _selectedCustomer = null;
                UpdateCustomerDetails();
            }
        }

        private void UpdateCustomerDetails()
        {
            CustomerInfo c = _selectedCustomer;
            _cmOptRealVq = null;
            _cmOptRealIvr = null;
            _cmOptUserTouchedVq = false;
            _cmOptUserTouchedIvr = false;

            if (c == null)
            {
                TxtFloorBadge.Text = "-";
                BdPathWarn.Visibility = Visibility.Collapsed;
                TxtPathLogin.Text = "1) Agent Login : (없음)";
                TxtPathAcd.Text = "2) ACD Position: (없음)";
                TxtPathDn.Text = "3) Agent DN    : (없음)";
                TxtPathPlace.Text = "4) Place       : (없음)";
                TxtPathNote.Visibility = Visibility.Collapsed;
                _isSyncingCenterOptChecks = true;
                ChkCenterVQueue.IsChecked = false;
                ChkCenterIvr.IsChecked = false;
                ChkCenterVQueue.IsEnabled = false;
                ChkCenterIvr.IsEnabled = false;
                BtnCheckCenterOpts.IsEnabled = false;
                BtnApplyCenterOpts.IsEnabled = false;
                TxtCmOptionSyncState.Text = "센터 미선택";
                _isSyncingCenterOptChecks = false;
                CmbOxePreset.ItemsSource = null;
                CmbOxePreset.SelectedIndex = -1;
                TxtEntityNo.Text = "";
                TxtAgentPg.Text = "";
                TxtProfExt.Text = "";
                TxtProfLogin.Text = "";
                return;
            }

            TxtFloorBadge.Text = string.IsNullOrEmpty(c.floor) ? "-" : c.floor;
            bool hasNullPath = (c.agentLogin == null || c.acdPosition == null || c.agentDn == null || c.place == null);
            bool hasDiffNote = !string.IsNullOrEmpty(c.note) && (c.note.Contains("다름") || c.note.Contains("차이") || c.note.Contains("누락"));
            BdPathWarn.Visibility = (hasNullPath || hasDiffNote) ? Visibility.Visible : Visibility.Collapsed;

            TxtPathLogin.Text = FormatCmPath("1) Agent Login ", c.agentLogin);
            TxtPathAcd.Text = FormatCmPath("2) ACD Position", c.acdPosition);
            TxtPathDn.Text = FormatCmPath("3) Agent DN    ", c.agentDn);
            TxtPathPlace.Text = FormatCmPath("4) Place       ", c.place);

            _isSyncingCenterOptChecks = true;
            ChkCenterVQueue.IsEnabled = !_isCmBusy;
            ChkCenterIvr.IsEnabled = !_isCmBusy;
            BtnCheckCenterOpts.IsEnabled = !_isCmBusy;
            BtnApplyCenterOpts.IsEnabled = !_isCmBusy;
            ChkCenterVQueue.IsChecked = c.useVQueue ?? false;
            ChkCenterIvr.IsChecked = c.useIvr ?? false;
            if (c.useVQueue.HasValue || c.useIvr.HasValue)
            {
                _cmOptRealVq = c.useVQueue;
                _cmOptRealIvr = c.useIvr;
                TxtCmOptionSyncState.Text = string.Format("저장값 (VQ:{0} / IVR:{1})",
                    c.useVQueue == true ? "ON" : "OFF",
                    c.useIvr == true ? "ON" : "OFF");
            }
            else
            {
                TxtCmOptionSyncState.Text = "미확인 ('상태 조회' 또는 '적용하기')";
            }
            _isSyncingCenterOptChecks = false;

            if (!string.IsNullOrEmpty(c.note))
            {
                TxtPathNote.Text = "※ 참고: " + c.note;
                TxtPathNote.Visibility = Visibility.Visible;
            }
            else
            {
                TxtPathNote.Visibility = Visibility.Collapsed;
            }

            PopulateOxePresets(c);
        }

        private static string FormatCmPath(string label, string[] arr)
        {
            if (arr == null || arr.Length == 0)
            {
                return label + " : [경로 없음 - 해당 메뉴 미존재]";
            }
            return label + " : " + string.Join(" > ", arr);
        }

        private void PopulateOxePresets(CustomerInfo c)
        {
            List<OxePreset> list = new List<OxePreset>();
            if (c.oxePresets != null && c.oxePresets.Count > 0)
            {
                list.AddRange(c.oxePresets);
            }
            else
            {
                OxePreset emptyPreset = new OxePreset();
                emptyPreset.label = "[선번장 정보 없음 — 직접 입력]";
                emptyPreset.entityNo = "";
                emptyPreset.agentPg = "";
                emptyPreset.sampleExt = _settings.DefaultProfileExt;
                emptyPreset.sampleLoginId = _settings.DefaultProfileLogin;
                list.Add(emptyPreset);
            }

            CmbOxePreset.ItemsSource = list;
            CmbOxePreset.DisplayMemberPath = "label";
            CmbOxePreset.SelectedIndex = 0;
        }

        private void ApplySelectedOxePreset()
        {
            OxePreset p = CmbOxePreset.SelectedItem as OxePreset;
            if (p == null) return;
            TxtEntityNo.Text = p.entityNo ?? "";
            TxtAgentPg.Text = p.agentPg ?? "";
            TxtProfExt.Text = !string.IsNullOrEmpty(p.sampleExt) ? p.sampleExt : (_settings.DefaultProfileExt ?? "31090");
            TxtProfLogin.Text = !string.IsNullOrEmpty(p.sampleLoginId) ? p.sampleLoginId : (_settings.DefaultProfileLogin ?? "71090");
        }

        // ==================== CHECK ALL & 4 CHECKBOXES SYNC ====================
        private void ChkMaster_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsInitialized || _isSyncingCheckboxes) return;
            if (ChkAgentDn == null || ChkPlace == null || ChkAgentLogin == null || ChkAcdPos == null) return;
            bool masterChecked = (ChkCheckAll.IsChecked == true);

            _isSyncingCheckboxes = true;
            try
            {
                if (ChkAgentDn.IsEnabled)
                {
                    ChkAgentDn.IsChecked = masterChecked;
                    _prefAgentDn = masterChecked;
                }
                if (ChkPlace.IsEnabled)
                {
                    ChkPlace.IsChecked = masterChecked;
                    _prefPlace = masterChecked;
                }
                if (ChkAgentLogin.IsEnabled)
                {
                    ChkAgentLogin.IsChecked = masterChecked;
                    _prefAgentLogin = masterChecked;
                }
                if (ChkAcdPos.IsEnabled)
                {
                    ChkAcdPos.IsChecked = masterChecked;
                    _prefAcdPos = masterChecked;
                }
            }
            finally
            {
                _isSyncingCheckboxes = false;
            }
            UpdatePreviewCounts();
        }

        private void ChkItem_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsInitialized || _isSyncingCheckboxes) return;
            if (ChkCheckAll == null || ChkAgentDn == null || ChkPlace == null || ChkAgentLogin == null || ChkAcdPos == null) return;
            _isSyncingCheckboxes = true;
            try
            {
                if (ChkAgentDn.IsEnabled) _prefAgentDn = (ChkAgentDn.IsChecked == true);
                if (ChkPlace.IsEnabled) _prefPlace = (ChkPlace.IsChecked == true);
                if (ChkAgentLogin.IsEnabled) _prefAgentLogin = (ChkAgentLogin.IsChecked == true);
                if (ChkAcdPos.IsEnabled) _prefAcdPos = (ChkAcdPos.IsChecked == true);

                UpdateMasterCheckboxState();
            }
            finally
            {
                _isSyncingCheckboxes = false;
            }
            UpdatePreviewCounts();
        }

        private void SyncCmCheckboxesWithInputs()
        {
            if (ChkCheckAll == null || ChkAgentDn == null || ChkPlace == null || ChkAgentLogin == null || ChkAcdPos == null) return;
            bool hasDnInput = !string.IsNullOrWhiteSpace(TxtDn.Text);
            bool hasCtiInput = !string.IsNullOrWhiteSpace(TxtCti.Text);

            _isSyncingCheckboxes = true;
            try
            {
                ChkAgentDn.IsEnabled = hasDnInput;
                ChkPlace.IsEnabled = hasDnInput;
                ChkAgentDn.IsChecked = hasDnInput ? _prefAgentDn : false;
                ChkPlace.IsChecked = hasDnInput ? _prefPlace : false;

                ChkAgentLogin.IsEnabled = hasCtiInput;
                ChkAcdPos.IsEnabled = hasCtiInput;
                ChkAgentLogin.IsChecked = hasCtiInput ? _prefAgentLogin : false;
                ChkAcdPos.IsChecked = hasCtiInput ? _prefAcdPos : false;

                UpdateMasterCheckboxState();
            }
            finally
            {
                _isSyncingCheckboxes = false;
            }
        }

        private void UpdateMasterCheckboxState()
        {
            bool anyEnabled = ChkAgentDn.IsEnabled || ChkPlace.IsEnabled || ChkAgentLogin.IsEnabled || ChkAcdPos.IsEnabled;
            ChkCheckAll.IsEnabled = anyEnabled;
            if (!anyEnabled)
            {
                ChkCheckAll.IsChecked = false;
                return;
            }

            bool allEnabledChecked =
                (!ChkAgentDn.IsEnabled || ChkAgentDn.IsChecked == true)
                && (!ChkPlace.IsEnabled || ChkPlace.IsChecked == true)
                && (!ChkAgentLogin.IsEnabled || ChkAgentLogin.IsChecked == true)
                && (!ChkAcdPos.IsEnabled || ChkAcdPos.IsChecked == true);
            ChkCheckAll.IsChecked = allEnabledChecked;
        }

        private bool IsLoginChecked() { return ChkAgentLogin.IsEnabled && ChkAgentLogin.IsChecked == true; }
        private bool IsAcdChecked() { return ChkAcdPos.IsEnabled && ChkAcdPos.IsChecked == true; }
        private bool IsDnChecked() { return ChkAgentDn.IsEnabled && ChkAgentDn.IsChecked == true; }
        private bool IsPlaceChecked() { return ChkPlace.IsEnabled && ChkPlace.IsChecked == true; }

        // ==================== PARSING & LIVE COUNTS ====================
        private void EnsureInputAdorners()
        {
            if (_dnErrorAdorner == null)
            {
                _dnErrorAdorner = AttachErrorAdorner(TxtDn);
            }
            if (_ctiErrorAdorner == null)
            {
                _ctiErrorAdorner = AttachErrorAdorner(TxtCti);
            }
        }

        private static TokenErrorUnderlineAdorner AttachErrorAdorner(TextBox tb)
        {
            if (tb == null) return null;
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(tb);
            if (layer == null) return null;

            TokenErrorUnderlineAdorner ad = new TokenErrorUnderlineAdorner(tb);
            layer.Add(ad);
            tb.SizeChanged += delegate { ad.InvalidateVisual(); };
            tb.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(delegate { ad.InvalidateVisual(); }));
            return ad;
        }

        private void UpdatePreviewCounts()
        {
            EnsureInputAdorners();
            SyncCmCheckboxesWithInputs();
            NumberParseResult dn = OxeScriptService.ParseNumbersStrict(TxtDn.Text, "3", 5, "내선번호");
            NumberParseResult cti = OxeScriptService.ParseNumbersStrict(TxtCti.Text, "7", 5, "CTI");
            _lastDnParse = dn;
            _lastCtiParse = cti;
            int threshold = _settings.DnWarnThreshold > 0 ? _settings.DnWarnThreshold : 8;

            TxtLiveCount.Text = string.Format("내선 {0}건 · CTI {1}건", dn.Items.Count, cti.Items.Count);
            UpdateNumberFieldFeedback(TxtDn, TxtDnTag, BdDnErr, TxtDnErrCount, TxtDnErrHint, _dnErrorAdorner, dn, threshold);
            UpdateNumberFieldFeedback(TxtCti, TxtCtiTag, BdCtiErr, TxtCtiErrCount, TxtCtiErrHint, _ctiErrorAdorner, cti, 0);
        }

        private static void UpdateNumberFieldFeedback(
            TextBox tb,
            TextBlock countLabel,
            Border errBadge,
            TextBlock errBadgeText,
            TextBlock errHintLabel,
            TokenErrorUnderlineAdorner adorner,
            NumberParseResult res,
            int warnThreshold)
        {
            int validCount = res != null ? res.Items.Count : 0;
            int errCount = res != null ? res.Errors.Count : 0;

            if (warnThreshold > 0 && validCount >= warnThreshold)
            {
                countLabel.Text = string.Format("{0}건 (대량 입력 주의)", validCount);
                countLabel.SetResourceReference(TextBlock.ForegroundProperty, "WarnFgBrush");
            }
            else
            {
                countLabel.Text = string.Format("{0}건", validCount);
                countLabel.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            }

            if (errCount > 0)
            {
                errBadge.Visibility = Visibility.Visible;
                errBadgeText.Text = string.Format("오류 {0}", errCount);
                string allErr = string.Join("\n", res.Errors.ToArray());
                errBadge.ToolTip = allErr;

                errHintLabel.Text = (errCount > 1)
                    ? string.Format("! {0} (외 {1}건)", res.Errors[0], errCount - 1)
                    : ("! " + res.Errors[0]);
                errHintLabel.Visibility = Visibility.Visible;
                tb.ToolTip = allErr;
            }
            else
            {
                errBadge.Visibility = Visibility.Collapsed;
                errBadge.ToolTip = null;
                errHintLabel.Visibility = Visibility.Collapsed;
                errHintLabel.Text = "";
                tb.ToolTip = null;
            }

            if (adorner != null)
            {
                adorner.UpdateSpans(res != null ? res.ErrorSpans : null);
                if (errCount > 0)
                {
                    tb.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                    {
                        adorner.InvalidateVisual();
                    }));
                }
            }
        }

        // ==================== EVENT HANDLERS ====================
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializingUi) return;
            FilterCustomers(TxtSearch.Text);
        }

        private void CmbCustomer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedCustomer = CmbCustomer.SelectedItem as CustomerInfo;
            UpdateCustomerDetails();
        }

        private void CmbOxePreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplySelectedOxePreset();
        }

        private void TxtNumbers_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializingUi) return;
            UpdatePreviewCounts();
        }

        private void TxtNumbers_MouseMove(object sender, MouseEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb == null) return;
            NumberParseResult res = (tb == TxtDn) ? _lastDnParse : (tb == TxtCti ? _lastCtiParse : null);
            if (res == null || res.ErrorSpans.Count == 0) return;

            try
            {
                int idx = tb.GetCharacterIndexFromPoint(e.GetPosition(tb), false);
                if (idx >= 0)
                {
                    for (int i = 0; i < res.ErrorSpans.Count; i++)
                    {
                        NumberErrorSpan sp = res.ErrorSpans[i];
                        if (idx >= sp.Start && idx < sp.Start + sp.Length)
                        {
                            tb.ToolTip = sp.Message;
                            return;
                        }
                    }
                }
                tb.ToolTip = string.Join("\n", res.Errors.ToArray());
            }
            catch { }
        }

        private void BtnTogglePaths_Click(object sender, RoutedEventArgs e)
        {
            _pathsExpanded = !_pathsExpanded;
            BdCmPaths.Visibility = _pathsExpanded ? Visibility.Visible : Visibility.Collapsed;
            BtnTogglePaths.Content = _pathsExpanded ? "Config 경로 ▴" : "Config 경로 ▾";
        }

        private async void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            string prevHost = _settings.CmPrimaryHost;
            int prevPort = _settings.CmPrimaryPort;
            string prevBkHost = _settings.CmBackupHost;
            int prevBkPort = _settings.CmBackupPort;
            string prevClient = _settings.CmClientName;
            string prevUser = _settings.CmUserId;
            string prevPass = _settings.CmPasswordEnc;
            int prevBePort = _settings.BackendPort;

            SettingsWindow sw = new SettingsWindow(_settingsService, _settings);
            sw.Owner = this;
            if (sw.ShowDialog() == true && sw.SavedSettings != null)
            {
                _settings = sw.SavedSettings;
                _isInitializingUi = true;
                ChkEmsTerminalOnly.IsChecked = _settings.EmsTerminalOnly;
                _isInitializingUi = false;

                ThemeService.ApplyTheme(_settings.ThemeKey);
                SyncTabAndSidebarVisuals();
                UpdateTabButtonStyle(BtnTabSplit, _viewMode == "Split");
                UpdateTabButtonStyle(BtnTabWork, _viewMode == "Work");
                UpdateTabButtonStyle(BtnTabLightGate, _viewMode == "LightGate");
                UpdatePreviewCounts();
                AppendLog("OK", "설정 저장 완료");

                bool cmChanged = !string.Equals(prevHost ?? "", _settings.CmPrimaryHost ?? "", StringComparison.OrdinalIgnoreCase)
                    || prevPort != _settings.CmPrimaryPort
                    || !string.Equals(prevBkHost ?? "", _settings.CmBackupHost ?? "", StringComparison.OrdinalIgnoreCase)
                    || prevBkPort != _settings.CmBackupPort
                    || !string.Equals(prevClient ?? "", _settings.CmClientName ?? "", StringComparison.Ordinal)
                    || !string.Equals(prevUser ?? "", _settings.CmUserId ?? "", StringComparison.Ordinal)
                    || !string.Equals(prevPass ?? "", _settings.CmPasswordEnc ?? "", StringComparison.Ordinal);

                if (prevBePort != _settings.BackendPort && _backendService.IsRunning)
                {
                    AppendLog("INFO", "로컬 백엔드 포트 변경은 프로그램 재시작 후 적용됩니다.");
                }

                if (cmChanged && !_isCmBusy)
                {
                    await _cmWorker.DisconnectDirectAsync();
                    await ConnectConfigBackgroundAsync(false);
                }

                if (_webViewReady)
                {
                    _autoLoginInjectedForUrl = false;
                    _autoOpenedServerDone = false;
                    await OnLightGateReadyOrNavigatedAsync();
                }
            }
        }

        private void SaveLogTextToFile(string logText, string filePrefix)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Log File (*.log)|*.log|Text File (*.txt)|*.txt";
            sfd.FileName = (string.IsNullOrWhiteSpace(filePrefix) ? "ExtBatch" : filePrefix) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";
            if (sfd.ShowDialog(this) == true)
            {
                File.WriteAllText(sfd.FileName, logText ?? "", new UTF8Encoding(true));
                AppendLog("OK", "Log 저장 완료: " + sfd.FileName);
            }
        }

        private void BtnSaveLog_Click(object sender, RoutedEventArgs e)
        {
            SaveLogTextToFile(TxtLog.Text, "ExtBatch");
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Clear();
        }

        private void AppendLog(string level, string message)
        {
            string line = string.Format("[{0}] [{1}] {2}", DateTime.Now.ToString("HH:mm:ss"), level, message);
            if (TxtLog.Text.Length > 0) TxtLog.AppendText(Environment.NewLine);
            TxtLog.AppendText(line);
            TxtLog.ScrollToEnd();
        }

        // ==================== PBX SCRIPT BUILD / SEND OR COPY ====================
        private bool CheckLargeDnBeforeCreate(List<string> dnItems)
        {
            int threshold = _settings.DnWarnThreshold > 0 ? _settings.DnWarnThreshold : 8;
            if (dnItems.Count >= threshold)
            {
                return PopupDialogs.ConfirmLargeExtensionWarning(this, dnItems.Count, threshold);
            }
            return true;
        }

        private OxeBuildResult BuildValidatedOxeScript(string mode)
        {
            if (mode == "create")
            {
                NumberParseResult dnPre = OxeScriptService.ParseNumbersStrict(TxtDn.Text, "3", 5, "내선번호");
                if (dnPre.Errors.Count == 0 && !CheckLargeDnBeforeCreate(dnPre.Items))
                {
                    AppendLog("WARN", string.Format("PBX 내선 {0}건 생성이 사용자에 의해 취소되었습니다.", dnPre.Items.Count));
                    return null;
                }
            }

            return OxeScriptService.BuildAndValidate(
                mode,
                TxtDn.Text,
                TxtCti.Text,
                TxtEntityNo.Text,
                TxtAgentPg.Text,
                TxtProfExt.Text,
                TxtProfLogin.Text,
                _settings.OxeNodeNumber);
        }

        private async Task SendOrCopyOxeScriptAsync(string mode, bool copyFallbackOnUnavail)
        {
            OxeBuildResult res = BuildValidatedOxeScript(mode);
            if (res == null) return;

            string custName = _selectedCustomer != null ? _selectedCustomer.name : "";
            string label = (mode == "create") ? "생성 스크립트" : "삭제 스크립트";
            if (!res.Ok)
            {
                string firstErr = res.Errors[0];
                string joinedErr = string.Join(" / ", res.Errors.ToArray());
                AppendLog("ERR", string.Format("[PBX {0} 문법 검사 실패] {1}", label, joinedErr));
                AuditLogService.AppendError(_auditLogDir, "PBX", mode == "delete" ? "Delete" : "Create", joinedErr, custName);
                PopupDialogs.ShowSyntaxToast(this, false, "문법 검사 실패 · 실행 중단", firstErr);
                return;
            }

            if (mode == "delete")
            {
                string delDetail = string.Format(
                    "대상: 내선 {0}건 / CTI {1}건\n전송/복사 대상: PBX 삭제 스크립트",
                    res.DnList.Count,
                    res.CtiList.Count);
                if (!PopupDialogs.ConfirmDelete(this, "PBX 삭제 확인", "PBX 삭제 스크립트 실행 확인", delDetail))
                {
                    AppendLog("WARN", "[PBX 삭제] 사용자에 의해 취소되었습니다.");
                    return;
                }
            }

            _lastOxeScript = res.Script;
            _lastOxeAction = mode;

            bool sentToTerminal = false;
            string sendDetail = "NO_WEBVIEW";

            sendDetail = await TryPasteToLightGateTerminalAsync(res.Script);
            sentToTerminal = string.Equals(sendDetail, "PASTED", StringComparison.Ordinal);

            string summary = string.Format("{0} ({1}줄 · 내선 {2} / CTI {3}건)", label, res.LineCount, res.DnList.Count, res.CtiList.Count);
            if (sentToTerminal)
            {
                AppendLog("OK", string.Format("[PBX] 터미널 전송 완료 ({0})", summary));
                PopupDialogs.ShowSyntaxToast(this, true, "문법 검사 통과 · LightGate 전송 완료", summary);
            }
            else if (copyFallbackOnUnavail)
            {
                Clipboard.SetText(res.Script);
                AppendLog("OK", string.Format("[PBX] 클립보드 복사 완료 ({0})", summary));
                PopupDialogs.ShowSyntaxToast(this, true, "터미널 미활성 · 클립보드 복사 완료", summary);
            }
            else
            {
                Clipboard.SetText(res.Script);
                AppendLog("WARN", "[PBX] 터미널 미활성 -> 클립보드 복사");
                PopupDialogs.ShowSyntaxToast(this, false, "활성 터미널 없음 · 클립보드 복사됨", "LightGate에서 서버(예: PBX_A)를 먼저 연 뒤 다시 전송하거나 붙여넣기하세요.");
            }

            string nodeVal = (_settings.OxeNodeNumber > 0 ? _settings.OxeNodeNumber : 1).ToString();
            string delivery = sentToTerminal ? "SSH 전송" : "클립보드 복사";
            AuditLogService.AppendPbxObjects(
                _auditLogDir,
                mode == "delete" ? "DELETE" : "CREATE",
                res.DnList,
                res.CtiList,
                custName,
                nodeVal,
                mode == "create" ? TxtEntityNo.Text.Trim() : null,
                mode == "create" ? TxtAgentPg.Text.Trim() : null,
                mode == "create" ? TxtProfExt.Text.Trim() : null,
                mode == "create" ? TxtProfLogin.Text.Trim() : null,
                delivery);
        }

        private async void BtnOxeCreateSendOrCopy_Click(object sender, RoutedEventArgs e)
        {
            await SendOrCopyOxeScriptAsync("create", true);
        }

        private async void BtnOxeDeleteSendOrCopy_Click(object sender, RoutedEventArgs e)
        {
            await SendOrCopyOxeScriptAsync("delete", true);
        }

        private void BtnOxeSaveSh_Click(object sender, RoutedEventArgs e)
        {
            OxeBuildResult res = BuildValidatedOxeScript("create");
            if (res == null) return;
            string custName = _selectedCustomer != null ? _selectedCustomer.name : "";
            if (!res.Ok)
            {
                AppendLog("ERR", "[PBX .sh 저장 중단] " + res.Errors[0]);
                AuditLogService.AppendError(_auditLogDir, "PBX", "ExportSh", string.Join(" / ", res.Errors.ToArray()), custName);
                PopupDialogs.ShowSyntaxToast(this, false, "문법 검사 실패 · 저장 취소됨", res.Errors[0]);
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Shell Script (*.sh)|*.sh|All Files (*.*)|*.*";
            sfd.FileName = string.Format("pbx_{0}_{1}.sh", _lastOxeAction, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            if (sfd.ShowDialog(this) == true)
            {
                string lfText = res.Script.Replace("\r\n", "\n");
                File.WriteAllText(sfd.FileName, lfText, new UTF8Encoding(false));
                AppendLog("OK", "[PBX] .sh 저장 완료: " + sfd.FileName);
                string nodeVal = (_settings.OxeNodeNumber > 0 ? _settings.OxeNodeNumber : 1).ToString();
                AuditLogService.AppendPbxObjects(
                    _auditLogDir,
                    "EXPORT_SH",
                    res.DnList,
                    res.CtiList,
                    custName,
                    nodeVal,
                    TxtEntityNo.Text.Trim(),
                    TxtAgentPg.Text.Trim(),
                    TxtProfExt.Text.Trim(),
                    TxtProfLogin.Text.Trim(),
                    "sh 저장(" + Path.GetFileName(sfd.FileName) + ")");
                PopupDialogs.ShowSyntaxToast(this, true, ".sh 저장 완료", Path.GetFileName(sfd.FileName) + " (UNIX LF)");
            }
        }

        // ==================== CONFIG SERVER BACKGROUND CONNECT ====================
        private async Task ConnectConfigBackgroundAsync(bool isAuto)
        {
            if (_isCmBusy) return;
            try
            {
                CmConnectResponse conn = await _cmWorker.ConnectDirectAsync(
                    (_settings.CmUserId ?? "").Trim(),
                    SettingsService.UnprotectString(_settings.CmPasswordEnc));
                if (conn != null && conn.connected)
                {
                    string epNote = string.IsNullOrEmpty(conn.activeEndpoint) ? "" : (" (" + conn.activeEndpoint + ")");
                    AppendLog("OK", "Genesys Config Server 연결 완료" + epNote);
                }
                else
                {
                    string msg = (conn != null && !string.IsNullOrEmpty(conn.message)) ? conn.message : "Config Server 연결 대기 중";
                    AppendLog("WARN", "Genesys Config Server 초기 연결 확인 필요: " + msg);
                }
            }
            catch (Exception ex)
            {
                AppendLog("WARN", "Config Server 백그라운드 연결 실패: " + ex.Message);
                if (!isAuto)
                {
                    PopupDialogs.ShowSyntaxToast(this, false, "Config Server 연결 실패", "설정의 Config Server 정보 또는 Log를 확인하세요.");
                }
            }
        }

        private static void ApplyConnPillVisual(Button btn, bool connected, string text)
        {
            btn.Content = text;
            btn.Tag = connected ? "Disconnect" : "Connect";
            btn.SetResourceReference(Button.BackgroundProperty, connected ? "OkBgBrush" : "SurfaceAltBrush");
            btn.SetResourceReference(Button.BorderBrushProperty, connected ? "OkBorderBrush" : "BorderBrush");
            btn.SetResourceReference(Button.ForegroundProperty, connected ? "OkFgBrush" : "MutedBrush");
        }

        private void SetEmsTerminalState(bool connected, string statusText = null)
        {
            _isEmsTerminalConnected = connected;
            string label = statusText ?? (connected ? "터미널 활성" : "터미널 미연결");
            ApplyConnPillVisual(BtnEmsTerminalToggle, connected, label);
        }

        private void SetEmsActionBusy(bool busy, string tempText = null)
        {
            _isEmsActionBusy = busy;
            BtnEmsTerminalToggle.IsEnabled = !busy;
            BtnEmsRestartTerminal.IsEnabled = !busy;
            if (busy && !string.IsNullOrEmpty(tempText))
            {
                BtnEmsTerminalToggle.Content = tempText;
            }
        }

        private void SetPbxCheckBusy(bool busy)
        {
            _isPbxCheckBusy = busy;
            BtnOxeCheck.IsEnabled = !busy;
            BtnOxeCheck.Content = busy ? "검색 중..." : "내선/CTI ID 검색";
            BtnOxeCreateSendOrCopy.IsEnabled = !busy;
            BtnOxeDeleteSendOrCopy.IsEnabled = !busy;
        }

        private async Task<string> TryPasteToLightGateTerminalAsync(string payload)
        {
            if (!_webViewReady || EmsWebView.CoreWebView2 == null)
            {
                return "NO_WEBVIEW";
            }

            JavaScriptSerializer ser = new JavaScriptSerializer();
            string payloadJson = ser.Serialize(payload ?? "");
            string js = @"
(function(payload){
  var ta = document.querySelector('.xterm-helper-textarea');
  if (!ta) return 'NO_TERMINAL';
  ta.focus();
  try {
    var dt = new DataTransfer();
    dt.setData('text/plain', payload);
    var ev = new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true });
    ta.dispatchEvent(ev);
    return 'PASTED';
  } catch (e) {
    return 'PASTE_ERR:' + e.message;
  }
})(" + payloadJson + ");";

            try
            {
                string raw = await EmsWebView.CoreWebView2.ExecuteScriptAsync(js);
                string decoded = ser.Deserialize<string>(raw);
                return string.IsNullOrEmpty(decoded) ? (raw ?? "") : decoded;
            }
            catch (Exception ex)
            {
                return "PASTE_ERR:" + ex.Message;
            }
        }

        private async Task<string> ReadLightGateTerminalTextAsync()
        {
            if (!_webViewReady || EmsWebView.CoreWebView2 == null) return "";
            string js = @"
(function(){
  try {
    var ta = document.querySelector('.xterm-helper-textarea');
    var root = (ta && ta.closest) ? ta.closest('.xterm') : null;
    var scope = root || document;
    var rows = Array.prototype.slice.call(scope.querySelectorAll('.xterm-rows > div'));
    return rows.map(function(d){
      return (d.textContent || '').replace(/\u00A0/g, ' ').replace(/\s+$/, '');
    }).join('');
  } catch (e) {
    return '';
  }
})();";
            try
            {
                string raw = await EmsWebView.CoreWebView2.ExecuteScriptAsync(js);
                if (string.IsNullOrEmpty(raw) || raw == "null") return "";
                string decoded = new JavaScriptSerializer().Deserialize<string>(raw);
                return decoded ?? "";
            }
            catch
            {
                return "";
            }
        }

        private async Task<bool> IsLightGateLoggedInAsync(string serverKeyword)
        {
            if (!_webViewReady || EmsWebView.CoreWebView2 == null) return false;
            string kwJson = new JavaScriptSerializer().Serialize(serverKeyword ?? "");
            string js = @"
(function(kw){
  try {
    if (document.querySelector('input[type=""password""]')) return 'LOGIN_FORM';
    if (document.querySelector('.xterm')) return 'LOGGED_IN';
    var txt = (document.body && document.body.innerText) ? document.body.innerText : '';
    if ((kw && txt.indexOf(kw) >= 0) || txt.indexOf('서버') >= 0 || txt.indexOf('SSH') >= 0) return 'LOGGED_IN';
    return 'WAIT';
  } catch (e) {
    return 'ERR';
  }
})(" + kwJson + ");";
            try
            {
                string raw = await EmsWebView.CoreWebView2.ExecuteScriptAsync(js);
                return raw != null && raw.Contains("LOGGED_IN");
            }
            catch
            {
                return false;
            }
        }

        public static bool ParsePbxCheckMarkers(
            string screenText,
            string currentToken,
            Dictionary<string, PbxCheckItem> parsed,
            List<CustomerInfo> customers)
        {
            string compact = (screenText ?? "").Replace("\r", "").Replace("\n", "");
            if (compact.Length == 0) return false;

            int scanStart = 0;
            if (!string.IsNullOrEmpty(currentToken))
            {
                MatchCollection endMatches = Regex.Matches(compact, @"@@PX_END\|([A-Za-z0-9_]+)@@");
                for (int i = 0; i < endMatches.Count; i++)
                {
                    string tok = endMatches[i].Groups[1].Value;
                    if (!string.Equals(tok, currentToken, StringComparison.Ordinal))
                    {
                        scanStart = endMatches[i].Index + endMatches[i].Length;
                    }
                }
            }

            string activeSlice = (scanStart > 0 && scanStart < compact.Length)
                ? compact.Substring(scanStart)
                : (scanStart >= compact.Length ? "" : compact);

            if (activeSlice.Length == 0) return false;

            if (parsed != null)
            {
                MatchCollection ms = Regex.Matches(activeSlice, @"@@PX\|([EC])\|(\d+)\|([01])\|([^|@]*)\|([^|@]*)@@");
                for (int i = 0; i < ms.Count; i++)
                {
                    Match m = ms[i];
                    string kind = m.Groups[1].Value;
                    string num = m.Groups[2].Value;
                    bool exists = (m.Groups[3].Value == "1");
                    string ent = (m.Groups[4].Value ?? "").Trim();
                    string pgRaw = (m.Groups[5].Value ?? "").Trim();

                    List<string> pgs = new List<string>();
                    if (!string.IsNullOrEmpty(pgRaw))
                    {
                        string[] parts = pgRaw.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int p = 0; p < parts.Length; p++)
                        {
                            string pg = parts[p].Trim();
                            if (pg.Length > 0 && !pgs.Contains(pg)) pgs.Add(pg);
                        }
                    }

                    PbxCheckItem item = new PbxCheckItem();
                    item.Kind = kind;
                    item.Number = num;
                    item.Exists = exists;
                    item.EntityNo = ent;
                    item.Pgs = pgs;
                    item.CustomerName = exists ? OxeScriptService.ResolveCenterName(customers, ent, pgs) : "";
                    parsed[kind + "|" + num] = item;
                }
            }

            return !string.IsNullOrEmpty(currentToken)
                && activeSlice.IndexOf("@@PX_END|" + currentToken + "@@", StringComparison.Ordinal) >= 0;
        }

        private async void BtnOxeCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_isPbxCheckBusy) return;

            NumberParseResult dnRes = OxeScriptService.ParseNumbersStrict(TxtDn.Text, "3", 5, "내선번호");
            NumberParseResult ctiRes = OxeScriptService.ParseNumbersStrict(TxtCti.Text, "7", 5, "CTI");

            if (dnRes.Errors.Count > 0)
            {
                AuditLogService.AppendError(_auditLogDir, "PBX", "Check", dnRes.Errors[0], _selectedCustomer != null ? _selectedCustomer.name : null);
                PopupDialogs.ShowSyntaxToast(this, false, "내선번호 형식 오류", dnRes.Errors[0]);
                return;
            }
            if (ctiRes.Errors.Count > 0)
            {
                AuditLogService.AppendError(_auditLogDir, "PBX", "Check", ctiRes.Errors[0], _selectedCustomer != null ? _selectedCustomer.name : null);
                PopupDialogs.ShowSyntaxToast(this, false, "CTI 형식 오류", ctiRes.Errors[0]);
                return;
            }
            if (dnRes.Items.Count == 0 && ctiRes.Items.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(this, false, "입력 값 없음", "내선번호(3XXXX) 또는 CTI 번호(7XXXX)를 입력하세요.");
                return;
            }
            if (!_webViewReady || EmsWebView.CoreWebView2 == null)
            {
                AuditLogService.AppendError(_auditLogDir, "PBX", "Check", "LightGate PBX 터미널 미연결", _selectedCustomer != null ? _selectedCustomer.name : null);
                PopupDialogs.ShowSyntaxToast(this, false, "터미널 미연결", "LightGate에서 PBX 터미널을 먼저 실행하세요.");
                return;
            }

            string token = "P" + Guid.NewGuid().ToString("N").Substring(0, 10);
            string cmd = OxeScriptService.BuildPbxDuplicateCheckCommand(dnRes.Items, ctiRes.Items, _settings.OxeNodeNumber, token);
            string custName = _selectedCustomer != null ? _selectedCustomer.name : null;

            SetPbxCheckBusy(true);
            try
            {
                string pasteRes = await TryPasteToLightGateTerminalAsync(cmd);
                if (!string.Equals(pasteRes, "PASTED", StringComparison.Ordinal))
                {
                    AuditLogService.AppendError(_auditLogDir, "PBX", "Check", "활성 LightGate PBX 터미널 창 미발견 (" + pasteRes + ")", custName);
                    PopupDialogs.ShowSyntaxToast(this, false, "터미널 미연결", "활성 LightGate PBX 터미널 창을 찾을 수 없습니다.");
                    AppendLog("WARN", "[PBX 내선/CTI ID 검색] LightGate 터미널 미활성");
                    return;
                }

                AppendLog("INFO", string.Format("[PBX 내선/CTI ID 검색] 검색 시작 (내선 {0} / CTI {1})", dnRes.Items.Count, ctiRes.Items.Count));

                Dictionary<string, PbxCheckItem> parsed = new Dictionary<string, PbxCheckItem>(StringComparer.Ordinal);
                int totalExpected = dnRes.Items.Count + ctiRes.Items.Count;
                int timeoutMs = Math.Min(60000, Math.Max(20000, totalExpected * 900));
                int elapsed = 0;
                bool done = false;

                while (elapsed < timeoutMs)
                {
                    await Task.Delay(65);
                    elapsed += 65;

                    string screen = await ReadLightGateTerminalTextAsync();
                    if (ParsePbxCheckMarkers(screen, token, parsed, _allCustomers))
                    {
                        done = true;
                        break;
                    }
                }

                if (!done)
                {
                    AppendLog("WARN", "[PBX 내선/CTI ID 검색] 터미널 응답 시간 초과");
                    AuditLogService.AppendError(_auditLogDir, "PBX", "Check", "LightGate PBX 터미널 응답 시간 초과", custName);
                    PopupDialogs.ShowSyntaxToast(this, false, "PBX 내선/CTI ID 검색 시간 초과", "LightGate PBX 터미널 응답을 확인하세요.");
                    return;
                }

                List<PbxCheckItem> ordered = new List<PbxCheckItem>();
                int missingCount = 0;
                int dupCount = 0;

                for (int i = 0; i < dnRes.Items.Count; i++)
                {
                    PbxCheckItem item;
                    if (parsed.TryGetValue("E|" + dnRes.Items[i], out item))
                    {
                        ordered.Add(item);
                        if (item.Exists) dupCount++;
                    }
                    else
                    {
                        missingCount++;
                    }
                }
                for (int i = 0; i < ctiRes.Items.Count; i++)
                {
                    PbxCheckItem item;
                    if (parsed.TryGetValue("C|" + ctiRes.Items[i], out item))
                    {
                        ordered.Add(item);
                        if (item.Exists) dupCount++;
                    }
                    else
                    {
                        missingCount++;
                    }
                }

                if (missingCount > 0)
                {
                    string missMsg = string.Format("일부 번호 조회 결과 누락 ({0}/{1}건 미수신)", missingCount, totalExpected);
                    AppendLog("ERR", string.Format("[PBX 내선/CTI ID 검색] 결과 누락 ({0}/{1}건 미수신)", missingCount, totalExpected));
                    AuditLogService.AppendError(_auditLogDir, "PBX", "Check", missMsg, custName);
                    PopupDialogs.ShowSyntaxToast(this, false, "PBX 내선/CTI ID 검색 불완전", "일부 번호의 조회 결과를 수신하지 못했습니다. 터미널 프롬프트 상태를 확인하세요.");
                    return;
                }

                if (dupCount == 0)
                {
                    AppendLog("OK", string.Format("[PBX 내선/CTI ID 검색] 전체 미사용 (내선 {0} / CTI {1})", dnRes.Items.Count, ctiRes.Items.Count));
                }
                else
                {
                    AppendLog("WARN", string.Format("[PBX 내선/CTI ID 검색] 사용 중 {0}건 발견", dupCount));
                }

                PbxCheckSummary summary = new PbxCheckSummary();
                summary.SelectedCustomer = (_selectedCustomer != null) ? _selectedCustomer.name : "";
                summary.Items = ordered;
                summary.DnList = dnRes.Items;
                summary.CtiList = ctiRes.Items;
                PopupDialogs.ShowPbxCheckResult(this, summary);
            }
            catch (Exception ex)
            {
                AppendLog("ERR", "[PBX 내선/CTI ID 검색 실패] " + ex.Message);
                AuditLogService.AppendError(_auditLogDir, "PBX", "Check", ex.Message, custName);
                PopupDialogs.ShowSyntaxToast(this, false, "PBX 내선/CTI ID 검색 오류", ex.Message);
            }
            finally
            {
                SetPbxCheckBusy(false);
            }
        }

        private bool ValidateInputsForCm(bool requireCustomer, out NumberParseResult dnRes, out NumberParseResult ctiRes)
        {
            dnRes = OxeScriptService.ParseNumbersStrict(TxtDn.Text, "3", 5, "내선번호");
            ctiRes = OxeScriptService.ParseNumbersStrict(TxtCti.Text, "7", 5, "CTI");

            if (requireCustomer && _selectedCustomer == null)
            {
                PopupDialogs.ShowSyntaxToast(this, false, "대상 미선택", "대상 고객사를 먼저 선택하세요.");
                return false;
            }
            if (dnRes.Errors.Count > 0)
            {
                PopupDialogs.ShowSyntaxToast(this, false, "내선번호 형식 오류", dnRes.Errors[0]);
                return false;
            }
            if (ctiRes.Errors.Count > 0)
            {
                PopupDialogs.ShowSyntaxToast(this, false, "CTI 형식 오류", ctiRes.Errors[0]);
                return false;
            }

            bool hasDnItems = dnRes.Items.Count > 0;
            bool hasCtiItems = ctiRes.Items.Count > 0;
            if (!hasDnItems && !hasCtiItems)
            {
                PopupDialogs.ShowSyntaxToast(this, false, "입력 값 없음", "내선번호(3XXXX) 또는 CTI 번호(7XXXX)를 입력하세요.");
                return false;
            }

            bool needDn = hasDnItems && (IsDnChecked() || IsPlaceChecked());
            bool needCti = hasCtiItems && (IsLoginChecked() || IsAcdChecked());

            if (!needCti && !needDn)
            {
                PopupDialogs.ShowSyntaxToast(this, false, "선택 항목 없음", "입력된 번호에 해당하는 Config 항목을 하나 이상 선택하세요.");
                return false;
            }
            return true;
        }

        private CmWorkerRequest BuildCmRequest(string action, NumberParseResult dnRes, NumberParseResult ctiRes)
        {
            bool hasDn = dnRes != null && dnRes.Items.Count > 0;
            bool hasCti = ctiRes != null && ctiRes.Items.Count > 0;
            bool runDn = hasDn && IsDnChecked();
            bool runPlace = hasDn && IsPlaceChecked();
            bool runLogin = hasCti && IsLoginChecked();
            bool runAcd = hasCti && IsAcdChecked();

            CmWorkerRequest req = new CmWorkerRequest();
            req.Action = action;
            req.CustomerName = string.Equals(action, "Check", StringComparison.OrdinalIgnoreCase)
                ? ""
                : ((_selectedCustomer != null) ? _selectedCustomer.name : "");
            req.DnSpec = (runDn || runPlace) ? TxtDn.Text : "";
            req.CtiSpec = (runLogin || runAcd) ? TxtCti.Text : "";
            req.CmUserId = _settings.CmUserId;
            req.CmPasswordPlain = SettingsService.UnprotectString(_settings.CmPasswordEnc);
            req.DoAgentLogin = runLogin;
            req.DoAcdPosition = runAcd;
            req.DoAgentDn = runDn;
            req.DoPlace = runPlace;
            return req;
        }

        private async void BtnCmCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_isCmBusy) return;
            NumberParseResult dnRes, ctiRes;
            if (!ValidateInputsForCm(false, out dnRes, out ctiRes)) return;

            CmWorkerRequest req = BuildCmRequest("Check", dnRes, ctiRes);
            int targetDn = (req.DoAgentDn || req.DoPlace) ? dnRes.Items.Count : 0;
            int targetCti = (req.DoAgentLogin || req.DoAcdPosition) ? ctiRes.Items.Count : 0;
            AppendLog("INFO", string.Format("[Config 내선/CTI ID 검색] 검색 시작 (내선 {0} / CTI {1})", targetDn, targetCti));

            string checkJsonRaw = null;
            int exit = await ExecuteCmWorkerAsync(req, "Config 내선/CTI ID 검색 중...", delegate(string rawLine)
            {
                if (rawLine.StartsWith("##CM_CHECK_JSON##", StringComparison.Ordinal))
                {
                    checkJsonRaw = rawLine.Substring("##CM_CHECK_JSON##".Length);
                }
            });

            if (exit == 0)
            {
                CmCheckSummary summary = ParseCmCheckSummary(checkJsonRaw);
                if (summary != null)
                {
                    PopupDialogs.ShowCmCheckResult(this, summary);
                }
                else
                {
                    AuditLogService.AppendError(_auditLogDir, "Config", "Batch:Check", "Config 내선/CTI ID 검색 결과를 파싱할 수 없습니다.");
                    PopupDialogs.ShowSyntaxToast(this, true, "Config 내선/CTI ID 검색 완료", "폴더 및 검색 결과를 Log에서 확인하세요.");
                }
            }
            else
            {
                PopupDialogs.ShowSyntaxToast(this, false, "Config 내선/CTI ID 검색 오류", "Log의 오류 내용을 확인하세요.");
            }
        }

        private static CmCheckSummary ParseCmCheckSummary(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                return ser.Deserialize<CmCheckSummary>(json);
            }
            catch
            {
                return null;
            }
        }

        private async void BtnCmCreate_Click(object sender, RoutedEventArgs e)
        {
            if (_isCmBusy) return;
            NumberParseResult dnRes, ctiRes;
            if (!ValidateInputsForCm(true, out dnRes, out ctiRes)) return;

            CmWorkerRequest req = BuildCmRequest("Create", dnRes, ctiRes);
            if ((req.DoAgentDn || req.DoPlace) && !CheckLargeDnBeforeCreate(dnRes.Items))
            {
                AppendLog("WARN", "[Object 생성] 사용자에 의해 취소되었습니다.");
                return;
            }

            int targetDn = (req.DoAgentDn || req.DoPlace) ? dnRes.Items.Count : 0;
            int targetCti = (req.DoAgentLogin || req.DoAcdPosition) ? ctiRes.Items.Count : 0;
            string startMsg = string.Format("[Object 생성] '{0}' 시작 (내선 {1} / CTI {2})", _selectedCustomer.name, targetDn, targetCti);
            AppendLog("INFO", startMsg);

            int exit = await ExecuteCmWorkerAsync(req, "Object 생성 중...");
            if (exit == 0)
            {
                PopupDialogs.ShowSyntaxToast(this, true, "Object 생성 완료", "요청된 Config Object 생성이 완료되었습니다.");
            }
            else
            {
                PopupDialogs.ShowSyntaxToast(this, false, "Object 생성 오류", "Log의 오류 내용을 확인하세요.");
            }
        }

        private async void BtnCmDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_isCmBusy) return;
            NumberParseResult dnRes, ctiRes;
            if (!ValidateInputsForCm(false, out dnRes, out ctiRes)) return;

            CmWorkerRequest req = BuildCmRequest("Delete", dnRes, ctiRes);
            int targetDn = (req.DoAgentDn || req.DoPlace) ? dnRes.Items.Count : 0;
            int targetCti = (req.DoAgentLogin || req.DoAcdPosition) ? ctiRes.Items.Count : 0;
            string custLabel = (_selectedCustomer != null) ? _selectedCustomer.name : "센터 무관 (Config 전체 검색 삭제)";
            string cmDelDetail = string.Format(
                "삭제 방식: Config 전체 검색 삭제 ({0})\n대상: 내선 {1}건 / CTI {2}건",
                custLabel,
                targetDn,
                targetCti);
            if (!PopupDialogs.ConfirmDelete(this, "Object 삭제 확인", "Config Object 삭제 확인", cmDelDetail))
            {
                AppendLog("WARN", "[Object 삭제] 사용자에 의해 취소되었습니다.");
                return;
            }

            AppendLog("WARN", string.Format("[Object 삭제] 시작 ({0} · 내선 {1} / CTI {2})", custLabel, targetDn, targetCti));

            int exit = await ExecuteCmWorkerAsync(req, "Object 삭제 중...");
            if (exit == 0)
            {
                PopupDialogs.ShowSyntaxToast(this, true, "Object 삭제 완료", "요청된 Config Object 삭제가 완료되었습니다.");
            }
            else
            {
                PopupDialogs.ShowSyntaxToast(this, false, "Object 삭제 오류", "Log의 오류 내용을 확인하세요.");
            }
        }

        private async Task<int> ExecuteCmWorkerAsync(CmWorkerRequest req, string busyMsg, Action<string> onControlLine = null)
        {
            SetCmButtonsEnabled(false);
            bool psErrorAudited = false;
            string lastPsErr = null;
            string actionLabel = (req != null && !string.IsNullOrWhiteSpace(req.Action))
                ? (string.Equals(req.Action, "Connect", StringComparison.OrdinalIgnoreCase) ? "Connect" : "Batch:" + req.Action)
                : "Worker";
            try
            {
                int exit = await _cmWorker.RunAsync(req, delegate(string line)
                {
                    bool isAuditErr;
                    if (AuditLogService.TryWriteFromWorkerLine(_auditLogDir, line, out isAuditErr))
                    {
                        if (isAuditErr) psErrorAudited = true;
                        return;
                    }
                    if (line != null && line.StartsWith("##CM_CHECK_JSON##", StringComparison.Ordinal))
                    {
                        if (onControlLine != null) onControlLine(line);
                        return;
                    }
                    if (line != null && line.StartsWith("[PS-ERR]", StringComparison.Ordinal))
                    {
                        lastPsErr = line;
                    }
                    Dispatcher.BeginInvoke(new Action(delegate()
                    {
                        if (TxtLog.Text.Length > 0) TxtLog.AppendText(Environment.NewLine);
                        TxtLog.AppendText(line);
                        TxtLog.ScrollToEnd();
                    }));
                });
                if (exit != 0 && !psErrorAudited)
                {
                    AuditLogService.AppendError(
                        _auditLogDir,
                        "Config",
                        actionLabel,
                        !string.IsNullOrWhiteSpace(lastPsErr) ? lastPsErr : string.Format("Config 작업 비정상 종료 (exit={0})", exit),
                        req != null ? req.CustomerName : null);
                }
                return exit;
            }
            catch (Exception ex)
            {
                if (!psErrorAudited)
                {
                    AuditLogService.AppendError(_auditLogDir, "Config", actionLabel, ex.Message, req != null ? req.CustomerName : null);
                }
                throw;
            }
            finally
            {
                SetCmButtonsEnabled(true);
            }
        }

        private void SetCmButtonsEnabled(bool enabled)
        {
            _isCmBusy = !enabled;
            BtnCmCheck.IsEnabled = enabled;
            BtnCmCreate.IsEnabled = enabled;
            BtnCmDelete.IsEnabled = enabled;
            bool hasCustomer = (_selectedCustomer != null);
            if (ChkCenterVQueue != null) ChkCenterVQueue.IsEnabled = enabled && hasCustomer;
            if (ChkCenterIvr != null) ChkCenterIvr.IsEnabled = enabled && hasCustomer;
            if (BtnCheckCenterOpts != null) BtnCheckCenterOpts.IsEnabled = enabled && hasCustomer;
            if (BtnApplyCenterOpts != null) BtnApplyCenterOpts.IsEnabled = enabled && hasCustomer;
            if (PersonTabControl != null)
            {
                PersonTabControl.SetBusyState(!enabled);
            }
            if (DialPlanTabControl != null)
                DialPlanTabControl.IsEnabled = enabled;
        }

        // ==================== PERSON TAB CONFIG HANDLERS ====================
        private void EnsureCmReadyForPerson()
        {
            if (_isCmBusy)
            {
                throw new InvalidOperationException("다른 Config 작업이 진행 중입니다. 완료 후 다시 시도하세요.");
            }
        }

        private CmPersonWorkerRequest BuildPersonWorkerRequest(string action, CustomerInfo customer, List<PersonPreviewRow> rows, bool doGeneral, bool doAgentLogin)
        {
            CmPersonWorkerRequest req = new CmPersonWorkerRequest();
            req.Action = action;
            req.CustomerName = (customer != null) ? customer.name : "";
            req.CmUserId = _settings.CmUserId;
            req.CmPasswordPlain = SettingsService.UnprotectString(_settings.CmPasswordEnc);
            req.DoGeneral = doGeneral;
            req.DoAgentLogin = doAgentLogin;
            if (rows != null)
            {
                req.Items = new List<PersonBatchPayloadItem>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    PersonPreviewRow r = rows[i];
                    PersonBatchPayloadItem item = new PersonBatchPayloadItem();
                    item.UserName = r.UserName ?? "";
                    item.FirstName = r.FirstName ?? "";
                    item.LastName = r.LastName ?? "";
                    item.EmployeeId = r.EmployeeId ?? "";
                    item.IsAgent = r.IsAgent;
                    item.AgentLogin = r.AgentLoginId ?? "";
                    req.Items.Add(item);
                }
            }
            return req;
        }

        private async Task<int> ExecutePersonWorkerAsync(
            CmPersonWorkerRequest req,
            string busyMsg,
            Action<string> onControlLine)
        {
            SetCmButtonsEnabled(false);
            bool psErrorAudited = false;
            string lastPsErr = null;
            string opLabel = "Person:" + ((req != null && !string.IsNullOrWhiteSpace(req.Action)) ? req.Action : "Worker");
            try
            {
                int exit = await _cmWorker.RunPersonAsync(req, delegate(string line)
                {
                    bool isAuditErr;
                    if (AuditLogService.TryWriteFromWorkerLine(_auditLogDir, line, out isAuditErr))
                    {
                        if (isAuditErr) psErrorAudited = true;
                        return;
                    }
                    if (line != null && (line.StartsWith("##PERSON_LOGINS_JSON##", StringComparison.Ordinal)
                        || line.StartsWith("##PERSON_CHECK_JSON##", StringComparison.Ordinal)
                        || line.StartsWith("##PERSON_CREATE_JSON##", StringComparison.Ordinal)
                        || line.StartsWith("##PERSON_LIST_JSON##", StringComparison.Ordinal)
                        || line.StartsWith("##PERSON_UPDATE_JSON##", StringComparison.Ordinal)
                        || line.StartsWith("##PERSON_DELETE_JSON##", StringComparison.Ordinal)))
                    {
                        if (onControlLine != null) onControlLine(line);
                        return;
                    }
                    if (line != null && line.StartsWith("[PS-ERR]", StringComparison.Ordinal))
                    {
                        lastPsErr = line;
                    }
                    if (PersonTabControl != null)
                    {
                        PersonTabControl.AppendExternalLog(line);
                    }
                });
                if (exit != 0 && !psErrorAudited)
                {
                    AuditLogService.AppendError(
                        _auditLogDir,
                        "Config",
                        opLabel,
                        !string.IsNullOrWhiteSpace(lastPsErr) ? lastPsErr : string.Format("Person 작업 비정상 종료 (exit={0})", exit),
                        req != null ? req.CustomerName : null);
                }
                return exit;
            }
            catch (Exception ex)
            {
                if (!psErrorAudited)
                {
                    AuditLogService.AppendError(_auditLogDir, "Config", opLabel, ex.Message, req != null ? req.CustomerName : null);
                }
                throw;
            }
            finally
            {
                SetCmButtonsEnabled(true);
            }
        }

        private static T DeserializeJsonPayload<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 16 * 1024 * 1024;
                return ser.Deserialize<T>(json);
            }
            catch
            {
                return null;
            }
        }

        private async Task<List<AgentLoginPoolItem>> HandlePersonFetchLoginPoolAsync(CustomerInfo customer)
        {
            EnsureCmReadyForPerson();
            if (customer == null) throw new InvalidOperationException("대상 센터를 선택하세요.");

            CmPersonWorkerRequest req = BuildPersonWorkerRequest("FetchAvailableLogins", customer, null, false, false);
            PersonTabControl.AppendExternalLog(string.Format("[조회 시작] '{0}' Config Agent Login 조회 중...", customer.name));

            string jsonRaw = null;
            int exit = await ExecutePersonWorkerAsync(req, "Config Login 조회 중...", delegate(string line)
            {
                if (line.StartsWith("##PERSON_LOGINS_JSON##", StringComparison.Ordinal))
                {
                    jsonRaw = line.Substring("##PERSON_LOGINS_JSON##".Length);
                }
            });

            if (exit != 0)
            {
                throw new InvalidOperationException("Config Agent Login 조회에 실패했습니다. Log를 확인하세요.");
            }

            PersonLoginPoolResponse resp = DeserializeJsonPayload<PersonLoginPoolResponse>(jsonRaw);
            if (resp == null || resp.items == null)
            {
                AuditLogService.AppendError(_auditLogDir, "Config", "Person:FetchAvailableLogins", "Config Agent Login 조회 응답을 파싱할 수 없습니다.", customer.name);
                throw new InvalidOperationException("Config Agent Login 조회 응답을 파싱할 수 없습니다.");
            }

            List<AgentLoginPoolItem> result = new List<AgentLoginPoolItem>(resp.items.Count);
            for (int i = 0; i < resp.items.Count; i++)
            {
                PersonLoginPoolDto d = resp.items[i];
                AgentLoginPoolItem item = new AgentLoginPoolItem();
                item.LoginId = d.loginId ?? "";
                item.IsInUseInCm = !d.isAvailable;
                item.InUseOwner = d.usedBy ?? "";
                result.Add(item);
            }

            PopupDialogs.ShowSyntaxToast(
                GetActiveDialogOwner(),
                true,
                "Agent Login 조회 완료",
                string.Format("'{0}' 전체 {1}건 중 사용 가능 {2}건 / 사용 중 {3}건을 불러왔습니다.",
                    customer.name, resp.totalCount, resp.availableCount, resp.totalCount - resp.availableCount));
            return result;
        }

        private Window GetActiveDialogOwner()
        {
            for (int i = OwnedWindows.Count - 1; i >= 0; i--)
            {
                if (OwnedWindows[i].IsVisible && OwnedWindows[i].IsActive) return OwnedWindows[i];
            }
            for (int i = OwnedWindows.Count - 1; i >= 0; i--)
            {
                if (OwnedWindows[i].IsVisible) return OwnedWindows[i];
            }
            return this;
        }

        private async Task HandlePersonDuplicateCheckAsync(CustomerInfo customer, List<PersonPreviewRow> rows)
        {
            EnsureCmReadyForPerson();
            CmPersonWorkerRequest req = BuildPersonWorkerRequest("CheckPersons", customer, rows, false, false);
            PersonTabControl.AppendExternalLog(string.Format("[중복체크] Config 전체 Person 검색 시작 (대상 {0}명)...", rows.Count));

            string jsonRaw = null;
            int exit = await ExecutePersonWorkerAsync(req, "Person 중복체크 중...", delegate(string line)
            {
                if (line.StartsWith("##PERSON_CHECK_JSON##", StringComparison.Ordinal))
                {
                    jsonRaw = line.Substring("##PERSON_CHECK_JSON##".Length);
                }
            });

            if (exit != 0)
            {
                throw new InvalidOperationException("Config Person 중복체크에 실패했습니다. Log를 확인하세요.");
            }

            PersonCheckSummary summary = DeserializeJsonPayload<PersonCheckSummary>(jsonRaw);
            if (summary == null)
            {
                AuditLogService.AppendError(_auditLogDir, "Config", "Person:CheckPersons", "Config Person 중복체크 결과를 파싱할 수 없습니다.", customer != null ? customer.name : null);
                throw new InvalidOperationException("Config Person 중복체크 결과를 파싱할 수 없습니다.");
            }

            HashSet<string> dupEmpSet = new HashSet<string>(summary.dupEmpIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            HashSet<string> dupUserSet = new HashSet<string>(summary.dupUserNames ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            PersonTabControl.ApplyDuplicateCheckStatuses(dupEmpSet, dupUserSet);

            List<string> cleanUsers = new List<string>();
            HashSet<string> seenUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows.Count; i++)
            {
                string un = (rows[i].UserName ?? "").Trim();
                if (!string.IsNullOrEmpty(un) && !dupUserSet.Contains(un) && seenUsers.Add(un))
                {
                    cleanUsers.Add(un);
                }
            }

            PopupDialogs.ShowPersonCheckResult(this, customer != null ? customer.name : "", summary, cleanUsers);
        }

        private async Task<bool> HandlePersonCreateAsync(CustomerInfo customer, List<PersonPreviewRow> rows, bool doGeneral, bool doAgentLogin)
        {
            EnsureCmReadyForPerson();
            if (customer == null) throw new InvalidOperationException("대상 센터를 선택하세요.");

            CmPersonWorkerRequest req = BuildPersonWorkerRequest("CreatePersons", customer, rows, doGeneral, doAgentLogin);
            PersonTabControl.AppendExternalLog(string.Format("[Person 생성] '{0}' {1}명 적용 시작 (General={2}, AgentLogin={3})",
                customer.name, rows.Count, doGeneral, doAgentLogin));

            string jsonRaw = null;
            int exit = await ExecutePersonWorkerAsync(req, "Config Person 생성 중...", delegate(string line)
            {
                if (line.StartsWith("##PERSON_CREATE_JSON##", StringComparison.Ordinal))
                {
                    jsonRaw = line.Substring("##PERSON_CREATE_JSON##".Length);
                }
            });

            if (exit != 0)
            {
                throw new InvalidOperationException("Config Person 일괄 생성 중 오류가 발생했습니다. Log를 확인하세요.");
            }

            PersonCreateSummary summary = DeserializeJsonPayload<PersonCreateSummary>(jsonRaw);
            if (summary == null)
            {
                AuditLogService.AppendError(_auditLogDir, "Config", "Person:Create", "Config Person 생성 결과 요약을 파싱할 수 없습니다.", customer.name);
                throw new InvalidOperationException("Config Person 생성 결과 요약을 파싱할 수 없습니다.");
            }

            HashSet<string> createdSet = new HashSet<string>(summary.createdEmpIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            PersonTabControl.ApplyCreatedStatuses(summary);

            int okCnt = createdSet.Count;
            int skipCnt = summary.skipped != null ? summary.skipped.Count : 0;
            int failCnt = summary.failed != null ? summary.failed.Count : 0;

            bool isSuccess = (failCnt == 0);
            string title = isSuccess ? "Config Person 일괄 생성 완료" : "Config Person 일괄 생성 부분 완료 (오류 확인)";
            string msg = string.Format("대상 폴더: {0}\n생성 완료: {1}건  |  중복 건너뜀: {2}건  |  실패: {3}건  (소요 {4:N1}초)",
                string.IsNullOrEmpty(summary.folderPath) ? customer.name : summary.folderPath,
                okCnt, skipCnt, failCnt, summary.elapsedSeconds);
            if (failCnt > 0 && summary.failed[0] != null)
            {
                msg += "\n첫 번째 오류: " + summary.failed[0].reason;
            }
            PopupDialogs.ShowSyntaxToast(this, isSuccess, title, msg);
            return isSuccess && okCnt > 0;
        }

        private CmPersonWorkerRequest BuildPersonManageWorkerRequest(string action, CustomerInfo customer, List<PersonManageRow> rows)
        {
            CmPersonWorkerRequest req = new CmPersonWorkerRequest();
            req.Action = action;
            req.CustomerName = (customer != null) ? customer.name : "";
            req.CmUserId = _settings.CmUserId;
            req.CmPasswordPlain = SettingsService.UnprotectString(_settings.CmPasswordEnc);
            if (rows != null)
            {
                req.ManageItems = new List<PersonManagePayloadItem>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    PersonManageRow r = rows[i];
                    PersonManagePayloadItem item = new PersonManagePayloadItem();
                    item.OriginalUserName = r.OriginalUserName ?? "";
                    item.OriginalEmployeeId = r.OriginalEmployeeId ?? "";
                    item.UserName = r.UserName ?? "";
                    item.FirstName = r.FirstName ?? "";
                    item.LastName = r.LastName ?? "";
                    item.EmployeeId = r.EmployeeId ?? "";
                    item.IsAgent = r.IsAgent;
                    item.AgentLogin = r.AgentLoginId ?? "";
                    item.UpdateGeneral = r.IsGeneralModified;
                    item.UpdateAgentLogin = r.IsAgentLoginModified;
                    req.ManageItems.Add(item);
                }
            }
            return req;
        }

        private async Task<PersonManageListResponse> HandlePersonFetchManageAsync(CustomerInfo customer)
        {
            EnsureCmReadyForPerson();
            if (customer == null) throw new InvalidOperationException("대상 센터를 선택하세요.");

            CmPersonWorkerRequest req = BuildPersonManageWorkerRequest("FetchPersonsInFolder", customer, null);
            PersonTabControl.AppendExternalLog(string.Format("[목록 조회] '{0}' Config Persons 폴더 조회 중...", customer.name));

            string jsonRaw = null;
            int exit = await ExecutePersonWorkerAsync(req, "Config Person 목록 조회 중...", delegate(string line)
            {
                if (line.StartsWith("##PERSON_LIST_JSON##", StringComparison.Ordinal))
                {
                    jsonRaw = line.Substring("##PERSON_LIST_JSON##".Length);
                }
            });

            if (exit != 0)
            {
                throw new InvalidOperationException("Config Person 목록 조회에 실패했습니다. Log를 확인하세요.");
            }

            PersonManageListResponse resp = DeserializeJsonPayload<PersonManageListResponse>(jsonRaw);
            if (resp == null || resp.items == null)
            {
                AuditLogService.AppendError(_auditLogDir, "Config", "Person:FetchPersonsInFolder", "Config Person 목록 조회 응답을 파싱할 수 없습니다.", customer.name);
                throw new InvalidOperationException("Config Person 목록 조회 응답을 파싱할 수 없습니다.");
            }

            PopupDialogs.ShowSyntaxToast(
                this,
                true,
                "Config Person 목록 조회 완료",
                string.Format("폴더: {0}\n총 {1}명의 Person을 불러왔습니다. (소요 {2:N1}초)",
                    string.IsNullOrEmpty(resp.folderPath) ? customer.name : resp.folderPath,
                    resp.totalCount,
                    resp.elapsedSeconds));
            return resp;
        }

        private async Task<bool> HandlePersonUpdateManageAsync(CustomerInfo customer, List<PersonManageRow> rows)
        {
            EnsureCmReadyForPerson();
            if (customer == null) throw new InvalidOperationException("대상 센터를 선택하세요.");

            CmPersonWorkerRequest req = BuildPersonManageWorkerRequest("UpdatePersons", customer, rows);
            PersonTabControl.AppendExternalLog(string.Format("[Person 수정] '{0}' {1}명 반영 시작...", customer.name, rows.Count));

            string jsonRaw = null;
            int exit = await ExecutePersonWorkerAsync(req, "Config Person 수정 중...", delegate(string line)
            {
                if (line.StartsWith("##PERSON_UPDATE_JSON##", StringComparison.Ordinal))
                {
                    jsonRaw = line.Substring("##PERSON_UPDATE_JSON##".Length);
                }
            });

            if (exit != 0)
            {
                throw new InvalidOperationException("Config Person 수정 반영 중 오류가 발생했습니다. Log를 확인하세요.");
            }

            PersonUpdateSummary summary = DeserializeJsonPayload<PersonUpdateSummary>(jsonRaw);
            if (summary == null)
            {
                AuditLogService.AppendError(_auditLogDir, "Config", "Person:Update", "Config Person 수정 결과 요약을 파싱할 수 없습니다.", customer.name);
                throw new InvalidOperationException("Config Person 수정 결과 요약을 파싱할 수 없습니다.");
            }

            PersonTabControl.ApplyUpdatedManageRows(summary);
            int okCnt = summary.updatedItems != null ? summary.updatedItems.Count : 0;
            int failCnt = summary.failed != null ? summary.failed.Count : 0;
            bool isSuccess = (failCnt == 0);
            string msg = string.Format("대상 폴더: {0}\n수정 완료: {1}건  |  실패: {2}건  (소요 {3:N1}초)",
                string.IsNullOrEmpty(summary.folderPath) ? customer.name : summary.folderPath,
                okCnt, failCnt, summary.elapsedSeconds);
            if (failCnt > 0 && summary.failed[0] != null)
            {
                msg += "\n첫 번째 오류: " + summary.failed[0].reason;
            }
            PopupDialogs.ShowSyntaxToast(this, isSuccess, isSuccess ? "Config Person 수정 반영 완료" : "Config Person 수정 부분 완료", msg);
            return isSuccess && okCnt > 0;
        }

        private async Task<bool> HandlePersonDeleteManageAsync(CustomerInfo customer, List<PersonManageRow> rows)
        {
            EnsureCmReadyForPerson();
            if (customer == null) throw new InvalidOperationException("대상 센터를 선택하세요.");

            int withLoginCnt = 0;
            List<string> delNames = new List<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(rows[i].OriginalAgentLoginId) || !string.IsNullOrWhiteSpace(rows[i].AgentLoginId))
                {
                    withLoginCnt++;
                }
                delNames.Add(string.Format("{0}({1})", rows[i].OriginalEmployeeId ?? rows[i].EmployeeId, rows[i].OriginalUserName ?? rows[i].UserName));
            }

            StringBuilder targetDetail = new StringBuilder();
            targetDetail.AppendLine(string.Format("대상 센터: '{0}'", customer.DisplayName));
            targetDetail.AppendLine(string.Format("삭제 인원: {0}명 (Agent Login 연결 해제 대상: {1}명)", rows.Count, withLoginCnt));
            List<string> preview = new List<string>();
            for (int i = 0; i < Math.Min(6, delNames.Count); i++) preview.Add(delNames[i]);
            targetDetail.Append("대상 목록: " + string.Join(", ", preview.ToArray()) + (delNames.Count > 6 ? string.Format(" 외 {0}명", delNames.Count - 6) : ""));

            string personDeleteWarning =
                "의도하지 않은 Config Person 삭제 시 상담원 로그인 및 센터 운영에 문제가 생길 수 있습니다.\n" +
                "연결된 Agent Login은 먼저 Delete ID로 해제 적용한 뒤 Person만 삭제합니다.\n\n" +
                "정말 삭제하시려면 '삭제한다' 를 입력해 주십시오.";

            if (!PopupDialogs.ConfirmDelete(this, "Config Person 삭제 확인", "Config Person 선택 삭제 확인", targetDetail.ToString(), personDeleteWarning))
            {
                PersonTabControl.AppendExternalLog("[Person 삭제] 사용자에 의해 취소되었습니다.");
                return false;
            }

            CmPersonWorkerRequest req = BuildPersonManageWorkerRequest("DeletePersons", customer, rows);
            PersonTabControl.AppendExternalLog(string.Format("[Person 삭제] '{0}' {1}명 삭제 시작 (Agent Login 연결 해제 우선 진행)...", customer.name, rows.Count));

            string jsonRaw = null;
            int exit = await ExecutePersonWorkerAsync(req, "Config Person 삭제 중...", delegate(string line)
            {
                if (line.StartsWith("##PERSON_DELETE_JSON##", StringComparison.Ordinal))
                {
                    jsonRaw = line.Substring("##PERSON_DELETE_JSON##".Length);
                }
            });

            if (exit != 0)
            {
                throw new InvalidOperationException("Config Person 삭제 중 오류가 발생했습니다. Log를 확인하세요.");
            }

            PersonDeleteSummary summary = DeserializeJsonPayload<PersonDeleteSummary>(jsonRaw);
            if (summary == null)
            {
                AuditLogService.AppendError(_auditLogDir, "Config", "Person:Delete", "Config Person 삭제 결과 요약을 파싱할 수 없습니다.", customer.name);
                throw new InvalidOperationException("Config Person 삭제 결과 요약을 파싱할 수 없습니다.");
            }

            PersonTabControl.ApplyDeletedManageRows(summary);
            int delCnt = summary.deletedItems != null ? summary.deletedItems.Count : 0;
            int skipCnt = summary.skipped != null ? summary.skipped.Count : 0;
            int failCnt = summary.failed != null ? summary.failed.Count : 0;
            bool isSuccess = (failCnt == 0);
            string msg = string.Format("대상 폴더: {0}\n삭제 완료: {1}건  |  건너뜀: {2}건  |  실패: {3}건  (소요 {4:N1}초)",
                string.IsNullOrEmpty(summary.folderPath) ? customer.name : summary.folderPath,
                delCnt, skipCnt, failCnt, summary.elapsedSeconds);
            if (failCnt > 0 && summary.failed[0] != null)
            {
                msg += "\n첫 번째 오류: " + summary.failed[0].reason;
            }
            PopupDialogs.ShowSyntaxToast(this, isSuccess, isSuccess ? "Config Person 삭제 완료" : "Config Person 삭제 부분 완료", msg);
            return isSuccess && delCnt > 0;
        }

        // ==================== CENTER ADD & VQUEUE / IVR OPTION HANDLERS ====================
        private void EnsureCmReadyForCenter()
        {
            if (_isCmBusy)
            {
                throw new InvalidOperationException("다른 Config 작업이 진행 중입니다. 완료 후 다시 시도하세요.");
            }
        }

        private CmCenterWorkerRequest BuildCenterWorkerRequest(string action, string centerName, string location)
        {
            CmCenterWorkerRequest req = new CmCenterWorkerRequest();
            req.Action = action;
            req.CenterName = centerName ?? "";
            req.Location = location ?? "";
            req.CmUserId = _settings.CmUserId;
            req.CmPasswordPlain = SettingsService.UnprotectString(_settings.CmPasswordEnc);
            return req;
        }

        private async Task<int> ExecuteCenterWorkerAsync(
            CmCenterWorkerRequest req,
            string busyMsg,
            bool logToCenterTab,
            Action<string> onControlLine)
        {
            SetCmButtonsEnabled(false);
            bool psErrorAudited = false;
            string lastPsErr = null;
            string opLabel = "Center:" + ((req != null && !string.IsNullOrWhiteSpace(req.Action)) ? req.Action : "Worker");

            try
            {
                int exit = await _cmWorker.RunCenterAsync(req, delegate(string line)
                {
                    bool isAuditErr;
                    if (AuditLogService.TryWriteFromWorkerLine(_auditLogDir, line, out isAuditErr))
                    {
                        if (isAuditErr) psErrorAudited = true;
                        return;
                    }
                    if (line != null && (line.StartsWith("##CENTER_CHECK_JSON##", StringComparison.Ordinal)
                        || line.StartsWith("##CENTER_CREATE_JSON##", StringComparison.Ordinal)
                        || line.StartsWith("##CENTER_STATUS_JSON##", StringComparison.Ordinal)
                        || line.StartsWith("##CENTER_APPLY_JSON##", StringComparison.Ordinal)
                        || line.StartsWith("##CENTER_DELETE_JSON##", StringComparison.Ordinal)))
                    {
                        if (onControlLine != null) onControlLine(line);
                        return;
                    }
                    if (line != null && line.StartsWith("[PS-ERR]", StringComparison.Ordinal))
                    {
                        lastPsErr = line;
                    }

                    if (logToCenterTab)
                    {
                        // Log output routed directly via active modal log view if open
                    if (_activeCenterAddPopupView != null) _activeCenterAddPopupView.AppendExternalLog(line);
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(new Action(delegate()
                        {
                            if (TxtLog.Text.Length > 0) TxtLog.AppendText(Environment.NewLine);
                            TxtLog.AppendText(line);
                            TxtLog.ScrollToEnd();
                        }));
                    }
                });

                if (exit != 0 && !psErrorAudited)
                {
                    AuditLogService.AppendError(
                        _auditLogDir,
                        "Config",
                        opLabel,
                        !string.IsNullOrWhiteSpace(lastPsErr) ? lastPsErr : string.Format("센터 Config 작업 비정상 종료 (exit={0})", exit),
                        req != null ? req.CenterName : null);
                }
                return exit;
            }
            catch (Exception ex)
            {
                if (!psErrorAudited)
                {
                    AuditLogService.AppendError(_auditLogDir, "Config", opLabel, ex.Message, req != null ? req.CenterName : null);
                }
                throw;
            }
            finally
            {
                SetCmButtonsEnabled(true);
            }
        }

        private void SaveCustomersToDiskAndRefreshAll(string preferSelectName = null)
        {
            string jsonPath = Path.Combine(_baseDir, "cm_customers.json");
            CustomerRepository repo = new CustomerRepository();
            repo.SaveAll(jsonPath, _allCustomers);

            if (!string.IsNullOrEmpty(preferSelectName))
            {
                for (int i = 0; i < _allCustomers.Count; i++)
                {
                    if (string.Equals(_allCustomers[i].name, preferSelectName, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedCustomer = _allCustomers[i];
                        break;
                    }
                }
            }

            FilterCustomers(TxtSearch.Text);
            if (PersonTabControl != null) PersonTabControl.SetCustomers(_allCustomers);
            if (_activeCenterAddPopupView != null) _activeCenterAddPopupView.SetCustomers(_allCustomers);
        }

        private async Task<CmCenterCheckResponse> HandleCenterPrecheckAsync(CenterCreateInput input)
        {
            EnsureCmReadyForCenter();
            CmCenterWorkerRequest req = BuildCenterWorkerRequest("CheckCenter", input.CenterName, input.Location);
            req.UseVQueue = input.UseVQueue;
            req.UseIvr = input.UseIvr;

            if (_activeCenterAddPopupView != null)
                _activeCenterAddPopupView.AppendExternalLog(string.Format("[Config 사전 점검] '{0}' 시작...", input.CenterName));
            string jsonRaw = null;
            int exit = await ExecuteCenterWorkerAsync(req, "센터 사전 점검 중...", true, delegate(string line)
            {
                if (line.StartsWith("##CENTER_CHECK_JSON##", StringComparison.Ordinal))
                {
                    jsonRaw = line.Substring("##CENTER_CHECK_JSON##".Length);
                }
            });

            if (exit != 0)
            {
                throw new InvalidOperationException("Config 사전 점검 중 오류가 발생했습니다. 작업 Log를 확인하세요.");
            }

            CmCenterCheckResponse res = DeserializeJsonPayload<CmCenterCheckResponse>(jsonRaw);
            if (res == null)
            {
                throw new InvalidOperationException("Config 사전 점검 응답을 파싱할 수 없습니다.");
            }
            return res;
        }

        private async Task<CenterSaveOutcome> HandleCenterCreateAsync(CenterCreateInput input)
        {
            EnsureCmReadyForCenter();
            CmCenterWorkerRequest req = BuildCenterWorkerRequest("CreateCenter", input.CenterName, input.Location);
            req.UseVQueue = input.UseVQueue;
            req.UseIvr = input.UseIvr;

            if (_activeCenterAddPopupView != null)
                _activeCenterAddPopupView.AppendExternalLog(string.Format("[Config 센터 생성] '{0}' 실행 시작...", input.CenterName));
            string jsonRaw = null;
            int exit = await ExecuteCenterWorkerAsync(req, "센터 Config 생성 중...", true, delegate(string line)
            {
                if (line.StartsWith("##CENTER_CREATE_JSON##", StringComparison.Ordinal))
                {
                    jsonRaw = line.Substring("##CENTER_CREATE_JSON##".Length);
                }
            });

            CmCenterCreateResponse cmRes = DeserializeJsonPayload<CmCenterCreateResponse>(jsonRaw);
            if (exit != 0 && cmRes == null)
            {
                throw new InvalidOperationException("센터 Config 생성 중 오류가 발생했습니다. 작업 Log를 확인하세요.");
            }

            CenterSaveOutcome outcome = new CenterSaveOutcome();
            outcome.CmResponse = cmRes;

            if (cmRes != null && (cmRes.createdCount > 0 || cmRes.skippedCount > 0))
            {
                bool hasLoc = !string.IsNullOrWhiteSpace(input.Location) && input.Location != "(위치 없음)" && input.Location != "-";
                string[] locArr = hasLoc ? new string[] { input.Location.Trim(), input.CenterName.Trim() } : new string[] { input.CenterName.Trim() };

                CustomerInfo existing = null;
                for (int i = 0; i < _allCustomers.Count; i++)
                {
                    if (string.Equals(_allCustomers[i].name, input.CenterName.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        existing = _allCustomers[i];
                        break;
                    }
                }

                CustomerInfo targetCustomer = null;
                if (existing != null)
                {
                    outcome.UpdatedExistingJson = true;
                    targetCustomer = existing;
                    if (string.IsNullOrWhiteSpace(existing.floor) && hasLoc) existing.floor = input.Location.Trim();
                    if (existing.agentLogin == null || existing.agentLogin.Length == 0) existing.agentLogin = locArr;
                    if (existing.acdPosition == null || existing.acdPosition.Length == 0) existing.acdPosition = locArr;
                    if (existing.agentDn == null || existing.agentDn.Length == 0) existing.agentDn = locArr;
                    if (existing.place == null || existing.place.Length == 0) existing.place = locArr;
                    if (existing.rsi == null || existing.rsi.Length == 0) existing.rsi = locArr;
                    existing.useVQueue = input.UseVQueue || (existing.useVQueue == true);
                    existing.useIvr = input.UseIvr || (existing.useIvr == true);
                    if (!string.IsNullOrWhiteSpace(input.Note)) existing.note = input.Note;
                    if (existing.oxePresets == null) existing.oxePresets = new List<OxePreset>();
                    if (input.Presets != null && input.Presets.Count > 0)
                    {
                        for (int pi = 0; pi < input.Presets.Count; pi++)
                        {
                            OxePreset np = input.Presets[pi];
                            bool dup = false;
                            for (int ei = 0; ei < existing.oxePresets.Count; ei++)
                            {
                                OxePreset ep = existing.oxePresets[ei];
                                if (string.Equals(ep.label ?? "", np.label ?? "", StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(ep.entityNo ?? "", np.entityNo ?? "", StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(ep.agentPg ?? "", np.agentPg ?? "", StringComparison.OrdinalIgnoreCase))
                                {
                                    existing.oxePresets[ei] = np;
                                    dup = true;
                                    break;
                                }
                            }
                            if (!dup) existing.oxePresets.Add(np);
                        }
                    }
                }
                else
                {
                    outcome.UpdatedExistingJson = false;
                    CustomerInfo createdCust = new CustomerInfo();
                    createdCust.name = input.CenterName.Trim();
                    createdCust.floor = hasLoc ? input.Location.Trim() : "";
                    createdCust.agentLogin = locArr;
                    createdCust.acdPosition = locArr;
                    createdCust.agentDn = locArr;
                    createdCust.place = locArr;
                    createdCust.rsi = locArr;
                    createdCust.useVQueue = input.UseVQueue;
                    createdCust.useIvr = input.UseIvr;
                    createdCust.note = input.Note ?? "";
                    createdCust.oxePresets = input.Presets ?? new List<OxePreset>();
                    _allCustomers.Add(createdCust);
                    targetCustomer = createdCust;
                }

                SaveCustomersToDiskAndRefreshAll(_selectedCustomer != null ? _selectedCustomer.name : input.CenterName.Trim());
                try
                {
                    string dpPath = Path.Combine(_baseDir, "dialplan_db.json");
                    DialPlanDatabase dpDb = DialPlanRepository.Load(dpPath);
                    DialPlanRepository.UpsertCenterFromCreatePopup(dpDb, targetCustomer, true);
                    DialPlanRepository.Save(dpPath, dpDb);
                    if (DialPlanTabControl != null) DialPlanTabControl.ReloadFromDisk(targetCustomer.name);
                }
                catch (Exception dpEx)
                {
                    AppendLog("WARN", "[선번장 연동] dialplan_db.json 갱신 경고: " + dpEx.Message);
                }

                outcome.SavedToJson = true;
                if (_activeCenterAddPopupView != null)
                {
                    _activeCenterAddPopupView.AppendExternalLog(string.Format("[저장 완료] cm_customers.json & dialplan_db.json {0} ('{1}')",
                        outcome.UpdatedExistingJson ? "업데이트" : "신규 등록", input.CenterName));
                }
                AppendLog("OK", string.Format("[센터 추가] '{0}' {1} 완료 (Config/JSON/선번장 반영)",
                    input.CenterName, outcome.UpdatedExistingJson ? "보완" : "등록"));
            }

            return outcome;
        }

        private void RefreshCustomersFromDialPlan()
        {
            FilterCustomers(TxtSearch.Text);
            if (PersonTabControl != null) PersonTabControl.SetCustomers(_allCustomers);
            if (_activeCenterAddPopupView != null) _activeCenterAddPopupView.SetCustomers(_allCustomers);
            if (UnifiedLookupTabControl != null) UnifiedLookupTabControl.RefreshCustomers();
            if (SnapshotDiffTabControl != null) SnapshotDiffTabControl.RefreshCustomers();
        }

        private void NavigateToExtCtiFromLookup(string customerName, string dnSpec, string ctiSpec)
        {
            OpenOrActivateTab("extcti");
            if (TxtSearch != null && !string.IsNullOrEmpty(TxtSearch.Text))
            {
                TxtSearch.Text = string.Empty;
            }
            if (!string.IsNullOrWhiteSpace(customerName) && _allCustomers != null)
            {
                for (int i = 0; i < _allCustomers.Count; i++)
                {
                    if (string.Equals(_allCustomers[i].name, customerName.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        CmbCustomer.SelectedItem = _allCustomers[i];
                        break;
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(dnSpec)) TxtDn.Text = dnSpec.Trim();
            if (!string.IsNullOrWhiteSpace(ctiSpec)) TxtCti.Text = ctiSpec.Trim();
            AppendLog("INFO", string.Format("[통합 조회 → 내선·CTI 이동] 센터='{0}', 내선='{1}', CTI='{2}'",
                customerName ?? "-", dnSpec ?? "-", ctiSpec ?? "-"));
        }

        private void NavigateToPersonFromLookup(string customerName)
        {
            OpenOrActivateTab("person");
            if (PersonTabControl != null && !string.IsNullOrWhiteSpace(customerName))
            {
                PersonTabControl.SelectCustomerByName(customerName, true);
            }
            AppendLog("INFO", string.Format("[통합 조회 → Person 관리 이동] 센터='{0}'", customerName ?? "-"));
        }

        private void NavigateToExtCtiFromDialPlan(DialPlanCenter center)
        {
            if (center == null) return;
            OpenOrActivateTab("extcti");

            if (TxtSearch != null && !string.IsNullOrEmpty(TxtSearch.Text))
                TxtSearch.Text = string.Empty;

            CustomerInfo match = null;
            string linkName = !string.IsNullOrWhiteSpace(center.cmCustomerName) ? center.cmCustomerName.Trim() : (center.project ?? "").Trim();
            for (int i = 0; i < _allCustomers.Count; i++)
            {
                if (string.Equals(_allCustomers[i].name, linkName, StringComparison.OrdinalIgnoreCase))
                {
                    match = _allCustomers[i];
                    break;
                }
            }
            if (match == null && !string.IsNullOrWhiteSpace(center.project))
            {
                string projTrim = center.project.Trim();
                for (int i = 0; i < _allCustomers.Count; i++)
                {
                    if (string.Equals(_allCustomers[i].name, projTrim, StringComparison.OrdinalIgnoreCase))
                    {
                        match = _allCustomers[i];
                        break;
                    }
                }
            }

            if (match != null)
            {
                CmbCustomer.SelectedItem = match;
                if (center.lines != null && center.lines.Count > 0 && CmbOxePreset != null && CmbOxePreset.Items.Count > 0)
                {
                    string wantEnt = DialPlanWorkbookMapper.ExtractLeadingIntToken(center.lines[0].entityNo);
                    string wantPg = DialPlanWorkbookMapper.ExtractLeadingIntToken(center.lines[0].agentPg);
                    for (int pi = 0; pi < CmbOxePreset.Items.Count; pi++)
                    {
                        OxePreset p = CmbOxePreset.Items[pi] as OxePreset;
                        if (p == null) continue;
                        if ((!string.IsNullOrEmpty(wantEnt) && string.Equals(p.entityNo, wantEnt, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(wantPg) && string.Equals(p.agentPg, wantPg, StringComparison.OrdinalIgnoreCase)))
                        {
                            CmbOxePreset.SelectedIndex = pi;
                            break;
                        }
                    }
                }
                AppendLog("INFO", string.Format("[선번장 → 개통 전환] '{0}' (Config '{1}') 선택됨", center.project, match.name));
                PopupDialogs.ShowSyntaxToast(this, true, "개통 탭 전환", "'" + match.name + "' 센터가 내선·CTI 탭에 선택되었습니다.");
            }
            else
            {
                AppendLog("WARN", string.Format("[선번장 → 개통 전환] '{0}'에 연결된 Config 고객사 없음", center.project));
                PopupDialogs.ShowSyntaxToast(this, false, "Config 연동 없음", "연결된 Config 고객사가 없습니다. '+ 센터 추가' 또는 Config연동명을 먼저 확인하세요.");
            }
        }

        private async Task<CenterFullDeleteOutcome> HandleCenterFullDeleteAsync(DialPlanCenter center)
        {
            var result = new CenterFullDeleteOutcome { Success = true, CmExecuted = false };
            if (center == null || !center.HasCmLink)
                return result;

            string targetName = center.cmCustomerName.Trim();
            EnsureCmReadyForCenter();
            CmCenterWorkerRequest req = BuildCenterWorkerRequest("DeleteCenterArtifacts", targetName, center.location ?? "");
            req.DeleteLocationIfSpecified = false;

            string jsonRaw = null;
            int exit = await ExecuteCenterWorkerAsync(req, string.Format("Config '{0}' 센터 폴더 삭제 중...", targetName), false, delegate(string line)
            {
                if (line.StartsWith("##CENTER_DELETE_JSON##", StringComparison.Ordinal))
                {
                    jsonRaw = line.Substring("##CENTER_DELETE_JSON##".Length);
                }
            });

            CmCenterDeleteResponse delRes = DeserializeJsonPayload<CmCenterDeleteResponse>(jsonRaw);
            if (exit != 0 && delRes == null)
            {
                result.Success = false;
                result.CmExecuted = true;
                result.Message = "Config 센터 아티팩트 삭제 중 오류가 발생했습니다. 로그를 확인하세요.";
                AppendLog("ERR", string.Format("[완전 삭제 실패] '{0}' Config Worker 종료코드 {1}", targetName, exit));
                return result;
            }

            result.CmExecuted = true;
            if (delRes != null)
            {
                result.CmDeletedCount = delRes.deletedCount;
                result.CmFailedCount = delRes.failedCount;
                if (delRes.failedCount > 0)
                {
                    result.Success = false;
                    result.Message = string.Format("Config 일부 항목 삭제 실패 (삭제 {0}건 / 실패 {1}건). JSON 유지됨.", delRes.deletedCount, delRes.failedCount);
                    AppendLog("ERR", "[완전 삭제 중단] " + result.Message);
                    return result;
                }
            }

            AppendLog("OK", string.Format("[완전 삭제] '{0}' Config 삭제 완료 (삭제 {1}건)", targetName, result.CmDeletedCount));
            return result;
        }

        private void ChkCenterOption_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializingUi || _isSyncingCenterOptChecks) return;
            if (sender == ChkCenterVQueue) _cmOptUserTouchedVq = true;
            if (sender == ChkCenterIvr) _cmOptUserTouchedIvr = true;
            if (TxtCmOptionSyncState != null && _selectedCustomer != null)
            {
                TxtCmOptionSyncState.Text = "변경 대기 ('적용하기' 클릭 시 반영)";
            }
        }

        private async Task<CmCenterStatusResponse> QueryLiveCenterOptionsAsync(CustomerInfo cust)
        {
            CmCenterWorkerRequest req = BuildCenterWorkerRequest("CheckCenterOptions", cust.name, cust.floor);
            string jsonRaw = null;
            int exit = await ExecuteCenterWorkerAsync(req, "Config 옵션 조회 중...", false, delegate(string line)
            {
                if (line.StartsWith("##CENTER_STATUS_JSON##", StringComparison.Ordinal))
                {
                    jsonRaw = line.Substring("##CENTER_STATUS_JSON##".Length);
                }
            });

            if (exit != 0)
            {
                throw new InvalidOperationException("Config VQueue / IVR 상태 조회 중 오류가 발생했습니다.");
            }
            CmCenterStatusResponse st = DeserializeJsonPayload<CmCenterStatusResponse>(jsonRaw);
            if (st == null)
            {
                throw new InvalidOperationException("Config VQueue / IVR 상태 응답을 파싱할 수 없습니다.");
            }
            return st;
        }

        private async void BtnCheckCenterOpts_Click(object sender, RoutedEventArgs e)
        {
            if (_isCmBusy || _selectedCustomer == null) return;
            CustomerInfo cust = _selectedCustomer;
            try
            {
                EnsureCmReadyForCenter();
                CmCenterStatusResponse st = await QueryLiveCenterOptionsAsync(cust);
                _cmOptRealVq = st.vqueueExists;
                _cmOptRealIvr = st.ivrExists;
                _cmOptUserTouchedVq = false;
                _cmOptUserTouchedIvr = false;
                cust.useVQueue = st.vqueueExists;
                cust.useIvr = st.ivrExists;

                _isSyncingCenterOptChecks = true;
                ChkCenterVQueue.IsChecked = st.vqueueExists;
                ChkCenterIvr.IsChecked = st.ivrExists;
                _isSyncingCenterOptChecks = false;

                TxtCmOptionSyncState.Text = string.Format("Config 실물 확인 (VQ:{0} / IVR:{1})",
                    st.vqueueExists ? "존재" : "없음",
                    st.ivrExists ? "존재" : "없음");
                AppendLog("INFO", string.Format("[Config 상태 조회] '{0}' -> VQueue={1}, IVR(Place Group)={2}",
                    cust.name, st.vqueueExists ? "존재" : "미생성", st.ivrExists ? "존재" : "미생성"));
            }
            catch (Exception ex)
            {
                AppendLog("ERR", "[Config 옵션 조회 실패] " + ex.Message);
                PopupDialogs.ShowSyntaxToast(this, false, "Config 상태 조회 오류", ex.Message);
            }
        }

        private async void BtnApplyCenterOpts_Click(object sender, RoutedEventArgs e)
        {
            if (_isCmBusy || _selectedCustomer == null) return;
            CustomerInfo cust = _selectedCustomer;
            bool wantVq = (ChkCenterVQueue.IsChecked == true);
            bool wantIvr = (ChkCenterIvr.IsChecked == true);

            try
            {
                EnsureCmReadyForCenter();
                CmCenterStatusResponse live = await QueryLiveCenterOptionsAsync(cust);
                bool realVq = live.vqueueExists;
                bool realIvr = live.ivrExists;

                bool applyVq = false;
                bool applyIvr = false;
                bool preserveNoticeVq = false;
                bool preserveNoticeIvr = false;

                if (wantVq && !realVq)
                {
                    applyVq = true;
                }
                else if (!wantVq && realVq)
                {
                    if (_cmOptRealVq == true || _cmOptUserTouchedVq)
                        applyVq = true;
                    else
                        preserveNoticeVq = true;
                }

                if (wantIvr && !realIvr)
                {
                    applyIvr = true;
                }
                else if (!wantIvr && realIvr)
                {
                    if (_cmOptRealIvr == true || _cmOptUserTouchedIvr)
                        applyIvr = true;
                    else
                        preserveNoticeIvr = true;
                }

                if (preserveNoticeVq || preserveNoticeIvr)
                {
                    _isSyncingCenterOptChecks = true;
                    if (preserveNoticeVq) ChkCenterVQueue.IsChecked = true;
                    if (preserveNoticeIvr) ChkCenterIvr.IsChecked = true;
                    _isSyncingCenterOptChecks = false;
                }

                _cmOptRealVq = realVq;
                _cmOptRealIvr = realIvr;
                cust.useVQueue = realVq;
                cust.useIvr = realIvr;

                if (!applyVq && !applyIvr)
                {
                    _cmOptUserTouchedVq = false;
                    _cmOptUserTouchedIvr = false;
                    TxtCmOptionSyncState.Text = string.Format("Config 일치 (VQ:{0} / IVR:{1})",
                        realVq ? "존재" : "없음", realIvr ? "존재" : "없음");
                    string note = (preserveNoticeVq || preserveNoticeIvr)
                        ? "기존 Config에 존재하는 항목을 안전을 위해 유지 체크로 동기화했습니다.\n삭제하려면 체크 해제 후 다시 '적용하기'를 누르세요."
                        : "현재 체크 상태와 실제 Config 구성이 이미 동일합니다.";
                    PopupDialogs.ShowSyntaxToast(this, true, "변경 사항 없음", note);
                    return;
                }

                bool hasDelete = (applyVq && !wantVq) || (applyIvr && !wantIvr);
                StringBuilder plan = new StringBuilder();
                plan.AppendLine(string.Format("• 대상 센터: {0}", cust.DisplayName));
                if (applyVq)
                {
                    plan.AppendLine(string.Format("• VQueue 폴더: {0} ({1})",
                        wantVq ? "신규 생성" : "Config에서 삭제", live.vqueuePath));
                }
                if (applyIvr)
                {
                    plan.AppendLine(string.Format("• IVR Place Group: {0} ({1})",
                        wantIvr ? "신규 생성" : "Config에서 삭제", live.ivrPath));
                }
                if (preserveNoticeVq || preserveNoticeIvr)
                {
                    plan.AppendLine("※ 기존 Config에 이미 있던 미선택 항목은 안전상 유지됩니다.");
                }

                bool confirmed = hasDelete
                    ? PopupDialogs.ConfirmDelete(this, "Config 선택 구성 삭제 포함 적용", "Config VQueue / Place Group 변경 적용", plan.ToString().TrimEnd(),
                        "체크 해제된 항목을 Config에서 삭제합니다. 내부에 하위 오브젝트(Virtual Queue 등)가 남아 있으면 삭제가 거부될 수 있습니다.\n\n진행하려면 '삭제한다' 를 입력하세요.")
                    : PopupDialogs.ConfirmAction(this, "Config 선택 구성 적용 확인", "Config VQueue / Place Group 생성 적용", plan.ToString().TrimEnd(), "적용하기");

                if (!confirmed)
                {
                    AppendLog("WARN", "[Config 옵션 적용] 사용자에 의해 취소되었습니다.");
                    return;
                }

                CmCenterWorkerRequest applyReq = BuildCenterWorkerRequest("ApplyCenterOptions", cust.name, cust.floor);
                applyReq.ApplyVQueue = applyVq;
                applyReq.WantVQueue = wantVq;
                applyReq.ApplyIvr = applyIvr;
                applyReq.WantIvr = wantIvr;

                string applyJsonRaw = null;
                int exit = await ExecuteCenterWorkerAsync(applyReq, "Config 옵션 적용 중...", false, delegate(string line)
                {
                    if (line.StartsWith("##CENTER_APPLY_JSON##", StringComparison.Ordinal))
                    {
                        applyJsonRaw = line.Substring("##CENTER_APPLY_JSON##".Length);
                    }
                });

                CmCenterApplyResponse applyRes = DeserializeJsonPayload<CmCenterApplyResponse>(applyJsonRaw);
                if (exit != 0 && applyRes == null)
                {
                    PopupDialogs.ShowSyntaxToast(this, false, "Config 옵션 적용 오류", "Log의 오류 내용을 확인하세요.");
                    return;
                }

                if (applyRes != null)
                {
                    _cmOptRealVq = applyRes.vqueueExists;
                    _cmOptRealIvr = applyRes.ivrExists;
                    _cmOptUserTouchedVq = false;
                    _cmOptUserTouchedIvr = false;
                    cust.useVQueue = applyRes.vqueueExists;
                    cust.useIvr = applyRes.ivrExists;

                    _isSyncingCenterOptChecks = true;
                    ChkCenterVQueue.IsChecked = applyRes.vqueueExists;
                    ChkCenterIvr.IsChecked = applyRes.ivrExists;
                    _isSyncingCenterOptChecks = false;

                    TxtCmOptionSyncState.Text = string.Format("적용 완료 (VQ:{0} / IVR:{1})",
                        applyRes.vqueueExists ? "존재" : "없음",
                        applyRes.ivrExists ? "존재" : "없음");

                    SaveCustomersToDiskAndRefreshAll(cust.name);

                    string toastMsg = string.Format("VQueue: {0}  |  IVR(Place Group): {1}{2}",
                        applyRes.vqueueExists ? "사용중(Config 존재)" : "미사용",
                        applyRes.ivrExists ? "사용중(Config 존재)" : "미사용",
                        applyRes.failedCount > 0 ? string.Format("\n실패 {0}건 (Log 확인)", applyRes.failedCount) : "");
                    PopupDialogs.ShowSyntaxToast(this, applyRes.failedCount == 0,
                        applyRes.failedCount == 0 ? "Config 구성 적용 완료" : "Config 구성 일부 실패",
                        toastMsg);
                }
            }
            catch (Exception ex)
            {
                AppendLog("ERR", "[Config 옵션 적용 실패] " + ex.Message);
                PopupDialogs.ShowSyntaxToast(this, false, "Config 옵션 적용 오류", ex.Message);
            }
        }

        // ==================== SIDEBAR & BROWSER-STYLE TAB SHELL ====================
        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            _isSidebarCollapsed = !_isSidebarCollapsed;
            ApplySidebarCollapsedVisuals();
        }

        private void ApplySidebarCollapsedVisuals()
        {
            if (_isSidebarCollapsed)
            {
                ColSidebar.Width = new GridLength(54);
                TxtSidebarHeader.Visibility = Visibility.Collapsed;
                SidebarFooter.Visibility = Visibility.Collapsed;
                TxtNavExtCtiLabel.Visibility = Visibility.Collapsed;
                TxtNavPersonLabel.Visibility = Visibility.Collapsed;
                TxtNavDialPlanLabel.Visibility = Visibility.Collapsed;
                TxtNavLookupLabel.Visibility = Visibility.Collapsed;
                TxtNavSnapshotLabel.Visibility = Visibility.Collapsed;
                DotNavExtCti.Visibility = Visibility.Collapsed;
                DotNavPerson.Visibility = Visibility.Collapsed;
                DotNavDialPlan.Visibility = Visibility.Collapsed;
                DotNavLookup.Visibility = Visibility.Collapsed;
                DotNavSnapshot.Visibility = Visibility.Collapsed;
                TxtSidebarToggleGlyph.Text = "»";
            }
            else
            {
                ColSidebar.Width = new GridLength(186);
                TxtSidebarHeader.Visibility = Visibility.Visible;
                SidebarFooter.Visibility = Visibility.Visible;
                TxtNavExtCtiLabel.Visibility = Visibility.Visible;
                TxtNavPersonLabel.Visibility = Visibility.Visible;
                TxtNavDialPlanLabel.Visibility = Visibility.Visible;
                TxtNavLookupLabel.Visibility = Visibility.Visible;
                TxtNavSnapshotLabel.Visibility = Visibility.Visible;
                TxtSidebarToggleGlyph.Text = "«";
                SyncSidebarDots();
            }
        }

        private void SyncSidebarDots()
        {
            if (_isSidebarCollapsed)
            {
                DotNavExtCti.Visibility = Visibility.Collapsed;
                DotNavPerson.Visibility = Visibility.Collapsed;
                DotNavDialPlan.Visibility = Visibility.Collapsed;
                DotNavLookup.Visibility = Visibility.Collapsed;
                DotNavSnapshot.Visibility = Visibility.Collapsed;
                return;
            }
            DotNavExtCti.Visibility = _openTabs.Contains("extcti") ? Visibility.Visible : Visibility.Collapsed;
            DotNavPerson.Visibility = _openTabs.Contains("person") ? Visibility.Visible : Visibility.Collapsed;
            DotNavDialPlan.Visibility = _openTabs.Contains("dialplan") ? Visibility.Visible : Visibility.Collapsed;
            DotNavLookup.Visibility = _openTabs.Contains("lookup") ? Visibility.Visible : Visibility.Collapsed;
            DotNavSnapshot.Visibility = _openTabs.Contains("snapshot") ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnNavExtCti_Click(object sender, RoutedEventArgs e)
        {
            OpenOrActivateTab("extcti");
        }

        private void BtnNavPerson_Click(object sender, RoutedEventArgs e)
        {
            OpenOrActivateTab("person");
        }

        private void BtnNavDialPlan_Click(object sender, RoutedEventArgs e)
        {
            OpenOrActivateTab("dialplan");
        }

        private void BtnNavLookup_Click(object sender, RoutedEventArgs e)
        {
            OpenOrActivateTab("lookup");
        }

        private void BtnNavSnapshot_Click(object sender, RoutedEventArgs e)
        {
            OpenOrActivateTab("snapshot");
        }

        private void OpenOrActivateTab(string tabKey)
        {
            if (string.IsNullOrEmpty(tabKey)) return;
            if (!_openTabs.Contains(tabKey))
            {
                _openTabs.Add(tabKey);
            }
            _activeTabKey = tabKey;
            SyncTabAndSidebarVisuals();
            if (tabKey == "snapshot" && SnapshotDiffTabControl != null)
            {
                SnapshotDiffTabControl.EnsureInitialListLoaded();
            }
        }

        private void CloseTab(string tabKey)
        {
            if (string.Equals(tabKey, "dialplan", StringComparison.OrdinalIgnoreCase) &&
                DialPlanTabControl != null &&
                !DialPlanTabControl.ConfirmDiscardOrSaveUnsaved("선번장 · 센터 탭 닫기"))
            {
                return;
            }
            int idx = _openTabs.IndexOf(tabKey);
            if (idx < 0) return;

            _openTabs.RemoveAt(idx);
            if (_activeTabKey == tabKey)
            {
                if (_openTabs.Count == 0)
                {
                    _activeTabKey = "";
                }
                else
                {
                    int nextIdx = Math.Min(idx, _openTabs.Count - 1);
                    _activeTabKey = _openTabs[nextIdx];
                }
            }
            SyncTabAndSidebarVisuals();
        }

        private void SyncTabAndSidebarVisuals()
        {
            bool hasTabs = _openTabs.Count > 0;
            bool showExtCti = hasTabs && _activeTabKey == "extcti";
            bool showPerson = hasTabs && _activeTabKey == "person";
            bool showDialPlan = hasTabs && _activeTabKey == "dialplan";
            bool showLookup = hasTabs && _activeTabKey == "lookup";
            bool showSnapshot = hasTabs && _activeTabKey == "snapshot";

            ExtCtiTabRoot.Visibility = showExtCti ? Visibility.Visible : Visibility.Collapsed;
            PersonTabRoot.Visibility = showPerson ? Visibility.Visible : Visibility.Collapsed;
            DialPlanTabRoot.Visibility = showDialPlan ? Visibility.Visible : Visibility.Collapsed;
            LookupTabRoot.Visibility = showLookup ? Visibility.Visible : Visibility.Collapsed;
            SnapshotTabRoot.Visibility = showSnapshot ? Visibility.Visible : Visibility.Collapsed;
            EmptyTabsRoot.Visibility = hasTabs ? Visibility.Collapsed : Visibility.Visible;

            if (showExtCti)
            {
                Dispatcher.BeginInvoke(new Action(delegate()
                {
                    if (_dnErrorAdorner != null) _dnErrorAdorner.InvalidateVisual();
                    if (_ctiErrorAdorner != null) _ctiErrorAdorner.InvalidateVisual();
                }), DispatcherPriority.Render);
            }

            UpdateSidebarItemStyle(BtnNavExtCti, showExtCti, _openTabs.Contains("extcti"));
            UpdateSidebarItemStyle(BtnNavPerson, showPerson, _openTabs.Contains("person"));
            UpdateSidebarItemStyle(BtnNavDialPlan, showDialPlan, _openTabs.Contains("dialplan"));
            UpdateSidebarItemStyle(BtnNavLookup, showLookup, _openTabs.Contains("lookup"));
            UpdateSidebarItemStyle(BtnNavSnapshot, showSnapshot, _openTabs.Contains("snapshot"));
            SyncSidebarDots();
            RenderBrowserTabs();
        }

        private static void UpdateSidebarItemStyle(Button btn, bool isActive, bool isOpen)
        {
            if (isActive)
            {
                btn.SetResourceReference(Button.BackgroundProperty, "ChipBgBrush");
                btn.SetResourceReference(Button.ForegroundProperty, "ChipFgBrush");
                btn.SetResourceReference(Button.BorderBrushProperty, "PrimaryBrush");
                btn.FontWeight = FontWeights.Bold;
            }
            else if (isOpen)
            {
                btn.SetResourceReference(Button.BackgroundProperty, "SurfaceAltBrush");
                btn.SetResourceReference(Button.ForegroundProperty, "TextBrush");
                btn.SetResourceReference(Button.BorderBrushProperty, "BorderBrush");
                btn.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.SetResourceReference(Button.ForegroundProperty, "MutedBrush");
                btn.BorderBrush = Brushes.Transparent;
                btn.FontWeight = FontWeights.SemiBold;
            }
        }

        private void RenderBrowserTabs()
        {
            if (TabStripPanel == null) return;
            TabStripPanel.Children.Clear();

            for (int i = 0; i < _openTabs.Count; i++)
            {
                string tabKey = _openTabs[i];
                bool isActive = (tabKey == _activeTabKey);
                TabStripPanel.Children.Add(BuildBrowserTabElement(tabKey, isActive));
            }
        }

        private UIElement BuildBrowserTabElement(string tabKey, bool isActive)
        {
            string title = "내선 · CTI";
            string dotBrushKey = "PrimaryBrush";
            if (tabKey == "person") { title = "Person 관리"; dotBrushKey = "OxeBrush"; }
            else if (tabKey == "dialplan") { title = "선번장 · 센터"; dotBrushKey = "OkFgBrush"; }
            else if (tabKey == "lookup") { title = "통합 조회"; dotBrushKey = "ChipFgBrush"; }
            else if (tabKey == "snapshot") { title = "스냅샷 · 복구"; dotBrushKey = "WarnFgBrush"; }

            Border tabBd = new Border();
            tabBd.CornerRadius = new CornerRadius(7, 7, 0, 0);
            tabBd.MinWidth = 154;
            tabBd.Height = 34;
            tabBd.Margin = isActive ? new Thickness(0, 0, 4, -1) : new Thickness(0, 2, 4, 0);
            tabBd.Padding = new Thickness(0);
            tabBd.Cursor = Cursors.Hand;
            tabBd.SnapsToDevicePixels = true;

            if (isActive)
            {
                tabBd.SetResourceReference(Border.BackgroundProperty, "AppBgBrush");
                tabBd.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                tabBd.BorderThickness = new Thickness(1, 1, 1, 0);
            }
            else
            {
                tabBd.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
                tabBd.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                tabBd.BorderThickness = new Thickness(1, 1, 1, 0);
                tabBd.Opacity = 0.78;
            }

            tabBd.MouseEnter += delegate(object s, MouseEventArgs e)
            {
                if (!isActive) tabBd.Opacity = 1.0;
            };
            tabBd.MouseLeave += delegate(object s, MouseEventArgs e)
            {
                if (!isActive) tabBd.Opacity = 0.78;
            };

            string capturedKey = tabKey;
            tabBd.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 1)
                {
                    OpenOrActivateTab(capturedKey);
                }
            };
            tabBd.MouseDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.Middle)
                {
                    e.Handled = true;
                    CloseTab(capturedKey);
                }
            };

            Grid container = new Grid();
            container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2.5) });
            container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            if (isActive)
            {
                Border topAccent = new Border();
                topAccent.CornerRadius = new CornerRadius(6, 6, 0, 0);
                topAccent.SetResourceReference(Border.BackgroundProperty, dotBrushKey);
                Grid.SetRow(topAccent, 0);
                container.Children.Add(topAccent);
            }

            DockPanel body = new DockPanel();
            body.LastChildFill = false;
            body.Margin = new Thickness(11, 0, 6, 0);
            body.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(body, 1);

            StackPanel leftGroup = new StackPanel();
            leftGroup.Orientation = Orientation.Horizontal;
            leftGroup.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(leftGroup, Dock.Left);

            System.Windows.Shapes.Ellipse dot = new System.Windows.Shapes.Ellipse();
            dot.Width = 7;
            dot.Height = 7;
            dot.Margin = new Thickness(0, 0, 7, 0);
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, isActive ? dotBrushKey : "MutedBrush");
            leftGroup.Children.Add(dot);

            TextBlock lbl = new TextBlock();
            lbl.Text = title;
            lbl.FontSize = 12;
            lbl.FontWeight = isActive ? FontWeights.Bold : FontWeights.SemiBold;
            lbl.VerticalAlignment = VerticalAlignment.Center;
            lbl.SetResourceReference(TextBlock.ForegroundProperty, isActive ? "TextBrush" : "MutedBrush");
            leftGroup.Children.Add(lbl);

            body.Children.Add(leftGroup);

            Button closeBtn = new Button();
            closeBtn.Content = "✕";
            closeBtn.Width = 18;
            closeBtn.Height = 18;
            closeBtn.FontSize = 9.5;
            closeBtn.Padding = new Thickness(0);
            closeBtn.Margin = new Thickness(8, 0, 0, 0);
            closeBtn.BorderThickness = new Thickness(0);
            closeBtn.Background = Brushes.Transparent;
            closeBtn.Cursor = Cursors.Hand;
            closeBtn.Focusable = false;
            closeBtn.VerticalAlignment = VerticalAlignment.Center;
            closeBtn.ToolTip = title + " 탭 닫기";
            closeBtn.SetResourceReference(Button.ForegroundProperty, "MutedBrush");
            closeBtn.Click += delegate(object s, RoutedEventArgs e)
            {
                e.Handled = true;
                CloseTab(capturedKey);
            };
            DockPanel.SetDock(closeBtn, Dock.Right);
            body.Children.Add(closeBtn);

            container.Children.Add(body);
            tabBd.Child = container;
            return tabBd;
        }

        // ==================== VIEW MODE TABS & LIGHTGATE WEBVIEW2 ====================
        private void SetActiveViewMode(string mode)
        {
            _viewMode = mode;
            if (mode == "Work")
            {
                ColWork.MinWidth = 0;
                ColEms.MinWidth = 0;
                ColWork.Width = new GridLength(1, GridUnitType.Star);
                ColSplitter.Width = new GridLength(0);
                ColEms.Width = new GridLength(0);
                WorkPane.Visibility = Visibility.Visible;
                PaneSplitter.Visibility = Visibility.Collapsed;
                EmsPane.Visibility = Visibility.Collapsed;
            }
            else if (mode == "LightGate")
            {
                ColWork.MinWidth = 0;
                ColEms.MinWidth = 0;
                ColWork.Width = new GridLength(0);
                ColSplitter.Width = new GridLength(0);
                ColEms.Width = new GridLength(1, GridUnitType.Star);
                WorkPane.Visibility = Visibility.Collapsed;
                PaneSplitter.Visibility = Visibility.Collapsed;
                EmsPane.Visibility = Visibility.Visible;
                if (this.IsLoaded) EnsureWebView2InitializedAsync();
            }
            else // Split
            {
                ColWork.MinWidth = 420;
                ColEms.MinWidth = 350;
                ColWork.Width = new GridLength(1.08, GridUnitType.Star);
                ColSplitter.Width = new GridLength(6);
                ColEms.Width = new GridLength(1.0, GridUnitType.Star);
                WorkPane.Visibility = Visibility.Visible;
                PaneSplitter.Visibility = Visibility.Visible;
                EmsPane.Visibility = Visibility.Visible;
                if (this.WindowState == WindowState.Normal && this.Width < 1320)
                {
                    this.Width = 1480;
                }
                if (this.IsLoaded) EnsureWebView2InitializedAsync();
            }

            UpdateTabButtonStyle(BtnTabSplit, mode == "Split");
            UpdateTabButtonStyle(BtnTabWork, mode == "Work");
            UpdateTabButtonStyle(BtnTabLightGate, mode == "LightGate");
        }

        private static void UpdateTabButtonStyle(Button btn, bool active)
        {
            if (active)
            {
                btn.SetResourceReference(Button.BackgroundProperty, "PrimaryBrush");
                btn.SetResourceReference(Button.ForegroundProperty, "PrimaryFgBrush");
                btn.FontWeight = FontWeights.Bold;
            }
            else
            {
                btn.SetResourceReference(Button.BackgroundProperty, "SurfaceAltBrush");
                btn.SetResourceReference(Button.ForegroundProperty, "MutedBrush");
                btn.FontWeight = FontWeights.SemiBold;
            }
        }

        private void BtnTabSplit_Click(object sender, RoutedEventArgs e)
        {
            SetActiveViewMode("Split");
        }

        private void BtnTabWork_Click(object sender, RoutedEventArgs e)
        {
            SetActiveViewMode("Work");
        }

        private void BtnTabLightGate_Click(object sender, RoutedEventArgs e)
        {
            SetActiveViewMode("LightGate");
        }

        private async void EnsureWebView2InitializedAsync()
        {
            if (_webViewInitStarted) return;
            _webViewInitStarted = true;
            try
            {
                SetEmsTerminalState(false, "LightGate 시작 중...");
                string userDataDir = _settingsService.WebViewDataDir;
                Directory.CreateDirectory(userDataDir);
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
                await EmsWebView.EnsureCoreWebView2Async(env);

                EmsWebView.CoreWebView2.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs args)
                {
                    if (_lastNavigatedUrl != args.Uri)
                    {
                        _lastNavigatedUrl = args.Uri;
                        _autoLoginInjectedForUrl = false;
                    }
                    Dispatcher.BeginInvoke(new Action(delegate()
                    {
                        SetEmsTerminalState(false, "LightGate 로딩 중...");
                    }));
                };

                EmsWebView.CoreWebView2.HistoryChanged += delegate(object s, object args)
                {
                    Dispatcher.BeginInvoke(new Action(async delegate()
                    {
                        await OnLightGateReadyOrNavigatedAsync();
                    }));
                };

                EmsWebView.CoreWebView2.NavigationCompleted += delegate(object s, CoreWebView2NavigationCompletedEventArgs args)
                {
                    Dispatcher.BeginInvoke(new Action(async delegate()
                    {
                        if (!args.IsSuccess)
                        {
                            SetEmsTerminalState(false, "연결 실패 (재시도)");
                            return;
                        }
                        await OnLightGateReadyOrNavigatedAsync();
                    }));
                };

                _webViewReady = true;
                string url = string.IsNullOrEmpty(_settings.EmsUrl) ? "https://lightgate.lbucess.com/terminal" : _settings.EmsUrl;
                EmsWebView.Source = new Uri(url);
            }
            catch (Exception ex)
            {
                _webViewInitStarted = false;
                SetEmsTerminalState(false, "WebView2 초기화 실패");
                AppendLog("ERR", "LightGate 초기화 실패 (Edge WebView2 Runtime 확인 필요): " + ex.Message);
                AuditLogService.AppendError(_auditLogDir, "PBX", "LightGate:Init", ex.Message);
            }
        }

        private async Task OnLightGateReadyOrNavigatedAsync()
        {
            if (!_webViewReady || EmsWebView.CoreWebView2 == null) return;
            if (!_isEmsTerminalConnected && !_isEmsActionBusy)
            {
                SetEmsTerminalState(false, "LightGate 연결됨");
            }

            if (_settings.EmsAutoLogin && !_autoLoginInjectedForUrl && HasSavedEmsCredentials())
            {
                await TryInjectEmsAutoLoginAsync();
            }

            await ApplyEmsDomModesAsync();

            if (!_autoSetupLoopRunning)
            {
                await RunLightGateStartupLoopAsync();
            }
        }

        private async Task TryInjectEmsAutoLoginAsync()
        {
            string user = (_settings.EmsUserId ?? "").Trim();
            string pass = SettingsService.UnprotectString(_settings.EmsPasswordEnc);
            if (user.Length == 0 || pass.Length == 0) return;

            JavaScriptSerializer ser = new JavaScriptSerializer();
            string userJson = ser.Serialize(user);
            string passJson = ser.Serialize(pass);

            string js = @"
(function(u, p){
  try {
    var pwInput = document.querySelector('input[type=""password""]');
    if (!pwInput) return 'NO_LOGIN_FORM';
    var inputs = Array.prototype.slice.call(document.querySelectorAll('input'));
    var idInput = inputs.find(function(el){
      var t = (el.type || '').toLowerCase();
      return el !== pwInput && (t === 'email' || t === 'text') && el.offsetParent !== null;
    });
    if (!idInput) return 'NO_ID_INPUT';
    var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
    setter.call(idInput, u);
    idInput.dispatchEvent(new Event('input', { bubbles: true }));
    idInput.dispatchEvent(new Event('change', { bubbles: true }));
    setter.call(pwInput, p);
    pwInput.dispatchEvent(new Event('input', { bubbles: true }));
    pwInput.dispatchEvent(new Event('change', { bubbles: true }));
    setTimeout(function(){
      var btn = document.querySelector('button[type=""submit""]') || Array.prototype.slice.call(document.querySelectorAll('button')).find(function(b){
        return (b.innerText || '').indexOf('로그인') >= 0;
      });
      if (btn) btn.click();
    }, 180);
    return 'FILLED';
  } catch (e) { return 'ERR:' + e.message; }
})(" + userJson + "," + passJson + ");";

            try
            {
                string res = await EmsWebView.CoreWebView2.ExecuteScriptAsync(js);
                if (res != null && res.Contains("FILLED"))
                {
                    _autoLoginInjectedForUrl = true;
                }
            }
            catch { }
        }

        private async Task RunLightGateStartupLoopAsync()
        {
            _autoSetupLoopRunning = true;
            int myGen = _emsLoopGen;
            try
            {
                bool autoOpenWanted = !_autoOpenedServerDone;
                bool minimizeAfterOpen = HasSavedEmsCredentials() || (_settings.EmsTerminalOnly);
                string kw = (_settings.EmsServerKeyword ?? "9. PBX_A").Trim();

                for (int step = 0; step < 30; step++)
                {
                    if (myGen != _emsLoopGen) return;

                    if (!_loggedLightGateLogin)
                    {
                        bool loggedInNow = await IsLightGateLoggedInAsync(kw);
                        if (loggedInNow)
                        {
                            _loggedLightGateLogin = true;
                            AppendLog("OK", "LightGate 로그인 성공");
                        }
                    }

                    await ApplyEmsDomModesAsync();

                    if (autoOpenWanted && !_autoOpenedServerDone)
                    {
                        string stepRes = await TryAutoOpenAndMinimizeServerAsync(kw, minimizeAfterOpen);
                        if (stepRes != null && (stepRes.Contains("MINIMIZED_READY") || (!minimizeAfterOpen && stepRes.Contains("TERM_OPEN"))))
                        {
                            if (!_loggedLightGateLogin)
                            {
                                _loggedLightGateLogin = true;
                                AppendLog("OK", "LightGate 로그인 성공");
                            }
                            _autoOpenedServerDone = true;
                            if (!_isEmsActionBusy)
                            {
                                SetEmsTerminalState(true, "터미널 활성");
                            }
                            if (!_loggedTerminalReady)
                            {
                                _loggedTerminalReady = true;
                                AppendLog("OK", string.Format("터미널 실행 완료 ({0})", kw));
                            }
                            await ApplyEmsDomModesAsync();
                            break;
                        }
                    }
                    else if (step >= 4)
                    {
                        break;
                    }

                    await Task.Delay(450);
                }
            }
            finally
            {
                _autoSetupLoopRunning = false;
            }
        }

        private async Task<string> TryAutoOpenAndMinimizeServerAsync(string serverKeyword, bool minimizeDrawer)
        {
            if (!_webViewReady || EmsWebView.CoreWebView2 == null) return "";
            JavaScriptSerializer ser = new JavaScriptSerializer();
            string kwJson = ser.Serialize(serverKeyword);
            string minFlag = minimizeDrawer ? "true" : "false";

            string js = @"
(function(kw, shouldMin){
  if (document.querySelector('input[type=""password""]')) return 'ON_LOGIN';
  function norm(s){ return (s || '').replace(/\s+/g, '').toLowerCase(); }
  function isVis(el){ return el && el.offsetParent !== null && el.getBoundingClientRect().width > 0; }
  function findTextEl(exactNorm, maxWidth, minWidth){
    var nodes = Array.prototype.slice.call(document.querySelectorAll('button,div,span,p,h1,h2,h3,li,a'));
    var best = null;
    for (var i = 0; i < nodes.length; i++){
      var el = nodes[i];
      if (!isVis(el)) continue;
      var r = el.getBoundingClientRect();
      if (maxWidth && r.width > maxWidth) continue;
      if (minWidth && r.width < minWidth) continue;
      if (norm(el.innerText) === exactNorm) best = el;
    }
    return best;
  }

  var hasTerm = !!document.querySelector('.xterm');
  if (!hasTerm) {
    var collapsedLbl = findTextEl('서버목록', 100, 0);
    if (collapsedLbl) {
      var now = Date.now();
      if (!window.__cmOxeExpandAt || (now - window.__cmOxeExpandAt) > 900) {
        window.__cmOxeExpandAt = now;
        var strip = collapsedLbl;
        for (var d = 0; d < 4 && strip; d++) {
          var b = strip.querySelector('button');
          if (b && isVis(b)) { b.click(); return 'EXPANDING_PANEL'; }
          if (strip.parentElement && strip.parentElement.getBoundingClientRect().width <= 120) strip = strip.parentElement;
          else break;
        }
        collapsedLbl.click();
      }
      return 'EXPANDING_PANEL';
    }

    var kwNorm = norm(kw);
    var shortNorm = norm(kw.replace(/^\d+\.\s*/, ''));
    var scope = document.body;
    var hdr = findTextEl('접속가능서버', 420, 0);
    if (hdr) {
      var p = hdr;
      for (var k = 0; k < 5 && p; k++) {
        var pr = p.getBoundingClientRect();
        if (pr.height > 200 && pr.width > 120 && pr.width <= 460) { scope = p; break; }
        p = p.parentElement;
      }
    }

    var items = Array.prototype.slice.call(scope.querySelectorAll('button,a,li,span,div'));
    var matchLeaf = null;
    var minArea = 99999999;
    for (var m = 0; m < items.length; m++) {
      var it = items[m];
      if (!isVis(it) || it.closest('.xterm')) continue;
      var tn = norm(it.innerText);
      if (tn.length === 0 || tn.length > 80) continue;
      if (tn === kwNorm || tn.indexOf(kwNorm) >= 0 || (shortNorm.length >= 4 && tn.indexOf(shortNorm) >= 0 && tn.indexOf('새터미널') < 0)) {
        var r2 = it.getBoundingClientRect();
        var area = r2.width * r2.height;
        if (r2.height > 10 && r2.height < 64 && area < minArea) {
          minArea = area;
          matchLeaf = it;
        }
      }
    }
    if (matchLeaf) {
      var clickTarget = matchLeaf.closest('button,[role=""button""],a,li') || matchLeaf;
      clickTarget.click();
      return 'CLICKED_SERVER';
    }

    // Filter via search input or expand folder nodes (관리자 -> 1.콜인프라 서버군)
    var searchInp = scope.querySelector('input[type=""text""],input[type=""search""],input:not([type])');
    if (searchInp && isVis(searchInp)) {
      var filterWord = kw.replace(/^\d+\.\s*/, '').trim() || kw;
      if (norm(searchInp.value).indexOf(norm(filterWord)) < 0) {
        var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
        setter.call(searchInp, filterWord);
        searchInp.dispatchEvent(new Event('input', { bubbles: true }));
        searchInp.dispatchEvent(new Event('change', { bubbles: true }));
        return 'FILTERED_LIST';
      }
    }

    var folders = Array.prototype.slice.call(scope.querySelectorAll('button,div,span,li'));
    for (var f = 0; f < folders.length; f++) {
      var fel = folders[f];
      if (!isVis(fel) || fel.dataset.cmFolderOpened === '1') continue;
      var ftxt = norm(fel.innerText);
      if (ftxt === '관리자' || ftxt.indexOf('콜인프라') >= 0) {
        var fr = fel.getBoundingClientRect();
        if (fr.height > 10 && fr.height < 56) {
          fel.dataset.cmFolderOpened = '1';
          (fel.closest('button,[role=""button""],li') || fel).click();
          return 'EXPANDING_FOLDER';
        }
      }
    }
    return 'WAIT_SERVER_LIST';
  }

  if (!shouldMin) return 'TERM_OPEN';

  var srvHeader = findTextEl('접속가능서버', 420, 0);
  if (srvHeader) {
    var box = srvHeader.parentElement;
    for (var depth = 0; depth < 4 && box; depth++) {
      var btns = box.querySelectorAll('button');
      if (btns && btns.length > 0 && box.getBoundingClientRect().height < 110 && box.getBoundingClientRect().width <= 460) {
        btns[btns.length - 1].click();
        return 'MINIMIZED_READY';
      }
      box = box.parentElement;
    }
  }
  var collapsedAfter = findTextEl('서버목록', 100, 0);
  if (collapsedAfter) return 'MINIMIZED_READY';
  return 'TERM_OPEN';
})(" + kwJson + ", " + minFlag + ");";

            try
            {
                return await EmsWebView.CoreWebView2.ExecuteScriptAsync(js);
            }
            catch
            {
                return "";
            }
        }

        private async Task ApplyEmsDomModesAsync()
        {
            if (!_webViewReady || EmsWebView.CoreWebView2 == null) return;
            bool termOnly = (ChkEmsTerminalOnly.IsChecked == true);
            string flagStr = termOnly ? "true" : "false";

            string js = @"
(function(onlyTerminal){
  function applyClean(){
    var onLogin = !!document.querySelector('input[type=""password""]');
    if (!onlyTerminal || onLogin) {
      document.querySelectorAll('[data-cm-oxe-hide]').forEach(function(el){ el.removeAttribute('data-cm-oxe-hide'); });
      document.querySelectorAll('[data-cm-oxe-full]').forEach(function(el){ el.removeAttribute('data-cm-oxe-full'); });
      var stOld = document.getElementById('cm-oxe-term-style');
      if (stOld) stOld.remove();
      window.dispatchEvent(new Event('resize'));
      return document.querySelector('.xterm') ? 'TERM_NORMAL' : 'NORMAL';
    }

    if (!document.getElementById('cm-oxe-term-style')) {
      var st = document.createElement('style');
      st.id = 'cm-oxe-term-style';
      st.textContent = '[data-cm-oxe-hide=""1""]{display:none !important;} [data-cm-oxe-full=""1""]{width:100% !important;max-width:100% !important;flex:1 1 100% !important;}';
      document.head.appendChild(st);
    }

    // Always locate and hide the outer LightGate app left navigation sidebar (works before AND after .xterm mounts!)
    var candidates = Array.prototype.slice.call(document.querySelectorAll('aside,nav,div'));
    for (var i = 0; i < candidates.length; i++) {
      var el = candidates[i];
      var rect = el.getBoundingClientRect();
      if (rect.width < 120 || rect.width > 340 || rect.left > 45 || rect.height < 250) continue;
      var txt = el.innerText || '';
      if (txt.indexOf('대시보드') >= 0 && (txt.indexOf('쿼리 실행') >= 0 || txt.indexOf('접속 현황') >= 0)) {
        if (txt.indexOf('새 터미널이 열립니다') >= 0 || el.querySelector('.xterm')) continue;
        // Walk up if immediate parent is also just the narrow nav shell
        var navShell = el;
        while (navShell.parentElement && navShell.parentElement !== document.body) {
          var pr = navShell.parentElement.getBoundingClientRect();
          if (pr.width <= 340 && pr.left <= 45) { navShell = navShell.parentElement; }
          else break;
        }
        navShell.setAttribute('data-cm-oxe-hide', '1');
        var parentRow = navShell.parentElement;
        if (parentRow) {
          for (var j = 0; j < parentRow.children.length; j++) {
            var ch = parentRow.children[j];
            if (ch !== navShell) ch.setAttribute('data-cm-oxe-full', '1');
          }
        }
        break;
      }
    }

    window.dispatchEvent(new Event('resize'));
    return document.querySelector('.xterm') ? 'TERM_ONLY_OK' : 'SIDEBAR_HIDDEN';
  }

  window.__cmOxeWantTermOnly = onlyTerminal;
  if (!window.__cmOxeObserver && window.MutationObserver && document.body) {
    window.__cmOxeObserver = new MutationObserver(function(){
      if (window.__cmOxeDebounce) clearTimeout(window.__cmOxeDebounce);
      window.__cmOxeDebounce = setTimeout(function(){
        if (window.__cmOxeWantTermOnly) applyClean();
      }, 180);
    });
    window.__cmOxeObserver.observe(document.body, { childList: true, subtree: true });
  }
  return applyClean();
})(" + flagStr + ");";

            try
            {
                string raw = await EmsWebView.CoreWebView2.ExecuteScriptAsync(js);
                if (_isEmsActionBusy) return;

                if (raw != null && (raw.Contains("TERM_ONLY_OK") || raw.Contains("TERM_NORMAL")))
                {
                    SetEmsTerminalState(true, "터미널 활성");
                }
                else if (raw != null && (raw.Contains("SIDEBAR_HIDDEN") || raw.Contains("NORMAL")))
                {
                    SetEmsTerminalState(false, _loggedLightGateLogin ? "터미널 미연결" : "LightGate 연결됨");
                }
            }
            catch { }
        }

        private async void ChkEmsTerminalOnly_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsInitialized || _isInitializingUi) return;
            _settings.EmsTerminalOnly = (ChkEmsTerminalOnly.IsChecked == true);
            _settingsService.Save(_settings);
            await ApplyEmsDomModesAsync();
        }

        private void BtnEmsPageReload_Click(object sender, RoutedEventArgs e)
        {
            if (!_webViewReady)
            {
                EnsureWebView2InitializedAsync();
                return;
            }
            unchecked { _emsLoopGen++; }
            _autoLoginInjectedForUrl = false;
            _autoOpenedServerDone = false;
            _loggedLightGateLogin = false;
            _loggedTerminalReady = false;
            SetEmsTerminalState(false, "LightGate 로딩 중...");
            EmsWebView.Reload();
        }

        private async Task<bool> TryCloseLightGateTerminalAsync()
        {
            if (!_webViewReady || EmsWebView.CoreWebView2 == null) return true;

            string closeJs = @"
(function(){
  try {
    var xt = document.querySelector('.xterm');
    if (!xt) return 'ALREADY_CLOSED';
    var xr = xt.getBoundingClientRect();
    var card = xt.parentElement;
    var bestCard = null;
    for (var i = 0; i < 6 && card && card !== document.body; i++) {
      var txt = card.innerText || '';
      if (txt.indexOf('접속 가능 서버') >= 0 || txt.indexOf('대시보드') >= 0) break;
      var cr = card.getBoundingClientRect();
      if (cr.top <= xr.top - 18) { bestCard = card; break; }
      card = card.parentElement;
    }
    var headerBtns = [];
    if (bestCard) {
      var allBtns = Array.prototype.slice.call(bestCard.querySelectorAll('button,[role=""button""]'));
      for (var b = 0; b < allBtns.length; b++) {
        var btn = allBtns[b];
        if (btn.offsetParent === null || btn.closest('.xterm')) continue;
        var br = btn.getBoundingClientRect();
        if (br.width <= 0 || br.height <= 0) continue;
        if ((br.top + br.height / 2) <= xr.top + 4) {
          headerBtns.push({ el: btn, left: br.left });
        }
      }
    }
    if (headerBtns.length > 0) {
      headerBtns.sort(function(a, b){ return a.left - b.left; });
      headerBtns[headerBtns.length - 1].el.click();
      return 'CLICKED_CLOSE';
    }
    return 'NO_CLOSE_BTN';
  } catch (e) { return 'ERR:' + e.message; }
})();";

            string res = "";
            try { res = await EmsWebView.CoreWebView2.ExecuteScriptAsync(closeJs); } catch { }
            if (res != null && res.Contains("ALREADY_CLOSED")) return true;
            if (res != null && res.Contains("NO_CLOSE_BTN"))
            {
                await TryPasteToLightGateTerminalAsync("\u0003\nexit\n");
            }

            string checkJs = @"
(function(){
  if (!document.querySelector('.xterm')) return 'CLOSED';
  var dlgs = Array.prototype.slice.call(document.querySelectorAll('[role=""dialog""],.modal'));
  for (var i = 0; i < dlgs.length; i++) {
    var d = dlgs[i];
    if (d.offsetParent === null) continue;
    var btns = Array.prototype.slice.call(d.querySelectorAll('button'));
    for (var j = 0; j < btns.length; j++) {
      var t = (btns[j].innerText || '').trim();
      if (t === '확인' || t === '종료' || t === '닫기') { btns[j].click(); return 'CONFIRMED'; }
    }
  }
  return 'OPEN';
})();";

            for (int w = 0; w < 18; w++)
            {
                await Task.Delay(140);
                try
                {
                    string st = await EmsWebView.CoreWebView2.ExecuteScriptAsync(checkJs);
                    if (st != null && st.Contains("CLOSED")) return true;
                }
                catch { }
            }
            return false;
        }

        private async Task<bool> OpenEmsTerminalInternalAsync(int myGen)
        {
            if (!_webViewReady || EmsWebView.CoreWebView2 == null) return false;
            string kw = (_settings.EmsServerKeyword ?? "9. PBX_A").Trim();
            bool minimizeAfterOpen = HasSavedEmsCredentials() || (_settings.EmsTerminalOnly);

            for (int i = 0; i < 16; i++)
            {
                if (myGen != _emsLoopGen) return false;
                if (_settings.EmsAutoLogin && !_autoLoginInjectedForUrl && HasSavedEmsCredentials())
                {
                    await TryInjectEmsAutoLoginAsync();
                }

                string r = await TryAutoOpenAndMinimizeServerAsync(kw, minimizeAfterOpen);
                if (r != null && (r.Contains("MINIMIZED_READY") || (!minimizeAfterOpen && r.Contains("TERM_OPEN")) || (r.Contains("TERM_OPEN") && i >= 6)))
                {
                    if (!_loggedLightGateLogin)
                    {
                        _loggedLightGateLogin = true;
                        AppendLog("OK", "LightGate 로그인 성공");
                    }
                    _autoOpenedServerDone = true;
                    SetEmsTerminalState(true, "터미널 활성");
                    if (!_loggedTerminalReady)
                    {
                        _loggedTerminalReady = true;
                        AppendLog("OK", string.Format("터미널 실행 완료 ({0})", kw));
                    }
                    await ApplyEmsDomModesAsync();
                    return true;
                }
                await Task.Delay(350);
            }
            await ApplyEmsDomModesAsync();
            return _isEmsTerminalConnected;
        }

        private async void BtnEmsTerminalToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_isEmsActionBusy) return;
            if (!_webViewReady || EmsWebView.CoreWebView2 == null)
            {
                EnsureWebView2InitializedAsync();
                return;
            }

            if (_isEmsTerminalConnected)
            {
                int myGen;
                unchecked { myGen = ++_emsLoopGen; }
                SetEmsActionBusy(true, "터미널 종료 중...");
                try
                {
                    _autoOpenedServerDone = true;
                    _loggedTerminalReady = false;
                    bool closed = await TryCloseLightGateTerminalAsync();
                    if (myGen == _emsLoopGen)
                    {
                        SetEmsTerminalState(!closed, closed ? "터미널 미연결" : "터미널 활성");
                        if (closed)
                        {
                            AppendLog("INFO", "터미널 연결 종료");
                        }
                        else
                        {
                            AppendLog("WARN", "[LightGate] 터미널 종료 실패");
                            AuditLogService.AppendError(_auditLogDir, "PBX", "LightGate:Disconnect", "터미널 종료 실패");
                        }
                    }
                }
                finally
                {
                    SetEmsActionBusy(false);
                }
                await ApplyEmsDomModesAsync();
            }
            else
            {
                int myGen;
                unchecked { myGen = ++_emsLoopGen; }
                SetEmsActionBusy(true, "터미널 연결 중...");
                try
                {
                    _autoOpenedServerDone = false;
                    bool ok = await OpenEmsTerminalInternalAsync(myGen);
                    if (myGen == _emsLoopGen && !ok)
                    {
                        SetEmsTerminalState(false, "터미널 미연결");
                        AppendLog("WARN", "[LightGate] 터미널 연결 실패");
                        AuditLogService.AppendError(_auditLogDir, "PBX", "LightGate:Connect", "터미널 연결 실패");
                    }
                }
                finally
                {
                    SetEmsActionBusy(false);
                }
                await ApplyEmsDomModesAsync();
            }
        }

        private async void BtnEmsRestartTerminal_Click(object sender, RoutedEventArgs e)
        {
            if (_isEmsActionBusy) return;
            if (!_webViewReady || EmsWebView.CoreWebView2 == null)
            {
                EnsureWebView2InitializedAsync();
                return;
            }

            int myGen;
            unchecked { myGen = ++_emsLoopGen; }
            SetEmsActionBusy(true, "터미널 재실행 중...");
            try
            {
                _autoOpenedServerDone = false;
                _loggedTerminalReady = false;
                await TryCloseLightGateTerminalAsync();
                await Task.Delay(250);
                if (myGen == _emsLoopGen)
                {
                    bool ok = await OpenEmsTerminalInternalAsync(myGen);
                    if (!ok)
                    {
                        SetEmsTerminalState(false, "터미널 미연결");
                        AppendLog("WARN", "[LightGate] 터미널 재실행 실패");
                        AuditLogService.AppendError(_auditLogDir, "PBX", "LightGate:Restart", "터미널 재실행 실패");
                    }
                }
            }
            finally
            {
                SetEmsActionBusy(false);
            }
            await ApplyEmsDomModesAsync();
        }

        private sealed class TokenErrorUnderlineAdorner : Adorner
        {
            private readonly TextBox _tb;
            private List<NumberErrorSpan> _spans = new List<NumberErrorSpan>();

            public TokenErrorUnderlineAdorner(TextBox tb) : base(tb)
            {
                _tb = tb;
                this.IsHitTestVisible = false;
                this.IsClipEnabled = true;
            }

            public void UpdateSpans(List<NumberErrorSpan> spans)
            {
                _spans = spans ?? new List<NumberErrorSpan>();
                this.InvalidateVisual();
            }

            protected override void OnRender(DrawingContext dc)
            {
                if (_tb == null || _spans == null || _spans.Count == 0) return;
                if (_tb.ActualWidth <= 4 || _tb.ActualHeight <= 4) return;
                string txt = _tb.Text ?? "";
                if (txt.Length == 0) return;

                Color errColor = Color.FromRgb(239, 68, 68);
                Brush strokeBrush = _tb.TryFindResource("ErrFgBrush") as Brush;
                if (strokeBrush is SolidColorBrush)
                {
                    Color resCol = ((SolidColorBrush)strokeBrush).Color;
                    errColor = (resCol.R > 220 && resCol.G > 140) ? Color.FromRgb(248, 113, 113) : resCol;
                }
                Pen wavePen = new Pen(new SolidColorBrush(errColor), 1.45);
                wavePen.StartLineCap = PenLineCap.Round;
                wavePen.EndLineCap = PenLineCap.Round;
                wavePen.Freeze();

                dc.PushClip(new RectangleGeometry(new Rect(1, 1, _tb.ActualWidth - 2, _tb.ActualHeight - 2)));
                try
                {
                    for (int i = 0; i < _spans.Count; i++)
                    {
                        NumberErrorSpan sp = _spans[i];
                        if (sp == null || sp.Length <= 0) continue;
                        if (sp.Start < 0 || sp.Start + sp.Length > txt.Length) continue;

                        try
                        {
                            int startLine = _tb.GetLineIndexFromCharacterIndex(sp.Start);
                            int endCharIdx = sp.Start + sp.Length - 1;
                            int endLine = _tb.GetLineIndexFromCharacterIndex(endCharIdx);
                            if (startLine < 0 || endLine < startLine) continue;

                            for (int line = startLine; line <= endLine; line++)
                            {
                                int lineStart = _tb.GetCharacterIndexFromLineIndex(line);
                                int lineLen = _tb.GetLineLength(line);
                                int segStart = Math.Max(sp.Start, lineStart);
                                int segEnd = Math.Min(sp.Start + sp.Length, lineStart + lineLen);
                                while (segEnd > segStart && (txt[segEnd - 1] == '\r' || txt[segEnd - 1] == '\n'))
                                {
                                    segEnd--;
                                }
                                if (segEnd <= segStart) continue;

                                Rect r0 = _tb.GetRectFromCharacterIndex(segStart, false);
                                Rect r1 = _tb.GetRectFromCharacterIndex(segEnd - 1, true);
                                if (r0.IsEmpty || r1.IsEmpty) continue;

                                double x0 = Math.Min(r0.Left, r1.Left);
                                double x1 = Math.Max(r0.Right, r1.Right);
                                if (x1 - x0 < 3) x1 = x0 + 6;
                                double y = Math.Max(r0.Bottom, r1.Bottom) - 0.4;

                                if (y < 0 || y > _tb.ActualHeight + 2) continue;

                                StreamGeometry geom = new StreamGeometry();
                                using (StreamGeometryContext ctx = geom.Open())
                                {
                                    ctx.BeginFigure(new Point(x0, y), false, false);
                                    double step = 3.0;
                                    bool up = false;
                                    for (double x = x0 + step; x < x1; x += step)
                                    {
                                        ctx.LineTo(new Point(x, up ? y - 0.95 : y + 0.95), true, false);
                                        up = !up;
                                    }
                                    ctx.LineTo(new Point(x1, y), true, false);
                                }
                                geom.Freeze();
                                dc.DrawGeometry(null, wavePen, geom);
                            }
                        }
                        catch { }
                    }
                }
                finally
                {
                    dc.Pop();
                }
            }
        }
    }
}
