using BLTools.Diagnostic.Logging;
using BLTools.Text;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Text;

namespace listdb {

  public abstract class ASqlDocumenter : ALoggable, ISqlDocumenter {

    protected Server _SqlServer;

    public enum EDocumentConfigType {
      Full,
      List
    }

    public enum EDocumentUsersType {
      Full,
      List
    }

    public enum EDocumentTablesType {
      Full,
      List,
      Indexes
    }

    public abstract void DocumentConfig(EDocumentConfigType configType);
    public abstract void DocumentUsers(EDocumentUsersType usersType);
    public abstract void DocumentTables(Database database, EDocumentTablesType tablesType, bool userOnly = false);
    public abstract void DocumentTables(Database database, EDocumentTablesType tablesType, IEnumerable<string> tableFilter, bool userOnly = false);

    protected string MakeSectionTitle(string title) {
      return TextBox.BuildHorizontalRowWithText($" {title} ", 120, TextBox.EHorizontalRowType.Single);
    }
    protected string MakeSectionFooter(string footer = "") {
      if (footer == "") {
        return TextBox.BuildHorizontalRowWithText("", 120, TextBox.EHorizontalRowType.Double);
      } else {
        return TextBox.BuildHorizontalRowWithText($" {footer} ", 120, TextBox.EHorizontalRowType.Double);
      }
    }

  }
  public abstract class ASqlDocumenter<T> : ASqlDocumenter {

    public Action<T> Output { get; set; }

  }
}
