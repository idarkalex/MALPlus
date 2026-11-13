using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;

namespace MALPlus.Services;

/// <summary>
/// Provides a 100% local, privacy-first rolling file logger and diagnostic exporter.
/// Zero data is transmitted to external telemetry servers.
/// </summary>
public static class AppLog
{
    private const string Tag = "MALPLUS";
    private const long MaxLogFileSizeBytes = 2 * 1024 * 1024; // 2 MB
    private static readonly object WriteLock = new();

    private static string LogsDirectory => Path.Combine(FileSystem.AppDataDirectory, "logs");
    private static string CurrentLogPath => Path.Combine(LogsDirectory, "malplus.log");
    private static string RolledLogPath => Path.Combine(LogsDirectory, "malplus.log.1");
    private static string CrashLogPath => Path.Combine(FileSystem.CacheDirectory, "crash_log.txt");

    private static readonly Regex TokenMaskRegex = new(
        @"(Bearer\s+[A-Za-z0-9\-._~+/]+=*)|(token[""':\s]+[A-Za-z0-9\-._~+/]{8,})|(password[""':\s]+[^""'\s,]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void Info(string source, string message) => Write("INFO", source, message);
    public static void Warn(string source, string message) => Write("WARN", source, message);
    public static void Debug(string source, string message) => Write("DEBUG", source, message);
    public static void Error(string source, string message, Exception ex = null)
    {
        var full = ex != null ? $"{message}{Environment.NewLine}{ex}" : message;
        Write("ERROR", source, full);
    }

    private static void Write(string level, string source, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var sanitized = Sanitize(message);
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{source}] {sanitized}";

#if ANDROID
        try
        {
            if (level == "ERROR")
                global::Android.Util.Log.Error(Tag, $"[{source}] {sanitized}");
            else if (level == "WARN")
                global::Android.Util.Log.Warn(Tag, $"[{source}] {sanitized}");
            else
                global::Android.Util.Log.Info(Tag, $"[{source}] {sanitized}");
        }
        catch { }
#else
        Console.WriteLine($"{Tag}: {line}");
#endif

        Task.Run(() =>
        {
            lock (WriteLock)
            {
                try
                {
                    if (!Directory.Exists(LogsDirectory))
                        Directory.CreateDirectory(LogsDirectory);

                    var fileInfo = new FileInfo(CurrentLogPath);
                    if (fileInfo.Exists && fileInfo.Length >= MaxLogFileSizeBytes)
                    {
                        if (File.Exists(RolledLogPath))
                            File.Delete(RolledLogPath);
                        File.Move(CurrentLogPath, RolledLogPath);
                    }

                    using var writer = File.AppendText(CurrentLogPath);
                    writer.WriteLine(line);
                }
                catch
                {
                    // Logging should never throw or crash the app.
                }
            }
        });
    }

    private static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;
        return TokenMaskRegex.Replace(input, "***REDACTED***");
    }

    public static string GetLogSizeSummary()
    {
        lock (WriteLock)
        {
            try
            {
                long totalBytes = 0;
                if (File.Exists(CurrentLogPath))
                    totalBytes += new FileInfo(CurrentLogPath).Length;
                if (File.Exists(RolledLogPath))
                    totalBytes += new FileInfo(RolledLogPath).Length;
                if (File.Exists(CrashLogPath))
                    totalBytes += new FileInfo(CrashLogPath).Length;

                if (totalBytes <= 0)
                    return "No local logs recorded yet";

                if (totalBytes < 1024)
                    return $"{totalBytes} B on disk (100% local)";
                if (totalBytes < 1024 * 1024)
                    return $"{totalBytes / 1024.0:F1} KB on disk (100% local)";
                return $"{totalBytes / (1024.0 * 1024.0):F2} MB on disk (100% local)";
            }
            catch
            {
                return "Local logs active";
            }
        }
    }

    public static void ClearLogs()
    {
        lock (WriteLock)
        {
            try
            {
                if (File.Exists(CurrentLogPath))
                    File.Delete(CurrentLogPath);
                if (File.Exists(RolledLogPath))
                    File.Delete(RolledLogPath);
                if (File.Exists(CrashLogPath))
                    File.Delete(CrashLogPath);
            }
            catch { }
        }
        Info("Diagnostics", "Logs cleared by user.");
    }

    public static async Task<bool> ExportDiagnosticsAsync()
    {
        string exportFile = null;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine("           MAL+ DIAGNOSTIC REPORT (100% LOCAL)     ");
            sb.AppendLine("==================================================");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Local)");
            sb.AppendLine($"App Version: {AppInfo.VersionString} (Build: {AppInfo.BuildString})");
            sb.AppendLine($"App Name: {AppInfo.Name} ({AppInfo.PackageName})");
            sb.AppendLine();
            sb.AppendLine("--- DEVICE INFORMATION ---");
            sb.AppendLine($"Manufacturer: {DeviceInfo.Manufacturer}");
            sb.AppendLine($"Model: {DeviceInfo.Model}");
            sb.AppendLine($"Platform: {DeviceInfo.Platform} {DeviceInfo.VersionString}");
            sb.AppendLine($"Device Type: {DeviceInfo.DeviceType}");
            sb.AppendLine($"Idiom: {DeviceInfo.Idiom}");
            try
            {
                var display = DeviceDisplay.MainDisplayInfo;
                sb.AppendLine($"Display: {display.Width}x{display.Height} @ {display.Density:F2}x ({display.Orientation})");
            }
            catch { }
            sb.AppendLine();

            sb.AppendLine("--- APP STATE ---");
            sb.AppendLine($"Authenticated: {XShared.Utils.Credentials.Authenticated}");
            sb.AppendLine($"User: {(!string.IsNullOrEmpty(XShared.Utils.Credentials.UserName) ? XShared.Utils.Credentials.UserName : "<none>")}");
            sb.AppendLine($"Has Stored Vault: {XShared.Utils.Credentials.HasStoredAccount()}");
            sb.AppendLine($"Default Menu Tab: {XShared.Utils.Settings.DefaultMenuTab}");
            sb.AppendLine();

            lock (WriteLock)
            {
                if (File.Exists(CrashLogPath))
                {
                    sb.AppendLine("--- CRASH LOGS ---");
                    try
                    {
                        sb.AppendLine(File.ReadAllText(CrashLogPath));
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"Could not read crash_log.txt: {ex.Message}");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("--- APPLICATION LOGS (TAIL) ---");
                if (File.Exists(RolledLogPath))
                {
                    try
                    {
                        var rolledLines = File.ReadAllLines(RolledLogPath);
                        var takeCount = Math.Min(rolledLines.Length, 300);
                        sb.AppendLine($"[Older log segment: last {takeCount} lines]");
                        for (int i = rolledLines.Length - takeCount; i < rolledLines.Length; i++)
                            sb.AppendLine(rolledLines[i]);
                    }
                    catch { }
                }

                if (File.Exists(CurrentLogPath))
                {
                    try
                    {
                        var lines = File.ReadAllLines(CurrentLogPath);
                        var takeCount = Math.Min(lines.Length, 1500);
                        sb.AppendLine($"[Current log segment: last {takeCount} lines]");
                        for (int i = lines.Length - takeCount; i < lines.Length; i++)
                            sb.AppendLine(lines[i]);
                    }
                    catch { }
                }
                else
                {
                    sb.AppendLine("No application log file found on disk.");
                }
            }

            exportFile = Path.Combine(FileSystem.CacheDirectory, $"malplus_support_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            await File.WriteAllTextAsync(exportFile, sb.ToString());

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Registros de diagnóstico MAL+",
                File = new ShareFile(exportFile)
            });

            return true;
        }
        catch (Exception ex)
        {
            Error("Diagnostics", "ExportDiagnosticsAsync failed", ex);
            return false;
        }
    }
}
