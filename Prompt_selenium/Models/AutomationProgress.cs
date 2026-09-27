namespace PromptSelenium.Models;

public sealed record AutomationProgress(
    int Completed,
    int Total,
    int Worker,
    int StoryNumber,
    int? PromptStep,
    string Message);
