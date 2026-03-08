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

        public async IAsyncEnumerable<ClsModStreamChunk> StreamViaProxyAsync(ClsModOllamaChatMessages request, string? currentUserId, [EnumeratorCancellation] CancellationToken ct)
        {
            // ValidateRequest logic (replicated from ClsNegChat.ValidateRequest)
            if (string.IsNullOrEmpty(currentUserId) || currentUserId != request.IdUser)
                throw new UnauthorizedAccessException("No tienes acceso para enviar mensajes como otro usuario");

            if (!request.Messages.Any() || request.Messages.All(m => m.Role != "user"))
                throw new ArgumentException("Debe incluir al menos un mensaje de usuario");

            // 2. Begin or resolve chat and persist user message
            var (idChat, isNewChat) = await _negChat.BeginChatAsync(request.IdChat, request.IdUser);
            request.IdChat = idChat;

            var lastUserMessage = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? string.Empty;
            await _negChat.SaveUserMessageAsync(request.IdChat, request.IdUser, lastUserMessage, request.Kind);

            // 3. Build context (history + system prompt) using existing business logic
            //temporalmente deshabilitado, ya que langgraph y langchain manejan mejor el contexto
            var ollamaContext = _negChat.BuildOllamaContextPublic(request.IdChat, request.IdUser, lastUserMessage, isNewChat);

            // Map Ollama context messages to ClsModOllamaChatMessages.Messages so Python receives the same history
            var payloadForProxy = new ClsModOllamaChatMessages
            {
                IdChat = request.IdChat,
                IdUser = request.IdUser,
                Model = request.Model,
                Stream = true,
                Messages = new List<ClsModChatMessageItem>()
            };

            foreach (var msg in ollamaContext)
            {
                // msg.Role is an enum ChatRole
                var role = msg.Role?.ToString().ToLower();
                payloadForProxy.Messages.Add(new ClsModChatMessageItem { Role = role, Content = msg.Content });
            }

            // 4. Orchestrate endpoint: single call that can stream (SSE) or return JSON
            OrchestrateRequestDto orchReq = new OrchestrateRequestDto
            {
                text = null, // enviar solo messages como contexto
                messages = payloadForProxy.Messages,
                model = request.Model,
                tool_hint = null, // se puede setear si el cliente lo envía en otra capa
                idChat = request.IdChat,
                session_id = null,
                isNewChat = isNewChat
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var responseSb = new StringBuilder();
            var fallbackContentSb = new StringBuilder();
            var sourcesBuffer = new List<string>();
            var imagePaths = new List<string>();
            string modelUsed = request.Model ?? "unknown";

            // Consumir el orquestador (SSE o JSON) en un solo llamado
            bool gotDone = false;
            await foreach (var chunk in _proxyService.OrchestrateStreamAsync(orchReq, ct))
            {
                if (ct.IsCancellationRequested)
                    yield break;

                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    if (chunk.Type == "response")
                    {
                        responseSb.Append(chunk.Content);
                    }
                    else if (chunk.Type == "sources")
                    {
                        // Guardar las referencias para agregarlas al final, debajo del texto
                        var formatted = TryFormatSources(chunk.Content);
                        if (!string.IsNullOrEmpty(formatted))
                            sourcesBuffer.Add(formatted);
                    }
                    else if (chunk.Type == "image")
                    {
                        imagePaths.Add(chunk.Content);
                    }
                    else
                    {
                        // Contenido de otros pasos (plan, tool_call, tool_result, etc.)
                        fallbackContentSb.Append(chunk.Content);
                    }
                }

                if (chunk.Type == "done")
                {
                    gotDone = true;
                    if (!string.IsNullOrEmpty(chunk.Model)) modelUsed = chunk.Model;
                }

                yield return chunk;
            }

            // Si no hubo response explícito pero sí contenido en otros pasos, usarlo
            if (responseSb.Length == 0 && fallbackContentSb.Length > 0)
            {
                responseSb.Append(fallbackContentSb.ToString());
            }

            // Append referencias al final del texto acumulado
            if (sourcesBuffer.Count > 0)
            {
                if (responseSb.Length > 0) responseSb.AppendLine();
                foreach (var src in sourcesBuffer)
                {
                    responseSb.Append(src);
                }
            }

            // Si el orquestador no envió done, lo emitimos aquí para cerrar el flujo
            if (!gotDone)
            {
                yield return new ClsModStreamChunk
                {
                    Type = "done",
                    IdChat = request.IdChat,
                    IsNewChat = isNewChat,
                    Model = request.Model ?? modelUsed
                };
            }

            stopwatch.Stop();
            var elapsed = stopwatch.ElapsedMilliseconds;

            // Persist final AI response and metrics (texto)
            try
            {
                await _negChat.FinalizeAiResponseAsyncPublic(request.IdChat, request.IdUser, responseSb.ToString(), modelUsed, ollamaContext, elapsed, isNewChat, lastUserMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist final AI response or metadata");
            }

            // Persistir imágenes como mensajes separados con kind="image"
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
