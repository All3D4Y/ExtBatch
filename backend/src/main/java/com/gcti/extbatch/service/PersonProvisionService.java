package com.gcti.extbatch.service;

import com.genesyslab.platform.applicationblocks.com.IConfService;
import com.genesyslab.platform.applicationblocks.com.objects.CfgAgentInfo;
import com.genesyslab.platform.applicationblocks.com.objects.CfgAgentLogin;
import com.genesyslab.platform.applicationblocks.com.objects.CfgAgentLoginInfo;
import com.genesyslab.platform.applicationblocks.com.objects.CfgFolder;
import com.genesyslab.platform.applicationblocks.com.objects.CfgPerson;
import com.genesyslab.platform.applicationblocks.com.queries.CfgAgentLoginQuery;
import com.genesyslab.platform.applicationblocks.com.queries.CfgPersonQuery;
import com.genesyslab.platform.configuration.protocol.types.CfgFlag;
import com.gcti.extbatch.dto.CommonDtos.AuditEventDto;
import com.gcti.extbatch.dto.PersonDtos.PersonBatchPayloadItem;
import com.gcti.extbatch.dto.PersonDtos.PersonCheckMatchDto;
import com.gcti.extbatch.dto.PersonDtos.PersonCheckRequest;
import com.gcti.extbatch.dto.PersonDtos.PersonCheckSummary;
import com.gcti.extbatch.dto.PersonDtos.PersonCreateFailureDto;
import com.gcti.extbatch.dto.PersonDtos.PersonCreateRequest;
import com.gcti.extbatch.dto.PersonDtos.PersonCreateResultItemDto;
import com.gcti.extbatch.dto.PersonDtos.PersonCreateSummary;
import com.gcti.extbatch.dto.PersonDtos.PersonDeleteResultItemDto;
import com.gcti.extbatch.dto.PersonDtos.PersonDeleteSummary;
import com.gcti.extbatch.dto.PersonDtos.PersonLoginPoolDto;
import com.gcti.extbatch.dto.PersonDtos.PersonLoginPoolResponse;
import com.gcti.extbatch.dto.PersonDtos.PersonManageActionRequest;
import com.gcti.extbatch.dto.PersonDtos.PersonManageFailureDto;
import com.gcti.extbatch.dto.PersonDtos.PersonManageItemDto;
import com.gcti.extbatch.dto.PersonDtos.PersonManageListResponse;
import com.gcti.extbatch.dto.PersonDtos.PersonManagePayloadItem;
import com.gcti.extbatch.dto.PersonDtos.PersonUpdateResultItemDto;
import com.gcti.extbatch.dto.PersonDtos.PersonUpdateSummary;
import com.gcti.extbatch.psdk.CmFolderResolver;
import com.gcti.extbatch.psdk.CmFolderResolver.FolderIndex;
import com.gcti.extbatch.psdk.CmFolderResolver.ResolvedFolder;
import com.gcti.extbatch.psdk.CustomerCatalogService;
import com.gcti.extbatch.psdk.CustomerCatalogService.CustomerRecord;
import com.gcti.extbatch.psdk.GenesysSessionManager;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collection;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import org.springframework.stereotype.Service;

@Service
public class PersonProvisionService {

    private final GenesysSessionManager sessionManager;
    private final CmFolderResolver folderResolver;
    private final CustomerCatalogService customerCatalog;

    public PersonProvisionService(
            GenesysSessionManager sessionManager,
            CmFolderResolver folderResolver,
            CustomerCatalogService customerCatalog) {
        this.sessionManager = sessionManager;
        this.folderResolver = folderResolver;
        this.customerCatalog = customerCatalog;
    }

    public PersonLoginPoolResponse fetchAvailableLogins(String customerName) throws Exception {
        IConfService service = sessionManager.ensureConnected();
        CustomerRecord cust = requireCustomer(customerName);

        String swName = sessionManager.getSwitchName();
        List<String> alSegs = folderResolver.makePath(
                Arrays.asList("Configuration", "Environment", "Switches", swName, "Agent Logins"),
                cust.agentLogin);
        if (alSegs == null || alSegs.isEmpty()) {
            throw new IllegalStateException("고객사 '" + cust.name + "'의 Agent Login 폴더 경로가 설정되어 있지 않습니다.");
        }

        FolderIndex folderIdx = folderResolver.loadIndex(service);
        CfgFolder alFolder = folderIdx.findBySegments(alSegs);
        if (alFolder == null) {
            throw new IllegalStateException("Agent Logins Config 폴더를 찾을 수 없습니다: " + String.join(" > ", alSegs));
        }

        PersonLoginPoolResponse resp = new PersonLoginPoolResponse();
        resp.customer = cust.name;
        resp.logs.add("[1/2] Config Agent Logins 목록 조회 중... (" + folderIdx.resolvePath(alFolder) + ")");

        CfgAgentLoginQuery alq = new CfgAgentLoginQuery(service);
        alq.setSwitchDbid(sessionManager.getSwitchDbid());
        Collection<CfgAgentLogin> allLogins = alq.execute();
        List<CfgAgentLogin> folderLogins = new ArrayList<>();
        if (allLogins != null) {
            for (CfgAgentLogin al : allLogins) {
                if (al.getFolderId() != null && al.getFolderId().equals(alFolder.getDBID())) {
                    folderLogins.add(al);
                }
            }
        }
        folderLogins.sort(Comparator.comparing(al -> safeTrim(al.getLoginCode())));
        resp.logs.add("  -> Agent Login " + folderLogins.size() + "건 로드 완료");

        ResolvedFolder personsResolved = folderResolver.resolvePersonsFolder(folderIdx, cust);
        resp.personFolderPath = personsResolved != null ? personsResolved.displayPath : "";
        if (personsResolved == null) {
            resp.logs.add("[경고] Config Persons 폴더에서 '" + cust.name + "' 폴더를 찾지 못했습니다. 전체 사용 현황 기준으로 표시됩니다.");
        } else {
            resp.logs.add("[2/2] Persons 사용 현황 매핑 중... (" + resp.personFolderPath + ")");
        }

        CfgPersonQuery pq = new CfgPersonQuery(service);
        pq.setTenantDbid(sessionManager.getTenantDbid());
        Collection<CfgPerson> allPersons = pq.execute();
        Map<Integer, List<String>> usedByLoginDbid = new HashMap<>();
        if (allPersons != null) {
            for (CfgPerson p : allPersons) {
                if (p.getAgentInfo() == null || p.getAgentInfo().getAgentLogins() == null) continue;
                String uName = safeTrim(p.getUserName());
                for (CfgAgentLoginInfo ali : p.getAgentInfo().getAgentLogins()) {
                    Integer lDbid = ali.getAgentLoginDBID();
                    if (lDbid != null && lDbid > 0) {
                        usedByLoginDbid.computeIfAbsent(lDbid, k -> new ArrayList<>()).add(uName);
                    }
                }
            }
        }

        int availCnt = 0;
        for (CfgAgentLogin al : folderLogins) {
            List<String> owners = usedByLoginDbid.get(al.getDBID());
            boolean available = (owners == null || owners.isEmpty());
            if (available) availCnt++;

            PersonLoginPoolDto dto = new PersonLoginPoolDto();
            dto.loginId = safeTrim(al.getLoginCode());
            dto.switchName = swName;
            dto.isAvailable = available;
            dto.usedBy = available ? "" : String.join(", ", owners);
            resp.items.add(dto);
        }

        resp.totalCount = resp.items.size();
        resp.availableCount = availCnt;
        resp.logs.add(String.format(Locale.ROOT, "[조회 완료] 전체 %d건 중 사용 가능 %d건 / 사용 중 %d건",
                resp.totalCount, resp.availableCount, (resp.totalCount - resp.availableCount)));
        return resp;
    }

    public PersonCheckSummary checkPersons(PersonCheckRequest req) throws Exception {
        IConfService service = sessionManager.ensureConnected();
        List<PersonBatchPayloadItem> rows = req != null && req.items != null ? req.items : Collections.emptyList();

        PersonCheckSummary resp = new PersonCheckSummary();
        resp.totalChecked = rows.size();
        resp.logs.add("[중복 체크] Config 전체 Person 목록 조회 중... (대상 " + rows.size() + "건)");

        FolderIndex folderIdx = folderResolver.loadIndex(service);
        CfgPersonQuery pq = new CfgPersonQuery(service);
        pq.setTenantDbid(sessionManager.getTenantDbid());
        Collection<CfgPerson> allPersons = pq.execute();
        int totalGlobal = allPersons != null ? allPersons.size() : 0;
        resp.globalPersonCount = totalGlobal;
        resp.logs.add("  -> Config 전체 Person " + totalGlobal + "건 인덱싱 완료");

        Map<String, List<CfgPerson>> byEmp = new HashMap<>();
        Map<String, List<CfgPerson>> byUser = new HashMap<>();
        if (allPersons != null) {
            for (CfgPerson p : allPersons) {
                String emp = safeTrim(p.getEmployeeID());
                String usr = safeTrim(p.getUserName());
                if (!emp.isEmpty()) {
                    byEmp.computeIfAbsent(emp.toLowerCase(Locale.ROOT), k -> new ArrayList<>()).add(p);
                }
                if (!usr.isEmpty()) {
                    byUser.computeIfAbsent(usr.toLowerCase(Locale.ROOT), k -> new ArrayList<>()).add(p);
                }
            }
        }

        Set<String> dupEmpSet = new LinkedHashSet<>();
        Set<String> dupUserSet = new LinkedHashSet<>();
        Set<String> cleanEmpSet = new LinkedHashSet<>();

        for (PersonBatchPayloadItem row : rows) {
            String eid = safeTrim(row.employeeId);
            String un = safeTrim(row.userName);
            String ek = eid.toLowerCase(Locale.ROOT);
            String uk = un.toLowerCase(Locale.ROOT);

            if (!eid.isEmpty() && byEmp.containsKey(ek)) {
                dupEmpSet.add(eid);
                for (CfgPerson m : byEmp.get(ek)) {
                    PersonCheckMatchDto dto = new PersonCheckMatchDto();
                    dto.inputId = eid;
                    dto.userName = safeTrim(m.getUserName());
                    dto.employeeId = safeTrim(m.getEmployeeID());
                    dto.folderPath = resolvePersonPath(m, folderIdx);
                    resp.empMatches.add(dto);
                }
            } else if (!eid.isEmpty()) {
                cleanEmpSet.add(eid);
            }

            if (!un.isEmpty() && byUser.containsKey(uk)) {
                dupUserSet.add(un);
                for (CfgPerson m : byUser.get(uk)) {
                    PersonCheckMatchDto dto = new PersonCheckMatchDto();
                    dto.inputId = un;
                    dto.userName = safeTrim(m.getUserName());
                    dto.employeeId = safeTrim(m.getEmployeeID());
                    dto.folderPath = resolvePersonPath(m, folderIdx);
                    resp.userMatches.add(dto);
                }
            }
        }

        resp.dupEmpIds.addAll(dupEmpSet);
        resp.dupUserNames.addAll(dupUserSet);
        resp.cleanEmpIds.addAll(cleanEmpSet);
        resp.logs.add(String.format(Locale.ROOT, "[중복 체크 완료] Employee ID 중복 %d건 / User Name 중복 %d건 / 사용 가능 %d건",
                resp.dupEmpIds.size(), resp.dupUserNames.size(), resp.cleanEmpIds.size()));
        return resp;
    }

    public PersonCreateSummary createPersons(PersonCreateRequest req) throws Exception {
        long startNs = System.nanoTime();
        IConfService service = sessionManager.ensureConnected();
        CustomerRecord cust = requireCustomer(req != null ? req.customerName : null);
        List<PersonBatchPayloadItem> rows = req != null && req.items != null ? req.items : Collections.emptyList();
        if (rows.isEmpty()) {
            throw new IllegalArgumentException("처리할 Person 대상 목록이 비어 있습니다.");
        }

        FolderIndex folderIdx = folderResolver.loadIndex(service);
        ResolvedFolder pResolved = folderResolver.resolvePersonsFolder(folderIdx, cust);
        if (pResolved == null) {
            throw new IllegalStateException("Config Persons 폴더에서 '" + cust.name + "' 고객사 폴더를 찾지 못했습니다.");
        }

        PersonCreateSummary resp = new PersonCreateSummary();
        resp.customer = cust.name;
        resp.folderPath = pResolved.displayPath;
        resp.logs.add(String.format(Locale.ROOT, "[대상 폴더] %s (대상 %d건)", pResolved.displayPath, rows.size()));

        Map<String, CfgAgentLogin> folderLoginsByCode = Collections.emptyMap();
        if (req.doAgentLogin) {
            folderLoginsByCode = loadCustomerAgentLoginsByCode(service, folderIdx, cust);
        }

        CfgPersonQuery pq = new CfgPersonQuery(service);
        pq.setTenantDbid(sessionManager.getTenantDbid());
        Collection<CfgPerson> allPersons = pq.execute();
        Set<String> existingEmpLower = new LinkedHashSet<>();
        Set<String> existingUserLower = new LinkedHashSet<>();
        Map<String, CfgPerson> folderPersonsByUser = new HashMap<>();
        if (allPersons != null) {
            for (CfgPerson p : allPersons) {
                String emp = safeTrim(p.getEmployeeID());
                String usr = safeTrim(p.getUserName());
                if (!emp.isEmpty()) existingEmpLower.add(emp.toLowerCase(Locale.ROOT));
                if (!usr.isEmpty()) existingUserLower.add(usr.toLowerCase(Locale.ROOT));
                if (p.getFolderId() != null && p.getFolderId().equals(pResolved.folder.getDBID()) && !usr.isEmpty()) {
                    folderPersonsByUser.put(usr.toLowerCase(Locale.ROOT), p);
                }
            }
        }

        int tenantDbid = sessionManager.getTenantDbid();

        for (PersonBatchPayloadItem row : rows) {
            String un = safeTrim(row.userName);
            String fn = safeTrim(row.firstName);
            String ln = safeTrim(row.lastName);
            String eid = safeTrim(row.employeeId);
            String aid = safeTrim(row.agentLogin);
            boolean wantIsAgent = row.isAgent || (req.doAgentLogin && !aid.isEmpty());

            if (un.isEmpty() || fn.isEmpty() || ln.isEmpty() || eid.isEmpty()) {
                PersonCreateFailureDto fail = new PersonCreateFailureDto();
                fail.employeeId = eid;
                fail.userName = un;
                fail.reason = "필수 필드 누락";
                resp.failed.add(fail);
                resp.logs.add("  [실패] " + eid + " -> 필수 필드 누락");
                AuditEventDto err = AuditEventDto.error("Create", "Person", un, cust.name, pResolved.displayPath, "필수 필드 누락");
                err.empId = eid;
                resp.auditEvents.add(err);
                continue;
            }

            if (req.doGeneral) {
                if (existingUserLower.contains(un.toLowerCase(Locale.ROOT)) || existingEmpLower.contains(eid.toLowerCase(Locale.ROOT))) {
                    String why = existingUserLower.contains(un.toLowerCase(Locale.ROOT))
                            ? ("User Name 중복(" + un + ")")
                            : ("Employee ID 중복(" + eid + ")");
                    PersonCreateFailureDto sk = new PersonCreateFailureDto();
                    sk.employeeId = eid;
                    sk.userName = un;
                    sk.reason = why;
                    resp.skipped.add(sk);
                    resp.logs.add("  [건너뜀] " + un + " (" + eid + ") -> " + why);
                    continue;
                }

                try {
                    CfgPerson p = new CfgPerson(service);
                    p.setTenantDBID(tenantDbid);
                    p.setFolderId(pResolved.folder.getDBID());
                    p.setUserName(un);
                    p.setFirstName(fn);
                    p.setLastName(ln);
                    p.setEmployeeID(eid);
                    p.setIsAgent(wantIsAgent ? CfgFlag.CFGTrue : CfgFlag.CFGFalse);

                    boolean loginLinked = false;
                    if (req.doAgentLogin && !aid.isEmpty()) {
                        CfgAgentLogin al = folderLoginsByCode.get(aid);
                        if (al == null) {
                            throw new IllegalStateException("고객사 Agent Logins 폴더에서 Agent Login '" + aid + "'를 찾을 수 없습니다.");
                        }
                        CfgAgentInfo info = new CfgAgentInfo(service, p);
                        CfgAgentLoginInfo ali = new CfgAgentLoginInfo(service, p);
                        ali.setAgentLoginDBID(al.getDBID());
                        info.setAgentLogins(Collections.singletonList(ali));
                        p.setAgentInfo(info);
                        loginLinked = true;
                    }

                    p.save();

                    existingUserLower.add(un.toLowerCase(Locale.ROOT));
                    existingEmpLower.add(eid.toLowerCase(Locale.ROOT));
                    resp.createdEmpIds.add(eid);
                    PersonCreateResultItemDto okItem = new PersonCreateResultItemDto();
                    okItem.userName = un;
                    okItem.employeeId = eid;
                    okItem.agentLogin = loginLinked ? aid : "";
                    resp.createdItems.add(okItem);

                    String loginTag = loginLinked ? (" + Agent Login: " + aid) : (req.doAgentLogin ? " (Agent Login 미지정)" : "");
                    resp.logs.add(String.format(Locale.ROOT, "  [생성 완료] User='%s', First='%s', Last='%s', EmpID='%s'%s",
                            un, fn, ln, eid, loginTag));

                    AuditEventDto evt = AuditEventDto.create("Person", un, cust.name, pResolved.displayPath, null);
                    evt.empId = eid;
                    evt.firstName = fn;
                    evt.lastName = ln;
                    evt.isAgent = Boolean.toString(wantIsAgent);
                    if (loginLinked) evt.linkedLogins.add(aid);
                    resp.auditEvents.add(evt);
                } catch (Exception ex) {
                    PersonCreateFailureDto fail = new PersonCreateFailureDto();
                    fail.employeeId = eid;
                    fail.userName = un;
                    fail.reason = ex.getMessage();
                    resp.failed.add(fail);
                    resp.logs.add("  [실패] " + un + " (" + eid + ") -> " + ex.getMessage());
                    AuditEventDto err = AuditEventDto.error("Create", "Person", un, cust.name, pResolved.displayPath, ex.getMessage());
                    err.empId = eid;
                    resp.auditEvents.add(err);
                }
            } else if (req.doAgentLogin && !aid.isEmpty()) {
                try {
                    CfgPerson p = folderPersonsByUser.get(un.toLowerCase(Locale.ROOT));
                    if (p == null) {
                        throw new IllegalStateException("폴더 내에 기존 Person '" + un + "'이 존재하지 않습니다.");
                    }
                    CfgAgentLogin al = folderLoginsByCode.get(aid);
                    if (al == null) {
                        throw new IllegalStateException("고객사 Agent Logins 폴더에서 Agent Login '" + aid + "'를 찾을 수 없습니다.");
                    }
                    if (p.getIsAgent() != CfgFlag.CFGTrue) {
                        throw new IllegalStateException("기존 비상담원(IsAgent=false) 계정 '" + un + "'에는 Agent Login을 연결할 수 없습니다. 삭제 후 IsAgent=true로 재생성해 주세요.");
                    }
                    CfgAgentInfo info = p.getAgentInfo();
                    if (info == null) {
                        info = new CfgAgentInfo(service, p);
                        p.setAgentInfo(info);
                    }
                    List<CfgAgentLoginInfo> curList = info.getAgentLogins() != null
                            ? new ArrayList<>(info.getAgentLogins())
                            : new ArrayList<>();
                    boolean alreadyLinked = false;
                    for (CfgAgentLoginInfo existingAli : curList) {
                        if (existingAli.getAgentLoginDBID() != null && existingAli.getAgentLoginDBID().equals(al.getDBID())) {
                            alreadyLinked = true;
                            break;
                        }
                    }
                    if (!alreadyLinked) {
                        CfgAgentLoginInfo ali = new CfgAgentLoginInfo(service, p);
                        ali.setAgentLoginDBID(al.getDBID());
                        curList.add(ali);
                        info.setAgentLogins(curList);
                    }
                    p.save();

                    resp.createdEmpIds.add(eid);
                    PersonCreateResultItemDto okItem = new PersonCreateResultItemDto();
                    okItem.userName = un;
                    okItem.employeeId = eid;
                    okItem.agentLogin = aid;
                    resp.createdItems.add(okItem);
                    resp.logs.add("  [Login 연결 완료] User='" + un + "' + Agent Login: " + aid);

                    AuditEventDto evt = new AuditEventDto();
                    evt.action = "LINK";
                    evt.objectType = "Person";
                    evt.name = un;
                    evt.empId = eid;
                    evt.customer = cust.name;
                    evt.path = pResolved.displayPath;
                    evt.linkedLogins.add(aid);
                    resp.auditEvents.add(evt);
                } catch (Exception ex) {
                    PersonCreateFailureDto fail = new PersonCreateFailureDto();
                    fail.employeeId = eid;
                    fail.userName = un;
                    fail.reason = ex.getMessage();
                    resp.failed.add(fail);
                    resp.logs.add("  [실패] " + un + " -> " + ex.getMessage());
                    AuditEventDto err = AuditEventDto.error("LinkAgentLogin", "Person", un, cust.name, pResolved.displayPath, ex.getMessage());
                    err.empId = eid;
                    resp.auditEvents.add(err);
                }
            }
        }

        resp.elapsedSeconds = round2((System.nanoTime() - startNs) / 1_000_000_000.0);
        resp.logs.add(String.format(Locale.ROOT, "[Person 작업 완료] 성공 %d건 / 건너뜀 %d건 / 실패 %d건 (%.1f초)",
                resp.createdEmpIds.size(), resp.skipped.size(), resp.failed.size(), resp.elapsedSeconds));
        return resp;
    }

    public PersonManageListResponse listPersonsInFolder(String customerName) throws Exception {
        long startNs = System.nanoTime();
        IConfService service = sessionManager.ensureConnected();
        CustomerRecord cust = requireCustomer(customerName);

        FolderIndex folderIdx = folderResolver.loadIndex(service);
        ResolvedFolder pResolved = folderResolver.resolvePersonsFolder(folderIdx, cust);
        if (pResolved == null) {
            throw new IllegalStateException("Config Persons 폴더에서 '" + cust.name + "' 고객사 폴더를 찾지 못했습니다.");
        }

        PersonManageListResponse resp = new PersonManageListResponse();
        resp.customer = cust.name;
        resp.folderPath = pResolved.displayPath;
        resp.logs.add("[Person 목록 조회] " + pResolved.displayPath);

        Map<Integer, String> loginCodeByDbid = loadAllAgentLoginCodesByDbid(service);

        CfgPersonQuery pq = new CfgPersonQuery(service);
        pq.setTenantDbid(sessionManager.getTenantDbid());
        Collection<CfgPerson> allPersons = pq.execute();
        List<CfgPerson> folderPersons = new ArrayList<>();
        if (allPersons != null) {
            for (CfgPerson p : allPersons) {
                if (p.getFolderId() != null && p.getFolderId().equals(pResolved.folder.getDBID())) {
                    folderPersons.add(p);
                }
            }
        }
        folderPersons.sort(Comparator.comparing(p -> safeTrim(p.getUserName())));

        for (CfgPerson p : folderPersons) {
            PersonManageItemDto item = new PersonManageItemDto();
            item.userName = safeTrim(p.getUserName());
            item.firstName = safeTrim(p.getFirstName());
            item.lastName = safeTrim(p.getLastName());
            item.employeeId = safeTrim(p.getEmployeeID());
            item.isAgent = (p.getIsAgent() == CfgFlag.CFGTrue);

            if (p.getAgentInfo() != null && p.getAgentInfo().getAgentLogins() != null) {
                for (CfgAgentLoginInfo ali : p.getAgentInfo().getAgentLogins()) {
                    Integer lDbid = ali.getAgentLoginDBID();
                    String code = lDbid != null ? loginCodeByDbid.get(lDbid) : null;
                    if (code != null && !code.isEmpty()) {
                        item.agentLogins.add(code);
                    }
                }
            }
            item.agentLogin = String.join(", ", item.agentLogins);
            resp.items.add(item);
        }

        resp.totalCount = resp.items.size();
        resp.elapsedSeconds = round2((System.nanoTime() - startNs) / 1_000_000_000.0);
        resp.logs.add(String.format(Locale.ROOT, "[Person 목록 조회 완료] 총 %d명 조회 (%.1f초)",
                resp.totalCount, resp.elapsedSeconds));
        return resp;
    }

    public PersonUpdateSummary updatePersons(PersonManageActionRequest req) throws Exception {
        long startNs = System.nanoTime();
        IConfService service = sessionManager.ensureConnected();
        CustomerRecord cust = requireCustomer(req != null ? req.customerName : null);
        List<PersonManagePayloadItem> rows = req != null && req.manageItems != null ? req.manageItems : Collections.emptyList();
        if (rows.isEmpty()) {
            throw new IllegalArgumentException("수정할 Person 대상 목록이 비어 있습니다.");
        }

        FolderIndex folderIdx = folderResolver.loadIndex(service);
        ResolvedFolder pResolved = folderResolver.resolvePersonsFolder(folderIdx, cust);
        if (pResolved == null) {
            throw new IllegalStateException("Config Persons 폴더에서 '" + cust.name + "' 고객사 폴더를 찾지 못했습니다.");
        }

        PersonUpdateSummary resp = new PersonUpdateSummary();
        resp.customer = cust.name;
        resp.folderPath = pResolved.displayPath;
        resp.logs.add(String.format(Locale.ROOT, "[Person 수정] %s (대상 %d건)", pResolved.displayPath, rows.size()));

        Map<Integer, String> loginCodeByDbid = loadAllAgentLoginCodesByDbid(service);
        Map<String, CfgAgentLogin> allSwitchLoginsByCode = loadAllAgentLoginsByCode(service);
        Map<String, CfgAgentLogin> customerLoginsByCode = loadCustomerAgentLoginsByCode(service, folderIdx, cust);
        List<CfgPerson> folderPersons = loadPersonsInFolder(service, pResolved.folder.getDBID());

        for (PersonManagePayloadItem row : rows) {
            String origUn = safeTrim(row.originalUserName);
            String origEid = safeTrim(row.originalEmployeeId);
            String newUn = safeTrim(row.userName);
            String newFn = safeTrim(row.firstName);
            String newLn = safeTrim(row.lastName);
            String newEid = safeTrim(row.employeeId);
            if (origUn.isEmpty()) origUn = newUn;

            if (newUn.isEmpty()) {
                PersonManageFailureDto fail = new PersonManageFailureDto();
                fail.originalUserName = origUn;
                fail.employeeId = newEid;
                fail.userName = newUn;
                fail.reason = "User Name이 비어 있습니다.";
                resp.failed.add(fail);
                resp.logs.add("  [실패] " + origUn + " -> User Name 누락");
                AuditEventDto err = AuditEventDto.error("Update", "Person", origUn, cust.name, pResolved.displayPath, fail.reason);
                err.empId = newEid;
                resp.auditEvents.add(err);
                continue;
            }

            CfgPerson target = findPersonInList(folderPersons, origUn, origEid);
            if (target == null) {
                PersonManageFailureDto fail = new PersonManageFailureDto();
                fail.originalUserName = origUn;
                fail.employeeId = newEid;
                fail.userName = newUn;
                fail.reason = "Config 폴더에서 기존 Person을 찾을 수 없습니다.";
                resp.failed.add(fail);
                resp.logs.add("  [실패] " + origUn + " (" + origEid + ") -> Config 폴더 미존재");
                AuditEventDto err = AuditEventDto.error("Update", "Person", origUn, cust.name, pResolved.displayPath, fail.reason);
                err.empId = newEid;
                resp.auditEvents.add(err);
                continue;
            }

            List<String> desiredLogins = parseAgentLoginTokens(row.agentLogin);
            boolean curIsAgent = (target.getIsAgent() == CfgFlag.CFGTrue);
            List<String> removed = new ArrayList<>();
            List<String> added = new ArrayList<>();

            try {
                // Validate immutable IsAgent transitions before mutating target
                if (row.updateGeneral && row.isAgent != curIsAgent) {
                    if (!curIsAgent) {
                        throw new IllegalStateException("이미 생성된 비상담원(IsAgent=false) 계정은 Genesys Config 정책상 상담원(Agent)으로 전환할 수 없습니다. 삭제 후 IsAgent=true로 재생성해 주세요.");
                    }
                    throw new IllegalStateException("이미 생성된 상담원(IsAgent=true) 계정의 IsAgent 속성은 Genesys Config 정책상 해제할 수 없습니다.");
                }
                if (row.updateAgentLogin && !desiredLogins.isEmpty() && !curIsAgent) {
                    throw new IllegalStateException("비상담원(IsAgent=false) 계정은 상담원 전환이 불가하여 Agent Login을 연결할 수 없습니다. 삭제 후 IsAgent=true로 재생성해 주세요.");
                }

                // General fields (never touch Password or IsAgent on saved CfgPerson!)
                if (row.updateGeneral) {
                    target.setFirstName(newFn);
                    target.setLastName(newLn);
                    target.setEmployeeID(newEid);
                    target.setUserName(newUn);
                }

                if (row.updateAgentLogin && curIsAgent) {
                    CfgAgentInfo info = target.getAgentInfo();
                    if (info == null) {
                        info = new CfgAgentInfo(service, target);
                        target.setAgentInfo(info);
                    }
                    List<String> curCodes = new ArrayList<>();
                    if (info.getAgentLogins() != null) {
                        for (CfgAgentLoginInfo ali : info.getAgentLogins()) {
                            Integer lDbid = ali.getAgentLoginDBID();
                            String code = lDbid != null ? loginCodeByDbid.get(lDbid) : null;
                            if (code != null && !code.isEmpty()) curCodes.add(code);
                        }
                    }

                    Set<String> desiredLower = new LinkedHashSet<>();
                    for (String dl : desiredLogins) desiredLower.add(dl.toLowerCase(Locale.ROOT));
                    Set<String> curLower = new LinkedHashSet<>();
                    for (String cl : curCodes) curLower.add(cl.toLowerCase(Locale.ROOT));

                    for (String cl : curCodes) {
                        if (!desiredLower.contains(cl.toLowerCase(Locale.ROOT))) removed.add(cl);
                    }
                    for (String dl : desiredLogins) {
                        if (!curLower.contains(dl.toLowerCase(Locale.ROOT))) added.add(dl);
                    }

                    List<CfgAgentLoginInfo> newLoginInfos = new ArrayList<>();
                    for (String dl : desiredLogins) {
                        CfgAgentLogin al = customerLoginsByCode.get(dl);
                        if (al == null) {
                            al = allSwitchLoginsByCode.get(dl);
                        }
                        if (al == null) {
                            throw new IllegalStateException("Agent Logins에서 '" + dl + "'를 찾을 수 없습니다.");
                        }
                        CfgAgentLoginInfo ali = new CfgAgentLoginInfo(service, target);
                        ali.setAgentLoginDBID(al.getDBID());
                        newLoginInfos.add(ali);
                    }
                    info.setAgentLogins(newLoginInfos);
                }

                target.save();

                PersonUpdateResultItemDto okItem = new PersonUpdateResultItemDto();
                okItem.originalUserName = origUn;
                okItem.userName = newUn;
                okItem.firstName = newFn;
                okItem.lastName = newLn;
                okItem.employeeId = newEid;
                okItem.isAgent = curIsAgent;
                okItem.agentLogin = String.join(", ", desiredLogins);
                resp.updatedItems.add(okItem);

                StringBuilder delta = new StringBuilder();
                if (!removed.isEmpty()) delta.append(" -Login[").append(String.join(",", removed)).append("]");
                if (!added.isEmpty()) delta.append(" +Login[").append(String.join(",", added)).append("]");
                resp.logs.add(String.format(Locale.ROOT, "  [수정 완료] User='%s' -> User='%s', First='%s', Last='%s', EmpID='%s'%s",
                        origUn, newUn, newFn, newLn, newEid, delta.toString()));

                AuditEventDto evt = new AuditEventDto();
                evt.action = "UPDATE";
                evt.objectType = "Person";
                evt.name = newUn;
                evt.empId = newEid;
                evt.firstName = newFn;
                evt.lastName = newLn;
                evt.isAgent = Boolean.toString(curIsAgent);
                evt.linkedLogins.addAll(added);
                evt.unlinkedLogins.addAll(removed);
                if (!origUn.equals(newUn)) evt.detail = "이전User=" + origUn;
                evt.customer = cust.name;
                evt.path = pResolved.displayPath;
                resp.auditEvents.add(evt);
            } catch (Exception ex) {
                PersonManageFailureDto fail = new PersonManageFailureDto();
                fail.originalUserName = origUn;
                fail.employeeId = newEid;
                fail.userName = newUn;
                fail.reason = ex.getMessage();
                resp.failed.add(fail);
                resp.logs.add("  [실패] " + origUn + " (" + newEid + ") -> " + ex.getMessage());
                AuditEventDto err = AuditEventDto.error("Update", "Person", origUn, cust.name, pResolved.displayPath, ex.getMessage());
                err.empId = newEid;
                resp.auditEvents.add(err);
            }
        }

        resp.elapsedSeconds = round2((System.nanoTime() - startNs) / 1_000_000_000.0);
        resp.logs.add(String.format(Locale.ROOT, "[Person 수정 완료] 성공 %d건 / 실패 %d건 (%.1f초)",
                resp.updatedItems.size(), resp.failed.size(), resp.elapsedSeconds));
        return resp;
    }

    public PersonDeleteSummary deletePersons(PersonManageActionRequest req) throws Exception {
        long startNs = System.nanoTime();
        IConfService service = sessionManager.ensureConnected();
        CustomerRecord cust = requireCustomer(req != null ? req.customerName : null);
        List<PersonManagePayloadItem> rows = req != null && req.manageItems != null ? req.manageItems : Collections.emptyList();
        if (rows.isEmpty()) {
            throw new IllegalArgumentException("삭제할 Person 대상 목록이 비어 있습니다.");
        }

        FolderIndex folderIdx = folderResolver.loadIndex(service);
        ResolvedFolder pResolved = folderResolver.resolvePersonsFolder(folderIdx, cust);
        if (pResolved == null) {
            throw new IllegalStateException("Config Persons 폴더에서 '" + cust.name + "' 고객사 폴더를 찾지 못했습니다.");
        }

        PersonDeleteSummary resp = new PersonDeleteSummary();
        resp.customer = cust.name;
        resp.folderPath = pResolved.displayPath;
        resp.logs.add(String.format(Locale.ROOT, "[Person 삭제] %s (대상 %d건)", pResolved.displayPath, rows.size()));

        Map<Integer, String> loginCodeByDbid = loadAllAgentLoginCodesByDbid(service);
        List<CfgPerson> folderPersons = loadPersonsInFolder(service, pResolved.folder.getDBID());

        for (PersonManagePayloadItem row : rows) {
            String origUn = safeTrim(row.originalUserName);
            if (origUn.isEmpty()) origUn = safeTrim(row.userName);
            String origEid = safeTrim(row.originalEmployeeId);
            if (origEid.isEmpty()) origEid = safeTrim(row.employeeId);

            CfgPerson target = findPersonInList(folderPersons, origUn, origEid);
            if (target == null) {
                PersonManageFailureDto sk = new PersonManageFailureDto();
                sk.originalUserName = origUn;
                sk.employeeId = origEid;
                sk.userName = origUn;
                sk.reason = "폴더 내 미존재";
                resp.skipped.add(sk);
                resp.logs.add("  [건너뜀] '" + origUn + "' (" + origEid + ") -> 폴더 내 미존재");
                continue;
            }

            String actualUser = safeTrim(target.getUserName());
            List<String> unlinked = new ArrayList<>();
            boolean unlinkedSaved = false;

            try {
                if (target.getAgentInfo() != null && target.getAgentInfo().getAgentLogins() != null
                        && !target.getAgentInfo().getAgentLogins().isEmpty()) {
                    for (CfgAgentLoginInfo ali : target.getAgentInfo().getAgentLogins()) {
                        Integer lDbid = ali.getAgentLoginDBID();
                        String code = lDbid != null ? loginCodeByDbid.get(lDbid) : null;
                        if (code != null && !code.isEmpty()) unlinked.add(code);
                    }
                    target.getAgentInfo().setAgentLogins(Collections.emptyList());
                    target.save();
                    unlinkedSaved = true;
                    resp.logs.add("  [Login 연결 해제] '" + actualUser + "' -> Agent Login [" + String.join(", ", unlinked) + "] 해제 저장 완료");
                }

                target.delete();

                PersonDeleteResultItemDto delItem = new PersonDeleteResultItemDto();
                delItem.originalUserName = origUn;
                delItem.userName = actualUser;
                delItem.employeeId = origEid;
                delItem.unlinkedLogins.addAll(unlinked);
                resp.deletedItems.add(delItem);

                String unlinkNote = unlinked.isEmpty() ? "" : (" (Login 연결 해제: " + String.join(", ", unlinked) + ")");
                resp.logs.add("  [삭제 완료] User='" + actualUser + "', EmpID='" + origEid + "'" + unlinkNote);

                AuditEventDto evt = AuditEventDto.delete("Person", actualUser, cust.name, pResolved.displayPath, null);
                evt.empId = origEid;
                evt.unlinkedLogins.addAll(unlinked);
                resp.auditEvents.add(evt);
            } catch (Exception ex) {
                if (unlinkedSaved && !unlinked.isEmpty()) {
                    AuditEventDto uEvt = new AuditEventDto();
                    uEvt.action = "UNLINK";
                    uEvt.objectType = "Person";
                    uEvt.name = actualUser;
                    uEvt.empId = origEid;
                    uEvt.customer = cust.name;
                    uEvt.path = pResolved.displayPath;
                    uEvt.unlinkedLogins.addAll(unlinked);
                    resp.auditEvents.add(uEvt);
                }
                PersonManageFailureDto fail = new PersonManageFailureDto();
                fail.originalUserName = origUn;
                fail.employeeId = origEid;
                fail.userName = actualUser;
                fail.reason = ex.getMessage();
                resp.failed.add(fail);
                resp.logs.add("  [실패] " + actualUser + " (" + origEid + ") -> " + ex.getMessage());
                AuditEventDto err = AuditEventDto.error("Delete", "Person", actualUser, cust.name, pResolved.displayPath, ex.getMessage());
                err.empId = origEid;
                resp.auditEvents.add(err);
            }
        }

        resp.elapsedSeconds = round2((System.nanoTime() - startNs) / 1_000_000_000.0);
        resp.logs.add(String.format(Locale.ROOT, "[Person 삭제 완료] 삭제 %d건 / 건너뜀 %d건 / 실패 %d건 (%.1f초)",
                resp.deletedItems.size(), resp.skipped.size(), resp.failed.size(), resp.elapsedSeconds));
        return resp;
    }

    private CustomerRecord requireCustomer(String customerName) {
        if (customerName == null || customerName.trim().isEmpty()) {
            throw new IllegalArgumentException("고객사를 선택하세요.");
        }
        CustomerRecord cust = customerCatalog.findByName(customerName);
        if (cust == null) {
            throw new IllegalArgumentException("고객사 '" + customerName + "'를 cm_customers.json에서 찾을 수 없습니다.");
        }
        return cust;
    }

    private Map<Integer, String> loadAllAgentLoginCodesByDbid(IConfService service) throws Exception {
        CfgAgentLoginQuery q = new CfgAgentLoginQuery(service);
        q.setSwitchDbid(sessionManager.getSwitchDbid());
        Collection<CfgAgentLogin> all = q.execute();
        Map<Integer, String> map = new HashMap<>();
        if (all != null) {
            for (CfgAgentLogin al : all) {
                if (al.getDBID() != null && al.getLoginCode() != null) {
                    map.put(al.getDBID(), al.getLoginCode().trim());
                }
            }
        }
        return map;
    }

    private Map<String, CfgAgentLogin> loadAllAgentLoginsByCode(IConfService service) throws Exception {
        CfgAgentLoginQuery q = new CfgAgentLoginQuery(service);
        q.setSwitchDbid(sessionManager.getSwitchDbid());
        Collection<CfgAgentLogin> all = q.execute();
        Map<String, CfgAgentLogin> map = new HashMap<>();
        if (all != null) {
            for (CfgAgentLogin al : all) {
                if (al.getLoginCode() != null) {
                    map.putIfAbsent(al.getLoginCode().trim(), al);
                }
            }
        }
        return map;
    }

    private Map<String, CfgAgentLogin> loadCustomerAgentLoginsByCode(
            IConfService service, FolderIndex folderIdx, CustomerRecord cust) throws Exception {
        String swName = sessionManager.getSwitchName();
        List<String> alSegs = folderResolver.makePath(
                Arrays.asList("Configuration", "Environment", "Switches", swName, "Agent Logins"),
                cust.agentLogin);
        if (alSegs == null || alSegs.isEmpty()) {
            throw new IllegalStateException("고객사 '" + cust.name + "'의 Agent Login 폴더 경로가 설정되어 있지 않습니다.");
        }
        CfgFolder alFolder = folderIdx.findBySegments(alSegs);
        if (alFolder == null) {
            throw new IllegalStateException("Agent Logins Config 폴더를 찾을 수 없습니다: " + String.join(" > ", alSegs));
        }

        CfgAgentLoginQuery q = new CfgAgentLoginQuery(service);
        q.setSwitchDbid(sessionManager.getSwitchDbid());
        Collection<CfgAgentLogin> all = q.execute();
        Map<String, CfgAgentLogin> map = new HashMap<>();
        if (all != null) {
            for (CfgAgentLogin al : all) {
                if (al.getFolderId() != null && al.getFolderId().equals(alFolder.getDBID()) && al.getLoginCode() != null) {
                    map.put(al.getLoginCode().trim(), al);
                }
            }
        }
        return map;
    }

    private List<CfgPerson> loadPersonsInFolder(IConfService service, Integer folderDbid) throws Exception {
        CfgPersonQuery pq = new CfgPersonQuery(service);
        pq.setTenantDbid(sessionManager.getTenantDbid());
        Collection<CfgPerson> all = pq.execute();
        List<CfgPerson> list = new ArrayList<>();
        if (all != null) {
            for (CfgPerson p : all) {
                if (p.getFolderId() != null && p.getFolderId().equals(folderDbid)) {
                    list.add(p);
                }
            }
        }
        return list;
    }

    private static CfgPerson findPersonInList(List<CfgPerson> persons, String userName, String employeeId) {
        if (userName != null && !userName.isEmpty()) {
            for (CfgPerson p : persons) {
                if (userName.equalsIgnoreCase(safeTrim(p.getUserName()))) return p;
            }
        }
        if (employeeId != null && !employeeId.isEmpty()) {
            for (CfgPerson p : persons) {
                if (employeeId.equalsIgnoreCase(safeTrim(p.getEmployeeID()))) return p;
            }
        }
        return null;
    }

    private static List<String> parseAgentLoginTokens(String raw) {
        List<String> list = new ArrayList<>();
        Set<String> seen = new LinkedHashSet<>();
        if (raw == null || raw.trim().isEmpty()) return list;
        for (String part : raw.split("[,;\\s]+")) {
            String p = part.trim();
            if (!p.isEmpty() && seen.add(p.toLowerCase(Locale.ROOT))) {
                list.add(p);
            }
        }
        return list;
    }

    private static String resolvePersonPath(CfgPerson p, FolderIndex folderIdx) {
        if (p.getObjectPath() != null && !p.getObjectPath().trim().isEmpty()) {
            return CmFolderResolver.toDisplayPath(p.getObjectPath());
        }
        return folderIdx.resolveDisplayPath(p.getFolderId());
    }

    private static String safeTrim(String s) {
        return s == null ? "" : s.trim();
    }

    private static double round2(double v) {
        return Math.round(v * 100.0) / 100.0;
    }
}
