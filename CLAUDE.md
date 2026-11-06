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

- 탑 기울기는 물리 엔진이 아니라 스크립트로 계산 (무게중심 x 평균 × tiltPerUnitCom × (1 + tiltHeightFactor × 개수)). 이유: 랭킹 게임이라 결과 재현성과 수치 조정이 중요. Rigidbody2D는 무너지는 연출에서만 사용.
- 흔들림(sway)은 기울기 판정에 넣지 않고 시각·타이밍 난이도로만 사용 (흔들림 때문에 입력 없이 지는 일이 없도록).
- 정중앙 판정이면 바로 아래 귤 x에 맞춰 붙이고 콤보 증가. 보너스 = 50 × 콤보.
- 프로토타입 그래픽은 코드로 만든 임시 스프라이트(`SpriteFactory`) 사용. 에셋 의존 없음.

## Assets 폴더 규칙 (사용자 지정, 2026-11-06)
- `00Scene/` — 씬
- `01Script/` — 스크립트
- `02Resources/00Component/` — 직접 만든 리소스(텍스처, 머티리얼, FBX, 설정 에셋 등)
- `02Resources/01Asset/` — 에셋스토어에서 받은 것
- `03Image/` — 이미지(스프라이트, UI 이미지)
- `04Animation/` — 애니메이션
- `05Audio/` — 사운드
- 주의: `02Resources`는 Unity 특수 폴더 `Resources`가 아니므로 `Resources.Load` 대상이 아님.
- 에셋 이동은 반드시 Unity 안에서(`AssetDatabase.MoveAsset`) 해서 GUID와 참조를 유지할 것.
- 템플릿 기본 파일(`SampleScene`, `TutorialInfo`, `Readme.asset`)은 사용자 승인으로 삭제함 (2026-11-06). 필요하면 첫 커밋 `70ce5b2`에서 복구 가능.

## 코드 구조 (2026-11-06 기준)
- 씬: `Assets/00Scene/Game.unity` (빌드 설정 0번, 프로젝트의 유일한 씬)
- `Assets/01Script/StackGame.cs` — 게임 상태(Ready/Swinging/Dropping/GameOver), 왕복·낙하·착지·기울기·게임오버, 임시 OnGUI HUD
- `Assets/01Script/SpriteFactory.cs` — 임시 사각형·원 스프라이트 생성
- URP 설정: `Assets/02Resources/00Component/Settings/` (PC_RPAsset 등. 이동 후에도 그래픽 설정 참조 정상 확인)
- Input 액션: `Assets/02Resources/00Component/InputSystem_Actions.inputactions` (현재 코드는 이 파일을 쓰지 않고 Keyboard/Mouse를 직접 읽음)
- 조정 수치는 모두 `StackGame` 인스펙터 public 필드

## 시행착오
- 이 환경에는 사용자 PC 셸 도구가 없어 Git 명령은 Unity MCP `execute_code`(safety_checks=false)에서 `Process.Start("git", ...)`로 실행함. `GIT_TERMINAL_PROMPT=0`으로 프롬프트 대기 방지.
- `execute_code` 기본 컴파일러가 CodeDom(C# 6)이라 로컬 함수 문법이 컴파일 실패함 → `System.Func<>` 람다로 대체. `Object`는 `UnityEngine.Object`로 명시해야 함(모호성 오류).
- Unity 에디터가 백그라운드일 때 플레이 모드 프레임이 진행되지 않음(frame=3에서 정지) → MCP로 판정을 테스트할 때는 리플렉션으로 `UpdateDrop`을 직접 반복 호출함. 실제 입력과 손맛은 에디터에 포커스를 두고 직접 플레이해서 확인해야 함.
- 스크린샷은 `output_folder: Temp/Screenshots`로 저장 (Assets에 파일이 생기지 않게 하려는 것, Temp는 Git 제외).

## 검증 기록
- 2026-11-06 리플렉션 테스트: 매번 +0.2 오프셋 → 9개째 기울기 -20.3°로 게임 오버(한계 20°). 정중앙 4연속 → 점수 150/350/600/900, 콤보 1~4. 오프셋 1.2 → "귤이 떨어졌습니다" 게임 오버. 콘솔 에러·경고 없음.
- 실제 키 입력(스페이스·좌클릭) 플레이: 확인 필요

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
- 임시 HUD는 OnGUI 기본 폰트 사용. 에디터에서는 한글이 보이지만 다른 PC 빌드에서도 보이는지는 확인 필요 → 11/7 UI 작업에서 한글 폰트를 포함한 UI로 교체 예정
- 2D 진행이지만 렌더러는 템플릿 기본 URP Universal Renderer(PC_Renderer) 그대로임. 스프라이트 표시는 정상 확인. 2D Renderer로 바꿀지는 미정
