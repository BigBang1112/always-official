using System.Text;

namespace AlwaysOfficial.Core;

public static class Log
{
    private static readonly object gate = new();
    private static StreamWriter? writer;
    private static readonly Queue<string> pending = new();
    private static Timer? initializationTimer;

    public static string DataDir { get; private set; } = "";

    internal static void Init(string modName)
    {
        if (!UserData.SupportedLayout())
        {
            return;
        }

        initializationTimer = new Timer(_ => TryInitialize(modName), null, Timeout.Infinite, Timeout.Infinite);
        initializationTimer.Change(0, 250);
    }

    private static void TryInitialize(string modName)
    {
        lock (gate)
        {
            if (writer != null)
            {
                return;
            }

            var directory = UserData.GetDirectory();
            if (directory == null)
            {
                return;
            }

            try
            {
                Open(directory, modName);

                while (pending.TryDequeue(out var line))
                {
                    writer!.WriteLine(line);
                }

                initializationTimer?.Dispose();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                initializationTimer?.Dispose();
                Win32.OutputDebugString($"{ModInfo.DisplayName}: cannot open log: {e.Message}\n");
            }
        }
    }

    private static void Open(string directory, string modName)
    {
        DataDir = directory;

        var path = Path.Combine(DataDir, modName + ".log");
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public static void Write(string message)
    {
        Win32.GetLocalTime(out var time);
        var line = $"[{time.Year:D4}-{time.Month:D2}-{time.Day:D2} {time.Hour:D2}:{time.Minute:D2}:{time.Second:D2}.{time.Milliseconds:D3}] {message}";

        lock (gate)
        {
            if (writer != null)
            {
                writer.WriteLine(line);
            }
            else
            {
                if (pending.Count == 256)
                {
                    pending.Dequeue();
                }
                pending.Enqueue(line);
            }
        }

        Win32.OutputDebugString(line + "\n");
    }

    public static void Error(string where, Exception e)
    {
        Write($"ERROR in {where}: {e}");
    }
}
