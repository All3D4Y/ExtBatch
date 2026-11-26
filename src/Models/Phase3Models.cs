using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace CmOxeProvisioner.Models
{
    public class LookupRelationDto
    {
        public string relType { get; set; }
        public string targetCategory { get; set; }
        public string targetKey { get; set; }
        public string targetName { get; set; }
        public string targetPath { get; set; }
    }

    public class LookupItemDto
    {
        public string id { get; set; }
        public string source { get; set; }
        public string category { get; set; }
        public string subType { get; set; }
        public int? dbid { get; set; }
        public string key { get; set; }
        public string name { get; set; }
        public string switchName { get; set; }
        public string customerInferred { get; set; }
        public string folderPath { get; set; }
        public string relationSummary { get; set; }
        public List<LookupRelationDto> relations { get; set; }
        public List<string> anomalies { get; set; }
        public string state { get; set; }
        public Dictionary<string, string> attributes { get; set; }

        public string SourceDisplay
        {
            get { return string.IsNullOrEmpty(source) ? "Config" : source; }
        }

        public bool IsDialPlanSource
        {
            get { return string.Equals(source, "선번장", StringComparison.OrdinalIgnoreCase); }
        }

        public string AnomalySummary
        {
            get
            {
                if (anomalies == null || anomalies.Count == 0) return "정상";
                return string.Join(", ", anomalies.ToArray());
            }
        }

        public bool HasAnomaly
        {
            get { return anomalies != null && anomalies.Count > 0; }
        }
    }

    public class LookupSearchResponseDto
    {
        public string query { get; set; }
        public string customerName { get; set; }
        public int totalLoaded { get; set; }
        public int totalMatched { get; set; }
        public bool fromCache { get; set; }
        public double elapsedSeconds { get; set; }
        public Dictionary<string, int> categoryCounts { get; set; }
        public List<LookupItemDto> items { get; set; }
        public List<string> logs { get; set; }
    }

    public class CenterPairRowDto
    {
        public string kind { get; set; }
        public string customer { get; set; }
        public string number { get; set; }
        public bool primaryExists { get; set; }
        public string primaryPath { get; set; }
        public string secondaryKey { get; set; }
        public bool secondaryExists { get; set; }
        public string secondaryPath { get; set; }
        public string linkedInfo { get; set; }
        public string status { get; set; }
        public string note { get; set; }

        public string PrimaryMark { get { return primaryExists ? "O" : "X"; } }
        public string SecondaryMark { get { return secondaryExists ? "O" : "X"; } }
        public bool IsIssue { get { return !string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase); } }
    }

    public class CenterAuditIssueDto
    {
        public string severity { get; set; }
        public string customer { get; set; }
        public string code { get; set; }
        public string target { get; set; }
        public string detail { get; set; }
        public string folderPath { get; set; }
    }

    public class CenterAuditSummaryRowDto
    {
        public string customer { get; set; }
        public string floor { get; set; }
        public int agentDnCount { get; set; }
        public int placeCount { get; set; }
        public int agentLoginCount { get; set; }
        public int acdPosCount { get; set; }
        public int personCount { get; set; }
        public int rsiCount { get; set; }
        public int issueCount { get; set; }
        public bool foldersOk { get; set; }
        public string note { get; set; }

        public string FolderHealthText { get { return foldersOk ? "정상" : "누락"; } }
        public bool HasIssues { get { return issueCount > 0 || !foldersOk; } }
    }

    public class CenterAuditResponseDto
    {
        public string customerName { get; set; }
        public int totalCustomersAudited { get; set; }
        public double elapsedSeconds { get; set; }
        public Dictionary<string, int> stats { get; set; }
        public List<CenterAuditSummaryRowDto> centerSummaries { get; set; }
        public List<CenterPairRowDto> pairRows { get; set; }
        public List<CenterAuditIssueDto> issues { get; set; }
        public List<string> logs { get; set; }
    }

    public class SnapshotMetaDto
    {
        public string id { get; set; }
        public string fileName { get; set; }
        public string label { get; set; }
        public string createdAt { get; set; }
        public string endpoint { get; set; }
        public string customerScope { get; set; }
        public bool includeGroupsAndScripts { get; set; }
        public int totalObjects { get; set; }
        public Dictionary<string, int> countsByCategory { get; set; }
        public long sizeBytes { get; set; }

        public string SizeKbText
        {
            get
            {
                if (sizeBytes <= 0) return "-";
                return string.Format("{0:0.0} KB", sizeBytes / 1024.0);
            }
        }

        public string DisplayOptionLabel
        {
            get
            {
                if (string.Equals(id, "__LIVE__", StringComparison.OrdinalIgnoreCase))
                {
                    return "LIVE (현재 실서버 Config)";
                }
                return string.Format("[{0}] {1} ({2}, {3}건)", createdAt ?? "-", label ?? id, customerScope ?? "전체", totalObjects);
            }
        }
    }

    public class SnapshotListResponseDto
    {
        public string snapshotsDir { get; set; }
        public int totalCount { get; set; }
        public List<SnapshotMetaDto> items { get; set; }
    }

    public class SnapshotCaptureResponseDto
    {
        public bool ok { get; set; }
        public double elapsedSeconds { get; set; }
        public SnapshotMetaDto meta { get; set; }
        public List<string> logs { get; set; }
    }

    public class SnapshotDeleteResponseDto
    {
        public bool ok { get; set; }
        public string deletedId { get; set; }
    }

    public class FieldDiffDto
    {
        public string field { get; set; }
        public string beforeVal { get; set; }
        public string afterVal { get; set; }
    }

    public class DiffEntryDto : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("IsSelected"));
                    }
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string diffType { get; set; }
        public string category { get; set; }
        public string identityKey { get; set; }
        public string naturalKey { get; set; }
        public string name { get; set; }
        public string customerInferred { get; set; }
        public string beforeFolderPath { get; set; }
        public string afterFolderPath { get; set; }
        public string summary { get; set; }
        public List<FieldDiffDto> fieldDiffs { get; set; }

        public string DiffBadgeText
        {
            get
            {
                if (string.Equals(diffType, "ADDED", StringComparison.OrdinalIgnoreCase)) return "+ 추가";
                if (string.Equals(diffType, "REMOVED", StringComparison.OrdinalIgnoreCase)) return "- 삭제";
                if (string.Equals(diffType, "MODIFIED", StringComparison.OrdinalIgnoreCase)) return "~ 변경";
                return "= 동일";
            }
        }
    }

    public class SnapshotDiffResponseDto
    {
        public SnapshotMetaDto leftMeta { get; set; }
        public SnapshotMetaDto rightMeta { get; set; }
        public int addedCount { get; set; }
        public int removedCount { get; set; }
        public int modifiedCount { get; set; }
        public int unchangedCount { get; set; }
        public int totalLeft { get; set; }
        public int totalRight { get; set; }
        public double elapsedSeconds { get; set; }
        public List<DiffEntryDto> entries { get; set; }
        public List<string> logs { get; set; }
    }

    public class SnapshotRestoreItemResultDto
    {
        public string identityKey { get; set; }
        public string category { get; set; }
        public string naturalKey { get; set; }
        public string operation { get; set; }
        public string status { get; set; }
        public string detail { get; set; }
        public string folderPath { get; set; }
    }

    public class SnapshotRestoreResponseDto
    {
        public bool ok { get; set; }
        public string snapshotId { get; set; }
        public bool dryRun { get; set; }
        public int recreatedCount { get; set; }
        public int updatedCount { get; set; }
        public int deletedCount { get; set; }
        public int skippedCount { get; set; }
        public int failedCount { get; set; }
        public double elapsedSeconds { get; set; }
        public List<SnapshotRestoreItemResultDto> results { get; set; }
        public List<string> logs { get; set; }
        public List<CmOxeProvisioner.Services.AuditEventDto> auditEvents { get; set; }
    }
}
