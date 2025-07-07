using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using TradingConsole.Wpf.Services;

namespace TradingConsole.Wpf.ViewModels
{
    public class AnalysisTabViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<AnalysisResult> AnalysisResults { get; } = new ObservableCollection<AnalysisResult>();

        public AnalysisTabViewModel()
        {
            // Constructor remains parameterless.
        }

        /// <summary>
        /// Updates an existing analysis result or adds a new one to the collection.
        /// </summary>
        public void UpdateAnalysisResult(AnalysisResult newResult)
        {
            var existingResult = AnalysisResults.FirstOrDefault(r => r.SecurityId == newResult.SecurityId);

            if (existingResult != null)
            {
                // Update all properties from the new result to the existing one.
                // This will trigger UI updates because AnalysisResult implements INotifyPropertyChanged.
                existingResult.Vwap = newResult.Vwap;
                existingResult.CurrentIv = newResult.CurrentIv;
                existingResult.AvgIv = newResult.AvgIv;
                existingResult.IvSignal = newResult.IvSignal;
                existingResult.CurrentVolume = newResult.CurrentVolume;
                existingResult.AvgVolume = newResult.AvgVolume;
                existingResult.VolumeSignal = newResult.VolumeSignal;
                existingResult.OiSignal = newResult.OiSignal;
                existingResult.Symbol = newResult.Symbol;
                existingResult.InstrumentGroup = newResult.InstrumentGroup;
                existingResult.UnderlyingGroup = newResult.UnderlyingGroup;
                existingResult.EmaSignal1Min = newResult.EmaSignal1Min;
                existingResult.EmaSignal5Min = newResult.EmaSignal5Min;
                existingResult.EmaSignal15Min = newResult.EmaSignal15Min;

                // --- FIX: Update the new price action signals ---
                existingResult.PriceVsVwapSignal = newResult.PriceVsVwapSignal;
                existingResult.PriceVsCloseSignal = newResult.PriceVsCloseSignal;
                existingResult.DayRangeSignal = newResult.DayRangeSignal;
                existingResult.OpenDriveSignal = newResult.OpenDriveSignal;
            }
            else
            {
                // If no existing result is found, add the new result to the collection.
                AnalysisResults.Add(newResult);
            }
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}
