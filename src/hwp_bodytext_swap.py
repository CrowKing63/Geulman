"""hwp_bodytext_swap.py — .hwp in-place 스왑 프로토타입 (M2)

hwpkit의 HwpFile 래퍼는 ViewText(디스플레이 캐시)까지 본문으로 취급하는 버그가
있어서 우회한다. 대신 레코드 레벨 API(records) + CFB 컨테이너(cfb)로
BodyText/Section* 스트림만 정확히 편집한다.

원칙:
  - swap_in_para_text는 같은 길이의 텍스트만 교체 → 바이트 길이 보존 →
    PARA_LINE_SEG(레이아웃 캐시) 무효화 불필요
  - BodyText 외 모든 CFB 스트림은 byte 그대로 (cfb.dump가 디렉토리 트리 보존)
  - ViewText 동기화: 웹 렌더러(한컴 Docs, rhwp)는 ViewText를 읽으므로
    편집된 BodyText 내용으로 대체한다. 한컴 데스크톱도 정상 오픈 확인됨.

사용:
  python hwp_bodytext_swap.py --list <in.hwp>
  python hwp_bodytext_swap.py <in.hwp> <out.hwp> <문단인덱스> <old> <new> [bt_sid]
  (len(old) == len(new) 필수; bt_sid 생략 시 문단 수가 적은 쪽을 BodyText로 가정)

문단 인덱스는 BodyText의 로컬 문단 번호다. --list 출력에서 BodyText sid를
판정해서 명시적으로 전달하는 것이 안전하다.
"""
import sys

from hwpkit import cfb, records as R


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

    in_path, out_path, para_idx, old, new = argv[0], argv[1], int(argv[2]), argv[3], argv[4]
    bt_sid_arg = int(argv[5]) if len(argv) > 5 else None
    if len(old) != len(new):
        print("error: equal-length swap required", file=sys.stderr)
        return 1

    entries = cfb.load(in_path)
    cands = find_bodytext_sids(entries)
    if bt_sid_arg is not None:
        bt_sid = bt_sid_arg
    else:
        # 휴리스틱: ViewText는 화면 행 분절이라 문단 수가 많다. BodyText가 적다.
        bt_sid = min(cands, key=lambda c: c[1])[0]
        print(f"note: assumed sid {bt_sid} is BodyText (fewest paras={min(c[1] for c in cands)}); verify with --list", file=sys.stderr)

    recs = R.parse(R.decompress(entries[bt_sid].data))
    R.swap_in_para_text(recs, para_idx, old, new)
    new_bytes = R.compress(R.serialize(recs))
    entries[bt_sid].data = new_bytes

    # ViewText 동기화: 웹 렌더러(한컴 Docs, rhwp)는 ViewText를 읽으므로
    # 편집된 BodyText 내용으로 대체한다.
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
