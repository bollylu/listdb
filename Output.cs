using System;
using System.Collections.Generic;
using System.Text;

namespace listdb {
  public static class OutputObject {

    public static Action<string> Output { get; set; } = new Action<string>(x => Console.Write(x));
    public static Action<string> OutputLine { get; set; } = new Action<string>(x => Console.WriteLine(x));

    public static void Write(string text = "") {
      Output(text);
    }

    public static void WriteLine(string text = "") {
      OutputLine(text);
    }
  }
}
