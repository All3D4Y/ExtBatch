package com.gcti.extbatch.dto;

import com.gcti.extbatch.dto.CommonDtos.AuditEventDto;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;

public class Phase3Dtos {

    // -------------------- Unified Lookup --------------------
    public static class LookupSearchRequest {
        public String query;
        public String customerName;
        public String category; // ALL | DN | Place | AgentLogin | Person | Group | Script
        public boolean exactMatch = false;
        public boolean refresh = false;
        public int limit = 1000;
    }

    public static class LookupRelationDto {
        public String relType;        // Place | DN | Person | AgentLogin | AgentGroup | PlaceGroup | DNGroup | Skill
        public String targetCategory;
        public String targetKey;
        public String targetName;
        public String targetPath;
    }

    public static class LookupItemDto {
        public String id;
        public String source = "Config";
        public String category;
        public String subType;
        public Integer dbid;
        public String key;
        public String name;
        public String switchName;
        public String customerInferred;
        public String folderPath;
        public String relationSummary;
        public List<LookupRelationDto> relations = new ArrayList<>();
        public List<String> anomalies = new ArrayList<>();
        public String state;
        public Map<String, String> attributes = new TreeMap<>();
    }

    public static class LookupSearchResponse {
        public String query = "";
        public String customerName = "";
        public int totalLoaded;
        public int totalMatched;
        public boolean fromCache;
        public double elapsedSeconds;
        public Map<String, Integer> categoryCounts = new LinkedHashMap<>();
        public List<LookupItemDto> items = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
    }

    // -------------------- Center Integrity Audit --------------------
    public static class CenterAuditRequest {
        public String customerName;
        public boolean refresh = true;
    }

    public static class CenterPairRowDto {
        public String kind; // Ext/Place | CTI/Person
        public String customer;
        public String number;
        public boolean primaryExists;
        public String primaryPath;
        public String secondaryKey;
        public boolean secondaryExists;
        public String secondaryPath;
        public String linkedInfo;
        public String status; // OK | Warn | Missing
        public String note;
    }

    public static class CenterAuditIssueDto {
        public String severity; // ERROR | WARN | INFO
        public String customer;
        public String code;
        public String target;
        public String detail;
        public String folderPath;
    }

    public static class CenterAuditSummaryRowDto {
        public String customer;
        public String floor;
        public int agentDnCount;
        public int placeCount;
        public int agentLoginCount;
        public int acdPosCount;
        public int personCount;
        public int rsiCount;
        public int issueCount;
        public boolean foldersOk;
        public String note;
    }

    public static class CenterAuditResponse {
        public String customerName = "";
        public int totalCustomersAudited;
        public double elapsedSeconds;
        public Map<String, Integer> stats = new LinkedHashMap<>();
        public List<CenterAuditSummaryRowDto> centerSummaries = new ArrayList<>();
        public List<CenterPairRowDto> pairRows = new ArrayList<>();
        public List<CenterAuditIssueDto> issues = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
    }

    // -------------------- Snapshot & Diff --------------------
    public static class SnapshotCaptureRequest {
        public String label;
        public String customerName;
        public boolean includeGroupsAndScripts = true;
    }

    public static class SnapshotDeleteRequest {
        public String snapshotId;
    }

    public static class SnapshotDiffRequest {
        public String leftId;
        public String rightId; // snapshotId or "__LIVE__"
        public String customerFilter;
        public String categoryFilter;
        public String searchText;
        public boolean onlyChanged = true;
    }

    public static class SnapshotMetaDto {
        public String id;
        public String fileName;
        public String label;
        public String createdAt;
        public String endpoint;
        public String customerScope;
        public boolean includeGroupsAndScripts;
        public int totalObjects;
        public Map<String, Integer> countsByCategory = new LinkedHashMap<>();
        public long sizeBytes;
    }

    public static class SnapshotRecordDto {
        public String identityKey;
        public String category;
        public String naturalKey;
        public Integer dbid;
        public String switchName;
        public String customerInferred;
        public String folderPath;
        public String name;
        public String state;
        public Map<String, String> fields = new TreeMap<>();
    }

    public static class SnapshotFileDto {
        public SnapshotMetaDto meta = new SnapshotMetaDto();
        public List<SnapshotRecordDto> records = new ArrayList<>();
    }

    public static class SnapshotListResponse {
        public String snapshotsDir;
        public int totalCount;
        public List<SnapshotMetaDto> items = new ArrayList<>();
    }

    public static class SnapshotCaptureResponse {
        public boolean ok = true;
        public double elapsedSeconds;
        public SnapshotMetaDto meta = new SnapshotMetaDto();
        public List<String> logs = new ArrayList<>();
    }

    public static class SnapshotDeleteResponse {
        public boolean ok = true;
        public String deletedId;
    }

    public static class FieldDiffDto {
        public String field;
        public String beforeVal;
        public String afterVal;
    }

    public static class DiffEntryDto {
        public String diffType; // ADDED | REMOVED | MODIFIED | UNCHANGED
        public String category;
        public String identityKey;
        public String naturalKey;
        public String name;
        public String customerInferred;
        public String beforeFolderPath;
        public String afterFolderPath;
        public String summary;
        public List<FieldDiffDto> fieldDiffs = new ArrayList<>();
    }

    public static class SnapshotDiffResponse {
        public SnapshotMetaDto leftMeta;
        public SnapshotMetaDto rightMeta;
        public int addedCount;
        public int removedCount;
        public int modifiedCount;
        public int unchangedCount;
        public int totalLeft;
        public int totalRight;
        public double elapsedSeconds;
        public List<DiffEntryDto> entries = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
    }

    // -------------------- Snapshot Restore --------------------
    public static class SnapshotRestoreRequest {
        public String snapshotId;
        public List<String> identityKeys = new ArrayList<>();
        public boolean deleteAddedObjects = false;
        public boolean dryRun = false;
    }

    public static class SnapshotRestoreItemResultDto {
        public String identityKey;
        public String category;
        public String naturalKey;
        public String operation; // RECREATE | UPDATE | DELETE | SKIP
        public String status;    // Recreated | Restored | Deleted | Skipped | Failed | Planned
        public String detail;
        public String folderPath;
    }

    public static class SnapshotRestoreResponse {
        public boolean ok = true;
        public String snapshotId;
        public boolean dryRun;
        public int recreatedCount;
        public int updatedCount;
        public int deletedCount;
        public int skippedCount;
        public int failedCount;
        public double elapsedSeconds;
        public List<SnapshotRestoreItemResultDto> results = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
        public List<AuditEventDto> auditEvents = new ArrayList<>();
    }
}
