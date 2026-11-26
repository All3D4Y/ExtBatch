# 변경 이력 (CHANGELOG)

## v1.2.1 (2026-11-25)
- **선번장 행 인라인 펼치기 및 강조 테두리 UX 개선 (`DialPlanTabView`)**:
  - 기존 하단 분리 패널을 제거하고, `Dial-Plan (센터별)` 테이블 각 행 맨 끝 **`▾ 상세` / `▴ 접기`** 버튼(또는 행 더블클릭)을 누르면 **해당 행 바로 아래로(`RowDetailsTemplate`)** 센터 설정 및 21컬럼 세부 선번 편집기가 펼쳐지도록 개선 (`RowDetailsVisibilityMode="Visible"` + 행별 지연 `ContentTemplate` 바인딩)
  - **선택 하이라이트 · 가시성 테두리 강조**: 블러 처리 없이 모든 행의 텍스트를 선명하게 유지하면서, 펼쳐진 센터 행과 상세 카드 전체를 선택 하이라이트 배경(`ChipBgBrush`) 및 2px 강조 테두리(`PrimaryBrush`)로 감싸 다른 센터와 확실히 구분되도록 적용 (`Esc` 또는 `▴ 접기`로 닫힘)
  - **연속 동일 셀 세로 병합 보기 (`동일 셀 병합`)**: 행을 펼쳤을 때 세부 선번 테이블에서 위아래 연속으로 같은 값을 갖는 셀의 중복 텍스트 및 가로 구분선을 감춰 엑셀 선번장과 동일하게 합쳐 보이도록 표시 (셀 더블클릭 편집 시 원본 값 전체 편집, 상단 `동일 셀 병합` 체크박스로 켜기/끄기 전환 가능)
  - **숫자 중심 요약 · 전체 가운데 정렬 · `상태` 컬럼 정리**: 상단 필터로 구분되는 마스터 표의 `상태` 컬럼을 제거하고, `대표번호`, `Entity NO`, `Agent PG`, `Phone 내선`, `Login-ID` 요약 컬럼을 숫자 위주로 표시하며, 선번장 모든 테이블의 헤더와 셀 내용을 가로·세로 정중앙 정렬 적용
  - **`AP` 연결 컬럼 (`미사용` / `바로가기`)**: `CM 연동`과 `상세` 사이에 `AP` 컬럼을 두어 URL 미입력 센터는 `미사용`으로 표시하고, 클릭 시 인라인 입력 후 URL이 저장되면 밑줄 강조된 **`바로가기`** 링크 버튼으로 전환되어 클릭으로 브라우저 연결 지원 (우클릭 메뉴로 수정·초기화 가능)
  - **`업무 종료로 이동` 경고 스타일(`WarnBtn`) 적용**: 오작동 방지를 위해 주황/호박색 경고 테두리·배경 스타일 적용 (`업무 종료` 상태에서는 청록색 `운영 중으로 복구`로 전환)

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
