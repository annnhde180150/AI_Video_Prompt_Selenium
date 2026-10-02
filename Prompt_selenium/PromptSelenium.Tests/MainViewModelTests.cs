using PromptSelenium.Models;
using PromptSelenium.Services;
using PromptSelenium.ViewModels;

namespace PromptSelenium.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void RunMode_DefaultsToThreePromptsAndCanSwitchToTwoPrompts()
    {
        using var viewModel = new MainViewModel();

        Assert.Equal(WorkerCountPolicy.Default, viewModel.WorkerCount);
        Assert.Equal(10, viewModel.WorkerCount);
        Assert.Equal(PromptRunMode.ThreePrompts, viewModel.RunMode);
        Assert.True(viewModel.IsThreePromptMode);
        Assert.False(viewModel.IsTwoPromptMode);
        Assert.Contains("10 ChatGPT web workers", viewModel.WorkflowSummary, StringComparison.Ordinal);
        Assert.Contains("Prompt 3", viewModel.WorkflowSummary, StringComparison.Ordinal);

        viewModel.IsTwoPromptMode = true;

        Assert.Equal(PromptRunMode.TwoPrompts, viewModel.RunMode);
        Assert.True(viewModel.IsTwoPromptMode);
        Assert.False(viewModel.IsThreePromptMode);
        Assert.DoesNotContain("Prompt 3", viewModel.WorkflowSummary, StringComparison.Ordinal);

        viewModel.WorkerCount = 7;

        Assert.Equal(PromptRunMode.TwoPrompts, viewModel.RunMode);
        Assert.Contains("7 ChatGPT web workers", viewModel.WorkflowSummary, StringComparison.Ordinal);
    }
}
