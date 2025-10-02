using ClbDatChatbot.common;
using ClbModChatbot;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Data;
using Microsoft.Extensions.Configuration;

namespace ClbDatChatbot
{
    public class ClsDatChat : DatabaseConnection
    {
        public ClsDatChat(IConfiguration configuration) : base(configuration)
        {
        }

        public List<ClsModChat> GetChatsWithIdUser(ClsModChatRequest request)
        {
            List<ClsModChat> chats = null;
            try
            {
                using var connection = new SqlConnection(GetConnectionString());
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@IdUser", request.IdUser, DbType.String);
                    chats = connection.Query<ClsModChat>("SpdGetChatsWithIdUser",
                                                         parameters,
                                                         commandType: CommandType.StoredProcedure)
                                      .ToList();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error retrieving chats by IdUser", ex);
            }
            return chats;
        }

        public ClsModChat GetChatWithIdChat(ClsModChatRequest request)
        {
            ClsModChat chat = null;
            try
            {
                using var connection = new SqlConnection(GetConnectionString());
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@IdChat", request.IdChat, DbType.String);
                    parameters.Add("@IdUser", request.IdUser, DbType.String);

                    chat = connection.QueryFirstOrDefault<ClsModChat>("SpdGetChatWithIdChat", parameters, commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error retrieving chat by IdChat", ex);
            }
            return chat;
        }
    }
}
