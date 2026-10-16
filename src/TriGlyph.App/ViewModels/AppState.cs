using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TriGlyph.App.ViewModels
{
    /// <summary>Live state the views bind to.</summary>
    public sealed class AppState : INotifyPropertyChanged
    {
        private string _display = "--";
        private string _unitSuffix = "C";
        private string _status = "starting up";
        private string _source = "-";
        private string _device = "not detected";
        private string _matchReason = "";
        private bool _isStale = true;
        private bool _isConnected;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Display { get => _display; set => Set(ref _display, value); }
        public string UnitSuffix { get => _unitSuffix; set => Set(ref _unitSuffix, value); }
        public string Status { get => _status; set => Set(ref _status, value); }
        public string Source { get => _source; set => Set(ref _source, value); }
        public string Device { get => _device; set => Set(ref _device, value); }
        public string MatchReason { get => _matchReason; set => Set(ref _matchReason, value); }
        public bool IsStale { get => _isStale; set => Set(ref _isStale, value); }
        public bool IsConnected { get => _isConnected; set => Set(ref _isConnected, value); }

        public ObservableCollection<string> Candidates { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Reports { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Log { get; } = new ObservableCollection<string>();

        public void AppendLog(string line)
        {
            Log.Insert(0, line);
            while (Log.Count > 400)
            {
                Log.RemoveAt(Log.Count - 1);
            }
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
