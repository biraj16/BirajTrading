// In TradingConsole.Wpf/Services/AnalysisService.cs

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using TradingConsole.Core.Models;

namespace TradingConsole.Wpf.Services
{
    #region Data Models

    /// <summary>
    /// Represents a single OHLCV candle for a specific timeframe.
    /// </summary>
    public class Candle
    {
        public DateTime Timestamp { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long Volume { get; set; }
    }

    /// <summary>
    /// Holds the calculated analysis state (like EMAs) for a single timeframe.
    /// </summary>
    public class TimeframeAnalysisState
    {
        public decimal CurrentShortEma { get; set; }
        public decimal CurrentLongEma { get; set; }
    }

    /// <summary>
    /// A data model to hold the state and calculated values for a single instrument being analyzed.
    /// This class now includes properties for multi-timeframe analysis.
    /// </summary>
    public class AnalysisResult : ObservableModel
    {
        // --- Existing Properties ---
        private string _securityId = string.Empty;
        private string _symbol = string.Empty;
        private decimal _vwap;
        private string _tradingSignal = string.Empty;
        private decimal _currentIv;
        private decimal _avgIv;
        private string _ivSignal = "Neutral";
        private long _currentVolume;
        private long _avgVolume;
        private string _volumeSignal = "Neutral";
        private string _instrumentGroup = string.Empty;
        private string _underlyingGroup = string.Empty;

        // --- NEW: Multi-Timeframe EMA Signal Properties ---
        private string _emaSignal1Min = "N/A";
        private string _emaSignal5Min = "N/A";
        private string _emaSignal15Min = "N/A";

        public string SecurityId { get => _securityId; set { _securityId = value; OnPropertyChanged(); } }
        public string Symbol { get => _symbol; set { _symbol = value; OnPropertyChanged(); } }
        public decimal Vwap { get => _vwap; set { if (_vwap != value) { _vwap = value; OnPropertyChanged(); } } }
        public string TradingSignal { get => _tradingSignal; set { if (_tradingSignal != value) { _tradingSignal = value; OnPropertyChanged(); } } }
        public decimal CurrentIv { get => _currentIv; set { if (_currentIv != value) { _currentIv = value; OnPropertyChanged(); } } }
        public decimal AvgIv { get => _avgIv; set { if (_avgIv != value) { _avgIv = value; OnPropertyChanged(); } } }
        public string IvSignal { get => _ivSignal; set { if (_ivSignal != value) { _ivSignal = value; OnPropertyChanged(); } } }
        public long CurrentVolume { get => _currentVolume; set { if (_currentVolume != value) { _currentVolume = value; OnPropertyChanged(); } } }
        public long AvgVolume { get => _avgVolume; set { if (_avgVolume != value) { _avgVolume = value; OnPropertyChanged(); } } }
        public string VolumeSignal { get => _volumeSignal; set { if (_volumeSignal != value) { _volumeSignal = value; OnPropertyChanged(); } } }
        public string InstrumentGroup { get => _instrumentGroup; set { if (_instrumentGroup != value) { _instrumentGroup = value; OnPropertyChanged(); } } }
        public string UnderlyingGroup { get => _underlyingGroup; set { if (_underlyingGroup != value) { _underlyingGroup = value; OnPropertyChanged(); } } }

        // --- NEW: Public accessors for MTA signals ---
        public string EmaSignal1Min { get => _emaSignal1Min; set { if (_emaSignal1Min != value) { _emaSignal1Min = value; OnPropertyChanged(); } } }
        public string EmaSignal5Min { get => _emaSignal5Min; set { if (_emaSignal5Min != value) { _emaSignal5Min = value; OnPropertyChanged(); } } }
        public string EmaSignal15Min { get => _emaSignal15Min; set { if (_emaSignal15Min != value) { _emaSignal15Min = value; OnPropertyChanged(); } } }

        public string FullGroupIdentifier
        {
            get
            {
                if (InstrumentGroup == "Options")
                {
                    if (UnderlyingGroup.ToUpper().Contains("NIFTY") && !UnderlyingGroup.ToUpper().Contains("BANK")) return "Nifty Options";
                    if (UnderlyingGroup.ToUpper().Contains("BANKNIFTY")) return "Banknifty Options";
                    if (UnderlyingGroup.ToUpper().Contains("SENSEX")) return "Sensex Options";
                    return "Other Stock Options";
                }
                if (InstrumentGroup == "Futures")
                {
                    if (UnderlyingGroup.ToUpper().Contains("NIFTY") || UnderlyingGroup.ToUpper().Contains("BANKNIFTY") || UnderlyingGroup.ToUpper().Contains("SENSEX"))
                        return "Index Futures";
                    return "Stock Futures";
                }
                return InstrumentGroup;
            }
        }
    }
    #endregion

    /// <summary>
    /// The core engine for performing live, intraday analysis on instrument data, now with multi-timeframe capabilities.
    /// </summary>
    public class AnalysisService : INotifyPropertyChanged
    {
        #region Parameters and State
        // --- Configurable Parameters ---
        public int ShortEmaLength { get; set; } = 9;
        public int LongEmaLength { get; set; } = 21;
        private readonly int _ivHistoryLength = 15;
        private readonly decimal _ivSpikeThreshold = 0.01m;
        private readonly int _volumeHistoryLength = 12;
        private readonly double _volumeBurstMultiplier = 2.0;
        private const int MinIvHistoryForSignal = 2;

        // --- ADDED: Constant to define the candle history limit ---
        private const int MaxCandlesToStore = 30;

        // --- NEW: Define the timeframes for MTA ---
        private readonly List<TimeSpan> _timeframes = new()
        {
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15)
        };

        // --- State Dictionaries ---
        // Tick-level analysis state
        private readonly Dictionary<string, (decimal cumulativePriceVolume, long cumulativeVolume, List<decimal> ivHistory, List<long> volumeHistory)> _tickAnalysisState = new();

        // NEW: State for Multi-Timeframe Analysis
        private readonly Dictionary<string, Dictionary<TimeSpan, List<Candle>>> _multiTimeframeCandles = new();
        private readonly Dictionary<string, Dictionary<TimeSpan, TimeframeAnalysisState>> _multiTimeframeAnalysisState = new();

        public event Action<AnalysisResult>? OnAnalysisUpdated;
        #endregion

        /// <summary>
        /// Entry point for all live instrument data. It aggregates data into candles and triggers analysis.
        /// </summary>
        public void OnInstrumentDataReceived(DashboardInstrument instrument)
        {
            // Initialize state for a new instrument if not already present.
            if (!_tickAnalysisState.ContainsKey(instrument.SecurityId))
            {
                _tickAnalysisState[instrument.SecurityId] = (0, 0, new List<decimal>(), new List<long>());
                _multiTimeframeCandles[instrument.SecurityId] = new Dictionary<TimeSpan, List<Candle>>();
                _multiTimeframeAnalysisState[instrument.SecurityId] = new Dictionary<TimeSpan, TimeframeAnalysisState>();
            }

            // Aggregate incoming tick data into candles for each defined timeframe.
            foreach (var timeframe in _timeframes)
            {
                AggregateIntoCandle(instrument, timeframe);
            }

            // Run all analysis calculations (tick-level and multi-timeframe).
            RunComplexAnalysis(instrument);
        }

        /// <summary>
        /// Aggregates live tick data into OHLCV candles for a given timeframe and trims the history.
        /// </summary>
        private void AggregateIntoCandle(DashboardInstrument instrument, TimeSpan timeframe)
        {
            // Ensure dictionaries are initialized for the instrument and timeframe.
            if (!_multiTimeframeCandles[instrument.SecurityId].ContainsKey(timeframe))
            {
                _multiTimeframeCandles[instrument.SecurityId][timeframe] = new List<Candle>();
            }

            var candles = _multiTimeframeCandles[instrument.SecurityId][timeframe];
            var now = DateTime.UtcNow;
            // Calculate the correct start time for the current candle interval.
            var candleTimestamp = new DateTime(now.Ticks - (now.Ticks % timeframe.Ticks), now.Kind);

            var currentCandle = candles.LastOrDefault();

            if (currentCandle == null || currentCandle.Timestamp != candleTimestamp)
            {
                // If it's a new interval, create a new candle.
                candles.Add(new Candle
                {
                    Timestamp = candleTimestamp,
                    Open = instrument.LTP,
                    High = instrument.LTP,
                    Low = instrument.LTP,
                    Close = instrument.LTP,
                    Volume = instrument.LastTradedQuantity
                });

                // --- MODIFIED: Trim the candle list to the desired size ---
                if (candles.Count > MaxCandlesToStore)
                {
                    candles.RemoveAt(0); // Remove the oldest candle to manage memory.
                }
            }
            else
            {
                // Otherwise, update the existing candle for the current interval.
                currentCandle.High = Math.Max(currentCandle.High, instrument.LTP);
                currentCandle.Low = Math.Min(currentCandle.Low, instrument.LTP);
                currentCandle.Close = instrument.LTP; // Always update the close with the latest price
                currentCandle.Volume += instrument.LastTradedQuantity;
            }
        }

        /// <summary>
        /// The main analysis orchestrator. Calculates tick-based indicators and then
        /// iterates through timeframes to calculate MTA indicators.
        /// </summary>
        private void RunComplexAnalysis(DashboardInstrument instrument)
        {
            // --- 1. TICK-LEVEL ANALYSIS (VWAP, IV, Volume) ---
            var tickState = _tickAnalysisState[instrument.SecurityId];

            // VWAP Calculation
            tickState.cumulativePriceVolume += instrument.AvgTradePrice * instrument.LastTradedQuantity;
            tickState.cumulativeVolume += instrument.LastTradedQuantity;
            decimal vwap = (tickState.cumulativeVolume > 0) ? tickState.cumulativePriceVolume / tickState.cumulativeVolume : 0;

            // IV Analysis
            if (instrument.ImpliedVolatility > 0) tickState.ivHistory.Add(instrument.ImpliedVolatility);
            if (tickState.ivHistory.Count > _ivHistoryLength) tickState.ivHistory.RemoveAt(0);
            var (avgIv, ivSignal) = CalculateIvSignal(instrument.ImpliedVolatility, tickState.ivHistory);

            // Volume Analysis
            tickState.volumeHistory.Add(instrument.Volume);
            if (tickState.volumeHistory.Count > _volumeHistoryLength) tickState.volumeHistory.RemoveAt(0);
            string volumeSignal = CalculateVolumeSignal(instrument.Volume, tickState.volumeHistory);

            _tickAnalysisState[instrument.SecurityId] = tickState; // Save updated tick state

            // --- 2. MULTI-TIMEFRAME ANALYSIS (EMAs) ---
            var mtaSignals = new Dictionary<TimeSpan, string>();
            foreach (var timeframe in _timeframes)
            {
                var candles = _multiTimeframeCandles[instrument.SecurityId][timeframe];
                if (!candles.Any()) continue;

                var emaSignal = CalculateEmaSignalForTimeframe(instrument.SecurityId, timeframe, candles);
                mtaSignals[timeframe] = emaSignal;
            }

            // --- 3. COMBINE AND EMIT RESULTS ---
            var finalResult = new AnalysisResult
            {
                SecurityId = instrument.SecurityId,
                Symbol = instrument.DisplayName,
                Vwap = vwap,
                CurrentIv = instrument.ImpliedVolatility,
                AvgIv = avgIv,
                IvSignal = ivSignal,
                CurrentVolume = instrument.Volume,
                AvgVolume = (long)(tickState.volumeHistory.Any() ? tickState.volumeHistory.Average() : 0),
                VolumeSignal = volumeSignal,
                // Assign MTA signals
                EmaSignal1Min = mtaSignals.GetValueOrDefault(TimeSpan.FromMinutes(1), "N/A"),
                EmaSignal5Min = mtaSignals.GetValueOrDefault(TimeSpan.FromMinutes(5), "N/A"),
                EmaSignal15Min = mtaSignals.GetValueOrDefault(TimeSpan.FromMinutes(15), "N/A"),
                // Set overall trading signal (can be enhanced later to use MTA)
                TradingSignal = mtaSignals.GetValueOrDefault(TimeSpan.FromMinutes(1), "Neutral"),
                // Grouping info
                InstrumentGroup = GetInstrumentGroup(instrument),
                UnderlyingGroup = instrument.UnderlyingSymbol
            };

            OnAnalysisUpdated?.Invoke(finalResult);
        }

        /// <summary>
        /// Calculates the Short and Long EMA for a given set of candles and determines the trend signal.
        /// </summary>
        private string CalculateEmaSignalForTimeframe(string securityId, TimeSpan timeframe, List<Candle> candles)
        {
            // Ensure state dictionary is initialized
            if (!_multiTimeframeAnalysisState[securityId].ContainsKey(timeframe))
            {
                _multiTimeframeAnalysisState[securityId][timeframe] = new TimeframeAnalysisState();
            }

            var state = _multiTimeframeAnalysisState[securityId][timeframe];
            var lastCandle = candles.Last();

            // Calculate Short EMA
            decimal shortMultiplier = 2.0m / (ShortEmaLength + 1);
            if (state.CurrentShortEma == 0) state.CurrentShortEma = lastCandle.Close;
            else state.CurrentShortEma = ((lastCandle.Close - state.CurrentShortEma) * shortMultiplier) + state.CurrentShortEma;

            // Calculate Long EMA
            decimal longMultiplier = 2.0m / (LongEmaLength + 1);
            if (state.CurrentLongEma == 0) state.CurrentLongEma = lastCandle.Close;
            else state.CurrentLongEma = ((lastCandle.Close - state.CurrentLongEma) * longMultiplier) + state.CurrentLongEma;

            // Determine signal
            if (state.CurrentShortEma > state.CurrentLongEma) return "Bullish Cross";
            if (state.CurrentShortEma < state.CurrentLongEma) return "Bearish Cross";
            return "Neutral";
        }

        #region Helper Calculation Methods
        private (decimal avgIv, string ivSignal) CalculateIvSignal(decimal currentIv, List<decimal> ivHistory)
        {
            string signal = "Neutral";
            decimal avgIv = 0;
            var validIvHistory = ivHistory.Where(iv => iv > 0).ToList();

            if (validIvHistory.Any() && validIvHistory.Count >= MinIvHistoryForSignal)
            {
                avgIv = validIvHistory.Average();
                if (currentIv > (avgIv + _ivSpikeThreshold)) signal = "IV Spike Up";
                else if (currentIv < (avgIv - _ivSpikeThreshold)) signal = "IV Drop Down";
            }
            else if (currentIv > 0)
            {
                signal = "Building History...";
            }
            return (avgIv, signal);
        }

        private string CalculateVolumeSignal(long currentVolume, List<long> volumeHistory)
        {
            if (volumeHistory.Any())
            {
                double avgVolume = volumeHistory.Average(v => (double)v);
                if (avgVolume > 0 && currentVolume > (avgVolume * _volumeBurstMultiplier))
                {
                    return "Volume Burst";
                }
            }
            return "Neutral";
        }

        private string GetInstrumentGroup(DashboardInstrument instrument)
        {
            if (instrument.SegmentId == 0) return "Indices";
            if (instrument.IsFuture) return "Futures";
            if (instrument.DisplayName.ToUpper().Contains("CALL") || instrument.DisplayName.ToUpper().Contains("PUT")) return "Options";
            return "Stocks";
        }
        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}