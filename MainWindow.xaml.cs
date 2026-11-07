using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SeriousFunLauncher
{
    public partial class MainWindow : Window
    {
        private readonly LauncherConfig _config = LauncherConfig.Load();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly StatusReporter _reporter;
        private bool _busy;

        public MainWindow()
        {
            InitializeComponent();

            _reporter = new StatusReporter(
                new Progress<string>(message => StatusText.Text = message),
                new Progress<double?>(ShowProgress)
            );
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ServerHostBox.Text = _config.ServerHost;
            ServerPortBox.Text = _config.ServerPort.ToString();
            UoDirectoryBox.Text = _config.UoDirectory;
            TazUoDirectoryBox.Text = _config.TazUoDirectory;
            CheckClientUpdatesBox.IsChecked = _config.CheckClientUpdates;
            CloseOnLaunchBox.IsChecked = _config.CloseOnLaunch;
            RefreshVersions();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_busy && MessageBox.Show(
                    this,
                    "A download is in progress. Quit anyway?",
                    Title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            _cancellation.Cancel();
        }

        private void OnBrowseUo(object sender, RoutedEventArgs e) => BrowseInto(UoDirectoryBox, "Folder for the UO Classic client");

        private void OnBrowseTazUo(object sender, RoutedEventArgs e) => BrowseInto(TazUoDirectoryBox, "Folder for TazUO");

        private async void OnPlay(object sender, RoutedEventArgs e) => await RunAsync(PlayAsync);

        private async void OnRepair(object sender, RoutedEventArgs e) => await RunAsync(RepairClientAsync);

        private async Task PlayAsync(CancellationToken cancellationToken)
        {
            var client = new UoClientInstaller(_reporter);

            if (!UoClientInstaller.IsInstalled(_config.UoDirectory))
            {
                await client.InstallAsync(_config.UoDirectory, cancellationToken);
            }
            else if (_config.CheckClientUpdates)
            {
                await client.PatchAsync(_config.UoDirectory, cancellationToken);
            }

            await new TazUoUpdater(_reporter).EnsureLatestAsync(_config.TazUoDirectory, cancellationToken);
            string mapOverrides = await new ShardDataUpdater(_reporter).EnsureLatestAsync(_config.TazUoDirectory, cancellationToken);
            ClientSettingsWriter.Write(_config, UoClientInstaller.GetClientVersion(_config.UoDirectory));

            _reporter.Status("Starting TazUO...");
            Process.Start(new ProcessStartInfo(Path.Combine(_config.TazUoDirectory, LauncherConstants.TazUoExecutable))
            {
                WorkingDirectory = _config.TazUoDirectory,
                UseShellExecute = false,
                Arguments = mapOverrides != null ? $"-uofilesoverride \"{mapOverrides}\"" : "",
            })?.Dispose();

            if (_config.CloseOnLaunch)
            {
                _busy = false;
                Close();
            }
            else
            {
                _reporter.Status("TazUO is running. Have fun!");
            }
        }

        private async Task RepairClientAsync(CancellationToken cancellationToken)
        {
            var client = new UoClientInstaller(_reporter);

            if (UoClientInstaller.IsInstalled(_config.UoDirectory))
            {
                await client.PatchAsync(_config.UoDirectory, cancellationToken);
            }
            else
            {
                await client.InstallAsync(_config.UoDirectory, cancellationToken);
            }
        }

        /// <summary>Saves the form, locks the UI, runs one job, and reports any failure.</summary>
        private async Task RunAsync(Func<CancellationToken, Task> job)
        {
            if (!TryApplyForm())
            {
                return;
            }

            SetBusy(true);
            try
            {
                await job(_cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // Window closing.
            }
            catch (Exception ex)
            {
                _reporter.Status("Something went wrong: " + ex.Message);
                MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (IsLoaded)
                {
                    SetBusy(false);
                    RefreshVersions();
                }
            }
        }

        private bool TryApplyForm()
        {
            string host = ServerHostBox.Text.Trim();
            string uoDirectory = NormalizePath(UoDirectoryBox.Text);
            string tazUoDirectory = NormalizePath(TazUoDirectoryBox.Text);

            string error = null;
            if (host.Length == 0)
            {
                error = "Enter the server address.";
            }
            else if (!int.TryParse(ServerPortBox.Text.Trim(), out int port) || port < 1 || port > 65535)
            {
                error = "The port must be a number from 1 to 65535.";
            }
            else if (uoDirectory == null || tazUoDirectory == null)
            {
                error = "Choose a full folder path for both UO Classic and TazUO.";
            }
            else if (IsSameOrInside(uoDirectory, tazUoDirectory) || IsSameOrInside(tazUoDirectory, uoDirectory))
            {
                error = "UO Classic and TazUO need separate folders (not one inside the other).";
            }
            else
            {
                _config.ServerHost = host;
                _config.ServerPort = port;
            }

            if (error != null)
            {
                MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            _config.UoDirectory = uoDirectory;
            _config.TazUoDirectory = tazUoDirectory;
            _config.CheckClientUpdates = CheckClientUpdatesBox.IsChecked == true;
            _config.CloseOnLaunch = CloseOnLaunchBox.IsChecked == true;
            _config.Save();

            UoDirectoryBox.Text = uoDirectory;
            TazUoDirectoryBox.Text = tazUoDirectory;
            return true;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            PlayButton.IsEnabled = !busy;
            RepairButton.IsEnabled = !busy;
            ServerHostBox.IsEnabled = !busy;
            ServerPortBox.IsEnabled = !busy;
            UoDirectoryBox.IsEnabled = !busy;
            TazUoDirectoryBox.IsEnabled = !busy;

            if (!busy)
            {
                ShowProgress(0);
            }
        }

        private void ShowProgress(double? fraction)
        {
            Progress.IsIndeterminate = fraction == null;
            Progress.Value = fraction ?? 0;
        }

        private void RefreshVersions()
        {
            string client = UoClientInstaller.IsInstalled(_config.UoDirectory)
                ? "UO client " + UoClientInstaller.GetClientVersion(_config.UoDirectory)
                : "UO client not installed";

            string tazUo = TazUoUpdater.IsInstalled(_config.TazUoDirectory)
                ? "TazUO " + (TazUoUpdater.GetInstalledVersion(_config.TazUoDirectory) ?? "unknown version")
                : "TazUO not installed";

            VersionText.Text = $"{client}  ·  {tazUo}";
        }

        private void BrowseInto(TextBox target, string description)
        {
            // The default folders don't exist before the first install. Create the current one so
            // the picker opens inside it; otherwise "Select Folder" would pick its parent.
            string current = NormalizePath(target.Text);
            if (current != null)
            {
                try
                {
                    Directory.CreateDirectory(current);
                }
                catch (Exception)
                {
                    // Unwritable or invalid; the picker falls back to the nearest existing parent.
                }
            }

            string chosen = FolderPicker.Show(this, description, current ?? target.Text);
            if (chosen != null)
            {
                target.Text = chosen;
            }
        }

        private static string NormalizePath(string path)
        {
            try
            {
                path = path.Trim();
                return Path.IsPathRooted(path) ? Path.GetFullPath(path).TrimEnd('\\') : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static bool IsSameOrInside(string path, string parent) =>
            string.Equals(path, parent, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
