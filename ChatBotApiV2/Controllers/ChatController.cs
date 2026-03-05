using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using ClbNegChatbot;
using ClbModChatbot;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using OllamaSharp;
using ChatBotApiV2.Services;
using Microsoft.Extensions.Logging;

namespace ChatBotApiV2.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Requerir autenticación para todos los endpoints
    public class ChatController : ControllerBase
    {
        private readonly ClsNegChat _negChat;
        private readonly IOllamaApiClient _ollamaClient;
        private readonly IConfiguration _configuration;
        private readonly IChatOrchestrator? _chatOrchestrator;
        private readonly ILogger<ChatController> _logger;
        public ChatController(ClsNegChat negChat, IOllamaApiClient ollamaClient, IConfiguration configuration, IChatOrchestrator? chatOrchestrator, ILogger<ChatController> logger)
        {
            _negChat = negChat;
            _ollamaClient = ollamaClient;
            _configuration = configuration;
            _chatOrchestrator = chatOrchestrator;
            _logger = logger;
        }

        [HttpPost]
        [Route("getChatsWithIdUser")]
        public IActionResult GetChatsWithIdUser([FromBody] ClsModChatRequest request)
        {
            try
            {
                // Verificar que el usuario autenticado solo pueda acceder a sus propios chats
                var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(currentUserId) || currentUserId != request.IdUser)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, 
                        new { message = "No tienes acceso a los chats de otro usuario" });
                }

                var result = _negChat.GetChatsWithIdUser(request);
                return StatusCode(StatusCodes.Status200OK, new { response = result });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new { message = "An error occurred while processing your request.", error = ex.Message });
            }
        }

        [HttpPost]
        [Route("getChatWithIdChat")]
        public IActionResult GetChatWithIdChat([FromBody] ClsModChatRequest request)
        {
            try
            {
                // Verificar que el usuario autenticado solo pueda acceder a sus propios chats
                var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(currentUserId) || currentUserId != request.IdUser)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, 
                        new { message = "No tienes acceso a los chats de otro usuario" });
                }

                var result = _negChat.GetChatWithIdChat(request);
                return StatusCode(StatusCodes.Status200OK, new { response = result });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new { message = "An error occurred while processing your request.", error = ex.Message });
            }
        }

        [HttpPost]
        [Route("getChatMessages")]
        public IActionResult GetChatMessages([FromBody] ClsModChatRequest request, [FromQuery] int? maxMessages = 50)
        {
            try
            {
                // Verificar que el usuario autenticado solo pueda acceder a sus propios chats
                var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(currentUserId) || currentUserId != request.IdUser)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, 
                        new { message = "No tienes acceso a los mensajes de otro usuario" });
                }

                // Validación básica
                if (string.IsNullOrEmpty(request.IdChat))
                {
                    return StatusCode(StatusCodes.Status400BadRequest, 
                        new { message = "IdChat es requerido" });
                }

                var messages = _negChat.GetChatMessages(request, maxMessages);
                
                return StatusCode(StatusCodes.Status200OK, new { 
                    success = true,
                    messages = messages,
                    totalMessages = messages.Count,
                    maxMessages = maxMessages,
                    chatId = request.IdChat,
                    timestamp = DateTime.Now
                });
            }                                    
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new { 
                        success = false,
                        message = "Error obteniendo mensajes del chat", 
                        error = ex.Message 
                    });
            }
        }

        [HttpGet]
        [Route("models")]
        [AllowAnonymous] // Permitir acceso sin autenticación para este endpoint
        public async Task<IActionResult> GetAvailableModels()
        {
            try
            {
                // Usar OllamaSharp para obtener modelos
                var models = await _ollamaClient.ListLocalModelsAsync();
                
                return Ok(new { 
                    success = true, 
                    message = "Modelos obtenidos exitosamente",
                    models = models.Select(m => new {
                        name = m.Name,
                        size = m.Size,
                        modified_at = m.ModifiedAt,
                        digest = m.Digest,
                        details = m.Details
                    })
                });
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { 
                    success = false,
                    message = "No se pudo conectar con Ollama. ¿Está ejecutándose?", 
                    error = ex.Message 
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { 
                    success = false,
                    message = "Error interno al conectar con Ollama", 
                    error = ex.Message 
                });
            }
        }

        
        /// <summary>
        /// ENDPOINT PRINCIPAL - Usa /api/chat de Ollama con mensajes estructurados
        /// Crea automáticamente el chat si no existe (nuevo flujo)
        /// </summary>
        [HttpPost]
        [Route("chatWithMemory")]
        public async Task<IActionResult> ChatWithMessages([FromBody] ClsModOllamaChatMessages request)
        {
            try
            {
                // Obtener el usuario autenticado
                var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                // Delegar lógica a la capa de negocio
                var result = await _negChat.GenerateResponseWithChatApi(request, currentUserId);

                // Obtener el último mensaje del usuario para mostrarlo en la respuesta
                var lastUserMessage = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";

                return Ok(new { 
                    success = true,
                    userPrompt = lastUserMessage,
                    aiResponse = result.message?.Content,
                    model = result.model,
                    timestamp = DateTime.Now,
                    conversationHistory = request.Messages.Count,
                    endpoint = "/api/chatWithMemory", // Para identificar que usa el endpoint de chat
                    // Información del chat (creado automáticamente si es necesario)
                    idChat = result.IdChat,
                    isNewChat = result.IsNewChat,
                    chatCreatedAutomatically = result.IsNewChat
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { 
                    success = false,
                    message = ex.Message 
                });
            }
            catch (ArgumentException ex)
            {
                return StatusCode(StatusCodes.Status400BadRequest, new { 
                    success = false,
                    message = ex.Message 
                });
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { 
                    success = false,
                    message = "No se pudo conectar con Ollama. ¿Está ejecutándose?", 
                    error = ex.Message 
                });
            }
            catch (TaskCanceledException ex)
            {
                return StatusCode(StatusCodes.Status408RequestTimeout, new { 
                    success = false,
                    message = "Timeout al conectar con Ollama. El modelo puede estar cargando.", 
                    error = ex.Message 
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { 
                    success = false,
                    message = "Error interno procesando chat con Ollama (Chat API)", 
                    error = ex.Message 
                });
            }
        }


        /// <summary>
        /// Generar ID para nuevo chat sin crear en BD (solo para frontend)
        /// </summary>
        [HttpGet]
        [Route("generateChatId")]
        public IActionResult GenerateChatId()
        {
            try
            {
                var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(currentUserId))
                {
                    return StatusCode(StatusCodes.Status401Unauthorized, new { 
                        success = false,
                        message = "Usuario no autenticado" 
                    });
                }

                var newChatId = Guid.NewGuid().ToString();

                return Ok(new { 
                    success = true,
                    message = "ID de chat generado - se creará automáticamente al enviar el primer mensaje",
                    idChat = newChatId,
                    userId = currentUserId,
                    instruction = "Usar este ID en chatWithMemory - el chat se creará automáticamente"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { 
                    success = false,
                    message = "Error generando ID de chat", 
                    error = ex.Message 
                });
            }
        }

        /// <summary>
        /// Endpoint SSE (Server-Sent Events) que transmite la respuesta de la IA en tiempo real,
        /// separando el Chain-of-Thought (CoT) de la respuesta final.
        /// <para>
        /// <b>Protocolo SSE:</b> Cada evento se emite en formato <c>data: {json}\n\n</c>.
        /// El cliente debe leer el stream con <c>fetch</c> + <c>ReadableStream</c>
        /// (NO usar <c>EventSource</c> ya que requiere POST + Authorization header).
        /// </para>
        /// <para>
        /// <b>Secuencia de eventos emitidos:</b>
        /// <code>
        /// data: {"type":"thinking_start"}
        ///
        /// data: {"type":"thinking","content":"texto del razonamiento..."}
        /// ... (múltiples chunks de thinking)
        ///
        /// data: {"type":"thinking_end"}
        ///
        /// data: {"type":"response","content":"texto de la respuesta..."}
        /// ... (múltiples chunks de response)
        ///
        /// data: {"type":"done","idChat":"abc-123","isNewChat":false,"model":"qwen3:8b"}
        /// </code>
        /// En caso de error:
        /// <code>
        /// data: {"type":"error","error":"mensaje del error"}
        /// </code>
        /// </para>
        /// <para>
        /// <b>Headers de respuesta:</b>
        /// <list type="bullet">
        ///   <item><c>Content-Type: text/event-stream</c></item>
        ///   <item><c>Cache-Control: no-cache</c></item>
        ///   <item><c>X-Accel-Buffering: no</c> — Deshabilita buffering en Nginx</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>Modelos compatibles con CoT:</b> Qwen3, DeepSeek-R1, DeepSeek-v3.1, GPT-OSS.
        /// Modelos sin CoT solo emitirán chunks de tipo <c>response</c> y <c>done</c>.
        /// </para>
        /// </summary>
        /// <remarks>
        /// Ejemplo de consumo desde Angular/JavaScript:
        /// <code>
        /// const response = await fetch('/api/chat/chatStream', {
        ///   method: 'POST',
        ///   headers: {
        ///     'Content-Type': 'application/json',
        ///     'Authorization': `Bearer ${token}`
        ///   },
        ///   body: JSON.stringify({
        ///     idChat: '',
        ///     idUser: 'user-id',
        ///     messages: [{ role: 'user', content: 'mensaje' }]
        ///   })
        /// });
        ///
        /// const reader = response.body.getReader();
        /// // Leer chunks y parsear líneas "data: {...}"
        /// </code>
        /// </remarks>
        [HttpPost]
        [Route("chatStream")]
        public async Task ChatStream([FromBody] ClsModOllamaChatMessages request)
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            Response.Headers.Append("Content-Type", "text/event-stream");
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("X-Accel-Buffering", "no");

            async Task SendChunk(ClsModStreamChunk chunk)
            {
                var json = JsonSerializer.Serialize(chunk, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                await Response.WriteAsync($"data: {json}\n\n");
                await Response.Body.FlushAsync();
            }

            try
            {
                if (_chatOrchestrator is not null)
                {
                    await foreach (var chunk in _chatOrchestrator.StreamViaProxyAsync(request, currentUserId, HttpContext.RequestAborted))
                    {
                        await SendChunk(chunk);
                    }
                    return;
                }

                // Fallback: existing internal streaming
                await foreach (var chunk in _negChat.GenerateStreamResponse(request, currentUserId))
                {
                    await SendChunk(chunk);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                await SendChunk(new ClsModStreamChunk { Type = "error", Error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                await SendChunk(new ClsModStreamChunk { Type = "error", Error = ex.Message });
            }
            catch (Exception ex)
            {
                await SendChunk(new ClsModStreamChunk { Type = "error", Error = $"Error interno: {ex.Message}" });
            }
        }
    }
}
