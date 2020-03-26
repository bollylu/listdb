using BLTools;
using BLTools.Text;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace listdb {
  public class TSqlDocumenterText : ASqlDocumenter<string> {

    public TSqlDocumenterText(Server sqlServer) {
      _SqlServer = sqlServer;
    }

    public override void DocumentConfig(EDocumentConfigType configType) {

      StringBuilder RetVal = new StringBuilder();

      switch (configType) {

        case EDocumentConfigType.Full:
          #region --- Full config --------------------------------------------
          RetVal.AppendLine(MakeSectionTitle($"Server version : {_SqlServer.VersionString}"));
          RetVal.AppendLine($"Instance name     = {_SqlServer.InstanceName}");
          RetVal.AppendLine($"In cluster        = {(_SqlServer.IsClustered ? "Yes" : "No")}");
          RetVal.AppendLine($"Network name      = {_SqlServer.NetName}");
          RetVal.AppendLine($"Collation         = {_SqlServer.Collation}");
          RetVal.AppendLine($"Service name      = {_SqlServer.ServiceName}");
          RetVal.AppendLine($"Startup account   = {_SqlServer.ServiceAccount}");
          RetVal.AppendLine($"SQL Root Path     = {_SqlServer.MasterDBPath}");
          RetVal.AppendLine($"Default log path  = {_SqlServer.MasterDBLogPath}");
          RetVal.AppendLine($"Error Log path    = {_SqlServer.ErrorLogPath}");
          RetVal.AppendLine($"Backup path       = {_SqlServer.BackupDirectory}");
          RetVal.AppendLine(MakeSectionFooter());

          RetVal.AppendLine(MakeSectionTitle($"Configuration"));
          foreach (ConfigProperty ConfigItem in _SqlServer.Configuration.Properties) {
            RetVal.AppendLine($"{ConfigItem.DisplayName.AlignedLeft(40, '.')} = {ConfigItem.ConfigValue}");
          }
          RetVal.AppendLine(MakeSectionFooter());
          break;

        #endregion --- Full config --------------------------------------------



        default:
        case EDocumentConfigType.List:
          RetVal.AppendLine(TextBox.BuildHorizontalRowWithText($" Server version : {_SqlServer.VersionString} ", 120, TextBox.EHorizontalRowType.Single));
          break;

      }

      Output?.Invoke(RetVal.ToString());

    }

    public override void DocumentUsers(EDocumentUsersType usersType) {

      StringBuilder RetVal = new StringBuilder();

      RetVal.AppendLine(MakeSectionTitle($"Server : {_SqlServer.Name} - Users list"));

      int UserCount = 0;
      foreach (Login LoginItem in _SqlServer.Logins) {
        UserCount++;

        switch (usersType) {
          case EDocumentUsersType.Full:
          case EDocumentUsersType.List:
          default:
            #region list
            RetVal.Append(LoginItem.Name.AlignedLeft(40, '.'));

            switch (LoginItem.LoginType) {
              case LoginType.WindowsGroup:
                RetVal.Append("Windows group".AlignedLeft(20, '.'));
                break;
              case LoginType.WindowsUser:
                RetVal.Append("NT user".AlignedLeft(20, '.'));
                break;
              case LoginType.SqlLogin:
                RetVal.Append("SQL User".AlignedLeft(20, '.'));
                break;
            }

            try {
              RetVal.Append(LoginItem.DefaultDatabase.AlignedLeft(40, '.'));
            } catch {
              RetVal.Append("*** Warning: no default database ***".AlignedLeft(40, '.'));
            }

            RetVal.Append(LoginItem.Language.AlignedLeft(15, '.'));
            RetVal.Append(LoginItem.IsDisabled ? "Login denied".AlignedLeft(15, '.') : "Login ok".AlignedLeft(15, '.'));

            List<ServerRole> UserRoles = new List<ServerRole>();
            foreach (ServerRole RoleItem in _SqlServer.Roles) {
              if (RoleItem.EnumMemberNames().Contains(LoginItem.Name)) {
                UserRoles.Add(RoleItem);
              }
            }
            RetVal.Append(string.Join(", ", UserRoles.Select(x => x.Name)));

            RetVal.AppendLine();

            break;
            #endregion

        }
      }

      RetVal.AppendLine(MakeSectionFooter($"{UserCount} user(s) listed"));
      Output?.Invoke(RetVal.ToString());
    }

    public override void DocumentTables(Database database, EDocumentTablesType tablesType, bool userOnly = false) {
      DocumentTables(database, tablesType, new string[0], userOnly);
    }

    public override void DocumentTables(Database database, EDocumentTablesType tablesType, IEnumerable<string> tableFilter, bool userOnly = false) {

      StringBuilder RetVal = new StringBuilder();

      RetVal.AppendLine(MakeSectionTitle($"Database : {database.Name} - Tables list"));

      foreach (Table TableItem in database.Tables) {

        if (tableFilter.IsEmpty() || tableFilter.Contains(TableItem.Name, StringComparer.InvariantCultureIgnoreCase)) {

          if (!userOnly || (userOnly && !database.IsSystemObject && !TableItem.IsSystemObject)) {

            Scripter TableScripter = new Scripter(_SqlServer);
            TableScripter.Options.ScriptDrops = false;
            TableScripter.Options.WithDependencies = true;
            TableScripter.Options.IncludeHeaders = true;
            //TableScripter.Options.ScriptForAlter = true;

            string strTableName;
            string strUnderline;
            switch (tablesType) {
              case EDocumentTablesType.Full:
                #region full
                strTableName = $"-- > Table : {database.Name}.{database.UserName}.{TableItem.Name}";
                strUnderline = "-- > " + new string('=', strTableName.Length - 5);
                RetVal.AppendLine(strUnderline);
                RetVal.AppendLine(strTableName);
                RetVal.AppendLine(strUnderline);
                foreach(string LineItem in TableScripter.Script(new SqlSmoObject[] { TableItem })) {
                  RetVal.AppendLine(LineItem);
                }
                RetVal.AppendLine("-- > ===[EOT]===");
                RetVal.AppendLine();
                break;
              #endregion

              /**
              //case "stats":
              //  #region stats
              //  strTableName = string.Format("-- > Table : {0}.{1}.{2}", oDB.Name, oDB.UserName, TableItem.Name);
              //  Console.Write(strTableName.PadRight(80, '.'));
              //  Console.Write(" (D={0,20}, ", TableItem.DataSpaceUsed.ToString("#,##0 KB"));
              //  Console.Write("{0,20}) ", TableItem.Rows.ToString("#,##0 recs"));
              //  Console.Write("(I={0,20})", TableItem.IndexSpaceUsed.ToString("#,##0 KB"));
              //  Console.WriteLine();
              //  break;
              //#endregion

              //case "indexes":
              //  #region indexes
              //  try {
              //      oTable.RecalcSpaceUsage();
              //    } catch (Exception ex) {
              //      Trace.WriteLine(string.Format("Error calculating space usage for {0}", oTable.Name));
              //      Trace.WriteLine(ex.Message, "Error");
              //    }
              //  strTableName = string.Format("-- > Table : {0}.{1}.{2}", oDB.Name, oDB.UserName, TableItem.Name);
              //  Console.Write(strTableName.PadRight(80, '.'));
              //  Console.Write(" (I={0,20})", TableItem.IndexSpaceUsed.ToString("#,##0 KB"));
              //  Console.Write(" (D={0,20}, ", TableItem.DataSpaceUsed.ToString("#,##0 KB"));
              //  Console.Write("{0,20}) ", TableItem.Rows.ToString("#,##0 recs"));
              //  Console.WriteLine();
              //  foreach (Index2 oIndex in TableItem.Indexes) {
              //    if (!UserOnly || (UserOnly && !oIndex.Name.StartsWith("_WA_Sys_"))) {
              //      Console.Write("     Index : {0}", oIndex.Name.PadRight(67, '.'));
              //      Console.Write(" Size = {0,16}, ", oIndex.SpaceUsed.ToString("#,##0 KB"));
              //      Console.Write("Key = ");
              //      SQLDMO.SQLObjectList oSqlObjectList = oIndex.ListIndexedColumns();
              //      StringBuilder sbText = new StringBuilder();
              //      foreach (SQLDMO.Column oColumn in oSqlObjectList) {
              //        sbText.AppendFormat("{0}+", oColumn.Name);
              //      }
              //      sbText.Remove(sbText.Length - 1, 1);
              //      Console.Write(sbText.ToString());

              //      Console.Write(", Type = {0}", SqlUtils.IndexType2String(oIndex.Type));
              //      if (oIndex.FillFactor > 0) {
              //        Console.Write(", Fill factor = {0}%", oIndex.FillFactor);
              //      }
              //      Console.Write(", Storage = {0}", oIndex.FileGroup);
              //      Console.WriteLine();
              //    }
              //  }
              //  Console.WriteLine();
              //  break;
              //#endregion

              //case "comments":
              //  strTableName = string.Format("-- > Table : {0}.{1}.{2}", oDB.Name, oDB.UserName, TableItem.Name);
              //  strUnderline = "-- > " + new string('=', strTableName.Length - 5);
              //  Console.WriteLine(strUnderline);
              //  Console.WriteLine(strTableName);
              //  Console.WriteLine(strUnderline);
              //  string SqlCommand = string.Format("SELECT objname, convert(varchar(512), value) as comment FROM ::fn_listextendedproperty('MS_Description', 'user', 'dbo', 'table', '{0}', 'column', NULL)", TableItem.Name);
              //  DataTable ExtendedProperties = SqlUtils.QR2DataTable(oDB.ExecuteWithResults(SqlCommand, null));
              //  foreach (SQLDMO.Column oColumnItem in TableItem.Columns) {
              //    Console.Write("{0}", oColumnItem.Name.PadRight(30, '.'));
              //    Console.Write(" {0}", oColumnItem.Length.ToString().PadLeft(8, '.'));
              //    Console.Write(" {0}", oColumnItem.Datatype.PadRight(10, '.'));
              //    Console.Write(" {0}", oColumnItem.AllowNulls ? "NULL....." : "NOT NULL.");
              //    if (oColumnItem.Default != null) {
              //      Console.Write(" {0}", oColumnItem.Default.PadRight(20, '.'));
              //    }
              //    string ExtendedPropertyComment = "";
              //    DataRow[] oRows = { };
              //    if (ExtendedProperties.Rows.Count > 0) {
              //      oRows = ExtendedProperties.Select(string.Format("objname = '{0}'", oColumnItem.Name));
              //      if (oRows.Length > 0) {
              //        ExtendedPropertyComment = Encoding.Unicode.GetString(Encoding.Unicode.GetBytes(oRows[0]["comment"].ToString()));
              //      }
              //    }
              //    int CommentSizeLimit = 100;
              //    if (ExtendedPropertyComment.Length <= CommentSizeLimit) {
              //      Console.Write(" {0}", ExtendedPropertyComment);
              //      Console.WriteLine();
              //    } else {
              //      string Filler = new string(' ', 62);
              //      int WhitePos = 80;
              //      while (WhitePos > 1 && ExtendedPropertyComment[WhitePos] != ' ') {
              //        WhitePos--;
              //      }
              //      Console.Write(" {0}", ExtendedPropertyComment.Substring(0, WhitePos));
              //      ExtendedPropertyComment = ExtendedPropertyComment.Substring(WhitePos);
              //      Console.WriteLine();
              //      while (ExtendedPropertyComment.Length > CommentSizeLimit) {
              //        WhitePos = 80;
              //        while (WhitePos > 1 && ExtendedPropertyComment[WhitePos] != ' ') {
              //          WhitePos--;
              //        }
              //        Console.WriteLine(Filler + ExtendedPropertyComment.Substring(0, WhitePos));
              //        ExtendedPropertyComment = ExtendedPropertyComment.Substring(WhitePos);
              //      }
              //      if (ExtendedPropertyComment.Length > 0) {
              //        Console.WriteLine(Filler + ExtendedPropertyComment);
              //      }
              //    }

              //  }
              //  Console.WriteLine();
              //  break;
              **/

              case EDocumentTablesType.List:
              default:
                #region list
                RetVal.AppendLine($"-- > Table : {database.Name}.{database.UserName}.{TableItem.Name}");
                break;
                #endregion
            }

            /**
            //#region Triggers
            //if (WithTriggers) {
            //  string strTriggerName;
            //  string strUnderTrigger;
            //  switch (TriggerDetails) {
            //    case "full":
            //      #region full
            //      foreach (Trigger oTrigger in TableItem.Triggers) {
            //        strTriggerName = string.Format("-- Trigger : {0}.{1}.{2}.{3}", oDB.Name, oDB.UserName, TableItem.Name, oTrigger.Name);
            //        strUnderTrigger = "-- " + new string('=', strTriggerName.Length - 3);
            //        Console.WriteLine(strUnderTrigger);
            //        Console.WriteLine(strTriggerName);
            //        Console.WriteLine(strUnderTrigger);
            //        Console.WriteLine(oTrigger.Text);
            //        Console.WriteLine("-- ===[EOT]===");
            //        Console.WriteLine();
            //      }
            //      break;
            //    #endregion
            //    case "list":
            //    case "":
            //    default:
            //      #region list
            //      foreach (Trigger oTrigger in TableItem.Triggers) {
            //        strTriggerName = string.Format("-- > > Trigger : {0}.{1}.{2}.{3}", oDB.Name, oDB.UserName, TableItem.Name, oTrigger.Name);
            //        Console.WriteLine(strTriggerName);
            //      }
            //      break;
            //      #endregion
            //  }
            //}
            //#endregion
            **/

          }
        }
      }

      Output?.Invoke(RetVal.ToString());
    }
  }
}
