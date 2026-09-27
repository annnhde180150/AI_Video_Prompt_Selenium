# Prompt Selenium

Windows desktop tool that reads an Excel `Story` column, controls pre-opened ChatGPT web tabs, executes Prompt 1 → Prompt 2 → Prompt 3 for every story, and exports all three prompt responses in separate Excel columns.

Despite the folder name, the implementation uses Microsoft Playwright over Chrome DevTools Protocol. This allows five pre-opened pages to be controlled concurrently without Selenium's shared current-window state.

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
2. Review all three prompt templates. Only Prompt 1 must contain `{{story}}`; Prompt 2 and Prompt 3 are follow-up messages and must not contain it.
3. Select `chrome.exe` if it was not detected automatically.
4. Select **Launch ChatGPT tabs**. The tool starts a dedicated Chrome profile with remote debugging on port `9222` and opens five ChatGPT tabs.
5. Sign in manually. Complete MFA or other verification yourself. Make sure every tab shows the ChatGPT prompt input.
6. Select **Connect & Verify tabs**.
7. Select **Start Processing**.

The defaults are bundled from `../Prompt/Prompt1.md`, `Prompt2.md`, and `Prompt3.md`. Each worker owns one tab and processes its story independently: it creates a fresh chat, completes Prompt 1 → Prompt 2 → Prompt 3 in that same chat, saves all three responses, and takes the next queued story. Workers paste, send, and generate independently without a shared composer lock. A response is complete when a new ChatGPT turn key associated with `Đọc to`/`Read aloud` appears and the Stop button is no longer visible. Its output is then read from that exact turn using the available assistant-content or markdown/prose layout before the worker continues. Prompt 1 receives the Excel story; Prompt 2 and Prompt 3 are sent unchanged as follow-up messages that use the existing chat context. The final workbook contains `Prompt 1 Output`, `Prompt 2 Output`, and `Prompt 3 Output` columns in the original story order.

Each response stays in its own output column. When one response exceeds Excel's 32,767-character cell limit, it continues in the following cells of that same column without truncation.

## Important limitations

- ChatGPT web is not a stable automation API. UI selector changes may require updates in `Services/ChatGptBrowserService.cs`.
- ChatGPT failures are detected only from visible error controls. Phrases such as `something went wrong` inside a generated story are treated as normal story content.
- All tabs share the same ChatGPT account limits. More tabs do not increase the account quota.
- The tool does not bypass CAPTCHA, MFA, usage limits, or account restrictions. It stops and asks the user to resolve browser/account issues.
- Do not use the normal Chrome profile. The launch command deliberately uses `%LOCALAPPDATA%\PromptSelenium\ChromeProfile` so Chrome can enable remote debugging safely.
- Do not close or manually reuse worker tabs during a running batch.
- Prompt 2 and Prompt 3 start empty. Paste the real follow-up prompts before processing; do not add `{{story}}` to them.

For production-grade unattended processing, use an official API integration instead of web UI automation.
