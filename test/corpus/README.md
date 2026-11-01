# Geulman Test Corpus

Round-trip 검증용 문서 컬렉션. 각 문서는 `test/Geulman.Tests`의 RoundtripTests에 의해 검증됨.

## 현재 인벤토리

| 파일 | 형식 | 특징 | 상태 |
|------|------|------|------|
| docx-form.docx | .docx | 양식 문서 (테이블, 빈칸) | ✅ round-trip |
| docx-novel.docx | .docx | 소설 본문 (다중 문단) | ✅ round-trip + replace-all |
| docx-report.docx | .docx | 보고서 (코멘트, 변경 추적) | ✅ round-trip + edited |
| docx-image.docx | .docx | 이미지 포함 문서 | ✅ round-trip |
| hwpx-form.hwpx | .hwpx | 한컴 양식 | ✅ round-trip |
| hwpx-novel.hwpx | .hwpx | 한컴 소설 | ✅ round-trip |
| hwp/original.hwp | .hwp | 연구계획서 (다중 문단) | ✅ save-edited + replace-all |

## 필요 추가 (Step 3a — 사용자 제공)

| 요구 | 수량 | 목적 |
|------|------|------|
| docx: 표+머리글/꼬릿글+섹션+footnote 포함 | ≥2 | 구조적 fidelity 검증 |
| docx: OLE 임베드 (엑셀 차트 등) | ≥1 | 미확인 구조 보존 검증 |
| hwpx: 표+header/footer 포함 | ≥2 | hwpx 구조적 fidelity |
| hwp: 다중 섹션 + OLE 임베드 | ≥2 | hwp 레코드 트리 보존 |

## 테스트 적용 방법

새 fixture 추가 시:
1. 파일을 `test/corpus/` (또는 `test/hwp/`)에 배치
2. `RoundtripTests.cs`의 `[InlineData]`에 파일명 추가
3. `dotnet test` 실행 → 통과 확인
4. 이 README에 행 추가
