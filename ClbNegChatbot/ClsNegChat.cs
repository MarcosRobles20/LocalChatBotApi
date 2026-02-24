using ClbDatChatbot;
using ClbModChatbot;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
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
            if(request.IdChat == null || request.IdChat == "") 
            {
                request.IdChat = Guid.NewGuid().ToString();
            }
            return _datChat.CreateNewChat(request);
        }

        public List<ClsModChatMessage> GetChatMessages(ClsModChatRequest request, int? maxMessages = null)
        {
            return _datChat.GetChatHistory(request.IdChat, request.IdUser, maxMessages);
        }

        // Usa /api/chat de Ollama con OllamaSharp, construye array de mensajes con contexto de BD
        public async Task<ClsModOllamaChatResponse> GenerateResponseWithChatApi(ClsModOllamaChatMessages request, string? currentUserId)
        {
            if (_ollamaClient == null)
                throw new InvalidOperationException("OllamaSharp client is not configured. Use the appropriate constructor.");

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Validaciones de negocio
                if (string.IsNullOrEmpty(currentUserId) || currentUserId != request.IdUser)
                    throw new UnauthorizedAccessException("No tienes acceso para enviar mensajes como otro usuario");

                if (!request.Messages.Any() || request.Messages.All(m => m.Role != "user"))
                    throw new ArgumentException("Debe incluir al menos un mensaje de usuario");

                var modelToUse = request.Model ?? _configuration["Ollama:DefaultModel"] ?? "mistral:latest";

                // Resolver si el chat es nuevo
                bool isNewChat = false;
                if (string.IsNullOrEmpty(request.IdChat))
                {
                    request.IdChat = Guid.NewGuid().ToString();
                    isNewChat = true;
                }
                else
                {
                    try
                    {
                        var existingChat = _datChat.GetChatWithIdChat(new ClsModChatRequest
                        {
                            IdChat = request.IdChat,
                            IdUser = request.IdUser
                        });
                        isNewChat = existingChat == null;
                    }
                    catch
                    {
                        isNewChat = true;
                    }
                }

                // Crear el chat en BD si es nuevo
                if (isNewChat)
                {
                    try
                    {
                        _datChat.CreateNewChat(new ClsModOllamaChatRequest
                        {
                            IdChat = request.IdChat,
                            IdUser = request.IdUser
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error creando chat automáticamente: {ex.Message}");
                        throw;
                    }
                }

                // US-001: Guardar mensaje del usuario INMEDIATAMENTE antes de llamar a Ollama
                var lastUserMessage = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";

                if (!string.IsNullOrEmpty(lastUserMessage))
                {
                    try
                    {
                        var userMessageId = _datChat.SaveUserMessage(request.IdChat, request.IdUser, lastUserMessage);
                        Console.WriteLine($"✅ Mensaje de usuario guardado (ID: {userMessageId}): {lastUserMessage.Substring(0, Math.Min(50, lastUserMessage.Length))}...");

                        if (isNewChat)
                            _datChat.UpdateChatTitle(request.IdChat, GenerateChatTitle(lastUserMessage));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error crítico guardando mensaje de usuario: {ex.Message}");
                        throw;
                    }
                }

                // Construir contexto para Ollama
                var ollamaMessages = new List<OllamaSharp.Models.Chat.Message>();

                // 1. System prompt
                ollamaMessages.Add(new OllamaSharp.Models.Chat.Message
                {
                    Role = OllamaSharp.Models.Chat.ChatRole.System,
                    Content = _configuration["Ollama:SystemPrompt"] ?? "Eres un asistente útil"
                });

                // 2. Historial de BD - excluir el mensaje recién guardado (sin respuesta aún)
                if (!isNewChat)
                {
                    var chatHistory = _datChat.GetChatHistory(request.IdChat, request.IdUser, 30);
                    foreach (var historyMessage in chatHistory
                        .Where(m => !string.IsNullOrEmpty(m.AiResponse) && !m.AiResponse.StartsWith("[Error"))
                        .OrderBy(m => m.MessageOrder)
                        .SkipLast(1)) // Excluir último: es el mensaje recién guardado sin respuesta
                    {
                        ollamaMessages.Add(new OllamaSharp.Models.Chat.Message
                        {
                            Role = OllamaSharp.Models.Chat.ChatRole.User,
                            Content = historyMessage.UserMessage
                        });
                        ollamaMessages.Add(new OllamaSharp.Models.Chat.Message
                        {
                            Role = OllamaSharp.Models.Chat.ChatRole.Assistant,
                            Content = historyMessage.AiResponse
                        });
                    }
                }

                // 3. Mensaje nuevo del usuario
                ollamaMessages.Add(new OllamaSharp.Models.Chat.Message
                {
                    Role = OllamaSharp.Models.Chat.ChatRole.User,
                    Content = lastUserMessage
                });

                var chatRequest = new OllamaSharp.Models.Chat.ChatRequest
                {
                    Model = modelToUse,
                    Messages = ollamaMessages.ToArray(),
                    Think = true  // Activa el campo thinking separado (Qwen3, DeepSeek-R1, etc.)
                };

                var fullResponse = new StringBuilder();

                // Si Ollama falla aquí, el mensaje del usuario YA está guardado en BD
                try
                {
                    await foreach (var responseStream in _ollamaClient.ChatAsync(chatRequest))
                    {
                        if (responseStream?.Message?.Content != null)
                            fullResponse.Append(responseStream.Message.Content);
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
                    catch { /* Ignorar error secundario */ }
                    throw;
                }

                stopwatch.Stop();
                var aiResponse = fullResponse.ToString();

                // Guardar respuesta real de la IA
                if (!string.IsNullOrEmpty(aiResponse))
                {
                    try
                    {
                        _datChat.SaveAiResponse(request.IdChat, request.IdUser, aiResponse, modelToUse);
                        Console.WriteLine("✅ Respuesta de IA guardada correctamente");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Error guardando respuesta de IA (mensaje usuario ya guardado): {ex.Message}");
                    }
                }

                // Logging de métricas usando ollamaMessages para contar tokens reales enviados
                try
                {
                    var promptTokens = EstimateTokens(string.Join(" ", ollamaMessages.Select(m => m.Content)));
                    var responseTokens = EstimateTokens(aiResponse);
                    _datChat.LogUsageMetrics(request.IdUser, modelToUse, promptTokens, responseTokens, stopwatch.ElapsedMilliseconds, request.IdChat);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error guardando métricas: {ex.Message}");
                }

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
            catch (Exception)
            {
                stopwatch.Stop();
                throw;
            }
        }

        /// <summary>
        /// Genera una respuesta de la IA en tiempo real usando Server-Sent Events (SSE),
        /// separando el Chain-of-Thought (CoT) de la respuesta final.
        /// <para>
        /// <b>Flujo completo:</b>
        /// <list type="number">
        ///   <item>Valida autenticación y datos del request.</item>
        ///   <item>Crea el chat en BD si no existe.</item>
        ///   <item>Guarda el mensaje del usuario en BD <b>antes</b> de llamar a Ollama,
        ///         garantizando que no se pierda aunque Ollama falle.</item>
        ///   <item>Construye el contexto: system prompt + historial de BD + mensaje nuevo.</item>
        ///   <item>Llama a Ollama con <c>Think = true</c> para activar el campo <c>thinking</c>.</item>
        ///   <item>Detecta chunks de CoT (<c>Message.Thinking</c>) y respuesta (<c>Message.Content</c>)
        ///         y los emite como chunks SSE tipados.</item>
        ///   <item>Al finalizar guarda la respuesta completa en BD y registra métricas.</item>
        ///   <item>Emite chunk <c>done</c> con metadata del chat.</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>Detección del CoT — 2 mecanismos:</b>
        /// <list type="bullet">
        ///   <item><b>Primario:</b> Campo <c>Message.Thinking</c> separado. Usado por Qwen3, DeepSeek-R1
        ///         cuando Ollama tiene <c>think: true</c>. Los chunks de thinking y content
        ///         llegan en campos separados del mismo objeto.</item>
        ///   <item><b>Fallback:</b> Marcadores de texto plano <c>"Thinking..."</c> y
        ///         <c>"...done thinking."</c> en <c>Message.Content</c>. Para modelos
        ///         que no separan el campo pero incluyen marcadores en el texto.</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>Garantía de persistencia:</b> El mensaje del usuario se guarda en BD antes de iniciar
        /// el stream. Si Ollama falla, el mensaje queda guardado con un placeholder de error.
        /// </para>
        /// <para>
        /// <b>Historial de contexto:</b> Se recuperan hasta 30 mensajes previos del chat,
        /// excluyendo el último (recién guardado sin respuesta) para evitar duplicado en el contexto.
        /// Solo se incluyen mensajes con respuesta válida (sin errores previos).
        /// </para>
        /// </summary>
        /// <param name="request">
        /// Request con los datos del chat. Campos relevantes:
        /// <list type="bullet">
        ///   <item><c>IdChat</c>: ID del chat existente. Si es vacío/null se crea uno nuevo automáticamente.</item>
        ///   <item><c>IdUser</c>: ID del usuario. Debe coincidir con el token JWT.</item>
        ///   <item><c>Model</c>: Modelo a usar. Si es null usa el configurado en <c>Ollama:DefaultModel</c>.</item>
        ///   <item><c>Messages</c>: Array de mensajes. Debe incluir al menos uno con <c>Role = "user"</c>.</item>
        /// </list>
        /// </param>
        /// <param name="currentUserId">
        /// ID del usuario autenticado extraído del JWT. Se valida contra <c>request.IdUser</c>
        /// para evitar que un usuario envíe mensajes como otro.
        /// </param>
        /// <returns>
        /// <see cref="IAsyncEnumerable{T}"/> de <see cref="ClsModStreamChunk"/> en el orden:
        /// <c>thinking_start</c> → <c>thinking</c>* → <c>thinking_end</c> → <c>response</c>* → <c>done</c>
        /// <br/>
        /// (Los tipos con * se repiten múltiples veces).
        /// En caso de error se emite un chunk <c>error</c> y el stream termina.
        /// </returns>
        /// <exception cref="InvalidOperationException">Si el cliente Ollama no está configurado.</exception>
        /// <exception cref="UnauthorizedAccessException">Si <c>currentUserId</c> no coincide con <c>request.IdUser</c>.</exception>
        /// <exception cref="ArgumentException">Si no hay mensajes o ninguno tiene <c>Role = "user"</c>.</exception>
        public async IAsyncEnumerable<ClsModStreamChunk> GenerateStreamResponse(
            ClsModOllamaChatMessages request,
            string? currentUserId)
        {
            if (_ollamaClient == null)
                throw new InvalidOperationException("OllamaSharp client is not configured.");

            var stopwatch = Stopwatch.StartNew();

            // Validaciones de negocio
            if (string.IsNullOrEmpty(currentUserId) || currentUserId != request.IdUser)
                throw new UnauthorizedAccessException("No tienes acceso para enviar mensajes como otro usuario");

            if (!request.Messages.Any() || request.Messages.All(m => m.Role != "user"))
                throw new ArgumentException("Debe incluir al menos un mensaje de usuario");

            var modelToUse = request.Model ?? _configuration["Ollama:DefaultModel"] ?? "qwen3:8b";

            // Resolver si el chat es nuevo
            bool isNewChat = false;
            if (string.IsNullOrEmpty(request.IdChat))
            {
                request.IdChat = Guid.NewGuid().ToString();
                isNewChat = true;
            }
            else
            {
                try
                {
                    var existingChat = _datChat.GetChatWithIdChat(new ClsModChatRequest
                    {
                        IdChat = request.IdChat,
                        IdUser = request.IdUser
                    });
                    isNewChat = existingChat == null;
                }
                catch
                {
                    isNewChat = true;
                }
            }

            // Crear el chat en BD si es nuevo
            string? chatCreationError = null;
            if (isNewChat)
            {
                try
                {
                    _datChat.CreateNewChat(new ClsModOllamaChatRequest
                    {
                        IdChat = request.IdChat,
                        IdUser = request.IdUser
                    });
                }
                catch (Exception ex)
                {
                    chatCreationError = $"Error creando chat: {ex.Message}";
                }
            }

            if (chatCreationError != null)
            {
                yield return new ClsModStreamChunk { Type = "error", Error = chatCreationError };
                yield break;
            }

            // Guardar mensaje del usuario INMEDIATAMENTE
            var lastUserMessage = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";
            string? saveMessageError = null;

            if (!string.IsNullOrEmpty(lastUserMessage))
            {
                try
                {
                    _datChat.SaveUserMessage(request.IdChat, request.IdUser, lastUserMessage);

                    if (isNewChat)
                        _datChat.UpdateChatTitle(request.IdChat, GenerateChatTitle(lastUserMessage));
                }
                catch (Exception ex)
                {
                    saveMessageError = $"Error guardando mensaje: {ex.Message}";
                }
            }

            if (saveMessageError != null)
            {
                yield return new ClsModStreamChunk { Type = "error", Error = saveMessageError };
                yield break;
            }

            // Construir contexto para Ollama
            var ollamaMessages = new List<OllamaSharp.Models.Chat.Message>
            {
                new OllamaSharp.Models.Chat.Message
                {
                    Role = OllamaSharp.Models.Chat.ChatRole.System,
                    Content = _configuration["Ollama:SystemPrompt"] ?? "Eres un asistente útil"
                }
            };

            if (!isNewChat)
            {
                var chatHistory = _datChat.GetChatHistory(request.IdChat, request.IdUser, 30);
                foreach (var historyMessage in chatHistory
                    .Where(m => !string.IsNullOrEmpty(m.AiResponse) && !m.AiResponse.StartsWith("[Error"))
                    .OrderBy(m => m.MessageOrder)
                    .SkipLast(1))
                {
                    ollamaMessages.Add(new OllamaSharp.Models.Chat.Message
                    {
                        Role = OllamaSharp.Models.Chat.ChatRole.User,
                        Content = historyMessage.UserMessage
                    });
                    ollamaMessages.Add(new OllamaSharp.Models.Chat.Message
                    {
                        Role = OllamaSharp.Models.Chat.ChatRole.Assistant,
                        Content = historyMessage.AiResponse
                    });
                }
            }

            ollamaMessages.Add(new OllamaSharp.Models.Chat.Message
            {
                Role = OllamaSharp.Models.Chat.ChatRole.User,
                Content = lastUserMessage
            });

            var chatRequest = new OllamaSharp.Models.Chat.ChatRequest
            {
                Model = modelToUse,
                Messages = ollamaMessages.ToArray(),
                //Think = true  // Activa el campo thinking separado (Qwen3, DeepSeek-R1, etc.) //si comentamos este campo ya no es necesario distinguir entre un modelo que si soporte thinking o no, se hace automatico en caso de que tenga el CoT o no.
            };

            // Estado del stream
            var fullResponse = new StringBuilder();
            var isThinking = false;

            // Streaming con detección de CoT
            // Qwen3 via OllamaSharp puede enviar el thinking en Message.Thinking (campo separado)
            // o como texto plano en Message.Content con marcadores "Thinking..." / "...done thinking."

            //si usamos un modelo que no soporta think va tronar, como lo evitamos? // leer tres la var chatRequest, ahi se soluciono el problema
            await foreach (var chunk in _ollamaClient.ChatAsync(chatRequest))
            {
                var content = chunk?.Message?.Content;
                var thinking = chunk?.Message?.Thinking;

                // Caso 1: OllamaSharp entrega el CoT en campo Thinking separado
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

                fullResponse.Append(content);

                // Caso 2: Fallback - modelo envía CoT como texto plano con marcadores
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

            // Si el stream terminó aún en estado thinking, cerrar el bloque
            if (isThinking)
                yield return new ClsModStreamChunk { Type = "thinking_end" };

            // Guardar respuesta real de la IA
            try
            {
                _datChat.SaveAiResponse(request.IdChat, request.IdUser, fullResponse.ToString(), modelToUse);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error guardando respuesta de IA: {ex.Message}");
            }

            // Logging de métricas
            try
            {
                var promptTokens = EstimateTokens(string.Join(" ", ollamaMessages.Select(m => m.Content)));
                var responseTokens = EstimateTokens(fullResponse.ToString());
                _datChat.LogUsageMetrics(request.IdUser, modelToUse, promptTokens, responseTokens, stopwatch.ElapsedMilliseconds, request.IdChat);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error guardando métricas: {ex.Message}");
            }

            // Chunk final con metadata
            yield return new ClsModStreamChunk
            {
                Type = "done",
                IdChat = request.IdChat,
                IsNewChat = isNewChat,
                Model = modelToUse
            };
        }

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

        private string GenerateChatTitle(string firstPrompt)
        {
            // Lógica simple para generar título basado en el primer prompt
            var words = firstPrompt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var title = string.Join(" ", words.Take(5));
            return title.Length > 50 ? title.Substring(0, 47) + "..." : title;
        }

        private int EstimateTokens(string text)
        {
            // Estimación simple: ~4 caracteres por token para texto en español
            return (int)Math.Ceiling(text.Length / 4.0);
        }
    }
}
