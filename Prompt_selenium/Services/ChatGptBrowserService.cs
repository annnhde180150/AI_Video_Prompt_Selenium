using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PromptSelenium.Services;

public sealed class ChatGptBrowserService : IDisposable
{
    private const string ChatGptUrl = "https://chatgpt.com/";
    private static readonly TimeSpan UiActionTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResponseExtractionTimeout = TimeSpan.FromSeconds(30);
    private static readonly string[] PromptInputSelectors =
    [
        "#prompt-textarea",
        "div[contenteditable='true'][data-virtualkeyboard='true']",
        "div[contenteditable='true']"
    ];
    private static readonly string[] SendButtonSelectors =
    [
        "button[data-testid='send-button']",
        "button[aria-label*='Send prompt']",
        "button[aria-label*='Send message']",
        "button[aria-label*='Gửi']"
    ];
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task<int> ConnectAsync(int debugPort, CancellationToken cancellationToken)
    {
        DisposeConnection();
        _playwright = await Playwright.CreateAsync().WaitAsync(cancellationToken);
        try
        {
            _browser = await _playwright.Chromium
                .ConnectOverCDPAsync($"http://127.0.0.1:{debugPort}")
                .WaitAsync(cancellationToken);
            foreach (var context in _browser.Contexts)
            {
                context.SetDefaultTimeout((float)UiActionTimeout.TotalMilliseconds);
            }
        }
        catch
        {
            DisposeConnection();
            throw;
        }

        return GetChatGptPages().Count;
    }

    public IReadOnlyList<IPage> GetWorkerPages(int workerCount)
    {
        if (_browser is null)
        {
            throw new InvalidOperationException("Chưa kết nối Chrome.");
        }

        var pages = GetChatGptPages();
        if (pages.Count < workerCount)
        {
            throw new InvalidOperationException(
                $"Cần ít nhất {workerCount} tab chatgpt.com, hiện chỉ tìm thấy {pages.Count} tab.");
        }

        return pages.Take(workerCount).ToArray();
    }

    public async Task VerifyTabsReadyAsync(int workerCount, CancellationToken cancellationToken)
    {
        var pages = GetWorkerPages(workerCount);
        for (var index = 0; index < pages.Count; index++)
        {
            try
            {
                await FindPromptInputAsync(pages[index], UiActionTimeout, cancellationToken);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Tab {index + 1} chưa sẵn sàng. Hãy đăng nhập ChatGPT và mở trang chat mới.",
                    exception);
            }
        }
    }

    public async Task StartNewChatAsync(IPage page, CancellationToken cancellationToken)
    {
        await page.GotoAsync(
                ChatGptUrl,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60_000
                })
            .WaitAsync(cancellationToken);
        await FindPromptInputAsync(page, UiActionTimeout, cancellationToken);
    }

    public async Task<string> SubmitPromptAsync(
        IPage page,
        string prompt,
        TimeSpan responseTimeout,
        CancellationToken cancellationToken)
    {
        var completedResponseKey = await GetLatestCompletedResponseKeyAsync(page, cancellationToken);
        var input = await InsertPromptAsync(page, prompt, cancellationToken);
        await SubmitComposerAsync(page, input, cancellationToken);
        string? newResponseKey = null;

        await WaitUntilAsync(
            async () =>
            {
                var chatGptError = await GetVisibleChatGptErrorAsync(page, cancellationToken);
                if (chatGptError is not null)
                {
                    throw new InvalidOperationException($"ChatGPT web báo lỗi: {chatGptError}");
                }

                if (await IsGeneratingAsync(page, cancellationToken))
                {
                    return false;
                }

                var latestResponseKey = await GetLatestCompletedResponseKeyAsync(
                    page,
                    cancellationToken);
                if (latestResponseKey is null
                    || string.Equals(
                        latestResponseKey,
                        completedResponseKey,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                newResponseKey = latestResponseKey;
                return true;
            },
            responseTimeout,
            cancellationToken,
            $"ChatGPT chưa tạo response turn mới hoặc vẫn đang hiển thị nút Stop sau {responseTimeout.TotalMinutes:0} phút.");

        var finalText = await ExtractLatestResponseAsync(
            page,
            newResponseKey!,
            cancellationToken);
        return finalText;
    }

    public void Dispose() => DisposeConnection();

    private IReadOnlyList<IPage> GetChatGptPages() =>
        _browser?.Contexts
            .SelectMany(context => context.Pages)
            .Where(page => Uri.TryCreate(page.Url, UriKind.Absolute, out var uri)
                           && uri.Host.EndsWith("chatgpt.com", StringComparison.OrdinalIgnoreCase))
            .ToArray()
        ?? [];

    private static async Task<ILocator> FindPromptInputAsync(
        IPage page,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var selector in PromptInputSelectors)
            {
                var locator = page.Locator(selector).Last;
                try
                {
                    if (await locator.CountAsync().WaitAsync(cancellationToken) > 0
                        && await locator.IsVisibleAsync().WaitAsync(cancellationToken)
                        && await locator.IsEnabledAsync().WaitAsync(cancellationToken)
                        && await locator.IsEditableAsync().WaitAsync(cancellationToken))
                    {
                        return locator;
                    }
                }
                catch (PlaywrightException)
                {
                    // ChatGPT can replace the ProseMirror node while the page hydrates.
                    // Re-query it on the next loop instead of retaining a stale locator.
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException("Không tìm thấy ô nhập ChatGPT ở trạng thái sẵn sàng chỉnh sửa.");
    }

    private static async Task<ILocator> InsertPromptAsync(
        IPage page,
        string prompt,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var input = await FindPromptInputAsync(page, UiActionTimeout, cancellationToken);
                await input.ClickAsync().WaitAsync(cancellationToken);
                await input.PressAsync("Control+A").WaitAsync(cancellationToken);

                // InsertText goes through Chrome's text-input channel and avoids FillAsync
                // waiting indefinitely while ChatGPT's ProseMirror processes a large prompt.
                await page.Keyboard.InsertTextAsync(prompt).WaitAsync(cancellationToken);

                await WaitUntilAsync(
                    () => ComposerContainsPromptAsync(page, prompt, cancellationToken),
                    UiActionTimeout,
                    cancellationToken,
                    "ChatGPT chưa hiển thị đầy đủ prompt trong ô nhập.");

                return await FindPromptInputAsync(page, UiActionTimeout, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastException = exception;
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
        }

        throw new InvalidOperationException(
            "Không thể nhập prompt vào ChatGPT sau 3 lần thử. Hãy reload tab và chạy lại.",
            lastException);
    }

    private static async Task<bool> ComposerContainsPromptAsync(
        IPage page,
        string prompt,
        CancellationToken cancellationToken)
    {
        try
        {
            var input = await FindPromptInputAsync(page, TimeSpan.FromSeconds(2), cancellationToken);
            var actual = await input.InnerTextAsync().WaitAsync(cancellationToken);
            var normalizedActual = NormalizeComposerText(actual);
            var normalizedExpected = NormalizeComposerText(prompt);

            if (string.Equals(normalizedActual, normalizedExpected, StringComparison.Ordinal))
            {
                return true;
            }

            if (normalizedExpected.Length < 200
                || normalizedActual.Length < normalizedExpected.Length * 0.9)
            {
                return false;
            }

            const int anchorLength = 100;
            return normalizedActual.StartsWith(normalizedExpected[..anchorLength], StringComparison.Ordinal)
                   && normalizedActual.EndsWith(normalizedExpected[^anchorLength..], StringComparison.Ordinal);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string NormalizeComposerText(string value) =>
        Regex.Replace(value.ReplaceLineEndings("\n"), @"\s+", " ").Trim();

    private static async Task<bool> IsGeneratingAsync(IPage page, CancellationToken cancellationToken)
    {
        var stopButtons = page.Locator(
            "button[data-testid='stop-button'], button[aria-label*='Stop'], button[aria-label*='Dừng']");
        var count = await stopButtons.CountAsync().WaitAsync(cancellationToken);
        for (var index = 0; index < count; index++)
        {
            if (await stopButtons.Nth(index).IsVisibleAsync().WaitAsync(cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static Task<string?> GetLatestCompletedResponseKeyAsync(
        IPage page,
        CancellationToken cancellationToken) =>
        page.EvaluateAsync<string?>(
                """
                () => {
                    const assistantAction = [...document.querySelectorAll('button[aria-label]')]
                        .reverse()
                        .find(button => /^(Đọc to|Read aloud)$/i.test(
                            (button.getAttribute('aria-label') || '').trim()));
                    if (!assistantAction) {
                        return null;
                    }

                    const turn = assistantAction.closest('[data-turn-key]');
                    if (turn) {
                        return turn.getAttribute('data-turn-key');
                    }

                    const searchTurn = assistantAction.closest('[data-content-search-turn-key]');
                    return searchTurn?.getAttribute('data-content-search-turn-key') || null;
                }
                """)
            .WaitAsync(cancellationToken);

    private static async Task<string> ExtractLatestResponseAsync(
        IPage page,
        string responseKey,
        CancellationToken cancellationToken)
    {
        const string extractResponseScript = """
            responseKey => {
                const getTurnKey = element => {
                    const turn = element.closest('[data-turn-key]');
                    if (turn) {
                        return turn.getAttribute('data-turn-key');
                    }

                    const searchTurn = element.closest('[data-content-search-turn-key]');
                    return searchTurn?.getAttribute('data-content-search-turn-key') || null;
                };

                const actionButtons = [...document.querySelectorAll('button[aria-label]')];
                const assistantAction = actionButtons.reverse().find(button => {
                    const label = (button.getAttribute('aria-label') || '').trim();
                    return /^(Đọc to|Read aloud)$/i.test(label)
                        && getTurnKey(button) === responseKey;
                });

                const keyedTurn = [...document.querySelectorAll(
                    '[data-content-search-turn-key], [data-turn-key]')]
                    .reverse()
                    .find(element => element.getAttribute('data-turn-key') === responseKey
                        || element.getAttribute('data-content-search-turn-key') === responseKey);
                if (!keyedTurn && !assistantAction) {
                    return null;
                }

                const readBlocks = (root, includePlainText = false) => {
                    const selectorGroups = [
                        '.markdown',
                        '[data-message-content-part]',
                        '.prose',
                        '[data-testid="assistant-message"]'
                    ];
                    if (includePlainText) {
                        selectorGroups.push('.whitespace-pre-wrap');
                    }

                    for (const selector of selectorGroups) {
                        const blocks = [
                            ...(root.matches?.(selector) ? [root] : []),
                            ...root.querySelectorAll(selector)
                        ].filter(element => element.innerText?.trim());
                        if (blocks.length === 0) {
                            continue;
                        }

                        const topLevelBlocks = blocks.filter(element =>
                            !blocks.some(other => other !== element && other.contains(element)));
                        const text = topLevelBlocks
                            .map(element => element.innerText.trim())
                            .join('\n\n')
                            .trim();
                        if (text) {
                            return text;
                        }
                    }

                    return null;
                };

                const roleRoot = assistantAction?.closest('[data-message-author-role="assistant"]')
                    || (keyedTurn?.matches('[data-message-author-role="assistant"]')
                        ? keyedTurn
                        : [...(keyedTurn?.querySelectorAll(
                            '[data-message-author-role="assistant"]') || [])].at(-1));
                const roleText = roleRoot ? readBlocks(roleRoot, true) : null;
                if (roleText) {
                    return roleText;
                }

                const turnText = keyedTurn ? readBlocks(keyedTurn) : null;
                if (turnText) {
                    return turnText;
                }

                const rawTurnText = keyedTurn?.innerText?.trim();
                if (rawTurnText) {
                    const assistantHeadings = [
                        'ChatGPT đã nói:',
                        'ChatGPT said:',
                        'Assistant said:'
                    ];
                    for (const heading of assistantHeadings) {
                        const headingIndex = rawTurnText.lastIndexOf(heading);
                        if (headingIndex >= 0) {
                            const responseText = rawTurnText
                                .slice(headingIndex + heading.length)
                                .trim();
                            if (responseText) {
                                return responseText;
                            }
                        }
                    }
                }

                const plainText = keyedTurn
                    ? [...keyedTurn.querySelectorAll('.whitespace-pre-wrap')]
                        .reverse()
                        .map(element => element.innerText?.trim())
                        .find(text => text)
                    : null;
                if (plainText) {
                    return plainText;
                }

                // Fallback for layouts where the response controls are outside the keyed turn.
                const actionControls = assistantAction?.closest('.turn-action-controls');
                let container = actionControls?.parentElement || assistantAction?.parentElement;
                for (let depth = 0; container && depth < 20; depth++, container = container.parentElement) {
                    const responseText = readBlocks(container);
                    if (responseText) {
                        return responseText;
                    }

                    if (container === keyedTurn) {
                        break;
                    }
                }

                return null;
            }
            """;

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < ResponseExtractionTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var text = await page
                    .EvaluateAsync<string?>(extractResponseScript, responseKey)
                    .WaitAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
            catch (PlaywrightException)
            {
                // The response DOM can be replaced once more immediately after generation.
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new InvalidOperationException(
            "ChatGPT đã hoàn tất nhưng không tìm thấy nội dung response mới nhất.");
    }

    private static async Task SubmitComposerAsync(
        IPage page,
        ILocator input,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < UiActionTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var selector in SendButtonSelectors)
            {
                var button = page.Locator(selector).Last;
                if (await button.CountAsync().WaitAsync(cancellationToken) > 0
                    && await button.IsVisibleAsync().WaitAsync(cancellationToken)
                    && await button.IsEnabledAsync().WaitAsync(cancellationToken))
                {
                    await button.ClickAsync().WaitAsync(cancellationToken);
                    return;
                }
            }

            await Task.Delay(250, cancellationToken);
        }

        // Keyboard submission is retained as a fallback for ChatGPT UI variants
        // that do not expose a stable send-button selector.
        var currentInput = await FindPromptInputAsync(page, UiActionTimeout, cancellationToken);
        await currentInput.PressAsync("Enter").WaitAsync(cancellationToken);
    }

    private static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string timeoutMessage)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await condition())
            {
                return;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException(timeoutMessage);
    }

    private static async Task<string?> GetVisibleChatGptErrorAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        var candidates = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const selectors = [
                        '[role="alert"]',
                        '[data-testid="conversation-turn-error"]',
                        '[data-testid="error-message"]',
                        '.text-token-text-error'
                    ];
                    const elements = [...new Set(
                        selectors.flatMap(selector => [...document.querySelectorAll(selector)]))];
                    return elements
                        .filter(element => {
                            const style = window.getComputedStyle(element);
                            const bounds = element.getBoundingClientRect();
                            return style.display !== 'none'
                                && style.visibility !== 'hidden'
                                && bounds.width > 0
                                && bounds.height > 0;
                        })
                        .map(element => element.innerText?.trim())
                        .filter(text => text);
                }
                """)
            .WaitAsync(cancellationToken);

        return candidates.FirstOrDefault(IsKnownChatGptErrorMessage);
    }

    internal static bool IsKnownChatGptErrorMessage(string text)
    {
        var normalized = NormalizeComposerText(text);
        return Regex.IsMatch(
            normalized,
            "^(Something went wrong\\b|You've reached\\b|You have reached\\b|(?:A )?network error\\b|There was an error generating a response\\b|\\u0110\\u00E3 x\\u1EA3y ra l\\u1ED7i\\b|C\\u00F3 l\\u1ED7i x\\u1EA3y ra\\b|L\\u1ED7i m\\u1EA1ng\\b|B\\u1EA1n \\u0111\\u00E3 \\u0111\\u1EA1t\\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private void DisposeConnection()
    {
        // Do not call Browser.CloseAsync(): Chrome belongs to the user.
        _browser = null;
        _playwright?.Dispose();
        _playwright = null;
    }
}
