# 변경 이력 (CHANGELOG)

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
