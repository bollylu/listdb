using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace listdb {
  public class TSelectionCriterias : ISelectionCriterias {
    public List<string> Filter { get; } = new List<string>();
    public Regex RegexFilter { get; set; }
    public bool SelectSystemData { get; set; }
    public bool SelectUserData { get; set; }
  }
}
