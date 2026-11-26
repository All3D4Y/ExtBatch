using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CmOxeProvisioner.Models;

namespace CmOxeProvisioner.Services
{
    /// <summary>
    /// Bidirectional mapper between DialPlanDatabase and OOXML/CSV workbooks.
    /// Supports both the original company multi-sheet Excel format (엘비유세스_선번장_*.xlsx)
    /// and ExtBatch's clean roundtrip Excel/CSV export format.
    /// </summary>
    public static class DialPlanWorkbookMapper
    {
        private static readonly Dictionary<string, string> ExcelToCmAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "수능_UHD", "한국교육과정평가원" },
                { "한국교육과정평가원", "한국교육과정평가원" },
                { "전자세금 계산서", "전자세금계산서" },
                { "세종텔레콤 (CCTV)", "세종텔레콤_CCTV" },
                { "LG하우시스-그린리모델링 센터", "LG하우시스_그린리모델링" },
                { "LG전자 IT 업무지원", "LGEIT" },
                { "하이프라자 개통실", "하이프라자_개통센터" },
                { "더본 코리아", "더본코리아" },
                { "볼트테크 코리아", "볼트테크코리아" },
                { "바바패션 A/S", "바바패션AS" },
                { "GS네트윅스", "GS네트웍스" },
                { "현대 홈쇼핑 PDS", "현대홈쇼핑CIC" },
                { "현대홈쇼핑 CIC", "현대홈쇼핑CIC" },
                { "LG 라이프케어 포탈", "LG라이프케어" },
                { "LG ID", "LG통합ID" },
                { "LG 하우시스-알루미늄 센터", "LG하우시스_알루미늄" },
                { "LX 하우시스 지인 해피콜 센터", "LG하우시스_지인_해피콜" },
                { "LX 하우시스 한샘견적 지원센터", "LX하우시스_한샘견적_지원센터" },
                { "LX하우시스 통합지원 센터", "LX하우시스_인테리어" },
                { "LX하우시스 제휴영업 견적지원센터", "LX하우시스_영업지점견적센터" },
                { "엘비그룹 헬프데스크", "LB헬프데스크" },
                { "경기도평생교육바우처", "경기평생교육바우처" },
                { "충북평생교육이용권센터", "충북평생교육이용권" },
                { "더원 플랫폼 알뜰폰 개통센터", "더원플랫폼_개통센터" },
                { "교육시설안전원 공제가입 CS센터", "교육시설안전원" },
                { "하이엠솔루텍OB (해피콜)", "하이엠솔루텍OB" },
                { "LGU+ 제휴센터", "유플러스제휴센터" },
                { "티켓플랫폼 고객센터", "티켓플랫폼" },
                { "LG생건 직영몰", "LG생활건강" },
                { "LG생건 뷰티", "LG생활건강" },
                { "LG 생활건강", "LG생활건강" },
                { "LG생건 외부몰", "LG생활건강_외부몰" },
                { "LG하우시스-창호SCM", "LX하우시스_창호SCM" },
                { "LG하우시스-인테리어", "LX하우시스_인테리어" },
                { "LG하우시스-신유통", "LG하우시스_신유통" },
                { "LG하우시스-커머셜 고객센터", "LG하우시스_커머셜" },
                { "LX하우시스 B2C온라인 지원센터", "LX하우시스_B2C" },
                { "AJ오토파킹시스템즈", "AJ오토파킹" },
                { "LG헬로비전", "헬로비전" },
                { "우리은행 사전예약 센터", "우리은행예약센터" },
                { "MYLGID 통신 상담 센터", "MYLGID통신상담센터" },
                { "민팃 (대표)", "민팃" },
                { "민팃 (KT)", "민팃" },
                { "민팃 (LGU+)", "민팃" },
                { "민팃 (안심거래 키오스크)", "민팃" },
                { "민팃 (관제센터)", "민팃" },
                { "테스트 (Test / Test2 / Test3)", "__TEST__ (테스트 폴더)" }
            };

        public static DialPlanDatabase ImportFromXlsx(
            string xlsxPath,
            IList<CustomerInfo> cmCustomers,
            out string reportSummary)
        {
            var sheets = ExcelWorkbookCodec.ReadXlsx(xlsxPath);
            var db = new DialPlanDatabase
            {
                version = "1.2.0",
                sourceFile = Path.GetFileName(xlsxPath) ?? string.Empty,
                updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            bool isRoundtripExport = sheets.Any(s =>
                string.Equals(s.Name, "Dial-Plan (운영·테스트)", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Name, "Entity마스터", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Name, "D.V.G멘트", StringComparison.OrdinalIgnoreCase));

            foreach (var sheet in sheets)
            {
                string sn = (sheet.Name ?? string.Empty).Trim();
                if (sn.Equals("Dial-Plan", StringComparison.OrdinalIgnoreCase) ||
                    sn.Equals("Dial-Plan #2", StringComparison.OrdinalIgnoreCase) ||
                    sn.StartsWith("Dial-Plan", StringComparison.OrdinalIgnoreCase))
                {
                    ParseDialPlanSheet(sheet, DialPlanStatus.Active, sn, db.centers);
                }
                else if (sn.IndexOf("종료", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ParseDialPlanSheet(sheet, DialPlanStatus.Closed, sn, db.centers);
                }
                else if (sn.IndexOf("PRI", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ParsePriSheet(sheet, db.priTrunks);
                }
                else if (sn.Equals("Sheet2", StringComparison.OrdinalIgnoreCase) ||
                         sn.IndexOf("Entity", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ParseEntitySheet(sheet, db.entities);
                }
                else if (sn.IndexOf("IP IVR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         sn.IndexOf("IP_IVR", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ParseIpIvrSheet(sheet, db.ipIvrChannels);
                }
                else if (sn.IndexOf("GPA", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ParseGpaSheet(sheet, db.gpaBoards);
                }
                else if (sn.IndexOf("멘트", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ParseDvgSheet(sheet, db.dvgPrompts);
                }
                else if (sn.IndexOf("IP정보", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         sn.IndexOf("IP_정보", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ParseIpHardwareSheet(sheet, db.pbxHardware);
                }
                else if (sn.IndexOf("MDF", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ParseMdfSheet(sheet, db.mdfBlocks);
                }
            }

            // Reconcile only when importing raw company Excel or when CM links are needed
            int promoted = 0;
            int addedFromCm = 0;
            if (!isRoundtripExport && cmCustomers != null && cmCustomers.Count > 0)
            {
                ReconcileWithCmCustomers(db, cmCustomers, out promoted, out addedFromCm);
            }
            else if (cmCustomers != null && cmCustomers.Count > 0)
            {
                // For roundtrip re-import, still ensure center IDs and CM link flags are tidy
                foreach (var c in db.centers)
                {
                    if (string.IsNullOrWhiteSpace(c.id))
                        c.id = BuildCenterId(c.project, db.centers);
                }
            }

            InferCenterFlags(db);
            db.EnsureLists();

            int activeCnt = db.centers.Count(c => c.status == DialPlanStatus.Active);
            int testCnt = db.centers.Count(c => c.status == DialPlanStatus.Test);
            int closedCnt = db.centers.Count(c => c.status == DialPlanStatus.Closed);
            reportSummary = string.Format(
                CultureInfo.InvariantCulture,
                "시트 {0}개 파싱 완료 -> 센터 {1}개 (운영 {2}, 테스트 {3}, 종료 {4}; 종료시트→운영 복원 {5}, Config신규보충 {6}), PRI {7}건, Entity {8}건, IP IVR {9}건, GPA {10}건, 멘트 {11}건, PBX보드 {12}건, MDF {13}행",
                sheets.Count,
                db.centers.Count,
                activeCnt,
                testCnt,
                closedCnt,
                promoted,
                addedFromCm,
                db.priTrunks.Count,
                db.entities.Count,
                db.ipIvrChannels.Count,
                db.gpaBoards.Count,
                db.dvgPrompts.Count,
                db.pbxHardware.Count,
                db.mdfBlocks.Count);

            return db;
        }

        public static List<DialPlanCenter> ImportDialPlanCsv(string csvPath)
        {
            var rows = ExcelWorkbookCodec.ReadCsv(csvPath);
            var centers = new List<DialPlanCenter>();
            if (rows.Count == 0) return centers;

            int headerRowIdx = -1;
            for (int r = 0; r < Math.Min(rows.Count, 10); r++)
            {
                string joined = string.Join("|", rows[r]);
                if (joined.Contains("프로젝트") || joined.Contains("센터") || joined.Contains("대표번호") || joined.Contains("Entity"))
                {
                    headerRowIdx = r;
                    break;
                }
            }

            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int startRow = 0;
            if (headerRowIdx >= 0)
            {
                var hdr = rows[headerRowIdx];
                for (int c = 0; c < hdr.Count; c++)
                {
                    string h = NormalizeHeader(hdr[c]);
                    if (!string.IsNullOrEmpty(h) && !colMap.ContainsKey(h))
                        colMap[h] = c;
                }
                startRow = headerRowIdx + 1;
            }

            DialPlanCenter current = null;
            for (int r = startRow; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row == null || row.All(string.IsNullOrWhiteSpace))
                {
                    current = null;
                    continue;
                }

                string statusRaw = GetCsvMapped(row, colMap, "상태", -1);
                string loc = GetCsvMapped(row, colMap, "위치", colMap.Count == 0 ? 0 : -1);
                string proj = GetCsvMapped(row, colMap, "프로젝트(콜센터)", colMap.Count == 0 ? 1 : -1);
                if (string.IsNullOrWhiteSpace(proj))
                    proj = GetCsvMapped(row, colMap, "프로젝트", -1);
                if (string.IsNullOrWhiteSpace(proj))
                    proj = GetCsvMapped(row, colMap, "센터명", -1);

                string sub = GetCsvMapped(row, colMap, "세부업무", colMap.Count == 0 ? 2 : -1);
                string status = string.IsNullOrWhiteSpace(statusRaw)
                    ? DialPlanStatus.Active
                    : DialPlanStatus.Normalize(statusRaw);

                if (!string.IsNullOrWhiteSpace(proj))
                {
                    current = centers.FirstOrDefault(c =>
                        string.Equals(c.project, proj.Trim(), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(c.status, status, StringComparison.OrdinalIgnoreCase));
                    if (current == null)
                    {
                        current = new DialPlanCenter
                        {
                            id = BuildCenterId(proj, centers),
                            status = status,
                            location = loc.Trim(),
                            project = proj.Trim(),
                            cmCustomerName = !string.IsNullOrEmpty(GetCsvMapped(row, colMap, "Config연동", -1))
                                ? GetCsvMapped(row, colMap, "Config연동", -1).Trim()
                                : GetCsvMapped(row, colMap, "CM연동", -1).Trim(),
                            apUrl = GetCsvMapped(row, colMap, "AP URL", -1).Trim(),
                            useVQueue = ParseBoolLoose(GetCsvMapped(row, colMap, "VQueue", -1)),
                            useIvr = ParseBoolLoose(GetCsvMapped(row, colMap, "IVR사용", -1)),
                            sourceSheet = "CSV"
                        };
                        centers.Add(current);
                    }
                }
                else if (current == null)
                {
                    continue;
                }

                var line = new DialPlanLine
                {
                    subTask = sub.Trim(),
                    mainNumber = GetCsvMapped(row, colMap, "대표번호", colMap.Count == 0 ? 3 : -1).Trim(),
                    carrier = GetCsvMapped(row, colMap, "통신사", colMap.Count == 0 ? 4 : -1).Trim(),
                    ownerName = GetCsvMapped(row, colMap, "대표번호 명의", colMap.Count == 0 ? 5 : -1).Trim(),
                    incomingNumber = GetCsvMapped(row, colMap, "착신번호", colMap.Count == 0 ? 6 : -1).Trim(),
                    inboundRsi = GetCsvMapped(row, colMap, "인입 RSI", colMap.Count == 0 ? 7 : -1).Trim(),
                    ivrPdsChannel = GetCsvMapped(row, colMap, "IVR (PDS) 채널", colMap.Count == 0 ? 8 : -1).Trim(),
                    ivrFwPilot = GetCsvMapped(row, colMap, "IVR-Fw Pilot", colMap.Count == 0 ? 9 : -1).Trim(),
                    agentTransferRsi = GetCsvMapped(row, colMap, "상담연결 RSI", colMap.Count == 0 ? 10 : -1).Trim(),
                    agentPilot = GetCsvMapped(row, colMap, "Agent Pilot", colMap.Count == 0 ? 11 : -1).Trim(),
                    agentQueue = GetCsvMapped(row, colMap, "Agent Queue", colMap.Count == 0 ? 12 : -1).Trim(),
                    agentPg = GetCsvMapped(row, colMap, "Agent PG", colMap.Count == 0 ? 13 : -1).Trim(),
                    phoneExt = GetCsvMapped(row, colMap, "Phone 내선", colMap.Count == 0 ? 14 : -1).Trim(),
                    loginId = GetCsvMapped(row, colMap, "Login-ID", colMap.Count == 0 ? 15 : -1).Trim(),
                    callType = GetCsvMapped(row, colMap, "Call Type", colMap.Count == 0 ? 16 : -1).Trim(),
                    entityNo = GetCsvMapped(row, colMap, "Entity NO", colMap.Count == 0 ? 17 : -1).Trim(),
                    trunkGroupNo = GetCsvMapped(row, colMap, "Trunk 그룹 NO", colMap.Count == 0 ? 18 : -1).Trim(),
                    testIvrDn = GetCsvMapped(row, colMap, "테스트 IVR DN", colMap.Count == 0 ? 19 : -1).Trim(),
                    note = GetCsvMapped(row, colMap, "비고", colMap.Count == 0 ? 20 : -1).Trim()
                };
                current.lines.Add(line);
            }

            return centers;
        }

        public static List<ExcelExportSheet> BuildExportWorkbook(DialPlanDatabase db)
        {
            if (db == null) db = new DialPlanDatabase();
            db.EnsureLists();
            var sheets = new List<ExcelExportSheet>();

            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            var dialPlanHeaders = new List<string>
            {
                "상태", "위치", "프로젝트(콜센터)", "(세부업무)", "대표번호", "통신사",
                "대표번호 명의", "착신번호", "인입 RSI", "IVR (PDS) 채널", "IVR-Fw Pilot",
                "상담연결 RSI", "Agent Pilot", "Agent Queue", "Agent PG", "Phone 내선",
                "Login-ID", "Call Type", "Entity NO", "Trunk 그룹 NO", "테스트 IVR DN",
                "Config연동", "VQueue", "IVR사용", "비고",
                "구_IVR_RSI", "구_IVR_Pilot", "구_IVR_Queue", "구_IVR_PG", "구_RSI특징", "AP URL"
            };

            var activeAndTest = db.centers
                .Where(c => c.status == DialPlanStatus.Active || c.status == DialPlanStatus.Test)
                .ToList();
            sheets.Add(BuildCenterExportSheet("Dial-Plan (운영·테스트)",
                "엘비유세스 선번장 Dial-Plan (운영 중 / 테스트용) - " + stamp,
                dialPlanHeaders, activeAndTest));

            var closedCenters = db.centers
                .Where(c => c.status == DialPlanStatus.Closed)
                .ToList();
            sheets.Add(BuildCenterExportSheet("업무종료 (종료센터)",
                "엘비유세스 선번장 업무 종료 센터 - " + stamp,
                dialPlanHeaders, closedCenters));

            // PRI 현황
            var priSheet = new ExcelExportSheet("PRI현황")
            {
                TitleBanner = "PRI 회선 현황 (" + stamp + ")",
                Headers = new List<string>
                {
                    "실장 보드", "Logical", "TG 신규", "상태", "사용센터", "트렁크 그룹이름",
                    "할당 DID 대역", "사업자", "DID/DOD", "가입자번호", "중계번호", "개통", "비고"
                }
            };
            foreach (var p in db.priTrunks)
            {
                priSheet.Rows.Add(new List<string>
                {
                    S(p.boardSlot), S(p.logical), S(p.tgNew), S(p.status), S(p.centerName),
                    S(p.trunkGroupName), S(p.didRange), S(p.carrier), S(p.didDod),
                    S(p.subscriberNo), S(p.relayNo), S(p.openedDate), S(p.note)
                });
            }
            sheets.Add(priSheet);

            // Entity 마스터
            var entSheet = new ExcelExportSheet("Entity마스터")
            {
                TitleBanner = "OXE Entity NO 마스터 (" + stamp + ")",
                Headers = new List<string> { "Entity", "엔티티명", "Disc.Num", "비고" }
            };
            foreach (var e in db.entities)
            {
                entSheet.Rows.Add(new List<string> { S(e.entityNo), S(e.entityName), S(e.discNum), S(e.note) });
            }
            sheets.Add(entSheet);

            // IP IVR
            var ivrSheet = new ExcelExportSheet("IP_IVR")
            {
                TitleBanner = "IP IVR 내선 / 채널 할당 현황 (" + stamp + ")",
                Headers = new List<string> { "장비명", "신규 IVR DN", "채널 수", "센터", "비고" }
            };
            foreach (var ch in db.ipIvrChannels)
            {
                ivrSheet.Rows.Add(new List<string> { S(ch.deviceName), S(ch.ivrDn), S(ch.channelCount), S(ch.centerName), S(ch.note) });
            }
            sheets.Add(ivrSheet);

            // GPA 시간
            var gpaSheet = new ExcelExportSheet("GPA시간")
            {
                TitleBanner = "GPA 보드 잔여시간 정보 (" + stamp + ")",
                Headers = new List<string> { "GPA 보드 위치", "잔여시간 정보", "비고" }
            };
            foreach (var g in db.gpaBoards)
            {
                gpaSheet.Rows.Add(new List<string> { S(g.boardLocation), S(g.remainingTime), S(g.note) });
            }
            sheets.Add(gpaSheet);

            // D.V.G 멘트
            var dvgSheet = new ExcelExportSheet("D.V.G멘트")
            {
                TitleBanner = "D.V.G 안내 멘트 통합 (" + stamp + ")",
                Headers = new List<string> { "대역", "구분", "세부", "내용", "D.V.G", "시간(Sec)", "보드", "비고" }
            };
            foreach (var d in db.dvgPrompts)
            {
                dvgSheet.Rows.Add(new List<string>
                {
                    S(d.band), S(d.category), S(d.subCategory), S(d.content),
                    S(d.dvgNo), S(d.durationSec), S(d.board), S(d.note)
                });
            }
            sheets.Add(dvgSheet);

            // PBX IP 정보
            var ipSheet = new ExcelExportSheet("PBX_IP정보")
            {
                TitleBanner = "MAIN / SUB PBX 보드 IP 및 MAC 정보 (" + stamp + ")",
                Headers = new List<string> { "PBX SET", "실장 보드", "보드명", "IP정보", "MAC 정보", "비고" }
            };
            foreach (var hw in db.pbxHardware)
            {
                ipSheet.Rows.Add(new List<string>
                {
                    S(hw.pbxSet), S(hw.boardSlot), S(hw.boardName),
                    S(hw.ipAddress), S(hw.macAddress), S(hw.note)
                });
            }
            sheets.Add(ipSheet);

            // MDF 블록
            var mdfSheet = new ExcelExportSheet("MDF블록")
            {
                TitleBanner = "MDF / 110 Block 선번 현황 (" + stamp + ")",
                Headers = new List<string> { "구분 섹션", "행번호" }
            };
            int maxCells = db.mdfBlocks.Count > 0 ? Math.Max(1, db.mdfBlocks.Max(m => m.cells != null ? m.cells.Count : 0)) : 24;
            for (int c = 0; c < maxCells; c++)
                mdfSheet.Headers.Add("C" + c);
            foreach (var m in db.mdfBlocks)
            {
                var r = new List<string> { S(m.section), m.rowIndex.ToString(CultureInfo.InvariantCulture) };
                for (int c = 0; c < maxCells; c++)
                    r.Add(m.cells != null && c < m.cells.Count ? S(m.cells[c]) : string.Empty);
                mdfSheet.Rows.Add(r);
            }
            sheets.Add(mdfSheet);

            return sheets;
        }

        private static ExcelExportSheet BuildCenterExportSheet(
            string sheetName,
            string banner,
            List<string> headers,
            List<DialPlanCenter> centers)
        {
            var sh = new ExcelExportSheet(sheetName)
            {
                TitleBanner = banner,
                Headers = headers
            };

            foreach (var c in centers)
            {
                var lines = (c.lines != null && c.lines.Count > 0)
                    ? c.lines
                    : new List<DialPlanLine> { new DialPlanLine() };

                for (int i = 0; i < lines.Count; i++)
                {
                    var ln = lines[i] ?? new DialPlanLine();
                    string centerNote = i == 0 && !string.IsNullOrWhiteSpace(c.notes) && string.IsNullOrWhiteSpace(ln.note)
                        ? c.notes
                        : S(ln.note);
                    sh.Rows.Add(new List<string>
                    {
                        S(c.StatusBadgeText),
                        S(c.location),
                        S(c.project),
                        S(ln.subTask),
                        S(ln.mainNumber),
                        S(ln.carrier),
                        S(ln.ownerName),
                        S(ln.incomingNumber),
                        S(ln.inboundRsi),
                        S(ln.ivrPdsChannel),
                        S(ln.ivrFwPilot),
                        S(ln.agentTransferRsi),
                        S(ln.agentPilot),
                        S(ln.agentQueue),
                        S(ln.agentPg),
                        S(ln.phoneExt),
                        S(ln.loginId),
                        S(ln.callType),
                        S(ln.entityNo),
                        S(ln.trunkGroupNo),
                        S(ln.testIvrDn),
                        S(c.cmCustomerName),
                        c.useVQueue ? "Y" : "N",
                        c.useIvr ? "Y" : "N",
                        centerNote,
                        S(ln.legacyIvrRsi),
                        S(ln.legacyIvrPilot),
                        S(ln.legacyIvrQueue),
                        S(ln.legacyIvrPg),
                        S(ln.legacyRsiNote),
                        S(c.apUrl)
                    });
                }
            }

            return sh;
        }

        private static void ParseDialPlanSheet(
            ExcelSheetData sheet,
            string defaultStatus,
            string sourceSheetName,
            List<DialPlanCenter> target)
        {
            int headerRow = -1;
            bool isRoundtrip = false;
            bool isLegacyClosed = false;

            for (int r = 0; r < Math.Min(sheet.RowCount, 12); r++)
            {
                string c0 = CleanCell(sheet.GetCell(r, 0));
                string c1 = CleanCell(sheet.GetCell(r, 1));
                string c3 = CleanCell(sheet.GetCell(r, 3));
                if (c0 == "상태" && c1 == "위치" && CleanCell(sheet.GetCell(r, 2)).Contains("프로젝트"))
                {
                    headerRow = r;
                    isRoundtrip = true;
                    break;
                }
                if (c0.Equals("위치", StringComparison.OrdinalIgnoreCase) ||
                    c0.Equals("Loc", StringComparison.OrdinalIgnoreCase) ||
                    c1.Contains("프로젝트") ||
                    c3.Equals("대표번호", StringComparison.OrdinalIgnoreCase))
                {
                    headerRow = r;
                    isLegacyClosed = CleanCell(sheet.GetCell(r, 8)).Contains("IVR RSI");
                    break;
                }
            }

            int startRow = headerRow >= 0 ? headerRow + 1 : 4;
            DialPlanCenter currentCenter = null;

            for (int r = startRow; r < sheet.RowCount; r++)
            {
                if (sheet.IsRowCompletelyEmpty(r, 30))
                {
                    currentCenter = null;
                    continue;
                }

                if (isRoundtrip)
                {
                    ParseRoundtripCenterRow(sheet, r, sourceSheetName, target, ref currentCenter);
                    continue;
                }

                string rawC0 = CleanSingleLine(sheet.GetCell(r, 0));
                string rawC1 = CleanSingleLine(sheet.GetCell(r, 1));
                string rawC2 = CleanSingleLine(sheet.GetCell(r, 2));

                // Skip section divider banners (e.g. R330 merged "테스트용 PBX Dial-plan")
                if (rawC0.IndexOf("테스트용 PBX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (string.IsNullOrWhiteSpace(sheet.GetRawCell(r, 1)) &&
                     !string.IsNullOrWhiteSpace(rawC0) &&
                     rawC0.Equals("위치", StringComparison.OrdinalIgnoreCase) &&
                     IsRangeEmpty(sheet, r, 2, 25)))
                {
                    currentCenter = null;
                    continue;
                }

                string effectiveLoc = rawC0;
                string effectiveProj = rawC1;
                string rowStatus = defaultStatus;
                string centerNote = string.Empty;

                // Special block 1: Dial-Plan bottom "테스트" section (R332..R339)
                if (rawC0 == "테스트" && string.IsNullOrWhiteSpace(rawC1))
                {
                    rowStatus = DialPlanStatus.Test;
                    if (rawC2.Contains("블랙 컨슈머") || (currentCenter != null && currentCenter.project == "블랙 컨슈머"))
                    {
                        effectiveLoc = "테스트";
                        effectiveProj = "블랙 컨슈머";
                    }
                    else
                    {
                        effectiveLoc = "TEST";
                        effectiveProj = "테스트 (Test / Test2 / Test3)";
                    }
                }
                else if (rawC1.IndexOf("T-server Test", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    rowStatus = DialPlanStatus.Test;
                }
                // Special block 2: 종료센터 R4..R18 ("LG생활건강 / 더 페이스샵" & "gs건설 / 자이")
                else if (sourceSheetName.IndexOf("종료", StringComparison.OrdinalIgnoreCase) >= 0 &&
                         string.IsNullOrWhiteSpace(rawC1) && !string.IsNullOrWhiteSpace(rawC0))
                {
                    if (rawC0.IndexOf("LG생활건강", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        effectiveLoc = string.Empty;
                        effectiveProj = string.IsNullOrWhiteSpace(rawC2) ? rawC0 : rawC2;
                        centerNote = "소속: " + rawC0;
                    }
                    else if (rawC0.IndexOf("gs건설", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        effectiveLoc = string.Empty;
                        effectiveProj = "GS건설 자이";
                        centerNote = "GS건설 프로젝트군";
                    }
                }

                // Determine whether this row uses old 26-col layout (종료센터 R4..R20) or standard 21-col layout
                bool use26Col = isLegacyClosed &&
                    ((r + 1) <= 20 || !string.IsNullOrWhiteSpace(sheet.GetCell(r, 21)) || !string.IsNullOrWhiteSpace(sheet.GetCell(r, 25)));

                DialPlanLine line = ExtractDialPlanLine(sheet, r, use26Col);
                if (IsDialPlanLineCompletelyEmpty(line) && string.IsNullOrWhiteSpace(effectiveProj))
                    continue;

                if (!string.IsNullOrWhiteSpace(effectiveProj))
                {
                    // Reuse current block if exact same project+location+status was already opened consecutively,
                    // OR create a distinct entry if from a different section/sheet.
                    DialPlanCenter existing = null;
                    if (currentCenter != null &&
                        string.Equals(currentCenter.project, effectiveProj, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(currentCenter.status, rowStatus, StringComparison.OrdinalIgnoreCase))
                    {
                        existing = currentCenter;
                    }
                    else
                    {
                        existing = target.FirstOrDefault(c =>
                            string.Equals(c.project, effectiveProj, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(c.status, rowStatus, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(c.sourceSheet, sourceSheetName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (existing != null)
                    {
                        currentCenter = existing;
                        if (string.IsNullOrWhiteSpace(currentCenter.location) && !string.IsNullOrWhiteSpace(effectiveLoc))
                            currentCenter.location = effectiveLoc;
                    }
                    else
                    {
                        currentCenter = new DialPlanCenter
                        {
                            id = BuildCenterId(effectiveProj, target),
                            status = rowStatus,
                            location = effectiveLoc,
                            project = effectiveProj,
                            sourceSheet = sourceSheetName,
                            notes = centerNote
                        };
                        target.Add(currentCenter);
                    }
                }
                else if (currentCenter == null)
                {
                    continue;
                }

                currentCenter.lines.Add(line);
            }
        }

        private static void ParseRoundtripCenterRow(
            ExcelSheetData sheet,
            int r,
            string sourceSheetName,
            List<DialPlanCenter> target,
            ref DialPlanCenter currentCenter)
        {
            string status = DialPlanStatus.Normalize(CleanCell(sheet.GetCell(r, 0)));
            string loc = CleanCell(sheet.GetCell(r, 1));
            string proj = CleanCell(sheet.GetCell(r, 2));
            if (string.IsNullOrWhiteSpace(proj) && currentCenter == null)
                return;

            string cmName = CleanCell(sheet.GetCell(r, 21));
            bool vq = ParseBoolLoose(sheet.GetCell(r, 22));
            bool ivr = ParseBoolLoose(sheet.GetCell(r, 23));
            string apUrl = CleanCell(sheet.GetCell(r, 30));

            if (!string.IsNullOrWhiteSpace(proj))
            {
                var found = target.FirstOrDefault(c =>
                    string.Equals(c.project, proj, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.status, status, StringComparison.OrdinalIgnoreCase));
                if (found == null)
                {
                    found = new DialPlanCenter
                    {
                        id = BuildCenterId(proj, target),
                        status = status,
                        location = loc,
                        project = proj,
                        cmCustomerName = cmName,
                        apUrl = apUrl,
                        useVQueue = vq,
                        useIvr = ivr,
                        sourceSheet = sourceSheetName
                    };
                    target.Add(found);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(found.location) && !string.IsNullOrWhiteSpace(loc)) found.location = loc;
                    if (string.IsNullOrWhiteSpace(found.cmCustomerName) && !string.IsNullOrWhiteSpace(cmName)) found.cmCustomerName = cmName;
                    if (string.IsNullOrWhiteSpace(found.apUrl) && !string.IsNullOrWhiteSpace(apUrl)) found.apUrl = apUrl;
                    found.useVQueue = found.useVQueue || vq;
                    found.useIvr = found.useIvr || ivr;
                }
                currentCenter = found;
            }

            var line = new DialPlanLine
            {
                subTask = CleanCell(sheet.GetCell(r, 3)),
                mainNumber = CleanCell(sheet.GetCell(r, 4)),
                carrier = CleanCell(sheet.GetCell(r, 5)),
                ownerName = CleanCell(sheet.GetCell(r, 6)),
                incomingNumber = CleanCell(sheet.GetCell(r, 7)),
                inboundRsi = CleanCell(sheet.GetCell(r, 8)),
                ivrPdsChannel = CleanCell(sheet.GetCell(r, 9)),
                ivrFwPilot = CleanCell(sheet.GetCell(r, 10)),
                agentTransferRsi = CleanCell(sheet.GetCell(r, 11)),
                agentPilot = CleanCell(sheet.GetCell(r, 12)),
                agentQueue = CleanCell(sheet.GetCell(r, 13)),
                agentPg = CleanCell(sheet.GetCell(r, 14)),
                phoneExt = CleanCell(sheet.GetCell(r, 15)),
                loginId = CleanCell(sheet.GetCell(r, 16)),
                callType = CleanCell(sheet.GetCell(r, 17)),
                entityNo = CleanCell(sheet.GetCell(r, 18)),
                trunkGroupNo = CleanCell(sheet.GetCell(r, 19)),
                testIvrDn = CleanCell(sheet.GetCell(r, 20)),
                note = CleanCell(sheet.GetCell(r, 24)),
                legacyIvrRsi = CleanCell(sheet.GetCell(r, 25)),
                legacyIvrPilot = CleanCell(sheet.GetCell(r, 26)),
                legacyIvrQueue = CleanCell(sheet.GetCell(r, 27)),
                legacyIvrPg = CleanCell(sheet.GetCell(r, 28)),
                legacyRsiNote = CleanCell(sheet.GetCell(r, 29))
            };
            currentCenter.lines.Add(line);
        }

        private static DialPlanLine ExtractDialPlanLine(ExcelSheetData sheet, int r, bool isLegacy26Col)
        {
            if (isLegacy26Col)
            {
                return new DialPlanLine
                {
                    subTask = CleanSingleLine(sheet.GetCell(r, 2)),
                    mainNumber = CleanCell(sheet.GetCell(r, 3)),
                    carrier = CleanSingleLine(sheet.GetCell(r, 4)),
                    ownerName = CleanSingleLine(sheet.GetCell(r, 5)),
                    incomingNumber = CleanCell(sheet.GetCell(r, 6)),
                    inboundRsi = CleanCell(sheet.GetCell(r, 7)),
                    legacyIvrRsi = CleanCell(sheet.GetCell(r, 8)),
                    legacyIvrPilot = CleanCell(sheet.GetCell(r, 9)),
                    legacyIvrQueue = CleanCell(sheet.GetCell(r, 10)),
                    legacyIvrPg = CleanSingleLine(sheet.GetCell(r, 11)),
                    ivrPdsChannel = CleanCell(sheet.GetCell(r, 12)),
                    ivrFwPilot = CleanCell(sheet.GetCell(r, 13)),
                    agentTransferRsi = CleanCell(sheet.GetCell(r, 14)),
                    agentPilot = CleanCell(sheet.GetCell(r, 15)),
                    legacyRsiNote = CleanCell(sheet.GetCell(r, 16)),
                    agentQueue = CleanCell(sheet.GetCell(r, 17)),
                    agentPg = CleanSingleLine(sheet.GetCell(r, 18)),
                    phoneExt = CleanSingleLine(sheet.GetCell(r, 19)),
                    loginId = CleanSingleLine(sheet.GetCell(r, 20)),
                    callType = CleanSingleLine(sheet.GetCell(r, 21)),
                    entityNo = CleanSingleLine(sheet.GetCell(r, 22)),
                    trunkGroupNo = CleanSingleLine(sheet.GetCell(r, 23)),
                    testIvrDn = CleanSingleLine(sheet.GetCell(r, 24)),
                    note = CleanCell(sheet.GetCell(r, 25))
                };
            }

            return new DialPlanLine
            {
                subTask = CleanSingleLine(sheet.GetCell(r, 2)),
                mainNumber = CleanCell(sheet.GetCell(r, 3)),
                carrier = CleanSingleLine(sheet.GetCell(r, 4)),
                ownerName = CleanSingleLine(sheet.GetCell(r, 5)),
                incomingNumber = CleanCell(sheet.GetCell(r, 6)),
                inboundRsi = CleanCell(sheet.GetCell(r, 7)),
                ivrPdsChannel = CleanCell(sheet.GetCell(r, 8)),
                ivrFwPilot = CleanCell(sheet.GetCell(r, 9)),
                agentTransferRsi = CleanCell(sheet.GetCell(r, 10)),
                agentPilot = CleanCell(sheet.GetCell(r, 11)),
                agentQueue = CleanCell(sheet.GetCell(r, 12)),
                agentPg = CleanSingleLine(sheet.GetCell(r, 13)),
                phoneExt = CleanSingleLine(sheet.GetCell(r, 14)),
                loginId = CleanSingleLine(sheet.GetCell(r, 15)),
                callType = CleanSingleLine(sheet.GetCell(r, 16)),
                entityNo = CleanSingleLine(sheet.GetCell(r, 17)),
                trunkGroupNo = CleanSingleLine(sheet.GetCell(r, 18)),
                testIvrDn = CleanSingleLine(sheet.GetCell(r, 19)),
                note = CleanCell(sheet.GetCell(r, 20))
            };
        }

        private static void ParsePriSheet(ExcelSheetData sheet, List<PriTrunkRow> list)
        {
            int headerRow = FindRowContaining(sheet, 0, "실장 보드");
            int startRow = headerRow >= 0 ? headerRow + 1 : 3;
            string lastBoard = string.Empty;

            for (int r = startRow; r < sheet.RowCount; r++)
            {
                if (sheet.IsRowCompletelyEmpty(r, 16)) continue;

                string board = CleanCell(sheet.GetCell(r, 0));
                if (!string.IsNullOrWhiteSpace(board)) lastBoard = board;
                else board = lastBoard;

                string logical = CleanCell(sheet.GetCell(r, 1));
                string tg = CleanCell(sheet.GetCell(r, 2));
                string center = CleanCell(sheet.GetCell(r, 4));
                string status = CleanCell(sheet.GetCell(r, 3));
                if (string.IsNullOrWhiteSpace(logical) && string.IsNullOrWhiteSpace(tg) &&
                    string.IsNullOrWhiteSpace(center) && string.IsNullOrWhiteSpace(status))
                    continue;

                string rawOpened = CleanCell(sheet.GetCell(r, 11));
                string formattedDate;
                if (ExcelWorkbookCodec.TryFormatExcelDateSerial(rawOpened, out formattedDate))
                    rawOpened = formattedDate;

                string note = CleanCell(sheet.GetCell(r, 12));
                string extra = CleanCell(sheet.GetCell(r, 14));
                if (!string.IsNullOrWhiteSpace(extra))
                    note = string.IsNullOrWhiteSpace(note) ? extra : (note + " | " + extra);

                list.Add(new PriTrunkRow
                {
                    boardSlot = board,
                    logical = logical,
                    tgNew = tg,
                    status = status,
                    centerName = center,
                    trunkGroupName = CleanCell(sheet.GetCell(r, 5)),
                    didRange = CleanCell(sheet.GetCell(r, 6)),
                    carrier = CleanCell(sheet.GetCell(r, 7)),
                    didDod = CleanCell(sheet.GetCell(r, 8)),
                    subscriberNo = CleanCell(sheet.GetCell(r, 9)),
                    relayNo = CleanCell(sheet.GetCell(r, 10)),
                    openedDate = rawOpened,
                    note = note
                });
            }
        }

        private static void ParseEntitySheet(ExcelSheetData sheet, List<EntityMasterRow> list)
        {
            int headerRow = -1;
            for (int r = 0; r < Math.Min(sheet.RowCount, 10); r++)
            {
                string c0 = CleanCell(sheet.GetCell(r, 0));
                string c1 = CleanCell(sheet.GetCell(r, 1));
                if (c0.Equals("Entity", StringComparison.OrdinalIgnoreCase) && c1.Contains("엔티티"))
                {
                    headerRow = r;
                    break;
                }
            }
            int startRow = headerRow >= 0 ? headerRow + 1 : 1;

            for (int r = startRow; r < sheet.RowCount; r++)
            {
                string ent = CleanCell(sheet.GetCell(r, 0));
                string name = CleanCell(sheet.GetCell(r, 1));
                string disc = CleanCell(sheet.GetCell(r, 2));
                string note = CleanCell(sheet.GetCell(r, 3));
                if (string.IsNullOrWhiteSpace(ent) && string.IsNullOrWhiteSpace(name))
                    continue;

                list.Add(new EntityMasterRow
                {
                    entityNo = ent,
                    entityName = name,
                    discNum = disc,
                    note = note
                });
            }
        }

        private static void ParseIpIvrSheet(ExcelSheetData sheet, List<IpIvrChannelRow> list)
        {
            int headerRow = FindRowContaining(sheet, 0, "장비명");
            int startRow = headerRow >= 0 ? headerRow + 1 : 2;
            string lastDevice = string.Empty;

            for (int r = startRow; r < sheet.RowCount; r++)
            {
                if (sheet.IsRowCompletelyEmpty(r, 6)) continue;

                string dev = CleanCell(sheet.GetCell(r, 0));
                if (!string.IsNullOrWhiteSpace(dev)) lastDevice = dev;
                else dev = lastDevice;

                string dn = CleanCell(sheet.GetCell(r, 1));
                string ch = CleanCell(sheet.GetCell(r, 2));
                string center = CleanCell(sheet.GetCell(r, 3));
                string note = CleanCell(sheet.GetCell(r, 4));
                string c5 = CleanCell(sheet.GetCell(r, 5));
                if (!string.IsNullOrWhiteSpace(c5))
                    note = string.IsNullOrWhiteSpace(note) ? c5 : (note + " | " + c5);

                if (string.IsNullOrWhiteSpace(dn) && string.IsNullOrWhiteSpace(center) && string.IsNullOrWhiteSpace(ch))
                    continue;

                list.Add(new IpIvrChannelRow
                {
                    deviceName = dev,
                    ivrDn = dn,
                    channelCount = ch,
                    centerName = center,
                    note = note
                });
            }
        }

        private static void ParseGpaSheet(ExcelSheetData sheet, List<GpaBoardRow> list)
        {
            int headerRow = -1;
            int colStart = 1;
            for (int r = 0; r < Math.Min(sheet.RowCount, 10); r++)
            {
                if (CleanCell(sheet.GetCell(r, 0)).Equals("GPA 보드 위치", StringComparison.OrdinalIgnoreCase))
                {
                    headerRow = r;
                    colStart = 0;
                    break;
                }
                if (CleanCell(sheet.GetCell(r, 1)).Equals("GPA 보드 위치", StringComparison.OrdinalIgnoreCase))
                {
                    headerRow = r;
                    colStart = 1;
                    break;
                }
            }

            int startRow = headerRow >= 0 ? headerRow + 1 : 4;
            for (int r = startRow; r < sheet.RowCount; r++)
            {
                string loc = CleanCell(sheet.GetCell(r, colStart));
                string rem = CleanCell(sheet.GetCell(r, colStart + 1));
                string note = CleanCell(sheet.GetCell(r, colStart + 2));
                if (string.IsNullOrWhiteSpace(loc) && string.IsNullOrWhiteSpace(rem))
                    continue;

                list.Add(new GpaBoardRow
                {
                    boardLocation = loc,
                    remainingTime = rem,
                    note = note
                });
            }
        }

        private static void ParseDvgSheet(ExcelSheetData sheet, List<DvgPromptRow> list)
        {
            string sn = sheet.Name ?? string.Empty;
            string defaultBand = string.Empty;
            if (sn.Contains("700")) defaultBand = "700~799";
            else if (sn.Contains("800")) defaultBand = "800~899";
            else if (sn.Contains("900")) defaultBand = "900~999";
            else if (sn.Contains("1800")) defaultBand = "1800~1899";

            int headerRow = -1;
            bool hasBandCol = false;
            for (int r = 0; r < Math.Min(sheet.RowCount, 8); r++)
            {
                string c0 = CleanCell(sheet.GetCell(r, 0));
                if (c0 == "대역" && CleanCell(sheet.GetCell(r, 1)) == "구분")
                {
                    headerRow = r;
                    hasBandCol = true;
                    break;
                }
                if (c0 == "구분" || CleanCell(sheet.GetRawCell(r, 3)).Equals("D.V.G", StringComparison.OrdinalIgnoreCase))
                {
                    headerRow = r;
                    break;
                }
            }

            int startRow = headerRow >= 0 ? headerRow + 1 : 1;
            int offset = hasBandCol ? 1 : 0;

            for (int r = startRow; r < sheet.RowCount; r++)
            {
                string band = hasBandCol ? CleanCell(sheet.GetCell(r, 0)) : defaultBand;
                string cat = CleanCell(sheet.GetCell(r, offset + 0));
                string sub = CleanCell(sheet.GetCell(r, offset + 1));
                string content = CleanCell(sheet.GetCell(r, offset + 2));
                string dvg = CleanCell(sheet.GetCell(r, offset + 3));
                string sec = CleanCell(sheet.GetCell(r, offset + 4));
                string board = CleanCell(sheet.GetCell(r, offset + 5));
                string note = CleanCell(sheet.GetCell(r, offset + 6));

                if (string.IsNullOrWhiteSpace(cat) && string.IsNullOrWhiteSpace(sub) &&
                    string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(dvg))
                    continue;

                list.Add(new DvgPromptRow
                {
                    band = band,
                    category = cat,
                    subCategory = sub,
                    content = content,
                    dvgNo = dvg,
                    durationSec = sec,
                    board = board,
                    note = note
                });
            }
        }

        private static void ParseIpHardwareSheet(ExcelSheetData sheet, List<PbxHardwareRow> list)
        {
            // Support exported unified sheet format first
            int unifiedHeader = FindRowContaining(sheet, 0, "PBX SET");
            if (unifiedHeader >= 0)
            {
                for (int r = unifiedHeader + 1; r < sheet.RowCount; r++)
                {
                    string set = CleanCell(sheet.GetCell(r, 0));
                    string slot = CleanCell(sheet.GetCell(r, 1));
                    string name = CleanCell(sheet.GetCell(r, 2));
                    string ip = CleanCell(sheet.GetCell(r, 3));
                    string mac = CleanCell(sheet.GetCell(r, 4));
                    string note = CleanCell(sheet.GetCell(r, 5));
                    if (string.IsNullOrWhiteSpace(slot) && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(ip))
                        continue;
                    list.Add(new PbxHardwareRow
                    {
                        pbxSet = set,
                        boardSlot = slot,
                        boardName = name,
                        ipAddress = ip,
                        macAddress = mac,
                        note = note
                    });
                }
                return;
            }

            // Original sheet11 side-by-side MAIN (C0..C3) & SUB (C7..C10)
            int headerRow = FindRowContaining(sheet, 0, "실장 보드");
            int startRow = headerRow >= 0 ? headerRow + 1 : 4;
            string lastMainSlot = string.Empty;
            string lastSubSlot = string.Empty;

            var mainRows = new List<PbxHardwareRow>();
            var subRows = new List<PbxHardwareRow>();

            for (int r = startRow; r < sheet.RowCount; r++)
            {
                string mSlot = CleanCell(sheet.GetCell(r, 0));
                string mName = CleanCell(sheet.GetCell(r, 1));
                string mIp = CleanCell(sheet.GetCell(r, 2));
                string mMac = CleanCell(sheet.GetCell(r, 3));
                if (!string.IsNullOrWhiteSpace(mSlot)) lastMainSlot = mSlot;
                if (!string.IsNullOrWhiteSpace(mName) || !string.IsNullOrWhiteSpace(mIp) || !string.IsNullOrWhiteSpace(mMac))
                {
                    mainRows.Add(new PbxHardwareRow
                    {
                        pbxSet = "MAIN",
                        boardSlot = string.IsNullOrWhiteSpace(mSlot) ? lastMainSlot : mSlot,
                        boardName = mName,
                        ipAddress = mIp,
                        macAddress = mMac,
                        note = string.Empty
                    });
                }

                string sSlot = CleanCell(sheet.GetCell(r, 7));
                string sName = CleanCell(sheet.GetCell(r, 8));
                string sIp = CleanCell(sheet.GetCell(r, 9));
                string sMac = CleanCell(sheet.GetCell(r, 10));
                if (!string.IsNullOrWhiteSpace(sSlot)) lastSubSlot = sSlot;
                if (!string.IsNullOrWhiteSpace(sName) || !string.IsNullOrWhiteSpace(sIp) || !string.IsNullOrWhiteSpace(sMac))
                {
                    subRows.Add(new PbxHardwareRow
                    {
                        pbxSet = "SUB",
                        boardSlot = string.IsNullOrWhiteSpace(sSlot) ? lastSubSlot : sSlot,
                        boardName = sName,
                        ipAddress = sIp,
                        macAddress = sMac,
                        note = string.Empty
                    });
                }
            }

            list.AddRange(mainRows);
            list.AddRange(subRows);
        }

        private static void ParseMdfSheet(ExcelSheetData sheet, List<MdfBlockRow> list)
        {
            int unifiedHeader = -1;
            for (int r = 0; r < Math.Min(sheet.RowCount, 6); r++)
            {
                if (CleanCell(sheet.GetCell(r, 0)) == "구분 섹션" && CleanCell(sheet.GetCell(r, 1)) == "행번호")
                {
                    unifiedHeader = r;
                    break;
                }
            }

            if (unifiedHeader >= 0)
            {
                for (int r = unifiedHeader + 1; r < sheet.RowCount; r++)
                {
                    string sec = CleanCell(sheet.GetCell(r, 0));
                    int rowNum;
                    int.TryParse(CleanCell(sheet.GetCell(r, 1)), out rowNum);
                    var cells = new List<string>();
                    for (int c = 2; c < sheet.ColCount; c++)
                        cells.Add(CleanCell(sheet.GetCell(r, c)));
                    while (cells.Count > 0 && string.IsNullOrWhiteSpace(cells[cells.Count - 1]))
                        cells.RemoveAt(cells.Count - 1);
                    if (string.IsNullOrWhiteSpace(sec) && cells.Count == 0) continue;

                    list.Add(new MdfBlockRow
                    {
                        section = sec,
                        rowIndex = rowNum > 0 ? rowNum : (r + 1),
                        cells = cells
                    });
                }
                return;
            }

            string currentSection = "PRI 110 Block";
            for (int r = 0; r < sheet.RowCount; r++)
            {
                if (sheet.IsRowCompletelyEmpty(r, 26)) continue;

                string c0 = CleanCell(sheet.GetCell(r, 0));
                if ((c0.Contains("Block") || c0.Contains("MDF")) && !c0.Contains("순서"))
                    currentSection = c0;

                var cells = new List<string>();
                int lastNonEmpty = -1;
                for (int c = 0; c < Math.Min(26, Math.Max(sheet.ColCount, 25)); c++)
                {
                    string val = CleanCell(sheet.GetCell(r, c));
                    cells.Add(val);
                    if (!string.IsNullOrWhiteSpace(val))
                        lastNonEmpty = c;
                }

                if (lastNonEmpty < 0) continue;
                while (cells.Count > lastNonEmpty + 1)
                    cells.RemoveAt(cells.Count - 1);

                list.Add(new MdfBlockRow
                {
                    section = currentSection,
                    rowIndex = r + 1,
                    cells = cells
                });
            }
        }

        private static void ReconcileWithCmCustomers(
            DialPlanDatabase db,
            IList<CustomerInfo> cmCustomers,
            out int promotedCount,
            out int addedFromCmCount)
        {
            promotedCount = 0;
            addedFromCmCount = 0;
            var cmByName = cmCustomers
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.name))
                .ToDictionary(c => c.name, c => c, StringComparer.OrdinalIgnoreCase);

            var linkedActiveCmNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Pass 1: Link existing active and test centers first
            foreach (var center in db.centers.Where(c => c.status != DialPlanStatus.Closed))
            {
                var matched = MatchCmCustomer(center, cmCustomers, cmByName);
                if (matched != null)
                {
                    center.cmCustomerName = matched.name;
                    center.useVQueue = matched.useVQueue == true;
                    center.useIvr = matched.useIvr == true;
                    if (string.IsNullOrWhiteSpace(center.location) && !string.IsNullOrWhiteSpace(matched.floor))
                        center.location = matched.floor;
                    if (center.status == DialPlanStatus.Active)
                        linkedActiveCmNames.Add(matched.name);
                }
            }

            // Pass 2: Inspect closed centers (종료센터) - promote reopened ones not yet claimed by active sheet
            // or whose active Entity/PG in cm_customers.json is not yet covered by another active Dial-Plan center
            var mergedToRemove = new List<DialPlanCenter>();
            foreach (var center in db.centers.Where(c => c.status == DialPlanStatus.Closed).ToList())
            {
                var matched = MatchCmCustomer(center, cmCustomers, cmByName);
                if (matched == null) continue;

                center.cmCustomerName = matched.name;
                bool alreadyClaimed = linkedActiveCmNames.Contains(matched.name);
                bool hasUncoveredCmPreset = alreadyClaimed && HasUncoveredCmPreset(center, matched, db.centers);

                if (!alreadyClaimed || hasUncoveredCmPreset)
                {
                    var sameProjectActive = db.centers.FirstOrDefault(ac =>
                        ac != center &&
                        ac.status == DialPlanStatus.Active &&
                        string.Equals(ac.project, center.project, StringComparison.OrdinalIgnoreCase));

                    if (sameProjectActive != null)
                    {
                        sameProjectActive.lines.AddRange(center.lines);
                        mergedToRemove.Add(center);
                    }
                    else
                    {
                        center.status = DialPlanStatus.Active;
                        center.useVQueue = matched.useVQueue == true;
                        center.useIvr = matched.useIvr == true;
                        if (string.IsNullOrWhiteSpace(center.location) && !string.IsNullOrWhiteSpace(matched.floor))
                            center.location = matched.floor;
                        if (string.IsNullOrWhiteSpace(center.notes))
                            center.notes = "종료센터 시트 → Config 운영 대조 후 운영 중으로 복원";
                        else if (!center.notes.Contains("복원"))
                            center.notes += " (Config 운영 확인 → 복원)";
                    }

                    linkedActiveCmNames.Add(matched.name);
                    promotedCount++;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(center.notes))
                        center.notes = "종료 이력 (동일 고객사 '" + matched.name + "' 운영 중)";
                }
            }
            foreach (var rm in mergedToRemove)
                db.centers.Remove(rm);

            // Pass 3: Ensure every active CM customer has a corresponding Dial-Plan center
            foreach (var cust in cmCustomers)
            {
                if (cust == null || string.IsNullOrWhiteSpace(cust.name)) continue;
                if (cust.name.IndexOf("__TEST__", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (linkedActiveCmNames.Contains(cust.name)) continue;

                var newCenter = new DialPlanCenter
                {
                    id = BuildCenterId(cust.name, db.centers),
                    status = DialPlanStatus.Active,
                    location = cust.floor ?? string.Empty,
                    project = cust.name,
                    cmCustomerName = cust.name,
                    useVQueue = cust.useVQueue == true,
                    useIvr = cust.useIvr == true,
                    sourceSheet = "Config",
                    notes = string.IsNullOrWhiteSpace(cust.note) ? "Config 고객사 기준 자동 보충" : cust.note
                };

                if (cust.oxePresets != null && cust.oxePresets.Count > 0)
                {
                    foreach (var p in cust.oxePresets)
                    {
                        if (p == null) continue;
                        newCenter.lines.Add(new DialPlanLine
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

                db.centers.Add(newCenter);
                addedFromCmCount++;
            }
        }

        private static bool HasUncoveredCmPreset(
            DialPlanCenter candidate,
            CustomerInfo matched,
            List<DialPlanCenter> allCenters)
        {
            if (candidate == null || matched == null || matched.oxePresets == null || matched.oxePresets.Count == 0)
                return false;

            var coveredEnts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var coveredPgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ac in allCenters.Where(c => c.status == DialPlanStatus.Active &&
                string.Equals(c.cmCustomerName, matched.name, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var ln in ac.lines)
                {
                    string ent = ExtractLeadingIntToken(ln.entityNo);
                    string pg = ExtractLeadingIntToken(ln.agentPg);
                    if (!string.IsNullOrEmpty(ent)) coveredEnts.Add(ent);
                    if (!string.IsNullOrEmpty(pg)) coveredPgs.Add(pg);
                }
            }

            var cmEnts = new HashSet<string>(
                matched.oxePresets.Select(p => (p != null ? p.entityNo : null) ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
            var cmPgs = new HashSet<string>(
                matched.oxePresets.Select(p => (p != null ? p.agentPg : null) ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var ln in candidate.lines)
            {
                string ent = ExtractLeadingIntToken(ln.entityNo);
                string pg = ExtractLeadingIntToken(ln.agentPg);
                if (!string.IsNullOrEmpty(ent) && cmEnts.Contains(ent) && !coveredEnts.Contains(ent))
                    return true;
                if (!string.IsNullOrEmpty(pg) && cmPgs.Contains(pg) && !coveredPgs.Contains(pg))
                    return true;
            }
            return false;
        }

        private static CustomerInfo MatchCmCustomer(
            DialPlanCenter center,
            IList<CustomerInfo> cmCustomers,
            Dictionary<string, CustomerInfo> cmByName)
        {
            if (center == null || string.IsNullOrWhiteSpace(center.project))
                return null;

            string aliasTarget;
            if (ExcelToCmAliases.TryGetValue(center.project.Trim(), out aliasTarget))
            {
                CustomerInfo aliased;
                if (cmByName.TryGetValue(aliasTarget, out aliased))
                    return aliased;
            }

            CustomerInfo exact;
            if (cmByName.TryGetValue(center.project.Trim(), out exact))
                return exact;

            string normProj = NormalizeMatchKey(center.project);
            foreach (var c in cmCustomers)
            {
                if (c == null || string.IsNullOrWhiteSpace(c.name)) continue;
                if (NormalizeMatchKey(c.name) == normProj)
                    return c;
            }

            // Fallback: exact PG + Entity overlap when there's an unmistakable match
            var centerPgs = new HashSet<string>(
                center.lines.Select(l => (l.agentPg ?? string.Empty).Trim()).Where(x => x.Length >= 4),
                StringComparer.OrdinalIgnoreCase);
            var centerEnts = new HashSet<string>(
                center.lines.Select(l => ExtractLeadingIntToken(l.entityNo)).Where(x => !string.IsNullOrEmpty(x)),
                StringComparer.OrdinalIgnoreCase);

            if (centerPgs.Count > 0 && centerEnts.Count > 0)
            {
                foreach (var c in cmCustomers)
                {
                    if (c == null || c.oxePresets == null) continue;
                    bool pgHit = c.oxePresets.Any(p => p != null && centerPgs.Contains((p.agentPg ?? "").Trim()));
                    bool entHit = c.oxePresets.Any(p => p != null && centerEnts.Contains((p.entityNo ?? "").Trim()));
                    if (pgHit && entHit)
                    {
                        // Avoid binding distinct unrelated LG centers sharing generic shared pools unless name shares substring
                        string nc = NormalizeMatchKey(c.name);
                        if (nc.Contains(normProj) || normProj.Contains(nc) ||
                            (normProj.Length >= 2 && nc.Contains(normProj.Substring(0, Math.Min(3, normProj.Length)))))
                            return c;
                    }
                }
            }

            return null;
        }

        private static void InferCenterFlags(DialPlanDatabase db)
        {
            foreach (var c in db.centers)
            {
                if (c.useIvr) continue;
                bool hasIvr = c.lines.Any(l =>
                {
                    string v = (l.ivrPdsChannel ?? string.Empty).Trim();
                    return !string.IsNullOrWhiteSpace(v) &&
                           v != "-" &&
                           !v.Contains("없음") &&
                           !v.Contains("미사용");
                });
                if (hasIvr)
                    c.useIvr = true;
            }
        }

        public static string ExtractLeadingIntToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var m = Regex.Match(text, @"\d+");
            return m.Success ? m.Value : string.Empty;
        }

        public static string BuildCenterId(string project, IEnumerable<DialPlanCenter> existing)
        {
            string baseId = Regex.Replace((project ?? "center").Trim(), @"[^0-9A-Za-z가-힣]+", "_").Trim('_');
            if (string.IsNullOrWhiteSpace(baseId)) baseId = "center";

            var used = new HashSet<string>(
                existing.Where(c => c != null && !string.IsNullOrWhiteSpace(c.id)).Select(c => c.id),
                StringComparer.OrdinalIgnoreCase);

            string cand = baseId;
            int seq = 2;
            while (used.Contains(cand))
                cand = baseId + "_" + seq++;
            return cand;
        }

        private static string NormalizeMatchKey(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            string t = s.Trim();
            foreach (var suffix in new[] { "고객행복센터", "고객센터", "지원센터", "상담센터", "콜센터", "센터" })
                t = t.Replace(suffix, string.Empty);
            t = Regex.Replace(t, @"[\s_\-\(\)\[\]/·\.]+", string.Empty);
            return t.ToLowerInvariant();
        }

        private static string NormalizeHeader(string h)
        {
            return (h ?? string.Empty).Replace("(", string.Empty).Replace(")", string.Empty).Trim();
        }

        private static string GetCsvMapped(List<string> row, Dictionary<string, int> map, string key, int fallbackIdx)
        {
            int idx = -1;
            if (map != null && map.Count > 0)
            {
                string norm = NormalizeHeader(key);
                if (!map.TryGetValue(norm, out idx))
                {
                    foreach (var kv in map)
                    {
                        if (kv.Key.Equals(norm, StringComparison.OrdinalIgnoreCase) ||
                            kv.Key.Contains(norm))
                        {
                            idx = kv.Value;
                            break;
                        }
                    }
                }
            }
            else
            {
                idx = fallbackIdx;
            }

            if (idx >= 0 && idx < row.Count)
                return row[idx] ?? string.Empty;
            return string.Empty;
        }

        private static int FindRowContaining(ExcelSheetData sheet, int col, string needle)
        {
            for (int r = 0; r < Math.Min(sheet.RowCount, 12); r++)
            {
                if (CleanCell(sheet.GetCell(r, col)).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return r;
            }
            return -1;
        }

        private static bool IsRangeEmpty(ExcelSheetData sheet, int row, int fromCol, int toCol)
        {
            for (int c = fromCol; c <= toCol; c++)
            {
                if (!string.IsNullOrWhiteSpace(sheet.GetRawCell(row, c)))
                    return false;
            }
            return true;
        }

        private static bool IsDialPlanLineCompletelyEmpty(DialPlanLine ln)
        {
            return string.IsNullOrWhiteSpace(ln.subTask) &&
                   string.IsNullOrWhiteSpace(ln.mainNumber) &&
                   string.IsNullOrWhiteSpace(ln.carrier) &&
                   string.IsNullOrWhiteSpace(ln.incomingNumber) &&
                   string.IsNullOrWhiteSpace(ln.inboundRsi) &&
                   string.IsNullOrWhiteSpace(ln.agentTransferRsi) &&
                   string.IsNullOrWhiteSpace(ln.agentPilot) &&
                   string.IsNullOrWhiteSpace(ln.agentPg) &&
                   string.IsNullOrWhiteSpace(ln.phoneExt) &&
                   string.IsNullOrWhiteSpace(ln.loginId) &&
                   string.IsNullOrWhiteSpace(ln.entityNo) &&
                   string.IsNullOrWhiteSpace(ln.testIvrDn) &&
                   string.IsNullOrWhiteSpace(ln.note);
        }

        private static bool ParseBoolLoose(string v)
        {
            string s = (v ?? string.Empty).Trim().ToLowerInvariant();
            return s == "y" || s == "yes" || s == "true" || s == "1" || s == "사용";
        }

        private static string CleanSingleLine(string s)
        {
            string c = CleanCell(s);
            if (string.IsNullOrEmpty(c)) return string.Empty;
            return Regex.Replace(c.Replace('\n', ' '), @"\s{2,}", " ").Trim();
        }

        private static string CleanCell(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var lines = s.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0);
            return string.Join("\n", lines);
        }

        private static string S(string s)
        {
            return s ?? string.Empty;
        }
    }
}
