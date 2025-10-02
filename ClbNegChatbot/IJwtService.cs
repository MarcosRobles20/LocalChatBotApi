using ClbModChatbot;
using System.Security.Claims;

namespace ClbNegChatbot
{
    public interface IJwtService
    {
        string GenerateToken(ClsModUser user);
        ClaimsPrincipal? ValidateToken(string token);
        bool IsTokenValid(string token);
    }
}