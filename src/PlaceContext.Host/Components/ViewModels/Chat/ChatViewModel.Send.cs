using PlaceContext.Application;
using PlaceContext.Application.Agents;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Chat;
using PlaceContext.Infrastructure.Caching;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ChatViewModel
{
    // ── Chat send ────────────────────────────────────────────────────────────

    public async Task SendAsync(Func<Task> scrollToBottom, Func<Task> scrollAfterRender)
    {
        var text = Input.Trim();
        if (string.IsNullOrEmpty(text) || Streaming || !ProjectId.HasValue) return;

        // Expand /commands into tool calls before sending to the LLM.
        if (text.StartsWith("/"))
        {
            var parts = text[1..].Split(' ', 2, StringSplitOptions.TrimEntries);
            var cmdName = parts[0];
            var cmdArgs = parts.Length > 1 ? parts[1] : "";
            var match = Commands.FirstOrDefault(c =>
                string.Equals(c.Name, cmdName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                var toolArgs = string.IsNullOrEmpty(match.Args) ? cmdArgs
                    : string.IsNullOrEmpty(cmdArgs) ? match.Args
                    : $"{match.Args} {cmdArgs}";
                text = AgentToolNames.FormatCall(match.ToolName, toolArgs);
            }
        }

        var userText = text;
        if (AttachedFileName != null && AttachedFileText != null)
            userText += $"\n\n## Attached file: {AttachedFileName}\n\n{AttachedFileText}";

        var userMsg = new AgentMessage("user", userText);

        if (AttachedFile != null && AttachedFileName != null && _sessionId.HasValue && _objectStore.IsEnabled)
        {
            try
            {
                if (!_attachmentsBucketEnsured)
                {
                    await _objectStore.EnsureBucketAsync(AttachmentsBucket);
                    _attachmentsBucketEnsured = true;
                }
                var contentType = ContentTypeFor(AttachedFileName);
                var key = $"chat/{_tenant.TenantId}/{ProjectId!.Value}/{_sessionId.Value}/{Guid.NewGuid():N}-{SanitizeFileName(AttachedFileName)}";
                await _objectStore.PutAsync(AttachmentsBucket, key, AttachedFile, contentType);
                userMsg.AttachmentName = AttachedFileName;
                userMsg.AttachmentKey = key;
                userMsg.AttachmentContentType = contentType;
                userMsg.AttachmentSizeBytes = AttachedFile.Length;
            }
            catch { }
        }
        else if (AttachedFileName != null)
        {
            userMsg.AttachmentName = AttachedFileName;
            userMsg.AttachmentSizeBytes = AttachedFile?.Length ?? 0;
        }

        Messages.Add(userMsg);
        Input = "";
        AttachedFile = null;
        AttachedFileName = null;
        AttachedFileText = null;
        Streaming = true;
        StreamBuffer = "";

        if (Messages.Count(m => m.Role == "user") == 1)
            _sessionTitle = text.Length > 50 ? text[..50] + "…" : text;

        // Replace any previous cancellation source so a fresh send always starts clean.
        _sendCts?.Dispose();
        _sendCts = new CancellationTokenSource();

        NotifyStateChanged();
        await scrollToBottom();

        // Build RAG context in parallel — don't block the typing indicator.
        var ragTask = _ragEnabled
            ? _contextBuilder.BuildContextAsync(ProjectId.Value, text, _maxContextChunks).ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result : "")
            : Task.FromResult("");

        try
        {
            var ct = _sendCts.Token;
            var maxToolRounds = 3;
            var round = 0;

            while (round < maxToolRounds)
            {
                round++;
                var ragContext = ragTask.IsCompleted ? ragTask.Result : await ragTask;
                var messages = BuildChatMessages(ragContext);
                StreamBuffer = "";
                CurrentToolCall = null;
                NotifyStateChanged();

                if (round > 1 || Messages.Any(m => m.ToolCalls.Count > 0))
                    await Task.Delay(300, ct);

                if (_gateway is ClusterChatGateway cg && cg.IsEnabled)
                {
                    var settings = new ChatSettings(Temperature: _temperature, MaxTokens: _maxTokens);
                    var tokenCount = 0;
                    var renderThrottle = System.Diagnostics.Stopwatch.StartNew();
                    await foreach (var token in cg.ChatStreamAsync(messages, settings, ct))
                    {
                        StreamBuffer += token;
                        tokenCount++;
                        if (IsRepetitionLoopTail(StreamBuffer))
                        {
                            StreamBuffer = TruncateRepeatedLines(StreamBuffer);
                            break;
                        }
                        // Throttle renders so the SignalR circuit isn't saturated by fast token streams.
                        if (renderThrottle.Elapsed > TimeSpan.FromMilliseconds(80))
                        {
                            renderThrottle.Restart();
                            NotifyStateChanged();
                            await scrollAfterRender();
                        }
                    }
                    NotifyStateChanged();
                    await scrollAfterRender();
                    if (tokenCount == 0)
                        StreamBuffer = ChatCopy.EmptyModelResponse;
                }
                else if (_gateway is ClusterChatGateway configuredButNotReady && !configuredButNotReady.IsEnabled)
                {
                    StreamBuffer = ChatCopy.ClusterStarting;
                }
                else
                {
                    var settings = new ChatSettings(Temperature: _temperature, MaxTokens: _maxTokens);
                    StreamBuffer = await _gateway.ChatAsync(messages, settings, ct);
                }

                var toolCalls = ParseToolCalls(StreamBuffer);
                if (toolCalls.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(StreamBuffer))
                        StreamBuffer = "I've processed your request but didn't generate a text response. The tool results are shown above.";
                    break;
                }

                var toolResults = new System.Text.StringBuilder();
                toolResults.Append("\n\n## Tool Results\n\n");
                foreach (var tc in toolCalls)
                {
                    CurrentToolCall = tc;
                    tc.Id = _toolCallCounter++;
                    tc.Status = AgentToolCallStatus.Running;
                    NotifyStateChanged();

                    // run_job / run_job_chain have side effects — no retries, and a long timeout
                    // (matches AgentSessionRunner launchpad behaviour).
                    var isJobTool = tc.ToolName is AgentToolNames.RunJob or AgentToolNames.RunJobChain;
                    var maxRetries = isJobTool ? 0 : 2;
                    var timeout = isJobTool ? TimeSpan.FromMinutes(15) : TimeSpan.FromSeconds(15);
                    ToolCallResult result = ToolCallResult.Fail("uninitialized");
                    for (var attempt = 0; attempt <= maxRetries; attempt++)
                    {
                        tc.RetryCount = attempt;
                        NotifyStateChanged();
                        using var timeoutCts = new CancellationTokenSource(timeout);
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                        try { result = await ExecuteToolAsync(tc.ToolName, tc.Args, linkedCts.Token); }
                        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                        { result = ToolCallResult.Fail($"Timed out after {(int)timeout.TotalSeconds}s"); }
                        if (result.Success || !IsTransientError(result.Error ?? "")) break;
                        if (attempt < maxRetries) await Task.Delay(1000, ct);
                    }

                    tc.Status = result.Success ? AgentToolCallStatus.Completed : AgentToolCallStatus.Error;
                    tc.Result = result.Data ?? result.Error;
                    tc.ResultType = result.IsMap ? "map" : result.IsGraph ? "graph" : result.IsArtifact ? "artifact" : "text";

                    var action = ActiveActions.FirstOrDefault(a => a.ToolName == tc.ToolName && a.Status == AgentToolCallStatus.Running);
                    if (action != null)
                    {
                        action.Status = result.Success ? AgentToolCallStatus.Completed : AgentToolCallStatus.Error;
                        action.Detail = result.Success ? "done" : (result.Error?.Length > 40 ? result.Error[..40] + "…" : result.Error ?? "error");
                    }

                    toolResults.Append("### " + tc.ToolName + "\n");
                    toolResults.Append("Args: " + tc.Args + "\n");
                    toolResults.Append(result.Success ? "Result:\n" : "Error:\n");
                    toolResults.Append(result.Data ?? result.Error);
                    toolResults.Append("\n\n");
                }

                var textContent = StripToolCallSyntax(StreamBuffer).Trim();
                var rawCleaned = textContent;
                var split = SplitThinking(textContent);
                textContent = split.Answer;
                if (string.IsNullOrWhiteSpace(textContent) && toolCalls.Count == 0 && !IsAllReasoning(rawCleaned))
                    textContent = FriendlyLoadingQuip();

                var assistantMsg = new AgentMessage("assistant", textContent);
                assistantMsg.Thinking = string.IsNullOrWhiteSpace(split.Thinking) ? null : split.Thinking;
                assistantMsg.ToolCalls.AddRange(toolCalls);
                Messages.Add(assistantMsg);
                Messages.Add(new AgentMessage("system", toolResults.ToString()));
                StreamBuffer = "";
                NotifyStateChanged();
                await scrollAfterRender();
            }

            if (Messages.Count == 0 || Messages.Last().Role != "assistant")
            {
                var finalSplit = SplitThinking(StreamBuffer ?? "");
                var finalContent = string.IsNullOrWhiteSpace(finalSplit.Answer) ? FriendlyLoadingQuip() : finalSplit.Answer;
                var finalMsg = new AgentMessage("assistant", finalContent);
                finalMsg.Thinking = string.IsNullOrWhiteSpace(finalSplit.Thinking) ? null : finalSplit.Thinking;
                Messages.Add(finalMsg);
            }

            var hallucination = DetectHallucination();
            if (hallucination.Detected)
            {
                Messages.Add(new AgentMessage("assistant", hallucination.CorrectionPrompt ?? "Let me try that again…"));
                NotifyStateChanged();
                await scrollAfterRender();

                if (hallucination.ArtifactId != null)
                {
                    Streaming = true;
                    StreamBuffer = "";
                    NotifyStateChanged();
                    var showResult = await ExecuteShowArtifactAsync(hallucination.ArtifactId, ct);
                    if (showResult.Success && !string.IsNullOrWhiteSpace(showResult.Data))
                    {
                        var followUp = $"The artifact content is below. Summarise it accurately — do not fabricate anything.\n\n{showResult.Data}";
                        Messages.Add(new AgentMessage("system", followUp));
                        NotifyStateChanged();
                        var retryMessages = BuildChatMessages("");
                        StreamBuffer = "";
                        var settings = new ChatSettings(Temperature: _temperature, MaxTokens: _maxTokens);
                        if (_gateway is ClusterChatGateway cg2 && cg2.IsEnabled)
                        {
                            var renderThrottle = System.Diagnostics.Stopwatch.StartNew();
                            await foreach (var token in cg2.ChatStreamAsync(retryMessages, settings, ct))
                            {
                                StreamBuffer += token;
                                if (IsRepetitionLoopTail(StreamBuffer)) { StreamBuffer = TruncateRepeatedLines(StreamBuffer); break; }
                                if (renderThrottle.Elapsed > TimeSpan.FromMilliseconds(80))
                                {
                                    renderThrottle.Restart();
                                    NotifyStateChanged();
                                    await scrollAfterRender();
                                }
                            }
                            NotifyStateChanged();
                            await scrollAfterRender();
                        }
                        else { StreamBuffer = await _gateway.ChatAsync(retryMessages, settings, ct); }
                        var sumSplit = SplitThinking(StreamBuffer);
                        var summarised = TruncateRepeatedLines(sumSplit.Answer);
                        if (!string.IsNullOrWhiteSpace(summarised))
                        {
                            var sumMsg = new AgentMessage("assistant", summarised);
                            sumMsg.Thinking = string.IsNullOrWhiteSpace(sumSplit.Thinking) ? null : sumSplit.Thinking;
                            Messages.Add(sumMsg);
                        }
                    }
                    else
                    {
                        Messages.Add(new AgentMessage("assistant", "Couldn't load the artifact content — it may be missing or corrupted."));
                    }
                    Streaming = false;
                    StreamBuffer = "";
                    NotifyStateChanged();
                    await scrollAfterRender();
                }
                else
                {
                    Streaming = true;
                    StreamBuffer = "";
                    NotifyStateChanged();
                    var retryMessages = BuildChatMessages("");
                    StreamBuffer = "";
                    var settings = new ChatSettings(Temperature: _temperature, MaxTokens: _maxTokens);
                    if (_gateway is ClusterChatGateway cg3 && cg3.IsEnabled)
                    {
                        var renderThrottle = System.Diagnostics.Stopwatch.StartNew();
                        await foreach (var token in cg3.ChatStreamAsync(retryMessages, settings, ct))
                        {
                            StreamBuffer += token;
                            if (IsRepetitionLoopTail(StreamBuffer)) { StreamBuffer = TruncateRepeatedLines(StreamBuffer); break; }
                            if (renderThrottle.Elapsed > TimeSpan.FromMilliseconds(80))
                            {
                                renderThrottle.Restart();
                                NotifyStateChanged();
                                await scrollAfterRender();
                            }
                        }
                        NotifyStateChanged();
                        await scrollAfterRender();
                    }
                    else { StreamBuffer = await _gateway.ChatAsync(retryMessages, settings, ct); }
                    var retrySplit = SplitThinking(StreamBuffer);
                    var retried = TruncateRepeatedLines(retrySplit.Answer);
                    if (!string.IsNullOrWhiteSpace(retried))
                    {
                        if (DetectGenericHallucination(retried).Detected)
                            retried = "The model got stuck repeating itself — please try asking again.";
                        var retryMsg = new AgentMessage("assistant", retried);
                        retryMsg.Thinking = string.IsNullOrWhiteSpace(retrySplit.Thinking) ? null : retrySplit.Thinking;
                        Messages.Add(retryMsg);
                    }
                    Streaming = false;
                    StreamBuffer = "";
                    NotifyStateChanged();
                    await scrollAfterRender();
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (!Messages.Any(m => m.Role == "assistant" && m.Content.StartsWith("[stopped")))
                Messages.Add(new AgentMessage("assistant", "[stopped — generation cancelled by user]"));
        }
        catch (Exception ex)
        {
            Messages.Add(new AgentMessage("assistant", $"[error: {ex.Message}]"));
        }
        finally
        {
            Streaming = false;
            StreamBuffer = "";
            CurrentToolCall = null;
            PendingClarification = null;
            ClarificationSelected.Clear();
            _clarificationTcs = null;
            await SaveCurrentSessionAsync();
            NotifyStateChanged();
            await scrollAfterRender();
            _sendCts?.Dispose();
            _sendCts = null;
        }
    }

    // ── Build chat messages ──────────────────────────────────────────────────

    internal List<ChatMessage> BuildChatMessages(string ragContext = "")
    {
        var preamble = string.IsNullOrWhiteSpace(_preamble) ? AgentConfig.DefaultPreamble : _preamble;
        var toolDesc = string.IsNullOrWhiteSpace(_toolCatalog) ? AgentConfig.DefaultToolCatalog : _toolCatalog;

        var systemPrompt = preamble + _systemPrompt + "\n\n" + toolDesc;
        if (!string.IsNullOrWhiteSpace(ragContext))
            systemPrompt += "\n\n## Project context (retrieved automatically)\n\n" + ragContext;

        var result = new List<ChatMessage> { new("system", systemPrompt) };
        var history = new List<ChatMessage>();
        for (var i = 0; i < Messages.Count; i++)
        {
            var m = Messages[i];
            if (m.Role == "system") continue;
            var content = m.Content;
            if (m.Role == "assistant")
            {
                if (m.ToolCalls.Count > 0)
                {
                    var toolCallText = string.Join("\n", m.ToolCalls.Select(tc => AgentToolNames.FormatCall(tc.ToolName, tc.Args)));
                    content = string.IsNullOrWhiteSpace(content) ? toolCallText : content + "\n\n" + toolCallText;
                }
                if (i + 1 < Messages.Count && Messages[i + 1].Role == "system" && Messages[i + 1].Content.Contains("Tool Results"))
                {
                    content += "\n\n" + Messages[i + 1].Content;
                    i++;
                }
                if (m.Role == "assistant") content = CleanAssistantOutput(content);
            }
            history.Add(new ChatMessage(m.Role, content));
        }
        foreach (var m in history.TakeLast(20)) result.Add(m);
        return result;
    }

}
