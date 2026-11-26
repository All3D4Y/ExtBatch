using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CmOxeProvisioner.Models
{
    /// <summary>
    /// Root persistence container for dialplan_db.json (v1.2.0).
    /// Stores migrated workbook data for Dial-Plan centers, PRI trunks,
    /// Entity master, IP IVR channels, GPA boards, D.V.G prompts, PBX hardware, and MDF blocks.
    /// </summary>
    public class DialPlanDatabase
    {
        public string version { get; set; }
        public string sourceFile { get; set; }
        public string updatedAt { get; set; }
        public List<DialPlanCenter> centers { get; set; }
        public List<PriTrunkRow> priTrunks { get; set; }
        public List<EntityMasterRow> entities { get; set; }
        public List<IpIvrChannelRow> ipIvrChannels { get; set; }
        public List<GpaBoardRow> gpaBoards { get; set; }
        public List<DvgPromptRow> dvgPrompts { get; set; }
        public List<PbxHardwareRow> pbxHardware { get; set; }
        public List<MdfBlockRow> mdfBlocks { get; set; }

        public DialPlanDatabase()
        {
            version = "1.2.0";
            sourceFile = string.Empty;
            updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            centers = new List<DialPlanCenter>();
            priTrunks = new List<PriTrunkRow>();
            entities = new List<EntityMasterRow>();
            ipIvrChannels = new List<IpIvrChannelRow>();
            gpaBoards = new List<GpaBoardRow>();
            dvgPrompts = new List<DvgPromptRow>();
            pbxHardware = new List<PbxHardwareRow>();
            mdfBlocks = new List<MdfBlockRow>();
        }

        public void EnsureLists()
        {
            if (centers == null) centers = new List<DialPlanCenter>();
            if (priTrunks == null) priTrunks = new List<PriTrunkRow>();
            if (entities == null) entities = new List<EntityMasterRow>();
            if (ipIvrChannels == null) ipIvrChannels = new List<IpIvrChannelRow>();
            if (gpaBoards == null) gpaBoards = new List<GpaBoardRow>();
            if (dvgPrompts == null) dvgPrompts = new List<DvgPromptRow>();
            if (pbxHardware == null) pbxHardware = new List<PbxHardwareRow>();
            if (mdfBlocks == null) mdfBlocks = new List<MdfBlockRow>();

            foreach (var c in centers)
            {
                if (c != null) c.EnsureLists();
            }
            foreach (var m in mdfBlocks)
            {
                if (m != null && m.cells == null) m.cells = new List<string>();
            }
        }
    }

    public static class DialPlanStatus
    {
        public const string Active = "active";
        public const string Test = "test";
        public const string Closed = "closed";

        public static string ToDisplay(string status)
        {
            if (string.Equals(status, Test, StringComparison.OrdinalIgnoreCase)) return "테스트용";
            if (string.Equals(status, Closed, StringComparison.OrdinalIgnoreCase)) return "업무 종료";
            return "운영 중";
        }

        public static string Normalize(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return Active;
            string s = status.Trim().ToLowerInvariant();
            if (s == Test || s.Contains("테스트")) return Test;
            if (s == Closed || s.Contains("종료")) return Closed;
            return Active;
        }
    }

    public class DialPlanCenter : INotifyPropertyChanged
    {
        private bool _isDetailExpanded = false;

        public event PropertyChangedEventHandler PropertyChanged;

        public string id { get; set; }
        public string status { get; set; }
        public string location { get; set; }
        public string project { get; set; }
        public string cmCustomerName { get; set; }
        public string sourceSheet { get; set; }
        public bool useVQueue { get; set; }
        public bool useIvr { get; set; }
        public string apUrl { get; set; }
        public string closedAt { get; set; }
        public string notes { get; set; }
        public List<DialPlanLine> lines { get; set; }

        public DialPlanCenter()
        {
            id = string.Empty;
            status = DialPlanStatus.Active;
            location = string.Empty;
            project = string.Empty;
            cmCustomerName = string.Empty;
            sourceSheet = "Dial-Plan";
            useVQueue = false;
            useIvr = false;
            apUrl = string.Empty;
            closedAt = string.Empty;
            notes = string.Empty;
            lines = new List<DialPlanLine>();
        }

        public void EnsureLists()
        {
            if (lines == null) lines = new List<DialPlanLine>();
            status = DialPlanStatus.Normalize(status);
        }

        [ScriptIgnore]
        public bool IsDetailExpanded { get { return _isDetailExpanded; } }

        [ScriptIgnore]
        public string ExpandButtonText { get { return _isDetailExpanded ? "▴ 접기" : "▾ 상세"; } }

        [ScriptIgnore]
        public string StatusMoveButtonText
        {
            get { return status == DialPlanStatus.Closed ? "운영 중으로 복구" : "업무 종료로 이동"; }
        }

        [ScriptIgnore]
        public string DetailHeaderTitleText
        {
            get { return "[" + StatusBadgeText + "] " + (project ?? string.Empty); }
        }

        [ScriptIgnore]
        public string DetailHeaderMetaText
        {
            get
            {
                return "위치: " + LocationDisplay +
                       " · 라인 " + LineCount + "건" +
                       " · CM 연동: " + CmLinkedSummary;
            }
        }

        public void SetExpanded(bool isExpanded)
        {
            if (_isDetailExpanded == isExpanded) return;
            _isDetailExpanded = isExpanded;
            OnPropertyChanged("IsDetailExpanded");
            OnPropertyChanged("ExpandButtonText");
            OnPropertyChanged("IsClosedStatus");
            OnPropertyChanged("StatusMoveButtonText");
            OnPropertyChanged("DetailHeaderTitleText");
            OnPropertyChanged("DetailHeaderMetaText");
        }

        public void RefreshLineMergeVisuals(bool enabled)
        {
            EnsureLists();
            DialPlanLine.UpdateCenterMergeVisuals(lines, enabled);
        }

        public void NotifySummaryFields()
        {
            OnPropertyChanged("StatusBadgeText");
            OnPropertyChanged("IsClosedStatus");
            OnPropertyChanged("LocationDisplay");
            OnPropertyChanged("LineCount");
            OnPropertyChanged("HasCmLink");
            OnPropertyChanged("CmLinkedSummary");
            OnPropertyChanged("HasApUrl");
            OnPropertyChanged("apUrl");
            OnPropertyChanged("SubTaskSummary");
            OnPropertyChanged("MainNumberSummary");
            OnPropertyChanged("EntitySummary");
            OnPropertyChanged("PgSummary");
            OnPropertyChanged("ExtRangeSummary");
            OnPropertyChanged("LoginRangeSummary");
            OnPropertyChanged("RsiSummary");
            OnPropertyChanged("TestIvrSummary");
            OnPropertyChanged("StatusMoveButtonText");
            OnPropertyChanged("DetailHeaderTitleText");
            OnPropertyChanged("DetailHeaderMetaText");
            OnPropertyChanged("project");
            OnPropertyChanged("location");
            OnPropertyChanged("status");
            OnPropertyChanged("cmCustomerName");
            OnPropertyChanged("useVQueue");
            OnPropertyChanged("useIvr");
            OnPropertyChanged("notes");
        }

        private void OnPropertyChanged(string propName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propName));
            }
        }

        [ScriptIgnore]
        public string StatusBadgeText { get { return DialPlanStatus.ToDisplay(status); } }

        [ScriptIgnore]
        public bool IsClosedStatus { get { return status == DialPlanStatus.Closed; } }

        [ScriptIgnore]
        public string LocationDisplay { get { return string.IsNullOrWhiteSpace(location) ? "-" : location; } }

        [ScriptIgnore]
        public int LineCount { get { return lines != null ? lines.Count : 0; } }

        [ScriptIgnore]
        public bool HasCmLink { get { return !string.IsNullOrWhiteSpace(cmCustomerName); } }

        [ScriptIgnore]
        public string CmLinkedSummary { get { return HasCmLink ? cmCustomerName : "미연동"; } }

        [ScriptIgnore]
        public bool HasApUrl { get { return !string.IsNullOrWhiteSpace(apUrl); } }

        [ScriptIgnore]
        public string SubTaskSummary { get { return JoinDistinct(lines, l => l.subTask, ", ", 3); } }

        [ScriptIgnore]
        public string MainNumberSummary { get { return JoinDistinctExtracted(lines, l => ExtractPhoneNumberTokens(l.mainNumber), ", ", 3); } }

        [ScriptIgnore]
        public string EntitySummary { get { return JoinDistinctExtracted(lines, l => ExtractSingleLeadingNumber(l.entityNo), ", ", 5); } }

        [ScriptIgnore]
        public string PgSummary { get { return JoinDistinctExtracted(lines, l => ExtractNumericOrRangeTokens(l.agentPg), ", ", 4); } }

        [ScriptIgnore]
        public string ExtRangeSummary { get { return JoinDistinctExtracted(lines, l => ExtractNumericOrRangeTokens(l.phoneExt), " / ", 2); } }

        [ScriptIgnore]
        public string LoginRangeSummary { get { return JoinDistinctExtracted(lines, l => ExtractNumericOrRangeTokens(l.loginId), " / ", 2); } }

        [ScriptIgnore]
        public string RsiSummary { get { return JoinDistinctExtracted(lines, l => ExtractNumericOrRangeTokens(FirstNonEmpty(l.agentTransferRsi, l.inboundRsi)), ", ", 3); } }

        [ScriptIgnore]
        public string TestIvrSummary { get { return JoinDistinctExtracted(lines, l => ExtractNumericOrRangeTokens(l.testIvrDn), ", ", 2); } }

        private static string FirstNonEmpty(string a, string b)
        {
            if (!string.IsNullOrWhiteSpace(a)) return a;
            return b ?? string.Empty;
        }

        private static IEnumerable<string> ExtractSingleLeadingNumber(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) yield break;
            var m = Regex.Match(raw.Trim(), @"\d+");
            if (m.Success) yield return m.Value;
        }

        private static IEnumerable<string> ExtractPhoneNumberTokens(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) yield break;
            var matches = Regex.Matches(raw, @"\d{2,4}(?:-\d{3,4}){1,2}");
            if (matches.Count > 0)
            {
                foreach (Match m in matches)
                    yield return m.Value;
                yield break;
            }
            var fallback = Regex.Matches(raw, @"\d{4,}");
            foreach (Match m in fallback)
                yield return m.Value;
        }

        private static IEnumerable<string> ExtractNumericOrRangeTokens(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) yield break;
            var matches = Regex.Matches(raw, @"\d+(?:\s*~\s*\d+)?");
            foreach (Match m in matches)
            {
                string norm = Regex.Replace(m.Value, @"\s+", string.Empty);
                if (!string.IsNullOrWhiteSpace(norm))
                    yield return norm;
            }
        }

        private static string JoinDistinct(List<DialPlanLine> rows, Func<DialPlanLine, string> selector, string sep, int maxItems)
        {
            if (rows == null || rows.Count == 0) return "-";
            var seen = new List<string>();
            foreach (var r in rows)
            {
                if (r == null) continue;
                string v = (selector(r) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(v) || v == "-") continue;
                if (!seen.Any(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase)))
                    seen.Add(v);
            }
            if (seen.Count == 0) return "-";
            if (seen.Count <= maxItems) return string.Join(sep, seen);
            return string.Join(sep, seen.Take(maxItems)) + " 외 " + (seen.Count - maxItems) + "건";
        }

        private static string JoinDistinctExtracted(List<DialPlanLine> rows, Func<DialPlanLine, IEnumerable<string>> extractor, string sep, int maxItems)
        {
            if (rows == null || rows.Count == 0) return "-";
            var seen = new List<string>();
            foreach (var r in rows)
            {
                if (r == null) continue;
                foreach (var token in extractor(r))
                {
                    if (string.IsNullOrWhiteSpace(token)) continue;
                    if (!seen.Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase)))
                        seen.Add(token);
                }
            }
            if (seen.Count == 0) return "-";
            if (seen.Count <= maxItems) return string.Join(sep, seen);
            return string.Join(sep, seen.Take(maxItems)) + " 외 " + (seen.Count - maxItems) + "건";
        }
    }

    public class LineCellMergeState
    {
        public bool SameAsAbove { get; private set; }
        public bool SameAsBelow { get; private set; }

        public LineCellMergeState(bool sameAsAbove, bool sameAsBelow)
        {
            SameAsAbove = sameAsAbove;
            SameAsBelow = sameAsBelow;
        }
    }

    public class LineMergeLookup
    {
        private static readonly LineCellMergeState EmptyState = new LineCellMergeState(false, false);
        private readonly Dictionary<string, LineCellMergeState> _map = new Dictionary<string, LineCellMergeState>(StringComparer.Ordinal);

        public LineCellMergeState this[string propName]
        {
            get
            {
                LineCellMergeState st;
                if (!string.IsNullOrEmpty(propName) && _map.TryGetValue(propName, out st))
                    return st;
                return EmptyState;
            }
        }

        internal void Set(string propName, bool sameAsAbove, bool sameAsBelow)
        {
            _map[propName] = new LineCellMergeState(sameAsAbove, sameAsBelow);
        }

        internal void Clear()
        {
            _map.Clear();
        }
    }

    public class DialPlanLine : INotifyPropertyChanged
    {
        private static readonly string[] MergeableProperties = new[]
        {
            "subTask", "mainNumber", "carrier", "ownerName", "incomingNumber", "inboundRsi",
            "ivrPdsChannel", "ivrFwPilot", "agentTransferRsi", "agentPilot", "agentQueue",
            "agentPg", "phoneExt", "loginId", "callType", "entityNo", "trunkGroupNo",
            "testIvrDn", "note", "legacyIvrRsi", "legacyIvrPilot", "legacyRsiNote"
        };

        private readonly LineMergeLookup _mergeLookup = new LineMergeLookup();

        public event PropertyChangedEventHandler PropertyChanged;
        public string subTask { get; set; }
        public string mainNumber { get; set; }
        public string carrier { get; set; }
        public string ownerName { get; set; }
        public string incomingNumber { get; set; }
        public string inboundRsi { get; set; }
        public string ivrPdsChannel { get; set; }
        public string ivrFwPilot { get; set; }
        public string agentTransferRsi { get; set; }
        public string agentPilot { get; set; }
        public string agentQueue { get; set; }
        public string agentPg { get; set; }
        public string phoneExt { get; set; }
        public string loginId { get; set; }
        public string callType { get; set; }
        public string entityNo { get; set; }
        public string trunkGroupNo { get; set; }
        public string testIvrDn { get; set; }
        public string note { get; set; }

        // Optional legacy columns from 종료센터 (R4..R20 26-column layout)
        public string legacyIvrRsi { get; set; }
        public string legacyIvrPilot { get; set; }
        public string legacyIvrQueue { get; set; }
        public string legacyIvrPg { get; set; }
        public string legacyRsiNote { get; set; }

        public DialPlanLine()
        {
            subTask = string.Empty;
            mainNumber = string.Empty;
            carrier = string.Empty;
            ownerName = string.Empty;
            incomingNumber = string.Empty;
            inboundRsi = string.Empty;
            ivrPdsChannel = string.Empty;
            ivrFwPilot = string.Empty;
            agentTransferRsi = string.Empty;
            agentPilot = string.Empty;
            agentQueue = string.Empty;
            agentPg = string.Empty;
            phoneExt = string.Empty;
            loginId = string.Empty;
            callType = string.Empty;
            entityNo = string.Empty;
            trunkGroupNo = string.Empty;
            testIvrDn = string.Empty;
            note = string.Empty;
            legacyIvrRsi = string.Empty;
            legacyIvrPilot = string.Empty;
            legacyIvrQueue = string.Empty;
            legacyIvrPg = string.Empty;
            legacyRsiNote = string.Empty;
        }

        [ScriptIgnore]
        public LineMergeLookup Merge { get { return _mergeLookup; } }

        public void NotifyMergeChanged()
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs("Merge"));
            }
        }

        public static void UpdateCenterMergeVisuals(List<DialPlanLine> lines, bool enabled)
        {
            if (lines == null || lines.Count == 0) return;
            for (int i = 0; i < lines.Count; i++)
            {
                DialPlanLine row = lines[i];
                if (row == null) continue;
                row._mergeLookup.Clear();
                if (!enabled)
                {
                    row.NotifyMergeChanged();
                    continue;
                }

                DialPlanLine prev = i > 0 ? lines[i - 1] : null;
                DialPlanLine next = (i + 1) < lines.Count ? lines[i + 1] : null;

                foreach (string prop in MergeableProperties)
                {
                    string curVal = NormalizeCompare(GetPropValue(row, prop));
                    if (string.IsNullOrEmpty(curVal))
                    {
                        row._mergeLookup.Set(prop, false, false);
                        continue;
                    }

                    string prevVal = prev != null ? NormalizeCompare(GetPropValue(prev, prop)) : string.Empty;
                    string nextVal = next != null ? NormalizeCompare(GetPropValue(next, prop)) : string.Empty;

                    bool sameAbove = string.Equals(curVal, prevVal, StringComparison.Ordinal);
                    bool sameBelow = string.Equals(curVal, nextVal, StringComparison.Ordinal);
                    row._mergeLookup.Set(prop, sameAbove, sameBelow);
                }

                row.NotifyMergeChanged();
            }
        }

        private static string NormalizeCompare(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            return Regex.Replace(raw.Replace("\r\n", "\n").Replace("\r", "\n").Trim(), @"[ \t]+", " ");
        }

        private static string GetPropValue(DialPlanLine ln, string prop)
        {
            switch (prop)
            {
                case "subTask": return ln.subTask;
                case "mainNumber": return ln.mainNumber;
                case "carrier": return ln.carrier;
                case "ownerName": return ln.ownerName;
                case "incomingNumber": return ln.incomingNumber;
                case "inboundRsi": return ln.inboundRsi;
                case "ivrPdsChannel": return ln.ivrPdsChannel;
                case "ivrFwPilot": return ln.ivrFwPilot;
                case "agentTransferRsi": return ln.agentTransferRsi;
                case "agentPilot": return ln.agentPilot;
                case "agentQueue": return ln.agentQueue;
                case "agentPg": return ln.agentPg;
                case "phoneExt": return ln.phoneExt;
                case "loginId": return ln.loginId;
                case "callType": return ln.callType;
                case "entityNo": return ln.entityNo;
                case "trunkGroupNo": return ln.trunkGroupNo;
                case "testIvrDn": return ln.testIvrDn;
                case "note": return ln.note;
                case "legacyIvrRsi": return ln.legacyIvrRsi;
                case "legacyIvrPilot": return ln.legacyIvrPilot;
                case "legacyRsiNote": return ln.legacyRsiNote;
                default: return string.Empty;
            }
        }

        public DialPlanLine Clone()
        {
            return (DialPlanLine)MemberwiseClone();
        }
    }

    public class PriTrunkRow
    {
        public string boardSlot { get; set; }
        public string logical { get; set; }
        public string tgNew { get; set; }
        public string status { get; set; }
        public string centerName { get; set; }
        public string trunkGroupName { get; set; }
        public string didRange { get; set; }
        public string carrier { get; set; }
        public string didDod { get; set; }
        public string subscriberNo { get; set; }
        public string relayNo { get; set; }
        public string openedDate { get; set; }
        public string note { get; set; }
    }

    public class EntityMasterRow
    {
        public string entityNo { get; set; }
        public string entityName { get; set; }
        public string discNum { get; set; }
        public string note { get; set; }
    }

    public class IpIvrChannelRow
    {
        public string deviceName { get; set; }
        public string ivrDn { get; set; }
        public string channelCount { get; set; }
        public string centerName { get; set; }
        public string note { get; set; }
    }

    public class GpaBoardRow
    {
        public string boardLocation { get; set; }
        public string remainingTime { get; set; }
        public string note { get; set; }
    }

    public class DvgPromptRow
    {
        public string band { get; set; }
        public string category { get; set; }
        public string subCategory { get; set; }
        public string content { get; set; }
        public string dvgNo { get; set; }
        public string durationSec { get; set; }
        public string board { get; set; }
        public string note { get; set; }
    }

    public class PbxHardwareRow
    {
        public string pbxSet { get; set; }
        public string boardSlot { get; set; }
        public string boardName { get; set; }
        public string ipAddress { get; set; }
        public string macAddress { get; set; }
        public string note { get; set; }
    }

    public class MdfBlockRow
    {
        public string section { get; set; }
        public int rowIndex { get; set; }
        public List<string> cells { get; set; }

        public MdfBlockRow()
        {
            section = string.Empty;
            cells = new List<string>();
        }

        [ScriptIgnore]
        public string CellsSummary
        {
            get
            {
                if (cells == null || cells.Count == 0) return string.Empty;
                return string.Join(" | ", cells.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()));
            }
        }
    }
}
