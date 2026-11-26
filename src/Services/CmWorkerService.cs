using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CmOxeProvisioner.Models;

namespace CmOxeProvisioner.Services
{
    public class CmWorkerRequest
    {
        public string Action { get; set; }
        public string CustomerName { get; set; }
        public string DnSpec { get; set; }
        public string CtiSpec { get; set; }
        public string CmUserId { get; set; }
        public string CmPasswordPlain { get; set; }
        public bool DoAgentLogin { get; set; }
        public bool DoAcdPosition { get; set; }
        public bool DoAgentDn { get; set; }
        public bool DoPlace { get; set; }
    }

    public class CmFindResultItem
    {
        public string Category { get; set; }
        public string Name { get; set; }
        public int Count { get; set; }
        public List<string> Matches { get; set; }
    }

    public class CmCheckSummary
    {
        public string Customer { get; set; }
        public bool FoldersOk { get; set; }
        public List<string> MissingFolder { get; set; }
        public List<string> DnList { get; set; }
        public List<string> CtiList { get; set; }
        public bool DoAgentDn { get; set; }
        public bool DoPlace { get; set; }
        public bool DoAgentLogin { get; set; }
        public bool DoAcdPos { get; set; }
        public List<CmFindResultItem> FindResults { get; set; }
    }

    public class PersonBatchPayloadItem
    {
        public string UserName { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string EmployeeId { get; set; }
        public bool IsAgent { get; set; }
        public string AgentLogin { get; set; }

        public PersonBatchPayloadItem()
        {
            IsAgent = true;
        }
    }

    public class PersonManagePayloadItem
    {
        public string OriginalUserName { get; set; }
        public string OriginalEmployeeId { get; set; }
        public string UserName { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string EmployeeId { get; set; }
        public bool IsAgent { get; set; }
        public string AgentLogin { get; set; }
        public bool UpdateGeneral { get; set; }
        public bool UpdateAgentLogin { get; set; }
    }

    public class CmPersonWorkerRequest
    {
        public string Action { get; set; }
        public string CustomerName { get; set; }
        public string CmUserId { get; set; }
        public string CmPasswordPlain { get; set; }
        public bool DoGeneral { get; set; }
        public bool DoAgentLogin { get; set; }
        public List<PersonBatchPayloadItem> Items { get; set; }
        public List<PersonManagePayloadItem> ManageItems { get; set; }
    }

    public class PersonLoginPoolDto
    {
        public string loginId { get; set; }
        public string switchName { get; set; }
        public bool isAvailable { get; set; }
        public string usedBy { get; set; }
    }

    public class PersonLoginPoolResponse
    {
        public string customer { get; set; }
        public string personFolderPath { get; set; }
        public int totalCount { get; set; }
        public int availableCount { get; set; }
        public List<PersonLoginPoolDto> items { get; set; }
    }

    public class PersonCheckMatchDto
    {
        public string inputId { get; set; }
        public string userName { get; set; }
        public string employeeId { get; set; }
        public string folderPath { get; set; }
    }

    public class PersonCheckSummary
    {
        public int totalChecked { get; set; }
        public int globalPersonCount { get; set; }
        public List<string> dupEmpIds { get; set; }
        public List<string> dupUserNames { get; set; }
        public List<string> cleanEmpIds { get; set; }
        public List<PersonCheckMatchDto> empMatches { get; set; }
        public List<PersonCheckMatchDto> userMatches { get; set; }
    }

    public class PersonCreateFailureDto
    {
        public string employeeId { get; set; }
        public string userName { get; set; }
        public string reason { get; set; }
    }

    public class PersonCreateResultItemDto
    {
        public string userName { get; set; }
        public string employeeId { get; set; }
        public string agentLogin { get; set; }
    }

    public class PersonCreateSummary
    {
        public string customer { get; set; }
        public string folderPath { get; set; }
        public double elapsedSeconds { get; set; }
        public List<string> createdEmpIds { get; set; }
        public List<PersonCreateResultItemDto> createdItems { get; set; }
        public List<PersonCreateFailureDto> skipped { get; set; }
        public List<PersonCreateFailureDto> failed { get; set; }
    }

    public class PersonManageItemDto
    {
        public string userName { get; set; }
        public string firstName { get; set; }
        public string lastName { get; set; }
        public string employeeId { get; set; }
        public bool isAgent { get; set; }
        public string agentLogin { get; set; }
        public List<string> agentLogins { get; set; }
    }

    public class PersonManageListResponse
    {
        public string customer { get; set; }
        public string folderPath { get; set; }
        public double elapsedSeconds { get; set; }
        public int totalCount { get; set; }
        public List<PersonManageItemDto> items { get; set; }
    }

    public class PersonManageFailureDto
    {
        public string originalUserName { get; set; }
        public string employeeId { get; set; }
        public string userName { get; set; }
        public string reason { get; set; }
    }

    public class PersonUpdateResultItemDto
    {
        public string originalUserName { get; set; }
        public string userName { get; set; }
        public string firstName { get; set; }
        public string lastName { get; set; }
        public string employeeId { get; set; }
        public bool isAgent { get; set; }
        public string agentLogin { get; set; }
    }

    public class PersonUpdateSummary
    {
        public string customer { get; set; }
        public string folderPath { get; set; }
        public double elapsedSeconds { get; set; }
        public List<PersonUpdateResultItemDto> updatedItems { get; set; }
        public List<PersonManageFailureDto> failed { get; set; }
    }

    public class PersonDeleteResultItemDto
    {
        public string originalUserName { get; set; }
        public string userName { get; set; }
        public string employeeId { get; set; }
        public List<string> unlinkedLogins { get; set; }
    }

    public class PersonDeleteSummary
    {
        public string customer { get; set; }
        public string folderPath { get; set; }
        public double elapsedSeconds { get; set; }
        public List<PersonDeleteResultItemDto> deletedItems { get; set; }
        public List<PersonManageFailureDto> skipped { get; set; }
        public List<PersonManageFailureDto> failed { get; set; }
    }

    public class CmCenterWorkerRequest
    {
        public string Action { get; set; }
        public string CenterName { get; set; }
        public string Location { get; set; }
        public bool UseVQueue { get; set; }
        public bool UseIvr { get; set; }
        public bool ApplyVQueue { get; set; }
        public bool WantVQueue { get; set; }
        public bool ApplyIvr { get; set; }
        public bool WantIvr { get; set; }
        public bool DeleteLocationIfSpecified { get; set; }
        public string CmUserId { get; set; }
        public string CmPasswordPlain { get; set; }
    }

    public class CmCenterItemResultDto
    {
        public string key { get; set; }
        public string label { get; set; }
        public string kind { get; set; }
        public string path { get; set; }
        public string status { get; set; }
        public string action { get; set; }
        public bool existsNow { get; set; }
        public string detail { get; set; }
    }

    public class CmCenterCheckResponse
    {
        public string centerName { get; set; }
        public string location { get; set; }
        public bool useVQueue { get; set; }
        public bool useIvr { get; set; }
        public List<CmCenterItemResultDto> items { get; set; }
    }

    public class CmCenterCreateResponse
    {
        public string centerName { get; set; }
        public string location { get; set; }
        public bool useVQueue { get; set; }
        public bool useIvr { get; set; }
        public int createdCount { get; set; }
        public int skippedCount { get; set; }
        public int failedCount { get; set; }
        public double elapsedSeconds { get; set; }
        public List<CmCenterItemResultDto> items { get; set; }
    }

    public class CmCenterStatusResponse
    {
        public string centerName { get; set; }
        public bool vqueueExists { get; set; }
        public bool ivrExists { get; set; }
        public string vqueuePath { get; set; }
        public string ivrPath { get; set; }
    }

    public class CmCenterApplyResponse
    {
        public string centerName { get; set; }
        public bool vqueueExists { get; set; }
        public bool ivrExists { get; set; }
        public int failedCount { get; set; }
        public List<CmCenterItemResultDto> items { get; set; }
    }

    public class CmCenterDeleteResponse
    {
        public string centerName { get; set; }
        public int deletedCount { get; set; }
        public int failedCount { get; set; }
    }

    public class CmConnectResponse
    {
        public bool connected { get; set; }
        public string channelState { get; set; }
        public string activeEndpoint { get; set; }
        public string warmStandbyState { get; set; }
        public int tenantDbid { get; set; }
        public int switchDbid { get; set; }
        public string message { get; set; }
    }

    public class CmHealthResponse
    {
        public string status { get; set; }
        public string service { get; set; }
        public string version { get; set; }
        public bool connected { get; set; }
        public string channelState { get; set; }
        public string activeEndpoint { get; set; }
        public string warmStandbyState { get; set; }
        public int tenantDbid { get; set; }
        public int switchDbid { get; set; }
        public string switchName { get; set; }
        public string lastError { get; set; }
    }

    public class CmWorkerService
    {
        private readonly BackendProcessService _backend;
        private readonly Func<AppSettings> _getSettings;

        static CmWorkerService()
        {
            ServicePointManager.Expect100Continue = false;
        }

        public CmWorkerService(BackendProcessService backend, Func<AppSettings> getSettings)
        {
            _backend = backend;
            _getSettings = getSettings;
        }

        public bool LastConnected { get; private set; }
        public string LastChannelState { get; private set; }
        public string LastActiveEndpoint { get; private set; }
        public string LastWarmStandbyState { get; private set; }
        public string LastError { get; private set; }

        public async Task<CmHealthResponse> GetHealthDirectAsync()
        {
            string json = await SendHttpAsync("GET", "/api/health", null, 5000).ConfigureAwait(false);
            JavaScriptSerializer ser = CreateSerializer();
            CmHealthResponse h = ser.Deserialize<CmHealthResponse>(json);
            if (h != null)
            {
                LastConnected = h.connected;
                LastChannelState = h.channelState;
                LastActiveEndpoint = h.activeEndpoint;
                LastWarmStandbyState = h.warmStandbyState;
                LastError = h.lastError;
            }
            return h;
        }

        public async Task<CmConnectResponse> ConnectDirectAsync(string userOverride = null, string passwordOverride = null)
        {
            Dictionary<string, object> payload = BuildConnectPayload(userOverride, passwordOverride);
            string json = await SendHttpAsync("POST", "/api/cm/connect", payload, 25000).ConfigureAwait(false);
            JavaScriptSerializer ser = CreateSerializer();
            CmConnectResponse conn = ser.Deserialize<CmConnectResponse>(json);
            if (conn != null)
            {
                LastConnected = conn.connected;
                LastChannelState = conn.channelState;
                LastActiveEndpoint = conn.activeEndpoint;
                LastWarmStandbyState = conn.warmStandbyState;
                LastError = conn.connected ? null : conn.message;
            }
            return conn;
        }

        public async Task DisconnectDirectAsync()
        {
            LastConnected = false;
            LastChannelState = "Closed";
            LastActiveEndpoint = "";
            LastWarmStandbyState = "Idle";
            if (_backend == null || !_backend.IsRunning) return;
            try
            {
                string json = await SendHttpAsync("POST", "/api/cm/disconnect", new Dictionary<string, object>(), 5000).ConfigureAwait(false);
                JavaScriptSerializer ser = CreateSerializer();
                CmHealthResponse h = ser.Deserialize<CmHealthResponse>(json);
                if (h != null)
                {
                    LastConnected = h.connected;
                    LastChannelState = h.channelState;
                    LastActiveEndpoint = h.activeEndpoint;
                    LastWarmStandbyState = h.warmStandbyState;
                }
            }
            catch
            {
            }
        }

        public async Task<int> RunAsync(CmWorkerRequest req, Action<string> onLogLine)
        {
            string action = req != null && req.Action != null ? req.Action.Trim() : "";
            Exception caughtEx = null;
            try
            {
                if (string.Equals(action, "Connect", StringComparison.OrdinalIgnoreCase))
                {
                    CmConnectResponse conn = await ConnectDirectAsync(
                        req != null ? req.CmUserId : null,
                        req != null ? req.CmPasswordPlain : null).ConfigureAwait(false);
                    if (conn != null && conn.connected)
                    {
                        EmitLine(onLogLine, string.Format(
                            "[PSDK] Config Server 연결 완료 (Endpoint={0}, State={1}, WarmStandby={2})",
                            conn.activeEndpoint ?? "-",
                            conn.channelState ?? "Opened",
                            conn.warmStandbyState ?? "-"));
                        return 0;
                    }
                    string err = conn != null && !string.IsNullOrEmpty(conn.message)
                        ? conn.message
                        : "Config Server 연결 실패";
                    EmitLine(onLogLine, "[PS-ERR] " + err);
                    return 1;
                }

                string path;
                string marker = null;
                if (string.Equals(action, "Check", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/api/cm/batch/check";
                    marker = "##CM_CHECK_JSON##";
                }
                else if (string.Equals(action, "Create", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/api/cm/batch/create";
                }
                else if (string.Equals(action, "Delete", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/api/cm/batch/delete";
                }
                else
                {
                    throw new ArgumentException("지원하지 않는 Batch 작업입니다: " + action);
                }

                Dictionary<string, object> payload = BuildBatchPayload(req);
                string rawJson = await SendHttpAsync("POST", path, payload, 120000).ConfigureAwait(false);
                LastConnected = true;

                JavaScriptSerializer ser = CreateSerializer();
                Dictionary<string, object> root = ser.Deserialize<Dictionary<string, object>>(rawJson);
                EmitLogsAndAudits(root, ser, onLogLine);
                if (!string.IsNullOrEmpty(marker))
                {
                    EmitLine(onLogLine, marker + rawJson);
                }

                int failedCount = ReadInt(root, "failedCount");
                return failedCount > 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }

            await RefreshConnectionStateQuietlyAsync().ConfigureAwait(false);
            EmitLine(onLogLine, "[PS-ERR] " + caughtEx.Message);
            return 1;
        }

        public async Task<int> RunPersonAsync(CmPersonWorkerRequest req, Action<string> onLogLine)
        {
            string action = req != null && req.Action != null ? req.Action.Trim() : "";
            Exception caughtEx = null;
            try
            {
                string path;
                string marker;
                Dictionary<string, object> payload;

                if (string.Equals(action, "FetchAvailableLogins", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/api/cm/person/available-logins";
                    marker = "##PERSON_LOGINS_JSON##";
                    payload = new Dictionary<string, object>();
                    payload["customerName"] = req.CustomerName ?? "";
                }
                else if (string.Equals(action, "CheckPersons", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/api/cm/person/check";
                    marker = "##PERSON_CHECK_JSON##";
                    payload = BuildPersonCheckPayload(req);
                }
                else if (string.Equals(action, "Create", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "CreatePersons", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/api/cm/person/create";
                    marker = "##PERSON_CREATE_JSON##";
                    payload = BuildPersonCreatePayload(req);
                }
                else if (string.Equals(action, "FetchPersonsInFolder", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/api/cm/person/list";
                    marker = "##PERSON_LIST_JSON##";
                    payload = new Dictionary<string, object>();
                    payload["customerName"] = req.CustomerName ?? "";
                }
                else if (string.Equals(action, "Update", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "UpdatePersons", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/api/cm/person/update";
                    marker = "##PERSON_UPDATE_JSON##";
                    payload = BuildPersonManagePayload(req);
                }
                else if (string.Equals(action, "Delete", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "DeletePersons", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/api/cm/person/delete";
                    marker = "##PERSON_DELETE_JSON##";
                    payload = BuildPersonManagePayload(req);
                }
                else
                {
                    throw new ArgumentException("지원하지 않는 Person 작업입니다: " + action);
                }

                string rawJson = await SendHttpAsync("POST", path, payload, 120000).ConfigureAwait(false);
                LastConnected = true;

                JavaScriptSerializer ser = CreateSerializer();
                Dictionary<string, object> root = ser.Deserialize<Dictionary<string, object>>(rawJson);
                EmitLogsAndAudits(root, ser, onLogLine);
                EmitLine(onLogLine, marker + rawJson);
                return 0;
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }

            await RefreshConnectionStateQuietlyAsync().ConfigureAwait(false);
            EmitLine(onLogLine, "[PS-ERR] " + caughtEx.Message);
            return 1;
        }

        public async Task<int> RunCenterAsync(CmCenterWorkerRequest req, Action<string> onLogLine)
        {
            string action = req != null && req.Action != null ? req.Action.Trim() : "";
            Exception caughtEx = null;
            try
            {
                string path;
                string marker;

                if (string.Equals(action, "Precheck", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "Check", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "CheckCenter", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/api/cm/center/check";
                    marker = "##CENTER_CHECK_JSON##";
                }
                else if (string.Equals(action, "Create", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "CreateCenter", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/api/cm/center/create";
                    marker = "##CENTER_CREATE_JSON##";
                }
                else if (string.Equals(action, "CheckCenterOptions", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/api/cm/center/options/status";
                    marker = "##CENTER_STATUS_JSON##";
                }
                else if (string.Equals(action, "ApplyCenterOptions", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/api/cm/center/options/apply";
                    marker = "##CENTER_APPLY_JSON##";
                }
                else if (string.Equals(action, "DeleteCenterArtifacts", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/api/cm/center/delete-artifacts";
                    marker = "##CENTER_DELETE_JSON##";
                }
                else
                {
                    throw new ArgumentException("지원하지 않는 Center 작업입니다: " + action);
                }

                Dictionary<string, object> payload = BuildCenterPayload(req);
                string rawJson = await SendHttpAsync("POST", path, payload, 120000).ConfigureAwait(false);
                LastConnected = true;

                JavaScriptSerializer ser = CreateSerializer();
                Dictionary<string, object> root = ser.Deserialize<Dictionary<string, object>>(rawJson);
                EmitLogsAndAudits(root, ser, onLogLine);
                EmitLine(onLogLine, marker + rawJson);
                return 0;
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }

            await RefreshConnectionStateQuietlyAsync().ConfigureAwait(false);
            EmitLine(onLogLine, "[PS-ERR] " + caughtEx.Message);
            return 1;
        }

        public Task<LookupSearchResponseDto> LookupSearchAsync(
            string query,
            string customerName,
            string category,
            bool exactMatch,
            bool refresh,
            int limit = 1000)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["query"] = query ?? "";
            payload["customerName"] = customerName ?? "";
            payload["category"] = string.IsNullOrEmpty(category) ? "ALL" : category;
            payload["exactMatch"] = exactMatch;
            payload["refresh"] = refresh;
            payload["limit"] = limit;
            return RequestTypedAsync<LookupSearchResponseDto>("POST", "/api/cm/lookup/search", payload, 120000);
        }

        public Task<CenterAuditResponseDto> CenterAuditAsync(string customerName, bool refresh)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["customerName"] = string.IsNullOrEmpty(customerName) ? "ALL" : customerName;
            payload["refresh"] = refresh;
            return RequestTypedAsync<CenterAuditResponseDto>("POST", "/api/cm/lookup/center-audit", payload, 120000);
        }

        public Task<SnapshotListResponseDto> ListSnapshotsAsync()
        {
            return RequestTypedAsync<SnapshotListResponseDto>("GET", "/api/cm/snapshot/list", null, 30000);
        }

        public Task<SnapshotCaptureResponseDto> CaptureSnapshotAsync(
            string label,
            string customerName,
            bool includeGroupsAndScripts)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["label"] = label ?? "";
            payload["customerName"] = customerName ?? "";
            payload["includeGroupsAndScripts"] = includeGroupsAndScripts;
            return RequestTypedAsync<SnapshotCaptureResponseDto>("POST", "/api/cm/snapshot/capture", payload, 180000);
        }

        public Task<SnapshotDeleteResponseDto> DeleteSnapshotAsync(string snapshotId)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["snapshotId"] = snapshotId ?? "";
            return RequestTypedAsync<SnapshotDeleteResponseDto>("POST", "/api/cm/snapshot/delete", payload, 30000);
        }

        public Task<SnapshotDiffResponseDto> DiffSnapshotsAsync(
            string leftId,
            string rightId,
            string customerFilter,
            string categoryFilter,
            string searchText,
            bool onlyChanged)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["leftId"] = leftId ?? "";
            payload["rightId"] = rightId ?? "";
            payload["customerFilter"] = customerFilter ?? "";
            payload["categoryFilter"] = categoryFilter ?? "";
            payload["searchText"] = searchText ?? "";
            payload["onlyChanged"] = onlyChanged;
            return RequestTypedAsync<SnapshotDiffResponseDto>("POST", "/api/cm/snapshot/diff", payload, 180000);
        }

        public Task<SnapshotRestoreResponseDto> RestoreSnapshotAsync(
            string snapshotId,
            List<string> identityKeys,
            bool deleteAddedObjects,
            bool dryRun)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["snapshotId"] = snapshotId ?? "";
            payload["identityKeys"] = identityKeys ?? new List<string>();
            payload["deleteAddedObjects"] = deleteAddedObjects;
            payload["dryRun"] = dryRun;
            return RequestTypedAsync<SnapshotRestoreResponseDto>("POST", "/api/cm/snapshot/restore", payload, 300000);
        }

        private async Task<T> RequestTypedAsync<T>(string method, string path, object bodyObj, int timeoutMs) where T : class
        {
            Exception caughtEx = null;
            try
            {
                string rawJson = await SendHttpAsync(method, path, bodyObj, timeoutMs).ConfigureAwait(false);
                LastConnected = true;
                JavaScriptSerializer ser = CreateSerializer();
                return ser.Deserialize<T>(rawJson);
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }

            await RefreshConnectionStateQuietlyAsync().ConfigureAwait(false);
            throw caughtEx;
        }

        private async Task<string> SendHttpAsync(string method, string path, object bodyObj, int timeoutMs)
        {
            string baseUrl = await _backend.EnsureRunningAsync().ConfigureAwait(false);
            string url = baseUrl.TrimEnd('/') + path;

            HttpWebRequest http = (HttpWebRequest)WebRequest.Create(url);
            http.Method = method;
            http.Accept = "application/json";
            http.Timeout = timeoutMs;
            http.ReadWriteTimeout = timeoutMs;
            http.Proxy = null;

            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                JavaScriptSerializer ser = CreateSerializer();
                string json = ser.Serialize(bodyObj ?? new Dictionary<string, object>());
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                http.ContentType = "application/json; charset=utf-8";
                http.ContentLength = bytes.Length;
                using (Stream reqStream = await http.GetRequestStreamAsync().ConfigureAwait(false))
                {
                    await reqStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                }
            }

            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)await http.GetResponseAsync().ConfigureAwait(false))
                using (Stream stream = resp.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }
            catch (WebException wex)
            {
                string detail = ExtractServerErrorMessage(wex);
                throw new InvalidOperationException(detail, wex);
            }
        }

        private async Task RefreshConnectionStateQuietlyAsync()
        {
            try
            {
                if (_backend != null && _backend.IsRunning)
                {
                    await GetHealthDirectAsync().ConfigureAwait(false);
                }
                else
                {
                    LastConnected = false;
                }
            }
            catch
            {
                LastConnected = false;
            }
        }

        private Dictionary<string, object> BuildConnectPayload(string userOverride, string passwordOverride)
        {
            AppSettings s = _getSettings != null ? (_getSettings() ?? AppSettings.CreateDefault()) : AppSettings.CreateDefault();
            string user = !string.IsNullOrWhiteSpace(userOverride)
                ? userOverride.Trim()
                : (string.IsNullOrWhiteSpace(s.CmUserId) ? "default" : s.CmUserId.Trim());
            string pass = passwordOverride != null
                ? passwordOverride
                : SettingsService.UnprotectString(s.CmPasswordEnc);

            Dictionary<string, object> d = new Dictionary<string, object>();
            d["primaryHost"] = string.IsNullOrWhiteSpace(s.CmPrimaryHost) ? "10.62.130.67" : s.CmPrimaryHost.Trim();
            d["primaryPort"] = s.CmPrimaryPort > 0 ? s.CmPrimaryPort : 2020;
            d["backupHost"] = (s.CmBackupHost ?? "").Trim();
            d["backupPort"] = s.CmBackupPort > 0 ? s.CmBackupPort : 2020;
            d["clientName"] = string.IsNullOrWhiteSpace(s.CmClientName) ? "default" : s.CmClientName.Trim();
            d["userName"] = user;
            d["password"] = pass ?? "";
            return d;
        }

        private static Dictionary<string, object> BuildBatchPayload(CmWorkerRequest req)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["customerName"] = req != null ? (req.CustomerName ?? "") : "";
            d["dnSpec"] = req != null ? (req.DnSpec ?? "") : "";
            d["ctiSpec"] = req != null ? (req.CtiSpec ?? "") : "";
            d["doAgentLogin"] = req != null && req.DoAgentLogin;
            d["doAcdPosition"] = req != null && req.DoAcdPosition;
            d["doAgentDn"] = req != null && req.DoAgentDn;
            d["doPlace"] = req != null && req.DoPlace;
            return d;
        }

        private static Dictionary<string, object> BuildPersonCheckPayload(CmPersonWorkerRequest req)
        {
            List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
            if (req != null && req.Items != null)
            {
                foreach (PersonBatchPayloadItem it in req.Items)
                {
                    Dictionary<string, object> row = new Dictionary<string, object>();
                    row["userName"] = it.UserName ?? "";
                    row["firstName"] = it.FirstName ?? "";
                    row["lastName"] = it.LastName ?? "";
                    row["employeeId"] = it.EmployeeId ?? "";
                    row["isAgent"] = it.IsAgent;
                    row["agentLogin"] = it.AgentLogin ?? "";
                    items.Add(row);
                }
            }

            Dictionary<string, object> d = new Dictionary<string, object>();
            d["customerName"] = req != null ? (req.CustomerName ?? "") : "";
            d["items"] = items;
            return d;
        }

        private static Dictionary<string, object> BuildPersonCreatePayload(CmPersonWorkerRequest req)
        {
            Dictionary<string, object> d = BuildPersonCheckPayload(req);
            d["doGeneral"] = req != null && req.DoGeneral;
            d["doAgentLogin"] = req != null && req.DoAgentLogin;
            return d;
        }

        private static Dictionary<string, object> BuildPersonManagePayload(CmPersonWorkerRequest req)
        {
            List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
            if (req != null && req.ManageItems != null)
            {
                foreach (PersonManagePayloadItem it in req.ManageItems)
                {
                    Dictionary<string, object> row = new Dictionary<string, object>();
                    row["originalUserName"] = it.OriginalUserName ?? "";
                    row["originalEmployeeId"] = it.OriginalEmployeeId ?? "";
                    row["userName"] = it.UserName ?? "";
                    row["firstName"] = it.FirstName ?? "";
                    row["lastName"] = it.LastName ?? "";
                    row["employeeId"] = it.EmployeeId ?? "";
                    row["isAgent"] = it.IsAgent;
                    row["agentLogin"] = it.AgentLogin ?? "";
                    row["updateGeneral"] = it.UpdateGeneral;
                    row["updateAgentLogin"] = it.UpdateAgentLogin;
                    items.Add(row);
                }
            }

            Dictionary<string, object> d = new Dictionary<string, object>();
            d["customerName"] = req != null ? (req.CustomerName ?? "") : "";
            d["manageItems"] = items;
            return d;
        }

        private static Dictionary<string, object> BuildCenterPayload(CmCenterWorkerRequest req)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["centerName"] = req != null ? (req.CenterName ?? "") : "";
            d["location"] = req != null ? (req.Location ?? "") : "";
            d["useVQueue"] = req != null && req.UseVQueue;
            d["useIvr"] = req != null && req.UseIvr;
            d["applyVQueue"] = req != null && req.ApplyVQueue;
            d["wantVQueue"] = req != null && req.WantVQueue;
            d["applyIvr"] = req != null && req.ApplyIvr;
            d["wantIvr"] = req != null && req.WantIvr;
            d["deleteLocationIfSpecified"] = req != null && req.DeleteLocationIfSpecified;
            return d;
        }

        private static void EmitLogsAndAudits(
            Dictionary<string, object> root,
            JavaScriptSerializer ser,
            Action<string> onLogLine)
        {
            if (root == null || onLogLine == null) return;

            object logsObj;
            if (root.TryGetValue("logs", out logsObj) && logsObj is IEnumerable)
            {
                foreach (object item in (IEnumerable)logsObj)
                {
                    string line = item != null ? item.ToString() : null;
                    if (!string.IsNullOrEmpty(line))
                    {
                        onLogLine(line);
                    }
                }
            }

            object auditsObj;
            if (root.TryGetValue("auditEvents", out auditsObj) && auditsObj is IEnumerable)
            {
                foreach (object evt in (IEnumerable)auditsObj)
                {
                    if (evt == null) continue;
                    onLogLine(AuditLogService.AuditPrefix + ser.Serialize(evt));
                }
            }
        }

        private static int ReadInt(Dictionary<string, object> root, string key)
        {
            object val;
            if (root == null || !root.TryGetValue(key, out val) || val == null) return 0;
            int n;
            return int.TryParse(val.ToString(), out n) ? n : 0;
        }

        private static string ExtractServerErrorMessage(WebException wex)
        {
            if (wex == null) return "알 수 없는 통신 오류";
            if (wex.Response != null)
            {
                try
                {
                    using (Stream stream = wex.Response.GetResponseStream())
                    {
                        if (stream != null)
                        {
                            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                string body = reader.ReadToEnd();
                                if (!string.IsNullOrWhiteSpace(body))
                                {
                                    JavaScriptSerializer ser = CreateSerializer();
                                    Dictionary<string, object> errMap = ser.Deserialize<Dictionary<string, object>>(body);
                                    if (errMap != null)
                                    {
                                        object msgObj;
                                        if (errMap.TryGetValue("error", out msgObj) && msgObj != null && !string.IsNullOrWhiteSpace(msgObj.ToString()))
                                        {
                                            return msgObj.ToString();
                                        }
                                        if (errMap.TryGetValue("message", out msgObj) && msgObj != null && !string.IsNullOrWhiteSpace(msgObj.ToString()))
                                        {
                                            return msgObj.ToString();
                                        }
                                    }
                                    return body;
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            return wex.Message;
        }

        private static void EmitLine(Action<string> onLogLine, string line)
        {
            if (onLogLine != null && !string.IsNullOrEmpty(line))
            {
                onLogLine(line);
            }
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            return ser;
        }
    }
}
