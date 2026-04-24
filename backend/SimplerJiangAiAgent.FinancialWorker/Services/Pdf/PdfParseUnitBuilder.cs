using SimplerJiangAiAgent.FinancialWorker.Models;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

/// <summary>
/// 由 <see cref="PdfExtractionResult"/> + <see cref="ParsedFinancialStatements"/>
/// 构造 v0.4.1 §9.1 要求的解析单元（含 page_start / page_end / block_kind 三字段）。
///
/// 兜底策略：
/// 1. Table 单元：直接复用 <see cref="ExtractedTable.PageNumber"/>（PdfPig 1-based），
///    page_start = page_end = PageNumber。若 PageNumber &lt; 1 则丢弃并视为缺页降级。
/// 2. Narrative 单元：在 <see cref="PdfExtractionResult.Pages"/> 中按页扫描区段标记，
///    page_start = 第一处出现该区段标记的页（1-based）；
///    page_end = 下一区段标记出现页 - 1，未找到时回退为 PageCount（单页报表则 = page_start）。
///    若所有页都找不到任何标记，但 ParsedFinancialStatements 又有该区段数据，
///    则 page_start 回退为 1、page_end 回退为 PageCount，并通过 Snippet 标注 fallback。
///    page_start = 0 视为缺页降级，绝不落库。
/// 3. FigureCaption 单元：当前提取链路尚未生产，留作 v0.4.x 后续步骤接入；
///    Builder 暴露 <see cref="BuildFigureCaption"/> 供未来调用，本步骤不会主动产生。
/// </summary>
public static class PdfParseUnitBuilder
{
    /// <summary>三主表区段的中文标记（顺序无关，命中即视为该区段）。</summary>
    private static readonly Dictionary<string, string[]> SectionMarkers = new()
    {
        ["BalanceSheet"] = ["合并资产负债表", "资产负债表", "BALANCE SHEET"],
        ["IncomeStatement"] = ["合并利润表", "利润表", "损益表", "INCOME STATEMENT", "PROFIT AND LOSS"],
        ["CashFlowStatement"] = ["合并现金流量表", "现金流量表", "CASH FLOW STATEMENT"],
    };

    /// <summary>
    /// 从提取结果与解析后的三主表数据构造解析单元数组。
    /// page_start = 0 的单元会被丢弃（v0.4.1 §9.1 验收标准：缺页降级拒收）。
    /// </summary>
    public static List<PdfParseUnit> Build(
        PdfExtractionResult extraction,
        ParsedFinancialStatements parsed)
    {
        var units = new List<PdfParseUnit>();
        if (extraction == null) return units;

        // ─── narrative_section：三主表 ───
        var pageCount = extraction.Pages.Count;
        var sectionFieldCounts = new Dictionary<string, int>
        {
            ["BalanceSheet"] = parsed?.BalanceSheet?.Count ?? 0,
            ["IncomeStatement"] = parsed?.IncomeStatement?.Count ?? 0,
            ["CashFlowStatement"] = parsed?.CashFlowStatement?.Count ?? 0,
        };

        // 先扫一遍每个区段在哪一页起始
        var sectionStartPage = new Dictionary<string, int>();
        foreach (var (section, markers) in SectionMarkers)
        {
            sectionStartPage[section] = FindFirstPageContainingAny(extraction.Pages, markers);
        }

        foreach (var (section, fieldCount) in sectionFieldCounts)
        {
            if (fieldCount <= 0) continue; // 无解析数据则不产生该单元

            var pageStart = sectionStartPage[section];
            var fallback = false;

            if (pageStart <= 0)
            {
                // 标记找不到但又有解析数据：极少见，回退为整文件范围
                if (pageCount >= 1)
                {
                    pageStart = 1;
                    fallback = true;
                }
                else
                {
                    // 完全没有页（空 PDF），缺页降级 → 拒收
                    continue;
                }
            }

            int pageEnd = ComputeSectionEnd(section, pageStart, sectionStartPage, pageCount);

            var unit = new PdfParseUnit
            {
                BlockKind = PdfBlockKind.NarrativeSection,
                PageStart = pageStart,
                PageEnd = pageEnd,
                SectionName = section,
                FieldCount = fieldCount,
                Snippet = fallback
                    ? "[fallback] 页码未命中区段标记，已回退为整文件范围"
                    : null,
            };

            // v0.4.2 NS4: populate ExtractedText from page range
            var textParts = new List<string>();
            for (int p = pageStart; p <= Math.Min(pageEnd, pageCount); p++)
            {
                textParts.Add(extraction.Pages[p - 1]);
            }
            unit.ExtractedText = string.Join("\n", textParts);

            // v0.4.2 NS4: populate ParsedFields from parsed statements
            if (parsed != null && section != null)
            {
                unit.ParsedFields = section switch
                {
                    "BalanceSheet" => parsed.BalanceSheet?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
                    "IncomeStatement" => parsed.IncomeStatement?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
                    "CashFlowStatement" => parsed.CashFlowStatement?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
                    _ => null
                };
            }

            if (unit.IsValid) units.Add(unit);
        }

        // ─── table：抽取器表格 ───
        foreach (var table in extraction.Tables ?? new List<ExtractedTable>())
        {
            if (table.PageNumber < 1) continue; // 缺页降级 → 拒收
            var unit = new PdfParseUnit
            {
                BlockKind = PdfBlockKind.Table,
                PageStart = table.PageNumber,
                PageEnd = table.PageNumber,
                SectionName = table.NearbyHeading,
                FieldCount = table.Rows?.Count ?? 0,
            };
            if (unit.IsValid) units.Add(unit);
        }

        return units;
    }

    /// <summary>
    /// 显式构造一个 figure_caption 解析单元（供后续提取层接入）。
    /// page_start &lt; 1 会返回 null（缺页降级 → 拒收）。
    /// </summary>
    public static PdfParseUnit? BuildFigureCaption(int pageStart, int pageEnd, string? caption = null)
    {
        if (pageStart < 1) return null;
        if (pageEnd < pageStart) pageEnd = pageStart;
        return new PdfParseUnit
        {
            BlockKind = PdfBlockKind.FigureCaption,
            PageStart = pageStart,
            PageEnd = pageEnd,
            SectionName = "FigureCaption",
            Snippet = caption,
        };
    }

    private static int FindFirstPageContainingAny(IReadOnlyList<string> pages, string[] markers)
    {
        if (pages == null) return 0;
        for (var i = 0; i < pages.Count; i++)
        {
            var pageText = pages[i] ?? string.Empty;
            foreach (var marker in markers)
            {
                if (pageText.IndexOf(marker, StringComparison.Ordinal) >= 0)
                    return i + 1; // 1-based
            }
        }
        return 0;
    }

    private static int ComputeSectionEnd(
        string section,
        int pageStart,
        Dictionary<string, int> sectionStartPage,
        int pageCount)
    {
        // 找到所有「在本节之后」的下一节起始页，取最小值即本节结尾的下一页
        var nextStart = int.MaxValue;
        foreach (var (other, otherStart) in sectionStartPage)
        {
            if (other == section) continue;
            if (otherStart > pageStart && otherStart < nextStart)
                nextStart = otherStart;
        }

        int pageEnd;
        if (nextStart == int.MaxValue)
        {
            // 没有下一节：本节延伸到 PDF 末尾
            pageEnd = Math.Max(pageStart, pageCount);
        }
        else
        {
            pageEnd = Math.Max(pageStart, nextStart - 1);
        }
        return pageEnd;
    }
}
