using PromptSelenium.Models;
using PromptSelenium.Services;

namespace PromptSelenium.Tests;

public sealed class PromptTemplateServiceTests
{
    [Fact]
    public void LoadPrompts_ReturnsAllBundledPromptsWithCorrectPlaceholderRules()
    {
        var prompts = new PromptTemplateService().LoadPrompts();

        Assert.Contains("SOURCE STORY:", prompts.Prompt1, StringComparison.Ordinal);
        Assert.Contains(PromptTemplateService.StoryPlaceholder, prompts.Prompt1, StringComparison.Ordinal);
        Assert.DoesNotContain(PromptTemplateService.StoryPlaceholder, prompts.Prompt2, StringComparison.Ordinal);
        Assert.DoesNotContain(PromptTemplateService.StoryPlaceholder, prompts.Prompt3, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(prompts.Prompt2));
        Assert.False(string.IsNullOrWhiteSpace(prompts.Prompt3));
    }

    [Fact]
    public void RenderForStep_InsertsStoryOnlyIntoPrompt1()
    {
        var prompts = new PromptSet
        {
            Prompt1 = "P1:{{story}}",
            Prompt2 = "P2: use the existing chat context",
            Prompt3 = "P3: finalize from the existing chat context"
        };

        PromptTemplateService.Validate(prompts);
        var input1 = PromptTemplateService.RenderForStep(prompts, 1, "original");
        var input2 = PromptTemplateService.RenderForStep(prompts, 2, "original");
        var input3 = PromptTemplateService.RenderForStep(prompts, 3, "original");

        Assert.Equal("P1:original", input1);
        Assert.Equal("P2: use the existing chat context", input2);
        Assert.Equal("P3: finalize from the existing chat context", input3);
    }

    [Fact]
    public void Validate_RejectsPrompt1WithoutPlaceholder()
    {
        var prompts = new PromptSet
        {
            Prompt1 = "missing",
            Prompt2 = "follow up 2",
            Prompt3 = "follow up 3"
        };

        var exception = Assert.Throws<InvalidDataException>(() => PromptTemplateService.Validate(prompts));
        Assert.Contains("Prompt 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsStoryPlaceholderInFollowUpPrompt()
    {
        var prompts = new PromptSet
        {
            Prompt1 = "P1:{{story}}",
            Prompt2 = "P2:{{story}}",
            Prompt3 = "follow up 3"
        };

        var exception = Assert.Throws<InvalidDataException>(() => PromptTemplateService.Validate(prompts));
        Assert.Contains("Prompt 2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("không được chứa", exception.Message, StringComparison.Ordinal);
    }
}
