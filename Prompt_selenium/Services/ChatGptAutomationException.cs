namespace PromptSelenium.Services;

public sealed class ChatGptAutomationException : Exception
{
    public ChatGptAutomationException(int worker, int storyNumber, int excelRow, int promptStep, Exception innerException)
        : base($"Worker {worker} - Story {storyNumber} (Excel row {excelRow}) - Prompt {promptStep}: {innerException.Message}", innerException)
    {
        Worker = worker;
        StoryNumber = storyNumber;
        ExcelRow = excelRow;
        PromptStep = promptStep;
    }

    public int Worker { get; }
    public int StoryNumber { get; }
    public int ExcelRow { get; }
    public int PromptStep { get; }
}
