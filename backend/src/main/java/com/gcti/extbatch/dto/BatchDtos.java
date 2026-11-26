package com.gcti.extbatch.dto;

import java.util.ArrayList;
import java.util.List;
import com.gcti.extbatch.dto.CommonDtos.AuditEventDto;

public class BatchDtos {

    public static class BatchRequest {
        public String customerName;
        public String dnSpec;
        public String ctiSpec;
        public boolean doAgentLogin = true;
        public boolean doAcdPosition = true;
        public boolean doAgentDn = true;
        public boolean doPlace = true;
        public boolean skipDuplicateCheck = false;
    }

    public static class CmFindResultItem {
        public String category;
        public String name;
        public int count;
        public List<String> matches = new ArrayList<>();
    }

    public static class BatchCheckResponse {
        public String customer = "";
        public boolean foldersOk = true;
        public List<String> missingFolder = new ArrayList<>();
        public List<String> dnList = new ArrayList<>();
        public List<String> ctiList = new ArrayList<>();
        public boolean doAgentDn;
        public boolean doPlace;
        public boolean doAgentLogin;
        public boolean doAcdPos;
        public List<CmFindResultItem> findResults = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
    }

    public static class BatchActionItemResult {
        public String category;
        public String name;
        public String status; // Created | Deleted | Skipped | Failed
        public String path;
        public String detail;
        public Integer dbid;
    }

    public static class BatchActionResponse {
        public boolean ok = true;
        public String action;
        public String customer = "";
        public double elapsedSeconds;
        public int createdCount;
        public int deletedCount;
        public int skippedCount;
        public int failedCount;
        public List<BatchActionItemResult> results = new ArrayList<>();
        public List<String> logs = new ArrayList<>();
        public List<AuditEventDto> auditEvents = new ArrayList<>();
    }
}
