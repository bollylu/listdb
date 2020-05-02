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

    #region --- Server configuration --------------------------------------------
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
    #endregion --- Server configuration --------------------------------------------

    public override void DocumentDatabases(EDocumentDatabasesType databasesType, ISelectionCriterias criterias) {
      StringBuilder RetVal = new StringBuilder();

      RetVal.AppendLine(MakeSectionTitle($"Server : {_SqlServer.Name} - Databases list"));

      

      switch (databasesType) {
        case EDocumentDatabasesType.Stats:
          foreach (Database DatabaseItem in _SqlServer.Databases) {

            if ((criterias.SelectUserData && !DatabaseItem.IsSystemObject) || (criterias.SelectSystemData && DatabaseItem.IsSystemObject)) {
              Log(string.Format("accessing database {0}", DatabaseItem.Name));
              #region stats
              RetVal.Append($"-- {DatabaseItem.Name.TrimEnd()}".AlignedLeft(40, '.'));
              RetVal.Append($"{DatabaseItem.Size:#,##0.00 MB}".AlignedRight(20, '.'));
              RetVal.Append($" {DatabaseItem.SpaceAvailable:#,##0.00 MB free}".AlignedRight(25, '.'));
              RetVal.Append($" (D={DatabaseItem.DataSpaceUsage:#,##0 KB})".AlignedRight(15, '.'));
              RetVal.Append($" (I={DatabaseItem.IndexSpaceUsage:#,##0 KB})".AlignedRight(20, '.'));
              RetVal.Append($" {DatabaseItem.Users.Count} users".AlignedLeft(10, '.'));

              DatabaseOptions oDBO;
              try {
                oDBO = DatabaseItem.DatabaseOptions;
                RetVal.Append($" | {SqlUtils.RecoveryModelText(oDBO.RecoveryModel)}");
              } catch {
                RetVal.Append(" | only for 2000+");
              }
              RetVal.AppendLine();
            }
          }
          break;
        #endregion

        case EDocumentDatabasesType.Physical:
          #region physical

          const string COL_DBNAME = "Db name";
          const int COL_DBNAME_WIDTH = 30;
          const string COL_LOGICAL_FILENAME = "Logical name";
          const int COL_LOGICAL_FILENAME_WIDTH = 40;
          const string COL_GROUP_NAME = "Group";
          const int COL_GROUP_NAME_WIDTH = 20;
          const string COL_PHYSICAL_FILENAME = "Physical name";
          const int COL_PHYSICAL_FILENAME_WIDTH = 120;
          const string COL_CURRENT_SIZE = "Current size";
          const int COL_CURRENT_SIZE_WIDTH = 20;
          const string COL_FREE_SPACE = "Free space";
          const int COL_FREE_SPACE_WIDTH = 20;
          const string COL_GROWTH = "Growth parameters";
          
          #region --- Columns header --------------------------------------------
          RetVal.Append(COL_DBNAME.AlignedCenter(COL_DBNAME_WIDTH));
          RetVal.Append(COL_LOGICAL_FILENAME.AlignedCenter(COL_LOGICAL_FILENAME_WIDTH));
          RetVal.Append(COL_GROUP_NAME.AlignedCenter(COL_GROUP_NAME_WIDTH));
          RetVal.Append(COL_PHYSICAL_FILENAME.AlignedCenter(COL_PHYSICAL_FILENAME_WIDTH));
          RetVal.Append(COL_CURRENT_SIZE.AlignedCenter(COL_CURRENT_SIZE_WIDTH));
          RetVal.Append(COL_FREE_SPACE.AlignedCenter(COL_FREE_SPACE_WIDTH));
          RetVal.Append(COL_GROWTH);
          RetVal.AppendLine();
          #endregion --- Columns header --------------------------------------------

          foreach (Database DatabaseItem in _SqlServer.Databases) {

            if ((criterias.SelectUserData && !DatabaseItem.IsSystemObject) || (criterias.SelectSystemData && DatabaseItem.IsSystemObject)) {
              Log(string.Format("accessing database {0}", DatabaseItem.Name));

              #region Data files
              foreach (FileGroup FileGroupItem in DatabaseItem.FileGroups) {
                foreach (DataFile DataFileItem in FileGroupItem.Files) {
                  RetVal.Append($"{DatabaseItem.Name.AlignedLeft(COL_DBNAME_WIDTH, '.')}");
                  RetVal.Append($"{DataFileItem.Name.AlignedLeft(COL_LOGICAL_FILENAME_WIDTH, '.')}");
                  RetVal.Append($"{FileGroupItem.Name.AlignedLeft(COL_GROUP_NAME_WIDTH, '.')}");
                  RetVal.Append($"{DataFileItem.FileName.AlignedLeft(COL_PHYSICAL_FILENAME_WIDTH, '.')}");
                  RetVal.Append($"{(float)(DataFileItem.Size / 1024.0):#0.00 MB}".AlignedRight(COL_CURRENT_SIZE_WIDTH, '.'));
                  RetVal.Append($"{DataFileItem.VolumeFreeSpace:#0.00 MB}".AlignedRight(COL_FREE_SPACE_WIDTH, '.'));

                  switch (DataFileItem.GrowthType) {
                    case FileGrowthType.KB:
                      #region Growth MB
                      if (DataFileItem.Growth > 0) {
                        RetVal.Append($" + {(int)(DataFileItem.Growth / 1024):#0 MB}");
                        if (DataFileItem.MaxSize > 0) {
                          RetVal.Append($", upto {DataFileItem.MaxSize:#0 MB}");
                        } else {
                          RetVal.Append(", no limit");
                        }
                      } else {
                        RetVal.Append(" + no growth");
                      }
                      break;
                    #endregion
                    case FileGrowthType.Percent:
                      #region Growth Percent
                      if (DataFileItem.Growth > 0) {
                        RetVal.Append($" + {DataFileItem.Growth:#0} % ");
                        if (DataFileItem.MaxSize > 0) {
                          RetVal.Append($", upto {DataFileItem.MaxSize:#0 MB}");
                        } else {
                          RetVal.Append(", no limit");
                        }
                      } else {
                        RetVal.Append(" + no growth");
                      }
                      break;
                    #endregion
                    default:
                      RetVal.Append(" + no growth");
                      break;

                  }

                  RetVal.AppendLine();
                }
              }
              #endregion

              #region Log files
              foreach (LogFile LogFileItem in DatabaseItem.LogFiles) {
                RetVal.Append($"{DatabaseItem.Name.AlignedLeft(COL_DBNAME_WIDTH, '.')}");
                RetVal.Append($"{LogFileItem.Name.AlignedLeft(COL_LOGICAL_FILENAME_WIDTH, '.')}");
                RetVal.Append($"{string.Empty.PadRight(COL_GROUP_NAME_WIDTH, '.')}");
                RetVal.Append($"{LogFileItem.FileName.AlignedLeft(COL_PHYSICAL_FILENAME_WIDTH, '.')}");
                RetVal.Append($"{(float)(LogFileItem.Size / 1024.0):#0.00 MB}".AlignedRight(COL_CURRENT_SIZE_WIDTH, '.'));
                RetVal.Append($"{LogFileItem.VolumeFreeSpace:#0.00 MB}".AlignedRight(COL_FREE_SPACE_WIDTH, '.'));

                switch (LogFileItem.GrowthType) {
                  case FileGrowthType.KB:
                    #region Growth MB
                    if (LogFileItem.Growth > 0) {
                      RetVal.Append($" + {(float)(LogFileItem.Growth / 1024):#0 MB}");
                      if (LogFileItem.MaxSize > 0) {
                        RetVal.Append($", upto {LogFileItem.MaxSize:#0 MB}");
                      } else {
                        RetVal.Append(", no limit");
                      }
                    } else {
                      RetVal.Append(" + no growth");
                    }
                    break;
                  #endregion
                  case FileGrowthType.Percent:
                    #region Growth Percent 
                    if (LogFileItem.Growth > 0) {
                      RetVal.Append($" + {LogFileItem.Growth:#0} % ");
                      if (LogFileItem.MaxSize > 0) {
                        RetVal.Append($", upto {LogFileItem.MaxSize:#0 MB}");
                      } else {
                        RetVal.Append(", no limit");
                      }
                    } else {
                      RetVal.Append(" + no growth");
                    }
                    break;
                  #endregion
                  default:
                    RetVal.Append(" + no growth");
                    break;
                }

                RetVal.AppendLine();
              }
              #endregion
            }
          }
          break;
        #endregion

        case EDocumentDatabasesType.Full:
        case EDocumentDatabasesType.List:
        default:
          #region list
          foreach (Database DatabaseItem in _SqlServer.Databases) {

            if ((criterias.SelectUserData && !DatabaseItem.IsSystemObject) || (criterias.SelectSystemData && DatabaseItem.IsSystemObject)) {
              Log(string.Format("accessing database {0}", DatabaseItem.Name));
              RetVal.Append($"-- Database : {DatabaseItem.Name.AlignedLeft(50, '.')}");
              RetVal.Append($" {SqlUtils.DbStatusText(DatabaseItem.Status)}");
              RetVal.AppendLine();
            }
          }
          break;
          #endregion
      }

      Output?.Invoke(RetVal.ToString());
    }


    #region --- Database tables --------------------------------------------

    public override void DocumentTables(Database database, EDocumentTablesType tablesType, ISelectionCriterias criterias) {

      StringBuilder RetVal = new StringBuilder();

      RetVal.AppendLine(MakeSectionTitle($"Database : {database.Name} - Tables list"));

      foreach (Table TableItem in database.Tables) {

        if (criterias.Filter.IsEmpty() || criterias.Filter.Contains(TableItem.Name, StringComparer.InvariantCultureIgnoreCase)) {

          if ((criterias.SelectUserData && !database.IsSystemObject && !TableItem.IsSystemObject) || (criterias.SelectSystemData && database.IsSystemObject && TableItem.IsSystemObject)) {

            Scripter TableScripter = new Scripter(_SqlServer);
            TableScripter.Options.ScriptDrops = false;
            TableScripter.Options.WithDependencies = true;
            TableScripter.Options.IncludeHeaders = true;

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
                foreach (string LineItem in TableScripter.Script(new SqlSmoObject[] { TableItem })) {
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
    #endregion --- Database tables --------------------------------------------
  }
}
