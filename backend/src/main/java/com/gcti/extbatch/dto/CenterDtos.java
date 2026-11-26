package com.gcti.extbatch.dto;

import java.util.ArrayList;
import java.util.List;
import com.gcti.extbatch.dto.CommonDtos.AuditEventDto;

public class CenterDtos {

    public static class CenterRequest {
        public String centerName;
        public String location;
        public boolean useVQueue;
        public boolean useIvr;
        public boolean applyVQueue;
        public boolean wantVQueue;
        public boolean applyIvr;
        public boolean wantIvr;
        public boolean deleteLocationIfSpecified;
    }

    public static class CmCenterItemResultDto {
        public String key;
        public String label;
        public String kind;
        public String path;
        public String status;
        public String action;
        public boolean existsNow;
        public String detail;
    }

    public static class CmCenterCheckResponse {
        public String centerName;
        public String location;
        public boolean useVQueue;
        public boolean useIvr;
        public List<CmCenterItemResultDto> items = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
    }

    public static class CmCenterCreateResponse {
        public String centerName;
        public String location;
        public boolean useVQueue;
        public boolean useIvr;
        public int createdCount;
        public int skippedCount;
        public int failedCount;
        public double elapsedSeconds;
        public List<CmCenterItemResultDto> items = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
        public List<AuditEventDto> auditEvents = new ArrayList<>();
    }

    public static class CmCenterStatusResponse {
        public String centerName;
        public boolean vqueueExists;
        public boolean ivrExists;
        public String vqueuePath;
        public String ivrPath;
        public List<String> logs = new ArrayList<>();
    }

    public static class CmCenterApplyResponse {
        public String centerName;
        public boolean vqueueExists;
        public boolean ivrExists;
        public int failedCount;
        public List<CmCenterItemResultDto> items = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
        public List<AuditEventDto> auditEvents = new ArrayList<>();
    }

    public static class CmCenterDeleteResponse {
        public String centerName;
        public int deletedCount;
        public int failedCount;
        public List<CmCenterItemResultDto> items = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
        public List<AuditEventDto> auditEvents = new ArrayList<>();
    }
}
