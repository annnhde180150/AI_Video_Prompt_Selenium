using ClosedXML.Excel;
using PromptSelenium.Models;
using System.IO;

namespace PromptSelenium.Services;

public sealed class ExcelService
{
    private const int MaxExcelCellCharacters = 32_767;

    public IReadOnlyList<StoryInput> ReadStories(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Không tìm thấy file Excel đầu vào.", path);
        }

        using var workbook = new XLWorkbook(path);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException("Workbook không có worksheet.");
        var storyColumn = worksheet.Row(1).CellsUsed()
            .FirstOrDefault(cell => string.Equals(cell.GetString().Trim(), "Story", StringComparison.OrdinalIgnoreCase))
            ?.Address.ColumnNumber;

        if (storyColumn is null)
        {
            throw new InvalidDataException("Hàng đầu tiên phải có cột 'Story'.");
        }

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        var stories = new List<StoryInput>();
        for (var row = 2; row <= lastRow; row++)
        {
            var value = worksheet.Cell(row, storyColumn.Value).GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                stories.Add(new StoryInput(stories.Count, row, value.Trim()));
            }
        }

        if (stories.Count == 0)
        {
            throw new InvalidDataException("Cột 'Story' không có dữ liệu.");
        }

        return stories;
    }

    public void WriteResults(string path, IReadOnlyList<StoryResult> results)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Final Stories");
        worksheet.Cell(1, 1).Value = "Prompt 1 Output";
        worksheet.Cell(1, 2).Value = "Prompt 2 Output";
        worksheet.Cell(1, 3).Value = "Prompt 3 Output";
        worksheet.Range(1, 1, 1, 3).Style.Font.Bold = true;

        var outputRow = 2;
        foreach (var result in results.OrderBy(result => result.Index))
        {
            var promptOutputs = new[]
            {
                SplitForExcel(result.Prompt1Output),
                SplitForExcel(result.Prompt2Output),
                SplitForExcel(result.Prompt3Output)
            };
            var rowCount = promptOutputs.Max(chunks => chunks.Count);

            for (var chunkIndex = 0; chunkIndex < rowCount; chunkIndex++)
            {
                for (var columnIndex = 0; columnIndex < promptOutputs.Length; columnIndex++)
                {
                    var chunks = promptOutputs[columnIndex];
                    worksheet.Cell(outputRow, columnIndex + 1).Value =
                        chunkIndex < chunks.Count ? chunks[chunkIndex] : string.Empty;
                }

                outputRow++;
            }
        }

        worksheet.Columns(1, 3).Width = 100;
        worksheet.Columns(1, 3).Style.Alignment.WrapText = true;
        worksheet.SheetView.FreezeRows(1);
        workbook.SaveAs(path);
    }

    private static IReadOnlyList<string> SplitForExcel(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [string.Empty];
        }

        var chunks = new List<string>();
        var offset = 0;
        while (offset < text.Length)
        {
            var length = Math.Min(MaxExcelCellCharacters, text.Length - offset);

            // Do not split a Unicode surrogate pair (for example an emoji) between two cells.
            if (offset + length < text.Length
                && char.IsHighSurrogate(text[offset + length - 1])
                && char.IsLowSurrogate(text[offset + length]))
            {
                length--;
            }

            chunks.Add(text.Substring(offset, length));
            offset += length;
        }

        return chunks;
    }
}
