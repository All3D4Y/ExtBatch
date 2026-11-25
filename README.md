# ExtBatch 사용 매뉴얼

Alcatel OXE(LightGate 터미널) 및 Genesys Configuration Manager(`sce.exe`)의 **내선(`3XXXX`) · CTI(`7XXXX`) 일괄 조회/생성/삭제** 도구입니다.

---

## 1. 실행 및 주요 파일

- **실행 방법**: `ExtBatch.exe` 또는 `run_extbatch.cmd` 실행
- **필수 환경**: `.NET Framework 4.8` · `Edge WebView2 Runtime` · `Configuration Manager (sce.exe)`

| 파일명 | 설명 |
| --- | --- |
| `ExtBatch.exe` | 메인 GUI 실행 파일 |
| `cm_customers.json` | 센터별 CM 폴더 경로 및 OXE Preset·선번장(`Entity NO`, `Agent PG`, `기준 Profile`, 대역) 매핑 DB |
| `dialplan_db.json` | 전체 선번장 통합 DB (`Dial-Plan`, `PRI현황`, `Entity`, `IP IVR`, `GPA`, `D.V.G 멘트`, `PBX IP`, `MDF`) |
| `cm_batch_engine.ps1` / `cm_person_engine.ps1` / `cm_center_engine.ps1` / `cm_win32_lib.ps1` | CM Win32 제어 및 내선·CTI / Person / 센터 폴더·Place Group 자동화 엔진 |

---

## 2. 초기 설정 (상단 `⚙` 버튼)

- 최초 실행 시 `%LocalAppData%\ExtBatch\settings.json`이 자동 생성됩니다.
  - 기본 테마: **Antigravity Light**
  - CM / LightGate 계정(ID·PW): **공란**
- 저장된 비밀번호는 **현재 Windows 사용자 계정 기준 DPAPI 암호화**로 로컬에만 보관됩니다.
- 설정된 CM 경로에 `sce.exe`가 있으면 앱 실행 시 자동 연결을 시도하며, LightGate 계정을 입력해 두면 자동 로그인 후 지정 서버(기본 `9. PBX_A`) 터미널까지 자동으로 엽니다.

---

## 3. 기능 요약

### 상단 타이틀 바

| 컨트롤 | 설명 |
| --- | --- |
| `View Mode` | `Split`(좌측 작업 + 우측 터미널) / `Workspace` / `LightGate` 화면 전환 |
| `CM 연결됨` / `CM 미연결` | 클릭하여 CM 세션 연결 또는 해제 |
| `⚙` | CM·LightGate·OXE Node·테마·대량 입력 경고 기준 설정 (`기본값 복원` 지원) |

### 1) 센터
- 드롭다운에서 센터를 선택하거나 우측 검색창(센터명/층)으로 필터링합니다. (초기 기본값은 공란)
- `CM 경로 ▾` 버튼으로 해당 센터의 CM 폴더 경로 확인 및 **선택 CM 구성(`VQueue 사용`, `IVR 사용 (Place Group)`)**을 `상태 조회` / `적용하기`로 안전하게 생성·변경할 수 있습니다.
> [!NOTE]
> CM 및 PBX **내선/CTI ID 검색**과 CM 일괄 삭제는 센터를 선택하지 않아도 실행할 수 있습니다.

### 2) 내선 / 로그인ID 입력
- **형식**: 내선 `3XXXX` (5자리) · CTI `7XXXX` (5자리)
- **구분자 및 범위**: 공백 · 줄바꿈 · 쉼표(`,`)로 구분, 연속 번호는 `~` 사용 (예: `31001~31005`)
- **오류 표시**: 잘못 입력된 토큰 아래에만 **빨간 물결 밑줄**이 표시되며, 우측 `오류 N` 배지 · 하단 안내 문구 · 마우스 오버 툴팁으로 사유를 바로 확인할 수 있습니다.
  *(정상 입력된 번호 건수는 `N건`으로 별도 정상 집계됩니다.)*

### 3) PBX (OXE)

| 버튼 | 동작 |
| --- | --- |
| `내선/CTI ID 검색` | 활성 LightGate 터미널에서 read-only 조회(`Subscriber` / `ACD2_Attaching_List`) 후 결과창 표시 *(사용 중인 번호는 `(센터, Entity No, Agent PG)` 표시, DB 미등록 시 `unknown`)* |
| `생성 스크립트 전송/복사` | `mgr -nodico -x / -X` 스크립트를 활성 터미널에 붙여넣기 *(터미널 미활성 시 클립보드 복사)* |
| `삭제 스크립트 전송/복사` | `'삭제한다'` 입력 확인 후 `Subscriber` 삭제 스크립트 전송 또는 복사 |
| `.sh 저장` | UNIX LF 포맷의 `.sh` 파일로 저장 |

> [!TIP]
> 센터 선택 시 `Preset` / `Entity NO` / `Agent PG` / `기준 Profile`이 자동 채워지며, 필요 시 수동으로 변경해 사용할 수 있습니다.

### 4) CM
- 입력칸 값 유무에 따라 4종 체크박스(`Agent DN`, `Place(+Shortcut)`, `Agent Login`, `ACD Position`)가 자동 활성/비활성됩니다.

| 버튼 | 동작 |
| --- | --- |
| `내선/CTI ID 검색` | 센터·프리셋과 무관하게 `Find Configuration Object` 전역 검색 후 결과창 표시 *(사용 중인 번호는 4종 모두 실제 CM 경로 표시)* |
| `CM 일괄 생성` | 선택된 센터 폴더 기준으로 사전 검색 통과 후 일괄 생성 |
| `CM 일괄 삭제` | `'삭제한다'` 입력 확인 후 센터 지정과 무관하게 CM 전역 검색(`Find Configuration Object`)으로 체크된 항목 일괄 삭제 |

> [!IMPORTANT]
> CM 일괄 생성/삭제는 Win32 제어 및 `Place` 연결 시 Windows 클립보드(Copy/Paste)를 사용하므로, 작업 중에는 **클립보드 복사(`Ctrl+C`)와 CM 창 조작을 피해 주세요.**

### 5) Log & 우측 LightGate 툴바
- **Log**: 연결 및 실행 이력 출력 (`Save`: `.txt` 저장 / `Clear`: 로그 비우기)
  - 생성·삭제 실행 이력은 프로그램 폴더 내 `logs\extbatch_YYYYMMDD.log`로 자동 저장됩니다.
- **LightGate 툴바**:
  - `● 터미널 활성` / `● 터미널 미연결` : 클릭으로 SSH 터미널 연결·종료 토글
  - `↻` (새로고침 아이콘) : LightGate 웹페이지 자체 새로고침
  - `터미널 창만 보기` : LightGate 좌측 메뉴 숨김 토글
  - `터미널 재실행` : 열려 있는 터미널만 종료 후 서버 재연결

### 6) 사이드바 `'선번장 · 센터'` 탭 & `'+ 센터 추가'` 팝업 (`v1.2.0`)
- 좌측 사이드바 **`선번장 · 센터`** 메뉴에서 엑셀 선번장 전체 시트(`Dial-Plan`, `PRI · Entity`, `IP IVR · D.V.G 멘트`, `PBX 보드 · 110 Block`)를 조회·편집할 수 있습니다.
- **A안 마스터-디테일**: 상단 표에서 센터당 1행 요약을 빠르게 스캔하고, 행 더블클릭 또는 `▾ 상세 펼치기`로 21컬럼 세부 선번을 인라인 편집한 뒤 `변경 저장 (DB + CM 프리셋)`으로 즉시 반영합니다.
- **센터 원클릭 연동**: **`이 센터로 개통 →`** 버튼으로 선택한 센터/프리셋을 `내선 · CTI` 탭에 바로 바인딩할 수 있으며, 우측 상단 **`+ 센터 추가`** 버튼이 모달 팝업으로 신규 센터 CM 폴더 생성과 선번장 등록을 동시에 처리합니다.
- **안전 종료 vs 완전 삭제**: `업무 종료로 이동 ↔ 운영 중으로 복구`로 비파괴 보관하며, **`완전 삭제 (JSON+CM)`**는 `'삭제한다'` 입력 확인 후 CM 센터 폴더·Place Group·Script(공용 위치 폴더 보존)와 JSON 데이터를 함께 삭제합니다.
- **`빈 번호 찾기` & 가져오기/내보내기**: 미사용 `Entity NO`, `6XX` 대역, `IP IVR DN`, `D.V.G` 슬롯을 즉시 조회하고 `.xlsx`(9시트) / `.csv` / `.json` 가져오기·내보내기를 지원합니다.

---

## 4. 버전 관리 (Git)

- **버전 정의**: `src\Properties\AssemblyInfo.cs`의 `AssemblyVersion` / `AssemblyFileVersion` 값을 기준으로 앱 상단 헤더(`vX.Y.Z`)와 `ExtBatch.exe` 파일 버전이 자동 동기화됩니다.
- **변경 이력**: 릴리스별 변경 내용은 `CHANGELOG.md`에 기록합니다.
- **추적 대상 (`.gitignore`)**: `src/`, `.ps1` 엔진, `cm_customers.json`, `dialplan_db.json`, `run_extbatch.cmd`, `ExtBatch.exe.config`, WebView2 참조 DLL은 Git에 포함하고, `src/bin/`, `src/obj/`, `logs/`, `/ExtBatch.exe`, `*.pdb`는 제외합니다.
- **로컬 Git 초기화 / 커밋 예시** *(Git for Windows 설치 후 `C:\GCTI\ExtBatch`에서 실행)*:

```powershell
git init
git add .
git commit -m "v1.0.0 초기 커밋"
git tag v1.0.0
```

---

## 5. 참고
- `cm_customers.json`을 수정한 경우 앱을 재실행하면 센터 목록과 Preset이 반영됩니다.
