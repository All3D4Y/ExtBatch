package com.gcti.extbatch.service;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.SerializationFeature;
import com.genesyslab.platform.applicationblocks.com.objects.CfgAgentGroup;
import com.genesyslab.platform.applicationblocks.com.objects.CfgAgentLogin;
import com.genesyslab.platform.applicationblocks.com.objects.CfgAgentLoginInfo;
import com.genesyslab.platform.applicationblocks.com.objects.CfgDN;
import com.genesyslab.platform.applicationblocks.com.objects.CfgDNGroup;
import com.genesyslab.platform.applicationblocks.com.objects.CfgDNInfo;
import com.genesyslab.platform.applicationblocks.com.objects.CfgFolder;
import com.genesyslab.platform.applicationblocks.com.objects.CfgPerson;
import com.genesyslab.platform.applicationblocks.com.objects.CfgPlace;
import com.genesyslab.platform.applicationblocks.com.objects.CfgPlaceGroup;
import com.genesyslab.platform.applicationblocks.com.objects.CfgScript;
import com.genesyslab.platform.configuration.protocol.types.CfgFlag;
import com.gcti.extbatch.dto.CommonDtos.HealthResponse;
import com.gcti.extbatch.dto.Phase3Dtos.DiffEntryDto;
import com.gcti.extbatch.dto.Phase3Dtos.FieldDiffDto;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotCaptureRequest;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotCaptureResponse;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotDeleteRequest;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotDeleteResponse;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotDiffRequest;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotDiffResponse;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotFileDto;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotListResponse;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotMetaDto;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotRecordDto;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotRestoreRequest;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotRestoreResponse;
import com.gcti.extbatch.psdk.CmCatalogSnapshotLoader;
import com.gcti.extbatch.psdk.CmCatalogSnapshotLoader.LoadedCatalog;
import com.gcti.extbatch.psdk.GenesysSessionManager;
import java.io.File;
import java.nio.file.Files;
import java.nio.file.Path;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.Date;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;
import java.util.TreeSet;
import java.util.UUID;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Service;

@Service
public class SnapshotService {

    public static final String LIVE_SNAPSHOT_ID = "__LIVE__";

    private final CmCatalogSnapshotLoader catalogLoader;
    private final GenesysSessionManager sessionManager;
    private final SnapshotRestoreSupport restoreSupport;
    private final Path snapshotsDir;
    private final ObjectMapper mapper;

    public SnapshotService(
            CmCatalogSnapshotLoader catalogLoader,
            GenesysSessionManager sessionManager,
            SnapshotRestoreSupport restoreSupport,
            @Value("${extbatch.snapshots-dir:../snapshots}") String configuredDir) {
        this.catalogLoader = catalogLoader;
        this.sessionManager = sessionManager;
        this.restoreSupport = restoreSupport;
        this.snapshotsDir = new File(configuredDir).toPath().toAbsolutePath().normalize();
        this.mapper = new ObjectMapper();
        this.mapper.enable(SerializationFeature.INDENT_OUTPUT);
    }

    public SnapshotListResponse listSnapshots() throws Exception {
        ensureDir();
        SnapshotListResponse resp = new SnapshotListResponse();
        resp.snapshotsDir = snapshotsDir.toString();

        File[] files = snapshotsDir.toFile().listFiles((dir, name) ->
                name.startsWith("snap_") && name.endsWith(".json"));
        if (files != null) {
            for (File f : files) {
                try {
                    SnapshotFileDto snap = mapper.readValue(f, SnapshotFileDto.class);
                    if (snap != null && snap.meta != null) {
                        snap.meta.fileName = f.getName();
                        snap.meta.sizeBytes = f.length();
                        if (snap.meta.id == null || snap.meta.id.isEmpty()) {
                            snap.meta.id = stripJsonExt(f.getName());
                        }
                        resp.items.add(snap.meta);
                    }
                } catch (Exception ignored) {
                }
            }
        }
        resp.items.sort((a, b) -> {
            String ca = a.createdAt != null ? a.createdAt : "";
            String cb = b.createdAt != null ? b.createdAt : "";
            return cb.compareTo(ca);
        });
        resp.totalCount = resp.items.size();
        return resp;
    }

    public SnapshotCaptureResponse capture(SnapshotCaptureRequest req) throws Exception {
        long startNs = System.nanoTime();
        ensureDir();

        String label = req != null && req.label != null ? req.label.trim() : "";
        String customerScope = req != null && req.customerName != null ? req.customerName.trim() : "";
        if ("ALL".equalsIgnoreCase(customerScope) || "전체".equals(customerScope)) {
            customerScope = "";
        }
        boolean includeGS = req == null || req.includeGroupsAndScripts;

        LoadedCatalog cat = catalogLoader.load(true, includeGS);
        String tsFile = new SimpleDateFormat("yyyyMMdd_HHmmss", Locale.ROOT).format(new Date());
        String shortUuid = UUID.randomUUID().toString().replace("-", "").substring(0, 6);
        String id = "snap_" + tsFile + "_" + shortUuid;
        String fileName = id + ".json";

        SnapshotFileDto fileDto = buildSnapshotDto(cat, id, fileName, label, customerScope, includeGS);
        Path outPath = snapshotsDir.resolve(fileName).normalize();
        mapper.writeValue(outPath.toFile(), fileDto);
        fileDto.meta.sizeBytes = Files.size(outPath);

        SnapshotCaptureResponse resp = new SnapshotCaptureResponse();
        resp.meta = fileDto.meta;
        resp.elapsedSeconds = round2((System.nanoTime() - startNs) / 1_000_000_000.0);
        resp.logs.add(String.format(Locale.ROOT,
                "[스냅샷 저장] id=%s, 범위=%s, 객체=%d건 (%.2f KB, %.2f초)",
                fileDto.meta.id,
                fileDto.meta.customerScope,
                fileDto.meta.totalObjects,
                fileDto.meta.sizeBytes / 1024.0,
                resp.elapsedSeconds));
        return resp;
    }

    public SnapshotDeleteResponse delete(SnapshotDeleteRequest req) throws Exception {
        String id = req != null && req.snapshotId != null ? req.snapshotId.trim() : "";
        Path p = resolveSafeSnapshotPath(id);
        if (!Files.exists(p)) {
            throw new IllegalArgumentException("삭제할 스냅샷 파일을 찾을 수 없습니다: " + id);
        }
        Files.delete(p);
        SnapshotDeleteResponse resp = new SnapshotDeleteResponse();
        resp.deletedId = id;
        return resp;
    }

    public SnapshotDiffResponse diff(SnapshotDiffRequest req) throws Exception {
        long startNs = System.nanoTime();
        String leftId = req != null && req.leftId != null ? req.leftId.trim() : "";
        String rightId = req != null && req.rightId != null ? req.rightId.trim() : "";
        if (leftId.isEmpty() || rightId.isEmpty()) {
            throw new IllegalArgumentException("비교할 Left / Right 스냅샷을 모두 선택하세요.");
        }

        boolean leftIsLive = LIVE_SNAPSHOT_ID.equalsIgnoreCase(leftId);
        boolean rightIsLive = LIVE_SNAPSHOT_ID.equalsIgnoreCase(rightId);

        SnapshotFileDto leftSnap;
        SnapshotFileDto rightSnap;
        if (!leftIsLive && rightIsLive) {
            leftSnap = loadSnapshotOrLive(leftId, true);
            rightSnap = loadSnapshotOrLive(rightId, leftSnap.meta.includeGroupsAndScripts);
        } else if (leftIsLive && !rightIsLive) {
            rightSnap = loadSnapshotOrLive(rightId, true);
            leftSnap = loadSnapshotOrLive(leftId, rightSnap.meta.includeGroupsAndScripts);
        } else {
            leftSnap = loadSnapshotOrLive(leftId, true);
            rightSnap = loadSnapshotOrLive(rightId, leftSnap.meta.includeGroupsAndScripts);
        }

        Map<String, SnapshotRecordDto> leftMap = new LinkedHashMap<>();
        for (SnapshotRecordDto r : leftSnap.records) {
            leftMap.put(r.identityKey, r);
        }
        Map<String, SnapshotRecordDto> rightMap = new LinkedHashMap<>();
        for (SnapshotRecordDto r : rightSnap.records) {
            rightMap.put(r.identityKey, r);
        }

        Set<String> allKeys = new TreeSet<>(leftMap.keySet());
        allKeys.addAll(rightMap.keySet());

        String custFilter = req != null && req.customerFilter != null ? req.customerFilter.trim() : "";
        if ("ALL".equalsIgnoreCase(custFilter) || "전체".equals(custFilter)) custFilter = "";
        String catFilter = req != null && req.categoryFilter != null ? req.categoryFilter.trim() : "";
        if ("ALL".equalsIgnoreCase(catFilter) || "전체".equals(catFilter)) catFilter = "";
        String searchLower = req != null && req.searchText != null ? req.searchText.trim().toLowerCase(Locale.ROOT) : "";
        boolean onlyChanged = req == null || req.onlyChanged;

        SnapshotDiffResponse resp = new SnapshotDiffResponse();
        resp.leftMeta = leftSnap.meta;
        resp.rightMeta = rightSnap.meta;
        resp.totalLeft = leftSnap.records.size();
        resp.totalRight = rightSnap.records.size();

        for (String key : allKeys) {
            SnapshotRecordDto l = leftMap.get(key);
            SnapshotRecordDto r = rightMap.get(key);
            SnapshotRecordDto ref = r != null ? r : l;

            if (!catFilter.isEmpty() && !ref.category.equalsIgnoreCase(catFilter)) continue;
            if (!custFilter.isEmpty()) {
                String cL = l != null ? CmCatalogSnapshotLoader.safe(l.customerInferred) : "";
                String cR = r != null ? CmCatalogSnapshotLoader.safe(r.customerInferred) : "";
                String pL = l != null ? CmCatalogSnapshotLoader.safe(l.folderPath) : "";
                String pR = r != null ? CmCatalogSnapshotLoader.safe(r.folderPath) : "";
                if (!cL.equalsIgnoreCase(custFilter) && !cR.equalsIgnoreCase(custFilter)
                        && !pL.toLowerCase(Locale.ROOT).contains(custFilter.toLowerCase(Locale.ROOT))
                        && !pR.toLowerCase(Locale.ROOT).contains(custFilter.toLowerCase(Locale.ROOT))) {
                    continue;
                }
            }

            DiffEntryDto entry = new DiffEntryDto();
            entry.category = ref.category;
            entry.identityKey = ref.identityKey;
            entry.naturalKey = ref.naturalKey;
            entry.name = ref.name;
            entry.customerInferred = (r != null && !r.customerInferred.isEmpty()) ? r.customerInferred
                    : (l != null ? l.customerInferred : "");
            entry.beforeFolderPath = l != null ? l.folderPath : "-";
            entry.afterFolderPath = r != null ? r.folderPath : "-";

            if (l == null) {
                entry.diffType = "ADDED";
                entry.summary = "신규 생성 (" + r.folderPath + ")";
                for (Map.Entry<String, String> fe : r.fields.entrySet()) {
                    if (fe.getValue() == null || fe.getValue().isEmpty()) continue;
                    FieldDiffDto fd = new FieldDiffDto();
                    fd.field = fe.getKey();
                    fd.beforeVal = "";
                    fd.afterVal = fe.getValue();
                    entry.fieldDiffs.add(fd);
                }
                resp.addedCount++;
            } else if (r == null) {
                entry.diffType = "REMOVED";
                entry.summary = "삭제됨 (" + l.folderPath + ")";
                for (Map.Entry<String, String> fe : l.fields.entrySet()) {
                    if (fe.getValue() == null || fe.getValue().isEmpty()) continue;
                    FieldDiffDto fd = new FieldDiffDto();
                    fd.field = fe.getKey();
                    fd.beforeVal = fe.getValue();
                    fd.afterVal = "";
                    entry.fieldDiffs.add(fd);
                }
                resp.removedCount++;
            } else {
                Set<String> fieldNames = new TreeSet<>(l.fields.keySet());
                fieldNames.addAll(r.fields.keySet());
                List<String> changes = new ArrayList<>();
                for (String fn : fieldNames) {
                    if ("dbid".equals(fn)) continue;
                    String bv = CmCatalogSnapshotLoader.safe(l.fields.get(fn));
                    String av = CmCatalogSnapshotLoader.safe(r.fields.get(fn));
                    if (!bv.equals(av)) {
                        FieldDiffDto fd = new FieldDiffDto();
                        fd.field = fn;
                        fd.beforeVal = bv;
                        fd.afterVal = av;
                        entry.fieldDiffs.add(fd);
                        changes.add(fn + ": " + trunc(bv, 28) + " -> " + trunc(av, 28));
                    }
                }
                if (entry.fieldDiffs.isEmpty()) {
                    entry.diffType = "UNCHANGED";
                    entry.summary = "변경 없음";
                    resp.unchangedCount++;
                } else {
                    entry.diffType = "MODIFIED";
                    entry.summary = String.join("; ", changes);
                    resp.modifiedCount++;
                }
            }

            if (onlyChanged && "UNCHANGED".equals(entry.diffType)) {
                continue;
            }
            if (!searchLower.isEmpty() && !matchesDiffSearch(entry, searchLower)) {
                continue;
            }
            resp.entries.add(entry);
        }

        // Order: ADDED, REMOVED, MODIFIED, UNCHANGED, then category & naturalKey
        resp.entries.sort(Comparator.comparingInt((DiffEntryDto e) -> diffOrder(e.diffType))
                .thenComparing(e -> e.category != null ? e.category : "")
                .thenComparing(e -> e.naturalKey != null ? e.naturalKey : ""));

        resp.elapsedSeconds = round2((System.nanoTime() - startNs) / 1_000_000_000.0);
        resp.logs.add(String.format(Locale.ROOT,
                "[변경 비교] %s <-> %s | +추가 %d / -삭제 %d / ~변경 %d / =동일 %d (%.2f초)",
                formatMetaTitle(leftSnap.meta),
                formatMetaTitle(rightSnap.meta),
                resp.addedCount, resp.removedCount, resp.modifiedCount, resp.unchangedCount,
                resp.elapsedSeconds));
        return resp;
    }

    public SnapshotRestoreResponse restore(SnapshotRestoreRequest req) throws Exception {
        String snapId = req != null && req.snapshotId != null ? req.snapshotId.trim() : "";
        if (snapId.isEmpty() || LIVE_SNAPSHOT_ID.equalsIgnoreCase(snapId)) {
            throw new IllegalArgumentException("복구할 기준 스냅샷 파일(snap_*.json)을 선택하세요.");
        }
        SnapshotFileDto snap = loadSnapshotOrLive(snapId, true);
        LoadedCatalog liveCat = catalogLoader.load(true, snap.meta.includeGroupsAndScripts);
        SnapshotFileDto liveSnap = buildSnapshotDto(liveCat, LIVE_SNAPSHOT_ID, "", "LIVE (현재 실서버 Config)", "", snap.meta.includeGroupsAndScripts);
        return restoreSupport.executeRestore(snap, liveCat, liveSnap, req);
    }

    private SnapshotFileDto loadSnapshotOrLive(String id, boolean includeGS) throws Exception {
        if (LIVE_SNAPSHOT_ID.equalsIgnoreCase(id)) {
            LoadedCatalog cat = catalogLoader.load(true, includeGS);
            return buildSnapshotDto(cat, LIVE_SNAPSHOT_ID, "", "LIVE (현재 실서버 Config)", "", includeGS);
        }
        Path p = resolveSafeSnapshotPath(id);
        if (!Files.exists(p)) {
            throw new IllegalArgumentException("스냅샷 파일을 찾을 수 없습니다: " + id);
        }
        SnapshotFileDto dto = mapper.readValue(p.toFile(), SnapshotFileDto.class);
        if (dto == null || dto.meta == null) {
            throw new IllegalStateException("스냅샷 JSON 형식이 올바르지 않습니다: " + id);
        }
        return dto;
    }

    private SnapshotFileDto buildSnapshotDto(
            LoadedCatalog cat,
            String id,
            String fileName,
            String label,
            String customerScope,
            boolean includeGS) {
        SnapshotFileDto snap = new SnapshotFileDto();
        HealthResponse health = sessionManager.getHealth();

        snap.meta.id = id;
        snap.meta.fileName = fileName;
        snap.meta.label = label != null && !label.isEmpty() ? label : (customerScope.isEmpty() ? "전체 Config 스냅샷" : customerScope);
        snap.meta.createdAt = new SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.ROOT).format(new Date());
        snap.meta.endpoint = health.activeEndpoint != null ? health.activeEndpoint : "";
        snap.meta.customerScope = customerScope.isEmpty() ? "전체" : customerScope;
        snap.meta.includeGroupsAndScripts = includeGS;

        Map<String, Integer> dupCounters = new HashMap<>();
        List<SnapshotRecordDto> recs = new ArrayList<>();

        // Folders
        for (CfgFolder f : cat.allFolders) {
            String path = cat.folderIdx.resolvePath(f);
            String cust = cat.inferCustomer(f.getDBID(), f.getName());
            SnapshotRecordDto r = new SnapshotRecordDto();
            r.category = "Folder";
            r.naturalKey = path;
            r.dbid = f.getDBID();
            r.name = CmCatalogSnapshotLoader.safe(f.getName());
            r.folderPath = path;
            r.customerInferred = cust;
            r.switchName = "-";
            r.state = f.getState() != null ? f.getState().name() : "";
            r.fields.put("dbid", str(f.getDBID()));
            r.fields.put("name", r.name);
            r.fields.put("folderPath", path);
            r.fields.put("type", f.getType() != null ? f.getType().name() : "");
            r.fields.put("state", r.state);
            finalizeKey(r, dupCounters, recs, customerScope);
        }

        // DNs
        for (CfgDN dn : cat.dns) {
            String path = cat.resolveFolderDisplayPath(dn.getObjectPath(), dn.getFolderId());
            String cust = cat.inferCustomer(dn.getFolderId(), null);
            String catName = CmCatalogSnapshotLoader.mapDnCategory(dn.getType());
            String swName = cat.resolveSwitchName(dn.getSwitchDBID());

            SnapshotRecordDto r = new SnapshotRecordDto();
            r.category = catName;
            r.naturalKey = CmCatalogSnapshotLoader.safe(dn.getNumber());
            r.dbid = dn.getDBID();
            r.switchName = swName;
            r.name = !CmCatalogSnapshotLoader.safe(dn.getName()).isEmpty() ? CmCatalogSnapshotLoader.safe(dn.getName()) : r.naturalKey;
            r.folderPath = path;
            r.customerInferred = cust;
            r.state = dn.getState() != null ? dn.getState().name() : "";

            List<String> plNames = new ArrayList<>();
            if (dn.getDBID() != null) {
                for (CfgPlace p : cat.dnDbidToPlaces.getOrDefault(dn.getDBID(), Collections.emptyList())) {
                    plNames.add(CmCatalogSnapshotLoader.safe(p.getName()));
                }
            }
            Collections.sort(plNames);
            List<String> dgNames = dn.getDBID() != null
                    ? new ArrayList<>(cat.dnDbidToDnGroups.getOrDefault(dn.getDBID(), Collections.emptyList()))
                    : new ArrayList<>();
            Collections.sort(dgNames);

            r.fields.put("dbid", str(dn.getDBID()));
            r.fields.put("number", r.naturalKey);
            r.fields.put("name", CmCatalogSnapshotLoader.safe(dn.getName()));
            r.fields.put("dnType", dn.getType() != null ? dn.getType().name() : "");
            r.fields.put("switchName", swName);
            r.fields.put("folderPath", path);
            r.fields.put("routeType", dn.getRouteType() != null ? dn.getRouteType().name() : "");
            r.fields.put("switchSpecificType", str(dn.getSwitchSpecificType()));
            r.fields.put("useOverride", dn.getUseOverride() != null ? dn.getUseOverride().name() : "");
            r.fields.put("override", CmCatalogSnapshotLoader.safe(dn.getOverride()));
            r.fields.put("association", CmCatalogSnapshotLoader.safe(dn.getAssociation()));
            r.fields.put("linkedPlaces", String.join(", ", plNames));
            r.fields.put("dnGroups", String.join(", ", dgNames));
            r.fields.put("state", r.state);
            r.fields.put("userProperties", CmCatalogSnapshotLoader.flattenUserProperties(dn.getUserProperties()));
            finalizeKey(r, dupCounters, recs, customerScope);
        }

        // Places
        for (CfgPlace p : cat.places) {
            String path = cat.resolveFolderDisplayPath(p.getObjectPath(), p.getFolderId());
            String cust = cat.inferCustomer(p.getFolderId(), null);

            List<String> dnNums = new ArrayList<>();
            if (p.getDNDBIDs() != null) {
                for (Integer dnDbid : p.getDNDBIDs()) {
                    CfgDN dn = cat.dnByDbid.get(dnDbid);
                    dnNums.add(dn != null ? CmCatalogSnapshotLoader.safe(dn.getNumber()) : ("DBID#" + dnDbid));
                }
            }
            Collections.sort(dnNums);
            List<String> pgNames = p.getDBID() != null
                    ? new ArrayList<>(cat.placeDbidToPlaceGroups.getOrDefault(p.getDBID(), Collections.emptyList()))
                    : new ArrayList<>();
            Collections.sort(pgNames);

            SnapshotRecordDto r = new SnapshotRecordDto();
            r.category = "Place";
            r.naturalKey = CmCatalogSnapshotLoader.safe(p.getName());
            r.dbid = p.getDBID();
            r.switchName = "-";
            r.name = r.naturalKey;
            r.folderPath = path;
            r.customerInferred = cust;
            r.state = p.getState() != null ? p.getState().name() : "";

            r.fields.put("dbid", str(p.getDBID()));
            r.fields.put("name", r.naturalKey);
            r.fields.put("folderPath", path);
            r.fields.put("dnNumbers", String.join(", ", dnNums));
            r.fields.put("placeGroups", String.join(", ", pgNames));
            r.fields.put("state", r.state);
            r.fields.put("userProperties", CmCatalogSnapshotLoader.flattenUserProperties(p.getUserProperties()));
            finalizeKey(r, dupCounters, recs, customerScope);
        }

        // AgentLogins
        for (CfgAgentLogin al : cat.logins) {
            String path = cat.resolveFolderDisplayPath(al.getObjectPath(), al.getFolderId());
            String cust = cat.inferCustomer(al.getFolderId(), null);
            String swName = cat.resolveSwitchName(al.getSwitchDBID());

            List<String> personUsers = new ArrayList<>();
            if (al.getDBID() != null) {
                for (CfgPerson p : cat.loginDbidToPersons.getOrDefault(al.getDBID(), Collections.emptyList())) {
                    personUsers.add(CmCatalogSnapshotLoader.safe(p.getUserName()));
                }
            }
            Collections.sort(personUsers);

            SnapshotRecordDto r = new SnapshotRecordDto();
            r.category = "Agent Login";
            r.naturalKey = CmCatalogSnapshotLoader.safe(al.getLoginCode());
            r.dbid = al.getDBID();
            r.switchName = swName;
            r.name = r.naturalKey;
            r.folderPath = path;
            r.customerInferred = cust;
            r.state = al.getState() != null ? al.getState().name() : "";

            r.fields.put("dbid", str(al.getDBID()));
            r.fields.put("loginCode", r.naturalKey);
            r.fields.put("switchName", swName);
            r.fields.put("folderPath", path);
            r.fields.put("useOverride", al.getUseOverride() != null ? al.getUseOverride().name() : "");
            r.fields.put("override", CmCatalogSnapshotLoader.safe(al.getOverride()));
            r.fields.put("assignedPersons", String.join(", ", personUsers));
            r.fields.put("state", r.state);
            r.fields.put("userProperties", CmCatalogSnapshotLoader.flattenUserProperties(al.getUserProperties()));
            finalizeKey(r, dupCounters, recs, customerScope);
        }

        // Persons
        for (CfgPerson p : cat.persons) {
            String path = cat.resolveFolderDisplayPath(p.getObjectPath(), p.getFolderId());
            String cust = cat.inferCustomer(p.getFolderId(), null);

            List<String> loginCodes = new ArrayList<>();
            if (p.getAgentInfo() != null && p.getAgentInfo().getAgentLogins() != null) {
                for (CfgAgentLoginInfo ali : p.getAgentInfo().getAgentLogins()) {
                    Integer lDbid = ali.getAgentLoginDBID();
                    CfgAgentLogin al = lDbid != null ? cat.loginByDbid.get(lDbid) : null;
                    loginCodes.add(al != null ? CmCatalogSnapshotLoader.safe(al.getLoginCode()) : ("DBID#" + lDbid));
                }
            }
            Collections.sort(loginCodes);
            List<String> agNames = p.getDBID() != null
                    ? new ArrayList<>(cat.personDbidToAgentGroups.getOrDefault(p.getDBID(), Collections.emptyList()))
                    : new ArrayList<>();
            Collections.sort(agNames);
            String skills = CmCatalogSnapshotLoader.formatSkillSummary(p, cat.skillNameByDbid);
            String fn = CmCatalogSnapshotLoader.safe(p.getFirstName());
            String ln = CmCatalogSnapshotLoader.safe(p.getLastName());
            String eid = CmCatalogSnapshotLoader.safe(p.getEmployeeID());

            SnapshotRecordDto r = new SnapshotRecordDto();
            r.category = "Person";
            r.naturalKey = CmCatalogSnapshotLoader.safe(p.getUserName());
            r.dbid = p.getDBID();
            r.switchName = "-";
            r.name = !fn.isEmpty() ? fn : r.naturalKey;
            r.folderPath = path;
            r.customerInferred = cust;
            r.state = p.getState() != null ? p.getState().name() : "";

            r.fields.put("dbid", str(p.getDBID()));
            r.fields.put("userName", r.naturalKey);
            r.fields.put("employeeId", eid);
            r.fields.put("firstName", fn);
            r.fields.put("lastName", ln);
            r.fields.put("isAgent", String.valueOf(p.getIsAgent() == CfgFlag.CFGTrue));
            r.fields.put("agentLogins", String.join(", ", loginCodes));
            r.fields.put("agentGroups", String.join(", ", agNames));
            r.fields.put("skills", skills);
            r.fields.put("folderPath", path);
            r.fields.put("state", r.state);
            r.fields.put("userProperties", CmCatalogSnapshotLoader.flattenUserProperties(p.getUserProperties()));
            finalizeKey(r, dupCounters, recs, customerScope);
        }

        if (includeGS) {
            for (CfgPlaceGroup pg : cat.placeGroups) {
                String path = cat.resolveFolderDisplayPath(pg.getObjectPath(), pg.getFolderId());
                String pgName = pg.getGroupInfo() != null ? CmCatalogSnapshotLoader.safe(pg.getGroupInfo().getName()) : "";
                String cust = cat.inferCustomer(pg.getFolderId(), pgName);

                List<String> members = new ArrayList<>();
                if (pg.getPlaceDBIDs() != null) {
                    for (Integer pDbid : pg.getPlaceDBIDs()) {
                        CfgPlace pl = cat.placeByDbid.get(pDbid);
                        members.add(pl != null ? CmCatalogSnapshotLoader.safe(pl.getName()) : ("DBID#" + pDbid));
                    }
                }
                Collections.sort(members);

                SnapshotRecordDto r = new SnapshotRecordDto();
                r.category = "Place Group";
                r.naturalKey = pgName;
                r.dbid = pg.getDBID();
                r.switchName = "-";
                r.name = pgName;
                r.folderPath = path;
                r.customerInferred = cust;
                r.state = pg.getGroupInfo() != null && pg.getGroupInfo().getState() != null ? pg.getGroupInfo().getState().name() : "";
                r.fields.put("dbid", str(pg.getDBID()));
                r.fields.put("name", pgName);
                r.fields.put("folderPath", path);
                r.fields.put("places", String.join(", ", members));
                r.fields.put("state", r.state);
                finalizeKey(r, dupCounters, recs, customerScope);
            }

            for (CfgAgentGroup ag : cat.agentGroups) {
                String path = cat.resolveFolderDisplayPath(ag.getObjectPath(), ag.getFolderId());
                String agName = ag.getGroupInfo() != null ? CmCatalogSnapshotLoader.safe(ag.getGroupInfo().getName()) : "";
                String cust = cat.inferCustomer(ag.getFolderId(), agName);

                List<String> members = new ArrayList<>();
                if (ag.getAgentDBIDs() != null) {
                    for (Integer aDbid : ag.getAgentDBIDs()) {
                        CfgPerson p = cat.personByDbid.get(aDbid);
                        members.add(p != null ? CmCatalogSnapshotLoader.safe(p.getUserName()) : ("DBID#" + aDbid));
                    }
                }
                Collections.sort(members);

                SnapshotRecordDto r = new SnapshotRecordDto();
                r.category = "Agent Group";
                r.naturalKey = agName;
                r.dbid = ag.getDBID();
                r.switchName = "-";
                r.name = agName;
                r.folderPath = path;
                r.customerInferred = cust;
                r.state = ag.getGroupInfo() != null && ag.getGroupInfo().getState() != null ? ag.getGroupInfo().getState().name() : "";
                r.fields.put("dbid", str(ag.getDBID()));
                r.fields.put("name", agName);
                r.fields.put("folderPath", path);
                r.fields.put("agents", String.join(", ", members));
                r.fields.put("state", r.state);
                finalizeKey(r, dupCounters, recs, customerScope);
            }

            for (CfgDNGroup dg : cat.dnGroups) {
                String path = cat.resolveFolderDisplayPath(dg.getObjectPath(), dg.getFolderId());
                String dgName = dg.getGroupInfo() != null ? CmCatalogSnapshotLoader.safe(dg.getGroupInfo().getName()) : "";
                String cust = cat.inferCustomer(dg.getFolderId(), dgName);

                List<String> members = new ArrayList<>();
                if (dg.getDNs() != null) {
                    for (CfgDNInfo info : dg.getDNs()) {
                        Integer dnDbid = info.getDNDBID();
                        CfgDN dn = dnDbid != null ? cat.dnByDbid.get(dnDbid) : null;
                        members.add(dn != null ? CmCatalogSnapshotLoader.safe(dn.getNumber()) : ("DBID#" + dnDbid));
                    }
                }
                Collections.sort(members);

                SnapshotRecordDto r = new SnapshotRecordDto();
                r.category = "DN Group";
                r.naturalKey = dgName;
                r.dbid = dg.getDBID();
                r.switchName = "-";
                r.name = dgName;
                r.folderPath = path;
                r.customerInferred = cust;
                r.state = dg.getGroupInfo() != null && dg.getGroupInfo().getState() != null ? dg.getGroupInfo().getState().name() : "";
                r.fields.put("dbid", str(dg.getDBID()));
                r.fields.put("name", dgName);
                r.fields.put("dnType", dg.getType() != null ? dg.getType().name() : "");
                r.fields.put("folderPath", path);
                r.fields.put("dns", String.join(", ", members));
                r.fields.put("state", r.state);
                finalizeKey(r, dupCounters, recs, customerScope);
            }

            for (CfgScript sc : cat.scripts) {
                String path = cat.resolveFolderDisplayPath(sc.getObjectPath(), sc.getFolderId());
                String cust = cat.inferCustomer(sc.getFolderId(), null);

                SnapshotRecordDto r = new SnapshotRecordDto();
                r.category = "Script";
                r.naturalKey = CmCatalogSnapshotLoader.safe(sc.getName());
                r.dbid = sc.getDBID();
                r.switchName = "-";
                r.name = r.naturalKey;
                r.folderPath = path;
                r.customerInferred = cust;
                r.state = sc.getState() != null ? sc.getState().name() : "";
                r.fields.put("dbid", str(sc.getDBID()));
                r.fields.put("name", r.naturalKey);
                r.fields.put("scriptType", sc.getType() != null ? sc.getType().name() : "");
                r.fields.put("index", str(sc.getIndex()));
                r.fields.put("folderPath", path);
                r.fields.put("state", r.state);
                finalizeKey(r, dupCounters, recs, customerScope);
            }
        }

        recs.sort(Comparator.comparing(r -> r.identityKey));
        snap.records = recs;

        Map<String, Integer> counts = new LinkedHashMap<>();
        for (SnapshotRecordDto r : recs) {
            counts.put(r.category, counts.getOrDefault(r.category, 0) + 1);
        }
        snap.meta.countsByCategory = counts;
        snap.meta.totalObjects = recs.size();
        return snap;
    }

    private static void finalizeKey(
            SnapshotRecordDto r,
            Map<String, Integer> dupCounters,
            List<SnapshotRecordDto> out,
            String customerScope) {
        String base = r.category + "|" + (r.switchName != null ? r.switchName : "-") + "|" + r.naturalKey;
        int n = dupCounters.getOrDefault(base, 0) + 1;
        dupCounters.put(base, n);
        r.identityKey = (n == 1) ? base : (base + "#" + n);
        if (matchesScope(customerScope, r.customerInferred, r.folderPath)
                || ("Place Group".equals(r.category) && r.name != null && r.name.equalsIgnoreCase(customerScope))) {
            out.add(r);
        }
    }

    private static boolean matchesScope(String customerScope, String inferredCustomer, String path) {
        if (customerScope == null || customerScope.isEmpty()) return true;
        if (inferredCustomer != null && inferredCustomer.equalsIgnoreCase(customerScope)) return true;
        return path != null && path.toLowerCase(Locale.ROOT).contains(customerScope.toLowerCase(Locale.ROOT));
    }

    private static boolean matchesDiffSearch(DiffEntryDto e, String q) {
        if (contains(e.naturalKey, q) || contains(e.name, q) || contains(e.category, q)
                || contains(e.customerInferred, q) || contains(e.summary, q)
                || contains(e.beforeFolderPath, q) || contains(e.afterFolderPath, q)) {
            return true;
        }
        for (FieldDiffDto fd : e.fieldDiffs) {
            if (contains(fd.field, q) || contains(fd.beforeVal, q) || contains(fd.afterVal, q)) {
                return true;
            }
        }
        return false;
    }

    private static boolean contains(String s, String q) {
        return s != null && s.toLowerCase(Locale.ROOT).contains(q);
    }

    private static int diffOrder(String diffType) {
        if ("ADDED".equals(diffType)) return 0;
        if ("REMOVED".equals(diffType)) return 1;
        if ("MODIFIED".equals(diffType)) return 2;
        return 3;
    }

    private Path resolveSafeSnapshotPath(String id) {
        String clean = id == null ? "" : id.trim();
        if (clean.endsWith(".json")) clean = clean.substring(0, clean.length() - 5);
        if (!clean.matches("[A-Za-z0-9_.-]+") || clean.contains("..")) {
            throw new IllegalArgumentException("올바르지 않은 스냅샷 ID입니다: " + id);
        }
        Path p = snapshotsDir.resolve(clean + ".json").normalize();
        if (!p.startsWith(snapshotsDir)) {
            throw new IllegalArgumentException("잘못된 스냅샷 경로입니다.");
        }
        return p;
    }

    private void ensureDir() throws Exception {
        if (!Files.exists(snapshotsDir)) {
            Files.createDirectories(snapshotsDir);
        }
    }

    private static String stripJsonExt(String name) {
        return name.endsWith(".json") ? name.substring(0, name.length() - 5) : name;
    }

    private static String str(Integer v) {
        return v == null ? "" : String.valueOf(v);
    }

    private static String trunc(String s, int max) {
        if (s == null) return "";
        return s.length() <= max ? s : (s.substring(0, max - 1) + "…");
    }

    private static String formatMetaTitle(SnapshotMetaDto m) {
        if (m == null) return "-";
        return (m.label != null && !m.label.isEmpty()) ? m.label : m.id;
    }

    private static double round2(double val) {
        return Math.round(val * 100.0) / 100.0;
    }
}
