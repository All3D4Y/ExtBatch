package com.gcti.extbatch.service;

import com.genesyslab.platform.applicationblocks.com.IConfService;
import com.genesyslab.platform.applicationblocks.com.objects.CfgFolder;
import com.genesyslab.platform.applicationblocks.com.objects.CfgGroup;
import com.genesyslab.platform.applicationblocks.com.objects.CfgObjectID;
import com.genesyslab.platform.applicationblocks.com.objects.CfgPlaceGroup;
import com.genesyslab.platform.applicationblocks.com.queries.CfgPlaceGroupQuery;
import com.gcti.extbatch.dto.CenterDtos.CenterRequest;
import com.gcti.extbatch.dto.CenterDtos.CmCenterApplyResponse;
import com.gcti.extbatch.dto.CenterDtos.CmCenterCheckResponse;
import com.gcti.extbatch.dto.CenterDtos.CmCenterCreateResponse;
import com.gcti.extbatch.dto.CenterDtos.CmCenterDeleteResponse;
import com.gcti.extbatch.dto.CenterDtos.CmCenterItemResultDto;
import com.gcti.extbatch.dto.CenterDtos.CmCenterStatusResponse;
import com.gcti.extbatch.dto.CommonDtos.AuditEventDto;
import com.gcti.extbatch.psdk.CmFolderResolver;
import com.gcti.extbatch.psdk.CmFolderResolver.FolderIndex;
import com.gcti.extbatch.psdk.GenesysSessionManager;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collection;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import org.springframework.stereotype.Service;

@Service
public class CenterProvisionService {

    private static class TargetSpec {
        final String key;
        final String label;
        final String kind; // Folder | PlaceGroup
        final boolean usesLocation;
        final String condition; // Always | VQueue | IVR
        final List<String> base;

        TargetSpec(String key, String label, String kind, boolean usesLocation, String condition, List<String> base) {
            this.key = key;
            this.label = label;
            this.kind = kind;
            this.usesLocation = usesLocation;
            this.condition = condition;
            this.base = base;
        }
    }

    private final GenesysSessionManager sessionManager;
    private final CmFolderResolver folderResolver;

    public CenterProvisionService(GenesysSessionManager sessionManager, CmFolderResolver folderResolver) {
        this.sessionManager = sessionManager;
        this.folderResolver = folderResolver;
    }

    private List<TargetSpec> getTargetSpecs() {
        String sw = sessionManager.getSwitchName();
        return Arrays.asList(
                new TargetSpec("Persons",     "Persons 폴더",                "Folder",     true,  "Always", Arrays.asList("Configuration", "Environment", "Persons")),
                new TargetSpec("PlaceAgent",  "Places (AGENT) 폴더",         "Folder",     true,  "Always", Arrays.asList("Configuration", "Environment", "Places", "AGENT")),
                new TargetSpec("AgentLogin",  "Agent Logins 폴더",           "Folder",     true,  "Always", Arrays.asList("Configuration", "Environment", "Switches", sw, "Agent Logins")),
                new TargetSpec("AcdPos",      "ACDPosition 폴더",            "Folder",     true,  "Always", Arrays.asList("Configuration", "Environment", "Switches", sw, "DNs", "ACDPosition")),
                new TargetSpec("AgentDn",     "AGENT_DN 폴더",               "Folder",     true,  "Always", Arrays.asList("Configuration", "Environment", "Switches", sw, "DNs", "AGENT_DN")),
                new TargetSpec("Rsi",         "RSI 폴더",                    "Folder",     true,  "Always", Arrays.asList("Configuration", "Environment", "Switches", sw, "DNs", "RSI")),
                new TargetSpec("AgentGroups", "Agent Groups 폴더",           "Folder",     false, "Always", Arrays.asList("Configuration", "Environment", "Agent Groups")),
                new TargetSpec("DnGroups",    "DN Groups 폴더",              "Folder",     false, "Always", Arrays.asList("Configuration", "Environment", "DN Groups")),
                new TargetSpec("Scripts",     "Scripts (유세스파트너스) 폴더", "Folder",     false, "Always", Arrays.asList("Configuration", "Environment", "Scripts", "유세스파트너스")),
                new TargetSpec("VQueue",      "VQueue 폴더",                 "Folder",     false, "VQueue", Arrays.asList("Configuration", "Environment", "Switches", sw, "DNs", "VQueue")),
                new TargetSpec("PlaceGroups", "Place Group 오브젝트",        "PlaceGroup", false, "IVR",    Arrays.asList("Configuration", "Environment", "Place Groups"))
        );
    }

    public CmCenterCheckResponse checkCenter(CenterRequest req) throws Exception {
        IConfService service = sessionManager.ensureConnected();
        String cname = safeTrim(req.centerName);
        if (cname.isEmpty()) throw new IllegalArgumentException("센터 이름을 입력하세요.");
        String loc = safeTrim(req.location);
        boolean useLoc = isEffectiveLocation(loc);

        FolderIndex folderIdx = folderResolver.loadIndex(service);
        Map<String, CfgPlaceGroup> pgByName = loadPlaceGroupsByName(service);

        CmCenterCheckResponse resp = new CmCenterCheckResponse();
        resp.centerName = cname;
        resp.location = loc;
        resp.useVQueue = req.useVQueue;
        resp.useIvr = req.useIvr;
        resp.logs.add(String.format(Locale.ROOT, "[사전 점검] 센터='%s', 위치='%s', VQueue=%s, IVR=%s",
                cname, useLoc ? loc : "없음", req.useVQueue, req.useIvr));

        for (TargetSpec sp : getTargetSpecs()) {
            if ("VQueue".equals(sp.condition) && !req.useVQueue) continue;
            if ("IVR".equals(sp.condition) && !req.useIvr) continue;

            List<String> fullSegs = resolveFullSegments(sp, cname, loc);
            String dispPath = String.join(" > ", fullSegs);
            CfgFolder baseFolder = folderIdx.findBySegments(sp.base);

            CmCenterItemResultDto item = new CmCenterItemResultDto();
            item.key = sp.key;
            item.label = sp.label;
            item.kind = sp.kind;
            item.path = dispPath;

            if (baseFolder == null) {
                item.status = "ParentMissing";
                item.detail = "Config 기본 상위 경로 없음: " + String.join(" > ", sp.base);
                resp.logs.add("  [오류] " + sp.label + ": 기본 경로 없음 (" + String.join(" > ", sp.base) + ")");
                resp.items.add(item);
                continue;
            }

            if ("PlaceGroup".equals(sp.kind)) {
                boolean exists = pgByName.containsKey(cname.toLowerCase(Locale.ROOT));
                item.status = exists ? "Exists" : "WillCreate";
                item.detail = exists ? "Config에 이미 존재 (생성 시 건너뜀)" : "신규 생성 가능";
                resp.logs.add((exists ? "  [이미 존재] " : "  [생성 가능] ") + dispPath);
            } else {
                CfgFolder parentFolder = baseFolder;
                boolean locWillCreate = false;
                if (sp.usesLocation && useLoc) {
                    CfgFolder locFolder = folderIdx.findChild(baseFolder, loc);
                    if (locFolder == null) {
                        locWillCreate = true;
                        parentFolder = null;
                    } else {
                        parentFolder = locFolder;
                    }
                }
                if (locWillCreate) {
                    item.status = "WillCreateWithLocation";
                    item.detail = "위치 폴더 '" + loc + "' 포함 신규 생성 예정";
                    resp.logs.add("  [생성 예정] " + dispPath + " (위치 폴더 '" + loc + "' 함께 생성)");
                } else {
                    CfgFolder child = folderIdx.findChild(parentFolder, cname);
                    if (child != null) {
                        item.status = "Exists";
                        item.detail = "Config에 이미 존재 (생성 시 건너뜀)";
                        resp.logs.add("  [이미 존재] " + dispPath);
                    } else {
                        item.status = "WillCreate";
                        item.detail = "신규 생성 가능";
                        resp.logs.add("  [생성 가능] " + dispPath);
                    }
                }
            }
            resp.items.add(item);
        }
        return resp;
    }

    public CmCenterCreateResponse createCenter(CenterRequest req) throws Exception {
        long startNs = System.nanoTime();
        IConfService service = sessionManager.ensureConnected();
        String cname = safeTrim(req.centerName);
        if (cname.isEmpty()) throw new IllegalArgumentException("센터 이름을 입력하세요.");
        String loc = safeTrim(req.location);
        boolean useLoc = isEffectiveLocation(loc);

        FolderIndex folderIdx = folderResolver.loadIndex(service);
        Map<String, CfgPlaceGroup> pgByName = loadPlaceGroupsByName(service);

        CmCenterCreateResponse resp = new CmCenterCreateResponse();
        resp.centerName = cname;
        resp.location = loc;
        resp.useVQueue = req.useVQueue;
        resp.useIvr = req.useIvr;
        resp.logs.add(String.format(Locale.ROOT, "[센터 Config 생성 시작] 센터='%s' | 위치='%s' | VQueue=%s | IVR=%s",
                cname, useLoc ? loc : "없음", req.useVQueue, req.useIvr));

        for (TargetSpec sp : getTargetSpecs()) {
            if ("VQueue".equals(sp.condition) && !req.useVQueue) continue;
            if ("IVR".equals(sp.condition) && !req.useIvr) continue;

            List<String> fullSegs = resolveFullSegments(sp, cname, loc);
            String dispPath = String.join(" > ", fullSegs);
            String objType = "PlaceGroup".equals(sp.kind) ? "Place Group" : "Folder";

            CmCenterItemResultDto item = new CmCenterItemResultDto();
            item.key = sp.key;
            item.label = sp.label;
            item.kind = sp.kind;
            item.path = dispPath;

            try {
                CfgFolder baseFolder = folderIdx.findBySegments(sp.base);
                if (baseFolder == null) {
                    throw new IllegalStateException("Config 기본 상위 경로를 찾을 수 없습니다: " + String.join(" > ", sp.base));
                }

                if ("PlaceGroup".equals(sp.kind)) {
                    if (pgByName.containsKey(cname.toLowerCase(Locale.ROOT))) {
                        resp.skippedCount++;
                        item.status = "Skipped";
                        item.detail = "이미 존재함";
                        resp.logs.add("  [건너뜀] " + sp.label + " -> 이미 존재 (" + dispPath + ")");
                    } else {
                        CfgPlaceGroup pg = new CfgPlaceGroup(service);
                        CfgGroup gi = new CfgGroup(service, pg);
                        gi.setTenantDBID(sessionManager.getTenantDbid());
                        gi.setName(cname);
                        pg.setGroupInfo(gi);
                        pg.setFolderId(baseFolder.getDBID());
                        pg.save();
                        pgByName.put(cname.toLowerCase(Locale.ROOT), pg);

                        resp.createdCount++;
                        item.status = "Created";
                        item.detail = "생성 완료";
                        resp.logs.add("  [생성 완료] " + sp.label + " -> " + dispPath);
                        resp.auditEvents.add(AuditEventDto.create(objType, cname, cname, dispPath, null));
                    }
                } else {
                    CfgFolder parentFolder = baseFolder;
                    if (sp.usesLocation && useLoc) {
                        CfgFolder locFolder = folderIdx.findChild(baseFolder, loc);
                        if (locFolder == null) {
                            String locPath = String.join(" > ", sp.base) + " > " + loc;
                            resp.logs.add("  [위치 폴더 생성] " + locPath);
                            locFolder = folderResolver.createSubFolder(service, baseFolder, loc);
                            resp.auditEvents.add(AuditEventDto.create("Folder", loc, cname, locPath, "상위 위치 폴더 생성"));
                            folderIdx = folderResolver.loadIndex(service);
                            locFolder = folderIdx.findChild(folderIdx.findBySegments(sp.base), loc);
                            if (locFolder == null) {
                                throw new IllegalStateException("위치 폴더 '" + loc + "' 생성 후 재조회 실패");
                            }
                        }
                        parentFolder = locFolder;
                    }

                    CfgFolder existing = folderIdx.findChild(parentFolder, cname);
                    if (existing != null) {
                        resp.skippedCount++;
                        item.status = "Skipped";
                        item.detail = "이미 존재함";
                        resp.logs.add("  [건너뜀] " + sp.label + " -> 이미 존재 (" + dispPath + ")");
                    } else {
                        folderResolver.createSubFolder(service, parentFolder, cname);
                        folderIdx = folderResolver.loadIndex(service);
                        resp.createdCount++;
                        item.status = "Created";
                        item.detail = "생성 완료";
                        resp.logs.add("  [생성 완료] " + sp.label + " -> " + dispPath);
                        resp.auditEvents.add(AuditEventDto.create(objType, cname, cname, dispPath, null));
                    }
                }
            } catch (Exception ex) {
                resp.failedCount++;
                item.status = "Failed";
                item.detail = ex.getMessage();
                resp.logs.add("  [실패] " + sp.label + " (" + dispPath + ") -> " + ex.getMessage());
                resp.auditEvents.add(AuditEventDto.error("CreateCenter", objType, cname, cname, dispPath, ex.getMessage()));
            }
            resp.items.add(item);
        }

        resp.elapsedSeconds = round2((System.nanoTime() - startNs) / 1_000_000_000.0);
        resp.logs.add(String.format(Locale.ROOT, "[센터 Config 생성 완료] 생성 %d건 / 건너뜀 %d건 / 실패 %d건 (%.1f초)",
                resp.createdCount, resp.skippedCount, resp.failedCount, resp.elapsedSeconds));
        return resp;
    }

    public CmCenterStatusResponse checkCenterOptions(String centerName) throws Exception {
        IConfService service = sessionManager.ensureConnected();
        String cname = safeTrim(centerName);
        if (cname.isEmpty()) throw new IllegalArgumentException("센터 이름이 비어 있습니다.");

        FolderIndex folderIdx = folderResolver.loadIndex(service);
        Map<String, CfgPlaceGroup> pgByName = loadPlaceGroupsByName(service);

        TargetSpec vqSpec = getTargetSpecByKey("VQueue");
        TargetSpec pgSpec = getTargetSpecByKey("PlaceGroups");

        CfgFolder vqBase = folderIdx.findBySegments(vqSpec.base);
        boolean vqExists = (vqBase != null && folderIdx.findChild(vqBase, cname) != null);
        boolean pgExists = pgByName.containsKey(cname.toLowerCase(Locale.ROOT));

        CmCenterStatusResponse resp = new CmCenterStatusResponse();
        resp.centerName = cname;
        resp.vqueueExists = vqExists;
        resp.ivrExists = pgExists;
        resp.vqueuePath = String.join(" > ", vqSpec.base) + " > " + cname;
        resp.ivrPath = String.join(" > ", pgSpec.base) + " > " + cname;
        resp.logs.add("[Config 옵션 상태 조회] '" + cname + "' (VQueue / Place Group) 확인 중...");
        resp.logs.add("  -> VQueue 폴더: " + (vqExists ? "Config 존재" : "Config 미생성") + " (" + resp.vqueuePath + ")");
        resp.logs.add("  -> Place Group 오브젝트: " + (pgExists ? "Config 존재" : "Config 미생성") + " (" + resp.ivrPath + ")");
        return resp;
    }

    public CmCenterApplyResponse applyCenterOptions(CenterRequest req) throws Exception {
        IConfService service = sessionManager.ensureConnected();
        String cname = safeTrim(req.centerName);
        if (cname.isEmpty()) throw new IllegalArgumentException("센터 이름이 비어 있습니다.");

        FolderIndex folderIdx = folderResolver.loadIndex(service);
        Map<String, CfgPlaceGroup> pgByName = loadPlaceGroupsByName(service);

        CmCenterApplyResponse resp = new CmCenterApplyResponse();
        resp.centerName = cname;

        for (TargetSpec sp : Arrays.asList(getTargetSpecByKey("VQueue"), getTargetSpecByKey("PlaceGroups"))) {
            boolean isVq = "VQueue".equals(sp.key);
            boolean doApply = isVq ? req.applyVQueue : req.applyIvr;
            boolean want = isVq ? req.wantVQueue : req.wantIvr;
            String objType = isVq ? "Folder" : "Place Group";
            String fullPath = String.join(" > ", sp.base) + " > " + cname;

            CfgFolder baseFolder = folderIdx.findBySegments(sp.base);
            boolean exists = isVq
                    ? (baseFolder != null && folderIdx.findChild(baseFolder, cname) != null)
                    : pgByName.containsKey(cname.toLowerCase(Locale.ROOT));

            CmCenterItemResultDto item = new CmCenterItemResultDto();
            item.key = sp.key;
            item.label = sp.label;
            item.kind = sp.kind;
            item.path = fullPath;

            if (!doApply) {
                item.action = "Unchanged";
                item.existsNow = exists;
                item.detail = "변경 대상 제외 (기존 상태 유지)";
                resp.items.add(item);
                continue;
            }

            if (baseFolder == null) {
                resp.failedCount++;
                item.action = "Failed";
                item.existsNow = exists;
                item.detail = "Config 기본 상위 경로 없음: " + String.join(" > ", sp.base);
                resp.auditEvents.add(AuditEventDto.error("ApplyCenterOption", objType, cname, cname, fullPath, item.detail));
                resp.items.add(item);
                continue;
            }

            try {
                if (want) {
                    if (exists) {
                        item.action = "Skipped";
                        item.existsNow = true;
                        item.detail = "이미 존재함";
                        resp.logs.add("  [유지] " + sp.label + " 이미 존재 (" + fullPath + ")");
                    } else {
                        if (isVq) {
                            folderResolver.createSubFolder(service, baseFolder, cname);
                        } else {
                            CfgPlaceGroup pg = new CfgPlaceGroup(service);
                            CfgGroup gi = new CfgGroup(service, pg);
                            gi.setTenantDBID(sessionManager.getTenantDbid());
                            gi.setName(cname);
                            pg.setGroupInfo(gi);
                            pg.setFolderId(baseFolder.getDBID());
                            pg.save();
                        }
                        item.action = "Created";
                        item.existsNow = true;
                        item.detail = "생성 완료";
                        resp.logs.add("  [생성 완료] " + sp.label + " -> " + fullPath);
                        resp.auditEvents.add(AuditEventDto.create(objType, cname, cname, fullPath, null));
                    }
                } else {
                    if (!exists) {
                        item.action = "Skipped";
                        item.existsNow = false;
                        item.detail = "이미 미존재";
                        resp.logs.add("  [유지] " + sp.label + " 이미 미존재 (" + fullPath + ")");
                    } else {
                        if (isVq) {
                            CfgFolder target = folderIdx.findChild(baseFolder, cname);
                            if (target != null) target.delete();
                        } else {
                            CfgPlaceGroup target = pgByName.get(cname.toLowerCase(Locale.ROOT));
                            if (target != null) target.delete();
                        }
                        item.action = "Deleted";
                        item.existsNow = false;
                        item.detail = "삭제 완료";
                        resp.logs.add("  [삭제 완료] " + sp.label + " -> " + fullPath);
                        resp.auditEvents.add(AuditEventDto.delete(objType, cname, cname, fullPath, null));
                    }
                }
            } catch (Exception ex) {
                resp.failedCount++;
                item.action = "Failed";
                item.existsNow = exists;
                item.detail = ex.getMessage();
                resp.logs.add("  [실패] " + sp.label + " -> " + ex.getMessage());
                resp.auditEvents.add(AuditEventDto.error("ApplyCenterOption", objType, cname, cname, fullPath, ex.getMessage()));
            }
            resp.items.add(item);
        }

        // Re-read ground truth
        FolderIndex finalIdx = folderResolver.loadIndex(service);
        Map<String, CfgPlaceGroup> finalPgs = loadPlaceGroupsByName(service);
        CfgFolder vqBaseFinal = finalIdx.findBySegments(getTargetSpecByKey("VQueue").base);
        resp.vqueueExists = (vqBaseFinal != null && finalIdx.findChild(vqBaseFinal, cname) != null);
        resp.ivrExists = finalPgs.containsKey(cname.toLowerCase(Locale.ROOT));
        return resp;
    }

    public CmCenterDeleteResponse deleteCenterArtifacts(CenterRequest req) throws Exception {
        IConfService service = sessionManager.ensureConnected();
        String cname = safeTrim(req.centerName);
        if (cname.isEmpty()) throw new IllegalArgumentException("센터 이름이 비어 있습니다.");
        String loc = safeTrim(req.location);
        boolean useLoc = isEffectiveLocation(loc);

        FolderIndex folderIdx = folderResolver.loadIndex(service);
        Map<String, CfgPlaceGroup> pgByName = loadPlaceGroupsByName(service);

        CmCenterDeleteResponse resp = new CmCenterDeleteResponse();
        resp.centerName = cname;
        resp.logs.add("[센터 Config 항목 삭제] '" + cname + "' 정리 시작...");

        for (TargetSpec sp : getTargetSpecs()) {
            String objType = "PlaceGroup".equals(sp.kind) ? "Place Group" : "Folder";
            String dispPath = String.join(" > ", resolveFullSegments(sp, cname, loc));
            try {
                CfgFolder baseFolder = folderIdx.findBySegments(sp.base);
                if (baseFolder == null) continue;

                if ("PlaceGroup".equals(sp.kind)) {
                    CfgPlaceGroup pg = pgByName.get(cname.toLowerCase(Locale.ROOT));
                    if (pg != null) {
                        pg.delete();
                        resp.deletedCount++;
                        resp.logs.add("  [삭제 완료] " + sp.label + " (" + dispPath + ")");
                        resp.auditEvents.add(AuditEventDto.delete(objType, cname, cname, dispPath, null));
                        resp.items.add(delItem(sp, dispPath, "Deleted", null));
                    }
                } else {
                    CfgFolder parentFolder = baseFolder;
                    if (sp.usesLocation && useLoc) {
                        parentFolder = folderIdx.findChild(baseFolder, loc);
                        if (parentFolder == null) continue;
                    }
                    CfgFolder target = folderIdx.findChild(parentFolder, cname);
                    if (target != null) {
                        target.delete();
                        resp.deletedCount++;
                        resp.logs.add("  [삭제 완료] " + sp.label + " (" + dispPath + ")");
                        resp.auditEvents.add(AuditEventDto.delete(objType, cname, cname, dispPath, null));
                        resp.items.add(delItem(sp, dispPath, "Deleted", null));
                    }
                    if (req.deleteLocationIfSpecified && sp.usesLocation && useLoc) {
                        folderIdx = folderResolver.loadIndex(service);
                        CfgFolder locFolder = folderIdx.findChild(folderIdx.findBySegments(sp.base), loc);
                        if (locFolder != null) {
                            Collection<CfgObjectID> kids = locFolder.getObjectIDs();
                            boolean hasChildFolder = !folderIdx.getChildren(locFolder).isEmpty();
                            if (!hasChildFolder && (kids == null || kids.isEmpty())) {
                                String locFull = String.join(" > ", sp.base) + " > " + loc;
                                locFolder.delete();
                                resp.deletedCount++;
                                resp.logs.add("  [빈 위치 폴더 삭제] " + locFull);
                                resp.auditEvents.add(AuditEventDto.delete("Folder", loc, cname, locFull, null));
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                resp.failedCount++;
                resp.logs.add("  [삭제 실패] " + sp.label + " -> " + ex.getMessage());
                resp.auditEvents.add(AuditEventDto.error("DeleteCenterArtifacts", objType, cname, cname, dispPath, ex.getMessage()));
                resp.items.add(delItem(sp, dispPath, "Failed", ex.getMessage()));
            }
        }
        return resp;
    }

    private TargetSpec getTargetSpecByKey(String key) {
        for (TargetSpec sp : getTargetSpecs()) {
            if (sp.key.equals(key)) return sp;
        }
        throw new IllegalArgumentException("Unknown center spec key: " + key);
    }

    private Map<String, CfgPlaceGroup> loadPlaceGroupsByName(IConfService service) throws Exception {
        CfgPlaceGroupQuery q = new CfgPlaceGroupQuery(service);
        q.setTenantDbid(sessionManager.getTenantDbid());
        Collection<CfgPlaceGroup> all = q.execute();
        Map<String, CfgPlaceGroup> map = new java.util.HashMap<>();
        if (all != null) {
            for (CfgPlaceGroup pg : all) {
                if (pg.getGroupInfo() != null && pg.getGroupInfo().getName() != null) {
                    map.put(pg.getGroupInfo().getName().trim().toLowerCase(Locale.ROOT), pg);
                }
            }
        }
        return map;
    }

    private static List<String> resolveFullSegments(TargetSpec sp, String centerName, String location) {
        List<String> segs = new ArrayList<>(sp.base);
        if (sp.usesLocation && isEffectiveLocation(location)) {
            segs.add(location.trim());
        }
        segs.add(centerName.trim());
        return segs;
    }

    private static boolean isEffectiveLocation(String location) {
        if (location == null) return false;
        String t = location.trim();
        return !t.isEmpty() && !"(위치 없음)".equals(t) && !"-".equals(t);
    }

    private static CmCenterItemResultDto delItem(TargetSpec sp, String path, String status, String detail) {
        CmCenterItemResultDto item = new CmCenterItemResultDto();
        item.key = sp.key;
        item.label = sp.label;
        item.kind = sp.kind;
        item.path = path;
        item.status = status;
        item.action = status;
        item.detail = detail;
        return item;
    }

    private static String safeTrim(String s) {
        return s == null ? "" : s.trim();
    }

    private static double round2(double v) {
        return Math.round(v * 100.0) / 100.0;
    }
}
