using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SeriousFunLauncher
{
    /// <summary>
    /// Installs and patches the official UO Classic client into a chosen folder.
    /// </summary>
    /// <remarks>
    /// EA's installer is a WinRAR self-extractor whose only game payload is data1\ (UO.exe, the
    /// EA patcher, plus two .cfg files). The patcher then downloads the actual game. So we extract
    /// data1\ with Windows' built-in tar.exe and drive the patcher ourselves, using its log file to
    /// tell when it is finished, because it stays open afterwards waiting for the user to hit Play.
    /// </remarks>
    public sealed class UoClientInstaller
    {
        private const string PatcherExe = "UO.exe";
        private const string ClientExe = "client.exe";
        private const string PatcherProductName = "EAMythic Patcher";

        // Rough size of a fully patched install, used only for the progress bar.
        private const long ExpectedInstallBytes = 2_700L * 1024 * 1024;
        private const long FreshInstallThresholdBytes = 100L * 1024 * 1024;

        private static readonly string[] InstallerPayload = { "data1/UO.exe", "data1/Uo.cfg", "data1/UO-patch.cfg" };
        private static readonly Regex InstallerLinkRegex =
            new Regex(@"https?://[^""'\s<>]+UOClassicSetup[^""'\s<>]*\.exe", RegexOptions.IgnoreCase);
        private static readonly Regex NothingToPatchRegex =
            new Regex(@"Nothing to patch for \[(?<product>[^\]]+)\]", RegexOptions.Compiled);

        private readonly StatusReporter _reporter;

        public UoClientInstaller(StatusReporter reporter)
        {
            _reporter = reporter;
        }

        /// <summary>True when the folder holds a patched client (the server needs these files too).</summary>
        public static bool IsInstalled(string directory) =>
            File.Exists(Path.Combine(directory, ClientExe)) && File.Exists(Path.Combine(directory, "tiledata.mul"));

        /// <summary>Returns the client.exe version as TazUO's settings.json expects it, e.g. "7.0.117.33".</summary>
        public static string GetClientVersion(string directory)
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(Path.Combine(directory, ClientExe));
            return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
        }

        public async Task InstallAsync(string directory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(directory);
            string tempDirectory = Downloader.CreateTempDirectory();

            try
            {
                _reporter.Status("Finding the latest UO Classic installer...");
                string installerUrl = await FindInstallerUrlAsync();
                string installerPath = Path.Combine(tempDirectory, Path.GetFileName(new Uri(installerUrl).LocalPath));

                _reporter.Status("Downloading the UO Classic installer...");
                await Downloader.DownloadFileAsync(installerUrl, installerPath, _reporter, cancellationToken);

                _reporter.Status("Unpacking the UO patcher...");
                _reporter.Progress(null);
                await ExtractPatcherAsync(installerPath, directory);
            }
            finally
            {
                Downloader.TryDeleteDirectory(tempDirectory);
            }

            await PatchAsync(directory, cancellationToken);
        }

        /// <summary>Runs EA's patcher until the game files are up to date, then closes it.</summary>
        public async Task PatchAsync(string directory, CancellationToken cancellationToken)
        {
            string logDirectory = Path.Combine(directory, "logs");
            Dictionary<string, long> logOffsets = SnapshotLogLengths(logDirectory);
            long startingSize = GetDirectorySize(directory);

            _reporter.Status("Starting the UO patcher...");
            _reporter.Progress(null);
            StartPatcher(directory);

            var log = new StringBuilder();
            long patchBytes = 0;
            DateTime lastSeenRunning = DateTime.UtcNow;

            try
            {
                while (true)
                {
                    await Task.Delay(1000, cancellationToken);
                    log.Append(ReadNewLogText(logDirectory, logOffsets));
                    string text = log.ToString();

                    Match size = Regex.Match(text, @"Patch size is (\d+)", RegexOptions.RightToLeft);
                    if (size.Success)
                    {
                        patchBytes = long.Parse(size.Groups[1].Value);
                    }

                    if (IsPatchFinished(text))
                    {
                        break;
                    }

                    ReportPatchProgress(directory, startingSize, patchBytes);

                    // The patcher restarts itself mid-run (UO.exe -> UO.bin), so only give up after a
                    // sustained period with none of its processes alive.
                    if (FindProcessesIn(directory).Any())
                    {
                        lastSeenRunning = DateTime.UtcNow;
                    }
                    else if (DateTime.UtcNow - lastSeenRunning > TimeSpan.FromSeconds(30))
                    {
                        throw new InvalidOperationException(
                            "The UO patcher closed before it finished. Check the logs folder in the UO directory.");
                    }
                }
            }
            finally
            {
                await Task.Delay(1500);
                StopProcessesIn(directory);
            }

            if (!IsInstalled(directory))
            {
                throw new InvalidOperationException("The UO patcher finished, but client.exe or tiledata.mul is missing.");
            }

            _reporter.Progress(1);
            _reporter.Status($"UO Classic client {GetClientVersion(directory)} is up to date.");
        }

        private static async Task<string> FindInstallerUrlAsync()
        {
            try
            {
                string page = await Downloader.Http.GetStringAsync(LauncherConstants.UoDownloadPage);
                Match match = InstallerLinkRegex.Match(page);
                if (match.Success)
                {
                    return match.Value;
                }
            }
            catch
            {
                // Fall through to the known installer.
            }

            return LauncherConstants.UoInstallerFallbackUrl;
        }

        private static async Task ExtractPatcherAsync(string installerPath, string directory)
        {
            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            var arguments = new StringBuilder($"-xf \"{installerPath}\" -C \"{directory}\" --strip-components 1");
            foreach (string entry in InstallerPayload)
            {
                arguments.Append($" \"{entry}\"");
            }

            var startInfo = new ProcessStartInfo(tar, arguments.ToString())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };

            using (Process process = Process.Start(startInfo))
            {
                string errors = await process.StandardError.ReadToEndAsync();
                process.WaitForExit();

                if (process.ExitCode != 0 || !File.Exists(Path.Combine(directory, PatcherExe)))
                {
                    throw new InvalidOperationException($"Could not unpack the UO installer: {errors.Trim()}");
                }
            }
        }

        private static void StartPatcher(string directory)
        {
            var startInfo = new ProcessStartInfo(Path.Combine(directory, PatcherExe))
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
            };

            // UO.exe's manifest demands admin rights, which it only needs for Program Files installs.
            // RunAsInvoker starts it (and the UO.bin it relaunches) as the current user: no UAC prompt,
            // and the launcher keeps the right to watch and close it.
            startInfo.EnvironmentVariables["__COMPAT_LAYER"] = "RunAsInvoker";
            Process.Start(startInfo)?.Dispose();
        }

        private static bool IsPatchFinished(string logText)
        {
            if (logText.Contains("Patch Operation Complete"))
            {
                return true;
            }

            // "Nothing to patch" is also logged for the patcher updating itself; only the game counts.
            return NothingToPatchRegex.Matches(logText)
                .Cast<Match>()
                .Any(match => match.Groups["product"].Value != PatcherProductName);
        }

        private void ReportPatchProgress(string directory, long startingSize, long patchBytes)
        {
            if (patchBytes <= 0)
            {
                _reporter.Progress(null);
                return;
            }

            // A fresh install grows toward a known size; an update rewrites files in place, so its
            // progress can't be measured from disk usage.
            if (startingSize >= FreshInstallThresholdBytes)
            {
                _reporter.Progress(null);
                _reporter.Status($"Updating UO game files ({patchBytes / (1024 * 1024):N0} MB)...");
                return;
            }

            long written = Math.Max(0, GetDirectorySize(directory) - startingSize);
            _reporter.Progress(Math.Min(0.99, (double)written / ExpectedInstallBytes));
            _reporter.Status($"Downloading UO game files... {written / (1024 * 1024):N0} MB");
        }

        private static Dictionary<string, long> SnapshotLogLengths(string logDirectory)
        {
            var lengths = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(logDirectory))
            {
                foreach (string file in Directory.GetFiles(logDirectory, "patcher*.log"))
                {
                    lengths[file] = new FileInfo(file).Length;
                }
            }

            return lengths;
        }

        /// <summary>Reads only text appended since the last call; the patcher appends to one log per day.</summary>
        private static string ReadNewLogText(string logDirectory, Dictionary<string, long> offsets)
        {
            if (!Directory.Exists(logDirectory))
            {
                return string.Empty;
            }

            var text = new StringBuilder();
            foreach (string file in Directory.GetFiles(logDirectory, "patcher*.log"))
            {
                offsets.TryGetValue(file, out long offset);

                try
                {
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        long length = stream.Length;
                        if (length <= offset)
                        {
                            continue;
                        }

                        var buffer = new byte[length - offset];
                        stream.Seek(offset, SeekOrigin.Begin);
                        int read = stream.Read(buffer, 0, buffer.Length);
                        text.Append(Encoding.UTF8.GetString(buffer, 0, read));
                        offsets[file] = offset + read;
                    }
                }
                catch (IOException)
                {
                    // Locked mid-write; pick it up next tick.
                }
            }

            return text.ToString();
        }

        private static long GetDirectorySize(string directory)
        {
            try
            {
                return new DirectoryInfo(directory)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(file => file.Length);
            }
            catch (IOException)
            {
                return 0;
            }
        }

        /// <summary>Processes whose executable lives in <paramref name="directory"/> (patcher, client).</summary>
        internal static List<Process> FindProcessesIn(string directory)
        {
            string root = Path.GetFullPath(directory).TrimEnd('\\') + "\\";
            var matches = new List<Process>();

            foreach (Process process in Process.GetProcesses())
            {
                string path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch
                {
                    // System and elevated processes are not readable; none of them are ours.
                }

                if (path != null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }

            return matches;
        }

        private static void StopProcessesIn(string directory)
        {
            foreach (Process process in FindProcessesIn(directory))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch
                {
                    // Already gone.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }
}
