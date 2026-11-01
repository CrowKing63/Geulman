using System.IO;
using System.IO.Compression;
using System.Text;

namespace Geulman.Tests;

public class RoundtripTests
{
    // test/corpus and test/hwp relative to built assembly (test/Geulman.Tests/bin/Debug/net8.0/)
    static string TestRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    static string CorpusDir => Path.Combine(TestRoot, "corpus");
    static string HwpDir => Path.Combine(TestRoot, "hwp");
    static string Scratch => Path.GetTempPath();

    // ── 2b: docx round-trip — 편집 없음 → 모든 zip 파트 byte-identical ───────
    [Theory]
    [InlineData("docx-form.docx")]
    [InlineData("docx-novel.docx")]
    [InlineData("docx-report.docx")]
    [InlineData("docx-image.docx")]
    [InlineData("docx-tables.docx")]
    [InlineData("docx-footnote.docx")]
    [InlineData("docx-mixed.docx")]
    public void Docx_Roundtrip_AllPartsByteIdentical(string filename)
    {
        var inPath = Path.Combine(CorpusDir, filename);
        var outPath = Path.Combine(Scratch, $"rt-{filename}");

        var doc = Engines.For(inPath);
        doc.Save(outPath);

        var a = ZipEngine.RawParts(inPath);
        var b = ZipEngine.RawParts(outPath);

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Name, b[i].Name);
            Assert.True(a[i].Bytes.AsSpan().SequenceEqual(b[i].Bytes),
                $"Part {a[i].Name} differs ({a[i].Bytes.Length} vs {b[i].Bytes.Length} bytes)");
        }
    }

    // ── 2c: hwpx round-trip — 편집 없음 → 모든 zip 파트 byte-identical ───────
    [Theory]
    [InlineData("hwpx-form.hwpx")]
    [InlineData("hwpx-novel.hwpx")]
    public void Hwpx_Roundtrip_AllPartsByteIdentical(string filename)
    {
        var inPath = Path.Combine(CorpusDir, filename);
        var outPath = Path.Combine(Scratch, $"rt-{filename}");

        var doc = Engines.For(inPath);
        doc.Save(outPath);

        var a = ZipEngine.RawParts(inPath);
        var b = ZipEngine.RawParts(outPath);

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Name, b[i].Name);
            Assert.True(a[i].Bytes.AsSpan().SequenceEqual(b[i].Bytes),
                $"Part {a[i].Name} differs");
        }
    }

    // ── 2d: edited round-trip — 변경 문단만 diff, fidelity markers 불변 ───────
    [Fact]
    public void Docx_EditedRoundtrip_OnlyChangedParaDiffers()
    {
        var inPath = Path.Combine(CorpusDir, "docx-report.docx");
        var outPath = Path.Combine(Scratch, "edited-report.docx");

        var doc = Engines.For(inPath);
        var paras = doc.ParagraphTexts();
        // Change paragraph 14: "총 12개월간 추진한다." → "총 24개월간 추진한다."
        doc.SaveEdited(new List<(int, string)> { (14, "총 24개월간 추진한다.") }, outPath);

        // Verify: only paragraph 14 changed
        var orig = Engines.For(inPath).ParagraphTexts();
        var edited = Engines.For(outPath).ParagraphTexts();
        Assert.Equal(orig.Count, edited.Count);
        for (int i = 0; i < orig.Count; i++)
        {
            if (i == 14)
                Assert.NotEqual(orig[i].Text, edited[i].Text);
            else
                Assert.Equal(orig[i].Text, edited[i].Text);
        }

        // Fidelity: comments and tracking markers preserved
        int CountIn(string path, string part, string token)
        {
            foreach (var (name, bytes) in ZipEngine.RawParts(path))
                if (name == part)
                    return Encoding.UTF8.GetString(bytes).Split(token, StringSplitOptions.None).Length - 1;
            return 0;
        }
        Assert.Equal(CountIn(inPath, "word/comments.xml", "<w:comment "),
                    CountIn(outPath, "word/comments.xml", "<w:comment "));
        Assert.Equal(CountIn(inPath, "word/document.xml", "<w:ins"),
                    CountIn(outPath, "word/document.xml", "<w:ins"));
        Assert.Equal(CountIn(inPath, "word/document.xml", "<w:del"),
                    CountIn(outPath, "word/document.xml", "<w:del"));
    }

    // ── 2f: replace-all — 여러 occurrence 문서에서 전체 교체 ─────────────────
    [Fact]
    public void Docx_ReplaceAll_AllOccurrencesReplaced()
    {
        var inPath = Path.Combine(CorpusDir, "docx-novel.docx");
        var outPath = Path.Combine(Scratch, "ral-novel.docx");

        var doc = Engines.For(inPath);
        int n = doc.ReplaceAll("그는", "그녀는", outPath);
        Assert.True(n >= 2, $"Expected at least 2 replacements, got {n}");

        // Verify no remaining "그는" in any paragraph
        var edited = Engines.For(outPath).ParagraphTexts();
        foreach (var (_, text) in edited)
            Assert.DoesNotContain("그는", text);
    }

    // ── 2e: .hwp save-edited — 문단 텍스트 대조 ───────────────────────────────
    [Fact]
    public void Hwp_SaveEdited_OnlyChangedParaDiffers()
    {
        var inPath = Path.Combine(HwpDir, "original.hwp");
        var outPath = Path.Combine(Scratch, "hwp-edited.hwp");

        var doc = Engines.For(inPath);
        var paras = doc.ParagraphTexts();
        Assert.True(paras.Count > 10, "Expected a multi-paragraph hwp");

        // Change paragraph 1: "연구계획서" → "연구 계획서" (length change)
        doc.SaveEdited(new List<(int, string)> { (1, "연구 계획서") }, outPath);

        var orig = Engines.For(inPath).ParagraphTexts();
        var edited = Engines.For(outPath).ParagraphTexts();
        Assert.Equal(orig.Count, edited.Count);
        for (int i = 0; i < orig.Count; i++)
        {
            if (i == 1)
                Assert.NotEqual(orig[i].Text, edited[i].Text);
            else
                Assert.Equal(orig[i].Text, edited[i].Text);
        }
    }
}
