﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

using MintChipWebApp.Data;

namespace MintChipWebApp
{
    public partial class _Default : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
#if DEBUG
            Data.SQL.TestConfirmAccount();
#endif
            try
            {
                //Response.Write(System.Environment.MachineName);
                //Response.Write(" ");
                //Response.Write(DateTime.Now.ToString());
            }
            catch
            {
            }
        }
    }
}