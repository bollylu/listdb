using BLTools;
using BLTools.Diagnostic.Logging;
using BLTools.Text;

using Microsoft.SqlServer.Management.Smo;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using static listdb.ASqlDocumenter;


/*
 * History:
 * v0.5a : improved smart mode: transform double spaces into CRLF, plus merge duplicate CRLF (in job history)
 * v0.6  : added DTS listing
 * v0.6a : correction: when a server is unavailable, skip gracefuly
 * v0.6b : added number of records in table when tables=stats
 * v0.6c : improved DTS listing with steps and full options
 * v0.6d : added MakeBlock, some bug corrections, added /config
 * v0.6e : added info for DTS packages, added timegrid option for Jobs
 * v0.6f : added get_xp_sendmail
 * v0.7  : added list of mail for jobs
 * v0.7b : Correction to a bug in list of failed jobs
 * v0.7c : added tablelist, finished indexes
 * v0.7e : tablelist option with tolower()
 * v0.7f : added jobregex, dtsregex, spregex; added NextAction for job; added sort order for jobs and global variables
 * v0.7g : display status of db in list mode
 * v0.7h : added documentation feature for jobs in xml
 * v0.8  : added complete xml + xsl feature
 * v0.8a : added filegroup for indexes
 * v0.8b : rebuilded after FxCop optimizations
 * v0.9  : added growth factor and size limits to database physical
 * v0.9a : added /userid and /password to connect in SQL mode
 * v0.9b : added better handling for DataDrivenQueryTask
 * v0.9c : corrected small bugs + presentation
 * v0.9d : added free space for physical databases
 * v0.9e : minor cosmetic corrections
 * v0.9f : does not fail anymore on SQL 7.0 when asking DB Status + cosmetic corrections
 * v0.9g : minor cosmetic corrections
 * v0.9h : included Metadata DTS packages in list
 * v0.9i : added space available to databases physical
 * v0.9k : added display of default data path
 * v0.10 : added listing of comments for columns in table (/tables=comments)
 */

namespace listdb {

  class Program : ALoggable {

    #region --- Parameter names --------------------------------------------
    public const string PARAM_SERVER_FILTER = "server";
    public const string PARAM_DB_FILTER = "dbfilter";
    public const string PARAM_TABLE_FILTER = "tablefilter";
    #endregion --- Parameter names --------------------------------------------

    static bool Verbose;
    static readonly Version VERSION = new Version("1.0");

    static void Main(string[] args) {

      XmlTextWriter oXmlTextWriter = null;
      ILogger VerboseLogger = new TConsoleLogger();

      #region Arguments

      SplitArgs Args = new SplitArgs(args);

      int JobHistoryDays;

      #region Initialize parameters
      List<string> ParamServerList = new List<string>();
      ParamServerList.AddRange(Args.GetValue(PARAM_SERVER_FILTER, Environment.MachineName).Split(';').Where(x => x != ""));

      List<string> ParamDbList = new List<string>();
      ParamDbList.AddRange(Args.GetValue(PARAM_DB_FILTER, "").Split(';').Where(x => x != ""));

      List<string> ParamTableList = new List<string>();
      ParamTableList.AddRange(Args.GetValue(PARAM_TABLE_FILTER, "").Split(';').Where(x => x != ""));

      bool ParamWithConfig = Args.IsDefined("config");
      EDocumentConfigType ParamConfigType = (EDocumentConfigType)Enum.Parse(typeof(EDocumentConfigType), Args.GetValue("config", "list"), true);

      bool ParamUserOnly = !Args.IsDefined("system");

      bool ParamWithDatabases = Args.IsDefined("databases");
      string ParamDatabaseDetails = Args.GetValue("databases", "list");
      ParamDatabaseDetails = ParamDatabaseDetails == "" ? "list" : ParamDatabaseDetails;

      bool WithSP = Args.IsDefined("sp");
      string SpDetails = Args.GetValue("sp", "list");

      bool WithTables = Args.IsDefined("tables");
      EDocumentTablesType ParamTablesType = (EDocumentTablesType)Enum.Parse(typeof(EDocumentTablesType), Args.GetValue("tables", "list"), true);

      bool WithViews = Args.IsDefined("views");
      string ViewDetails = Args.GetValue("views", "list");

      bool WithUsers = Args.IsDefined("users");
      EDocumentUsersType ParamUsersType = (EDocumentUsersType)Enum.Parse(typeof(EDocumentUsersType), Args.GetValue("users", "list"), true);

      bool WithTriggers = Args.IsDefined("triggers");
      string TriggerDetails = Args.GetValue("triggers", "list");

      bool WithJobs = Args.IsDefined("jobs");
      string JobDetails = Args.GetValue("jobs", "list");

      List<string> JobStatusList = new List<string>();
      foreach (string JobItem in Args.GetValue("jobstatus", "").Split(';').Where(x => x != "")) {
        JobStatusList.Add(JobItem.ToLower());
      }

      bool WithJobHistory = Args.IsDefined("jobhistory");
      string JobHistoryDetails = Args.GetValue("jobhistory", "list");
      if (JobHistoryDetails.IndexOf(":") != -1) {
        JobHistoryDays = Int32.Parse(JobHistoryDetails.Split(':')[1]);
        JobHistoryDetails = JobHistoryDetails.Split(':')[0];
      } else {
        JobHistoryDays = 0;
      }

      Verbose = Args.IsDefined("verbose");
      bool SmartList = Args.IsDefined("smart");

      bool WithFunctions = Args.IsDefined("udf");
      string FunctionDetails = Args.GetValue("udf", "list");

      bool WithUDT = Args.IsDefined("udt");
      string UDTDetails = Args.GetValue("udt", "list");

      bool WithDTS = Args.IsDefined("dts");
      string DtsDetails = Args.GetValue("dts", "list");

      string XmlOutputPath = Args.GetValue("xmloutputpath", "");
      string XmlOutputFile = Args.GetValue("xmloutputfile", "");

      bool WithDebug = Args.IsDefined("debug");
      string DebugFile = Args.GetValue("debugfile", "");

      string JobRegEx = Args.GetValue("jobsregex", "");
      string DtsRegEx = Args.GetValue("dtsregex", "");
      string SpRegEx = Args.GetValue("spregex", "");
      string ViewRegEx = Args.GetValue("viewsregex", "");
      string TableRegEx = Args.GetValue("tablesregex", "");

      string ParamUserId = Args.GetValue("userid", "");
      string ParamPassword = Args.GetValue("password", "");

      #endregion
      #endregion

      #region Trace initialization
      Trace.AutoFlush = true;
      if (WithDebug) {
        if (DebugFile.Length != 0) {
          Trace.Listeners.Add(new TextWriterTraceListener(DebugFile, "debug"));
        } else {
          Trace.Listeners.Add(new TextWriterTraceListener(Console.Error, "debug"));
        }
      } else {
        Trace.Listeners.Add(new NullTraceListener("debug"));
      }
      //if (Verbose) {
      //  Trace.Listeners.Add(new TextWriterTraceListener(Console.Out, "verbose"));
      //} else {
      //  Trace.Listeners.Add(new NullTraceListener("verbose"));
      //}
      #endregion

      #region Xml init
      if (XmlOutputFile.Length != 0) {

        if (XmlOutputPath.Length != 0) {
          XmlOutputPath = XmlOutputPath.TrimEnd(new char[] { ' ', '\\' });
          if (!Directory.Exists(XmlOutputPath)) {
            Directory.CreateDirectory(XmlOutputPath);
          }
          XmlOutputPath += "\\";
        }

        // generate the xsl style sheet from the embedded resource
        XmlDocument oXsl = new XmlDocument();
        Stream oStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("listdb.report.xslt");
        XmlReader oXslReader = new XmlTextReader(oStream);
        oXsl.Load(oXslReader);
        oXslReader.Close();

        oXmlTextWriter = new XmlTextWriter(XmlOutputPath + "report.xslt", System.Text.Encoding.UTF8);
        oXmlTextWriter.Formatting = System.Xml.Formatting.Indented;
        oXmlTextWriter.Indentation = 2;
        oXsl.WriteTo(oXmlTextWriter);
        oXmlTextWriter.Flush();
        oXmlTextWriter.Close();

        Trace.Listeners["debug"].WriteLine(XmlOutputPath + XmlOutputFile);
        oXmlTextWriter = new XmlTextWriter(XmlOutputPath + XmlOutputFile, System.Text.Encoding.UTF8);
        oXmlTextWriter.Formatting = System.Xml.Formatting.Indented;
        oXmlTextWriter.Indentation = 2;
        oXmlTextWriter.WriteStartDocument(true);
        oXmlTextWriter.WriteProcessingInstruction("xml-stylesheet", "type=\"text/xsl\" href=\"report.xslt\"");

        oXmlTextWriter.WriteStartElement("root");
      }

      #endregion

      bool SqlConnectionActive = false;
      if (ParamServerList.Count == 0) {
        Usage();
      }

      #region Display Verbose summary
      VerboseLogger.Log("");
      VerboseLogger.Log($"Listing the objects of the server(s) {string.Join(", ", ParamServerList)}");
      VerboseLogger.Log("");

      if (ParamWithDatabases) {
        VerboseLogger.Log($"# Displays the databases, mode {ParamDatabaseDetails}");
      }
      if (ParamDbList.Count == 0) {
        VerboseLogger.Log("# No restrictions on databases");
      } else {
        VerboseLogger.Log($"# Restricted to the following database(s): {string.Join(", ", ParamDbList)}");
      }

      if (WithTables) {
        VerboseLogger.Log($"# Displays the tables, mode {ParamTablesType.ToString()}");
        if (ParamTableList.Count == 0) {
          VerboseLogger.Log("# No restrictions on tables");
        } else {
          VerboseLogger.Log($"# Restricted to the following table(s): {string.Join(", ", ParamTableList)}");
        }
      }

      if (WithViews) {
        VerboseLogger.Log(string.Format("# Displays the views, mode {0}", ViewDetails));
      }

      if (WithTriggers) {
        VerboseLogger.Log(string.Format("# Displays the triggers, mode {0}", TriggerDetails));
      }

      if (WithUsers) {
        VerboseLogger.Log(string.Format("# Displays the users, mode {0}", ParamUsersType.ToString()));
      }

      if (WithSP) {
        VerboseLogger.Log(string.Format("# Displays the stored procedures, mode {0}", SpDetails));
      }

      if (WithJobs) {
        VerboseLogger.Log(string.Format("# Displays the jobs, mode {0}", JobDetails));
        if (JobRegEx.Length != 0) {
          VerboseLogger.Log(string.Format("#   retricted to the following RegEx : {0}", JobRegEx));
        }
      }

      if (WithDTS) {
        VerboseLogger.Log(string.Format("# Displays the DTS packages, mode {0}", DtsDetails));
        if (DtsRegEx.Length != 0) {
          VerboseLogger.Log(string.Format("#   retricted to the following RegEx : {0}", DtsRegEx));
        }
      }
      VerboseLogger.Log("");
      #endregion


      #region Connect to the SQL server
      List<string> ServerRoles = new List<string>();
      foreach (string strServer in ParamServerList) {

        Server SqlServer = new Server();
        if (ParamUserId == "") {
          SqlServer.ConnectionContext.LoginSecure = true;
        } else {
          SqlServer.ConnectionContext.LoginSecure = false;
          SqlServer.ConnectionContext.Login = ParamUserId;
          SqlServer.ConnectionContext.Password = ParamPassword;
        }

        try {
          VerboseLogger.Log(string.Format("-- ##### Connecting to the server : {0} ##### ... ", strServer));
          VerboseLogger.Log("ok.");
          VerboseLogger.Log("");
          SqlConnectionActive = true;
          foreach (ServerRole oServerRole in SqlServer.Roles) {
            ServerRoles.Add(oServerRole.Name);
          }
        } catch (Exception ex) {
          VerboseLogger.Log("-- unable to connect to the server.");
          VerboseLogger.Log("-- " + ex.Message);
          VerboseLogger.Log("");
          SqlConnectionActive = false;
        }
        #endregion

        if (SqlConnectionActive) {

          if (XmlOutputFile.Length != 0) {
            oXmlTextWriter.WriteStartElement("server");
            oXmlTextWriter.WriteAttributeString("serverName", SqlServer.Name);
          }

          ISqlDocumenter Documenter = new TSqlDocumenterConsole(SqlServer);

          #region Config
          if (ParamWithConfig) {
            Documenter.DocumentConfig(ParamConfigType);
          }
          #endregion

          #region Logins
          if (WithUsers) {
            Documenter.DocumentUsers(ParamUsersType);
          }

          #endregion

          if (ParamWithDatabases || WithTables || WithViews || WithTriggers || WithUsers || WithSP || WithFunctions || WithUDT) {
            DatabaseCollection oDBs = SqlServer.Databases;
            foreach (Database DatabaseItem in oDBs) {
              if ((ParamDbList.Count == 0) || (ParamDbList.Contains(DatabaseItem.Name.ToLower()))) {

                #region Databases
                if (ParamWithDatabases) {
                  if (!ParamUserOnly || (ParamUserOnly && !DatabaseItem.IsSystemObject)) {
                    VerboseLogger.Log(string.Format("accessing database {0}", DatabaseItem.Name));
                    switch (ParamDatabaseDetails) {
                      case "stats":
                        #region stats
                        Console.Write("-- {0,-40}", DatabaseItem.Name.TrimEnd().PadRight(40, '.'));
                        Console.Write(" {0,20}", DatabaseItem.Size.ToString("#,##0.00 MB"));
                        Console.Write(" {0,25}", DatabaseItem.SpaceAvailable.ToString("#,##0.00 MB free"));
                        Console.Write(" (D={0,20})", DatabaseItem.DataSpaceUsage.ToString("#,##0 KB"));
                        Console.Write(" (I={0,20})", DatabaseItem.IndexSpaceUsage.ToString("#,##0 KB"));
                        Console.Write(" {0,4} users", DatabaseItem.Users.Count);
                        DatabaseOptions oDBO;
                        try {
                          oDBO = DatabaseItem.DatabaseOptions;
                          Console.Write(" | {0}", SqlUtils.RecoveryModelText(oDBO.RecoveryModel));
                        } catch {
                          Console.Write(" | only for 2000+");
                        }
                        Console.WriteLine();
                        break;
                      #endregion
                      case "physical":
                        #region physical
                        VerboseLogger.Log(string.Format("-- Database : {0}", DatabaseItem.Name.TrimEnd()));

                        #region Data files
                        foreach (FileGroup oFG in DatabaseItem.FileGroups) {
                          foreach (DataFile oDBF in oFG.Files) {
                            //Console.Write("{0}", oSql.TrueName.PadRight(30, '.'));
                            Console.Write(" {0}", DatabaseItem.Name.PadRight(30, '.'));
                            Console.Write(" {0}", oDBF.Name.PadRight(40, '.'));
                            Console.Write(" {0}", oFG.Name.PadRight(15, '.'));
                            Console.Write(" {0}", oDBF.FileName.Substring(0, 80).TrimEnd().PadRight(80, '.'));
                            Console.Write(" ({0,13} [{1,13}])", oDBF.Size.ToString("#0.00 MB"), oDBF.VolumeFreeSpace.ToString("#0.00 MB"));

                            switch (oDBF.GrowthType) {
                              case FileGrowthType.KB:
                                #region Growth MB
                                if (oDBF.Growth > 0) {
                                  Console.Write(" + {0,8}", ((int)(oDBF.Growth / 1024)).ToString("#0 MB"));
                                  if (oDBF.MaxSize > 0) {
                                    Console.Write(", upto {0,-8}", oDBF.MaxSize.ToString("#0 MB"));
                                  } else {
                                    Console.Write(", no limit     ");
                                  }
                                } else {
                                  Console.Write(" + no growth            ");
                                }
                                break;
                              #endregion
                              case FileGrowthType.Percent:
                                #region Growth Percent
                                if (oDBF.Growth > 0) {
                                  Console.Write(" + {0,5} % ", oDBF.Growth.ToString("#0"));
                                  if (oDBF.MaxSize > 0) {
                                    Console.Write(", upto {0,-8}", oDBF.MaxSize.ToString("#0 MB"));
                                  } else {
                                    Console.Write(", no limit     ");
                                  }
                                } else {
                                  Console.Write(" + no growth            ");
                                }
                                break;
                              #endregion
                              default:
                                Console.Write(" + no growth            ");
                                break;

                            }

                            string oDbDisk = "";
                            if (oDBF.FileName.Substring(1, 1) == ":") {
                              oDbDisk = oDBF.FileName.Substring(0, 1);
                            }
                            int DiskSpaceFree = GetDiskFreeSpaceSQL(SqlServer, oDbDisk);
                            Console.Write(" {0,10} MB free", DiskSpaceFree);

                            Console.WriteLine();
                          }
                        }
                        #endregion
                        #region Log files
                        foreach (LogFile oLog in DatabaseItem.LogFiles) {
                          //Console.Write("{0}", oSql.TrueName.PadRight(30, '.'));
                          Console.Write(" {0}", DatabaseItem.Name.PadRight(30, '.'));
                          Console.Write(" {0}", oLog.Name.PadRight(40, '.'));
                          Console.Write(" {0}", string.Empty.PadRight(15, '.'));
                          Console.Write(" {0}", oLog.FileName.Substring(0, 80).TrimEnd().PadRight(80, '.'));
                          //Console.Write(" ({0,13})", ((float)(oLog.SizeInKB/1024.0)).ToString("#0.00 MB"));
                          Console.Write(" ({0,13} [{1,13}])", oLog.Size.ToString("#0.00 MB"), oLog.VolumeFreeSpace.ToString("#0.00 MB"));
                          switch (oLog.GrowthType) {
                            case FileGrowthType.KB:
                              #region Growth MB
                              if (oLog.Growth > 0) {
                                Console.Write(" + {0,8}", ((float)(oLog.Growth / 1024)).ToString("#0 MB"));
                                if (oLog.MaxSize > 0) {
                                  Console.Write(", upto {0,-8}", oLog.MaxSize.ToString("#0 MB"));
                                } else {
                                  Console.Write(", no limit     ");
                                }
                              } else {
                                Console.Write(" + no growth              ");
                              }
                              break;
                            #endregion
                            case FileGrowthType.Percent:
                              #region Growth Percent 
                              if (oLog.Growth > 0) {
                                Console.Write(" + {0,5} % ", oLog.Growth.ToString("#0"));
                                if (oLog.MaxSize > 0) {
                                  Console.Write(", upto {0,-8}", oLog.MaxSize.ToString("#0 MB"));
                                } else {
                                  Console.Write(", no limit     ");
                                }
                              } else {
                                Console.Write(" + no growth              ");
                              }
                              break;
                            #endregion
                            default:
                              Console.Write(" + no growth              ");
                              break;
                          }
                          string oDbDisk = "";
                          if (oLog.FileName.Substring(1, 1) == ":") {
                            oDbDisk = oLog.FileName.Substring(0, 1);
                          }
                          int DiskSpaceFree = GetDiskFreeSpaceSQL(SqlServer, oDbDisk);
                          Console.Write(" {0,10} MB free", DiskSpaceFree);

                          Console.WriteLine();
                        }
                        #endregion
                        Console.WriteLine();
                        break;
                      #endregion
                      case "list":
                      case "":
                      default:
                        #region list
                        Console.Write("-- Database : {0}", DatabaseItem.Name.PadRight(50, '.'));
                        Console.Write(" {0}", SqlUtils.DbStatusText(DatabaseItem.Status));
                        Console.WriteLine();
                        break;
                        #endregion
                    }
                  }
                  #endregion

                  #region Database users
                  if (WithUsers) {
                    //if (!ParamUserOnly || (ParamUserOnly && !oDB.IsSystemObject)) {
                    //  foreach (User oUser in oDB.Users) {
                    //    switch (UserDetails) {
                    //      case "full":
                    //        #region full
                    //        Console.Write("{0,-40}", oUser.Name.TrimEnd().PadRight(40, '.'));
                    //        try {
                    //          //string test = oUser.Login.TrimEnd();
                    //          Console.Write("{0,-40}", oUser.Login.TrimEnd().PadRight(40, '.'));
                    //        } catch {
                    //          Console.Write("{0,-40}", "! no login defined !".PadRight(40, '.'));
                    //        }
                    //        StringBuilder sbRoles = new StringBuilder();
                    //        foreach (string strRoles in oUser.EnumRoles()) {
                    //          sbRoles.Append(strRoles + ", ");
                    //        }
                    //        sbRoles.Remove(sbRoles.Length - 2, 2);
                    //        Console.Write(sbRoles.ToString());
                    //        Console.WriteLine();
                    //        break;
                    //      #endregion

                    //      case "":
                    //      case "list":
                    //      default:
                    //        #region list
                    //        Console.Write("-- > User : {0}", oUser.Name.PadRight(40, '.'));
                    //        if (oUser.Login != null) {
                    //          Console.Write(" {0}", oUser.Login.PadRight(40, '.'));
                    //        } else {
                    //          Console.Write(" *** warning: not mapped ***".PadRight(41, '.'));
                    //        }
                    //        //Console.Write(" {0}", oUser.Group.PadRight(40, '.'));
                    //        Console.Write(" {0}", oUser.HasDBAccess);
                    //        Console.WriteLine();
                    //        break;
                    //        #endregion

                    //    }
                    //  }
                    //}
                  }

                  #endregion
                }

                //#region Tables
                if (WithTables) {
                  Documenter.DocumentTables(DatabaseItem, ParamTablesType);
                }

                /**
                //  foreach (Table2 oTable in oDB.Tables) {
                //    if (TableList.Count == 0 || TableList.Contains(oTable.Name.ToLower())) {
                //      if (!UserOnly || (UserOnly && !oDB.SystemObject && !oTable.SystemObject)) {
                //        if (TableRegEx.Length == 0 || Regex.IsMatch(oTable.Name, TableRegEx, RegexOptions.IgnoreCase)) {
                //          string strTableName;
                //          string strUnderline;
                //          switch (TableDetails) {
                //            case "full":
                //              #region full
                //              strTableName = string.Format("-- > Table : {0}.{1}.{2}", oDB.Name, oDB.UserName, oTable.Name);
                //              strUnderline = "-- > " + new string('=', strTableName.Length - 5);
                //              Console.WriteLine(strUnderline);
                //              Console.WriteLine(strTableName);
                //              Console.WriteLine(strUnderline);
                //              Console.WriteLine(oTable.Script(SQLDMO_SCRIPT_TYPE.SQLDMOScript_Default, mv, mv, SQLDMO_SCRIPT2_TYPE.SQLDMOScript2_Default | SQLDMO_SCRIPT2_TYPE.SQLDMOScript2_ExtendedProperty).TrimStart(new char[] { ' ', '\n', '\r' }).TrimEnd(new char[] { ' ', '\n', '\r' }));
                //              Console.WriteLine("-- > ===[EOT]===");
                //              Console.WriteLine();
                //              break;
                //            #endregion

                //            case "stats":
                //              #region stats
                //              strTableName = string.Format("-- > Table : {0}.{1}.{2}", oDB.Name, oDB.UserName, oTable.Name);
                //              Console.Write(strTableName.PadRight(80, '.'));
                //              Console.Write(" (D={0,20}, ", oTable.DataSpaceUsed.ToString("#,##0 KB"));
                //              Console.Write("{0,20}) ", oTable.Rows.ToString("#,##0 recs"));
                //              Console.Write("(I={0,20})", oTable.IndexSpaceUsed.ToString("#,##0 KB"));
                //              Console.WriteLine();
                //              break;
                //            #endregion

                //            case "indexes":
                //              #region indexes
                //              try {
                //                  oTable.RecalcSpaceUsage();
                //                } catch (Exception ex) {
                //                  Trace.WriteLine(string.Format("Error calculating space usage for {0}", oTable.Name));
                //                  Trace.WriteLine(ex.Message, "Error");
                //                }
                //              strTableName = string.Format("-- > Table : {0}.{1}.{2}", oDB.Name, oDB.UserName, oTable.Name);
                //              Console.Write(strTableName.PadRight(80, '.'));
                //              Console.Write(" (I={0,20})", oTable.IndexSpaceUsed.ToString("#,##0 KB"));
                //              Console.Write(" (D={0,20}, ", oTable.DataSpaceUsed.ToString("#,##0 KB"));
                //              Console.Write("{0,20}) ", oTable.Rows.ToString("#,##0 recs"));
                //              Console.WriteLine();
                //              foreach (Index2 oIndex in oTable.Indexes) {
                //                if (!UserOnly || (UserOnly && !oIndex.Name.StartsWith("_WA_Sys_"))) {
                //                  Console.Write("     Index : {0}", oIndex.Name.PadRight(67, '.'));
                //                  Console.Write(" Size = {0,16}, ", oIndex.SpaceUsed.ToString("#,##0 KB"));
                //                  Console.Write("Key = ");
                //                  SQLDMO.SQLObjectList oSqlObjectList = oIndex.ListIndexedColumns();
                //                  StringBuilder sbText = new StringBuilder();
                //                  foreach (SQLDMO.Column oColumn in oSqlObjectList) {
                //                    sbText.AppendFormat("{0}+", oColumn.Name);
                //                  }
                //                  sbText.Remove(sbText.Length - 1, 1);
                //                  Console.Write(sbText.ToString());

                //                  Console.Write(", Type = {0}", SqlUtils.IndexType2String(oIndex.Type));
                //                  if (oIndex.FillFactor > 0) {
                //                    Console.Write(", Fill factor = {0}%", oIndex.FillFactor);
                //                  }
                //                  Console.Write(", Storage = {0}", oIndex.FileGroup);
                //                  Console.WriteLine();
                //                }
                //              }
                //              Console.WriteLine();
                //              break;
                //            #endregion

                //            case "comments":
                //              strTableName = string.Format("-- > Table : {0}.{1}.{2}", oDB.Name, oDB.UserName, oTable.Name);
                //              strUnderline = "-- > " + new string('=', strTableName.Length - 5);
                //              Console.WriteLine(strUnderline);
                //              Console.WriteLine(strTableName);
                //              Console.WriteLine(strUnderline);
                //              string SqlCommand = string.Format("SELECT objname, convert(varchar(512), value) as comment FROM ::fn_listextendedproperty('MS_Description', 'user', 'dbo', 'table', '{0}', 'column', NULL)", oTable.Name);
                //              DataTable ExtendedProperties = SqlUtils.QR2DataTable(oDB.ExecuteWithResults(SqlCommand, null));
                //              foreach (SQLDMO.Column oColumnItem in oTable.Columns) {
                //                Console.Write("{0}", oColumnItem.Name.PadRight(30, '.'));
                //                Console.Write(" {0}", oColumnItem.Length.ToString().PadLeft(8, '.'));
                //                Console.Write(" {0}", oColumnItem.Datatype.PadRight(10, '.'));
                //                Console.Write(" {0}", oColumnItem.AllowNulls ? "NULL....." : "NOT NULL.");
                //                if (oColumnItem.Default != null) {
                //                  Console.Write(" {0}", oColumnItem.Default.PadRight(20, '.'));
                //                }
                //                string ExtendedPropertyComment = "";
                //                DataRow[] oRows = { };
                //                if (ExtendedProperties.Rows.Count > 0) {
                //                  oRows = ExtendedProperties.Select(string.Format("objname = '{0}'", oColumnItem.Name));
                //                  if (oRows.Length > 0) {
                //                    ExtendedPropertyComment = Encoding.Unicode.GetString(Encoding.Unicode.GetBytes(oRows[0]["comment"].ToString()));
                //                  }
                //                }
                //                int CommentSizeLimit = 100;
                //                if (ExtendedPropertyComment.Length <= CommentSizeLimit) {
                //                  Console.Write(" {0}", ExtendedPropertyComment);
                //                  Console.WriteLine();
                //                } else {
                //                  string Filler = new string(' ', 62);
                //                  int WhitePos = 80;
                //                  while (WhitePos > 1 && ExtendedPropertyComment[WhitePos] != ' ') {
                //                    WhitePos--;
                //                  }
                //                  Console.Write(" {0}", ExtendedPropertyComment.Substring(0, WhitePos));
                //                  ExtendedPropertyComment = ExtendedPropertyComment.Substring(WhitePos);
                //                  Console.WriteLine();
                //                  while (ExtendedPropertyComment.Length > CommentSizeLimit) {
                //                    WhitePos = 80;
                //                    while (WhitePos > 1 && ExtendedPropertyComment[WhitePos] != ' ') {
                //                      WhitePos--;
                //                    }
                //                    Console.WriteLine(Filler + ExtendedPropertyComment.Substring(0, WhitePos));
                //                    ExtendedPropertyComment = ExtendedPropertyComment.Substring(WhitePos);
                //                  }
                //                  if (ExtendedPropertyComment.Length > 0) {
                //                    Console.WriteLine(Filler + ExtendedPropertyComment);
                //                  }
                //                }

                //              }
                //              Console.WriteLine();
                //              break;

                //            case "":
                //            case "list":
                //            default:
                //              #region list
                //              strTableName = string.Format("-- > Table : {0}.{1}.{2}", oDB.Name, oDB.UserName, oTable.Name);
                //              Console.WriteLine(strTableName);
                //              break;
                //              #endregion
                //          }
                //          #region Triggers
                //          if (WithTriggers) {
                //            string strTriggerName;
                //            string strUnderTrigger;
                //            switch (TriggerDetails) {
                //              case "full":
                //                #region full
                //                foreach (Trigger oTrigger in oTable.Triggers) {
                //                  strTriggerName = string.Format("-- Trigger : {0}.{1}.{2}.{3}", oDB.Name, oDB.UserName, oTable.Name, oTrigger.Name);
                //                  strUnderTrigger = "-- " + new string('=', strTriggerName.Length - 3);
                //                  Console.WriteLine(strUnderTrigger);
                //                  Console.WriteLine(strTriggerName);
                //                  Console.WriteLine(strUnderTrigger);
                //                  Console.WriteLine(oTrigger.Text);
                //                  Console.WriteLine("-- ===[EOT]===");
                //                  Console.WriteLine();
                //                }
                //                break;
                //              #endregion
                //              case "list":
                //              case "":
                //              default:
                //                #region list
                //                foreach (Trigger oTrigger in oTable.Triggers) {
                //                  strTriggerName = string.Format("-- > > Trigger : {0}.{1}.{2}.{3}", oDB.Name, oDB.UserName, oTable.Name, oTrigger.Name);
                //                  Console.WriteLine(strTriggerName);
                //                }
                //                break;
                //                #endregion
                //            }
                //          }
                //          #endregion
                //        }
                //      }
                //    }
                //  }
                //}
                //#endregion

                //#region Stored Procedures
                //if (WithSP) {
                //  foreach (StoredProcedure2 oSP in oDB.StoredProcedures) {
                //    if (!UserOnly || (UserOnly && !oDB.SystemObject && !oSP.SystemObject)) {
                //      if (SpRegEx.Length == 0 || Regex.IsMatch(oSP.Name, SpRegEx, RegexOptions.IgnoreCase)) {
                //        string strSpName = string.Format("-- > Procedure : {0}.{1}.{2}", oDB.Name, oDB.UserName, oSP.Name);
                //        string strUnderline = "-- " + new string('=', strSpName.Length - 3);
                //        switch (SpDetails) {
                //          case "full":
                //            Console.WriteLine(strUnderline);
                //            Console.WriteLine(strSpName);
                //            Console.WriteLine(strUnderline);
                //            if (!oSP.Encrypted) {
                //              Console.WriteLine(oSP.Text);
                //            } else {
                //              Console.WriteLine("*** unable to display encrypted stored procedure ***");
                //            }
                //            Console.WriteLine("-- ===[EOP]===");
                //            Console.WriteLine();
                //            break;
                //          case "list":
                //          case "":
                //          default:
                //            Console.WriteLine(strSpName);
                //            break;
                //        }
                //      }
                //    }
                //  }
                //}
                //#endregion

                //#region Functions
                //if (WithFunctions) {
                //  foreach (UserDefinedFunction oUdf in oDB.UserDefinedFunctions) {
                //    if (!UserOnly || (UserOnly && !oDB.SystemObject && !oUdf.SystemObject)) {
                //      string strUdfName = string.Format("-- > UDF : {0}.{1}.{2}", oDB.Name, oDB.UserName, oUdf.Name);
                //      string strUnderline = "-- " + new string('=', strUdfName.Length - 5);
                //      switch (FunctionDetails) {
                //        case "full":
                //          Console.WriteLine(strUnderline);
                //          Console.WriteLine(strUdfName);
                //          Console.WriteLine(strUnderline);
                //          if (!oUdf.Encrypted) {
                //            Console.WriteLine(oUdf.Text);
                //          } else {
                //            Console.WriteLine("-- *** unable to display encrypted user defined function ***");
                //          }
                //          Console.WriteLine("-- ===[EOF]===");
                //          Console.WriteLine();
                //          break;
                //        case "list":
                //        case "":
                //        default:
                //          Console.WriteLine(strUdfName);
                //          break;
                //      }
                //    }
                //  }
                //}
                //#endregion

                //#region User defined datatype
                //if (WithUDT) {
                //  foreach (UserDefinedDatatype2 oUdt in oDB.UserDefinedDatatypes) {
                //    if (!UserOnly || (UserOnly && !oDB.SystemObject)) {
                //      string strUdtName = string.Format("-- > UDT : {0}.{1}.{2}", oDB.Name, oDB.UserName, oUdt.Name);
                //      string strUnderline = "-- " + new string('=', strUdtName.Length - 5);
                //      switch (UDTDetails) {
                //        case "full":
                //          Console.WriteLine(strUnderline);
                //          Console.WriteLine(strUdtName);
                //          Console.WriteLine(strUnderline);
                //          Console.WriteLine(oUdt.Script(SQLDMO_SCRIPT_TYPE.SQLDMOScript_Default, mv, SQLDMO_SCRIPT2_TYPE.SQLDMOScript2_Default));
                //          Console.WriteLine("-- ===[EOT]===");
                //          Console.WriteLine();
                //          break;
                //        case "list":
                //        case "":
                //        default:
                //          Console.WriteLine(strUdtName);
                //          break;
                //      }
                //    }
                //  }
                //}
                //#endregion

                //#region Views
                //if (WithViews) {
                //  foreach (View2 oView in oDB.Views) {
                //    if (!UserOnly || (UserOnly && !oView.SystemObject)) {
                //      if (ViewRegEx.Length == 0 || Regex.IsMatch(oView.Name, ViewRegEx, RegexOptions.IgnoreCase)) {
                //        string strViewName = string.Format("-- > View : {0}.{1}.{2}", oDB.Name, oDB.UserName, oView.Name);
                //        string strUnderline = "-- " + new string('=', strViewName.Length - 3);
                //        switch (ViewDetails) {
                //          case "full":
                //            Console.WriteLine("-- #### Database : {0} ####", oDB.Name.TrimEnd());
                //            Console.WriteLine(strUnderline);
                //            Console.WriteLine(strViewName);
                //            Console.WriteLine(strUnderline);
                //            if (!oView.Encrypted) {
                //              Console.WriteLine(oView.Text);
                //            } else {
                //              Console.WriteLine("-- *** unable to display encrypted views ***");
                //            }
                //            Console.WriteLine("-- ===[EOV]===");
                //            Console.WriteLine();
                //            break;
                //          case "list":
                //          case "":
                //          default:
                //            Console.WriteLine(strViewName);
                //            break;
                //        }

                //        if (WithTriggers) {
                //          string strTriggerName;
                //          string strUnderTrigger;
                //          switch (TriggerDetails) {
                //            case "full":
                //              if (oSql.VersionMajor >= 8) {
                //                foreach (Trigger2 oTrigger in oView.Triggers) {
                //                  strTriggerName = string.Format("-- > > Trigger : {0}.{1}.{2}", oDB.Name, oDB.UserName, oTrigger.Name);
                //                  strUnderTrigger = "-- > > " + new string('=', strTriggerName.Length - 7);
                //                  Console.WriteLine(strUnderTrigger);
                //                  Console.WriteLine(strTriggerName);
                //                  Console.WriteLine(strUnderTrigger);
                //                  Console.WriteLine(oTrigger.Text);
                //                  Console.WriteLine("-- ===[EOT]===");
                //                  Console.WriteLine();
                //                }
                //              }
                //              break;
                //            case "list":
                //            case "":
                //              if (oSql.VersionMajor >= 8) {
                //                foreach (Trigger2 oTrigger in oView.Triggers) {
                //                  strTriggerName = string.Format("-- > > Trigger : {0}.{1}.{2}", oDB.Name, oDB.UserName, oTrigger.Name);
                //                  Console.WriteLine(strTriggerName);
                //                }
                //              }
                //              break;
                //          }
                //        }
                //      }
                //    }
                //  }
                //}
                //#endregion
                **/

              }
            }

          }

          /**
          //#region Jobs
          //if (WithJobs) {
          //  #region timegrid header
          //  if (JobDetails == "timegrid") {
          //    Console.WriteLine("<HTML><HEAD><style>body {font-family: arial; font-size: 8pt }");
          //    Console.WriteLine(" TABLE {font-size: 8pt}");
          //    Console.WriteLine(" .daily {background: lightblue}");
          //    Console.WriteLine(" .weekly {background: lightgreen}");
          //    Console.WriteLine(" .monthly {background: yellow}");
          //    Console.WriteLine(" .other {background: lightgrey}");
          //    Console.WriteLine("</STYLE></HEAD><BODY>");
          //    Console.WriteLine("<TABLE WIDTH='95%' ALIGN='center' border='1' cellspacing='0'>");
          //    Console.Write("<THEAD><TR><TD WIDTH='15%' ALIGN='center'>Job</TD>");
          //    for (int i = 0; i < 24; i++) {
          //      Console.Write("<TD ColSpan='4' align='center'>{0}</TD>", i);
          //    }
          //    Console.WriteLine("</TR></THEAD>");
          //  }
          //  #endregion

          //  if (JobDetails == "documentation") {
          //    oXmlTextWriter.WriteStartElement("jobs");
          //  }

          //  // obtains the jobs list sorted by name
          //  Job[] aJobs = new Job[oSql.JobServer.Jobs.Count];
          //  string[] aJobsName = new string[oSql.JobServer.Jobs.Count];
          //  for (int iJob = 1; iJob <= oSql.JobServer.Jobs.Count; iJob++) {
          //    aJobs[iJob - 1] = oSql.JobServer.Jobs.Item(iJob);
          //    aJobsName[iJob - 1] = oSql.JobServer.Jobs.Item(iJob).Name;
          //  }
          //  Array.Sort(aJobsName, aJobs);

          //  // browse the jobs list
          //  foreach (Job oJob in aJobs) {
          //    bool bRegEx = true;
          //    try {
          //      bRegEx = Regex.IsMatch(oJob.Name, JobRegEx, RegexOptions.IgnoreCase);
          //    } catch {
          //      bRegEx = false;
          //    }
          //    if (JobRegEx.Length == 0 || bRegEx) {
          //      if (((JobStatusList.Count == 0) || JobStatusList.Contains("all"))
          //        || (JobStatusList.Contains("failed") && (oJob.LastRunOutcome == SQLDMO_JOBOUTCOME_TYPE.SQLDMOJobOutcome_Failed))
          //        || (JobStatusList.Contains("succeeded") && (oJob.LastRunOutcome == SQLDMO_JOBOUTCOME_TYPE.SQLDMOJobOutcome_Succeeded))
          //        || (JobStatusList.Contains("cancelled") && (oJob.LastRunOutcome == SQLDMO_JOBOUTCOME_TYPE.SQLDMOJobOutcome_Cancelled))
          //        || (JobStatusList.Contains("unknown") && (oJob.LastRunOutcome == SQLDMO_JOBOUTCOME_TYPE.SQLDMOJobOutcome_Unknown))
          //        || (JobStatusList.Contains("inprogress") && (oJob.LastRunOutcome == SQLDMO_JOBOUTCOME_TYPE.SQLDMOJobOutcome_InProgress))) {

          //        string strLastRun = SqlUtils.Int2DT(oJob.LastRunDate, oJob.LastRunTime).ToString("dd/MM/yyyy HH:mm:ss");
          //        string strNextRun;
          //        if (!oJob.Enabled) {
          //          strNextRun = "Disabled";
          //        } else {
          //          strNextRun = oJob.HasSchedule ? SqlUtils.Int2DT(oJob.NextRunDate, oJob.NextRunTime).ToString("dd/MM/yyyy HH:mm:ss") : "Not scheduled";
          //        }

          //        string strJobName;
          //        string strUnderline;
          //        TimeSpan LastRunDelay = DateTime.Today - DateTime.Parse(strLastRun);

          //        if ((!SmartList
          //               || (SmartList
          //                    && (!(JobStatusList.Contains("failed")
          //                             && (oJob.LastRunOutcome == SQLDMO_JOBOUTCOME_TYPE.SQLDMOJobOutcome_Failed)
          //                             && (LastRunDelay.Days < 30)
          //                             && (!oJob.HasSchedule)
          //                           )
          //                       )
          //                    && (oJob.Enabled)
          //                  )
          //             )
          //          ) {
          //          #region JobDetails
          //          switch (JobDetails) {
          //            case "create":
          //              #region create
          //              strJobName = string.Format("-- Job : {0} - {1} - {2} - {3} - {4}",
          //                oJob.Name,
          //                strLastRun,
          //                SqlUtils.JobStatus(oJob.LastRunOutcome).PadRight(10, '.'),
          //                strNextRun,
          //                oJob.Owner);
          //              strUnderline = "-- " + new string('=', strJobName.Length - 3);
          //              Console.WriteLine(strUnderline);
          //              Console.WriteLine(strJobName);
          //              Console.WriteLine(strUnderline);
          //              Console.WriteLine(oJob.Script(SQLDMO_SCRIPT_TYPE.SQLDMOScript_Default, mv, SQLDMO_SCRIPT2_TYPE.SQLDMOScript2_Default));
          //              Console.WriteLine("-- ===[EOJ]===");
          //              Console.WriteLine();
          //              break;
          //            #endregion

          //            case "full":
          //              #region full
          //              strJobName = string.Format("Job : {0} - {1} - {2} - {3} - {4}",
          //                oJob.Name,
          //                strLastRun,
          //                SqlUtils.JobStatus(oJob.LastRunOutcome).PadRight(10, '.'),
          //                strNextRun,
          //                oJob.Owner);
          //              strUnderline = new string('=', strJobName.Length);
          //              Console.WriteLine(strUnderline);
          //              Console.WriteLine(strJobName);
          //              Console.WriteLine(strUnderline);
          //              for (int iStep = 1; iStep <= oJob.JobSteps.Count; iStep++) {
          //                JobStep oJobStep = oJob.JobSteps.ItemByID(iStep);
          //                try {
          //                  Console.WriteLine(MakeBlock(string.Format("Step {0} as {1} : {2}{3} : Ok={4}, Fail={5}", oJobStep.StepID, oJobStep.SubSystem, oJobStep.Name, oJobStep.DatabaseName == null ? "" : string.Concat(" (", oJobStep.DatabaseName, ")"), SqlUtils.JobNextAction(oJobStep.OnSuccessAction, oJobStep.OnSuccessStep), SqlUtils.JobNextAction(oJobStep.OnFailAction, oJobStep.OnFailStep)), oJobStep.Command, 2));
          //                } catch {
          //                  Console.WriteLine(MakeBlock(string.Format("Step {0} as {1} : {2} : Ok={3}, Fail={4}", oJobStep.StepID, oJobStep.SubSystem, oJobStep.Name, SqlUtils.JobNextAction(oJobStep.OnSuccessAction, oJobStep.OnSuccessStep), SqlUtils.JobNextAction(oJobStep.OnFailAction, oJobStep.OnFailStep)), oJobStep.Command, 2));
          //                }
          //                Console.WriteLine();
          //              }
          //              Console.WriteLine("===[EOJ]===");
          //              Console.WriteLine();
          //              break;
          //            #endregion

          //            case "properties":
          //              #region properties
          //              strJobName = string.Format("Job : {0} - {1} - {2} - {3} - {4}",
          //                oJob.Name.PadRight(60, '.'),
          //                strLastRun,
          //                SqlUtils.JobStatus(oJob.LastRunOutcome).PadRight(10, '.'),
          //                strNextRun,
          //                oJob.Owner);
          //              Console.WriteLine(strJobName);

          //              for (int iStep = 1; iStep <= oJob.JobSteps.Count; iStep++) {
          //                JobStep oJobStep = oJob.JobSteps.ItemByID(iStep);
          //                if (oJob.JobSteps.Count > 9) {
          //                  Console.Write("-- > Step {0} : {1}", oJobStep.StepID.ToString("00"), oJobStep.SubSystem.PadRight(16, '.'));
          //                } else {
          //                  Console.Write("-- > Step {0} : {1}", oJobStep.StepID, oJobStep.SubSystem.PadRight(16, '.'));
          //                }
          //                try {
          //                  Console.Write(" - {0}", oJobStep.DatabaseName.PadRight(30, '.'));
          //                } catch {
          //                  Console.Write(" - {0}", new string('.', 30));
          //                }
          //                Console.Write(" - {0}", oJobStep.Name);
          //                Console.WriteLine();
          //                try {
          //                  SQLDMO.Properties oProperties = oJobStep.Properties;
          //                  foreach (SQLDMO.Property oProperty in oProperties) {
          //                    Console.Write("{0,-15} | {1} = ", SqlUtils.Int2DataType(oProperty.Type), oProperty.Name);
          //                    try {
          //                      Console.WriteLine("{0}", oProperty.Value);
          //                    } catch {
          //                      Console.WriteLine();
          //                    }
          //                  }
          //                } catch (Exception ex) {
          //                  Console.WriteLine(ex.Message);
          //                  Console.WriteLine(ex.StackTrace);
          //                }
          //              }

          //              Console.WriteLine();
          //              break;
          //            #endregion


          //            case "steps":
          //              #region steps
          //              strJobName = string.Format("Job : {0} - {1} - {2} - {3} - {4}",
          //                oJob.Name.PadRight(60, '.'),
          //                strLastRun,
          //                SqlUtils.JobStatus(oJob.LastRunOutcome).PadRight(10, '.'),
          //                strNextRun,
          //                oJob.Owner);
          //              Console.WriteLine(strJobName);

          //              for (int iStep = 1; iStep <= oJob.JobSteps.Count; iStep++) {
          //                JobStep oJobStep = oJob.JobSteps.ItemByID(iStep);
          //                Console.Write(" > Step {0} : {1}", oJobStep.StepID.ToString("00"), oJobStep.SubSystem.PadRight(16, '.'));
          //                try {
          //                  Console.Write(" - {0}", oJobStep.DatabaseName.PadRight(30, '.'));
          //                } catch {
          //                  Console.Write(" - {0}", new string('.', 30));
          //                }
          //                Console.Write(" - {0}", oJobStep.Name);
          //                Console.WriteLine();
          //              }

          //              Console.WriteLine();
          //              break;
          //            #endregion


          //            case "sendmail":
          //              #region sendmail
          //              for (int iStep = 1; iStep <= oJob.JobSteps.Count; iStep++) {
          //                JobStep oJobStep = oJob.JobSteps.ItemByID(iStep);
          //                if (oJobStep.SubSystem == "TSQL" && oJobStep.Command.ToLower().IndexOf("xp_sendmail") >= 0) {
          //                  foreach (string sItem in SqlUtils.Get_xp_sendmail(oJobStep.Command)) {
          //                    WriteMultipleLines(oJob.Name, 50, oJobStep.Name, 50, sItem, 80);
          //                  }
          //                  Console.WriteLine();
          //                }
          //              }

          //              #endregion
          //              break;

          //            case "documentation":
          //              #region documentation
          //              oXmlTextWriter.WriteStartElement("job");
          //              oXmlTextWriter.WriteElementString("category", oJob.Category);
          //              oXmlTextWriter.WriteElementString("jobName", oJob.Name);
          //              oXmlTextWriter.WriteElementString("description", oJob.Description);
          //              oXmlTextWriter.WriteElementString("owner", oJob.Owner);
          //              oXmlTextWriter.WriteEndElement();
          //              break;
          //            #endregion

          //            case "notification":
          //              #region Notification
          //              Console.Write("{0}", oJob.Name.PadRight(60, '.').Substring(0, 60));
          //              if (oJob.OperatorToEmail != "" && oJob.OperatorToEmail.ToLower() != "(unknown)") {
          //                Console.Write(string.Format(" | Mail to {0}, {1}", oJob.OperatorToEmail, SqlUtils.JobNotificationLevel(oJob.EmailLevel)).PadRight(60, '.'));
          //              } else {
          //                Console.Write(" | No Email operator".PadRight(60, '.'));
          //              }
          //              if (oJob.OperatorToPage != "" && oJob.OperatorToPage.ToLower() != "(unknown)") {
          //                Console.Write(string.Format(" | Page to {0}, {1}", oJob.OperatorToPage, SqlUtils.JobNotificationLevel(oJob.PageLevel)).PadRight(60));
          //              } else {
          //                Console.Write(" | No Pager operator".PadRight(60));
          //              }
          //              Console.WriteLine();
          //              break;
          //            #endregion

          //            case "list":
          //            case "":
          //            default:
          //              #region list
          //              Console.WriteLine("Job : {0} - {1} - {2} - {3} - {4} - {5}",
          //                oJob.Name.PadRight(60, '.').Substring(0, 60),
          //                oJob.Category.PadRight(40, '.'),
          //                strLastRun,
          //                SqlUtils.JobStatus(oJob.LastRunOutcome).PadRight(10, '.'),
          //                strNextRun.PadRight(22, '.'),
          //                oJob.Owner);
          //              break;
          //              #endregion

          //          }

          //          #endregion

          //          #region JobHistory
          //          if (WithJobHistory) {
          //            DataSet oHistory = SqlUtils.QR2DataSet((QueryResults2)oJob.EnumHistory(mv));
          //            bool FirstLine = true;
          //            switch (JobHistoryDetails) {

          //              case "full":
          //                #region JH-Full2
          //                Console.WriteLine();
          //                foreach (DataRow oRow in oHistory.Tables[0].Rows) {
          //                  DateTime dtRunDateTime = SqlUtils.Int2DT((int)oRow["run_date"], (int)oRow["run_time"]);
          //                  if (dtRunDateTime.Date >= DateTime.Today.Date.AddDays(-1) && ((int)oRow["step_id"] != 0)) {
          //                    if (FirstLine) {
          //                      Console.WriteLine();
          //                      FirstLine = false;
          //                    }
          //                    {
          //                      string sStepName;
          //                      if (oRow["step_name"].ToString().Length > 40) {
          //                        sStepName = ((string)oRow["step_name"]).Substring(0, 40) + "...";
          //                      } else {
          //                        sStepName = (string)oRow["step_name"];
          //                      }
          //                      if (SmartList) {
          //                        Console.WriteLine(MakeBlock(string.Format("Step {0,2} | {1} | {2} | {3} secs", ((int)oRow["step_id"]).ToString(), sStepName, dtRunDateTime.ToString("dd/MM/yyyy HH:mm:ss"), ((int)oRow["run_duration"]).ToString()), ((string)oRow["message"]).Replace("  ", "\n").Replace("\n\n", "\n")), '-', 0, 120);
          //                      } else {
          //                        Console.WriteLine(MakeBlock(string.Format("Step {0,2} | {1} | {2} | {3} secs", ((int)oRow["step_id"]).ToString(), sStepName, dtRunDateTime.ToString("dd/MM/yyyy HH:mm:ss"), ((int)oRow["run_duration"]).ToString()), (string)oRow["message"], '-', 0, 120));
          //                      }
          //                      Console.WriteLine("");
          //                    }
          //                  }
          //                }
          //                if (!FirstLine) {
          //                  Console.WriteLine();
          //                }
          //                break;
          //              #endregion

          //              case "":
          //              case "list":
          //              default:
          //                #region JH-List
          //                foreach (DataRow oRow in oHistory.Tables[0].Rows) {
          //                  DateTime dtRunDateTime = SqlUtils.Int2DT((int)oRow["run_date"], (int)oRow["run_time"]);

          //                  if (dtRunDateTime >= DateTime.Today.Date.AddDays(-JobHistoryDays) && ((int)oRow["step_id"] == 0)) {
          //                    if (FirstLine) {
          //                      Console.WriteLine();
          //                      FirstLine = false;
          //                    }
          //                    Console.Write("{0,2} | ", (int)oRow["step_id"]);
          //                    Console.Write("{0} | ", (string)oRow["step_name"]);
          //                    Console.Write("{0} | ", dtRunDateTime.ToString("dd/MM/yyyy HH:mm:ss"));
          //                    Console.Write("{0} | ", SqlUtils.JobStatus((SQLDMO_JOBOUTCOME_TYPE)oRow["run_status"]).PadRight(10, '.'));
          //                    Console.WriteLine("{0,6} secs", (int)oRow["run_duration"]);
          //                  }
          //                }
          //                if (!FirstLine) {
          //                  Console.WriteLine();
          //                }
          //                break;
          //                #endregion

          //            }


          //          }
          //          #endregion
          //        }
          //      }
          //    }
          //  }
          //  if (JobDetails == "documentation") {
          //    oXmlTextWriter.WriteEndElement(); // jobs
          //  }

          //  #region timegrid footer
          //  if (JobDetails == "timegrid") {
          //    Console.WriteLine("</TABLE>");
          //    Console.WriteLine("<BR><table border='0' cellspacing='5'><TR><TD colspan='2'>Legend</TD></TR>");
          //    Console.WriteLine("<TR><TD class='daily' width=30>&nbsp;</TD><TD>Daily</TD></TR>");
          //    Console.WriteLine("<TR><TD class='weekly' width=30>&nbsp;</TD><TD>Weekly</TD></TR>");
          //    Console.WriteLine("<TR><TD class='monthly' width=30>&nbsp;</TD><TD>Monthly</TD></TR>");
          //    Console.WriteLine("<TR><TD class='other' width=30>&nbsp;</TD><TD>Other</TD></TR>");
          //    Console.WriteLine("</TABLE>");
          //    Console.WriteLine("</BODY></HTML>");
          //  }
          //  #endregion
          //}
          //#endregion

          //#region DTS
          //if (WithDTS) {
          //  if (XmlOutputFile.Length != 0) {
          //    oXmlTextWriter.WriteStartElement("dts");
          //  }

          //  DTS.Application oDtsApp = new DTS.ApplicationClass();

          //  #region SQL Server storage
          //  DTS.PackageSQLServer oPackageSqlServer;
          //  if (UserId == "") {
          //    oPackageSqlServer = oDtsApp.GetPackageSQLServer(strServer, "", "", DTS.DTSSQLServerStorageFlags.DTSSQLStgFlag_UseTrustedConnection);
          //  } else {
          //    oPackageSqlServer = oDtsApp.GetPackageSQLServer(strServer, UserId, Password, DTS.DTSSQLServerStorageFlags.DTSSQLStgFlag_Default);
          //  }

          //  DataTable oDtsPackages = SqlUtils.QR2DataTable(oSql.ExecuteWithResults("msdb..sp_enum_dtspackages", System.Type.Missing));
          //  ArrayList aPackages = new ArrayList();
          //  foreach (DataRow oRow in oDtsPackages.Rows) {
          //    aPackages.Add("SQL:" + oRow["name"]);
          //  }
          //  #endregion SQL Server storage

          //  #region META DATA Repositery
          //  
          //      DTS.PackageRepository oPackageRepository;
          //      if (UserId=="") {
          //        oPackageRepository = oDtsApp.GetPackageRepository(strServer,"","","",DTS.DTSRepositoryStorageFlags.DTSReposFlag_UseTrustedConnection);
          //      } else {
          //        oPackageRepository = oDtsApp.GetPackageRepository(strServer,"",UserId,Password,DTS.DTSRepositoryStorageFlags.DTSReposFlag_Default);
          //      }

          //      Repository oRepositery = new RepositoryClass();
          //      if (UserId=="") {
          //        oRepositery.Open("Driver={SQL Server};Server=" + strServer + ";Trusted_Connection=yes;Database=msdb", "", "", 0);
          //      } else {
          //        oRepositery.Open(string.Format("Driver={SQL Server};Server={0};Database=msdb", strServer), UserId, Password, 0);
          //      }
          //      InterfaceDef oInterfaceDef = (InterfaceDef)oRepositery.get_Object(("{{EBB9995C-BA15-11d1-901B-0000F87A3B33},000032CA}"));
          //      ObjectCol oObjectCol = oInterfaceDef.ObjectInstances();
          //      foreach(RepositoryObject oRepositeryObject in oObjectCol) {
          //        aPackages.Add("META:" + oRepositeryObject.Name);
          //      }
          //
          //  #endregion META DATA Repositery

          //  aPackages.Sort();

          //  object pVarPersistStgOfHost = null; // unused, but needed by API

          //  switch (DtsDetails) {
          //    case "list":
          //    case "":
          //    default:
          //      #region list
          //      foreach (string sQualifiedPackage in aPackages) {
          //        if (DtsRegEx.Length == 0 || Regex.IsMatch(sQualifiedPackage, DtsRegEx, RegexOptions.IgnoreCase)) {
          //          string sPackageType = sQualifiedPackage.Split(':')[0];
          //          string sPackage = sQualifiedPackage.Split(':')[1];
          //          if (XmlOutputFile.Length != 0) {
          //            oXmlTextWriter.WriteStartElement("package");
          //            oXmlTextWriter.WriteAttributeString("name", sPackage);
          //          }
          //          DTS.PackageInfos oPackageInfos = null;
          //          switch (sPackageType) {
          //            case "SQL":
          //              oPackageInfos = oPackageSqlServer.EnumPackageInfos(sPackage, true, "");
          //              break;
          //              //case "META":
          //              //  oPackageInfos = oPackageRepository.EnumPackageInfos(sPackage, true, "");
          //              //  break;
          //          }
          //          foreach (DTS.PackageInfo oPackageInfo in oPackageInfos) {
          //            if (XmlOutputFile.Length != 0) {
          //              oXmlTextWriter.WriteElementString("description", oPackageInfo.Description);
          //              oXmlTextWriter.WriteElementString("creation", NoNull(oPackageInfo.CreationDate, ""));
          //              oXmlTextWriter.WriteElementString("packageid", NoNull(oPackageInfo.PackageID, ""));
          //              oXmlTextWriter.WriteElementString("owner", oPackageInfo.Owner);
          //              oXmlTextWriter.WriteElementString("version", oPackageInfo.VersionID);
          //            } else {
          //              Console.Write("{0}", sPackageType.PadRight(4, '.'));
          //              Console.Write(" {0}", oPackageInfo.Name.PadRight(60, '.').Substring(0, 60));
          //              Console.Write(" {0}", NoNull(oPackageInfo.Description, "").PadRight(80, '.').Substring(0, 80));
          //              Console.WriteLine(" {0}", NoNull(oPackageInfo.CreationDate.ToString("yyyy-MM-dd HH:mm:ss"), ""));
          //            }
          //          }
          //          if (XmlOutputFile.Length != 0) {
          //            oXmlTextWriter.WriteEndElement();
          //          }
          //        }
          //      }
          //      break;
          //    #endregion

          //    case "steps":
          //      #region steps
          //      foreach (string sQualifiedPackage in aPackages) {
          //        if (DtsRegEx.Length == 0 || Regex.IsMatch(sQualifiedPackage, DtsRegEx, RegexOptions.IgnoreCase)) {
          //          string sPackageType = sQualifiedPackage.Split(':')[0];
          //          string sPackage = sQualifiedPackage.Split(':')[1];
          //          Console.WriteLine(new string('-', 120));
          //          Console.WriteLine(MakeTitle(string.Format("DTS Package : {0}", sQualifiedPackage), 120, '#'));
          //          Console.WriteLine(new string('-', 120));

          //          DTS.Package2 oPackage = new DTS.Package2Class();
          //          switch (sPackageType) {
          //            case "SQL":
          //              try {
          //                if (UserId == "") {
          //                  oPackage.LoadFromSQLServer(strServer, "", "", DTSSQLServerStorageFlags.DTSSQLStgFlag_UseTrustedConnection, "", "", "", sPackage, ref pVarPersistStgOfHost);
          //                } else {
          //                  oPackage.LoadFromSQLServer(strServer, UserId, Password, DTSSQLServerStorageFlags.DTSSQLStgFlag_Default, "", "", "", sPackage, ref pVarPersistStgOfHost);
          //                }
          //              } catch (Exception ex) {
          //                Console.WriteLine(ex.Message);
          //              }
          //              break;

          //            case "META":
          //              try {
          //                if (UserId == "") {
          //                  oPackage.LoadFromRepository(strServer, "", "", "", "", "", sPackage, DTSRepositoryStorageFlags.DTSReposFlag_UseTrustedConnection, ref pVarPersistStgOfHost);
          //                } else {
          //                  oPackage.LoadFromRepository(strServer, "", UserId, Password, "", "", sPackage, DTSRepositoryStorageFlags.DTSReposFlag_UseTrustedConnection, ref pVarPersistStgOfHost);
          //                }
          //              } catch (Exception ex) {
          //                Console.WriteLine(ex.Message);
          //              }
          //              break;
          //          }
          //          try {

          //            if (oPackage.Steps.Count > 0) {
          //              foreach (DTS.Step2 oStep in oPackage.Steps) {
          //                if (oPackage.Steps.Count > 1 && oStep.PrecedenceConstraints.Count > 0) {
          //                  Console.WriteLine("   Preceding : {0}", oStep.PrecedenceConstraints.Item(1).StepName);
          //                }
          //                Console.WriteLine("-- > Step : {0}", oStep.Name);
          //                Console.WriteLine("");
          //              }
          //            }
          //            System.Runtime.InteropServices.Marshal.ReleaseComObject(oPackage);
          //          } catch (Exception ex) {
          //            Console.WriteLine(ex.Message);
          //          }
          //          Console.WriteLine("");
          //        }
          //      }
          //      break;
          //    #endregion

          //    case "full":
          //      #region full

          //      foreach (string sQualifiedPackage in aPackages) {
          //        if (DtsRegEx.Length == 0 || Regex.IsMatch(sQualifiedPackage, DtsRegEx, RegexOptions.IgnoreCase)) {
          //          string sPackageType = sQualifiedPackage.Split(':')[0];
          //          string sPackage = sQualifiedPackage.Split(':')[1];
          //          Console.WriteLine(new string('-', 120));
          //          Console.WriteLine(MakeTitle(string.Format("DTS Package : {0}", sQualifiedPackage), 120, '#'));
          //          Console.WriteLine(new string('-', 120));
          //          DTS.Package2 oPackage = new DTS.Package2Class();
          //          switch (sPackageType) {
          //            case "SQL":
          //              try {
          //                if (UserId == "") {
          //                  oPackage.LoadFromSQLServer(strServer, "", "", DTSSQLServerStorageFlags.DTSSQLStgFlag_UseTrustedConnection, "", "", "", sPackage, ref pVarPersistStgOfHost);
          //                } else {
          //                  oPackage.LoadFromSQLServer(strServer, UserId, Password, DTSSQLServerStorageFlags.DTSSQLStgFlag_Default, "", "", "", sPackage, ref pVarPersistStgOfHost);
          //                }
          //              } catch (Exception ex) {
          //                Console.WriteLine(ex.Message);
          //              }
          //              break;

          //            case "META":
          //              try {
          //                if (UserId == "") {
          //                  oPackage.LoadFromRepository(strServer, "", "", "", "", "", sPackage, DTSRepositoryStorageFlags.DTSReposFlag_UseTrustedConnection, ref pVarPersistStgOfHost);
          //                } else {
          //                  oPackage.LoadFromRepository(strServer, "", UserId, Password, "", "", sPackage, DTSRepositoryStorageFlags.DTSReposFlag_UseTrustedConnection, ref pVarPersistStgOfHost);
          //                }
          //              } catch (Exception ex) {
          //                Console.WriteLine(ex.Message);
          //              }
          //              break;
          //          }

          //          Hashtable hConnections = new Hashtable();
          //          foreach (DTS.Connection2 oConnection in oPackage.Connections) {
          //            hConnections.Add(oConnection.ID, oConnection.Name);
          //          }

          //          try {
          //            #region Display info about package
          //            Console.WriteLine("Description: {0}", oPackage.Description);
          //            Console.WriteLine("Created with computer: {0}", oPackage.CreatorComputerName);
          //            Console.WriteLine("Created by: {0}", oPackage.CreatorName);
          //            Console.WriteLine("Creation date : {0}", oPackage.CreationDate);
          //            Console.WriteLine("Max concurrent steps : {0}", oPackage.MaxConcurrentSteps);
          //            if (oPackage.LogToSQLServer) {
          //              Console.Write("Log to SQL server : {0}", oPackage.LogServerName);
          //              if (oPackage.LogServerUserName == null) {
          //                Console.WriteLine(" using Integrated Authentication");
          //              } else {
          //                Console.WriteLine(" using username {0}, password {1}", oPackage.LogServerUserName, oPackage.LogServerPassword);
          //              }
          //            } else {
          //              if (oPackage.LogFileName == null) {
          //                Console.WriteLine("No logging enabled");
          //              } else {
          //                Console.WriteLine("Log to filename : {0}", oPackage.LogFileName);
          //              }
          //            }
          //            if (oPackage.UseTransaction) {
          //              Console.WriteLine("Use transaction, mode {0}{1}", oPackage.TransactionIsolationLevel.ToString(), oPackage.AutoCommitTransaction ? ", with auto-commit" : ", no auto-commit");
          //            } else {
          //              Console.WriteLine("No transaction");
          //            }
          //            #endregion

          //            Console.WriteLine("");

          //            #region Global variables
          //            if (oPackage.GlobalVariables.Count > 0) {
          //              StringBuilder oSb = new StringBuilder();
          //              SortedList oSortedList = new SortedList();
          //              foreach (DTS.GlobalVariable2 oVariable in oPackage.GlobalVariables) {
          //                oSortedList.Add(oVariable.Name, oVariable.Value);
          //              }
          //              foreach (string sValue in oSortedList.Keys) {
          //                oSb.AppendFormat("{0} = {1}\n", sValue, oSortedList[sValue]);
          //              }
          //              Console.WriteLine(MakeBlock("Global Variables", oSb.ToString(), 4));
          //              Console.WriteLine();
          //            }
          //            #endregion

          //            #region Connections
          //            if (oPackage.Connections.Count > 0) {
          //              StringBuilder oSb = new StringBuilder();
          //              foreach (DTS.Connection2 oConnection in oPackage.Connections) {
          //                if (oConnection.Catalog != null) {
          //                  oSb.AppendFormat("{0} = {1} ({2}, {3})\n", oConnection.Name, oConnection.DataSource, oConnection.ProviderID, oConnection.Catalog);
          //                } else {
          //                  oSb.AppendFormat("{0} = {1} ({2})\n", oConnection.Name, oConnection.DataSource, oConnection.ProviderID);
          //                }
          //              }
          //              Console.WriteLine(MakeBlock("Available connections", oSb.ToString(), 4));
          //              Console.WriteLine();
          //            }
          //            #endregion

          //            if (oPackage.Steps.Count > 0) {
          //              foreach (DTS.Step2 oStep in oPackage.Steps) {
          //                Console.WriteLine("--> Step : {0}", oStep.Name);
          //                Console.WriteLine("--> Desc.: {0}", oStep.Description);
          //                foreach (DTS.Task oTask in oPackage.Tasks) {
          //                  #region Display info about task
          //                  if (oTask.Name != null && oTask.Name == oStep.TaskName) {
          //                    Console.WriteLine("--> Type : {0}", oTask.CustomTaskID);
          //                    Console.WriteLine("");
          //                    switch (oTask.CustomTaskID) {

          //                      case "DTSExecuteSQLTask":
          //                        #region DTSExecuteSQLTask
          //                        DTS.ExecuteSQLTask2 oDTSExecuteSQLTask = (DTS.ExecuteSQLTask2)oTask.CustomTask;
          //                        Console.WriteLine("    Connection = {0}", hConnections[oDTSExecuteSQLTask.ConnectionID].ToString());
          //                        Console.WriteLine("    Output the result as recordset = {0}", oDTSExecuteSQLTask.OutputAsRecordset ? "Yes" : "No");
          //                        Console.WriteLine("    Input global variables names = {0}", NoNull(oDTSExecuteSQLTask.InputGlobalVariableNames, "(none)"));
          //                        Console.WriteLine("    Output global variables names = {0}", NoNull(oDTSExecuteSQLTask.OutputGlobalVariableNames, "(none)"));
          //                        Console.WriteLine("    Timeout = {0}", oDTSExecuteSQLTask.CommandTimeout);
          //                        Console.WriteLine("");
          //                        Console.WriteLine(MakeBlock("TSQL Code", oDTSExecuteSQLTask.SQLStatement, 4));
          //                        break;
          //                      #endregion

          //                      case "DTSTransferObjectsTask":
          //                        #region DTSTransferObjectsTask
          //                        DTS.TransferObjectsTask2 oDTSTransferObjectsTask = (DTS.TransferObjectsTask2)oTask.CustomTask;
          //                        Console.WriteLine("    Source server = {0}", oDTSTransferObjectsTask.SourceServer);
          //                        Console.WriteLine("    Source database = {0}", oDTSTransferObjectsTask.SourceDatabase);
          //                        Console.WriteLine("    Source login : {0}", oDTSTransferObjectsTask.SourceUseTrustedConnection ? "Trusted connection" : oDTSTransferObjectsTask.SourceLogin);
          //                        Console.WriteLine("    Destination server = {0}", oDTSTransferObjectsTask.DestinationServer);
          //                        Console.WriteLine("    Destination database = {0}", oDTSTransferObjectsTask.DestinationDatabase);
          //                        Console.WriteLine("    Destination login : {0}", oDTSTransferObjectsTask.DestinationUseTrustedConnection ? "Trusted connection" : oDTSTransferObjectsTask.DestinationLogin);
          //                        Console.WriteLine("    Script file directory: {0}", oDTSTransferObjectsTask.ScriptFileDirectory);
          //                        break;
          //                      #endregion

          //                      case "DTSDataPumpTask":
          //                        #region DTSDataPumpTask
          //                        DTS.DataPumpTask2 oDataPumpTask = (DTS.DataPumpTask2)oTask.CustomTask;
          //                        Console.WriteLine("    Source connection = {0}", hConnections[oDataPumpTask.SourceConnectionID].ToString());
          //                        if (oDataPumpTask.SourceObjectName != null) {
          //                          Console.WriteLine("    Source Object = {0}", oDataPumpTask.SourceObjectName);
          //                        } else {
          //                          Console.WriteLine(MakeBlock("Source SQL statment", oDataPumpTask.SourceSQLStatement, 4));
          //                        }

          //                        Console.WriteLine("    Destination connection = {0}", hConnections[oDataPumpTask.DestinationConnectionID].ToString());
          //                        if (oDataPumpTask.DestinationObjectName != null) {
          //                          Console.WriteLine("    Destination Object = {0}", oDataPumpTask.DestinationObjectName);
          //                        } else {
          //                          Console.WriteLine(MakeBlock("Destination SQL statment", oDataPumpTask.DestinationSQLStatement, 4));
          //                        }
          //                        break;
          //                      #endregion

          //                      case "DTSDynamicPropertiesTask":
          //                        #region DTSDynamicPropertiesTask
          //                        DynamicPropertiesTask oDynamicPropertiesTask = (DynamicPropertiesTask)oTask.CustomTask;
          //                        foreach (DynamicPropertiesTaskAssignment oDPTA in oDynamicPropertiesTask.Assignments()) {
          //                          Console.WriteLine("    Destination property = {0}", oDPTA.DestinationPropertyID);
          //                          switch ((DynamicPropertiesTaskSourceType)oDPTA.SourceType) {
          //                            case DynamicPropertiesTaskSourceType.DTSDynamicPropertiesSourceType_IniFile:
          //                              Console.WriteLine("    Source file = {0}, section = {1}, key = {2}", oDPTA.SourceIniFileFileName, oDPTA.SourceIniFileSection, oDPTA.SourceIniFileKey);
          //                              break;
          //                            case DynamicPropertiesTaskSourceType.DTSDynamicPropertiesSourceType_DataFile:
          //                              Console.WriteLine("    Source file = {0}", oDPTA.SourceDataFileFileName);
          //                              break;
          //                            case DynamicPropertiesTaskSourceType.DTSDynamicPropertiesSourceType_EnvironmentVariable:
          //                              Console.WriteLine("    Source environment variable = {0}", oDPTA.SourceEnvironmentVariable);
          //                              break;
          //                            case DynamicPropertiesTaskSourceType.DTSDynamicPropertiesSourceType_GlobalVariable:
          //                              Console.WriteLine("    Source global variable = {0}", oDPTA.SourceGlobalVariable);
          //                              break;
          //                            case DynamicPropertiesTaskSourceType.DTSDynamicPropertiesSourceType_Constant:
          //                              Console.WriteLine("    Source constant value = {0}", oDPTA.SourceConstantValue);
          //                              break;
          //                            case DynamicPropertiesTaskSourceType.DTSDynamicPropertiesSourceType_Query:
          //                              Console.WriteLine("    Source Query Connection = {0}", hConnections[oDPTA.SourceQueryConnectionID].ToString());
          //                              Console.WriteLine("    Source Query SQL = {0}", oDPTA.SourceQuerySQL);
          //                              break;
          //                          }
          //                        }
          //                        break;
          //                      #endregion

          //                      case "DTSActiveScriptTask":
          //                        #region DTSActiveScriptTask
          //                        DTS.ActiveScriptTask oDTSActiveScriptTask = (DTS.ActiveScriptTask)oTask.CustomTask;
          //                        Console.WriteLine(MakeBlock("ActiveX code", oDTSActiveScriptTask.ActiveXScript, 4));
          //                        break;
          //                      #endregion

          //                      case "DTSBulkInsertTask":
          //                        #region DTSBulkInsertTask
          //                        DTS.BulkInsertTask oBulkInsertTask = (DTS.BulkInsertTask)oTask.CustomTask;
          //                        Console.WriteLine("    Source datafile = {0}", oBulkInsertTask.DataFile);
          //                        Console.WriteLine("    Connection = {0}", oBulkInsertTask.ConnectionID);
          //                        Console.WriteLine("    Destination table = {0}", oBulkInsertTask.DestinationTableName);
          //                        break;
          //                      #endregion

          //                      case "DTSSendMailTask":
          //                        #region DTSSendMailTask
          //                        DTS.SendMailTask oSendMailTask = (DTS.SendMailTask)oTask.CustomTask;
          //                        Console.WriteLine("    Message sent to: {0}", oSendMailTask.ToLine);
          //                        Console.WriteLine("    Subject: {0}", NoNull(oSendMailTask.Subject, "(none)"));
          //                        if (oSendMailTask.MessageText != null) {
          //                          Console.WriteLine(MakeBlock("Body", oSendMailTask.MessageText, 4));
          //                        }
          //                        Console.WriteLine("    Attachments: {0}", NoNull(oSendMailTask.FileAttachments, "(none)"));
          //                        break;
          //                      #endregion

          //                      case "DTSDataDrivenQueryTask":
          //                        #region DTSDataDrivenQueryTask
          //                        StringBuilder oSb;
          //                        DTS.DataDrivenQueryTask2 oDataDrivenQueryTask = (DTS.DataDrivenQueryTask2)oTask.CustomTask;
          //                        Console.WriteLine("    Source Connection = {0}", hConnections[oDataDrivenQueryTask.SourceConnectionID].ToString());
          //                        if (oDataDrivenQueryTask.SourceObjectName != null) {
          //                          Console.WriteLine("    Source object name = {0}", oDataDrivenQueryTask.SourceObjectName);
          //                        } else {
          //                          Console.WriteLine(MakeBlock("Source SQL Statement", oDataDrivenQueryTask.SourceSQLStatement, 4));
          //                        }
          //                        Console.WriteLine("    Destination Connection = {0}", hConnections[oDataDrivenQueryTask.DestinationConnectionID].ToString());
          //                        if (oDataDrivenQueryTask.DestinationObjectName != null) {
          //                          Console.WriteLine("    Destination object name = {0}", oDataDrivenQueryTask.DestinationObjectName);
          //                        } else {
          //                          Console.WriteLine(MakeBlock("Destination SQL Statement", oDataDrivenQueryTask.DestinationSQLStatement, 4));
          //                        }
          //                        Console.WriteLine();

          //                        #region QUERIES
          //                        oSb = new StringBuilder();
          //                        if (oDataDrivenQueryTask.UserQuery != null) {
          //                          oSb.Append("\n" + MakeBlock("Select", oDataDrivenQueryTask.UserQuery) + "\n");
          //                          StringBuilder oSbUserColumns = new StringBuilder();
          //                          DTS.Columns oColumns = oDataDrivenQueryTask.UserQueryColumns;
          //                          foreach (DTS.Column oColumn in oColumns) {
          //                            oSbUserColumns.AppendFormat("{0,2}", oColumn.Ordinal);
          //                            oSbUserColumns.AppendFormat(" {0}", oColumn.Name);
          //                            oSbUserColumns.Append("\n");
          //                          }
          //                          oSb.Append("\n" + MakeBlock("Select query columns", oSbUserColumns.ToString(), '.', 2, 40) + "\n");
          //                        }

          //                        if (oDataDrivenQueryTask.DeleteQuery != null) {
          //                          oSb.Append("\n" + MakeBlock("Delete", oDataDrivenQueryTask.DeleteQuery) + "\n");
          //                          StringBuilder oSbDeleteColumns = new StringBuilder();
          //                          DTS.Columns oColumns = oDataDrivenQueryTask.DeleteQueryColumns;
          //                          foreach (DTS.Column oColumn in oColumns) {
          //                            oSbDeleteColumns.AppendFormat("{0,2}", oColumn.Ordinal);
          //                            oSbDeleteColumns.AppendFormat(" {0}", oColumn.Name);
          //                            oSbDeleteColumns.Append("\n");
          //                          }
          //                          oSb.Append("\n" + MakeBlock("Delete query columns", oSbDeleteColumns.ToString(), '.', 2, 40) + "\n");
          //                        }

          //                        if (oDataDrivenQueryTask.InsertQuery != null) {
          //                          oSb.Append("\n" + MakeBlock("Insert", oDataDrivenQueryTask.InsertQuery) + "\n");
          //                          StringBuilder oSbInsertColumns = new StringBuilder();
          //                          DTS.Columns oColumns = oDataDrivenQueryTask.InsertQueryColumns;
          //                          foreach (DTS.Column oColumn in oColumns) {
          //                            oSbInsertColumns.AppendFormat("{0,2}", oColumn.Ordinal);
          //                            oSbInsertColumns.AppendFormat(" {0}", oColumn.Name);
          //                            oSbInsertColumns.Append("\n");
          //                          }
          //                          oSb.Append("\n" + MakeBlock("Insert query columns", oSbInsertColumns.ToString(), '.', 2, 40) + "\n");
          //                        }

          //                        if (oDataDrivenQueryTask.UpdateQuery != null) {
          //                          oSb.Append("\n" + MakeBlock("Update", oDataDrivenQueryTask.UpdateQuery) + "\n");
          //                          StringBuilder oSbUpdateColumns = new StringBuilder();
          //                          DTS.Columns oColumns = oDataDrivenQueryTask.UpdateQueryColumns;
          //                          foreach (DTS.Column oColumn in oColumns) {
          //                            oSbUpdateColumns.AppendFormat("{0,2}", oColumn.Ordinal);
          //                            oSbUpdateColumns.AppendFormat(" {0}", oColumn.Name);
          //                            oSbUpdateColumns.Append("\n");
          //                          }
          //                          oSb.Append("\n" + MakeBlock("Update query columns", oSbUpdateColumns.ToString(), '.', 2, 40) + "\n");
          //                        }
          //                        Console.WriteLine(MakeBlock("QUERIES", oSb.ToString(), 4));
          //                        Console.WriteLine();
          //                        #endregion

          //                        DTS.Transformations oTransformations = oDataDrivenQueryTask.Transformations;
          //                        oSb = new StringBuilder();
          //                        foreach (DTS.Transformation2 oTransformation in oTransformations) {
          //                          switch (oTransformation.TransformServerID) {
          //                            case "DTS.DataPumpTransformScript.1":
          //                              DTSPump.DataPumpTransformScript oDataPumpTransformScript = (DTSPump.DataPumpTransformScript)oTransformation.TransformServer;
          //                              oSb.Append("\n" + MakeBlock("Data pump Transform script", oDataPumpTransformScript.Text, 2) + "\n");
          //                              break;

          //                            default:
          //                              oSb.Append("\n******************* unmanaged: " + oTransformation.TransformServerID + "********************\n");
          //                              break;
          //                          }

          //                          StringBuilder oSbSourceColumns = new StringBuilder();
          //                          DTS.Columns oSourceColumns = oTransformation.SourceColumns;
          //                          foreach (DTS.Column oColumn in oSourceColumns) {
          //                            oSbSourceColumns.AppendFormat("{0,2}", oColumn.Ordinal);
          //                            oSbSourceColumns.AppendFormat(" {0}", oColumn.Name.PadRight(30, '.'));
          //                            oSbSourceColumns.AppendFormat(" {0}", SqlUtils.Int2DataType(oColumn.DataType).PadRight(20, '.'));
          //                            oSbSourceColumns.AppendFormat(" {0}", oColumn.Nullable ? "NULL" : "NON NULL");
          //                            oSbSourceColumns.Append("\n");
          //                          }
          //                          oSb.Append("\n" + MakeBlock("Source Columns", oSbSourceColumns.ToString(), 2) + "\n");

          //                          StringBuilder oSbDestinationColumns = new StringBuilder();
          //                          DTS.Columns oDestinationColumns = oTransformation.DestinationColumns;
          //                          foreach (DTS.Column oColumn in oDestinationColumns) {
          //                            oSbDestinationColumns.AppendFormat("{0,2}", oColumn.Ordinal);
          //                            oSbDestinationColumns.AppendFormat(" {0}", oColumn.Name.PadRight(30, '.'));
          //                            oSbDestinationColumns.AppendFormat(" {0}", SqlUtils.Int2DataType(oColumn.DataType).PadRight(20, '.'));
          //                            oSbDestinationColumns.AppendFormat(" {0}", oColumn.Nullable ? "NULL" : "NON NULL");
          //                            oSbDestinationColumns.Append("\n");
          //                          }
          //                          oSb.Append("\n" + MakeBlock("Destination Columns", oSbDestinationColumns.ToString(), 2) + "\n");

          //                          Console.WriteLine(MakeBlock("Transformation " + oTransformation.Name, oSb.ToString(), 4));
          //                          Console.WriteLine();
          //                        }


          //                        break;
          //                      #endregion

          //                      case "DTSCreateProcessTask":
          //                        #region DTSCreateProcessTask
          //                        DTS.CreateProcessTask2 oCreateProcessTask = (DTS.CreateProcessTask2)oTask.CustomTask;
          //                        Console.WriteLine("    Command line = {0}", oCreateProcessTask.ProcessCommandLine);
          //                        if (oCreateProcessTask.ProcessCommandLine.IndexOf("%") >= 0) {
          //                          Console.WriteLine("    Expanded = {0}", oCreateProcessTask.GetExpandedProcessCommandLine());
          //                        }
          //                        Console.WriteLine("    Timeout = {0} secs", oCreateProcessTask.Timeout);
          //                        Console.WriteLine("    Success code = {0}", oCreateProcessTask.SuccessReturnCode);
          //                        break;
          //                      #endregion

          //                      case "DTSExecutePackageTask":
          //                        #region DTSExecutePackageTask
          //                        DTS.ExecutePackageTask oExecutePackageTask = (DTS.ExecutePackageTask)oTask.CustomTask;
          //                        Console.WriteLine("    Package to execute = {0}", oExecutePackageTask.PackageName);
          //                        if (oExecutePackageTask.FileName != null) {
          //                          Console.WriteLine("    Filename = {0}", oExecutePackageTask.FileName);
          //                        } else {
          //                          Console.WriteLine("    Server = {0}", oExecutePackageTask.ServerName);
          //                          if (oExecutePackageTask.UseTrustedConnection) {
          //                            Console.WriteLine("    Use trusted connection");
          //                          } else {
          //                            Console.WriteLine("    Username = {0}", oExecutePackageTask.ServerUserName);
          //                            //Console.WriteLine("    Password = {0}", oExecutePackageTask.PackagePassword);
          //                          }
          //                        }
          //                        if (oExecutePackageTask.UseRepository) {
          //                          Console.WriteLine("    Repository database name = {0}", oExecutePackageTask.RepositoryDatabaseName);
          //                        }
          //                        break;
          //                      #endregion

          //                      case "DTSOlapProcess.Task":
          //                        #region DTSOlapProcess.Task
          //                        DTSOlapProcess.TaskClass oDTSOlapProcessTask = (DTSOlapProcess.TaskClass)oTask.CustomTask;
          //                        Console.WriteLine("    Incrementally update dimensions = {0}", oDTSOlapProcessTask.IncrementallyUpdateDimensions ? "Yes" : "No");
          //                        DTS.Properties oDTSOlapProcessTaskProperties = oDTSOlapProcessTask.Properties;
          //                        string[] aProperties = new string[] { "TreeKey", "FactTable", "TrainingQuery", "DataSource", "Filter" };
          //                        foreach (string sProperty in aProperties) {
          //                          try {
          //                            if (oDTSOlapProcessTaskProperties.Item(sProperty).Value.ToString().Length != 0) {
          //                              Console.WriteLine("    {0} = {1}", sProperty, oDTSOlapProcessTaskProperties.Item(sProperty).Value.ToString());
          //                            }
          //                          } catch { }
          //                        }
          //                        try {
          //                          if ((short)oDTSOlapProcessTaskProperties.Item("ItemType").Value != 0) {
          //                            Console.WriteLine("    ItemType = {0}: {1}", (short)oDTSOlapProcessTaskProperties.Item("ItemType").Value, SqlUtils.OlapItemType((short)oDTSOlapProcessTaskProperties.Item("ItemType").Value));
          //                          }
          //                        } catch { }
          //                        try {
          //                          if ((short)oDTSOlapProcessTaskProperties.Item("ProcessOption").Value != 0) {
          //                            Console.WriteLine("    ItemType = {0}", (short)oDTSOlapProcessTaskProperties.Item("ProcessOption").Value);
          //                          }
          //                        } catch { }

          //                        break;
          //                      #endregion

          //                      default:
          //                        Console.WriteLine("******************* unmanaged: " + oTask.CustomTaskID + "********************");
          //                        break;
          //                    }
          //                  }
          //                  #endregion
          //                }
          //                Console.WriteLine("");
          //              }
          //            }
          //            System.Runtime.InteropServices.Marshal.ReleaseComObject(oPackage);
          //          } catch (Exception ex) {
          //            Console.WriteLine(ex.StackTrace);
          //            Console.WriteLine(ex.Message);
          //          }
          //          Console.WriteLine("");
          //        }
          //      }
          //      break;
          //    #endregion

          //    case "sendmail":
          //      #region SendMailTasks only
          //      foreach (string sQualifiedPackage in aPackages) {
          //        if (DtsRegEx.Length == 0 || Regex.IsMatch(sQualifiedPackage, DtsRegEx, RegexOptions.IgnoreCase)) {
          //          #region Create oPackage
          //          string sPackageType = sQualifiedPackage.Split(':')[0];
          //          string sPackage = sQualifiedPackage.Split(':')[1];
          //          DTS.Package2 oPackage = new DTS.Package2Class();
          //          switch (sPackageType) {
          //            case "SQL":
          //              try {
          //                if (UserId == "") {
          //                  oPackage.LoadFromSQLServer(strServer, "", "", DTSSQLServerStorageFlags.DTSSQLStgFlag_UseTrustedConnection, "", "", "", sPackage, ref pVarPersistStgOfHost);
          //                } else {
          //                  oPackage.LoadFromSQLServer(strServer, UserId, Password, DTSSQLServerStorageFlags.DTSSQLStgFlag_Default, "", "", "", sPackage, ref pVarPersistStgOfHost);
          //                }
          //              } catch (Exception ex) {
          //                Console.WriteLine(ex.Message);
          //              }
          //              break;

          //            case "META":
          //              try {
          //                if (UserId == "") {
          //                  oPackage.LoadFromRepository(strServer, "", "", "", "", "", sPackage, DTSRepositoryStorageFlags.DTSReposFlag_UseTrustedConnection, ref pVarPersistStgOfHost);
          //                } else {
          //                  oPackage.LoadFromRepository(strServer, "", UserId, Password, "", "", sPackage, DTSRepositoryStorageFlags.DTSReposFlag_UseTrustedConnection, ref pVarPersistStgOfHost);
          //                }
          //              } catch (Exception ex) {
          //                Console.WriteLine(ex.Message);
          //              }
          //              break;
          //          }
          //          #endregion Create oPackage

          //          Hashtable hConnections = new Hashtable();
          //          foreach (DTS.Connection2 oConnection in oPackage.Connections) {
          //            hConnections.Add(oConnection.ID, oConnection.Name);
          //          }

          //          try {
          //            #region Display info about package
          //            Console.WriteLine(new string('-', 120));
          //            Console.WriteLine(MakeTitle(string.Format("DTS Package : {0}", sQualifiedPackage), 120, '#'));
          //            Console.WriteLine(new string('-', 120));
          //            Console.WriteLine("Description: {0}", oPackage.Description);
          //            Console.WriteLine("Created with computer: {0}", oPackage.CreatorComputerName);
          //            Console.WriteLine("Created by: {0}", oPackage.CreatorName);
          //            Console.WriteLine("Creation date : {0}", oPackage.CreationDate);
          //            Console.WriteLine("Max concurrent steps : {0}", oPackage.MaxConcurrentSteps);
          //            if (oPackage.LogToSQLServer) {
          //              Console.Write("Log to SQL server : {0}", oPackage.LogServerName);
          //              if (oPackage.LogServerUserName == null) {
          //                Console.WriteLine(" using Integrated Authentication");
          //              } else {
          //                Console.WriteLine(" using username {0}, password {1}", oPackage.LogServerUserName, oPackage.LogServerPassword);
          //              }
          //            } else {
          //              if (oPackage.LogFileName == null) {
          //                Console.WriteLine("No logging enabled");
          //              } else {
          //                Console.WriteLine("Log to filename : {0}", oPackage.LogFileName);
          //              }
          //            }
          //            #endregion Display info about package

          //            Console.WriteLine("");

          //            #region Global variables
          //            if (oPackage.GlobalVariables.Count > 0) {
          //              Console.WriteLine("  > Global Variables");
          //              foreach (DTS.GlobalVariable2 oVariable in oPackage.GlobalVariables) {
          //                Console.WriteLine("    {0} = {1}", oVariable.Name, oVariable.Value);
          //              }
          //              Console.WriteLine("");
          //            }
          //            #endregion

          //            if (oPackage.Steps.Count > 0) {
          //              foreach (DTS.Step2 oStep in oPackage.Steps) {
          //                foreach (DTS.Task oTask in oPackage.Tasks) {
          //                  #region Display info about task
          //                  if (oTask.Name != null
          //                    && oTask.Name == oStep.TaskName
          //                    && (oTask.CustomTaskID == "DTSSendMailTask"
          //                    || (oTask.CustomTaskID == "DTSExecuteSQLTask"
          //                    && (NoNull(((DTS.ExecuteSQLTask2)oTask.CustomTask).SQLStatement, "").ToLower().IndexOf("xp_sendmail") >= 0))
          //                    )
          //                    ) {
          //                    switch (oTask.CustomTaskID) {
          //                      case "DTSSendMailTask":
          //                        #region DTSSendMailTask
          //                        Console.WriteLine("-- > Task : {0}", oTask.Name);
          //                        Console.WriteLine("");
          //                        DTS.SendMailTask oSendMailTask = (DTS.SendMailTask)oTask.CustomTask;
          //                        Console.WriteLine("  Message sent to: {0}", oSendMailTask.ToLine);
          //                        Console.WriteLine("  Subject: {0}", NoNull(oSendMailTask.Subject, "(none)"));
          //                        if (oSendMailTask.MessageText != null && oSendMailTask.MessageText.Trim().Length != 0) {
          //                          Console.WriteLine(MakeBlock("Body", oSendMailTask.MessageText, 2));
          //                        }
          //                        Console.WriteLine("  Attachments: {0}", NoNull(oSendMailTask.FileAttachments, "(none)"));
          //                        Console.WriteLine();
          //                        break;
          //                      #endregion DTSSendMailTask
          //                      case "DTSExecuteSQLTask":
          //                        #region DTSExecuteSQLTask
          //                        Console.WriteLine("-- > Task : {0}", oTask.Name);
          //                        Console.WriteLine();
          //                        DTS.ExecuteSQLTask2 oDTSExecuteSQLTask = (DTS.ExecuteSQLTask2)oTask.CustomTask;
          //                        foreach (string sItem in SqlUtils.Get_xp_sendmail(oDTSExecuteSQLTask.SQLStatement.Replace("\t", " ").ToLower())) {
          //                          Console.WriteLine("  xp_sendmail : Message sent to: {0}", sItem);
          //                        }
          //                        break;
          //                        #endregion DTSExecuteSQLTask
          //                    }
          //                    Console.WriteLine();
          //                  }
          //                  #endregion Display info about task
          //                }
          //              }
          //            }
          //            System.Runtime.InteropServices.Marshal.ReleaseComObject(oPackage);

          //          } catch (SqlException ex) {
          //            Console.WriteLine(ex.StackTrace);
          //            Console.WriteLine(ex.Message);
          //          }
          //          Console.WriteLine("");
          //        }
          //      }
          //      break;
          //    #endregion

          //    case "sendmail-compact":
          //      #region SendMailTasks in compact list mode
          //      Trace.WriteLine("Start SendMailTasks");
          //      Trace.Indent();
          //      foreach (string sQualifiedPackage in aPackages) {
          //        if (DtsRegEx.Length == 0 || Regex.IsMatch(sQualifiedPackage, DtsRegEx, RegexOptions.IgnoreCase)) {
          //          #region Create oPackage
          //          string sPackageType = sQualifiedPackage.Split(':')[0];
          //          string sPackage = sQualifiedPackage.Split(':')[1];
          //          DTS.Package2 oPackage = new DTS.Package2Class();
          //          Trace.Listeners["debug"].WriteLine(string.Format("Loading package : {0}", sQualifiedPackage));
          //          switch (sPackageType) {
          //            case "SQL":
          //              try {
          //                if (UserId == "") {
          //                  oPackage.LoadFromSQLServer(strServer, "", "", DTSSQLServerStorageFlags.DTSSQLStgFlag_UseTrustedConnection, "", "", "", sPackage, ref pVarPersistStgOfHost);
          //                } else {
          //                  oPackage.LoadFromSQLServer(strServer, UserId, Password, DTSSQLServerStorageFlags.DTSSQLStgFlag_Default, "", "", "", sPackage, ref pVarPersistStgOfHost);
          //                }
          //              } catch (Exception ex) {
          //                Console.WriteLine(ex.Message);
          //              }
          //              break;

          //            case "META":
          //              try {
          //                if (UserId == "") {
          //                  oPackage.LoadFromRepository(strServer, "", "", "", "", "", sPackage, DTSRepositoryStorageFlags.DTSReposFlag_UseTrustedConnection, ref pVarPersistStgOfHost);
          //                } else {
          //                  oPackage.LoadFromRepository(strServer, "", UserId, Password, "", "", sPackage, DTSRepositoryStorageFlags.DTSReposFlag_UseTrustedConnection, ref pVarPersistStgOfHost);
          //                }
          //              } catch (Exception ex) {
          //                Console.WriteLine(ex.Message);
          //              }
          //              break;
          //          }
          //          #endregion Create oPackage

          //          Hashtable hConnections = new Hashtable();
          //          foreach (DTS.Connection2 oConnection in oPackage.Connections) {
          //            hConnections.Add(oConnection.ID, oConnection.Name);
          //          }

          //          try {
          //            bool ContainsSendMailTask = false;
          //            #region Determine if contains SendMail task
          //            try {
          //              foreach (DTS.Task oTask in oPackage.Tasks) {
          //                switch (oTask.CustomTaskID) {
          //                  case "DTSSendMailTask":
          //                    ContainsSendMailTask = true;
          //                    break;
          //                  case "DTSExecuteSQLTask":
          //                    DTS.ExecuteSQLTask2 oDTSExecuteSQLTask = (DTS.ExecuteSQLTask2)oTask.CustomTask;
          //                    if (NoNull(oDTSExecuteSQLTask.SQLStatement, "").ToLower().IndexOf("xp_sendmail") >= 0) {
          //                      ContainsSendMailTask = true;
          //                    }
          //                    break;
          //                }
          //              }

          //            } catch (Exception ex) {
          //              Trace.Listeners["debug"].WriteLine(ex.Message);
          //              Trace.Listeners["debug"].WriteLine(ex.Source);
          //            }
          //            #endregion Determine if contains SendMail task

          //            #region ContainsSendMailTasks
          //            if (ContainsSendMailTask) {
          //              if (oPackage.Steps.Count > 0) {
          //                if (XmlOutputFile.Length != 0) {
          //                  oXmlTextWriter.WriteStartElement("package");
          //                  oXmlTextWriter.WriteAttributeString("name", sQualifiedPackage);
          //                }
          //                Trace.Indent();
          //                foreach (DTS.Step2 oStep in oPackage.Steps) {
          //                  foreach (DTS.Task oTask in oPackage.Tasks) {
          //                    #region Display info about task
          //                    if (oTask.Name != null && oTask.Name == oStep.TaskName) {
          //                      Trace.Listeners["debug"].WriteLine(string.Format("Analyse task : {0}", oTask.Name));
          //                      switch (oTask.CustomTaskID) {
          //                        case "DTSSendMailTask":
          //                          #region DTSSendMailTask
          //                          Trace.Indent();
          //                          Trace.Listeners["debug"].WriteLine("DTSSendMailTask");
          //                          DTS.SendMailTask oSendMailTask = (DTS.SendMailTask)oTask.CustomTask;

          //                          if (XmlOutputFile.Length != 0) {
          //                            #region xml
          //                            oXmlTextWriter.WriteStartElement("task");
          //                            oXmlTextWriter.WriteAttributeString("type", "DTSSendMailTask");
          //                            oXmlTextWriter.WriteElementString("name", oSendMailTask.Name);
          //                            oXmlTextWriter.WriteElementString("emailTo", oSendMailTask.ToLine);
          //                            oXmlTextWriter.WriteElementString("emailAttach", NoNull(oSendMailTask.FileAttachments, "(none)"));
          //                            oXmlTextWriter.WriteEndElement();
          //                            #endregion
          //                          } else {
          //                            WriteMultipleLines(sQualifiedPackage, 50, oSendMailTask.ToLine, 50, NoNull(oSendMailTask.FileAttachments, ""), 80);
          //                          }
          //                          Trace.Unindent();
          //                          break;
          //                        #endregion

          //                        case "DTSExecuteSQLTask":
          //                          #region DTSExecuteSQLTask
          //                          Trace.Indent();
          //                          Trace.Listeners["debug"].WriteLine("DTSExecuteSQLTask");
          //                          DTS.ExecuteSQLTask2 oDTSExecuteSQLTask = (DTS.ExecuteSQLTask2)oTask.CustomTask;
          //                          foreach (string sItem in SqlUtils.Get_xp_sendmail(oDTSExecuteSQLTask.SQLStatement.Replace("\t", " ").ToLower())) {
          //                            Trace.Indent();
          //                            Trace.Listeners["debug"].WriteLine(string.Format("found one: {0}", sItem));
          //                            Trace.Unindent();
          //                            if (XmlOutputFile.Length != 0) {
          //                              #region xml
          //                              oXmlTextWriter.WriteStartElement("task");
          //                              oXmlTextWriter.WriteAttributeString("type", "DTSExecuteSqlTask/xp_sendmail");
          //                              oXmlTextWriter.WriteElementString("name", oDTSExecuteSQLTask.Name);
          //                              oXmlTextWriter.WriteElementString("emailTo", sItem);
          //                              oXmlTextWriter.WriteElementString("emailAttach", "(none)");
          //                              oXmlTextWriter.WriteEndElement();
          //                              #endregion
          //                            } else {
          //                              WriteMultipleLines(string.Concat(sQualifiedPackage, "/", oDTSExecuteSQLTask.Name, "/xp_sendmail"), 50, sItem, 50, "", 80);
          //                            }
          //                          }
          //                          Trace.Unindent();
          //                          break;
          //                      }


          //                      #endregion
          //                    }
          //                  }
          //                  #endregion
          //                }
          //                Trace.Unindent();
          //                if (XmlOutputFile.Length != 0) {
          //                  oXmlTextWriter.WriteEndElement();
          //                }
          //                Console.WriteLine("");
          //              }
          //            }
          //            #endregion

          //            Trace.Listeners["debug"].WriteLine("Releasing COM object ... ");
          //            System.Runtime.InteropServices.Marshal.ReleaseComObject(oPackage);
          //            Trace.Listeners["debug"].WriteLine("Done.");

          //          } catch (Exception ex) {
          //            Trace.Listeners["debug"].WriteLine(ex.StackTrace);
          //            Trace.Listeners["debug"].WriteLine(ex.Message);
          //          }
          //        }
          //      }
          //      Trace.Unindent();
          //      break;
          //      #endregion

          //  }
          //  if (XmlOutputFile.Length != 0) {
          //    oXmlTextWriter.WriteEndElement();
          //  }

          //}
          //#endregion
  **/

          if (XmlOutputFile.Length != 0) {
            oXmlTextWriter.WriteEndElement();
          }
          Console.WriteLine();
        }
      }
      if (XmlOutputFile.Length != 0) {
        oXmlTextWriter.WriteEndElement();  // root
        oXmlTextWriter.WriteEndDocument();
        oXmlTextWriter.Flush();
        oXmlTextWriter.Close();
      }
      Environment.Exit(0);
    }
    #region utils
    static void Usage() {
      Console.WriteLine($"listdb for .NET Core {VERSION} - Build ({Assembly.GetExecutingAssembly().FullName})");
      Console.WriteLine("Usage: listdb /Server=server1[[;server2];...]");
      Console.WriteLine("              [/Config] : displays server configuration");
      Console.WriteLine();
      Console.WriteLine("              [/Databases[=list|stats|physical]] : lists databases");
      Console.WriteLine("              [/DbList=db1[[;db2];...] : restrict selection to these databases");
      Console.WriteLine();
      Console.WriteLine("              [/Tables[=list|full|stats|indexes|comments]] : lists tables");
      Console.WriteLine("              [/TablesList=table1[[;table2];...] : restrict selection to these tables");
      Console.WriteLine("              [/TablesRegEx=\"regular expression\"] (restrict based on name)");
      Console.WriteLine();
      Console.WriteLine("              [/SP[=list|full]] : lists stored procedures");
      Console.WriteLine("              [/SPRegEx=\"regular expression\"] (restrict based on name)");
      Console.WriteLine();
      Console.WriteLine("              [/Views[=list|full]] : lists views");
      Console.WriteLine("              [/ViewsRegEx=\"regular expression\"] (restrict based on name)");
      Console.WriteLine();
      Console.WriteLine("              [/Users[=list|full]] : lists users");
      Console.WriteLine();
      Console.WriteLine("              [/Triggers[=list|full]] : lists triggers with tables and/or views");
      Console.WriteLine();
      Console.WriteLine("              [/UDF[=list|full]] : lists user defined functions");
      Console.WriteLine();
      Console.WriteLine("              [/UDT[=list|full]] : lists user defined data types");
      Console.WriteLine();
      Console.WriteLine("              [/DTS[=list|full|sendmail|sendmail-compact]] : lists DTS packages");
      Console.WriteLine("              [/DTSRegEx=\"regular expression\"] (restrict based on name)");
      Console.WriteLine();
      Console.WriteLine("              [/Jobs[=list|steps|full|create|sendmail|notification]] : lists jobs");
      Console.WriteLine("              [/JobStatus[=failed;succeeded;cancelled;unknown|all]]");
      Console.WriteLine("              [/JobHistory[[=list|full][:number of days]]");
      Console.WriteLine("              [/JobsRegEx=\"regular expression\"] (restrict based on name)");
      Console.WriteLine();
      Console.WriteLine("              [/system] : adds system objects");
      Console.WriteLine("              [/smart] : suppress garbage values from the list");
      Console.WriteLine("              [/verbose] : displays additional info about the process");
      Console.WriteLine("              [/debug] : displays additional debugging info about the process");

      Environment.Exit(1);
    }


    #region StringUtils
    static string NoNull(Object var, string defaultValue) {
      if (var == null) {
        return defaultValue;
      } else {
        return var.ToString();
      }
    }

    //static string MakeTitle(string sTitle, int iLength) {
    //  return MakeTitle(sTitle, iLength, '-');
    //}
    //static string MakeTitle(string sTitle, int iLength, char filler) {
    //  StringBuilder sbRetVal = new StringBuilder();
    //  try {
    //    sbRetVal.AppendFormat("{0}[ {1} ]{2}", new string(filler, 3), sTitle, new string(filler, iLength - 3 - sTitle.Length - 4));
    //  } catch {
    //    sbRetVal.Length = 0;
    //    sbRetVal.Append(sTitle);
    //  }
    //  return sbRetVal.ToString();
    //}

    //static string MakeBlock(string sTitle, string sText) {
    //  return MakeBlock(sTitle, sText, '-', 0, 132);
    //}
    //static string MakeBlock(string sTitle, string sText, int iMargin) {
    //  return MakeBlock(sTitle, sText, '-', iMargin, 132);
    //}
    //static string MakeBlock(string sTitle, string sText, char cFiller) {
    //  return MakeBlock(sTitle, sText, cFiller, 0, 132);
    //}
    //static string MakeBlock(string sTitle, string sText, char filler, int iMargin, int iLength) {
    //  StringBuilder sbRetVal = new StringBuilder();
    //  string sMargin;
    //  if (iMargin > 0) {
    //    sMargin = new string(' ', iMargin);
    //  } else {
    //    sMargin = "";
    //  }

    //  string[] aText;
    //  aText = sText.TrimEnd('\n').TrimEnd(' ').Replace("\t", "    ").Split(new char[] { '\n', '\x0D', '\x0A' });

    //  int iLargest = 0;
    //  foreach (string sLine in aText) {
    //    if (sLine.Length > iLargest) {
    //      iLargest = sLine.Length;
    //    }
    //  }
    //  iLargest = Math.Max(iLargest, iLength);
    //  if (sTitle.Length > (iLargest - iMargin - 15)) {
    //    sTitle = sTitle.Substring(0, iLargest - iMargin - 15) + "...";
    //  }
    //  sbRetVal.AppendFormat("{0}+{1}[ {2} ]{3}\n", sMargin, new string(filler, 4), sTitle, new string(filler, iLargest - 5 - sTitle.Length - 4 + 2));
    //  foreach (string sLine in aText) {
    //    sbRetVal.AppendFormat("{0}| {1}\n", sMargin, sLine);
    //  }
    //  sbRetVal.AppendFormat("{0}+{1}", sMargin, new string(filler, iLargest + 1));
    //  return sbRetVal.ToString();

    //}
    //static void WriteMultipleLines(string sText1, int iText1, string sText2, int iText2, string sText3, int iText3) {
    //  string sTemplate1, sTemplate2, sTemplate3;
    //  while (sText1.Length != 0 || sText2.Length != 0 || sText3.Length != 0) {
    //    if (sText1.Length < iText1) {
    //      if (sText1.Length == 0) {
    //        sTemplate1 = sText1.PadRight(iText1, ' ');
    //      } else {
    //        sTemplate1 = sText1.PadRight(iText1, '.');
    //      }
    //      sText1 = "";
    //    } else {
    //      sTemplate1 = sText1.Substring(0, iText1);
    //      sText1 = sText1.Substring(iText1);
    //    }
    //    if (sText2.Length < iText2) {
    //      sTemplate2 = sText2.PadRight(iText2, '.');
    //      if (sText2.Length == 0) {
    //        sTemplate2 = sText2.PadRight(iText2, ' ');
    //      } else {
    //        sTemplate2 = sText2.PadRight(iText2, '.');
    //      }
    //      sText2 = "";
    //    } else {
    //      sTemplate2 = sText2.Substring(0, iText2);
    //      sText2 = sText2.Substring(iText2);
    //    }
    //    if (sText3.Length < iText3) {
    //      sTemplate3 = sText3.PadRight(iText3, '.');
    //      if (sText3.Length == 0) {
    //        sTemplate3 = sText3.PadRight(iText3, ' ');
    //      } else {
    //        sTemplate3 = sText3.PadRight(iText3, '.');
    //      }
    //      sText3 = "";
    //    } else {
    //      sTemplate3 = sText3.Substring(0, iText3);
    //      sText3 = sText3.Substring(iText3);
    //    }
    //    Console.WriteLine("{0} {1} {2}", sTemplate1, sTemplate2, sTemplate3);
    //  }
    //}

    #endregion


    static int GetDiskFreeSpaceSQL(Server oSql, string disk) {
      int RetVal = 0;
      //DataTable oDiskSpace = SqlUtils.QR2DataTable(oSql.ExecuteWithResults("master..xp_fixeddrives", System.Type.Missing));
      DataTable oDiskSpace = new DataTable();
      foreach (DataRow oRow in oDiskSpace.Rows) {
        if (oRow["drive"].ToString().ToLower() == disk.ToLower()) {
          RetVal = (int)oRow["MB Free"];
        }
      }
      return RetVal;
    }
    #endregion
  }

}

