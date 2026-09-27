namespace PromptSelenium.Models;

public sealed record StoryResult(
    int Index,
    int ExcelRow,
    string Prompt1Output,
    string Prompt2Output,
    string Prompt3Output);
