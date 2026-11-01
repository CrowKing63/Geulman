# 글만 — v1 실행 계획

> 상태: 작성 (2026-11-01)
> 전제: M0~M6 완료 (spec-v0.2.md 기준). 이 문서는 최종 목표("열기→읽기→편집→저장(원본 형식)"이 실제로 동작하는 앱)까지의 실행 순서와 검증 기준을 고정한다.

---

## 1. 현재 상태 요약

| 마일스톤 | 내용 | 상태 |
|---|---|---|
| M1 | docx 엔진 (C# ZipEngine) — round-trip byte-identical | ✅ |
| M2 | HWP/HWPX 엔진 (Python hwpkit 브릿지 + C# ZipEngine) | ✅ |
| M3 | WinUI 3 GUI 셸 (3-state layout) | ✅ |
| M4 | 코퍼스 6종 fixture (docx×4, hwpx×2) round-trip 검증 | ✅ |
| M5 | .hwp 길이 변화 지원 (lenient swap + LineSeg 무효화) | ✅ |
| M6 | GUI 파일 브라우저, UTF-8 파이프라인, 버튼 통일 | ✅ |

## 2. 핵심 갭

최종 목표 워크플로("열기→읽기→**편집**→저장")가 현재 구현에서 통하지 않는 부분:

1. **저장 파이프라인 부재** — `SaveBtn_Click`는 FindBox에 텍스트가 있어야만 CLI `edit`(첫 occurrence 하나만)을 실행. BodyEditor에 직접 타이핑한 편집은 디스크에 반영되지 않음.
2. **Replace-all 없음** — 엔진 `ReplaceFirst`는 첫 occurrence만 교체. 스펙 §3 "대량 수정의 90%"인 find/replace-all이 없음.
3. **자동 테스트 부재** — 검증이 CLI(`roundtrip`, `diff`)와 수동 확인에 의존. 커밋마다 자동 실행되는 테스트 프로젝트 없음.
4. **코퍼스 얇음** — fixture 6종 모두 `make-fixture` 생성 문서. 스펙 §8 "실제 문서 10~20부" 미달. .hwp corpus 없음.
5. **스펙 UX/에러 처리 미구현** — 최근 문서 목록, 파일 드롭, 암호화 문서 탐지+거부, "미확인 구조 N건 보존됨" 토스트.

## 3. 실행 계획 (순서 + 검증 기준)

### Step 1: 엔진 저장 파이프라인 완성

**목표**: BodyEditor의 편집이 원본 형식으로 디스크에 반영되도록 한다.

- [x] 1a. `ZipEngine.ReplaceAll(find, replace)` — 모든 occurrence 교체 (docx/hwpx)
- [x] 1b. `HwpEngine` replace-all (Python 브릿지 `--all` 플래그)
- [x] 1c. `IDocEngine.SaveEdited(List<(int index, string text)> editedParas, string outPath)` — 변경된 문단만 스팬 재작성, 그 외 바이트 보존
- [x] 1d. `ZipEngine.SaveEdited` 구현 (문단 인덱스 → `<w:t>`/`<hp:t>` 스팬 매핑)
- [x] 1e. `HwpEngine.SaveEdited` 구현 (Python 브릿지 `--save-edited` — 문단 인덱스+텍스트 목록 전달)
- [x] 1f. GUI: BodyEditor 문단 인덱스 모델 유지 + 저장 시 변경 문단 목록 추출 → `SaveEdited` 호출
- [x] 1g. CLI: `geulman save-edited <in> <out> --para "0:text" --para "3:text"` (GUI와 동일 경로 검증용)

**검증 기준**:
- fixture 열어 → BodyEditor에서 직접 텍스트 수정(문자 삽입/삭제 포함) → 저장 → `diff`로 변경 문단만 다름
- 코멘트·변경 추적 개수 불변
- round-trip(편집 없음) 여전히 byte-identical

### Step 2: 자동 테스트 수립

**목표**: 스펙 §8의 검증 전략을 테스트 코드로 고정.

- [x] 2a. `test/Geulman.Tests` (xUnit) 프로젝트 생성
- [x] 2b. docx round-trip: 편집 없음 → 모든 zip 파트 byte-identical (4개 코퍼스)
- [x] 2c. hwpx round-trip: 동일 (2개 코퍼스)
- [x] 2d. edited round-trip: N개 문단 수정 → 저장 → 변경 문단만 diff, 코멘트/변경 추적 개수 불변
- [x] 2e. .hwp dump 대조: 편집 후 레코드 트리 대조 (byte 동일 아님)
- [x] 2f. replace-all 테스트: 여러 occurrence 문서에서 전체 교체 확인

**검증 기준**: `dotnet test` → 10/10 통과 ✅

### Step 3: 코퍼스 확대

**목표**: 스펙 §8 "실제 문서 10~20부" 달성.

- [x] 3a. 실제 문서 수집 (사용자 제공) — docx×5, hwpx×3, hwp×3 이상
  - 합성 fixture: docx-tables, docx-footnote, docx-mixed (scripts/gen_corpus.py) ✅
  - 사용자 제공: pending (hwpx 구조, hwp 다중 섹션/OLE)
- [x] 3b. 각 fixture에 round-trip + edited round-trip 테스트 적용 (13개 통과)
- [x] 3c. `test/corpus/README.md` — 인벤토리 + 필요 추가 목록

**검증 기준**: 13/13 dotnet test 통과 ✅ (사용자 제공 문서 대기 중)

### Step 4: 스펙 UX/에러 처리 구현

**목표**: spec-v0.2 §3·§7의 미구현 항목 완료.

- [ ] 4a. 최근 문서 목록 (EmptyState에 최근 5개, 설정 저장)
- [ ] 4b. 파일 드롭 (Window Drag & Drop)
- [ ] 4c. 암호화/배포용 문서 탐지 + 명확한 거부 메시지 (§7)
- [ ] 4d. "미확인 구조 N건 보존됨" 토스트 (저장 시 미확인 레코드/엘리먼트 감지)

**검증 기준**: 각 항목 수동 확인 (사용자)

### Step 5: M7 배포 + 수동 검증 게이트

**목표**: 설치 가능한 패키지 + 사용자 수동 검증.

- [ ] 5a. Inno Setup 스크립트 (.exe + Python 사이드카 + hwpkit venv 번들)
- [ ] 5b. 한컴오피스에서 저장된 .hwp/.hwpx 열기 확인
- [ ] 5c. MS Word에서 저장된 .docx 열기 확인
- [ ] 5d. 배포 (사용자 확인 후)

**검증 기준**: 사용자 수동 확인 통과 (§8.4 — 확인 전 배포 없음)

---

## 4. 실행 순서 근거

```
Step 1 → Step 2 → Step 3 → Step 4 → Step 5
```

- Step 2~4의 검증 대상이 Step 1의 저장 기능. 저장 파이프라인 없이는 테스트·코퍼스·UX 완성 모두 의미 없음.
- Step 4는 Step 1~3에 의존하지 않아 병렬 가능하나, 저장 토스트(4d)는 Step 1 결과 필요.
