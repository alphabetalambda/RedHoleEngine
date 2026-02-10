using System.Diagnostics;

namespace RedHoleEngine.Editor.Project;

/// <summary>
/// Build configuration options
/// </summary>
public enum BuildConfiguration
{
    Debug,
    Release
}

/// <summary>
/// Result of a build operation
/// </summary>
public class BuildResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Output { get; set; } = "";
    public string ErrorOutput { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
}

/// <summary>
/// Handles building game projects from the editor
/// </summary>
public class BuildSystem
{
    private readonly ProjectManager _projectManager;
    
    private Process? _currentBuildProcess;
    private bool _isBuildInProgress;
    private BuildResult? _lastBuildResult;
    
    /// <summary>
    /// Whether a build is currently in progress
    /// </summary>
    public bool IsBuildInProgress => _isBuildInProgress;
    
    /// <summary>
    /// Last build result
    /// </summary>
    public BuildResult? LastBuildResult => _lastBuildResult;
    
    /// <summary>
    /// Event raised when build starts
    /// </summary>
    public event Action? BuildStarted;
    
    /// <summary>
    /// Event raised when build completes
    /// </summary>
    public event Action<BuildResult>? BuildCompleted;
    
    /// <summary>
    /// Event raised when build output is received
    /// </summary>
    public event Action<string, bool>? OutputReceived;

    public BuildSystem(ProjectManager projectManager)
    {
        _projectManager = projectManager;
    }

    /// <summary>
    /// Builds the current project
    /// </summary>
    public async Task<BuildResult> BuildAsync(BuildConfiguration configuration = BuildConfiguration.Debug)
    {
        if (_isBuildInProgress)
        {
            return new BuildResult
            {
                Success = false,
                ErrorOutput = "A build is already in progress."
            };
        }

        var csProjectPath = _projectManager.GetCsProjectPath();
        if (string.IsNullOrEmpty(csProjectPath) || !File.Exists(csProjectPath))
        {
            return new BuildResult
            {
                Success = false,
                ErrorOutput = "No C# project found. Configure the project path in Project Settings."
            };
        }

        _isBuildInProgress = true;
        BuildStarted?.Invoke();

        var stopwatch = Stopwatch.StartNew();
        var result = new BuildResult();

        try
        {
            Console.WriteLine($"[BUILD] Starting build: {Path.GetFileName(csProjectPath)}");
            Console.WriteLine($"[BUILD] Configuration: {configuration}");

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{csProjectPath}\" --configuration {configuration}",
                WorkingDirectory = Path.GetDirectoryName(csProjectPath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            _currentBuildProcess = process;

            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                    ProcessOutputLine(e.Data, result);
                    OutputReceived?.Invoke(e.Data, false);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                    ProcessOutputLine(e.Data, result);
                    OutputReceived?.Invoke(e.Data, true);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            stopwatch.Stop();

            result.ExitCode = process.ExitCode;
            result.Success = process.ExitCode == 0;
            result.Output = outputBuilder.ToString();
            result.ErrorOutput = errorBuilder.ToString();
            result.Duration = stopwatch.Elapsed;

            if (result.Success)
            {
                Console.WriteLine($"[BUILD] Build succeeded in {result.Duration.TotalSeconds:F1}s ({result.WarningCount} warnings)");
            }
            else
            {
                Console.WriteLine($"[BUILD] Build FAILED with {result.ErrorCount} error(s) and {result.WarningCount} warning(s)");
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Success = false;
            result.ErrorOutput = ex.Message;
            result.Duration = stopwatch.Elapsed;
            Console.WriteLine($"[BUILD] Build failed: {ex.Message}");
        }
        finally
        {
            _currentBuildProcess = null;
            _isBuildInProgress = false;
            _lastBuildResult = result;
            BuildCompleted?.Invoke(result);
        }

        return result;
    }

    /// <summary>
    /// Cleans the build output
    /// </summary>
    public async Task<BuildResult> CleanAsync()
    {
        if (_isBuildInProgress)
        {
            return new BuildResult
            {
                Success = false,
                ErrorOutput = "A build is already in progress."
            };
        }

        var csProjectPath = _projectManager.GetCsProjectPath();
        if (string.IsNullOrEmpty(csProjectPath) || !File.Exists(csProjectPath))
        {
            return new BuildResult
            {
                Success = false,
                ErrorOutput = "No C# project found."
            };
        }

        _isBuildInProgress = true;
        BuildStarted?.Invoke();

        var stopwatch = Stopwatch.StartNew();
        var result = new BuildResult();

        try
        {
            Console.WriteLine($"[BUILD] Cleaning: {Path.GetFileName(csProjectPath)}");

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"clean \"{csProjectPath}\"",
                WorkingDirectory = Path.GetDirectoryName(csProjectPath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            _currentBuildProcess = process;

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    OutputReceived?.Invoke(e.Data, false);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            stopwatch.Stop();

            result.ExitCode = process.ExitCode;
            result.Success = process.ExitCode == 0;
            result.Duration = stopwatch.Elapsed;

            Console.WriteLine(result.Success ? "[BUILD] Clean succeeded" : "[BUILD] Clean failed");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Success = false;
            result.ErrorOutput = ex.Message;
            result.Duration = stopwatch.Elapsed;
            Console.WriteLine($"[BUILD] Clean failed: {ex.Message}");
        }
        finally
        {
            _currentBuildProcess = null;
            _isBuildInProgress = false;
            BuildCompleted?.Invoke(result);
        }

        return result;
    }

    /// <summary>
    /// Rebuilds the project (clean + build)
    /// </summary>
    public async Task<BuildResult> RebuildAsync(BuildConfiguration configuration = BuildConfiguration.Debug)
    {
        var cleanResult = await CleanAsync();
        if (!cleanResult.Success)
        {
            return cleanResult;
        }

        return await BuildAsync(configuration);
    }

    /// <summary>
    /// Cancels the current build
    /// </summary>
    public void CancelBuild()
    {
        if (_currentBuildProcess != null && !_currentBuildProcess.HasExited)
        {
            try
            {
                _currentBuildProcess.Kill(entireProcessTree: true);
                Console.WriteLine("[BUILD] Build cancelled by user");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BUILD] Failed to cancel build: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Restores NuGet packages
    /// </summary>
    public async Task<BuildResult> RestoreAsync()
    {
        if (_isBuildInProgress)
        {
            return new BuildResult
            {
                Success = false,
                ErrorOutput = "A build is already in progress."
            };
        }

        var csProjectPath = _projectManager.GetCsProjectPath();
        if (string.IsNullOrEmpty(csProjectPath) || !File.Exists(csProjectPath))
        {
            return new BuildResult
            {
                Success = false,
                ErrorOutput = "No C# project found."
            };
        }

        _isBuildInProgress = true;
        BuildStarted?.Invoke();

        var stopwatch = Stopwatch.StartNew();
        var result = new BuildResult();

        try
        {
            Console.WriteLine($"[BUILD] Restoring packages: {Path.GetFileName(csProjectPath)}");

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"restore \"{csProjectPath}\"",
                WorkingDirectory = Path.GetDirectoryName(csProjectPath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            _currentBuildProcess = process;

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    OutputReceived?.Invoke(e.Data, false);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            stopwatch.Stop();

            result.ExitCode = process.ExitCode;
            result.Success = process.ExitCode == 0;
            result.Duration = stopwatch.Elapsed;

            Console.WriteLine(result.Success ? "[BUILD] Restore succeeded" : "[BUILD] Restore failed");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Success = false;
            result.ErrorOutput = ex.Message;
            result.Duration = stopwatch.Elapsed;
            Console.WriteLine($"[BUILD] Restore failed: {ex.Message}");
        }
        finally
        {
            _currentBuildProcess = null;
            _isBuildInProgress = false;
            BuildCompleted?.Invoke(result);
        }

        return result;
    }

    private void ProcessOutputLine(string line, BuildResult result)
    {
        // Count errors and warnings from MSBuild output
        if (line.Contains(": error ", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(": error CS", StringComparison.OrdinalIgnoreCase))
        {
            result.ErrorCount++;
            Console.WriteLine($"error: {line}");
        }
        else if (line.Contains(": warning ", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains(": warning CS", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains(": warning NU", StringComparison.OrdinalIgnoreCase))
        {
            result.WarningCount++;
            Console.WriteLine($"warning: {line}");
        }
        else if (!string.IsNullOrWhiteSpace(line))
        {
            // Regular output - only log important lines
            if (line.Contains("Build succeeded") || 
                line.Contains("Build FAILED") ||
                line.Contains("->") ||
                line.StartsWith("  "))
            {
                Console.WriteLine(line);
            }
        }
    }

    /// <summary>
    /// Gets the output assembly path for the current project
    /// </summary>
    public string? GetOutputAssemblyPath(BuildConfiguration configuration = BuildConfiguration.Debug)
    {
        var csProjectPath = _projectManager.GetCsProjectPath();
        if (string.IsNullOrEmpty(csProjectPath)) return null;

        var projectDir = Path.GetDirectoryName(csProjectPath);
        var projectName = Path.GetFileNameWithoutExtension(csProjectPath);
        var framework = _projectManager.CurrentProject?.Build.TargetFramework ?? "net9.0";

        return Path.Combine(projectDir!, "bin", configuration.ToString(), framework, projectName + ".dll");
    }

    /// <summary>
    /// Checks if the output assembly exists and is up to date
    /// </summary>
    public bool IsOutputUpToDate(BuildConfiguration configuration = BuildConfiguration.Debug)
    {
        var outputPath = GetOutputAssemblyPath(configuration);
        if (string.IsNullOrEmpty(outputPath) || !File.Exists(outputPath))
            return false;

        var csProjectPath = _projectManager.GetCsProjectPath();
        if (string.IsNullOrEmpty(csProjectPath) || !File.Exists(csProjectPath))
            return false;

        var outputTime = File.GetLastWriteTimeUtc(outputPath);
        var projectTime = File.GetLastWriteTimeUtc(csProjectPath);

        return outputTime > projectTime;
    }
}
