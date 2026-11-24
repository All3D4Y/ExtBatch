using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using CmOxeProvisioner.Models;

namespace CmOxeProvisioner.Services
{
    public class NumberErrorSpan
    {
        public int Start { get; set; }
        public int Length { get; set; }
        public string Token { get; set; }
        public string Message { get; set; }
    }

    public class NumberParseResult
    {
        public List<string> Items { get; set; }
        public List<string> Errors { get; set; }
        public List<NumberErrorSpan> ErrorSpans { get; set; }

        public NumberParseResult()
        {
            Items = new List<string>();
            Errors = new List<string>();
            ErrorSpans = new List<NumberErrorSpan>();
        }
    }

    public class OxeBuildResult
    {
        public bool Ok { get; set; }
        public List<string> Errors { get; set; }
        public string Script { get; set; }
        public List<string> DnList { get; set; }
        public List<string> CtiList { get; set; }
        public int LineCount { get; set; }

        public OxeBuildResult()
        {
            Errors = new List<string>();
            DnList = new List<string>();
            CtiList = new List<string>();
            Script = "";
        }
    }

    public class PbxCheckItem
    {
        public string Kind { get; set; }
        public string Number { get; set; }
        public bool Exists { get; set; }
        public string EntityNo { get; set; }
        public List<string> Pgs { get; set; }
        public string CustomerName { get; set; }

        public PbxCheckItem()
        {
            Pgs = new List<string>();
            EntityNo = "";
            CustomerName = "unknown";
        }
    }

    public class PbxCheckSummary
    {
        public string SelectedCustomer { get; set; }
        public List<PbxCheckItem> Items { get; set; }
        public List<string> DnList { get; set; }
        public List<string> CtiList { get; set; }

        public PbxCheckSummary()
        {
            Items = new List<PbxCheckItem>();
            DnList = new List<string>();
            CtiList = new List<string>();
        }
    }

    public static class OxeScriptService
    {
        private static readonly Regex DigitsOnlyRegex = new Regex(@"^\d+$", RegexOptions.Compiled);
        private static readonly Regex RangeRegex = new Regex(@"^(\d+)[~\-](\d+)$", RegexOptions.Compiled);
        private static readonly Regex TokenRegex = new Regex(@"[^,;\r\n\s\t]+", RegexOptions.Compiled);

        public static NumberParseResult ParseNumbersStrict(string raw)
        {
            return ParseNumbersStrict(raw, "", 0, "번호");
        }

        private static void AddParseError(NumberParseResult res, int start, int length, string tok, string message)
        {
            res.Errors.Add(message);
            NumberErrorSpan sp = new NumberErrorSpan();
            sp.Start = start;
            sp.Length = length;
            sp.Token = tok;
            sp.Message = message;
            res.ErrorSpans.Add(sp);
        }

        public static NumberParseResult ParseNumbersStrict(string raw, string requiredPrefix, int requiredLength, string fieldLabel)
        {
            NumberParseResult res = new NumberParseResult();
            if (string.IsNullOrWhiteSpace(raw)) return res;

            HashSet<string> seen = new HashSet<string>();
            MatchCollection matches = TokenRegex.Matches(raw);
            for (int i = 0; i < matches.Count; i++)
            {
                Match tm = matches[i];
                string tok = tm.Value;

                if (tok.Contains("~") || tok.Contains("-"))
                {
                    Match m = RangeRegex.Match(tok);
                    if (!m.Success)
                    {
                        AddParseError(res, tm.Index, tm.Length, tok, string.Format("{0} 범위 형식 오류: '{1}' (예: 39950~39955)", fieldLabel, tok));
                        continue;
                    }
                    string sStr = m.Groups[1].Value;
                    string eStr = m.Groups[2].Value;
                    string errS = GetTokenFormatError(sStr, requiredPrefix, requiredLength, fieldLabel);
                    if (errS != null)
                    {
                        AddParseError(res, tm.Index, sStr.Length, sStr, errS);
                    }
                    string errE = GetTokenFormatError(eStr, requiredPrefix, requiredLength, fieldLabel);
                    if (errE != null)
                    {
                        int eOffset = tm.Index + sStr.Length + 1;
                        AddParseError(res, eOffset, eStr.Length, eStr, errE);
                    }
                    if (errS != null || errE != null) continue;

                    long a = long.Parse(sStr);
                    long b = long.Parse(eStr);
                    if (b < a)
                    {
                        AddParseError(res, tm.Index, tm.Length, tok, string.Format("{0} 범위 역전 오류: '{1}' (끝 번호가 시작 번호보다 작음)", fieldLabel, tok));
                        continue;
                    }
                    if ((b - a + 1) > 500)
                    {
                        AddParseError(res, tm.Index, tm.Length, tok, string.Format("단일 범위 과다: '{0}' (최대 500건 제한)", tok));
                        continue;
                    }
                    int width = sStr.Length;
                    for (long n = a; n <= b; n++)
                    {
                        string val = n.ToString().PadLeft(width, '0');
                        if (seen.Add(val)) res.Items.Add(val);
                    }
                }
                else if (DigitsOnlyRegex.IsMatch(tok))
                {
                    string errTok = GetTokenFormatError(tok, requiredPrefix, requiredLength, fieldLabel);
                    if (errTok != null)
                    {
                        AddParseError(res, tm.Index, tm.Length, tok, errTok);
                        continue;
                    }
                    if (seen.Add(tok)) res.Items.Add(tok);
                }
                else
                {
                    AddParseError(res, tm.Index, tm.Length, tok, string.Format("{0} 형식이 맞지 않습니다: '{1}' (숫자 또는 ~ 범위만 허용)", fieldLabel, tok));
                }
            }
            return res;
        }

        private static string GetTokenFormatError(string numStr, string requiredPrefix, int requiredLength, string fieldLabel)
        {
            if (requiredLength > 0 && numStr.Length != requiredLength)
            {
                return string.Format("{0} 형식이 맞지 않습니다: '{1}' ({2}로 시작하는 {3}자리 숫자여야 함)", fieldLabel, numStr, requiredPrefix, requiredLength);
            }
            if (!string.IsNullOrEmpty(requiredPrefix) && !numStr.StartsWith(requiredPrefix))
            {
                return string.Format("{0} 형식이 맞지 않습니다: '{1}' ({2}로 시작하는 번호여야 함)", fieldLabel, numStr, requiredPrefix);
            }
            return null;
        }

        public static OxeBuildResult BuildAndValidate(
            string mode,
            string dnRaw,
            string ctiRaw,
            string entityNo,
            string agentPg,
            string profileExt,
            string profileLogin,
            int nodeNumber)
        {
            OxeBuildResult result = new OxeBuildResult();
            NumberParseResult dnParsed = ParseNumbersStrict(dnRaw, "3", 5, "내선번호");
            NumberParseResult ctiParsed = ParseNumbersStrict(ctiRaw, "7", 5, "CTI");

            result.Errors.AddRange(dnParsed.Errors);
            result.Errors.AddRange(ctiParsed.Errors);

            List<string> oxeDns = dnParsed.Items;
            List<string> oxeCtis = ctiParsed.Items;

            if (oxeDns.Count == 0 && oxeCtis.Count == 0 && result.Errors.Count == 0)
            {
                result.Errors.Add("내선번호(3XXXX) 또는 CTI 번호(7XXXX)를 입력하세요.");
            }

            string ent = (entityNo ?? "").Trim();
            string pg = (agentPg ?? "").Trim();
            string pExt = (profileExt ?? "").Trim();
            string pLog = (profileLogin ?? "").Trim();
            if (nodeNumber <= 0) nodeNumber = 1;

            if (mode == "create")
            {
                if (oxeCtis.Count > 0)
                {
                    if (!DigitsOnlyRegex.IsMatch(ent))
                        result.Errors.Add("Entity NO 값이 누락되었거나 숫자가 아닙니다.");
                    if (!DigitsOnlyRegex.IsMatch(pg))
                        result.Errors.Add("Agent PG 값이 누락되었거나 숫자가 아닙니다.");
                    if (!DigitsOnlyRegex.IsMatch(pLog))
                        result.Errors.Add("기준 Login(Profile) 번호가 숫자가 아닙니다.");
                }
                if (oxeDns.Count > 0 && !DigitsOnlyRegex.IsMatch(pExt))
                {
                    result.Errors.Add("기준 내선(Profile) 번호가 숫자가 아닙니다.");
                }
            }

            if (result.Errors.Count > 0)
            {
                result.Ok = false;
                return result;
            }

            List<string> lines = new List<string>();
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string cfgFile = string.Format("/tmp/oxe_{0}_{1}.cfg", mode, ts);

            lines.Add("#!/bin/sh");
            lines.Add("# ===================================================================");

            if (mode == "create")
            {
                lines.Add("# [Alcatel OXE R12.4 Create]");
                lines.Add(string.Format("# Ext({0}): {1}", oxeDns.Count, string.Join(", ", oxeDns.ToArray())));
                lines.Add(string.Format("# Login({0}): {1} | Entity={2} | PG={3}", oxeCtis.Count, string.Join(", ", oxeCtis.ToArray()), ent, pg));
                lines.Add("# ===================================================================");
                lines.Add(string.Format("cat << 'EOF_OXE' > \"{0}\"", cfgFile));

                for (int i = 0; i < oxeDns.Count; i++)
                {
                    string dn = oxeDns[i];
                    lines.Add(string.Format("CREATE Profiled_Subscriber   \"{0}\": \"{1}\"", nodeNumber, dn));
                    lines.Add("{");
                    lines.Add(" Station_Type = \"NOE_C_IP\",");
                    lines.Add(string.Format(" Station_Profil_From = \"{0}\",", pExt));
                    lines.Add(string.Format(" Keys_Profil_From = \"{0}\"", pExt));
                    lines.Add("}");
                }

                for (int i = 0; i < oxeCtis.Count; i++)
                {
                    string id = oxeCtis[i];
                    lines.Add(string.Format("CREATE Profiled_Subscriber   \"{0}\": \"{1}\"", nodeNumber, id));
                    lines.Add("{");
                    lines.Add(" Station_Type = \"NOE_C_Color_IP\",");
                    lines.Add(string.Format(" Entity_Number = \"{0}\",", ent));
                    lines.Add(string.Format(" Station_Profil_From = \"{0}\",", pLog));
                    lines.Add(string.Format(" Keys_Profil_From = \"{0}\"", pLog));
                    lines.Add("}");
                    lines.Add(string.Format("CREATE ACD2_Attaching_List   \"{0}\": \"1\": \"1\": \"1\": \"{1}\":", nodeNumber, id));
                    lines.Add(string.Format(" \"{0}\"", pg));
                    lines.Add(string.Format("SET ACD2_Operator_data   \"{0}\": \"1\": \"1\": \"1\": \"{1}\"", nodeNumber, id));
                    lines.Add("{");
                    lines.Add(" Agent_Facility = {\"TRUE\",\"FALSE\"}");
                    lines.Add("}");
                }

                lines.Add("EOF_OXE");
                lines.Add(string.Format("if mgr -nodico -x \"{0}\"; then", cfgFile));
                lines.Add(string.Format("    mgr -nodico -X \"{0}\" && echo \"[OK] OXE Create completed.\"", cfgFile));
                lines.Add("else");
                lines.Add("    echo \"[ABORT] mgr -nodico -x syntax check failed.\"");
                lines.Add("fi");
                lines.Add(string.Format("rm -f \"{0}\"", cfgFile));
            }
            else
            {
                lines.Add("# [Alcatel OXE R12.4 Delete]");
                lines.Add(string.Format("# Query attached PGs -> Delete PG -> Delete Login({0}) -> Delete Ext({1})", oxeCtis.Count, oxeDns.Count));
                lines.Add("# ===================================================================");
                lines.Add(string.Format(": > \"{0}\"", cfgFile));

                if (oxeCtis.Count > 0)
                {
                    string ctiJoined = string.Join(" ", oxeCtis.ToArray());
                    lines.Add(string.Format("for id in {0}; do", ctiJoined));
                    lines.Add(string.Format("    pgs=$(printf 'ACD2_Attaching_List\\n4\\n{0}\\n1\\n1\\n1\\n%s\\n1\\n1\\n$\\n$\\n' \"$id\" | mgr -nodico -line 2>/dev/null | awk -F':' '/Processing_Group_MCDU/{{gsub(/[^0-9]/,\"\",$2); if($2!=\"\") print $2}}')", nodeNumber));
                    lines.Add("    for pg in $pgs; do");
                    lines.Add(string.Format("        printf 'DELETE ACD2_Attaching_List   \"%s\": \"1\": \"1\": \"1\": \"%s\":\\n \"%s\"\\n' \"{0}\" \"$id\" \"$pg\" >> \"{1}\"", nodeNumber, cfgFile));
                    lines.Add("    done");
                    lines.Add(string.Format("    printf 'DELETE Subscriber   \"%s\": \"%s\"\\n' \"{0}\" \"$id\" >> \"{1}\"", nodeNumber, cfgFile));
                    lines.Add("done");
                }

                if (oxeDns.Count > 0)
                {
                    string dnJoined = string.Join(" ", oxeDns.ToArray());
                    lines.Add(string.Format("for dn in {0}; do", dnJoined));
                    lines.Add(string.Format("    printf 'DELETE Subscriber   \"%s\": \"%s\"\\n' \"{0}\" \"$dn\" >> \"{1}\"", nodeNumber, cfgFile));
                    lines.Add("done");
                }

                lines.Add(string.Format("if [ -s \"{0}\" ]; then", cfgFile));
                lines.Add(string.Format("    if mgr -nodico -x \"{0}\"; then", cfgFile));
                lines.Add(string.Format("        mgr -nodico -X \"{0}\" && echo \"[OK] OXE Delete completed.\"", cfgFile));
                lines.Add("    else");
                lines.Add("        echo \"[ABORT] mgr -nodico -x syntax check failed.\"");
                lines.Add("    fi");
                lines.Add("fi");
                lines.Add(string.Format("rm -f \"{0}\"", cfgFile));
            }

            string script = string.Join("\n", lines.ToArray()) + "\n";
            ValidateGeneratedSyntax(script, mode, result.Errors);

            result.Ok = (result.Errors.Count == 0);
            result.Script = result.Ok ? script : "";
            result.DnList = oxeDns;
            result.CtiList = oxeCtis;
            result.LineCount = lines.Count;
            return result;
        }

        private static void ValidateGeneratedSyntax(string script, string mode, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                errors.Add("생성된 스크립트가 비어 있습니다.");
                return;
            }
            if (!script.Contains("mgr -nodico -x") || !script.Contains("mgr -nodico -X"))
            {
                errors.Add("OXE 안전 실행 래퍼(mgr -nodico -x / -X) 구문 누락");
            }
            if (mode == "create")
            {
                if (!script.Contains("EOF_OXE"))
                {
                    errors.Add("heredoc 구분자(EOF_OXE) 누락");
                }
                int opens = 0;
                int closes = 0;
                for (int i = 0; i < script.Length; i++)
                {
                    if (script[i] == '{') opens++;
                    else if (script[i] == '}') closes++;
                }
                if (opens != closes)
                {
                    errors.Add(string.Format("중괄호 짝 불일치 ({{={0}, }}={1})", opens, closes));
                }
            }
            for (int i = 0; i < script.Length; i++)
            {
                if (script[i] > 127)
                {
                    errors.Add("스크립트 내에 비ASCII 문자가 포함되어 있습니다.");
                    break;
                }
            }
        }

        public static string BuildPbxDuplicateCheckCommand(List<string> dnList, List<string> ctiList, int nodeNumber, string token)
        {
            if (nodeNumber <= 0) nodeNumber = 1;
            string dns = (dnList != null && dnList.Count > 0) ? string.Join(" ", dnList.ToArray()) : "";
            string ctis = (ctiList != null && ctiList.Count > 0) ? string.Join(" ", ctiList.ToArray()) : "";

            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("_nd={0}; _dns=\"{1}\"; _ctis=\"{2}\"; ", nodeNumber, dns, ctis);
            sb.Append("_sub=$({ printf 'Subscriber\\n4\\n%s\\n1\\n2\\n' \"$_nd\"; for x in $_dns $_ctis; do printf '%s\\n' \"$x\"; done; printf '$\\n$\\n$\\n$\\n'; } ");
            sb.Append("| mgr -nodico -line 2>/dev/null ");
            sb.Append("| awk '/GOT_NO_ANSWER|Displaying Object/{c=\"\"}/^[[:space:]]*Directory_Number[[:space:]]*:/{c=$NF;gsub(/[^0-9]/,\"\",c);if(c!=\"\")ex[c]=1}/^[[:space:]]*Entity_Number[[:space:]]*:/{if(c!=\"\"){v=$NF;gsub(/[^0-9]/,\"\",v);ent[c]=v}}END{for(k in ex)printf \"%s=%s \",k,ent[k]}'); ");
            sb.Append("_cnt=0; ");
            sb.Append("for x in $_dns; do _e=\"\"; _f=0; for p in $_sub; do case \"$p\" in \"$x=\"*) _e=\"${p#*=}\"; _f=1; break ;; esac; done; ");
            sb.Append("printf '@@P%s|E|%s|%s|%s|@@\\n' \"X\" \"$x\" \"$_f\" \"$_e\"; ");
            sb.Append("_cnt=$((_cnt+1)); [ $((_cnt%12)) -eq 0 ] && sleep 0.15 2>/dev/null; done; ");
            sb.Append("for x in $_ctis; do _e=\"\"; _f=0; _pg=\"\"; for p in $_sub; do case \"$p\" in \"$x=\"*) _e=\"${p#*=}\"; _f=1; break ;; esac; done; ");
            sb.Append("if [ \"$_f\" = \"1\" ]; then _pg=$(printf 'ACD2_Attaching_List\\n4\\n%s\\n1\\n1\\n1\\n%s\\n1\\n1\\n$\\n$\\n' \"$_nd\" \"$x\" | mgr -nodico -line 2>/dev/null | awk -F':' 'BEGIN{m=0}/Processing_Group_MCDU/{gsub(/[^0-9]/,\"\",$2);if($2!=\"\"){printf \"%s%s\",(m?\",\":\"\"),$2;m++}}'); fi; ");
            sb.Append("printf '@@P%s|C|%s|%s|%s|%s@@\\n' \"X\" \"$x\" \"$_f\" \"$_e\" \"$_pg\"; ");
            sb.Append("_cnt=$((_cnt+1)); [ $((_cnt%12)) -eq 0 ] && sleep 0.15 2>/dev/null; done; ");
            sb.AppendFormat("printf '@@PX_E%s|%s@@\\n' \"ND\" \"{0}\"\n", token);
            return sb.ToString();
        }

        public static string ResolveCenterName(List<CustomerInfo> customers, string entityNo, List<string> pgs)
        {
            if (customers == null || customers.Count == 0) return "unknown";
            string ent = (entityNo ?? "").Trim();
            HashSet<string> pgSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pgs != null)
            {
                for (int i = 0; i < pgs.Count; i++)
                {
                    string pg = (pgs[i] ?? "").Trim();
                    if (pg.Length > 0) pgSet.Add(pg);
                }
            }

            List<string> tierBoth = new List<string>();
            List<string> tierPg = new List<string>();
            List<string> tierEnt = new List<string>();

            for (int i = 0; i < customers.Count; i++)
            {
                CustomerInfo c = customers[i];
                if (c == null || c.oxePresets == null || string.IsNullOrEmpty(c.name)) continue;
                for (int j = 0; j < c.oxePresets.Count; j++)
                {
                    OxePreset p = c.oxePresets[j];
                    if (p == null) continue;
                    string pEnt = (p.entityNo ?? "").Trim();
                    string pPg = (p.agentPg ?? "").Trim();

                    bool pgMatch = pPg.Length > 0 && pgSet.Contains(pPg);
                    bool entMatch = ent.Length > 0 && ent != "0" && pEnt == ent;
                    if (ent == "1" && !pgSet.Contains("60991")) entMatch = false;

                    if (pgMatch && entMatch && !tierBoth.Contains(c.name)) tierBoth.Add(c.name);
                    else if (pgMatch && !tierPg.Contains(c.name)) tierPg.Add(c.name);
                    else if (entMatch && !tierEnt.Contains(c.name)) tierEnt.Add(c.name);
                }
            }

            List<string> chosen = tierBoth.Count > 0 ? tierBoth : (tierPg.Count > 0 ? tierPg : tierEnt);
            return chosen.Count > 0 ? string.Join(", ", chosen.ToArray()) : "unknown";
        }
    }
}
