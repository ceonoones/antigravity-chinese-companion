using System;
using System.Threading;
static class StopCompanion {
  [STAThread] static void Main() {
    try {
      using (var signal=EventWaitHandle.OpenExisting("Local\\AntigravityChineseCompanion_Stop_"+Environment.UserName))signal.Set();
    } catch (WaitHandleCannotBeOpenedException) { }
  }
}
