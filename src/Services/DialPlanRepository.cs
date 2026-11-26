using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using CmOxeProvisioner.Models;

namespace CmOxeProvisioner.Services
{
    public class EmptyNumberReport
    {
        public List<string> FreeEntityNumbers { get; set; }
        public List<string> FreeCenter6xxPrefixes { get; set; }
        public List<string> FreeIvrDns { get; set; }
        public Dictionary<string, List<string>> FreeDvgByBand { get; set; }

        public EmptyNumberReport()
        {
            FreeEntityNumbers = new List<string>();
            FreeCenter6xxPrefixes = new List<string>();
            FreeIvrDns = new List<string>();
            FreeDvgByBand = new Dictionary<string, List<string>>();
        }
    }

    public class DialPlanBackupEntry
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public DateTime SavedAt { get; set; }
        public long SizeBytes { get; set; }
        public string DisplayLabel
        {
            get
            {
                double kb = SizeBytes / 1024.0;
                return string.Format("{0:yyyy-MM-dd HH:mm:ss}  ({1:0.0} KB)  -  {2}", SavedAt, kb, FileName);
            }
        }
    }

    /// <summary>
    /// Persistence, preset synchronization, and free-resource finder for dialplan_db.json.
    /// </summary>
    public static class DialPlanRepository
    {
        private const int MaxAutoBackups = 20;

        public static string SerializeSnapshot(DialPlanDatabase db)
        {
            if (db == null) db = new DialPlanDatabase();
            db.EnsureLists();
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string rawJson = ser.Serialize(db);
            object tree = ser.DeserializeObject(rawJson);
            var sb = new StringBuilder(rawJson.Length);
            AppendCanonicalJsonValue(sb, tree, ser);
            return sb.ToString();
        }

        private static void AppendCanonicalJsonValue(StringBuilder sb, object node, JavaScriptSerializer ser)
        {
            if (node == null)
            {
                sb.Append("null");
                return;
            }

            var dict = node as IDictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                var keys = new List<string>(dict.Keys);
                keys.Sort(StringComparer.Ordinal);
                for (int i = 0; i < keys.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    string key = keys[i];
                    sb.Append(ser.Serialize(key));
                    sb.Append(':');
                    AppendCanonicalJsonValue(sb, dict[key], ser);
                }
                sb.Append('}');
                return;
            }

            if (node is string)
            {
                sb.Append(ser.Serialize(node));
                return;
            }

            var list = node as System.Collections.IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    AppendCanonicalJsonValue(sb, item, ser);
                }
                sb.Append(']');
                return;
            }

            sb.Append(ser.Serialize(node));
        }

        public static DialPlanDatabase DeserializeSnapshot(string snapshotJson)
        {
            if (string.IsNullOrWhiteSpace(snapshotJson)) return new DialPlanDatabase();
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var db = ser.Deserialize<DialPlanDatabase>(snapshotJson) ?? new DialPlanDatabase();
            db.EnsureLists();
            foreach (var c in db.centers)
            {
                if (string.IsNullOrWhiteSpace(c.id))
                    c.id = DialPlanWorkbookMapper.BuildCenterId(c.project, db.centers);
            }
            return db;
        }

        public static List<DialPlanBackupEntry> ListBackups(string dbPath)
        {
            var result = new List<DialPlanBackupEntry>();
            if (string.IsNullOrWhiteSpace(dbPath)) return result;
            string dir = Path.GetDirectoryName(dbPath);
            if (string.IsNullOrWhiteSpace(dir)) return result;
            string backupDir = Path.Combine(dir, "backups");
            if (!Directory.Exists(backupDir)) return result;

            foreach (string fp in Directory.GetFiles(backupDir, "dialplan_db_*.json"))
            {
                try
                {
                    var fi = new FileInfo(fp);
                    result.Add(new DialPlanBackupEntry
                    {
                        FilePath = fp,
                        FileName = fi.Name,
                        SavedAt = fi.LastWriteTime,
                        SizeBytes = fi.Length
                    });
                }
                catch
                {
                }
            }
            return result.OrderByDescending(x => x.SavedAt).ToList();
        }

        private static void CreateAutoBackupIfPrimaryDb(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath)) return;
            string fileName = Path.GetFileName(dbPath) ?? string.Empty;
            if (!string.Equals(fileName, "dialplan_db.json", StringComparison.OrdinalIgnoreCase))
                return;

            string dir = Path.GetDirectoryName(dbPath);
            if (string.IsNullOrWhiteSpace(dir)) return;

            try
            {
                string backupDir = Path.Combine(dir, "backups");
                if (!Directory.Exists(backupDir))
                    Directory.CreateDirectory(backupDir);

                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string dest = Path.Combine(backupDir, "dialplan_db_" + stamp + ".json");
                int seq = 1;
                while (File.Exists(dest))
                {
                    dest = Path.Combine(backupDir, "dialplan_db_" + stamp + "_" + seq + ".json");
                    seq++;
                }
                File.Copy(dbPath, dest, false);

                var files = Directory.GetFiles(backupDir, "dialplan_db_*.json")
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .ToList();
                for (int i = MaxAutoBackups; i < files.Count; i++)
                {
                    try { files[i].Delete(); } catch { }
                }
            }
            catch
            {
                // Backup rotation failure should not block primary save
            }
        }

        public static DialPlanDatabase Load(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
                return new DialPlanDatabase();

            string json = File.ReadAllText(dbPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
                return new DialPlanDatabase();

            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var db = ser.Deserialize<DialPlanDatabase>(json) ?? new DialPlanDatabase();
            db.EnsureLists();
            foreach (var c in db.centers)
            {
                if (string.IsNullOrWhiteSpace(c.id))
                    c.id = DialPlanWorkbookMapper.BuildCenterId(c.project, db.centers);
            }
            return db;
        }

        public static void Save(string dbPath, DialPlanDatabase db)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentNullException("dbPath");
            if (db == null)
                db = new DialPlanDatabase();

            db.EnsureLists();
            db.updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            string dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            CreateAutoBackupIfPrimaryDb(dbPath);

            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string rawJson = ser.Serialize(db);
            string prettyJson = CustomerRepository.FormatPrettyJson(rawJson);

            string tempPath = dbPath + ".tmp";
            File.WriteAllText(tempPath, prettyJson, new UTF8Encoding(false));
            if (File.Exists(dbPath))
                File.Delete(dbPath);
            File.Move(tempPath, dbPath);
        }

        /// <summary>
        /// Synchronizes presets and CM flags from a DialPlanCenter into matching cm_customers.json entry.
        /// Returns true if any CustomerInfo entry was modified.
        /// </summary>
        public static bool SyncCenterToCustomers(
            DialPlanCenter center,
            IList<CustomerInfo> customers,
            bool overwritePresetsIfNonEmpty = true)
        {
            if (center == null || customers == null) return false;
            string linkName = !string.IsNullOrWhiteSpace(center.cmCustomerName)
                ? center.cmCustomerName.Trim()
                : (center.project ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(linkName)) return false;

            var cust = customers.FirstOrDefault(c =>
                c != null && string.Equals(c.name, linkName, StringComparison.OrdinalIgnoreCase));
            if (cust == null) return false;

            bool changed = false;
            if (string.IsNullOrWhiteSpace(cust.floor) && !string.IsNullOrWhiteSpace(center.location))
            {
                cust.floor = center.location.Trim();
                changed = true;
            }
            if (cust.useVQueue != center.useVQueue)
            {
                cust.useVQueue = center.useVQueue;
                changed = true;
            }
            if (cust.useIvr != center.useIvr)
            {
                cust.useIvr = center.useIvr;
                changed = true;
            }

            if (overwritePresetsIfNonEmpty)
            {
                var built = BuildPresetsFromCenter(center);
                if (built.Count > 0)
                {
                    cust.oxePresets = built;
                    changed = true;
                }
            }

            return changed;
        }

        public static List<OxePreset> BuildPresetsFromCenter(DialPlanCenter center)
        {
            var result = new List<OxePreset>();
            if (center == null || center.lines == null) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int idx = 1;
            foreach (var ln in center.lines)
            {
                if (ln == null) continue;
                string entRaw = (ln.entityNo ?? string.Empty).Trim();
                string pgRaw = (ln.agentPg ?? string.Empty).Trim();
                string extRaw = (ln.phoneExt ?? string.Empty).Trim();
                string loginRaw = (ln.loginId ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(entRaw) &&
                    string.IsNullOrWhiteSpace(pgRaw) &&
                    string.IsNullOrWhiteSpace(extRaw) &&
                    string.IsNullOrWhiteSpace(loginRaw))
                {
                    continue;
                }

                string entNo, entName;
                SplitEntityNoAndName(entRaw, out entNo, out entName);

                string dedupeKey = entNo + "|" + pgRaw + "|" + extRaw + "|" + loginRaw;
                if (seen.Contains(dedupeKey))
                    continue;
                seen.Add(dedupeKey);

                string subLabel = !string.IsNullOrWhiteSpace(ln.subTask)
                    ? ln.subTask.Trim()
                    : (!string.IsNullOrWhiteSpace(ln.mainNumber) ? ln.mainNumber.Trim() : (center.project + " #" + idx));
                string suffix = "Ent " + (string.IsNullOrWhiteSpace(entNo) ? "-" : entNo) +
                                " / PG " + (string.IsNullOrWhiteSpace(pgRaw) ? "-" : pgRaw);

                result.Add(new OxePreset
                {
                    label = subLabel + " (" + suffix + ")",
                    entityNo = entNo,
                    entityName = entName,
                    agentPg = ExtractFirstDigits(pgRaw),
                    sampleExt = ExtractSampleExtension(extRaw),
                    sampleLoginId = ExtractSampleExtension(loginRaw),
                    source = "Dial-Plan",
                    dnRange = extRaw,
                    ctiRange = loginRaw,
                    note = ln.note ?? string.Empty
                });
                idx++;
            }

            return result;
        }

        public static DialPlanCenter UpsertCenterFromCreatePopup(
            DialPlanDatabase db,
            CustomerInfo cust,
            bool alsoCreateCmCenter)
        {
            if (db == null || cust == null || string.IsNullOrWhiteSpace(cust.name))
                return null;
            db.EnsureLists();

            var existing = db.centers.FirstOrDefault(c =>
                string.Equals(c.cmCustomerName, cust.name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.project, cust.name, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                existing = new DialPlanCenter
                {
                    id = DialPlanWorkbookMapper.BuildCenterId(cust.name, db.centers),
                    status = DialPlanStatus.Active,
                    location = cust.floor ?? string.Empty,
                    project = cust.name,
                    cmCustomerName = alsoCreateCmCenter ? cust.name : string.Empty,
                    useVQueue = cust.useVQueue == true,
                    useIvr = cust.useIvr == true,
                    sourceSheet = "CenterAdd",
                    notes = cust.note ?? string.Empty
                };
                db.centers.Add(existing);
            }
            else
            {
                existing.status = DialPlanStatus.Active;
                if (!string.IsNullOrWhiteSpace(cust.floor))
                    existing.location = cust.floor;
                if (alsoCreateCmCenter)
                    existing.cmCustomerName = cust.name;
                existing.useVQueue = cust.useVQueue == true;
                existing.useIvr = cust.useIvr == true;
            }

            if (existing.lines.Count == 0 && cust.oxePresets != null && cust.oxePresets.Count > 0)
            {
                foreach (var p in cust.oxePresets)
                {
                    if (p == null) continue;
                    existing.lines.Add(new DialPlanLine
                    {
                        subTask = p.label ?? string.Empty,
                        entityNo = string.IsNullOrWhiteSpace(p.entityName)
                            ? (p.entityNo ?? string.Empty)
                            : ((p.entityNo ?? "") + " " + p.entityName).Trim(),
                        agentPg = p.agentPg ?? string.Empty,
                        phoneExt = !string.IsNullOrWhiteSpace(p.dnRange) ? p.dnRange : (p.sampleExt ?? string.Empty),
                        loginId = !string.IsNullOrWhiteSpace(p.ctiRange) ? p.ctiRange : (p.sampleLoginId ?? string.Empty),
                        note = p.note ?? string.Empty
                    });
                }
            }

            return existing;
        }

        public static EmptyNumberReport FindEmptyNumbers(DialPlanDatabase db)
        {
            var report = new EmptyNumberReport();
            if (db == null) return report;
            db.EnsureLists();

            // 1. Unused Entity numbers (10..150)
            var usedEntities = new HashSet<int>();
            foreach (var e in db.entities)
            {
                int n;
                if (int.TryParse(DialPlanWorkbookMapper.ExtractLeadingIntToken(e.entityNo), out n))
                    usedEntities.Add(n);
            }
            foreach (var c in db.centers)
            {
                if (c.status == DialPlanStatus.Closed) continue;
                foreach (var l in c.lines)
                {
                    int n;
                    if (int.TryParse(DialPlanWorkbookMapper.ExtractLeadingIntToken(l.entityNo), out n))
                        usedEntities.Add(n);
                }
            }
            for (int i = 10; i <= 150; i++)
            {
                if (!usedEntities.Contains(i))
                    report.FreeEntityNumbers.Add(i.ToString(CultureInfo.InvariantCulture));
            }

            // 2. Unused 600..699 RSI/PG center prefixes (e.g. 601, 602...)
            var usedPrefixes = new HashSet<int>();
            foreach (var c in db.centers)
            {
                if (c.status == DialPlanStatus.Closed) continue;
                foreach (var l in c.lines)
                {
                    Collect6xxPrefixes(l.inboundRsi, usedPrefixes);
                    Collect6xxPrefixes(l.agentTransferRsi, usedPrefixes);
                    Collect6xxPrefixes(l.agentPilot, usedPrefixes);
                    Collect6xxPrefixes(l.agentQueue, usedPrefixes);
                    Collect6xxPrefixes(l.agentPg, usedPrefixes);
                }
            }
            for (int p = 600; p <= 699; p++)
            {
                if (!usedPrefixes.Contains(p))
                    report.FreeCenter6xxPrefixes.Add(p + "xx");
            }

            // 3. Unused IP IVR DNs in 21001..23100
            var usedIvrDns = new HashSet<int>();
            foreach (var ch in db.ipIvrChannels)
                ExpandIntRanges(ch.ivrDn, usedIvrDns);
            foreach (var c in db.centers)
            {
                if (c.status == DialPlanStatus.Closed) continue;
                foreach (var l in c.lines)
                {
                    ExpandIntRanges(l.ivrPdsChannel, usedIvrDns);
                    ExpandIntRanges(l.testIvrDn, usedIvrDns);
                }
            }
            var free21k = FindGapRanges(usedIvrDns, 21001, 21120);
            var free22k = FindGapRanges(usedIvrDns, 22001, 22120);
            var free23k = FindGapRanges(usedIvrDns, 23001, 23100);
            report.FreeIvrDns.AddRange(free21k);
            report.FreeIvrDns.AddRange(free22k);
            report.FreeIvrDns.AddRange(free23k);

            // 4. Unused D.V.G prompt slots by band
            var bands = new[]
            {
                Tuple.Create("700~799", 700, 799),
                Tuple.Create("800~899", 800, 899),
                Tuple.Create("900~999", 900, 999),
                Tuple.Create("1800~1899", 1800, 1899)
            };
            var usedDvg = new HashSet<int>();
            foreach (var d in db.dvgPrompts)
            {
                if (string.IsNullOrWhiteSpace(d.dvgNo)) continue;
                string txt = ((d.category ?? "") + " " + (d.subCategory ?? "") + " " + (d.content ?? "")).Trim();
                bool hasOwner = !string.IsNullOrWhiteSpace(txt) &&
                                txt.IndexOf("미사용", StringComparison.OrdinalIgnoreCase) < 0 &&
                                txt.IndexOf("사용안함", StringComparison.OrdinalIgnoreCase) < 0;
                if (!hasOwner) continue;
                int dvgNum;
                if (int.TryParse(DialPlanWorkbookMapper.ExtractLeadingIntToken(d.dvgNo), out dvgNum))
                    usedDvg.Add(dvgNum);
            }
            foreach (var b in bands)
            {
                var free = new List<string>();
                for (int n = b.Item2; n <= b.Item3; n++)
                {
                    if (!usedDvg.Contains(n))
                        free.Add(n.ToString(CultureInfo.InvariantCulture));
                }
                report.FreeDvgByBand[b.Item1] = free;
            }

            return report;
        }

        private static void SplitEntityNoAndName(string raw, out string entityNo, out string entityName)
        {
            entityNo = string.Empty;
            entityName = string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return;

            var m = Regex.Match(raw.Trim(), @"(\d+)");
            if (m.Success)
            {
                entityNo = m.Value;
                entityName = raw.Trim().Substring(m.Index + m.Length).Trim(' ', '-', ')', '(', '/');
            }
            else
            {
                entityName = raw.Trim();
            }
        }

        private static string ExtractFirstDigits(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var m = Regex.Match(raw, @"\d+");
            return m.Success ? m.Value : string.Empty;
        }

        private static string ExtractSampleExtension(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var m = Regex.Match(raw, @"\d{4,5}");
            return m.Success ? m.Value : string.Empty;
        }

        private static void Collect6xxPrefixes(string text, HashSet<int> used)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (Match m in Regex.Matches(text, @"\b(6\d{2})\d{2}\b"))
            {
                int p;
                if (int.TryParse(m.Groups[1].Value, out p))
                    used.Add(p);
            }
        }

        private static void ExpandIntRanges(string text, HashSet<int> target)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (Match m in Regex.Matches(text, @"(\d{5})\s*[~\-]\s*(\d{5})"))
            {
                int a, b;
                if (int.TryParse(m.Groups[1].Value, out a) && int.TryParse(m.Groups[2].Value, out b))
                {
                    if (b >= a && (b - a) <= 500)
                    {
                        for (int n = a; n <= b; n++) target.Add(n);
                    }
                }
            }
            foreach (Match m in Regex.Matches(text, @"\b(\d{5})\b"))
            {
                int n;
                if (int.TryParse(m.Groups[1].Value, out n))
                    target.Add(n);
            }
        }

        private static List<string> FindGapRanges(HashSet<int> used, int min, int max)
        {
            var gaps = new List<string>();
            int? start = null;
            for (int n = min; n <= max; n++)
            {
                bool isUsed = used.Contains(n);
                if (!isUsed && !start.HasValue)
                    start = n;
                else if (isUsed && start.HasValue)
                {
                    gaps.Add(FormatRange(start.Value, n - 1));
                    start = null;
                }
            }
            if (start.HasValue)
                gaps.Add(FormatRange(start.Value, max));
            return gaps;
        }

        private static string FormatRange(int a, int b)
        {
            return a == b ? a.ToString(CultureInfo.InvariantCulture) : (a + "~" + b);
        }
    }
}
