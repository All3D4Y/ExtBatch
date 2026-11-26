package com.gcti.extbatch.dto;

import java.util.ArrayList;
import java.util.List;

public class CommonDtos {

    public static class HealthResponse {
        public String status = "UP";
        public String service = "extbatch-backend";
        public String version = "2.0.0";
        public boolean connected;
        public String channelState;
        public String activeEndpoint;
        public String warmStandbyState;
        public int tenantDbid;
        public int switchDbid;
        public String switchName;
        public String lastError;
    }

    public static class ConnectRequest {
        public String primaryHost;
        public Integer primaryPort;
        public String backupHost;
        public Integer backupPort;
        public String userName;
        public String password;
        public String clientName;
    }

    public static class ConnectResponse {
        public boolean connected;
        public String channelState;
        public String activeEndpoint;
        public String warmStandbyState;
        public int tenantDbid;
        public int switchDbid;
        public String message;
    }

    public static class AuditEventDto {
        public String action;
        public String target = "Config";
        public String objectType;
        public String name;
        public String customer;
        public String path;
        public String detail;
        public String empId;
        public String firstName;
        public String lastName;
        public String isAgent;
        public List<String> linkedLogins = new ArrayList<>();
        public List<String> unlinkedLogins = new ArrayList<>();
        public String operation;
        public String reason;

        public static AuditEventDto create(String objectType, String name, String customer, String path, String detail) {
            AuditEventDto e = new AuditEventDto();
            e.action = "CREATE";
            e.objectType = objectType;
            e.name = name;
            e.customer = customer;
            e.path = path;
            e.detail = detail;
            return e;
        }

        public static AuditEventDto delete(String objectType, String name, String customer, String path, String detail) {
            AuditEventDto e = new AuditEventDto();
            e.action = "DELETE";
            e.objectType = objectType;
            e.name = name;
            e.customer = customer;
            e.path = path;
            e.detail = detail;
            return e;
        }

        public static AuditEventDto update(String objectType, String name, String customer, String path, String detail) {
            AuditEventDto e = new AuditEventDto();
            e.action = "UPDATE";
            e.objectType = objectType;
            e.name = name;
            e.customer = customer;
            e.path = path;
            e.detail = detail;
            return e;
        }

        public static AuditEventDto error(String operation, String objectType, String name, String customer, String path, String reason) {
            AuditEventDto e = new AuditEventDto();
            e.action = "ERROR";
            e.operation = operation;
            e.objectType = objectType;
            e.name = name;
            e.customer = customer;
            e.path = path;
            e.reason = reason;
            return e;
        }
    }

    public static class ApiErrorResponse {
        public boolean ok = false;
        public String error;
        public String detail;
        public String path;

        public ApiErrorResponse() {}
        public ApiErrorResponse(String error, String detail, String path) {
            this.error = error;
            this.detail = detail;
            this.path = path;
        }
    }
}
