using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CmOxeProvisioner.Services
{
    public class ExcelMergeRange
    {
        public int StartRow { get; set; }
        public int StartCol { get; set; }
        public int EndRow { get; set; }
        public int EndCol { get; set; }

        public bool Contains(int row, int col)
        {
            return row >= StartRow && row <= EndRow && col >= StartCol && col <= EndCol;
        }
    }

    public class ExcelSheetData
    {
        public string Name { get; set; }
        public int RowCount { get; set; }
        public int ColCount { get; set; }
        public Dictionary<int, Dictionary<int, string>> RawCells { get; private set; }
        public List<ExcelMergeRange> MergedRanges { get; private set; }
        private readonly Dictionary<long, ExcelMergeRange> _mergeLookup;

        public ExcelSheetData(string name)
        {
            Name = name ?? string.Empty;
            RawCells = new Dictionary<int, Dictionary<int, string>>();
            MergedRanges = new List<ExcelMergeRange>();
            _mergeLookup = new Dictionary<long, ExcelMergeRange>();
        }

        public void SetRawCell(int row, int col, string value)
        {
            if (row < 0 || col < 0) return;
            Dictionary<int, string> rowDict;
            if (!RawCells.TryGetValue(row, out rowDict))
            {
                rowDict = new Dictionary<int, string>();
                RawCells[row] = rowDict;
            }
            rowDict[col] = value ?? string.Empty;
            if (row + 1 > RowCount) RowCount = row + 1;
            if (col + 1 > ColCount) ColCount = col + 1;
        }

        public void AddMergedRange(ExcelMergeRange range)
        {
            if (range == null) return;
            MergedRanges.Add(range);
            if (range.EndRow + 1 > RowCount) RowCount = range.EndRow + 1;
            if (range.EndCol + 1 > ColCount) ColCount = range.EndCol + 1;

            for (int r = range.StartRow; r <= range.EndRow; r++)
            {
                for (int c = range.StartCol; c <= range.EndCol; c++)
                {
                    long key = MakeKey(r, c);
                    if (!_mergeLookup.ContainsKey(key))
                        _mergeLookup[key] = range;
                }
            }
        }

        public string GetRawCell(int row, int col)
        {
            Dictionary<int, string> rowDict;
            string val;
            if (RawCells.TryGetValue(row, out rowDict) && rowDict.TryGetValue(col, out val))
                return val ?? string.Empty;
            return string.Empty;
        }

        /// <summary>
        /// Returns the cell value, resolving merged cells to their top-left origin cell
        /// if the coordinate lies inside an explicit OOXML mergeCell span.
        /// </summary>
        public string GetCell(int row, int col)
        {
            string raw = GetRawCell(row, col);
            if (!string.IsNullOrEmpty(raw))
                return raw;

            ExcelMergeRange mr;
            if (_mergeLookup.TryGetValue(MakeKey(row, col), out mr))
                return GetRawCell(mr.StartRow, mr.StartCol);

            return string.Empty;
        }

        public ExcelMergeRange GetMergeRangeAt(int row, int col)
        {
            ExcelMergeRange mr;
            if (_mergeLookup.TryGetValue(MakeKey(row, col), out mr))
                return mr;
            return null;
        }

        public bool IsRowCompletelyEmpty(int row, int maxCol = -1)
        {
            int limit = maxCol > 0 ? maxCol : ColCount;
            for (int c = 0; c < limit; c++)
            {
                if (!string.IsNullOrWhiteSpace(GetRawCell(row, c)))
                    return false;
            }
            return true;
        }

        private static long MakeKey(int row, int col)
        {
            return ((long)row << 32) | (uint)col;
        }
    }

    public class ExcelExportSheet
    {
        public string Name { get; set; }
        public string TitleBanner { get; set; }
        public List<string> Headers { get; set; }
        public List<List<string>> Rows { get; set; }

        public ExcelExportSheet(string name)
        {
            Name = name ?? "Sheet";
            Headers = new List<string>();
            Rows = new List<List<string>>();
        }
    }

    /// <summary>
    /// Zero-dependency pure-.NET Framework 4.8 OOXML (.xlsx) and CSV reader/writer.
    /// Uses System.IO.Compression and System.Xml.Linq directly.
    /// </summary>
    public static class ExcelWorkbookCodec
    {
        private static readonly XNamespace NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace NsPkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        public static List<ExcelSheetData> ReadXlsx(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("엑셀 파일을 찾을 수 없습니다.", filePath);

            var result = new List<ExcelSheetData>();
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var sharedStrings = ReadSharedStrings(zip);
                var dateStyleIndices = ReadDateStyleIndices(zip);
                var relTargets = ReadWorkbookRels(zip);

                var wbEntry = FindEntry(zip, "xl/workbook.xml");
                if (wbEntry == null)
                    throw new InvalidDataException("xl/workbook.xml 이 없는 유효하지 않은 XLSX 파일입니다.");

                XDocument wbDoc;
                using (var s = wbEntry.Open())
                    wbDoc = XDocument.Load(s);

                var sheetEls = wbDoc.Descendants(NsMain + "sheet").ToList();
                foreach (var sheetEl in sheetEls)
                {
                    string name = (string)sheetEl.Attribute("name") ?? "Sheet";
                    string rId = (string)sheetEl.Attribute(NsRel + "id") ?? string.Empty;
                    string target;
                    if (!relTargets.TryGetValue(rId, out target))
                        continue;

                    string zipPath = target.TrimStart('/');
                    if (!zipPath.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                        zipPath = "xl/" + zipPath.TrimStart('/');

                    var sheetEntry = FindEntry(zip, zipPath);
                    if (sheetEntry == null)
                        continue;

                    result.Add(ParseSheetXml(sheetEntry, name, sharedStrings, dateStyleIndices));
                }
            }
            return result;
        }

        public static void WriteXlsx(string filePath, IList<ExcelExportSheet> sheets)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException("filePath");
            if (sheets == null || sheets.Count == 0)
                throw new ArgumentException("내보낼 시트가 하나 이상 필요합니다.", "sheets");

            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sanitizedSheets = new List<ExcelExportSheet>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sh in sheets)
            {
                if (sh == null) continue;
                string safeName = SanitizeSheetName(sh.Name, usedNames);
                sanitizedSheets.Add(new ExcelExportSheet(safeName)
                {
                    TitleBanner = sh.TitleBanner,
                    Headers = sh.Headers ?? new List<string>(),
                    Rows = sh.Rows ?? new List<List<string>>()
                });
            }

            string tempPath = filePath + ".tmp";
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteZipText(zip, "[Content_Types].xml", BuildContentTypesXml(sanitizedSheets.Count));
                WriteZipText(zip, "_rels/.rels", BuildRootRelsXml());
                WriteZipText(zip, "xl/workbook.xml", BuildWorkbookXml(sanitizedSheets));
                WriteZipText(zip, "xl/_rels/workbook.xml.rels", BuildWorkbookRelsXml(sanitizedSheets.Count));
                WriteZipText(zip, "xl/styles.xml", BuildStylesXml());

                for (int i = 0; i < sanitizedSheets.Count; i++)
                {
                    string xml = BuildWorksheetXml(sanitizedSheets[i]);
                    WriteZipText(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", xml);
                }
            }

            if (File.Exists(filePath))
                File.Delete(filePath);
            File.Move(tempPath, filePath);
        }

        public static List<List<string>> ReadCsv(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("CSV 파일을 찾을 수 없습니다.", filePath);

            byte[] bytes = File.ReadAllBytes(filePath);
            string text = DecodeCsvBytesWithFallback(bytes);
            return ParseCsvText(text);
        }

        public static void WriteCsv(string filePath, IList<string> headers, IList<IList<string>> rows)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            if (headers != null && headers.Count > 0)
                sb.AppendLine(string.Join(",", headers.Select(EscapeCsvCell)));

            if (rows != null)
            {
                foreach (var row in rows)
                {
                    if (row == null) continue;
                    sb.AppendLine(string.Join(",", row.Select(EscapeCsvCell)));
                }
            }

            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
        }

        public static bool TryFormatExcelDateSerial(string rawText, out string formattedDate)
        {
            formattedDate = null;
            if (string.IsNullOrWhiteSpace(rawText)) return false;

            double serial;
            if (!double.TryParse(rawText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out serial))
                return false;

            // Typical modern dates: 1995-10-10 (35000) to 2064-04-06 (60000)
            if (serial < 35000 || serial > 65000)
                return false;

            try
            {
                DateTime dt = DateTime.FromOADate(serial);
                formattedDate = dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static ZipArchiveEntry FindEntry(ZipArchive zip, string path)
        {
            string normalized = (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
            return zip.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName.Replace('\\', '/').TrimStart('/'), normalized, StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var list = new List<string>();
            var entry = FindEntry(zip, "xl/sharedStrings.xml");
            if (entry == null) return list;

            using (var s = entry.Open())
            {
                var doc = XDocument.Load(s, LoadOptions.PreserveWhitespace);
                foreach (var si in doc.Descendants(NsMain + "si"))
                {
                    var sb = new StringBuilder();
                    foreach (var t in si.Descendants(NsMain + "t"))
                    {
                        bool inPhonetic = t.Ancestors(NsMain + "rPh").Any();
                        if (!inPhonetic)
                            sb.Append(t.Value);
                    }
                    list.Add(NormalizeLineEndings(sb.ToString()));
                }
            }
            return list;
        }

        private static HashSet<int> ReadDateStyleIndices(ZipArchive zip)
        {
            var dateXfs = new HashSet<int>();
            var entry = FindEntry(zip, "xl/styles.xml");
            if (entry == null) return dateXfs;

            try
            {
                using (var s = entry.Open())
                {
                    var doc = XDocument.Load(s);
                    var customDateNumFmts = new HashSet<int>();

                    foreach (var nf in doc.Descendants(NsMain + "numFmt"))
                    {
                        int id;
                        string code = ((string)nf.Attribute("formatCode") ?? string.Empty).ToLowerInvariant();
                        if (int.TryParse((string)nf.Attribute("numFmtId"), out id))
                        {
                            if (IsDateFormatCode(code))
                                customDateNumFmts.Add(id);
                        }
                    }

                    var cellXfs = doc.Descendants(NsMain + "cellXfs").FirstOrDefault();
                    if (cellXfs != null)
                    {
                        int idx = 0;
                        foreach (var xf in cellXfs.Elements(NsMain + "xf"))
                        {
                            int numFmtId;
                            if (int.TryParse((string)xf.Attribute("numFmtId"), out numFmtId))
                            {
                                if (IsBuiltInDateNumFmt(numFmtId) || customDateNumFmts.Contains(numFmtId))
                                    dateXfs.Add(idx);
                            }
                            idx++;
                        }
                    }
                }
            }
            catch
            {
                // Non-fatal: fallback column-level date conversion handles date fields if styles.xml is minimal
            }

            return dateXfs;
        }

        private static bool IsBuiltInDateNumFmt(int id)
        {
            return (id >= 14 && id <= 22) || (id >= 27 && id <= 36) || (id >= 45 && id <= 47) || (id >= 50 && id <= 58);
        }

        private static bool IsDateFormatCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            // Ignore color tokens like [Red]
            if (code.Contains("yy") || code.Contains("yyyy")) return true;
            if (code.Contains("mm") && code.Contains("dd")) return true;
            return false;
        }

        private static Dictionary<string, string> ReadWorkbookRels(ZipArchive zip)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var entry = FindEntry(zip, "xl/_rels/workbook.xml.rels");
            if (entry == null) return dict;

            using (var s = entry.Open())
            {
                var doc = XDocument.Load(s);
                foreach (var rel in doc.Descendants(NsPkgRel + "Relationship"))
                {
                    string id = (string)rel.Attribute("Id");
                    string target = (string)rel.Attribute("Target");
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(target))
                        dict[id] = target;
                }
            }
            return dict;
        }

        private static ExcelSheetData ParseSheetXml(
            ZipArchiveEntry entry,
            string name,
            List<string> sharedStrings,
            HashSet<int> dateStyleIndices)
        {
            var sheet = new ExcelSheetData(name);
            using (var s = entry.Open())
            {
                var doc = XDocument.Load(s, LoadOptions.PreserveWhitespace);

                foreach (var rowEl in doc.Descendants(NsMain + "row"))
                {
                    int rowIdx = -1;
                    int parsedRow;
                    if (int.TryParse((string)rowEl.Attribute("r"), out parsedRow) && parsedRow > 0)
                        rowIdx = parsedRow - 1;
                    else
                        rowIdx = sheet.RowCount;

                    int nextColIdx = 0;
                    foreach (var cEl in rowEl.Elements(NsMain + "c"))
                    {
                        int colIdx = nextColIdx;
                        string cellRef = (string)cEl.Attribute("r");
                        if (!string.IsNullOrWhiteSpace(cellRef))
                        {
                            int pr, pc;
                            if (TryParseCellRef(cellRef, out pr, out pc))
                            {
                                rowIdx = pr;
                                colIdx = pc;
                            }
                        }
                        nextColIdx = colIdx + 1;

                        string val = ExtractCellValue(cEl, sharedStrings, dateStyleIndices);
                        if (!string.IsNullOrEmpty(val))
                            sheet.SetRawCell(rowIdx, colIdx, val);
                    }
                }

                foreach (var mc in doc.Descendants(NsMain + "mergeCell"))
                {
                    string refStr = (string)mc.Attribute("ref");
                    ExcelMergeRange range;
                    if (TryParseMergeRange(refStr, out range))
                        sheet.AddMergedRange(range);
                }
            }
            return sheet;
        }

        private static string ExtractCellValue(XElement cEl, List<string> sharedStrings, HashSet<int> dateStyleIndices)
        {
            string type = ((string)cEl.Attribute("t") ?? string.Empty).Trim();

            if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase))
            {
                var vEl = cEl.Element(NsMain + "v");
                int idx;
                if (vEl != null && int.TryParse(vEl.Value, out idx) && idx >= 0 && idx < sharedStrings.Count)
                    return sharedStrings[idx];
                return string.Empty;
            }

            if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
            {
                var isEl = cEl.Element(NsMain + "is");
                if (isEl == null) return string.Empty;
                var sb = new StringBuilder();
                foreach (var t in isEl.Descendants(NsMain + "t"))
                {
                    if (!t.Ancestors(NsMain + "rPh").Any())
                        sb.Append(t.Value);
                }
                return NormalizeLineEndings(sb.ToString());
            }

            if (string.Equals(type, "str", StringComparison.OrdinalIgnoreCase))
            {
                var vEl = cEl.Element(NsMain + "v");
                return vEl != null ? NormalizeLineEndings(vEl.Value) : string.Empty;
            }

            if (string.Equals(type, "b", StringComparison.OrdinalIgnoreCase))
            {
                var vEl = cEl.Element(NsMain + "v");
                return (vEl != null && vEl.Value == "1") ? "TRUE" : "FALSE";
            }

            if (string.Equals(type, "e", StringComparison.OrdinalIgnoreCase))
            {
                var vEl = cEl.Element(NsMain + "v");
                return vEl != null ? vEl.Value : string.Empty;
            }

            var valEl = cEl.Element(NsMain + "v");
            if (valEl == null) return string.Empty;

            string raw = (valEl.Value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            int styleIdx;
            if (int.TryParse((string)cEl.Attribute("s"), out styleIdx) && dateStyleIndices.Contains(styleIdx))
            {
                string formattedDate;
                if (TryFormatExcelDateSerial(raw, out formattedDate))
                    return formattedDate;
            }

            return raw;
        }

        private static bool TryParseCellRef(string cellRef, out int rowIdx, out int colIdx)
        {
            rowIdx = 0;
            colIdx = 0;
            if (string.IsNullOrWhiteSpace(cellRef)) return false;

            int i = 0;
            int col = 0;
            while (i < cellRef.Length && char.IsLetter(cellRef[i]))
            {
                char ch = char.ToUpperInvariant(cellRef[i]);
                if (ch < 'A' || ch > 'Z') return false;
                col = col * 26 + (ch - 'A' + 1);
                i++;
            }
            if (col <= 0 || i >= cellRef.Length) return false;

            int row;
            if (!int.TryParse(cellRef.Substring(i), out row) || row <= 0)
                return false;

            colIdx = col - 1;
            rowIdx = row - 1;
            return true;
        }

        private static bool TryParseMergeRange(string refStr, out ExcelMergeRange range)
        {
            range = null;
            if (string.IsNullOrWhiteSpace(refStr)) return false;
            var parts = refStr.Split(':');
            if (parts.Length != 2) return false;

            int r1, c1, r2, c2;
            if (!TryParseCellRef(parts[0].Trim(), out r1, out c1)) return false;
            if (!TryParseCellRef(parts[1].Trim(), out r2, out c2)) return false;

            range = new ExcelMergeRange
            {
                StartRow = Math.Min(r1, r2),
                EndRow = Math.Max(r1, r2),
                StartCol = Math.Min(c1, c2),
                EndCol = Math.Max(c1, c2)
            };
            return true;
        }

        private static string NormalizeLineEndings(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static string SanitizeXmlText(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var sb = new StringBuilder(input.Length);
            foreach (char ch in input)
            {
                if (ch == '\t' || ch == '\n' || ch == '\r' || (ch >= 0x20 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD))
                    sb.Append(ch);
            }
            return sb.ToString();
        }

        private static string SanitizeSheetName(string name, HashSet<string> usedNames)
        {
            string cleaned = (name ?? "Sheet").Trim();
            foreach (char bad in new[] { '\\', '/', '?', '*', '[', ']', ':' })
                cleaned = cleaned.Replace(bad, '_');
            if (string.IsNullOrWhiteSpace(cleaned))
                cleaned = "Sheet";
            if (cleaned.Length > 31)
                cleaned = cleaned.Substring(0, 31);

            string candidate = cleaned;
            int seq = 2;
            while (usedNames.Contains(candidate))
            {
                string suffix = "_" + seq++;
                int maxBase = Math.Max(1, 31 - suffix.Length);
                candidate = (cleaned.Length > maxBase ? cleaned.Substring(0, maxBase) : cleaned) + suffix;
            }
            usedNames.Add(candidate);
            return candidate;
        }

        private static string ColumnIndexToLetters(int zeroBasedCol)
        {
            int dividend = zeroBasedCol + 1;
            var sb = new StringBuilder();
            while (dividend > 0)
            {
                int modulo = (dividend - 1) % 26;
                sb.Insert(0, (char)('A' + modulo));
                dividend = (dividend - modulo) / 26;
            }
            return sb.ToString();
        }

        private static void WriteZipText(ZipArchive zip, string entryPath, string content)
        {
            var entry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private static string BuildContentTypesXml(int sheetCount)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 1; i <= sheetCount; i++)
                sb.Append("<Override PartName=\"/xl/worksheets/sheet" + i + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            sb.Append("</Types>");
            return sb.ToString();
        }

        private static string BuildRootRelsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                + "</Relationships>";
        }

        private static string BuildWorkbookXml(IList<ExcelExportSheet> sheets)
        {
            var doc = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(NsMain + "workbook",
                    new XAttribute(XNamespace.Xmlns + "r", NsRel.NamespaceName),
                    new XElement(NsMain + "sheets",
                        sheets.Select((s, idx) => new XElement(NsMain + "sheet",
                            new XAttribute("name", s.Name),
                            new XAttribute("sheetId", idx + 1),
                            new XAttribute(NsRel + "id", "rId" + (idx + 1)))))));
            return doc.Declaration + doc.ToString(SaveOptions.DisableFormatting);
        }

        private static string BuildWorkbookRelsXml(int sheetCount)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 1; i <= sheetCount; i++)
                sb.Append("<Relationship Id=\"rId" + i + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet" + i + ".xml\"/>");
            sb.Append("<Relationship Id=\"rId" + (sheetCount + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        private static string BuildStylesXml()
        {
            // Style indices:
            // 0 = Normal default
            // 1 = Title banner (bold, dark navy fill, white text)
            // 2 = Header row (bold, slate fill, white text, centered, wrapped, border)
            // 3 = Data cell (white bg, wrapped, thin border)
            // 4 = Alternating data cell (soft slate bg #F8FAFC, wrapped, thin border)
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<fonts count=\"3\">"
                + "<font><sz val=\"10\"/><name val=\"맑은 고딕\"/></font>"
                + "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"맑은 고딕\"/></font>"
                + "<font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"맑은 고딕\"/></font>"
                + "</fonts>"
                + "<fills count=\"5\">"
                + "<fill><patternFill patternType=\"none\"/></fill>"
                + "<fill><patternFill patternType=\"gray125\"/></fill>"
                + "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF0F172A\"/></patternFill></fill>"
                + "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1E293B\"/></patternFill></fill>"
                + "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF8FAFC\"/></patternFill></fill>"
                + "</fills>"
                + "<borders count=\"2\">"
                + "<border><left/><right/><top/><bottom/><diagonal/></border>"
                + "<border>"
                + "<left style=\"thin\"><color rgb=\"FFCBD5E1\"/></left>"
                + "<right style=\"thin\"><color rgb=\"FFCBD5E1\"/></right>"
                + "<top style=\"thin\"><color rgb=\"FFCBD5E1\"/></top>"
                + "<bottom style=\"thin\"><color rgb=\"FFCBD5E1\"/></bottom>"
                + "<diagonal/>"
                + "</border>"
                + "</borders>"
                + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
                + "<cellXfs count=\"5\">"
                + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
                + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf>"
                + "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>"
                + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>"
                + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>"
                + "</cellXfs>"
                + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>"
                + "</styleSheet>";
        }

        private static string BuildWorksheetXml(ExcelExportSheet sheet)
        {
            int colCount = Math.Max(sheet.Headers.Count, sheet.Rows.Count > 0 ? sheet.Rows.Max(r => r != null ? r.Count : 0) : 1);
            if (colCount < 1) colCount = 1;

            double[] colWidths = new double[colCount];
            for (int c = 0; c < colCount; c++)
            {
                string hdr = c < sheet.Headers.Count ? sheet.Headers[c] : string.Empty;
                colWidths[c] = Math.Max(8.0, EstimateDisplayWidth(hdr) + 3.0);
            }
            foreach (var row in sheet.Rows)
            {
                if (row == null) continue;
                for (int c = 0; c < row.Count && c < colCount; c++)
                {
                    double w = EstimateDisplayWidth(row[c]) + 2.5;
                    if (w > colWidths[c]) colWidths[c] = w;
                }
            }
            for (int c = 0; c < colCount; c++)
                colWidths[c] = Math.Min(52.0, colWidths[c]);

            bool hasBanner = !string.IsNullOrWhiteSpace(sheet.TitleBanner);
            int headerExcelRow = hasBanner ? 2 : 1;

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

            sb.Append("<sheetViews><sheetView workbookViewId=\"0\">");
            sb.Append("<pane ySplit=\"" + headerExcelRow + "\" topLeftCell=\"A" + (headerExcelRow + 1) + "\" activePane=\"bottomLeft\" state=\"frozen\"/>");
            sb.Append("</sheetView></sheetViews>");

            sb.Append("<cols>");
            for (int c = 0; c < colCount; c++)
            {
                sb.Append("<col min=\"" + (c + 1) + "\" max=\"" + (c + 1) + "\" width=\"" + colWidths[c].ToString("0.0", CultureInfo.InvariantCulture) + "\" customWidth=\"1\"/>");
            }
            sb.Append("</cols>");

            sb.Append("<sheetData>");
            int excelRow = 1;

            if (hasBanner)
            {
                sb.Append("<row r=\"" + excelRow + "\" ht=\"24\" customHeight=\"1\">");
                AppendInlineStringCell(sb, excelRow, 0, sheet.TitleBanner, 1);
                for (int c = 1; c < colCount; c++)
                    AppendInlineStringCell(sb, excelRow, c, string.Empty, 1);
                sb.Append("</row>");
                excelRow++;
            }

            if (sheet.Headers.Count > 0)
            {
                sb.Append("<row r=\"" + excelRow + "\" ht=\"22\" customHeight=\"1\">");
                for (int c = 0; c < colCount; c++)
                {
                    string h = c < sheet.Headers.Count ? sheet.Headers[c] : string.Empty;
                    AppendInlineStringCell(sb, excelRow, c, h, 2);
                }
                sb.Append("</row>");
                excelRow++;
            }

            for (int r = 0; r < sheet.Rows.Count; r++)
            {
                var row = sheet.Rows[r];
                int styleId = (r % 2 == 1) ? 4 : 3;
                sb.Append("<row r=\"" + excelRow + "\">");
                for (int c = 0; c < colCount; c++)
                {
                    string val = (row != null && c < row.Count) ? row[c] : string.Empty;
                    AppendInlineStringCell(sb, excelRow, c, val, styleId);
                }
                sb.Append("</row>");
                excelRow++;
            }

            sb.Append("</sheetData>");

            if (sheet.Headers.Count > 0)
            {
                int lastDataRow = Math.Max(headerExcelRow, excelRow - 1);
                string endCol = ColumnIndexToLetters(colCount - 1);
                sb.Append("<autoFilter ref=\"A" + headerExcelRow + ":" + endCol + lastDataRow + "\"/>");
            }

            if (hasBanner && colCount > 1)
            {
                sb.Append("<mergeCells count=\"1\"><mergeCell ref=\"A1:" + ColumnIndexToLetters(colCount - 1) + "1\"/></mergeCells>");
            }

            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static void AppendInlineStringCell(StringBuilder sb, int excelRow, int colIdx, string value, int styleId)
        {
            string cellRef = ColumnIndexToLetters(colIdx) + excelRow;
            string safe = SanitizeXmlText(value);
            string escaped = new XText(safe).ToString();
            sb.Append("<c r=\"" + cellRef + "\" t=\"inlineStr\" s=\"" + styleId + "\"><is><t xml:space=\"preserve\">" + escaped + "</t></is></c>");
        }

        private static double EstimateDisplayWidth(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0.0;
            double maxLine = 0.0;
            foreach (var line in text.Split('\n'))
            {
                double w = 0.0;
                foreach (char ch in line)
                    w += ch > 127 ? 2.0 : 1.1;
                if (w > maxLine) maxLine = w;
            }
            return maxLine;
        }

        private static string DecodeCsvBytesWithFallback(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            try
            {
                var strictUtf8 = new UTF8Encoding(false, true);
                return strictUtf8.GetString(bytes);
            }
            catch
            {
                try
                {
                    return Encoding.GetEncoding(949).GetString(bytes);
                }
                catch
                {
                    return Encoding.Default.GetString(bytes);
                }
            }
        }

        private static List<List<string>> ParseCsvText(string text)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrEmpty(text)) return rows;

            var currentRow = new List<string>();
            var cell = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        cell.Append(ch);
                    }
                }
                else
                {
                    if (ch == '"' && cell.Length == 0)
                    {
                        inQuotes = true;
                    }
                    else if (ch == ',')
                    {
                        currentRow.Add(cell.ToString());
                        cell.Clear();
                    }
                    else if (ch == '\r' || ch == '\n')
                    {
                        if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                            i++;
                        currentRow.Add(cell.ToString());
                        cell.Clear();
                        rows.Add(currentRow);
                        currentRow = new List<string>();
                    }
                    else
                    {
                        cell.Append(ch);
                    }
                }
            }

            if (cell.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(cell.ToString());
                rows.Add(currentRow);
            }

            return rows;
        }

        private static string EscapeCsvCell(string val)
        {
            string s = val ?? string.Empty;
            if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
