package com.gcti.extbatch.service;

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
import com.genesyslab.platform.configuration.protocol.types.CfgDNType;
import com.genesyslab.platform.configuration.protocol.types.CfgFlag;
import com.gcti.extbatch.dto.Phase3Dtos.CenterAuditIssueDto;
import com.gcti.extbatch.dto.Phase3Dtos.CenterAuditRequest;
import com.gcti.extbatch.dto.Phase3Dtos.CenterAuditResponse;
import com.gcti.extbatch.dto.Phase3Dtos.CenterAuditSummaryRowDto;
import com.gcti.extbatch.dto.Phase3Dtos.CenterPairRowDto;
import com.gcti.extbatch.dto.Phase3Dtos.LookupItemDto;
import com.gcti.extbatch.dto.Phase3Dtos.LookupRelationDto;
import com.gcti.extbatch.dto.Phase3Dtos.LookupSearchRequest;
import com.gcti.extbatch.dto.Phase3Dtos.LookupSearchResponse;
import com.gcti.extbatch.psdk.CmCatalogSnapshotLoader;
import com.gcti.extbatch.psdk.CmCatalogSnapshotLoader.LoadedCatalog;
import com.gcti.extbatch.psdk.CmFolderResolver;
import com.gcti.extbatch.psdk.CmFolderResolver.FolderIndex;
import com.gcti.extbatch.psdk.CmFolderResolver.ResolvedFolder;
import com.gcti.extbatch.psdk.CustomerCatalogService.CustomerRecord;
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
import org.springframework.stereotype.Service;

@Service
public class LookupService {

    private final CmCatalogSnapshotLoader catalogLoader;
    private final CmFolderResolver folderResolver;
    private final GenesysSessionManager sessionManager;

    public LookupService(
            CmCatalogSnapshotLoader catalogLoader,
            CmFolderResolver folderResolver,
            GenesysSessionManager sessionManager) {
        this.catalogLoader = catalogLoader;
        this.folderResolver = folderResolver;
        this.sessionManager = sessionManager;
    }

    public LookupSearchResponse search(LookupSearchRequest req) throws Exception {
        long startNs = System.nanoTime();
        String queryRaw = req != null && req.query != null ? req.query.trim() : "";
        String customerFilter = req != null && req.customerName != null ? req.customerName.trim() : "";
        String catFilter = req != null && req.category != null ? req.category.trim() : "ALL";
        boolean exact = req != null && req.exactMatch;
        boolean refresh = req != null && req.refresh;
        int limit = (req != null && req.limit > 0) ? Math.min(req.limit, 5000) : 1000;

        LoadedCatalog cat = catalogLoader.load(refresh, true);

        Set<String> expandedNumbers = new LinkedHashSet<>();
        if (!queryRaw.isEmpty()) {
            try {
                if (queryRaw.matches("[0-9,~\\s\\r\\n]+")) {
                    expandedNumbers.addAll(NumberSpecParser.expand(queryRaw));
                }
            } catch (Exception ignored) {
                // Treat as plain text query if invalid range syntax
            }
        }
        String queryLower = queryRaw.toLowerCase(Locale.ROOT);
        boolean hasAllCustomers = customerFilter.isEmpty() || "ALL".equalsIgnoreCase(customerFilter) || "전체".equals(customerFilter);

        LookupSearchResponse resp = new LookupSearchResponse();
        resp.query = queryRaw;
        resp.customerName = customerFilter;
        resp.fromCache = cat.fromCache;
        resp.totalLoaded = cat.totalObjectCount();

        List<LookupItemDto> candidates = new ArrayList<>();

        // 1. DNs
        if (includeCategory(catFilter, "DN")) {
            for (CfgDN dn : cat.dns) {
                LookupItemDto item = toDnLookupItem(dn, cat);
                if (matchesFilter(item, queryLower, expandedNumbers, exact, catFilter, hasAllCustomers ? "" : customerFilter)) {
                    candidates.add(item);
                }
            }
        }

        // 2. Places
        if (includeCategory(catFilter, "Place")) {
            for (CfgPlace p : cat.places) {
                LookupItemDto item = toPlaceLookupItem(p, cat);
                if (matchesFilter(item, queryLower, expandedNumbers, exact, catFilter, hasAllCustomers ? "" : customerFilter)) {
                    candidates.add(item);
                }
            }
        }

        // 3. Agent Logins
        if (includeCategory(catFilter, "AgentLogin")) {
            for (CfgAgentLogin al : cat.logins) {
                LookupItemDto item = toLoginLookupItem(al, cat);
                if (matchesFilter(item, queryLower, expandedNumbers, exact, catFilter, hasAllCustomers ? "" : customerFilter)) {
                    candidates.add(item);
                }
            }
        }

        // 4. Persons
        if (includeCategory(catFilter, "Person")) {
            for (CfgPerson p : cat.persons) {
                LookupItemDto item = toPersonLookupItem(p, cat);
                if (matchesFilter(item, queryLower, expandedNumbers, exact, catFilter, hasAllCustomers ? "" : customerFilter)) {
                    candidates.add(item);
                }
            }
        }

        // 5. Groups & Scripts
        if (includeCategory(catFilter, "Group")) {
            for (CfgPlaceGroup pg : cat.placeGroups) {
                LookupItemDto item = toPlaceGroupLookupItem(pg, cat);
                if (matchesFilter(item, queryLower, expandedNumbers, exact, catFilter, hasAllCustomers ? "" : customerFilter)) {
                    candidates.add(item);
                }
            }
            for (CfgAgentGroup ag : cat.agentGroups) {
                LookupItemDto item = toAgentGroupLookupItem(ag, cat);
                if (matchesFilter(item, queryLower, expandedNumbers, exact, catFilter, hasAllCustomers ? "" : customerFilter)) {
                    candidates.add(item);
                }
            }
            for (CfgDNGroup dg : cat.dnGroups) {
                LookupItemDto item = toDnGroupLookupItem(dg, cat);
                if (matchesFilter(item, queryLower, expandedNumbers, exact, catFilter, hasAllCustomers ? "" : customerFilter)) {
                    candidates.add(item);
                }
            }
        }

        if (includeCategory(catFilter, "Script")) {
            for (CfgScript sc : cat.scripts) {
                LookupItemDto item = toScriptLookupItem(sc, cat);
                if (matchesFilter(item, queryLower, expandedNumbers, exact, catFilter, hasAllCustomers ? "" : customerFilter)) {
                    candidates.add(item);
                }
            }
        }

        Map<String, Integer> counts = new LinkedHashMap<>();
        for (LookupItemDto item : candidates) {
            counts.put(item.category, counts.getOrDefault(item.category, 0) + 1);
        }
        resp.categoryCounts = counts;
        resp.totalMatched = candidates.size();
        if (candidates.size() > limit) {
            resp.items = new ArrayList<>(candidates.subList(0, limit));
        } else {
            resp.items = candidates;
        }
        resp.elapsedSeconds = round2((System.nanoTime() - startNs) / 1_000_000_000.0);
        resp.logs.add(String.format(Locale.ROOT,
                "[통합 조회] 검색어='%s', 범위=%s, 전체 로드=%d건, 매칭=%d건 (표시 %d건, %.2f초%s)",
                queryRaw.isEmpty() ? "*" : queryRaw,
                hasAllCustomers ? "전체" : customerFilter,
                resp.totalLoaded,
                resp.totalMatched,
                resp.items.size(),
                resp.elapsedSeconds,
                cat.fromCache ? ", 캐시" : ", 라이브"));
        return resp;
    }

    public CenterAuditResponse auditCenter(CenterAuditRequest req) throws Exception {
        long startNs = System.nanoTime();
        String targetCustomer = req != null && req.customerName != null ? req.customerName.trim() : "";
        boolean isAll = targetCustomer.isEmpty() || "ALL".equalsIgnoreCase(targetCustomer) || "전체".equals(targetCustomer);
        boolean refresh = req == null || req.refresh;

        LoadedCatalog cat = catalogLoader.load(refresh, true);
        String sw = sessionManager.getSwitchName();
        FolderIndex idx = cat.folderIdx;

        // Index CM objects by folderDbid for fast exact center lookup
        Map<Integer, List<CfgDN>> dnsByFolder = new HashMap<>();
        for (CfgDN dn : cat.dns) {
            if (dn.getFolderId() != null) {
                dnsByFolder.computeIfAbsent(dn.getFolderId(), k -> new ArrayList<>()).add(dn);
            }
        }
        Map<Integer, List<CfgPlace>> placesByFolder = new HashMap<>();
        for (CfgPlace p : cat.places) {
            if (p.getFolderId() != null) {
                placesByFolder.computeIfAbsent(p.getFolderId(), k -> new ArrayList<>()).add(p);
            }
        }
        Map<Integer, List<CfgAgentLogin>> loginsByFolder = new HashMap<>();
        for (CfgAgentLogin al : cat.logins) {
            if (al.getFolderId() != null) {
                loginsByFolder.computeIfAbsent(al.getFolderId(), k -> new ArrayList<>()).add(al);
            }
        }
        Map<Integer, List<CfgPerson>> personsByFolder = new HashMap<>();
        for (CfgPerson p : cat.persons) {
            if (p.getFolderId() != null) {
                personsByFolder.computeIfAbsent(p.getFolderId(), k -> new ArrayList<>()).add(p);
            }
        }
        Map<String, CfgPlaceGroup> pgByNameLower = new HashMap<>();
        for (CfgPlaceGroup pg : cat.placeGroups) {
            if (pg.getGroupInfo() != null && pg.getGroupInfo().getName() != null) {
                pgByNameLower.put(pg.getGroupInfo().getName().trim().toLowerCase(Locale.ROOT), pg);
            }
        }

        List<CustomerRecord> targets = new ArrayList<>();
        if (isAll) {
            targets.addAll(cat.customers);
        } else {
            CustomerRecord hit = cat.customersByNameLower.get(targetCustomer.toLowerCase(Locale.ROOT));
            if (hit == null) {
                throw new IllegalArgumentException("cm_customers.json에서 '" + targetCustomer + "' 고객사를 찾을 수 없습니다.");
            }
            targets.add(hit);
        }

        CenterAuditResponse resp = new CenterAuditResponse();
        resp.customerName = isAll ? "전체" : targets.get(0).name;
        resp.totalCustomersAudited = targets.size();

        int totalErrors = 0;
        int totalWarns = 0;
        int totalPairs = 0;
        int okPairs = 0;

        for (CustomerRecord cust : targets) {
            CfgFolder loginFolder = findSwitchSubFolder(idx, sw, "Agent Logins", cust.agentLogin);
            CfgFolder acdFolder = findSwitchDnSubFolder(idx, sw, "ACDPosition", cust.acdPosition);
            CfgFolder dnFolder = findSwitchDnSubFolder(idx, sw, "AGENT_DN", cust.agentDn);
            CfgFolder rsiFolder = findSwitchDnSubFolder(idx, sw, "RSI", cust.rsi);
            CfgFolder placeFolder = idx.findBySegments(folderResolver.makePath(
                    Arrays.asList("Configuration", "Environment", "Places", "AGENT"), cust.place));
            ResolvedFolder personResolved = folderResolver.resolvePersonsFolder(idx, cust);
            CfgFolder personFolder = personResolved != null ? personResolved.folder : null;

            List<CfgDN> folderAgentDns = new ArrayList<>();
            List<CfgDN> folderAcds = new ArrayList<>();
            List<CfgDN> folderRsis = new ArrayList<>();
            List<CfgPlace> folderPlaces = new ArrayList<>();
            List<CfgAgentLogin> folderLogins = new ArrayList<>();
            List<CfgPerson> folderPersons = new ArrayList<>();

            collectSubtreeDNs(idx, dnFolder, dnsByFolder, folderAgentDns, CfgDNType.CFGExtension);
            collectSubtreeDNs(idx, acdFolder, dnsByFolder, folderAcds, CfgDNType.CFGACDPosition);
            collectSubtreeDNs(idx, rsiFolder, dnsByFolder, folderRsis, null);
            collectSubtreeObjects(idx, placeFolder, placesByFolder, folderPlaces);
            collectSubtreeObjects(idx, loginFolder, loginsByFolder, folderLogins);
            collectSubtreeObjects(idx, personFolder, personsByFolder, folderPersons);

            int custIssues = 0;
            boolean foldersOk = true;
            if (cust.agentDn != null && !cust.agentDn.isEmpty() && dnFolder == null) {
                foldersOk = false;
                custIssues++;
                totalErrors++;
                resp.issues.add(makeIssue("ERROR", cust.name, "MISSING_FOLDER", "AGENT_DN",
                        "Agent DN 폴더 미존재: " + String.join(" > ", cust.agentDn), ""));
            }
            if (cust.place != null && !cust.place.isEmpty() && placeFolder == null) {
                foldersOk = false;
                custIssues++;
                totalErrors++;
                resp.issues.add(makeIssue("ERROR", cust.name, "MISSING_FOLDER", "Places",
                        "Place 폴더 미존재: " + String.join(" > ", cust.place), ""));
            }
            if (cust.agentLogin != null && !cust.agentLogin.isEmpty() && loginFolder == null) {
                foldersOk = false;
                custIssues++;
                totalErrors++;
                resp.issues.add(makeIssue("ERROR", cust.name, "MISSING_FOLDER", "Agent Logins",
                        "Agent Logins 폴더 미존재: " + String.join(" > ", cust.agentLogin), ""));
            }
            if (cust.acdPosition != null && !cust.acdPosition.isEmpty() && acdFolder == null) {
                foldersOk = false;
                custIssues++;
                totalErrors++;
                resp.issues.add(makeIssue("ERROR", cust.name, "MISSING_FOLDER", "ACDPosition",
                        "ACDPosition 폴더 미존재: " + String.join(" > ", cust.acdPosition), ""));
            }
            if (Boolean.TRUE.equals(cust.useIvr)) {
                CfgPlaceGroup pg = pgByNameLower.get(cust.name.trim().toLowerCase(Locale.ROOT));
                if (pg == null) {
                    custIssues++;
                    totalWarns++;
                    resp.issues.add(makeIssue("WARN", cust.name, "MISSING_PLACE_GROUP", cust.name,
                            "useIvr=true 설정이나 CfgPlaceGroup '" + cust.name + "' 미존재", "Configuration > Environment > Place Groups"));
                }
            }

            // Analyze Ext DN <-> Place pairing
            Map<String, CfgDN> dnByNum = new HashMap<>();
            for (CfgDN dn : folderAgentDns) {
                dnByNum.put(CmCatalogSnapshotLoader.safe(dn.getNumber()), dn);
            }
            Map<String, CfgPlace> placeByName = new HashMap<>();
            for (CfgPlace p : folderPlaces) {
                placeByName.put(CmCatalogSnapshotLoader.safe(p.getName()), p);
            }

            Set<String> handledPlaces = new HashSet<>();
            Set<String> extNums = new LinkedHashSet<>(dnByNum.keySet());
            for (CfgPlace p : folderPlaces) {
                String pn = CmCatalogSnapshotLoader.safe(p.getName());
                if ((pn.startsWith("P") || pn.startsWith("p")) && pn.length() > 1) {
                    String candidateNum = pn.substring(1);
                    if (candidateNum.matches("\\d+")) {
                        extNums.add(candidateNum);
                    }
                }
            }
            List<String> sortedExts = new ArrayList<>(extNums);
            Collections.sort(sortedExts);

            for (String num : sortedExts) {
                CfgDN dn = dnByNum.get(num);
                String expectedPlaceName = "P" + num;
                List<CfgPlace> linkedPlaces = dn != null && dn.getDBID() != null
                        ? cat.dnDbidToPlaces.getOrDefault(dn.getDBID(), Collections.emptyList())
                        : Collections.emptyList();
                CfgPlace matchedPlace = placeByName.get(expectedPlaceName);
                if (matchedPlace == null && !linkedPlaces.isEmpty()) {
                    matchedPlace = linkedPlaces.get(0);
                }
                if (matchedPlace != null) {
                    handledPlaces.add(CmCatalogSnapshotLoader.safe(matchedPlace.getName()));
                }

                boolean dnExists = dn != null;
                boolean placeExists = matchedPlace != null;
                boolean dnLinkedInPlace = dnExists && placeExists && matchedPlace.getDNDBIDs() != null
                        && matchedPlace.getDNDBIDs().contains(dn.getDBID());

                String status = "OK";
                String note = "정상 연결";
                if (!dnExists && placeExists) {
                    status = "Missing";
                    note = "Place 존재하나 대응 Agent DN 누락";
                    custIssues++;
                    totalWarns++;
                    resp.issues.add(makeIssue("WARN", cust.name, "PLACE_WITHOUT_DN", expectedPlaceName,
                            note, cat.resolveFolderDisplayPath(matchedPlace.getObjectPath(), matchedPlace.getFolderId())));
                } else if (dnExists && !placeExists) {
                    status = "Warn";
                    note = "Agent DN 존재하나 대응 Place('" + expectedPlaceName + "') 미생성";
                    custIssues++;
                    totalWarns++;
                    resp.issues.add(makeIssue("WARN", cust.name, "DN_WITHOUT_PLACE", num,
                            note, cat.resolveFolderDisplayPath(dn.getObjectPath(), dn.getFolderId())));
                } else if (dnExists && placeExists && !dnLinkedInPlace) {
                    status = "Warn";
                    note = "Place에 DN DBID 미연결 (Shortcut 누락)";
                    custIssues++;
                    totalWarns++;
                    resp.issues.add(makeIssue("WARN", cust.name, "PLACE_DN_UNLINKED", matchedPlace.getName(),
                            "Place에 DN " + num + " 연결 누락", cat.resolveFolderDisplayPath(matchedPlace.getObjectPath(), matchedPlace.getFolderId())));
                } else if (linkedPlaces.size() > 1) {
                    status = "Warn";
                    note = "DN이 복수 Place에 중복 연결됨 (" + linkedPlaces.size() + "곳)";
                    custIssues++;
                    totalWarns++;
                    resp.issues.add(makeIssue("WARN", cust.name, "MULTI_PLACE_DN", num,
                            note, cat.resolveFolderDisplayPath(dn.getObjectPath(), dn.getFolderId())));
                }

                totalPairs++;
                if ("OK".equals(status)) okPairs++;

                if (!isAll) {
                    CenterPairRowDto row = new CenterPairRowDto();
                    row.kind = "Ext/Place";
                    row.customer = cust.name;
                    row.number = num;
                    row.primaryExists = dnExists;
                    row.primaryPath = dnExists ? cat.resolveFolderDisplayPath(dn.getObjectPath(), dn.getFolderId()) : "-";
                    row.secondaryKey = matchedPlace != null ? matchedPlace.getName() : expectedPlaceName;
                    row.secondaryExists = placeExists;
                    row.secondaryPath = placeExists ? cat.resolveFolderDisplayPath(matchedPlace.getObjectPath(), matchedPlace.getFolderId()) : "-";
                    if (placeExists && matchedPlace.getDBID() != null) {
                        List<String> pgs = cat.placeDbidToPlaceGroups.getOrDefault(matchedPlace.getDBID(), Collections.emptyList());
                        row.linkedInfo = pgs.isEmpty() ? "-" : ("PlaceGroup: " + String.join(", ", pgs));
                    } else {
                        row.linkedInfo = "-";
                    }
                    row.status = status;
                    row.note = note;
                    resp.pairRows.add(row);
                }
            }

            // Non-standard Places in folder with 0 DN links
            for (CfgPlace p : folderPlaces) {
                String pn = CmCatalogSnapshotLoader.safe(p.getName());
                if (!handledPlaces.contains(pn) && (p.getDNDBIDs() == null || p.getDNDBIDs().isEmpty())) {
                    custIssues++;
                    totalWarns++;
                    resp.issues.add(makeIssue("WARN", cust.name, "EMPTY_PLACE", pn,
                            "연결된 DN이 없는 빈 Place", cat.resolveFolderDisplayPath(p.getObjectPath(), p.getFolderId())));
                }
            }

            // Analyze Agent Login <-> ACD Position <-> Person pairing
            Map<String, CfgAgentLogin> loginByCode = new HashMap<>();
            for (CfgAgentLogin al : folderLogins) {
                loginByCode.put(CmCatalogSnapshotLoader.safe(al.getLoginCode()), al);
            }
            Map<String, CfgDN> acdByNum = new HashMap<>();
            for (CfgDN acd : folderAcds) {
                acdByNum.put(CmCatalogSnapshotLoader.safe(acd.getNumber()), acd);
            }
            Set<String> allCtiCodes = new LinkedHashSet<>(loginByCode.keySet());
            allCtiCodes.addAll(acdByNum.keySet());
            List<String> sortedCti = new ArrayList<>(allCtiCodes);
            Collections.sort(sortedCti);

            for (String code : sortedCti) {
                CfgAgentLogin al = loginByCode.get(code);
                CfgDN acd = acdByNum.get(code);
                List<CfgPerson> assigned = al != null && al.getDBID() != null
                        ? cat.loginDbidToPersons.getOrDefault(al.getDBID(), Collections.emptyList())
                        : Collections.emptyList();

                String status = "OK";
                String note = assigned.isEmpty() ? "가용 Login (미할당)" : "할당됨";
                if (al == null && acd != null) {
                    status = "Missing";
                    note = "ACD Position만 존재하고 Agent Login 누락";
                    custIssues++;
                    totalWarns++;
                    resp.issues.add(makeIssue("WARN", cust.name, "ACD_WITHOUT_LOGIN", code,
                            note, cat.resolveFolderDisplayPath(acd.getObjectPath(), acd.getFolderId())));
                } else if (al != null && acd == null) {
                    status = "Warn";
                    note = "Agent Login만 존재하고 ACD Position 누락";
                    custIssues++;
                    totalWarns++;
                    resp.issues.add(makeIssue("WARN", cust.name, "LOGIN_WITHOUT_ACD", code,
                            note, cat.resolveFolderDisplayPath(al.getObjectPath(), al.getFolderId())));
                } else if (assigned.size() > 1) {
                    status = "Warn";
                    note = "복수 Person이 동일 Login 공유 (" + assigned.size() + "명)";
                    custIssues++;
                    totalWarns++;
                    resp.issues.add(makeIssue("WARN", cust.name, "MULTI_PERSON_LOGIN", code,
                            note, cat.resolveFolderDisplayPath(al.getObjectPath(), al.getFolderId())));
                }

                totalPairs++;
                if ("OK".equals(status)) okPairs++;

                if (!isAll) {
                    CenterPairRowDto row = new CenterPairRowDto();
                    row.kind = "CTI/Person";
                    row.customer = cust.name;
                    row.number = code;
                    row.primaryExists = (al != null);
                    row.primaryPath = al != null ? cat.resolveFolderDisplayPath(al.getObjectPath(), al.getFolderId()) : "-";
                    row.secondaryKey = acd != null ? ("ACD:" + acd.getNumber()) : ("ACD:" + code);
                    row.secondaryExists = (acd != null);
                    row.secondaryPath = acd != null ? cat.resolveFolderDisplayPath(acd.getObjectPath(), acd.getFolderId()) : "-";
                    if (!assigned.isEmpty()) {
                        List<String> pNames = new ArrayList<>();
                        for (CfgPerson p : assigned) {
                            pNames.add(formatPersonLabel(p));
                        }
                        row.linkedInfo = String.join(", ", pNames);
                    } else {
                        row.linkedInfo = "(미할당)";
                    }
                    row.status = status;
                    row.note = note;
                    resp.pairRows.add(row);
                }
            }

            CenterAuditSummaryRowDto sumRow = new CenterAuditSummaryRowDto();
            sumRow.customer = cust.name;
            sumRow.floor = cust.floor != null ? cust.floor : "";
            sumRow.agentDnCount = folderAgentDns.size();
            sumRow.placeCount = folderPlaces.size();
            sumRow.agentLoginCount = folderLogins.size();
            sumRow.acdPosCount = folderAcds.size();
            sumRow.personCount = folderPersons.size();
            sumRow.rsiCount = folderRsis.size();
            sumRow.issueCount = custIssues;
            sumRow.foldersOk = foldersOk;
            sumRow.note = cust.note != null ? cust.note : "";
            resp.centerSummaries.add(sumRow);
        }

        // For specific center view, sort pairRows nicely; for All view sort summaries by issueCount desc
        if (isAll) {
            resp.centerSummaries.sort(Comparator.comparingInt((CenterAuditSummaryRowDto r) -> -r.issueCount)
                    .thenComparing(r -> r.customer != null ? r.customer : ""));
        }

        resp.stats.put("centers", targets.size());
        resp.stats.put("totalPairs", totalPairs);
        resp.stats.put("okPairs", okPairs);
        resp.stats.put("totalIssues", resp.issues.size());
        resp.stats.put("errors", totalErrors);
        resp.stats.put("warns", totalWarns);
        resp.elapsedSeconds = round2((System.nanoTime() - startNs) / 1_000_000_000.0);
        resp.logs.add(String.format(Locale.ROOT,
                "[정합성 감사] 대상=%s (%d개 센터), 페어 점검=%d건 (정상 %d), 이슈=%d건 (오류 %d / 경고 %d, %.2f초)",
                resp.customerName, targets.size(), totalPairs, okPairs, resp.issues.size(), totalErrors, totalWarns, resp.elapsedSeconds));
        return resp;
    }

    private CfgFolder findSwitchSubFolder(FolderIndex idx, String sw, String category, List<String> sub) {
        if (sub == null || sub.isEmpty()) return null;
        return idx.findBySegments(folderResolver.makePath(
                Arrays.asList("Configuration", "Environment", "Switches", sw, category), sub));
    }

    private CfgFolder findSwitchDnSubFolder(FolderIndex idx, String sw, String dnCategory, List<String> sub) {
        if (sub == null || sub.isEmpty()) return null;
        return idx.findBySegments(folderResolver.makePath(
                Arrays.asList("Configuration", "Environment", "Switches", sw, "DNs", dnCategory), sub));
    }

    private static void collectSubtreeDNs(
            FolderIndex idx,
            CfgFolder root,
            Map<Integer, List<CfgDN>> dnsByFolder,
            List<CfgDN> out,
            CfgDNType filterType) {
        if (root == null || root.getDBID() == null) return;
        for (CfgDN dn : dnsByFolder.getOrDefault(root.getDBID(), Collections.emptyList())) {
            if (filterType == null || dn.getType() == filterType) {
                out.add(dn);
            }
        }
        for (CfgFolder child : idx.getChildren(root)) {
            collectSubtreeDNs(idx, child, dnsByFolder, out, filterType);
        }
    }

    private static <T> void collectSubtreeObjects(
            FolderIndex idx,
            CfgFolder root,
            Map<Integer, List<T>> byFolder,
            List<T> out) {
        if (root == null || root.getDBID() == null) return;
        out.addAll(byFolder.getOrDefault(root.getDBID(), Collections.emptyList()));
        for (CfgFolder child : idx.getChildren(root)) {
            collectSubtreeObjects(idx, child, byFolder, out);
        }
    }

    private static CenterAuditIssueDto makeIssue(String sev, String cust, String code, String target, String detail, String path) {
        CenterAuditIssueDto dto = new CenterAuditIssueDto();
        dto.severity = sev;
        dto.customer = cust;
        dto.code = code;
        dto.target = target;
        dto.detail = detail;
        dto.folderPath = path;
        return dto;
    }

    private static LookupItemDto toDnLookupItem(CfgDN dn, LoadedCatalog cat) {
        LookupItemDto item = new LookupItemDto();
        item.category = CmCatalogSnapshotLoader.mapDnCategory(dn.getType());
        item.subType = dn.getType() != null ? dn.getType().name() : "";
        item.dbid = dn.getDBID();
        item.key = CmCatalogSnapshotLoader.safe(dn.getNumber());
        item.name = !CmCatalogSnapshotLoader.safe(dn.getName()).isEmpty()
                ? CmCatalogSnapshotLoader.safe(dn.getName())
                : item.key;
        item.switchName = cat.resolveSwitchName(dn.getSwitchDBID());
        item.folderPath = cat.resolveFolderDisplayPath(dn.getObjectPath(), dn.getFolderId());
        item.customerInferred = cat.inferCustomer(dn.getFolderId(), null);
        item.state = dn.getState() != null ? dn.getState().name() : "";
        item.id = "DN:" + dn.getDBID();

        List<String> relParts = new ArrayList<>();
        List<CfgPlace> linkedPlaces = dn.getDBID() != null
                ? cat.dnDbidToPlaces.getOrDefault(dn.getDBID(), Collections.emptyList())
                : Collections.emptyList();
        for (CfgPlace p : linkedPlaces) {
            LookupRelationDto r = new LookupRelationDto();
            r.relType = "Place";
            r.targetCategory = "Place";
            r.targetKey = CmCatalogSnapshotLoader.safe(p.getName());
            r.targetName = r.targetKey;
            r.targetPath = cat.resolveFolderDisplayPath(p.getObjectPath(), p.getFolderId());
            item.relations.add(r);
            relParts.add("Place:" + r.targetKey);

            if (p.getDBID() != null) {
                for (String pg : cat.placeDbidToPlaceGroups.getOrDefault(p.getDBID(), Collections.emptyList())) {
                    LookupRelationDto rPg = new LookupRelationDto();
                    rPg.relType = "PlaceGroup";
                    rPg.targetCategory = "Place Group";
                    rPg.targetKey = pg;
                    rPg.targetName = pg;
                    item.relations.add(rPg);
                    relParts.add("PG:" + pg);
                }
            }
        }
        if (dn.getDBID() != null) {
            for (String dg : cat.dnDbidToDnGroups.getOrDefault(dn.getDBID(), Collections.emptyList())) {
                LookupRelationDto rDg = new LookupRelationDto();
                rDg.relType = "DNGroup";
                rDg.targetCategory = "DN Group";
                rDg.targetKey = dg;
                rDg.targetName = dg;
                item.relations.add(rDg);
                relParts.add("DNG:" + dg);
            }
        }
        item.relationSummary = relParts.isEmpty() ? "-" : String.join(", ", relParts);

        if (dn.getType() == CfgDNType.CFGExtension && item.folderPath.contains("AGENT_DN")) {
            if (linkedPlaces.isEmpty()) {
                item.anomalies.add("Place 미연결");
            } else if (linkedPlaces.size() > 1) {
                item.anomalies.add("복수 Place 연결(" + linkedPlaces.size() + ")");
            }
        }

        item.attributes.put("DBID", String.valueOf(dn.getDBID()));
        item.attributes.put("Number", item.key);
        item.attributes.put("Name", CmCatalogSnapshotLoader.safe(dn.getName()));
        item.attributes.put("DNType", item.subType);
        item.attributes.put("Switch", item.switchName);
        item.attributes.put("RouteType", dn.getRouteType() != null ? dn.getRouteType().name() : "");
        item.attributes.put("SwitchSpecificType", String.valueOf(dn.getSwitchSpecificType()));
        item.attributes.put("UseOverride", dn.getUseOverride() != null ? dn.getUseOverride().name() : "");
        item.attributes.put("Override", CmCatalogSnapshotLoader.safe(dn.getOverride()));
        item.attributes.put("Association", CmCatalogSnapshotLoader.safe(dn.getAssociation()));
        item.attributes.put("UserProperties", CmCatalogSnapshotLoader.flattenUserProperties(dn.getUserProperties()));
        return item;
    }

    private static LookupItemDto toPlaceLookupItem(CfgPlace p, LoadedCatalog cat) {
        LookupItemDto item = new LookupItemDto();
        item.category = "Place";
        item.subType = "CfgPlace";
        item.dbid = p.getDBID();
        item.key = CmCatalogSnapshotLoader.safe(p.getName());
        item.name = item.key;
        item.switchName = "-";
        item.folderPath = cat.resolveFolderDisplayPath(p.getObjectPath(), p.getFolderId());
        item.customerInferred = cat.inferCustomer(p.getFolderId(), null);
        item.state = p.getState() != null ? p.getState().name() : "";
        item.id = "PLACE:" + p.getDBID();

        List<String> relParts = new ArrayList<>();
        if (p.getDNDBIDs() != null) {
            for (Integer dnDbid : p.getDNDBIDs()) {
                CfgDN dn = cat.dnByDbid.get(dnDbid);
                LookupRelationDto r = new LookupRelationDto();
                r.relType = "DN";
                r.targetCategory = dn != null ? CmCatalogSnapshotLoader.mapDnCategory(dn.getType()) : "DN";
                r.targetKey = dn != null ? CmCatalogSnapshotLoader.safe(dn.getNumber()) : ("DBID#" + dnDbid);
                r.targetName = dn != null && dn.getType() != null ? dn.getType().name() : "";
                r.targetPath = dn != null ? cat.resolveFolderDisplayPath(dn.getObjectPath(), dn.getFolderId()) : "";
                item.relations.add(r);
                relParts.add("DN:" + r.targetKey);
            }
        }
        if (p.getDBID() != null) {
            for (String pg : cat.placeDbidToPlaceGroups.getOrDefault(p.getDBID(), Collections.emptyList())) {
                LookupRelationDto rPg = new LookupRelationDto();
                rPg.relType = "PlaceGroup";
                rPg.targetCategory = "Place Group";
                rPg.targetKey = pg;
                rPg.targetName = pg;
                item.relations.add(rPg);
                relParts.add("PG:" + pg);
            }
        }
        item.relationSummary = relParts.isEmpty() ? "-" : String.join(", ", relParts);
        if (p.getDNDBIDs() == null || p.getDNDBIDs().isEmpty()) {
            item.anomalies.add("DN 미연결");
        }

        item.attributes.put("DBID", String.valueOf(p.getDBID()));
        item.attributes.put("Name", item.key);
        item.attributes.put("LinkedDNs", item.relationSummary);
        item.attributes.put("UserProperties", CmCatalogSnapshotLoader.flattenUserProperties(p.getUserProperties()));
        return item;
    }

    private static LookupItemDto toLoginLookupItem(CfgAgentLogin al, LoadedCatalog cat) {
        LookupItemDto item = new LookupItemDto();
        item.category = "Agent Login";
        item.subType = "CfgAgentLogin";
        item.dbid = al.getDBID();
        item.key = CmCatalogSnapshotLoader.safe(al.getLoginCode());
        item.name = item.key;
        item.switchName = cat.resolveSwitchName(al.getSwitchDBID());
        item.folderPath = cat.resolveFolderDisplayPath(al.getObjectPath(), al.getFolderId());
        item.customerInferred = cat.inferCustomer(al.getFolderId(), null);
        item.state = al.getState() != null ? al.getState().name() : "";
        item.id = "LOGIN:" + al.getDBID();

        List<String> relParts = new ArrayList<>();
        List<CfgPerson> persons = al.getDBID() != null
                ? cat.loginDbidToPersons.getOrDefault(al.getDBID(), Collections.emptyList())
                : Collections.emptyList();
        for (CfgPerson p : persons) {
            LookupRelationDto r = new LookupRelationDto();
            r.relType = "Person";
            r.targetCategory = "Person";
            r.targetKey = CmCatalogSnapshotLoader.safe(p.getUserName());
            r.targetName = formatPersonLabel(p);
            r.targetPath = cat.resolveFolderDisplayPath(p.getObjectPath(), p.getFolderId());
            item.relations.add(r);
            relParts.add(r.targetName);
        }
        item.relationSummary = relParts.isEmpty() ? "(미할당)" : String.join(", ", relParts);
        if (persons.size() > 1) {
            item.anomalies.add("복수 Person 공유(" + persons.size() + ")");
        }

        item.attributes.put("DBID", String.valueOf(al.getDBID()));
        item.attributes.put("LoginCode", item.key);
        item.attributes.put("Switch", item.switchName);
        item.attributes.put("UseOverride", al.getUseOverride() != null ? al.getUseOverride().name() : "");
        item.attributes.put("Override", CmCatalogSnapshotLoader.safe(al.getOverride()));
        item.attributes.put("AssignedPersons", item.relationSummary);
        item.attributes.put("UserProperties", CmCatalogSnapshotLoader.flattenUserProperties(al.getUserProperties()));
        return item;
    }

    private static LookupItemDto toPersonLookupItem(CfgPerson p, LoadedCatalog cat) {
        LookupItemDto item = new LookupItemDto();
        item.category = "Person";
        boolean isAgent = p.getIsAgent() == CfgFlag.CFGTrue;
        item.subType = isAgent ? "Agent" : "Non-Agent";
        item.dbid = p.getDBID();
        item.key = CmCatalogSnapshotLoader.safe(p.getUserName());
        String fn = CmCatalogSnapshotLoader.safe(p.getFirstName());
        String ln = CmCatalogSnapshotLoader.safe(p.getLastName());
        String eid = CmCatalogSnapshotLoader.safe(p.getEmployeeID());
        item.name = (!fn.isEmpty() ? fn : item.key) + (eid.isEmpty() ? "" : " (" + eid + ")");
        item.switchName = "-";
        item.folderPath = cat.resolveFolderDisplayPath(p.getObjectPath(), p.getFolderId());
        item.customerInferred = cat.inferCustomer(p.getFolderId(), null);
        item.state = p.getState() != null ? p.getState().name() : "";
        item.id = "PERSON:" + p.getDBID();

        List<String> relParts = new ArrayList<>();
        List<String> loginCodes = new ArrayList<>();
        if (p.getAgentInfo() != null && p.getAgentInfo().getAgentLogins() != null) {
            for (CfgAgentLoginInfo ali : p.getAgentInfo().getAgentLogins()) {
                Integer lDbid = ali.getAgentLoginDBID();
                CfgAgentLogin al = lDbid != null ? cat.loginByDbid.get(lDbid) : null;
                String code = al != null ? CmCatalogSnapshotLoader.safe(al.getLoginCode()) : ("DBID#" + lDbid);
                loginCodes.add(code);
                LookupRelationDto r = new LookupRelationDto();
                r.relType = "AgentLogin";
                r.targetCategory = "Agent Login";
                r.targetKey = code;
                r.targetName = al != null ? cat.resolveSwitchName(al.getSwitchDBID()) : "";
                r.targetPath = al != null ? cat.resolveFolderDisplayPath(al.getObjectPath(), al.getFolderId()) : "";
                item.relations.add(r);
            }
        }
        if (!loginCodes.isEmpty()) {
            relParts.add("Login:" + String.join("/", loginCodes));
        }

        if (p.getAgentInfo() != null && p.getAgentInfo().getPlaceDBID() != null && p.getAgentInfo().getPlaceDBID() > 0) {
            CfgPlace defPlace = cat.placeByDbid.get(p.getAgentInfo().getPlaceDBID());
            if (defPlace != null) {
                LookupRelationDto rp = new LookupRelationDto();
                rp.relType = "Place";
                rp.targetCategory = "Place";
                rp.targetKey = CmCatalogSnapshotLoader.safe(defPlace.getName());
                rp.targetName = "Default Place";
                rp.targetPath = cat.resolveFolderDisplayPath(defPlace.getObjectPath(), defPlace.getFolderId());
                item.relations.add(rp);
                relParts.add("DefaultPlace:" + rp.targetKey);
            }
        }

        if (p.getDBID() != null) {
            for (String ag : cat.personDbidToAgentGroups.getOrDefault(p.getDBID(), Collections.emptyList())) {
                LookupRelationDto rag = new LookupRelationDto();
                rag.relType = "AgentGroup";
                rag.targetCategory = "Agent Group";
                rag.targetKey = ag;
                rag.targetName = ag;
                item.relations.add(rag);
                relParts.add("AG:" + ag);
            }
        }

        String skills = CmCatalogSnapshotLoader.formatSkillSummary(p, cat.skillNameByDbid);
        if (!skills.isEmpty()) {
            relParts.add("Skill:" + skills);
        }
        item.relationSummary = relParts.isEmpty() ? "-" : String.join(", ", relParts);

        item.attributes.put("DBID", String.valueOf(p.getDBID()));
        item.attributes.put("UserName", item.key);
        item.attributes.put("EmployeeID", eid);
        item.attributes.put("FirstName", fn);
        item.attributes.put("LastName", ln);
        item.attributes.put("IsAgent", String.valueOf(isAgent));
        item.attributes.put("AgentLogins", String.join(", ", loginCodes));
        item.attributes.put("Skills", skills);
        item.attributes.put("UserProperties", CmCatalogSnapshotLoader.flattenUserProperties(p.getUserProperties()));
        return item;
    }

    private static LookupItemDto toPlaceGroupLookupItem(CfgPlaceGroup pg, LoadedCatalog cat) {
        LookupItemDto item = new LookupItemDto();
        item.category = "Place Group";
        item.subType = "CfgPlaceGroup";
        item.dbid = pg.getDBID();
        item.key = pg.getGroupInfo() != null ? CmCatalogSnapshotLoader.safe(pg.getGroupInfo().getName()) : "";
        item.name = item.key;
        item.switchName = "-";
        item.folderPath = cat.resolveFolderDisplayPath(pg.getObjectPath(), pg.getFolderId());
        item.customerInferred = cat.inferCustomer(pg.getFolderId(), item.key);
        item.state = pg.getGroupInfo() != null && pg.getGroupInfo().getState() != null ? pg.getGroupInfo().getState().name() : "";
        item.id = "PG:" + pg.getDBID();

        List<String> members = new ArrayList<>();
        if (pg.getPlaceDBIDs() != null) {
            for (Integer pDbid : pg.getPlaceDBIDs()) {
                CfgPlace pl = cat.placeByDbid.get(pDbid);
                String pn = pl != null ? CmCatalogSnapshotLoader.safe(pl.getName()) : ("DBID#" + pDbid);
                members.add(pn);
                if (item.relations.size() < 25) {
                    LookupRelationDto r = new LookupRelationDto();
                    r.relType = "Place";
                    r.targetCategory = "Place";
                    r.targetKey = pn;
                    r.targetName = pn;
                    r.targetPath = pl != null ? cat.resolveFolderDisplayPath(pl.getObjectPath(), pl.getFolderId()) : "";
                    item.relations.add(r);
                }
            }
        }
        Collections.sort(members);
        item.relationSummary = "Place " + members.size() + "개" + (members.isEmpty() ? "" : ": " + previewList(members, 6));
        item.attributes.put("DBID", String.valueOf(pg.getDBID()));
        item.attributes.put("Name", item.key);
        item.attributes.put("MemberCount", String.valueOf(members.size()));
        item.attributes.put("Members", String.join(", ", members));
        return item;
    }

    private static LookupItemDto toAgentGroupLookupItem(CfgAgentGroup ag, LoadedCatalog cat) {
        LookupItemDto item = new LookupItemDto();
        item.category = "Agent Group";
        item.subType = "CfgAgentGroup";
        item.dbid = ag.getDBID();
        item.key = ag.getGroupInfo() != null ? CmCatalogSnapshotLoader.safe(ag.getGroupInfo().getName()) : "";
        item.name = item.key;
        item.switchName = "-";
        item.folderPath = cat.resolveFolderDisplayPath(ag.getObjectPath(), ag.getFolderId());
        item.customerInferred = cat.inferCustomer(ag.getFolderId(), item.key);
        item.state = ag.getGroupInfo() != null && ag.getGroupInfo().getState() != null ? ag.getGroupInfo().getState().name() : "";
        item.id = "AG:" + ag.getDBID();

        List<String> members = new ArrayList<>();
        if (ag.getAgentDBIDs() != null) {
            for (Integer aDbid : ag.getAgentDBIDs()) {
                CfgPerson p = cat.personByDbid.get(aDbid);
                String pn = p != null ? formatPersonLabel(p) : ("DBID#" + aDbid);
                members.add(pn);
                if (item.relations.size() < 25 && p != null) {
                    LookupRelationDto r = new LookupRelationDto();
                    r.relType = "Person";
                    r.targetCategory = "Person";
                    r.targetKey = CmCatalogSnapshotLoader.safe(p.getUserName());
                    r.targetName = pn;
                    r.targetPath = cat.resolveFolderDisplayPath(p.getObjectPath(), p.getFolderId());
                    item.relations.add(r);
                }
            }
        }
        Collections.sort(members);
        item.relationSummary = "Agent " + members.size() + "명" + (members.isEmpty() ? "" : ": " + previewList(members, 5));
        item.attributes.put("DBID", String.valueOf(ag.getDBID()));
        item.attributes.put("Name", item.key);
        item.attributes.put("MemberCount", String.valueOf(members.size()));
        item.attributes.put("Members", String.join(", ", members));
        return item;
    }

    private static LookupItemDto toDnGroupLookupItem(CfgDNGroup dg, LoadedCatalog cat) {
        LookupItemDto item = new LookupItemDto();
        item.category = "DN Group";
        item.subType = dg.getType() != null ? dg.getType().name() : "CfgDNGroup";
        item.dbid = dg.getDBID();
        item.key = dg.getGroupInfo() != null ? CmCatalogSnapshotLoader.safe(dg.getGroupInfo().getName()) : "";
        item.name = item.key;
        item.switchName = "-";
        item.folderPath = cat.resolveFolderDisplayPath(dg.getObjectPath(), dg.getFolderId());
        item.customerInferred = cat.inferCustomer(dg.getFolderId(), item.key);
        item.state = dg.getGroupInfo() != null && dg.getGroupInfo().getState() != null ? dg.getGroupInfo().getState().name() : "";
        item.id = "DG:" + dg.getDBID();

        List<String> members = new ArrayList<>();
        if (dg.getDNs() != null) {
            for (CfgDNInfo info : dg.getDNs()) {
                Integer dnDbid = info.getDNDBID();
                CfgDN dn = dnDbid != null ? cat.dnByDbid.get(dnDbid) : null;
                String num = dn != null ? CmCatalogSnapshotLoader.safe(dn.getNumber()) : ("DBID#" + dnDbid);
                members.add(num);
                if (item.relations.size() < 25 && dn != null) {
                    LookupRelationDto r = new LookupRelationDto();
                    r.relType = "DN";
                    r.targetCategory = CmCatalogSnapshotLoader.mapDnCategory(dn.getType());
                    r.targetKey = num;
                    r.targetName = dn.getType() != null ? dn.getType().name() : "";
                    r.targetPath = cat.resolveFolderDisplayPath(dn.getObjectPath(), dn.getFolderId());
                    item.relations.add(r);
                }
            }
        }
        Collections.sort(members);
        item.relationSummary = "DN " + members.size() + "개" + (members.isEmpty() ? "" : ": " + previewList(members, 6));
        item.attributes.put("DBID", String.valueOf(dg.getDBID()));
        item.attributes.put("Name", item.key);
        item.attributes.put("DNGroupType", item.subType);
        item.attributes.put("MemberCount", String.valueOf(members.size()));
        item.attributes.put("Members", String.join(", ", members));
        return item;
    }

    private static LookupItemDto toScriptLookupItem(CfgScript sc, LoadedCatalog cat) {
        LookupItemDto item = new LookupItemDto();
        item.category = "Script";
        item.subType = sc.getType() != null ? sc.getType().name() : "CfgScript";
        item.dbid = sc.getDBID();
        item.key = CmCatalogSnapshotLoader.safe(sc.getName());
        item.name = item.key;
        item.switchName = "-";
        item.folderPath = cat.resolveFolderDisplayPath(sc.getObjectPath(), sc.getFolderId());
        item.customerInferred = cat.inferCustomer(sc.getFolderId(), null);
        item.state = sc.getState() != null ? sc.getState().name() : "";
        item.id = "SCRIPT:" + sc.getDBID();
        item.relationSummary = item.subType;
        item.attributes.put("DBID", String.valueOf(sc.getDBID()));
        item.attributes.put("Name", item.key);
        item.attributes.put("ScriptType", item.subType);
        item.attributes.put("Index", String.valueOf(sc.getIndex()));
        item.attributes.put("UserProperties", CmCatalogSnapshotLoader.flattenUserProperties(sc.getUserProperties()));
        return item;
    }

    private static boolean includeCategory(String catFilter, String group) {
        if (catFilter == null || catFilter.isEmpty() || "ALL".equalsIgnoreCase(catFilter) || "전체".equals(catFilter)) {
            return true;
        }
        String f = catFilter.toLowerCase(Locale.ROOT);
        if ("dn".equals(group.toLowerCase(Locale.ROOT))) {
            return f.equals("dn") || f.equals("agent dn") || f.equals("acd position") || f.equals("rsi") || f.equals("vqueue");
        }
        if ("group".equals(group.toLowerCase(Locale.ROOT))) {
            return f.equals("group") || f.equals("place group") || f.equals("agent group") || f.equals("dn group");
        }
        if ("agentlogin".equals(group.toLowerCase(Locale.ROOT))) {
            return f.equals("agentlogin") || f.equals("agent login");
        }
        return f.equals(group.toLowerCase(Locale.ROOT));
    }

    private static boolean matchesFilter(
            LookupItemDto item,
            String queryLower,
            Set<String> expandedNumbers,
            boolean exact,
            String catFilter,
            String customerFilter) {

        if (catFilter != null && !catFilter.isEmpty()
                && !"ALL".equalsIgnoreCase(catFilter) && !"전체".equals(catFilter)
                && !"DN".equalsIgnoreCase(catFilter) && !"Group".equalsIgnoreCase(catFilter)
                && !"AgentLogin".equalsIgnoreCase(catFilter)) {
            if (!item.category.equalsIgnoreCase(catFilter)) {
                return false;
            }
        }
        if (customerFilter != null && !customerFilter.isEmpty()) {
            boolean custMatch = item.customerInferred.equalsIgnoreCase(customerFilter)
                    || item.folderPath.toLowerCase(Locale.ROOT).contains(customerFilter.toLowerCase(Locale.ROOT));
            if (!custMatch) return false;
        }

        if (queryLower == null || queryLower.isEmpty()) {
            return true;
        }

        if (!expandedNumbers.isEmpty()) {
            if (expandedNumbers.contains(item.key)) return true;
            if ((item.key.startsWith("P") || item.key.startsWith("p")) && expandedNumbers.contains(item.key.substring(1))) {
                return true;
            }
            for (LookupRelationDto r : item.relations) {
                if (expandedNumbers.contains(r.targetKey)) return true;
            }
            return false;
        }

        if (exact) {
            if (item.key.equalsIgnoreCase(queryLower) || item.name.equalsIgnoreCase(queryLower)) return true;
            for (String v : item.attributes.values()) {
                if (v != null && v.equalsIgnoreCase(queryLower)) return true;
            }
            return false;
        }

        if (containsIgnoreCase(item.key, queryLower)
                || containsIgnoreCase(item.name, queryLower)
                || containsIgnoreCase(item.folderPath, queryLower)
                || containsIgnoreCase(item.customerInferred, queryLower)
                || containsIgnoreCase(item.relationSummary, queryLower)
                || containsIgnoreCase(item.subType, queryLower)) {
            return true;
        }
        for (String v : item.attributes.values()) {
            if (containsIgnoreCase(v, queryLower)) return true;
        }
        return false;
    }

    private static boolean containsIgnoreCase(String s, String qLower) {
        return s != null && s.toLowerCase(Locale.ROOT).contains(qLower);
    }

    private static String formatPersonLabel(CfgPerson p) {
        String fn = CmCatalogSnapshotLoader.safe(p.getFirstName());
        String un = CmCatalogSnapshotLoader.safe(p.getUserName());
        String eid = CmCatalogSnapshotLoader.safe(p.getEmployeeID());
        String base = !fn.isEmpty() ? fn : un;
        return eid.isEmpty() ? base : (base + "(" + eid + ")");
    }

    private static String previewList(List<String> list, int max) {
        if (list.size() <= max) return String.join(", ", list);
        return String.join(", ", list.subList(0, max)) + " ...+" + (list.size() - max);
    }

    private static double round2(double val) {
        return Math.round(val * 100.0) / 100.0;
    }
}
