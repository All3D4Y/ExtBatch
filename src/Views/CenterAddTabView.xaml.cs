using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CmOxeProvisioner.Models;
using CmOxeProvisioner.Services;

namespace CmOxeProvisioner.Views
{
    public class CenterPresetDraftRow : INotifyPropertyChanged
    {
        private string _label = "";
        private string _entityNo = "";
        private string _entityName = "";
        private string _agentPg = "";
        private string _sampleExt = "";
        private string _sampleLoginId = "";
        private string _dnRange = "";
        private string _ctiRange = "";
        private string _note = "";

        public string Label { get { return _label; } set { _label = value ?? ""; RaiseChanged("Label"); } }
        public string EntityNo { get { return _entityNo; } set { _entityNo = value ?? ""; RaiseChanged("EntityNo"); } }
        public string EntityName { get { return _entityName; } set { _entityName = value ?? ""; RaiseChanged("EntityName"); } }
        public string AgentPg { get { return _agentPg; } set { _agentPg = value ?? ""; RaiseChanged("AgentPg"); } }
        public string SampleExt { get { return _sampleExt; } set { _sampleExt = value ?? ""; RaiseChanged("SampleExt"); } }
        public string SampleLoginId { get { return _sampleLoginId; } set { _sampleLoginId = value ?? ""; RaiseChanged("SampleLoginId"); } }
        public string DnRange { get { return _dnRange; } set { _dnRange = value ?? ""; RaiseChanged("DnRange"); } }
        public string CtiRange { get { return _ctiRange; } set { _ctiRange = value ?? ""; RaiseChanged("CtiRange"); } }
        public string Note { get { return _note; } set { _note = value ?? ""; RaiseChanged("Note"); } }

        public bool IsCompletelyEmpty()
        {
            return string.IsNullOrWhiteSpace(Label)
                && string.IsNullOrWhiteSpace(EntityNo)
                && string.IsNullOrWhiteSpace(EntityName)
                && string.IsNullOrWhiteSpace(AgentPg)
                && string.IsNullOrWhiteSpace(SampleExt)
                && string.IsNullOrWhiteSpace(SampleLoginId)
                && string.IsNullOrWhiteSpace(DnRange)
                && string.IsNullOrWhiteSpace(CtiRange)
                && string.IsNullOrWhiteSpace(Note);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void RaiseChanged(string prop)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(prop));
        }
    }

    public class CenterTargetPreviewRow : INotifyPropertyChanged
    {
        private string _key;
        private string _label;
        private string _typeDisplay;
        private string _pathDisplay;
        private string _statusText;

        public string Key { get { return _key; } set { _key = value; RaiseChanged("Key"); } }
        public string Label { get { return _label; } set { _label = value; RaiseChanged("Label"); } }
        public string TypeDisplay { get { return _typeDisplay; } set { _typeDisplay = value; RaiseChanged("TypeDisplay"); } }
        public string PathDisplay { get { return _pathDisplay; } set { _pathDisplay = value; RaiseChanged("PathDisplay"); } }
        public string StatusText { get { return _statusText; } set { _statusText = value; RaiseChanged("StatusText"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void RaiseChanged(string prop)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(prop));
        }
    }

    public class CenterCreateInput
    {
        public string CenterName { get; set; }
        public string Location { get; set; }
        public string Note { get; set; }
        public bool UseVQueue { get; set; }
        public bool UseIvr { get; set; }
        public List<OxePreset> Presets { get; set; }
    }

    public class CenterSaveOutcome
    {
        public CmCenterCreateResponse CmResponse { get; set; }
        public bool SavedToJson { get; set; }
        public bool UpdatedExistingJson { get; set; }
    }

    public partial class CenterAddTabView : UserControl
    {
        private sealed class TargetDef
        {
            public string Key;
            public string Label;
            public string Kind;
            public bool UsesLocation;
            public string BasePath;
            public string Condition;
        }

        private static readonly TargetDef[] TargetDefs = new TargetDef[]
        {
            new TargetDef { Key = "Persons",     Label = "Persons",                 Kind = "폴더",        UsesLocation = true,  BasePath = "Configuration > Environment > Persons",                                          Condition = "Always" },
            new TargetDef { Key = "PlaceAgent",  Label = "Places (AGENT)",          Kind = "폴더",        UsesLocation = true,  BasePath = "Configuration > Environment > Places > AGENT",                                   Condition = "Always" },
            new TargetDef { Key = "AgentLogin",  Label = "Agent Logins",            Kind = "폴더",        UsesLocation = true,  BasePath = "Configuration > Environment > Switches > TServerAlcatel > Agent Logins",         Condition = "Always" },
            new TargetDef { Key = "AcdPos",      Label = "DNs > ACDPosition",       Kind = "폴더",        UsesLocation = true,  BasePath = "Configuration > Environment > Switches > TServerAlcatel > DNs > ACDPosition",    Condition = "Always" },
            new TargetDef { Key = "AgentDn",     Label = "DNs > AGENT_DN",          Kind = "폴더",        UsesLocation = true,  BasePath = "Configuration > Environment > Switches > TServerAlcatel > DNs > AGENT_DN",       Condition = "Always" },
            new TargetDef { Key = "Rsi",         Label = "DNs > RSI",               Kind = "폴더",        UsesLocation = true,  BasePath = "Configuration > Environment > Switches > TServerAlcatel > DNs > RSI",            Condition = "Always" },
            new TargetDef { Key = "AgentGroups", Label = "Agent Groups",            Kind = "폴더",        UsesLocation = false, BasePath = "Configuration > Environment > Agent Groups",                                     Condition = "Always" },
            new TargetDef { Key = "DnGroups",    Label = "DN Groups",               Kind = "폴더",        UsesLocation = false, BasePath = "Configuration > Environment > DN Groups",                                        Condition = "Always" },
            new TargetDef { Key = "Scripts",     Label = "Scripts > 유세스파트너스", Kind = "폴더",        UsesLocation = false, BasePath = "Configuration > Environment > Scripts > 유세스파트너스",                          Condition = "Always" },
            new TargetDef { Key = "VQueue",      Label = "DNs > VQueue",            Kind = "폴더",        UsesLocation = false, BasePath = "Configuration > Environment > Switches > TServerAlcatel > DNs > VQueue",         Condition = "VQueue" },
            new TargetDef { Key = "PlaceGroups", Label = "Place Groups",            Kind = "Place Group", UsesLocation = false, BasePath = "Configuration > Environment > Place Groups",                                     Condition = "IVR" }
        };

        private List<CustomerInfo> _allCustomers = new List<CustomerInfo>();
        private readonly ObservableCollection<CenterPresetDraftRow> _presetRows = new ObservableCollection<CenterPresetDraftRow>();
        private readonly ObservableCollection<CenterTargetPreviewRow> _targetRows = new ObservableCollection<CenterTargetPreviewRow>();
        private bool _isInitializing = true;
        private bool _isBusy = false;

        public Func<CenterCreateInput, System.Threading.Tasks.Task<CmCenterCheckResponse>> PrecheckCenterAsyncHandler { get; set; }
        public Func<CenterCreateInput, System.Threading.Tasks.Task<CenterSaveOutcome>> CreateCenterAsyncHandler { get; set; }
        public Action<string, string> SaveLogHandler { get; set; }

        public CenterAddTabView()
        {
            InitializeComponent();
            DgCenterPresets.ItemsSource = _presetRows;
            DgCmTargets.ItemsSource = _targetRows;
            EnsureAtLeastOnePresetRow();
            _isInitializing = false;
            RebuildTargetPreview();
        }

        public void SetCustomers(List<CustomerInfo> customers)
        {
            _allCustomers = customers != null ? new List<CustomerInfo>(customers) : new List<CustomerInfo>();
            PopulateLocationsDropdown();
            UpdateCustomerExistingBadge();
            RebuildTargetPreview();
        }

        public void SetBusyState(bool busy)
        {
            _isBusy = busy;
            bool enabled = !busy;
            BtnCenterPrecheck.IsEnabled = enabled;
            BtnCenterCreate.IsEnabled = enabled;
            BtnFillCenterSample.IsEnabled = enabled;
            BtnResetCenterTab.IsEnabled = enabled;
            BtnAddPresetRow.IsEnabled = enabled;
            BtnRemovePresetRow.IsEnabled = enabled;
            CmbLocation.IsEnabled = enabled;
            TxtCenterName.IsReadOnly = busy;
            TxtCenterNote.IsReadOnly = busy;
            ChkUseVQueue.IsEnabled = enabled;
            ChkUseIvr.IsEnabled = enabled;
            DgCenterPresets.IsReadOnly = busy;
        }

        public void AppendExternalLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            Dispatcher.BeginInvoke(new Action(delegate()
            {
                if (TxtCenterLog.Text.Length > 0) TxtCenterLog.AppendText(Environment.NewLine);
                TxtCenterLog.AppendText(line);
                TxtCenterLog.ScrollToEnd();
            }));
        }

        private void PopulateLocationsDropdown()
        {
            string prev = CmbLocation.SelectedItem as string;
            List<string> locs = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _allCustomers.Count; i++)
            {
                string raw = (_allCustomers[i].floor ?? "").Trim();
                if (string.IsNullOrEmpty(raw) || raw == "(위치 없음)" || raw == "-") continue;
                string[] parts = raw.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                for (int p = 0; p < parts.Length; p++)
                {
                    string token = parts[p].Trim();
                    if (!string.IsNullOrEmpty(token) && seen.Add(token))
                    {
                        locs.Add(token);
                    }
                }
            }

            if (locs.Count == 0)
            {
                locs.Add("TEST");
            }
            locs.Add("(위치 없음)");

            bool wasInit = _isInitializing;
            _isInitializing = true;
            CmbLocation.ItemsSource = locs;
            if (!string.IsNullOrEmpty(prev) && locs.Contains(prev))
            {
                CmbLocation.SelectedItem = prev;
            }
            else if (locs.Count > 0)
            {
                CmbLocation.SelectedIndex = 0;
            }
            _isInitializing = wasInit;
        }

        private void EnsureAtLeastOnePresetRow()
        {
            if (_presetRows.Count == 0)
            {
                _presetRows.Add(new CenterPresetDraftRow());
            }
        }

        private string GetSelectedLocationValue()
        {
            string loc = CmbLocation.SelectedItem as string;
            return (loc ?? "").Trim();
        }

        private void UpdateCustomerExistingBadge()
        {
            string cname = (TxtCenterName.Text ?? "").Trim();
            bool exists = false;
            if (!string.IsNullOrEmpty(cname))
            {
                for (int i = 0; i < _allCustomers.Count; i++)
                {
                    if (string.Equals(_allCustomers[i].name, cname, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }
            TxtCenterExistChip.Text = exists ? "기존 등록됨 (보완 가능)" : "신규 센터";
        }

        private void InputField_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateCustomerExistingBadge();
            RebuildTargetPreview();
        }

        private void InputField_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateCustomerExistingBadge();
            RebuildTargetPreview();
        }

        private void InputField_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            RebuildTargetPreview();
        }

        private void RebuildTargetPreview()
        {
            if (_targetRows == null) return;
            string cnameRaw = (TxtCenterName.Text ?? "").Trim();
            string cnameDisp = string.IsNullOrEmpty(cnameRaw) ? "<센터명>" : cnameRaw;
            string loc = GetSelectedLocationValue();
            bool hasLoc = !string.IsNullOrEmpty(loc) && loc != "(위치 없음)" && loc != "-";
            bool useVq = (ChkUseVQueue.IsChecked == true);
            bool useIvr = (ChkUseIvr.IsChecked == true);

            _targetRows.Clear();
            int folderCnt = 0;
            int pgCnt = 0;

            for (int i = 0; i < TargetDefs.Length; i++)
            {
                TargetDef def = TargetDefs[i];
                if (def.Condition == "VQueue" && !useVq) continue;
                if (def.Condition == "IVR" && !useIvr) continue;

                string full = (def.UsesLocation && hasLoc)
                    ? string.Format("{0} > {1} > {2}", def.BasePath, loc, cnameDisp)
                    : string.Format("{0} > {1}", def.BasePath, cnameDisp);

                if (def.Kind == "Place Group") pgCnt++;
                else folderCnt++;

                _targetRows.Add(new CenterTargetPreviewRow
                {
                    Key = def.Key,
                    Label = def.Label,
                    TypeDisplay = def.Kind,
                    PathDisplay = full,
                    StatusText = string.IsNullOrEmpty(cnameRaw) ? "센터명 입력 대기" : "생성 대기"
                });
            }

            TxtTargetSummary.Text = string.Format("총 {0}건 (폴더 {1} · Place Group {2})", _targetRows.Count, folderCnt, pgCnt);
        }

        private void ApplyStatusMapFromResults(List<CmCenterItemResultDto> items, bool isCheckMode)
        {
            if (items == null) return;
            Dictionary<string, CmCenterItemResultDto> byKey = new Dictionary<string, CmCenterItemResultDto>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < items.Count; i++)
            {
                if (!string.IsNullOrEmpty(items[i].key)) byKey[items[i].key] = items[i];
            }

            for (int i = 0; i < _targetRows.Count; i++)
            {
                CenterTargetPreviewRow row = _targetRows[i];
                CmCenterItemResultDto dto;
                if (!byKey.TryGetValue(row.Key, out dto)) continue;

                string st = dto.status ?? "";
                if (isCheckMode)
                {
                    if (st == "Exists") row.StatusText = "이미 존재 (건너뜀)";
                    else if (st == "WillCreateWithLocation") row.StatusText = "생성 예정 (위치포함)";
                    else if (st == "WillCreate") row.StatusText = "생성 가능";
                    else if (st == "ParentMissing") row.StatusText = "상위 경로 오류";
                    else row.StatusText = st;
                }
                else
                {
                    if (st == "Created") row.StatusText = "생성 완료";
                    else if (st == "Skipped") row.StatusText = "건너뜀 (존재)";
                    else if (st == "Failed") row.StatusText = "실패";
                    else row.StatusText = st;
                }
            }
        }

        private CenterCreateInput BuildCreateInput()
        {
            DgCenterPresets.CommitEdit(DataGridEditingUnit.Cell, true);
            DgCenterPresets.CommitEdit(DataGridEditingUnit.Row, true);

            string cname = (TxtCenterName.Text ?? "").Trim();
            string loc = GetSelectedLocationValue();
            string note = (TxtCenterNote.Text ?? "").Trim();

            List<OxePreset> presets = new List<OxePreset>();
            int seq = 0;
            for (int i = 0; i < _presetRows.Count; i++)
            {
                CenterPresetDraftRow r = _presetRows[i];
                if (r == null || r.IsCompletelyEmpty()) continue;
                seq++;

                string label = (r.Label ?? "").Trim();
                string entNo = (r.EntityNo ?? "").Trim();
                string entName = (r.EntityName ?? "").Trim();
                string pg = (r.AgentPg ?? "").Trim();
                if (string.IsNullOrEmpty(label))
                {
                    if (!string.IsNullOrEmpty(entNo) || !string.IsNullOrEmpty(pg))
                        label = string.Format("{0} (Ent {1} / PG {2})", cname, string.IsNullOrEmpty(entNo) ? "-" : entNo, string.IsNullOrEmpty(pg) ? "-" : pg);
                    else if (!string.IsNullOrEmpty(entName))
                        label = string.Format("{0} ({1})", cname, entName);
                    else
                        label = string.Format("{0} #{1}", cname, seq);
                }

                OxePreset op = new OxePreset();
                op.label = label;
                op.entityNo = entNo;
                op.entityName = entName;
                op.agentPg = pg;
                op.sampleExt = (r.SampleExt ?? "").Trim();
                op.sampleLoginId = (r.SampleLoginId ?? "").Trim();
                op.source = "직접 등록";
                op.dnRange = (r.DnRange ?? "").Trim();
                op.ctiRange = (r.CtiRange ?? "").Trim();
                op.note = (r.Note ?? "").Trim();
                presets.Add(op);
            }

            CenterCreateInput input = new CenterCreateInput();
            input.CenterName = cname;
            input.Location = loc;
            input.Note = note;
            input.UseVQueue = (ChkUseVQueue.IsChecked == true);
            input.UseIvr = (ChkUseIvr.IsChecked == true);
            input.Presets = presets;
            return input;
        }

        private void BtnAddPresetRow_Click(object sender, RoutedEventArgs e)
        {
            _presetRows.Add(new CenterPresetDraftRow());
        }

        private void BtnRemovePresetRow_Click(object sender, RoutedEventArgs e)
        {
            CenterPresetDraftRow sel = DgCenterPresets.SelectedItem as CenterPresetDraftRow;
            if (sel != null)
            {
                _presetRows.Remove(sel);
            }
            else if (_presetRows.Count > 0)
            {
                _presetRows.RemoveAt(_presetRows.Count - 1);
            }
            EnsureAtLeastOnePresetRow();
        }

        private void BtnFillCenterSample_Click(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            List<string> locs = CmbLocation.ItemsSource as List<string>;
            if (locs != null)
            {
                int testIdx = locs.FindIndex(x => string.Equals(x, "TEST", StringComparison.OrdinalIgnoreCase));
                if (testIdx >= 0) CmbLocation.SelectedIndex = testIdx;
            }
            TxtCenterName.Text = "FTEST";
            TxtCenterNote.Text = "폴더/Place Group 테스트용 임시 센터";
            ChkUseVQueue.IsChecked = true;
            ChkUseIvr.IsChecked = true;
            _presetRows.Clear();
            _presetRows.Add(new CenterPresetDraftRow
            {
                Label = "FTEST #1 (Ent 999 / PG 69999)",
                EntityNo = "999",
                EntityName = "FTEST",
                AgentPg = "69999",
                SampleExt = "39950",
                SampleLoginId = "79950",
                DnRange = "39950~39959",
                CtiRange = "79950~79959",
                Note = "테스트 샘플"
            });
            _isInitializing = false;
            UpdateCustomerExistingBadge();
            RebuildTargetPreview();
        }

        private void BtnResetCenterTab_Click(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            if (CmbLocation.Items.Count > 0) CmbLocation.SelectedIndex = 0;
            TxtCenterName.Text = "";
            TxtCenterNote.Text = "";
            ChkUseVQueue.IsChecked = false;
            ChkUseIvr.IsChecked = false;
            _presetRows.Clear();
            EnsureAtLeastOnePresetRow();
            _isInitializing = false;
            UpdateCustomerExistingBadge();
            RebuildTargetPreview();
        }

        private async void BtnCenterPrecheck_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            CenterCreateInput input = BuildCreateInput();
            if (string.IsNullOrWhiteSpace(input.CenterName))
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "센터명 누락", "사전 점검할 센터명을 입력하세요.");
                TxtCenterName.Focus();
                return;
            }
            if (PrecheckCenterAsyncHandler == null) return;

            try
            {
                CmCenterCheckResponse res = await PrecheckCenterAsyncHandler(input);
                if (res != null && res.items != null)
                {
                    ApplyStatusMapFromResults(res.items, true);
                    int existsCnt = 0;
                    int createCnt = 0;
                    int errCnt = 0;
                    for (int i = 0; i < res.items.Count; i++)
                    {
                        string st = res.items[i].status ?? "";
                        if (st == "Exists") existsCnt++;
                        else if (st == "ParentMissing") errCnt++;
                        else createCnt++;
                    }
                    string msg = string.Format(
                        "대상 {0}건 중 신규 생성 가능 {1}건 / 이미 존재(건너뜀) {2}건{3}",
                        res.items.Count,
                        createCnt,
                        existsCnt,
                        errCnt > 0 ? string.Format(" / 상위 경로 오류 {0}건", errCnt) : "");
                    PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), errCnt == 0, "Config 사전 점검 완료", msg);
                }
            }
            catch (Exception ex)
            {
                AppendExternalLog("[사전 점검 오류] " + ex.Message);
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "Config 사전 점검 오류", ex.Message);
            }
        }

        private async void BtnCenterCreate_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            CenterCreateInput input = BuildCreateInput();
            if (string.IsNullOrWhiteSpace(input.CenterName))
            {
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "센터명 누락", "생성할 센터명을 입력하세요. (프리셋/선번장은 공란 허용)");
                TxtCenterName.Focus();
                return;
            }
            if (CreateCenterAsyncHandler == null) return;

            bool isExistingCenter = false;
            for (int i = 0; i < _allCustomers.Count; i++)
            {
                if (string.Equals(_allCustomers[i].name, input.CenterName, StringComparison.OrdinalIgnoreCase))
                {
                    isExistingCenter = true;
                    break;
                }
            }

            StringBuilder detail = new StringBuilder();
            detail.AppendLine(string.Format("• 센터명: {0} (위치: {1})", input.CenterName, string.IsNullOrEmpty(input.Location) ? "(위치 없음)" : input.Location));
            detail.AppendLine(string.Format("• Config 생성 대상: 총 {0}곳 (VQueue={1}, IVR Place Group={2})",
                _targetRows.Count, input.UseVQueue ? "사용" : "미사용", input.UseIvr ? "사용" : "미사용"));
            detail.AppendLine(string.Format("• OXE 프리셋 · 선번장 등록: {0}건", input.Presets.Count));
            if (isExistingCenter)
            {
                detail.AppendLine("※ 동일 이름의 센터가 이미 목록에 있습니다. Config 누락 경로 보완 및 선번장/프리셋 정보를 업데이트합니다.");
            }

            if (!PopupDialogs.ConfirmAction(
                Window.GetWindow(this),
                "센터 Config 생성 및 등록 확인",
                isExistingCenter ? "기존 센터 Config 보완 생성 및 설정 업데이트" : "신규 센터 Config 경로 일괄 생성 및 등록",
                detail.ToString().TrimEnd(),
                "생성 실행"))
            {
                return;
            }

            try
            {
                CenterSaveOutcome outcome = await CreateCenterAsyncHandler(input);
                if (outcome != null && outcome.CmResponse != null)
                {
                    ApplyStatusMapFromResults(outcome.CmResponse.items, false);
                    string summary = string.Format(
                        "Config 생성 {0}건 / 건너뜀 {1}건 / 실패 {2}건 ({3:N1}초)\ncm_customers.json {4}",
                        outcome.CmResponse.createdCount,
                        outcome.CmResponse.skippedCount,
                        outcome.CmResponse.failedCount,
                        outcome.CmResponse.elapsedSeconds,
                        outcome.SavedToJson ? (outcome.UpdatedExistingJson ? "업데이트 완료" : "신규 등록 완료") : "저장 안 됨");
                    PopupDialogs.ShowSyntaxToast(
                        Window.GetWindow(this),
                        outcome.CmResponse.failedCount == 0,
                        outcome.CmResponse.failedCount == 0 ? "센터 생성 · 등록 완료" : "일부 항목 실패",
                        summary);
                }
            }
            catch (Exception ex)
            {
                AppendExternalLog("[센터 생성 오류] " + ex.Message);
                PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), false, "센터 생성 오류", ex.Message);
            }
        }

        private void BtnSaveCenterLog_Click(object sender, RoutedEventArgs e)
        {
            if (SaveLogHandler != null)
            {
                SaveLogHandler(TxtCenterLog.Text, "ExtBatch_Center");
            }
        }

        private void BtnClearCenterLog_Click(object sender, RoutedEventArgs e)
        {
            TxtCenterLog.Clear();
        }
    }
}
