using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CmOxeProvisioner.Services
{
    public class AuditEventDto
    {
        public string Action { get; set; }
        public string Target { get; set; }
        public string ObjectType { get; set; }
        public string Name { get; set; }
        public string Customer { get; set; }
        public string Path { get; set; }
        public string Detail { get; set; }
        public string EmpId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string IsAgent { get; set; }
        public List<string> LinkedLogins { get; set; }
        public List<string> UnlinkedLogins { get; set; }
        public string Operation { get; set; }
        public string Reason { get; set; }

        public AuditEventDto()
        {
            LinkedLogins = new List<string>();
            UnlinkedLogins = new List<string>();
        }
    }

    public static class AuditLogService
    {
        public const string AuditPrefix = "##AUDIT_EVENT##";
        private static readonly object FileLock = new object();
        private static readonly JavaScriptSerializer JsonSerializer = new JavaScriptSerializer();

        public static string CleanOneLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var cleaned = text.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Trim();
            while (cleaned.Contains("  ")) cleaned = cleaned.Replace("  ", " ");
            return cleaned;
        }

        public static string GetDailyLogPath(string logDirPath)
        {
            return Path.Combine(logDirPath, "extbatch_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
        }

        public static void AppendLine(string logDirPath, string formattedBody)
        {
            if (string.IsNullOrWhiteSpace(logDirPath) || string.IsNullOrWhiteSpace(formattedBody)) return;
            try
            {
                lock (FileLock)
                {
                    if (!Directory.Exists(logDirPath))
                        Directory.CreateDirectory(logDirPath);

                    string logPath = GetDailyLogPath(logDirPath);
                    string line = string.Format("[{0}] {1}{2}",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        CleanOneLine(formattedBody),
                        Environment.NewLine);
                    File.AppendAllText(logPath, line, new UTF8Encoding(false));
                }
            }
            catch
            {
                // Ignore disk logging failure to keep worker execution resilient
            }
        }

        public static void AppendError(
            string logDirPath,
            string target,
            string operation,
            string reason,
            string customer = null,
            string objectType = null,
            string name = null,
            string path = null,
            string detail = null)
        {
            var dto = new AuditEventDto
            {
                Action = "ERROR",
                Target = target,
                Operation = operation,
                Reason = reason,
                Customer = customer,
                ObjectType = objectType,
                Name = name,
                Path = path,
                Detail = detail
            };
            AppendLine(logDirPath, FormatAuditEvent(dto));
        }

        public static bool TryWriteFromWorkerLine(string logDirPath, string rawLine, out bool isErrorEvent)
        {
            isErrorEvent = false;
            if (string.IsNullOrWhiteSpace(rawLine)) return false;
            string trimmed = rawLine.Trim();
            if (!trimmed.StartsWith(AuditPrefix, StringComparison.Ordinal)) return false;

            string json = trimmed.Substring(AuditPrefix.Length).Trim();
            if (string.IsNullOrEmpty(json)) return true;

            try
            {
                var dto = JsonSerializer.Deserialize<AuditEventDto>(json);
                if (dto == null) return true;
                isErrorEvent = string.Equals(dto.Action, "ERROR", StringComparison.OrdinalIgnoreCase);
                string formatted = FormatAuditEvent(dto);
                if (!string.IsNullOrWhiteSpace(formatted))
                    AppendLine(logDirPath, formatted);
            }
            catch
            {
                isErrorEvent = trimmed.IndexOf("\"ERROR\"", StringComparison.OrdinalIgnoreCase) >= 0;
                AppendLine(logDirPath, "[ERROR] [Config] [AuditParse] Raw=" + CleanOneLine(json));
            }
            return true;
        }

        public static void AppendPbxObjects(
            string logDirPath,
            string action,
            IReadOnlyList<string> dnList,
            IReadOnlyList<string> ctiList,
            string customerName,
            string node,
            string entityNo,
            string agentPg,
            string extProfile,
            string ctiProfile,
            string deliveryMode)
        {
            string act = string.IsNullOrWhiteSpace(action) ? "CREATE" : action.Trim().ToUpperInvariant();
            string cust = CleanOneLine(customerName);
            string nodeVal = string.IsNullOrWhiteSpace(node) ? "(기본)" : CleanOneLine(node);
            string modeVal = CleanOneLine(deliveryMode);

            if (dnList != null)
            {
                foreach (var dn in dnList)
                {
                    if (string.IsNullOrWhiteSpace(dn)) continue;
                    var parts = new List<string>
                    {
                        string.Format("[{0}] [PBX] [Subscriber(Extension)] {1}", act, CleanOneLine(dn))
                    };
                    if (!string.IsNullOrWhiteSpace(cust)) parts.Add("센터=" + cust);
                    parts.Add("Node=" + nodeVal);
                    if (!string.IsNullOrWhiteSpace(extProfile)) parts.Add("Profile=" + CleanOneLine(extProfile));
                    if (!string.IsNullOrWhiteSpace(modeVal)) parts.Add("전송=" + modeVal);
                    AppendLine(logDirPath, string.Join(" | ", parts));
                }
            }

            if (ctiList != null)
            {
                foreach (var cti in ctiList)
                {
                    if (string.IsNullOrWhiteSpace(cti)) continue;
                    var parts = new List<string>
                    {
                        string.Format("[{0}] [PBX] [Subscriber(CTI / ACD2)] {1}", act, CleanOneLine(cti))
                    };
                    if (!string.IsNullOrWhiteSpace(cust)) parts.Add("센터=" + cust);
                    parts.Add("Node=" + nodeVal);
                    if (!string.IsNullOrWhiteSpace(entityNo)) parts.Add("Entity=" + CleanOneLine(entityNo));
                    if (!string.IsNullOrWhiteSpace(agentPg)) parts.Add("PG=" + CleanOneLine(agentPg));
                    if (!string.IsNullOrWhiteSpace(ctiProfile)) parts.Add("Profile=" + CleanOneLine(ctiProfile));
                    if (!string.IsNullOrWhiteSpace(modeVal)) parts.Add("전송=" + modeVal);
                    AppendLine(logDirPath, string.Join(" | ", parts));
                }
            }
        }

        public static string FormatAuditEvent(AuditEventDto dto)
        {
            if (dto == null) return "";
            string action = string.IsNullOrWhiteSpace(dto.Action) ? "INFO" : dto.Action.Trim().ToUpperInvariant();
            string target = string.IsNullOrWhiteSpace(dto.Target) ? "Config" : dto.Target.Trim();

            if (action == "ERROR")
            {
                var sb = new StringBuilder();
                sb.Append("[ERROR] [").Append(target).Append(']');
                if (!string.IsNullOrWhiteSpace(dto.Operation))
                    sb.Append(" [").Append(CleanOneLine(dto.Operation)).Append(']');
                else if (!string.IsNullOrWhiteSpace(dto.ObjectType))
                    sb.Append(" [").Append(CleanOneLine(dto.ObjectType)).Append(']');

                var subjectMeta = new List<string>();
                if (!string.IsNullOrWhiteSpace(dto.ObjectType) && !string.IsNullOrWhiteSpace(dto.Operation))
                    subjectMeta.Add("형식=" + CleanOneLine(dto.ObjectType));
                if (!string.IsNullOrWhiteSpace(dto.EmpId))
                    subjectMeta.Add("EmpID=" + CleanOneLine(dto.EmpId));

                string subj = CleanOneLine(dto.Name);
                if (!string.IsNullOrEmpty(subj))
                    sb.Append(' ').Append(subj);
                if (subjectMeta.Count > 0)
                    sb.Append(" (").Append(string.Join(", ", subjectMeta)).Append(')');

                var errSegments = new List<string> { sb.ToString() };
                if (!string.IsNullOrWhiteSpace(dto.Customer)) errSegments.Add("센터=" + CleanOneLine(dto.Customer));
                if (!string.IsNullOrWhiteSpace(dto.Path)) errSegments.Add("경로=" + CleanOneLine(dto.Path));
                if (!string.IsNullOrWhiteSpace(dto.Detail)) errSegments.Add(CleanOneLine(dto.Detail));
                if (!string.IsNullOrWhiteSpace(dto.Reason)) errSegments.Add("사유=" + CleanOneLine(dto.Reason));
                return string.Join(" | ", errSegments);
            }

            var header = new StringBuilder();
            header.Append('[').Append(action).Append("] [").Append(target).Append(']');
            if (!string.IsNullOrWhiteSpace(dto.ObjectType))
                header.Append(" [").Append(CleanOneLine(dto.ObjectType)).Append(']');
            if (!string.IsNullOrWhiteSpace(dto.Name))
                header.Append(' ').Append(CleanOneLine(dto.Name));

            var meta = new List<string>();
            if (!string.IsNullOrWhiteSpace(dto.EmpId)) meta.Add("EmpID=" + CleanOneLine(dto.EmpId));
            if (!string.IsNullOrWhiteSpace(dto.FirstName)) meta.Add("First=" + CleanOneLine(dto.FirstName));
            if (!string.IsNullOrWhiteSpace(dto.LastName)) meta.Add("Last=" + CleanOneLine(dto.LastName));
            if (!string.IsNullOrWhiteSpace(dto.IsAgent))
            {
                bool isAg;
                if (bool.TryParse(dto.IsAgent, out isAg))
                    meta.Add("IsAgent=" + (isAg ? "Y" : "N"));
                else
                    meta.Add("IsAgent=" + CleanOneLine(dto.IsAgent));
            }
            if (dto.LinkedLogins != null && dto.LinkedLogins.Count > 0)
                meta.Add("Agent Login 연결=" + string.Join(",", dto.LinkedLogins));
            if (dto.UnlinkedLogins != null && dto.UnlinkedLogins.Count > 0)
                meta.Add("Agent Login 해제=" + string.Join(",", dto.UnlinkedLogins));
            if (!string.IsNullOrWhiteSpace(dto.Detail))
                meta.Add(CleanOneLine(dto.Detail));

            if (meta.Count > 0)
                header.Append(" (").Append(string.Join(", ", meta)).Append(')');

            var segs = new List<string> { header.ToString() };
            if (!string.IsNullOrWhiteSpace(dto.Customer)) segs.Add("센터=" + CleanOneLine(dto.Customer));
            if (!string.IsNullOrWhiteSpace(dto.Path)) segs.Add("경로=" + CleanOneLine(dto.Path));
            return string.Join(" | ", segs);
        }
    }
}
