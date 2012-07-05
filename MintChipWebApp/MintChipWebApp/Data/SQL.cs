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

                        sqlConnection.Open();

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

                        sqlConnection.Open();

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

        #region Confirm Account

        public ConfirmAccountResult ConfirmAccount(string emailAddress, string confirmationCode)
        {
            if (string.IsNullOrEmpty(emailAddress))
                return ConfirmAccountResult.NoSuchEmail;

            if (string.IsNullOrEmpty(confirmationCode))
                return ConfirmAccountResult.InvalidCode;

            try
            {
                // find the row (if any) for this email address
                using (SqlConnection sqlConnection = GetConnection())
                {
                    using (SqlCommand sqlCommand = new SqlCommand("SELECT * FROM Users WHERE [Email] = @email", sqlConnection))
                    {
                        AddVarCharParameter("email", emailAddress, sqlCommand);

                        DataSet ds = new DataSet();
                        SqlDataAdapter sqlDataAdapter = new SqlDataAdapter(sqlCommand);

                        sqlDataAdapter.Fill(ds);

                        if (ds.Tables[0].Rows.Count == 0)
                            return ConfirmAccountResult.NoSuchEmail;

                        bool foundCode = false;

                        foreach (DataRow row in ds.Tables[0].Rows)
                        {
                            // don't worry about what the user typed in, make it a case insensitive search
                            if (row["ConfirmationCode"].ToString().Equals(confirmationCode, StringComparison.CurrentCultureIgnoreCase))
                            {
                                foundCode = true;

                                if ((bool)row["Confirmed"])
                                    return ConfirmAccountResult.AlreadyConfirmed;

                                break;
                            }
                        }

                        if (!foundCode)
                            return ConfirmAccountResult.InvalidCode;
                    }

                    using (SqlCommand sqlCommand = new SqlCommand("UPDATE Users SET Confirmed = 1 WHERE [Email] = @email AND ConfirmationCode = @code", sqlConnection))
                    {
                        AddVarCharParameter("email", emailAddress, sqlCommand);
                        AddVarCharParameter("code", confirmationCode, sqlCommand);

                        if (sqlConnection.State == ConnectionState.Closed)
                            sqlConnection.Open();

                        int numRowsAffected = sqlCommand.ExecuteNonQuery();

                        if (numRowsAffected != 1)
                            return ConfirmAccountResult.UnknownError;
                    }
                }
            }
            catch (Exception ex)
            {
                SQLLogger.LogException(ex);
                return ConfirmAccountResult.UnknownError;
            }

            return ConfirmAccountResult.Success;
        }

#if DEBUG

        /*  IF NOT EXISTS (SELECT 1 FROM Users WHERE [Email] = 'test@test.com')
	            INSERT INTO Users ([Name], Nickname, [Email], ConfirmationCode, Confirmed) VALUES ('Test', 'Testing', 'test@test.com', 'ABCDEF', 0)
            ELSE
	            UPDATE Users SET Confirmed = 0 WHERE [Email] = 'test@test.com'
         */
        /// <summary>Run tests for each scenario</summary>
        internal static void TestConfirmAccount()
        {
            string correctEmail = "test@test.com";
            string correctCode = "ABCDEF";

            SQL sql = new SQL();

            sql.TestConfirmAccount("nosuchemail", "A", ConfirmAccountResult.NoSuchEmail);
            sql.TestConfirmAccount(correctEmail, "A", ConfirmAccountResult.InvalidCode);
            sql.TestConfirmAccount(correctEmail, correctCode, ConfirmAccountResult.Success);
            sql.TestConfirmAccount(correctEmail, correctCode, ConfirmAccountResult.AlreadyConfirmed);
        }

        private void TestConfirmAccount(string email, string confirmationCode, ConfirmAccountResult expectedResult)
        {
            ConfirmAccountResult result = ConfirmAccount(email, confirmationCode);

            if (result != expectedResult)
                throw new Exception(string.Format("Expected '{0}' but '{1}' was returned.", expectedResult.ToString(), result.ToString()));
        }
#endif

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

    #region Enums

    public enum ConfirmAccountResult
    {
        Success = 0,
        NoSuchEmail = 1,
        AlreadyConfirmed = 2,
        InvalidCode = 3,
        UnknownError = 4,
    }

    #endregion
}