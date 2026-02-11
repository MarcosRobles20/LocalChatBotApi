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

      

        // Usa /api/chat de Ollama con OllamaSharp, create an array of messages to respond with context
        public async Task<ClsModOllamaChatResponse> GenerateResponseWithChatApi(ClsModOllamaChatMessages request, string? currentUserId)
        {
            if (_ollamaClient == null)
            {
                throw new InvalidOperationException("OllamaSharp client is not configured. Use the appropriate constructor.");
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Validaciones de negocio
                if (string.IsNullOrEmpty(currentUserId) || currentUserId != request.IdUser)
                {
                    throw new UnauthorizedAccessException("No tienes acceso para enviar mensajes como otro usuario");
                }

                if (!request.Messages.Any() || request.Messages.All(m => m.Role != "user"))
                {
                    throw new ArgumentException("Debe incluir al menos un mensaje de usuario");
                }

                var defaultModel = _configuration["Ollama:DefaultModel"] ?? "mistral:latest";
                var modelToUse = request.Model ?? defaultModel;

                // Construir mensajes para OllamaSharp
                var ollamaMessages = new List<OllamaSharp.Models.Chat.Message>();

                // Agregar mensaje del sistema si no existe
                if (!request.Messages.Any(m => m.Role == "system"))
                {
                    ollamaMessages.Add(new OllamaSharp.Models.Chat.Message
                    {
                        Role = OllamaSharp.Models.Chat.ChatRole.System,
                        Content = "Eres un asistente útil y amigable. Responde de manera clara y concisa."
                    });
                }

                //Recuperar contexto previo si existe un IdChat
                if (!string.IsNullOrEmpty(request.IdChat))
                {
                    var chatHistory = _datChat.GetChatHistory(request.IdChat, request.IdUser, 5);
                    if (chatHistory.Any())
                    {
                        foreach (var historyMessage in chatHistory.OrderBy(m => m.MessageOrder))
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
                }

                // Convertir los mensajes de la request a formato OllamaSharp
                foreach (var msg in request.Messages)
                {
                    var role = msg.Role.ToLower() switch
                    {
                        "system" => OllamaSharp.Models.Chat.ChatRole.System,
                        "user" => OllamaSharp.Models.Chat.ChatRole.User,
                        "assistant" => OllamaSharp.Models.Chat.ChatRole.Assistant,
                        _ => OllamaSharp.Models.Chat.ChatRole.User
                    };

                    ollamaMessages.Add(new OllamaSharp.Models.Chat.Message
                    {
                        Role = role,
                        Content = msg.Content
                    });
                }

                // Crear request para OllamaSharp
                var chatRequest = new OllamaSharp.Models.Chat.ChatRequest
                {
                    Model = modelToUse,
                    Messages = ollamaMessages.ToArray()
                };

                var fullResponse = new StringBuilder();
                
                await foreach (var responseStream in _ollamaClient.ChatAsync(chatRequest))
                {
                    if (responseStream?.Message?.Content != null)
                    {
                        fullResponse.Append(responseStream.Message.Content);
                    }
                }
                
                stopwatch.Stop();
                var aiResponse = fullResponse.ToString();
                var lastUserMessage = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";

                // Persistir la conversación si hay IdChat
                if (!string.IsNullOrEmpty(request.IdChat) && !string.IsNullOrEmpty(lastUserMessage))
                {
                    try
                    {
                        _datChat.SaveChatMessage(request.IdChat, request.IdUser, lastUserMessage, aiResponse, modelToUse);
                        
                        // Si es el primer mensaje, actualizar el título del chat
                        var chatHistory = _datChat.GetChatHistory(request.IdChat, request.IdUser, 10);
                        if (chatHistory.Count <= 2)
                        {
                            var title = GenerateChatTitle(lastUserMessage);
                            _datChat.UpdateChatTitle(request.IdChat, title);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error guardando conversación: {ex.Message}");
                    }
                }

                // Logging de métricas
                try
                {
                    var promptTokens = EstimateTokens(string.Join(" ", request.Messages.Select(m => m.Content)));
                    var responseTokens = EstimateTokens(aiResponse);
                    _datChat.LogUsageMetrics(request.IdUser, modelToUse, promptTokens, responseTokens, stopwatch.ElapsedMilliseconds, request.IdChat);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error guardando métricas: {ex.Message}");
                }

                // Crear respuesta estructurada
                return new ClsModOllamaChatResponse
                {
                    model = modelToUse,
                    message = new ClsModChatMessageItem
                    {
                        Role = "assistant",
                        Content = !string.IsNullOrEmpty(aiResponse) ? aiResponse : "Sin respuesta del modelo"
                    },
                    created_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    done = true
                };
            }
            catch (Exception)
            {
                stopwatch.Stop();
                throw; // Re-throw para que el controladormaneje la respuesta HTTP apropiada
            }
        }

        // MÉTODOS DE NEGOCIO (NO van a la capa de datos)

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
