# 변경 이력 (CHANGELOG)

## v2.0.0 (2026-11-26)
- **Win32 `sce.exe` UI Automation 제거 및 Genesys Platform SDK(PSDK) 직접 연동 전환**:
  - 기존 PowerShell Win32 제어 스크립트(`cm_batch_engine.ps1`, `cm_person_engine.ps1`, `cm_center_engine.ps1`, `cm_win32_lib.ps1`) 완전 제거.
  - `backend/` 내 Java 8 + Spring Boot 2.7.5 + Genesys Platform SDK 9.0.001.07 로컬 REST 백엔드(`127.0.0.1:18080`, `euc-kr` 인코딩 + WarmStandby 이중화) 구축.
  - WPF 앱(`BackendProcessService`, `CmWorkerService`)이 백엔드를 자동 기동/헬스체크하고 종료 시 Job Object + `/api/cm/disconnect`로 안전 정리.
- **설정 구조 개편 및 독립 저장**:
  - 설정 저장 경로를 `%LocalAppData%\ExtBatch_v2\settings.json`으로 분리(`ExtBatch` v1.2.1에서 LightGate/OXE/테마 설정 최초 1회 선택 이관).
  - `SettingsWindow`에서 Config Server Primary/Backup Host·Port, Client Name, 계정, 로컬 백엔드 Port 설정 지원.
- **UI 전면 개편 및 `CM` → `Config` 명칭 통일**:
  - 상단 타이틀 바의 수동 `CM 연결/해제` 버튼 제거(앱 실행/설정 변경 시 백그라운드 Config Server 자동 연결 유지), `SettingsWindow` 및 전체 UI/로그/백엔드 표시 문자열을 `Config` 기준으로 통일(`Config 아이디`/`비밀번호`, `Config 경로 ▾`, `내선 · CTI` → `Config` 블록 및 `Object 생성`/`Object 삭제` 버튼).
- **신규 `'통합 조회'` 탭 추가 (`UnifiedLookupTabView`, `LookupService`)**:
  - Config 실서버(`DN`/`Place`/`Agent Login`/`ACD Position`/`Person`/`RSI`/`VQueue`/`Group`/`Script`)와 로컬 `dialplan_db.json`(`센터`/`세부라인`/`Entity`/`IP IVR`/`PRI`/`D.V.G`) 동시 검색(번호/범위`~`/이름/사번/폴더), 우측 연관 관계·선번장 교차 상세 패널 및 `내선·CTI`/`Person 관리` 바로가기 지원(`출처` 배지 `Config` / `선번장`).
  - **센터 정합성 감사**: 전체 또는 단일 센터 대상 `Agent DN ↔ Place`, `Agent Login ↔ ACD Position ↔ Person`, 누락 폴더, 빈 Place, 복수 연결 등을 자동 탐지하고 CSV로 내보내기 가능.
- **신규 `'스냅샷 · 복구'` 탭 추가 (`SnapshotDiffTabView`, `SnapshotService`, `SnapshotRestoreSupport`)**:
  - 전체 Config 또는 고객사 단위 구성 스냅샷(`snapshots\snap_*.json`) 저장·관리, 두 스냅샷 간 또는 기준 스냅샷 ↔ 실시간 `LIVE` 변경 내역 비교(`+추가`/`-삭제`/`~변경`, `dbid` 자동 증가 차이 제외, 속성별 변경 상세) 및 비교 CSV/Markdown 리포트 저장 지원.
  - **실서버 객체 복구(Restore)**: 기준 스냅샷 ↔ `LIVE` 비교 결과에서 복구할 항목을 체크해 **`↩ 선택 항목 복구`** 실행 가능(의존성 순서 기반 삭제 객체 재생성 · 변경 속성/연결 복원 · 선택적 `신규(+추가) 객체 삭제 포함` 역순 삭제 + 감사 로그 자동 기록).

## v1.2.1 (2026-11-25)
- **선번장 행 인라인 펼치기 및 강조 테두리 UX 개선 (`DialPlanTabView`)**:
  - 기존 하단 분리 패널을 제거하고, `Dial-Plan (센터별)` 테이블 각 행 맨 끝 **`▾ 상세` / `▴ 접기`** 버튼(또는 행 더블클릭)을 누르면 **해당 행 바로 아래로(`RowDetailsTemplate`)** 센터 설정 및 21컬럼 세부 선번 편집기가 펼쳐지도록 개선 (`RowDetailsVisibilityMode="Visible"` + 행별 지연 `ContentTemplate` 바인딩)
  - **선택 하이라이트 · 가시성 테두리 강조**: 블러 처리 없이 모든 행의 텍스트를 선명하게 유지하면서, 펼쳐진 센터 행과 상세 카드 전체를 선택 하이라이트 배경(`ChipBgBrush`) 및 2px 강조 테두리(`PrimaryBrush`)로 감싸 다른 센터와 확실히 구분되도록 적용 (`Esc` 또는 `▴ 접기`로 닫힘)
  - **연속 동일 셀 세로 병합 보기 (`동일 셀 병합`)**: 행을 펼쳤을 때 세부 선번 테이블에서 위아래 연속으로 같은 값을 갖는 셀의 중복 텍스트 및 가로 구분선을 감춰 엑셀 선번장과 동일하게 합쳐 보이도록 표시 (셀 더블클릭 편집 시 원본 값 전체 편집, 상단 `동일 셀 병합` 체크박스로 켜기/끄기 전환 가능)
  - **숫자 중심 요약 · 전체 가운데 정렬 · 소형 `상태` 배지 복원**: 마스터 표 첫 열에 `CM 연결`과 유사한 소형 점+알약 배지(`운영 중` 녹색 · `테스트용` 주황 · `업무 종료` 적색)를 적용해 전체 검색 시에도 상태를 한눈에 구분할 수 있으며, `대표번호`·`Entity NO`·`Agent PG`·`Phone 내선`·`Login-ID` 숫자 요약 및 모든 선번장 테이블 헤더/셀 정중앙 정렬을 지원
  - **표 가시성 · 시선 계층 전면 개선 (`ThemeService`, `DialPlanTabView`)**:
    - 전 테마용 `TableAltRowBrush`·`TableGridLineBrush` 도입으로 Light/Dark 모든 테마에서 짝수·홀수 행 줄무늬 명암과 연한 셀 가로·세로 구분선·헤더 하단 2px 강조선 제공
    - `프로젝트 (센터명)` `SemiBold` 강조, 숫자·대역 컬럼 고정폭(`Consolas`) 열 맞춤, 공백(`-`)·`미연동` 흐림 처리, `CM 연동` 청록 점 강조 및 `업무 종료` 행 텍스트 톤 다운 적용
    - 여유 있는 행 높이(`35px`), 창 폭에 맞는 비례(`*`) 열 너비, 마스터 표 좌측 3열(`상태·위치·프로젝트`) & 상세 표 좌측 2열(`세부업무·대표번호`) 틀 고정, 상세 22컬럼 역할군별(`인입·IVR` / `상담·Agent` / `OXE·기타` / `구버전`) 헤더 상단 컬러 바 구분 추가
  - **검색 속도 · 렌더링 최적화**: 센터 검색 인덱스(`MatchesSearchQuery`)·요약 속성 캐싱, 정규식 컴파일, 입력 디바운스(`110ms` / `Enter` 즉시 검색 · `Esc` 초기화), `ListCollectionView` 필터 재사용, 지연 `TxtApInline` 및 미펼침 시 `RowDetailsVisibilityMode="Collapsed"` 유지로 한글 입력 지연 대폭 개선
  - **`AP` 연결 컬럼 (`미사용` / `바로가기`)**: `CM 연동`과 `상세` 사이에 `AP` 컬럼을 두어 URL 미입력 센터는 `미사용`으로 표시하고, 클릭 시 인라인 입력 후 URL이 저장되면 밑줄 강조된 **`바로가기`** 링크 버튼으로 전환되어 클릭으로 브라우저 연결 지원 (우클릭 메뉴로 수정·초기화 가능)
  - **`업무 종료로 이동` 경고 스타일(`WarnBtn`) 적용**: 오작동 방지를 위해 주황/호박색 경고 테두리·배경 스타일 적용 (`업무 종료` 상태에서는 청록색 `운영 중으로 복구`로 전환)
  - **상세 `+ 라인 추가` / `선택 라인 삭제` 편집 트랜잭션 · 예외 루프 버그 수정 (`App`, `DialPlanTabView`)**: 세부 선번 셀 편집 중(`EditItem` 트랜잭션 활성 상태)에 `+ 라인 추가`·`선택 라인 삭제`·필터 전환 시 `CommitEdit`/`CancelEdit` 선행 종료 및 `CollectionViewSource.GetDefaultView().Refresh()` 전환으로 `'DeferRefresh'` 예외 방지, `DispatcherUnhandledException` 재진입 가드로 모달 오류창 연속 팝업 차단
  - **선번장 조회/편집 모드 분리 · Undo/Redo(`Ctrl+Z`/`Ctrl+Y`) · 일괄 저장 · 자동 백업 (`DialPlanTabView`, `DialPlanEditCommands`, `DialPlanRepository`, `PopupDialogs`, `MainWindow`)**:
    - 탭 최상단 중앙 **`조회 모드` / `편집 모드`** 전환 바 추가: 기본은 읽기 전용 **`조회 모드 (잠금)`**으로 열어 4개 서브탭 8개 표(`Dial-Plan`, `PRI`, `Entity`, `IP IVR`, `D.V.G`, `PBX`, `GPA`, `110 Block MDF`)와 `AP` 주소 및 수정/삭제 버튼 오동작 차단
    - **커맨드 패턴 Undo/Redo (`DialPlanCommandHistory`, 최대 150단계)**: `Ctrl+Z`/`Ctrl+Y` 단축키 및 상단 **`↶ 취소` · `↷ 재실행`** 버튼으로 셀 편집·헤더(위치/센터명/상태/메모/VQueue/IVR)·`AP` 주소 수정/초기화·행 추가/삭제(원래 위치 인덱스 보존)를 4개 서브탭 전체에 걸쳐 즉시 되돌리고 다시 실행 가능
    - **스냅샷 기반 변경 추적 & 안전 이탈 가드 (`PromptSaveDiscardCancel`)**: 결정론적 JSON 스냅샷(`SerializeSnapshot`)으로 미저장 변경 건수를 추적하며 `AP` 주소 편집도 통합 저장에 포함, **`변경사항 되돌리기`**로 마지막 저장 시점 즉시 원복, **`변경사항 저장 (Ctrl+S)`** 또는 저장 없이 `조회 모드` 전환·`+ 센터 추가`·`가져오기`·탭/앱 종료 시 저장/저장 안 함/취소 확인 제공
    - **자동 백업 & `백업 복원` (`backups\dialplan_db_yyyyMMdd_HHmmss.json`)**: `dialplan_db.json` 저장 직전 최근 **20개**까지 자동 백업을 회전 보관하고(`.gitignore`에 `backups/` 추가), 상단 **`백업 복원`** 창(`PickDialPlanBackup`)에서 타임스탬프·용량 확인 후 원클릭 복원 지원

## v1.2.0 (2026-11-25)
- **사이드바 `'선번장 · 센터'` 통합 관리 개편 (`DialPlanTabView`, `CenterAddWindow`, `dialplan_db.json`)**:
  - 기존 `'센터 추가'` 단독 탭을 통합 선번장·센터 관리 탭으로 개편하고, 신규 센터 생성 화면은 **`+ 센터 추가`** 모달 팝업(`CenterAddWindow`)으로 통합
  - **엑셀 선번장 전량 이관 (`dialplan_db.json`)**: `Dial-Plan` / `Dial-Plan #2` / `PRI현황` / `IP IVR` / `MDF` / `GPA시간` / `멘트-700~1800` / `IP정보` / `종료센터` / `Sheet2(Entity)` 13개 시트 전량 반영 (`109`개 센터: `운영 중 97`, `테스트용 3`, `업무 종료 9`; 종료시트 재운영 복원 `26`건, CM 보충 `14`건)
  - **4개 서브탭**: `Dial-Plan (센터별)` | `PRI · Entity 마스터` | `IP IVR · D.V.G 멘트` | `PBX 보드 · 110 Block`
  - **A안 마스터-디테일 편집 · 연동**: 상단 1행 요약 + 하단 접이식 21컬럼 상세 선번 인라인 편집, 변경 저장 시 `cm_customers.json` OXE 프리셋 자동 동기화 및 **`이 센터로 개통 →`** 원클릭 탭 전환 지원
  - **`빈 번호 찾기` (`EmptyNumberFinderWindow`)**: 미사용 Entity NO(`10~150`), 6XX 센터 대역, IP IVR DN(`21001~23100`), 대역별 D.V.G 멘트 슬롯 즉시 조회
  - **안전 종료 · 완전 삭제**: `업무 종료로 이동 ↔ 운영 중으로 복구` 비파괴 보관 지원 및 `'삭제한다'` 확인 기반 **`완전 삭제 (JSON+CM)`** 지원(공용 위치 폴더 보존, 센터 폴더·Place Group·Script만 안전 삭제)
  - **순수 C# `.xlsx` / `.csv` / `.json` 가져오기·내보내기 (`ExcelWorkbookCodec`)**: 외부 NuGet 없이 9시트 OOXML 워크북 양방향 라운드트립 지원

## v1.1.0 (2026-11-25)
- **사이드바 신규 `'센터 추가'` 탭 추가 (`CenterAddTabView`, `cm_center_engine.ps1`)**:
  - 위치(기존 `cm_customers.json` 위치 목록 드롭다운 + `(위치 없음)`) 및 센터명(필수) 입력, 실시간 생성 대상 CM 경로 미리보기, CM 사전 점검 및 일괄 생성·저장 지원
  - 위치 포함 6곳(`Persons`, `Places\AGENT`, `Agent Logins`, `DNs\ACDPosition`, `DNs\AGENT_DN`, `DNs\RSI`) 및 위치 미포함 3곳(`Agent Groups`, `DN Groups`, `Scripts\유세스파트너스`) 폴더 자동 생성 *(누락된 상위 위치 폴더 자동 보완 포함)*
  - `VQueue 사용` 체크 시 `Switches\TServerAlcatel\DNs\VQueue\<센터명>` 폴더, `IVR 사용` 체크 시 `Place Groups\<센터명>` `Place Group` 오브젝트 생성
  - **OXE 프리셋 · 선번장 다건 등록(선택 입력 / 공란 허용)**: `Entity No`, `Entity Name`, `Agent PG`, `기준 내선/CTI`, `내선/CTI 대역`, `비고`를 여러 행 추가 가능하며 생성 시 `cm_customers.json`에 즉시 저장되어 `내선 · CTI` 및 `Person 관리` 탭 콤보박스에 실시간 반영
- **기존 센터 `VQueue` / `IVR (Place Group)` 온디맨드 적용 (`내선 · CTI` → `CM 경로 ▾`)**:
  - `CM 경로 ▾` 펼침 영역 내 `VQueue 사용`, `IVR 사용 (Place Group)` 체크박스 및 `상태 조회` / `적용하기` 버튼 추가
  - 체크박스 변경만으로는 CM을 수정하지 않고 `적용하기` 클릭 시 실제 CM 존재 여부를 먼저 조회한 뒤 확인 팝업을 거쳐 안전하게 생성/삭제 반영

## v1.0.0 (2026-11-24)
- **검색 문구 통일**: PBX·CM `중복 체크` 버튼, 결과 다이얼로그, 로그·토스트 문구를 `내선/CTI ID 검색`으로 통일 (`Employee ID 중복체크`는 Person 전용 검증 유지)
- **버전 관리 기반 구축**:
  - `src\Properties\AssemblyInfo.cs` (`AssemblyVersion` / `AssemblyFileVersion` `1.0.0.0`) 추가 및 메인 헤더 버전 배지 연동
  - `.gitignore` 및 `CHANGELOG.md` 구성
- **이전 안정화 포함**:
  - `cm_person_engine.ps1` Person 생성 시 `Agent Logins` Browse 경로 이동 안정화 (`ClipCursor` + `Static/1153` 경로 검증 + 상위 복구)
  - CM 일괄 삭제 시 센터 지정과 무관하게 전역 검색(`Find Configuration Object`)으로 삭제 지원
  - 객체당 1줄 일일 감사 로그(`logs\extbatch_YYYYMMDD.log`) 및 Person 체크 행 전용 미리보기/자동 분배 적용
