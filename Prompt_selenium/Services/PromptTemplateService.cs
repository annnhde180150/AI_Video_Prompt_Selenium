using PromptSelenium.Models;
using System.IO;

namespace PromptSelenium.Services;

public sealed class PromptTemplateService
{
    public const string StoryPlaceholder = "{{story}}";

    public PromptSet LoadPrompts()
    {
        var prompts = new PromptSet
        {
            Prompt1 = LoadPromptFile("Prompt1.md"),
            Prompt2 = LoadPromptFile("Prompt2.md"),
            Prompt3 = LoadPromptFile("Prompt3.md")
        };
        Validate(prompts);
        return prompts;
    }

    private static string LoadPromptFile(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Prompt", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Không tìm thấy Prompt/{fileName} trong thư mục ứng dụng.", path);
        }

        return File.ReadAllText(path).Trim();
    }

    public static void Validate(PromptSet prompts)
    {
        ValidateTemplate(prompts.Prompt1, "Prompt 1");
        ValidateFollowUpPrompt(prompts.Prompt2, "Prompt 2");
        ValidateFollowUpPrompt(prompts.Prompt3, "Prompt 3");
    }

    public static string RenderForStep(PromptSet prompts, int step, string story) =>
        step switch
        {
            1 => prompts.Prompt1.Replace(StoryPlaceholder, story, StringComparison.Ordinal),
            2 => prompts.Prompt2,
            3 => prompts.Prompt3,
            _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Prompt step must be from 1 to 3.")
        };

    private static void ValidateTemplate(string template, string name)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new InvalidDataException($"{name} không được để trống.");
        }

        if (!template.Contains(StoryPlaceholder, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{name} phải chứa {StoryPlaceholder}.");
        }
    }

    private static void ValidateFollowUpPrompt(string prompt, string name)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidDataException($"{name} không được để trống.");
        }

        if (prompt.Contains(StoryPlaceholder, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{name} không được chứa {StoryPlaceholder}. Prompt này được gửi tiếp trong cùng chat và dùng context đã có.");
        }
    }
}
