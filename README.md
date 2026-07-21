# 랑그릿사 드라마틱 에디션 한글패치 v0.13.17

세가 새턴판 `LANGRISSER Dramatic Edition`용 비공식 한국어 번역 패치입니다.
게임 원본은 포함하지 않으며, 사용자가 정품에서 직접 만든 일본판 MDF에만 적용됩니다.

## 가장 쉬운 패치 방법

Python 설치나 명령어 입력은 필요 없습니다.

1. 받은 ZIP을 우클릭하고 **모두 압축 풀기**를 누릅니다.
2. `랑그릿사_DE_한글패치_v0.13.17.exe`를 실행합니다.
3. 일본판 원본 `.mdf`를 고른 뒤 **한글판 만들기**를 누릅니다.

완료되면 결과 폴더가 자동으로 열립니다. 에뮬레이터에서 다음 파일 하나를 여세요.

`Langrisser_Dramatic_Edition_Korean_v0.13.17.cue`

원본 MDF를 EXE 위로 끌어다 놓아도 됩니다. 원본 파일은 수정하지 않습니다.

## 준비물

- Windows 10/11
- 일본 정품판에서 만든 `langDramaticEdition.mdf`
- 결과 파일을 저장할 드라이브의 여유 공간 800MB 이상

지원 원본:

- 크기: `682,656,624`바이트
- SHA-256: `1a9d479d3238bd1932fe2faee0c2b146c6333127a5b39d83e7d3d81a067505c1`

## 안 될 때

- **ZIP 안에서 바로 실행하지 마세요.** 먼저 `모두 압축 풀기`를 누르세요.
- **원본이 아니라고 나옵니다.** `.mds`, `.bin`, `.cue`, 이미 패치된 파일이 아니라 일본 정품판 원본 `.mdf`를 선택하세요.
- **용량이 부족하다고 나옵니다.** 결과를 저장할 드라이브에 800MB 이상을 비워 주세요.
- **음성이나 배경음이 나오지 않습니다.** 완성된 `.bin`이나 원본 `.mdf`가 아니라 위의 `.cue`를 여세요. `.cue`와 `.bin`은 같은 폴더에 두어야 합니다.
- **예전 글꼴이 보입니다.** 에뮬레이터를 완전히 종료하고 다시 실행한 뒤, 구형 세이브스테이트 대신 게임 내부 세이브를 불러오세요.
- Windows가 알 수 없는 게시자 경고를 표시하면 GitHub 릴리스의 `SHA256SUMS.txt`와 EXE 해시가 같은지 먼저 확인하세요.

## v0.13.17 주요 내용

### 랑그릿사 I UI 교정

- 타이틀 메뉴의 `START / LOAD / BACKUP RAM`을 공통 시작점 `x=17`에 좌측 정렬하고 글자 간격을 1픽셀로 통일
- 로드 화면 제목을 `LOAD`로 복구하고 `출격`, `결정`, `저장`, `공격`의 깨짐·오역 수정
- 용병 선택·결정 창의 용병명을 기존 16×16 Neo둥근모 경로로 표시하고 캐시 페이지 충돌 방지
- 전투 화면 직업명과 큰 `마법` 표기를 실제 소비 글꼴 페이지에서 교정
- 지휘 메뉴의 `공격 / 방어 / 수동`을 Neo둥근모로 통일하고 작은 글꼴은 갈무리7 경로 유지
- `ENEMY PHASE` 명령열과 11개 글리프 셀을 게임 원본과 바이트 단위로 동일하게 복구

### 누적 보존

- v0.13.13의 안전한 HUD 초기화, 상태창·배치 메뉴·고정 UI 흰색 교정 유지
- 랑그릿사 II 시나리오 16×16 글꼴과 하단 HUD 교정 유지
- 기존 번역, Neo둥근모·갈무리 기반 글꼴, 이벤트 음성·MODE2/XA·CDDA 보존 계보 유지

## 고급 사용자용 수동 적용

수동 적용용 LDP는 원클릭 ZIP에 포함되지 않으며, GitHub 릴리스의 별도 자산 `langrisser_de_ko_v0.13.17.ldp`로 제공됩니다. 저장소의 `apply_patch.py`, `make_cue_bin.py`와 이 LDP를 같은 폴더에 둔 뒤 Python 3.8 이상에서 실행합니다.

```powershell
python apply_patch.py "D:\경로\langDramaticEdition.mdf" "D:\경로\langDramaticEdition_ko_v0.13.17.mdf"
python make_cue_bin.py "D:\경로\langDramaticEdition_ko_v0.13.17.mdf" "D:\경로\Langrisser_Dramatic_Edition_Korean_v0.13.17.bin"
```

두 번째 명령이 만드는 `Langrisser_Dramatic_Edition_Korean_v0.13.17.cue`가 권장 MODE2/XA CUE입니다. 일반 사용자는 이 수동 방법을 사용할 필요가 없습니다.

## 검증 범위와 중요 사항

- v0.13.17의 런타임 화면은 `RetroArch 1.22.2 / Beetle Saturn` 코어에서 검증했습니다. 검수한 v01317 QA MDF/BIN과 릴리스 목표 MDF/BIN은 바이트 단위로 같습니다.
- 용병 선택 이름, 큰 `마법`, 원본 `ENEMY PHASE`, 지휘 메뉴 글꼴을 같은 빌드에서 사용자 검수했습니다.
- **v0.13.17부터 공식 화면 검수는 RetroArch의 Beetle Saturn 코어에서만 수행합니다.** Ymir를 포함한 다른 세가 새턴 에뮬레이터와 실기는 공식 검증 대상이 아니며 호환성을 보장하지 않습니다.
- 랑그릿사 I의 알려진 `아이템 장비 → 구입` 경로는 캡처했지만 모든 상점 분기나 게임 전체 완주를 검증한 것은 아닙니다.
- 기존 게임 내부 세이브는 사용할 수 있지만, 구형 세이브스테이트에는 이전 글꼴 또는 실행 상태가 남을 수 있습니다.
- MDF/BIN/CUE 및 원본 게임 파일의 재배포는 금지합니다.
- 본 패치는 무료 비공식 팬 번역이며 원저작권자와 무관합니다.

## 글꼴 고지

패치의 한글 비트맵은 다음 OFL-1.1 글꼴을 바탕으로 제작했습니다. 원본 TTF는 패키지에 포함하지 않습니다.

- [Neo둥근모](https://neodgm.dalgona.dev/) — SIL Open Font License 1.1
- [갈무리](https://quiple.dev/font/galmuri) — SIL Open Font License 1.1

상세 변경 및 검증값은 `CHANGELOG.md`, `VALIDATION.md`, `SHA256SUMS.txt`를 참고하십시오. EXE와 ZIP 해시는 패키지 빌드 완료 후 생성되는 검증 보고서와 GitHub 릴리스 설명에 기록합니다.
