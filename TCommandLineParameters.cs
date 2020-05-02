using System;
using System.Collections.Generic;
using System.Text;
using BLTools;

namespace listdb {
  public class TCommandLineParameters {

    public SplitArgs Args { get; private set; }

    public TCommandLineParameters(SplitArgs args) {
      Args = args;
    }

  }
}
