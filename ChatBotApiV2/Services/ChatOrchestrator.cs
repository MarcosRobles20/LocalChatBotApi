using ClbModChatbot;
using ClbNegChatbot;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Linq;
using OllamaSharp.Models.Chat;
using ChatBotApiV2.Models;

namespace ChatBotApiV2.Services
{
    public class ChatOrchestrator : IChatOrchestrator
    {
        private readonly ClsNegChat _negChat;
        private readonly IChatSseProxyService _proxyService;
        private readonly ILogger<ChatOrchestrator> _logger;

        public ChatOrchestrator(ClsNegChat negChat, IChatSseProxyService proxyService, ILogger<ChatOrchestrator> logger)
        {
            _negChat = negChat;
            _proxyService = proxyService;
            _logger = logger;
        }

        public async IAsyncEnumerable<ClsModStreamChunk> StreamViaProxyAsync(
            ClsModOllamaChatMessages request,
            string? currentUserId,
            [EnumeratorCancellation] CancellationToken ct)
        {
            // Validación de usuario
            if (string.IsNullOrEmpty(currentUserId) || currentUserId != request.IdUser)
                throw new UnauthorizedAccessException("No tienes acceso para enviar mensajes como otro usuario");

            if (!request.Messages.Any() || request.Messages.All(m => m.Role != "user"))
                throw new ArgumentException("Debe incluir al menos un mensaje de usuario");

            // Iniciar chat y guardar último mensaje de usuario
            var (idChat, isNewChat) = await _negChat.BeginChatAsync(request.IdChat, request.IdUser);
            request.IdChat = idChat;

            var lastUserMessage = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? string.Empty;
            await _negChat.SaveUserMessageAsync(request.IdChat, request.IdUser, lastUserMessage, request.Kind);

            // Construir contexto (LangGraph maneja contexto, opcional)
            var ollamaContext = _negChat.BuildOllamaContextPublic(request.IdChat, request.IdUser, lastUserMessage, isNewChat);

            // Payload para proxy Python
            var payloadForProxy = new ClsModOllamaChatMessages
            {
                IdChat = request.IdChat,
                IdUser = request.IdUser,
                Model = request.Model,
                Stream = true,
                Messages = ollamaContext.Select(msg => new ClsModChatMessageItem
                {
                    Role = msg.Role?.ToString().ToLower(),
                    Content = msg.Content
                }).ToList()
            };

            // Request para orquestador
            var orchReq = new OrchestrateRequestDto
            {
                text = null,
                messages = payloadForProxy.Messages,
                model = request.Model,
                tool_hint = null,
                idChat = request.IdChat,
                session_id = null,
                isNewChat = isNewChat
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var responseSb = new StringBuilder();
            var imagePaths = new List<string>();
            string modelUsed = request.Model ?? "unknown";
            bool gotDone = false;
            var references = new List<object>(); // Acumula referencias

            await foreach (var chunk in _proxyService.OrchestrateStreamAsync(orchReq, ct))
            {
                if (ct.IsCancellationRequested)
                    yield break;

                // =====================
                // Agente y herramientas
                // =====================
                if ((chunk.Type == "agent_event" || chunk.Type == "agent_events") &&
                    string.Equals(chunk.event_type, "source", StringComparison.OrdinalIgnoreCase) &&
                    chunk.Metadata != null)
                {
                    // Extraer metadata como diccionario
                    if (chunk.Metadata is JsonElement je)
                    {
                        try
                        {
                            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText());
                            if (dict != null) references.Add(dict);
                        }
                        catch { }
                    }
                    else if (chunk.Metadata is Dictionary<string, object> dict)
                    {
                        references.Add(dict);
                    }
                }

                if (chunk.Type == "agent_event" || chunk.Type == "tool_call" || chunk.Type == "agent_events")
                {
                    yield return chunk;
                    continue;
                }

                // =====================
                // Tokens: flujo principal
                // =====================
                if (chunk.Type == "token" && !string.IsNullOrEmpty(chunk.Content))
                {
                    responseSb.Append(chunk.Content);  // acumular para DB
                    yield return chunk;               // mantener type=token para Angular
                    continue;
                }

                // =====================
                // Imágenes
                // =====================
                if (chunk.Type == "image" && !string.IsNullOrEmpty(chunk.Content))
                {
                    imagePaths.Add(chunk.Content);
                    yield return chunk;
                    continue;
                }

                // =====================
                // Modelo final
                // =====================
                if (chunk.Type == "done" && !gotDone)
                {
                    gotDone = true;
                    if (!string.IsNullOrEmpty(chunk.Model))
                        modelUsed = chunk.Model;
                }

                // fallback
                yield return chunk;
            }

            stopwatch.Stop();
            var elapsed = stopwatch.ElapsedMilliseconds;

            // Persistir respuesta final
            try
            {
                await _negChat.FinalizeAiResponseAsyncPublic(
                    request.IdChat, request.IdUser, responseSb.ToString(),
                    modelUsed, ollamaContext, elapsed, isNewChat, lastUserMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist final AI response or metadata");
            }

            // Persistir referencias como mensaje separado si existen
            if (references.Count > 0)
            {
                try
                {
                    var referencesJson = JsonSerializer.Serialize(references);
                    await _negChat.SaveAiResponseAsyncPublic(
                        request.IdChat,
                        request.IdUser,
                        referencesJson,
                        modelUsed,
                        "references" // kind
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist references message");
                }
            }

            // Persistir imágenes como mensajes separados
            foreach (var imgPath in imagePaths)
            {
                if (string.IsNullOrWhiteSpace(imgPath)) continue;
                try
                {
                    await _negChat.SaveAiResponseAsyncPublic(request.IdChat, request.IdUser, imgPath, modelUsed, "image");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist image response");
                }
            }
        }
        private static string TryFormatSources(string content)
        {
            try
            {
                var sources = JsonSerializer.Deserialize<List<SearchResultDto>>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (sources == null || sources.Count == 0) return content;

                var sb = new StringBuilder();
                sb.AppendLine("\n\nReferencias:");
                foreach (var s in sources)
                {
                    if (string.IsNullOrWhiteSpace(s.url)) continue;
                    var title = string.IsNullOrWhiteSpace(s.title) ? s.url : s.title;
                    var snippet = string.IsNullOrWhiteSpace(s.snippet) ? string.Empty : $" — {s.snippet}";
                    sb.AppendLine($"- {title} ({s.url}){snippet}");
                }
                return sb.ToString();
            }
            catch
            {
                return content;
            }
        }
    }
}
