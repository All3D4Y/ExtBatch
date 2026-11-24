using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CmOxeProvisioner.Services
{
    public class CmWorkerRequest
    {
        public string Action { get; set; }
        public string CustomerName { get; set; }
        public string DnSpec { get; set; }
        public string CtiSpec { get; set; }
        public string CmExePath { get; set; }
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
        public string CmExePath { get; set; }
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

    public class CmWorkerService
    {
        private static readonly Regex ClixmlMsgRegex = new Regex(
            @"<S\s+S=""(Error|Warning)"">(.*?)</S>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex ClixmlIgnorableObjRegex = new Regex(
            @"<(Obj|S)\s+S=""(progress|verbose|debug|information)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly string _enginePath;
        private readonly string _personEnginePath;

        public CmWorkerService(string enginePath)
        {
            _enginePath = enginePath;
            string dir = string.IsNullOrEmpty(enginePath) ? "" : Path.GetDirectoryName(enginePath);
            _personEnginePath = string.IsNullOrEmpty(dir) ? "" : Path.Combine(dir, "cm_person_engine.ps1");
        }

        private static string EscapePsSingle(string val)
        {
            if (val == null) return "";
            return val.Replace("'", "''");
        }

        private static List<string> ExtractClixmlMessages(string xml)
        {
            List<string> lines = new List<string>();
            if (string.IsNullOrWhiteSpace(xml)) return lines;

            MatchCollection matches = ClixmlMsgRegex.Matches(xml);
            if (matches.Count > 0)
            {
                StringBuilder combined = new StringBuilder();
                foreach (Match m in matches)
                {
                    string chunk = m.Groups[2].Value
                        .Replace("_x000D__x000A_", "\n")
                        .Replace("_x000D_", "\n")
                        .Replace("_x000A_", "\n")
                        .Replace("_x0009_", "\t");
                    combined.Append(WebUtility.HtmlDecode(chunk));
                }
                string[] parts = combined.ToString().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(parts[i]))
                    {
                        lines.Add(parts[i].TrimEnd());
                    }
                }
                return lines;
            }

            // Progress/verbose/debug-only CLIXML packets are suppressed completely.
            if (ClixmlIgnorableObjRegex.IsMatch(xml))
            {
                return lines;
            }

            string plain = WebUtility.HtmlDecode(Regex.Replace(xml, "<[^>]+>", " ").Replace("_x000D__x000A_", " ")).Trim();
            if (!string.IsNullOrEmpty(plain))
            {
                lines.Add(plain);
            }
            return lines;
        }

        public Task<int> RunAsync(CmWorkerRequest req, Action<string> onLogLine)
        {
            if (!File.Exists(_enginePath))
            {
                if (onLogLine != null) onLogLine("[ERR] cm_batch_engine.ps1 파일을 찾을 수 없습니다: " + _enginePath);
                return Task.FromResult(1);
            }

            string psLine = string.Format(
                "& '{0}' -Action '{1}' -Customer '{2}' -DnSpec '{3}' -CtiSpec '{4}' -CmExePath '{5}' -CmUser '{6}' -DoAgentLogin:${7} -DoAcdPosition:${8} -DoAgentDn:${9} -DoPlace:${10}",
                EscapePsSingle(_enginePath),
                EscapePsSingle(req.Action),
                EscapePsSingle(req.CustomerName),
                EscapePsSingle(req.DnSpec),
                EscapePsSingle(req.CtiSpec),
                EscapePsSingle(req.CmExePath),
                EscapePsSingle(req.CmUserId),
                req.DoAgentLogin ? "true" : "false",
                req.DoAcdPosition ? "true" : "false",
                req.DoAgentDn ? "true" : "false",
                req.DoPlace ? "true" : "false"
            );

            return ExecutePsScriptAsync(psLine, req.CmPasswordPlain, null, onLogLine);
        }

        public Task<int> RunPersonAsync(CmPersonWorkerRequest req, Action<string> onLogLine)
        {
            if (string.IsNullOrEmpty(_personEnginePath) || !File.Exists(_personEnginePath))
            {
                if (onLogLine != null) onLogLine("[ERR] cm_person_engine.ps1 파일을 찾을 수 없습니다: " + (_personEnginePath ?? ""));
                return Task.FromResult(1);
            }

            string tempPayloadPath = null;
            if (req.ManageItems != null && req.ManageItems.Count > 0)
            {
                tempPayloadPath = Path.Combine(Path.GetTempPath(), "extbatch_person_manage_" + Guid.NewGuid().ToString("N") + ".json");
                JavaScriptSerializer jss = new JavaScriptSerializer();
                jss.MaxJsonLength = 16 * 1024 * 1024;
                File.WriteAllText(tempPayloadPath, jss.Serialize(req.ManageItems), new UTF8Encoding(false));
            }
            else if (req.Items != null && req.Items.Count > 0)
            {
                tempPayloadPath = Path.Combine(Path.GetTempPath(), "extbatch_person_" + Guid.NewGuid().ToString("N") + ".json");
                JavaScriptSerializer jss = new JavaScriptSerializer();
                jss.MaxJsonLength = 16 * 1024 * 1024;
                File.WriteAllText(tempPayloadPath, jss.Serialize(req.Items), new UTF8Encoding(false));
            }

            string psLine = string.Format(
                "& '{0}' -Action '{1}' -Customer '{2}' -CmExePath '{3}' -CmUser '{4}' -DoGeneral:${5} -DoAgentLogin:${6}",
                EscapePsSingle(_personEnginePath),
                EscapePsSingle(req.Action),
                EscapePsSingle(req.CustomerName),
                EscapePsSingle(req.CmExePath),
                EscapePsSingle(req.CmUserId),
                req.DoGeneral ? "true" : "false",
                req.DoAgentLogin ? "true" : "false"
            );

            return ExecutePsScriptAsync(psLine, req.CmPasswordPlain, tempPayloadPath, onLogLine);
        }

        private Task<int> ExecutePsScriptAsync(string commandLine, string passwordPlain, string payloadJsonPath, Action<string> onLogLine)
        {
            return Task.Run(delegate()
            {
                try
                {
                    StringBuilder psCmd = new StringBuilder();
                    psCmd.AppendLine("$ProgressPreference = 'SilentlyContinue'");
                    psCmd.AppendLine("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8");
                    psCmd.AppendLine(commandLine);

                    byte[] cmdBytes = Encoding.Unicode.GetBytes(psCmd.ToString());
                    string encoded = Convert.ToBase64String(cmdBytes);

                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
                    psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -STA -EncodedCommand " + encoded;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    psi.StandardErrorEncoding = Encoding.UTF8;
                    psi.EnvironmentVariables["CM_PASS_TEMP"] = passwordPlain ?? "";
                    if (!string.IsNullOrEmpty(payloadJsonPath))
                    {
                        psi.EnvironmentVariables["CM_PERSON_PAYLOAD_PATH"] = payloadJsonPath;
                    }

                    object errLock = new object();
                    StringBuilder clixmlBuf = new StringBuilder();

                    using (Process proc = new Process())
                    {
                        proc.StartInfo = psi;
                        proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                        {
                            if (!string.IsNullOrEmpty(e.Data) && onLogLine != null)
                            {
                                onLogLine(e.Data);
                            }
                        };
                        proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                        {
                            if (onLogLine == null) return;
                            lock (errLock)
                            {
                                if (e.Data == null)
                                {
                                    if (clixmlBuf.Length > 0)
                                    {
                                        List<string> rem = ExtractClixmlMessages(clixmlBuf.ToString());
                                        clixmlBuf.Length = 0;
                                        for (int i = 0; i < rem.Count; i++) onLogLine("[PS-ERR] " + rem[i]);
                                    }
                                    return;
                                }

                                string raw = e.Data;
                                string trimmed = raw.Trim();
                                if (trimmed.Length == 0 || trimmed == "#< CLIXML") return;

                                if (clixmlBuf.Length > 0 || trimmed.StartsWith("<Objs", StringComparison.OrdinalIgnoreCase))
                                {
                                    clixmlBuf.AppendLine(raw);
                                    if (raw.IndexOf("</Objs>", StringComparison.OrdinalIgnoreCase) >= 0 || clixmlBuf.Length > 65536)
                                    {
                                        List<string> msgs = ExtractClixmlMessages(clixmlBuf.ToString());
                                        clixmlBuf.Length = 0;
                                        for (int i = 0; i < msgs.Count; i++) onLogLine("[PS-ERR] " + msgs[i]);
                                    }
                                    return;
                                }

                                onLogLine("[PS-ERR] " + raw);
                            }
                        };
                        proc.Start();
                        proc.BeginOutputReadLine();
                        proc.BeginErrorReadLine();
                        proc.WaitForExit();
                        return proc.ExitCode;
                    }
                }
                finally
                {
                    if (!string.IsNullOrEmpty(payloadJsonPath))
                    {
                        try { if (File.Exists(payloadJsonPath)) File.Delete(payloadJsonPath); } catch { }
                    }
                }
            });
        }
    }
}
