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
using static listdb.OutputObject;


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

  class Program {

    static readonly Version VERSION = new Version("2.0");

    static void Main(string[] args) {

      SplitArgs Args = new SplitArgs(args);

      if (Args.IsDefined("?") || Args.IsDefined("help")) {
        Usage();
      }

      TAnalyseSql AnalyseSql = new TAnalyseSql(Args);

      AnalyseSql.Initialize();

      if (AnalyseSql.Verbose) {
        WriteLine(AnalyseSql.GetVerbose());
      }

      AnalyseSql.Process();
      
      Environment.Exit(0);
    }
    #region utils
    static void Usage() {
      WriteLine($"listdb for .NET Core {VERSION} - Build ({Assembly.GetExecutingAssembly().FullName})");
      WriteLine("Usage: listdb /Server=server1[[;server2];...]");
      WriteLine("              [/Config] : displays server configuration");
      WriteLine();
      WriteLine("              [/Databases[=list|stats|physical]] : lists databases");
      WriteLine("              [/DbList=db1[[;db2];...] : restrict selection to these databases");
      WriteLine();
      WriteLine("              [/Tables[=list|full|stats|indexes|comments]] : lists tables");
      WriteLine("              [/TablesList=table1[[;table2];...] : restrict selection to these tables");
      WriteLine("              [/TablesRegEx=\"regular expression\"] (restrict based on name)");
      WriteLine();
      WriteLine("              [/SP[=list|full]] : lists stored procedures");
      WriteLine("              [/SPRegEx=\"regular expression\"] (restrict based on name)");
      WriteLine();
      WriteLine("              [/Views[=list|full]] : lists views");
      WriteLine("              [/ViewsRegEx=\"regular expression\"] (restrict based on name)");
      WriteLine();
      WriteLine("              [/Users[=list|full]] : lists users");
      WriteLine();
      WriteLine("              [/Triggers[=list|full]] : lists triggers with tables and/or views");
      WriteLine();
      WriteLine("              [/UDF[=list|full]] : lists user defined functions");
      WriteLine();
      WriteLine("              [/UDT[=list|full]] : lists user defined data types");
      WriteLine();
      WriteLine("              [/DTS[=list|full|sendmail|sendmail-compact]] : lists DTS packages");
      WriteLine("              [/DTSRegEx=\"regular expression\"] (restrict based on name)");
      WriteLine();
      WriteLine("              [/Jobs[=list|steps|full|create|sendmail|notification]] : lists jobs");
      WriteLine("              [/JobStatus[=failed;succeeded;cancelled;unknown|all]]");
      WriteLine("              [/JobHistory[[=list|full][:number of days]]");
      WriteLine("              [/JobsRegEx=\"regular expression\"] (restrict based on name)");
      WriteLine();
      WriteLine("              [/system] : adds system objects");
      WriteLine("              [/smart] : suppress garbage values from the list");
      WriteLine("              [/verbose] : displays additional info about the process");
      WriteLine("              [/debug] : displays additional debugging info about the process");

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

