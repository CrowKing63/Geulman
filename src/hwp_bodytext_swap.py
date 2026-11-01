"""hwp_bodytext_swap.py — .hwp in-place 스왑 프로토타입 (M2)

hwpkit의 HwpFile 래퍼는 ViewText(디스플레이 캐시)까지 본문으로 취급하는 버그가
있어서 우회한다. 대신 레코드 레벨 API(records) + CFB 컨테이너(cfb)로
BodyText/Section* 스트림만 정확히 편집한다.

원칙:
  - swap_in_para_text는 같은 길이의 텍스트만 교체 → 바이트 길이 보존 →
    PARA_LINE_SEG(레이아웃 캐시) 무효화 불필요
  - 길이 변화(len(old) != len(new)는 swap_in_para_text_lenient가 PARA_TEXT
    바이트를 교체하고 PARA_HEADER char count를 갱신한 뒤, 존재하는
    PARA_LINE_SEG만 더미화해 한컴이 레이아웃을 재계산하게 한다.
  - BodyText 외 모든 CFB 스트림은 byte 그대로 (cfb.dump가 디렉토리 트리 보존)
  - ViewText 동기화: 웹 렌더러(한컴 Docs, rhwp)는 ViewText를 읽으므로
    편집된 BodyText 내용으로 대체한다. 한컴 데스크톱도 정상 오픈 확인됨.

사용:
  python hwp_bodytext_swap.py --list <in.hwp>
  python hwp_bodytext_swap.py <in.hwp> <out.hwp> <문단인덱스> <old> <new> [bt_sid]
  (len(old) == len(new)이면 동일 길이 스왑, 다르면 길이 변화 스왑;
   bt_sid 생략 시 문단 수가 적은 쪽을 BodyText로 가정)

문단 인덱스는 BodyText의 로컬 문단 번호다. --list 출력에서 BodyText sid를
판정해서 명시적으로 전달하는 것이 안전하다.
"""
import struct
import sys

from hwpkit import cfb, records as R


def swap_in_para_text_lenient(records, paragraph_index, old, new):
    """Replace a UTF-16LE substring of any length in paragraph N's PARA_TEXT.

    Unlike records.swap_in_para_text (equal-length only), this handles
    len(old) != len(new). It updates the PARA_HEADER char count by the
    delta and regenerates the cached PARA_LINE_SEG as an all-zero dummy so
    Hancom recomputes the layout from PARA_CHAR_SHAPE on open. BodyText
    streams rarely carry a LineSeg, so _regenerate_lineseg is usually a
    no-op; it only matters for the few paragraphs that do.
    """
    para_indices = R.index_paragraphs(records)
    pr_idx = para_indices[paragraph_index]
    pt_idx = R._para_text_after(records, pr_idx)
    if pt_idx is None:
        raise ValueError(f"P{paragraph_index} has no PARA_TEXT to edit")
    old_b = old.encode("utf-16-le")
    new_b = new.encode("utf-16-le")
    body = records[pt_idx]["body"]
    if old_b not in body:
        raise ValueError(f"P{paragraph_index} PARA_TEXT does not contain {old!r}")

    # 1) swap the text bytes (first occurrence, like the equal-length path)
    records[pt_idx]["body"] = body.replace(old_b, new_b, 1)
    records[pt_idx]["size"] = len(records[pt_idx]["body"])
    if records[pt_idx]["size"] >= 0xFFF:
        records[pt_idx]["force_extended_header"] = True
        records[pt_idx]["header_len"] = 8
    else:
        records[pt_idx].pop("force_extended_header", None)
        records[pt_idx]["header_len"] = 4

    # 2) bump the PARA_HEADER char count by (len(new) - len(old))
    para = records[pr_idx]
    pbody = bytearray(para["body"])
    chars_raw = struct.unpack_from("<I", pbody, 0)[0]
    high_bit = chars_raw & 0x80000000
    new_chars = (chars_raw & 0x7FFFFFFF) + (len(new) - len(old))
    struct.pack_into("<I", pbody, 0, high_bit | (new_chars & 0x7FFFFFFF))
    para["body"] = bytes(pbody)

    # 3) invalidate the cached line layout so Hancom recomputes it
    R._regenerate_lineseg(records, pr_idx, new_chars)


def find_bodytext_sids(entries):
    """'Section*' 디렉터리 엔트리 중 BodyText 쪽을 골라낸다.

    구분법: ViewText는 화면 행 단위 분절이라 문단 수가 많다. BodyText는
    논리 문단이라 적다. 단, 확실한 검증은 독립 파서(syhwp) 텍스트 대조다.
    여기서는 (문단 수, 첫 비어 있지 않은 텍스트)를 출력해서 사람이 판정한다.
    """
    sids = []
    for sid, de in entries.items():
        if de.etype == 2 and de.data and de.name.startswith("Section"):
            recs = R.parse(R.decompress(de.data))
            lines = [l for l in R.extract_text(recs).split("\n") if l.strip()]
            sids.append((sid, len(lines), lines[0][:40] if lines else ""))
    return sids


def set_para_text(records, paragraph_index, new_text):
    """Set paragraph N's PARA_TEXT to new_text (any length).

    Replaces the entire text content, updates PARA_HEADER char count,
    and invalidates LineSeg so Hancom recomputes layout.
    """
    para_indices = R.index_paragraphs(records)
    pr_idx = para_indices[paragraph_index]
    pt_idx = R._para_text_after(records, pr_idx)
    if pt_idx is None:
        raise ValueError(f"P{paragraph_index} has no PARA_TEXT to edit")

    new_b = new_text.encode("utf-16-le")
    records[pt_idx]["body"] = new_b
    records[pt_idx]["size"] = len(new_b)
    if len(new_b) >= 0xFFF:
        records[pt_idx]["force_extended_header"] = True
        records[pt_idx]["header_len"] = 8
    else:
        records[pt_idx].pop("force_extended_header", None)
        records[pt_idx]["header_len"] = 4

    # Update PARA_HEADER char count
    para = records[pr_idx]
    pbody = bytearray(para["body"])
    chars_raw = struct.unpack_from("<I", pbody, 0)[0]
    high_bit = chars_raw & 0x80000000
    struct.pack_into("<I", pbody, 0, high_bit | (len(new_text) & 0x7FFFFFFF))
    para["body"] = bytes(pbody)

    # Invalidate cached line layout
    R._regenerate_lineseg(records, pr_idx, len(new_text))


def main(argv):
    if argv and argv[0] == "--list":
        entries = cfb.load(argv[1])
        for sid, n, first in find_bodytext_sids(entries):
            print(f"sid {sid}: non-empty paras={n} first={first!r}")
        return 0

    if argv and argv[0] == "--dump":
        entries = cfb.load(argv[1])
        cands = find_bodytext_sids(entries)
        bt_sid = min(cands, key=lambda c: c[1])[0]
        recs = R.parse(R.decompress(entries[bt_sid].data))
        lines = [l for l in R.extract_text(recs).split("\n")]
        for i, line in enumerate(lines):
            print(f"[{i}] {line}")
        return 0

    if argv and argv[0] == "--replace-all":
        in_path, out_path, find, replace = argv[1], argv[2], argv[3], argv[4]
        entries = cfb.load(in_path)
        cands = find_bodytext_sids(entries)
        bt_sid = min(cands, key=lambda c: c[1])[0]
        recs = R.parse(R.decompress(entries[bt_sid].data))
        lines = R.extract_text(recs).split("\n")
        count = 0
        for i, line in enumerate(lines):
            if find in line:
                set_para_text(recs, i, line.replace(find, replace))
                count += 1
        if count == 0:
            print("0")
            return 0
        new_bytes = R.compress(R.serialize(recs))
        entries[bt_sid].data = new_bytes
        vt_sids = [c[0] for c in cands if c[0] != bt_sid]
        for vt_sid in vt_sids:
            entries[vt_sid].data = new_bytes
        cfb.dump(entries, out_path)
        print(count)
        return 0

    if argv and argv[0] == "--save-edited":
        import json
        in_path, out_path, json_path = argv[1], argv[2], argv[3]
        with open(json_path, "r", encoding="utf-8") as f:
            edited = json.load(f)
        entries = cfb.load(in_path)
        cands = find_bodytext_sids(entries)
        bt_sid = min(cands, key=lambda c: c[1])[0]
        recs = R.parse(R.decompress(entries[bt_sid].data))
        for item in edited:
            set_para_text(recs, item["index"], item["text"])
        new_bytes = R.compress(R.serialize(recs))
        entries[bt_sid].data = new_bytes
        vt_sids = [c[0] for c in cands if c[0] != bt_sid]
        for vt_sid in vt_sids:
            entries[vt_sid].data = new_bytes
        cfb.dump(entries, out_path)
        print(f"saved {len(edited)} paragraph(s)")
        return 0

    in_path, out_path, para_idx, old, new = argv[0], argv[1], int(argv[2]), argv[3], argv[4]
    bt_sid_arg = int(argv[5]) if len(argv) > 5 else None

    entries = cfb.load(in_path)
    cands = find_bodytext_sids(entries)
    if bt_sid_arg is not None:
        bt_sid = bt_sid_arg
    else:
        # 휴리스틱: ViewText는 화면 행 분절이라 문단 수가 많다. BodyText가 적다.
        bt_sid = min(cands, key=lambda c: c[1])[0]
        print(f"note: assumed sid {bt_sid} is BodyText (fewest paras={min(c[1] for c in cands)}); verify with --list", file=sys.stderr)

    recs = R.parse(R.decompress(entries[bt_sid].data))
    if len(old) == len(new):
        R.swap_in_para_text(recs, para_idx, old, new)
    else:
        swap_in_para_text_lenient(recs, para_idx, old, new)
    new_bytes = R.compress(R.serialize(recs))
    entries[bt_sid].data = new_bytes

    # ViewText 동기화: HWP Writer는 ViewText를 디스플레이 소스로 읽으므로
    # 편집된 BodyText 데이터로 대체한다. (ViewText 레코드 인코딩이
    # BodyText와 달라 same-length byte swap 불가 — 크기 불일치를
    # HWP Writer가 오픈 시 감지하고 재구성)
    vt_sids = [c[0] for c in cands if c[0] != bt_sid]
    if vt_sids:
        for vt_sid in vt_sids:
            entries[vt_sid].data = new_bytes
            print(f"note: synced ViewText sid {vt_sid} with edited BodyText", file=sys.stderr)

    cfb.dump(entries, out_path)
    print(f"swapped para {para_idx}: {old!r} -> {new!r}  ({in_path} -> {out_path})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
