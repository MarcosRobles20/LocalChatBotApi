using ClbModChatbot;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.CompilerServices;
using ChatBotApiV2.Models;

namespace ChatBotApiV2.Services
{
    public interface IChatSseProxyService
    {
        IAsyncEnumerable<ClsModStreamChunk> ProxyStreamAsync(ClsModOllamaChatMessages payload, [EnumeratorCancellation] CancellationToken ct);
        IAsyncEnumerable<ClsModStreamChunk> OrchestrateStreamAsync(OrchestrateRequestDto payload, [EnumeratorCancellation] CancellationToken ct);
        Task<OrchestrateResponseDto> OrchestrateAsync(OrchestrateRequestDto payload, CancellationToken ct);
    }
}
