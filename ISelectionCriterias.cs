using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace listdb {
  public interface ISelectionCriterias {
    List<string> Filter { get; }
    Regex RegexFilter { get; }
    bool SelectSystemData { get; }
    bool SelectUserData { get; }
  }
}
