using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Text;
using static listdb.ASqlDocumenter;

namespace listdb {
  interface ISqlDocumenter {

    void DocumentConfig(EDocumentConfigType configType);

    void DocumentUsers(EDocumentUsersType usersType);

    void DocumentDatabases(EDocumentDatabasesType databasesType, ISelectionCriterias criterias);
    void DocumentTables(Database database, EDocumentTablesType tablesType, ISelectionCriterias criterias);

  }
}
