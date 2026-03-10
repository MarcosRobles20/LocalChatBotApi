using ClbModChatbot;

namespace ChatBotApiV2.Services
{
    public interface IChatOrchestrator
    {
        IAsyncEnumerable<ClsModStreamChunk> StreamViaProxyAsync(ClsModOllamaChatMessages request, string? currentUserId, CancellationToken ct);
    }
}
