package com.gcti.extbatch.service;

import com.genesyslab.platform.applicationblocks.com.IConfService;
import com.genesyslab.platform.applicationblocks.com.objects.CfgAgentGroup;
import com.genesyslab.platform.applicationblocks.com.objects.CfgAgentInfo;
import com.genesyslab.platform.applicationblocks.com.objects.CfgAgentLogin;
import com.genesyslab.platform.applicationblocks.com.objects.CfgAgentLoginInfo;
import com.genesyslab.platform.applicationblocks.com.objects.CfgDN;
import com.genesyslab.platform.applicationblocks.com.objects.CfgDNGroup;
import com.genesyslab.platform.applicationblocks.com.objects.CfgDNInfo;
import com.genesyslab.platform.applicationblocks.com.objects.CfgFolder;
import com.genesyslab.platform.applicationblocks.com.objects.CfgGroup;
import com.genesyslab.platform.applicationblocks.com.objects.CfgPerson;
import com.genesyslab.platform.applicationblocks.com.objects.CfgPlace;
import com.genesyslab.platform.applicationblocks.com.objects.CfgPlaceGroup;
import com.genesyslab.platform.applicationblocks.com.objects.CfgScript;
import com.genesyslab.platform.commons.collections.KeyValueCollection;
import com.genesyslab.platform.configuration.protocol.types.CfgDNGroupType;
import com.genesyslab.platform.configuration.protocol.types.CfgDNRegisterFlag;
import com.genesyslab.platform.configuration.protocol.types.CfgDNType;
import com.genesyslab.platform.configuration.protocol.types.CfgFlag;
import com.genesyslab.platform.configuration.protocol.types.CfgObjectState;
import com.genesyslab.platform.configuration.protocol.types.CfgRouteType;
import com.genesyslab.platform.configuration.protocol.types.CfgScriptType;
import com.gcti.extbatch.dto.CommonDtos.AuditEventDto;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotFileDto;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotRecordDto;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotRestoreItemResultDto;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotRestoreRequest;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotRestoreResponse;
import com.gcti.extbatch.psdk.CmCatalogSnapshotLoader;
import com.gcti.extbatch.psdk.CmCatalogSnapshotLoader.LoadedCatalog;
import com.gcti.extbatch.psdk.CmFolderResolver;
import com.gcti.extbatch.psdk.CmFolderResolver.FolderIndex;
import com.gcti.extbatch.psdk.GenesysSessionManager;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collection;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.TreeSet;
import org.springframework.stereotype.Component;

@Component
public class SnapshotRestoreSupport {

    private static final Set<String> DERIVED_READONLY_FIELDS = new HashSet<>(Arrays.asList(
            "dbid", "linkedPlaces", "dnGroups", "placeGroups", "assignedPersons", "agentGroups"
    ));

    private final GenesysSessionManager sessionManager;
    private final CmFolderResolver folderResolver;

    public SnapshotRestoreSupport(GenesysSessionManager sessionManager, CmFolderResolver folderResolver) {
        this.sessionManager = sessionManager;
        this.folderResolver = folderResolver;
    }

    private enum OpKind { RECREATE, UPDATE, DELETE, SKIP }

    private static class RestorePlanItem {
        String key;
        OpKind op;
        SnapshotRecordDto snap;
        SnapshotRecordDto live;
        Set<String> changedFields = new TreeSet<>();
        int sortRank;
        int pathDepth;
    }

    private static class LiveState {
        final IConfService service;
        final int tenantDbid;
        final int defaultSwitchDbid;
        final String defaultSwitchName;
        FolderIndex folderIdx;
        final Map<Integer, CfgFolder> folderByDbid = new HashMap<>();
        final Map<String, CfgFolder> folderByLowerPath = new HashMap<>();
        final Map<String, Integer> switchDbidByLowerName = new HashMap<>();
        final Map<Integer, CfgDN> dnByDbid = new HashMap<>();
        final Map<String, List<CfgDN>> dnsByCategorySwitchNum = new HashMap<>();
        final Map<String, List<CfgDN>> dnsByNum = new HashMap<>();
        final Map<Integer, CfgPlace> placeByDbid = new HashMap<>();
        final Map<String, CfgPlace> placeByLowerName = new HashMap<>();
        final Map<Integer, CfgAgentLogin> loginByDbid = new HashMap<>();
        final Map<String, CfgAgentLogin> loginBySwitchCode = new HashMap<>();
        final Map<String, CfgAgentLogin> loginByCode = new HashMap<>();
        final Map<Integer, CfgPerson> personByDbid = new HashMap<>();
        final Map<String, CfgPerson> personByLowerUser = new HashMap<>();
        final Map<Integer, CfgPlaceGroup> pgByDbid = new HashMap<>();
        final Map<String, CfgPlaceGroup> pgByLowerName = new HashMap<>();
        final Map<Integer, CfgAgentGroup> agByDbid = new HashMap<>();
        final Map<String, CfgAgentGroup> agByLowerName = new HashMap<>();
        final Map<Integer, CfgDNGroup> dgByDbid = new HashMap<>();
        final Map<String, CfgDNGroup> dgByLowerName = new HashMap<>();
        final Map<Integer, CfgScript> scriptByDbid = new HashMap<>();
        final Map<String, CfgScript> scriptByLowerName = new HashMap<>();

        LiveState(IConfService service, LoadedCatalog cat, int tenantDbid, int defaultSwitchDbid, String defaultSwitchName) {
            this.service = service;
            this.folderIdx = cat.folderIdx;
            this.tenantDbid = tenantDbid;
            this.defaultSwitchDbid = defaultSwitchDbid;
            this.defaultSwitchName = defaultSwitchName;

            for (Map.Entry<Integer, String> sw : cat.switchNamesByDbid.entrySet()) {
                if (sw.getValue() != null && !sw.getValue().trim().isEmpty()) {
                    switchDbidByLowerName.put(sw.getValue().trim().toLowerCase(Locale.ROOT), sw.getKey());
                }
            }
            for (CfgFolder f : cat.allFolders) {
                registerFolder(f, cat.folderIdx.resolvePath(f));
            }
            for (CfgDN dn : cat.dns) {
                registerDn(dn, cat.resolveSwitchName(dn.getSwitchDBID()));
            }
            for (CfgPlace p : cat.places) {
                registerPlace(p);
            }
            for (CfgAgentLogin al : cat.logins) {
                registerLogin(al, cat.resolveSwitchName(al.getSwitchDBID()));
            }
            for (CfgPerson p : cat.persons) {
                registerPerson(p);
            }
            for (CfgPlaceGroup pg : cat.placeGroups) {
                registerPlaceGroup(pg);
            }
            for (CfgAgentGroup ag : cat.agentGroups) {
                registerAgentGroup(ag);
            }
            for (CfgDNGroup dg : cat.dnGroups) {
                registerDnGroup(dg);
            }
            for (CfgScript sc : cat.scripts) {
                registerScript(sc);
            }
        }

        void registerFolder(CfgFolder f, String dispPath) {
            if (f == null) return;
            if (f.getDBID() != null) folderByDbid.put(f.getDBID(), f);
            if (dispPath != null && !dispPath.isEmpty()) folderByLowerPath.put(dispPath.toLowerCase(Locale.ROOT), f);
        }

        void registerDn(CfgDN dn, String swName) {
            if (dn == null) return;
            if (dn.getDBID() != null) dnByDbid.put(dn.getDBID(), dn);
            String num = safe(dn.getNumber());
            String cat = CmCatalogSnapshotLoader.mapDnCategory(dn.getType());
            dnsByCategorySwitchNum.computeIfAbsent(dnKey(cat, swName, num), k -> new ArrayList<>()).add(dn);
            dnsByNum.computeIfAbsent(num, k -> new ArrayList<>()).add(dn);
        }

        void registerPlace(CfgPlace p) {
            if (p == null) return;
            if (p.getDBID() != null) placeByDbid.put(p.getDBID(), p);
            String nm = safe(p.getName()).toLowerCase(Locale.ROOT);
            if (!nm.isEmpty()) placeByLowerName.put(nm, p);
        }

        void registerLogin(CfgAgentLogin al, String swName) {
            if (al == null) return;
            if (al.getDBID() != null) loginByDbid.put(al.getDBID(), al);
            String code = safe(al.getLoginCode());
            if (!code.isEmpty()) {
                loginBySwitchCode.put(swName.toLowerCase(Locale.ROOT) + "|" + code, al);
                loginByCode.putIfAbsent(code, al);
            }
        }

        void registerPerson(CfgPerson p) {
            if (p == null) return;
            if (p.getDBID() != null) personByDbid.put(p.getDBID(), p);
            String un = safe(p.getUserName()).toLowerCase(Locale.ROOT);
            if (!un.isEmpty()) personByLowerUser.put(un, p);
        }

        void registerPlaceGroup(CfgPlaceGroup pg) {
            if (pg == null) return;
            if (pg.getDBID() != null) pgByDbid.put(pg.getDBID(), pg);
            String nm = pg.getGroupInfo() != null ? safe(pg.getGroupInfo().getName()).toLowerCase(Locale.ROOT) : "";
            if (!nm.isEmpty()) pgByLowerName.put(nm, pg);
        }

        void registerAgentGroup(CfgAgentGroup ag) {
            if (ag == null) return;
            if (ag.getDBID() != null) agByDbid.put(ag.getDBID(), ag);
            String nm = ag.getGroupInfo() != null ? safe(ag.getGroupInfo().getName()).toLowerCase(Locale.ROOT) : "";
            if (!nm.isEmpty()) agByLowerName.put(nm, ag);
        }

        void registerDnGroup(CfgDNGroup dg) {
            if (dg == null) return;
            if (dg.getDBID() != null) dgByDbid.put(dg.getDBID(), dg);
            String nm = dg.getGroupInfo() != null ? safe(dg.getGroupInfo().getName()).toLowerCase(Locale.ROOT) : "";
            if (!nm.isEmpty()) dgByLowerName.put(nm, dg);
        }

        void registerScript(CfgScript sc) {
            if (sc == null) return;
            if (sc.getDBID() != null) scriptByDbid.put(sc.getDBID(), sc);
            String nm = safe(sc.getName()).toLowerCase(Locale.ROOT);
            if (!nm.isEmpty()) scriptByLowerName.put(nm, sc);
        }

        int resolveSwitchDbid(String swName) {
            if (swName != null && !swName.trim().isEmpty() && !"-".equals(swName.trim())) {
                Integer hit = switchDbidByLowerName.get(swName.trim().toLowerCase(Locale.ROOT));
                if (hit != null) return hit;
            }
            return defaultSwitchDbid;
        }
    }

    public SnapshotRestoreResponse executeRestore(
            SnapshotFileDto snap,
            LoadedCatalog liveCat,
            SnapshotFileDto liveSnap,
            SnapshotRestoreRequest req) throws Exception {
        long startNs = System.nanoTime();
        IConfService service = sessionManager.ensureConnected();

        Map<String, SnapshotRecordDto> snapMap = new LinkedHashMap<>();
        for (SnapshotRecordDto r : snap.records) snapMap.put(r.identityKey, r);
        Map<String, SnapshotRecordDto> liveMap = new LinkedHashMap<>();
        for (SnapshotRecordDto r : liveSnap.records) liveMap.put(r.identityKey, r);

        String snapScope = safe(snap.meta.customerScope);
        boolean isCustomerScoped = !snapScope.isEmpty() && !"전체".equals(snapScope) && !"ALL".equalsIgnoreCase(snapScope);
        Set<String> selectedKeys = new LinkedHashSet<>();
        if (req.identityKeys != null) {
            for (String k : req.identityKeys) {
                if (k != null && !k.trim().isEmpty()) selectedKeys.add(k.trim());
            }
        }
        if (selectedKeys.isEmpty()) {
            throw new IllegalArgumentException("복구할 대상을 1개 이상 선택하세요.");
        }

        List<RestorePlanItem> plan = new ArrayList<>();
        for (String key : selectedKeys) {
            SnapshotRecordDto sRec = snapMap.get(key);
            SnapshotRecordDto lRec = liveMap.get(key);
            RestorePlanItem item = new RestorePlanItem();
            item.key = key;
            item.snap = sRec;
            item.live = lRec;

            if (sRec != null && lRec == null) {
                item.op = OpKind.RECREATE;
                item.sortRank = recreateRank(sRec.category);
                item.pathDepth = pathDepth(sRec.folderPath);
            } else if (sRec != null && lRec != null) {
                Set<String> diffFields = collectChangedFields(sRec, lRec);
                if (diffFields.isEmpty()) {
                    item.op = OpKind.SKIP;
                    item.sortRank = 900;
                } else {
                    item.op = OpKind.UPDATE;
                    item.changedFields = diffFields;
                    item.sortRank = updateRank(sRec.category);
                    item.pathDepth = pathDepth(sRec.folderPath);
                }
            } else if (sRec == null && lRec != null) {
                if (!req.deleteAddedObjects) {
                    item.op = OpKind.SKIP;
                    item.sortRank = 910;
                } else if (isCustomerScoped && !matchesScope(snapScope, lRec.customerInferred, lRec.folderPath)
                        && !("Place Group".equals(lRec.category) && snapScope.equalsIgnoreCase(safe(lRec.name)))) {
                    throw new IllegalStateException(
                            "센터 범위('" + snapScope + "') 스냅샷 복구 중 범위 외 객체 삭제가 차단되었습니다: " + key);
                } else {
                    item.op = OpKind.DELETE;
                    item.sortRank = deleteRank(lRec.category);
                    item.pathDepth = pathDepth(lRec.folderPath);
                }
            } else {
                item.op = OpKind.SKIP;
                item.sortRank = 920;
            }
            plan.add(item);
        }

        plan.sort(Comparator.comparingInt((RestorePlanItem a) -> a.sortRank)
                .thenComparingInt(a -> a.op == OpKind.DELETE ? -a.pathDepth : a.pathDepth)
                .thenComparing(a -> a.key));

        SnapshotRestoreResponse resp = new SnapshotRestoreResponse();
        resp.snapshotId = snap.meta.id;
        resp.dryRun = req.dryRun;
        resp.logs.add(String.format(Locale.ROOT,
                "[스냅샷 복구%s] 기준='%s' (%s) | 선택 %d건 | 신규삭제포함=%s",
                req.dryRun ? " 미리보기" : " 시작",
                safe(snap.meta.label),
                safe(snap.meta.id),
                selectedKeys.size(),
                req.deleteAddedObjects));

        LiveState state = req.dryRun ? null : new LiveState(
                service, liveCat, sessionManager.getTenantDbid(), sessionManager.getSwitchDbid(), sessionManager.getSwitchName());

        for (RestorePlanItem item : plan) {
            SnapshotRecordDto ref = item.snap != null ? item.snap : item.live;
            String catName = ref != null ? safe(ref.category) : "-";
            String natKey = ref != null ? safe(ref.naturalKey) : item.key;
            String path = ref != null ? safe(ref.folderPath) : "";
            String custLabel = ref != null && !safe(ref.customerInferred).isEmpty() ? ref.customerInferred : snapScope;

            SnapshotRestoreItemResultDto resRow = new SnapshotRestoreItemResultDto();
            resRow.identityKey = item.key;
            resRow.category = catName;
            resRow.naturalKey = natKey;
            resRow.operation = item.op.name();
            resRow.folderPath = path;

            if (item.op == OpKind.SKIP) {
                resp.skippedCount++;
                resRow.status = "Skipped";
                if (item.snap != null && item.live != null) {
                    resRow.detail = "이미 스냅샷 상태와 동일";
                } else if (item.live != null && !req.deleteAddedObjects) {
                    resRow.detail = "추가 객체 삭제 옵션 미선택";
                } else {
                    resRow.detail = "스냅샷 및 실서버 모두 미존재";
                }
                resp.results.add(resRow);
                continue;
            }

            if (req.dryRun) {
                resRow.status = "Planned";
                resRow.detail = item.op == OpKind.RECREATE ? "실서버에 재생성 예정"
                        : (item.op == OpKind.DELETE ? "실서버에서 삭제 예정"
                        : ("변경 필드 복원 예정: " + String.join(", ", item.changedFields)));
                if (item.op == OpKind.RECREATE) resp.recreatedCount++;
                else if (item.op == OpKind.UPDATE) resp.updatedCount++;
                else if (item.op == OpKind.DELETE) resp.deletedCount++;
                resp.results.add(resRow);
                continue;
            }

            try {
                String detail;
                if (item.op == OpKind.RECREATE) {
                    detail = recreateObject(state, item.snap);
                    resp.recreatedCount++;
                    resRow.status = "Recreated";
                    resRow.detail = detail;
                    resp.auditEvents.add(AuditEventDto.create(catName, natKey, custLabel, path, "스냅샷 복구(재생성): " + detail));
                    resp.logs.add("  [재생성 완료] " + catName + " '" + natKey + "' (" + detail + ")");
                } else if (item.op == OpKind.UPDATE) {
                    detail = updateObject(state, item.snap, item.live, item.changedFields);
                    if (detail.startsWith("SKIP:")) {
                        resp.skippedCount++;
                        resRow.operation = OpKind.SKIP.name();
                        resRow.status = "Skipped";
                        resRow.detail = detail.substring(5).trim();
                        resp.logs.add("  [건너뜀] " + catName + " '" + natKey + "' (" + resRow.detail + ")");
                    } else {
                        resp.updatedCount++;
                        resRow.status = "Restored";
                        resRow.detail = detail;
                        resp.auditEvents.add(AuditEventDto.update(catName, natKey, custLabel, path, "스냅샷 복구: " + detail));
                        resp.logs.add("  [복원 완료] " + catName + " '" + natKey + "' (" + detail + ")");
                    }
                } else {
                    detail = deleteObject(state, item.live);
                    resp.deletedCount++;
                    resRow.status = "Deleted";
                    resRow.detail = detail;
                    resp.auditEvents.add(AuditEventDto.delete(catName, natKey, custLabel, path, "스냅샷 복구(추가 객체 제거)"));
                    resp.logs.add("  [삭제 완료] " + catName + " '" + natKey + "' (" + path + ")");
                }
            } catch (Exception ex) {
                resp.failedCount++;
                resp.ok = false;
                resRow.status = "Failed";
                resRow.detail = ex.getMessage();
                resp.auditEvents.add(AuditEventDto.error("SnapshotRestore:" + item.op.name(), catName, natKey, custLabel, path, ex.getMessage()));
                resp.logs.add("  [실패] " + catName + " '" + natKey + "' (" + item.op.name() + ") -> " + ex.getMessage());
            }
            resp.results.add(resRow);
        }

        resp.elapsedSeconds = Math.round(((System.nanoTime() - startNs) / 1_000_000_000.0) * 100.0) / 100.0;
        resp.logs.add(String.format(Locale.ROOT,
                "[스냅샷 복구 완료] 재생성 %d건 / 속성복원 %d건 / 삭제 %d건 / 건너뜀 %d건 / 실패 %d건 (%.2f초)",
                resp.recreatedCount, resp.updatedCount, resp.deletedCount, resp.skippedCount, resp.failedCount, resp.elapsedSeconds));
        return resp;
    }

    // ==================== RECREATE ====================
    private String recreateObject(LiveState state, SnapshotRecordDto sRec) throws Exception {
        String cat = sRec.category;
        if ("Folder".equals(cat)) {
            List<String> segs = splitDisplayPath(sRec.folderPath);
            if (segs.size() < 2) {
                throw new IllegalStateException("루트 경로 폴더는 생성할 수 없습니다: " + sRec.folderPath);
            }
            String parentPath = String.join(" > ", segs.subList(0, segs.size() - 1));
            String name = segs.get(segs.size() - 1);
            CfgFolder parent = ensureFolderChain(state, parentPath);
            CfgFolder existing = state.folderIdx.findChild(parent, name);
            if (existing != null) {
                state.registerFolder(existing, sRec.folderPath);
                return "기존 폴더 연결";
            }
            CfgFolder created = folderResolver.createSubFolder(state.service, parent, name);
            state.folderIdx = folderResolver.loadIndex(state.service);
            state.registerFolder(created, sRec.folderPath);
            return "폴더 생성 완료";
        }

        CfgFolder targetFolder = ensureFolderChain(state, sRec.folderPath);
        Integer folderId = targetFolder != null ? targetFolder.getDBID() : null;

        if ("Agent Login".equals(cat)) {
            CfgAgentLogin al = new CfgAgentLogin(state.service);
            al.setTenantDBID(state.tenantDbid);
            al.setSwitchDBID(state.resolveSwitchDbid(sRec.switchName));
            al.setLoginCode(sRec.naturalKey);
            al.setSwitchSpecificType(1);
            al.setUseOverride(resolveFlag(sRec.fields.get("useOverride"), CfgFlag.CFGTrue));
            String ovr = safe(sRec.fields.get("override"));
            if (!ovr.isEmpty()) al.setOverride(ovr);
            if (folderId != null) al.setFolderId(folderId);
            KeyValueCollection kv = parseFlattenedUserProps(sRec.fields.get("userProperties"));
            if (kv != null && kv.size() > 0) al.setUserProperties(kv);
            al.save();
            state.registerLogin(al, sRec.switchName);
            return "Agent Login 생성 (DBID=" + al.getDBID() + ")";
        }

        if (isDnCategory(cat)) {
            CfgDN dn = new CfgDN(state.service);
            dn.setTenantDBID(state.tenantDbid);
            dn.setSwitchDBID(state.resolveSwitchDbid(sRec.switchName));
            dn.setType(resolveDnType(sRec.fields.get("dnType"), cat));
            dn.setNumber(sRec.naturalKey);
            String nm = safe(sRec.fields.get("name"));
            if (!nm.isEmpty() && !nm.equals(sRec.naturalKey)) {
                dn.setName(nm);
            }
            dn.setSwitchSpecificType(parseIntOr(sRec.fields.get("switchSpecificType"), 1));
            dn.setRouteType(resolveRouteType(sRec.fields.get("routeType")));
            dn.setUseOverride(resolveFlag(sRec.fields.get("useOverride"), CfgFlag.CFGTrue));
            String ovr = safe(sRec.fields.get("override"));
            if (!ovr.isEmpty()) dn.setOverride(ovr);
            String assoc = safe(sRec.fields.get("association"));
            if (!assoc.isEmpty()) dn.setAssociation(assoc);
            if ("Agent DN".equals(cat)) {
                dn.setRegisterAll(CfgDNRegisterFlag.CFGDRTrue);
            }
            if (folderId != null) dn.setFolderId(folderId);
            KeyValueCollection kv = parseFlattenedUserProps(sRec.fields.get("userProperties"));
            if (kv != null && kv.size() > 0) dn.setUserProperties(kv);
            dn.save();
            state.registerDn(dn, sRec.switchName);
            return cat + " 생성 (DBID=" + dn.getDBID() + ")";
        }

        if ("Place".equals(cat)) {
            CfgPlace pl = new CfgPlace(state.service);
            pl.setTenantDBID(state.tenantDbid);
            pl.setName(sRec.naturalKey);
            if (folderId != null) pl.setFolderId(folderId);
            List<Integer> dnDbids = resolveDnDbidsCsv(state, sRec.fields.get("dnNumbers"));
            if (!dnDbids.isEmpty()) {
                pl.setDNDBIDs(dnDbids);
            }
            KeyValueCollection kv = parseFlattenedUserProps(sRec.fields.get("userProperties"));
            if (kv != null && kv.size() > 0) pl.setUserProperties(kv);
            pl.save();
            state.registerPlace(pl);
            return dnDbids.isEmpty() ? "Place 생성" : ("Place 생성 (DN " + dnDbids.size() + "개 연결)");
        }

        if ("Person".equals(cat)) {
            CfgPerson p = new CfgPerson(state.service);
            p.setTenantDBID(state.tenantDbid);
            if (folderId != null) p.setFolderId(folderId);
            p.setUserName(safe(sRec.fields.get("userName")).isEmpty() ? sRec.naturalKey : safe(sRec.fields.get("userName")));
            p.setFirstName(safe(sRec.fields.get("firstName")));
            p.setLastName(safe(sRec.fields.get("lastName")));
            p.setEmployeeID(safe(sRec.fields.get("employeeId")));
            boolean isAgent = "true".equalsIgnoreCase(safe(sRec.fields.get("isAgent")));
            p.setIsAgent(isAgent ? CfgFlag.CFGTrue : CfgFlag.CFGFalse);
            List<String> logins = parseCsv(sRec.fields.get("agentLogins"));
            if (isAgent && !logins.isEmpty()) {
                CfgAgentInfo info = new CfgAgentInfo(state.service, p);
                info.setAgentLogins(buildAgentLoginInfoList(state, p, logins));
                p.setAgentInfo(info);
            }
            KeyValueCollection kv = parseFlattenedUserProps(sRec.fields.get("userProperties"));
            if (kv != null && kv.size() > 0) p.setUserProperties(kv);
            p.save();
            state.registerPerson(p);
            return logins.isEmpty() ? "Person 생성" : ("Person 생성 (Login: " + String.join(",", logins) + ")");
        }

        if ("Place Group".equals(cat)) {
            CfgPlaceGroup pg = new CfgPlaceGroup(state.service);
            CfgGroup gi = new CfgGroup(state.service, pg);
            gi.setTenantDBID(state.tenantDbid);
            gi.setName(sRec.naturalKey);
            pg.setGroupInfo(gi);
            if (folderId != null) pg.setFolderId(folderId);
            List<Integer> pDbids = resolvePlaceDbidsCsv(state, sRec.fields.get("places"));
            if (!pDbids.isEmpty()) pg.setPlaceDBIDs(pDbids);
            pg.save();
            state.registerPlaceGroup(pg);
            return "Place Group 생성 (멤버 " + pDbids.size() + "개)";
        }

        if ("Agent Group".equals(cat)) {
            CfgAgentGroup ag = new CfgAgentGroup(state.service);
            CfgGroup gi = new CfgGroup(state.service, ag);
            gi.setTenantDBID(state.tenantDbid);
            gi.setName(sRec.naturalKey);
            ag.setGroupInfo(gi);
            if (folderId != null) ag.setFolderId(folderId);
            List<Integer> aDbids = resolvePersonDbidsCsv(state, sRec.fields.get("agents"));
            if (!aDbids.isEmpty()) ag.setAgentDBIDs(aDbids);
            ag.save();
            state.registerAgentGroup(ag);
            return "Agent Group 생성 (멤버 " + aDbids.size() + "명)";
        }

        if ("DN Group".equals(cat)) {
            CfgDNGroup dg = new CfgDNGroup(state.service);
            CfgGroup gi = new CfgGroup(state.service, dg);
            gi.setTenantDBID(state.tenantDbid);
            gi.setName(sRec.naturalKey);
            dg.setGroupInfo(gi);
            dg.setType(resolveDnGroupType(sRec.fields.get("dnType")));
            if (folderId != null) dg.setFolderId(folderId);
            List<CfgDNInfo> infos = buildDnGroupInfoList(state, dg, sRec.fields.get("dns"));
            if (!infos.isEmpty()) dg.setDNs(infos);
            dg.save();
            state.registerDnGroup(dg);
            return "DN Group 생성 (멤버 " + infos.size() + "개)";
        }

        if ("Script".equals(cat)) {
            CfgScript sc = new CfgScript(state.service);
            sc.setTenantDBID(state.tenantDbid);
            sc.setName(sRec.naturalKey);
            sc.setType(resolveScriptType(sRec.fields.get("scriptType")));
            String idxStr = safe(sRec.fields.get("index"));
            if (!idxStr.isEmpty()) sc.setIndex(parseIntOr(idxStr, 0));
            if (folderId != null) sc.setFolderId(folderId);
            sc.save();
            state.registerScript(sc);
            return "Script 생성";
        }

        throw new UnsupportedOperationException("지원되지 않는 복구 객체 유형: " + cat);
    }

    // ==================== UPDATE ====================
    private String updateObject(
            LiveState state,
            SnapshotRecordDto sRec,
            SnapshotRecordDto lRec,
            Set<String> changedFields) throws Exception {
        String cat = sRec.category;
        Set<String> directChanges = new TreeSet<>(changedFields);
        directChanges.removeAll(DERIVED_READONLY_FIELDS);

        if ("Folder".equals(cat)) {
            CfgFolder folder = findLiveFolder(state, lRec);
            if (folder == null) throw new IllegalStateException("실서버 Folder를 찾을 수 없습니다: " + lRec.folderPath);
            if (directChanges.contains("state")) {
                CfgObjectState st = resolveObjectState(sRec.fields.get("state"));
                if (st != null) folder.setState(st);
            }
            if (directChanges.size() == 1 && directChanges.contains("state")) {
                folder.save();
                return "Folder 상태 복원 (" + sRec.fields.get("state") + ")";
            }
            return "SKIP: Folder 경로/유형 차이는 자동 변경 불가 (" + String.join(", ", directChanges) + ")";
        }

        if ("Agent Login".equals(cat)) {
            if (directChanges.isEmpty() && changedFields.contains("assignedPersons")) {
                CfgAgentLogin al = findLiveLogin(state, lRec);
                if (al != null && isLoginAssignedPersonsMatched(state, al, sRec.fields.get("assignedPersons"))) {
                    return "연관 Person 복원으로 할당 상태 일치";
                }
                return "SKIP: assignedPersons는 연결된 Person 객체의 agentLogins 복원으로 반영됩니다";
            }
            CfgAgentLogin al = findLiveLogin(state, lRec);
            if (al == null) throw new IllegalStateException("실서버 Agent Login을 찾을 수 없습니다: " + lRec.naturalKey);
            if (directChanges.contains("useOverride")) al.setUseOverride(resolveFlag(sRec.fields.get("useOverride"), CfgFlag.CFGTrue));
            if (directChanges.contains("override")) al.setOverride(safe(sRec.fields.get("override")));
            if (directChanges.contains("state")) {
                CfgObjectState st = resolveObjectState(sRec.fields.get("state"));
                if (st != null) al.setState(st);
            }
            if (directChanges.contains("userProperties")) al.setUserProperties(parseFlattenedUserProps(sRec.fields.get("userProperties")));
            al.save();
            return "복원 필드: " + String.join(", ", directChanges);
        }

        if (isDnCategory(cat)) {
            if (directChanges.isEmpty()) {
                return "SKIP: Place/DN Group 등 상위 객체 연결 복원으로 반영됩니다 (" + String.join(", ", changedFields) + ")";
            }
            CfgDN dn = findLiveDn(state, lRec);
            if (dn == null) throw new IllegalStateException("실서버 DN을 찾을 수 없습니다: " + lRec.naturalKey);
            if (directChanges.contains("name")) dn.setName(safe(sRec.fields.get("name")));
            if (directChanges.contains("routeType")) dn.setRouteType(resolveRouteType(sRec.fields.get("routeType")));
            if (directChanges.contains("switchSpecificType")) dn.setSwitchSpecificType(parseIntOr(sRec.fields.get("switchSpecificType"), 1));
            if (directChanges.contains("useOverride")) dn.setUseOverride(resolveFlag(sRec.fields.get("useOverride"), CfgFlag.CFGTrue));
            if (directChanges.contains("override")) dn.setOverride(safe(sRec.fields.get("override")));
            if (directChanges.contains("association")) dn.setAssociation(safe(sRec.fields.get("association")));
            if (directChanges.contains("state")) {
                CfgObjectState st = resolveObjectState(sRec.fields.get("state"));
                if (st != null) dn.setState(st);
            }
            if (directChanges.contains("userProperties")) dn.setUserProperties(parseFlattenedUserProps(sRec.fields.get("userProperties")));
            dn.save();
            return "복원 필드: " + String.join(", ", directChanges);
        }

        if ("Place".equals(cat)) {
            if (directChanges.isEmpty()) {
                return "SKIP: Place Group 등 상위 그룹 복원으로 반영됩니다 (" + String.join(", ", changedFields) + ")";
            }
            CfgPlace pl = findLivePlace(state, lRec);
            if (pl == null) throw new IllegalStateException("실서버 Place를 찾을 수 없습니다: " + lRec.naturalKey);
            if (directChanges.contains("dnNumbers")) {
                pl.setDNDBIDs(resolveDnDbidsCsv(state, sRec.fields.get("dnNumbers")));
            }
            if (directChanges.contains("state")) {
                CfgObjectState st = resolveObjectState(sRec.fields.get("state"));
                if (st != null) pl.setState(st);
            }
            if (directChanges.contains("userProperties")) pl.setUserProperties(parseFlattenedUserProps(sRec.fields.get("userProperties")));
            pl.save();
            return "복원 필드: " + String.join(", ", directChanges);
        }

        if ("Person".equals(cat)) {
            if (directChanges.isEmpty()) {
                return "SKIP: Agent Group 등 상위 그룹 복원으로 반영됩니다 (" + String.join(", ", changedFields) + ")";
            }
            CfgPerson p = findLivePerson(state, lRec);
            if (p == null) throw new IllegalStateException("실서버 Person을 찾을 수 없습니다: " + lRec.naturalKey);
            boolean curIsAgent = (p.getIsAgent() == CfgFlag.CFGTrue);
            if (directChanges.contains("isAgent")) {
                boolean wantIsAgent = "true".equalsIgnoreCase(safe(sRec.fields.get("isAgent")));
                if (wantIsAgent != curIsAgent) {
                    throw new IllegalStateException("기존 Person의 IsAgent 속성은 Genesys Config 정책상 변경할 수 없습니다.");
                }
            }
            if (directChanges.contains("firstName")) p.setFirstName(safe(sRec.fields.get("firstName")));
            if (directChanges.contains("lastName")) p.setLastName(safe(sRec.fields.get("lastName")));
            if (directChanges.contains("employeeId")) p.setEmployeeID(safe(sRec.fields.get("employeeId")));
            if (directChanges.contains("userName")) p.setUserName(safe(sRec.fields.get("userName")));
            if (directChanges.contains("state")) {
                CfgObjectState st = resolveObjectState(sRec.fields.get("state"));
                if (st != null) p.setState(st);
            }
            if (directChanges.contains("agentLogins")) {
                List<String> desired = parseCsv(sRec.fields.get("agentLogins"));
                if (!curIsAgent && !desired.isEmpty()) {
                    throw new IllegalStateException("비상담원(IsAgent=false) 계정에 Agent Login을 복원할 수 없습니다.");
                }
                if (curIsAgent) {
                    CfgAgentInfo info = p.getAgentInfo();
                    if (info == null) {
                        info = new CfgAgentInfo(state.service, p);
                        p.setAgentInfo(info);
                    }
                    info.setAgentLogins(buildAgentLoginInfoList(state, p, desired));
                }
            }
            if (directChanges.contains("userProperties")) p.setUserProperties(parseFlattenedUserProps(sRec.fields.get("userProperties")));
            p.save();
            return "복원 필드: " + String.join(", ", directChanges);
        }

        if ("Place Group".equals(cat)) {
            CfgPlaceGroup pg = findLivePlaceGroup(state, lRec);
            if (pg == null) throw new IllegalStateException("실서버 Place Group을 찾을 수 없습니다: " + lRec.naturalKey);
            if (directChanges.contains("places")) {
                pg.setPlaceDBIDs(resolvePlaceDbidsCsv(state, sRec.fields.get("places")));
            }
            if (directChanges.contains("state") && pg.getGroupInfo() != null) {
                CfgObjectState st = resolveObjectState(sRec.fields.get("state"));
                if (st != null) pg.getGroupInfo().setState(st);
            }
            pg.save();
            return "복원 필드: " + String.join(", ", directChanges);
        }

        if ("Agent Group".equals(cat)) {
            CfgAgentGroup ag = findLiveAgentGroup(state, lRec);
            if (ag == null) throw new IllegalStateException("실서버 Agent Group을 찾을 수 없습니다: " + lRec.naturalKey);
            if (directChanges.contains("agents")) {
                ag.setAgentDBIDs(resolvePersonDbidsCsv(state, sRec.fields.get("agents")));
            }
            if (directChanges.contains("state") && ag.getGroupInfo() != null) {
                CfgObjectState st = resolveObjectState(sRec.fields.get("state"));
                if (st != null) ag.getGroupInfo().setState(st);
            }
            ag.save();
            return "복원 필드: " + String.join(", ", directChanges);
        }

        if ("DN Group".equals(cat)) {
            CfgDNGroup dg = findLiveDnGroup(state, lRec);
            if (dg == null) throw new IllegalStateException("실서버 DN Group을 찾을 수 없습니다: " + lRec.naturalKey);
            if (directChanges.contains("dns")) {
                dg.setDNs(buildDnGroupInfoList(state, dg, sRec.fields.get("dns")));
            }
            if (directChanges.contains("dnType")) {
                dg.setType(resolveDnGroupType(sRec.fields.get("dnType")));
            }
            if (directChanges.contains("state") && dg.getGroupInfo() != null) {
                CfgObjectState st = resolveObjectState(sRec.fields.get("state"));
                if (st != null) dg.getGroupInfo().setState(st);
            }
            dg.save();
            return "복원 필드: " + String.join(", ", directChanges);
        }

        if ("Script".equals(cat)) {
            CfgScript sc = findLiveScript(state, lRec);
            if (sc == null) throw new IllegalStateException("실서버 Script를 찾을 수 없습니다: " + lRec.naturalKey);
            if (directChanges.contains("scriptType")) sc.setType(resolveScriptType(sRec.fields.get("scriptType")));
            if (directChanges.contains("index")) sc.setIndex(parseIntOr(sRec.fields.get("index"), 0));
            if (directChanges.contains("state")) {
                CfgObjectState st = resolveObjectState(sRec.fields.get("state"));
                if (st != null) sc.setState(st);
            }
            sc.save();
            return "복원 필드: " + String.join(", ", directChanges);
        }

        throw new UnsupportedOperationException("지원되지 않는 복구 객체 유형: " + cat);
    }

    // ==================== DELETE ====================
    private String deleteObject(LiveState state, SnapshotRecordDto lRec) throws Exception {
        String cat = lRec.category;
        if ("Folder".equals(cat)) {
            CfgFolder f = findLiveFolder(state, lRec);
            if (f == null) throw new IllegalStateException("실서버 Folder를 찾을 수 없습니다: " + lRec.folderPath);
            Integer dbid = f.getDBID();
            f.delete();
            state.folderByDbid.remove(dbid);
            state.folderByLowerPath.remove(safe(lRec.folderPath).toLowerCase(Locale.ROOT));
            return "Folder 삭제 (DBID=" + dbid + ")";
        }
        if ("Person".equals(cat)) {
            CfgPerson p = findLivePerson(state, lRec);
            if (p == null) throw new IllegalStateException("실서버 Person을 찾을 수 없습니다: " + lRec.naturalKey);
            Integer dbid = p.getDBID();
            p.delete();
            state.personByDbid.remove(dbid);
            state.personByLowerUser.remove(safe(lRec.naturalKey).toLowerCase(Locale.ROOT));
            return "Person 삭제 (DBID=" + dbid + ")";
        }
        if ("Place".equals(cat)) {
            CfgPlace pl = findLivePlace(state, lRec);
            if (pl == null) throw new IllegalStateException("실서버 Place를 찾을 수 없습니다: " + lRec.naturalKey);
            Integer dbid = pl.getDBID();
            pl.delete();
            state.placeByDbid.remove(dbid);
            state.placeByLowerName.remove(safe(lRec.naturalKey).toLowerCase(Locale.ROOT));
            return "Place 삭제 (DBID=" + dbid + ")";
        }
        if (isDnCategory(cat)) {
            CfgDN dn = findLiveDn(state, lRec);
            if (dn == null) throw new IllegalStateException("실서버 DN을 찾을 수 없습니다: " + lRec.naturalKey);
            Integer dbid = dn.getDBID();
            dn.delete();
            state.dnByDbid.remove(dbid);
            return cat + " 삭제 (DBID=" + dbid + ")";
        }
        if ("Agent Login".equals(cat)) {
            CfgAgentLogin al = findLiveLogin(state, lRec);
            if (al == null) throw new IllegalStateException("실서버 Agent Login을 찾을 수 없습니다: " + lRec.naturalKey);
            Integer dbid = al.getDBID();
            al.delete();
            state.loginByDbid.remove(dbid);
            return "Agent Login 삭제 (DBID=" + dbid + ")";
        }
        if ("Place Group".equals(cat)) {
            CfgPlaceGroup pg = findLivePlaceGroup(state, lRec);
            if (pg == null) throw new IllegalStateException("실서버 Place Group을 찾을 수 없습니다: " + lRec.naturalKey);
            Integer dbid = pg.getDBID();
            pg.delete();
            state.pgByDbid.remove(dbid);
            return "Place Group 삭제 (DBID=" + dbid + ")";
        }
        if ("Agent Group".equals(cat)) {
            CfgAgentGroup ag = findLiveAgentGroup(state, lRec);
            if (ag == null) throw new IllegalStateException("실서버 Agent Group을 찾을 수 없습니다: " + lRec.naturalKey);
            Integer dbid = ag.getDBID();
            ag.delete();
            state.agByDbid.remove(dbid);
            return "Agent Group 삭제 (DBID=" + dbid + ")";
        }
        if ("DN Group".equals(cat)) {
            CfgDNGroup dg = findLiveDnGroup(state, lRec);
            if (dg == null) throw new IllegalStateException("실서버 DN Group을 찾을 수 없습니다: " + lRec.naturalKey);
            Integer dbid = dg.getDBID();
            dg.delete();
            state.dgByDbid.remove(dbid);
            return "DN Group 삭제 (DBID=" + dbid + ")";
        }
        if ("Script".equals(cat)) {
            CfgScript sc = findLiveScript(state, lRec);
            if (sc == null) throw new IllegalStateException("실서버 Script를 찾을 수 없습니다: " + lRec.naturalKey);
            Integer dbid = sc.getDBID();
            sc.delete();
            state.scriptByDbid.remove(dbid);
            return "Script 삭제 (DBID=" + dbid + ")";
        }
        throw new UnsupportedOperationException("지원되지 않는 삭제 객체 유형: " + cat);
    }

    // ==================== HELPERS ====================
    private CfgFolder ensureFolderChain(LiveState state, String displayPath) throws Exception {
        String clean = safe(displayPath);
        if (clean.isEmpty()) return null;
        CfgFolder direct = state.folderByLowerPath.get(clean.toLowerCase(Locale.ROOT));
        if (direct != null) return direct;

        List<String> segs = splitDisplayPath(clean);
        if (segs.isEmpty()) return null;

        CfgFolder cur = null;
        StringBuilder pathAcc = new StringBuilder();
        for (int i = 0; i < segs.size(); i++) {
            String seg = segs.get(i);
            if (i > 0) pathAcc.append(" > ");
            pathAcc.append(seg);
            String curPath = pathAcc.toString();
            CfgFolder hit = state.folderByLowerPath.get(curPath.toLowerCase(Locale.ROOT));
            if (hit == null && cur != null) {
                hit = state.folderIdx.findChild(cur, seg);
            }
            if (hit == null) {
                if (cur == null) {
                    throw new IllegalStateException("상위 루트 폴더를 찾을 수 없습니다: " + curPath);
                }
                hit = folderResolver.createSubFolder(state.service, cur, seg);
                state.folderIdx = folderResolver.loadIndex(state.service);
                state.registerFolder(hit, curPath);
            }
            cur = hit;
        }
        return cur;
    }

    private CfgFolder findLiveFolder(LiveState state, SnapshotRecordDto rec) {
        if (rec.dbid != null && state.folderByDbid.containsKey(rec.dbid)) {
            return state.folderByDbid.get(rec.dbid);
        }
        return state.folderByLowerPath.get(safe(rec.folderPath).toLowerCase(Locale.ROOT));
    }

    private CfgDN findLiveDn(LiveState state, SnapshotRecordDto rec) {
        if (rec.dbid != null && state.dnByDbid.containsKey(rec.dbid)) {
            CfgDN hit = state.dnByDbid.get(rec.dbid);
            if (safe(hit.getNumber()).equals(rec.naturalKey)) return hit;
        }
        List<CfgDN> list = state.dnsByCategorySwitchNum.get(dnKey(rec.category, rec.switchName, rec.naturalKey));
        if (list != null && !list.isEmpty()) return list.get(0);
        List<CfgDN> byNum = state.dnsByNum.get(rec.naturalKey);
        if (byNum != null) {
            for (CfgDN d : byNum) {
                if (rec.category.equals(CmCatalogSnapshotLoader.mapDnCategory(d.getType()))) return d;
            }
        }
        return null;
    }

    private CfgPlace findLivePlace(LiveState state, SnapshotRecordDto rec) {
        if (rec.dbid != null && state.placeByDbid.containsKey(rec.dbid)) {
            CfgPlace hit = state.placeByDbid.get(rec.dbid);
            if (safe(hit.getName()).equalsIgnoreCase(rec.naturalKey)) return hit;
        }
        return state.placeByLowerName.get(safe(rec.naturalKey).toLowerCase(Locale.ROOT));
    }

    private CfgAgentLogin findLiveLogin(LiveState state, SnapshotRecordDto rec) {
        if (rec.dbid != null && state.loginByDbid.containsKey(rec.dbid)) {
            CfgAgentLogin hit = state.loginByDbid.get(rec.dbid);
            if (safe(hit.getLoginCode()).equals(rec.naturalKey)) return hit;
        }
        CfgAgentLogin bySw = state.loginBySwitchCode.get(safe(rec.switchName).toLowerCase(Locale.ROOT) + "|" + rec.naturalKey);
        return bySw != null ? bySw : state.loginByCode.get(rec.naturalKey);
    }

    private CfgPerson findLivePerson(LiveState state, SnapshotRecordDto rec) {
        if (rec.dbid != null && state.personByDbid.containsKey(rec.dbid)) {
            CfgPerson hit = state.personByDbid.get(rec.dbid);
            if (safe(hit.getUserName()).equalsIgnoreCase(rec.naturalKey)) return hit;
        }
        return state.personByLowerUser.get(safe(rec.naturalKey).toLowerCase(Locale.ROOT));
    }

    private CfgPlaceGroup findLivePlaceGroup(LiveState state, SnapshotRecordDto rec) {
        if (rec.dbid != null && state.pgByDbid.containsKey(rec.dbid)) return state.pgByDbid.get(rec.dbid);
        return state.pgByLowerName.get(safe(rec.naturalKey).toLowerCase(Locale.ROOT));
    }

    private CfgAgentGroup findLiveAgentGroup(LiveState state, SnapshotRecordDto rec) {
        if (rec.dbid != null && state.agByDbid.containsKey(rec.dbid)) return state.agByDbid.get(rec.dbid);
        return state.agByLowerName.get(safe(rec.naturalKey).toLowerCase(Locale.ROOT));
    }

    private CfgDNGroup findLiveDnGroup(LiveState state, SnapshotRecordDto rec) {
        if (rec.dbid != null && state.dgByDbid.containsKey(rec.dbid)) return state.dgByDbid.get(rec.dbid);
        return state.dgByLowerName.get(safe(rec.naturalKey).toLowerCase(Locale.ROOT));
    }

    private CfgScript findLiveScript(LiveState state, SnapshotRecordDto rec) {
        if (rec.dbid != null && state.scriptByDbid.containsKey(rec.dbid)) return state.scriptByDbid.get(rec.dbid);
        return state.scriptByLowerName.get(safe(rec.naturalKey).toLowerCase(Locale.ROOT));
    }

    private boolean isLoginAssignedPersonsMatched(LiveState state, CfgAgentLogin al, String expectedCsv) {
        if (al == null || al.getDBID() == null) return false;
        Set<String> expected = new TreeSet<>(parseCsv(expectedCsv));
        Set<String> actual = new TreeSet<>();
        for (CfgPerson p : state.personByDbid.values()) {
            if (p.getAgentInfo() == null || p.getAgentInfo().getAgentLogins() == null) continue;
            for (CfgAgentLoginInfo ali : p.getAgentInfo().getAgentLogins()) {
                if (al.getDBID().equals(ali.getAgentLoginDBID())) {
                    actual.add(safe(p.getUserName()));
                }
            }
        }
        return expected.equals(actual);
    }

    private List<Integer> resolveDnDbidsCsv(LiveState state, String csv) {
        List<Integer> out = new ArrayList<>();
        for (String token : parseCsv(csv)) {
            if (token.startsWith("DBID#")) {
                out.add(parseIntOr(token.substring(5), 0));
                continue;
            }
            List<CfgDN> hits = state.dnsByNum.get(token);
            if (hits == null || hits.isEmpty()) {
                throw new IllegalStateException("연결할 DN을 실서버에서 찾을 수 없습니다: " + token);
            }
            CfgDN chosen = hits.get(0);
            for (CfgDN cand : hits) {
                if ("Agent DN".equals(CmCatalogSnapshotLoader.mapDnCategory(cand.getType()))) {
                    chosen = cand;
                    break;
                }
            }
            if (chosen.getDBID() != null) out.add(chosen.getDBID());
        }
        return out;
    }

    private List<CfgAgentLoginInfo> buildAgentLoginInfoList(LiveState state, CfgPerson owner, List<String> codes) {
        List<CfgAgentLoginInfo> out = new ArrayList<>();
        for (String code : codes) {
            CfgAgentLogin al = state.loginByCode.get(code);
            if (al == null || al.getDBID() == null) {
                throw new IllegalStateException("연결할 Agent Login을 실서버에서 찾을 수 없습니다: " + code);
            }
            CfgAgentLoginInfo ali = new CfgAgentLoginInfo(state.service, owner);
            ali.setAgentLoginDBID(al.getDBID());
            out.add(ali);
        }
        return out;
    }

    private List<Integer> resolvePlaceDbidsCsv(LiveState state, String csv) {
        List<Integer> out = new ArrayList<>();
        for (String name : parseCsv(csv)) {
            CfgPlace pl = state.placeByLowerName.get(name.toLowerCase(Locale.ROOT));
            if (pl == null || pl.getDBID() == null) {
                throw new IllegalStateException("연결할 Place를 실서버에서 찾을 수 없습니다: " + name);
            }
            out.add(pl.getDBID());
        }
        return out;
    }

    private List<Integer> resolvePersonDbidsCsv(LiveState state, String csv) {
        List<Integer> out = new ArrayList<>();
        for (String un : parseCsv(csv)) {
            CfgPerson p = state.personByLowerUser.get(un.toLowerCase(Locale.ROOT));
            if (p == null || p.getDBID() == null) {
                throw new IllegalStateException("연결할 Person을 실서버에서 찾을 수 없습니다: " + un);
            }
            out.add(p.getDBID());
        }
        return out;
    }

    private List<CfgDNInfo> buildDnGroupInfoList(LiveState state, CfgDNGroup dg, String csv) {
        List<CfgDNInfo> out = new ArrayList<>();
        for (Integer dnDbid : resolveDnDbidsCsv(state, csv)) {
            CfgDNInfo info = new CfgDNInfo(state.service, dg);
            info.setDNDBID(dnDbid);
            out.add(info);
        }
        return out;
    }

    private static Set<String> collectChangedFields(SnapshotRecordDto sRec, SnapshotRecordDto lRec) {
        Set<String> all = new TreeSet<>(sRec.fields.keySet());
        all.addAll(lRec.fields.keySet());
        Set<String> changed = new TreeSet<>();
        for (String fn : all) {
            if ("dbid".equals(fn)) continue;
            String sv = safe(sRec.fields.get(fn));
            String lv = safe(lRec.fields.get(fn));
            if (!sv.equals(lv)) {
                changed.add(fn);
            }
        }
        return changed;
    }

    private static boolean matchesScope(String customerScope, String inferredCustomer, String path) {
        if (customerScope == null || customerScope.isEmpty()) return true;
        if (inferredCustomer != null && inferredCustomer.equalsIgnoreCase(customerScope)) return true;
        return path != null && path.toLowerCase(Locale.ROOT).contains(customerScope.toLowerCase(Locale.ROOT));
    }

    private static int recreateRank(String cat) {
        if ("Folder".equals(cat)) return 10;
        if ("Agent Login".equals(cat)) return 20;
        if (isDnCategory(cat)) return 30;
        if ("Place".equals(cat)) return 40;
        if ("Person".equals(cat)) return 50;
        return 60;
    }

    private static int updateRank(String cat) {
        if ("Folder".equals(cat)) return 100;
        if (isDnCategory(cat)) return 110;
        if ("Place".equals(cat)) return 120;
        if ("Person".equals(cat)) return 130;
        if ("Agent Login".equals(cat)) return 140;
        return 150;
    }

    private static int deleteRank(String cat) {
        if ("Place Group".equals(cat) || "Agent Group".equals(cat) || "DN Group".equals(cat) || "Script".equals(cat)) return 200;
        if ("Person".equals(cat)) return 210;
        if ("Place".equals(cat)) return 220;
        if (isDnCategory(cat)) return 230;
        if ("Agent Login".equals(cat)) return 240;
        return 250;
    }

    private static boolean isDnCategory(String cat) {
        return "Agent DN".equals(cat) || "ACD Position".equals(cat)
                || "RSI".equals(cat) || "VQueue".equals(cat) || "DN".equals(cat);
    }

    private static int pathDepth(String path) {
        return splitDisplayPath(path).size();
    }

    private static List<String> splitDisplayPath(String path) {
        List<String> segs = new ArrayList<>();
        if (path == null) return segs;
        for (String s : path.split(" > ")) {
            if (s != null && !s.trim().isEmpty()) segs.add(s.trim());
        }
        return segs;
    }

    private static List<String> parseCsv(String csv) {
        List<String> out = new ArrayList<>();
        if (csv == null || csv.trim().isEmpty()) return out;
        for (String part : csv.split(",")) {
            String t = part.trim();
            if (!t.isEmpty()) out.add(t);
        }
        return out;
    }

    private static KeyValueCollection parseFlattenedUserProps(String flat) {
        KeyValueCollection kv = new KeyValueCollection();
        if (flat == null || flat.trim().isEmpty()) return kv;
        List<String> tokens = splitTopLevel(flat.trim(), ';');
        for (String tok : tokens) {
            int eq = tok.indexOf('=');
            if (eq <= 0) continue;
            String key = tok.substring(0, eq).trim();
            String val = tok.substring(eq + 1).trim();
            if (key.isEmpty()) continue;
            if (val.startsWith("[") && val.endsWith("]")) {
                kv.addList(key, parseFlattenedUserProps(val.substring(1, val.length() - 1)));
            } else {
                kv.addObject(key, val);
            }
        }
        return kv;
    }

    private static List<String> splitTopLevel(String s, char sep) {
        List<String> out = new ArrayList<>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            if (c == '[') depth++;
            else if (c == ']' && depth > 0) depth--;
            else if (c == sep && depth == 0) {
                String part = s.substring(start, i).trim();
                if (!part.isEmpty()) out.add(part);
                start = i + 1;
            }
        }
        String tail = s.substring(start).trim();
        if (!tail.isEmpty()) out.add(tail);
        return out;
    }

    private static String dnKey(String cat, String swName, String num) {
        return safe(cat) + "|" + safe(swName).toLowerCase(Locale.ROOT) + "|" + safe(num);
    }

    private static CfgDNType resolveDnType(String dnTypeName, String cat) {
        if (dnTypeName != null && !dnTypeName.trim().isEmpty()) {
            try { return CfgDNType.valueOf(dnTypeName.trim()); } catch (Exception ignored) {}
        }
        if ("Agent DN".equals(cat)) return CfgDNType.CFGExtension;
        if ("ACD Position".equals(cat)) return CfgDNType.CFGACDPosition;
        if ("RSI".equals(cat)) return CfgDNType.CFGRoutingPoint;
        if ("VQueue".equals(cat)) return CfgDNType.CFGVirtACDQueue;
        return CfgDNType.CFGExtension;
    }

    private static CfgRouteType resolveRouteType(String raw) {
        if (raw != null && !raw.trim().isEmpty()) {
            try { return CfgRouteType.valueOf(raw.trim()); } catch (Exception ignored) {}
        }
        return CfgRouteType.CFGDefault;
    }

    private static CfgFlag resolveFlag(String raw, CfgFlag fallback) {
        if (raw != null && !raw.trim().isEmpty()) {
            try { return CfgFlag.valueOf(raw.trim()); } catch (Exception ignored) {}
        }
        return fallback;
    }

    private static CfgObjectState resolveObjectState(String raw) {
        if (raw != null && !raw.trim().isEmpty()) {
            try { return CfgObjectState.valueOf(raw.trim()); } catch (Exception ignored) {}
        }
        return null;
    }

    private static CfgDNGroupType resolveDnGroupType(String raw) {
        if (raw != null && !raw.trim().isEmpty()) {
            try { return CfgDNGroupType.valueOf(raw.trim()); } catch (Exception ignored) {}
        }
        return CfgDNGroupType.CFGACDQueues;
    }

    private static CfgScriptType resolveScriptType(String raw) {
        if (raw != null && !raw.trim().isEmpty()) {
            try { return CfgScriptType.valueOf(raw.trim()); } catch (Exception ignored) {}
        }
        return CfgScriptType.CFGEnhancedRouting;
    }

    private static int parseIntOr(String raw, int def) {
        try { return Integer.parseInt(safe(raw)); } catch (Exception ex) { return def; }
    }

    private static String safe(String s) {
        return s == null ? "" : s.trim();
    }
}
