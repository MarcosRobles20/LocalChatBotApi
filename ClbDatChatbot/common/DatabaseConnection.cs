using Microsoft.Extensions.Configuration;

namespace ClbDatChatbot.common
{
    public class DatabaseConnection
    {
        private readonly IConfiguration _configuration;

        public DatabaseConnection(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GetConnectionString()
        {
            var connectionString = _configuration.GetConnectionString("Conexion");
            return connectionString ?? string.Empty;
        }
    }
}
