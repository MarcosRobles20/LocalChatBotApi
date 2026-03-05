using ClbModChatbot;
using ChatBotApiV2.Models;

namespace ChatBotApiV2.Services
{
    public interface IIaProxyClient
    {
        IAsyncEnumerable<string> StreamFromPythonAsync(ClsModOllamaChatMessages payload, CancellationToken ct);
        IAsyncEnumerable<string> StreamOrchestrateAsync(OrchestrateRequestDto payload, CancellationToken ct);
        Task<OrchestrateResponseDto> CallOrchestrateAsync(OrchestrateRequestDto payload, CancellationToken ct);
    }
}
