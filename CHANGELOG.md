# 변경 이력 (CHANGELOG)

## v1.0.0 (2026-11-24)
- **검색 문구 통일**: PBX·CM `중복 체크` 버튼, 결과 다이얼로그, 로그·토스트 문구를 `내선/CTI ID 검색`으로 통일 (`Employee ID 중복체크`는 Person 전용 검증 유지)
- **버전 관리 기반 구축**:
  - `src\Properties\AssemblyInfo.cs` (`AssemblyVersion` / `AssemblyFileVersion` `1.0.0.0`) 추가 및 메인 헤더 버전 배지 연동
  - `.gitignore` 및 `CHANGELOG.md` 구성
- **이전 안정화 포함**:
  - `cm_person_engine.ps1` Person 생성 시 `Agent Logins` Browse 경로 이동 안정화 (`ClipCursor` + `Static/1153` 경로 검증 + 상위 복구)
  - CM 일괄 삭제 시 센터 지정과 무관하게 전역 검색(`Find Configuration Object`)으로 삭제 지원
  - 객체당 1줄 일일 감사 로그(`logs\extbatch_YYYYMMDD.log`) 및 Person 체크 행 전용 미리보기/자동 분배 적용
