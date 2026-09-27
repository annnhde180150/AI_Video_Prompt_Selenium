using ClosedXML.Excel;
using PromptSelenium.Models;
using PromptSelenium.Services;

namespace PromptSelenium.Tests;

public sealed class ExcelServiceTests
{
    [Fact]
    public void ReadAndWrite_PreservesInputOrderAndAllPromptOutputs()
    {
        var inputPath = TempPath();
        var outputPath = TempPath();
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Input");
                sheet.Cell(1, 1).Value = "Story";
                sheet.Cell(2, 1).Value = "First";
                sheet.Cell(3, 1).Value = " ";
                sheet.Cell(4, 1).Value = "Second";
                workbook.SaveAs(inputPath);
            }

            var service = new ExcelService();
            var stories = service.ReadStories(inputPath);
            Assert.Equal(2, stories.Count);
            Assert.Equal(2, stories[0].ExcelRow);
            Assert.Equal(4, stories[1].ExcelRow);

            service.WriteResults(outputPath,
            [
                new StoryResult(1, 4, "P1 second", "P2 second", "P3 second"),
                new StoryResult(0, 2, "P1 first", "P2 first", "P3 first")
            ]);

            using var output = new XLWorkbook(outputPath);
            var outputSheet = output.Worksheet(1);
            Assert.Equal("Prompt 1 Output", outputSheet.Cell(1, 1).GetString());
            Assert.Equal("Prompt 2 Output", outputSheet.Cell(1, 2).GetString());
            Assert.Equal("Prompt 3 Output", outputSheet.Cell(1, 3).GetString());
            Assert.Equal("P1 first", outputSheet.Cell(2, 1).GetString());
            Assert.Equal("P2 first", outputSheet.Cell(2, 2).GetString());
            Assert.Equal("P3 first", outputSheet.Cell(2, 3).GetString());
            Assert.Equal("P1 second", outputSheet.Cell(3, 1).GetString());
            Assert.Equal("P2 second", outputSheet.Cell(3, 2).GetString());
            Assert.Equal("P3 second", outputSheet.Cell(3, 3).GetString());
            Assert.Equal(3, outputSheet.LastColumnUsed()!.ColumnNumber());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void WriteResults_SplitsLongOutputsIntoCellsInTheirOwnColumnsWithoutDataLoss()
    {
        var outputPath = TempPath();
        var prompt1Output = new string('A', 70_000);
        var prompt2Output = new string('B', 32_768);
        var prompt3Output = string.Concat(Enumerable.Repeat("\U0001F642", 20_000));

        try
        {
            var service = new ExcelService();
            service.WriteResults(outputPath,
            [
                new StoryResult(0, 2, prompt1Output, prompt2Output, prompt3Output)
            ]);

            using var output = new XLWorkbook(outputPath);
            var outputSheet = output.Worksheet(1);
            var lastRow = outputSheet.LastRowUsed()!.RowNumber();

            Assert.Equal(4, lastRow);
            Assert.Equal(3, outputSheet.LastColumnUsed()!.ColumnNumber());

            for (var row = 2; row <= lastRow; row++)
            {
                for (var column = 1; column <= 3; column++)
                {
                    Assert.True(outputSheet.Cell(row, column).GetString().Length <= 32_767);
                }
            }

            Assert.Equal(prompt1Output, JoinColumn(outputSheet, 1, lastRow));
            Assert.Equal(prompt2Output, JoinColumn(outputSheet, 2, lastRow));
            Assert.Equal(prompt3Output, JoinColumn(outputSheet, 3, lastRow));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    private static string JoinColumn(IXLWorksheet worksheet, int column, int lastRow) =>
        string.Concat(Enumerable.Range(2, lastRow - 1)
            .Select(row => worksheet.Cell(row, column).GetString()));

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"PromptSelenium-{Guid.NewGuid():N}.xlsx");
}
