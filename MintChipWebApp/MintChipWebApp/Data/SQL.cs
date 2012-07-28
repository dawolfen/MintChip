﻿using System;
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

        #region AddFriend

        /// <summary>friendNickname and friendMintChipId are set only in the case where AddFriend is successful</summary>
        public AddFriendResult AddFriend(string emailAddress, string friendEmailAddress, out string friendNickname, out string friendMintChipId)
        {
            friendNickname = string.Empty;
            friendMintChipId = string.Empty;

            if (string.IsNullOrEmpty(emailAddress))
                return AddFriendResult.NoSuchEmail;

            if (string.IsNullOrEmpty(friendEmailAddress))
                return AddFriendResult.NoSuchFriendEmail;

            try
            {
                // find the row (if any) for this email address
                using (SqlConnection sqlConnection = GetConnection())
                {
                    // find the row for the email address
                    DataRow emailRow = GetUser(sqlConnection, emailAddress);

                    if (emailRow == null)
                        return AddFriendResult.NoSuchEmail;

                    int emailId = (int)emailRow["Id"];

                    // find the row for the Friend email address
                    DataRow friendEmailRow = GetUser(sqlConnection, friendEmailAddress);

                    if (friendEmailRow == null)
                        return AddFriendResult.NoSuchFriendEmail;

                    int friendEmailId = (int)friendEmailRow["Id"];

                    // check it hasn't been added already
                    using (SqlCommand sqlCommand = new SqlCommand("SELECT COUNT(*) FROM Friends WHERE Friend = @friend AND FriendWith = @friendWith", sqlConnection))
                    {
                        AddIntParameter("friend", emailId, sqlCommand);
                        AddIntParameter("friendWith", friendEmailId, sqlCommand);

                        if (sqlConnection.State == ConnectionState.Closed)
                            sqlConnection.Open();

                        int numRows = (int)sqlCommand.ExecuteScalar();

                        if (numRows > 0)
                            return AddFriendResult.AlreadyAdded;
                    }

                    // insert row
                    using (SqlCommand sqlCommand = new SqlCommand("INSERT INTO Friends (Friend, FriendWith, Confirmed) VALUES (@friend, @friendWith, 0)", sqlConnection))
                    {
                        AddIntParameter("friend", emailId, sqlCommand);
                        AddIntParameter("friendWith", friendEmailId, sqlCommand);

                        if (sqlConnection.State == ConnectionState.Closed)
                            sqlConnection.Open();

                        int numRowsAffected = sqlCommand.ExecuteNonQuery();

                        if (numRowsAffected != 1)
                            return AddFriendResult.UnknownError;

                        friendNickname = (string)friendEmailRow["Nickname"];
                        friendMintChipId = (string)friendEmailRow["MintChipId"];
                    }
                }
            }
            catch (Exception ex)
            {
                SQLLogger.LogException(ex);
                return AddFriendResult.UnknownError;
            }

            return AddFriendResult.Success;
        }

        #endregion

        #region GetPendingFriendRequests

        public DataSet GetPendingFriendRequests(string emailAddress)
        {
            DataSet ds = new DataSet();

            if (string.IsNullOrEmpty(emailAddress))
                return ds;

            try
            {
                // find the row (if any) for this email address
                using (SqlConnection sqlConnection = GetConnection())
                {
                    string sql = @"DECLARE @id INT = NULL
                                    SELECT @id = Id FROM Users WHERE Email = @email

                                    IF NOT @id IS NULL
                                    BEGIN
	                                    SELECT Users.Id, Users.Email FROM Users, Friends WHERE Users.Id = Friends.Friend AND Friends.Confirmed = 0 AND Friends.FriendWith = @id
                                    END
                                    ELSE
                                    BEGIN
	                                    SELECT * FROM Users WHERE Id = -1
                                    END";

                    using (SqlCommand sqlCommand = new SqlCommand(sql, sqlConnection))
                    {
                        AddVarCharParameter("email", emailAddress, sqlCommand);

                        using (SqlDataAdapter sqlDataAdapter = new SqlDataAdapter(sqlCommand))
                        {
                            sqlDataAdapter.Fill(ds);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SQLLogger.LogException(ex);
            }

            return ds;
        }

        #endregion

        #region ConfirmFriend

        public string ConfirmFriend(string emailAddress, string friendEmailAddress)
        {
            if (string.IsNullOrEmpty(emailAddress))
                return string.Empty;

            if (string.IsNullOrEmpty(friendEmailAddress))
                return string.Empty;

            int updateId = -1;
            string mintChipId = null;

            try
            {
                // double check the relationship exists and that it is unconfirmed
                using (SqlConnection sqlConnection = GetConnection())
                {
                    string sql = @"DECLARE @id INT = NULL, @friendId INT = NULL
                                    SELECT @id = Id FROM Users WHERE Email = @email
                                    SELECT @friendId = Id FROM Users WHERE Email = @friendEmail

                                    IF NOT @id IS NULL AND NOT @friendId IS NULL
                                    BEGIN
	                                    SELECT Users.Id, Users.Email, Users.MintChipId, Friends.Id AS UpdateId FROM Users, Friends WHERE Users.Id = Friends.Friend AND Friends.Confirmed = 0 AND Friends.FriendWith = @id AND Friends.Friend = @friendId
                                    END
                                    ELSE
                                    BEGIN
	                                    SELECT * FROM Users WHERE Id = -1
                                    END";

                    using (SqlCommand sqlCommand = new SqlCommand(sql, sqlConnection))
                    {
                        AddVarCharParameter("email", emailAddress, sqlCommand);
                        AddVarCharParameter("friendEmail", friendEmailAddress, sqlCommand);

                        using (SqlDataAdapter sqlDataAdapter = new SqlDataAdapter(sqlCommand))
                        {
                            DataSet ds = new DataSet();
                            sqlDataAdapter.Fill(ds);

                            if (ds.Tables.Count > 0 && ds.Tables[0].Rows.Count == 1)
                            {
                                updateId = (int)ds.Tables[0].Rows[0]["UpdateId"];
                                mintChipId = (string)ds.Tables[0].Rows[0]["MintChipId"];

                                // update the row to Confirmed
                                using (SqlCommand updateSqlCommand = new SqlCommand(string.Format("UPDATE Friends SET Confirmed = 1 WHERE Id = {0}", updateId), sqlConnection))
                                {
                                    if (sqlConnection.State == ConnectionState.Closed)
                                        sqlConnection.Open();

                                    int numRowsAffected = updateSqlCommand.ExecuteNonQuery();

                                    if (numRowsAffected != 1)
                                        mintChipId = null;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SQLLogger.LogException(ex);
            }

            if (string.IsNullOrEmpty(mintChipId))
                return string.Empty;

            return mintChipId;
        }

        #endregion

        #region CreateBill

        public string CreateBill(string emailAddress, string friendEmailAddresses, double totalBill, int tipType, double tip, double paymentTotal, double portion)
        {
            if (string.IsNullOrEmpty(emailAddress))
                return string.Empty;

            if (string.IsNullOrEmpty(friendEmailAddresses))
                return string.Empty;

            string[] friends = friendEmailAddresses.Split('|');

            try
            {
                using (SqlConnection sqlConnection = GetConnection())
                {
                    // get the id of the creator
                    int? owner = GetUserId(sqlConnection, emailAddress);

                    if (!owner.HasValue)
                        return "";

                    // find the friends, check they are confirmed (in interest of time though, do this later, at the deadline...)
                    List<int> friendIdList = new List<int>();

                    foreach (string friendEmail in friends)
                    {
                        int? friend = GetUserId(sqlConnection, friendEmail);

                        if (friend.HasValue)
                            friendIdList.Add(friend.Value);
                    }

                    if (friendIdList.Count == 0)
                        return "";

                    // have enough participants, create bill

                    #region Create Bill

                    string billSql = "INSERT INTO Bill ([Name], OwnerId, Total, TipType, Tip, PaymentTotal) VALUES ('', @ownerId, @total, @tipType, @paymentTotal); SELECT SCOPE_IDENTITY();";
                    int billId;

                    using (SqlCommand sqlCommand = new SqlCommand(billSql, sqlConnection))
                    {
                        #region Parameters

                        AddIntParameter("ownerId", owner.Value, sqlCommand);
                        AddDecimalParameter("total", totalBill, sqlCommand);
                        AddIntParameter("tipType", tipType, sqlCommand);
                        AddDecimalParameter("paymentTotal", paymentTotal, sqlCommand);

                        #endregion

                        if (sqlConnection.State == ConnectionState.Closed)
                            sqlConnection.Open();

                        billId = (int)sqlCommand.ExecuteScalar();
                    }

                    if (billId < 1)
                        return "";

                    #endregion

                    #region Create each of the Bill Participants

                    string billParticipantSql = "INSERT INTO BillParticipant (BillId, OwnerId, DisplayName, Payment, TransactionId) VALUES (@billId, @ownerId, '', @payment, '')";

                    foreach (int friendId in friendIdList)
                    {
                        using (SqlCommand sqlCommand = new SqlCommand(billSql, sqlConnection))
                        {
                            #region Parameters

                            AddIntParameter("billId", billId, sqlCommand);
                            AddIntParameter("ownerId", friendId, sqlCommand);
                            AddDecimalParameter("payment", portion, sqlCommand);

                            #endregion

                            if (sqlConnection.State == ConnectionState.Closed)
                                sqlConnection.Open();

                            sqlCommand.ExecuteNonQuery();
                        }
                    }

                    #endregion
                }
            }
            catch (Exception ex)
            {
                SQLLogger.LogException(ex);
            }

            return "";
        }

        #endregion

        #region AddParameter functions

        internal static void AddVarCharParameter(string name, string value, SqlCommand sqlCommand)
        {
            int length = 1;

            if (!string.IsNullOrEmpty(value))
                length = value.Length;

            SqlParameter sqlParameter = new SqlParameter(name, SqlDbType.VarChar, length);
            sqlParameter.Value = value;

            sqlCommand.Parameters.Add(sqlParameter);
        }

        internal static void AddIntParameter(string name, int value, SqlCommand sqlCommand)
        {
            SqlParameter sqlParameter = new SqlParameter(name, SqlDbType.Int);
            sqlParameter.Value = value;

            sqlCommand.Parameters.Add(sqlParameter);
        }

        internal static void AddDecimalParameter(string name, double value, SqlCommand sqlCommand)
        {
            SqlParameter sqlParameter = new SqlParameter(name, SqlDbType.Decimal);
            sqlParameter.Value = value;
            sqlParameter.Precision = 18;
            sqlParameter.Scale = 2;

        }

        #endregion

        #region Helper functions

        private DataRow GetUser(SqlConnection sqlConnection, string emailAddress)
        {
            using (SqlCommand sqlCommand = new SqlCommand("SELECT * FROM Users WHERE [Email] = @email", sqlConnection))
            {
                AddVarCharParameter("email", emailAddress, sqlCommand);

                DataSet ds = new DataSet();
                SqlDataAdapter sqlDataAdapter = new SqlDataAdapter(sqlCommand);

                sqlDataAdapter.Fill(ds);

                if (ds.Tables[0].Rows.Count == 0)
                    return null;

                return ds.Tables[0].Rows[0];
            }
        }

        private int? GetUserId(SqlConnection sqlConnection, string emailAddress)
        {
            DataRow row = GetUser(sqlConnection, emailAddress);

            if (row == null)
                return null;

            return (int)row["Id"];
        }

        #endregion
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

    public enum AddFriendResult
    {
        Success = 0,
        NoSuchEmail = 1,
        NoSuchFriendEmail = 2,
        AlreadyAdded = 3,
        UnknownError = 4,
    }

    #endregion
}