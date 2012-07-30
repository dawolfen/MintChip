using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using System.Web;
using System.Web.Services;

using MintChipWebApp.Data;

namespace MintChipWebApp
{
    /// <summary>
    /// Summary description for DataService
    /// </summary>
    [WebService(Namespace = "http://tempuri.org/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    // To allow this Web Service to be called from script, using ASP.NET AJAX, uncomment the following line. 
    [System.Web.Script.Services.ScriptService]
    public class DataService : System.Web.Services.WebService
    {
        public static readonly string APP_NAME = "Bill Splittr";

        // just here for debugging to see if there is only ever one instance created
        public static Guid Id = Guid.NewGuid();

        [WebMethod]
        public string HelloWorld()
        {
            System.Diagnostics.Debug.WriteLine(Id.ToString());
            return "Hello World";
        }

        #region CreateAccount

        [WebMethod]
        public string CreateAccount(string userName, string nickname, string emailAddress)
        {
            SQLLogger.LogInfo(string.Format("Creating account for user '{0}', nickname '{1}' and emailAddress '{2}'.", userName, nickname, emailAddress));

            if (string.IsNullOrEmpty(userName))
                return "<xml><IsValid>0</IsValid><FailureReason>InvalidUserName</FailureReason></xml>";
            else if (string.IsNullOrEmpty(userName))
                return "<xml><IsValid>0</IsValid><FailureReason>InvalidEmailAddress</FailureReason></xml>";

            // check if the user exists already
            SQL sql = new SQL();

            if (sql.IsDuplicateUser(emailAddress))
                return "<xml><IsValid>0</IsValid><FailureReason>DuplicateEmailAddress</FailureReason></xml>";

            string code = CreateRandomConfirmationCode();
            sql.CreateAccount(userName, nickname, emailAddress, code);

            #region Create Email

            bool createHTML = false;
            bool emailCreation = false;

            string body;

            if (createHTML)
            {   // tried html and it didn't work, probably just needs more investigation
                #region HTML

                body = @"<html><body>Thanks for something

Here is your code:   223489

<a href='www.yahoo.com?test=abc>Confirm email</a>

Enjoy the app.</body></html>";

                #endregion
            }
            else
            {
                body = string.Format(@"Thanks for creating an account with {0}.

To confirm your email address, please enter this code in the application on your device: {1}

Enjoy the app.", APP_NAME, code);

                // Create the web request
                HttpWebRequest request = WebRequest.Create(string.Format("http://sendgrid.com/api/mail.send.json?to={0}&toname={1}&from=donotreply%40apphb.com&fromname={2}&subject={3}&text={4}&api_user=ca015344-7e31-4a27-abf0-0762e0fa3436@apphb.com&api_key=yg5wbbu6",
                    HttpUtility.UrlEncode(emailAddress), HttpUtility.UrlEncode(userName), HttpUtility.UrlEncode(APP_NAME), HttpUtility.UrlEncode(string.Format("Confirmation code for {0}", APP_NAME)), body)) as HttpWebRequest;

                // Get response  
                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                {
                    // Get the response stream  
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        string responseText = reader.ReadToEnd();

                        if (responseText == "{\"message\":\"success\"}")
                            emailCreation = true;
                    }
                }

                // this looks more correct, however the linebreaks don't work
                //System.Net.HttpWebRequest request = System.Net.WebRequest.Create(string.Format("http://sendgrid.com/api/mail.send.json?to=wolfendaled%40yahoo.com&toname=Dave&from=donotreplywolfendaled%40yahoo.com&fromname=David&subject=Testing123&html={0}&api_user=ca015344-7e31-4a27-abf0-0762e0fa3436@apphb.com&api_key=yg5wbbu6", System.Web.HttpUtility.UrlEncode(body))) as System.Net.HttpWebRequest;
            }

            #endregion

            return string.Format("<xml><IsValid>1</IsValid><EmailCreated>{0}</EmailCreated></xml>", emailCreation ? "1" : "0");
        }

        #endregion

        #region ConfirmAccount

        [WebMethod]
        public string ConfirmAccount(string emailAddress, string confirmationCode)
        {
            SQLLogger.LogInfo(string.Format("Confirming account for emailAddress '{0}' with code '{1}'.", emailAddress, confirmationCode));

            if (string.IsNullOrEmpty(emailAddress))
                return "<xml><IsValid>0</IsValid><FailureReason>InvalidEmailAddress</FailureReason></xml>";

            // perform the operation and get the return code
            SQL sql = new SQL();

            ConfirmAccountResult result = sql.ConfirmAccount(emailAddress, confirmationCode);

            if (result == ConfirmAccountResult.Success)
                return "<xml><IsValid>1</IsValid></xml>";
            else
                return string.Format("<xml><IsValid>0</IsValid><FailureReason>{0}</FailureReason></xml>", result.ToString());
        }

        #endregion

        #region AddFriend

        [WebMethod]
        public string AddFriend(string emailAddress, string friendEmailAddress)
        {
            SQLLogger.LogInfo(string.Format("Adding friend '{0}' to '{1}'.", friendEmailAddress, emailAddress));

            if (string.IsNullOrEmpty(emailAddress))
                return "<xml><IsValid>0</IsValid><FailureReason>InvalidEmailAddress</FailureReason></xml>";
            else if (string.IsNullOrEmpty(friendEmailAddress))
                return "<xml><IsValid>0</IsValid><FailureReason>InvalidFriendEmailAddress</FailureReason></xml>";

            // perform the operation and get the return code
            SQL sql = new SQL();

            string friendNickname, friendMintChipId;

            AddFriendResult result = sql.AddFriend(emailAddress, friendEmailAddress, out friendNickname, out friendMintChipId);

            if (result == AddFriendResult.Success)
                return string.Format("<xml><IsValid>1</IsValid><Nickname>{0}</Nickname><MintChipId>{1}</MintChipId></xml>", SecurityElement.Escape(friendNickname), SecurityElement.Escape(friendMintChipId));
            else
                return string.Format("<xml><IsValid>0</IsValid><FailureReason>{0}</FailureReason></xml>", result.ToString());
        }

        #endregion

        #region GetPendingFriendRequests

        [WebMethod]
        public string GetPendingFriendRequests(string emailAddress)
        {
            SQLLogger.LogInfo(string.Format("GetPendingFriendRequests for '{0}'.", emailAddress));

            SQL sql = new SQL();

            DataSet ds = sql.GetPendingFriendRequests(emailAddress);

            string requests = string.Empty;

            if (ds.Tables.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    requests += string.Format("<Request>{0}</Request>", SecurityElement.Escape((string)row["Email"]));
                }
            }

            return string.Format("<PendingFriendRequests>{0}</PendingFriendRequests>", requests);
        }

        #endregion

        #region ConfirmFriend

        [WebMethod]
        public string ConfirmFriend(string emailAddress, string friendEmailAddress)
        {
            // to save time, for now the only thing that is returned is the MintChipId if things succeed, empty string if it fails for whatever reason
            SQLLogger.LogInfo(string.Format("ConfirmFriend request for '{0}' and '{1}'.", emailAddress, friendEmailAddress));

            SQL sql = new SQL();

            return sql.ConfirmFriend(emailAddress, friendEmailAddress);
        }

        #endregion

        #region CreateBill

        [WebMethod]
        public string CreateBill(string emailAddress, string friendEmailAddresses, double totalBill, int tipType, double tip, double paymentTotal, double portion)
        {
            SQLLogger.LogInfo(string.Format("Creating Bill by '{0}' for '{1}'. Billed amount {2}, tip type {3}, tip {4}, total of {5} paying {6} each.", emailAddress, friendEmailAddresses, totalBill, tipType, tip, paymentTotal, portion));

            SQL sql = new SQL();

            return sql.CreateBill(emailAddress, friendEmailAddresses, totalBill, tipType, tip, paymentTotal, portion);
        }

        #endregion

        #region GetUnpaidBills

        [WebMethod]
        public string GetUnpaidBills(string emailAddress)
        {
            SQLLogger.LogInfo(string.Format("GetUnpaidBills({0})", emailAddress));

            SQL sql = new SQL();

            DataSet ds = sql.GetUnpaidBills(emailAddress);

            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return "";

            // cheat for now and only return the first row
            DataRow row = ds.Tables[0].Rows[0];

            return string.Format("<Bill><BillParticipantId>{0}</BillParticipantId><BillId>{1}</BillId><Payment>{2}</Payment><BillOwner>{3}</BillOwner><BillOwnerMintChipId>{4}</BillOwnerMintChipId></Bill>", row["BillParticipantId"], row["BillId"], row["Payment"], SecurityElement.Escape((string)row["UserEmail"]), SecurityElement.Escape((string)row["UserMintChipId"]));
        }

        #endregion

        #region FinalizePayment

        [WebMethod]
        public string FinalizePayment(int billParticipantId, string transactionId)
        {
            SQLLogger.LogInfo(string.Format("Finalize payment {0}, '{1}'.", billParticipantId, transactionId));

            SQL sql = new SQL();

            sql.FinalizePayment(billParticipantId, transactionId);

            return "";
        }

        #endregion

        #region Utility functions

        // an example would be CW8E3W
        /// <summary>Return a random 6 digit code that users will type in confirm their email</summary>
        private string CreateRandomConfirmationCode()
        {
            string availableChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            Random random = new Random();

            StringBuilder sb = new StringBuilder(6);    // 6 characters in total

            for (int i = 0; i < 6; i++)
            {
                int index = random.Next(0, availableChars.Length);

                sb.Append(availableChars[index]);
            }

            return sb.ToString();
        }

        #endregion
    }
}

#region Old Code

// this does not work
/*
 System.Net.Mail.SmtpClient smtpClient = new System.Net.Mail.SmtpClient("smtp.mailgun.org", 587);
                smtpClient.Credentials = new NetworkCredential("postmaster@app942.mailgun.org", "1aqaxoc547c2");
                smtpClient.UseDefaultCredentials = false;
                smtpClient.DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network;

                System.Net.Mail.MailMessage message = new System.Net.Mail.MailMessage("wolfendaled@yahoo.com", "wolfendaled@yahoo.com");
                message.Subject = "Testing";
                message.Body = "Game tonight.";

                smtpClient.Send(message);
*/

#endregion