# Prompt Selenium

Windows desktop tool that reads an Excel `Story` column, controls pre-opened ChatGPT web tabs, executes either Prompt 1 → Prompt 2 or Prompt 1 → Prompt 2 → Prompt 3 for every story, and exports the selected prompt responses to Excel.

Despite the folder name, the implementation uses Microsoft Playwright over Chrome DevTools Protocol. This allows up to ten pre-opened pages to be controlled concurrently without Selenium's shared current-window state.

## Requirements

- Windows 10/11
- .NET 9 SDK
- Google Chrome
- A ChatGPT account
- Input `.xlsx` with a `Story` header in row 1

## Build and run

```powershell
dotnet restore .\Prompt_selenium\PromptSelenium.sln
dotnet run --project .\Prompt_selenium\PromptSelenium.csproj
```

## Usage

1. Select the input and output Excel files.
2. Review the prompt templates and select **Prompt 1 + 2** or **Prompt 1 + 2 + 3**. Only Prompt 1 must contain `{{story}}`; active follow-up prompts must not contain it.
3. Select `chrome.exe` if it was not detected automatically.
4. Choose between 1 and 10 workers, then select **Launch ChatGPT tabs**. The default is 10. The tool starts a dedicated Chrome profile with remote debugging on port `9222` and opens one ChatGPT tab per worker.
5. Sign in manually. Complete MFA or other verification yourself. Make sure every tab shows the ChatGPT prompt input.
6. Select **Connect & Verify tabs**.
7. Select **Start Processing**.

The defaults are bundled from `../Prompt/Prompt1.md`, `Prompt2.md`, and `Prompt3.md`. Each worker owns one tab and processes its story independently: it creates a fresh chat, completes the selected two- or three-prompt chain in that same chat, saves the active responses, and takes the next queued story. Workers paste, send, and generate independently without a shared composer lock. A response is complete when a new ChatGPT turn key associated with `Đọc to`/`Read aloud` appears and the Stop button is no longer visible. Its output is then read from that exact turn using the available assistant-content or markdown/prose layout before the worker continues. Prompt 1 receives the Excel story; active follow-up prompts are sent unchanged and use the existing chat context. The final workbook preserves the original story order and contains only the primary output columns selected by the run mode.

Prompt 1 and Prompt 2 remain in their own output columns and continue into following rows if they exceed Excel's 32,767-character cell limit. In three-prompt mode, Prompt 3 starts in column C and any overflow continues into unnamed cells to the right on the same row. In two-prompt mode, Prompt 3 is not validated, submitted, or exported.

## Important limitations

- ChatGPT web is not a stable automation API. UI selector changes may require updates in `Services/ChatGptBrowserService.cs`.
- ChatGPT failures are detected only from visible error controls. Phrases such as `something went wrong` inside a generated story are treated as normal story content.
- All tabs share the same ChatGPT account limits. More tabs do not increase the account quota. Ten active workers also use more RAM/CPU and may hit ChatGPT limits sooner; reduce the worker count if the account is throttled or the machine becomes overloaded.
- The tool does not bypass CAPTCHA, MFA, usage limits, or account restrictions. It stops and asks the user to resolve browser/account issues.
- Do not use the normal Chrome profile. The launch command deliberately uses `%LOCALAPPDATA%\PromptSelenium\ChromeProfile` so Chrome can enable remote debugging safely.
- Do not close or manually reuse worker tabs during a running batch.
- Prompt 2 and Prompt 3 start empty. Prompt 2 must be filled before every run; Prompt 3 is required only in three-prompt mode. Do not add `{{story}}` to an active follow-up prompt.

For production-grade unattended processing, use an official API integration instead of web UI automation.
