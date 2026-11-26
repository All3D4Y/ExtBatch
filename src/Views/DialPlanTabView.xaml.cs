using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CmOxeProvisioner.Models;
using CmOxeProvisioner.Services;
using Microsoft.Win32;

namespace CmOxeProvisioner.Views
{
    public class CenterFullDeleteOutcome
    {
        public bool Success { get; set; }
        public bool CmExecuted { get; set; }
        public int CmDeletedCount { get; set; }
        public int CmFailedCount { get; set; }
        public string Message { get; set; }
    }

    public partial class DialPlanTabView : UserControl
    {
        private string _dbPath = string.Empty;
        private string _cmCustomersPath = string.Empty;
        private DialPlanDatabase _db = new DialPlanDatabase();

        private Func<List<CustomerInfo>> _getCustomers;
        private Func<CenterCreateInput, Task<CmCenterCheckResponse>> _precheckCenterHandler;
        private Func<CenterCreateInput, Task<CenterSaveOutcome>> _createCenterHandler;
        private Action<string, string> _saveLogHandler;
        private Action<CenterAddTabView> _onCenterAddPopupStateChanged;
        private Func<DialPlanCenter, Task<CenterFullDeleteOutcome>> _fullDeleteCenterHandler;
        private Action<DialPlanCenter> _navigateToExtCtiHandler;
        private Action<string> _writeAuditLogHandler;
        private Action _onDatabaseOrCustomerChanged;

        private string _activeSubTab = "dialplan";
        private string _statusFilter = DialPlanStatus.Active;
        private string _dvgBandFilter = "all";
        private string _selectedLocation = "전체 위치";
        private bool _suppressSelectionEvents = false;
        private bool _mergeCellsEnabled = true;
        private DialPlanCenter _selectedCenter = null;
        private DialPlanCenter _expandedCenter = null;
        private DataGrid _expandedLinesGrid = null;
        private DataTable _mdfDataTable = null;
        private ListCollectionView _centersView = null;
        private string _activeSearchQuery = string.Empty;
        private string _lastMdfQuery = null;
        private int _lastMdfSourceCount = -1;
        private DispatcherTimer _searchDebounceTimer = null;
        private string _apUrlEditOrigin = string.Empty;
        private bool _isCancellingApEdit = false;

        private static readonly IValueConverter HideMergedTextConv = new InverseBoolToVisConverter();
        private static readonly IValueConverter MergeBorderConv = new MergeBottomBorderConverter();

        public DialPlanTabView()
        {
            InitializeComponent();
            ApplyCenteredTextColumns();
            _searchDebounceTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(110);
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
            PreviewKeyDown += DialPlanTabView_PreviewKeyDown;
        }

        private void SearchDebounceTimer_Tick(object sender, EventArgs e)
        {
            if (_searchDebounceTimer != null)
                _searchDebounceTimer.Stop();
            ApplyFiltersAndBind();
        }

        private void ApplyCenteredTextColumns()
        {
            var textStyle = TryFindResource("DialPlanCenteredTextStyle") as Style;
            var numStyle = TryFindResource("DialPlanNumericTextStyle") as Style;
            var editStyle = TryFindResource("DialPlanCenteredEditTextStyle") as Style;
            DataGrid[] grids = new[]
            {
                GridCenters, GridPri, GridEntities, GridIpIvr, GridDvg, GridPbxHw, GridGpa
            };
            foreach (var grid in grids)
            {
                if (grid == null) continue;
                foreach (var col in grid.Columns)
                {
                    var tc = col as DataGridTextColumn;
                    if (tc == null) continue;
                    string hdr = Convert.ToString(tc.Header) ?? string.Empty;
                    bool isNumericCol =
                        hdr == "Logical" || hdr == "TG신규" || hdr == "가입자번호" || hdr == "중계번호" || hdr == "개통일" ||
                        hdr == "Entity NO" || hdr == "Disc.Num" || hdr == "신규 IVR DN" || hdr == "채널 수" ||
                        hdr == "D.V.G" || hdr == "시간(Sec)" || hdr == "IP 정보" || hdr == "MAC 정보" || hdr == "실장보드";
                    ApplyCenteredStyleToTextColumn(tc, tc.ElementStyle ?? (isNumericCol && numStyle != null ? numStyle : textStyle), editStyle);
                }
            }
        }

        private static void ApplyCenteredStyleToTextColumn(DataGridTextColumn textCol, Style textStyle, Style editStyle)
        {
            if (textCol == null) return;
            if (textCol.ElementStyle == null && textStyle != null) textCol.ElementStyle = textStyle;
            if (textCol.EditingElementStyle == null && editStyle != null) textCol.EditingElementStyle = editStyle;
        }

        private void GridMdf_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            var textCol = e.Column as DataGridTextColumn;
            if (textCol == null) return;
            var textStyle = TryFindResource("DialPlanCenteredTextStyle") as Style;
            var editStyle = TryFindResource("DialPlanCenteredEditTextStyle") as Style;
            ApplyCenteredStyleToTextColumn(textCol, textStyle, editStyle);
        }

        public void Initialize(
            string dialPlanDbPath,
            string cmCustomersPath,
            Func<List<CustomerInfo>> getCustomers,
            Func<CenterCreateInput, Task<CmCenterCheckResponse>> precheckHandler,
            Func<CenterCreateInput, Task<CenterSaveOutcome>> createHandler,
            Action<string, string> saveLogHandler,
            Action<CenterAddTabView> onCenterAddPopupStateChanged,
            Func<DialPlanCenter, Task<CenterFullDeleteOutcome>> fullDeleteHandler,
            Action<DialPlanCenter> navigateToExtCtiHandler,
            Action<string> writeAuditLogHandler,
            Action onDatabaseOrCustomerChanged)
        {
            _dbPath = dialPlanDbPath ?? string.Empty;
            _cmCustomersPath = cmCustomersPath ?? string.Empty;
            _getCustomers = getCustomers;
            _precheckCenterHandler = precheckHandler;
            _createCenterHandler = createHandler;
            _saveLogHandler = saveLogHandler;
            _onCenterAddPopupStateChanged = onCenterAddPopupStateChanged;
            _fullDeleteCenterHandler = fullDeleteHandler;
            _navigateToExtCtiHandler = navigateToExtCtiHandler;
            _writeAuditLogHandler = writeAuditLogHandler;
            _onDatabaseOrCustomerChanged = onDatabaseOrCustomerChanged;

            ReloadFromDisk(null);
        }

        private void WriteAudit(string action, string message)
        {
            if (_writeAuditLogHandler != null)
                _writeAuditLogHandler("[" + action + "] " + message);
        }

        public void ReloadFromDisk(string selectCenterNameOrId)
        {
            _expandedCenter = null;
            _expandedLinesGrid = null;
            if (GridCenters != null)
                GridCenters.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Collapsed;
            _lastMdfQuery = null;
            _lastMdfSourceCount = -1;
            _db = DialPlanRepository.Load(_dbPath);
            if (_db != null && _db.centers != null)
            {
                for (int i = 0; i < _db.centers.Count; i++)
                {
                    if (_db.centers[i] != null)
                        _db.centers[i].EnsureSearchIndexReady();
                }
            }
            BindCentersCollectionView();
            PopulateLocationFilter();
            UpdateStatusCounts();
            ApplyFiltersAndBind();

            if (!string.IsNullOrWhiteSpace(selectCenterNameOrId))
            {
                var found = _db.centers.FirstOrDefault(c =>
                    string.Equals(c.id, selectCenterNameOrId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.project, selectCenterNameOrId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.cmCustomerName, selectCenterNameOrId, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    if (_statusFilter != "all" && !string.Equals(found.status, _statusFilter, StringComparison.OrdinalIgnoreCase))
                        SetStatusFilter(found.status);

                    GridCenters.SelectedItem = found;
                    GridCenters.ScrollIntoView(found);
                }
            }
        }

        private void PopulateLocationFilter()
        {
            _suppressSelectionEvents = true;
            try
            {
                string prev = CmbLocationFilter.SelectedItem as string;
                CmbLocationFilter.Items.Clear();
                CmbLocationFilter.Items.Add("전체 위치");

                var locs = _db.centers
                    .Select(c => (c.location ?? string.Empty).Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                foreach (var loc in locs)
                    CmbLocationFilter.Items.Add(loc);

                if (!string.IsNullOrWhiteSpace(prev) && CmbLocationFilter.Items.Contains(prev))
                {
                    CmbLocationFilter.SelectedItem = prev;
                    _selectedLocation = prev;
                }
                else
                {
                    CmbLocationFilter.SelectedIndex = 0;
                    _selectedLocation = "전체 위치";
                }
            }
            finally
            {
                _suppressSelectionEvents = false;
            }
        }

        private void UpdateStatusCounts()
        {
            int active = _db.centers.Count(c => c.status == DialPlanStatus.Active);
            int test = _db.centers.Count(c => c.status == DialPlanStatus.Test);
            int closed = _db.centers.Count(c => c.status == DialPlanStatus.Closed);

            BtnStatusActive.Content = "운영 중 (" + active + ")";
            BtnStatusTest.Content = "테스트용 (" + test + ")";
            BtnStatusClosed.Content = "업무 종료 (" + closed + ")";
            BtnStatusAll.Content = "전체 (" + _db.centers.Count + ")";
        }

        private void BtnSubTab_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            string tab = btn.Tag as string ?? "dialplan";
            SetActiveSubTab(tab);
        }

        private void SetActiveSubTab(string tab)
        {
            _activeSubTab = tab;

            ApplySegmentedStyle(BtnSubDialPlan, tab == "dialplan");
            ApplySegmentedStyle(BtnSubPriEntity, tab == "pri");
            ApplySegmentedStyle(BtnSubIvrDvg, tab == "ivr");
            ApplySegmentedStyle(BtnSubHardware, tab == "hw");

            PanelDialPlan.Visibility = tab == "dialplan" ? Visibility.Visible : Visibility.Collapsed;
            PanelPriEntity.Visibility = tab == "pri" ? Visibility.Visible : Visibility.Collapsed;
            PanelIvrDvg.Visibility = tab == "ivr" ? Visibility.Visible : Visibility.Collapsed;
            PanelHardware.Visibility = tab == "hw" ? Visibility.Visible : Visibility.Collapsed;

            PanelStatusFilter.Visibility = tab == "dialplan" ? Visibility.Visible : Visibility.Collapsed;
            PanelLocationFilter.Visibility = tab == "dialplan" ? Visibility.Visible : Visibility.Collapsed;
            PanelDvgBandFilter.Visibility = tab == "ivr" ? Visibility.Visible : Visibility.Collapsed;

            ApplyFiltersAndBind();
        }

        private void BtnStatusFilter_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            SetStatusFilter(btn.Tag as string ?? DialPlanStatus.Active);
        }

        private void SetStatusFilter(string status)
        {
            _statusFilter = status;
            ApplySegmentedStyle(BtnStatusActive, status == DialPlanStatus.Active);
            ApplySegmentedStyle(BtnStatusTest, status == DialPlanStatus.Test);
            ApplySegmentedStyle(BtnStatusClosed, status == DialPlanStatus.Closed);
            ApplySegmentedStyle(BtnStatusAll, status == "all");
            ApplyFiltersAndBind();
        }

        private void BtnDvgBand_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            _dvgBandFilter = btn.Tag as string ?? "all";
            ApplySegmentedStyle(BtnDvgAll, _dvgBandFilter == "all");
            ApplySegmentedStyle(BtnDvg700, _dvgBandFilter == "700");
            ApplySegmentedStyle(BtnDvg800, _dvgBandFilter == "800");
            ApplySegmentedStyle(BtnDvg900, _dvgBandFilter == "900");
            ApplySegmentedStyle(BtnDvg1800, _dvgBandFilter == "1800");
            ApplyFiltersAndBind();
        }

        private void ApplySegmentedStyle(Button btn, bool active)
        {
            if (btn == null) return;
            var styleKey = active ? "PrimaryBtn" : "BaseBtn";
            var st = TryFindResource(styleKey) as Style;
            if (st != null) btn.Style = st;
        }

        private void CmbLocationFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents) return;
            _selectedLocation = CmbLocationFilter.SelectedItem as string ?? "전체 위치";
            ApplyFiltersAndBind();
        }

        private void BindCentersCollectionView()
        {
            if (_db == null) return;
            _db.EnsureLists();
            _centersView = new ListCollectionView(_db.centers);
            _centersView.Filter = FilterCenterObject;
            GridCenters.ItemsSource = _centersView;
        }

        private bool FilterCenterObject(object obj)
        {
            var c = obj as DialPlanCenter;
            return c != null && MatchesCurrentDialPlanFilter(c, _activeSearchQuery);
        }

        private bool MatchesCurrentDialPlanFilter(DialPlanCenter c, string q)
        {
            if (c == null) return false;
            if (_statusFilter != "all" && !string.Equals(c.status, _statusFilter, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrWhiteSpace(_selectedLocation) && _selectedLocation != "전체 위치")
            {
                if (!string.Equals((c.location ?? string.Empty).Trim(), _selectedLocation, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            if (!string.IsNullOrWhiteSpace(q))
                return c.MatchesSearchQuery(q);
            return true;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_searchDebounceTimer != null && TxtSearch != null &&
                TxtSearch.IsKeyboardFocusWithin && !string.IsNullOrWhiteSpace(TxtSearch.Text))
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            }
            else
            {
                if (_searchDebounceTimer != null) _searchDebounceTimer.Stop();
                ApplyFiltersAndBind();
            }
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (_searchDebounceTimer != null) _searchDebounceTimer.Stop();
                ApplyFiltersAndBind();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && TxtSearch != null && !string.IsNullOrEmpty(TxtSearch.Text))
            {
                if (_searchDebounceTimer != null) _searchDebounceTimer.Stop();
                TxtSearch.Text = string.Empty;
                e.Handled = true;
            }
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            if (_searchDebounceTimer != null) _searchDebounceTimer.Stop();
            TxtSearch.Text = string.Empty;
            if (CmbLocationFilter.Items.Count > 0)
            {
                _suppressSelectionEvents = true;
                try
                {
                    CmbLocationFilter.SelectedIndex = 0;
                    _selectedLocation = "전체 위치";
                }
                finally
                {
                    _suppressSelectionEvents = false;
                }
            }
            ApplyFiltersAndBind();
        }

        private void ApplyFiltersAndBind()
        {
            if (_searchDebounceTimer != null)
                _searchDebounceTimer.Stop();
            if (_db == null) return;
            string q = (TxtSearch != null && TxtSearch.Text != null) ? TxtSearch.Text.Trim() : string.Empty;
            _activeSearchQuery = q;

            if (_activeSubTab == "dialplan")
            {
                if (_centersView == null || !ReferenceEquals(_centersView.SourceCollection, _db.centers))
                {
                    BindCentersCollectionView();
                }

                string prevSelId = _selectedCenter != null ? _selectedCenter.id : null;
                _centersView.Refresh();

                int filteredCount = 0;
                int totalLines = 0;
                bool expandedVisible = false;
                DialPlanCenter selMatch = null;
                for (int i = 0; i < _db.centers.Count; i++)
                {
                    var c = _db.centers[i];
                    if (!MatchesCurrentDialPlanFilter(c, q)) continue;
                    filteredCount++;
                    totalLines += c.LineCount;
                    if (c == _expandedCenter) expandedVisible = true;
                    if (selMatch == null && !string.IsNullOrEmpty(prevSelId) && string.Equals(c.id, prevSelId, StringComparison.Ordinal))
                        selMatch = c;
                }

                if (_expandedCenter != null && !expandedVisible)
                {
                    _expandedCenter = null;
                    _expandedLinesGrid = null;
                    if (GridCenters != null)
                        GridCenters.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Collapsed;
                }

                RefreshExpandFocusVisuals();
                TxtResultSummary.Text = "센터 " + filteredCount + "개 · 선번 " + totalLines + "행";

                if (selMatch != null && !ReferenceEquals(GridCenters.SelectedItem, selMatch))
                {
                    GridCenters.SelectedItem = selMatch;
                }
            }
            else if (_activeSubTab == "pri")
            {
                bool hasQ = !string.IsNullOrWhiteSpace(q);
                var pris = hasQ
                    ? _db.priTrunks.Where(p => ContainsCI(p.boardSlot, q) || ContainsCI(p.tgNew, q) || ContainsCI(p.centerName, q) ||
                        ContainsCI(p.trunkGroupName, q) || ContainsCI(p.didRange, q) || ContainsCI(p.carrier, q) ||
                        ContainsCI(p.subscriberNo, q) || ContainsCI(p.relayNo, q) || ContainsCI(p.note, q)).ToList()
                    : _db.priTrunks;
                var ents = hasQ
                    ? _db.entities.Where(e => ContainsCI(e.entityNo, q) || ContainsCI(e.entityName, q) || ContainsCI(e.discNum, q) || ContainsCI(e.note, q)).ToList()
                    : _db.entities;
                GridPri.ItemsSource = pris;
                GridEntities.ItemsSource = ents;
                TxtResultSummary.Text = "PRI " + pris.Count + "건 · Entity " + ents.Count + "건";
            }
            else if (_activeSubTab == "ivr")
            {
                bool hasQ = !string.IsNullOrWhiteSpace(q);
                var ivrs = hasQ
                    ? _db.ipIvrChannels.Where(x => ContainsCI(x.deviceName, q) || ContainsCI(x.ivrDn, q) || ContainsCI(x.centerName, q) || ContainsCI(x.note, q)).ToList()
                    : _db.ipIvrChannels;
                var dvgs = (_dvgBandFilter == "all" && !hasQ)
                    ? _db.dvgPrompts
                    : _db.dvgPrompts.Where(d =>
                    {
                        if (_dvgBandFilter != "all" && (d.band == null || d.band.IndexOf(_dvgBandFilter, StringComparison.OrdinalIgnoreCase) < 0))
                            return false;
                        if (!hasQ) return true;
                        return ContainsCI(d.band, q) || ContainsCI(d.dvgNo, q) || ContainsCI(d.category, q) ||
                               ContainsCI(d.subCategory, q) || ContainsCI(d.content, q) || ContainsCI(d.board, q) || ContainsCI(d.note, q);
                    }).ToList();
                GridIpIvr.ItemsSource = ivrs;
                GridDvg.ItemsSource = dvgs;
                TxtResultSummary.Text = "IP IVR " + ivrs.Count + "건 · D.V.G 멘트 " + dvgs.Count + "건";
            }
            else if (_activeSubTab == "hw")
            {
                bool hasQ = !string.IsNullOrWhiteSpace(q);
                var hws = hasQ
                    ? _db.pbxHardware.Where(h => ContainsCI(h.pbxSet, q) || ContainsCI(h.boardSlot, q) || ContainsCI(h.boardName, q) ||
                        ContainsCI(h.ipAddress, q) || ContainsCI(h.macAddress, q) || ContainsCI(h.note, q)).ToList()
                    : _db.pbxHardware;
                var gpas = hasQ
                    ? _db.gpaBoards.Where(g => ContainsCI(g.boardLocation, q) || ContainsCI(g.remainingTime, q) || ContainsCI(g.note, q)).ToList()
                    : _db.gpaBoards;
                GridPbxHw.ItemsSource = hws;
                GridGpa.ItemsSource = gpas;
                BindMdfGrid(q);
                TxtResultSummary.Text = "PBX 보드 " + hws.Count + "건 · GPA " + gpas.Count + "건";
            }
        }

        private void BindMdfGrid(string query)
        {
            string q = query ?? string.Empty;
            int totalRows = _db.mdfBlocks != null ? _db.mdfBlocks.Count : 0;
            if (GridMdf.ItemsSource != null && string.Equals(_lastMdfQuery, q, StringComparison.Ordinal) && _lastMdfSourceCount == totalRows)
                return;

            bool hasQ = !string.IsNullOrWhiteSpace(q);
            var rows = hasQ
                ? _db.mdfBlocks.Where(m => ContainsCI(m.section, q) || ContainsCI(m.CellsSummary, q)).ToList()
                : _db.mdfBlocks;

            int maxCols = rows.Count > 0 ? Math.Max(1, rows.Max(m => m.cells != null ? m.cells.Count : 0)) : 25;
            if (maxCols > 26) maxCols = 26;

            var dt = new DataTable();
            dt.Columns.Add("구분", typeof(string));
            dt.Columns.Add("행", typeof(int));
            for (int c = 0; c < maxCols; c++)
                dt.Columns.Add("C" + c, typeof(string));

            foreach (var m in rows)
            {
                var dr = dt.NewRow();
                dr[0] = m.section ?? string.Empty;
                dr[1] = m.rowIndex;
                for (int c = 0; c < maxCols; c++)
                    dr[c + 2] = (m.cells != null && c < m.cells.Count) ? (m.cells[c] ?? string.Empty) : string.Empty;
                dt.Rows.Add(dr);
            }

            _mdfDataTable = dt;
            GridMdf.ItemsSource = dt.DefaultView;
            _lastMdfQuery = q;
            _lastMdfSourceCount = totalRows;
        }

        private void GridCenters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedCenter = GridCenters.SelectedItem as DialPlanCenter;
        }

        private void GridCenters_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            DependencyObject origin = e.OriginalSource as DependencyObject;
            if (IsInsideDetailsOrInteractiveControl(origin))
                return;

            var sel = GridCenters.SelectedItem as DialPlanCenter;
            if (sel == null) return;

            ToggleExpandedCenter(sel);
        }

        private void DialPlanTabView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _expandedCenter != null && _activeSubTab == "dialplan")
            {
                if (IsInsideDetailsEditingTextBox(Keyboard.FocusedElement as DependencyObject))
                    return;

                SetExpandedCenter(null);
                e.Handled = true;
            }
        }

        private void InlineLinesGrid_Initialized(object sender, EventArgs e)
        {
            EnsureMergeableLineColumns(sender as DataGrid);
        }

        private void InlineLinesGrid_Loaded(object sender, RoutedEventArgs e)
        {
            var grid = sender as DataGrid;
            if (grid == null) return;

            EnsureMergeableLineColumns(grid);
            var center = grid.DataContext as DialPlanCenter;
            if (center != null)
            {
                center.RefreshLineMergeVisuals(_mergeCellsEnabled);
            }
            if (center == _expandedCenter)
            {
                _expandedLinesGrid = grid;
            }
        }

        private void InlineLinesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            var grid = sender as DataGrid;
            var center = (grid != null ? grid.DataContext as DialPlanCenter : null) ?? _expandedCenter;
            if (center == null) return;

            Dispatcher.BeginInvoke(new Action(delegate()
            {
                center.RefreshLineMergeVisuals(_mergeCellsEnabled);
                center.NotifySummaryFields();
            }), DispatcherPriority.Background);
        }

        private void ChkInlineMergeCells_Loaded(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            if (cb != null) cb.IsChecked = _mergeCellsEnabled;
        }

        private void ChkInlineMergeCells_Changed(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            if (cb == null) return;
            _mergeCellsEnabled = (cb.IsChecked == true);
            if (_expandedCenter != null)
            {
                _expandedCenter.RefreshLineMergeVisuals(_mergeCellsEnabled);
            }
        }

        private static readonly Brush GroupInboundBrush = CreateFrozenBrush("#3B82F6");
        private static readonly Brush GroupAgentBrush = CreateFrozenBrush("#0D9488");
        private static readonly Brush GroupOxeBrush = CreateFrozenBrush("#6366F1");
        private static readonly Brush GroupLegacyBrush = CreateFrozenBrush("#94A3B8");
        private static readonly FontFamily InlineMonoFont = new FontFamily("Consolas, Malgun Gothic, Segoe UI");

        private static Brush CreateFrozenBrush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        private Style BuildInlineGroupHeaderStyle(Style baseStyle, Brush accentBrush, string groupName, string headerTitle)
        {
            var st = baseStyle != null ? new Style(typeof(DataGridColumnHeader), baseStyle) : new Style(typeof(DataGridColumnHeader));
            st.Setters.Add(new Setter(FrameworkElement.TagProperty, accentBrush));
            st.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, "[" + groupName + "] " + headerTitle));
            return st;
        }

        private void EnsureMergeableLineColumns(DataGrid grid)
        {
            if (grid == null || grid.Columns.Count > 0) return;

            var baseGroupHdrStyle = TryFindResource("InlineGroupedHeaderStyle") as Style;
            var groupStyles = new[]
            {
                Tuple.Create("인입 · IVR", GroupInboundBrush),
                Tuple.Create("상담 · Agent", GroupAgentBrush),
                Tuple.Create("OXE · 기타", GroupOxeBrush),
                Tuple.Create("구버전 레거시", GroupLegacyBrush)
            };

            // GroupId: 0=Inbound/IVR, 1=Agent, 2=OXE/Other, 3=Legacy
            // Bold / Numeric flags for typography hierarchy
            var specs = new[]
            {
                Tuple.Create("(세부업무)", "subTask", 132.0, 0, true, false),
                Tuple.Create("대표번호", "mainNumber", 118.0, 0, false, true),
                Tuple.Create("통신사", "carrier", 74.0, 0, false, false),
                Tuple.Create("명의", "ownerName", 90.0, 0, false, false),
                Tuple.Create("착신번호", "incomingNumber", 118.0, 0, false, true),
                Tuple.Create("인입 RSI", "inboundRsi", 78.0, 0, false, true),
                Tuple.Create("IVR (PDS) 채널", "ivrPdsChannel", 125.0, 0, false, true),
                Tuple.Create("IVR-Fw Pilot", "ivrFwPilot", 92.0, 0, false, true),
                Tuple.Create("상담연결 RSI", "agentTransferRsi", 108.0, 1, false, true),
                Tuple.Create("Agent Pilot", "agentPilot", 88.0, 1, false, true),
                Tuple.Create("Agent Queue", "agentQueue", 92.0, 1, false, true),
                Tuple.Create("Agent PG", "agentPg", 84.0, 1, false, true),
                Tuple.Create("Phone 내선", "phoneExt", 126.0, 1, false, true),
                Tuple.Create("Login-ID", "loginId", 126.0, 1, false, true),
                Tuple.Create("Call Type", "callType", 78.0, 2, false, false),
                Tuple.Create("Entity NO", "entityNo", 122.0, 2, false, true),
                Tuple.Create("Trunk 그룹 NO", "trunkGroupNo", 118.0, 2, false, true),
                Tuple.Create("테스트 IVR DN", "testIvrDn", 108.0, 2, false, true),
                Tuple.Create("비고", "note", 180.0, 2, false, false),
                Tuple.Create("구 IVR RSI", "legacyIvrRsi", 86.0, 3, false, true),
                Tuple.Create("구 IVR Pilot", "legacyIvrPilot", 96.0, 3, false, true),
                Tuple.Create("구 RSI 특징", "legacyRsiNote", 105.0, 3, false, false)
            };

            foreach (var sp in specs)
            {
                var grp = groupStyles[sp.Item4];
                var col = BuildMergeableLineColumn(sp.Item1, sp.Item2, sp.Item3, sp.Item5, sp.Item6);
                col.HeaderStyle = BuildInlineGroupHeaderStyle(baseGroupHdrStyle, grp.Item2, grp.Item1, sp.Item1);
                grid.Columns.Add(col);
            }
        }

        private static DataGridTemplateColumn BuildMergeableLineColumn(string header, string propName, double width, bool isBold, bool isNumeric)
        {
            var col = new DataGridTemplateColumn
            {
                Header = header,
                Width = new DataGridLength(width)
            };

            var displayTemplate = new DataTemplate(typeof(DialPlanLine));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(7, 5, 7, 5));
            borderFactory.SetBinding(FrameworkElement.ToolTipProperty, new Binding(propName));
            borderFactory.SetBinding(Border.BorderThicknessProperty,
                new Binding("Merge[" + propName + "].SameAsBelow") { Converter = MergeBorderConv });
            borderFactory.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(delegate(object s, RoutedEventArgs ev)
            {
                var b = s as Border;
                if (b != null) b.SetResourceReference(Border.BorderBrushProperty, "TableGridLineBrush");
            }));

            var textFactory = new FrameworkElementFactory(typeof(TextBlock));
            textFactory.SetBinding(TextBlock.TextProperty, new Binding(propName));
            textFactory.SetBinding(UIElement.VisibilityProperty,
                new Binding("Merge[" + propName + "].SameAsAbove") { Converter = HideMergedTextConv });
            textFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            textFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            textFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            textFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            if (isBold)
                textFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            if (isNumeric)
            {
                textFactory.SetValue(TextBlock.FontFamilyProperty, InlineMonoFont);
                textFactory.SetValue(TextBlock.FontSizeProperty, 11.5);
            }

            borderFactory.AppendChild(textFactory);
            displayTemplate.VisualTree = borderFactory;
            col.CellTemplate = displayTemplate;

            var editTemplate = new DataTemplate(typeof(DialPlanLine));
            var tbFactory = new FrameworkElementFactory(typeof(TextBox));
            tbFactory.SetBinding(TextBox.TextProperty,
                new Binding(propName) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            tbFactory.SetValue(Control.PaddingProperty, new Thickness(5, 3, 5, 3));
            tbFactory.SetValue(Control.BorderThicknessProperty, new Thickness(1));
            tbFactory.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            tbFactory.SetValue(TextBox.TextAlignmentProperty, TextAlignment.Center);
            tbFactory.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(delegate(object s, RoutedEventArgs ev)
            {
                var tb = s as TextBox;
                if (tb == null) return;
                tb.SetResourceReference(Control.BackgroundProperty, "InputBgBrush");
                tb.SetResourceReference(Control.ForegroundProperty, "TextBrush");
                tb.SetResourceReference(Control.BorderBrushProperty, "PrimaryBrush");
                tb.Focus();
                tb.SelectAll();
            }));
            editTemplate.VisualTree = tbFactory;
            col.CellEditingTemplate = editTemplate;

            return col;
        }

        private void BtnApLink_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var btn = sender as Button;
            if (btn == null) return;
            var center = (btn.Tag as DialPlanCenter) ?? (btn.DataContext as DialPlanCenter);
            if (center == null) return;

            if (center.HasApUrl)
            {
                OpenApUrlForCenter(center);
            }
            else
            {
                BeginInlineApEdit(btn, center);
            }
        }

        private static Button ResolveApButtonFromMenuItem(object sender)
        {
            var mi = sender as MenuItem;
            if (mi == null) return null;
            var cm = mi.Parent as ContextMenu;
            if (cm != null && cm.PlacementTarget is Button)
                return cm.PlacementTarget as Button;
            return mi.Tag as Button;
        }

        private void MenuApOpen_Click(object sender, RoutedEventArgs e)
        {
            var btn = ResolveApButtonFromMenuItem(sender);
            var center = btn != null ? ((btn.Tag as DialPlanCenter) ?? (btn.DataContext as DialPlanCenter)) : null;
            OpenApUrlForCenter(center);
        }

        private void MenuApEdit_Click(object sender, RoutedEventArgs e)
        {
            var btn = ResolveApButtonFromMenuItem(sender);
            var center = btn != null ? ((btn.Tag as DialPlanCenter) ?? (btn.DataContext as DialPlanCenter)) : null;
            if (btn != null && center != null)
            {
                BeginInlineApEdit(btn, center);
            }
        }

        private void MenuApClear_Click(object sender, RoutedEventArgs e)
        {
            var btn = ResolveApButtonFromMenuItem(sender);
            var center = btn != null ? ((btn.Tag as DialPlanCenter) ?? (btn.DataContext as DialPlanCenter)) : null;
            if (center == null) return;

            center.apUrl = string.Empty;
            center.NotifySummaryFields();
            DialPlanRepository.Save(_dbPath, _db);
            WriteAudit("AP URL 초기화", "Center=" + center.project);
            Toast(true, "AP 초기화", "'" + center.project + "' AP가 미사용으로 변경되었습니다.");
        }

        private void BeginInlineApEdit(Button btn, DialPlanCenter center)
        {
            var root = btn != null ? btn.Parent as Grid : null;
            if (root == null || center == null) return;

            var tb = FindNamedDescendant<TextBox>(root, "TxtApInline");
            if (tb == null)
            {
                tb = new TextBox
                {
                    Name = "TxtApInline",
                    Visibility = Visibility.Collapsed,
                    BorderThickness = new Thickness(1),
                    FontSize = 11.5,
                    Height = 24,
                    Padding = new Thickness(4, 1, 4, 1),
                    TextAlignment = TextAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                tb.SetResourceReference(Control.BackgroundProperty, "InputBgBrush");
                tb.SetResourceReference(Control.ForegroundProperty, "TextBrush");
                tb.SetResourceReference(Control.BorderBrushProperty, "PrimaryBrush");
                tb.LostFocus += TxtApInline_LostFocus;
                tb.KeyDown += TxtApInline_KeyDown;
                root.Children.Add(tb);
            }

            tb.Tag = center;
            _isCancellingApEdit = false;
            _apUrlEditOrigin = (center.apUrl ?? string.Empty).Trim();
            tb.Text = _apUrlEditOrigin;
            btn.Visibility = Visibility.Collapsed;
            tb.Visibility = Visibility.Visible;

            Dispatcher.BeginInvoke(new Action(delegate()
            {
                tb.Focus();
                tb.SelectAll();
            }), DispatcherPriority.Input);
        }

        private void TxtApInline_LostFocus(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null || tb.Visibility != Visibility.Visible) return;
            FinishInlineApEdit(tb, save: !_isCancellingApEdit, showToast: false);
            _isCancellingApEdit = false;
        }

        private void TxtApInline_KeyDown(object sender, KeyEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;

            if (e.Key == Key.Enter)
            {
                _isCancellingApEdit = false;
                FinishInlineApEdit(tb, save: true, showToast: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _isCancellingApEdit = true;
                FinishInlineApEdit(tb, save: false, showToast: false);
                e.Handled = true;
            }
        }

        private void FinishInlineApEdit(TextBox tb, bool save, bool showToast)
        {
            if (tb == null) return;
            var root = tb.Parent as Grid;
            var btn = root != null ? FindNamedDescendant<Button>(root, "BtnApLink") : null;
            var center = (tb.Tag as DialPlanCenter) ?? (tb.DataContext as DialPlanCenter);

            if (save && center != null)
            {
                string trimmed = (tb.Text ?? string.Empty).Trim();
                center.apUrl = trimmed;
                center.NotifySummaryFields();

                if (!string.Equals(_apUrlEditOrigin ?? string.Empty, trimmed, StringComparison.Ordinal))
                {
                    _apUrlEditOrigin = trimmed;
                    DialPlanRepository.Save(_dbPath, _db);
                    WriteAudit("AP URL 수정", "Center=" + center.project + " Url=" + trimmed);
                    if (showToast)
                    {
                        Toast(true, "AP 설정 저장", center.HasApUrl
                            ? ("'" + center.project + "' AP가 '바로가기'로 설정되었습니다.")
                            : ("'" + center.project + "' AP가 '미사용'으로 설정되었습니다."));
                    }
                }
            }

            tb.Visibility = Visibility.Collapsed;
            if (btn != null) btn.Visibility = Visibility.Visible;
        }

        private void BtnOpenApUrl_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            DialPlanCenter center = ExtractCenterFromSender(sender) ?? _expandedCenter;
            OpenApUrlForCenter(center);
        }

        private void OpenApUrlForCenter(DialPlanCenter center)
        {
            if (center == null) return;

            string raw = (center.apUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                Toast(false, "AP 주소 없음", "먼저 AP 웹주소를 입력하세요.");
                return;
            }

            if (raw.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                raw = "http://" + raw;
            }

            Uri uri;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Toast(false, "URL 형식 오류", "유효한 http:// 또는 https:// 웹주소를 입력하세요: " + raw);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("브라우저를 열지 못했습니다:\n" + ex.Message, "AP 연결 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnInlineToggle_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            DialPlanCenter center = ExtractCenterFromSender(sender);
            if (center == null) return;
            ToggleExpandedCenter(center);
        }

        private void ToggleExpandedCenter(DialPlanCenter center)
        {
            if (center == null) return;
            if (_expandedCenter == center)
            {
                SetExpandedCenter(null);
            }
            else
            {
                SetExpandedCenter(center);
            }
        }

        private void SetExpandedCenter(DialPlanCenter center)
        {
            if (center != null)
            {
                center.EnsureLists();
                center.RefreshLineMergeVisuals(_mergeCellsEnabled);
            }

            _expandedCenter = center;
            if (center == null)
            {
                _expandedLinesGrid = null;
            }

            if (GridCenters != null)
            {
                GridCenters.RowDetailsVisibilityMode = center != null
                    ? DataGridRowDetailsVisibilityMode.Visible
                    : DataGridRowDetailsVisibilityMode.Collapsed;
            }

            RefreshExpandFocusVisuals();

            if (center != null)
            {
                _selectedCenter = center;
                GridCenters.SelectedItem = center;
                Dispatcher.BeginInvoke(new Action(delegate()
                {
                    if (_expandedCenter == center)
                        GridCenters.ScrollIntoView(center);
                }), DispatcherPriority.Loaded);
            }
        }

        private void RefreshExpandFocusVisuals()
        {
            if (_db == null || _db.centers == null) return;
            foreach (var c in _db.centers)
            {
                if (c != null)
                    c.SetExpanded(c == _expandedCenter);
            }
        }

        private void BtnInlineAddLine_Click(object sender, RoutedEventArgs e)
        {
            DialPlanCenter center = ExtractCenterFromSender(sender) ?? _expandedCenter;
            if (center == null) return;
            center.EnsureLists();

            var line = new DialPlanLine { subTask = center.project };
            center.lines.Add(line);
            center.RefreshLineMergeVisuals(_mergeCellsEnabled);
            center.NotifySummaryFields();

            DataGrid linesGrid = FindLinesGridForSender(sender);
            if (linesGrid != null)
            {
                linesGrid.ItemsSource = null;
                linesGrid.ItemsSource = center.lines;
                linesGrid.SelectedItem = line;
                linesGrid.ScrollIntoView(line);
            }
        }

        private void BtnInlineRemoveLine_Click(object sender, RoutedEventArgs e)
        {
            DialPlanCenter center = ExtractCenterFromSender(sender) ?? _expandedCenter;
            if (center == null) return;

            DataGrid linesGrid = FindLinesGridForSender(sender);
            var line = linesGrid != null ? linesGrid.SelectedItem as DialPlanLine : null;
            if (line == null)
            {
                Toast(false, "선택 필요", "삭제할 선번 라인을 먼저 선택하세요.");
                return;
            }

            center.lines.Remove(line);
            center.RefreshLineMergeVisuals(_mergeCellsEnabled);
            center.NotifySummaryFields();
            if (linesGrid != null)
            {
                linesGrid.ItemsSource = null;
                linesGrid.ItemsSource = center.lines;
            }
        }

        private void BtnInlineSaveCenter_Click(object sender, RoutedEventArgs e)
        {
            DialPlanCenter center = ExtractCenterFromSender(sender) ?? _expandedCenter;
            if (center == null) return;

            DataGrid linesGrid = FindLinesGridForSender(sender);
            CommitGridEdits(linesGrid);

            string newProject = (center.project ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newProject))
            {
                Toast(false, "입력 확인", "센터명은 비워둘 수 없습니다.");
                return;
            }

            center.location = (center.location ?? string.Empty).Trim();
            center.project = newProject;
            center.cmCustomerName = (center.cmCustomerName ?? string.Empty).Trim();
            center.apUrl = (center.apUrl ?? string.Empty).Trim();
            center.notes = (center.notes ?? string.Empty).Trim();
            center.EnsureLists();
            center.RefreshLineMergeVisuals(_mergeCellsEnabled);
            center.NotifySummaryFields();

            SaveDbAndSyncCustomers(center);
            WriteAudit("선번장 저장", "Center=" + center.project + " Status=" + center.status + " Lines=" + center.LineCount);
            Toast(true, "저장 완료", "'" + center.project + "' 변경사항 저장 및 CM 프리셋 동기화 완료");
        }

        private void BtnInlineToggleStatus_Click(object sender, RoutedEventArgs e)
        {
            DialPlanCenter center = ExtractCenterFromSender(sender) ?? _expandedCenter;
            if (center == null) return;

            CommitGridEdits(FindLinesGridForSender(sender));

            bool movingToClosed = center.status != DialPlanStatus.Closed;
            string nextStatus = movingToClosed ? DialPlanStatus.Closed : DialPlanStatus.Active;
            string verb = movingToClosed ? "업무 종료(종료센터 보관)" : "운영 중 복구";

            if (!PopupDialogs.ConfirmAction(
                Window.GetWindow(this),
                verb + " 확인",
                "'" + center.project + "' → " + DialPlanStatus.ToDisplay(nextStatus),
                "이 작업은 선번 데이터와 CM 폴더를 삭제하지 않으며 언제든 상태를 되돌릴 수 있습니다.",
                verb))
            {
                return;
            }

            center.status = nextStatus;
            center.closedAt = movingToClosed ? DateTime.Now.ToString("yyyy-MM-dd") : string.Empty;
            center.NotifySummaryFields();

            SaveDbAndSyncCustomers(null);
            WriteAudit("선번장 상태변경", "Center=" + center.project + " -> " + nextStatus);
            Toast(true, "상태 변경 완료", "'" + center.project + "' 상태가 '" + DialPlanStatus.ToDisplay(nextStatus) + "'(으)로 변경되었습니다.");
        }

        private async void BtnInlineFullDelete_Click(object sender, RoutedEventArgs e)
        {
            DialPlanCenter target = ExtractCenterFromSender(sender) ?? _expandedCenter;
            if (target == null) return;

            var btn = sender as Button;
            string cmTargetDesc = target.HasCmLink
                ? ("CM 고객사 연동 대상: '" + target.cmCustomerName + "' (위치 폴더 '" + target.LocationDisplay + "'는 보존되고 센터 폴더·Place Group·Script만 삭제)")
                : "CM 연동 정보 없음 (선번장 JSON DB에서만 영구 삭제)";

            if (!PopupDialogs.ConfirmDelete(
                Window.GetWindow(this),
                "완전 삭제 확인 (JSON + CM)",
                "'" + target.project + "' (" + target.LocationDisplay + ") 영구 삭제",
                "단순 종료 처리는 '업무 종료로 이동' 버튼을 권장합니다.",
                "선번장 데이터(dialplan_db.json) 및 연결된 CM 센터 구조가 영구 삭제됩니다.\n\n" +
                "• 대상 센터: " + target.project + " (" + target.LocationDisplay + ")\n" +
                "• 선번 라인: " + target.LineCount + "행\n" +
                "• " + cmTargetDesc))
            {
                return;
            }

            CenterFullDeleteOutcome outcome = null;
            if (_fullDeleteCenterHandler != null)
            {
                if (btn != null) btn.IsEnabled = false;
                try
                {
                    outcome = await _fullDeleteCenterHandler(target);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("CM 완전 삭제 실행 중 오류: " + ex.Message, "삭제 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    if (btn != null) btn.IsEnabled = true;
                    return;
                }
                finally
                {
                    if (btn != null) btn.IsEnabled = true;
                }

                if (outcome != null && !outcome.Success)
                {
                    MessageBox.Show(outcome.Message ?? "CM 삭제에 실패해 JSON 삭제를 중단했습니다.", "삭제 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            _db.centers.Remove(target);
            DialPlanRepository.Save(_dbPath, _db);

            if (!string.IsNullOrWhiteSpace(target.cmCustomerName) && _getCustomers != null)
            {
                var customers = _getCustomers();
                var cmObj = customers.FirstOrDefault(c =>
                    c != null && string.Equals(c.name, target.cmCustomerName, StringComparison.OrdinalIgnoreCase));
                if (cmObj != null)
                {
                    customers.Remove(cmObj);
                    new CustomerRepository().SaveAll(_cmCustomersPath, customers);
                }
            }

            if (_expandedCenter == target) _expandedCenter = null;
            if (_selectedCenter == target) _selectedCenter = null;
            _expandedLinesGrid = null;
            if (GridCenters != null && _expandedCenter == null)
                GridCenters.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Collapsed;

            BindCentersCollectionView();
            PopulateLocationFilter();
            UpdateStatusCounts();
            ApplyFiltersAndBind();
            if (_onDatabaseOrCustomerChanged != null) _onDatabaseOrCustomerChanged();

            string extra = (outcome != null && outcome.CmExecuted)
                ? (" · CM 삭제 " + outcome.CmDeletedCount + "건 (실패 " + outcome.CmFailedCount + "건)")
                : string.Empty;
            WriteAudit("선번장 완전삭제", "Center=" + target.project + extra);
            Toast(true, "완전 삭제 완료", "'" + target.project + "' 완전 삭제 완료" + extra);
        }

        private void BtnInlineOpenExtCti_Click(object sender, RoutedEventArgs e)
        {
            DialPlanCenter center = ExtractCenterFromSender(sender) ?? _expandedCenter;
            if (center == null || _navigateToExtCtiHandler == null) return;
            _navigateToExtCtiHandler(center);
        }

        private static DialPlanCenter ExtractCenterFromSender(object sender)
        {
            var fe = sender as FrameworkElement;
            if (fe == null) return null;
            return (fe.Tag as DialPlanCenter) ?? (fe.DataContext as DialPlanCenter);
        }

        private DataGrid FindLinesGridForSender(object sender)
        {
            DependencyObject cur = sender as DependencyObject;
            while (cur != null)
            {
                var rootGrid = cur as Grid;
                if (rootGrid != null && rootGrid.Name == "InlineDetailRoot")
                {
                    var found = FindNamedDescendant<DataGrid>(rootGrid, "InlineLinesGrid");
                    if (found != null) return found;
                }
                cur = VisualTreeHelper.GetParent(cur);
            }
            return _expandedLinesGrid;
        }

        private static bool IsInsideDetailsOrInteractiveControl(DependencyObject origin)
        {
            DependencyObject cur = origin;
            while (cur != null)
            {
                if (cur is DataGridDetailsPresenter ||
                    cur is ButtonBase ||
                    cur is TextBox ||
                    cur is ComboBox)
                {
                    return true;
                }
                if (cur is DataGridRow)
                    return false;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return false;
        }

        private static bool IsInsideDetailsEditingTextBox(DependencyObject focused)
        {
            DependencyObject cur = focused;
            while (cur != null)
            {
                if (cur is TextBox)
                {
                    return true;
                }
                cur = VisualTreeHelper.GetParent(cur);
            }
            return false;
        }

        private static T FindNamedDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                var match = child as T;
                if (match != null && (string.IsNullOrEmpty(name) || match.Name == name))
                    return match;
                var deep = FindNamedDescendant<T>(child, name);
                if (deep != null) return deep;
            }
            return null;
        }

        private void BtnAddCenterPopup_Click(object sender, RoutedEventArgs e)
        {
            var win = new CenterAddWindow();
            win.Owner = Window.GetWindow(this);
            var customers = _getCustomers != null ? _getCustomers() : new List<CustomerInfo>();
            win.InitializeHandlers(customers, _precheckCenterHandler, _createCenterHandler, _saveLogHandler);
            if (_onCenterAddPopupStateChanged != null)
                _onCenterAddPopupStateChanged(win.InnerView);

            try
            {
                win.ShowDialog();
            }
            finally
            {
                if (_onCenterAddPopupStateChanged != null)
                    _onCenterAddPopupStateChanged(null);
            }

            if (win.HasCreatedCenter)
            {
                ReloadFromDisk(win.LastCreatedCenterName);
                if (_onDatabaseOrCustomerChanged != null)
                    _onDatabaseOrCustomerChanged();
            }
        }

        private void BtnFindEmpty_Click(object sender, RoutedEventArgs e)
        {
            var report = DialPlanRepository.FindEmptyNumbers(_db);
            var win = new EmptyNumberFinderWindow(report);
            win.Owner = Window.GetWindow(this);
            win.ShowDialog();
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "선번장 파일 가져오기 (.xlsx / .csv / .json)",
                Filter = "선번장 지원 파일 (*.xlsx;*.csv;*.json)|*.xlsx;*.csv;*.json|Excel 통합 문서 (*.xlsx)|*.xlsx|CSV 파일 (*.csv)|*.csv|JSON DB (*.json)|*.json"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                string ext = (Path.GetExtension(dlg.FileName) ?? string.Empty).ToLowerInvariant();
                var customers = _getCustomers != null ? _getCustomers() : new List<CustomerInfo>();

                if (ext == ".xlsx")
                {
                    string summary;
                    var imported = DialPlanWorkbookMapper.ImportFromXlsx(dlg.FileName, customers, out summary);
                    var ans = MessageBox.Show(
                        summary + "\n\n[예] 현재 선번 DB를 가져온 내용으로 전체 교체합니다.\n[아니오] 기존 DB에 없는 센터·레코드만 추가 병합합니다.\n[취소] 가져오기를 취소합니다.",
                        "Excel 가져오기 방식 선택",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);
                    if (ans == MessageBoxResult.Cancel) return;

                    if (ans == MessageBoxResult.Yes)
                    {
                        _db = imported;
                    }
                    else
                    {
                        MergeDatabases(_db, imported);
                    }

                    DialPlanRepository.Save(_dbPath, _db);
                    ReloadFromDisk(null);
                    if (_onDatabaseOrCustomerChanged != null) _onDatabaseOrCustomerChanged();
                    WriteAudit("선번장 XLSX 가져오기", Path.GetFileName(dlg.FileName) + " | " + summary);
                    Toast(true, "Excel 가져오기 완료", _db.centers.Count + "개 센터가 반영되었습니다.");
                }
                else if (ext == ".csv")
                {
                    var csvCenters = DialPlanWorkbookMapper.ImportDialPlanCsv(dlg.FileName);
                    if (csvCenters.Count == 0)
                    {
                        Toast(false, "가져오기 실패", "CSV에서 유효한 센터 행을 찾지 못했습니다.");
                        return;
                    }
                    int added = 0;
                    foreach (var c in csvCenters)
                    {
                        if (!_db.centers.Any(x => string.Equals(x.project, c.project, StringComparison.OrdinalIgnoreCase) && x.status == c.status))
                        {
                            c.id = DialPlanWorkbookMapper.BuildCenterId(c.project, _db.centers);
                            _db.centers.Add(c);
                            added++;
                        }
                    }
                    DialPlanRepository.Save(_dbPath, _db);
                    ReloadFromDisk(null);
                    WriteAudit("선번장 CSV 가져오기", Path.GetFileName(dlg.FileName) + " Added=" + added);
                    Toast(true, "CSV 가져오기 완료", added + "개 센터가 추가되었습니다.");
                }
                else if (ext == ".json")
                {
                    var loaded = DialPlanRepository.Load(dlg.FileName);
                    _db = loaded;
                    DialPlanRepository.Save(_dbPath, _db);
                    ReloadFromDisk(null);
                    WriteAudit("선번장 JSON 복원", Path.GetFileName(dlg.FileName) + " Centers=" + _db.centers.Count);
                    Toast(true, "JSON 복원 완료", _db.centers.Count + "개 센터가 복원되었습니다.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("가져오기 중 오류가 발생했습니다:\n" + ex.Message, "가져오기 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "선번장 내보내기 (.xlsx / .json / .csv)",
                FileName = "엘비유세스_선번장_" + DateTime.Now.ToString("yyyyMMdd"),
                Filter = "Excel 통합 문서 (*.xlsx)|*.xlsx|JSON 전체 백업 (*.json)|*.json|Dial-Plan CSV (*.csv)|*.csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                string ext = (Path.GetExtension(dlg.FileName) ?? string.Empty).ToLowerInvariant();
                if (ext == ".json")
                {
                    DialPlanRepository.Save(dlg.FileName, _db);
                    Toast(true, "JSON 내보내기 완료", Path.GetFileName(dlg.FileName) + " 저장 완료");
                }
                else if (ext == ".csv")
                {
                    var headers = new List<string>
                    {
                        "상태", "위치", "프로젝트(콜센터)", "(세부업무)", "대표번호", "통신사",
                        "대표번호 명의", "착신번호", "인입 RSI", "IVR (PDS) 채널", "IVR-Fw Pilot",
                        "상담연결 RSI", "Agent Pilot", "Agent Queue", "Agent PG", "Phone 내선",
                        "Login-ID", "Call Type", "Entity NO", "Trunk 그룹 NO", "테스트 IVR DN",
                        "CM연동", "VQueue", "IVR사용", "비고", "AP URL"
                    };
                    var rows = new List<IList<string>>();
                    foreach (var c in _db.centers)
                    {
                        var lines = c.lines.Count > 0 ? c.lines : new List<DialPlanLine> { new DialPlanLine() };
                        foreach (var l in lines)
                        {
                            rows.Add(new List<string>
                            {
                                c.StatusBadgeText, c.location ?? "", c.project ?? "", l.subTask ?? "", l.mainNumber ?? "",
                                l.carrier ?? "", l.ownerName ?? "", l.incomingNumber ?? "", l.inboundRsi ?? "",
                                l.ivrPdsChannel ?? "", l.ivrFwPilot ?? "", l.agentTransferRsi ?? "", l.agentPilot ?? "",
                                l.agentQueue ?? "", l.agentPg ?? "", l.phoneExt ?? "", l.loginId ?? "", l.callType ?? "",
                                l.entityNo ?? "", l.trunkGroupNo ?? "", l.testIvrDn ?? "", c.cmCustomerName ?? "",
                                c.useVQueue ? "Y" : "N", c.useIvr ? "Y" : "N", l.note ?? "", c.apUrl ?? ""
                            });
                        }
                    }
                    ExcelWorkbookCodec.WriteCsv(dlg.FileName, headers, rows);
                    Toast(true, "CSV 내보내기 완료", rows.Count + "행 저장 완료");
                }
                else
                {
                    var workbook = DialPlanWorkbookMapper.BuildExportWorkbook(_db);
                    ExcelWorkbookCodec.WriteXlsx(dlg.FileName, workbook);
                    Toast(true, "Excel 내보내기 완료", workbook.Count + "개 시트 저장 완료");
                }
                WriteAudit("선번장 내보내기", Path.GetFileName(dlg.FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show("내보내기 중 오류가 발생했습니다:\n" + ex.Message, "내보내기 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAddPri_Click(object sender, RoutedEventArgs e)
        {
            var row = new PriTrunkRow { status = "Alive" };
            _db.priTrunks.Add(row);
            ApplyFiltersAndBind();
            GridPri.SelectedItem = row;
            GridPri.ScrollIntoView(row);
        }

        private void BtnRemovePri_Click(object sender, RoutedEventArgs e)
        {
            var sel = GridPri.SelectedItem as PriTrunkRow;
            if (sel == null) return;
            _db.priTrunks.Remove(sel);
            ApplyFiltersAndBind();
        }

        private void BtnAddEntity_Click(object sender, RoutedEventArgs e)
        {
            var row = new EntityMasterRow();
            _db.entities.Add(row);
            ApplyFiltersAndBind();
            GridEntities.SelectedItem = row;
            GridEntities.ScrollIntoView(row);
        }

        private void BtnRemoveEntity_Click(object sender, RoutedEventArgs e)
        {
            var sel = GridEntities.SelectedItem as EntityMasterRow;
            if (sel == null) return;
            _db.entities.Remove(sel);
            ApplyFiltersAndBind();
        }

        private void BtnAddIpIvr_Click(object sender, RoutedEventArgs e)
        {
            var row = new IpIvrChannelRow { deviceName = "IP IVR 1" };
            _db.ipIvrChannels.Add(row);
            ApplyFiltersAndBind();
            GridIpIvr.SelectedItem = row;
            GridIpIvr.ScrollIntoView(row);
        }

        private void BtnRemoveIpIvr_Click(object sender, RoutedEventArgs e)
        {
            var sel = GridIpIvr.SelectedItem as IpIvrChannelRow;
            if (sel == null) return;
            _db.ipIvrChannels.Remove(sel);
            ApplyFiltersAndBind();
        }

        private void BtnAddDvg_Click(object sender, RoutedEventArgs e)
        {
            var row = new DvgPromptRow { band = _dvgBandFilter == "all" ? "700~799" : _dvgBandFilter };
            _db.dvgPrompts.Add(row);
            ApplyFiltersAndBind();
            GridDvg.SelectedItem = row;
            GridDvg.ScrollIntoView(row);
        }

        private void BtnRemoveDvg_Click(object sender, RoutedEventArgs e)
        {
            var sel = GridDvg.SelectedItem as DvgPromptRow;
            if (sel == null) return;
            _db.dvgPrompts.Remove(sel);
            ApplyFiltersAndBind();
        }

        private void BtnAddHw_Click(object sender, RoutedEventArgs e)
        {
            var row = new PbxHardwareRow { pbxSet = "MAIN" };
            _db.pbxHardware.Add(row);
            ApplyFiltersAndBind();
            GridPbxHw.SelectedItem = row;
            GridPbxHw.ScrollIntoView(row);
        }

        private void BtnRemoveHw_Click(object sender, RoutedEventArgs e)
        {
            var sel = GridPbxHw.SelectedItem as PbxHardwareRow;
            if (sel == null) return;
            _db.pbxHardware.Remove(sel);
            ApplyFiltersAndBind();
        }

        private void BtnAddGpa_Click(object sender, RoutedEventArgs e)
        {
            var row = new GpaBoardRow();
            _db.gpaBoards.Add(row);
            ApplyFiltersAndBind();
            GridGpa.SelectedItem = row;
            GridGpa.ScrollIntoView(row);
        }

        private void BtnRemoveGpa_Click(object sender, RoutedEventArgs e)
        {
            var sel = GridGpa.SelectedItem as GpaBoardRow;
            if (sel == null) return;
            _db.gpaBoards.Remove(sel);
            ApplyFiltersAndBind();
        }

        private void BtnSaveAllMaster_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdits(GridPri);
            CommitGridEdits(GridEntities);
            CommitGridEdits(GridIpIvr);
            CommitGridEdits(GridDvg);
            CommitGridEdits(GridPbxHw);
            CommitGridEdits(GridGpa);
            DialPlanRepository.Save(_dbPath, _db);
            Toast(true, "저장 완료", "마스터 테이블 변경사항을 저장했습니다.");
        }

        private void BtnSaveMdf_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdits(GridMdf);
            if (_mdfDataTable != null)
            {
                string q = (TxtSearch.Text ?? string.Empty).Trim();
                // Rebuild or update visible rows when unfiltered
                if (string.IsNullOrWhiteSpace(q))
                {
                    var rebuilt = new List<MdfBlockRow>();
                    foreach (DataRow dr in _mdfDataTable.Rows)
                    {
                        int rIdx;
                        int.TryParse(Convert.ToString(dr[1], CultureInfo.InvariantCulture), out rIdx);
                        var cells = new List<string>();
                        for (int c = 2; c < _mdfDataTable.Columns.Count; c++)
                            cells.Add(Convert.ToString(dr[c], CultureInfo.InvariantCulture) ?? string.Empty);
                        while (cells.Count > 0 && string.IsNullOrWhiteSpace(cells[cells.Count - 1]))
                            cells.RemoveAt(cells.Count - 1);
                        rebuilt.Add(new MdfBlockRow
                        {
                            section = Convert.ToString(dr[0], CultureInfo.InvariantCulture) ?? string.Empty,
                            rowIndex = rIdx,
                            cells = cells
                        });
                    }
                    _db.mdfBlocks = rebuilt;
                }
                else
                {
                    foreach (DataRow dr in _mdfDataTable.Rows)
                    {
                        int rIdx;
                        if (!int.TryParse(Convert.ToString(dr[1], CultureInfo.InvariantCulture), out rIdx)) continue;
                        var target = _db.mdfBlocks.FirstOrDefault(m => m.rowIndex == rIdx);
                        if (target == null) continue;
                        target.section = Convert.ToString(dr[0], CultureInfo.InvariantCulture) ?? string.Empty;
                        var cells = new List<string>();
                        for (int c = 2; c < _mdfDataTable.Columns.Count; c++)
                            cells.Add(Convert.ToString(dr[c], CultureInfo.InvariantCulture) ?? string.Empty);
                        while (cells.Count > 0 && string.IsNullOrWhiteSpace(cells[cells.Count - 1]))
                            cells.RemoveAt(cells.Count - 1);
                        target.cells = cells;
                    }
                }
            }
            DialPlanRepository.Save(_dbPath, _db);
            Toast(true, "저장 완료", "MDF / 110 Block 배선 데이터를 저장했습니다.");
        }

        private void Toast(bool isSuccess, string title, string message)
        {
            PopupDialogs.ShowSyntaxToast(Window.GetWindow(this), isSuccess, title, message);
        }

        private void SaveDbAndSyncCustomers(DialPlanCenter syncTarget)
        {
            DialPlanRepository.Save(_dbPath, _db);

            if (syncTarget != null && _getCustomers != null)
            {
                var customers = _getCustomers();
                if (DialPlanRepository.SyncCenterToCustomers(syncTarget, customers, overwritePresetsIfNonEmpty: true))
                {
                    new CustomerRepository().SaveAll(_cmCustomersPath, customers);
                }
            }

            PopulateLocationFilter();
            UpdateStatusCounts();
            ApplyFiltersAndBind();
            if (syncTarget != null) syncTarget.NotifySummaryFields();
            if (_onDatabaseOrCustomerChanged != null)
                _onDatabaseOrCustomerChanged();
        }

        private static void MergeDatabases(DialPlanDatabase target, DialPlanDatabase incoming)
        {
            if (target == null || incoming == null) return;
            target.EnsureLists();
            incoming.EnsureLists();

            foreach (var c in incoming.centers)
            {
                if (!target.centers.Any(x =>
                    string.Equals(x.project, c.project, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.status, c.status, StringComparison.OrdinalIgnoreCase)))
                {
                    c.id = DialPlanWorkbookMapper.BuildCenterId(c.project, target.centers);
                    target.centers.Add(c);
                }
            }

            foreach (var p in incoming.priTrunks)
            {
                if (!target.priTrunks.Any(x => x.boardSlot == p.boardSlot && x.logical == p.logical && x.didRange == p.didRange))
                    target.priTrunks.Add(p);
            }
            foreach (var ent in incoming.entities)
            {
                if (!target.entities.Any(x => x.entityNo == ent.entityNo))
                    target.entities.Add(ent);
            }
            foreach (var ch in incoming.ipIvrChannels)
            {
                if (!target.ipIvrChannels.Any(x => x.ivrDn == ch.ivrDn && x.centerName == ch.centerName))
                    target.ipIvrChannels.Add(ch);
            }
            foreach (var d in incoming.dvgPrompts)
            {
                if (!target.dvgPrompts.Any(x => x.dvgNo == d.dvgNo && x.content == d.content))
                    target.dvgPrompts.Add(d);
            }
        }

        private static void CommitGridEdits(DataGrid grid)
        {
            if (grid == null) return;
            try
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch
            {
                // Ignore commit warnings when no active edit cell
            }
        }

        private static bool MatchesCenterQuery(DialPlanCenter c, string q)
        {
            return c != null && c.MatchesSearchQuery(q);
        }

        private static bool ContainsCI(string field, string needle)
        {
            if (string.IsNullOrEmpty(needle)) return true;
            return !string.IsNullOrEmpty(field) && field.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsAny(string needle, params string[] fields)
        {
            if (string.IsNullOrWhiteSpace(needle)) return true;
            foreach (var f in fields)
            {
                if (!string.IsNullOrEmpty(f) && f.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }

    internal sealed class InverseBoolToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool && (bool)value) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    internal sealed class MergeBottomBorderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool sameBelow = (value is bool && (bool)value);
            return sameBelow ? new Thickness(0, 0, 1, 0) : new Thickness(0, 0, 1, 1);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
