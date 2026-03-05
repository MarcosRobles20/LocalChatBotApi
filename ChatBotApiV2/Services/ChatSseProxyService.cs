using ClbModChatbot;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using ChatBotApiV2.Models;
using System.Text;

namespace ChatBotApiV2.Services
{
    public class ChatSseProxyService : IChatSseProxyService
    {
        private readonly IIaProxyClient _iaProxyClient;
        private readonly ILogger<ChatSseProxyService> _logger;

        public ChatSseProxyService(IIaProxyClient iaProxyClient, ILogger<ChatSseProxyService> logger)
        {
            _iaProxyClient = iaProxyClient;
            _logger = logger;
        }

        public async IAsyncEnumerable<ClsModStreamChunk> ProxyStreamAsync(ClsModOllamaChatMessages payload, [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var chunk in StreamAndParseAsync(() => _iaProxyClient.StreamFromPythonAsync(payload, ct), ct))
            {
                yield return chunk;
            }
        }

        public async IAsyncEnumerable<ClsModStreamChunk> OrchestrateStreamAsync(OrchestrateRequestDto payload, [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var chunk in StreamAndParseOrchestrateAsync(payload, ct))
            {
                yield return chunk;
            }
        }

        public async Task<OrchestrateResponseDto> OrchestrateAsync(OrchestrateRequestDto payload, CancellationToken ct)
        {
            _logger.LogInformation("Calling /orchestrate with model {Model} and tool_hint {ToolHint}", payload.model, payload.tool_hint);
            return await _iaProxyClient.CallOrchestrateAsync(payload, ct);
        }

        private async IAsyncEnumerable<ClsModStreamChunk> StreamAndParseOrchestrateAsync(OrchestrateRequestDto payload, [EnumeratorCancellation] CancellationToken ct)
        {
            // This handles both SSE (stream) and JSON (single payload) from /orchestrate
            var lastThinkingContent = string.Empty;
            var isThinkingState = false;

            await foreach (var raw in _iaProxyClient.StreamOrchestrateAsync(payload, ct))
            {
                if (ct.IsCancellationRequested) yield break;

                // Try parse as chunk directly (SSE flow)
                ClsModStreamChunk? chunk = null;
                try
                {
                    chunk = JsonSerializer.Deserialize<ClsModStreamChunk>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch { }

                if (chunk != null && !string.IsNullOrEmpty(chunk.Type))
                {
                    // dedupe logic similar to chat-stream
                    if (string.Equals(chunk.Type, "thinking_start", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isThinkingState) continue;
                        isThinkingState = true; lastThinkingContent = string.Empty;
                        yield return chunk; continue;
                    }
                    if (string.Equals(chunk.Type, "thinking", StringComparison.OrdinalIgnoreCase))
                    {
                        isThinkingState = true; lastThinkingContent = chunk.Content ?? string.Empty;
                        yield return chunk; continue;
                    }
                    if (string.Equals(chunk.Type, "thinking_end", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!isThinkingState) continue;
                        isThinkingState = false; yield return chunk; continue;
                    }
                    if (string.Equals(chunk.Type, "response", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(lastThinkingContent) && NormalizeWhitespace(chunk.Content) == NormalizeWhitespace(lastThinkingContent))
                        { lastThinkingContent = string.Empty; continue; }
                        yield return chunk; continue;
                    }
                    // done/error/image/etc.
                    yield return chunk;
                    continue;
                }

                // If not a chunk, maybe it's full JSON orchestrate response
                OrchestrateResponseDto? resp = null;
                try
                {
                    resp = JsonSerializer.Deserialize<OrchestrateResponseDto>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch { }

                if (resp != null && !string.IsNullOrEmpty(resp.action))
                {
                    var action = resp.action.ToLowerInvariant();
                    if (action == "text_to_image" || action == "image_to_image")
                    {
                        yield return new ClsModStreamChunk { Type = "image", Content = resp.image_path };
                        yield return new ClsModStreamChunk { Type = "done", Model = payload.model };
                    }
                    else
                    {
                        // Build a single response content that includes main content + sources (if any)
                        var combined = new StringBuilder();
                        if (!string.IsNullOrEmpty(resp.content)) combined.Append(resp.content);
                        if (resp.sources != null && resp.sources.Any())
                        {
                            if (combined.Length > 0) combined.Append("\n\n");
                            combined.Append("Fuentes:\n");
                            combined.Append(string.Join("\n", resp.sources.Select(s => $"- {s.title} ({s.url})")));
                        }
                        if (combined.Length > 0)
                            yield return new ClsModStreamChunk { Type = "response", Content = combined.ToString() };

                        yield return new ClsModStreamChunk { Type = "done", Model = payload.model };
                    }
                }
                else
                {
                    // fallback: emit as response text
                    yield return new ClsModStreamChunk { Type = "response", Content = raw };
                }
            }
        }

        private async IAsyncEnumerable<ClsModStreamChunk> StreamAndParseAsync(Func<IAsyncEnumerable<string>> streamFactory, [EnumeratorCancellation] CancellationToken ct)
        {
            var lastThinkingContent = string.Empty;
            var isThinkingState = false;

            await foreach (var raw in streamFactory())
            {
                if (ct.IsCancellationRequested)
                    yield break;

                ClsModStreamChunk? chunk = null;
                try
                {
                    chunk = JsonSerializer.Deserialize<ClsModStreamChunk>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize raw chunk from Python: {Raw}", raw);
                }

                if (chunk != null)
                {
                    if (string.Equals(chunk.Type, "thinking_start", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isThinkingState) continue;
                        isThinkingState = true; lastThinkingContent = string.Empty;
                        yield return chunk; continue;
                    }

                    if (string.Equals(chunk.Type, "thinking", StringComparison.OrdinalIgnoreCase))
                    {
                        isThinkingState = true; lastThinkingContent = (chunk.Content ?? string.Empty);
                        yield return chunk; continue;
                    }

                    if (string.Equals(chunk.Type, "thinking_end", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!isThinkingState) continue;
                        isThinkingState = false; yield return chunk; continue;
                    }

                    if (string.Equals(chunk.Type, "response", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(chunk.Content))
                    {
                        if (!string.IsNullOrEmpty(lastThinkingContent) && NormalizeWhitespace(chunk.Content) == NormalizeWhitespace(lastThinkingContent))
                        { lastThinkingContent = string.Empty; continue; }
                    }

                    if (string.Equals(chunk.Type, "done", StringComparison.OrdinalIgnoreCase) || string.Equals(chunk.Type, "error", StringComparison.OrdinalIgnoreCase))
                    {
                        isThinkingState = false; lastThinkingContent = string.Empty;
                        yield return chunk; continue;
                    }

                    yield return chunk;
                }
                else
                {
                    yield return new ClsModStreamChunk { Type = "response", Content = raw };
                }
            }
        }

        private static string NormalizeWhitespace(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var parts = input.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts).Trim();
        }
    }
}
