using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using SimplerJiangAiAgent.FinancialWorker.Models;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Rag;

public class FinancialReportChunker : IChunker
{
    private const int MaxChunkLength = 800;
    private const int TargetChunkLength = 600;
    private const int OverlapLength = 80;

    // Chinese heading patterns
    private static readonly Regex HeadingRegex = new(
        @"^(?:" +
        @"[一二三四五六七八九十]+、" +                 // 一、二、三、
        @"|（[一二三四五六七八九十]+）" +               // （一）（二）
        @"|\([一二三四五六七八九十]+\)" +               // (一)(二)
        @"|第[一二三四五六七八九十百]+[章节条款]" +      // 第一章 第二节
        @"|\d+[、.]" +                                  // 1、 2. 3、
        @"|[ⅠⅡⅢⅣⅤⅥⅦⅧⅨⅩ]" +                        // Roman numerals
        @")",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex SentenceEndRegex = new(
        @"[。！？\.\!\?]",
        RegexOptions.Compiled);

    public List<FinancialChunk> Chunk(PdfFileDocument document)
    {
        var chunks = new List<FinancialChunk>();
        var sourceId = document.Id.ToString();

        // Strategy: Use ParseUnits if available, otherwise fall back to FullTextPages
        if (document.ParseUnits.Count > 0)
        {
            ChunkFromParseUnits(document, sourceId, chunks);
        }

        // If no chunks from ParseUnits, fall back to FullTextPages
        if (chunks.Count == 0 && document.FullTextPages.Count > 0)
        {
            ChunkFromFullTextPages(document, sourceId, chunks);
        }

        return chunks;
    }

    private void ChunkFromParseUnits(PdfFileDocument doc, string sourceId, List<FinancialChunk> chunks)
    {
        foreach (var unit in doc.ParseUnits)
        {
            if (!unit.IsValid) continue;

            if (unit.BlockKind == PdfBlockKind.Table)
            {
                // Layer 3: Table isolation
                var tableText = unit.ParsedFields != null && unit.ParsedFields.Count > 0
                    ? JsonSerializer.Serialize(unit.ParsedFields, new JsonSerializerOptions
                        { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
                    : unit.ExtractedText ?? unit.Snippet ?? "";

                if (string.IsNullOrWhiteSpace(tableText)) continue;

                chunks.Add(CreateChunk(doc, sourceId, tableText, "table",
                    unit.SectionName, unit.PageStart, unit.PageEnd));
            }
            else
            {
                // Layer 1+2: Narrative sections
                var text = unit.ExtractedText ?? GetTextForPageRange(doc, unit.PageStart, unit.PageEnd);
                if (string.IsNullOrWhiteSpace(text)) continue;

                var sectionChunks = SplitByHeadingsAndParagraphs(text);
                foreach (var sc in sectionChunks)
                {
                    chunks.Add(CreateChunk(doc, sourceId, sc.Text, "prose",
                        sc.Section ?? unit.SectionName, unit.PageStart, unit.PageEnd));
                }
            }
        }
    }

    private void ChunkFromFullTextPages(PdfFileDocument doc, string sourceId, List<FinancialChunk> chunks)
    {
        var fullText = new StringBuilder();
        foreach (var page in doc.FullTextPages.OrderBy(p => p.PageNumber))
        {
            fullText.AppendLine(page.Text);
        }

        var text = fullText.ToString();
        if (string.IsNullOrWhiteSpace(text)) return;

        var sectionChunks = SplitByHeadingsAndParagraphs(text);
        var firstPage = doc.FullTextPages.Min(p => p.PageNumber);
        var lastPage = doc.FullTextPages.Max(p => p.PageNumber);

        foreach (var sc in sectionChunks)
        {
            chunks.Add(CreateChunk(doc, sourceId, sc.Text, "prose",
                sc.Section, firstPage, lastPage));
        }
    }

    /// <summary>
    /// Layer 1: Split text by headings. Layer 2: Split oversized sections into paragraphs.
    /// </summary>
    private List<(string? Section, string Text)> SplitByHeadingsAndParagraphs(string text)
    {
        var result = new List<(string? Section, string Text)>();

        var sections = SplitByHeadings(text);

        foreach (var (heading, content) in sections)
        {
            if (string.IsNullOrWhiteSpace(content)) continue;

            if (content.Length <= MaxChunkLength)
            {
                result.Add((heading, content.Trim()));
            }
            else
            {
                // Layer 2: Paragraph/sentence splitting for oversized sections
                var subChunks = SplitLongText(content);
                for (int i = 0; i < subChunks.Count; i++)
                {
                    var section = heading != null ? $"{heading}（{i + 1}/{subChunks.Count}）" : null;
                    result.Add((section, subChunks[i].Trim()));
                }
            }
        }

        return result;
    }

    private List<(string? Heading, string Content)> SplitByHeadings(string text)
    {
        var lines = text.Split('\n');
        var sections = new List<(string? Heading, string Content)>();
        string? currentHeading = null;
        var currentContent = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (HeadingRegex.IsMatch(trimmed) && trimmed.Length < 100)
            {
                // Save previous section
                if (currentContent.Length > 0)
                {
                    sections.Add((currentHeading, currentContent.ToString()));
                    currentContent.Clear();
                }
                currentHeading = trimmed.TrimEnd();
                // Include heading in content too
                currentContent.AppendLine(trimmed);
            }
            else
            {
                currentContent.AppendLine(line);
            }
        }

        if (currentContent.Length > 0)
        {
            sections.Add((currentHeading, currentContent.ToString()));
        }

        return sections;
    }

    /// <summary>
    /// Split long text into chunks of 512-800 chars with ~80 char overlap.
    /// Prefer splitting at paragraph boundaries or sentence ends.
    /// </summary>
    private List<string> SplitLongText(string text)
    {
        var chunks = new List<string>();
        var remaining = text;

        while (remaining.Length > 0)
        {
            if (remaining.Length <= MaxChunkLength)
            {
                chunks.Add(remaining);
                break;
            }

            var splitAt = FindSplitPoint(remaining, TargetChunkLength, MaxChunkLength);
            chunks.Add(remaining[..splitAt]);

            // Apply overlap
            var overlapStart = Math.Max(0, splitAt - OverlapLength);
            remaining = remaining[overlapStart..];
        }

        return chunks;
    }

    private int FindSplitPoint(string text, int target, int max)
    {
        var searchEnd = Math.Min(max, text.Length);
        var searchStart = Math.Max(target / 2, 1);

        // Try paragraph boundary first (double newline)
        var searchLen = searchEnd - searchStart;
        if (searchLen > 0)
        {
            var paraIdx = text.LastIndexOf("\n\n", searchEnd - 1, searchLen);
            if (paraIdx >= searchStart)
                return paraIdx + 2;
        }

        // Try sentence boundary
        if (searchEnd > searchStart)
        {
            var match = SentenceEndRegex.Match(text, searchStart, searchEnd - searchStart);
            if (match.Success)
                return match.Index + match.Length;
        }

        // Try single newline
        if (searchLen > 0)
        {
            var nlIdx = text.LastIndexOf('\n', searchEnd - 1, searchLen);
            if (nlIdx >= searchStart)
                return nlIdx + 1;
        }

        // Hard cut at target
        return Math.Min(target, text.Length);
    }

    private string GetTextForPageRange(PdfFileDocument doc, int pageStart, int pageEnd)
    {
        var sb = new StringBuilder();
        foreach (var page in doc.FullTextPages
            .Where(p => p.PageNumber >= pageStart && p.PageNumber <= pageEnd)
            .OrderBy(p => p.PageNumber))
        {
            sb.AppendLine(page.Text);
        }
        return sb.ToString();
    }

    private FinancialChunk CreateChunk(PdfFileDocument doc, string sourceId, string text, string blockKind,
        string? section, int? pageStart, int? pageEnd)
    {
        return new FinancialChunk
        {
            SourceId = sourceId,
            Symbol = doc.Symbol,
            ReportDate = doc.ReportPeriod,
            ReportType = doc.ReportType,
            Section = section,
            BlockKind = blockKind,
            PageStart = pageStart,
            PageEnd = pageEnd,
            Text = text,
            TokenizedText = ""  // Filled by pipeline after tokenization
        };
    }
}
