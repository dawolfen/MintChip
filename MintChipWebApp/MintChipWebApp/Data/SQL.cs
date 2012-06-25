using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.Linq;
using System.Web;

// just keep everything in one file so there are fewer checkins to make
namespace MintChipWebApp.Data
{
    public class SQL
    {
        internal static SqlConnection GetConnection()
        {
            AppSettingsReader appSettingsReader = new AppSettingsReader();
            string connectionString = (string)appSettingsReader.GetValue("sqlConnection", typeof(string));

            return new SqlConnection(connectionString);
        }

        #region IsDuplicateUser

        /// <summary>Returns true if a user exists with the given email address</summary>
        public bool IsDuplicateUser(string emailAddress)
        {
            try
            {
                using (SqlConnection sqlConnection = GetConnection())
                {
                    using (SqlCommand sqlCommand = new SqlCommand("SELECT COUNT(*) FROM Users WHERE Email = @emailAddress", sqlConnection))
                    {
                        AddVarCharParameter("emailAddress", emailAddress, sqlCommand);

                        int count = (int)sqlCommand.ExecuteScalar();
                    }
                }
            }
            catch (Exception ex)
            {
                SQLLogger.LogException(ex);
            }

            return false;
        }

        #endregion

        #region Create Account

        public void CreateAccount(string userName, string nickname, string emailAddress, string confirmationCode)
        {
            try
            {
                using (SqlConnection sqlConnection = GetConnection())
                {
                    using (SqlCommand sqlCommand = new SqlCommand("INSERT INTO Users ([Name], Nickname, [Email], ConfirmationCode, Confirmed) VALUES (@name, @nickname, @email, @confirmationCode, 0)", sqlConnection))
                    {
                        AddVarCharParameter("name", userName, sqlCommand);
                        AddVarCharParameter("nickname", nickname, sqlCommand);
                        AddVarCharParameter("email", emailAddress, sqlCommand);
                        AddVarCharParameter("confirmationCode", confirmationCode, sqlCommand);

                        sqlCommand.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                SQLLogger.LogException(ex);
            }
        }

        #endregion

        internal static void AddVarCharParameter(string name, string value, SqlCommand sqlCommand)
        {
            int length = 1;

            if (!string.IsNullOrEmpty(value))
                length = value.Length;

            SqlParameter sqlParameter = new SqlParameter(name, SqlDbType.VarChar, length);
            sqlParameter.Value = value;

            sqlCommand.Parameters.Add(sqlParameter);
        }
    }

    public static class SQLLogger
    {
        #region Logging

        // since we can't debug the webservice, log to a sql table
        public static void LogInfo(string info)
        {
            if (string.IsNullOrEmpty(info))
                return;

            try
            {
                using (SqlConnection sqlConnection = SQL.GetConnection())
                {
                    using (SqlCommand sqlCommand = new SqlCommand("INSERT INTO [Log] ([Info]) VALUES (@info)", sqlConnection))
                    {
                        sqlCommand.CommandType = CommandType.Text;

                        SQL.AddVarCharParameter("info", info, sqlCommand);

                        sqlConnection.Open();

                        sqlCommand.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        public static void LogException(Exception ex)
        {
            LogInfo(ex.ToString());
        }

        #endregion
    }
}