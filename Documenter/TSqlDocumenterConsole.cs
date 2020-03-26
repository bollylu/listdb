using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Text;

namespace listdb {
  public class TSqlDocumenterConsole : TSqlDocumenterText {

    public TSqlDocumenterConsole(Server sqlServer) : base(sqlServer) {
      Output = x => Console.WriteLine(x);
    }
  }
}
