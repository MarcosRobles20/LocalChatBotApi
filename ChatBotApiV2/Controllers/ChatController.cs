using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using ClbNegChatbot;
using ClbModChatbot;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ChatBotApiV2.Services;
using Microsoft.Extensions.Logging;

namespace ChatBotApiV2.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly ClsNegChat _negChat;
        private readonly IConfiguration _configuration;
        private readonly IChatOrchestrator? _chatOrchestrator;
        private readonly ILogger<ChatController> _logger;

        public ChatController(ClsNegChat negChat, IConfiguration configuration, IChatOrchestrator? chatOrchestrator, ILogger<ChatController> logger)
        {
            _negChat = negChat;
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
        public IActionResult GetAvailableModels()
        {
            return StatusCode(StatusCodes.Status501NotImplemented, new
            {
                success = false,
                message = "La obtención de modelos locales fue deshabilitada. Usa el IA Proxy Service para información de modelos."
            });
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
