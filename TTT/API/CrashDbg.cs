namespace TTT.API;

// TEMP crash diagnostics. Writes flushed breadcrumbs to a file so the last lines
// survive a native crash (independent of the CSS/Console logging pipeline).
// Read with: docker exec cs2-ttt-dev cat /tmp/ttt-crashdbg.log
// REMOVE once the crash is located.
public static class CrashDbg {
  private const           string Path = "/tmp/ttt-crashdbg.log";
  private static readonly object Lock = new();

  public static void Crumb(string message) {
    try {
      lock (Lock)
        System.IO.File.AppendAllText(Path,
          $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
    } catch {
      // diagnostics must never throw
    }
  }
}
