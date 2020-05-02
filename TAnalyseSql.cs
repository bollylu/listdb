using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using BLTools;
using BLTools.Diagnostic.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Smo;
using static listdb.ASqlDocumenter;

namespace listdb {
  public class TAnalyseSql : ALoggable {

    #region --- Parameter names --------------------------------------------
    public const string PARAM_SERVER_FILTER = "server";
    public const string PARAM_DB_FILTER = "dbfilter";
    public const string PARAM_TABLE_FILTER = "tablefilter";
    #endregion --- Parameter names --------------------------------------------

    #region --- Public properties ------------------------------------------------------------------------------
    public bool Verbose { get; private set; }
    public List<string> ParamServerList { get; private set; } = new List<string>();
    public List<string> ParamDbList { get; private set; } = new List<string>();
    public List<string> ParamTableList { get; private set; } = new List<string>();
    public bool ParamWithConfig { get; private set; }
    public EDocumentConfigType ParamConfigType { get; private set; }
    public bool ParamUserOnly { get; private set; }
    public bool ParamWithDatabases { get; private set; }
    public EDocumentDatabasesType ParamDatabaseType { get; private set; }
    public bool WithSP { get; private set; }
    public string SpDetails { get; private set; }
    public bool WithTables { get; private set; }
    public EDocumentTablesType ParamTablesType { get; private set; }
    public bool WithViews { get; private set; }
    public string ViewDetails { get; private set; }
    public bool WithUsers { get; private set; }
    public EDocumentUsersType ParamUsersType { get; private set; }
    public bool WithTriggers { get; private set; }
    public string TriggerDetails { get; private set; }
    public bool WithJobs { get; private set; }
    public string JobDetails { get; private set; }
    public List<string> JobStatusList { get; private set; } = new List<string>();
    public bool WithJobHistory { get; private set; }
    public string JobHistoryDetails { get; private set; }
    public bool SmartList { get; private set; }
    public bool WithFunctions { get; private set; }
    public string FunctionDetails { get; private set; }
    public bool WithUDT { get; private set; }
    public string UDTDetails { get; private set; }
    public bool WithDTS { get; private set; }
    public string DtsDetails { get; private set; }
    public string XmlOutputPath { get; private set; }
    public string XmlOutputFile { get; private set; }
    public bool WithDebug { get; private set; }
    public string DebugFile { get; private set; }
    public string JobRegEx { get; private set; }
    public string DtsRegEx { get; private set; }
    public string SpRegEx { get; private set; }
    public string ViewRegEx { get; private set; }
    public string TableRegEx { get; private set; }
    public string ParamUserId { get; private set; }
    public string ParamPassword { get; private set; }
    public int JobHistoryDays { get; private set; }

    public List<string> ServerRoles { get; private set; } = new List<string>();
    #endregion --- Public properties ---------------------------------------------------------------------------

    private bool IsSqlConnectionActive = false;

    /***************************************************************************************************/

    public SplitArgs Args { get; private set; }

    public TAnalyseSql(SplitArgs args) {

      Args = args;



    }

    public void Initialize() {

      #region Initialize parameters

      ParamServerList.AddRange(Args.GetValue(PARAM_SERVER_FILTER, Environment.MachineName).Split(';').Where(x => x != ""));

      ParamDbList.AddRange(Args.GetValue(PARAM_DB_FILTER, "").Split(';').Where(x => x != ""));

      ParamTableList.AddRange(Args.GetValue(PARAM_TABLE_FILTER, "").Split(';').Where(x => x != ""));

      ParamWithConfig = Args.IsDefined("config");
      ParamConfigType = (EDocumentConfigType)Enum.Parse(typeof(EDocumentConfigType), Args.GetValue("config", "list"), true);

      ParamUserOnly = !Args.IsDefined("system");

      ParamWithDatabases = Args.IsDefined("databases");

      ParamDatabaseType = (EDocumentDatabasesType)Enum.Parse(typeof(EDocumentDatabasesType), Args.GetValue("databases", "list"), true);

      WithSP = Args.IsDefined("sp");
      SpDetails = Args.GetValue("sp", "list");

      WithTables = Args.IsDefined("tables");
      ParamTablesType = (EDocumentTablesType)Enum.Parse(typeof(EDocumentTablesType), Args.GetValue("tables", "list"), true);

      WithViews = Args.IsDefined("views");
      ViewDetails = Args.GetValue("views", "list");

      WithUsers = Args.IsDefined("users");
      ParamUsersType = (EDocumentUsersType)Enum.Parse(typeof(EDocumentUsersType), Args.GetValue("users", "list"), true);

      WithTriggers = Args.IsDefined("triggers");
      TriggerDetails = Args.GetValue("triggers", "list");

      WithJobs = Args.IsDefined("jobs");
      JobDetails = Args.GetValue("jobs", "list");

      foreach (string JobItem in Args.GetValue("jobstatus", "").Split(';').Where(x => x != "")) {
        JobStatusList.Add(JobItem.ToLower());
      }

      WithJobHistory = Args.IsDefined("jobhistory");
      JobHistoryDetails = Args.GetValue("jobhistory", "list");
      if (JobHistoryDetails.IndexOf(":") != -1) {
        JobHistoryDays = Int32.Parse(JobHistoryDetails.Split(':')[1]);
        JobHistoryDetails = JobHistoryDetails.Split(':')[0];
      } else {
        JobHistoryDays = 0;
      }

      Verbose = Args.IsDefined("verbose");
      SmartList = Args.IsDefined("smart");

      WithFunctions = Args.IsDefined("udf");
      FunctionDetails = Args.GetValue("udf", "list");

      WithUDT = Args.IsDefined("udt");
      UDTDetails = Args.GetValue("udt", "list");

      WithDTS = Args.IsDefined("dts");
      DtsDetails = Args.GetValue("dts", "list");

      XmlOutputPath = Args.GetValue("xmloutputpath", "");
      XmlOutputFile = Args.GetValue("xmloutputfile", "");

      WithDebug = Args.IsDefined("debug");
      DebugFile = Args.GetValue("debugfile", "");

      JobRegEx = Args.GetValue("jobsregex", "");
      DtsRegEx = Args.GetValue("dtsregex", "");
      SpRegEx = Args.GetValue("spregex", "");
      ViewRegEx = Args.GetValue("viewsregex", "");
      TableRegEx = Args.GetValue("tablesregex", "");

      ParamUserId = Args.GetValue("userid", "");
      ParamPassword = Args.GetValue("password", "");

      #endregion

      #region Xml init
      if (XmlOutputFile.Length != 0) {

        if (XmlOutputPath.Length != 0) {
          XmlOutputPath = XmlOutputPath.TrimEnd(' ', '\\');
          if (!Directory.Exists(XmlOutputPath)) {
            Directory.CreateDirectory(XmlOutputPath);
          }
        }

        // generate the xsl style sheet from the embedded resource
        Stream XsltStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("listdb.report.xslt");
        XDocument Xslt = XDocument.Load(XsltStream);
        Xslt.Save(Path.Combine(XmlOutputPath, "report.xslt"));

        XDocument XmlOutput = new XDocument();
        XmlOutput.Add(new XProcessingInstruction("xml-stylesheet", "type=\"text/xsl\" href=\"report.xslt\""));
      }

      #endregion
    }

    public string GetVerbose() {
      StringBuilder RetVal = new StringBuilder();

      #region Display Verbose summary
      RetVal.AppendLine($"Listing the objects of the server(s) {string.Join(", ", ParamServerList)}");
      RetVal.AppendLine();

      if (ParamWithDatabases) {
        RetVal.AppendLine($"# Displays the databases, mode {ParamDatabaseType}");
      }
      if (ParamDbList.Count == 0) {
        RetVal.AppendLine("# No restrictions on databases");
      } else {
        RetVal.AppendLine($"# Restricted to the following database(s): {string.Join(", ", ParamDbList)}");
      }

      if (WithTables) {
        RetVal.AppendLine($"# Displays the tables, mode {ParamTablesType}");
        if (ParamTableList.Count == 0) {
          RetVal.AppendLine("# No restrictions on tables");
        } else {
          RetVal.AppendLine($"# Restricted to the following table(s): {string.Join(", ", ParamTableList)}");
        }
      }

      if (WithViews) {
        RetVal.AppendLine($"# Displays the views, mode {ViewDetails}");
      }

      if (WithTriggers) {
        RetVal.AppendLine($"# Displays the triggers, mode {TriggerDetails}");
      }

      if (WithUsers) {
        RetVal.AppendLine($"# Displays the users, mode {ParamUsersType}");
      }

      if (WithSP) {
        RetVal.AppendLine($"# Displays the stored procedures, mode {SpDetails}");
      }

      if (WithJobs) {
        RetVal.AppendLine($"# Displays the jobs, mode {JobDetails}");
        if (JobRegEx.Length != 0) {
          RetVal.AppendLine($"#   retricted to the following RegEx : {JobRegEx}");
        }
      }

      if (WithDTS) {
        RetVal.AppendLine($"# Displays the DTS packages, mode {DtsDetails}");
        if (DtsRegEx.Length != 0) {
          RetVal.AppendLine($"#   retricted to the following RegEx : {DtsRegEx}");
        }
      }
      #endregion

      return RetVal.ToString();
    }
    public void Process() {

      foreach (string ServerItem in ParamServerList) {
        Server SqlServer = _Connect(ServerItem);

        if (SqlServer == null) {
          continue;
        }

        ISqlDocumenter Documenter = new TSqlDocumenterConsole(SqlServer);

        if (ParamWithConfig) {
          Documenter.DocumentConfig(ParamConfigType);
        }

        if (WithUsers) {
          Documenter.DocumentUsers(ParamUsersType);
        }

        if (ParamWithDatabases) {
          TSelectionCriterias DatabaseCriterias = new TSelectionCriterias();
          DatabaseCriterias.Filter.AddRange(ParamDbList);
          DatabaseCriterias.SelectUserData = ParamUserOnly;
          DatabaseCriterias.SelectSystemData = !ParamUserOnly;
          Documenter.DocumentDatabases(ParamDatabaseType, DatabaseCriterias);
        }

        if (ParamWithDatabases && (WithTables || WithViews || WithTriggers || WithUsers || WithSP || WithFunctions || WithUDT)) {
          foreach (Database DatabaseItem in SqlServer.Databases) {
            if ((ParamDbList.Count == 0) || (ParamDbList.Contains(DatabaseItem.Name.ToLower()))) {

              if (WithTables) {
                TSelectionCriterias TableCriterias = new TSelectionCriterias();
                TableCriterias.Filter.AddRange(ParamTableList);
                TableCriterias.SelectUserData = ParamUserOnly;
                TableCriterias.SelectSystemData = !ParamUserOnly;
                Documenter.DocumentTables(DatabaseItem, ParamTablesType, TableCriterias);
              }
            }
          }
        }

      }

    }

    private Server _Connect(string sqlServer) {

      Server RetVal = new Server();
      if (ParamUserId == "") {
        RetVal.ConnectionContext.LoginSecure = true;
      } else {
        RetVal.ConnectionContext.LoginSecure = false;
        RetVal.ConnectionContext.Login = ParamUserId;
        RetVal.ConnectionContext.Password = ParamPassword;
      }

      StringBuilder LogText = new StringBuilder($"-- ##### Connecting to the server : {sqlServer} ##### ... :");
      try {
        IsSqlConnectionActive = true;
        foreach (ServerRole ServerRoleItem in RetVal.Roles) {
          ServerRoles.Add(ServerRoleItem.Name);
        }
        LogText.Append("OK");
        return RetVal;
      } catch (Exception ex) {
        IsSqlConnectionActive = false;
        LogText.Append($"FAILED : {ex.Message}");
        return null;
      } finally {
        Log(LogText.ToString());
      }
    }
  }
}
