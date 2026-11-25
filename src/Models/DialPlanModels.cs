using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
        private bool _isDimmedByOther = false;
        private double _rowBlurRadius = 0.0;
        private double _rowContentOpacity = 1.0;

        public event PropertyChangedEventHandler PropertyChanged;

        public string id { get; set; }
        public string status { get; set; }
        public string location { get; set; }
        public string project { get; set; }
        public string cmCustomerName { get; set; }
        public string sourceSheet { get; set; }
        public bool useVQueue { get; set; }
        public bool useIvr { get; set; }
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
        public bool IsBlurred { get { return _isDimmedByOther && _rowBlurRadius > 0.01; } }

        [ScriptIgnore]
        public double RowBlurRadius { get { return _rowBlurRadius; } }

        [ScriptIgnore]
        public double RowContentOpacity { get { return _rowContentOpacity; } }

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

        public void ApplyExpandFocusVisual(bool isExpanded, bool anyOtherExpanded, int blurPercent)
        {
            int clamped = Math.Max(0, Math.Min(100, blurPercent));
            _isDetailExpanded = isExpanded;
            _isDimmedByOther = !isExpanded && anyOtherExpanded;

            if (_isDimmedByOther && clamped > 0)
            {
                _rowBlurRadius = Math.Round((clamped / 100.0) * 10.0, 2);
                _rowContentOpacity = Math.Round(1.0 - (clamped / 100.0) * 0.50, 3);
            }
            else
            {
                _rowBlurRadius = 0.0;
                _rowContentOpacity = 1.0;
            }

            OnPropertyChanged("IsDetailExpanded");
            OnPropertyChanged("IsBlurred");
            OnPropertyChanged("RowBlurRadius");
            OnPropertyChanged("RowContentOpacity");
            OnPropertyChanged("ExpandButtonText");
            OnPropertyChanged("StatusMoveButtonText");
            OnPropertyChanged("DetailHeaderTitleText");
            OnPropertyChanged("DetailHeaderMetaText");
        }

        public void NotifySummaryFields()
        {
            OnPropertyChanged("StatusBadgeText");
            OnPropertyChanged("LocationDisplay");
            OnPropertyChanged("LineCount");
            OnPropertyChanged("HasCmLink");
            OnPropertyChanged("CmLinkedSummary");
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
        public string LocationDisplay { get { return string.IsNullOrWhiteSpace(location) ? "-" : location; } }

        [ScriptIgnore]
        public int LineCount { get { return lines != null ? lines.Count : 0; } }

        [ScriptIgnore]
        public bool HasCmLink { get { return !string.IsNullOrWhiteSpace(cmCustomerName); } }

        [ScriptIgnore]
        public string CmLinkedSummary { get { return HasCmLink ? cmCustomerName : "미연동"; } }

        [ScriptIgnore]
        public string SubTaskSummary { get { return JoinDistinct(lines, l => l.subTask, ", ", 3); } }

        [ScriptIgnore]
        public string MainNumberSummary { get { return JoinDistinct(lines, l => l.mainNumber, ", ", 3); } }

        [ScriptIgnore]
        public string EntitySummary { get { return JoinDistinct(lines, l => l.entityNo, ", ", 3); } }

        [ScriptIgnore]
        public string PgSummary { get { return JoinDistinct(lines, l => l.agentPg, ", ", 3); } }

        [ScriptIgnore]
        public string ExtRangeSummary { get { return JoinDistinct(lines, l => l.phoneExt, " / ", 2); } }

        [ScriptIgnore]
        public string LoginRangeSummary { get { return JoinDistinct(lines, l => l.loginId, " / ", 2); } }

        [ScriptIgnore]
        public string RsiSummary { get { return JoinDistinct(lines, l => FirstNonEmpty(l.agentTransferRsi, l.inboundRsi), ", ", 2); } }

        [ScriptIgnore]
        public string TestIvrSummary { get { return JoinDistinct(lines, l => l.testIvrDn, ", ", 2); } }

        private static string FirstNonEmpty(string a, string b)
        {
            if (!string.IsNullOrWhiteSpace(a)) return a;
            return b ?? string.Empty;
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
    }

    public class DialPlanLine
    {
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
