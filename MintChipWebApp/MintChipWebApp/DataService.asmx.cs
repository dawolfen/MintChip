using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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

        [WebMethod]
        public string HelloWorld()
        {
            return "Hello World";
        }

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