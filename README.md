# 랑그릿사 드라마틱 에디션 한글패치 v0.12.25

세가 새턴판 `LANGRISSER Dramatic Edition`용 비공식 한국어 번역 패치입니다.
게임 데이터는 포함하지 않으며, 사용자가 직접 준비한 검증된 정품 MDF에만 적용됩니다.

## 준비물

- Windows 10/11 또는 Python 3.8 이상을 사용할 수 있는 환경
- 정품에서 직접 만든 `langDramaticEdition.mdf`
- 원본 MDF SHA-256: `1a9d479d3238bd1932fe2faee0c2b146c6333127a5b39d83e7d3d81a067505c1`

## 적용 방법

패키지 폴더에서 다음 명령을 실행합니다.

```powershell
python apply_patch.py "D:\경로\langDramaticEdition.mdf"
```

완성 파일은 원본 옆의 `langDramaticEdition_ko_v0.12.25.mdf`입니다. 원본 `MDS`를 함께 두고 MDF를 불러오거나, 다음 명령으로 음성 호환 BIN/CUE를 만듭니다.

```powershell
python make_cue_bin.py "D:\경로\langDramaticEdition_ko_v0.12.25.mdf"
```

생성된 일반 CUE가 권장 MODE2/XA 버전입니다. `_mode1.cue`는 일부 환경용 대체 파일입니다.

## 중요 사항

- 본 패치는 Ymir 에뮬레이터에서만 테스트했습니다. 다른 에뮬레이터와 세가 새턴 실기에서의 작동 여부는 확인하지 않았습니다.
- 패치 후 게임과 에뮬레이터를 완전히 재시작하십시오.
- 기존 세이브는 사용할 수 있지만, 구형 세이브스테이트에는 이전 클래스 데이터가 남을 수 있습니다.
- MDF/BIN 및 원본 게임 파일의 재배포는 금지합니다.
- 본 패치는 무료 비공식 팬 번역이며 원저작권자와 무관합니다.

상세 변경 및 검증값은 `CHANGELOG.md`, `VALIDATION.md`, `SHA256SUMS.txt`를 참고하십시오.
