using System;

namespace SeriousFunLauncher
{
    /// <summary>
    /// Forwards status lines and progress from background work to the UI.
    /// Progress is 0..1, or null for "busy, unknown length".
    /// </summary>
    public sealed class StatusReporter
    {
        private readonly IProgress<string> _status;
        private readonly IProgress<double?> _progress;

        public StatusReporter(IProgress<string> status, IProgress<double?> progress)
        {
            _status = status;
            _progress = progress;
        }

        public void Status(string message) => _status.Report(message);

        public void Progress(double? fraction) => _progress.Report(fraction);
    }
}
