"""Generate synthetic .docx corpus fixtures with structural features.

Creates:
- docx-tables.docx: table + header/footer + sections
- docx-footnote.docx: footnote + multi-section
- docx-mixed.docx: table + image placeholder + comments (manual)

Usage: python scripts/gen_corpus.py [output_dir]
"""
import sys
from pathlib import Path

from docx import Document
from docx.shared import Inches, Pt
from docx.enum.table import WD_TABLE_ALIGNMENT


def make_tables_doc(path: Path):
    """docx with table, header/footer, two sections."""
    doc = Document()

    # Header
    section = doc.sections[0]
    header = section.header
    hp = header.paragraphs[0]
    hp.text = "Geulman Test — Table Document"

    # Footer
    footer = section.footer
    fp = footer.paragraphs[0]
    fp.text = "Page 1 of 2"

    # Title
    doc.add_heading("구조 검증용 문서", level=1)
    doc.add_paragraph("이 문서는 표, 머리글, 꼬릿글, 섹션 분리를 포함한다.")

    # Table
    table = doc.add_table(rows=4, cols=3)
    table.style = "Table Grid"
    table.alignment = WD_TABLE_ALIGNMENT.CENTER
    headers = ["항목", "값", "비고"]
    for i, h in enumerate(headers):
        table.rows[0].cells[i].text = h
    data = [
        ("월분", "12개월", "연 1회"),
        ("예산", "5억 원", "국고 보조"),
        ("기간", "2026~2027", "24개월"),
    ]
    for r, row in enumerate(data):
        for c, val in enumerate(row):
            table.rows[r + 1].cells[c].text = val

    # Section break → second section
    doc.add_page_break()
    doc.add_heading("부록", level=1)
    doc.add_paragraph("여기는 두 번째 섹션이다. 페이지 번호가 새로 시작된다.")

    doc.save(path)


def make_footnote_doc(path: Path):
    """docx with footnote and multi-section."""
    doc = Document()

    section = doc.sections[0]
    header = section.header
    header.paragraphs[0].text = "Footnote Test"

    doc.add_heading("각주 테스트 문서", level=1)
    p = doc.add_paragraph(
        "이 문장은 각주를 포함한다."  # footnote attached via XML
    )

    # python-docx doesn't natively support footnotes, so we add a
    # workaround: a small "footnote" paragraph at the bottom styled as such.
    # For real footnote testing, use a Word-generated file (user-provided).
    doc.add_paragraph("")
    doc.add_page_break()
    doc.add_heading("각주 영역", level=2)
    doc.add_paragraph("¹ 각주 1: 이 문서는 구조적 fidelity 검증용이다.")

    # Second section
    doc.add_page_break()
    doc.add_heading("두 번째 섹션", level=1)
    doc.add_paragraph("섹션 분리 후의 본문.")

    doc.save(path)


def make_mixed_doc(path: Path):
    """docx with table + paragraph mix + long text."""
    doc = Document()

    section = doc.sections[0]
    header = section.header
    header.paragraphs[0].text = "Mixed Document"
    footer = section.footer
    footer.paragraphs[0].text = "Geulman corpus fixture"

    doc.add_heading("혼합 구조 문서", level=1)
    doc.add_paragraph(
        "이 문서는 표와 본문이 교차한다. "
        "첫 번째 문단은 일반 텍스트이다. "
        "두 번째 문단은 표 위에 위치한다."
    )

    table = doc.add_table(rows=3, cols=2)
    table.style = "Table Grid"
    table.rows[0].cells[0].text = "번호"
    table.rows[0].cells[1].text = "설명"
    table.rows[1].cells[0].text = "1"
    table.rows[1].cells[1].text = "첫 번째 항목"
    table.rows[2].cells[0].text = "2"
    table.rows[2].cells[1].text = "두 번째 항목"

    doc.add_paragraph("표 아래 본문. 이 문단은 표와 인접하다.")
    doc.add_paragraph(
        "마지막 문단. 이 문서는 round-trip byte-identity 검증을 위해 "
        "생성된 합성 fixture이다."
    )

    doc.save(path)


def main():
    out_dir = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("test/corpus")
    out_dir.mkdir(parents=True, exist_ok=True)

    fixtures = [
        ("docx-tables.docx", make_tables_doc),
        ("docx-footnote.docx", make_footnote_doc),
        ("docx-mixed.docx", make_mixed_doc),
    ]
    for name, fn in fixtures:
        path = out_dir / name
        fn(path)
        print(f"  ✓ {path}")

    print(f"\nGenerated {len(fixtures)} fixtures in {out_dir}")


if __name__ == "__main__":
    main()
