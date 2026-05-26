using System;
using System.IO;
using UnityEngine;

// File logger voor multi-brain debugging.
// Schrijft naar <projectRoot>/Logs/multibrain-<timestamp>.log met
// AutoFlush=true zodat lines op disk staan ook als de scene freeze of crasht.
// Centraal entry-point: DebugLogger.Log(category, message). Doet OOK een
// Debug.Log(...) zodat de Unity console hetzelfde laat zien.
public static class DebugLogger
{
    private static StreamWriter writer;
    private static bool initialized;
    private static readonly object gate = new object();
    private static string logPath;

    public static string LogPath => logPath;

    private static void EnsureInit()
    {
        if (initialized) return;
        lock (gate)
        {
            if (initialized) return;
            initialized = true; // ook bij failure: niet retry-en

            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string logDir = Path.Combine(projectRoot, "Logs");
                Directory.CreateDirectory(logDir);

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                logPath = Path.Combine(logDir, $"multibrain-{stamp}.log");

                // append:true zodat meerdere helpers binnen één session kunnen
                // bijschrijven; AutoFlush voor freeze-resistente logging.
                writer = new StreamWriter(logPath, append: true) { AutoFlush = true };
                writer.WriteLine($"=== multi-brain log opened {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===");
                writer.WriteLine($"=== unity={Application.unityVersion} platform={Application.platform} ===");

                Application.quitting += Close;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("DebugLogger init failed: " + e.Message);
                writer = null;
            }
        }
    }

    public static void Log(string category, string message)
    {
        EnsureInit();

        int frame = 0;
        try { frame = Time.frameCount; } catch { /* niet op main thread */ }

        string line = $"[{DateTime.Now:HH:mm:ss.fff} f={frame}] [{category}] {message}";

        // Console
        UnityEngine.Debug.Log(line);

        // File
        if (writer == null) return;
        try
        {
            lock (gate)
            {
                writer?.WriteLine(line);
            }
        }
        catch { /* swallow — logger mag nooit zelf de game stoppen */ }
    }

    private static void Close()
    {
        lock (gate)
        {
            if (writer == null) return;
            try
            {
                writer.WriteLine($"=== closed {DateTime.Now:HH:mm:ss.fff} ===");
                writer.Flush();
                writer.Dispose();
            }
            catch { }
            writer = null;
        }
    }
}
