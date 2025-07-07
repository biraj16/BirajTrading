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

    public class Candle
    {
        public DateTime Timestamp { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long Volume { get; set; }
        public long OpenInterest { get; set; }
    }

    public class TimeframeAnalysisState
    {
        public decimal CurrentShortEma { get; set; }
        public decimal CurrentLongEma { get; set; }
    }

    /// <summary>
    /// --- MODIFIED: Replaced TradingSignal with specific price action properties ---
    /// </summary>
    public class AnalysisResult : ObservableModel
    {
        private string _securityId = string.Empty;
        private string _symbol = string.Empty;
        private decimal _vwap;
        private decimal _currentIv;
        private decimal _avgIv;
        private string _ivSignal = "Neutral";
        private long _currentVolume;
        private long _avgVolume;
        private string _volumeSignal = "Neutral";
        private string _oiSignal = "N/A";
        private string _instrumentGroup = string.Empty;
        private string _underlyingGroup = string.Empty;
        private string _emaSignal1Min = "N/A";
        private string _emaSignal5Min = "N/A";
        private string _emaSignal15Min = "N/A";

        // --- NEW: Specific Price Action Signal Properties ---
        private string _priceVsVwapSignal = "Neutral";
        private string _priceVsCloseSignal = "Neutral";
        private string _dayRangeSignal = "Neutral";
        private string _openDriveSignal = "Neutral";

        public string SecurityId { get => _securityId; set { _securityId = value; OnPropertyChanged(); } }
        public string Symbol { get => _symbol; set { _symbol = value; OnPropertyChanged(); } }
        public decimal Vwap { get => _vwap; set { if (_vwap != value) { _vwap = value; OnPropertyChanged(); } } }
        public decimal CurrentIv { get => _currentIv; set { if (_currentIv != value) { _currentIv = value; OnPropertyChanged(); } } }
        public decimal AvgIv { get => _avgIv; set { if (_avgIv != value) { _avgIv = value; OnPropertyChanged(); } } }
        public string IvSignal { get => _ivSignal; set { if (_ivSignal != value) { _ivSignal = value; OnPropertyChanged(); } } }
        public long CurrentVolume { get => _currentVolume; set { if (_currentVolume != value) { _currentVolume = value; OnPropertyChanged(); } } }
        public long AvgVolume { get => _avgVolume; set { if (_avgVolume != value) { _avgVolume = value; OnPropertyChanged(); } } }
        public string VolumeSignal { get => _volumeSignal; set { if (_volumeSignal != value) { _volumeSignal = value; OnPropertyChanged(); } } }
        public string OiSignal { get => _oiSignal; set { if (_oiSignal != value) { _oiSignal = value; OnPropertyChanged(); } } }
        public string InstrumentGroup { get => _instrumentGroup; set { if (_instrumentGroup != value) { _instrumentGroup = value; OnPropertyChanged(); } } }
        public string UnderlyingGroup { get => _underlyingGroup; set { if (_underlyingGroup != value) { _underlyingGroup = value; OnPropertyChanged(); } } }
        public string EmaSignal1Min { get => _emaSignal1Min; set { if (_emaSignal1Min != value) { _emaSignal1Min = value; OnPropertyChanged(); } } }
        public string EmaSignal5Min { get => _emaSignal5Min; set { if (_emaSignal5Min != value) { _emaSignal5Min = value; OnPropertyChanged(); } } }
        public string EmaSignal15Min { get => _emaSignal15Min; set { if (_emaSignal15Min != value) { _emaSignal15Min = value; OnPropertyChanged(); } } }

        // --- NEW: Public accessors for the new price action signals ---
        public string PriceVsVwapSignal { get => _priceVsVwapSignal; set { if (_priceVsVwapSignal != value) { _priceVsVwapSignal = value; OnPropertyChanged(); } } }
        public string PriceVsCloseSignal { get => _priceVsCloseSignal; set { if (_priceVsCloseSignal != value) { _priceVsCloseSignal = value; OnPropertyChanged(); } } }
        public string DayRangeSignal { get => _dayRangeSignal; set { if (_dayRangeSignal != value) { _dayRangeSignal = value; OnPropertyChanged(); } } }
        public string OpenDriveSignal { get => _openDriveSignal; set { if (_openDriveSignal != value) { _openDriveSignal = value; OnPropertyChanged(); } } }


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

    public class AnalysisService : INotifyPropertyChanged
    {
        #region Parameters and State
        public int ShortEmaLength { get; set; } = 9;
        public int LongEmaLength { get; set; } = 21;
        private readonly int _ivHistoryLength = 15;
        private readonly decimal _ivSpikeThreshold = 0.01m;
        private readonly int _volumeHistoryLength = 12;
        private readonly double _volumeBurstMultiplier = 2.0;
        private const int MinIvHistoryForSignal = 2;
        private const int MaxCandlesToStore = 30;
        private readonly List<TimeSpan> _timeframes = new()
        {
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15)
        };
        private readonly Dictionary<string, (decimal cumulativePriceVolume, long cumulativeVolume, List<decimal> ivHistory)> _tickAnalysisState = new();
        private readonly Dictionary<string, Dictionary<TimeSpan, List<Candle>>> _multiTimeframeCandles = new();
        private readonly Dictionary<string, Dictionary<TimeSpan, TimeframeAnalysisState>> _multiTimeframeAnalysisState = new();
        public event Action<AnalysisResult>? OnAnalysisUpdated;
        #endregion

        public void OnInstrumentDataReceived(DashboardInstrument instrument)
        {
            if (!_tickAnalysisState.ContainsKey(instrument.SecurityId))
            {
                _tickAnalysisState[instrument.SecurityId] = (0, 0, new List<decimal>());
                _multiTimeframeCandles[instrument.SecurityId] = new Dictionary<TimeSpan, List<Candle>>();
                _multiTimeframeAnalysisState[instrument.SecurityId] = new Dictionary<TimeSpan, TimeframeAnalysisState>();
            }

            foreach (var timeframe in _timeframes)
            {
                AggregateIntoCandle(instrument, timeframe);
            }

            RunComplexAnalysis(instrument);
        }

        private void AggregateIntoCandle(DashboardInstrument instrument, TimeSpan timeframe)
        {
            if (!_multiTimeframeCandles[instrument.SecurityId].ContainsKey(timeframe))
            {
                _multiTimeframeCandles[instrument.SecurityId][timeframe] = new List<Candle>();
            }

            var candles = _multiTimeframeCandles[instrument.SecurityId][timeframe];
            var now = DateTime.UtcNow;
            var candleTimestamp = new DateTime(now.Ticks - (now.Ticks % timeframe.Ticks), now.Kind);

            var currentCandle = candles.LastOrDefault();

            if (currentCandle == null || currentCandle.Timestamp != candleTimestamp)
            {
                candles.Add(new Candle
                {
                    Timestamp = candleTimestamp,
                    Open = instrument.LTP,
                    High = instrument.LTP,
                    Low = instrument.LTP,
                    Close = instrument.LTP,
                    Volume = instrument.LastTradedQuantity,
                    OpenInterest = instrument.OpenInterest
                });

                if (candles.Count > MaxCandlesToStore)
                {
                    candles.RemoveAt(0);
                }
            }
            else
            {
                currentCandle.High = Math.Max(currentCandle.High, instrument.LTP);
                currentCandle.Low = Math.Min(currentCandle.Low, instrument.LTP);
                currentCandle.Close = instrument.LTP;
                currentCandle.Volume += instrument.LastTradedQuantity;
                currentCandle.OpenInterest = instrument.OpenInterest;
            }
        }

        /// <summary>
        /// --- MODIFIED: Now calculates individual price action signals. ---
        /// </summary>
        private void RunComplexAnalysis(DashboardInstrument instrument)
        {
            var tickState = _tickAnalysisState[instrument.SecurityId];
            tickState.cumulativePriceVolume += instrument.AvgTradePrice * instrument.LastTradedQuantity;
            tickState.cumulativeVolume += instrument.LastTradedQuantity;
            decimal vwap = (tickState.cumulativeVolume > 0) ? tickState.cumulativePriceVolume / tickState.cumulativeVolume : 0;

            if (instrument.ImpliedVolatility > 0) tickState.ivHistory.Add(instrument.ImpliedVolatility);
            if (tickState.ivHistory.Count > _ivHistoryLength) tickState.ivHistory.RemoveAt(0);
            var (avgIv, ivSignal) = CalculateIvSignal(instrument.ImpliedVolatility, tickState.ivHistory);

            _tickAnalysisState[instrument.SecurityId] = tickState;

            var mtaSignals = new Dictionary<TimeSpan, string>();
            foreach (var timeframe in _timeframes)
            {
                var candles = _multiTimeframeCandles[instrument.SecurityId].GetValueOrDefault(timeframe);
                if (candles == null || !candles.Any()) continue;
                mtaSignals[timeframe] = CalculateEmaSignalForTimeframe(instrument.SecurityId, timeframe, candles);
            }

            var oneMinCandles = _multiTimeframeCandles[instrument.SecurityId].GetValueOrDefault(TimeSpan.FromMinutes(1));

            var (volumeSignal, currentCandleVolume, avgCandleVolume) = ("Neutral", 0L, 0L);
            if (oneMinCandles != null && oneMinCandles.Any())
            {
                (volumeSignal, currentCandleVolume, avgCandleVolume) = CalculateVolumeSignalForTimeframe(oneMinCandles);
            }

            string oiSignal = "N/A";
            if (oneMinCandles != null && oneMinCandles.Any())
            {
                oiSignal = CalculateOiSignal(oneMinCandles);
            }

            // --- NEW: Calculate all price action signals ---
            var paSignals = CalculatePriceActionSignals(instrument, vwap);

            var finalResult = new AnalysisResult
            {
                SecurityId = instrument.SecurityId,
                Symbol = instrument.DisplayName,
                Vwap = vwap,
                CurrentIv = instrument.ImpliedVolatility,
                AvgIv = avgIv,
                IvSignal = ivSignal,
                CurrentVolume = currentCandleVolume,
                AvgVolume = avgCandleVolume,
                VolumeSignal = volumeSignal,
                OiSignal = oiSignal,
                EmaSignal1Min = mtaSignals.GetValueOrDefault(TimeSpan.FromMinutes(1), "N/A"),
                EmaSignal5Min = mtaSignals.GetValueOrDefault(TimeSpan.FromMinutes(5), "N/A"),
                EmaSignal15Min = mtaSignals.GetValueOrDefault(TimeSpan.FromMinutes(15), "N/A"),
                InstrumentGroup = GetInstrumentGroup(instrument),
                UnderlyingGroup = instrument.UnderlyingSymbol,

                // --- NEW: Populate the individual price action signals ---
                PriceVsVwapSignal = paSignals.priceVsVwap,
                PriceVsCloseSignal = paSignals.priceVsClose,
                DayRangeSignal = paSignals.dayRange,
                OpenDriveSignal = paSignals.openDrive
            };

            OnAnalysisUpdated?.Invoke(finalResult);
        }

        private string CalculateEmaSignalForTimeframe(string securityId, TimeSpan timeframe, List<Candle> candles)
        {
            if (!_multiTimeframeAnalysisState[securityId].ContainsKey(timeframe))
            {
                _multiTimeframeAnalysisState[securityId][timeframe] = new TimeframeAnalysisState();
            }

            var state = _multiTimeframeAnalysisState[securityId][timeframe];
            var lastCandle = candles.Last();

            decimal shortMultiplier = 2.0m / (ShortEmaLength + 1);
            state.CurrentShortEma = (state.CurrentShortEma == 0) ? lastCandle.Close : ((lastCandle.Close - state.CurrentShortEma) * shortMultiplier) + state.CurrentShortEma;

            decimal longMultiplier = 2.0m / (LongEmaLength + 1);
            state.CurrentLongEma = (state.CurrentLongEma == 0) ? lastCandle.Close : ((lastCandle.Close - state.CurrentLongEma) * longMultiplier) + state.CurrentLongEma;

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

        private (string signal, long currentVolume, long averageVolume) CalculateVolumeSignalForTimeframe(List<Candle> candles)
        {
            if (!candles.Any()) return ("N/A", 0, 0);

            long currentCandleVolume = candles.Last().Volume;
            if (candles.Count < 2) return ("Building History...", currentCandleVolume, 0);

            var historyCandles = candles.Take(candles.Count - 1).ToList();
            if (historyCandles.Count > _volumeHistoryLength)
            {
                historyCandles = historyCandles.Skip(historyCandles.Count - _volumeHistoryLength).ToList();
            }

            if (!historyCandles.Any()) return ("Building History...", currentCandleVolume, 0);

            double averageVolume = historyCandles.Average(c => (double)c.Volume);
            if (averageVolume > 0 && currentCandleVolume > (averageVolume * _volumeBurstMultiplier))
            {
                return ("Volume Burst", currentCandleVolume, (long)averageVolume);
            }
            return ("Neutral", currentCandleVolume, (long)averageVolume);
        }

        private string CalculateOiSignal(List<Candle> candles)
        {
            if (candles.Count < 2) return "Building History...";

            var currentCandle = candles.Last();
            var previousCandle = candles[candles.Count - 2];

            bool isPriceUp = currentCandle.Close > previousCandle.Close;
            bool isPriceDown = currentCandle.Close < previousCandle.Close;
            bool isOiUp = currentCandle.OpenInterest > previousCandle.OpenInterest;
            bool isOiDown = currentCandle.OpenInterest < previousCandle.OpenInterest;

            if (isPriceUp && isOiUp) return "Long Buildup";
            if (isPriceUp && isOiDown) return "Short Covering";
            if (isPriceDown && isOiUp) return "Short Buildup";
            if (isPriceDown && isOiDown) return "Long Unwinding";

            return "Neutral";
        }

        /// <summary>
        /// --- NEW: Calculates multiple price action signals based on live data. ---
        /// </summary>
        private (string priceVsVwap, string priceVsClose, string dayRange, string openDrive) CalculatePriceActionSignals(DashboardInstrument instrument, decimal vwap)
        {
            // 1. Price vs VWAP
            string priceVsVwap = "Neutral";
            if (vwap > 0)
            {
                if (instrument.LTP > vwap) priceVsVwap = "Above VWAP";
                else if (instrument.LTP < vwap) priceVsVwap = "Below VWAP";
            }

            // 2. Price vs Previous Close
            string priceVsClose = "Neutral";
            if (instrument.Close > 0)
            {
                if (instrument.LTP > instrument.Close) priceVsClose = "Above Close";
                else if (instrument.LTP < instrument.Close) priceVsClose = "Below Close";
            }

            // 3. Day's Range
            string dayRange = "Neutral";
            decimal range = instrument.High - instrument.Low;
            if (range > 0)
            {
                decimal positionInDayRange = (instrument.LTP - instrument.Low) / range;
                if (positionInDayRange > 0.8m) dayRange = "Near High";
                else if (positionInDayRange < 0.2m) dayRange = "Near Low";
                else dayRange = "Mid-Range";
            }

            // 4. Open Drive
            string openDrive = "No";
            if (instrument.Open > 0 && instrument.Low > 0 && instrument.High > 0)
            {
                if (instrument.Open == instrument.Low) openDrive = "Drive Up";
                else if (instrument.Open == instrument.High) openDrive = "Drive Down";
            }

            return (priceVsVwap, priceVsClose, dayRange, openDrive);
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
