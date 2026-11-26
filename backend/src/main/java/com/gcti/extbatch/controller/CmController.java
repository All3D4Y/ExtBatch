package com.gcti.extbatch.controller;

import com.gcti.extbatch.dto.BatchDtos.BatchActionResponse;
import com.gcti.extbatch.dto.BatchDtos.BatchCheckResponse;
import com.gcti.extbatch.dto.BatchDtos.BatchRequest;
import com.gcti.extbatch.dto.CenterDtos.CenterRequest;
import com.gcti.extbatch.dto.CenterDtos.CmCenterApplyResponse;
import com.gcti.extbatch.dto.CenterDtos.CmCenterCheckResponse;
import com.gcti.extbatch.dto.CenterDtos.CmCenterCreateResponse;
import com.gcti.extbatch.dto.CenterDtos.CmCenterDeleteResponse;
import com.gcti.extbatch.dto.CenterDtos.CmCenterStatusResponse;
import com.gcti.extbatch.dto.CommonDtos.ConnectRequest;
import com.gcti.extbatch.dto.CommonDtos.ConnectResponse;
import com.gcti.extbatch.dto.CommonDtos.HealthResponse;
import com.gcti.extbatch.dto.PersonDtos.PersonCheckRequest;
import com.gcti.extbatch.dto.PersonDtos.PersonCheckSummary;
import com.gcti.extbatch.dto.PersonDtos.PersonCreateRequest;
import com.gcti.extbatch.dto.PersonDtos.PersonCreateSummary;
import com.gcti.extbatch.dto.PersonDtos.PersonCustomerRequest;
import com.gcti.extbatch.dto.PersonDtos.PersonDeleteSummary;
import com.gcti.extbatch.dto.PersonDtos.PersonLoginPoolResponse;
import com.gcti.extbatch.dto.PersonDtos.PersonManageActionRequest;
import com.gcti.extbatch.dto.PersonDtos.PersonManageListResponse;
import com.gcti.extbatch.dto.PersonDtos.PersonUpdateSummary;
import com.gcti.extbatch.dto.Phase3Dtos.CenterAuditRequest;
import com.gcti.extbatch.dto.Phase3Dtos.CenterAuditResponse;
import com.gcti.extbatch.dto.Phase3Dtos.LookupSearchRequest;
import com.gcti.extbatch.dto.Phase3Dtos.LookupSearchResponse;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotCaptureRequest;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotCaptureResponse;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotDeleteRequest;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotDeleteResponse;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotDiffRequest;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotDiffResponse;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotListResponse;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotRestoreRequest;
import com.gcti.extbatch.dto.Phase3Dtos.SnapshotRestoreResponse;
import com.gcti.extbatch.psdk.CmCatalogSnapshotLoader;
import com.gcti.extbatch.psdk.GenesysSessionManager;
import com.gcti.extbatch.service.BatchProvisionService;
import com.gcti.extbatch.service.CenterProvisionService;
import com.gcti.extbatch.service.LookupService;
import com.gcti.extbatch.service.PersonProvisionService;
import com.gcti.extbatch.service.SnapshotService;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping(value = "/api", produces = "application/json;charset=UTF-8")
public class CmController {

    private final GenesysSessionManager sessionManager;
    private final BatchProvisionService batchService;
    private final PersonProvisionService personService;
    private final CenterProvisionService centerService;
    private final LookupService lookupService;
    private final SnapshotService snapshotService;
    private final CmCatalogSnapshotLoader catalogLoader;

    public CmController(
            GenesysSessionManager sessionManager,
            BatchProvisionService batchService,
            PersonProvisionService personService,
            CenterProvisionService centerService,
            LookupService lookupService,
            SnapshotService snapshotService,
            CmCatalogSnapshotLoader catalogLoader) {
        this.sessionManager = sessionManager;
        this.batchService = batchService;
        this.personService = personService;
        this.centerService = centerService;
        this.lookupService = lookupService;
        this.snapshotService = snapshotService;
        this.catalogLoader = catalogLoader;
    }

    @GetMapping("/health")
    public HealthResponse health() {
        return sessionManager.getHealth();
    }

    @PostMapping("/cm/connect")
    public ConnectResponse connect(@RequestBody(required = false) ConnectRequest req) throws Exception {
        try {
            return sessionManager.connect(req);
        } finally {
            catalogLoader.invalidateCache();
        }
    }

    @PostMapping("/cm/disconnect")
    public HealthResponse disconnect() {
        sessionManager.disconnect();
        catalogLoader.invalidateCache();
        return sessionManager.getHealth();
    }

    @PostMapping("/cm/batch/check")
    public BatchCheckResponse batchCheck(@RequestBody BatchRequest req) throws Exception {
        return batchService.check(req);
    }

    @PostMapping("/cm/batch/create")
    public BatchActionResponse batchCreate(@RequestBody BatchRequest req) throws Exception {
        try {
            return batchService.create(req);
        } finally {
            catalogLoader.invalidateCache();
        }
    }

    @PostMapping("/cm/batch/delete")
    public BatchActionResponse batchDelete(@RequestBody BatchRequest req) throws Exception {
        try {
            return batchService.delete(req);
        } finally {
            catalogLoader.invalidateCache();
        }
    }

    @PostMapping("/cm/person/available-logins")
    public PersonLoginPoolResponse availableLogins(@RequestBody PersonCustomerRequest req) throws Exception {
        return personService.fetchAvailableLogins(req != null ? req.customerName : null);
    }

    @PostMapping("/cm/person/check")
    public PersonCheckSummary personCheck(@RequestBody PersonCheckRequest req) throws Exception {
        return personService.checkPersons(req);
    }

    @PostMapping("/cm/person/create")
    public PersonCreateSummary personCreate(@RequestBody PersonCreateRequest req) throws Exception {
        try {
            return personService.createPersons(req);
        } finally {
            catalogLoader.invalidateCache();
        }
    }

    @PostMapping("/cm/person/list")
    public PersonManageListResponse personList(@RequestBody PersonCustomerRequest req) throws Exception {
        return personService.listPersonsInFolder(req != null ? req.customerName : null);
    }

    @PostMapping("/cm/person/update")
    public PersonUpdateSummary personUpdate(@RequestBody PersonManageActionRequest req) throws Exception {
        try {
            return personService.updatePersons(req);
        } finally {
            catalogLoader.invalidateCache();
        }
    }

    @PostMapping("/cm/person/delete")
    public PersonDeleteSummary personDelete(@RequestBody PersonManageActionRequest req) throws Exception {
        try {
            return personService.deletePersons(req);
        } finally {
            catalogLoader.invalidateCache();
        }
    }

    @PostMapping("/cm/center/check")
    public CmCenterCheckResponse centerCheck(@RequestBody CenterRequest req) throws Exception {
        return centerService.checkCenter(req);
    }

    @PostMapping("/cm/center/create")
    public CmCenterCreateResponse centerCreate(@RequestBody CenterRequest req) throws Exception {
        try {
            return centerService.createCenter(req);
        } finally {
            catalogLoader.invalidateCache();
        }
    }

    @PostMapping("/cm/center/options/status")
    public CmCenterStatusResponse centerOptionsStatus(@RequestBody CenterRequest req) throws Exception {
        return centerService.checkCenterOptions(req != null ? req.centerName : null);
    }

    @PostMapping("/cm/center/options/apply")
    public CmCenterApplyResponse centerOptionsApply(@RequestBody CenterRequest req) throws Exception {
        try {
            return centerService.applyCenterOptions(req);
        } finally {
            catalogLoader.invalidateCache();
        }
    }

    @PostMapping("/cm/center/delete-artifacts")
    public CmCenterDeleteResponse centerDeleteArtifacts(@RequestBody CenterRequest req) throws Exception {
        try {
            return centerService.deleteCenterArtifacts(req);
        } finally {
            catalogLoader.invalidateCache();
        }
    }

    // ==================== Phase 3: Unified Lookup & Snapshot Diff ====================
    @PostMapping("/cm/lookup/search")
    public LookupSearchResponse lookupSearch(@RequestBody(required = false) LookupSearchRequest req) throws Exception {
        return lookupService.search(req);
    }

    @PostMapping("/cm/lookup/center-audit")
    public CenterAuditResponse lookupCenterAudit(@RequestBody(required = false) CenterAuditRequest req) throws Exception {
        return lookupService.auditCenter(req);
    }

    @GetMapping("/cm/snapshot/list")
    public SnapshotListResponse snapshotList() throws Exception {
        return snapshotService.listSnapshots();
    }

    @PostMapping("/cm/snapshot/capture")
    public SnapshotCaptureResponse snapshotCapture(@RequestBody(required = false) SnapshotCaptureRequest req) throws Exception {
        return snapshotService.capture(req);
    }

    @PostMapping("/cm/snapshot/delete")
    public SnapshotDeleteResponse snapshotDelete(@RequestBody SnapshotDeleteRequest req) throws Exception {
        return snapshotService.delete(req);
    }

    @PostMapping("/cm/snapshot/diff")
    public SnapshotDiffResponse snapshotDiff(@RequestBody SnapshotDiffRequest req) throws Exception {
        return snapshotService.diff(req);
    }

    @PostMapping("/cm/snapshot/restore")
    public SnapshotRestoreResponse snapshotRestore(@RequestBody SnapshotRestoreRequest req) throws Exception {
        try {
            return snapshotService.restore(req);
        } finally {
            catalogLoader.invalidateCache();
        }
    }
}
