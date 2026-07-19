# 랑그릿사 드라마틱 에디션 한글패치 v0.13.7

세가 새턴판 `LANGRISSER Dramatic Edition`용 비공식 한국어 번역 패치입니다.
게임 데이터는 포함하지 않으며, 사용자가 직접 준비한 검증된 정품 MDF에만 적용됩니다.

이번 버전에는 기존 랑그릿사 II 번역과 음성 복원 계보를 유지하면서 랑그릿사 I 한국어 번역, UI 보완, 새 글꼴과 상점 오류 수정이 추가되었습니다.

## 준비물

- Windows 10/11 또는 Python 3.8 이상을 사용할 수 있는 환경
- 정품에서 직접 만든 `langDramaticEdition.mdf`
- 원본 MDF SHA-256: `1a9d479d3238bd1932fe2faee0c2b146c6333127a5b39d83e7d3d81a067505c1`

## 적용 방법

패키지 폴더에서 다음 명령을 실행합니다.

```powershell
python apply_patch.py "D:\경로\langDramaticEdition.mdf"
```

완성 파일은 원본 옆의 `langDramaticEdition_ko_v0.13.7.mdf`입니다. 원본 `MDS`를 함께 두고 MDF를 불러오거나, 다음 명령으로 음성 호환 BIN/CUE를 만듭니다.

```powershell
python make_cue_bin.py "D:\경로\langDramaticEdition_ko_v0.13.7.mdf"
```

생성된 일반 CUE가 권장 MODE2/XA 버전입니다. `_mode1.cue`는 일부 환경용 대체 파일입니다.

## v0.13.7 주요 내용

- 랑그릿사 I 본편 대사와 주요 메뉴·전투 UI 한국어화
- 큰 한글 글꼴을 Neo둥근모 16px 기반 비트맵으로 교체
- 작은 한글 글꼴을 갈무리7 8px 기반 비트맵으로 교체
- 루시리스 문답 말투, 시스템·설정·명령 메뉴 및 배치 화면 보완
- `SCENARIO`, `TURN`, `PCM`, `ON`, `OFF` 영문 표기 유지
- 랑그릿사 II에서 장착품만 있을 때 판매창에 들어가면 발생하던 SH-2 오류 수정
- MODE2/XA 이벤트 음성과 CDDA 배경음 보존

## 중요 사항

- 본 패치는 Ymir 에뮬레이터에서만 테스트했습니다. 다른 에뮬레이터와 세가 새턴 실기에서의 작동 여부는 확인하지 않았습니다.
- 패치 후 게임과 에뮬레이터를 완전히 재시작하십시오.
- 기존 세이브는 사용할 수 있지만, 구형 세이브스테이트에는 이전 글꼴·클래스 데이터가 남을 수 있습니다.
- MDF/BIN/CUE 및 원본 게임 파일의 재배포는 금지합니다.
- 본 패치는 무료 비공식 팬 번역이며 원저작권자와 무관합니다.

상세 변경 및 검증값은 `CHANGELOG.md`, `VALIDATION.md`, `SHA256SUMS.txt`를 참고하십시오.

## 글꼴 고지

패치에 포함된 한글 비트맵은 다음 OFL-1.1 글꼴을 바탕으로 제작했습니다. 원본 TTF 파일은 패키지에 포함하지 않습니다.

- [Neo둥근모](https://neodgm.dalgona.dev/) — SIL Open Font License 1.1
- [갈무리](https://quiple.dev/font/galmuri) — SIL Open Font License 1.1
