# CLAUDE.md — 카피바라 귤 쌓기 (MandarinCapy)

채용박람회 AI 게임 제작 이벤트 출품용 짧은 Windows 랭킹 게임.
모델과 사운드는 다른 프로젝트에서도 다시 쓸 수 있게 정리한다.

## 마감
- 2026-11-10(화) 17:00 제출 마감, 11-10 오전 제출 목표

## 작업 규칙
- 모든 답변은 한국어 존댓말로.
- 추측은 적지 않는다. 확인되지 않은 내용은 "확인 필요"로 표시한다.
- 요청 전에는 백업 파일을 만들지 않는다.
- 최종 지시 전에는 기획서 등 문서를 만들지 않는다.
- 버전·환경, 확정된 설계 결정, 시행착오(증상·원인·해결), 미해결 과제는 이 파일에 기록한다.

## 폴더 구조 (Git 저장소 루트 = D:\Unity\MandarinCapy)
- `00Project/MandarinCapy/` — Unity 프로젝트 루트
- `01Build/` — Windows 빌드 출력 (Git 제외)
- `02Resource/` — 모델·사운드 등 원본 리소스 (재사용 대상)

## 버전 · 환경 (확인됨, 2026-11-06)
- Unity 6000.3.17f1
- 렌더 파이프라인: URP 17.3.0 (RP 에셋 `PC_RPAsset`)
- 입력: Input System 1.19.0 전용 (Player Settings activeInputHandler = 1). 구 `UnityEngine.Input` API는 쓰지 않는다.
- 에셋 직렬화 ForceText, Version Control 모드 Visible Meta Files
- Unity MCP: `com.coplaydev.unity-mcp` (git `#main`, 정확한 커밋은 `Packages/packages-lock.json` 기준)
- Git 2.51.0.windows.1, Git LFS 3.7.0
- 원격: https://github.com/woyeonggwang/MandarinCapy.git (브랜치 `main`)
- 개발 PC: Windows 데스크톱

## 확정된 설계 결정
- 2D로 진행 (사용자 결정, 2026-11-06).
- Orca는 이 프로젝트에서 사용하지 않음 (사용자 결정).
- 외부 서버·계정·온라인 API 사용 안 함. 랭킹은 PC에 JSON으로 저장.
- 바이너리(모델, 사운드, 폰트, 이미지)는 Git LFS로 관리 (`.gitattributes`).
- Git 저장소 루트를 Unity 프로젝트 상위 폴더로 둔 이유: `02Resource`의 원본 리소스까지 함께 버전 관리하기 위함.

## 공지 필수 조건 (체크리스트)
- [ ] 랭킹: 점수 순위 표시, 로컬 저장, 종료 후에도 유지
- [ ] 닉네임 입력 (최대 8자, 한글·영문)
- [ ] 인터넷 없이 Windows에서 실행
- [ ] 한글 폰트 빌드 포함
- [ ] Ctrl+Shift+R 랭킹 초기화 (부스 운영자용)

## 미해결 과제 / 확인 필요
- 그래픽 방식(스프라이트를 무엇으로 만들지): 확인 필요 (11/8 전 결정)
- Claude Code CLI, Codex 사용 여부: 확인 필요 (첫 프로토타입에는 불필요)
- 해상도·전체화면 설정: 현재 기본값(1024x768, fullscreenMode 1) 그대로. 부스 환경에 맞춰 결정 필요
- Unity MCP 패키지가 `#main` 브랜치를 따라가므로, 제출 전 갱신하지 않도록 주의 필요
