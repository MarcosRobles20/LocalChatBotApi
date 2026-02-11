using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using ClbNegChatbot;
using ClbModChatbot;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using OllamaSharp;

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
        public ChatController(ClsNegChat negChat, IOllamaApiClient ollamaClient, IConfiguration configuration)
        {
            _negChat = negChat;
            _ollamaClient = ollamaClient;
            _configuration = configuration;
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
                    timestamp = DateTime.UtcNow
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

        
        [HttpPost]
        [Route("createChat")]
        public IActionResult CreateNewChat([FromBody] ClsModOllamaChatRequest request)
        {
            try
            {
                // Verificar que el usuario autenticado solo pueda crear sus propios mensajes
                var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(currentUserId) || currentUserId != request.IdUser)
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        new { message = "No tienes acceso para crear chat como otro usuario" });
                }

                var result = _negChat.CreateNewChat(request);

                return StatusCode(StatusCodes.Status200OK, new { 
                    success = true,
                    message = "Chat creado exitosamente",
                    response = result
                });

            }
            catch(Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { 
                    success = false,
                    message = "Error interno creando nuevo chat", 
                    error = ex.Message 
                });
            }
        }

        /// <summary>
        /// NUEVO ENDPOINT - Usa /api/chat de Ollama con mensajes estructurados
        /// </summary>
        [HttpPost]
        [Route("chatWithMemory")]
        public async Task<IActionResult> ChatWithMessages([FromBody] ClsModOllamaChatMessages request)
        {
            try
            {
                // Obtener el usuario autenticado
                var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                // lógica a la capa de negocio
                var result = await _negChat.GenerateResponseWithChatApi(request, currentUserId);

                // Obtener el último mensaje del usuario para mostrarlo en la respuesta
                var lastUserMessage = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";

                return Ok(new { 
                    success = true,
                    userPrompt = lastUserMessage,
                    aiResponse = result.message?.Content,
                    model = result.model,
                    timestamp = DateTime.UtcNow,
                    conversationHistory = request.Messages.Count,
                    endpoint = "/api/chatWithMemory" // Para identificar que usa el endpoint de chat
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
    }
}
