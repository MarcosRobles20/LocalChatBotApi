using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using ClbNegChatbot;
using ClbModChatbot;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace ChatBotApiV2.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Requerir autenticación para todos los endpoints
    public class ChatController : ControllerBase
    {
        private readonly ClsNegChat _negChat;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public ChatController(ClsNegChat negChat, HttpClient httpClient, IConfiguration configuration)
        {
            _negChat = negChat;
            _httpClient = httpClient;
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

                var result = _negChat.GetChatsWithIdChat(request);
                return StatusCode(StatusCodes.Status200OK, new { response = result });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new { message = "An error occurred while processing your request.", error = ex.Message });
            }
        }

        [HttpGet]
        [Route("models")]
        [AllowAnonymous] // Permitir acceso sin autenticación para este endpoint
        public async Task<IActionResult> GetAvailableModels()
        {
            try
            {
                var ollamaUrl = _configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
                var response = await _httpClient.GetAsync($"{ollamaUrl}/tags");
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var modelsData = JsonSerializer.Deserialize<object>(content);
                    
                    return Ok(new { 
                        success = true, 
                        message = "Modelos obtenidos exitosamente",
                        models = modelsData
                    });
                }
                else
                {
                    return StatusCode((int)response.StatusCode, new { 
                        success = false, 
                        message = "Error obteniendo modelos de Ollama",
                        error = await response.Content.ReadAsStringAsync()
                    });
                }
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
        [Route("generate")]
        public async Task<IActionResult> GenerateResponse([FromBody] ClsModOllamaChatRequest request)
        {
            try
            {
                // Verificar que el usuario autenticado solo pueda enviar sus propios mensajes
                var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(currentUserId) || currentUserId != request.UserId)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, 
                        new { message = "No tienes acceso para enviar mensajes como otro usuario" });
                }

                var ollamaUrl = _configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
                var defaultModel = _configuration["Ollama:DefaultModel"] ?? "llama3.2";

                // Preparar payload para Ollama
                var ollamaPayload = new
                {
                    model = request.Model ?? defaultModel,
                    prompt = request.Prompt,
                    stream = true // Para obtener respuesta completa de una vez
                };

                var jsonContent = JsonSerializer.Serialize(ollamaPayload);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Enviar request a Ollama
                var response = await _httpClient.PostAsync($"{ollamaUrl}/generate", httpContent);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var ollamaResponse = JsonSerializer.Deserialize<ClsModOllamaGenerateResponse>(responseContent);
                    
                    // Aquí guardar el chat una vez este implementado, tanto el mensaje del usuario como la respuesta
                    // await SaveChatToDatabase(request.UserId, request.Prompt, ollamaResponse.response);

                    return Ok(new { 
                        success = true,
                        userPrompt = request.Prompt,
                        aiResponse = ollamaResponse?.response ?? "Sin respuesta del modelo",
                        model = request.Model ?? defaultModel,
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    return StatusCode((int)response.StatusCode, new { 
                        success = false, 
                        message = "Error en respuesta de Ollama",
                        error = responseContent 
                    });
                }
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
                    message = "Error interno procesando chat con Ollama", 
                    error = ex.Message 
                });
            }
        }
    }

}
