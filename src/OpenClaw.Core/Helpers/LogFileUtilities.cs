// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Text;

namespace OpenClaw.Helpers;

public static class LogFileUtilities
{
    public const int DefaultTailLineCount = 500;

    public static LogTailResult ReadLastLines(string path, int maxLines)
    {
        return ReadLastLines(path, maxLines, CancellationToken.None);
    }

    public static LogTailResult ReadLastLines(string path, int maxLines, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLines);
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096);
        if (stream.Length == 0)
        {
            return new LogTailResult([], 0);
        }

        stream.Seek(0, SeekOrigin.End);
        var lastByte = ReadByteAt(stream, stream.Length - 1);
        var buffer = new byte[4096];
        var tailBytes = new List<byte>();
        var position = stream.Length;
        var totalNewLines = 0;
        var capturedNewLines = 0;
        var shouldCaptureTail = maxLines > 0;

        while (position > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var readSize = (int)Math.Min(buffer.Length, position);
            position -= readSize;
            stream.Seek(position, SeekOrigin.Begin);
            var bytesRead = stream.Read(buffer, 0, readSize);

            for (var i = bytesRead - 1; i >= 0; i--)
            {
                var value = buffer[i];
                if (value == (byte)'\n')
                {
                    totalNewLines++;
                    if (shouldCaptureTail)
                    {
                        capturedNewLines++;
                        if (capturedNewLines > maxLines)
                        {
                            shouldCaptureTail = false;
                            continue;
                        }
                    }
                }

                if (shouldCaptureTail)
                {
                    tailBytes.Add(value);
                }
            }
        }

        var totalLineCount = totalNewLines + (lastByte == (byte)'\n' ? 0 : 1);
        if (maxLines == 0)
        {
            return new LogTailResult([], totalLineCount);
        }

        tailBytes.Reverse();
        var text = Encoding.UTF8.GetString(tailBytes.ToArray()).Replace("\r\n", "\n");
        var lines = text.Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count > maxLines)
        {
            lines = lines.Skip(lines.Count - maxLines).ToList();
        }

        return new LogTailResult(lines, totalLineCount);
    }

    private static byte ReadByteAt(FileStream stream, long position)
    {
        stream.Seek(position, SeekOrigin.Begin);
        var value = stream.ReadByte();
        return value < 0 ? (byte)0 : (byte)value;
    }

    public static int DeleteExpiredLogs(string logDirectory, TimeSpan retention, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(logDirectory) || !Directory.Exists(logDirectory))
        {
            return 0;
        }

        var cutoffUtc = now.Subtract(retention).UtcDateTime;
        var deleted = 0;

        foreach (var file in Directory.EnumerateFiles(logDirectory, "openclaw-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) >= cutoffUtc)
                {
                    continue;
                }

                File.Delete(file);
                deleted++;
            }
            catch
            {
                // Retention is best-effort; logging should never fail startup.
            }
        }

        return deleted;
    }
}

public readonly record struct LogTailResult(IReadOnlyList<string> Lines, int TotalLineCount)
{
    public bool WasTruncated => Lines.Count < TotalLineCount;
}
