using ClbDatChatbot.common;
using ClbModChatbot;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Data;

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

        public ClsModChat CreateNewChat(ClsModOllamaChatRequest request)
        {
            ClsModChat newChat = null;
            try
            {
                using var connection = new SqlConnection(GetConnectionString());
                {
                    
                    var parameters = new DynamicParameters();
                    parameters.Add("@IdChat", request.IdChat, DbType.String);
                    parameters.Add("@IdUser", request.IdUser, DbType.String);

                    newChat = connection.QueryFirstOrDefault<ClsModChat>("SpdAddChatWithIdUser", parameters, commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error creating new chat", ex);
            }
            return newChat;
        }

        /// <summary>
        /// Guarda un mensaje del usuario y la respuesta de la IA en la base de datos
        /// </summary>
        public void SaveChatMessage(string idChat, string idUser, string userMessage, string aiResponse, string model)
        {
            try
            {
                using var connection = new SqlConnection(GetConnectionString());
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@IdChat", idChat, DbType.String);
                    parameters.Add("@IdUser", idUser, DbType.String);
                    parameters.Add("@UserMessage", userMessage, DbType.String);
                    parameters.Add("@AiResponse", aiResponse, DbType.String);
                    parameters.Add("@Model", model, DbType.String);

                    connection.Execute("SpdSaveChatMessage", parameters, commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error saving chat message", ex);
            }
        }

        /// <summary>
        /// Recupera el contexto/historial de un chat para mantener conversación
        /// </summary>
        public List<ClsModChatMessage> GetChatHistory(string idChat, string idUser, int? lastMessages = null)
        {
            try
            {
                using var connection = new SqlConnection(GetConnectionString());
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@IdChat", idChat, DbType.String);
                    parameters.Add("@IdUser", idUser, DbType.String);
                    parameters.Add("@LastMessages", lastMessages, DbType.Int32);

                    return connection.Query<ClsModChatMessage>("SpdGetChatHistory", 
                                                              parameters, 
                                                              commandType: CommandType.StoredProcedure)
                                   .ToList();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error retrieving chat history", ex);
            }
        }

        /// <summary>
        /// Actualiza el título del chat basado en la primera interacción
        /// </summary>
        public void UpdateChatTitle(string idChat, string title)
        {
            try
            {
                using var connection = new SqlConnection(GetConnectionString());
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@IdChat", idChat, DbType.String);
                    parameters.Add("@Title", title, DbType.String);

                    connection.Execute("SpdUpdateChatTitle", parameters, commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error updating chat title", ex);
            }
        }

        /// <summary>
        /// Registra métricas de uso de la IA (tiempo de respuesta, tokens, etc.)
        /// </summary>
        public void LogUsageMetrics(string idUser, string model, int promptTokens, int responseTokens, long responseDurationMs, string? idChat = null)
        {
            try
            {
                using var connection = new SqlConnection(GetConnectionString());
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@IdUser", idUser, DbType.String);
                    parameters.Add("@Model", model, DbType.String);
                    parameters.Add("@PromptTokens", promptTokens, DbType.Int32);
                    parameters.Add("@ResponseTokens", responseTokens, DbType.Int32);
                    parameters.Add("@ResponseDurationMs", responseDurationMs, DbType.Int64);
                    parameters.Add("@IdChat", idChat, DbType.String); // Agregué IdChat

                    connection.Execute("SpdLogUsageMetrics", parameters, commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error logging usage metrics", ex);
            }
        }
    }
}
