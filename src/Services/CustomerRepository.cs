using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using CmOxeProvisioner.Models;

namespace CmOxeProvisioner.Services
{
    public class CustomerRepository
    {
        public List<CustomerInfo> LoadAll(string jsonPath)
        {
            if (!File.Exists(jsonPath))
            {
                throw new FileNotFoundException("고객사 매핑 파일을 찾을 수 없습니다.", jsonPath);
            }
            string raw = File.ReadAllText(jsonPath, Encoding.UTF8);
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 10485760;
            List<CustomerInfo> list = ser.Deserialize<List<CustomerInfo>>(raw);
            if (list == null) return new List<CustomerInfo>();
            return list;
        }

        public void SaveAll(string jsonPath, IList<CustomerInfo> customers)
        {
            if (string.IsNullOrWhiteSpace(jsonPath)) throw new ArgumentNullException("jsonPath");
            List<Dictionary<string, object>> payload = new List<Dictionary<string, object>>();
            if (customers != null)
            {
                for (int i = 0; i < customers.Count; i++)
                {
                    CustomerInfo c = customers[i];
                    if (c == null) continue;
                    payload.Add(ToSerializableCustomer(c));
                }
            }

            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 16 * 1024 * 1024;
            string compact = ser.Serialize(payload);
            string pretty = FormatPrettyJson(compact);

            string dir = Path.GetDirectoryName(jsonPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string tmpPath = jsonPath + ".tmp";
            File.WriteAllText(tmpPath, pretty, new UTF8Encoding(false));
            if (File.Exists(jsonPath))
            {
                File.Replace(tmpPath, jsonPath, null);
            }
            else
            {
                File.Move(tmpPath, jsonPath);
            }
        }

        private static Dictionary<string, object> ToSerializableCustomer(CustomerInfo c)
        {
            var dict = new Dictionary<string, object>();
            dict["name"] = c.name ?? "";
            dict["floor"] = c.floor ?? "";
            dict["agentLogin"] = c.agentLogin;
            dict["acdPosition"] = c.acdPosition;
            dict["agentDn"] = c.agentDn;
            dict["place"] = c.place;
            if (c.rsi != null && c.rsi.Length > 0) dict["rsi"] = c.rsi;
            if (c.useVQueue.HasValue) dict["useVQueue"] = c.useVQueue.Value;
            if (c.useIvr.HasValue) dict["useIvr"] = c.useIvr.Value;
            dict["note"] = c.note ?? "";

            var presets = new List<Dictionary<string, object>>();
            if (c.oxePresets != null)
            {
                for (int i = 0; i < c.oxePresets.Count; i++)
                {
                    OxePreset p = c.oxePresets[i];
                    if (p == null) continue;
                    var pd = new Dictionary<string, object>();
                    pd["label"] = p.label ?? "";
                    pd["entityNo"] = p.entityNo ?? "";
                    pd["entityName"] = p.entityName ?? "";
                    pd["agentPg"] = p.agentPg ?? "";
                    pd["sampleExt"] = p.sampleExt ?? "";
                    pd["sampleLoginId"] = p.sampleLoginId ?? "";
                    pd["source"] = p.source ?? "";
                    if (!string.IsNullOrWhiteSpace(p.dnRange)) pd["dnRange"] = p.dnRange;
                    if (!string.IsNullOrWhiteSpace(p.ctiRange)) pd["ctiRange"] = p.ctiRange;
                    if (!string.IsNullOrWhiteSpace(p.note)) pd["note"] = p.note;
                    presets.Add(pd);
                }
            }
            dict["oxePresets"] = presets;
            return dict;
        }

        public static string FormatPrettyJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return "[]";
            StringBuilder sb = new StringBuilder(json.Length * 2);
            int indent = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < json.Length; i++)
            {
                char ch = json[i];
                if (inString)
                {
                    sb.Append(ch);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (char.IsWhiteSpace(ch)) continue;

                switch (ch)
                {
                    case '"':
                        inString = true;
                        sb.Append(ch);
                        break;
                    case '{':
                    case '[':
                        sb.Append(ch);
                        char nextNonWs = PeekNextNonWhitespace(json, i + 1);
                        if ((ch == '{' && nextNonWs == '}') || (ch == '[' && nextNonWs == ']'))
                        {
                            sb.Append(nextNonWs);
                            i = IndexOfNextNonWhitespace(json, i + 1);
                        }
                        else
                        {
                            indent++;
                            AppendIndent(sb, indent);
                        }
                        break;
                    case '}':
                    case ']':
                        indent = Math.Max(0, indent - 1);
                        AppendIndent(sb, indent);
                        sb.Append(ch);
                        break;
                    case ',':
                        sb.Append(ch);
                        AppendIndent(sb, indent);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        private static char PeekNextNonWhitespace(string s, int start)
        {
            for (int i = start; i < s.Length; i++)
            {
                if (!char.IsWhiteSpace(s[i])) return s[i];
            }
            return '\0';
        }

        private static int IndexOfNextNonWhitespace(string s, int start)
        {
            for (int i = start; i < s.Length; i++)
            {
                if (!char.IsWhiteSpace(s[i])) return i;
            }
            return start;
        }

        private static void AppendIndent(StringBuilder sb, int level)
        {
            sb.Append("\r\n");
            for (int i = 0; i < level; i++) sb.Append("    ");
        }
    }
}
