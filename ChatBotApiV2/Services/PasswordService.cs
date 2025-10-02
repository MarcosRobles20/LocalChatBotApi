using System.Security.Cryptography;
using System.Text;
using ClbNegChatbot;
using Microsoft.Extensions.Configuration;

namespace ChatBotApiV2.Services
{
    public class PasswordService : IPasswordService
    {
        private readonly IConfiguration _configuration;
        private readonly string _salt;

        public PasswordService(IConfiguration configuration)
        {
            _configuration = configuration;
            _salt = _configuration["Security:PasswordSalt"] ?? "DefaultSaltForDevelopment2024!";
        }

        public string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + _salt));
            return Convert.ToBase64String(hashedBytes);
        }

        public bool VerifyPassword(string password, string hashedPassword)
        {
            var hashedInput = HashPassword(password);
            return hashedInput == hashedPassword;
        }
    }
}