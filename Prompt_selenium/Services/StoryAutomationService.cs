using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Microsoft.Playwright;
using PromptSelenium.Models;

namespace PromptSelenium.Services;

public sealed class StoryAutomationService(ChatGptBrowserService browserService)
{
    public async Task<IReadOnlyList<StoryResult>> ProcessAsync(
        IReadOnlyList<StoryInput> stories,
        PromptSet prompts,
        PromptRunMode runMode,
        int workerCount,
        TimeSpan responseTimeout,
        IProgress<AutomationProgress>? progress,
        CancellationToken cancellationToken)
    {
        PromptTemplateService.Validate(prompts, runMode);
        WorkerCountPolicy.Validate(workerCount);
        var stepCount = PromptTemplateService.GetStepCount(runMode);

        var pages = browserService.GetWorkerPages(workerCount);
        var queue = new ConcurrentQueue<StoryInput>(stories);
        var results = new ConcurrentDictionary<int, StoryResult>();
        using var failureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ExceptionDispatchInfo? firstFailure = null;
        var completed = 0;

        var workers = pages.Select((page, index) => RunWorkerAsync(page, index + 1)).ToArray();
        try
        {
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException) when (firstFailure is not null)
        {
            firstFailure.Throw();
        }
        catch when (firstFailure is not null)
        {
            firstFailure.Throw();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return results.Values.OrderBy(result => result.Index).ToArray();

        async Task RunWorkerAsync(IPage page, int workerNumber)
        {
            while (!failureCancellation.IsCancellationRequested && queue.TryDequeue(out var story))
            {
                try
                {
                    await browserService.StartNewChatAsync(page, failureCancellation.Token);
                    var promptOutputs = new[] { string.Empty, string.Empty, string.Empty };
                    for (var step = 1; step <= stepCount; step++)
                    {
                        failureCancellation.Token.ThrowIfCancellationRequested();
                        progress?.Report(new AutomationProgress(
                            Volatile.Read(ref completed),
                            stories.Count,
                            workerNumber,
                            story.Index + 1,
                            step,
                            $"Worker {workerNumber}: Story {story.Index + 1} - Prompt {step}"));

                        try
                        {
                            var prompt = PromptTemplateService.RenderForStep(prompts, step, story.Story);
                            promptOutputs[step - 1] = await browserService.SubmitPromptAsync(
                                page,
                                prompt,
                                responseTimeout,
                                failureCancellation.Token);
                        }
                        catch (OperationCanceledException) when (failureCancellation.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            throw new ChatGptAutomationException(
                                workerNumber,
                                story.Index + 1,
                                story.ExcelRow,
                                step,
                                exception);
                        }
                    }

                    results[story.Index] = new StoryResult(
                        story.Index,
                        story.ExcelRow,
                        promptOutputs[0],
                        promptOutputs[1],
                        promptOutputs[2]);
                    var done = Interlocked.Increment(ref completed);
                    progress?.Report(new AutomationProgress(
                        done,
                        stories.Count,
                        workerNumber,
                        story.Index + 1,
                        null,
                        $"Đã hoàn thành {done} / {stories.Count}"));
                }
                catch (OperationCanceledException) when (failureCancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    if (Interlocked.CompareExchange(
                            ref firstFailure,
                            ExceptionDispatchInfo.Capture(exception),
                            null) is null)
                    {
                        failureCancellation.Cancel();
                    }

                    throw;
                }
            }
        }
    }
}
