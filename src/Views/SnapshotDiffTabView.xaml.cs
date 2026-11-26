using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CmOxeProvisioner.Models;
using CmOxeProvisioner.Services;
using Microsoft.Win32;

namespace CmOxeProvisioner.Views
{
    public partial class SnapshotDiffTabView : UserControl
    {
        private CmWorkerService _cmWorker;
        private Func<List<CustomerInfo>> _getCustomers;
        private Action<string, string> _logCallback;
        private Action<List<AuditEventDto>> _auditCallback;

        private bool _isBusy = false;
        private bool _loadedOnce = false;
        private List<SnapshotMetaDto> _snapshots = new List<SnapshotMetaDto>();
        private SnapshotDiffResponseDto _lastDiff = null;
        private string _lastDiffLeftId = "";
        private string _lastDiffRightId = "";
        private List<DiffEntryDto> _currentEntries = new List<DiffEntryDto>();

        public SnapshotDiffTabView()
        {
            InitializeComponent();
            InitCategoryCombo();
        }

        public void Initialize(
            CmWorkerService cmWorker,
            Func<List<CustomerInfo>> getCustomers,
            Action<string, string> logCallback,
            Action<List<AuditEventDto>> auditCallback = null)
        {
            _cmWorker = cmWorker;
            _getCustomers = getCustomers;
            _logCallback = logCallback;
            _auditCallback = auditCallback;
            RefreshCustomers();
        }

        public void RefreshCustomers()
        {
            string prevScope = CmbSnapScope.SelectedValue as string;
            string prevFilter = CmbDiffCustomer.SelectedValue as string;

            List<CustomerInfo> customers = _getCustomers != null ? _getCustomers() : new List<CustomerInfo>();
            List<FilterOptionItem> scopeList = new List<FilterOptionItem>();
            scopeList.Add(new FilterOptionItem { Label = "전체 Config (모든 센터 · 폴더 · 그룹)", Value = "" });

            List<FilterOptionItem> filterList = new List<FilterOptionItem>();
            filterList.Add(new FilterOptionItem { Label = "전체 고객사", Value = "" });

            if (customers != null)
            {
                for (int i = 0; i < customers.Count; i++)
                {
                    CustomerInfo c = customers[i];
                    if (c == null || string.IsNullOrWhiteSpace(c.name)) continue;
                    FilterOptionItem item = new FilterOptionItem { Label = c.DisplayName, Value = c.name };
                    scopeList.Add(item);
                    filterList.Add(item);
                }
            }

            CmbSnapScope.ItemsSource = scopeList;
            CmbSnapScope.SelectedValue = string.IsNullOrEmpty(prevScope) ? "" : prevScope;
            if (CmbSnapScope.SelectedIndex < 0) CmbSnapScope.SelectedIndex = 0;

            CmbDiffCustomer.ItemsSource = filterList;
            CmbDiffCustomer.SelectedValue = string.IsNullOrEmpty(prevFilter) ? "" : prevFilter;
            if (CmbDiffCustomer.SelectedIndex < 0) CmbDiffCustomer.SelectedIndex = 0;
        }

        public void EnsureInitialListLoaded()
        {
            if (_loadedOnce || _isBusy || _cmWorker == null) return;
            _loadedOnce = true;
            LoadSnapshotsAsync();
        }

        private void InitCategoryCombo()
        {
            CmbDiffCategory.ItemsSource = new List<string>
            {
                "전체",
                "Agent DN",
                "Place",
                "Agent Login",
                "ACD Position",
                "Person",
                "RSI",
                "VQueue",
                "Folder",
                "Place Group",
                "Agent Group",
                "DN Group",
                "Script"
            };
            CmbDiffCategory.SelectedIndex = 0;
        }

        private void BtnRefreshSnapshots_Click(object sender, RoutedEventArgs e)
        {
            LoadSnapshotsAsync();
        }

        private async void LoadSnapshotsAsync()
        {
            if (_isBusy || _cmWorker == null) return;
            SetBusy(true, "저장된 스냅샷 목록 불러오는 중...");
            try
            {
                SnapshotListResponseDto resp = await _cmWorker.ListSnapshotsAsync();
                _snapshots = (resp != null && resp.items != null) ? resp.items : new List<SnapshotMetaDto>();
                BindSnapshotSelectors(null, null);
                TxtSnapshotStatus.Text = string.Format("스냅샷 폴더: {0}",
                    resp != null && !string.IsNullOrEmpty(resp.snapshotsDir) ? resp.snapshotsDir : "snapshots\\");
                TxtSnapshotCountBadge.Text = string.Format("{0}개", _snapshots.Count);
            }
            catch (Exception ex)
            {
                TxtSnapshotStatus.Text = "목록 조회 실패: " + ex.Message;
                if (_logCallback != null) _logCallback("ERR", "[스냅샷 목록 실패] " + ex.Message);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void BindSnapshotSelectors(string forceLeftId, string forceRightId)
        {
            string prevLeft = !string.IsNullOrEmpty(forceLeftId) ? forceLeftId : (CmbDiffLeft.SelectedValue as string);
            string prevRight = !string.IsNullOrEmpty(forceRightId) ? forceRightId : (CmbDiffRight.SelectedValue as string);

            DgSnapshots.ItemsSource = null;
            DgSnapshots.ItemsSource = _snapshots;
            TxtSnapshotCountBadge.Text = string.Format("{0}개", _snapshots.Count);

            SnapshotMetaDto liveOption = new SnapshotMetaDto
            {
                id = "__LIVE__",
                label = "LIVE (현재 실서버 Config)"
            };

            List<SnapshotMetaDto> leftOptions = new List<SnapshotMetaDto>(_snapshots);
            leftOptions.Add(liveOption);

            List<SnapshotMetaDto> rightOptions = new List<SnapshotMetaDto>();
            rightOptions.Add(liveOption);
            for (int i = 0; i < _snapshots.Count; i++)
            {
                rightOptions.Add(_snapshots[i]);
            }

            CmbDiffLeft.ItemsSource = leftOptions;
            CmbDiffRight.ItemsSource = rightOptions;

            if (!string.IsNullOrEmpty(prevLeft))
            {
                CmbDiffLeft.SelectedValue = prevLeft;
            }
            if (CmbDiffLeft.SelectedIndex < 0 && leftOptions.Count > 0)
            {
                CmbDiffLeft.SelectedIndex = 0;
            }

            if (!string.IsNullOrEmpty(prevRight))
            {
                CmbDiffRight.SelectedValue = prevRight;
            }
            if (CmbDiffRight.SelectedIndex < 0 && rightOptions.Count > 0)
            {
                CmbDiffRight.SelectedIndex = 0; // defaults to __LIVE__
            }
        }

        private async void BtnCaptureSnapshot_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy || _cmWorker == null) return;
            string label = (TxtSnapLabel.Text ?? "").Trim();
            string scope = (CmbSnapScope.SelectedValue as string) ?? "";
            bool includeGs = (ChkSnapIncludeGS.IsChecked == true);

            SetBusy(true, "Config Server 전체 스냅샷 저장 중...");
            try
            {
                SnapshotCaptureResponseDto resp = await _cmWorker.CaptureSnapshotAsync(label, scope, includeGs);
                if (resp != null && resp.meta != null)
                {
                    if (_logCallback != null && resp.logs != null)
                    {
                        for (int i = 0; i < resp.logs.Count; i++) _logCallback("OK", resp.logs[i]);
                    }
                    TxtSnapLabel.Text = "";
                    SnapshotListResponseDto listResp = await _cmWorker.ListSnapshotsAsync();
                    _snapshots = (listResp != null && listResp.items != null) ? listResp.items : new List<SnapshotMetaDto>();
                    BindSnapshotSelectors(resp.meta.id, "__LIVE__");
                    TxtSnapshotStatus.Text = string.Format("스냅샷 저장 완료: {0} ({1}건, {2})",
                        resp.meta.id, resp.meta.totalObjects, resp.meta.SizeKbText);
                }
            }
            catch (Exception ex)
            {
                TxtSnapshotStatus.Text = "저장 실패: " + ex.Message;
                if (_logCallback != null) _logCallback("ERR", "[스냅샷 저장 실패] " + ex.Message);
                MessageBox.Show(Window.GetWindow(this), "스냅샷 저장 실패:\n" + ex.Message, "스냅샷 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void BtnPickLeft_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            SnapshotMetaDto meta = btn != null ? (btn.Tag as SnapshotMetaDto) : null;
            if (meta != null) CmbDiffLeft.SelectedValue = meta.id;
        }

        private void BtnPickRight_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            SnapshotMetaDto meta = btn != null ? (btn.Tag as SnapshotMetaDto) : null;
            if (meta != null) CmbDiffRight.SelectedValue = meta.id;
        }

        private async void BtnDeleteSnapshot_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy || _cmWorker == null) return;
            Button btn = sender as Button;
            SnapshotMetaDto meta = btn != null ? (btn.Tag as SnapshotMetaDto) : null;
            if (meta == null || string.IsNullOrEmpty(meta.id)) return;

            MessageBoxResult confirm = MessageBox.Show(
                Window.GetWindow(this),
                string.Format("스냅샷 '{0}' ({1})을 삭제하시겠습니까?", meta.label ?? meta.id, meta.createdAt ?? ""),
                "스냅샷 삭제 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            SetBusy(true, "스냅샷 삭제 중...");
            try
            {
                await _cmWorker.DeleteSnapshotAsync(meta.id);
                SnapshotListResponseDto listResp = await _cmWorker.ListSnapshotsAsync();
                _snapshots = (listResp != null && listResp.items != null) ? listResp.items : new List<SnapshotMetaDto>();
                BindSnapshotSelectors(null, null);
                TxtSnapshotStatus.Text = "삭제 완료: " + meta.id;
                if (_logCallback != null) _logCallback("INFO", "[스냅샷 삭제] " + meta.id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), "스냅샷 삭제 실패:\n" + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async void TxtDiffSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await RunDiffAsync();
            }
        }

        private async void BtnRunDiff_Click(object sender, RoutedEventArgs e)
        {
            await RunDiffAsync();
        }

        private async Task RunDiffAsync()
        {
            if (_isBusy || _cmWorker == null) return;
            string leftId = CmbDiffLeft.SelectedValue as string;
            string rightId = CmbDiffRight.SelectedValue as string;
            if (string.IsNullOrEmpty(leftId) || string.IsNullOrEmpty(rightId))
            {
                MessageBox.Show(Window.GetWindow(this), "비교할 기준 스냅샷과 비교 대상 스냅샷(또는 LIVE)을 선택하세요.", "선택 확인", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string catFilter = (CmbDiffCategory.SelectedItem as string) ?? "전체";
            if (catFilter == "전체") catFilter = "";
            string custFilter = (CmbDiffCustomer.SelectedValue as string) ?? "";
            string search = (TxtDiffSearch.Text ?? "").Trim();
            bool onlyChanged = (ChkDiffOnlyChanged.IsChecked == true);

            SetBusy(true, "스냅샷 변경 내역 비교 중...");
            try
            {
                SnapshotDiffResponseDto resp = await _cmWorker.DiffSnapshotsAsync(
                    leftId, rightId, custFilter, catFilter, search, onlyChanged);
                _lastDiff = resp;
                _lastDiffLeftId = leftId;
                _lastDiffRightId = rightId;

                BindEntryRows((resp != null && resp.entries != null) ? resp.entries : new List<DiffEntryDto>());
                RenderDiffSummaryChips(resp);

                if (resp != null)
                {
                    TxtDiffStatus.Text = string.Format("기준 {0}건 ↔ 대상 {1}건 | 표시 {2}건 ({3:0.00}s)",
                        resp.totalLeft, resp.totalRight, _currentEntries.Count, resp.elapsedSeconds);
                    if (_logCallback != null && resp.logs != null)
                    {
                        for (int i = 0; i < resp.logs.Count; i++) _logCallback("INFO", resp.logs[i]);
                    }
                }

                if (_currentEntries.Count > 0)
                {
                    DgDiffEntries.SelectedIndex = 0;
                }
                else
                {
                    ClearDiffInspector();
                }
            }
            catch (Exception ex)
            {
                TxtDiffStatus.Text = "비교 실패: " + ex.Message;
                if (_logCallback != null) _logCallback("ERR", "[변경 비교 실패] " + ex.Message);
                MessageBox.Show(Window.GetWindow(this), "변경 비교 실패:\n" + ex.Message, "비교 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SetBusy(false, null);
                UpdateRestoreControlsState();
            }
        }

        private void BindEntryRows(List<DiffEntryDto> entries)
        {
            for (int i = 0; i < _currentEntries.Count; i++)
            {
                _currentEntries[i].PropertyChanged -= Entry_PropertyChanged;
            }
            _currentEntries = entries ?? new List<DiffEntryDto>();
            for (int i = 0; i < _currentEntries.Count; i++)
            {
                _currentEntries[i].PropertyChanged += Entry_PropertyChanged;
            }
            DgDiffEntries.ItemsSource = _currentEntries;
            ChkHeaderSelectDiff.IsChecked = false;
            UpdateRestoreControlsState();
        }

        private void Entry_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e != null && e.PropertyName == "IsSelected")
            {
                UpdateRestoreControlsState();
            }
        }

        private bool IsRestoreComparisonEligible()
        {
            return !string.IsNullOrEmpty(_lastDiffLeftId)
                && !string.Equals(_lastDiffLeftId, "__LIVE__", StringComparison.OrdinalIgnoreCase)
                && string.Equals(_lastDiffRightId, "__LIVE__", StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateRestoreControlsState()
        {
            if (BtnRestoreSelected == null) return;
            bool eligible = IsRestoreComparisonEligible();
            int checkedChangedCount = 0;
            for (int i = 0; i < _currentEntries.Count; i++)
            {
                DiffEntryDto d = _currentEntries[i];
                if (d.IsSelected && !string.Equals(d.diffType, "UNCHANGED", StringComparison.OrdinalIgnoreCase))
                {
                    checkedChangedCount++;
                }
            }
            BtnRestoreSelected.Content = checkedChangedCount > 0
                ? string.Format("↩ 선택 항목 복구 ({0}건)", checkedChangedCount)
                : "↩ 선택 항목 복구";
            BtnRestoreSelected.IsEnabled = !_isBusy && eligible && checkedChangedCount > 0;
            if (!eligible && _currentEntries.Count > 0)
            {
                BtnRestoreSelected.ToolTip = "실서버 복구를 실행하려면 기준에 저장된 스냅샷을, 비교 대상에 LIVE를 선택한 상태로 비교하세요.";
            }
            else
            {
                BtnRestoreSelected.ToolTip = "체크한 항목을 기준 스냅샷 상태로 실서버에 복구합니다.";
            }
        }

        private void ChkHeaderSelectDiff_Click(object sender, RoutedEventArgs e)
        {
            bool target = (ChkHeaderSelectDiff.IsChecked == true);
            SetSelectionForEntries(delegate(DiffEntryDto d) { return target; });
        }

        private void BtnSelectAllDiff_Click(object sender, RoutedEventArgs e)
        {
            SetSelectionForEntries(delegate(DiffEntryDto d)
            {
                return !string.Equals(d.diffType, "UNCHANGED", StringComparison.OrdinalIgnoreCase);
            });
            ChkHeaderSelectDiff.IsChecked = true;
        }

        private void BtnSelectRestorable_Click(object sender, RoutedEventArgs e)
        {
            SetSelectionForEntries(delegate(DiffEntryDto d)
            {
                return string.Equals(d.diffType, "REMOVED", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(d.diffType, "MODIFIED", StringComparison.OrdinalIgnoreCase);
            });
            ChkHeaderSelectDiff.IsChecked = false;
        }

        private void BtnClearDiffSelection_Click(object sender, RoutedEventArgs e)
        {
            SetSelectionForEntries(delegate(DiffEntryDto d) { return false; });
            ChkHeaderSelectDiff.IsChecked = false;
        }

        private void SetSelectionForEntries(Func<DiffEntryDto, bool> selector)
        {
            for (int i = 0; i < _currentEntries.Count; i++)
            {
                _currentEntries[i].IsSelected = selector(_currentEntries[i]);
            }
            UpdateRestoreControlsState();
        }

        private async void BtnRestoreSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy || _cmWorker == null) return;
            if (!IsRestoreComparisonEligible())
            {
                MessageBox.Show(Window.GetWindow(this),
                    "실서버 복구를 실행하려면 '기준 스냅샷'에 저장된 스냅샷을, '비교 대상'에 LIVE를 선택해 비교하세요.",
                    "복구 대상 안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            List<DiffEntryDto> checkedRows = new List<DiffEntryDto>();
            int recreateCnt = 0;
            int updateCnt = 0;
            int addedCnt = 0;
            for (int i = 0; i < _currentEntries.Count; i++)
            {
                DiffEntryDto d = _currentEntries[i];
                if (!d.IsSelected) continue;
                if (string.Equals(d.diffType, "UNCHANGED", StringComparison.OrdinalIgnoreCase)) continue;
                checkedRows.Add(d);
                if (string.Equals(d.diffType, "REMOVED", StringComparison.OrdinalIgnoreCase)) recreateCnt++;
                else if (string.Equals(d.diffType, "MODIFIED", StringComparison.OrdinalIgnoreCase)) updateCnt++;
                else if (string.Equals(d.diffType, "ADDED", StringComparison.OrdinalIgnoreCase)) addedCnt++;
            }

            if (checkedRows.Count == 0)
            {
                MessageBox.Show(Window.GetWindow(this), "복구할 변경/삭제 항목을 체크하세요.", "선택 필요", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool includeAddedDelete = (ChkRestoreDeleteAdded.IsChecked == true);
            if (!includeAddedDelete && recreateCnt == 0 && updateCnt == 0 && addedCnt > 0)
            {
                MessageBox.Show(Window.GetWindow(this),
                    "선택하신 항목은 모두 스냅샷 이후 신규(+ 추가) 생성된 객체입니다.\n신규 추가된 객체를 실서버에서 삭제해 되돌리려면 상단 '신규(+추가) 객체 삭제 포함'을 체크한 뒤 다시 실행하세요.",
                    "삭제 옵션 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            List<string> restoreKeys = new List<string>();
            List<string> sampleLines = new List<string>();
            for (int i = 0; i < checkedRows.Count; i++)
            {
                DiffEntryDto d = checkedRows[i];
                bool isAdded = string.Equals(d.diffType, "ADDED", StringComparison.OrdinalIgnoreCase);
                if (isAdded && !includeAddedDelete) continue;
                restoreKeys.Add(d.identityKey);
                if (sampleLines.Count < 8)
                {
                    string actionTag = isAdded ? "삭제" : (string.Equals(d.diffType, "REMOVED", StringComparison.OrdinalIgnoreCase) ? "재생성" : "속성복원");
                    sampleLines.Add(string.Format("• [{0}] {1} · {2}", actionTag, d.category, d.naturalKey));
                }
            }

            int actualDeleteCnt = includeAddedDelete ? addedCnt : 0;
            StringBuilder summary = new StringBuilder();
            summary.AppendLine(string.Format("• 기준 스냅샷: {0} ({1})",
                _lastDiff != null && _lastDiff.leftMeta != null ? (_lastDiff.leftMeta.label ?? _lastDiffLeftId) : _lastDiffLeftId,
                _lastDiff != null && _lastDiff.leftMeta != null ? (_lastDiff.leftMeta.createdAt ?? "-") : "-"));
            summary.AppendLine(string.Format("• 복구 계획: 재생성 {0}건 · 속성/연결 복원 {1}건 · 신규객체 삭제 {2}건",
                recreateCnt, updateCnt, actualDeleteCnt));
            if (sampleLines.Count > 0)
            {
                summary.AppendLine();
                summary.Append(string.Join("\n", sampleLines.ToArray()));
                if (restoreKeys.Count > sampleLines.Count)
                {
                    summary.AppendFormat("\n... 외 {0}건", restoreKeys.Count - sampleLines.Count);
                }
            }

            Window owner = Window.GetWindow(this);
            bool confirmed = actualDeleteCnt > 0
                ? PopupDialogs.ConfirmDelete(
                    owner,
                    "스냅샷 복구 (삭제 포함) 확인",
                    "Genesys Config 실서버 복구 실행",
                    summary.ToString(),
                    string.Format("스냅샷 이후 추가된 {0}개 객체가 Config Server에서 영구 삭제되며, 나머지 변경/삭제 항목이 스냅샷 상태로 복구됩니다.\n\n진행하려면 '삭제한다' 를 입력하세요.", actualDeleteCnt))
                : PopupDialogs.ConfirmAction(
                    owner,
                    "스냅샷 복구 확인",
                    "Genesys Config 실서버 복구 실행",
                    summary.ToString(),
                    "복구 실행");
            if (!confirmed) return;

            SetBusy(true, string.Format("Config 실서버 복구 실행 중... ({0}건)", restoreKeys.Count));
            SnapshotRestoreResponseDto restoreResp = null;
            try
            {
                restoreResp = await _cmWorker.RestoreSnapshotAsync(_lastDiffLeftId, restoreKeys, includeAddedDelete, false);
                if (restoreResp != null)
                {
                    if (_logCallback != null && restoreResp.logs != null)
                    {
                        for (int i = 0; i < restoreResp.logs.Count; i++)
                        {
                            _logCallback(restoreResp.failedCount == 0 ? "OK" : "WARN", restoreResp.logs[i]);
                        }
                    }
                    if (_auditCallback != null && restoreResp.auditEvents != null && restoreResp.auditEvents.Count > 0)
                    {
                        _auditCallback(restoreResp.auditEvents);
                    }

                    string resultMsg = string.Format(
                        "재생성 {0}건 · 복원 {1}건 · 삭제 {2}건 · 건너뜀 {3}건 · 실패 {4}건 ({5:0.00}s)",
                        restoreResp.recreatedCount,
                        restoreResp.updatedCount,
                        restoreResp.deletedCount,
                        restoreResp.skippedCount,
                        restoreResp.failedCount,
                        restoreResp.elapsedSeconds);
                    if (restoreResp.failedCount > 0 && restoreResp.results != null)
                    {
                        for (int i = 0; i < restoreResp.results.Count; i++)
                        {
                            if (string.Equals(restoreResp.results[i].status, "Failed", StringComparison.OrdinalIgnoreCase))
                            {
                                resultMsg += "\n첫 오류: " + restoreResp.results[i].naturalKey + " -> " + restoreResp.results[i].detail;
                                break;
                            }
                        }
                    }
                    PopupDialogs.ShowSyntaxToast(
                        owner,
                        restoreResp.failedCount == 0,
                        restoreResp.failedCount == 0 ? "Config 스냅샷 복구 완료" : "Config 스냅샷 복구 일부 실패",
                        resultMsg);
                }
            }
            catch (Exception ex)
            {
                TxtDiffStatus.Text = "복구 실패: " + ex.Message;
                if (_logCallback != null) _logCallback("ERR", "[스냅샷 복구 실패] " + ex.Message);
                PopupDialogs.ShowSyntaxToast(owner, false, "Config 스냅샷 복구 오류", ex.Message);
                SetBusy(false, null);
                UpdateRestoreControlsState();
                return;
            }
            finally
            {
                SetBusy(false, null);
                UpdateRestoreControlsState();
            }

            if (restoreResp != null)
            {
                await RunDiffAsync();
            }
        }

        private void RenderDiffSummaryChips(SnapshotDiffResponseDto resp)
        {
            DiffSummaryChips.Children.Clear();
            if (resp == null) return;
            DiffSummaryChips.Children.Add(MakeChip("+ 추가 " + resp.addedCount, "OkFgBrush", "OkBgBrush"));
            DiffSummaryChips.Children.Add(MakeChip("- 삭제 " + resp.removedCount, "ErrFgBrush", "ErrBgBrush"));
            DiffSummaryChips.Children.Add(MakeChip("~ 변경 " + resp.modifiedCount, "WarnFgBrush", "WarnBgBrush"));
            DiffSummaryChips.Children.Add(MakeChip("= 동일 " + resp.unchangedCount, "MutedBrush", "SurfaceAltBrush"));
        }

        private static Border MakeChip(string text, string fgRes, string bgRes)
        {
            Border bd = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(0, 0, 6, 2),
                BorderThickness = new Thickness(1)
            };
            bd.SetResourceReference(Border.BackgroundProperty, bgRes);
            bd.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            TextBlock tb = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.Bold };
            tb.SetResourceReference(TextBlock.ForegroundProperty, fgRes);
            bd.Child = tb;
            return bd;
        }

        private void DgDiffEntries_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DiffEntryDto entry = DgDiffEntries.SelectedItem as DiffEntryDto;
            if (entry == null)
            {
                ClearDiffInspector();
                return;
            }
            TxtDiffInspectorTitle.Text = string.Format("[{0}] {1} · {2}",
                entry.DiffBadgeText, entry.category ?? "-", entry.naturalKey ?? "-");
            TxtDiffInspectorPaths.Text = string.Format("기준: {0}\n비교: {1}",
                entry.beforeFolderPath ?? "-", entry.afterFolderPath ?? "-");
            DgFieldDiffs.ItemsSource = entry.fieldDiffs ?? new List<FieldDiffDto>();
            BtnCopyDiffDetail.IsEnabled = true;
        }

        private void ClearDiffInspector()
        {
            TxtDiffInspectorTitle.Text = "좌측 표에서 항목을 선택하면 기준(스냅샷) → 비교 대상 속성 변경 내역이 표시됩니다.";
            TxtDiffInspectorPaths.Text = "";
            DgFieldDiffs.ItemsSource = null;
            BtnCopyDiffDetail.IsEnabled = false;
        }

        private void BtnCopyDiffDetail_Click(object sender, RoutedEventArgs e)
        {
            DiffEntryDto entry = DgDiffEntries.SelectedItem as DiffEntryDto;
            if (entry == null) return;
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("[{0}] {1} | Key={2} | Name={3} | Customer={4}",
                entry.diffType, entry.category, entry.naturalKey, entry.name, entry.customerInferred).AppendLine();
            sb.AppendFormat("기준 경로: {0}", entry.beforeFolderPath).AppendLine();
            sb.AppendFormat("비교 경로: {0}", entry.afterFolderPath).AppendLine();
            if (entry.fieldDiffs != null)
            {
                foreach (FieldDiffDto fd in entry.fieldDiffs)
                {
                    sb.AppendFormat("  - {0}: \"{1}\" -> \"{2}\"", fd.field, fd.beforeVal, fd.afterVal).AppendLine();
                }
            }
            try
            {
                Clipboard.SetText(sb.ToString());
                TxtDiffStatus.Text = "선택 항목의 변경 내역을 클립보드에 복사했습니다.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), "클립보드 복사 실패: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnExportDiffCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_lastDiff == null || _lastDiff.entries == null)
            {
                MessageBox.Show(Window.GetWindow(this), "먼저 변경 비교를 실행하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "CSV Files (*.csv)|*.csv";
            sfd.FileName = string.Format("extbatch_compare_{0:yyyyMMdd_HHmmss}.csv", DateTime.Now);
            if (sfd.ShowDialog(Window.GetWindow(this)) != true) return;

            StringBuilder csv = new StringBuilder();
            csv.AppendLine("변경유형,객체유형,식별키,이름,센터,요약,기준경로,비교경로");
            foreach (DiffEntryDto d in _lastDiff.entries)
            {
                csv.AppendLine(string.Join(",", new string[]
                {
                    CsvEsc(d.diffType), CsvEsc(d.category), CsvEsc(d.naturalKey), CsvEsc(d.name),
                    CsvEsc(d.customerInferred), CsvEsc(d.summary), CsvEsc(d.beforeFolderPath), CsvEsc(d.afterFolderPath)
                }));
            }
            File.WriteAllText(sfd.FileName, csv.ToString(), new UTF8Encoding(true));
            TxtDiffStatus.Text = "비교 CSV 저장 완료: " + sfd.FileName;
        }

        private void BtnExportDiffMd_Click(object sender, RoutedEventArgs e)
        {
            if (_lastDiff == null || _lastDiff.entries == null)
            {
                MessageBox.Show(Window.GetWindow(this), "먼저 변경 비교를 실행하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Markdown Files (*.md)|*.md";
            sfd.FileName = string.Format("extbatch_compare_{0:yyyyMMdd_HHmmss}.md", DateTime.Now);
            if (sfd.ShowDialog(Window.GetWindow(this)) != true) return;

            StringBuilder md = new StringBuilder();
            md.AppendLine("# Config 변경 비교 리포트");
            md.AppendLine();
            md.AppendFormat("- **기준 스냅샷**: `{0}` ({1})",
                _lastDiff.leftMeta != null ? _lastDiff.leftMeta.label : "-",
                _lastDiff.leftMeta != null ? _lastDiff.leftMeta.createdAt : "-").AppendLine();
            md.AppendFormat("- **비교 대상**: `{0}` ({1})",
                _lastDiff.rightMeta != null ? _lastDiff.rightMeta.label : "-",
                _lastDiff.rightMeta != null ? _lastDiff.rightMeta.createdAt : "-").AppendLine();
            md.AppendFormat("- **요약**: `+추가 {0}` · `-삭제 {1}` · `~변경 {2}` · `=동일 {3}`",
                _lastDiff.addedCount, _lastDiff.removedCount, _lastDiff.modifiedCount, _lastDiff.unchangedCount).AppendLine();
            md.AppendLine();
            md.AppendLine("| 상태 | 유형 | 식별 키 | 이름 | 센터 | 변경 내역 |");
            md.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (DiffEntryDto d in _lastDiff.entries)
            {
                md.AppendFormat("| {0} | {1} | `{2}` | {3} | {4} | {5} |",
                    d.DiffBadgeText,
                    EscapeMd(d.category),
                    EscapeMd(d.naturalKey),
                    EscapeMd(d.name),
                    EscapeMd(d.customerInferred),
                    EscapeMd(d.summary)).AppendLine();
            }

            File.WriteAllText(sfd.FileName, md.ToString(), new UTF8Encoding(false));
            TxtDiffStatus.Text = "비교 리포트 저장 완료: " + sfd.FileName;
        }

        private void SetBusy(bool busy, string msg)
        {
            _isBusy = busy;
            BtnCaptureSnapshot.IsEnabled = !busy;
            BtnRefreshSnapshots.IsEnabled = !busy;
            BtnRunDiff.IsEnabled = !busy;
            if (!string.IsNullOrEmpty(msg))
            {
                TxtDiffStatus.Text = msg;
            }
            UpdateRestoreControlsState();
        }

        private static string CsvEsc(string v)
        {
            if (v == null) return "\"\"";
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        private static string EscapeMd(string v)
        {
            if (string.IsNullOrEmpty(v)) return "-";
            return v.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
