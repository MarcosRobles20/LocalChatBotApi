using ClbDatChatbot;
using ClbModChatbot;
using Microsoft.Extensions.Configuration;

namespace ClbNegChatbot
{
    public class ClsNegAuth
    {
        private readonly ClsDatUser _datUser;
        private readonly IPasswordService _passwordService;
        private readonly IJwtService _jwtService;

        public ClsNegAuth(IConfiguration configuration, IPasswordService passwordService, IJwtService jwtService)
        {
            _datUser = new ClsDatUser(configuration);
            _passwordService = passwordService;
            _jwtService = jwtService;
        }

        public ClsModLoginResponse Login(ClsModLoginRequest request)
        {
            try
            {
                var user = _datUser.GetUserByEmail(request.Email);
                if (user == null)
                {
                    return new ClsModLoginResponse
                    {
                        Success = false,
                        Message = "Usuario no encontrado"
                    };
                }

                if (!_passwordService.VerifyPassword(request.Password, user.Password ?? string.Empty))
                {
                    return new ClsModLoginResponse
                    {
                        Success = false,
                        Message = "Credenciales inválidas"
                    };
                }

                var token = _jwtService.GenerateToken(user);
                
                // No devolver la contraseña en la respuesta
                user.Password = null;

                return new ClsModLoginResponse
                {
                    Success = true,
                    Message = "Login exitoso",
                    Token = token,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(60),
                    User = user
                };
            }
            catch (Exception ex)
            {
                return new ClsModLoginResponse
                {
                    Success = false,
                    Message = "Error interno del servidor: " + ex.Message
                };
            }
        }

        public ClsModLoginResponse Register(ClsModRegisterRequest request)
        {
            try
            {
                // Validar si el email ya existe
                if (_datUser.EmailExists(request.Email))
                {
                    return new ClsModLoginResponse
                    {
                        Success = false,
                        Message = "El email ya está registrado"
                    };
                }

                // Crear nuevo usuario
                var user = new ClsModUser
                {
                    IdUser = Guid.NewGuid().ToString(),
                    Name = request.Name,
                    Email = request.Email,
                    Password = _passwordService.HashPassword(request.Password),
                    Role = "User",
                    RegisterDate = DateTime.Now
                };

                bool created = _datUser.CreateUser(user);
                if (!created)
                {
                    return new ClsModLoginResponse
                    {
                        Success = false,
                        Message = "Error al crear el usuario"
                    };
                }

                // Generar token para el nuevo usuario
                var token = _jwtService.GenerateToken(user);
                
                // No devolver la contraseña en la respuesta
                user.Password = null;

                return new ClsModLoginResponse
                {
                    Success = true,
                    Message = "Usuario registrado exitosamente",
                    Token = token,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(60),
                    User = user
                };
            }
            catch (Exception ex)
            {
                return new ClsModLoginResponse
                {
                    Success = false,
                    Message = "Error interno del servidor: " + ex.Message
                };
            }
        }

        public ClsModUser? GetUserProfile(string userId)
        {
            try
            {
                var user = _datUser.GetUserById(userId);
                if (user != null)
                {
                    user.Password = null; // No devolver la contraseña
                }
                return user;
            }
            catch (Exception ex)
            {
                throw new Exception("Error obteniendo perfil de usuario: " + ex.Message, ex);
            }
        }
    }
}