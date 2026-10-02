using PromptSelenium.Services;
using PromptSelenium.Models;

namespace PromptSelenium.Tests;

public sealed class WorkerCountPolicyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void Validate_AcceptsSupportedWorkerCounts(int workerCount)
    {
        WorkerCountPolicy.Validate(workerCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Validate_RejectsUnsupportedWorkerCounts(int workerCount)
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            WorkerCountPolicy.Validate(workerCount));

        Assert.Contains("1 đến 10", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChromeLauncher_RejectsUnsupportedTabCountBeforeLaunching()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            new ChromeLauncher().Launch("missing-chrome.exe", 9222, 11));

        Assert.Contains("1 đến 10", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserService_RejectsUnsupportedWorkerCountBeforeConnecting()
    {
        using var browserService = new ChatGptBrowserService();

        var exception = Assert.Throws<InvalidDataException>(() =>
            browserService.GetWorkerPages(11));

        Assert.Contains("1 đến 10", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutomationService_RejectsUnsupportedWorkerCountBeforeReadingPages()
    {
        using var browserService = new ChatGptBrowserService();
        var service = new StoryAutomationService(browserService);
        var prompts = new PromptSet
        {
            Prompt1 = "P1: {{story}}",
            Prompt2 = "P2"
        };

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ProcessAsync(
                [],
                prompts,
                PromptRunMode.TwoPrompts,
                11,
                TimeSpan.FromMinutes(1),
                null,
                CancellationToken.None));

        Assert.Contains("1 đến 10", exception.Message, StringComparison.Ordinal);
    }
}
