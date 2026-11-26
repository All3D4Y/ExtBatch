using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CmOxeProvisioner.Models;
using CmOxeProvisioner.Services;
using Microsoft.Win32;

namespace CmOxeProvisioner.Views
{
    public class AgentLoginPoolItem
    {
        public string LoginId { get; set; }
        public bool IsInUseInCm { get; set; }
        public string InUseOwner { get; set; }
        public string SwitchName { get; set; }
    }

    public class AgentLoginPoolTile : INotifyPropertyChanged
    {
        private string _loginId = "";
        private bool _isInUseInCm = false;
        private string _inUseOwner = "";
        private bool _isPicked = false;
        private int _pickOrder = 0;
        private string _assignedToEmpId = "";

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged(string propName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propName));
            }
        }

        public string LoginId
        {
            get { return _loginId; }
            set
            {
                if (_loginId != value)
                {
                    _loginId = value ?? "";
                    RaisePropertyChanged("LoginId");
                }
            }
        }

        public bool IsInUseInCm
        {
            get { return _isInUseInCm; }
            set
            {
                if (_isInUseInCm != value)
                {
                    _isInUseInCm = value;
                    RaisePropertyChanged("IsInUseInCm");
                    RaisePropertyChanged("IsAssignedToTable");
                    RaisePropertyChanged("StatusCaption");
                }
            }
        }

        public string InUseOwner
        {
            get { return _inUseOwner; }
            set
            {
                if (_inUseOwner != value)
                {
                    _inUseOwner = value ?? "";
                    RaisePropertyChanged("InUseOwner");
                    RaisePropertyChanged("StatusCaption");
                }
            }
        }

        public bool IsPicked
        {
            get { return _isPicked; }
            set
            {
                if (_isPicked != value)
                {
                    _isPicked = value;
                    RaisePropertyChanged("IsPicked");
                    RaisePropertyChanged("PickBadgeVisibility");
                    RaisePropertyChanged("StatusCaption");
                }
            }
        }

        public int PickOrder
        {
            get { return _pickOrder; }
            set
            {
                if (_pickOrder != value)
                {
                    _pickOrder = value;
                    RaisePropertyChanged("PickOrder");
                    RaisePropertyChanged("PickOrderText");
                }
            }
        }

        public string AssignedToEmpId
        {
            get { return _assignedToEmpId; }
            set
            {
                string next = value ?? "";
                if (_assignedToEmpId != next)
                {
                    _assignedToEmpId = next;
                    RaisePropertyChanged("AssignedToEmpId");
                    RaisePropertyChanged("IsAssignedToTable");
                    RaisePropertyChanged("StatusCaption");
                }
            }
        }

        public bool IsAssignedToTable
        {
            get { return !_isInUseInCm && !string.IsNullOrEmpty(_assignedToEmpId); }
        }

        public Visibility PickBadgeVisibility
        {
            get { return _isPicked ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string PickOrderText
        {
            get { return _pickOrder > 0 ? _pickOrder.ToString() : ""; }
        }

        public string StatusCaption
        {
            get
            {
                if (_isInUseInCm)
                {
                    return string.IsNullOrEmpty(_inUseOwner) ? "사용중" : ("사용중 · " + _inUseOwner);
                }
                if (!string.IsNullOrEmpty(_assignedToEmpId))
                {
                    return "→ " + _assignedToEmpId;
                }
                return _isPicked ? "선택됨" : "사용 가능";
            }
        }
    }

    public class PersonPreviewRow : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _employeeId = "";
        private string _userName = "";
        private string _firstName = "";
        private string _lastName = "";
        private string _agentLoginId = "";
        private bool _isAgent = true;
        private string _statusText = "대기";

        protected bool SuppressAutoSync { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged(string propName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propName));
            }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    RaisePropertyChanged("IsSelected");
                }
            }
        }

        public virtual string EmployeeId
        {
            get { return _employeeId; }
            set
            {
                string next = (value ?? "").Trim();
                if (_employeeId != next)
                {
                    string prevEmp = _employeeId;
                    _employeeId = next;
                    RaisePropertyChanged("EmployeeId");

                    if (!SuppressAutoSync && (string.IsNullOrEmpty(_userName) || _userName == prevEmp))
                    {
                        UserName = next;
                    }
                    RefreshStatusText();
                }
            }
        }

        public virtual string UserName
        {
            get { return _userName; }
            set
            {
                string raw = (value ?? "").Trim();
                string next = (!SuppressAutoSync && raw.Length == 0) ? _employeeId : raw;
                if (_userName != next)
                {
                    string prevUser = _userName;
                    _userName = next;
                    RaisePropertyChanged("UserName");

                    if (!SuppressAutoSync && (string.IsNullOrEmpty(_firstName) || _firstName == prevUser))
                    {
                        FirstName = next;
                    }
                    RefreshStatusText();
                }
            }
        }

        public virtual string FirstName
        {
            get { return _firstName; }
            set
            {
                string raw = (value ?? "").Trim();
                string next = (!SuppressAutoSync && raw.Length == 0) ? _userName : raw;
                if (_firstName != next)
                {
                    _firstName = next;
                    RaisePropertyChanged("FirstName");
                }
            }
        }

        public virtual string LastName
        {
            get { return _lastName; }
            set
            {
                string next = (value ?? "").Trim();
                if (_lastName != next)
                {
                    _lastName = next;
                    RaisePropertyChanged("LastName");
                }
            }
        }

        public virtual string AgentLoginId
        {
            get { return _agentLoginId; }
            set
            {
                string next = (value ?? "").Trim();
                if (_agentLoginId != next)
                {
                    _agentLoginId = next;
                    RaisePropertyChanged("AgentLoginId");
                }
            }
        }

        public virtual bool IsAgent
        {
            get { return _isAgent; }
            set
            {
                if (_isAgent != value)
                {
                    _isAgent = value;
                    RaisePropertyChanged("IsAgent");
                }
            }
        }

        public string StatusText
        {
            get { return _statusText; }
            set
            {
                string next = value ?? "";
                if (_statusText != next)
                {
                    _statusText = next;
                    RaisePropertyChanged("StatusText");
                }
            }
        }

        public virtual void RefreshStatusText()
        {
            if (string.IsNullOrEmpty(_employeeId))
            {
                StatusText = "EmpID 필요";
            }
            else if (string.IsNullOrEmpty(_lastName))
            {
                StatusText = "센터명 필요";
            }
            else
            {
                StatusText = "대기";
            }
        }
    }

    public class PersonManageRow : PersonPreviewRow
    {
        public PersonManageRow()
        {
            SuppressAutoSync = true;
            IsSelected = false;
        }

        public string OriginalEmployeeId { get; set; }
        public string OriginalUserName { get; set; }
        public string OriginalFirstName { get; set; }
        public string OriginalLastName { get; set; }
        public string OriginalAgentLoginId { get; set; }
        public bool OriginalIsAgent { get; set; }

        private static string NormalizeLoginString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string[] parts = raw.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(", ", parts);
        }

        public bool IsGeneralModified
        {
            get
            {
                return (EmployeeId ?? "") != (OriginalEmployeeId ?? "") ||
                       (UserName ?? "") != (OriginalUserName ?? "") ||
                       (FirstName ?? "") != (OriginalFirstName ?? "") ||
                       (LastName ?? "") != (OriginalLastName ?? "") ||
                       IsAgent != OriginalIsAgent;
            }
        }

        public bool IsAgentLoginModified
        {
            get
            {
                return !string.Equals(
                    NormalizeLoginString(AgentLoginId),
                    NormalizeLoginString(OriginalAgentLoginId),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool IsModified
        {
            get { return IsGeneralModified || IsAgentLoginModified; }
        }

        public string AgentLoginDisplay
        {
            get
            {
                string cur = NormalizeLoginString(AgentLoginId);
                if (cur.Length > 0) return cur;
                string orig = NormalizeLoginString(OriginalAgentLoginId);
                if (orig.Length > 0) return "(해제 · " + orig + ")";
                return "(미연결)";
            }
        }

        public void SnapshotOriginals()
        {
            OriginalEmployeeId = EmployeeId ?? "";
            OriginalUserName = UserName ?? "";
            OriginalFirstName = FirstName ?? "";
            OriginalLastName = LastName ?? "";
            OriginalAgentLoginId = AgentLoginId ?? "";
            OriginalIsAgent = IsAgent;
            RaisePropertyChanged("AgentLoginDisplay");
            RefreshStatusText();
        }

        public void RevertToOriginals()
        {
            EmployeeId = OriginalEmployeeId ?? "";
            UserName = OriginalUserName ?? "";
            FirstName = OriginalFirstName ?? "";
            LastName = OriginalLastName ?? "";
            AgentLoginId = OriginalAgentLoginId ?? "";
            IsAgent = OriginalIsAgent;
            RaisePropertyChanged("AgentLoginDisplay");
            RefreshStatusText();
        }

        public void MarkApplied()
        {
            SnapshotOriginals();
            StatusText = "반영 완료";
        }

        public override void RefreshStatusText()
        {
            StatusText = IsModified ? "수정됨" : "원본";
        }

        public override string EmployeeId
        {
            get { return base.EmployeeId; }
            set { base.EmployeeId = value; RefreshStatusText(); }
        }

        public override string UserName
        {
            get { return base.UserName; }
            set { base.UserName = value; RefreshStatusText(); }
        }

        public override string FirstName
        {
            get { return base.FirstName; }
            set { base.FirstName = value; RefreshStatusText(); }
        }

        public override string LastName
        {
            get { return base.LastName; }
            set { base.LastName = value; RefreshStatusText(); }
        }

        public override string AgentLoginId
        {
            get { return base.AgentLoginId; }
            set
            {
                base.AgentLoginId = value;
                RaisePropertyChanged("AgentLoginDisplay");
                RefreshStatusText();
            }
        }

        public override bool IsAgent
        {
            get { return base.IsAgent; }
            set { base.IsAgent = value; RefreshStatusText(); }
        }
    }

    public class ExtCtiImportContext
    {
        public CustomerInfo Customer { get; set; }
        public List<string> CtiNumbers { get; set; }
    }

    public partial class PersonTabView : UserControl
    {
        private static readonly Regex DigitsOnlyRegex = new Regex(@"^\d+$", RegexOptions.Compiled);
        private static readonly Regex SeqRangeRegex = new Regex(@"^(\d+)[~\-](\d+)$", RegexOptions.Compiled);

        private readonly ObservableCollection<PersonPreviewRow> _rows = new ObservableCollection<PersonPreviewRow>();
        private readonly List<AgentLoginPoolTile> _allLoginTiles = new List<AgentLoginPoolTile>();
        private readonly ObservableCollection<AgentLoginPoolTile> _visibleLoginTiles = new ObservableCollection<AgentLoginPoolTile>();
        private readonly List<AgentLoginPoolTile> _pickedOrder = new List<AgentLoginPoolTile>();

        private readonly List<PersonManageRow> _allManageRows = new List<PersonManageRow>();
        private readonly ObservableCollection<PersonManageRow> _filteredManageRows = new ObservableCollection<PersonManageRow>();

        private List<CustomerInfo> _allCustomers = new List<CustomerInfo>();
        private List<CustomerInfo> _filteredCustomers = new List<CustomerInfo>();
        private CustomerInfo _selectedCustomer;
        private CustomerInfo _loadedManageCustomer;
        private string _loginPoolCustomerName = "";
        private Func<ExtCtiImportContext> _extCtiContextProvider;

        public Func<CustomerInfo, Task<List<AgentLoginPoolItem>>> FetchLoginPoolAsyncHandler { get; set; }
        public Func<CustomerInfo, List<PersonPreviewRow>, Task> DuplicateCheckAsyncHandler { get; set; }
        public Func<CustomerInfo, List<PersonPreviewRow>, bool, bool, Task<bool>> CreatePersonsAsyncHandler { get; set; }
        public Func<CustomerInfo, Task<PersonManageListResponse>> FetchManagePersonsAsyncHandler { get; set; }
        public Func<CustomerInfo, List<PersonManageRow>, Task<bool>> UpdateManagePersonsAsyncHandler { get; set; }
        public Func<CustomerInfo, List<PersonManageRow>, Task<bool>> DeleteManagePersonsAsyncHandler { get; set; }

        private string _lastAutoCenterName = "";
        private bool _pathsExpanded = false;
        private bool _isPasteMode = false;
        private bool _isManageSubView = false;
        private string _distMode = "Auto"; // Auto | Manual | Direct
        private bool _isGalleryDragging = false;
        private bool _dragPickTargetState = true;
        private bool _isSyncingSelectAll = false;
        private bool _isSyncingManageSelectAll = false;
        private bool _isSyncingCustomerCombo = false;
        private bool _isBusy = false;

        public PersonTabView()
        {
            InitializeComponent();
            DgPersons.ItemsSource = _rows;
            IcLoginPool.ItemsSource = _visibleLoginTiles;
            DgManagePersons.ItemsSource = _filteredManageRows;

            this.IsVisibleChanged += PersonTabView_IsVisibleChanged;
            DgPersons.SizeChanged += DgPersons_SizeChanged;
            DgManagePersons.SizeChanged += DgPersons_SizeChanged;

            UpdateSubModeVisuals();
            UpdateInputModeVisuals();
            UpdateDistModeVisuals();
            UpdateRuleEmpPreview();
            RefreshRowSummary();
            RefreshLoginPoolSummary();
            RefreshManageSummary();

            AppendPersonLog("INFO", "준비 완료");
        }

        private void PersonTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible)
            {
                Dispatcher.BeginInvoke(new Action(delegate()
                {
                    RefreshGridColumnWidths();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void DgPersons_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 2.0)
            {
                RefreshGridColumnWidths();
            }
        }

        private void RefreshGridColumnWidths()
        {
            RefreshStarColumns(DgPersons);
            RefreshStarColumns(DgManagePersons);
        }

        internal static void RefreshStarColumns(DataGrid dg)
        {
            if (dg == null || !dg.IsVisible || dg.ActualWidth < 200) return;
            List<int> starIdx = new List<int>();
            List<double> starVals = new List<double>();
            for (int i = 0; i < dg.Columns.Count; i++)
            {
                DataGridColumn col = dg.Columns[i];
                if (col.Width.IsStar)
                {
                    starIdx.Add(i);
                    starVals.Add(col.Width.Value);
                    col.Width = DataGridLength.Auto;
                }
            }
            dg.UpdateLayout();
            for (int i = 0; i < starIdx.Count; i++)
            {
                dg.Columns[starIdx[i]].Width = new DataGridLength(starVals[i], DataGridLengthUnitType.Star);
            }
            dg.UpdateLayout();
        }

        public void SetCustomers(List<CustomerInfo> customers)
        {
            _allCustomers = customers != null ? new List<CustomerInfo>(customers) : new List<CustomerInfo>();
            FilterCustomers(TxtPersonSearch.Text);
            CmbManageCustomer.ItemsSource = null;
            CmbManageCustomer.DisplayMemberPath = "DisplayName";
            CmbManageCustomer.ItemsSource = _allCustomers;
        }

        public void SelectCustomerByName(string customerName, bool switchToManage)
        {
            if (string.IsNullOrWhiteSpace(customerName) || _allCustomers == null) return;
            string target = customerName.Trim();
            CustomerInfo found = null;
            for (int i = 0; i < _allCustomers.Count; i++)
            {
                if (string.Equals(_allCustomers[i].name, target, StringComparison.OrdinalIgnoreCase))
                {
                    found = _allCustomers[i];
                    break;
                }
            }
            if (found == null) return;

            if (switchToManage)
            {
                BtnSubModeManage_Click(null, null);
                CmbManageCustomer.SelectedItem = found;
            }
            else
            {
                BtnSubModeCreate_Click(null, null);
                CmbPersonCustomer.SelectedItem = found;
            }
        }

        public void SetExtCtiContextProvider(Func<ExtCtiImportContext> provider)
        {
            _extCtiContextProvider = provider;
        }

        public void SetBusyState(bool busy)
        {
            _isBusy = busy;
            bool enabled = !busy;
            BtnFetchLoginPool.IsEnabled = enabled;
            BtnPersonCheck.IsEnabled = enabled;
            BtnPersonCreate.IsEnabled = enabled;
            BtnFillSample.IsEnabled = enabled;
            BtnResetPersonTab.IsEnabled = enabled;
            if (CmbManageCustomer != null) CmbManageCustomer.IsEnabled = enabled;
            if (BtnFetchExistingPersons != null) BtnFetchExistingPersons.IsEnabled = enabled;
            if (BtnManageOpenLoginPopup != null) BtnManageOpenLoginPopup.IsEnabled = enabled;
            if (BtnManageRevertChanges != null) BtnManageRevertChanges.IsEnabled = enabled;
            if (BtnManageApplyEdit != null) BtnManageApplyEdit.IsEnabled = enabled;
            if (BtnManageDeletePersons != null) BtnManageDeletePersons.IsEnabled = enabled;
            if (DgManagePersons != null) DgManagePersons.IsReadOnly = busy;
        }

        public void AppendExternalLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            Dispatcher.BeginInvoke(new Action(delegate()
            {
                string trimmed = line.TrimEnd();
                string formatted = string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss"), trimmed);
                AppendFormattedLogLine(formatted);
            }));
        }

        private void AppendFormattedLogLine(string formattedLine)
        {
            if (TxtPersonLog != null)
            {
                if (TxtPersonLog.Text.Length > 0) TxtPersonLog.AppendText(Environment.NewLine);
                TxtPersonLog.AppendText(formattedLine);
                TxtPersonLog.ScrollToEnd();
            }
            if (TxtManageLog != null)
            {
                if (TxtManageLog.Text.Length > 0) TxtManageLog.AppendText(Environment.NewLine);
                TxtManageLog.AppendText(formattedLine);
                TxtManageLog.ScrollToEnd();
            }
        }

        public void ApplyDuplicateCheckStatuses(HashSet<string> dupEmpIds, HashSet<string> dupUserNames)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                PersonPreviewRow r = _rows[i];
                if (!r.IsSelected) continue;
                bool dupE = (dupEmpIds != null && !string.IsNullOrEmpty(r.EmployeeId) && dupEmpIds.Contains(r.EmployeeId));
                bool dupU = (dupUserNames != null && !string.IsNullOrEmpty(r.UserName) && dupUserNames.Contains(r.UserName));
                if (dupE && dupU) r.StatusText = "중복(사번·ID)";
                else if (dupE) r.StatusText = "중복(사번)";
                else if (dupU) r.StatusText = "중복(이름)";
                else r.StatusText = "생성 가능";
            }
        }

        public void ApplyCreatedStatuses(PersonCreateSummary summary)
        {
            HashSet<string> createdEmpIds = new HashSet<string>(
                (summary != null && summary.createdEmpIds != null) ? summary.createdEmpIds : new List<string>(),
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _rows.Count; i++)
            {
                PersonPreviewRow r = _rows[i];
                if (r.IsSelected && !string.IsNullOrEmpty(r.EmployeeId) && createdEmpIds.Contains(r.EmployeeId))
                {
                    r.StatusText = "생성 완료";
                }
            }

            if (summary != null && summary.createdItems != null && summary.createdItems.Count > 0 &&
                !string.IsNullOrEmpty(_loginPoolCustomerName) &&
                string.Equals(_loginPoolCustomerName, summary.customer ?? "", StringComparison.OrdinalIgnoreCase))
            {
                Dictionary<string, string> claimLogins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < summary.createdItems.Count; i++)
                {
                    PersonCreateResultItemDto item = summary.createdItems[i];
                    if (item == null || string.IsNullOrWhiteSpace(item.agentLogin)) continue;
                    claimLogins[item.agentLogin.Trim()] = !string.IsNullOrWhiteSpace(item.userName) ? item.userName : (item.employeeId ?? "");
                }
                if (claimLogins.Count > 0)
                {
                    UpdateLoginPoolCacheStatus(null, claimLogins);
                }
            }
        }

        // ==================== SUB-MODE SWITCHER (CREATE vs MANAGE) ====================
        private void UpdateSubModeVisuals()
        {
            PnlCreateView.Visibility = _isManageSubView ? Visibility.Collapsed : Visibility.Visible;
            PnlManageView.Visibility = _isManageSubView ? Visibility.Visible : Visibility.Collapsed;
            if (BtnFillSample != null)
            {
                BtnFillSample.Visibility = _isManageSubView ? Visibility.Collapsed : Visibility.Visible;
            }
            UpdateModeBtn(BtnSubModeCreate, !_isManageSubView);
            UpdateModeBtn(BtnSubModeManage, _isManageSubView);
        }

        private void BtnSubModeCreate_Click(object sender, RoutedEventArgs e)
        {
            _isManageSubView = false;
            UpdateSubModeVisuals();
            Dispatcher.BeginInvoke(new Action(delegate() { RefreshGridColumnWidths(); }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void BtnSubModeManage_Click(object sender, RoutedEventArgs e)
        {
            _isManageSubView = true;
            UpdateSubModeVisuals();
            SyncManageCustomerSelection();
            Dispatcher.BeginInvoke(new Action(delegate() { RefreshGridColumnWidths(); }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void SyncManageCustomerSelection()
        {
            if (_selectedCustomer == null) return;
            _isSyncingCustomerCombo = true;
            for (int i = 0; i < _allCustomers.Count; i++)
            {
                if (_allCustomers[i].name == _selectedCustomer.name)
                {
                    CmbManageCustomer.SelectedIndex = i;
                    break;
                }
            }
            _isSyncingCustomerCombo = false;
        }

        // ==================== CUSTOMER & DEFAULTS ====================
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

            _isSyncingCustomerCombo = true;
            CmbPersonCustomer.ItemsSource = null;
            CmbPersonCustomer.DisplayMemberPath = "DisplayName";
            CmbPersonCustomer.ItemsSource = _filteredCustomers;
            LblPersonCustHeader.Text = string.Format("({0}개)", _filteredCustomers.Count);

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
                CmbPersonCustomer.SelectedIndex = restoreIdx;
                _selectedCustomer = (restoreIdx >= 0) ? _filteredCustomers[restoreIdx] : null;
            }
            else
            {
                CmbPersonCustomer.SelectedIndex = -1;
                _selectedCustomer = null;
            }
            _isSyncingCustomerCombo = false;
            UpdateSelectedCustomerInfo();
        }

        private static string ResolveCenterLastName(CustomerInfo c)
        {
            if (c == null) return "";
            if (!string.IsNullOrEmpty(c.floor) && string.Equals(c.floor, "TEST", StringComparison.OrdinalIgnoreCase))
            {
                return "TEST";
            }
            return c.name ?? "";
        }

        private void UpdateSelectedCustomerInfo()
        {
            CustomerInfo c = _selectedCustomer;
            if (c == null)
            {
                TxtPersonFloorBadge.Text = "-";
                TxtPersonPathTarget.Text = "Persons 폴더  : (센터 미선택)";
                TxtPersonPathLogin.Text = "Agent Login   : (센터 미선택)";
                return;
            }

            TxtPersonFloorBadge.Text = string.IsNullOrEmpty(c.floor) ? "-" : c.floor;
            string expectedPersons;
            if (string.Equals(c.floor, "TEST", StringComparison.OrdinalIgnoreCase))
            {
                expectedPersons = "TEST";
            }
            else if (string.IsNullOrEmpty(c.floor))
            {
                expectedPersons = c.name ?? "";
            }
            else
            {
                expectedPersons = string.Format("{0} > {1}", c.floor, c.name);
            }
            TxtPersonPathTarget.Text = "Persons 폴더  : Configuration > Environment > Persons > " + expectedPersons;
            TxtPersonPathLogin.Text = (c.agentLogin != null && c.agentLogin.Length > 0)
                ? "Agent Login   : " + string.Join(" > ", c.agentLogin)
                : "Agent Login   : [경로 없음]";

            string nextLastName = ResolveCenterLastName(c);
            string curLastName = (TxtPersonLastName.Text ?? "").Trim();
            if (string.IsNullOrEmpty(curLastName) || curLastName == _lastAutoCenterName)
            {
                TxtPersonLastName.Text = nextLastName;
            }
            _lastAutoCenterName = nextLastName;
        }

        private void ResetLoginPoolIfCustomerChanged(CustomerInfo nextCustomer)
        {
            string nextName = (nextCustomer != null) ? (nextCustomer.name ?? "") : "";
            if (_allLoginTiles.Count == 0 && string.IsNullOrEmpty(_loginPoolCustomerName)) return;
            if (!string.Equals(_loginPoolCustomerName, nextName, StringComparison.OrdinalIgnoreCase))
            {
                _loginPoolCustomerName = "";
                _allLoginTiles.Clear();
                _visibleLoginTiles.Clear();
                _pickedOrder.Clear();
                RefreshLoginPoolSummary();
            }
        }

        private void CmbPersonCustomer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingCustomerCombo) return;
            _selectedCustomer = CmbPersonCustomer.SelectedItem as CustomerInfo;
            UpdateSelectedCustomerInfo();
            ResetLoginPoolIfCustomerChanged(_selectedCustomer);
            SyncManageCustomerSelection();
        }

        private void CmbManageCustomer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingCustomerCombo) return;
            CustomerInfo c = CmbManageCustomer.SelectedItem as CustomerInfo;
            if (c != null)
            {
                _selectedCustomer = c;
                _isSyncingCustomerCombo = true;
                for (int i = 0; i < _filteredCustomers.Count; i++)
                {
                    if (_filteredCustomers[i].name == c.name)
                    {
                        CmbPersonCustomer.SelectedIndex = i;
                        break;
                    }
                }
                _isSyncingCustomerCombo = false;
                UpdateSelectedCustomerInfo();
                ResetLoginPoolIfCustomerChanged(_selectedCustomer);
            }
        }

        private void TxtPersonSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterCustomers(TxtPersonSearch.Text);
        }

        private void TxtPersonLastName_TextChanged(object sender, TextChangedEventArgs e)
        {
            string newLast = (TxtPersonLastName.Text ?? "").Trim();
            for (int i = 0; i < _rows.Count; i++)
            {
                PersonPreviewRow r = _rows[i];
                if (string.IsNullOrEmpty(r.LastName) || r.LastName == _lastAutoCenterName)
                {
                    r.LastName = newLast;
                    r.RefreshStatusText();
                }
            }
            if (newLast.Length > 0)
            {
                _lastAutoCenterName = newLast;
            }
            UpdateSelectedRowPreview();
        }

        private void BtnTogglePersonPaths_Click(object sender, RoutedEventArgs e)
        {
            _pathsExpanded = !_pathsExpanded;
            BdPersonPaths.Visibility = _pathsExpanded ? Visibility.Visible : Visibility.Collapsed;
            BtnTogglePersonPaths.Content = _pathsExpanded ? "경로 ▴" : "경로 ▾";
        }

        // ==================== INPUT MODE SWITCHER (RULE vs PASTE) ====================
        private void UpdateInputModeVisuals()
        {
            PnlRuleMode.Visibility = _isPasteMode ? Visibility.Collapsed : Visibility.Visible;
            PnlPasteMode.Visibility = _isPasteMode ? Visibility.Visible : Visibility.Collapsed;
            UpdateModeBtn(BtnModeRule, !_isPasteMode);
            UpdateModeBtn(BtnModePaste, _isPasteMode);
        }

        private static void UpdateModeBtn(Button btn, bool active)
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

        private void BtnModeRule_Click(object sender, RoutedEventArgs e)
        {
            _isPasteMode = false;
            UpdateInputModeVisuals();
        }

        private void BtnModePaste_Click(object sender, RoutedEventArgs e)
        {
            _isPasteMode = true;
            UpdateInputModeVisuals();
        }

        private void RbUserMode_Changed(object sender, RoutedEventArgs e)
        {
            if (PnlManualNames == null || RbUserManualList == null) return;
            PnlManualNames.Visibility = (RbUserManualList.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TxtRuleInputs_Changed(object sender, TextChangedEventArgs e)
        {
            UpdateRuleEmpPreview();
        }

        private static List<string> ExpandSequenceTokens(string raw, out string error)
        {
            error = null;
            List<string> list = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return list;

            string[] tokens = raw.Split(new[] { ',', ';', '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            HashSet<string> seen = new HashSet<string>();

            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i].Trim();
                if (tok.Length == 0) continue;

                Match m = SeqRangeRegex.Match(tok);
                if (m.Success)
                {
                    string sStr = m.Groups[1].Value;
                    string eStr = m.Groups[2].Value;
                    long sNum, eNum;
                    if (!long.TryParse(sStr, out sNum) || !long.TryParse(eStr, out eNum))
                    {
                        error = string.Format("잘못된 범위입니다: '{0}'", tok);
                        return list;
                    }
                    if (eNum < sNum)
                    {
                        error = string.Format("끝 번호가 시작 번호보다 작습니다: '{0}'", tok);
                        return list;
                    }
                    if (eNum - sNum + 1 > 500)
                    {
                        error = string.Format("한 번에 최대 500명까지 생성할 수 있습니다: '{0}'", tok);
                        return list;
                    }
                    int width = Math.Max(sStr.Length, eStr.Length);
                    for (long v = sNum; v <= eNum; v++)
                    {
                        string formatted = v.ToString().PadLeft(width, '0');
                        if (seen.Add(formatted)) list.Add(formatted);
                    }
                }
                else
                {
                    if (seen.Add(tok)) list.Add(tok);
                }
            }
            return list;
        }

        private void UpdateRuleEmpPreview()
        {
            if (TxtRuleEmpPreview == null) return;
            string prefix = (TxtPersonPrefix.Text ?? "").Trim();
            string err;
            List<string> seq = ExpandSequenceTokens(TxtRuleSeq.Text, out err);
            if (!string.IsNullOrEmpty(err))
            {
                TxtRuleEmpPreview.Text = "형식 확인 필요";
                return;
            }
            if (seq.Count == 0)
            {
                TxtRuleEmpPreview.Text = prefix.Length > 0 ? string.Format("예시: {0}001~", prefix) : "예시: PTEST001~";
                return;
            }
            if (seq.Count == 1)
            {
                TxtRuleEmpPreview.Text = string.Format("{0}{1} (1명)", prefix, seq[0]);
                return;
            }
            TxtRuleEmpPreview.Text = string.Format("{0}{1} ~ {0}{2} ({3}명)", prefix, seq[0], seq[seq.Count - 1], seq.Count);
        }

        private void GenerateRowsFromRule(bool replaceAll)
        {
            string prefix = (TxtPersonPrefix.Text ?? "").Trim();
            string seqErr;
            List<string> seqList = ExpandSequenceTokens(TxtRuleSeq.Text, out seqErr);
            if (!string.IsNullOrEmpty(seqErr))
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "Employee ID 범위 오류", seqErr);
                return;
            }
            if (seqList.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "입력 필요", "Employee ID 번호 또는 범위를 입력하세요. (예: 001~010)");
                return;
            }

            List<string> customNames = new List<string>();
            if (RbUserManualList.IsChecked == true && !string.IsNullOrWhiteSpace(TxtRuleNames.Text))
            {
                string[] rawNames = TxtRuleNames.Text.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.None);
                for (int i = 0; i < rawNames.Length; i++)
                {
                    customNames.Add((rawNames[i] ?? "").Trim());
                }
            }

            string lastName = (TxtPersonLastName.Text ?? "").Trim();
            if (string.IsNullOrEmpty(lastName) && _selectedCustomer != null)
            {
                lastName = ResolveCenterLastName(_selectedCustomer);
                TxtPersonLastName.Text = lastName;
            }
            bool isAgent = (ChkRuleIsAgent.IsChecked == true);

            if (replaceAll)
            {
                _rows.Clear();
            }

            int added = 0;
            for (int i = 0; i < seqList.Count; i++)
            {
                string empId = prefix + seqList[i];
                string userName = (i < customNames.Count && customNames[i].Length > 0) ? customNames[i] : empId;

                PersonPreviewRow row = new PersonPreviewRow();
                row.IsSelected = true;
                row.EmployeeId = empId;
                row.UserName = userName;
                row.FirstName = userName;
                row.LastName = lastName;
                row.AgentLoginId = "";
                row.IsAgent = isAgent;
                row.RefreshStatusText();
                _rows.Add(row);
                added++;
            }

            if (_rows.Count > 0 && DgPersons.SelectedItem == null)
            {
                DgPersons.SelectedIndex = 0;
            }
            RefreshRowSummary();
            AppendPersonLog("INFO", string.Format("프리셋 목록 반영: {0}명 {1}", added, replaceAll ? "새로 채움" : "추가"));
        }

        private void BtnRuleAppend_Click(object sender, RoutedEventArgs e)
        {
            GenerateRowsFromRule(false);
        }

        private void BtnRuleReplace_Click(object sender, RoutedEventArgs e)
        {
            GenerateRowsFromRule(true);
        }

        private void ApplyPastedRows(bool replaceAll)
        {
            string raw = TxtPasteInput.Text ?? "";
            if (string.IsNullOrWhiteSpace(raw))
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "입력 필요", "붙여넣을 명단을 입력하세요.");
                return;
            }

            string lastName = (TxtPersonLastName.Text ?? "").Trim();
            if (string.IsNullOrEmpty(lastName) && _selectedCustomer != null)
            {
                lastName = ResolveCenterLastName(_selectedCustomer);
                TxtPersonLastName.Text = lastName;
            }
            bool isAgent = (ChkRuleIsAgent.IsChecked == true);

            List<PersonPreviewRow> parsed = new List<PersonPreviewRow>();
            string[] lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;

                string[] cols;
                if (line.Contains("\t"))
                {
                    cols = line.Split('\t');
                }
                else if (line.Contains(","))
                {
                    cols = line.Split(',');
                }
                else
                {
                    cols = Regex.Split(line, @"\s+");
                }

                string col0 = cols.Length > 0 ? cols[0].Trim() : "";
                if (col0.Length == 0) continue;
                string upper0 = col0.ToUpperInvariant();
                if (i == 0 && (upper0 == "EMPID" || upper0 == "EMPLOYEE ID" || col0 == "사번" || upper0 == "AP ID"))
                {
                    continue;
                }

                string col1 = cols.Length > 1 ? cols[1].Trim() : "";
                string col2 = cols.Length > 2 ? cols[2].Trim() : "";

                string empId = col0;
                string userName;
                string loginId;
                if (cols.Length >= 3)
                {
                    userName = col1.Length > 0 ? col1 : empId;
                    loginId = col2;
                }
                else if (cols.Length == 2)
                {
                    if (DigitsOnlyRegex.IsMatch(col1) && col1.Length >= 4)
                    {
                        userName = empId;
                        loginId = col1;
                    }
                    else
                    {
                        userName = col1.Length > 0 ? col1 : empId;
                        loginId = "";
                    }
                }
                else
                {
                    userName = empId;
                    loginId = "";
                }

                PersonPreviewRow r = new PersonPreviewRow();
                r.IsSelected = true;
                r.EmployeeId = empId;
                r.UserName = userName;
                r.FirstName = userName;
                r.LastName = lastName;
                r.AgentLoginId = loginId;
                r.IsAgent = isAgent;
                r.RefreshStatusText();
                parsed.Add(r);
            }

            if (parsed.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "파싱 결과 없음", "유효한 Person 행을 찾지 못했습니다.");
                return;
            }

            if (replaceAll)
            {
                _rows.Clear();
            }
            for (int i = 0; i < parsed.Count; i++)
            {
                _rows.Add(parsed[i]);
            }
            if (_rows.Count > 0 && DgPersons.SelectedItem == null)
            {
                DgPersons.SelectedIndex = 0;
            }
            RefreshRowSummary();
            AppendPersonLog("INFO", string.Format("명단 붙여넣기: {0}명 {1}", parsed.Count, replaceAll ? "새로 채움" : "추가"));
        }

        private void BtnPasteAppend_Click(object sender, RoutedEventArgs e)
        {
            ApplyPastedRows(false);
        }

        private void BtnPasteReplace_Click(object sender, RoutedEventArgs e)
        {
            ApplyPastedRows(true);
        }

        // ==================== AGENT LOGIN PICKER / DISTRIBUTION / DIRECT INPUT ====================
        private void UpdateDistModeVisuals()
        {
            if (PnlAutoDistBar == null) return;

            bool isAuto = (_distMode == "Auto");
            bool isManual = (_distMode == "Manual");
            bool isDirect = (_distMode == "Direct");

            PnlAutoDistBar.Visibility = isAuto ? Visibility.Visible : Visibility.Collapsed;
            PnlManualDistBar.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;
            PnlDirectDistBar.Visibility = isDirect ? Visibility.Visible : Visibility.Collapsed;

            PnlGalleryHost.Visibility = isDirect ? Visibility.Collapsed : Visibility.Visible;
            PnlDirectInputHost.Visibility = isDirect ? Visibility.Visible : Visibility.Collapsed;

            UpdateModeBtn(BtnDistModeAuto, isAuto);
            UpdateModeBtn(BtnDistModeManual, isManual);
            UpdateModeBtn(BtnDistModeDirect, isDirect);

            if (isManual)
            {
                TxtGalleryTip.Text = "💡 [수동 분배] 아래 표에서 대상 Person 행을 고른 뒤, 위 사용 가능 Login 타일을 클릭하면 즉시 1:1 매핑됩니다.";
                UpdateManualTargetBadge();
            }
            else if (isAuto)
            {
                TxtGalleryTip.Text = "💡 [자동 분배] Ctrl 키 없이 클릭·드래그로 다중 선택한 뒤 [자동 1:1 분배 ↓]를 누르세요. (미선택 시 사용 가능 전체 순차 분배)";
            }
        }

        private void BtnDistModeAuto_Click(object sender, RoutedEventArgs e)
        {
            _distMode = "Auto";
            UpdateDistModeVisuals();
        }

        private void BtnDistModeManual_Click(object sender, RoutedEventArgs e)
        {
            _distMode = "Manual";
            ClearPickedOrder();
            if (_rows.Count > 0 && DgPersons.SelectedItem == null)
            {
                DgPersons.SelectedIndex = 0;
            }
            UpdateDistModeVisuals();
        }

        private void BtnDistModeDirect_Click(object sender, RoutedEventArgs e)
        {
            _distMode = "Direct";
            UpdateDistModeVisuals();
        }

        private void UpdateManualTargetBadge()
        {
            if (TxtManualTargetBadge == null) return;
            PersonPreviewRow r = DgPersons.SelectedItem as PersonPreviewRow;
            if (r == null)
            {
                TxtManualTargetBadge.Text = "대상 행 ▶ (표에서 Person을 선택하세요)";
                return;
            }
            int idx = _rows.IndexOf(r) + 1;
            string curLogin = string.IsNullOrEmpty(r.AgentLoginId) ? "미지정" : r.AgentLoginId;
            TxtManualTargetBadge.Text = string.Format("대상 ▶ #{0} {1} ({2}) [Login: {3}]", idx, r.EmployeeId, r.UserName, curLogin);
        }

        private async void BtnFetchLoginPool_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            if (_selectedCustomer == null)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "센터 미선택", "먼저 좌측 [센터 및 기본값]에서 센터를 선택하세요.");
                return;
            }
            if (FetchLoginPoolAsyncHandler == null)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "기능 미연결", "Config 조회 핸들러가 연결되지 않았습니다.");
                return;
            }

            SetBusyState(true);
            try
            {
                List<AgentLoginPoolItem> items = await FetchLoginPoolAsyncHandler(_selectedCustomer);
                ApplyFetchedLoginPool(items, _selectedCustomer.name);
            }
            catch (Exception ex)
            {
                AppendPersonLog("ERR", "Agent Login 목록 조회 실패: " + ex.Message);
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "Agent Login 조회 실패", ex.Message);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        public void ApplyFetchedLoginPool(List<AgentLoginPoolItem> items)
        {
            ApplyFetchedLoginPool(items, _selectedCustomer != null ? _selectedCustomer.name : "");
        }

        public void ApplyFetchedLoginPool(List<AgentLoginPoolItem> items, string customerName)
        {
            _loginPoolCustomerName = customerName ?? "";
            _allLoginTiles.Clear();
            _pickedOrder.Clear();

            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    AgentLoginPoolItem it = items[i];
                    if (it == null || string.IsNullOrEmpty(it.LoginId)) continue;
                    AgentLoginPoolTile tile = new AgentLoginPoolTile();
                    tile.LoginId = it.LoginId;
                    tile.IsInUseInCm = it.IsInUseInCm;
                    tile.InUseOwner = it.InUseOwner ?? "";
                    _allLoginTiles.Add(tile);
                }
            }

            SyncLoginTileAssignments();
            RebuildVisibleLoginTiles();
        }

        private void RebuildVisibleLoginTiles()
        {
            _visibleLoginTiles.Clear();
            bool hideInUse = (ChkHideInUseLogins != null && ChkHideInUseLogins.IsChecked == true);
            for (int i = 0; i < _allLoginTiles.Count; i++)
            {
                AgentLoginPoolTile t = _allLoginTiles[i];
                if (hideInUse && t.IsInUseInCm) continue;
                _visibleLoginTiles.Add(t);
            }
            RefreshLoginPoolSummary();
        }

        private void ChkHideInUseLogins_Changed(object sender, RoutedEventArgs e)
        {
            RebuildVisibleLoginTiles();
        }

        private void SyncLoginTileAssignments()
        {
            Dictionary<string, string> loginToEmp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _rows.Count; i++)
            {
                string lid = (_rows[i].AgentLoginId ?? "").Trim();
                if (lid.Length > 0 && !loginToEmp.ContainsKey(lid))
                {
                    loginToEmp[lid] = string.IsNullOrEmpty(_rows[i].EmployeeId) ? ("#" + (i + 1)) : _rows[i].EmployeeId;
                }
            }

            for (int i = 0; i < _allLoginTiles.Count; i++)
            {
                AgentLoginPoolTile t = _allLoginTiles[i];
                string assignedEmp;
                if (!t.IsInUseInCm && loginToEmp.TryGetValue(t.LoginId, out assignedEmp))
                {
                    t.AssignedToEmpId = assignedEmp;
                }
                else
                {
                    t.AssignedToEmpId = "";
                }
            }
            RefreshLoginPoolSummary();
        }

        private void RefreshLoginPoolSummary()
        {
            if (TxtLoginPoolCount == null || PnlEmptyGalleryHint == null) return;
            if (_allLoginTiles.Count == 0)
            {
                TxtLoginPoolCount.Text = "미조회 (조회 버튼으로 불러오기)";
                PnlEmptyGalleryHint.Visibility = Visibility.Visible;
                TxtGalleryPickedCount.Text = "선택 0개";
                return;
            }

            PnlEmptyGalleryHint.Visibility = Visibility.Collapsed;
            int availCount = 0;
            int assignedCount = 0;
            for (int i = 0; i < _allLoginTiles.Count; i++)
            {
                if (!_allLoginTiles[i].IsInUseInCm)
                {
                    availCount++;
                    if (!string.IsNullOrEmpty(_allLoginTiles[i].AssignedToEmpId)) assignedCount++;
                }
            }
            TxtLoginPoolCount.Text = string.Format("전체 {0} · 사용 가능 {1} (배정 {2})", _allLoginTiles.Count, availCount, assignedCount);
            TxtGalleryPickedCount.Text = string.Format("선택 {0}개", _pickedOrder.Count);
        }

        private void SetTilePicked(AgentLoginPoolTile tile, bool picked)
        {
            if (tile == null || tile.IsInUseInCm) return;
            if (picked)
            {
                if (!tile.IsPicked)
                {
                    tile.IsPicked = true;
                    if (!_pickedOrder.Contains(tile)) _pickedOrder.Add(tile);
                }
            }
            else
            {
                if (tile.IsPicked)
                {
                    tile.IsPicked = false;
                    _pickedOrder.Remove(tile);
                }
            }
            RefreshPickedOrderBadges();
        }

        private void RefreshPickedOrderBadges()
        {
            for (int i = 0; i < _pickedOrder.Count; i++)
            {
                _pickedOrder[i].PickOrder = i + 1;
            }
            TxtGalleryPickedCount.Text = string.Format("선택 {0}개", _pickedOrder.Count);
        }

        private void ClearPickedOrder()
        {
            for (int i = 0; i < _allLoginTiles.Count; i++)
            {
                _allLoginTiles[i].IsPicked = false;
                _allLoginTiles[i].PickOrder = 0;
            }
            _pickedOrder.Clear();
            TxtGalleryPickedCount.Text = "선택 0개";
        }

        private void LoginTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            FrameworkElement fe = sender as FrameworkElement;
            AgentLoginPoolTile tile = fe != null ? fe.DataContext as AgentLoginPoolTile : null;
            if (tile == null || tile.IsInUseInCm) return;

            if (_distMode == "Manual")
            {
                AssignTileManuallyToCurrentRow(tile);
                e.Handled = true;
                return;
            }

            _isGalleryDragging = true;
            _dragPickTargetState = !tile.IsPicked;
            SetTilePicked(tile, _dragPickTargetState);
            e.Handled = true;
        }

        private void LoginTile_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isGalleryDragging || _distMode != "Auto") return;
            if (Mouse.LeftButton != MouseButtonState.Pressed)
            {
                _isGalleryDragging = false;
                return;
            }
            FrameworkElement fe = sender as FrameworkElement;
            AgentLoginPoolTile tile = fe != null ? fe.DataContext as AgentLoginPoolTile : null;
            if (tile != null && !tile.IsInUseInCm)
            {
                SetTilePicked(tile, _dragPickTargetState);
            }
        }

        private void PersonTabView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isGalleryDragging = false;
        }

        private void AssignTileManuallyToCurrentRow(AgentLoginPoolTile tile)
        {
            if (_rows.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "대상 Person 없음", "먼저 좌측 [Person 프리셋 설정]에서 미리보기 목록을 생성하세요.");
                return;
            }

            PersonPreviewRow target = DgPersons.SelectedItem as PersonPreviewRow;
            if (target == null)
            {
                target = _rows[0];
                DgPersons.SelectedIndex = 0;
            }

            if (target.AgentLoginId == tile.LoginId)
            {
                target.AgentLoginId = "";
                RefreshRowSummary();
                AppendPersonLog("INFO", string.Format("[수동 분배 해제] {0}의 Login({1}) 배정을 해제했습니다.", target.EmployeeId, tile.LoginId));
                return;
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != target && _rows[i].AgentLoginId == tile.LoginId)
                {
                    _rows[i].AgentLoginId = "";
                }
            }

            target.AgentLoginId = tile.LoginId;
            AppendPersonLog("INFO", string.Format("[수동 분배] {0} ({1}) ← Login {2}", target.EmployeeId, target.UserName, tile.LoginId));

            int curIdx = _rows.IndexOf(target);
            int nextIdx = -1;
            for (int i = curIdx + 1; i < _rows.Count; i++)
            {
                if (string.IsNullOrEmpty(_rows[i].AgentLoginId))
                {
                    nextIdx = i;
                    break;
                }
            }
            if (nextIdx < 0 && curIdx + 1 < _rows.Count)
            {
                nextIdx = curIdx + 1;
            }
            if (nextIdx >= 0)
            {
                DgPersons.SelectedIndex = nextIdx;
                DgPersons.ScrollIntoView(_rows[nextIdx]);
            }

            RefreshRowSummary();
        }

        private void BtnUnassignSelectedRow_Click(object sender, RoutedEventArgs e)
        {
            PersonPreviewRow r = DgPersons.SelectedItem as PersonPreviewRow;
            if (r == null) return;
            r.AgentLoginId = "";
            RefreshRowSummary();
        }

        private void BtnSelectAllAvailLogins_Click(object sender, RoutedEventArgs e)
        {
            if (_allLoginTiles.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "목록 미조회", "먼저 [사용 가능 Login 조회] 버튼을 눌러 Config에서 목록을 불러오세요.");
                return;
            }
            for (int i = 0; i < _visibleLoginTiles.Count; i++)
            {
                AgentLoginPoolTile t = _visibleLoginTiles[i];
                if (!t.IsInUseInCm)
                {
                    SetTilePicked(t, true);
                }
            }
        }

        private void BtnClearLoginPicks_Click(object sender, RoutedEventArgs e)
        {
            ClearPickedOrder();
        }

        private void AssignLoginSequenceToRows(List<string> sourceLogins, string methodDesc)
        {
            if (_rows.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "대상 Person 없음", "미리보기 목록이 비어 있습니다. 좌측에서 먼저 목록을 생성하세요.");
                return;
            }

            List<PersonPreviewRow> checkedRows = new List<PersonPreviewRow>();
            List<PersonPreviewRow> targetRows = new List<PersonPreviewRow>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsSelected)
                {
                    checkedRows.Add(_rows[i]);
                    if (string.IsNullOrEmpty(_rows[i].AgentLoginId))
                    {
                        targetRows.Add(_rows[i]);
                    }
                }
            }
            if (checkedRows.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "선택된 행 없음", "미리보기 표에서 체크된 Person 행이 없습니다.");
                return;
            }
            bool overwriteMode = false;
            if (targetRows.Count == 0)
            {
                targetRows.AddRange(checkedRows);
                overwriteMode = true;
            }

            HashSet<PersonPreviewRow> targetSet = new HashSet<PersonPreviewRow>(targetRows);
            HashSet<string> protectedLogins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _rows.Count; i++)
            {
                if (targetSet.Contains(_rows[i])) continue;
                string lid = (_rows[i].AgentLoginId ?? "").Trim();
                if (lid.Length > 0) protectedLogins.Add(lid);
            }

            List<string> usableLogins = new List<string>();
            HashSet<string> seenSrc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sourceLogins.Count; i++)
            {
                string lid = (sourceLogins[i] ?? "").Trim();
                if (lid.Length == 0 || protectedLogins.Contains(lid)) continue;
                if (seenSrc.Add(lid)) usableLogins.Add(lid);
            }

            if (usableLogins.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "분배 가능한 Login 없음", "사용 가능하면서 다른 행에 배정되지 않은 Agent Login이 없습니다.");
                return;
            }

            int count = Math.Min(targetRows.Count, usableLogins.Count);
            for (int i = 0; i < count; i++)
            {
                string lid = usableLogins[i];
                for (int k = 0; k < targetRows.Count; k++)
                {
                    if (targetRows[k] != targetRows[i] && string.Equals(targetRows[k].AgentLoginId, lid, StringComparison.OrdinalIgnoreCase))
                    {
                        targetRows[k].AgentLoginId = "";
                    }
                }
                targetRows[i].AgentLoginId = lid;
            }

            RefreshRowSummary();
            AppendPersonLog("INFO", string.Format("[{0}] 체크된 {1}명에게 Agent Login 1:1 매칭 완료{2}",
                methodDesc, count, overwriteMode ? " (기존 배정 덮어씀)" : ""));

            if (usableLogins.Count < targetRows.Count)
            {
                PopupDialogs.ShowSyntaxToast(
                    Window.GetWindow(this),
                    true,
                    "일부 Login 배정 완료",
                    string.Format("{0}건을 순서대로 매칭했습니다. (남은 {1}명은 미배정)", count, targetRows.Count - count));
            }
        }

        private void BtnApplyAutoDist_Click(object sender, RoutedEventArgs e)
        {
            if (_allLoginTiles.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "목록 미조회", "먼저 [사용 가능 Login 조회] 버튼으로 Config의 Agent Login 목록을 불러오세요.");
                return;
            }

            int checkedCount = 0;
            bool hasUnassigned = false;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (!_rows[i].IsSelected) continue;
                checkedCount++;
                if (string.IsNullOrEmpty(_rows[i].AgentLoginId))
                {
                    hasUnassigned = true;
                }
            }
            if (checkedCount == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "선택된 행 없음", "미리보기 표에서 분배할 Person의 체크박스를 선택하세요.");
                return;
            }

            HashSet<string> protectedLogins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _rows.Count; i++)
            {
                string lid = (_rows[i].AgentLoginId ?? "").Trim();
                if (lid.Length == 0) continue;
                if (hasUnassigned || !_rows[i].IsSelected)
                {
                    protectedLogins.Add(lid);
                }
            }

            List<string> sourceLogins = new List<string>();
            if (_pickedOrder.Count > 0)
            {
                for (int i = 0; i < _pickedOrder.Count; i++)
                {
                    AgentLoginPoolTile t = _pickedOrder[i];
                    if (t.IsInUseInCm || protectedLogins.Contains(t.LoginId)) continue;
                    sourceLogins.Add(t.LoginId);
                }
            }
            else
            {
                for (int i = 0; i < _allLoginTiles.Count; i++)
                {
                    AgentLoginPoolTile t = _allLoginTiles[i];
                    if (t.IsInUseInCm || protectedLogins.Contains(t.LoginId)) continue;
                    if (hasUnassigned && !string.IsNullOrEmpty(t.AssignedToEmpId)) continue;
                    sourceLogins.Add(t.LoginId);
                }
            }

            if (sourceLogins.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "분배 가능한 Login 없음", "사용 가능한 Agent Login이 없거나 이미 모두 배정되었습니다.");
                return;
            }

            AssignLoginSequenceToRows(sourceLogins, _pickedOrder.Count > 0 ? "자동 분배 · 선택순" : "자동 분배 · 순차");
        }

        private void BtnImportCtiLogins_Click(object sender, RoutedEventArgs e)
        {
            if (_extCtiContextProvider == null) return;
            ExtCtiImportContext ctx = _extCtiContextProvider();
            if (ctx == null) return;

            if (ctx.Customer != null)
            {
                for (int i = 0; i < _filteredCustomers.Count; i++)
                {
                    if (_filteredCustomers[i].name == ctx.Customer.name)
                    {
                        CmbPersonCustomer.SelectedIndex = i;
                        break;
                    }
                }
            }

            if (ctx.CtiNumbers == null || ctx.CtiNumbers.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(
                    Window.GetWindow(this),
                    false,
                    "CTI 번호 없음",
                    "내선 · CTI 탭의 CTI 입력칸에 유효한 번호가 없습니다.");
                return;
            }

            TxtRuleLogins.Text = string.Join(", ", ctx.CtiNumbers.ToArray());
            AppendPersonLog("INFO", string.Format("내선·CTI 탭에서 Agent Login {0}건을 가져왔습니다.", ctx.CtiNumbers.Count));
        }

        private void BtnApplyDirectLogins_Click(object sender, RoutedEventArgs e)
        {
            NumberParseResult parsed = OxeScriptService.ParseNumbersStrict(TxtRuleLogins.Text ?? "");
            if (parsed.Errors.Count > 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "Agent Login ID 형식 오류", string.Join("\n", parsed.Errors.ToArray()));
                return;
            }
            if (parsed.Items.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "입력 필요", "적용할 Agent Login ID 또는 범위를 입력하세요.");
                return;
            }

            if (_allLoginTiles.Count > 0)
            {
                Dictionary<string, AgentLoginPoolTile> poolMap = new Dictionary<string, AgentLoginPoolTile>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _allLoginTiles.Count; i++)
                {
                    if (!poolMap.ContainsKey(_allLoginTiles[i].LoginId)) poolMap[_allLoginTiles[i].LoginId] = _allLoginTiles[i];
                }
                List<string> usedConflicts = new List<string>();
                List<string> missingIds = new List<string>();
                for (int i = 0; i < parsed.Items.Count; i++)
                {
                    string lid = parsed.Items[i];
                    AgentLoginPoolTile t;
                    if (!poolMap.TryGetValue(lid, out t))
                    {
                        missingIds.Add(lid);
                    }
                    else if (t.IsInUseInCm)
                    {
                        usedConflicts.Add(string.Format("{0}({1})", lid, t.InUseOwner));
                    }
                }
                if (usedConflicts.Count > 0)
                {
                    TxtDirectLoginStatus.Text = "⚠ 다른 Person 사용 중: " + string.Join(", ", usedConflicts.ToArray());
                    PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "사용 중인 Agent Login 포함", "이미 다른 Person에 연결된 Agent Login이 포함되어 있습니다:\n" + string.Join(", ", usedConflicts.ToArray()));
                    return;
                }
                if (missingIds.Count > 0)
                {
                    TxtDirectLoginStatus.Text = string.Format("안내: 조회 목록에 없는 Login {0}건 포함", missingIds.Count);
                }
                else
                {
                    TxtDirectLoginStatus.Text = string.Format("확인 완료: {0}건 모두 사용 가능 상태입니다.", parsed.Items.Count);
                }
            }

            AssignLoginSequenceToRows(parsed.Items, "직접 입력");
        }

        private void BtnClearTableLogins_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsSelected) _rows[i].AgentLoginId = "";
            }
            RefreshRowSummary();
            AppendPersonLog("INFO", "선택된 행의 Agent Login 매핑을 초기화했습니다.");
        }

        // ==================== PREVIEW TABLE ACTIONS & EXECUTION ====================
        private void BtnAddEmptyRow_Click(object sender, RoutedEventArgs e)
        {
            PersonPreviewRow r = new PersonPreviewRow();
            r.IsSelected = true;
            r.LastName = (TxtPersonLastName.Text ?? "").Trim();
            r.IsAgent = (ChkRuleIsAgent.IsChecked == true);
            r.RefreshStatusText();
            _rows.Add(r);
            DgPersons.SelectedItem = r;
            RefreshRowSummary();
        }

        private void BtnDeleteSelectedRows_Click(object sender, RoutedEventArgs e)
        {
            List<PersonPreviewRow> toRemove = new List<PersonPreviewRow>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsSelected) toRemove.Add(_rows[i]);
            }
            if (toRemove.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "선택된 행 없음", "제거할 Person 행을 체크박스로 선택하세요.");
                return;
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                _rows.Remove(toRemove[i]);
            }
            RefreshRowSummary();
        }

        private void BtnClearRows_Click(object sender, RoutedEventArgs e)
        {
            _rows.Clear();
            RefreshRowSummary();
        }

        private void ChkSelectAllRows_Click(object sender, RoutedEventArgs e)
        {
            if (_isSyncingSelectAll) return;
            bool checkAll = (ChkSelectAllRows.IsChecked == true);
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].IsSelected = checkAll;
            }
            RefreshRowSummary();
        }

        private void RowSelectionCheckBox_Click(object sender, RoutedEventArgs e)
        {
            RefreshRowSummary();
        }

        private void RowAgentCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateSelectedRowPreview();
        }

        private void DgPersons_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectedRowPreview();
            UpdateManualTargetBadge();
        }

        private void DgPersons_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate()
            {
                RefreshRowSummary();
                UpdateSelectedRowPreview();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void RefreshRowSummary()
        {
            int total = _rows.Count;
            int selectedCount = 0;
            int loginCount = 0;
            for (int i = 0; i < total; i++)
            {
                PersonPreviewRow r = _rows[i];
                if (r.IsSelected) selectedCount++;
                if (!string.IsNullOrEmpty(r.AgentLoginId)) loginCount++;
            }

            TxtPersonCountBadge.Text = string.Format("총 {0}명 (선택 {1}) · Login 연결 {2}건", total, selectedCount, loginCount);
            PnlEmptyGridOverlay.Visibility = (total == 0) ? Visibility.Visible : Visibility.Collapsed;
            RefreshGridColumnWidths();
            SyncLoginTileAssignments();
            UpdateManualTargetBadge();

            _isSyncingSelectAll = true;
            if (total == 0) ChkSelectAllRows.IsChecked = false;
            else if (selectedCount == total) ChkSelectAllRows.IsChecked = true;
            else if (selectedCount == 0) ChkSelectAllRows.IsChecked = false;
            else ChkSelectAllRows.IsChecked = null;
            _isSyncingSelectAll = false;

            UpdateSelectedRowPreview();
        }

        private void UpdateSelectedRowPreview()
        {
            PersonPreviewRow r = DgPersons.SelectedItem as PersonPreviewRow;
            if (r == null && _rows.Count > 0) r = _rows[0];
            if (r == null)
            {
                TxtSelectedCmPreview.Text = "[Config 입력 미리보기] 행을 선택하면 General / Agent Login 매핑 값이 표시됩니다.";
                return;
            }

            string emp = string.IsNullOrEmpty(r.EmployeeId) ? "(미입력)" : r.EmployeeId;
            string user = string.IsNullOrEmpty(r.UserName) ? emp : r.UserName;
            string first = string.IsNullOrEmpty(r.FirstName) ? user : r.FirstName;
            string last = string.IsNullOrEmpty(r.LastName) ? "(센터 미지정)" : r.LastName;
            string login = string.IsNullOrEmpty(r.AgentLoginId) ? "(미연결)" : r.AgentLoginId;

            TxtSelectedCmPreview.Text = string.Format(
                "[Config 미리보기] General -> UserName: {0} | First: {1} | Last: {2} | EmpID: {3}   ||   Agent Info -> IsAgent: {4} | Agent Login: {5}",
                user, first, last, emp, r.IsAgent ? "Y" : "N", login);
        }

        private List<PersonPreviewRow> GetTargetSelectedRows()
        {
            List<PersonPreviewRow> target = new List<PersonPreviewRow>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsSelected) target.Add(_rows[i]);
            }
            return target;
        }

        private bool ValidateTargetRows(out List<PersonPreviewRow> targets)
        {
            targets = GetTargetSelectedRows();
            if (targets.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "대상 없음", "실행할 Person 행이 없습니다. 좌측에서 프리셋을 추가하거나 체크박스를 선택하세요.");
                return false;
            }

            List<string> missingEmp = new List<string>();
            HashSet<string> seenEmp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> dupEmpInList = new List<string>();
            HashSet<string> seenLogin = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> dupLoginInList = new List<string>();

            for (int i = 0; i < targets.Count; i++)
            {
                PersonPreviewRow r = targets[i];
                if (string.IsNullOrEmpty(r.EmployeeId))
                {
                    missingEmp.Add(string.Format("#{0}", i + 1));
                }
                else if (!seenEmp.Add(r.EmployeeId))
                {
                    dupEmpInList.Add(r.EmployeeId);
                }
                if (ChkPersonAddLogin.IsChecked == true && !string.IsNullOrEmpty(r.AgentLoginId))
                {
                    if (!seenLogin.Add(r.AgentLoginId))
                    {
                        dupLoginInList.Add(r.AgentLoginId);
                    }
                }
            }

            if (missingEmp.Count > 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "Employee ID 누락", "Employee ID가 비어 있는 행이 있습니다: " + string.Join(", ", missingEmp.ToArray()));
                return false;
            }
            if (dupEmpInList.Count > 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "목록 내 Employee ID 중복", "미리보기 목록 안에 중복된 Employee ID가 있습니다:\n" + string.Join(", ", dupEmpInList.ToArray()));
                return false;
            }
            if (dupLoginInList.Count > 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "목록 내 Agent Login 중복", "1:1 매칭 원칙 위반: 같은 Agent Login ID가 여러 Person에 지정되었습니다:\n" + string.Join(", ", dupLoginInList.ToArray()));
                return false;
            }
            return true;
        }

        private async void BtnPersonCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            List<PersonPreviewRow> targets;
            if (!ValidateTargetRows(out targets)) return;

            if (DuplicateCheckAsyncHandler == null)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "기능 미연결", "Config 중복 체크 핸들러가 연결되지 않았습니다.");
                return;
            }

            SetBusyState(true);
            try
            {
                await DuplicateCheckAsyncHandler(_selectedCustomer, targets);
            }
            catch (Exception ex)
            {
                AppendPersonLog("ERR", "Employee ID 중복 체크 오류: " + ex.Message);
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "중복 체크 실패", ex.Message);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        private async void BtnPersonCreate_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            if (_selectedCustomer == null)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "센터 미선택", "대상 센터를 선택하세요.");
                return;
            }

            bool doGeneral = (ChkPersonGeneral.IsChecked == true);
            bool doAgentLogin = (ChkPersonAddLogin.IsChecked == true);
            if (!doGeneral && !doAgentLogin)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "옵션 미선택", "General 또는 Agent Login 적용하기 중 하나 이상을 선택하세요.");
                return;
            }

            List<PersonPreviewRow> targets;
            if (!ValidateTargetRows(out targets)) return;

            int loginCnt = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (!string.IsNullOrEmpty(targets[i].AgentLoginId)) loginCnt++;
            }

            StringBuilder detail = new StringBuilder();
            detail.AppendLine(string.Format("• 대상 센터: {0}", _selectedCustomer.DisplayName));
            detail.AppendLine(string.Format("• 생성 인원: {0}명 (Agent Login 매핑: {1}명)", targets.Count, doAgentLogin ? loginCnt : 0));
            detail.AppendLine(string.Format("• 적용 항목: General({0}) · Agent Login 적용하기({1})",
                doGeneral ? "Y" : "N",
                doAgentLogin ? "Y" : "N"));
            PersonPreviewRow first = targets[0];
            PersonPreviewRow last = targets[targets.Count - 1];
            detail.AppendLine(string.Format("• 범위 요약: {0} ({1}) ~ {2} ({3})",
                first.EmployeeId, first.UserName, last.EmployeeId, last.UserName));
            detail.Append("• Password: 공란 (Config 기본값 자동 설정)");

            bool confirmed = PopupDialogs.ConfirmAction(
                Window.GetWindow(this),
                "Config Person 일괄 생성 확인",
                string.Format("미리보기 목록 {0}명으로 Config Person을 생성하시겠습니까?", targets.Count),
                detail.ToString(),
                "일괄 생성 실행");
            if (!confirmed) return;

            if (CreatePersonsAsyncHandler == null)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "기능 미연결", "Config 생성 핸들러가 연결되지 않았습니다.");
                return;
            }

            SetBusyState(true);
            try
            {
                await CreatePersonsAsyncHandler(_selectedCustomer, targets, doGeneral, doAgentLogin);
            }
            catch (Exception ex)
            {
                AppendPersonLog("ERR", "Config Person 일괄 생성 실패: " + ex.Message);
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "Config Person 일괄 생성 실패", ex.Message);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        // ==================== MANAGE TAB (QUERY / EDIT / DELETE) ====================
        private CustomerInfo ResolveManageTargetCustomer()
        {
            return (CmbManageCustomer != null ? CmbManageCustomer.SelectedItem as CustomerInfo : null) ?? _selectedCustomer;
        }

        private List<AgentLoginPoolItem> GetCachedLoginPoolItems(string customerName)
        {
            if (string.IsNullOrEmpty(customerName) ||
                !string.Equals(_loginPoolCustomerName, customerName, StringComparison.OrdinalIgnoreCase) ||
                _allLoginTiles.Count == 0)
            {
                return null;
            }
            List<AgentLoginPoolItem> clone = new List<AgentLoginPoolItem>(_allLoginTiles.Count);
            for (int i = 0; i < _allLoginTiles.Count; i++)
            {
                AgentLoginPoolTile t = _allLoginTiles[i];
                AgentLoginPoolItem item = new AgentLoginPoolItem();
                item.LoginId = t.LoginId;
                item.IsInUseInCm = t.IsInUseInCm;
                item.InUseOwner = t.InUseOwner;
                clone.Add(item);
            }
            return clone;
        }

        private void UpdateLoginPoolCacheStatus(IEnumerable<string> releaseLogins, Dictionary<string, string> claimLoginOwners)
        {
            if (_allLoginTiles.Count == 0) return;

            HashSet<string> releaseSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (releaseLogins != null)
            {
                foreach (string s in releaseLogins)
                {
                    if (!string.IsNullOrWhiteSpace(s)) releaseSet.Add(s.Trim());
                }
            }

            for (int i = 0; i < _allLoginTiles.Count; i++)
            {
                AgentLoginPoolTile t = _allLoginTiles[i];
                string newOwner;
                if (claimLoginOwners != null && claimLoginOwners.TryGetValue(t.LoginId, out newOwner))
                {
                    t.IsInUseInCm = true;
                    t.InUseOwner = newOwner ?? "";
                    if (t.IsPicked)
                    {
                        t.IsPicked = false;
                        t.PickOrder = 0;
                        _pickedOrder.Remove(t);
                    }
                }
                else if (releaseSet.Contains(t.LoginId))
                {
                    t.IsInUseInCm = false;
                    t.InUseOwner = "";
                }
            }
            RefreshPickedOrderBadges();
            SyncLoginTileAssignments();
            RebuildVisibleLoginTiles();
        }

        private async void BtnFetchExistingPersons_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            CustomerInfo targetCust = ResolveManageTargetCustomer();
            if (targetCust == null)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "센터 미선택", "먼저 상단에서 조회할 센터를 선택하세요.");
                return;
            }
            if (FetchManagePersonsAsyncHandler == null)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "기능 미연결", "Config Person 조회 핸들러가 연결되지 않았습니다.");
                return;
            }

            SetBusyState(true);
            try
            {
                PersonManageListResponse resp = await FetchManagePersonsAsyncHandler(targetCust);
                ApplyFetchedManagePersons(targetCust, resp);
            }
            catch (Exception ex)
            {
                AppendPersonLog("ERR", "Config Person 목록 조회 실패: " + ex.Message);
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "Config Person 조회 실패", ex.Message);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        public void ApplyFetchedManagePersons(CustomerInfo customer, PersonManageListResponse resp)
        {
            _loadedManageCustomer = customer;
            _allManageRows.Clear();

            string folderPath = (resp != null && !string.IsNullOrEmpty(resp.folderPath))
                ? resp.folderPath
                : (customer != null ? customer.DisplayName : "");
            if (BdManageFolderChip != null && TxtManageLoadedFolder != null)
            {
                TxtManageLoadedFolder.Text = folderPath;
                BdManageFolderChip.Visibility = string.IsNullOrEmpty(folderPath) ? Visibility.Collapsed : Visibility.Visible;
            }

            if (resp != null && resp.items != null)
            {
                for (int i = 0; i < resp.items.Count; i++)
                {
                    PersonManageItemDto d = resp.items[i];
                    if (d == null) continue;
                    PersonManageRow row = new PersonManageRow();
                    row.EmployeeId = d.employeeId ?? "";
                    row.UserName = d.userName ?? "";
                    row.FirstName = d.firstName ?? "";
                    row.LastName = d.lastName ?? "";
                    row.IsAgent = d.isAgent;
                    row.AgentLoginId = d.agentLogin ?? "";
                    row.SnapshotOriginals();
                    row.PropertyChanged += ManageRow_PropertyChanged;
                    _allManageRows.Add(row);
                }
            }

            ApplyManageFilter();
            Dispatcher.BeginInvoke(new Action(delegate() { RefreshStarColumns(DgManagePersons); }), System.Windows.Threading.DispatcherPriority.Render);
            if (resp != null)
            {
                AppendPersonLog("OK", string.Format("'{0}' 기존 Person {1}명 조회 완료 ({2:N1}초)",
                    customer != null ? customer.name : "", _allManageRows.Count, resp.elapsedSeconds));
            }
        }

        private void ManageRow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e != null && (e.PropertyName == "StatusText" || e.PropertyName == "IsSelected" || e.PropertyName == "AgentLoginId"))
            {
                RefreshManageSummary();
            }
        }

        private void ApplyManageFilter()
        {
            string q = (TxtManageFilter.Text ?? "").Trim().ToLowerInvariant();
            _filteredManageRows.Clear();
            for (int i = 0; i < _allManageRows.Count; i++)
            {
                PersonManageRow r = _allManageRows[i];
                bool match = q.Length == 0 ||
                    (r.EmployeeId ?? "").ToLowerInvariant().Contains(q) ||
                    (r.UserName ?? "").ToLowerInvariant().Contains(q) ||
                    (r.FirstName ?? "").ToLowerInvariant().Contains(q) ||
                    (r.LastName ?? "").ToLowerInvariant().Contains(q) ||
                    (r.AgentLoginId ?? "").ToLowerInvariant().Contains(q);
                if (match) _filteredManageRows.Add(r);
            }
            RefreshManageSummary();
        }

        private void TxtManageFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyManageFilter();
        }

        private void ChkManageSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_isSyncingManageSelectAll) return;
            bool checkAll = (ChkManageSelectAll.IsChecked == true);
            for (int i = 0; i < _filteredManageRows.Count; i++)
            {
                _filteredManageRows[i].IsSelected = checkAll;
            }
            RefreshManageSummary();
        }

        private void ManageRowSelection_Click(object sender, RoutedEventArgs e)
        {
            RefreshManageSummary();
        }

        private void ManageRowAgentCheck_Click(object sender, RoutedEventArgs e)
        {
            RefreshManageSummary();
        }

        private void DgManagePersons_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate()
            {
                RefreshManageSummary();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void RefreshManageSummary()
        {
            int visibleCount = _filteredManageRows.Count;
            int selected = 0;
            int modified = 0;
            for (int i = 0; i < visibleCount; i++)
            {
                if (_filteredManageRows[i].IsSelected) selected++;
            }
            for (int i = 0; i < _allManageRows.Count; i++)
            {
                if (_allManageRows[i].IsModified) modified++;
            }

            TxtManageCountBadge.Text = string.Format("조회 {0}명 · 선택 {1}명 · 변경 {2}건", _allManageRows.Count, selected, modified);
            PnlEmptyManageOverlay.Visibility = (visibleCount == 0) ? Visibility.Visible : Visibility.Collapsed;
            RefreshGridColumnWidths();

            _isSyncingManageSelectAll = true;
            if (visibleCount == 0 || selected == 0) ChkManageSelectAll.IsChecked = false;
            else if (selected == visibleCount) ChkManageSelectAll.IsChecked = true;
            else ChkManageSelectAll.IsChecked = null;
            _isSyncingManageSelectAll = false;
        }

        private void OpenAgentLoginPickerForTargets(List<PersonManageRow> targets)
        {
            if (_isBusy) return;
            if (targets == null || targets.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "대상 없음", "Agent Login을 변경할 Person을 선택하세요.");
                return;
            }

            CustomerInfo targetCust = _loadedManageCustomer ?? ResolveManageTargetCustomer();
            if (targetCust == null)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "센터 미선택", "먼저 센터를 선택하고 목록을 조회하세요.");
                return;
            }

            HashSet<PersonManageRow> targetSet = new HashSet<PersonManageRow>(targets);
            Dictionary<string, string> reservedByOthers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _allManageRows.Count; i++)
            {
                PersonManageRow other = _allManageRows[i];
                if (targetSet.Contains(other)) continue;
                string lid = (other.AgentLoginId ?? "").Trim();
                if (lid.Length == 0) continue;
                string[] parts = lid.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int k = 0; k < parts.Length; k++)
                {
                    if (!reservedByOthers.ContainsKey(parts[k]))
                    {
                        reservedByOthers[parts[k]] = string.IsNullOrEmpty(other.UserName) ? other.EmployeeId : other.UserName;
                    }
                }
            }

            List<AgentLoginPoolItem> initialPool = GetCachedLoginPoolItems(targetCust.name);
            AgentLoginPickerWindow dlg = new AgentLoginPickerWindow(
                targetCust,
                targets,
                initialPool,
                reservedByOthers,
                FetchLoginPoolAsyncHandler,
                _extCtiContextProvider);
            Window ownerWin = Window.GetWindow(this);
            if (ownerWin != null && ownerWin.IsVisible) dlg.Owner = ownerWin;

            bool? ok = dlg.ShowDialog();
            if (dlg.LatestFetchedPool != null)
            {
                ApplyFetchedLoginPool(dlg.LatestFetchedPool, targetCust.name);
            }
            if (ok == true)
            {
                RefreshManageSummary();
                AppendPersonLog("INFO", string.Format("Agent Login 변경 임시 적용 완료 (대상 {0}명 · 대상 체크 후 [수정 반영] 시 Config 저장)", targets.Count));
            }
        }

        private void BtnRowChangeAgentLogin_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            DgManagePersons.CommitEdit(DataGridEditingUnit.Cell, true);
            DgManagePersons.CommitEdit(DataGridEditingUnit.Row, true);
            FrameworkElement fe = sender as FrameworkElement;
            PersonManageRow row = fe != null ? fe.DataContext as PersonManageRow : null;
            if (row == null) return;
            OpenAgentLoginPickerForTargets(new List<PersonManageRow> { row });
        }

        private void BtnManageOpenLoginPopup_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            DgManagePersons.CommitEdit(DataGridEditingUnit.Cell, true);
            DgManagePersons.CommitEdit(DataGridEditingUnit.Row, true);

            List<PersonManageRow> targets = new List<PersonManageRow>();
            for (int i = 0; i < _filteredManageRows.Count; i++)
            {
                if (_filteredManageRows[i].IsSelected) targets.Add(_filteredManageRows[i]);
            }
            if (targets.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "체크된 대상 없음", "목록에서 Agent Login을 변경할 Person을 체크박스로 선택한 뒤 클릭하세요.");
                return;
            }
            OpenAgentLoginPickerForTargets(targets);
        }

        private void BtnManageRevertChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            DgManagePersons.CommitEdit(DataGridEditingUnit.Cell, true);
            DgManagePersons.CommitEdit(DataGridEditingUnit.Row, true);

            int checkedCount = 0;
            List<PersonManageRow> checkedMod = new List<PersonManageRow>();
            for (int i = 0; i < _filteredManageRows.Count; i++)
            {
                PersonManageRow r = _filteredManageRows[i];
                if (!r.IsSelected) continue;
                checkedCount++;
                if (r.IsModified) checkedMod.Add(r);
            }

            if (checkedCount == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "체크된 대상 없음", "원본으로 복원할 Person을 체크박스로 선택한 뒤 실행하세요.");
                return;
            }
            if (checkedMod.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), true, "변경 내역 없음", "체크된 행 중 원본에서 수정된 항목이 없습니다.");
                return;
            }

            for (int i = 0; i < checkedMod.Count; i++) checkedMod[i].RevertToOriginals();
            RefreshManageSummary();
            AppendPersonLog("INFO", string.Format("체크된 수정 내용 {0}건 원본으로 복원 완료", checkedMod.Count));
        }

        public void ApplyUpdatedManageRows(PersonUpdateSummary summary)
        {
            if (summary == null || summary.updatedItems == null) return;
            List<string> releaseLogins = new List<string>();
            Dictionary<string, string> claimLogins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < summary.updatedItems.Count; i++)
            {
                PersonUpdateResultItemDto u = summary.updatedItems[i];
                if (u == null) continue;
                PersonManageRow row = null;
                for (int k = 0; k < _allManageRows.Count; k++)
                {
                    if (string.Equals(_allManageRows[k].OriginalUserName, u.originalUserName, StringComparison.OrdinalIgnoreCase))
                    {
                        row = _allManageRows[k];
                        break;
                    }
                }
                if (row == null) continue;

                string oldLogin = row.OriginalAgentLoginId ?? "";
                string newLogin = u.agentLogin ?? "";
                if (!string.Equals(oldLogin, newLogin, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string s in oldLogin.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        releaseLogins.Add(s);
                    }
                    foreach (string s in newLogin.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        claimLogins[s] = u.userName ?? "";
                    }
                }

                row.EmployeeId = u.employeeId ?? "";
                row.UserName = u.userName ?? "";
                row.FirstName = u.firstName ?? "";
                row.LastName = u.lastName ?? "";
                row.IsAgent = u.isAgent;
                row.AgentLoginId = newLogin;
                row.MarkApplied();
            }

            UpdateLoginPoolCacheStatus(releaseLogins, claimLogins);
            RefreshManageSummary();
        }

        public void ApplyDeletedManageRows(PersonDeleteSummary summary)
        {
            if (summary == null || summary.deletedItems == null) return;
            List<string> releaseLogins = new List<string>();

            for (int i = 0; i < summary.deletedItems.Count; i++)
            {
                PersonDeleteResultItemDto d = summary.deletedItems[i];
                if (d == null) continue;
                if (d.unlinkedLogins != null)
                {
                    releaseLogins.AddRange(d.unlinkedLogins);
                }

                for (int k = _allManageRows.Count - 1; k >= 0; k--)
                {
                    PersonManageRow r = _allManageRows[k];
                    if (string.Equals(r.OriginalUserName, d.originalUserName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r.OriginalUserName, d.userName, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string s in (r.OriginalAgentLoginId ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            releaseLogins.Add(s);
                        }
                        _allManageRows.RemoveAt(k);
                    }
                }
            }

            UpdateLoginPoolCacheStatus(releaseLogins, null);
            ApplyManageFilter();
        }

        private async void BtnManageApplyEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            DgManagePersons.CommitEdit(DataGridEditingUnit.Cell, true);
            DgManagePersons.CommitEdit(DataGridEditingUnit.Row, true);

            CustomerInfo targetCust = _loadedManageCustomer ?? ResolveManageTargetCustomer();
            if (targetCust == null || _allManageRows.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "대상 없음", "먼저 [Config Person 목록 조회]로 Person 목록을 조회하세요.");
                return;
            }

            List<PersonManageRow> checkedAll = new List<PersonManageRow>();
            List<PersonManageRow> targets = new List<PersonManageRow>();
            for (int i = 0; i < _filteredManageRows.Count; i++)
            {
                PersonManageRow r = _filteredManageRows[i];
                if (!r.IsSelected) continue;
                checkedAll.Add(r);
                if (r.IsModified) targets.Add(r);
            }

            if (checkedAll.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "체크된 대상 없음", "수정 반영할 Person의 체크박스를 선택한 뒤 실행하세요.");
                return;
            }
            if (targets.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "수정 사항 없음", "선택(체크)된 행 중 수정된 내용이 없습니다.");
                return;
            }

            // Validation (no empty UserName/EmpID, no duplicate login conflicts across customer rows)
            List<string> missingUsers = new List<string>();
            int genChangedCount = 0;
            int loginChangedCount = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                PersonManageRow r = targets[i];
                if (string.IsNullOrWhiteSpace(r.UserName) || string.IsNullOrWhiteSpace(r.EmployeeId))
                {
                    missingUsers.Add(string.IsNullOrWhiteSpace(r.OriginalUserName) ? ("#" + (i + 1)) : r.OriginalUserName);
                }
                if (r.IsGeneralModified) genChangedCount++;
                if (r.IsAgentLoginModified) loginChangedCount++;
            }
            if (missingUsers.Count > 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "필수 값 누락", "Employee ID 또는 User Name이 비어 있는 행이 있습니다:\n" + string.Join(", ", missingUsers.ToArray()));
                return;
            }

            Dictionary<string, string> loginOwnerCheck = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> dupLogins = new List<string>();
            for (int i = 0; i < _allManageRows.Count; i++)
            {
                PersonManageRow r = _allManageRows[i];
                string[] parts = (r.AgentLoginId ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int k = 0; k < parts.Length; k++)
                {
                    string prevUser;
                    if (loginOwnerCheck.TryGetValue(parts[k], out prevUser))
                    {
                        dupLogins.Add(string.Format("{0} ({1} ↔ {2})", parts[k], prevUser, r.UserName));
                    }
                    else
                    {
                        loginOwnerCheck[parts[k]] = r.UserName;
                    }
                }
            }
            if (dupLogins.Count > 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "Agent Login 중복", "1:1 매칭 원칙 위반: 동일한 Agent Login ID가 여러 Person에 지정되었습니다:\n" + string.Join("\n", dupLogins.ToArray()));
                return;
            }

            if (UpdateManagePersonsAsyncHandler == null)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "기능 미연결", "Config 수정 반영 핸들러가 연결되지 않았습니다.");
                return;
            }

            StringBuilder detail = new StringBuilder();
            detail.AppendLine(string.Format("• 대상 센터: {0}", targetCust.DisplayName));
            detail.AppendLine(string.Format("• 수정 대상: {0}명 (General 변경 {1}건 / Agent Login 변경 {2}건)", targets.Count, genChangedCount, loginChangedCount));
            List<string> sampleNames = new List<string>();
            for (int i = 0; i < Math.Min(5, targets.Count); i++)
            {
                sampleNames.Add(string.Format("{0}({1})", targets[i].EmployeeId, targets[i].UserName));
            }
            detail.AppendLine("• 대상 요약: " + string.Join(", ", sampleNames.ToArray()) + (targets.Count > 5 ? string.Format(" 외 {0}명", targets.Count - 5) : ""));
            detail.Append("• Password: 변경 없음 (기존 비밀번호 유지)");

            bool confirmed = PopupDialogs.ConfirmAction(
                Window.GetWindow(this),
                "Config Person 수정 반영 확인",
                string.Format("수정된 {0}명의 정보를 Config에 반영하시겠습니까?", targets.Count),
                detail.ToString(),
                "수정 반영 실행");
            if (!confirmed) return;

            SetBusyState(true);
            try
            {
                await UpdateManagePersonsAsyncHandler(targetCust, targets);
            }
            catch (Exception ex)
            {
                AppendPersonLog("ERR", "Config Person 수정 반영 실패: " + ex.Message);
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "Config Person 수정 실패", ex.Message);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        private async void BtnManageDeletePersons_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            DgManagePersons.CommitEdit(DataGridEditingUnit.Cell, true);
            DgManagePersons.CommitEdit(DataGridEditingUnit.Row, true);

            CustomerInfo targetCust = _loadedManageCustomer ?? ResolveManageTargetCustomer();
            if (targetCust == null || _allManageRows.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "대상 없음", "먼저 [Config Person 목록 조회]로 Person 목록을 조회하세요.");
                return;
            }

            List<PersonManageRow> targets = new List<PersonManageRow>();
            for (int i = 0; i < _filteredManageRows.Count; i++)
            {
                if (_filteredManageRows[i].IsSelected) targets.Add(_filteredManageRows[i]);
            }
            if (targets.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "삭제 대상 미선택", "삭제할 Person을 체크박스로 선택한 뒤 [선택 삭제]를 누르세요.");
                return;
            }

            if (DeleteManagePersonsAsyncHandler == null)
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "기능 미연결", "Config Person 삭제 핸들러가 연결되지 않았습니다.");
                return;
            }

            SetBusyState(true);
            try
            {
                await DeleteManagePersonsAsyncHandler(targetCust, targets);
            }
            catch (Exception ex)
            {
                AppendPersonLog("ERR", "Config Person 삭제 실패: " + ex.Message);
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "Config Person 삭제 실패", ex.Message);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        // ==================== SAMPLE FILL / RESET / LOGS ====================
        private void BtnFillSample_Click(object sender, RoutedEventArgs e)
        {
            TxtPersonSearch.Text = "";
            int testIdx = -1;
            for (int i = 0; i < _filteredCustomers.Count; i++)
            {
                CustomerInfo c = _filteredCustomers[i];
                if ((c.floor != null && string.Equals(c.floor, "TEST", StringComparison.OrdinalIgnoreCase)) ||
                    (c.name != null && c.name.Contains("TEST")))
                {
                    testIdx = i;
                    break;
                }
            }
            if (testIdx >= 0)
            {
                CmbPersonCustomer.SelectedIndex = testIdx;
            }
            else if (string.IsNullOrWhiteSpace(TxtPersonLastName.Text))
            {
                TxtPersonLastName.Text = "TEST";
            }

            _isManageSubView = false;
            _isPasteMode = false;
            UpdateSubModeVisuals();
            UpdateInputModeVisuals();

            TxtPersonPrefix.Text = "PTEST";
            TxtRuleSeq.Text = "001~005";
            RbUserSameAsEmp.IsChecked = true;
            ChkRuleIsAgent.IsChecked = true;
            GenerateRowsFromRule(true);

            if (_rows.Count >= 2)
            {
                _rows[0].UserName = "홍길동";
                _rows[1].UserName = "김영희";
            }
            RefreshRowSummary();
        }

        private void ResetManageTab()
        {
            _allManageRows.Clear();
            _filteredManageRows.Clear();
            _loadedManageCustomer = null;
            TxtManageFilter.Text = "";
            if (BdManageFolderChip != null) BdManageFolderChip.Visibility = Visibility.Collapsed;
            if (TxtManageLoadedFolder != null) TxtManageLoadedFolder.Text = "";
            RefreshManageSummary();
            AppendPersonLog("INFO", "조회 · 수정 / 삭제 화면 초기화 완료");
        }

        private void BtnResetPersonTab_Click(object sender, RoutedEventArgs e)
        {
            if (_isManageSubView)
            {
                ResetManageTab();
                return;
            }

            TxtPersonSearch.Text = "";
            CmbPersonCustomer.SelectedIndex = -1;
            _selectedCustomer = null;
            _lastAutoCenterName = "";
            UpdateSelectedCustomerInfo();

            TxtPersonLastName.Text = "";
            TxtPersonPrefix.Text = "";
            TxtRuleSeq.Text = "";
            TxtRuleNames.Text = "";
            TxtRuleLogins.Text = "";
            TxtDirectLoginStatus.Text = "※ [사용 가능 Login 조회] 후 적용하면 사용 중 여부도 함께 확인할 수 있습니다.";
            TxtPasteInput.Text = "";
            RbUserSameAsEmp.IsChecked = true;
            ChkRuleIsAgent.IsChecked = true;
            _rows.Clear();
            _loginPoolCustomerName = "";
            _allLoginTiles.Clear();
            _visibleLoginTiles.Clear();
            _pickedOrder.Clear();
            RefreshRowSummary();
            RefreshLoginPoolSummary();

            AppendPersonLog("INFO", "신규 생성 입력 화면 초기화 완료");
        }

        private void SaveLogTextToFile(string logText, string prefix)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Log File (*.log)|*.log|Text File (*.txt)|*.txt";
            sfd.FileName = prefix + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";
            if (sfd.ShowDialog(Window.GetWindow(this)) == true)
            {
                File.WriteAllText(sfd.FileName, logText ?? "", Encoding.UTF8);
                AppendPersonLog("OK", "로그 저장 완료: " + sfd.FileName);
            }
        }

        private void BtnSavePersonLog_Click(object sender, RoutedEventArgs e)
        {
            SaveLogTextToFile(TxtPersonLog.Text, "ExtBatch_Person");
        }

        private void BtnClearPersonLog_Click(object sender, RoutedEventArgs e)
        {
            TxtPersonLog.Clear();
        }

        private void BtnSaveManageLog_Click(object sender, RoutedEventArgs e)
        {
            SaveLogTextToFile(TxtManageLog.Text, "ExtBatch_Person_Manage");
        }

        private void BtnClearManageLog_Click(object sender, RoutedEventArgs e)
        {
            TxtManageLog.Clear();
        }

        private void AppendPersonLog(string level, string msg)
        {
            string line = string.Format("[{0}] [{1}] {2}", DateTime.Now.ToString("HH:mm:ss"), level, msg);
            AppendFormattedLogLine(line);
        }
    }
}
