using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Text;
using static listdb.ASqlDocumenter;

namespace listdb {
  interface ISqlDocumenter {

    void DocumentConfig(EDocumentConfigType configType);

    void DocumentUsers(EDocumentUsersType usersType);

    void DocumentTables(Database database, EDocumentTablesType tablesType, bool userOnly = false);
    void DocumentTables(Database database, EDocumentTablesType tablesType, IEnumerable<string> tableFilter, bool userOnly = false);

  }
}
