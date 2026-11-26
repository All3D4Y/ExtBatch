package com.gcti.extbatch.service;

import com.genesyslab.platform.applicationblocks.com.IConfService;
import com.genesyslab.platform.applicationblocks.com.objects.CfgAgentLogin;
import com.genesyslab.platform.applicationblocks.com.objects.CfgDN;
import com.genesyslab.platform.applicationblocks.com.objects.CfgFolder;
import com.genesyslab.platform.applicationblocks.com.objects.CfgPlace;
import com.genesyslab.platform.applicationblocks.com.queries.CfgAgentLoginQuery;
import com.genesyslab.platform.applicationblocks.com.queries.CfgDNQuery;
import com.genesyslab.platform.applicationblocks.com.queries.CfgPlaceQuery;
import com.genesyslab.platform.configuration.protocol.types.CfgDNRegisterFlag;
import com.genesyslab.platform.configuration.protocol.types.CfgDNType;
import com.genesyslab.platform.configuration.protocol.types.CfgFlag;
import com.genesyslab.platform.configuration.protocol.types.CfgRouteType;
import com.gcti.extbatch.dto.BatchDtos.BatchActionItemResult;
import com.gcti.extbatch.dto.BatchDtos.BatchActionResponse;
import com.gcti.extbatch.dto.BatchDtos.BatchCheckResponse;
import com.gcti.extbatch.dto.BatchDtos.BatchRequest;
import com.gcti.extbatch.dto.BatchDtos.CmFindResultItem;
import com.gcti.extbatch.dto.CommonDtos.AuditEventDto;
import com.gcti.extbatch.psdk.CmFolderResolver;
import com.gcti.extbatch.psdk.CmFolderResolver.FolderIndex;
import com.gcti.extbatch.psdk.CustomerCatalogService;
import com.gcti.extbatch.psdk.CustomerCatalogService.CustomerRecord;
import com.gcti.extbatch.psdk.GenesysSessionManager;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collection;
import java.util.Collections;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import org.springframework.stereotype.Service;

@Service
public class BatchProvisionService {

    private final GenesysSessionManager sessionManager;
    private final CmFolderResolver folderResolver;
    private final CustomerCatalogService customerCatalog;

    public BatchProvisionService(
            GenesysSessionManager sessionManager,
            CmFolderResolver folderResolver,
            CustomerCatalogService customerCatalog) {
        this.sessionManager = sessionManager;
        this.folderResolver = folderResolver;
        this.customerCatalog = customerCatalog;
    }

    public BatchCheckResponse check(BatchRequest req) throws Exception {
        IConfService service = sessionManager.ensureConnected();
        List<String> dnList = req.doAgentDn || req.doPlace ? NumberSpecParser.expand(req.dnSpec) : Collections.emptyList();
        List<String> ctiList = req.doAgentLogin || req.doAcdPosition ? NumberSpecParser.expand(req.ctiSpec) : Collections.emptyList();

        if (dnList.isEmpty() && ctiList.isEmpty()) {
            throw new IllegalArgumentException("처리할 DN 또는 CTI ID 번호를 입력하세요.");
        }

        BatchCheckResponse resp = new BatchCheckResponse();
        resp.customer = req.customerName != null ? req.customerName.trim() : "";
        resp.dnList = dnList;
        resp.ctiList = ctiList;
        resp.doAgentDn = req.doAgentDn && !dnList.isEmpty();
        resp.doPlace = req.doPlace && !dnList.isEmpty();
        resp.doAgentLogin = req.doAgentLogin && !ctiList.isEmpty();
        resp.doAcdPos = req.doAcdPosition && !ctiList.isEmpty();
        resp.foldersOk = true;

        FolderIndex folderIdx = folderResolver.loadIndex(service);
        LookupIndex lookup = loadGlobalLookup(service, dnList, ctiList, resp.doAgentDn, resp.doPlace, resp.doAgentLogin, resp.doAcdPos);

        int totalQueries = 0;
        for (String id : ctiList) {
            if (resp.doAgentLogin) {
                totalQueries++;
                List<CfgAgentLogin> hits = lookup.loginsByCode.getOrDefault(id, Collections.emptyList());
                CmFindResultItem item = new CmFindResultItem();
                item.category = "Agent Login";
                item.name = id;
                item.count = hits.size();
                for (CfgAgentLogin al : hits) {
                    String path = resolveBackslashPath(al.getObjectPath(), al.getFolderId(), folderIdx);
                    item.matches.add(id + "|" + safeSwitchName(al.getSwitchDBID()) + "|" + path);
                }
                resp.findResults.add(item);
            }
            if (resp.doAcdPos) {
                totalQueries++;
                List<CfgDN> hits = lookup.acdByNum.getOrDefault(id, Collections.emptyList());
                CmFindResultItem item = new CmFindResultItem();
                item.category = "ACD Position (DN)";
                item.name = id;
                item.count = hits.size();
                for (CfgDN dn : hits) {
                    String path = resolveBackslashPath(dn.getObjectPath(), dn.getFolderId(), folderIdx);
                    item.matches.add(id + "|ACD Position|" + path);
                }
                resp.findResults.add(item);
            }
        }
        for (String dnNum : dnList) {
            if (resp.doAgentDn) {
                totalQueries++;
                List<CfgDN> hits = lookup.extByNum.getOrDefault(dnNum, Collections.emptyList());
                CmFindResultItem item = new CmFindResultItem();
                item.category = "Agent DN";
                item.name = dnNum;
                item.count = hits.size();
                for (CfgDN dn : hits) {
                    String path = resolveBackslashPath(dn.getObjectPath(), dn.getFolderId(), folderIdx);
                    item.matches.add(dnNum + "|Extension|" + path);
                }
                resp.findResults.add(item);
            }
            if (resp.doPlace) {
                totalQueries++;
                String pName = "P" + dnNum;
                List<CfgPlace> hits = lookup.placesByName.getOrDefault(pName.toLowerCase(Locale.ROOT), Collections.emptyList());
                CmFindResultItem item = new CmFindResultItem();
                item.category = "Place";
                item.name = pName;
                item.count = hits.size();
                for (CfgPlace pl : hits) {
                    String path = resolveBackslashPath(pl.getObjectPath(), pl.getFolderId(), folderIdx);
                    item.matches.add(pName + "|" + path);
                }
                resp.findResults.add(item);
            }
        }

        resp.logs.add("[내선/CTI ID 검색] PSDK 전역 검색 (총 " + totalQueries + "개 항목)...");
        int existingCnt = 0;
        for (CmFindResultItem fr : resp.findResults) {
            if (fr.count > 0) {
                existingCnt++;
                resp.logs.add("  [검색됨] " + fr.category + " '" + fr.name + "' -> " + fr.count + "건 (" + String.join("; ", fr.matches) + ")");
            }
        }
        if (existingCnt == 0) {
            resp.logs.add("  [OK] 미사용 상태 (전체 Config 미등록).");
        }
        return resp;
    }

    public BatchActionResponse create(BatchRequest req) throws Exception {
        long startNs = System.nanoTime();
        IConfService service = sessionManager.ensureConnected();

        if (req.customerName == null || req.customerName.trim().isEmpty()) {
            throw new IllegalArgumentException("대상 고객사를 선택하세요.");
        }
        CustomerRecord cust = customerCatalog.findByName(req.customerName);
        if (cust == null) {
            throw new IllegalArgumentException("고객사 '" + req.customerName + "'를 cm_customers.json에서 찾을 수 없습니다.");
        }

        List<String> dnList = req.doAgentDn || req.doPlace ? NumberSpecParser.expand(req.dnSpec) : Collections.emptyList();
        List<String> ctiList = req.doAgentLogin || req.doAcdPosition ? NumberSpecParser.expand(req.ctiSpec) : Collections.emptyList();
        if (dnList.isEmpty() && ctiList.isEmpty()) {
            throw new IllegalArgumentException("처리할 DN 또는 CTI ID 번호를 입력하세요.");
        }

        boolean needLogin = !ctiList.isEmpty() && req.doAgentLogin;
        boolean needAcd = !ctiList.isEmpty() && req.doAcdPosition;
        boolean needDnObj = !dnList.isEmpty() && req.doAgentDn;
        boolean needPlace = !dnList.isEmpty() && req.doPlace;
        if (!needLogin && !needAcd && !needDnObj && !needPlace) {
            throw new IllegalArgumentException("실행할 Config 항목이 하나도 선택되지 않았습니다.");
        }

        BatchActionResponse resp = new BatchActionResponse();
        resp.action = "Create";
        resp.customer = cust.name;

        String swName = sessionManager.getSwitchName();
        List<String> agPathSegs = folderResolver.makePath(Arrays.asList("Configuration", "Environment", "Switches", swName, "Agent Logins"), cust.agentLogin);
        List<String> acdPathSegs = folderResolver.makePath(Arrays.asList("Configuration", "Environment", "Switches", swName, "DNs", "ACDPosition"), cust.acdPosition);
        List<String> dnPathSegs = folderResolver.makePath(Arrays.asList("Configuration", "Environment", "Switches", swName, "DNs", "AGENT_DN"), cust.agentDn);
        List<String> plPathSegs = folderResolver.makePath(Arrays.asList("Configuration", "Environment", "Places", "AGENT"), cust.place);

        FolderIndex folderIdx = folderResolver.loadIndex(service);
        resp.logs.add("[대상 폴더] 고객사 '" + cust.name + "' 폴더 존재 여부 확인 중...");
        List<String> missing = new ArrayList<>();

        CfgFolder agFolder = null, acdFolder = null, dnFolder = null, plFolder = null;
        if (needLogin) {
            agFolder = resolveRequiredFolder(folderIdx, agPathSegs, "AgentLogin", missing);
        }
        if (needAcd) {
            acdFolder = resolveRequiredFolder(folderIdx, acdPathSegs, "AcdPosition", missing);
        }
        if (needDnObj) {
            dnFolder = resolveRequiredFolder(folderIdx, dnPathSegs, "AgentDN", missing);
        } else if (needPlace && dnPathSegs != null) {
            dnFolder = folderIdx.findBySegments(dnPathSegs);
        }
        if (needPlace) {
            plFolder = resolveRequiredFolder(folderIdx, plPathSegs, "Place", missing);
        }

        if (!missing.isEmpty()) {
            throw new IllegalStateException("대상 Config 폴더 검증 실패:\n - " + String.join("\n - ", missing));
        }
        resp.logs.add("  [OK] 대상 폴더 확인 완료.");

        if (!req.skipDuplicateCheck) {
            BatchCheckResponse chk = check(req);
            resp.logs.addAll(chk.logs);
            List<String> dups = new ArrayList<>();
            for (CmFindResultItem fr : chk.findResults) {
                if (fr.count > 0) {
                    dups.add(fr.category + ": " + fr.name);
                }
            }
            if (!dups.isEmpty()) {
                throw new IllegalStateException("중복 객체가 발견되어 생성을 중단했습니다: " + String.join(", ", dups));
            }
        }

        int tenantDbid = sessionManager.getTenantDbid();
        int switchDbid = sessionManager.getSwitchDbid();
        String agPathStr = agFolder != null ? folderIdx.resolvePath(agFolder) : "";
        String acdPathStr = acdFolder != null ? folderIdx.resolvePath(acdFolder) : "";
        String dnPathStr = dnFolder != null ? folderIdx.resolvePath(dnFolder) : "";
        String plPathStr = plFolder != null ? folderIdx.resolvePath(plFolder) : "";

        // 1/4 Agent Login
        if (needLogin) {
            resp.logs.add("[1/4 Agent Login] " + ctiList.size() + "건 생성 중...");
            for (String cti : ctiList) {
                try {
                    CfgAgentLogin al = new CfgAgentLogin(service);
                    al.setTenantDBID(tenantDbid);
                    al.setSwitchDBID(switchDbid);
                    al.setLoginCode(cti);
                    al.setSwitchSpecificType(1);
                    al.setUseOverride(CfgFlag.CFGTrue);
                    al.setFolderId(agFolder.getDBID());
                    al.save();

                    resp.createdCount++;
                    resp.results.add(itemResult("Agent Login", cti, "Created", agPathStr, null, al.getDBID()));
                    resp.auditEvents.add(AuditEventDto.create("Agent Login", cti, cust.name, agPathStr, null));
                    resp.logs.add("  [완료] Agent Login " + cti);
                } catch (Exception ex) {
                    resp.failedCount++;
                    resp.ok = false;
                    resp.results.add(itemResult("Agent Login", cti, "Failed", agPathStr, ex.getMessage(), null));
                    resp.auditEvents.add(AuditEventDto.error("Create", "Agent Login", cti, cust.name, agPathStr, ex.getMessage()));
                    throw new IllegalStateException("Agent Login 생성 실패 (" + cti + "): " + ex.getMessage(), ex);
                }
            }
        }

        // 2/4 ACD Position (DN)
        if (needAcd) {
            resp.logs.add("[2/4 ACD Position] " + ctiList.size() + "건 생성 중...");
            for (String cti : ctiList) {
                try {
                    CfgDN dn = new CfgDN(service);
                    dn.setTenantDBID(tenantDbid);
                    dn.setSwitchDBID(switchDbid);
                    dn.setType(CfgDNType.CFGACDPosition);
                    dn.setNumber(cti);
                    dn.setSwitchSpecificType(1);
                    dn.setRouteType(CfgRouteType.CFGDefault);
                    dn.setUseOverride(CfgFlag.CFGTrue);
                    dn.setFolderId(acdFolder.getDBID());
                    dn.save();

                    resp.createdCount++;
                    resp.results.add(itemResult("ACD Position", cti, "Created", acdPathStr, null, dn.getDBID()));
                    resp.auditEvents.add(AuditEventDto.create("DN (ACD Position)", cti, cust.name, acdPathStr, null));
                    resp.logs.add("  [완료] ACD Position " + cti);
                } catch (Exception ex) {
                    resp.failedCount++;
                    resp.ok = false;
                    resp.results.add(itemResult("ACD Position", cti, "Failed", acdPathStr, ex.getMessage(), null));
                    resp.auditEvents.add(AuditEventDto.error("Create", "DN (ACD Position)", cti, cust.name, acdPathStr, ex.getMessage()));
                    throw new IllegalStateException("ACD Position 생성 실패 (" + cti + "): " + ex.getMessage(), ex);
                }
            }
        }

        // 3/4 Agent DN (Extension)
        Map<String, Integer> createdDnDbidByNum = new HashMap<>();
        if (needDnObj) {
            resp.logs.add("[3/4 Agent DN] " + dnList.size() + "건 생성 중...");
            for (String dnNum : dnList) {
                try {
                    CfgDN dn = new CfgDN(service);
                    dn.setTenantDBID(tenantDbid);
                    dn.setSwitchDBID(switchDbid);
                    dn.setType(CfgDNType.CFGExtension);
                    dn.setNumber(dnNum);
                    dn.setSwitchSpecificType(1);
                    dn.setRouteType(CfgRouteType.CFGDefault);
                    dn.setUseOverride(CfgFlag.CFGTrue);
                    dn.setRegisterAll(CfgDNRegisterFlag.CFGDRTrue);
                    dn.setFolderId(dnFolder.getDBID());
                    dn.save();

                    createdDnDbidByNum.put(dnNum, dn.getDBID());
                    resp.createdCount++;
                    resp.results.add(itemResult("Agent DN", dnNum, "Created", dnPathStr, null, dn.getDBID()));
                    resp.auditEvents.add(AuditEventDto.create("DN (Extension)", dnNum, cust.name, dnPathStr, null));
                    resp.logs.add("  [완료] Agent DN " + dnNum);
                } catch (Exception ex) {
                    resp.failedCount++;
                    resp.ok = false;
                    resp.results.add(itemResult("Agent DN", dnNum, "Failed", dnPathStr, ex.getMessage(), null));
                    resp.auditEvents.add(AuditEventDto.error("Create", "DN (Extension)", dnNum, cust.name, dnPathStr, ex.getMessage()));
                    throw new IllegalStateException("Agent DN 생성 실패 (" + dnNum + "): " + ex.getMessage(), ex);
                }
            }
        }

        // 4/4 Place (+ direct DNDBIDs link)
        if (needPlace) {
            resp.logs.add("[4/4 Place] " + dnList.size() + "건 생성 및 DN 연결 중...");
            Map<String, Integer> existingFolderDnDbids = new HashMap<>();
            if (!needDnObj && dnFolder != null) {
                CfgDNQuery q = new CfgDNQuery(service);
                q.setSwitchDbid(switchDbid);
                q.setDnType(CfgDNType.CFGExtension);
                Collection<CfgDN> existingDns = q.execute();
                if (existingDns != null) {
                    for (CfgDN ed : existingDns) {
                        if (ed.getFolderId() != null && ed.getFolderId().equals(dnFolder.getDBID()) && ed.getNumber() != null) {
                            existingFolderDnDbids.put(ed.getNumber(), ed.getDBID());
                        }
                    }
                }
            }

            for (String dnNum : dnList) {
                String pName = "P" + dnNum;
                Integer linkDnDbid = createdDnDbidByNum.get(dnNum);
                if (linkDnDbid == null && !needDnObj) {
                    linkDnDbid = existingFolderDnDbids.get(dnNum);
                }
                if (needDnObj && linkDnDbid == null) {
                    throw new IllegalStateException("AGENT_DN 생성 DBID를 찾지 못했습니다: " + dnNum);
                }

                try {
                    CfgPlace place = new CfgPlace(service);
                    place.setTenantDBID(tenantDbid);
                    place.setName(pName);
                    place.setFolderId(plFolder.getDBID());
                    if (linkDnDbid != null) {
                        place.setDNDBIDs(Collections.singletonList(linkDnDbid));
                    }
                    place.save();

                    String detail = linkDnDbid != null ? ("DN Shortcut=" + dnNum) : "단독 생성";
                    resp.createdCount++;
                    resp.results.add(itemResult("Place", pName, "Created", plPathStr, detail, place.getDBID()));
                    resp.auditEvents.add(AuditEventDto.create("Place", pName, cust.name, plPathStr, detail));
                    if (linkDnDbid != null) {
                        resp.logs.add("  [연결 완료] " + pName + " <- " + dnNum);
                    } else {
                        resp.logs.add("  [안내] AGENT_DN 미존재 -> Place(" + pName + ") 단독 생성됨");
                    }
                } catch (Exception ex) {
                    resp.failedCount++;
                    resp.ok = false;
                    resp.results.add(itemResult("Place", pName, "Failed", plPathStr, ex.getMessage(), null));
                    resp.auditEvents.add(AuditEventDto.error("Create", "Place", pName, cust.name, plPathStr, ex.getMessage()));
                    throw new IllegalStateException("Place 생성 실패 (" + pName + "): " + ex.getMessage(), ex);
                }
            }
        }

        resp.elapsedSeconds = round2((System.nanoTime() - startNs) / 1_000_000_000.0);
        resp.logs.add(String.format(Locale.ROOT, "[전체 생성 완료] 생성 %d건 / 소요 시간: %.1f초", resp.createdCount, resp.elapsedSeconds));
        return resp;
    }

    public BatchActionResponse delete(BatchRequest req) throws Exception {
        long startNs = System.nanoTime();
        IConfService service = sessionManager.ensureConnected();

        List<String> dnList = req.doAgentDn || req.doPlace ? NumberSpecParser.expand(req.dnSpec) : Collections.emptyList();
        List<String> ctiList = req.doAgentLogin || req.doAcdPosition ? NumberSpecParser.expand(req.ctiSpec) : Collections.emptyList();
        if (dnList.isEmpty() && ctiList.isEmpty()) {
            throw new IllegalArgumentException("처리할 DN 또는 CTI ID 번호를 입력하세요.");
        }

        boolean needPlace = !dnList.isEmpty() && req.doPlace;
        boolean needDnObj = !dnList.isEmpty() && req.doAgentDn;
        boolean needAcd = !ctiList.isEmpty() && req.doAcdPosition;
        boolean needLogin = !ctiList.isEmpty() && req.doAgentLogin;
        if (!needPlace && !needDnObj && !needAcd && !needLogin) {
            throw new IllegalArgumentException("삭제할 Config 항목이 하나도 선택되지 않았습니다.");
        }

        BatchActionResponse resp = new BatchActionResponse();
        resp.action = "Delete";
        resp.customer = req.customerName != null ? req.customerName.trim() : "";

        FolderIndex folderIdx = folderResolver.loadIndex(service);
        Map<String, String> folderCustLookup = buildCustomerFolderLookup();

        resp.logs.add("[삭제 대상 탐색] 센터 무관 Config 전역 검색 중...");
        LookupIndex lookup = loadGlobalLookup(service, dnList, ctiList, needDnObj, needPlace, needLogin, needAcd);

        // Strict reverse delete order: 1. Place -> 2. Agent DN -> 3. ACD Position -> 4. Agent Login
        if (needPlace) {
            resp.logs.add("[1/4 Place 일괄 삭제] 대상 확인 중...");
            for (String dnNum : dnList) {
                String pName = "P" + dnNum;
                List<CfgPlace> hits = lookup.placesByName.getOrDefault(pName.toLowerCase(Locale.ROOT), Collections.emptyList());
                if (hits.isEmpty()) {
                    resp.skippedCount++;
                    resp.results.add(itemResult("Place", pName, "Skipped", "", "Config 전체 미존재", null));
                    resp.logs.add("  [건너뜀] Place '" + pName + "' -> Config 전체 미존재");
                    continue;
                }
                for (CfgPlace pl : hits) {
                    String pathStr = resolveDisplayPath(pl.getObjectPath(), pl.getFolderId(), folderIdx);
                    String custLabel = resolveAuditCustomer("Place", pathStr, folderCustLookup, resp.customer);
                    try {
                        Integer dbid = pl.getDBID();
                        pl.delete();
                        resp.deletedCount++;
                        resp.results.add(itemResult("Place", pName, "Deleted", pathStr, null, dbid));
                        resp.auditEvents.add(AuditEventDto.delete("Place", pName, custLabel, pathStr, null));
                        resp.logs.add("  [삭제 완료] Place '" + pName + "' (" + pathStr + ")");
                    } catch (Exception ex) {
                        resp.failedCount++;
                        resp.ok = false;
                        resp.results.add(itemResult("Place", pName, "Failed", pathStr, ex.getMessage(), pl.getDBID()));
                        resp.auditEvents.add(AuditEventDto.error("Delete", "Place", pName, custLabel, pathStr, ex.getMessage()));
                        resp.logs.add("  [삭제 실패] Place '" + pName + "' -> " + ex.getMessage());
                    }
                }
            }
        }

        if (needDnObj) {
            resp.logs.add("[2/4 Agent DN 일괄 삭제] 대상 확인 중...");
            for (String dnNum : dnList) {
                List<CfgDN> hits = lookup.extByNum.getOrDefault(dnNum, Collections.emptyList());
                if (hits.isEmpty()) {
                    resp.skippedCount++;
                    resp.results.add(itemResult("Agent DN", dnNum, "Skipped", "", "Config 전체 미존재", null));
                    resp.logs.add("  [건너뜀] Agent DN '" + dnNum + "' -> Config 전체 미존재");
                    continue;
                }
                for (CfgDN dn : hits) {
                    String pathStr = resolveDisplayPath(dn.getObjectPath(), dn.getFolderId(), folderIdx);
                    String custLabel = resolveAuditCustomer("AgentDN", pathStr, folderCustLookup, resp.customer);
                    try {
                        Integer dbid = dn.getDBID();
                        dn.delete();
                        resp.deletedCount++;
                        resp.results.add(itemResult("Agent DN", dnNum, "Deleted", pathStr, null, dbid));
                        resp.auditEvents.add(AuditEventDto.delete("DN (Extension)", dnNum, custLabel, pathStr, null));
                        resp.logs.add("  [삭제 완료] Agent DN '" + dnNum + "' (" + pathStr + ")");
                    } catch (Exception ex) {
                        resp.failedCount++;
                        resp.ok = false;
                        resp.results.add(itemResult("Agent DN", dnNum, "Failed", pathStr, ex.getMessage(), dn.getDBID()));
                        resp.auditEvents.add(AuditEventDto.error("Delete", "DN (Extension)", dnNum, custLabel, pathStr, ex.getMessage()));
                        resp.logs.add("  [삭제 실패] Agent DN '" + dnNum + "' -> " + ex.getMessage());
                    }
                }
            }
        }

        if (needAcd) {
            resp.logs.add("[3/4 ACD Position 일괄 삭제] 대상 확인 중...");
            for (String cti : ctiList) {
                List<CfgDN> hits = lookup.acdByNum.getOrDefault(cti, Collections.emptyList());
                if (hits.isEmpty()) {
                    resp.skippedCount++;
                    resp.results.add(itemResult("ACD Position", cti, "Skipped", "", "Config 전체 미존재", null));
                    resp.logs.add("  [건너뜀] ACD Position '" + cti + "' -> Config 전체 미존재");
                    continue;
                }
                for (CfgDN dn : hits) {
                    String pathStr = resolveDisplayPath(dn.getObjectPath(), dn.getFolderId(), folderIdx);
                    String custLabel = resolveAuditCustomer("ACDPos", pathStr, folderCustLookup, resp.customer);
                    try {
                        Integer dbid = dn.getDBID();
                        dn.delete();
                        resp.deletedCount++;
                        resp.results.add(itemResult("ACD Position", cti, "Deleted", pathStr, null, dbid));
                        resp.auditEvents.add(AuditEventDto.delete("DN (ACD Position)", cti, custLabel, pathStr, null));
                        resp.logs.add("  [삭제 완료] ACD Position '" + cti + "' (" + pathStr + ")");
                    } catch (Exception ex) {
                        resp.failedCount++;
                        resp.ok = false;
                        resp.results.add(itemResult("ACD Position", cti, "Failed", pathStr, ex.getMessage(), dn.getDBID()));
                        resp.auditEvents.add(AuditEventDto.error("Delete", "DN (ACD Position)", cti, custLabel, pathStr, ex.getMessage()));
                        resp.logs.add("  [삭제 실패] ACD Position '" + cti + "' -> " + ex.getMessage());
                    }
                }
            }
        }

        if (needLogin) {
            resp.logs.add("[4/4 Agent Login 일괄 삭제] 대상 확인 중...");
            for (String cti : ctiList) {
                List<CfgAgentLogin> hits = lookup.loginsByCode.getOrDefault(cti, Collections.emptyList());
                if (hits.isEmpty()) {
                    resp.skippedCount++;
                    resp.results.add(itemResult("Agent Login", cti, "Skipped", "", "Config 전체 미존재", null));
                    resp.logs.add("  [건너뜀] Agent Login '" + cti + "' -> Config 전체 미존재");
                    continue;
                }
                for (CfgAgentLogin al : hits) {
                    String pathStr = resolveDisplayPath(al.getObjectPath(), al.getFolderId(), folderIdx);
                    String custLabel = resolveAuditCustomer("AgentLogin", pathStr, folderCustLookup, resp.customer);
                    try {
                        Integer dbid = al.getDBID();
                        al.delete();
                        resp.deletedCount++;
                        resp.results.add(itemResult("Agent Login", cti, "Deleted", pathStr, null, dbid));
                        resp.auditEvents.add(AuditEventDto.delete("Agent Login", cti, custLabel, pathStr, null));
                        resp.logs.add("  [삭제 완료] Agent Login '" + cti + "' (" + pathStr + ")");
                    } catch (Exception ex) {
                        resp.failedCount++;
                        resp.ok = false;
                        resp.results.add(itemResult("Agent Login", cti, "Failed", pathStr, ex.getMessage(), al.getDBID()));
                        resp.auditEvents.add(AuditEventDto.error("Delete", "Agent Login", cti, custLabel, pathStr, ex.getMessage()));
                        resp.logs.add("  [삭제 실패] Agent Login '" + cti + "' -> " + ex.getMessage());
                    }
                }
            }
        }

        resp.elapsedSeconds = round2((System.nanoTime() - startNs) / 1_000_000_000.0);
        resp.logs.add(String.format(Locale.ROOT, "[전체 삭제 완료] 삭제 %d건 / 건너뜀 %d건 / 실패 %d건 (%.1f초)",
                resp.deletedCount, resp.skippedCount, resp.failedCount, resp.elapsedSeconds));
        return resp;
    }

    private CfgFolder resolveRequiredFolder(FolderIndex idx, List<String> segs, String key, List<String> missing) {
        if (segs == null || segs.isEmpty()) {
            missing.add(key + ": 매핑 정보 없음(null)");
            return null;
        }
        CfgFolder found = idx.findBySegments(segs);
        if (found == null) {
            missing.add(key + ": Config 폴더를 찾을 수 없음 (" + String.join(" > ", segs) + ")");
            return null;
        }
        return found;
    }

    private static class LookupIndex {
        final Map<String, List<CfgAgentLogin>> loginsByCode = new HashMap<>();
        final Map<String, List<CfgDN>> acdByNum = new HashMap<>();
        final Map<String, List<CfgDN>> extByNum = new HashMap<>();
        final Map<String, List<CfgPlace>> placesByName = new HashMap<>();
    }

    private LookupIndex loadGlobalLookup(
            IConfService service,
            List<String> dnList,
            List<String> ctiList,
            boolean needExt,
            boolean needPlace,
            boolean needLogin,
            boolean needAcd) throws Exception {

        LookupIndex idx = new LookupIndex();
        int tenantDbid = sessionManager.getTenantDbid();

        if (needLogin && !ctiList.isEmpty()) {
            if (ctiList.size() <= 5) {
                for (String code : ctiList) {
                    CfgAgentLoginQuery q = new CfgAgentLoginQuery(service);
                    q.setTenantDbid(tenantDbid);
                    q.setLoginCode(code);
                    Collection<CfgAgentLogin> res = q.execute();
                    if (res != null) {
                        for (CfgAgentLogin al : res) {
                            idx.loginsByCode.computeIfAbsent(al.getLoginCode(), k -> new ArrayList<>()).add(al);
                        }
                    }
                }
            } else {
                CfgAgentLoginQuery q = new CfgAgentLoginQuery(service);
                q.setTenantDbid(tenantDbid);
                Collection<CfgAgentLogin> res = q.execute();
                if (res != null) {
                    for (CfgAgentLogin al : res) {
                        if (al.getLoginCode() != null) {
                            idx.loginsByCode.computeIfAbsent(al.getLoginCode(), k -> new ArrayList<>()).add(al);
                        }
                    }
                }
            }
        }

        if (needAcd && !ctiList.isEmpty()) {
            if (ctiList.size() <= 5) {
                for (String num : ctiList) {
                    CfgDNQuery q = new CfgDNQuery(service);
                    q.setTenantDbid(tenantDbid);
                    q.setDnType(CfgDNType.CFGACDPosition);
                    q.setDnNumber(num);
                    Collection<CfgDN> res = q.execute();
                    if (res != null) {
                        for (CfgDN dn : res) {
                            idx.acdByNum.computeIfAbsent(dn.getNumber(), k -> new ArrayList<>()).add(dn);
                        }
                    }
                }
            } else {
                CfgDNQuery q = new CfgDNQuery(service);
                q.setTenantDbid(tenantDbid);
                q.setDnType(CfgDNType.CFGACDPosition);
                Collection<CfgDN> res = q.execute();
                if (res != null) {
                    for (CfgDN dn : res) {
                        if (dn.getNumber() != null) {
                            idx.acdByNum.computeIfAbsent(dn.getNumber(), k -> new ArrayList<>()).add(dn);
                        }
                    }
                }
            }
        }

        if (needExt && !dnList.isEmpty()) {
            if (dnList.size() <= 5) {
                for (String num : dnList) {
                    CfgDNQuery q = new CfgDNQuery(service);
                    q.setTenantDbid(tenantDbid);
                    q.setDnType(CfgDNType.CFGExtension);
                    q.setDnNumber(num);
                    Collection<CfgDN> res = q.execute();
                    if (res != null) {
                        for (CfgDN dn : res) {
                            idx.extByNum.computeIfAbsent(dn.getNumber(), k -> new ArrayList<>()).add(dn);
                        }
                    }
                }
            } else {
                CfgDNQuery q = new CfgDNQuery(service);
                q.setTenantDbid(tenantDbid);
                q.setDnType(CfgDNType.CFGExtension);
                Collection<CfgDN> res = q.execute();
                if (res != null) {
                    for (CfgDN dn : res) {
                        if (dn.getNumber() != null) {
                            idx.extByNum.computeIfAbsent(dn.getNumber(), k -> new ArrayList<>()).add(dn);
                        }
                    }
                }
            }
        }

        if (needPlace && !dnList.isEmpty()) {
            if (dnList.size() <= 5) {
                for (String dnNum : dnList) {
                    CfgPlaceQuery q = new CfgPlaceQuery(service);
                    q.setTenantDbid(tenantDbid);
                    q.setName("P" + dnNum);
                    Collection<CfgPlace> res = q.execute();
                    if (res != null) {
                        for (CfgPlace pl : res) {
                            if (pl.getName() != null) {
                                idx.placesByName.computeIfAbsent(pl.getName().toLowerCase(Locale.ROOT), k -> new ArrayList<>()).add(pl);
                            }
                        }
                    }
                }
            } else {
                CfgPlaceQuery q = new CfgPlaceQuery(service);
                q.setTenantDbid(tenantDbid);
                Collection<CfgPlace> res = q.execute();
                if (res != null) {
                    for (CfgPlace pl : res) {
                        if (pl.getName() != null) {
                            idx.placesByName.computeIfAbsent(pl.getName().toLowerCase(Locale.ROOT), k -> new ArrayList<>()).add(pl);
                        }
                    }
                }
            }
        }

        return idx;
    }

    private Map<String, String> buildCustomerFolderLookup() {
        Map<String, String> map = new LinkedHashMap<>();
        String swName = sessionManager.getSwitchName();
        for (CustomerRecord c : customerCatalog.loadAll()) {
            if (c.name == null) continue;
            addCustLookup(map, "Place", folderResolver.makePath(Arrays.asList("Configuration", "Environment", "Places", "AGENT"), c.place), c.name);
            addCustLookup(map, "AgentDN", folderResolver.makePath(Arrays.asList("Configuration", "Environment", "Switches", swName, "DNs", "AGENT_DN"), c.agentDn), c.name);
            addCustLookup(map, "ACDPos", folderResolver.makePath(Arrays.asList("Configuration", "Environment", "Switches", swName, "DNs", "ACDPosition"), c.acdPosition), c.name);
            addCustLookup(map, "AgentLogin", folderResolver.makePath(Arrays.asList("Configuration", "Environment", "Switches", swName, "Agent Logins"), c.agentLogin), c.name);
        }
        return map;
    }

    private static void addCustLookup(Map<String, String> map, String kind, List<String> segs, String custName) {
        if (segs == null || segs.isEmpty()) return;
        String k = (kind + "|" + String.join(" > ", segs)).toLowerCase(Locale.ROOT);
        map.putIfAbsent(k, custName);
    }

    private static String resolveAuditCustomer(String kind, String dispPath, Map<String, String> lookup, String fallback) {
        if (dispPath != null) {
            String found = lookup.get((kind + "|" + dispPath).toLowerCase(Locale.ROOT));
            if (found != null) return found;
        }
        return fallback != null ? fallback : "";
    }

    private String safeSwitchName(Integer swDbid) {
        if (swDbid != null && swDbid == sessionManager.getSwitchDbid()) {
            return sessionManager.getSwitchName();
        }
        return swDbid != null ? ("Switch#" + swDbid) : "";
    }

    private static String resolveBackslashPath(String objPath, Integer folderId, FolderIndex idx) {
        if (objPath != null && !objPath.trim().isEmpty()) {
            return objPath.trim();
        }
        return idx.resolveBackslashPath(folderId);
    }

    private static String resolveDisplayPath(String objPath, Integer folderId, FolderIndex idx) {
        if (objPath != null && !objPath.trim().isEmpty()) {
            return CmFolderResolver.toDisplayPath(objPath);
        }
        return idx.resolveDisplayPath(folderId);
    }

    private static BatchActionItemResult itemResult(String cat, String name, String status, String path, String detail, Integer dbid) {
        BatchActionItemResult r = new BatchActionItemResult();
        r.category = cat;
        r.name = name;
        r.status = status;
        r.path = path;
        r.detail = detail;
        r.dbid = dbid;
        return r;
    }

    private static double round2(double v) {
        return Math.round(v * 100.0) / 100.0;
    }
}
