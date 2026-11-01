// 글만 (Geulman) — M2 프로토타입: .docx / .hwpx / .hwp in-place 텍스트 패치 엔진
//
// 원칙:
//   docx/hwpx: zip 안의 본문 XML(docx: word/document.xml, hwpx: Contents/section0.xml)
//     안에서 텍스트 스팬(<w:t> / <hp:t>)만 건드린다. 그 외 모든 바이트(서식, rsid,
//     코멘트, 변경 추적, 그림)는 그대로.
//   hwp: CFB(OLE2) 컨테이너 안의 BodyText + ViewText 스트림을 같은 길이 스왑으로
//     편집하고 ViewText를 동기화한다. (Python hwpkit 브릿지 — MVP)
//
// CLI:
//   geulman make-fixture <out.docx>          테스트용 현실적 docx 생성
//   geulman open <file>                      문단 인덱스 + 텍스트 덤프 (.docx/.hwpx/.hwp)
//   geulman roundtrip <in> <out>             편집 없이 저장 → 모든 파트 byte 비교 (zip only)
//   geulman edit <in> <out> --find "..." --replace "..."
//   geulman diff <a> <b>                     파트별 비교 + 변경 문단 + 보존 마커

using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;

internal static class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            return args.Length switch
            {
                > 0 => Dispatch(args),
                _ => PrintUsage()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[error] {ex.Message}");
            return 1;
        }
    }

    static int PrintUsage()
    {
        Console.WriteLine("usage: geulman <make-fixture|open|roundtrip|edit|diff> ...");
        return 1;
    }

    static int Dispatch(string[] args)
    {
        var cmd = args[0];
        switch (cmd)
        {
            case "make-fixture":
                var cat = args.Length > 2 ? args[2] : "report";
                Fixture.Make(args[1], cat);
                Console.WriteLine($"fixture written: {args[1]} ({cat})");
                return 0;
            case "open":
                foreach (var (i, p) in Engines.For(args[1]).ParagraphTexts())
                    Console.WriteLine($"[{i}] {p}");
                return 0;
            case "roundtrip":
                return Roundtrip(args[1], args[2]);
            case "edit":
                return Edit(args);
            case "diff":
                return Diff(args[1], args[2]);
            default:
                return PrintUsage();
        }
    }

    // ── roundtrip: 편집 없이 저장 → 모든 zip 파트가 동일해야 함 (zip only) ──
    static int Roundtrip(string inPath, string outPath)
    {
        if (inPath.EndsWith(".hwp", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("[error] roundtrip not supported for .hwp (CFB format)");
            return 1;
        }
        var doc = Engines.For(inPath);
        doc.Save(outPath);

        var a = ZipEngine.RawParts(inPath);
        var b = ZipEngine.RawParts(outPath);
        bool ok = true;
        if (a.Count != b.Count) { Console.WriteLine($"FAIL: part count {a.Count} vs {b.Count}"); return 1; }
        for (int i = 0; i < a.Count; i++)
        {
            var (nameA, bytesA) = a[i];
            var (nameB, bytesB) = b[i];
            bool sameName = nameA == nameB;
            bool sameBytes = bytesA.AsSpan().SequenceEqual(bytesB);
            if (!sameName || !sameBytes) ok = false;
            Console.WriteLine($"  {(sameName && sameBytes ? "OK  " : "DIFF")} {nameA}{(sameName ? "" : $" (-> {nameB})")} [{bytesA.Length} bytes]");
        }
        Console.WriteLine(ok ? "ROUNDTRIP PASS: all parts byte-identical" : "ROUNDTRIP FAIL");
        return ok ? 0 : 1;
    }

    // ── edit: find/replace 한 곳 ─────────────────────────────────────────
    static int Edit(string[] args)
    {
        string? inPath = null, outPath = null, find = null, replace = null;
        for (int i = 1; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "--find") find = args[++i];
            else if (a == "--replace") replace = args[++i];
            else if (inPath == null) inPath = a;
            else outPath = a;
        }
        if (inPath == null || outPath == null || find == null || replace == null) return PrintUsage();

        var doc = Engines.For(inPath);
        int n = doc.Edit(find, replace, outPath);
        if (n == 0) { Console.Error.WriteLine($"[error] not found: {find}"); return 1; }
        Console.WriteLine($"edited {n} occurrence(s) -> {outPath}");
        return 0;
    }

    // ── diff: 두 문서 비교 ───────────────────────────────────────────────
    static int Diff(string aPath, string bPath)
    {
        bool isZip = !aPath.EndsWith(".hwp", StringComparison.OrdinalIgnoreCase);
        if (isZip)
        {
            var a = ZipEngine.RawParts(aPath);
            var b = ZipEngine.RawParts(bPath);
            Console.WriteLine("parts:");
            for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
            {
                string na = i < a.Count ? a[i].Name : "<missing>";
                bool same = i < a.Count && i < b.Count && a[i].Name == b[i].Name
                    && a[i].Bytes.AsSpan().SequenceEqual(b[i].Bytes);
                Console.WriteLine($"  {(same ? "OK  " : "DIFF")} {na}");
            }
        }

        var pa = Engines.For(aPath).ParagraphTexts();
        var pb = Engines.For(bPath).ParagraphTexts();
        Console.WriteLine("changed paragraphs:");
        int changed = 0;
        for (int i = 0; i < Math.Max(pa.Count, pb.Count); i++)
        {
            string ta = i < pa.Count ? pa[i].Text : "<missing>";
            string tb = i < pb.Count ? pb[i].Text : "<missing>";
            if (ta != tb)
            {
                changed++;
                Console.WriteLine($"  [{i}] - {ta}");
                Console.WriteLine($"       + {tb}");
            }
        }
        if (changed == 0) Console.WriteLine("  (none)");

        if (aPath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            int CountIn(string path, string part, string token)
            {
                foreach (var (name, bytes) in ZipEngine.RawParts(path))
                    if (name == part)
                    {
                        var s = Encoding.UTF8.GetString(bytes);
                        return s.Split(token, StringSplitOptions.None).Length - 1;
                    }
                return 0;
            }
            Console.WriteLine("fidelity markers:");
            Console.WriteLine($"  comments.xml <w:comment : a={CountIn(aPath, "word/comments.xml", "<w:comment ")} b={CountIn(bPath, "word/comments.xml", "<w:comment ")}");
            Console.WriteLine($"  document.xml <w:ins      : a={CountIn(aPath, "word/document.xml", "<w:ins")} b={CountIn(bPath, "word/document.xml", "<w:ins")}");
            Console.WriteLine($"  document.xml <w:del      : a={CountIn(aPath, "word/document.xml", "<w:del")} b={CountIn(bPath, "word/document.xml", "<w:del")}");
        }
        return 0;
    }
}

// ── 공통 문서 엔진 인터페이스 ────────────────────────────────────────────
internal interface IDocEngine
{
    List<(int Index, string Text)> ParagraphTexts();
    int Edit(string find, string replace, string outPath);
    void Save(string outPath);
}

// ── 형식 감지: 확장자로 엔진 생성 ─────────────────────────────────────────
internal static class Engines
{
    public static IDocEngine For(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".hwp":
                return new HwpEngine(path);
            default:
                return ZipEngine.For(path);
        }
    }
}

// ── .hwp 엔진: Python hwpkit 스크립트 브릿지 (MVP) ───────────────────────
internal sealed class HwpEngine : IDocEngine
{
    readonly string _path;
    static string PyScript => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "hwp_bodytext_swap.py"));

    public HwpEngine(string path) => _path = path;

    public List<(int Index, string Text)> ParagraphTexts()
    {
        var (exit, stdout, stderr) = RunPy("--dump", _path);
        if (exit != 0) throw new Exception($"hwp dump failed: {stderr}");
        var result = new List<(int, string)>();
        int i = 0;
        foreach (var line in stdout.Split('\n'))
        {
            var t = line.TrimEnd('\r');
            if (t.StartsWith("["))
            {
                int close = t.IndexOf(']');
                result.Add((i++, t.Substring(close + 2)));
            }
        }
        return result;
    }

    public int Edit(string find, string replace, string outPath)
    {
        var paras = ParagraphTexts();
        int idx = -1;
        for (int i = 0; i < paras.Count; i++)
            if (paras[i].Text.Contains(find, StringComparison.Ordinal)) { idx = i; break; }
        if (idx < 0) return 0;

        var (exit, stdout, stderr) = RunPy(_path, outPath, idx.ToString(), find, replace);
        if (exit != 0) throw new Exception($"hwp edit failed: {stderr}");
        return 1;
    }

    public void Save(string outPath) { }

    static (int Exit, string Out, string Err) RunPy(params string[] args)
    {
        var pyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "hermes", "hermes-agent", "venv", "Scripts", "python.exe");
        var psi = new ProcessStartInfo(pyPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.ArgumentList.Add(PyScript);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, err);
    }
}

// ── zip+XML 본문 엔진 (docx / hwpx 공용) ──────────────────────────────────
internal sealed class ZipEngine : IDocEngine
{
    public static List<(string Name, byte[] Bytes)> RawParts(string path)
    {
        var list = new List<(string, byte[])>();
        using var zip = ZipFile.OpenRead(path);
        foreach (var e in zip.Entries)
            list.Add((e.FullName, ReadAll(e)));
        return list;
    }

    public static ZipEngine For(string path)
    {
        var parts = RawParts(path);
        string ext = Path.GetExtension(path).ToLowerInvariant();

        (Func<string, bool> match, Regex para, Regex span) fmt;
        switch (ext)
        {
            case ".docx":
                fmt = (
                    n => n == "word/document.xml",
                    new Regex("<w:p(?: [^>]*)?>.*?</w:p>", RegexOptions.Singleline),
                    new Regex("<w:t(?: [^>]*)?>[^<]*</w:t>"));
                break;
            case ".hwpx":
                fmt = (
                    n => SectionRe().IsMatch(n),
                    new Regex("<hp:p(?: [^>]*)?>.*?</hp:p>", RegexOptions.Singleline),
                    new Regex("<hp:t(?: [^>]*)?>[^<]*</hp:t>"));
                break;
            default:
                throw new Exception($"unsupported format: {ext}");
        }

        var candidates = parts.Where(p => fmt.match(p.Name)).ToList();
        if (candidates.Count == 0) throw new Exception("body part not found");
        if (candidates.Count > 1) throw new Exception("multiple body sections — not supported in v1");

        return new ZipEngine(parts, candidates[0].Name, candidates[0].Bytes, fmt.para, fmt.span);
    }

    static Regex? _section;
    static Regex SectionRe() => _section ??= new Regex("(?:^|/)section\\d+\\.xml$", RegexOptions.IgnoreCase);

    string PartName { get; }
    private string DocumentXml { get; set; }
    List<(string Name, byte[] Bytes)> Parts { get; }
    bool HasBom { get; }
    readonly Regex _para, _span;

    ZipEngine(List<(string Name, byte[] Bytes)> parts, string partName, byte[] partBytes, Regex paraRegex, Regex spanRegex)
    {
        Parts = parts;
        PartName = partName;
        _para = paraRegex;
        _span = spanRegex;
        HasBom = partBytes.Length >= 3 && partBytes[0] == 0xEF && partBytes[1] == 0xBB && partBytes[2] == 0xBF;
        DocumentXml = Encoding.UTF8.GetString(partBytes, HasBom ? 3 : 0, partBytes.Length - (HasBom ? 3 : 0));
    }

    public List<(int Index, string Text)> ParagraphTexts()
    {
        var result = new List<(int, string)>();
        int i = 0;
        foreach (Match m in _para.Matches(DocumentXml))
            result.Add((i++, SpanText(m.Value, _span)));
        return result;
    }

    public int Edit(string find, string replace, string outPath)
    {
        int n = ReplaceFirst(find, replace);
        if (n > 0) Save(outPath);
        return n;
    }

    int ReplaceFirst(string find, string replace)
    {
        int paraIdx = -1;
        int charStart = -1;
        foreach (Match m in _para.Matches(DocumentXml))
        {
            var text = SpanText(m.Value, _span);
            int at = text.IndexOf(find, StringComparison.Ordinal);
            if (at >= 0) { paraIdx = m.Index; charStart = at; break; }
        }
        if (paraIdx < 0) return 0;

        var paraMatch = _para.Match(DocumentXml, paraIdx);
        int paraBase = paraMatch.Index;
        var spans = new List<(int ts, int te, int cs, int len)>();
        foreach (Match m in _span.Matches(paraMatch.Value))
        {
            int tagEnd = m.Value.IndexOf('>', StringComparison.Ordinal);
            int ts = m.Index + tagEnd + 1;
            string inner = m.Value.Substring(tagEnd + 1, m.Value.Length - tagEnd - 1 - ClosingTagLen(_span));
            spans.Add((ts + paraBase, ts + inner.Length + paraBase, spans.Sum(s => s.len), inner.Length));
        }

        int s = charStart;
        int e = charStart + find.Length;
        string esc = XmlEscape(replace);

        int first = -1, last = -1;
        for (int i = 0; i < spans.Count; i++)
        {
            var (ts, te, cs, len) = spans[i];
            if (first < 0 && cs <= s && s < cs + len) first = i;
            if (cs < e && s < cs + len) last = i;
        }
        if (first < 0)
            first = spans.Count - 1;

        var edits = new List<(int start, int end, string replacement)>();
        void SetSpan(int i, string newText)
        {
            var (ts, te, _, _) = spans[i];
            edits.Add((ts, te, newText));
        }

        if (s == e)
        {
            var (ts, te, cs, len) = spans[first];
            SetSpan(first, DocumentXml.Substring(ts, s - cs) + esc + DocumentXml.Substring(ts + (s - cs), te - ts - (s - cs)));
        }
        else
        {
            {
                var (ts, te, cs, _) = spans[first];
                SetSpan(first, DocumentXml.Substring(ts, s - cs) + esc);
            }
            for (int i = first + 1; i < last; i++) SetSpan(i, "");
            if (last > first)
            {
                var (ts, te, cs, len) = spans[last];
                SetSpan(last, DocumentXml.Substring(ts + (e - cs), te - ts - (e - cs)));
            }
        }

        edits = edits.OrderByDescending(x => x.start).ToList();
        var sb = new StringBuilder(DocumentXml);
        foreach (var (start, end, replacement) in edits)
            sb.Remove(start, end - start).Insert(start, replacement);
        DocumentXml = sb.ToString();
        return 1;
    }

    public void Save(string outPath)
    {
        byte[] docBytes;
        if (HasBom)
            docBytes = Concat(new byte[] { 0xEF, 0xBB, 0xBF }, Encoding.UTF8.GetBytes(DocumentXml));
        else
            docBytes = Encoding.UTF8.GetBytes(DocumentXml);

        using var zip = new ZipArchive(new FileStream(outPath, FileMode.Create, FileAccess.ReadWrite), ZipArchiveMode.Create);
        foreach (var (name, bytes) in Parts)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var es = entry.Open();
            es.Write(name == PartName ? docBytes : bytes);
        }
    }

    static int ClosingTagLen(Regex spanRegex)
    {
        string p = spanRegex.ToString();
        int i = p.LastIndexOf("</", StringComparison.Ordinal);
        return p.Length - i;
    }

    static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Array.Copy(a, 0, r, 0, a.Length);
        Array.Copy(b, 0, r, a.Length, b.Length);
        return r;
    }

    static string XmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    static string SpanText(string paraXml, Regex spanRegex)
    {
        var sb = new StringBuilder();
        int closeLen = ClosingTagLen(spanRegex);
        foreach (Match m in spanRegex.Matches(paraXml))
        {
            int tagEnd = m.Value.IndexOf('>', StringComparison.Ordinal);
            sb.Append(m.Value, tagEnd + 1, m.Value.Length - tagEnd - 1 - closeLen);
        }
        return sb.ToString();
    }

    static byte[] ReadAll(ZipArchiveEntry e)
    {
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}

// ── 테스트 픽스처 생성기: 멀티 런, rsid, 코멘트, 변경 추적을 담은 현실적 docx ──
internal static class Fixture
{
    public static void Make(string outPath, string category)
    {
        var ext = Path.GetExtension(outPath).ToLowerInvariant();
        switch (ext)
        {
            case ".docx": MakeDocx(outPath, category); break;
            case ".hwpx": MakeHwpx(outPath, category); break;
            default: throw new Exception($"make-fixture: unsupported format {ext}");
        }
    }

    // ── .docx: 카테고리별 본문 (테이블/장문/보고서+코멘트+변경추적/그림) ──
    static void MakeDocx(string outPath, string category)
    {
        var (document, comments, media, docRelsExtra, ctExtra) = DocxBody(category);

        string contentTypes =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
            ctExtra +
            "</Types>";

        string rels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "</Relationships>";

        string docRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            docRelsExtra +
            "</Relationships>";

        string styles =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r" +
            "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
            "<w:style w:type=\"character\" w:styleId=\"CommentReference\"><w:name w:val=\"annotation reference\"/></w:style>" +
            "<w:style w:type=\"table\" w:styleId=\"TableGrid\"><w:name w:val=\"Table Grid\"/></w:style>" +
            "</w:styles>";

        using var zip = new ZipArchive(new FileStream(outPath, FileMode.Create, FileAccess.ReadWrite), ZipArchiveMode.Create);
        void AddStr(string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var es = e.Open();
            var b = Encoding.UTF8.GetBytes(content);
            es.Write(b, 0, b.Length);
        }
        void AddBytes(string name, byte[] bytes)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var es = e.Open();
            es.Write(bytes, 0, bytes.Length);
        }
        AddStr("[Content_Types].xml", contentTypes);
        AddStr("_rels/.rels", rels);
        AddStr("word/document.xml", document);
        AddStr("word/styles.xml", styles);
        if (comments != null) AddStr("word/comments.xml", comments);
        AddStr("word/_rels/document.xml.rels", docRels);
        if (media != null) AddBytes("word/media/image1.png", media);
    }

    static (string doc, string? comments, byte[]? media, string docRelsExtra, string ctExtra) DocxBody(string category)
    {
        switch (category)
        {
            case "form":
            {
                var table = Table(new[]
                {
                    new[] { "사업명", "스마트 팩토리 전환 지원" },
                    new[] { "신청기관", "중소 제조기업 (가칭)" },
                    new[] { "지원기간", "12개월" },
                });
                var doc = Header("정부 양식 — 지원 신청서") + table + P("상기 내용을 확인했습니다.");
                return (doc, null, null, "", "");
            }
            case "novel":
            {
                var paras = new[]
                {
                    "창밖으로 비가 내리고 있었다.",
                    "그는 창문을 열고 손을 뻗었다.",
                    "냉기가 손바닥에 닿았다.",
                    "도시의 불빛이 물 위에 번졌다.",
                    "그 순간, 그는 결심을 했다.",
                };
                var doc = Header("소설 원고 — 새벽") + string.Join("", paras.Select(t => P(t)));
                return (doc, null, null, "", "");
            }
            case "image":
            {
                var imgPara =
                    "<w:p><w:r><w:drawing>" +
                    "<wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
                    "<wp:extent cx=\"914400\" cy=\"914400\"/><wp:docPr id=\"1\" name=\"Image 1\"/>" +
                    "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
                    "<a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
                    "<pic:pic xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
                    "<pic:nvPicPr><pic:cNvPr id=\"1\" name=\"image1.png\"/><pic:cNvPicPr/></pic:nvPicPr>" +
                    "<pic:blipFill><a:blip r:embed=\"rId5\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"/></pic:blipFill>" +
                    "<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"914400\" cy=\"914400\"/></a:xfrm></pic:spPr>" +
                    "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";
                var doc = Header("그림 포함 문서") + P("아래는 첨부된 도판이다.") + imgPara;
                var ctExtra = "<Default Extension=\"png\" ContentType=\"image/png\"/>";
                var docRelsExtra = "<Relationship Id=\"rId5\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.png\"/>";
                return (doc, null, PngBytes(), docRelsExtra, ctExtra);
            }
            case "report":
            default:
            {
                var t1 = Table(new[]
                {
                    new[] { "단계", "기간" },
                    new[] { "설계", "3개월" },
                    new[] { "시행", "6개월" },
                });
                var t2 = Table(new[]
                {
                    new[] { "지표", "목표" },
                    new[] { "생산성", "+15%" },
                    new[] { "비용", "-10%" },
                });
                var insDel =
                    "<w:p><w:r w:rsidR=\"00112233\"><w:t xml:space=\"preserve\">총 </w:t></w:r>" +
                    "<w:ins w:id=\"1\" w:author=\"김작가\" w:date=\"2026-10-30T12:00:00Z\"><w:r><w:t>12개월간</w:t></w:r></w:ins>" +
                    "<w:del w:id=\"2\" w:author=\"김작가\" w:date=\"2026-10-30T12:05:00Z\"><w:r><w:delText>6개월간</w:delText></w:r></w:del>" +
                    "<w:r w:rsidR=\"00112233\"><w:t xml:space=\"preserve\"> 추진한다.</w:t></w:r></w:p>";
                var commentPara =
                    "<w:p><w:commentRangeStart w:id=\"0\"/><w:r w:rsidR=\"00445566\"><w:t>현장 작업자와 함께 실행 계획을 수립한다.</w:t></w:r>" +
                    "<w:commentRangeEnd w:id=\"0\"/><w:r><w:rPr><w:rStyle w:val=\"CommentReference\"/></w:rPr><w:commentReference w:id=\"0\"/></w:r></w:p>";
                var doc = Header("표 많은 보고서") + t1 + P("다음 표는 단계별 기간이다.") + t2 + insDel + commentPara;
                string comments =
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r" +
                    "<w:comments xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                    "<w:comment w:id=\"0\" w:author=\"박편집장\" w:date=\"2026-10-30T13:00:00Z\" w:initials=\"박\">" +
                    "<w:p><w:r><w:t>이 문장은 원고 메모에 근거해 주세요.</w:t></w:r></w:p></w:comment>" +
                    "</w:comments>";
                var ctExtra = "<Override PartName=\"/word/comments.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml\"/>";
                var docRelsExtra = "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments\" Target=\"comments.xml\"/>";
                return (doc, comments, null, docRelsExtra, ctExtra);
            }
        }
    }

    static string Header(string text) =>
        "<w:p w14:paraId=\"1A2B3C4D\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\">" +
        $"<w:r w:rsidR=\"00A1B2C3\"><w:rPr><w:b/><w:sz w:val=\"32\"/></w:rPr><w:t>{Esc(text)}</w:t></w:r></w:p>";

    static string P(string text, string rsid = "00A1B2C3") =>
        "<w:p w14:paraId=\"9C8D7E6F\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\">" +
        $"<w:r w:rsidR=\"{rsid}\"><w:t xml:space=\"preserve\">{Esc(text)}</w:t></w:r></w:p>";

    static string Table(string[][] rows)
    {
        int cols = rows[0].Length;
        var sb = new StringBuilder();
        sb.Append("<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid>");
        for (int c = 0; c < cols; c++) sb.Append("<w:gridCol w:w=\"4513\"/>");
        sb.Append("</w:tblGrid>");
        foreach (var row in rows)
        {
            sb.Append("<w:tr>");
            foreach (var cell in row)
                sb.Append($"<w:tc><w:tcPr><w:tcW w:w=\"4513\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>{Esc(cell)}</w:t></w:r></w:p></w:tc>");
            sb.Append("</w:tr>");
        }
        sb.Append("</w:tbl>");
        return sb.ToString();
    }

    static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ── .hwpx (OWPML): 장문 본문 (hp: 네임스페이스 경로 검증용) ─────────────
    static void MakeHwpx(string outPath, string category)
    {
        var (title, paras) = HwpxContent(category);
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r");
        sb.Append("<hp:body xmlns:hp=\"http://www.hancom.co.kr/hwpml/2017\">");
        sb.Append($"<hp:p><hp:r><hp:t>{Esc(title)}</hp:t></hp:r></hp:p>");
        foreach (var p in paras)
            sb.Append($"<hp:p><hp:r><hp:t xml:space=\"preserve\">{Esc(p)}</hp:t></hp:r></hp:p>");
        sb.Append("</hp:body>");
        var section0 = sb.ToString();

        using var zip = new ZipArchive(new FileStream(outPath, FileMode.Create, FileAccess.ReadWrite), ZipArchiveMode.Create);
        void AddStr(string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var es = e.Open();
            var b = Encoding.UTF8.GetBytes(content);
            es.Write(b, 0, b.Length);
        }
        AddStr("[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/Contents/section0.xml\" ContentType=\"application/xml\"/>" +
            "</Types>");
        AddStr("_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://www.hancom.co.kr/hwpml/2017#section\" Target=\"Contents/section0.xml\"/>" +
            "</Relationships>");
        AddStr("Contents/section0.xml", section0);
    }

    static (string title, string[] paras) HwpxContent(string category) => category switch
    {
        "form" => ("지원 신청서", new[] { "사업명: 스마트 팩토리 전환 지원", "신청기관: 중소 제조기업 (가칭)", "지원기간: 12개월" }),
        "novel" => ("새벽", new[] { "창밖으로 비가 내리고 있었다.", "그는 창문을 열고 손을 뻗었다.", "냉기가 손바닥에 닿았다." }),
        _ => ("보고서", new[] { "다음 표는 단계별 기간이다.", "총 12개월간 추진한다.", "현장 작업자와 함께 실행 계획을 수립한다." }),
    };

    // ── 최소 유효 PNG (1x1 빨강) — 그림 파트 보존 검증용 ──────────────────
    static byte[] PngBytes()
    {
        var png = new List<byte>();
        void Chunk(string type, byte[] data)
        {
            var tb = Encoding.ASCII.GetBytes(type);
            var len = BitConverter.GetBytes((uint)data.Length); Array.Reverse(len);
            png.AddRange(len); png.AddRange(tb); png.AddRange(data);
            var crc = Crc32(ConcatBytes(tb, data));
            var cb = BitConverter.GetBytes(crc); Array.Reverse(cb);
            png.AddRange(cb);
        }
        png.AddRange(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        Chunk("IHDR", new byte[] { 0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0 }); // 1x1 RGB
        Chunk("IDAT", Zlib(new byte[] { 0, 255, 0, 0 })); // filter=0, R,G,B
        Chunk("IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    static byte[] Zlib(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionMode.Compress, true))
            ds.Write(data, 0, data.Length);
        var deflated = ms.ToArray();
        var adler = Adler32(data);
        var result = new byte[2 + deflated.Length + 4];
        result[0] = 0x78; result[1] = 0x9C;
        Array.Copy(deflated, 0, result, 2, deflated.Length);
        var ab = BitConverter.GetBytes(adler); Array.Reverse(ab);
        Array.Copy(ab, 0, result, 2 + deflated.Length, 4);
        return result;
    }

    static uint Crc32(byte[] data)
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var x in data) { a = (a + x) & 0xFFFF; b = (b + a) & 0xFFFF; }
        return (b << 16) | a;
    }

    static byte[] ConcatBytes(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Array.Copy(a, 0, r, 0, a.Length);
        Array.Copy(b, 0, r, a.Length, b.Length);
        return r;
    }
}
