using BLTools;

using Microsoft.SqlServer.Management.Smo;

using System;
using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Threading;

namespace listdb {
  /// <summary>
  /// Summary description for SqlUtils.
  /// </summary>

  public class SqlUtils {
    static public string OlapItemType(short itemType) {
      string[] Descriptions = new string[] {
                                             "processing whole database",
                                             "processing whole folder specified in TreeKey",
                                             "",
                                             "one cube",
                                             "one virtual cube",
                                             "",
                                             "one partition",
                                             "",
                                             "one shared dimension",
                                             "",
                                             "one virtual dimension",
                                             "one relational mining model",
                                             "one olap mining model"
                                           };
      if (itemType >= 0 && itemType < Descriptions.Length) {
        return Descriptions[itemType - 1];
      } else {
        return "";
      }
    }

    static public string[] Get_xp_sendmail(string sText) {
      int StartPos;
      ArrayList aRetVal = new ArrayList();
      while ((StartPos = sText.IndexOf("xp_sendmail")) >= 0) {
        sText = sText.Substring(StartPos);
        sText = sText.Substring(sText.IndexOf("@recipients") + 11);
        while (sText[0] == ' ' || sText[0] == '=' || sText[0] == '\n' || sText[0] == '\r') {
          sText = sText.Substring(1);
        }
        int NextPos = 2;
        if (sText[0] == '@') {
          for (int i = 2; i < sText.Length && !(sText[i] == ' ' || sText[i] == ',' || sText[i] == '\n'); i++) {
            NextPos++;
          }
          aRetVal.Add(sText.Substring(0, NextPos));
        } else {
          NextPos = sText.IndexOf("'", 1) - 1;
          aRetVal.Add(sText.Substring(1, NextPos));
        }
        sText = sText.Substring(NextPos + 1);
      }
      return (string[])aRetVal.ToArray(typeof(string));
    }

    static public string RecoveryModelText(RecoveryModel recoveryModel) {
      return recoveryModel switch
      {
        RecoveryModel.Full => "Full",
        RecoveryModel.Simple => "Simple",
        RecoveryModel.BulkLogged => "Bulk",
        _ => "Unknown"
      };
    }

    static public DateTime Int2DT(int dateValue, int timeValue) {
      DateTime Retval;
      if (dateValue == 0 && timeValue == 0) {
        Retval = DateTime.MaxValue;
      } else {
        int iYear = (int)(dateValue / 10000);
        int iMonth = (int)((dateValue - iYear * 10000) / 100);
        int iDay = (int)(dateValue - iYear * 10000 - iMonth * 100);
        int iHour = (int)(timeValue / 10000);
        int iMinute = (int)((timeValue - iHour * 10000) / 100);
        int iSecond = (int)(timeValue - iHour * 10000 - iMinute * 100);
        string strDateTime = string.Concat(iDay.ToString(), "/", iMonth.ToString(), "/", iYear.ToString(), " ", iHour.ToString(), ":", iMinute.ToString(), ":", iSecond.ToString());
        CultureInfo OldCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("FR-be");
        try {
          Retval = DateTime.Parse(strDateTime);
        } catch {
          Retval = DateTime.MinValue;
        }
        Thread.CurrentThread.CurrentCulture = OldCulture;
      }

      return Retval;
    }

    static public string IndexType2String(IndexType indexType) {
      StringBuilder sbRetVal = new StringBuilder();

      if ((indexType.HasFlag(IndexType.ClusteredIndex))) {
        sbRetVal.Append("clustered, ");
      } else {
        sbRetVal.Append("non-clustered, ");
      }

      if (sbRetVal.Length > 2) {
        sbRetVal.Truncate(2);
        return sbRetVal.ToString();
      } else {
        return indexType.ToString();
      }
    }

    //static public string JobNextAction(SQLDMO_JOBSTEPACTION_TYPE oJobStepAction, int NextStep) {
    //  string RetVal = "";
    //  switch (oJobStepAction) {
    //    case SQLDMO_JOBSTEPACTION_TYPE.SQLDMOJobStepAction_GotoNextStep:
    //      RetVal = "Next step";
    //      break;
    //    case SQLDMO_JOBSTEPACTION_TYPE.SQLDMOJobStepAction_QuitWithSuccess:
    //      RetVal = "Quit with success";
    //      break;
    //    case SQLDMO_JOBSTEPACTION_TYPE.SQLDMOJobStepAction_QuitWithFailure:
    //      RetVal = "Quit with failure";
    //      break;
    //    case SQLDMO_JOBSTEPACTION_TYPE.SQLDMOJobStepAction_GotoStep:
    //      RetVal = "Goto step " + NextStep.ToString();
    //      break;
    //  }
    //  return RetVal;
    //}

    //static public string JobStatus(SQLDMO_JOBOUTCOME_TYPE oStatus) {
    //  switch (oStatus) {
    //    case SQLDMO_JOBOUTCOME_TYPE.SQLDMOJobOutcome_Cancelled:
    //      return "Cancelled";
    //    case SQLDMO_JOBOUTCOME_TYPE.SQLDMOJobOutcome_Failed:
    //      return "Failed";
    //    case SQLDMO_JOBOUTCOME_TYPE.SQLDMOJobOutcome_InProgress:
    //      return "In progress";
    //    case SQLDMO_JOBOUTCOME_TYPE.SQLDMOJobOutcome_Succeeded:
    //      return "Succeeded";
    //    default:
    //      return "Unknown";
    //  }
    //}

    static public string DbStatusText(DatabaseStatus oStatus) {
      return oStatus.ToString();
    }
    //static public string JobNotificationLevel(SQLDMO_COMPLETION_TYPE oLevel) {
    //  string RetVal;
    //  switch (oLevel) {
    //    case SQLDMO_COMPLETION_TYPE.SQLDMOComp_Failure:
    //      RetVal = "Failure";
    //      break;
    //    case SQLDMO_COMPLETION_TYPE.SQLDMOComp_Success:
    //      RetVal = "Success";
    //      break;
    //    case SQLDMO_COMPLETION_TYPE.SQLDMOComp_Always:
    //    case SQLDMO_COMPLETION_TYPE.SQLDMOComp_All:
    //      RetVal = "Always (success and failure)";
    //      break;
    //    case SQLDMO_COMPLETION_TYPE.SQLDMOComp_None:
    //      RetVal = "Never";
    //      break;
    //    case SQLDMO_COMPLETION_TYPE.SQLDMOComp_Unknown:
    //    default:
    //      RetVal = "Unknown";
    //      break;
    //  }
    //  return RetVal;
    //}

    static public string Int2DataType(int dataType) {
      IDictionary oDataType2String = new ListDictionary();
      string RetVal;
      // data types need to be validated. I found no valid source but personal comparison
      oDataType2String.Add(3, "Unsigned Int");
      oDataType2String.Add(8, "String");
      oDataType2String.Add(29, "Int");
      oDataType2String.Add(135, "DateTime");
      oDataType2String.Add(129, "Varchar");
      oDataType2String.Add(131, "Numeric");

      if (oDataType2String.Contains(dataType)) {
        RetVal = (string)oDataType2String[dataType];
      } else {
        RetVal = "Unknown type: " + dataType.ToString();
      }
      return RetVal;
    }

    //static public DataTable QR2DataTable(QueryResults2 oQuery) {
    //  DataTable oDT = new DataTable();
    //  #region Build Table
    //  for (int iCol = 1; iCol <= oQuery.Columns; iCol++) {
    //    switch (oQuery.get_ColumnType(iCol)) {

    //      case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeVarchar:
    //      case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeText:
    //      case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeUChar:
    //      case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeChar:
    //      case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeNText:
    //      case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeUnknown:
    //      case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeUVarchar:
    //        oDT.Columns.Add(oQuery.get_ColumnName(iCol), typeof(string));
    //        break;

    //      case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeInt1:
    //      case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeInt2:
    //      case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeInt4:
    //        oDT.Columns.Add(oQuery.get_ColumnName(iCol), typeof(int));
    //        break;

    //      case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeDateTime:
    //      case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeDateTime4:
    //        oDT.Columns.Add(oQuery.get_ColumnName(iCol), typeof(DateTime));
    //        break;

    //      case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeBit:
    //        oDT.Columns.Add(oQuery.get_ColumnName(iCol), typeof(bool));
    //        break;

    //      default:
    //        oDT.Columns.Add(oQuery.get_ColumnName(iCol));
    //        break;
    //    }

    //  }
    //  #endregion
    //  #region Fill Table
    //  for (int iRow = 1; iRow <= oQuery.Rows; iRow++) {
    //    DataRow oRow = oDT.NewRow();
    //    int iCol = 1;
    //    foreach (DataColumn oColumn in oDT.Columns) {

    //      switch (oColumn.DataType.ToString().ToLower()) {
    //        case "system.string":
    //          oRow[oColumn.ColumnName] = oQuery.GetColumnString(iRow, iCol++);
    //          break;
    //        case "system.int32":
    //          oRow[oColumn.ColumnName] = oQuery.GetColumnLong(iRow, iCol++);
    //          break;
    //        case "system.bool":
    //          oRow[oColumn.ColumnName] = oQuery.GetColumnBool(iRow, iCol++);
    //          break;
    //        case "system.datetime":
    //          oRow[oColumn.ColumnName] = oQuery.GetColumnDate(iRow, iCol++);
    //          break;
    //        default:
    //          oRow[oColumn.ColumnName] = oQuery.GetColumnString(iRow, iCol++);
    //          break;
    //      }
    //    }
    //    oDT.Rows.Add(oRow);
    //  }
    //  #endregion
    //  return oDT;
    //}

    //static public DataTable QR2DataTable(QueryResults oQuery) {
    //  return (QR2DataTable((QueryResults2)oQuery));
    //}

    //static public DataSet QR2DataSet(QueryResults2 oQuery) {
    //  DataSet oRetVal = new DataSet();
    //  for (int i = 1; i <= oQuery.ResultSets; i++) {
    //    oQuery.CurrentResultSet = i;
    //    DataTable oDT = new DataTable("RS" + i.ToString());
    //    for (int iCol = 1; iCol <= oQuery.Columns; iCol++) {
    //      switch (oQuery.get_ColumnType(iCol)) {
    //        case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeVarchar:
    //        case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeText:
    //        case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeUChar:
    //        case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeChar:
    //        case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeNText:
    //        case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeUnknown:
    //        case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeUVarchar:
    //          oDT.Columns.Add(oQuery.get_ColumnName(iCol), typeof(string));
    //          break;

    //        case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeInt1:
    //        case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeInt2:
    //        case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeInt4:
    //          oDT.Columns.Add(oQuery.get_ColumnName(iCol), typeof(int));
    //          break;

    //        case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeDateTime:
    //        case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeDateTime4:
    //          oDT.Columns.Add(oQuery.get_ColumnName(iCol), typeof(DateTime));
    //          break;

    //        case SQLDMO_QUERY_DATATYPE.SQLDMO_DTypeBit:
    //          oDT.Columns.Add(oQuery.get_ColumnName(iCol), typeof(bool));
    //          break;

    //        default:
    //          oDT.Columns.Add(oQuery.get_ColumnName(iCol));
    //          break;
    //      }

    //    }
    //    for (int iRow = 1; iRow <= oQuery.Rows; iRow++) {
    //      DataRow oRow = oDT.NewRow();
    //      int iCol = 1;
    //      foreach (DataColumn oColumn in oDT.Columns) {
    //        //Debug.WriteLine(oColumn.DataType.ToString().ToLower());
    //        switch (oColumn.DataType.ToString().ToLower()) {
    //          case "system.string":
    //            oRow[oColumn.ColumnName] = oQuery.GetColumnString(iRow, iCol++);
    //            break;
    //          case "system.int32":
    //            oRow[oColumn.ColumnName] = oQuery.GetColumnLong(iRow, iCol++);
    //            break;
    //          case "system.bool":
    //            oRow[oColumn.ColumnName] = oQuery.GetColumnBool(iRow, iCol++);
    //            break;
    //          case "system.datetime":
    //            oRow[oColumn.ColumnName] = oQuery.GetColumnDate(iRow, iCol++);
    //            break;
    //          default:
    //            oRow[oColumn.ColumnName] = oQuery.GetColumnString(iRow, iCol++);
    //            break;
    //        }
    //      }
    //      oDT.Rows.Add(oRow);
    //    }
    //    oRetVal.Tables.Add(oDT);
    //  }

    //  return oRetVal;
    //}

    //static public DataSet QR2DataSet(QueryResults oQuery) {
    //  return (QR2DataSet((QueryResults2)oQuery));
    //}

    static public string TypeOf2String(DatabaseObjectTypes objectType) {
      IDictionary oTypeOf2String = new ListDictionary();
      string RetVal;
      oTypeOf2String.Add(DatabaseObjectTypes.View, "View");
      oTypeOf2String.Add(DatabaseObjectTypes.Table, "User table");

      if (oTypeOf2String.Contains(objectType)) {
        RetVal = (string)oTypeOf2String[objectType];
      } else {
        RetVal = "Unknown type: " + objectType.ToString();
      }
      return RetVal;
    }
  }

}
