using System;
using System.IO;
using System.Data;
using System.Data.SqlClient;
using System.Collections;
using System.Text;
using System.Xml;
using Microsoft.SqlServer.Management.Smo;

namespace listdb {

  class DumpSql {

    public string SqlServer = "";
    private Server oSql;
    private bool IsConnected = false;
    public bool UserOnly = true;

    public DumpSql() {
    }

    public DumpSql(string sqlServer) {
      SqlServer = sqlServer;
    }

    public bool Open() {
      oSql = new Server();
      oSql.ConnectionContext.LoginSecure = true;
      try {
        IsConnected = true;
        return true;
      } catch {
        IsConnected = false;
        return false;
      }
    }

    public bool Close() {
      if (IsConnected) {
        IsConnected = false;
        return true;
      } else {
        return false;
      }
    }

    public DataTable GetDbList() {
      DataTable oDT = new DataTable("Databases");
      oDT.Columns.Add("name", typeof(string));
      oDT.Columns.Add("size", typeof(float));
      oDT.Columns.Add("SpaceAvailable", typeof(float));
      oDT.Columns.Add("DataSpaceUsage", typeof(int));
      oDT.Columns.Add("IndexSpaceUsage", typeof(int));
      oDT.Columns.Add("Users", typeof(int));

      foreach(Database oDatabase in oSql.Databases) {
        if (!UserOnly || (UserOnly && !oDatabase.IsSystemObject)) {
          DataRow oRow = oDT.NewRow();
          oRow["name"] = oDatabase.Name;
          oRow["size"] = oDatabase.Size;
          oRow["SpaceAvailable"] = oDatabase.SpaceAvailable;
          oRow["DataSpaceUsage"] = oDatabase.DataSpaceUsage;
          oRow["IndexSpaceUsage"] = oDatabase.IndexSpaceUsage;
          oRow["Users"] = oDatabase.Users.Count;
          oDT.Rows.Add(oRow);
        }
      }

      return oDT;
    }

    public DataTable GetTablesList() {
      return GetTablesList("");
    }

    public DataTable GetTablesList(string sDatabase) {
      string DataTableName = sDatabase == "" ? "" : "-" + sDatabase;
      DataTable oDT = new DataTable(string.Concat("Tables", DataTableName));
      oDT.Columns.Add("dbname", typeof(string));
      oDT.Columns.Add("tablename", typeof(string));
      oDT.Columns.Add("DataSpaceUsed", typeof(int));
      oDT.Columns.Add("Script", typeof(string));

      if (!IsConnected) {
        return oDT;
      }

      //if (sDatabase=="") {
      //  foreach(Database oDatabase in oSql.Databases) {
      //    if (!UserOnly || (UserOnly && !oDatabase.IsSystemObject)) {
      //      foreach(Table oTable in oDatabase.Tables) {
      //        if (!UserOnly || (UserOnly && !oTable.IsSystemObject)) {
      //          DataRow oRow = oDT.NewRow();
      //          oRow["dbname"] = oDatabase.Name;
      //          oRow["tablename"] = oDatabase.Name + "." + oTable.Owner + "." + oTable.Name;
      //          oRow["DataSpaceUsed"] = oTable.DataSpaceUsed;
      //          string sTemp = oTable.Script(SQLDMO_SCRIPT_TYPE.SQLDMOScript_Default,System.Type.Missing,System.Type.Missing,SQLDMO_SCRIPT2_TYPE.SQLDMOScript2_Default);
      //          string[] aTemp = sTemp.TrimEnd().Replace("\t", "  ").Split('\n');
      //          StringBuilder sbTemp = new StringBuilder();
      //          foreach(string sLine in aTemp) {
      //            sbTemp.AppendFormat("{0}\n", sLine.TrimEnd());
      //          }
      //          oRow["Script"] = sbTemp.ToString();
      //          oDT.Rows.Add(oRow);
      //        }
      //      }
      //    }
      //  }
      //}

      return oDT;
    }

    //public DataSet GetErrorLogs() {
    //  return QR2DataSet((QueryResults2)oSql.EnumErrorLogs());
    //}

    //protected DataSet QR2DataSet(QueryResults2 oQuery) {
    //  DataSet oRetVal = new DataSet();
    //  for(int i=1; i<=oQuery.ResultSets; i++) {
    //    oQuery.CurrentResultSet = i;
    //    DataTable oDT = new DataTable(i.ToString());
    //    for(int iCol=1; iCol<=oQuery.Columns; iCol++) {
    //      oDT.Columns.Add(oQuery.get_ColumnName(iCol));
    //    }
    //    for(int iRow=1; iRow<=oQuery.Rows; iRow++) {
    //      DataRow oRow = oDT.NewRow();
    //      for(int iCol=1; iCol<=oQuery.Columns; iCol++) {
    //        oRow[oQuery.get_ColumnName(iCol)] = oQuery.GetColumnString(iRow, iCol);
    //      }
    //      oDT.Rows.Add(oRow);
    //    }
    //    oRetVal.Tables.Add(oDT);
    //  }

    //  return oRetVal;
    //}
  }

  
}
