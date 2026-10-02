using System.Diagnostics;
using System.IO;

namespace PromptSelenium.Services;

public sealed class ChromeLauncher
{
    public string? DetectChromePath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public void Launch(string chromePath, int debugPort, int tabCount)
    {
        WorkerCountPolicy.Validate(tabCount);

        if (!File.Exists(chromePath))
        {
            throw new FileNotFoundException("Không tìm thấy chrome.exe.", chromePath);
        }

        var profileDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptSelenium",
            "ChromeProfile");
        Directory.CreateDirectory(profileDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = chromePath,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add($"--remote-debugging-port={debugPort}");
        startInfo.ArgumentList.Add($"--user-data-dir={profileDirectory}");
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--no-default-browser-check");
        for (var index = 0; index < tabCount; index++)
        {
            startInfo.ArgumentList.Add("https://chatgpt.com/");
        }

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Không thể khởi động Chrome.");
    }
}
