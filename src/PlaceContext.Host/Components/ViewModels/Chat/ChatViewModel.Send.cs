using PlaceContext.Application;
using PlaceContext.Application.Agents;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
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

        NotifyStateChanged();
        await scrollToBottom();

        // Build RAG context in parallel — don't block the typing indicator.
        var ragTask = _ragEnabled
            ? _contextBuilder.BuildContextAsync(ProjectId.Value, text, _maxContextChunks).ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result : "")
            : Task.FromResult("");

        try
        {
            var ct = CancellationToken.None;
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
                    await foreach (var token in cg.ChatStreamAsync(messages, settings, ct))
                    {
                        StreamBuffer += token;
                        tokenCount++;
                        if (IsRepetitionLoopTail(StreamBuffer))
                        {
                            StreamBuffer = TruncateRepeatedLines(StreamBuffer);
                            break;
                        }
                        NotifyStateChanged();
                        await scrollAfterRender();
                    }
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

                    const int maxRetries = 2;
                    const int timeoutSeconds = 15;
                    ToolCallResult result = ToolCallResult.Fail("uninitialized");
                    for (var attempt = 0; attempt <= maxRetries; attempt++)
                    {
                        tc.RetryCount = attempt;
                        NotifyStateChanged();
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                        try { result = await ExecuteToolAsync(tc.ToolName, tc.Args, linkedCts.Token); }
                        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                        { result = ToolCallResult.Fail($"Timed out after {timeoutSeconds}s"); }
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
                            await foreach (var token in cg2.ChatStreamAsync(retryMessages, settings, ct))
                            {
                                StreamBuffer += token;
                                if (IsRepetitionLoopTail(StreamBuffer)) { StreamBuffer = TruncateRepeatedLines(StreamBuffer); break; }
                                NotifyStateChanged();
                                await scrollAfterRender();
                            }
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
                        await foreach (var token in cg3.ChatStreamAsync(retryMessages, settings, ct))
                        {
                            StreamBuffer += token;
                            if (IsRepetitionLoopTail(StreamBuffer)) { StreamBuffer = TruncateRepeatedLines(StreamBuffer); break; }
                            NotifyStateChanged();
                            await scrollAfterRender();
                        }
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
        }
    }

    // ── Build chat messages ──────────────────────────────────────────────────

    internal List<ChatMessage> BuildChatMessages(string ragContext = "")
    {
        var antiCoT = "CRITICAL: NEVER think out loud. NEVER write your thought process, reasoning, self-correction, or commentary about the conversation. " +
            "NEVER output text like 'Looking at the conversation', 'Let me think', 'I notice', 'Actually', 'Re-reading', or 'Hmm'. " +
            "NEVER wrap your answer in <think>, <reasoning>, or <reflection> tags. " +
            "If you catch yourself starting to explain your reasoning, STOP and give the answer directly. " +
            "You are a casual Australian mate. Talk like one: use 'mate', 'no worries', 'righto', 'cheers', 'sweet as'. " +
            "Provide only the final answer or the tool call. If a tool is needed, emit it immediately without explanation.\n\n";

        var toolDesc = "Available tools (use [[tool:toolName|args]] syntax). " +
            "IMPORTANT: Always pass ALL known parameters (table names, column names, IDs, etc.) from the conversation context. " +
            "Do not ask the user for information you already have. " +
            "Tool routing: use get_artifacts for reports/files/artifacts, search only for run output text, query_table for table data.\n\n" +
            "Built-in tools:\n" +
            "- [[tool:list_tables|]] - List all project data tables\n" +
            "- [[tool:query_table|tableName|page]] - Query a table (pass table name from context)\n" +
            "- [[tool:list_jobs|]] - List all jobs\n" +
            "- [[tool:list_job_runs|jobId]] - List runs for a job\n" +
            "- [[tool:render_graph|chartType|tableName|columnName]] - Render a chart (pass table AND column names from context, e.g. [[tool:render_graph|bar|cashflow_runs|amount]])\n" +
            "- [[tool:query_graph|]] - Query project dependency graph\n" +
            "- [[tool:search|query]] - Semantic search over job run output text/logs only (not files/reports)\n" +
            "- [[tool:get_artifacts|query]] - Search project artifacts by title/kind. Returns METADATA ONLY: title, kind, size, and id. Does NOT return file content. Use this to find the artifact id, then ALWAYS call show_artifact to get the actual content. Do NOT summarize or describe artifact content based on get_artifacts results alone — you have not seen the content yet.\n" +
            "- [[tool:show_artifact|artifactId]] - Fetches and returns the ACTUAL CONTENT of an artifact (extracted text for docs, raw content for text files). You MUST call this after get_artifacts before summarizing, describing, or answering questions about artifact content. This is the only way to see what's inside an artifact.\n" +
            "- [[tool:schedule_job|jobId|name|cron]] - Create a cron schedule\n" +
            "- [[tool:list_schedules|jobId]] - List job schedules\n" +
            "- [[tool:toggle_schedule|triggerId|true|false]] - Enable/disable schedule\n" +
            "- [[tool:run_job|jobId]] - Run a job now\n" +
            "- [[tool:call_mcp|serverName|toolName|argsJson]] - Call a tool on an external MCP server\n" +
            "- [[tool:list_mcp_tools|serverName]] - List available tools on an MCP server\n" +
            "- [[tool:render_map|specJson]] - Render a Leaflet map (JSON spec with {markers:[{lat,lng,label,color}], polygons:[{coords,color}], center:[lat,lng], zoom}). Example: [[tool:render_map|{\\\"markers\\\":[{\\\"lat\\\":48.135,\\\"lng\\\":11.582,\\\"label\\\":\\\"Munich\\\"}]}]]";

        var systemPrompt = antiCoT + _systemPrompt + "\n\n" + toolDesc;
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
