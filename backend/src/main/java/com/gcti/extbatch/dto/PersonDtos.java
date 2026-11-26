package com.gcti.extbatch.dto;

import java.util.ArrayList;
import java.util.List;
import com.gcti.extbatch.dto.CommonDtos.AuditEventDto;

public class PersonDtos {

    public static class PersonCustomerRequest {
        public String customerName;
    }

    public static class PersonBatchPayloadItem {
        public String userName;
        public String firstName;
        public String lastName;
        public String employeeId;
        public boolean isAgent = true;
        public String agentLogin;
    }

    public static class PersonManagePayloadItem {
        public String originalUserName;
        public String originalEmployeeId;
        public String userName;
        public String firstName;
        public String lastName;
        public String employeeId;
        public boolean isAgent;
        public String agentLogin;
        public boolean updateGeneral;
        public boolean updateAgentLogin;
    }

    public static class PersonCheckRequest {
        public String customerName;
        public List<PersonBatchPayloadItem> items = new ArrayList<>();
    }

    public static class PersonCreateRequest {
        public String customerName;
        public boolean doGeneral = true;
        public boolean doAgentLogin = true;
        public List<PersonBatchPayloadItem> items = new ArrayList<>();
    }

    public static class PersonManageActionRequest {
        public String customerName;
        public List<PersonManagePayloadItem> manageItems = new ArrayList<>();
    }

    public static class PersonLoginPoolDto {
        public String loginId;
        public String switchName;
        public boolean isAvailable;
        public String usedBy;
    }

    public static class PersonLoginPoolResponse {
        public String customer;
        public String personFolderPath;
        public int totalCount;
        public int availableCount;
        public List<PersonLoginPoolDto> items = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
    }

    public static class PersonCheckMatchDto {
        public String inputId;
        public String userName;
        public String employeeId;
        public String folderPath;
    }

    public static class PersonCheckSummary {
        public int totalChecked;
        public int globalPersonCount;
        public List<String> dupEmpIds = new ArrayList<>();
        public List<String> dupUserNames = new ArrayList<>();
        public List<String> cleanEmpIds = new ArrayList<>();
        public List<PersonCheckMatchDto> empMatches = new ArrayList<>();
        public List<PersonCheckMatchDto> userMatches = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
    }

    public static class PersonCreateResultItemDto {
        public String userName;
        public String employeeId;
        public String agentLogin;
    }

    public static class PersonCreateFailureDto {
        public String employeeId;
        public String userName;
        public String reason;
    }

    public static class PersonCreateSummary {
        public String customer;
        public String folderPath;
        public double elapsedSeconds;
        public List<String> createdEmpIds = new ArrayList<>();
        public List<PersonCreateResultItemDto> createdItems = new ArrayList<>();
        public List<PersonCreateFailureDto> skipped = new ArrayList<>();
        public List<PersonCreateFailureDto> failed = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
        public List<AuditEventDto> auditEvents = new ArrayList<>();
    }

    public static class PersonManageItemDto {
        public String userName;
        public String firstName;
        public String lastName;
        public String employeeId;
        public boolean isAgent;
        public String agentLogin;
        public List<String> agentLogins = new ArrayList<>();
    }

    public static class PersonManageListResponse {
        public String customer;
        public String folderPath;
        public double elapsedSeconds;
        public int totalCount;
        public List<PersonManageItemDto> items = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
    }

    public static class PersonManageFailureDto {
        public String originalUserName;
        public String employeeId;
        public String userName;
        public String reason;
    }

    public static class PersonUpdateResultItemDto {
        public String originalUserName;
        public String userName;
        public String firstName;
        public String lastName;
        public String employeeId;
        public boolean isAgent;
        public String agentLogin;
    }

    public static class PersonUpdateSummary {
        public String customer;
        public String folderPath;
        public double elapsedSeconds;
        public List<PersonUpdateResultItemDto> updatedItems = new ArrayList<>();
        public List<PersonManageFailureDto> failed = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
        public List<AuditEventDto> auditEvents = new ArrayList<>();
    }

    public static class PersonDeleteResultItemDto {
        public String originalUserName;
        public String userName;
        public String employeeId;
        public List<String> unlinkedLogins = new ArrayList<>();
    }

    public static class PersonDeleteSummary {
        public String customer;
        public String folderPath;
        public double elapsedSeconds;
        public List<PersonDeleteResultItemDto> deletedItems = new ArrayList<>();
        public List<PersonManageFailureDto> skipped = new ArrayList<>();
        public List<PersonManageFailureDto> failed = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
        public List<AuditEventDto> auditEvents = new ArrayList<>();
    }
}
