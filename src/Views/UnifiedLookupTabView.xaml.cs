using System;
using System.Collections.Generic;
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
    public class FilterOptionItem
    {
        public string Label { get; set; }
        public string Value { get; set; }
    }

    public class AttrRowItem
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    public partial class UnifiedLookupTabView : UserControl
    {
        private CmWorkerService _cmWorker;
        private Func<List<CustomerInfo>> _getCustomers;
        private Func<string> _getDialPlanDbPath;
        private Action<string, string, string> _navigateToExtCti;
        private Action<string> _navigateToPerson;
        private Action<string, string> _logCallback;

        private bool _isUiInit = true;
        private bool _isBusy = false;
        private string _activeMode = "search";     // search | audit
        private string _activeAuditSub = "summary"; // summary | pairs | issues

        private List<LookupItemDto> _lastSearchItems = new List<LookupItemDto>();
        private CenterAuditResponseDto _lastAuditResp = null;

        public UnifiedLookupTabView()
        {
            InitializeComponent();
            InitFilterCombos();
            ApplyModeVisuals();
            ApplyAuditSubVisuals();
            _isUiInit = false;
        }

        public void Initialize(
            CmWorkerService cmWorker,
            Func<List<CustomerInfo>> getCustomers,
            Func<string> getDialPlanDbPath,
            Action<string, string, string> navigateToExtCti,
            Action<string> navigateToPerson,
            Action<string, string> logCallback)
        {
            _cmWorker = cmWorker;
            _getCustomers = getCustomers;
            _getDialPlanDbPath = getDialPlanDbPath;
            _navigateToExtCti = navigateToExtCti;
            _navigateToPerson = navigateToPerson;
            _logCallback = logCallback;
            RefreshCustomers();
        }

        public void RefreshCustomers()
        {
            string prevSearchCust = CmbLookupCustomer.SelectedValue as string;
            string prevAuditCust = CmbAuditCustomer.SelectedValue as string;

            List<CustomerInfo> customers = _getCustomers != null ? _getCustomers() : new List<CustomerInfo>();
            List<FilterOptionItem> searchList = new List<FilterOptionItem>();
            searchList.Add(new FilterOptionItem { Label = "전체 센터", Value = "" });

            List<FilterOptionItem> auditList = new List<FilterOptionItem>();
            auditList.Add(new FilterOptionItem { Label = "전체 고객사 요약 (ALL)", Value = "ALL" });

            if (customers != null)
            {
                for (int i = 0; i < customers.Count; i++)
                {
                    CustomerInfo c = customers[i];
                    if (c == null || string.IsNullOrWhiteSpace(c.name)) continue;
                    FilterOptionItem item = new FilterOptionItem
                    {
                        Label = c.DisplayName,
                        Value = c.name
                    };
                    searchList.Add(item);
                    auditList.Add(item);
                }
            }

            _isUiInit = true;
            CmbLookupCustomer.ItemsSource = searchList;
            CmbLookupCustomer.SelectedValue = string.IsNullOrEmpty(prevSearchCust) ? "" : prevSearchCust;
            if (CmbLookupCustomer.SelectedIndex < 0) CmbLookupCustomer.SelectedIndex = 0;

            CmbAuditCustomer.ItemsSource = auditList;
            CmbAuditCustomer.SelectedValue = string.IsNullOrEmpty(prevAuditCust) ? "ALL" : prevAuditCust;
            if (CmbAuditCustomer.SelectedIndex < 0) CmbAuditCustomer.SelectedIndex = 0;
            _isUiInit = false;
        }

        private void InitFilterCombos()
        {
            CmbLookupSource.ItemsSource = new List<string>
            {
                "전체 (Config + 선번장)",
                "Config 실서버만",
                "선번장 DB만"
            };
            CmbLookupSource.SelectedIndex = 0;

            CmbLookupCategory.ItemsSource = new List<string>
            {
                "전체",
                "Agent DN",
                "Place",
                "Agent Login",
                "ACD Position",
                "Person",
                "RSI",
                "VQueue",
                "Place Group",
                "Agent Group",
                "DN Group",
                "Script",
                "선번장 센터",
                "선번장 라인",
                "Entity/IVR/PRI/DVG"
            };
            CmbLookupCategory.SelectedIndex = 0;
        }

        private void BtnModeSearch_Click(object sender, RoutedEventArgs e)
        {
            _activeMode = "search";
            ApplyModeVisuals();
        }

        private void BtnModeAudit_Click(object sender, RoutedEventArgs e)
        {
            _activeMode = "audit";
            ApplyModeVisuals();
        }

        private void ApplyModeVisuals()
        {
            bool isSearch = (_activeMode == "search");
            SearchModeRoot.Visibility = isSearch ? Visibility.Visible : Visibility.Collapsed;
            AuditModeRoot.Visibility = isSearch ? Visibility.Collapsed : Visibility.Visible;
            ApplySegmentedButtonStyle(BtnModeSearch, isSearch);
            ApplySegmentedButtonStyle(BtnModeAudit, !isSearch);
        }

        private void BtnAuditSubSummary_Click(object sender, RoutedEventArgs e)
        {
            _activeAuditSub = "summary";
            ApplyAuditSubVisuals();
        }

        private void BtnAuditSubPairs_Click(object sender, RoutedEventArgs e)
        {
            _activeAuditSub = "pairs";
            ApplyAuditSubVisuals();
        }

        private void BtnAuditSubIssues_Click(object sender, RoutedEventArgs e)
        {
            _activeAuditSub = "issues";
            ApplyAuditSubVisuals();
        }

        private void ApplyAuditSubVisuals()
        {
            bool sSum = (_activeAuditSub == "summary");
            bool sPair = (_activeAuditSub == "pairs");
            bool sIss = (_activeAuditSub == "issues");

            DgAuditSummary.Visibility = sSum ? Visibility.Visible : Visibility.Collapsed;
            DgAuditPairs.Visibility = sPair ? Visibility.Visible : Visibility.Collapsed;
            DgAuditIssues.Visibility = sIss ? Visibility.Visible : Visibility.Collapsed;

            ApplySegmentedButtonStyle(BtnAuditSubSummary, sSum);
            ApplySegmentedButtonStyle(BtnAuditSubPairs, sPair);
            ApplySegmentedButtonStyle(BtnAuditSubIssues, sIss);
        }

        private static void ApplySegmentedButtonStyle(Button btn, bool active)
        {
            if (btn == null) return;
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

        private void TxtLookupQuery_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                RunSearchAsync(false);
            }
        }

        private void FilterCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isUiInit || _isBusy) return;
            if (_lastSearchItems != null && _lastSearchItems.Count > 0)
            {
                RunSearchAsync(false);
            }
        }

        private void BtnLookupSearch_Click(object sender, RoutedEventArgs e)
        {
            RunSearchAsync(false);
        }

        private void BtnLookupForceRefresh_Click(object sender, RoutedEventArgs e)
        {
            RunSearchAsync(true);
        }

        private async void RunSearchAsync(bool forceRefresh)
        {
            if (_isBusy) return;
            SetBusy(true, forceRefresh ? "Config Server 최신 데이터 로딩 중..." : "통합 검색 실행 중...");
            try
            {
                string query = (TxtLookupQuery.Text ?? "").Trim();
                string srcMode = (CmbLookupSource.SelectedItem as string) ?? "전체 (Config + 선번장)";
                string catSel = (CmbLookupCategory.SelectedItem as string) ?? "전체";
                string custSel = (CmbLookupCustomer.SelectedValue as string) ?? "";
                bool exact = (ChkLookupExact.IsChecked == true);

                bool wantCm = !string.Equals(srcMode, "선번장 DB만", StringComparison.Ordinal) && !IsDialPlanOnlyCategory(catSel);
                bool wantDialPlan = !string.Equals(srcMode, "Config 실서버만", StringComparison.Ordinal) && !IsCmOnlyCategory(catSel);

                List<LookupItemDto> merged = new List<LookupItemDto>();
                Dictionary<string, int> chipCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                string cmNote = "";
                double cmElapsed = 0;
                int cmLoaded = 0;

                if (wantCm)
                {
                    if (_cmWorker == null)
                    {
                        throw new InvalidOperationException("Config 백엔드 워커가 초기화되지 않았습니다.");
                    }
                    string backendCat = MapToBackendCategory(catSel);
                    LookupSearchResponseDto cmResp = await _cmWorker.LookupSearchAsync(
                        query,
                        custSel,
                        backendCat,
                        exact,
                        forceRefresh,
                        1500);
                    if (cmResp != null)
                    {
                        cmLoaded = cmResp.totalLoaded;
                        cmElapsed = cmResp.elapsedSeconds;
                        cmNote = cmResp.fromCache ? "Config 캐시" : "Config 라이브";
                        if (cmResp.items != null)
                        {
                            for (int i = 0; i < cmResp.items.Count; i++)
                            {
                                LookupItemDto it = cmResp.items[i];
                                if (string.IsNullOrEmpty(it.source)) it.source = "Config";
                                merged.Add(it);
                            }
                        }
                        if (cmResp.categoryCounts != null)
                        {
                            foreach (KeyValuePair<string, int> kv in cmResp.categoryCounts)
                            {
                                AddChipCount(chipCounts, kv.Key, kv.Value);
                            }
                        }
                    }
                }

                if (wantDialPlan)
                {
                    List<LookupItemDto> dpHits = await Task.Run(delegate()
                    {
                        return SearchLocalDialPlan(query, catSel, custSel, exact);
                    });
                    for (int i = 0; i < dpHits.Count; i++)
                    {
                        merged.Add(dpHits[i]);
                        AddChipCount(chipCounts, dpHits[i].category, 1);
                    }
                }

                _lastSearchItems = merged;
                DgLookupResults.ItemsSource = _lastSearchItems;
                RenderLookupChips(chipCounts, merged.Count);

                TxtLookupStatus.Text = wantCm
                    ? string.Format("조회 완료: 총 {0}건 표시 (Config 전체 {1}개 객체 중 검색 · {2})", merged.Count, cmLoaded, cmNote)
                    : string.Format("선번장 DB 조회 완료: 총 {0}건", merged.Count);
                TxtLookupElapsed.Text = wantCm ? string.Format("{0:0.00}s", cmElapsed) : "Local JSON";

                if (_lastSearchItems.Count > 0)
                {
                    DgLookupResults.SelectedIndex = 0;
                }
                else
                {
                    ClearDetailInspector();
                }

                if (_logCallback != null)
                {
                    _logCallback("INFO", string.Format("[통합 조회] 검색='{0}', 출처='{1}', 결과={2}건",
                        string.IsNullOrEmpty(query) ? "*" : query, srcMode, merged.Count));
                }
            }
            catch (Exception ex)
            {
                TxtLookupStatus.Text = "조회 오류: " + ex.Message;
                if (_logCallback != null) _logCallback("ERR", "[통합 조회 실패] " + ex.Message);
                MessageBox.Show(Window.GetWindow(this), "통합 조회 중 오류가 발생했습니다:\n" + ex.Message, "조회 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private List<LookupItemDto> SearchLocalDialPlan(string query, string catSel, string custSel, bool exact)
        {
            List<LookupItemDto> results = new List<LookupItemDto>();
            string dpPath = _getDialPlanDbPath != null ? _getDialPlanDbPath() : null;
            if (string.IsNullOrEmpty(dpPath) || !File.Exists(dpPath)) return results;

            DialPlanDatabase db = DialPlanRepository.Load(dpPath);
            if (db == null) return results;

            HashSet<string> numTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(query))
            {
                NumberParseResult parsed = OxeScriptService.ParseNumbersStrict(query);
                if (parsed != null && parsed.Errors.Count == 0 && parsed.Items.Count > 0)
                {
                    for (int i = 0; i < parsed.Items.Count; i++) numTokens.Add(parsed.Items[i]);
                }
            }

            string qLower = (query ?? "").ToLowerInvariant();
            bool hasCustFilter = !string.IsNullOrEmpty(custSel);
            string custLower = hasCustFilter ? custSel.ToLowerInvariant() : "";

            // 1. DialPlan Centers & Lines
            bool wantCenter = (catSel == "전체" || catSel == "선번장 센터");
            bool wantLine = (catSel == "전체" || catSel == "선번장 라인");

            foreach (DialPlanCenter c in db.centers)
            {
                if (c == null) continue;
                string custName = !string.IsNullOrEmpty(c.cmCustomerName) ? c.cmCustomerName : c.project;
                if (hasCustFilter && !ContainsIgnoreCase(custName, custLower) && !ContainsIgnoreCase(c.project, custLower))
                    continue;

                if (wantCenter)
                {
                    string summaryText = string.Format("Entity:{0} | PG:{1} | Ext:{2} | Login:{3}",
                        SafeDash(c.EntitySummary), SafeDash(c.PgSummary), SafeDash(c.ExtRangeSummary), SafeDash(c.LoginRangeSummary));
                    bool hit = string.IsNullOrEmpty(qLower)
                        || MatchesTextOrNumbers(c.project, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(c.location, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(c.cmCustomerName, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(c.MainNumberSummary, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(c.EntitySummary, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(c.PgSummary, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(c.ExtRangeSummary, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(c.LoginRangeSummary, qLower, numTokens, exact);
                    if (hit)
                    {
                        LookupItemDto dto = new LookupItemDto();
                        dto.id = "DP_CENTER:" + (c.id ?? c.project);
                        dto.source = "선번장";
                        dto.category = "선번장 센터";
                        dto.subType = DialPlanStatus.ToDisplay(c.status);
                        dto.key = c.project ?? "";
                        dto.name = string.IsNullOrEmpty(c.MainNumberSummary) || c.MainNumberSummary == "-"
                            ? (c.location ?? "-")
                            : (c.location + " · 대표 " + c.MainNumberSummary);
                        dto.customerInferred = custName;
                        dto.folderPath = string.IsNullOrEmpty(c.apUrl) ? ("Dial-Plan > " + c.location) : c.apUrl;
                        dto.relationSummary = summaryText;
                        dto.state = c.status;
                        dto.attributes = new Dictionary<string, string>();
                        dto.attributes["상태"] = DialPlanStatus.ToDisplay(c.status);
                        dto.attributes["위치"] = c.location ?? "";
                        dto.attributes["프로젝트"] = c.project ?? "";
                        dto.attributes["Config센터명"] = c.cmCustomerName ?? "";
                        dto.attributes["대표번호"] = c.MainNumberSummary ?? "";
                        dto.attributes["Entity NO"] = c.EntitySummary ?? "";
                        dto.attributes["Agent PG"] = c.PgSummary ?? "";
                        dto.attributes["Phone 내선"] = c.ExtRangeSummary ?? "";
                        dto.attributes["Login-ID"] = c.LoginRangeSummary ?? "";
                        dto.attributes["RSI"] = c.RsiSummary ?? "";
                        dto.attributes["AP URL"] = c.apUrl ?? "";
                        dto.attributes["비고"] = c.notes ?? "";
                        results.Add(dto);
                    }
                }

                if (wantLine && c.lines != null)
                {
                    for (int li = 0; li < c.lines.Count; li++)
                    {
                        DialPlanLine ln = c.lines[li];
                        if (ln == null) continue;
                        bool hit = string.IsNullOrEmpty(qLower)
                            || MatchesTextOrNumbers(ln.phoneExt, qLower, numTokens, exact)
                            || MatchesTextOrNumbers(ln.loginId, qLower, numTokens, exact)
                            || MatchesTextOrNumbers(ln.agentPg, qLower, numTokens, exact)
                            || MatchesTextOrNumbers(ln.entityNo, qLower, numTokens, exact)
                            || MatchesTextOrNumbers(ln.mainNumber, qLower, numTokens, exact)
                            || MatchesTextOrNumbers(ln.inboundRsi, qLower, numTokens, exact)
                            || MatchesTextOrNumbers(ln.agentTransferRsi, qLower, numTokens, exact)
                            || MatchesTextOrNumbers(ln.subTask, qLower, numTokens, exact)
                            || MatchesTextOrNumbers(ln.note, qLower, numTokens, exact)
                            || MatchesTextOrNumbers(c.project, qLower, numTokens, exact);
                        if (hit)
                        {
                            LookupItemDto dto = new LookupItemDto();
                            dto.id = "DP_LINE:" + c.project + ":" + li;
                            dto.source = "선번장";
                            dto.category = "선번장 라인";
                            dto.subType = string.IsNullOrEmpty(ln.subTask) ? DialPlanStatus.ToDisplay(c.status) : ln.subTask;
                            dto.key = !string.IsNullOrEmpty(ln.phoneExt) ? ln.phoneExt
                                : (!string.IsNullOrEmpty(ln.loginId) ? ln.loginId : SafeDash(ln.mainNumber));
                            dto.name = string.IsNullOrEmpty(ln.subTask) ? (c.project ?? "") : (c.project + " · " + ln.subTask);
                            dto.customerInferred = custName;
                            dto.folderPath = string.Format("Dial-Plan > {0} > {1}", c.location ?? "-", c.project ?? "-");
                            dto.relationSummary = string.Format("Ent:{0} | PG:{1} | Ext:{2} | Login:{3}",
                                SafeDash(ln.entityNo), SafeDash(ln.agentPg), SafeDash(ln.phoneExt), SafeDash(ln.loginId));
                            dto.attributes = new Dictionary<string, string>();
                            dto.attributes["센터"] = c.project ?? "";
                            dto.attributes["세부업무"] = ln.subTask ?? "";
                            dto.attributes["대표번호"] = ln.mainNumber ?? "";
                            dto.attributes["입중계(DID)"] = ln.incomingNumber ?? "";
                            dto.attributes["인입 RSI"] = ln.inboundRsi ?? "";
                            dto.attributes["전환 RSI"] = ln.agentTransferRsi ?? "";
                            dto.attributes["Entity NO"] = ln.entityNo ?? "";
                            dto.attributes["Agent PG"] = ln.agentPg ?? "";
                            dto.attributes["Phone 내선"] = ln.phoneExt ?? "";
                            dto.attributes["Login-ID"] = ln.loginId ?? "";
                            dto.attributes["테스트 IVR"] = ln.testIvrDn ?? "";
                            dto.attributes["비고"] = ln.note ?? "";
                            results.Add(dto);
                        }
                        if (results.Count >= 600) break;
                    }
                }
                if (results.Count >= 600) break;
            }

            // 2. Entity / IVR / PRI / DVG
            if ((catSel == "전체" || catSel == "Entity/IVR/PRI/DVG") && results.Count < 800)
            {
                foreach (EntityMasterRow ent in db.entities)
                {
                    if (ent == null) continue;
                    if (hasCustFilter && !ContainsIgnoreCase(ent.entityName, custLower)) continue;
                    if (string.IsNullOrEmpty(qLower)
                        || MatchesTextOrNumbers(ent.entityNo, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(ent.entityName, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(ent.discNum, qLower, numTokens, exact))
                    {
                        LookupItemDto dto = new LookupItemDto();
                        dto.id = "DP_ENT:" + ent.entityNo;
                        dto.source = "선번장";
                        dto.category = "Entity";
                        dto.subType = "EntityMaster";
                        dto.key = ent.entityNo ?? "";
                        dto.name = ent.entityName ?? "";
                        dto.customerInferred = ent.entityName ?? "";
                        dto.folderPath = "선번장 DB > Entity";
                        dto.relationSummary = "Disc:" + SafeDash(ent.discNum);
                        dto.attributes = new Dictionary<string, string>
                        {
                            { "Entity NO", ent.entityNo ?? "" },
                            { "Entity Name", ent.entityName ?? "" },
                            { "Disc Num", ent.discNum ?? "" },
                            { "비고", ent.note ?? "" }
                        };
                        results.Add(dto);
                    }
                }

                foreach (IpIvrChannelRow ivr in db.ipIvrChannels)
                {
                    if (ivr == null) continue;
                    if (hasCustFilter && !ContainsIgnoreCase(ivr.centerName, custLower)) continue;
                    if (string.IsNullOrEmpty(qLower)
                        || MatchesTextOrNumbers(ivr.ivrDn, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(ivr.centerName, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(ivr.deviceName, qLower, numTokens, exact))
                    {
                        LookupItemDto dto = new LookupItemDto();
                        dto.id = "DP_IVR:" + ivr.ivrDn + ":" + ivr.deviceName;
                        dto.source = "선번장";
                        dto.category = "IP IVR";
                        dto.subType = ivr.deviceName ?? "";
                        dto.key = ivr.ivrDn ?? "";
                        dto.name = ivr.centerName ?? "";
                        dto.customerInferred = ivr.centerName ?? "";
                        dto.folderPath = "선번장 DB > IP IVR";
                        dto.relationSummary = string.Format("장치:{0} | 채널:{1}", SafeDash(ivr.deviceName), SafeDash(ivr.channelCount));
                        dto.attributes = new Dictionary<string, string>
                        {
                            { "IVR DN", ivr.ivrDn ?? "" },
                            { "센터명", ivr.centerName ?? "" },
                            { "장비명", ivr.deviceName ?? "" },
                            { "채널 수", ivr.channelCount ?? "" },
                            { "비고", ivr.note ?? "" }
                        };
                        results.Add(dto);
                    }
                }

                foreach (PriTrunkRow pri in db.priTrunks)
                {
                    if (pri == null) continue;
                    if (hasCustFilter && !ContainsIgnoreCase(pri.centerName, custLower)) continue;
                    if (string.IsNullOrEmpty(qLower)
                        || MatchesTextOrNumbers(pri.centerName, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(pri.didRange, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(pri.subscriberNo, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(pri.trunkGroupName, qLower, numTokens, exact))
                    {
                        LookupItemDto dto = new LookupItemDto();
                        dto.id = "DP_PRI:" + pri.tgNew + ":" + pri.didRange;
                        dto.source = "선번장";
                        dto.category = "PRI";
                        dto.subType = pri.carrier ?? "";
                        dto.key = !string.IsNullOrEmpty(pri.subscriberNo) ? pri.subscriberNo : (pri.didRange ?? "");
                        dto.name = pri.centerName ?? "";
                        dto.customerInferred = pri.centerName ?? "";
                        dto.folderPath = "선번장 DB > PRI 현황";
                        dto.relationSummary = string.Format("TG:{0} | DID:{1}", SafeDash(pri.tgNew), SafeDash(pri.didRange));
                        dto.attributes = new Dictionary<string, string>
                        {
                            { "센터명", pri.centerName ?? "" },
                            { "회선사", pri.carrier ?? "" },
                            { "DID 대역", pri.didRange ?? "" },
                            { "가입자번호", pri.subscriberNo ?? "" },
                            { "TG 번호", pri.tgNew ?? "" },
                            { "비고", pri.note ?? "" }
                        };
                        results.Add(dto);
                    }
                }

                foreach (DvgPromptRow dvg in db.dvgPrompts)
                {
                    if (dvg == null) continue;
                    if (hasCustFilter && !ContainsIgnoreCase(dvg.content, custLower) && !ContainsIgnoreCase(dvg.category, custLower)) continue;
                    if (string.IsNullOrEmpty(qLower)
                        || MatchesTextOrNumbers(dvg.dvgNo, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(dvg.content, qLower, numTokens, exact)
                        || MatchesTextOrNumbers(dvg.category, qLower, numTokens, exact))
                    {
                        LookupItemDto dto = new LookupItemDto();
                        dto.id = "DP_DVG:" + dvg.dvgNo;
                        dto.source = "선번장";
                        dto.category = "D.V.G";
                        dto.subType = dvg.band ?? "";
                        dto.key = dvg.dvgNo ?? "";
                        dto.name = dvg.content ?? "";
                        dto.customerInferred = dvg.category ?? "";
                        dto.folderPath = "선번장 DB > D.V.G 멘트";
                        dto.relationSummary = string.Format("{0} · {1}초", SafeDash(dvg.category), SafeDash(dvg.durationSec));
                        dto.attributes = new Dictionary<string, string>
                        {
                            { "DVG 번호", dvg.dvgNo ?? "" },
                            { "대역", dvg.band ?? "" },
                            { "대분류", dvg.category ?? "" },
                            { "내용", dvg.content ?? "" },
                            { "길이(초)", dvg.durationSec ?? "" },
                            { "비고", dvg.note ?? "" }
                        };
                        results.Add(dto);
                    }
                }
            }

            return results;
        }

        private static bool MatchesTextOrNumbers(string fieldVal, string qLower, HashSet<string> numTokens, bool exact)
        {
            if (string.IsNullOrEmpty(fieldVal)) return false;
            if (numTokens != null && numTokens.Count > 0)
            {
                NumberParseResult fieldParsed = OxeScriptService.ParseNumbersStrict(fieldVal);
                if (fieldParsed != null && fieldParsed.Items.Count > 0)
                {
                    for (int i = 0; i < fieldParsed.Items.Count; i++)
                    {
                        if (numTokens.Contains(fieldParsed.Items[i])) return true;
                    }
                }
            }
            if (exact) return string.Equals(fieldVal.Trim(), qLower, StringComparison.OrdinalIgnoreCase);
            return fieldVal.ToLowerInvariant().Contains(qLower);
        }

        private static bool ContainsIgnoreCase(string text, string subLower)
        {
            return !string.IsNullOrEmpty(text) && text.ToLowerInvariant().Contains(subLower);
        }

        private static bool IsDialPlanOnlyCategory(string cat)
        {
            return cat == "선번장 센터" || cat == "선번장 라인" || cat == "Entity/IVR/PRI/DVG";
        }

        private static bool IsCmOnlyCategory(string cat)
        {
            return cat == "Agent DN" || cat == "Place" || cat == "Agent Login" || cat == "ACD Position"
                || cat == "Person" || cat == "RSI" || cat == "VQueue"
                || cat == "Place Group" || cat == "Agent Group" || cat == "DN Group" || cat == "Script";
        }

        private static string MapToBackendCategory(string catSel)
        {
            if (string.IsNullOrEmpty(catSel) || catSel == "전체") return "ALL";
            return catSel;
        }

        private void RenderLookupChips(Dictionary<string, int> counts, int total)
        {
            LookupChipsPanel.Children.Clear();
            LookupChipsPanel.Children.Add(MakeSummaryChip("총 " + total + "건", "PrimaryBrush", "ChipBgBrush"));
            foreach (KeyValuePair<string, int> kv in counts)
            {
                LookupChipsPanel.Children.Add(MakeSummaryChip(string.Format("{0} {1}", kv.Key, kv.Value), "TextBrush", "SurfaceAltBrush"));
            }
        }

        private static Border MakeSummaryChip(string text, string fgRes, string bgRes)
        {
            Border bd = new Border();
            bd.CornerRadius = new CornerRadius(10);
            bd.Padding = new Thickness(9, 3, 9, 3);
            bd.Margin = new Thickness(0, 0, 6, 4);
            bd.BorderThickness = new Thickness(1);
            bd.SetResourceReference(Border.BackgroundProperty, bgRes);
            bd.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            TextBlock tb = new TextBlock();
            tb.Text = text;
            tb.FontSize = 11.5;
            tb.FontWeight = FontWeights.SemiBold;
            tb.SetResourceReference(TextBlock.ForegroundProperty, fgRes);
            bd.Child = tb;
            return bd;
        }

        private void DgLookupResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LookupItemDto sel = DgLookupResults.SelectedItem as LookupItemDto;
            if (sel == null)
            {
                ClearDetailInspector();
                return;
            }
            RenderDetailInspector(sel);
        }

        private void ClearDetailInspector()
        {
            BdDetailBadge.Visibility = Visibility.Collapsed;
            BdDetailAnomaly.Visibility = Visibility.Collapsed;
            TxtDetailTitle.Text = "좌측 표에서 항목을 선택하면 연관 관계와 선번장 매칭 정보가 표시됩니다.";
            TxtDetailPath.Text = "";
            DetailRelationsPanel.Children.Clear();
            DetailRelationsPanel.Children.Add(new TextBlock { Text = "(선택된 항목 없음)", FontSize = 11.5 });
            TxtDetailDialPlanMatch.Text = "(항목 선택 시 자동 교차 조회)";
            DgDetailAttrs.ItemsSource = null;
            BtnDetailCopy.IsEnabled = false;
            BtnDetailToExtCti.IsEnabled = false;
            BtnDetailToPerson.IsEnabled = false;
        }

        private void RenderDetailInspector(LookupItemDto item)
        {
            BdDetailBadge.Visibility = Visibility.Visible;
            TxtDetailCategoryBadge.Text = string.Format("{0} · {1}", item.SourceDisplay, item.category);
            TxtDetailTitle.Text = string.Format("{0}  —  {1}", item.key ?? "-", item.name ?? "-");
            TxtDetailPath.Text = string.IsNullOrEmpty(item.folderPath) ? "-" : item.folderPath;

            if (item.HasAnomaly)
            {
                BdDetailAnomaly.Visibility = Visibility.Visible;
                TxtDetailAnomaly.Text = "진단 경고: " + item.AnomalySummary;
            }
            else
            {
                BdDetailAnomaly.Visibility = Visibility.Collapsed;
            }

            DetailRelationsPanel.Children.Clear();
            if (item.relations != null && item.relations.Count > 0)
            {
                for (int i = 0; i < item.relations.Count; i++)
                {
                    LookupRelationDto r = item.relations[i];
                    StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 2) };
                    Border tag = new Border
                    {
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(5, 1, 5, 1),
                        Margin = new Thickness(0, 0, 6, 0)
                    };
                    tag.SetResourceReference(Border.BackgroundProperty, "ChipBgBrush");
                    TextBlock tagTxt = new TextBlock { Text = r.relType, FontSize = 10.5, FontWeight = FontWeights.Bold };
                    tagTxt.SetResourceReference(TextBlock.ForegroundProperty, "ChipFgBrush");
                    tag.Child = tagTxt;
                    sp.Children.Add(tag);

                    TextBlock valTxt = new TextBlock
                    {
                        Text = string.Format("{0} {1}", r.targetKey ?? "", string.IsNullOrEmpty(r.targetName) ? "" : ("(" + r.targetName + ")")),
                        FontSize = 11.5,
                        TextWrapping = TextWrapping.Wrap
                    };
                    valTxt.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                    sp.Children.Add(valTxt);
                    DetailRelationsPanel.Children.Add(sp);
                }
            }
            else
            {
                TextBlock none = new TextBlock { Text = "(연결된 Config 하위/상위 객체 없음)", FontSize = 11.5 };
                none.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                DetailRelationsPanel.Children.Add(none);
            }

            TxtDetailDialPlanMatch.Text = BuildDialPlanCrossMatchText(item);

            List<AttrRowItem> attrs = new List<AttrRowItem>();
            if (item.attributes != null)
            {
                foreach (KeyValuePair<string, string> kv in item.attributes)
                {
                    attrs.Add(new AttrRowItem { Key = kv.Key, Value = kv.Value ?? "" });
                }
            }
            DgDetailAttrs.ItemsSource = attrs;

            BtnDetailCopy.IsEnabled = true;
            BtnDetailToExtCti.IsEnabled = (!string.IsNullOrEmpty(item.customerInferred) || !string.IsNullOrEmpty(item.key));
            BtnDetailToPerson.IsEnabled = !string.IsNullOrEmpty(item.customerInferred);
        }

        private string BuildDialPlanCrossMatchText(LookupItemDto item)
        {
            StringBuilder sb = new StringBuilder();
            string custName = item.customerInferred ?? "";

            if (_getCustomers != null && !string.IsNullOrEmpty(custName))
            {
                List<CustomerInfo> custs = _getCustomers();
                if (custs != null)
                {
                    for (int i = 0; i < custs.Count; i++)
                    {
                        if (string.Equals(custs[i].name, custName, StringComparison.OrdinalIgnoreCase))
                        {
                            CustomerInfo ci = custs[i];
                            sb.AppendFormat("고객사 DB: {0} [{1}] | Presets: {2}개",
                                ci.name, ci.floor ?? "-", ci.oxePresets != null ? ci.oxePresets.Count : 0);
                            sb.AppendLine();
                            break;
                        }
                    }
                }
            }

            string dpPath = _getDialPlanDbPath != null ? _getDialPlanDbPath() : null;
            if (!string.IsNullOrEmpty(dpPath) && File.Exists(dpPath))
            {
                try
                {
                    DialPlanDatabase db = DialPlanRepository.Load(dpPath);
                    string numTarget = ExtractPureDigits(item.key);
                    int matchedCenters = 0;
                    foreach (DialPlanCenter c in db.centers)
                    {
                        bool custMatch = !string.IsNullOrEmpty(custName)
                            && (string.Equals(c.cmCustomerName, custName, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(c.project, custName, StringComparison.OrdinalIgnoreCase));
                        bool numMatch = false;
                        if (!string.IsNullOrEmpty(numTarget) && c.lines != null)
                        {
                            foreach (DialPlanLine ln in c.lines)
                            {
                                if (NumberSpecParserContains(ln.phoneExt, numTarget) || NumberSpecParserContains(ln.loginId, numTarget))
                                {
                                    numMatch = true;
                                    break;
                                }
                            }
                        }
                        if (custMatch || numMatch)
                        {
                            matchedCenters++;
                            sb.AppendFormat("선번장[{0}]: {1} ({2}) | Ent:{3} | PG:{4} | Ext:{5} | Login:{6}",
                                DialPlanStatus.ToDisplay(c.status),
                                c.project,
                                c.location ?? "-",
                                SafeDash(c.EntitySummary),
                                SafeDash(c.PgSummary),
                                SafeDash(c.ExtRangeSummary),
                                SafeDash(c.LoginRangeSummary));
                            sb.AppendLine();
                            if (matchedCenters >= 3) break;
                        }
                    }
                }
                catch
                {
                }
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "연결된 선번장 센터/대역 매칭 없음";
        }

        private static bool NumberSpecParserContains(string spec, string singleNum)
        {
            if (string.IsNullOrWhiteSpace(spec) || string.IsNullOrWhiteSpace(singleNum)) return false;
            NumberParseResult res = OxeScriptService.ParseNumbersStrict(spec);
            return res != null && res.Items != null && res.Items.Contains(singleNum);
        }

        private static string ExtractPureDigits(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            if (raw.StartsWith("P", StringComparison.OrdinalIgnoreCase) && raw.Length > 1) raw = raw.Substring(1);
            for (int i = 0; i < raw.Length; i++)
            {
                if (!char.IsDigit(raw[i])) return "";
            }
            return raw;
        }

        private void BtnDetailCopy_Click(object sender, RoutedEventArgs e)
        {
            LookupItemDto item = DgLookupResults.SelectedItem as LookupItemDto;
            if (item == null) return;
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("[{0}] {1} | Key={2} | Name={3}", item.SourceDisplay, item.category, item.key, item.name).AppendLine();
            sb.AppendFormat("Customer={0} | Path={1}", item.customerInferred ?? "", item.folderPath ?? "").AppendLine();
            sb.AppendFormat("Relations={0}", item.relationSummary ?? "").AppendLine();
            if (item.attributes != null)
            {
                foreach (KeyValuePair<string, string> kv in item.attributes)
                {
                    sb.AppendFormat("  {0}: {1}", kv.Key, kv.Value).AppendLine();
                }
            }
            try
            {
                Clipboard.SetText(sb.ToString());
                TxtLookupStatus.Text = "선택 항목 상세 정보를 클립보드에 복사했습니다.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), "클립보드 복사 실패: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnDetailToExtCti_Click(object sender, RoutedEventArgs e)
        {
            LookupItemDto item = DgLookupResults.SelectedItem as LookupItemDto;
            if (item == null || _navigateToExtCti == null) return;

            string dnSpec = "";
            string ctiSpec = "";
            string digits = ExtractPureDigits(item.key);
            if (!string.IsNullOrEmpty(digits))
            {
                if (item.category == "Agent DN" || item.category == "Place") dnSpec = digits;
                else if (item.category == "Agent Login" || item.category == "ACD Position") ctiSpec = digits;
            }
            if (item.category == "Person" && item.attributes != null && item.attributes.ContainsKey("AgentLogins"))
            {
                ctiSpec = item.attributes["AgentLogins"];
            }
            if (item.category == "선번장 라인" && item.attributes != null)
            {
                if (item.attributes.ContainsKey("Phone 내선")) dnSpec = item.attributes["Phone 내선"];
                if (item.attributes.ContainsKey("Login-ID")) ctiSpec = item.attributes["Login-ID"];
            }
            _navigateToExtCti(item.customerInferred ?? "", dnSpec, ctiSpec);
        }

        private void BtnDetailToPerson_Click(object sender, RoutedEventArgs e)
        {
            LookupItemDto item = DgLookupResults.SelectedItem as LookupItemDto;
            if (item == null || _navigateToPerson == null || string.IsNullOrEmpty(item.customerInferred)) return;
            _navigateToPerson(item.customerInferred);
        }

        // ==================== CENTER INTEGRITY AUDIT ====================
        private void BtnAuditRun_Click(object sender, RoutedEventArgs e)
        {
            string cust = (CmbAuditCustomer.SelectedValue as string) ?? "ALL";
            RunCenterAuditAsync(cust);
        }

        private async void RunCenterAuditAsync(string customerName)
        {
            if (_isBusy || _cmWorker == null) return;
            SetBusy(true, "Config 센터 정합성 감사 실행 중...");
            try
            {
                CenterAuditResponseDto resp = await _cmWorker.CenterAuditAsync(customerName, true);
                _lastAuditResp = resp;
                BindAuditViews();
                if (string.Equals(customerName, "ALL", StringComparison.OrdinalIgnoreCase))
                {
                    _activeAuditSub = "summary";
                }
                else
                {
                    _activeAuditSub = "pairs";
                }
                ApplyAuditSubVisuals();

                if (_logCallback != null && resp != null && resp.logs != null)
                {
                    for (int i = 0; i < resp.logs.Count; i++) _logCallback("INFO", resp.logs[i]);
                }
            }
            catch (Exception ex)
            {
                if (_logCallback != null) _logCallback("ERR", "[정합성 감사 실패] " + ex.Message);
                MessageBox.Show(Window.GetWindow(this), "센터 정합성 감사 실패:\n" + ex.Message, "감사 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void ChkAuditIssuesOnly_Changed(object sender, RoutedEventArgs e)
        {
            BindAuditViews();
        }

        private void DgAuditSummary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            CenterAuditSummaryRowDto row = DgAuditSummary.SelectedItem as CenterAuditSummaryRowDto;
            if (row == null || string.IsNullOrEmpty(row.customer)) return;
            _isUiInit = true;
            CmbAuditCustomer.SelectedValue = row.customer;
            _isUiInit = false;
            RunCenterAuditAsync(row.customer);
        }

        private void BindAuditViews()
        {
            if (_lastAuditResp == null) return;
            bool issuesOnly = (ChkAuditIssuesOnly.IsChecked == true);

            List<CenterAuditSummaryRowDto> sums = _lastAuditResp.centerSummaries ?? new List<CenterAuditSummaryRowDto>();
            if (issuesOnly)
            {
                sums = sums.FindAll(delegate(CenterAuditSummaryRowDto r) { return r.HasIssues; });
            }
            DgAuditSummary.ItemsSource = sums;

            List<CenterPairRowDto> pairs = _lastAuditResp.pairRows ?? new List<CenterPairRowDto>();
            if (issuesOnly)
            {
                pairs = pairs.FindAll(delegate(CenterPairRowDto r) { return r.IsIssue; });
            }
            DgAuditPairs.ItemsSource = pairs;

            DgAuditIssues.ItemsSource = _lastAuditResp.issues ?? new List<CenterAuditIssueDto>();

            AuditKpiPanel.Children.Clear();
            if (_lastAuditResp.stats != null)
            {
                int centers = GetStat("centers");
                int totalPairs = GetStat("totalPairs");
                int okPairs = GetStat("okPairs");
                int errors = GetStat("errors");
                int warns = GetStat("warns");
                AuditKpiPanel.Children.Add(MakeSummaryChip("감사 센터 " + centers + "개", "ChipFgBrush", "ChipBgBrush"));
                AuditKpiPanel.Children.Add(MakeSummaryChip(string.Format("페어 정상 {0}/{1}", okPairs, totalPairs), "OkFgBrush", "OkBgBrush"));
                AuditKpiPanel.Children.Add(MakeSummaryChip("폴더/구조 오류 " + errors + "건", errors > 0 ? "ErrFgBrush" : "OkFgBrush", errors > 0 ? "ErrBgBrush" : "OkBgBrush"));
                AuditKpiPanel.Children.Add(MakeSummaryChip("경고 " + warns + "건", warns > 0 ? "WarnFgBrush" : "OkFgBrush", warns > 0 ? "WarnBgBrush" : "OkBgBrush"));
                AuditKpiPanel.Children.Add(MakeSummaryChip(string.Format("소요 {0:0.00}s", _lastAuditResp.elapsedSeconds), "MutedBrush", "SurfaceAltBrush"));
            }
        }

        private int GetStat(string k)
        {
            int v;
            if (_lastAuditResp != null && _lastAuditResp.stats != null && _lastAuditResp.stats.TryGetValue(k, out v))
                return v;
            return 0;
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "CSV Files (*.csv)|*.csv";
            sfd.FileName = (_activeMode == "search")
                ? string.Format("extbatch_lookup_{0:yyyyMMdd_HHmmss}.csv", DateTime.Now)
                : string.Format("extbatch_audit_{0:yyyyMMdd_HHmmss}.csv", DateTime.Now);
            if (sfd.ShowDialog(Window.GetWindow(this)) != true) return;

            StringBuilder csv = new StringBuilder();
            if (_activeMode == "search")
            {
                csv.AppendLine("출처,유형,키/번호,이름,센터,연관관계,진단,경로");
                foreach (LookupItemDto it in _lastSearchItems)
                {
                    csv.AppendLine(string.Join(",", new string[]
                    {
                        CsvEsc(it.SourceDisplay), CsvEsc(it.category), CsvEsc(it.key), CsvEsc(it.name),
                        CsvEsc(it.customerInferred), CsvEsc(it.relationSummary), CsvEsc(it.AnomalySummary), CsvEsc(it.folderPath)
                    }));
                }
            }
            else if (_activeAuditSub == "summary" && _lastAuditResp != null && _lastAuditResp.centerSummaries != null)
            {
                csv.AppendLine("센터명,위치,AgentDN,Place,AgentLogin,ACDPos,Person,RSI,폴더상태,이슈수,비고");
                foreach (CenterAuditSummaryRowDto r in _lastAuditResp.centerSummaries)
                {
                    csv.AppendLine(string.Join(",", new string[]
                    {
                        CsvEsc(r.customer), CsvEsc(r.floor), r.agentDnCount.ToString(), r.placeCount.ToString(),
                        r.agentLoginCount.ToString(), r.acdPosCount.ToString(), r.personCount.ToString(), r.rsiCount.ToString(),
                        CsvEsc(r.FolderHealthText), r.issueCount.ToString(), CsvEsc(r.note)
                    }));
                }
            }
            else if (_activeAuditSub == "pairs" && _lastAuditResp != null && _lastAuditResp.pairRows != null)
            {
                csv.AppendLine("구분,번호,1차존재,대응객체,2차존재,연결정보,상태,진단,1차경로,2차경로");
                foreach (CenterPairRowDto r in _lastAuditResp.pairRows)
                {
                    csv.AppendLine(string.Join(",", new string[]
                    {
                        CsvEsc(r.kind), CsvEsc(r.number), CsvEsc(r.PrimaryMark), CsvEsc(r.secondaryKey),
                        CsvEsc(r.SecondaryMark), CsvEsc(r.linkedInfo), CsvEsc(r.status), CsvEsc(r.note),
                        CsvEsc(r.primaryPath), CsvEsc(r.secondaryPath)
                    }));
                }
            }
            else if (_lastAuditResp != null && _lastAuditResp.issues != null)
            {
                csv.AppendLine("심각도,센터,이슈코드,대상,상세내용,경로");
                foreach (CenterAuditIssueDto r in _lastAuditResp.issues)
                {
                    csv.AppendLine(string.Join(",", new string[]
                    {
                        CsvEsc(r.severity), CsvEsc(r.customer), CsvEsc(r.code), CsvEsc(r.target), CsvEsc(r.detail), CsvEsc(r.folderPath)
                    }));
                }
            }

            File.WriteAllText(sfd.FileName, csv.ToString(), new UTF8Encoding(true));
            TxtLookupStatus.Text = "CSV 저장 완료: " + sfd.FileName;
        }

        private void SetBusy(bool busy, string msg)
        {
            _isBusy = busy;
            BtnLookupSearch.IsEnabled = !busy;
            BtnLookupForceRefresh.IsEnabled = !busy;
            BtnAuditRun.IsEnabled = !busy;
            if (!string.IsNullOrEmpty(msg))
            {
                TxtLookupStatus.Text = msg;
            }
        }

        private static string CsvEsc(string v)
        {
            if (v == null) return "\"\"";
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        private static string SafeDash(string v)
        {
            return string.IsNullOrWhiteSpace(v) ? "-" : v;
        }

        private static void AddChipCount(Dictionary<string, int> counts, string key, int delta)
        {
            if (counts == null || string.IsNullOrEmpty(key)) return;
            int prev;
            counts.TryGetValue(key, out prev);
            counts[key] = prev + delta;
        }
    }
}
