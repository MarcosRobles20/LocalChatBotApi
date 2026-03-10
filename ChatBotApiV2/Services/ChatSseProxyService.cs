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
            var lastThinkingContent = string.Empty;
            var isThinkingState = false;

            await foreach (var raw in _iaProxyClient.StreamOrchestrateAsync(payload, ct))
            {
                if (ct.IsCancellationRequested) yield break;

                ClsModStreamChunk? chunk = null;
                try { chunk = JsonSerializer.Deserialize<ClsModStreamChunk>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
                catch { }

                if (chunk != null && !string.IsNullOrEmpty(chunk.Type))
                {
                    // ======= Agent event =======
                    if (string.Equals(chunk.Type, "agent_event", StringComparison.OrdinalIgnoreCase))
                    {
                        object? metadataObj = null;
                        if (chunk.Metadata is JsonElement je)
                        {
                            try { metadataObj = JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText()); }
                            catch { }
                        }

                        yield return new ClsModStreamChunk
                        {
                            Type = "agent_event",
                            Content = chunk.Content,
                            event_type = chunk.event_type,
                            Metadata = metadataObj,
                            IdChat = chunk.IdChat,
                            IsNewChat = chunk.IsNewChat,
                            Model = chunk.Model,
                            Tool = chunk.Tool
                        };
                        continue;
                    }

                    // ======= Tokens =======
                    if (string.Equals(chunk.Type, "token", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(chunk.Content))
                    {
                        yield return chunk;  // emitimos directamente token
                        continue;
                    }

                    // ======= Thinking =======
                    if (string.Equals(chunk.Type, "thinking_start", StringComparison.OrdinalIgnoreCase))
                    { if (!isThinkingState) { isThinkingState = true; lastThinkingContent = string.Empty; yield return chunk; } continue; }

                    if (string.Equals(chunk.Type, "thinking", StringComparison.OrdinalIgnoreCase))
                    { isThinkingState = true; lastThinkingContent = chunk.Content ?? string.Empty; yield return chunk; continue; }

                    if (string.Equals(chunk.Type, "thinking_end", StringComparison.OrdinalIgnoreCase))
                    { if (isThinkingState) { isThinkingState = false; yield return chunk; } continue; }

                    // ======= Done/Error =======
                    if (string.Equals(chunk.Type, "done", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(chunk.Type, "error", StringComparison.OrdinalIgnoreCase))
                    { isThinkingState = false; lastThinkingContent = string.Empty; yield return chunk; continue; }

                    yield return chunk; // fallback
                    continue;
                }

                // fallback: raw response
                yield return new ClsModStreamChunk { Type = "response", Content = raw };
            }
        }

        private async IAsyncEnumerable<ClsModStreamChunk> StreamAndParseAsync(Func<IAsyncEnumerable<string>> streamFactory, [EnumeratorCancellation] CancellationToken ct)
        {
            var lastThinkingContent = string.Empty;
            var isThinkingState = false;

            await foreach (var raw in streamFactory())
            {
                if (ct.IsCancellationRequested) yield break;

                ClsModStreamChunk? chunk = null;
                try { chunk = JsonSerializer.Deserialize<ClsModStreamChunk>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to deserialize raw chunk from Python: {Raw}", raw); }

                if (chunk != null)
                {
                    // ======= Agent event =======
                    if (string.Equals(chunk.Type, "agent_event", StringComparison.OrdinalIgnoreCase))
                    {
                        object? metadataObj = null;
                        if (chunk.Metadata is JsonElement je)
                        {
                            try { metadataObj = JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText()); }
                            catch { }
                        }

                        yield return new ClsModStreamChunk
                        {
                            Type = "agent_event",
                            Content = chunk.Content,
                            event_type = chunk.event_type,
                            Metadata = metadataObj,
                            IdChat = chunk.IdChat,
                            IsNewChat = chunk.IsNewChat,
                            Model = chunk.Model,
                            Tool = chunk.Tool
                        };
                        continue;
                    }

                    // ======= Tokens =======
                    if (string.Equals(chunk.Type, "token", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(chunk.Content))
                    {
                        yield return chunk;
                        continue;
                    }

                    // ======= Thinking =======
                    if (string.Equals(chunk.Type, "thinking_start", StringComparison.OrdinalIgnoreCase))
                    { if (!isThinkingState) { isThinkingState = true; lastThinkingContent = string.Empty; yield return chunk; } continue; }

                    if (string.Equals(chunk.Type, "thinking", StringComparison.OrdinalIgnoreCase))
                    { isThinkingState = true; lastThinkingContent = chunk.Content ?? string.Empty; yield return chunk; continue; }

                    if (string.Equals(chunk.Type, "thinking_end", StringComparison.OrdinalIgnoreCase))
                    { if (isThinkingState) { isThinkingState = false; yield return chunk; } continue; }

                    // ======= Done/Error =======
                    if (string.Equals(chunk.Type, "done", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(chunk.Type, "error", StringComparison.OrdinalIgnoreCase))
                    { isThinkingState = false; lastThinkingContent = string.Empty; yield return chunk; continue; }

                    yield return chunk; // fallback
                    continue;
                }

                // fallback: raw response
                yield return new ClsModStreamChunk { Type = "response", Content = raw };
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
