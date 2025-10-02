using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ClbModChatbot;
using ClbNegChatbot;
using System.Security.Claims;

namespace ChatBotApiV2.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ClsNegAuth _negAuth;

        public AuthController(IConfiguration configuration, IPasswordService passwordService, IJwtService jwtService)
        {
            _negAuth = new ClsNegAuth(configuration, passwordService, jwtService);
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] ClsModLoginRequest request)
        {           
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var result = _negAuth.Login(request);
                
                if (result.Success)
                {
                    return Ok(result);
                }
                
                return Unauthorized(new { message = result.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new { message = "Error interno del servidor", error = ex.Message });
            }
        }

        [HttpPost("register")]
        public IActionResult Register([FromBody] ClsModRegisterRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var result = _negAuth.Register(request);
                
                if (result.Success)
                {
                    return Ok(result);
                }
                
                return BadRequest(new { message = result.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new { message = "Error interno del servidor", error = ex.Message });
            }
        }

        [HttpGet("profile")]
        [Authorize]
        public IActionResult GetProfile()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { message = "Token inválido" });
                }

                var user = _negAuth.GetUserProfile(userId);
                
                if (user == null)
                {
                    return NotFound(new { message = "Usuario no encontrado" });
                }

                return Ok(new { user });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new { message = "Error interno del servidor", error = ex.Message });
            }
        }

        [HttpPost("validateToken")]
        [Authorize]
        public IActionResult ValidateToken()
        {
            // Si llegamos aquí, el token es válido (gracias al [Authorize])
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userName = User.FindFirst(ClaimTypes.Name)?.Value;
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            return Ok(new 
            { 
                valid = true, 
                user = new 
                { 
                    id = userId, 
                    name = userName, 
                    email = userEmail, 
                    role = userRole 
                } 
            });
        }
    }
}