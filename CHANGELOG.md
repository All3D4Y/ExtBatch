# 변경 이력 (CHANGELOG)

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
