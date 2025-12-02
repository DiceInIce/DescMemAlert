using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MemAlerts.Client.Services;

public class VideoDownloaderService
{
    private const string YtDlpFileName = "yt-dlp.exe";
    private const string DesktopUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    private const string AndroidUserAgent = "Mozilla/5.0 (Linux; Android 11; Pixel 5 Build/RQ3A.210805.001.A1; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/120.0.6099.230 Mobile Safari/537.36";

    public bool IsDownloaderAvailable()
    {
        // Check in the same directory as the executable
        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var exeDir = Path.GetDirectoryName(exePath);
        var ytDlpPath = Path.Combine(exeDir ?? AppDomain.CurrentDomain.BaseDirectory, YtDlpFileName);
        
        if (File.Exists(ytDlpPath))
            return true;

        // Also check BaseDirectory (for development scenarios)
        if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, YtDlpFileName)))
            return true;

        // Check PATH as fallback
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = YtDlpFileName,
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> DownloadVideoAsync(string url, string outputDirectory)
    {
        if (!IsDownloaderAvailable())
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var exeDir = Path.GetDirectoryName(exePath);
            throw new FileNotFoundException($"Не найден {YtDlpFileName}. Пожалуйста, скачайте yt-dlp.exe и поместите его в папку: {exeDir}");
        }

        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        // Find yt-dlp.exe path
        var exePath2 = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var exeDir2 = Path.GetDirectoryName(exePath2);
        var ytDlpPath = Path.Combine(exeDir2 ?? AppDomain.CurrentDomain.BaseDirectory, YtDlpFileName);
        
        if (!File.Exists(ytDlpPath))
        {
            ytDlpPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, YtDlpFileName);
            if (!File.Exists(ytDlpPath))
            {
                ytDlpPath = YtDlpFileName; // Fallback to PATH
            }
        }

        // Template for filename: "id.ext"
        var outputTemplate = Path.Combine(outputDirectory, "%(id)s.%(ext)s");
        
        var isYouTube = url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || 
                        url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
        var userAgent = isYouTube ? AndroidUserAgent : DesktopUserAgent;
        
        // First, get the expected filename using --print to ensure we can find the file later
        var getFilenameArgs = new StringBuilder();
        AppendArgument(getFilenameArgs, "--print \"%(id)s.%(ext)s\"");
        AppendArgument(getFilenameArgs, "--no-playlist");
        AppendArgument(getFilenameArgs, $"--user-agent \"{userAgent}\"");
        
        if (isYouTube)
        {
            AppendArgument(getFilenameArgs, "--referer \"https://www.youtube.com/\"");
            AppendArgument(getFilenameArgs, "--extractor-args \"youtube:player_client=android\"");
        }
        
        AppendArgument(getFilenameArgs, $"\"{url}\"");
        
        var filenameInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            Arguments = getFilenameArgs.ToString(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        
        string? expectedFilename = null;
        using (var filenameProcess = new Process { StartInfo = filenameInfo })
        {
            filenameProcess.Start();
            var output = await filenameProcess.StandardOutput.ReadToEndAsync();
            await filenameProcess.WaitForExitAsync();
            if (filenameProcess.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                expectedFilename = output.Trim();
            }
        }
        
        // Build arguments for yt-dlp
        var arguments = new System.Text.StringBuilder();
        
        // Anti-bot detection measures:
        // Use a modern browser user agent to avoid detection
        AppendArgument(arguments, $"--user-agent \"{userAgent}\"");
        
        // Add referer header to make requests look more legitimate
        if (isYouTube)
        {
            AppendArgument(arguments, "--referer \"https://www.youtube.com/\"");
        }
        
        if (isYouTube)
        {
            // Force Android client to bypass SABR restrictions without needing an external JS runtime
            AppendArgument(arguments, "-f \"bestvideo+bestaudio/best\"");
            AppendArgument(arguments, "--extractor-args \"youtube:player_client=android\"");
        }
        else
        {
            // For other platforms, prefer separate streams for better quality
            AppendArgument(arguments, "-f \"bestvideo+bestaudio/best\"");
        }
        
        // Common options:
        // --merge-output-format mp4: Ensure merged output is mp4 container
        // --no-playlist: Download only single video
        // --no-part: Don't use .part files (ensures complete file)
        // --progress: Show progress (helps with debugging)
        AppendArgument(arguments, "--merge-output-format mp4");
        AppendArgument(arguments, "--no-playlist");
        AppendArgument(arguments, "--no-part");
        
        AppendArgument(arguments, $"-o \"{outputTemplate}\"");
        AppendArgument(arguments, $"\"{url}\"");
        
        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            Arguments = arguments.ToString(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };

        // We need to capture the filename to return it
        string? downloadedFilePath = null;

        using var process = new Process();
        process.StartInfo = startInfo;
        
        var errorOutput = new StringBuilder();
        
        process.OutputDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            
            // Look for final destination file
            if (e.Data.Contains("[download] Destination:") || e.Data.Contains("[Merger] Merging formats into"))
            {
                var parts = e.Data.Split(':', 2);
                if (parts.Length > 1)
                {
                    var path = parts[1].Trim().Replace("\"", "").Trim();
                    if (File.Exists(path))
                    {
                        downloadedFilePath = path;
                    }
                }
            }
            // Look for "has already been downloaded" message
            else if (e.Data.Contains("[download]") && e.Data.Contains("has already been downloaded"))
            {
                // Extract filename from message like "[download] filename.mp4 has already been downloaded"
                var match = Regex.Match(e.Data, @"\[download\]\s+(.+?)\s+has already been downloaded");
                if (match.Success)
                {
                    var filename = match.Groups[1].Value.Trim();
                    var path = Path.Combine(outputDirectory, filename);
                    if (File.Exists(path))
                    {
                        downloadedFilePath = path;
                    }
                }
            }
        };
        
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorOutput.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine(); // Consume error stream to prevent deadlock

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var errorMsg = errorOutput.Length > 0 ? errorOutput.ToString() : "Неизвестная ошибка";
            throw new Exception($"Ошибка загрузки (код {process.ExitCode}): {errorMsg}");
        }

        // Try to find the downloaded file using multiple methods
        string? finalPath = null;
        
        // Method 1: Use path from output parsing
        if (downloadedFilePath != null && File.Exists(downloadedFilePath))
        {
            finalPath = downloadedFilePath;
        }
        // Method 2: Use expected filename if we got it from --print
        else if (!string.IsNullOrEmpty(expectedFilename))
        {
            var expectedPath = Path.Combine(outputDirectory, expectedFilename);
            if (File.Exists(expectedPath))
            {
                finalPath = expectedPath;
            }
        }
        
        // Method 3: Find most recently created file in the directory
        if (finalPath == null)
        {
            var dirInfo = new DirectoryInfo(outputDirectory);
            var files = dirInfo.GetFiles("*.mp4", SearchOption.TopDirectoryOnly);
            if (files.Length > 0)
            {
                // Get the most recently modified file
                var newestFile = files.OrderByDescending(f => f.LastWriteTime).First();
                finalPath = newestFile.FullName;
            }
        }

        // Verify the file exists and is not empty
        if (finalPath != null && File.Exists(finalPath))
        {
            var fileInfo = new FileInfo(finalPath);
            // Check if file is not empty (at least 1KB)
            if (fileInfo.Length < 1024)
            {
                throw new Exception($"Скачанный файл слишком мал ({fileInfo.Length} байт). Возможно, загрузка не завершена.");
            }
            return finalPath;
        }

        throw new Exception($"Не удалось найти скачанный файл в папке: {outputDirectory}");
    }
    
    private static void AppendArgument(StringBuilder builder, string argument)
    {
        if (builder.Length > 0)
        {
            builder.Append(' ');
        }
        builder.Append(argument);
    }
}

