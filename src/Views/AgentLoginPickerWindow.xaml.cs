using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CmOxeProvisioner.Models;
using CmOxeProvisioner.Services;

namespace CmOxeProvisioner.Views
{
    public class AgentLoginTargetDraft : INotifyPropertyChanged
    {
        private string _draftAgentLoginId = "";

        public PersonManageRow SourceRow { get; set; }
        public string EmployeeId { get; set; }
        public string UserName { get; set; }
        public string OriginalAgentLoginId { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged(string propName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propName));
        }

        public string DraftAgentLoginId
        {
            get { return _draftAgentLoginId; }
            set
            {
                string next = (value ?? "").Trim();
                if (_draftAgentLoginId != next)
                {
                    _draftAgentLoginId = next;
                    RaisePropertyChanged("DraftAgentLoginId");
                    RaisePropertyChanged("DraftLoginDisplay");
                    RaisePropertyChanged("DeltaLabel");
                }
            }
        }

        public string OriginalLoginDisplay
        {
            get { return string.IsNullOrEmpty(OriginalAgentLoginId) ? "(미연결)" : OriginalAgentLoginId; }
        }

        public string DraftLoginDisplay
        {
            get
            {
                if (!string.IsNullOrEmpty(_draftAgentLoginId)) return _draftAgentLoginId;
                return string.IsNullOrEmpty(OriginalAgentLoginId) ? "(미연결)" : "(연결 해제)";
            }
        }

        public bool IsChanged
        {
            get
            {
                return !string.Equals((_draftAgentLoginId ?? "").Trim(), (OriginalAgentLoginId ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        public string DeltaLabel
        {
            get
            {
                string orig = (OriginalAgentLoginId ?? "").Trim();
                string draft = (_draftAgentLoginId ?? "").Trim();
                if (string.Equals(orig, draft, StringComparison.OrdinalIgnoreCase)) return "유지";
                if (string.IsNullOrEmpty(orig) && !string.IsNullOrEmpty(draft)) return "추가";
                if (!string.IsNullOrEmpty(orig) && string.IsNullOrEmpty(draft)) return "삭제(해제)";
                return "변경";
            }
        }
    }

    public partial class AgentLoginPickerWindow : Window
    {
        private readonly CustomerInfo _customer;
        private readonly Func<CustomerInfo, Task<List<AgentLoginPoolItem>>> _fetchHandler;
        private readonly Func<ExtCtiImportContext> _extCtiProvider;
        private readonly Dictionary<string, string> _reservedByOtherRows;

        private readonly List<AgentLoginPoolTile> _allTiles = new List<AgentLoginPoolTile>();
        private readonly ObservableCollection<AgentLoginPoolTile> _visibleTiles = new ObservableCollection<AgentLoginPoolTile>();
        private readonly List<AgentLoginPoolTile> _pickedOrder = new List<AgentLoginPoolTile>();
        private readonly ObservableCollection<AgentLoginTargetDraft> _targets = new ObservableCollection<AgentLoginTargetDraft>();

        private string _distMode = "Auto"; // Auto | Manual | Direct
        private bool _isDragging = false;
        private bool _dragTargetState = true;
        private bool _isFetching = false;

        public List<AgentLoginPoolItem> LatestFetchedPool { get; private set; }

        public AgentLoginPickerWindow(
            CustomerInfo customer,
            List<PersonManageRow> targetRows,
            List<AgentLoginPoolItem> initialPool,
            Dictionary<string, string> reservedByOtherRows,
            Func<CustomerInfo, Task<List<AgentLoginPoolItem>>> fetchHandler,
            Func<ExtCtiImportContext> extCtiProvider)
        {
            InitializeComponent();
            _customer = customer;
            _fetchHandler = fetchHandler;
            _extCtiProvider = extCtiProvider;
            _reservedByOtherRows = reservedByOtherRows ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            IcLoginPool.ItemsSource = _visibleTiles;
            DgTargets.ItemsSource = _targets;

            if (_customer != null)
            {
                TxtDialogHeaderTitle.Text = string.Format("Agent Login 선택 · 분배  —  {0}", _customer.DisplayName);
            }

            if (targetRows != null)
            {
                for (int i = 0; i < targetRows.Count; i++)
                {
                    PersonManageRow r = targetRows[i];
                    AgentLoginTargetDraft d = new AgentLoginTargetDraft();
                    d.SourceRow = r;
                    d.EmployeeId = r.EmployeeId ?? "";
                    d.UserName = r.UserName ?? "";
                    d.OriginalAgentLoginId = (r.OriginalAgentLoginId ?? "").Trim();
                    d.DraftAgentLoginId = (r.AgentLoginId ?? "").Trim();
                    _targets.Add(d);
                }
            }

            if (_targets.Count == 1)
            {
                _distMode = "Manual";
            }
            if (_targets.Count > 0)
            {
                DgTargets.SelectedIndex = 0;
            }

            if (initialPool != null && initialPool.Count > 0)
            {
                ApplyFetchedPool(initialPool);
            }
            else
            {
                RefreshPoolSummary();
            }

            UpdateDistModeVisuals();
            RefreshTargetSummary();
            PreviewKeyDown += AgentLoginPickerWindow_PreviewKeyDown;
            Loaded += delegate { Dispatcher.BeginInvoke(new Action(delegate { PersonTabView.RefreshStarColumns(DgTargets); }), DispatcherPriority.Render); };
            DgTargets.SizeChanged += delegate { PersonTabView.RefreshStarColumns(DgTargets); };
        }

        private void AgentLoginPickerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                DialogResult = false;
                Close();
            }
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

        private void UpdateDistModeVisuals()
        {
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
            if (_targets.Count > 0 && DgTargets.SelectedItem == null) DgTargets.SelectedIndex = 0;
            UpdateDistModeVisuals();
        }

        private void BtnDistModeDirect_Click(object sender, RoutedEventArgs e)
        {
            _distMode = "Direct";
            UpdateDistModeVisuals();
        }

        private async void BtnFetchLoginPool_Click(object sender, RoutedEventArgs e)
        {
            if (_isFetching) return;
            if (_customer == null)
            {
                PopupDialogs.ShowSyntaxToast(this, false, "센터 미선택", "대상 센터 정보가 없습니다.");
                return;
            }
            if (_fetchHandler == null)
            {
                PopupDialogs.ShowSyntaxToast(this, false, "기능 미연결", "CM Agent Login 조회 핸들러가 연결되지 않았습니다.");
                return;
            }

            _isFetching = true;
            BtnFetchLoginPool.IsEnabled = false;
            BtnConfirmApply.IsEnabled = false;
            try
            {
                List<AgentLoginPoolItem> items = await _fetchHandler(_customer);
                LatestFetchedPool = items;
                ApplyFetchedPool(items);
            }
            catch (Exception ex)
            {
                PopupDialogs.ShowSyntaxToast(this, false, "Agent Login 조회 실패", ex.Message);
            }
            finally
            {
                _isFetching = false;
                BtnFetchLoginPool.IsEnabled = true;
                BtnConfirmApply.IsEnabled = true;
            }
        }

        public void ApplyFetchedPool(List<AgentLoginPoolItem> items)
        {
            _allTiles.Clear();
            _pickedOrder.Clear();

            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    AgentLoginPoolItem it = items[i];
                    if (it == null || string.IsNullOrEmpty(it.LoginId)) continue;
                    AgentLoginPoolTile tile = new AgentLoginPoolTile();
                    tile.LoginId = it.LoginId;

                    string reservedOwner;
                    if (!it.IsInUseInCm && _reservedByOtherRows.TryGetValue(it.LoginId, out reservedOwner))
                    {
                        tile.IsInUseInCm = true;
                        tile.InUseOwner = "임시배정 · " + reservedOwner;
                    }
                    else
                    {
                        tile.IsInUseInCm = it.IsInUseInCm;
                        tile.InUseOwner = it.InUseOwner ?? "";
                    }
                    _allTiles.Add(tile);
                }
            }

            SyncTileAssignments();
            RebuildVisibleTiles();
        }

        private void RebuildVisibleTiles()
        {
            _visibleTiles.Clear();
            bool hideInUse = (ChkHideInUseLogins != null && ChkHideInUseLogins.IsChecked == true);
            for (int i = 0; i < _allTiles.Count; i++)
            {
                AgentLoginPoolTile t = _allTiles[i];
                if (hideInUse && t.IsInUseInCm) continue;
                _visibleTiles.Add(t);
            }
            RefreshPoolSummary();
        }

        private void ChkHideInUseLogins_Changed(object sender, RoutedEventArgs e)
        {
            RebuildVisibleTiles();
        }

        private void SyncTileAssignments()
        {
            Dictionary<string, string> loginToEmp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _targets.Count; i++)
            {
                string lid = (_targets[i].DraftAgentLoginId ?? "").Trim();
                if (lid.Length > 0 && !loginToEmp.ContainsKey(lid))
                {
                    string lbl = string.IsNullOrEmpty(_targets[i].EmployeeId) ? _targets[i].UserName : _targets[i].EmployeeId;
                    loginToEmp[lid] = string.IsNullOrEmpty(lbl) ? ("#" + (i + 1)) : lbl;
                }
            }

            for (int i = 0; i < _allTiles.Count; i++)
            {
                AgentLoginPoolTile t = _allTiles[i];
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
            RefreshPoolSummary();
        }

        private void RefreshPoolSummary()
        {
            if (_allTiles.Count == 0)
            {
                TxtLoginPoolCount.Text = "미조회 (조회 버튼으로 불러오기)";
                PnlEmptyGalleryHint.Visibility = Visibility.Visible;
                TxtGalleryPickedCount.Text = "선택 0개";
                return;
            }

            PnlEmptyGalleryHint.Visibility = Visibility.Collapsed;
            int avail = 0;
            int assigned = 0;
            for (int i = 0; i < _allTiles.Count; i++)
            {
                if (!_allTiles[i].IsInUseInCm)
                {
                    avail++;
                    if (!string.IsNullOrEmpty(_allTiles[i].AssignedToEmpId)) assigned++;
                }
            }
            TxtLoginPoolCount.Text = string.Format("전체 {0} · 사용 가능 {1} (배정 {2})", _allTiles.Count, avail, assigned);
            TxtGalleryPickedCount.Text = string.Format("선택 {0}개", _pickedOrder.Count);
        }

        private void RefreshTargetSummary()
        {
            int changed = 0;
            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i].IsChanged) changed++;
            }
            TxtTargetSummary.Text = string.Format("대상 {0}명 · 변경 {1}건", _targets.Count, changed);
            SyncTileAssignments();
            UpdateManualTargetBadge();
        }

        private void UpdateManualTargetBadge()
        {
            if (TxtManualTargetBadge == null) return;
            AgentLoginTargetDraft d = DgTargets.SelectedItem as AgentLoginTargetDraft;
            if (d == null)
            {
                TxtManualTargetBadge.Text = "대상 행 ▶ (아래 표에서 Person을 선택하세요)";
                return;
            }
            int idx = _targets.IndexOf(d) + 1;
            string cur = string.IsNullOrEmpty(d.DraftAgentLoginId) ? "미연결" : d.DraftAgentLoginId;
            TxtManualTargetBadge.Text = string.Format("대상 ▶ #{0} {1} ({2}) [현재: {3}]", idx, d.EmployeeId, d.UserName, cur);
        }

        private void DgTargets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateManualTargetBadge();
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
            for (int i = 0; i < _pickedOrder.Count; i++)
            {
                _pickedOrder[i].PickOrder = i + 1;
            }
            TxtGalleryPickedCount.Text = string.Format("선택 {0}개", _pickedOrder.Count);
        }

        private void ClearPickedOrder()
        {
            for (int i = 0; i < _allTiles.Count; i++)
            {
                _allTiles[i].IsPicked = false;
                _allTiles[i].PickOrder = 0;
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
                AssignTileManually(tile);
                e.Handled = true;
                return;
            }

            _isDragging = true;
            _dragTargetState = !tile.IsPicked;
            SetTilePicked(tile, _dragTargetState);
            e.Handled = true;
        }

        private void LoginTile_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _distMode != "Auto") return;
            if (Mouse.LeftButton != MouseButtonState.Pressed)
            {
                _isDragging = false;
                return;
            }
            FrameworkElement fe = sender as FrameworkElement;
            AgentLoginPoolTile tile = fe != null ? fe.DataContext as AgentLoginPoolTile : null;
            if (tile != null && !tile.IsInUseInCm) SetTilePicked(tile, _dragTargetState);
        }

        private void PickerWindow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
        }

        private void AssignTileManually(AgentLoginPoolTile tile)
        {
            if (_targets.Count == 0) return;
            AgentLoginTargetDraft target = DgTargets.SelectedItem as AgentLoginTargetDraft;
            if (target == null)
            {
                target = _targets[0];
                DgTargets.SelectedIndex = 0;
            }

            if (string.Equals(target.DraftAgentLoginId, tile.LoginId, StringComparison.OrdinalIgnoreCase))
            {
                target.DraftAgentLoginId = "";
                RefreshTargetSummary();
                return;
            }

            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i] != target && string.Equals(_targets[i].DraftAgentLoginId, tile.LoginId, StringComparison.OrdinalIgnoreCase))
                {
                    _targets[i].DraftAgentLoginId = "";
                }
            }

            target.DraftAgentLoginId = tile.LoginId;
            int curIdx = _targets.IndexOf(target);
            int nextIdx = -1;
            for (int i = curIdx + 1; i < _targets.Count; i++)
            {
                if (string.IsNullOrEmpty(_targets[i].DraftAgentLoginId))
                {
                    nextIdx = i;
                    break;
                }
            }
            if (nextIdx < 0 && curIdx + 1 < _targets.Count) nextIdx = curIdx + 1;
            if (nextIdx >= 0)
            {
                DgTargets.SelectedIndex = nextIdx;
                DgTargets.ScrollIntoView(_targets[nextIdx]);
            }
            RefreshTargetSummary();
        }

        private void BtnSelectAllAvailLogins_Click(object sender, RoutedEventArgs e)
        {
            if (_allTiles.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(this, false, "목록 미조회", "먼저 [사용 가능 Login 조회]를 눌러 목록을 불러오세요.");
                return;
            }
            for (int i = 0; i < _visibleTiles.Count; i++)
            {
                if (!_visibleTiles[i].IsInUseInCm) SetTilePicked(_visibleTiles[i], true);
            }
        }

        private void BtnClearLoginPicks_Click(object sender, RoutedEventArgs e)
        {
            ClearPickedOrder();
        }

        private void AssignLoginSequence(List<string> sourceLogins)
        {
            if (_targets.Count == 0) return;
            List<AgentLoginTargetDraft> applyTargets = new List<AgentLoginTargetDraft>();
            for (int i = 0; i < _targets.Count; i++)
            {
                if (string.IsNullOrEmpty(_targets[i].DraftAgentLoginId)) applyTargets.Add(_targets[i]);
            }
            if (applyTargets.Count == 0)
            {
                for (int i = 0; i < _targets.Count; i++) applyTargets.Add(_targets[i]);
            }

            HashSet<AgentLoginTargetDraft> targetSet = new HashSet<AgentLoginTargetDraft>(applyTargets);
            HashSet<string> protectedLogins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _targets.Count; i++)
            {
                if (targetSet.Contains(_targets[i])) continue;
                string existing = (_targets[i].DraftAgentLoginId ?? "").Trim();
                if (existing.Length > 0) protectedLogins.Add(existing);
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
                PopupDialogs.ShowSyntaxToast(this, false, "분배 가능한 Login 없음", "사용 가능하면서 아직 배정되지 않은 Agent Login이 없습니다.");
                return;
            }

            int count = Math.Min(applyTargets.Count, usableLogins.Count);
            for (int i = 0; i < count; i++)
            {
                string lid = usableLogins[i];
                for (int k = 0; k < applyTargets.Count; k++)
                {
                    if (applyTargets[k] != applyTargets[i] && string.Equals(applyTargets[k].DraftAgentLoginId, lid, StringComparison.OrdinalIgnoreCase))
                    {
                        applyTargets[k].DraftAgentLoginId = "";
                    }
                }
                applyTargets[i].DraftAgentLoginId = lid;
            }

            RefreshTargetSummary();
            if (usableLogins.Count < applyTargets.Count)
            {
                PopupDialogs.ShowSyntaxToast(
                    this,
                    true,
                    "일부 Login 배정 완료",
                    string.Format("{0}건을 순서대로 매칭했습니다. (나머지 {1}명은 유지/미지정)", count, applyTargets.Count - count));
            }
        }

        private void BtnApplyAutoDist_Click(object sender, RoutedEventArgs e)
        {
            if (_allTiles.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(this, false, "목록 미조회", "먼저 [사용 가능 Login 조회]를 눌러 CM에서 목록을 불러오세요.");
                return;
            }

            bool hasUnassigned = false;
            HashSet<string> assignedOnOtherTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _targets.Count; i++)
            {
                string lid = (_targets[i].DraftAgentLoginId ?? "").Trim();
                if (string.IsNullOrEmpty(lid))
                {
                    hasUnassigned = true;
                }
                else
                {
                    assignedOnOtherTargets.Add(lid);
                }
            }

            List<string> src = new List<string>();
            if (_pickedOrder.Count > 0)
            {
                for (int i = 0; i < _pickedOrder.Count; i++)
                {
                    AgentLoginPoolTile t = _pickedOrder[i];
                    if (t.IsInUseInCm) continue;
                    if (hasUnassigned && assignedOnOtherTargets.Contains(t.LoginId)) continue;
                    src.Add(t.LoginId);
                }
            }
            else
            {
                for (int i = 0; i < _allTiles.Count; i++)
                {
                    AgentLoginPoolTile t = _allTiles[i];
                    if (t.IsInUseInCm) continue;
                    if (hasUnassigned && (!string.IsNullOrEmpty(t.AssignedToEmpId) || assignedOnOtherTargets.Contains(t.LoginId))) continue;
                    src.Add(t.LoginId);
                }
            }

            if (src.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(this, false, "분배 가능한 Login 없음", "사용 가능한 Agent Login이 없거나 이미 모두 배정되었습니다.");
                return;
            }

            AssignLoginSequence(src);
        }

        private void BtnUnassignSelectedRow_Click(object sender, RoutedEventArgs e)
        {
            AgentLoginTargetDraft d = DgTargets.SelectedItem as AgentLoginTargetDraft;
            if (d == null) return;
            d.DraftAgentLoginId = "";
            RefreshTargetSummary();
        }

        private void BtnImportCtiLogins_Click(object sender, RoutedEventArgs e)
        {
            if (_extCtiProvider == null) return;
            ExtCtiImportContext ctx = _extCtiProvider();
            if (ctx == null || ctx.CtiNumbers == null || ctx.CtiNumbers.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(this, false, "CTI 번호 없음", "내선 · CTI 탭의 CTI 입력칸에 유효한 번호가 없습니다.");
                return;
            }
            TxtRuleLogins.Text = string.Join(", ", ctx.CtiNumbers.ToArray());
        }

        private void BtnApplyDirectLogins_Click(object sender, RoutedEventArgs e)
        {
            NumberParseResult parsed = OxeScriptService.ParseNumbersStrict(TxtRuleLogins.Text ?? "");
            if (parsed.Errors.Count > 0)
            {
                PopupDialogs.ShowSyntaxToast(this, false, "Agent Login ID 형식 오류", string.Join("\n", parsed.Errors.ToArray()));
                return;
            }
            if (parsed.Items.Count == 0)
            {
                PopupDialogs.ShowSyntaxToast(this, false, "입력 필요", "적용할 Agent Login ID 또는 범위를 입력하세요.");
                return;
            }

            if (_allTiles.Count > 0)
            {
                Dictionary<string, AgentLoginPoolTile> poolMap = new Dictionary<string, AgentLoginPoolTile>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _allTiles.Count; i++)
                {
                    if (!poolMap.ContainsKey(_allTiles[i].LoginId)) poolMap[_allTiles[i].LoginId] = _allTiles[i];
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
                        bool isOwnedByCurrentTarget = false;
                        for (int k = 0; k < _targets.Count; k++)
                        {
                            if (string.Equals(_targets[k].OriginalAgentLoginId, lid, StringComparison.OrdinalIgnoreCase))
                            {
                                isOwnedByCurrentTarget = true;
                                break;
                            }
                        }
                        if (!isOwnedByCurrentTarget)
                        {
                            usedConflicts.Add(string.Format("{0}({1})", lid, t.InUseOwner));
                        }
                    }
                }
                if (usedConflicts.Count > 0)
                {
                    TxtDirectLoginStatus.Text = "⚠ 다른 Person 사용 중: " + string.Join(", ", usedConflicts.ToArray());
                    PopupDialogs.ShowSyntaxToast(this, false, "사용 중인 Agent Login 포함", "이미 다른 Person에 연결된 Agent Login이 포함되어 있습니다:\n" + string.Join(", ", usedConflicts.ToArray()));
                    return;
                }
                TxtDirectLoginStatus.Text = missingIds.Count > 0
                    ? string.Format("안내: 조회 목록에 없는 Login {0}건 포함", missingIds.Count)
                    : string.Format("확인 완료: {0}건 모두 사용 가능 상태입니다.", parsed.Items.Count);
            }

            AssignLoginSequence(parsed.Items);
        }

        private void BtnClearSingleTargetRow_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement fe = sender as FrameworkElement;
            AgentLoginTargetDraft d = fe != null ? fe.DataContext as AgentLoginTargetDraft : null;
            if (d == null) return;
            d.DraftAgentLoginId = "";
            RefreshTargetSummary();
        }

        private void BtnClearAllDrafts_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                _targets[i].DraftAgentLoginId = "";
            }
            RefreshTargetSummary();
        }

        private void BtnRestoreOriginalLogins_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                _targets[i].DraftAgentLoginId = _targets[i].OriginalAgentLoginId ?? "";
            }
            RefreshTargetSummary();
        }

        private void BtnConfirmApply_Click(object sender, RoutedEventArgs e)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _targets.Count; i++)
            {
                string lid = (_targets[i].DraftAgentLoginId ?? "").Trim();
                if (lid.Length == 0) continue;
                if (!seen.Add(lid))
                {
                    PopupDialogs.ShowSyntaxToast(this, false, "Agent Login 중복", string.Format("1:1 매칭 원칙 위반: '{0}'가 여러 대상에게 중복 지정되었습니다.", lid));
                    return;
                }
            }

            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i].SourceRow != null)
                {
                    _targets[i].SourceRow.AgentLoginId = (_targets[i].DraftAgentLoginId ?? "").Trim();
                    if (!string.IsNullOrEmpty(_targets[i].SourceRow.AgentLoginId))
                    {
                        _targets[i].SourceRow.IsAgent = true;
                    }
                }
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
