using PromptSelenium.Services;

namespace PromptSelenium.Tests;

public sealed class ChatGptBrowserServiceTests
{
    [Theory]
    [InlineData("Something went wrong. Please try again later.")]
    [InlineData("You've reached our limits of messages. Please try again later.")]
    [InlineData("Network error. Please check your connection.")]
    [InlineData("Đã xảy ra lỗi. Vui lòng thử lại.")]
    public void IsKnownChatGptErrorMessage_AcceptsActualErrorText(string text)
    {
        Assert.True(ChatGptBrowserService.IsKnownChatGptErrorMessage(text));
    }

    [Theory]
    [InlineData("The first time he called me after something went wrong, I felt my entire body brace.")]
    [InlineData("She said there was a network error in the old report.")]
    [InlineData("The story says you've reached the point of no return.")]
    public void IsKnownChatGptErrorMessage_DoesNotTreatStoryContentAsAnError(string text)
    {
        Assert.False(ChatGptBrowserService.IsKnownChatGptErrorMessage(text));
    }
}
