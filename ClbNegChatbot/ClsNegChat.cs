using ClbDatChatbot;
using ClbModChatbot;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;
using System.Text;
using OllamaSharp;
using System.Diagnostics;

namespace ClbNegChatbot
{
    public class ClsNegChat
    {
        private readonly ClsDatChat _datChat;
        private readonly IConfiguration _configuration;
        private readonly IOllamaApiClient? _ollamaClient;

        // Constructor original para compatibilidad
        public ClsNegChat(IConfiguration configuration)
        {
            _datChat = new ClsDatChat(configuration);
            _configuration = configuration;
        }

        // Constructor con OllamaSharp
        public ClsNegChat(IConfiguration configuration, IOllamaApiClient ollamaClient)
        {
            _datChat = new ClsDatChat(configuration);
            _configuration = configuration;
            _ollamaClient = ollamaClient;
        }

        public List<ClsModChat> GetChatsWithIdUser(ClsModChatRequest request)
        {
            return _datChat.GetChatsWithIdUser(request);
        }

        public ClsModChat GetChatWithIdChat(ClsModChatRequest request)
        {
            return _datChat.GetChatWithIdChat(request);
        }

        public ClsModChat CreateNewChat(ClsModOllamaChatRequest request)
        {
            if (string.IsNullOrEmpty(request.IdChat))
                request.IdChat = Guid.NewGuid().ToString();

            return _datChat.CreateNewChat(request);
        }

        public List<ClsModChatMessage> GetChatMessages(ClsModChatRequest request, int? maxMessages = null)
        {
            return _datChat.GetChatHistory(request.IdChat, request.IdUser, maxMessages);
        }

        // ---------------------------------------------------------------------------
        // Métodos públicos — orquestadores puros
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Usa /api/chat de Ollama con OllamaSharp.
        /// Orquesta validación, persistencia, construcción de contexto y llamada al modelo.
        /// </summary>
        public async Task<ClsModOllamaChatResponse> GenerateResponseWithChatApi(
            ClsModOllamaChatMessages request,
            string? currentUserId)
        {
            if (_ollamaClient == null)
                throw new InvalidOperationException("OllamaSharp client is not configured. Use the appropriate constructor.");

            var stopwatch = Stopwatch.StartNew();

            ValidateRequest(request, currentUserId);

            var modelToUse = request.Model ?? _configuration["Ollama:DefaultModel"] ?? "mistral:latest";

            var (idChat, isNewChat) = await ResolveOrCreateChatAsync(request.IdChat, request.IdUser);
            request.IdChat = idChat;

            var lastUserMessage = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";
            await PersistUserMessageAsync(request.IdChat, request.IdUser, lastUserMessage, request.Kind);

            var ollamaMessages = BuildOllamaContext(request.IdChat, request.IdUser, lastUserMessage, isNewChat);

            var chatRequest = new OllamaSharp.Models.Chat.ChatRequest
            {
                Model = modelToUse,
                Messages = ollamaMessages.ToArray(),
                Think = true
            };

            var fullResponse = new StringBuilder();

            try
            {
                await foreach (var chunk in _ollamaClient.ChatAsync(chatRequest))
                {
                    if (chunk?.Message?.Content != null)
                        fullResponse.Append(chunk.Message.Content);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error en Ollama: {ex.Message}");
                try
                {
                    _datChat.SaveAiResponse(request.IdChat, request.IdUser,
                        $"[Error: No se pudo generar respuesta - {ex.Message}]",
                        modelToUse);
                }
                catch { }
                throw;
            }

            stopwatch.Stop();

            await PersistAiResponseAsync(
                request.IdChat, request.IdUser, fullResponse.ToString(),
                modelToUse, ollamaMessages, stopwatch.ElapsedMilliseconds);

            var aiResponse = fullResponse.ToString();

            return new ClsModOllamaChatResponse
            {
                model = modelToUse,
                message = new ClsModChatMessageItem
                {
                    Role = "assistant",
                    Content = !string.IsNullOrEmpty(aiResponse) ? aiResponse : "Sin respuesta del modelo"
                },
                created_at = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                done = true,
                IdChat = request.IdChat,
                IsNewChat = isNewChat
            };
        }

        /// <summary>
        /// Genera una respuesta en tiempo real via SSE separando CoT de la respuesta final.
        /// Orquestador puro: delega cada paso a una subfunción especializada.
        /// </summary>
        /// <param name="request">Datos del chat. <c>IdChat</c> vacío crea un chat nuevo automáticamente.</param>
        /// <param name="currentUserId">ID del usuario autenticado extraído del JWT.</param>
        /// <param name="cancellationToken">Permite cancelar el stream si el cliente se desconecta.</param>
        /// <returns>
        /// Stream de <see cref="ClsModStreamChunk"/> en el orden:
        /// <c>thinking_start</c> → <c>thinking</c>* → <c>thinking_end</c> → <c>response</c>* → <c>done</c>.
        /// En caso de error se emite un chunk <c>error</c> y el stream termina.
        /// </returns>
        public async IAsyncEnumerable<ClsModStreamChunk> GenerateStreamResponse(
            ClsModOllamaChatMessages request,
            string? currentUserId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_ollamaClient == null)
                throw new InvalidOperationException("OllamaSharp client is not configured.");

            var stopwatch = Stopwatch.StartNew();

            ValidateRequest(request, currentUserId);

            var modelToUse = request.Model ?? _configuration["Ollama:DefaultModel"] ?? "qwen3:8b";

            // Paso 1: Resolver o crear el chat
            string idChat;
            bool isNewChat;
            string? stepError = null;
            try
            {
                (idChat, isNewChat) = await ResolveOrCreateChatAsync(request.IdChat, request.IdUser);
                request.IdChat = idChat;
            }
            catch (Exception ex)
            {
                stepError = ex.Message;
                idChat = request.IdChat ?? string.Empty;
                isNewChat = false;
            }

            if (stepError != null)
            {
                yield return new ClsModStreamChunk { Type = "error", Error = stepError };
                yield break;
            }

            // Paso 2: Guardar mensaje del usuario antes de llamar a Ollama
            var lastUserMessage = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";
            stepError = null;
            try
            {
                await PersistUserMessageAsync(request.IdChat, request.IdUser, lastUserMessage, request.Kind);
            }
            catch (Exception ex)
            {
                stepError = ex.Message;
            }

            if (stepError != null)
            {
                yield return new ClsModStreamChunk { Type = "error", Error = stepError };
                yield break;
            }

            // Paso 3: Construir contexto para Ollama
            var ollamaMessages = BuildOllamaContext(request.IdChat, request.IdUser, lastUserMessage, isNewChat);

            var chatRequest = new OllamaSharp.Models.Chat.ChatRequest
            {
                Model = modelToUse,
                Messages = ollamaMessages.ToArray()
                // Think omitido: Ollama lo activa automáticamente según el modelo.
            };

            // Paso 4: Stream con detección de CoT — acumula la respuesta para persistencia
            var fullResponse = new StringBuilder();

            await foreach (var chunk in StreamOllamaResponseAsync(chatRequest, fullResponse, cancellationToken))
            {
                yield return chunk;
            }

            stopwatch.Stop();

            // Paso 5: Persistir respuesta y métricas
            var aiResponse = fullResponse.ToString();
            await PersistAiResponseAsync(
                request.IdChat, request.IdUser, aiResponse,
                modelToUse, ollamaMessages, stopwatch.ElapsedMilliseconds);

            // Paso 6: Título generado con Q&A completo — solo para chats nuevos
            if (isNewChat)
            {
                try
                {
                    var title = await GenerateChatTitleAsync(lastUserMessage, aiResponse);
                    _datChat.UpdateChatTitle(request.IdChat, title);
                }
                catch (Exception ex) { Console.WriteLine($"⚠️ Error generando título: {ex.Message}"); }
            }

            // Chunk final con metadata del chat
            yield return new ClsModStreamChunk
            {
                Type = "done",
                IdChat = request.IdChat,
                IsNewChat = isNewChat,
                Model = modelToUse
            };
        }

        // ---------------------------------------------------------------------------
        // Subfunciones privadas — responsabilidad única, reutilizables
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Valida que el usuario autenticado coincida con el del request y que haya
        /// al menos un mensaje de tipo "user".
        /// Lanza excepción tipada para que el llamador decida cómo responder.
        /// </summary>
        private static void ValidateRequest(ClsModOllamaChatMessages request, string? currentUserId)
        {
            if (string.IsNullOrEmpty(currentUserId) || currentUserId != request.IdUser)
                throw new UnauthorizedAccessException("No tienes acceso para enviar mensajes como otro usuario");

            if (!request.Messages.Any() || request.Messages.All(m => m.Role != "user"))
                throw new ArgumentException("Debe incluir al menos un mensaje de usuario");
        }

        /// <summary>
        /// Determina si el chat existe en BD. Si no existe (o el ID es nulo) lo crea.
        /// Devuelve el ID definitivo del chat y si fue recién creado.
        /// </summary>
        private async Task<(string IdChat, bool IsNewChat)> ResolveOrCreateChatAsync(
            string? requestedIdChat,
            string idUser)
        {
            bool isNewChat;
            string idChat;

            if (string.IsNullOrEmpty(requestedIdChat))
            {
                idChat = Guid.NewGuid().ToString();
                isNewChat = true;
            }
            else
            {
                idChat = requestedIdChat;
                try
                {
                    var existing = _datChat.GetChatWithIdChat(new ClsModChatRequest
                    {
                        IdChat = idChat,
                        IdUser = idUser
                    });
                    isNewChat = existing == null;
                }
                catch
                {
                    isNewChat = true;
                }
            }

            if (isNewChat)
            {
                _datChat.CreateNewChat(new ClsModOllamaChatRequest
                {
                    IdChat = idChat,
                    IdUser = idUser
                });
            }

            return await Task.FromResult((idChat, isNewChat));
        }

        /// <summary>
        /// Guarda el mensaje del usuario en BD antes de llamar a Ollama, garantizando
        /// que no se pierda aunque el modelo falle.
        /// Si es un chat nuevo, actualiza el título con las primeras palabras del mensaje.
        /// </summary>
        private async Task PersistUserMessageAsync(
            string idChat,
            string idUser,
            string userMessage,
            string kind)
        {
            if (string.IsNullOrEmpty(userMessage))
                return;

            _datChat.SaveUserMessage(idChat, idUser, userMessage, kind);

            await Task.CompletedTask;
        }

        /// <summary>
        /// Construye la lista de mensajes que se enviará a Ollama:
        /// system prompt → historial de BD (pares user/assistant) → mensaje nuevo.
        /// <para>
        /// Punto de extensión para futuros tipos de entrada (OCR, imágenes, herramientas,
        /// embeddings, etc.). Solo cambia quién llama a este método y qué pasa como
        /// <paramref name="userMessage"/>.
        /// </para>
        /// </summary>
        private List<OllamaSharp.Models.Chat.Message> BuildOllamaContext(
            string idChat,
            string idUser,
            string userMessage,
            bool isNewChat)
        {
            var messages = new List<OllamaSharp.Models.Chat.Message>
            {
                new OllamaSharp.Models.Chat.Message
                {
                    Role = OllamaSharp.Models.Chat.ChatRole.System,
                    Content = _configuration["Ollama:SystemPrompt"] ?? "Tu nombre como LLM es Markong"
                }
            };

            if (!isNewChat)
            {
                var history = _datChat.GetChatHistory(idChat, idUser, 10);

                // Cada fila de BD es un mensaje individual (user O assistant), no un par.
                // Se excluye el último registro porque es el mensaje recién guardado sin respuesta aún.
                // Ignorar entradas vacías o que solo contienen whitespace.
                foreach (var entry in history
                    .Where(m => (m.Role == "user" || m.Role == "assistant") && !string.IsNullOrWhiteSpace(m.Content))
                    .OrderBy(m => m.MessageOrder)
                    .SkipLast(1))
                {
                    var ollamaRole = entry.Role == "user"
                        ? OllamaSharp.Models.Chat.ChatRole.User
                        : OllamaSharp.Models.Chat.ChatRole.Assistant;

                    messages.Add(new OllamaSharp.Models.Chat.Message
                    {
                        Role = ollamaRole,
                        Content = entry.Content.Trim()
                    });
                }
            }

            // Añadir el mensaje del usuario sólo si contiene texto útil
            if (!string.IsNullOrWhiteSpace(userMessage))
            {
                messages.Add(new OllamaSharp.Models.Chat.Message
                {
                    Role = OllamaSharp.Models.Chat.ChatRole.User,
                    Content = userMessage.Trim()
                });
            }

            return messages;
        }

        /// <summary>
        /// Itera el stream de Ollama y emite chunks SSE tipados, detectando CoT por dos mecanismos:
        /// <list type="bullet">
        ///   <item><b>Primario:</b> Campo <c>Message.Thinking</c> separado (Qwen3, DeepSeek-R1).</item>
        ///   <item><b>Fallback:</b> Marcadores de texto <c>"Thinking..."</c> / <c>"...done thinking."</c>.</item>
        /// </list>
        /// Acumula la respuesta en <paramref name="responseAccumulator"/> para que el llamador
        /// pueda persistirla sin un segundo viaje a Ollama.
        /// </summary>
        private async IAsyncEnumerable<ClsModStreamChunk> StreamOllamaResponseAsync(
            OllamaSharp.Models.Chat.ChatRequest chatRequest,
            StringBuilder responseAccumulator,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var isThinking = false;

            await foreach (var chunk in _ollamaClient!.ChatAsync(chatRequest, cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                var content = chunk?.Message?.Content;
                var thinking = chunk?.Message?.Thinking;

                // Caso 1: campo Thinking separado (Qwen3, DeepSeek-R1 con Think=true)
                if (!string.IsNullOrEmpty(thinking))
                {
                    if (!isThinking)
                    {
                        isThinking = true;
                        yield return new ClsModStreamChunk { Type = "thinking_start" };
                    }
                    yield return new ClsModStreamChunk { Type = "thinking", Content = thinking };
                    continue;
                }

                // Transición: había thinking y ahora llega content → fin del CoT
                if (isThinking && !string.IsNullOrEmpty(content))
                {
                    isThinking = false;
                    yield return new ClsModStreamChunk { Type = "thinking_end" };
                }

                if (string.IsNullOrEmpty(content)) continue;

                responseAccumulator.Append(content);

                // Caso 2: fallback — CoT como texto plano con marcadores
                if (!isThinking && content.Contains("Thinking..."))
                {
                    isThinking = true;
                    yield return new ClsModStreamChunk { Type = "thinking_start" };
                    continue;
                }

                if (isThinking && content.Contains("...done thinking."))
                {
                    isThinking = false;
                    yield return new ClsModStreamChunk { Type = "thinking_end" };
                    continue;
                }

                yield return new ClsModStreamChunk
                {
                    Type = isThinking ? "thinking" : "response",
                    Content = content
                };
            }

            if (isThinking)
                yield return new ClsModStreamChunk { Type = "thinking_end" };
        }

        /// <summary>
        /// Guarda la respuesta de la IA en BD y registra métricas de uso.
        /// Los errores se logean sin propagarse para no afectar al usuario por
        /// problemas de persistencia secundaria.
        /// </summary>
        private async Task PersistAiResponseAsync(
            string idChat,
            string idUser,
            string aiResponse,
            string model,
            List<OllamaSharp.Models.Chat.Message> contextMessages,
            long elapsedMs,
            string kind = "text")
        {
            try
            {
                _datChat.SaveAiResponse(idChat, idUser, aiResponse, model, kind);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error guardando respuesta de IA: {ex.Message}");
            }

            try
            {
                var promptTokens = EstimateTokens(string.Join(" ", contextMessages.Select(m => m.Content)));
                var responseTokens = EstimateTokens(aiResponse);
                _datChat.LogUsageMetrics(idUser, model, promptTokens, responseTokens, elapsedMs, idChat);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error guardando métricas: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        // ---------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------

        private string BuildContextFromHistory(List<ClsModChatMessage> history)
        {
            var context = new StringBuilder();
            foreach (var message in history.OrderBy(m => m.MessageOrder))
            {
                context.AppendLine($"Usuario: {message.UserMessage}");
                context.AppendLine($"Asistente: {message.AiResponse}");
            }
            return context.ToString();
        }

        private async Task<string> GenerateChatTitleAsync(string userMessage, string aiResponse)
        {
            if (_ollamaClient == null)
                return GenerateChatTitleFallback(userMessage);

            try
            {
                var model = _configuration["Ollama:TitleModel"]
                         ?? _configuration["Ollama:DefaultModel"]
                         ?? "qwen3:8b";

                var timeoutSeconds = int.TryParse(_configuration["Ollama:TitleTimeoutSeconds"], out var t) ? t : 45;

                var titleRequest = new OllamaSharp.Models.Chat.ChatRequest
                {
                    Model = model,
                    Messages = new []
                    {
                        new OllamaSharp.Models.Chat.Message
                        {
                            Role = OllamaSharp.Models.Chat.ChatRole.System,
                            Content = "Eres un asistente que genera títulos. Responde ÚNICAMENTE con el título, sin explicaciones, sin comillas, sin puntos al final."
                        },
                        new OllamaSharp.Models.Chat.Message
                        {
                            Role = OllamaSharp.Models.Chat.ChatRole.User,
                            Content = $"Genera un título corto (máximo 6 palabras) para esta conversación:\nPregunta: {userMessage}\nRespuesta: {aiResponse}"
                        }
                    },
                    Think = false,
                    Options = new OllamaSharp.Models.RequestOptions { Temperature = 0.3f, NumPredict = 20 }
                };

                var titleBuilder = new StringBuilder();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

                await foreach (var chunk in _ollamaClient.ChatAsync(titleRequest, cts.Token))
                    if (chunk?.Message?.Content != null)
                        titleBuilder.Append(chunk.Message.Content);

                var aiTitle = titleBuilder.ToString().Trim();
                return !string.IsNullOrEmpty(aiTitle)
                    ? aiTitle.Length > 60 ? aiTitle[..57] + "..." : aiTitle
                    : GenerateChatTitleFallback(userMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"IA no pudo generar título, usando fallback: {ex.Message}");
                return GenerateChatTitleFallback(userMessage);
            }
        }

        private static string GenerateChatTitleFallback(string firstPrompt)
        {
            var words = firstPrompt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var title = string.Join(" ", words.Take(5));
            return title.Length > 50 ? title[..47] + "..." : title;
        }

        private int EstimateTokens(string text)
        {
            // ~4 caracteres por token para texto en español
            return (int)Math.Ceiling(text.Length / 4.0);
        }

        // Public wrappers for orchestrator
        public async Task<(string IdChat, bool IsNewChat)> BeginChatAsync(string? requestedIdChat, string idUser)
        {
            return await ResolveOrCreateChatAsync(requestedIdChat, idUser);
        }

        public async Task SaveUserMessageAsync(string idChat, string idUser, string userMessage, string kind)
        {
            await PersistUserMessageAsync(idChat, idUser, userMessage, kind);
        }

        public List<OllamaSharp.Models.Chat.Message> BuildOllamaContextPublic(string idChat, string idUser, string userMessage, bool isNewChat)
        {
            return BuildOllamaContext(idChat, idUser, userMessage, isNewChat);
        }

        public async Task FinalizeAiResponseAsyncPublic(string idChat, string idUser, string aiResponse, string model, List<OllamaSharp.Models.Chat.Message> contextMessages, long elapsedMs, bool isNewChat, string lastUserMessage)
        {
            await PersistAiResponseAsync(idChat, idUser, aiResponse, model, contextMessages, elapsedMs);

            if (isNewChat)
            {
                try
                {
                    var title = await GenerateChatTitleAsync(lastUserMessage, aiResponse);
                    _datChat.UpdateChatTitle(idChat, title);
                }
                catch (Exception ex) { Console.WriteLine($"Error generando título: {ex.Message}"); }
            }
        }

        public async Task SaveAiResponseAsyncPublic(string idChat, string idUser, string aiResponse, string model, string kind = "text")
        {
            await PersistAiResponseAsync(idChat, idUser, aiResponse, model, new List<OllamaSharp.Models.Chat.Message>(), 0, kind);
        }
    }
}
