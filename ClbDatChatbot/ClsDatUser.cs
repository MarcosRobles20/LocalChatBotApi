using ClbDatChatbot.common;
using ClbModChatbot;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Data;
using Microsoft.Extensions.Configuration;

namespace ClbDatChatbot
{
    public class ClsDatUser : DatabaseConnection
    {
        public ClsDatUser(IConfiguration configuration) : base(configuration)
        {
        }

        // Métodos para autenticación
        public ClsModUser? GetUserByEmail(string email)
        {
            try
            {
                using var connection = new SqlConnection(GetConnectionString());
                var parameters = new DynamicParameters();
                parameters.Add("@Email", email, DbType.String);

                return connection.QueryFirstOrDefault<ClsModUser>(
                    "SpdGetUserByEmail", 
                    parameters, 
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                throw new Exception("Error retrieving user by email", ex);
            }
        }

        public ClsModUser? GetUserById(string idUser)
        {
            try
            {
                using var connection = new SqlConnection(GetConnectionString());
                var parameters = new DynamicParameters();
                parameters.Add("@IdUser", idUser, DbType.String);

                return connection.QueryFirstOrDefault<ClsModUser>(
                    "SpdGetUserById", 
                    parameters, 
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                throw new Exception("Error retrieving user by ID", ex);
            }
        }

        public bool CreateUser(ClsModUser user)
        {
            try
            {
                using var connection = new SqlConnection(GetConnectionString());
                var parameters = new DynamicParameters();
                parameters.Add("@IdUser", user.IdUser, DbType.String);
                parameters.Add("@Name", user.Name, DbType.String);
                parameters.Add("@Email", user.Email, DbType.String);
                parameters.Add("@Password", user.Password, DbType.String);
                parameters.Add("@Role", user.Role ?? "User", DbType.String);
                parameters.Add("@RegisterDate", user.RegisterDate, DbType.DateTime);

                var result = connection.QueryFirstOrDefault<dynamic>(
                    "SpdCreateUser", 
                    parameters, 
                    commandType: CommandType.StoredProcedure);

                return result?.Result == 1;
            }
            catch (Exception ex)
            {
                throw new Exception("Error creating user", ex);
            }
        }

        public bool EmailExists(string email)
        {
            try
            {
                using var connection = new SqlConnection(GetConnectionString());
                var parameters = new DynamicParameters();
                parameters.Add("@Email", email, DbType.String);

                var result = connection.QueryFirstOrDefault<dynamic>(
                    "SpdEmailExists", 
                    parameters, 
                    commandType: CommandType.StoredProcedure);

                return result?.EmailCount > 0;
            }
            catch (Exception ex)
            {
                throw new Exception("Error checking if email exists", ex);
            }
        }
    }
}
