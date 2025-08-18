// ============================================================================
// Strategy Name : ECP_MultiSession_23x5
// Author        : OpenAI ChatGPT
// Date Created  : 2025-08-17
// Version       : 1.1
// Description   : Multi-session strategy implementing full ECP spec
// ============================================================================

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.StrategyGenerator;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// ECP Multi-session strategy with session router, risk controls,
    /// sizing engine, execution router and module framework. Designed
    /// to satisfy the 23x5 specification with a 16:55-18:00 ET no-trade block.
    /// </summary>
    public class ECP_MultiSession_23x5 : Strategy
    {
        #region Enumerations
        /// <summary>Session identifiers.</summary>
        public enum SessionId { ASIA, LONDON_OPEN, EU_MID, US_PRE, US_OPEN, US_MID, US_POWER, NONE }
        /// <summary>Module identifiers.</summary>
        public enum ModuleType { MR_ASIA, BO_LONDON, TP_EU, MR_VWAP, AV_PRE, ORB_US, MR_MID, TC_PH, NONE }
        /// <summary>Position sizing modes.</summary>
        public enum SizingMode { FIXED_CONTRACTS, RISK_PER_TRADE_USD, RISK_PCT_EQUITY, KELLY_FRACTIONAL }
        private enum TPMode { NONE, SINGLE, TIERED }
        private enum TrailMode { NONE, CHANDELIER, ATR_STEP }
        #endregion

        private class TradeStat
        {
            public DateTime closeEt;
            public SessionId session;
            public ModuleType module;
            public double R;
            public double mfeUsd;
            public double maeUsd;
        }

        private class EdgeStats
        {
            public double expectancy;
            public double sharpe;
            public double mfeMae;
            public int trades;
            public DateTime lastComputed;
        }

        #region Variables
        private TimeZoneInfo eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        private TimeSpan autoFlatEt;
        private TimeSpan reopenEt;
        private TimeSpan resetEdgeEt;

        private bool inNoTradeBlock;
        private DateTime lastAutoFlatDate = Core.Globals.MinDate;
        private bool didResetToday;
        private TimeSpan prevTod;

        private double highWaterMark;
        private double maxBalance;
        private double dailyRealized;
        private double realizedCumulative;
        private int dayTrades;
        private int consecutiveLosses;
        private bool tradeLock;

        private double unrealizedNow;

        private ATR atr14;
        private ATR atr10;
        private RSI rsi2;
        private EMA ema20;
        private EMA ema50;
        private ADX adx14;
        private CCI cci20;
        private ATR atr20;

        private StreamWriter logWriter;
        private List<double> atrOpenHistory = new List<double>();
        private double orHigh = double.MinValue;
        private double orLow = double.MaxValue;
        private bool orComplete;
        private DateTime orEndTime;
        private DateTime orCalcDate = Core.Globals.MinDate;
        private bool orbEntrySubmitted;
        private DateTime orbEntryTime = Core.Globals.MinDate;
        private bool orbFirstTargetHit;
        private int orDurationMin;

        private DateTime boCalcDate = Core.Globals.MinDate;
        private bool boOrComplete;
        private double boOrHigh = double.MinValue;
        private double boOrLow = double.MaxValue;
        private DateTime boOrEnd;
        private DateTime boEntryTime = Core.Globals.MinDate;

        private DateTime mrAsiaEntryTime = Core.Globals.MinDate;
        private bool mrAsiaLongReady = true;
        private bool mrAsiaShortReady = true;
        private DateTime tpEuEntryTime = Core.Globals.MinDate;
        private DateTime mrVwapEntryTime = Core.Globals.MinDate;
        private DateTime mrMidEntryTime = Core.Globals.MinDate;

        // Health/Monitor fields
        private DateTime lastDataTime = Core.Globals.MinDate;
        private DateTime staleSince = Core.Globals.MinDate;
        private bool isStaleLatched;
        private bool disconnectLatched;
        private bool rejectLatched;
        private DateTime disconnectStart = Core.Globals.MinDate;
        private DateTime reconnectTime = Core.Globals.MinDate;
        private bool isConnected = true;

        private readonly List<TradeStat> tradeStats = new List<TradeStat>();
        private readonly Dictionary<SessionId, Dictionary<ModuleType, EdgeStats>> edgeStats = new Dictionary<SessionId, Dictionary<ModuleType, EdgeStats>>();
        private readonly Dictionary<SessionId, Tuple<ModuleType, double>> chosenModules = new Dictionary<SessionId, Tuple<ModuleType, double>>();
        private readonly Dictionary<string, DateTime> lastLogByReason = new Dictionary<string, DateTime>();
        private static readonly HashSet<string> rateLimitedReasons = new HashSet<string> { "SpreadGate", "VolClampGate", "TDDCushionBlock", "AbsHaltCushionBlock", "NoTradeBlock1655_1800" };
        #endregion

        #region Properties - Core
        /// <summary>Indicator period base.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "Period", GroupName = "Core", Order = 0)]
        public int Period { get; set; } = 14;

        /// <summary>Enable verbose debugging prints.</summary>
        [NinjaScriptProperty]
        [Display(Name = "DebugMode", GroupName = "Core", Order = 1)]
        public bool DebugMode { get; set; }
        #endregion

        #region Properties - Router & Schedule
        /// <summary>Auto select best strategy per session.</summary>
        [NinjaScriptProperty]
        [Display(Name = "AUTO_SELECT_BEST_SESSION_STRATEGY", GroupName = "Session Router", Order = 0)]
        public bool AutoSelectBestSessionStrategy { get; set; } = true;

        /// <summary>Forced strategy for ASIA session.</summary>
        [NinjaScriptProperty]
        [Display(Name = "FORCED_STRATEGY_ASIA", GroupName = "Session Router", Order = 1)]
        public ModuleType ForcedStrategyAsia { get; set; } = ModuleType.MR_ASIA;

        /// <summary>Forced strategy for LONDON session.</summary>
        [NinjaScriptProperty]
        [Display(Name = "FORCED_STRATEGY_LONDON", GroupName = "Session Router", Order = 2)]
        public ModuleType ForcedStrategyLondon { get; set; } = ModuleType.BO_LONDON;

        /// <summary>Forced strategy for EU MID session.</summary>
        [NinjaScriptProperty]
        [Display(Name = "FORCED_STRATEGY_EU_MID", GroupName = "Session Router", Order = 3)]
        public ModuleType ForcedStrategyEuMid { get; set; } = ModuleType.TP_EU;

        /// <summary>Forced strategy for US PRE session.</summary>
        [NinjaScriptProperty]
        [Display(Name = "FORCED_STRATEGY_US_PRE", GroupName = "Session Router", Order = 4)]
        public ModuleType ForcedStrategyUsPre { get; set; } = ModuleType.AV_PRE;

        /// <summary>Forced strategy for US OPEN session.</summary>
        [NinjaScriptProperty]
        [Display(Name = "FORCED_STRATEGY_US_OPEN", GroupName = "Session Router", Order = 5)]
        public ModuleType ForcedStrategyUsOpen { get; set; } = ModuleType.ORB_US;

        /// <summary>Forced strategy for US MID session.</summary>
        [NinjaScriptProperty]
        [Display(Name = "FORCED_STRATEGY_US_MID", GroupName = "Session Router", Order = 6)]
        public ModuleType ForcedStrategyUsMid { get; set; } = ModuleType.MR_MID;

        /// <summary>Forced strategy for US POWER session.</summary>
        [NinjaScriptProperty]
        [Display(Name = "FORCED_STRATEGY_US_POWER", GroupName = "Session Router", Order = 7)]
        public ModuleType ForcedStrategyUsPower { get; set; } = ModuleType.TC_PH;

        /// <summary>Edge score lookback days.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "EDGE_LOOKBACK_DAYS", GroupName = "Session Router", Order = 8)]
        public int EdgeLookbackDays { get; set; } = 60;

        /// <summary>Edge ranking metric.</summary>
        [NinjaScriptProperty]
        [Display(Name = "EDGE_METRIC", GroupName = "Session Router", Order = 9)]
        public string EdgeMetric { get; set; } = "ExpectancyTimesSharpe";

        /// <summary>Auto flat time ET.</summary>
        [NinjaScriptProperty]
        [Display(Name = "AUTO_FLAT_ET", GroupName = "Schedule", Order = 0)]
        public string AutoFlatEt { get; set; } = "16:55";

        /// <summary>Re-open time ET.</summary>
        [NinjaScriptProperty]
        [Display(Name = "REOPEN_ET", GroupName = "Schedule", Order = 1)]
        public string ReopenEt { get; set; } = "18:00";

        /// <summary>Edge recalculation time ET.</summary>
        [NinjaScriptProperty]
        [Display(Name = "RESET_EDGE_RECALC_ET", GroupName = "Schedule", Order = 2)]
        public string ResetEdgeRecalcEt { get; set; } = "17:05";
        #endregion

        #region Properties - ECP Risk
        /// <summary>Enable trailing drawdown.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ECP_ENABLE_TDD", GroupName = "ECP Risk", Order = 0)]
        public bool EcpEnableTdd { get; set; } = true;

        /// <summary>TDD mode string.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ECP_TDD_MODE", GroupName = "ECP Risk", Order = 1)]
        public string EcpTddMode { get; set; } = "REALIZED_PLUS_UNREALIZED";

        /// <summary>TDD amount USD.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ECP_TDT_AMOUNT_USD", GroupName = "ECP Risk", Order = 2)]
        public double EcpTdtAmountUsd { get; set; } = 2500;

        /// <summary>Enable absolute halt.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ECP_ENABLE_ABS_HALT", GroupName = "ECP Risk", Order = 3)]
        public bool EcpEnableAbsHalt { get; set; } = true;

        /// <summary>Absolute halt delta USD.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ECP_ABS_HALT_DELTA_USD", GroupName = "ECP Risk", Order = 4)]
        public double EcpAbsHaltDeltaUsd { get; set; } = 2000;

        /// <summary>Daily loss cap USD.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ECP_DAILY_LOSS_CAP_USD", GroupName = "ECP Risk", Order = 5)]
        public double EcpDailyLossCapUsd { get; set; } = 700;

        /// <summary>Max consecutive losses.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "ECP_MAX_CONSEC_LOSSES", GroupName = "ECP Risk", Order = 6)]
        public int EcpMaxConsecLosses { get; set; } = 2;

        /// <summary>TDD cushion USD.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ECP_TDT_CUSHION_USD", GroupName = "ECP Risk", Order = 7)]
        public double EcpTdtCushionUsd { get; set; } = 250;

        /// <summary>Manual unlock flag.</summary>
        [NinjaScriptProperty]
        [Display(Name = "MANUAL_UNLOCK", GroupName = "ECP Risk", Order = 8)]
        public bool ManualUnlock { get; set; }

        /// <summary>Seed value for maximum account balance.</summary>
        [NinjaScriptProperty]
        [Display(Name = "INITIAL_MAX_BALANCE", GroupName = "ECP Risk", Order = 9)]
        public double InitialMaxBalance { get; set; }
        #endregion

        #region Properties - Sizing
        /// <summary>Sizing mode.</summary>
        [NinjaScriptProperty]
        [Display(Name = "SIZING_MODE", GroupName = "Sizing", Order = 0)]
        public SizingMode PositionSizingMode { get; set; } = SizingMode.RISK_PER_TRADE_USD;

        /// <summary>Fixed contract quantity.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "FIXED_QTY", GroupName = "Sizing", Order = 1)]
        public int FixedQty { get; set; } = 1;

        /// <summary>Risk per trade USD.</summary>
        [NinjaScriptProperty]
        [Display(Name = "RISK_PER_TRADE_USD", GroupName = "Sizing", Order = 2)]
        public double RiskPerTradeUsd { get; set; } = 150;

        /// <summary>Risk percent equity.</summary>
        [NinjaScriptProperty]
        [Display(Name = "RISK_PCT_EQUITY", GroupName = "Sizing", Order = 3)]
        public double RiskPctEquity { get; set; } = 0.5;

        /// <summary>Kelly fraction.</summary>
        [NinjaScriptProperty]
        [Display(Name = "KELLY_FRACTION", GroupName = "Sizing", Order = 4)]
        public double KellyFraction { get; set; } = 0.25;

        /// <summary>Max contracts ES.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "MAX_CONTRACTS_ES", GroupName = "Sizing", Order = 10)]
        public int MaxContractsEs { get; set; } = 3;

        /// <summary>Max contracts NQ.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "MAX_CONTRACTS_NQ", GroupName = "Sizing", Order = 11)]
        public int MaxContractsNq { get; set; } = 2;

        /// <summary>Max contracts CL.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "MAX_CONTRACTS_CL", GroupName = "Sizing", Order = 12)]
        public int MaxContractsCl { get; set; } = 2;

        /// <summary>Max contracts GC.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "MAX_CONTRACTS_GC", GroupName = "Sizing", Order = 13)]
        public int MaxContractsGc { get; set; } = 2;

        /// <summary>Max contracts MES.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "MAX_CONTRACTS_MES", GroupName = "Sizing", Order = 14)]
        public int MaxContractsMes { get; set; } = 10;

        /// <summary>Max contracts MNQ.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "MAX_CONTRACTS_MNQ", GroupName = "Sizing", Order = 15)]
        public int MaxContractsMnq { get; set; } = 10;
        #endregion

        #region Properties - Execution & Health
        /// <summary>Spread max ticks ES.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "SPREAD_MAX_TICKS_ES", GroupName = "Execution", Order = 0)]
        public int SpreadMaxTicksEs { get; set; } = 2;

        /// <summary>Spread max ticks NQ.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "SPREAD_MAX_TICKS_NQ", GroupName = "Execution", Order = 1)]
        public int SpreadMaxTicksNq { get; set; } = 2;

        /// <summary>Spread max ticks CL.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "SPREAD_MAX_TICKS_CL", GroupName = "Execution", Order = 2)]
        public int SpreadMaxTicksCl { get; set; } = 3;

        /// <summary>Spread max ticks GC.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "SPREAD_MAX_TICKS_GC", GroupName = "Execution", Order = 3)]
        public int SpreadMaxTicksGc { get; set; } = 3;

        /// <summary>Spread max ticks MES.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "SPREAD_MAX_TICKS_MES", GroupName = "Execution", Order = 4)]
        public int SpreadMaxTicksMes { get; set; } = 3;

        /// <summary>Spread max ticks MNQ.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "SPREAD_MAX_TICKS_MNQ", GroupName = "Execution", Order = 5)]
        public int SpreadMaxTicksMnq { get; set; } = 3;

        /// <summary>ATR min ES.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ATR_MIN_ES", GroupName = "Execution", Order = 10)]
        public double AtrMinEs { get; set; } = 6;

        /// <summary>ATR max ES.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ATR_MAX_ES", GroupName = "Execution", Order = 11)]
        public double AtrMaxEs { get; set; } = 50;

        /// <summary>ATR min NQ.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ATR_MIN_NQ", GroupName = "Execution", Order = 12)]
        public double AtrMinNq { get; set; } = 20;

        /// <summary>ATR max NQ.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ATR_MAX_NQ", GroupName = "Execution", Order = 13)]
        public double AtrMaxNq { get; set; } = 140;

        /// <summary>ATR min CL.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ATR_MIN_CL", GroupName = "Execution", Order = 14)]
        public double AtrMinCl { get; set; } = 0.25;

        /// <summary>ATR max CL.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ATR_MAX_CL", GroupName = "Execution", Order = 15)]
        public double AtrMaxCl { get; set; } = 2.0;

        /// <summary>ATR min GC.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ATR_MIN_GC", GroupName = "Execution", Order = 16)]
        public double AtrMinGc { get; set; } = 1.0;

        /// <summary>ATR max GC.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ATR_MAX_GC", GroupName = "Execution", Order = 17)]
        public double AtrMaxGc { get; set; } = 15.0;

        /// <summary>Queue persist seconds.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "QUEUE_PERSIST_SEC", GroupName = "Execution", Order = 20)]
        public int QueuePersistSec { get; set; } = 15;

        /// <summary>Queue max seconds.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "QUEUE_MAX_SEC", GroupName = "Execution", Order = 21)]
        public int QueueMaxSec { get; set; } = 45;

        /// <summary>MIT seconds.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "MIT_SEC", GroupName = "Execution", Order = 22)]
        public int MitSec { get; set; } = 20;

        /// <summary>Move stop to break-even at R.</summary>
        [NinjaScriptProperty]
        [Display(Name = "MOVE_STOP_TO_BE_AT_MFE_R", GroupName = "Execution", Order = 23)]
        public double MoveStopToBeAtMfeR { get; set; } = 1.0;

        /// <summary>Break-even offset at first target R.</summary>
        [NinjaScriptProperty]
        [Display(Name = "BE_OFFSET_AT_FIRST_TGT_R", GroupName = "Execution", Order = 24)]
        public double BeOffsetAtFirstTgtR { get; set; } = 0.2;
        #endregion

        #region Properties - Modules: MR-Asia
        /// <summary>Z-score threshold.</summary>
        [NinjaScriptProperty]
        [Display(Name = "MR_ASIA_Z", GroupName = "Module: MR-Asia", Order = 0)]
        public double MrAsiaZ { get; set; } = 2.0;

        /// <summary>RSI2 long threshold.</summary>
        [NinjaScriptProperty]
        [Display(Name = "MR_ASIA_RSI2_LONG", GroupName = "Module: MR-Asia", Order = 1)]
        public double MrAsiaRsi2Long { get; set; } = 10;

        /// <summary>RSI2 short threshold.</summary>
        [NinjaScriptProperty]
        [Display(Name = "MR_ASIA_RSI2_SHORT", GroupName = "Module: MR-Asia", Order = 2)]
        public double MrAsiaRsi2Short { get; set; } = 90;

        /// <summary>Stop ATR multiple.</summary>
        [NinjaScriptProperty]
        [Display(Name = "MR_ASIA_STOP_ATR", GroupName = "Module: MR-Asia", Order = 3)]
        public double MrAsiaStopAtr { get; set; } = 1.5;

        /// <summary>Timeout minutes.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "MR_ASIA_TIMEOUT_MIN", GroupName = "Module: MR-Asia", Order = 4)]
        public int MrAsiaTimeoutMin { get; set; } = 90;
        #endregion

        #region Properties - Modules: BO-London
        /// <summary>OR start time.</summary>
        [NinjaScriptProperty]
        [Display(Name = "BO_LONDON_OR_START", GroupName = "Module: BO-London", Order = 0)]
        public string BoLondonOrStart { get; set; } = "03:00";

        /// <summary>OR end time.</summary>
        [NinjaScriptProperty]
        [Display(Name = "BO_LONDON_OR_END", GroupName = "Module: BO-London", Order = 1)]
        public string BoLondonOrEnd { get; set; } = "03:30";

        /// <summary>Timeout minutes.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "BO_LONDON_TIMEOUT_MIN", GroupName = "Module: BO-London", Order = 2)]
        public int BoLondonTimeoutMin { get; set; } = 120;
        #endregion

        #region Properties - Modules: TP-EU / MR-VWAP
        /// <summary>TP-EU EMA fast.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "TP_EU_EMA_FAST", GroupName = "Module: TP-EU", Order = 0)]
        public int TpEuEmaFast { get; set; } = 20;

        /// <summary>TP-EU EMA slow.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "TP_EU_EMA_SLOW", GroupName = "Module: TP-EU", Order = 1)]
        public int TpEuEmaSlow { get; set; } = 50;

        /// <summary>TP-EU CCI period.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "TP_EU_CCI", GroupName = "Module: TP-EU", Order = 2)]
        public int TpEuCci { get; set; } = 20;

        /// <summary>TP-EU stop ATR multiple.</summary>
        [NinjaScriptProperty]
        [Display(Name = "TP_EU_STOP_ATR", GroupName = "Module: TP-EU", Order = 3)]
        public double TpEuStopAtr { get; set; } = 1.8;

        /// <summary>TP-EU trail ATR multiple.</summary>
        [NinjaScriptProperty]
        [Display(Name = "TP_EU_TRAIL_CHANDELIER_ATR", GroupName = "Module: TP-EU", Order = 4)]
        public double TpEuTrailChandelierAtr { get; set; } = 2.5;

        /// <summary>MR-VWAP Z threshold.</summary>
        [NinjaScriptProperty]
        [Display(Name = "MR_VWAP_Z", GroupName = "Module: MR-VWAP", Order = 0)]
        public double MrVwapZ { get; set; } = 1.6;

        /// <summary>MR-VWAP ADX max.</summary>
        [NinjaScriptProperty]
        [Display(Name = "MR_VWAP_ADX_MAX", GroupName = "Module: MR-VWAP", Order = 1)]
        public double MrVwapAdxMax { get; set; } = 18;

        /// <summary>MR-VWAP stop ATR multiple.</summary>
        [NinjaScriptProperty]
        [Display(Name = "MR_VWAP_STOP_ATR", GroupName = "Module: MR-VWAP", Order = 2)]
        public double MrVwapStopAtr { get; set; } = 1.3;

        /// <summary>MR-VWAP timeout minutes.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "MR_VWAP_TIMEOUT_MIN", GroupName = "Module: MR-VWAP", Order = 3)]
        public int MrVwapTimeoutMin { get; set; } = 120;
        #endregion

        #region Properties - Modules: AV-Pre
        /// <summary>AV-Pre pullback ATR multiple.</summary>
        [NinjaScriptProperty]
        [Display(Name = "AV_PRE_PULLBACK_ATR", GroupName = "Module: AV-Pre", Order = 0)]
        public double AvPrePullbackAtr { get; set; } = 0.5;

        /// <summary>AV-Pre stop ATR multiple.</summary>
        [NinjaScriptProperty]
        [Display(Name = "AV_PRE_STOP_ATR", GroupName = "Module: AV-Pre", Order = 1)]
        public double AvPreStopAtr { get; set; } = 1.2;

        /// <summary>AV-Pre exit ATR multiple.</summary>
        [NinjaScriptProperty]
        [Display(Name = "AV_PRE_EXIT_ATR", GroupName = "Module: AV-Pre", Order = 2)]
        public double AvPreExitAtr { get; set; } = 0.75;
        #endregion

        #region Properties - Modules: ORB-US
        /// <summary>Primary OR minutes.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "ORB_US_OR_PRIMARY_MIN", GroupName = "Module: ORB-US", Order = 0)]
        public int OrbUsOrPrimaryMin { get; set; } = 15;

        /// <summary>Alternate OR minutes.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "ORB_US_OR_ALTERNATE_MIN", GroupName = "Module: ORB-US", Order = 1)]
        public int OrbUsOrAlternateMin { get; set; } = 30;

        /// <summary>Vol switch percentile.</summary>
        [NinjaScriptProperty]
        [Display(Name = "ORB_US_SWITCH_PCTL", GroupName = "Module: ORB-US", Order = 2)]
        public double OrbUsSwitchPctl { get; set; } = 80;

        /// <summary>Timeout minutes.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "ORB_US_TIMEOUT_MIN", GroupName = "Module: ORB-US", Order = 3)]
        public int OrbUsTimeoutMin { get; set; } = 60;

        /// <summary>Target weight 1.</summary>
        [NinjaScriptProperty]
        [Display(Name = "TGT_W1", GroupName = "Module: ORB-US", Order = 4)]
        public double OrbUsTgtW1 { get; set; } = 0.40;

        /// <summary>Target weight 2.</summary>
        [NinjaScriptProperty]
        [Display(Name = "TGT_W2", GroupName = "Module: ORB-US", Order = 5)]
        public double OrbUsTgtW2 { get; set; } = 0.35;

        /// <summary>Target weight 3.</summary>
        [NinjaScriptProperty]
        [Display(Name = "TGT_W3", GroupName = "Module: ORB-US", Order = 6)]
        public double OrbUsTgtW3 { get; set; } = 0.25;

        /// <summary>Target multiple 1.</summary>
        [NinjaScriptProperty]
        [Display(Name = "TP1_MULT", GroupName = "Module: ORB-US", Order = 7)]
        public double OrbUsTp1Mult { get; set; } = 1.0;

        /// <summary>Target multiple 2.</summary>
        [NinjaScriptProperty]
        [Display(Name = "TP2_MULT", GroupName = "Module: ORB-US", Order = 8)]
        public double OrbUsTp2Mult { get; set; } = 1.5;

        /// <summary>Target multiple 3.</summary>
        [NinjaScriptProperty]
        [Display(Name = "TP3_MULT", GroupName = "Module: ORB-US", Order = 9)]
        public double OrbUsTp3Mult { get; set; } = 2.0;
        #endregion

        #region Properties - Modules: MR-Mid
        /// <summary>Z-score threshold.</summary>
        [NinjaScriptProperty]
        [Display(Name = "MR_MID_Z", GroupName = "Module: MR-Mid", Order = 0)]
        public double MrMidZ { get; set; } = 1.6;

        /// <summary>ADX max.</summary>
        [NinjaScriptProperty]
        [Display(Name = "MR_MID_ADX_MAX", GroupName = "Module: MR-Mid", Order = 1)]
        public double MrMidAdxMax { get; set; } = 18;

        /// <summary>MR-Mid stop ATR multiple.</summary>
        [NinjaScriptProperty]
        [Display(Name = "MR_MID_STOP_ATR", GroupName = "Module: MR-Mid", Order = 2)]
        public double MrMidStopAtr { get; set; } = 1.3;

        /// <summary>MR-Mid timeout minutes.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "MR_MID_TIMEOUT_MIN", GroupName = "Module: MR-Mid", Order = 3)]
        public int MrMidTimeoutMin { get; set; } = 120;
        #endregion

        #region Properties - Modules: TC-PH
        /// <summary>EMA fast.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "TC_PH_EMA_FAST", GroupName = "Module: TC-PH", Order = 0)]
        public int TcPhEmaFast { get; set; } = 20;

        /// <summary>EMA slow.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "TC_PH_EMA_SLOW", GroupName = "Module: TC-PH", Order = 1)]
        public int TcPhEmaSlow { get; set; } = 50;

        /// <summary>Slope lookback.</summary>
        [NinjaScriptProperty]
        [Range(1, int.MaxValue), Display(Name = "TC_PH_SLOPE_LOOKBACK", GroupName = "Module: TC-PH", Order = 2)]
        public int TcPhSlopeLookback { get; set; } = 20;
        #endregion

        #region Properties - Carry Flags
        /// <summary>Allow carry ES.</summary>
        [NinjaScriptProperty]
        [Display(Name = "CARRY_ALLOWED_ES", GroupName = "Carry", Order = 0)]
        public bool CarryAllowedEs { get; set; } = false;

        /// <summary>Allow carry NQ.</summary>
        [NinjaScriptProperty]
        [Display(Name = "CARRY_ALLOWED_NQ", GroupName = "Carry", Order = 1)]
        public bool CarryAllowedNq { get; set; } = false;

        /// <summary>Allow carry CL.</summary>
        [NinjaScriptProperty]
        [Display(Name = "CARRY_ALLOWED_CL", GroupName = "Carry", Order = 2)]
        public bool CarryAllowedCl { get; set; } = false;

        /// <summary>Allow carry GC.</summary>
        [NinjaScriptProperty]
        [Display(Name = "CARRY_ALLOWED_GC", GroupName = "Carry", Order = 3)]
        public bool CarryAllowedGc { get; set; } = false;

        /// <summary>Allow carry MES.</summary>
        [NinjaScriptProperty]
        [Display(Name = "CARRY_ALLOWED_MES", GroupName = "Carry", Order = 4)]
        public bool CarryAllowedMes { get; set; } = true;

        /// <summary>Allow carry MNQ.</summary>
        [NinjaScriptProperty]
        [Display(Name = "CARRY_ALLOWED_MNQ", GroupName = "Carry", Order = 5)]
        public bool CarryAllowedMnq { get; set; } = true;
        #endregion

        #region State Management
        /// <summary>Handle state transitions.</summary>
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "ECP MultiSession 23x5 strategy";
                Name = "ECP_MultiSession_23x5_v1_1";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                BarsRequiredToTrade = 20;
            }
            else if (State == State.Configure)
            {
                autoFlatEt = TimeSpan.Parse(AutoFlatEt);
                reopenEt = TimeSpan.Parse(ReopenEt);
                resetEdgeEt = TimeSpan.Parse(ResetEdgeRecalcEt);
            }
            else if (State == State.DataLoaded)
            {
                atr14 = ATR(14);
                rsi2 = RSI(2, 1);
                ema20 = EMA(Close, 20);
                ema50 = EMA(Close, 50);
                adx14 = ADX(14);
                cci20 = CCI(20);
                atr20 = ATR(20);

                highWaterMark = GetAccountEquity();
                maxBalance = Math.Max(highWaterMark, InitialMaxBalance);
                logWriter = new StreamWriter(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Name + "_log.csv"), true);
                logWriter.AutoFlush = true;
                logWriter.WriteLine("UTC,ET,Account,Instrument,SessionID,Module,AutoMode,Signal,EntryPx,StopPx,TargetPx,ATR14,ZVWAP,AvwapSlope,EMA20,EMA50,ADX,Spread,Size,FillType,ReasonCode,LockState,HWM,MaxBal,EquityReal,EquityUnreal,DayPnLReal,DayTrades,ConsecLosses,EdgeScore");
                Print("StartupBanner: ECP MultiSession strategy initialized.");
            }
            else if (State == State.Terminated)
            {
                if (logWriter != null)
                {
                    logWriter.Flush();
                    logWriter.Close();
                }
            }
        }
        #endregion

        #region OnBarUpdate
        /// <summary>Main bar update loop.</summary>
        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade)
                return;

            DateTime et = TimeZoneInfo.ConvertTime(Time[0], eastern);
            TimeSpan tod = et.TimeOfDay;

            CheckHealth();

            CheckSessionEndExits(tod);
            HandleSchedule(tod);
            ProcessPendingEntries();
            if (inNoTradeBlock)
            {
                Log("CORE", "NoTradeBlock1655_1800");
                return;
            }
            if (tradeLock)
                return;

            CheckRisk();
            ProcessPendingEntries();
            if (tradeLock)
                return;

            SessionId session = GetSession(tod);
            ModuleType mod = ResolveModule(session);

            switch (mod)
            {
                case ModuleType.MR_ASIA:
                    ExecuteMrAsia();
                    break;
                case ModuleType.BO_LONDON:
                    ExecuteBoLondon();
                    break;
                case ModuleType.TP_EU:
                    ExecuteTpEu();
                    break;
                case ModuleType.MR_VWAP:
                    ExecuteMrVwap();
                    break;
                case ModuleType.AV_PRE:
                    ExecuteAvPre();
                    break;
                case ModuleType.ORB_US:
                    ExecuteOrbUs();
                    break;
                case ModuleType.MR_MID:
                    ExecuteMrMid();
                    break;
                case ModuleType.TC_PH:
                    ExecuteTcPh();
                    break;
            }

            CheckBreakEven();
        }
        #endregion

        #region Router
        private SessionId GetSession(TimeSpan tod)
        {
            if (tod >= new TimeSpan(18, 0, 0) || tod < new TimeSpan(1, 0, 0)) return SessionId.ASIA;
            if (tod >= new TimeSpan(3, 0, 0) && tod < new TimeSpan(5, 0, 0)) return SessionId.LONDON_OPEN;
            if (tod >= new TimeSpan(5, 0, 0) && tod < new TimeSpan(7, 0, 0)) return SessionId.EU_MID;
            if (tod >= new TimeSpan(7, 0, 0) && tod < new TimeSpan(9, 30, 0)) return SessionId.US_PRE;
            if (tod >= new TimeSpan(9, 30, 0) && tod < new TimeSpan(10, 30, 0)) return SessionId.US_OPEN;
            if (tod >= new TimeSpan(10, 30, 0) && tod < new TimeSpan(14, 30, 0)) return SessionId.US_MID;
            if (tod >= new TimeSpan(15, 0, 0) && tod < autoFlatEt) return SessionId.US_POWER;
            return SessionId.NONE;
        }

        private ModuleType ResolveModule(SessionId session)
        {
            if (!AutoSelectBestSessionStrategy)
                return GetForcedModule(session);
            Tuple<ModuleType, double> chosen;
            if (chosenModules.TryGetValue(session, out chosen))
                return chosen.Item1;
            return GetForcedModule(session);
        }

        private ModuleType GetForcedModule(SessionId session)
        {
            switch (session)
            {
                case SessionId.ASIA: return ForcedStrategyAsia;
                case SessionId.LONDON_OPEN: return ForcedStrategyLondon;
                case SessionId.EU_MID: return ForcedStrategyEuMid;
                case SessionId.US_PRE: return ForcedStrategyUsPre;
                case SessionId.US_OPEN: return ForcedStrategyUsOpen;
                case SessionId.US_MID: return ForcedStrategyUsMid;
                case SessionId.US_POWER: return ForcedStrategyUsPower;
                default: return ModuleType.NONE;
            }
        }

        private ModuleType ModuleFromSignal(string sig)
        {
            if (string.IsNullOrEmpty(sig)) return ModuleType.NONE;
            if (sig.StartsWith("MR_ASIA")) return ModuleType.MR_ASIA;
            if (sig.StartsWith("BO_LONDON")) return ModuleType.BO_LONDON;
            if (sig.StartsWith("TP_EU")) return ModuleType.TP_EU;
            if (sig.StartsWith("MR_VWAP")) return ModuleType.MR_VWAP;
            if (sig.StartsWith("AV_PRE")) return ModuleType.AV_PRE;
            if (sig.StartsWith("ORB_US")) return ModuleType.ORB_US;
            if (sig.StartsWith("MR_MID")) return ModuleType.MR_MID;
            if (sig.StartsWith("TC_PH")) return ModuleType.TC_PH;
            return ModuleType.NONE;
        }

        private SessionId SessionForModule(ModuleType mod)
        {
            switch (mod)
            {
                case ModuleType.MR_ASIA: return SessionId.ASIA;
                case ModuleType.BO_LONDON: return SessionId.LONDON_OPEN;
                case ModuleType.TP_EU:
                case ModuleType.MR_VWAP: return SessionId.EU_MID;
                case ModuleType.AV_PRE: return SessionId.US_PRE;
                case ModuleType.ORB_US: return SessionId.US_OPEN;
                case ModuleType.MR_MID: return SessionId.US_MID;
                case ModuleType.TC_PH: return SessionId.US_POWER;
                default: return SessionId.NONE;
            }
        }

        private void RecordTradeStats(string sig, double pnl, double riskUsd, double mfe, double mae)
        {
            ModuleType mod = ModuleFromSignal(sig);
            SessionId session = SessionForModule(mod);
            if (mod == ModuleType.NONE || session == SessionId.NONE)
                return;
            if (riskUsd == 0)
                riskUsd = Math.Abs(pnl);
            if (riskUsd == 0)
                return;
            TradeStat ts = new TradeStat();
            ts.closeEt = TimeZoneInfo.ConvertTime(Time[0], eastern);
            ts.session = session;
            ts.module = mod;
            ts.R = pnl / riskUsd;
            ts.mfeUsd = mfe;
            ts.maeUsd = mae;
            tradeStats.Add(ts);
            DateTime cutoff = ts.closeEt.Date.AddDays(-EdgeLookbackDays);
            for (int i = tradeStats.Count - 1; i >= 0; i--)
            {
                if (tradeStats[i].closeEt.Date < cutoff)
                    tradeStats.RemoveAt(i);
            }
        }

        private void RecomputeEdgeStats()
        {
            edgeStats.Clear();
            chosenModules.Clear();
            DateTime nowEt = TimeZoneInfo.ConvertTime(Time[0], eastern);
            DateTime cutoff = nowEt.Date.AddDays(-EdgeLookbackDays);
            // organize trades by session and module
            Dictionary<SessionId, Dictionary<ModuleType, List<TradeStat>>> buckets = new Dictionary<SessionId, Dictionary<ModuleType, List<TradeStat>>>();
            foreach (var ts in tradeStats)
            {
                if (ts.closeEt.Date < cutoff)
                    continue;
                Dictionary<ModuleType, List<TradeStat>> byMod;
                if (!buckets.TryGetValue(ts.session, out byMod))
                {
                    byMod = new Dictionary<ModuleType, List<TradeStat>>();
                    buckets[ts.session] = byMod;
                }
                List<TradeStat> list;
                if (!byMod.TryGetValue(ts.module, out list))
                {
                    list = new List<TradeStat>();
                    byMod[ts.module] = list;
                }
                list.Add(ts);
            }

            foreach (var kvSession in buckets)
            {
                SessionId session = kvSession.Key;
                edgeStats[session] = new Dictionary<ModuleType, EdgeStats>();
                foreach (var kvMod in kvSession.Value)
                {
                    List<TradeStat> list = kvMod.Value;
                    double sumR = 0;
                    double sumR2 = 0;
                    double sumRatio = 0;
                    int n = 0;
                    int nRatio = 0;
                    foreach (var ts in list)
                    {
                        sumR += ts.R;
                        sumR2 += ts.R * ts.R;
                        if (ts.maeUsd != 0)
                        {
                            sumRatio += ts.mfeUsd / Math.Abs(ts.maeUsd);
                            nRatio++;
                        }
                        n++;
                    }
                    double expectancy = n > 0 ? sumR / n : 0;
                    double variance = n > 1 ? (sumR2 - (sumR * sumR) / n) / (n - 1) : 0;
                    double std = variance > 0 ? Math.Sqrt(variance) : 0;
                    double sharpe = std > 0 ? expectancy / std : 0;
                    double mfeMae = nRatio > 0 ? sumRatio / nRatio : double.MaxValue;
                    EdgeStats stats = new EdgeStats();
                    stats.expectancy = expectancy;
                    stats.sharpe = sharpe;
                    stats.mfeMae = mfeMae;
                    stats.trades = n;
                    stats.lastComputed = Time[0];
                    edgeStats[session][kvMod.Key] = stats;
                }

                // choose best module
                ModuleType best = ModuleType.NONE;
                double bestScore = double.MinValue;
                double bestMfeMae = double.MaxValue;
                foreach (var kvMod in edgeStats[session])
                {
                    EdgeStats st = kvMod.Value;
                    if (st.trades < 10)
                        continue;
                    double score = st.expectancy * st.sharpe;
                    if (score > bestScore || (Math.Abs(score - bestScore) < 1e-6 && st.mfeMae < bestMfeMae))
                    {
                        bestScore = score;
                        bestMfeMae = st.mfeMae;
                        best = kvMod.Key;
                    }
                }
                if (best != ModuleType.NONE)
                {
                    chosenModules[session] = Tuple.Create(best, bestScore);
                    Log(best.ToString(), "RouterRecompute", "AUTO", 0, 0, 0, "", 0);
                }
                else
                {
                    ModuleType forced = GetForcedModule(session);
                    chosenModules[session] = Tuple.Create(forced, 0.0);
                    Log(forced.ToString(), "RouterRecompute", "AUTO", 0, 0, 0, "", 0);
                }
            }
        }
        #endregion

        #region Schedule
        private void HandleSchedule(TimeSpan tod)
        {
            // Auto-flat once at 16:55 ET
            if (tod >= autoFlatEt && tod < reopenEt)
            {
                if (!inNoTradeBlock)
                {
                    inNoTradeBlock = true;
                    if (lastAutoFlatDate.Date != Time[0].Date)
                        lastAutoFlatDate = Time[0].Date;
                    if (activeSignal.StartsWith("TC_PH"))
                        Log("TC_PH", "SessionEndExit", activeSignal, Close[0], 0, 0, "", Position.Quantity);
                    Flatten("AutoFlat1655");
                }
                return;
            }

            if (tod >= reopenEt)
                inNoTradeBlock = false;

            // daily reset at 17:05 ET
            if (!didResetToday && prevTod < resetEdgeEt && tod >= resetEdgeEt)
            {
                dailyRealized = 0;
                dayTrades = 0;
                consecutiveLosses = 0;
                rejectLatched = false;
                isStaleLatched = false;
                disconnectLatched = false;
                if (ManualUnlock)
                {
                    tradeLock = false;
                    Log("CORE", "ManualUnlock");
                    ManualUnlock = false;
                }
                RecomputeEdgeStats();
                didResetToday = true;
            }

            // reset flag after midnight
            if (tod < prevTod)
                didResetToday = false;

            prevTod = tod;
        }
        #endregion

        private void CheckSessionEndExits(TimeSpan tod)
        {
            if (activeSignal.StartsWith("BO_LONDON") && tod >= new TimeSpan(5, 0, 0))
            {
                Flatten("SessionEndExit", "BO_LONDON");
                lastSignalBarTime = Time[0];
            }
            else if (activeSignal.StartsWith("AV_PRE") && tod >= new TimeSpan(9, 30, 0))
            {
                Flatten("SessionEndExit", "AV_PRE");
                lastSignalBarTime = Time[0];
            }
            else if (activeSignal.StartsWith("TP_EU") && tod >= new TimeSpan(7, 0, 0))
            {
                Flatten("SessionEndExit", "TP_EU");
                lastSignalBarTime = Time[0];
            }
            else if (activeSignal.StartsWith("MR_VWAP") && tod >= new TimeSpan(7, 0, 0))
            {
                Flatten("SessionEndExit", "MR_VWAP");
                lastSignalBarTime = Time[0];
            }
            else if (activeSignal.StartsWith("ORB_US") && tod >= new TimeSpan(10, 30, 0))
            {
                Flatten("SessionEndExit", "ORB_US");
                lastSignalBarTime = Time[0];
            }
            else if (activeSignal.StartsWith("MR_MID") && tod >= new TimeSpan(14, 30, 0))
            {
                Flatten("SessionEndExit", "MR_MID");
                lastSignalBarTime = Time[0];
            }
            else if (activeSignal.StartsWith("TC_PH") && tod >= new TimeSpan(16, 55, 0))
            {
                Flatten("SessionEndExit", "TC_PH");
                lastSignalBarTime = Time[0];
            }
        }

        #region Risk
        private void CheckRisk()
        {
            double cash = 0;
            try { cash = Account.Get(AccountItem.CashValue, Currency.UsDollar); } catch { }
            try { unrealizedNow = Account.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar); } catch { unrealizedNow = 0; }

            double equity = cash + unrealizedNow;
            if (equity > highWaterMark) highWaterMark = equity;
            if (equity > maxBalance) maxBalance = equity;

            double tddTrigger = highWaterMark - EcpTdtAmountUsd;
            double absTrigger = maxBalance - EcpAbsHaltDeltaUsd;

            if (EcpEnableTdd && equity <= tddTrigger)
            {
                tradeLock = true;
                Flatten("TDDHit");
                return;
            }

            if (EcpEnableAbsHalt && equity <= absTrigger)
            {
                tradeLock = true;
                Flatten("AbsHalt");
                return;
            }

            if (dailyRealized <= -EcpDailyLossCapUsd)
            {
                tradeLock = true;
                Flatten("DailyCap700");
                return;
            }

            if (consecutiveLosses >= EcpMaxConsecLosses)
            {
                tradeLock = true;
                Flatten("ConsecLoss2");
                return;
            }
        }

        private double GetAccountEquity()
        {
            double cash = 0, unreal = 0;
            try { cash = Account.Get(AccountItem.CashValue, Currency.UsDollar); } catch { }
            try { unreal = Account.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar); } catch { }
            return cash + unreal;
        }
        #endregion

        #region Sizing
        private int CalcContracts(double stopDistance)
        {
            double equity = GetAccountEquity();
            double allowable = 0;
            switch (PositionSizingMode)
            {
                case SizingMode.FIXED_CONTRACTS:
                    allowable = FixedQty;
                    break;
                case SizingMode.RISK_PER_TRADE_USD:
                    if (stopDistance > 0)
                        allowable = RiskPerTradeUsd / (stopDistance * Instrument.MasterInstrument.PointValue);
                    break;
                case SizingMode.RISK_PCT_EQUITY:
                    if (stopDistance > 0)
                        allowable = (equity * RiskPctEquity / 100.0) / (stopDistance * Instrument.MasterInstrument.PointValue);
                    break;
                case SizingMode.KELLY_FRACTIONAL:
                    if (stopDistance > 0)
                        allowable = (equity * KellyFraction) / (stopDistance * Instrument.MasterInstrument.PointValue);
                    break;
            }

            int qty = (int)Math.Floor(allowable);
            qty = Math.Min(qty, CapForCurrentSymbol());
            if (qty < 1)
                return 0;
            return qty;
        }
        #endregion

        #region Entry Filters
        private bool CanEnter(double stopDistance)
        {
            if (inNoTradeBlock) return false;

            double equity = GetAccountEquity();
            double tddTrigger = highWaterMark - EcpTdtAmountUsd + EcpTdtCushionUsd;
            double absTrigger = maxBalance - EcpAbsHaltDeltaUsd + EcpTdtCushionUsd;
            if (equity < tddTrigger)
            {
                Log("CORE", "TDDCushionBlock");
                return false;
            }
            if (equity < absTrigger)
            {
                Log("CORE", "AbsHaltCushionBlock");
                return false;
            }

            if (!AtrInRange())
            {
                Log("CORE", "VolClampGate");
                return false;
            }

            if (GetSpreadTicks() > GetSpreadMax())
            {
                Log("CORE", "SpreadGate");
                return false;
            }

            if (lastSignalBarTime == Time[0])
                return false;

            return true;
        }

        private int GetSpreadTicks()
        {
            double bid = GetCurrentBid();
            double ask = GetCurrentAsk();
            double spread = (ask - bid) / Instrument.MasterInstrument.TickSize;
            return (int)Math.Round(spread);
        }

        private int GetSpreadMax()
        {
            string name = Instrument.MasterInstrument.Name;
            switch (name)
            {
                case "ES": return SpreadMaxTicksEs;
                case "NQ": return SpreadMaxTicksNq;
                case "CL": return SpreadMaxTicksCl;
                case "GC": return SpreadMaxTicksGc;
                case "MES": return SpreadMaxTicksMes;
                case "MNQ": return SpreadMaxTicksMnq;
                default: return SpreadMaxTicksEs;
            }
        }

        private bool AtrInRange()
        {
            double atr = atr14[0];
            string name = Instrument.MasterInstrument.Name;
            switch (name)
            {
                case "ES": return atr >= AtrMinEs && atr <= AtrMaxEs;
                case "NQ": return atr >= AtrMinNq && atr <= AtrMaxNq;
                case "CL": return atr >= AtrMinCl && atr <= AtrMaxCl;
                case "GC": return atr >= AtrMinGc && atr <= AtrMaxGc;
                case "MES": return atr >= AtrMinEs && atr <= AtrMaxEs;
                case "MNQ": return atr >= AtrMinNq && atr <= AtrMaxNq;
                default: return true;
            }
        }

        private int CapForCurrentSymbol()
        {
            string name = Instrument.MasterInstrument.Name;
            switch (name)
            {
                case "ES": return MaxContractsEs;
                case "NQ": return MaxContractsNq;
                case "CL": return MaxContractsCl;
                case "GC": return MaxContractsGc;
                case "MES": return MaxContractsMes;
                case "MNQ": return MaxContractsMnq;
                default: return 1;
            }
        }

        private int GetK1TicksForSymbol()
        {
            string name = Instrument.MasterInstrument.Name;
            switch (name)
            {
                case "ES":
                case "MES":
                    return 8;
                case "NQ":
                case "MNQ":
                    return 12;
                case "CL":
                    return 15;
                case "GC":
                    return 12;
                default:
                    return 8;
            }
        }

        private DateTime lastSignalBarTime = Core.Globals.MinDate;
        #endregion

        #region Indicators/AVWAP
        private struct AvwapCacheKey
        {
            public DateTime AnchorEt;
            public int Bip;
            public override int GetHashCode()
            {
                return AnchorEt.GetHashCode() ^ Bip.GetHashCode();
            }
            public override bool Equals(object obj)
            {
                if (!(obj is AvwapCacheKey))
                    return false;
                AvwapCacheKey other = (AvwapCacheKey)obj;
                return AnchorEt == other.AnchorEt && Bip == other.Bip;
            }
        }

        private struct AvwapCacheVal
        {
            public double SumPv;
            public double SumV;
            public DateTime LastBarEt;
        }

        private System.Collections.Generic.Dictionary<AvwapCacheKey, AvwapCacheVal> avwapCache =
            new System.Collections.Generic.Dictionary<AvwapCacheKey, AvwapCacheVal>();

        private double GetAvwapSince(DateTime anchorEt, DateTime? endEt = null)
        {
            if (endEt == null)
            {
                AvwapCacheKey key = new AvwapCacheKey { AnchorEt = anchorEt, Bip = BarsInProgress };
                AvwapCacheVal val;
                DateTime etNow = TimeZoneInfo.ConvertTime(Time[0], eastern);
                if (!avwapCache.TryGetValue(key, out val))
                {
                    val.SumPv = 0;
                    val.SumV = 0;
                    val.LastBarEt = anchorEt.AddMinutes(-1);
                    for (int i = CurrentBar; i >= 0; i--)
                    {
                        DateTime barEt = TimeZoneInfo.ConvertTime(Time[i], eastern);
                        if (barEt < anchorEt)
                            break;
                        val.SumPv += Close[i] * Volume[i];
                        val.SumV += Volume[i];
                        val.LastBarEt = barEt;
                    }
                    avwapCache[key] = val;
                }
                else if (etNow > val.LastBarEt)
                {
                    val.SumPv += Close[0] * Volume[0];
                    val.SumV += Volume[0];
                    val.LastBarEt = etNow;
                    avwapCache[key] = val;
                }
                return val.SumV > 0 ? val.SumPv / val.SumV : Close[0];
            }
            else
            {
                DateTime end = (DateTime)endEt;
                double pv = 0;
                double vol = 0;
                for (int i = CurrentBar; i >= 0; i--)
                {
                    DateTime et = TimeZoneInfo.ConvertTime(Time[i], eastern);
                    if (et < anchorEt)
                        break;
                    if (et > end)
                        continue;
                    pv += Close[i] * Volume[i];
                    vol += Volume[i];
                }
                return vol > 0 ? pv / vol : Close[0];
            }
        }

        private double GetZToAvwap(int periodMinutes, DateTime anchorEt)
        {
            double avwap = GetAvwapSince(anchorEt);
            int bars = (int)Math.Round((double)periodMinutes / BarsPeriod.Value);
            if (bars < 20)
                bars = 20;
            if (bars > CurrentBar + 1)
                bars = CurrentBar + 1;
            double sum = 0;
            for (int i = 0; i < bars; i++)
                sum += Close[i] - avwap;
            double mean = sum / bars;
            double var = 0;
            for (int i = 0; i < bars; i++)
            {
                double d = (Close[i] - avwap) - mean;
                var += d * d;
            }
            double std = Math.Sqrt(var / bars);
            if (std < Instrument.MasterInstrument.TickSize)
                return 0;
            return (Close[0] - avwap - mean) / std;
        }
        #endregion

        #region Modules
        private void ExecuteMrAsia()
        {
            DateTime et = TimeZoneInfo.ConvertTime(Time[0], eastern);
            DateTime anchor = GetAvwapAnchor();
            double z = GetZToAvwap(120, anchor);

            if (activeSignal == "MR_ASIA_LONG" && Position.MarketPosition == MarketPosition.Long)
            {
                double av = GetAvwapSince(anchor);
                if (Close[0] >= av || rsi2[0] >= 80 || Time[0] >= mrAsiaEntryTime.AddMinutes(MrAsiaTimeoutMin))
                {
                    string rc = Time[0] >= mrAsiaEntryTime.AddMinutes(MrAsiaTimeoutMin) ? "Timeout" : "ExitTP";
                    ExitLong("MR_ASIA_EXIT", "MR_ASIA_LONG");
                    Log("MR_ASIA", rc, "MR_ASIA_LONG", Close[0], 0, 0, "", Position.Quantity, z, 0);
                    mrAsiaEntryTime = Core.Globals.MinDate;
                }
                return;
            }
            if (activeSignal == "MR_ASIA_SHORT" && Position.MarketPosition == MarketPosition.Short)
            {
                double av = GetAvwapSince(anchor);
                if (Close[0] <= av || rsi2[0] <= 20 || Time[0] >= mrAsiaEntryTime.AddMinutes(MrAsiaTimeoutMin))
                {
                    string rc = Time[0] >= mrAsiaEntryTime.AddMinutes(MrAsiaTimeoutMin) ? "Timeout" : "ExitTP";
                    ExitShort("MR_ASIA_EXIT", "MR_ASIA_SHORT");
                    Log("MR_ASIA", rc, "MR_ASIA_SHORT", Close[0], 0, 0, "", Position.Quantity, z, 0);
                    mrAsiaEntryTime = Core.Globals.MinDate;
                }
                return;
            }

            if (!mrAsiaLongReady && z >= -0.5) mrAsiaLongReady = true;
            if (!mrAsiaShortReady && z <= 0.5) mrAsiaShortReady = true;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            double stopDist = Math.Max(atr14[0] * MrAsiaStopAtr, GetK1TicksForSymbol() * Instrument.MasterInstrument.TickSize);
            if (!CanEnter(stopDist))
                return;
            int stopTicks = (int)Math.Round(stopDist / Instrument.MasterInstrument.TickSize);

            if (mrAsiaLongReady && z <= -MrAsiaZ && rsi2[0] <= MrAsiaRsi2Long)
            {
                int qty = CalcContracts(stopDist);
                if (qty < 1)
                    return;
                double entryPrice = Close[0];
                double stopPrice = entryPrice - stopDist;
                SetStopLoss("MR_ASIA_LONG", CalculationMode.Ticks, stopTicks, false);
                entryInitialRiskUsd["MR_ASIA_LONG"] = stopDist * Instrument.MasterInstrument.PointValue * qty;
                EnterLong(qty, "MR_ASIA_LONG");
                Log("MR_ASIA", "Enter", "MR_ASIA_LONG", entryPrice, stopPrice, 0, "", qty, z, 0);
                mrAsiaEntryTime = Time[0];
                mrAsiaLongReady = false;
                lastSignalBarTime = Time[0];
            }
            else if (mrAsiaShortReady && z >= MrAsiaZ && rsi2[0] >= MrAsiaRsi2Short)
            {
                int qty = CalcContracts(stopDist);
                if (qty < 1)
                    return;
                double entryPrice = Close[0];
                double stopPrice = entryPrice + stopDist;
                SetStopLoss("MR_ASIA_SHORT", CalculationMode.Ticks, stopTicks, false);
                entryInitialRiskUsd["MR_ASIA_SHORT"] = stopDist * Instrument.MasterInstrument.PointValue * qty;
                EnterShort(qty, "MR_ASIA_SHORT");
                Log("MR_ASIA", "Enter", "MR_ASIA_SHORT", entryPrice, stopPrice, 0, "", qty, z, 0);
                mrAsiaEntryTime = Time[0];
                mrAsiaShortReady = false;
                lastSignalBarTime = Time[0];
            }
        }

        private void ExecuteBoLondon()
        {
            TimeSpan tod = TimeZoneInfo.ConvertTime(Time[0], eastern).TimeOfDay;
            TimeSpan sessionStart = new TimeSpan(3, 0, 0);
            TimeSpan sessionEnd   = new TimeSpan(5, 0, 0);
            if (tod < sessionStart || tod >= sessionEnd) return;

            TimeSpan orStart = TimeSpan.Parse(BoLondonOrStart);
            TimeSpan orEnd   = TimeSpan.Parse(BoLondonOrEnd);

            if (boCalcDate != Time[0].Date && tod >= orStart)
            {
                boOrHigh = double.MinValue;
                boOrLow  = double.MaxValue;
                boOrComplete = false;
                boOrEnd = new DateTime(Time[0].Year, Time[0].Month, Time[0].Day, orEnd.Hours, orEnd.Minutes, 0);
                boCalcDate = Time[0].Date;
            }

            if (!boOrComplete)
            {
                if (tod < orEnd)
                {
                    boOrHigh = Math.Max(boOrHigh, High[0]);
                    boOrLow  = Math.Min(boOrLow,  Low[0]);
                    return;
                }
                else
                {
                    boOrComplete = true;
                    return;
                }
            }

            if (boEntryTime != Core.Globals.MinDate && Position.MarketPosition != MarketPosition.Flat)
            {
                if (Time[0] - boEntryTime >= TimeSpan.FromMinutes(BoLondonTimeoutMin))
                {
                    Flatten("Timeout", "BO_LONDON");
                    boEntryTime = Core.Globals.MinDate;
                    lastSignalBarTime = Time[0];
                }
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat) return;

            double tick = Instrument.MasterInstrument.TickSize;
            double range = Math.Max(tick, boOrHigh - boOrLow);

            if (Close[1] <= boOrHigh && Close[0] > boOrHigh)
            {
                double entryPrice = Instrument.MasterInstrument.Round2TickSize(boOrHigh + tick);
                double stopPrice  = Instrument.MasterInstrument.Round2TickSize(boOrLow  - tick);
                double stopDist   = Math.Max(entryPrice - stopPrice, GetK1TicksForSymbol() * tick);
                if (!CanEnter(stopDist)) return;

                int qty = CalcContracts(stopDist);
                if (qty < 1) return;

                double tPx = Instrument.MasterInstrument.Round2TickSize(entryPrice + 1.5 * range);
                int stopTicks = (int)Math.Round(stopDist / tick);
                SetStopLoss("BO_LONDON_LONG", CalculationMode.Ticks, stopTicks, false);
                ExitLongLimit(qty, true, tPx, "TP_BO_LONG", "BO_LONDON_LONG");

                int trailTicks = (int)Math.Max(1, Math.Round(0.8 * atr14[0] / tick));
                SetTrailStop("BO_LONDON_LONG", CalculationMode.Ticks, trailTicks, false);

                EnterLongStopMarket(qty, entryPrice, "BO_LONDON_LONG");
                entryInitialRiskUsd["BO_LONDON_LONG"] = stopDist * Instrument.MasterInstrument.PointValue * qty;
                Log("BO_LONDON", "Enter", "BO_LONDON_LONG", entryPrice, stopPrice, tPx, "", qty);
                boEntryTime = Time[0];
                lastSignalBarTime = Time[0];
                return;
            }

            if (Close[1] >= boOrLow && Close[0] < boOrLow)
            {
                double entryPrice = Instrument.MasterInstrument.Round2TickSize(boOrLow - tick);
                double stopPrice  = Instrument.MasterInstrument.Round2TickSize(boOrHigh + tick);
                double stopDist   = Math.Max(stopPrice - entryPrice, GetK1TicksForSymbol() * tick);
                if (!CanEnter(stopDist)) return;

                int qty = CalcContracts(stopDist);
                if (qty < 1) return;

                double tPx = Instrument.MasterInstrument.Round2TickSize(entryPrice - 1.5 * range);
                int stopTicks = (int)Math.Round(stopDist / tick);
                SetStopLoss("BO_LONDON_SHORT", CalculationMode.Ticks, stopTicks, false);
                ExitShortLimit(qty, true, tPx, "TP_BO_SHORT", "BO_LONDON_SHORT");

                int trailTicks = (int)Math.Max(1, Math.Round(0.8 * atr14[0] / tick));
                SetTrailStop("BO_LONDON_SHORT", CalculationMode.Ticks, trailTicks, false);

                EnterShortStopMarket(qty, entryPrice, "BO_LONDON_SHORT");
                entryInitialRiskUsd["BO_LONDON_SHORT"] = stopDist * Instrument.MasterInstrument.PointValue * qty;
                Log("BO_LONDON", "Enter", "BO_LONDON_SHORT", entryPrice, stopPrice, tPx, "", qty);
                boEntryTime = Time[0];
                lastSignalBarTime = Time[0];
                return;
            }
        }

        private void ExecuteTpEu()
        {
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            // Trend regime checks
            bool trendLong = ema20[0] > ema50[0] && EmaSlope(ema50, 30) > 0;
            bool trendShort = ema20[0] < ema50[0] && EmaSlope(ema50, 30) < 0;

            double stopBase = atr14[0] * TpEuStopAtr;
            double k1 = GetK1TicksForSymbol() * Instrument.MasterInstrument.TickSize;

            // LONG pullback case
            if (trendLong && Close[0] <= ema20[0] && Close[0] > ema50[0] && CrossAbove(cci20, -100, 1))
            {
                double stopDist = Math.Max(stopBase, k1);
                if (!CanEnter(stopDist))
                    return;
                int qty = CalcContracts(stopDist);
                if (qty < 1)
                    return;

                double entryPrice = Close[0];
                int stopTicks = (int)Math.Round(stopDist / Instrument.MasterInstrument.TickSize);
                SetStopLoss("TP_EU_LONG", CalculationMode.Ticks, stopTicks, false);

                int trailTicks = (int)Math.Max(1, Math.Round(TpEuTrailChandelierAtr * atr20[0] / Instrument.MasterInstrument.TickSize));
                SetTrailStop("TP_EU_LONG", CalculationMode.Ticks, trailTicks, false);

                entryInitialRiskUsd["TP_EU_LONG"] = stopDist * Instrument.MasterInstrument.PointValue * qty;
                EnterLong(qty, "TP_EU_LONG");
                double z = GetZToAvwap(240, GetAvwapAnchor());
                Log("TP_EU", "Enter", "TP_EU_LONG", entryPrice, entryPrice - stopDist, 0, "Trend", qty, z);
                lastSignalBarTime = Time[0];
                return;
            }

            // SHORT pullback case (inverse)
            if (trendShort && Close[0] >= ema20[0] && Close[0] < ema50[0] && CrossBelow(cci20, 100, 1))
            {
                double stopDist = Math.Max(stopBase, k1);
                if (!CanEnter(stopDist))
                    return;
                int qty = CalcContracts(stopDist);
                if (qty < 1)
                    return;

                double entryPrice = Close[0];
                int stopTicks = (int)Math.Round(stopDist / Instrument.MasterInstrument.TickSize);
                SetStopLoss("TP_EU_SHORT", CalculationMode.Ticks, stopTicks, false);

                int trailTicks = (int)Math.Max(1, Math.Round(TpEuTrailChandelierAtr * atr20[0] / Instrument.MasterInstrument.TickSize));
                SetTrailStop("TP_EU_SHORT", CalculationMode.Ticks, trailTicks, false);

                entryInitialRiskUsd["TP_EU_SHORT"] = stopDist * Instrument.MasterInstrument.PointValue * qty;
                EnterShort(qty, "TP_EU_SHORT");
                double z = GetZToAvwap(240, GetAvwapAnchor());
                Log("TP_EU", "Enter", "TP_EU_SHORT", entryPrice, entryPrice + stopDist, 0, "Trend", qty, z);
                lastSignalBarTime = Time[0];
                return;
            }
        }

        private void ExecuteMrVwap()
        {
            DateTime et = TimeZoneInfo.ConvertTime(Time[0], eastern);
            if (activeSignal.StartsWith("MR_VWAP") && Position.MarketPosition != MarketPosition.Flat)
            {
                DateTime anchor = GetAvwapAnchor();
                double avwap = GetAvwapSince(anchor);
                double zExit = GetZToAvwap(240, anchor);
                if ((Position.MarketPosition == MarketPosition.Long && Close[0] >= avwap) ||
                    (Position.MarketPosition == MarketPosition.Short && Close[0] <= avwap))
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong("MR_VWAP_EXIT", "MR_VWAP_LONG");
                    else
                        ExitShort("MR_VWAP_EXIT", "MR_VWAP_SHORT");
                    Log("MR_VWAP", "ExitTP", activeSignal, Close[0], 0, avwap, "", Position.Quantity, zExit);
                }
                else if (mrVwapEntryTime != Core.Globals.MinDate && Time[0] >= mrVwapEntryTime.AddMinutes(MrVwapTimeoutMin))
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong("MR_VWAP_TIMEOUT", "MR_VWAP_LONG");
                    else
                        ExitShort("MR_VWAP_TIMEOUT", "MR_VWAP_SHORT");
                    Log("MR_VWAP", "Timeout", activeSignal, Close[0], 0, avwap, "", Position.Quantity, zExit);
                }
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            bool trendLong = ema20[0] > ema50[0] && EmaSlope(ema50, 30) > 0;
            bool trendShort = ema20[0] < ema50[0] && EmaSlope(ema50, 30) < 0;
            if (trendLong || trendShort)
                return;

            double z = GetZToAvwap(240, GetAvwapAnchor());
            double stopDist = Math.Max(atr14[0] * MrVwapStopAtr, GetK1TicksForSymbol() * Instrument.MasterInstrument.TickSize);
            if (!CanEnter(stopDist) || adx14[0] >= MrVwapAdxMax)
                return;

            int qty = CalcContracts(stopDist);
            if (qty < 1)
                return;
            double avwapTarget = GetAvwapSince(GetAvwapAnchor());
            double entryPrice;
            double stopPrice;
            int stopTicks = (int)Math.Round(stopDist / Instrument.MasterInstrument.TickSize);
            if (z <= -MrVwapZ)
            {
                entryPrice = Close[0];
                stopPrice = entryPrice - stopDist;
                SetStopLoss("MR_VWAP_LONG", CalculationMode.Ticks, stopTicks, false);
                SetProfitTarget("MR_VWAP_LONG", CalculationMode.Price, avwapTarget);
                entryInitialRiskUsd["MR_VWAP_LONG"] = stopDist * Instrument.MasterInstrument.PointValue * qty;
                EnterLong(qty, "MR_VWAP_LONG");
                Log("MR_VWAP", "Enter", "MR_VWAP_LONG", entryPrice, stopPrice, avwapTarget, "NonTrend", qty, z);
                lastSignalBarTime = Time[0];
            }
            else if (z >= MrVwapZ)
            {
                entryPrice = Close[0];
                stopPrice = entryPrice + stopDist;
                SetStopLoss("MR_VWAP_SHORT", CalculationMode.Ticks, stopTicks, false);
                SetProfitTarget("MR_VWAP_SHORT", CalculationMode.Price, avwapTarget);
                entryInitialRiskUsd["MR_VWAP_SHORT"] = stopDist * Instrument.MasterInstrument.PointValue * qty;
                EnterShort(qty, "MR_VWAP_SHORT");
                Log("MR_VWAP", "Enter", "MR_VWAP_SHORT", entryPrice, stopPrice, avwapTarget, "NonTrend", qty, z);
                lastSignalBarTime = Time[0];
            }
        }

        private void ExecuteAvPre()
        {
            TimeSpan tod = Time[0].TimeOfDay;
            if (Position.MarketPosition == MarketPosition.Long && activeSignal == "AV_PRE_LONG")
            {
                if (tod >= new TimeSpan(9, 25, 0))
                {
                    ExitLong("AV_PRE_EXIT", "AV_PRE_LONG");
                    DateTime anchor = GetAvwapAnchor();
                    double avSlope = GetAvwapSince(anchor, TimeZoneInfo.ConvertTime(Time[0], eastern)) -
                        GetAvwapSince(anchor, TimeZoneInfo.ConvertTime(Time[0], eastern).AddMinutes(-30));
                    double z = GetZToAvwap(120, anchor);
                    Log("AV_PRE", "Timeout", "AV_PRE_LONG", Close[0], 0, 0, "", Position.Quantity, z, avSlope);
                }
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            DateTime et = TimeZoneInfo.ConvertTime(Time[0], eastern);
            DateTime anchor = GetAvwapAnchor();
            double avwap = GetAvwapSince(anchor);
            double avwapSlope = avwap - GetAvwapSince(anchor, et.AddMinutes(-30));
            if (Close[0] <= avwap || avwapSlope <= 0)
                return;
            double stopDist = Math.Max(atr14[0] * AvPreStopAtr, GetK1TicksForSymbol() * Instrument.MasterInstrument.TickSize);
            if (!CanEnter(stopDist))
                return;
            int qty = CalcContracts(stopDist);
            if (qty < 1)
                return;
            double entryPrice = Instrument.MasterInstrument.Round2TickSize(avwap - AvPrePullbackAtr * atr14[0]);
            double stopPrice = entryPrice - stopDist;
            double targetPrice = avwap + AvPreExitAtr * atr14[0];
            int stopTicks = (int)Math.Round(stopDist / Instrument.MasterInstrument.TickSize);
            SetStopLoss("AV_PRE_LONG", CalculationMode.Ticks, stopTicks, false);
            SetProfitTarget("AV_PRE_LONG", CalculationMode.Price, targetPrice);
            SubmitLongLimit(qty, entryPrice, "AV_PRE_LONG");
            pendingEntries["AV_PRE_LONG"] = Time[0];
            entryInitialRiskUsd["AV_PRE_LONG"] = stopDist * Instrument.MasterInstrument.PointValue * qty;
            double z = GetZToAvwap(120, anchor);
            Log("AV_PRE", "Enter", "AV_PRE_LONG", entryPrice, stopPrice, targetPrice, "", qty, z, avwapSlope);
            lastSignalBarTime = Time[0];
        }

        private DateTime GetAvwapAnchor()
        {
            DateTime et = TimeZoneInfo.ConvertTime(Time[0], eastern);
            DateTime target = new DateTime(et.Year, et.Month, et.Day, 18, 0, 0);
            if (et.TimeOfDay < new TimeSpan(18, 0, 0))
                target = target.AddDays(-1);
            DateTime anchor = target;
            for (int i = CurrentBar; i >= 0; i--)
            {
                DateTime barEt = TimeZoneInfo.ConvertTime(Time[i], eastern);
                if (barEt >= target)
                    anchor = barEt;
                else
                    break;
            }
            return anchor;
        }

        private DateTime GetUsOpenAnchor(DateTime et)
        {
            DateTime target = new DateTime(et.Year, et.Month, et.Day, 9, 30, 0);
            if (et.TimeOfDay < new TimeSpan(9, 30, 0))
                target = target.AddDays(-1);
            DateTime anchor = target;
            for (int i = CurrentBar; i >= 0; i--)
            {
                DateTime barEt = TimeZoneInfo.ConvertTime(Time[i], eastern);
                if (barEt >= target)
                    anchor = barEt;
                else
                    break;
            }
            return anchor;
        }

        private void ExecuteOrbUs()
        {
            TimeSpan tod = Time[0].TimeOfDay;
            TimeSpan start = new TimeSpan(9, 30, 0);
            TimeSpan end = new TimeSpan(10, 30, 0);
            if (tod < start || tod >= end)
                return;

            // initialize OR each day
            if (orCalcDate != Time[0].Date && tod >= start)
            {
                double atrOpen = atr14[0];
                atrOpenHistory.Add(atrOpen);
                if (atrOpenHistory.Count > 60) atrOpenHistory.RemoveAt(0);
                double thresh = Percentile(atrOpenHistory, OrbUsSwitchPctl / 100.0);
                orDurationMin = atrOpen >= thresh ? OrbUsOrAlternateMin : OrbUsOrPrimaryMin;
                orEndTime = new DateTime(Time[0].Year, Time[0].Month, Time[0].Day, 9, 30, 0).AddMinutes(orDurationMin);
                orHigh = double.MinValue;
                orLow = double.MaxValue;
                orComplete = false;
                orbEntrySubmitted = false;
                orbFirstTargetHit = false;
                orbEntryTime = Core.Globals.MinDate;
                orbQtySplit.Clear();
                orbFilledQty.Clear();
                orbTierLogged.Clear();
                orbTrailActive.Clear();
                entryQuantity.Clear();
                entryAvgPrice.Clear();
                orCalcDate = Time[0].Date;
            }

            // build OR
            if (!orComplete)
            {
                orHigh = Math.Max(orHigh, High[0]);
                orLow = Math.Min(orLow, Low[0]);
                if (Time[0] >= orEndTime)
                    orComplete = true;
                return;
            }

            // timeout check
            if (orbEntryTime != Core.Globals.MinDate && activeSignal.StartsWith("ORB_US") && !orbFirstTargetHit)
            {
                if (Time[0] >= orbEntryTime.AddMinutes(OrbUsTimeoutMin))
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        ExitLong("ORB_US_LONG");
                        Log("ORB_US", "Timeout", "ORB_US_LONG", Close[0], 0, 0, "", Position.Quantity);
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        ExitShort("ORB_US_SHORT");
                        Log("ORB_US", "Timeout", "ORB_US_SHORT", Close[0], 0, 0, "", Position.Quantity);
                    }
                    ClearOrbOrders(activeSignal);
                    orbEntryTime = Core.Globals.MinDate;
                }
            }

            if (orbEntrySubmitted || Position.MarketPosition != MarketPosition.Flat)
                return;

            double tick = Instrument.MasterInstrument.TickSize;
            double range = orHigh - orLow;

            // breakout long
            if (Close[0] > orHigh)
            {
                double entryPrice = Instrument.MasterInstrument.Round2TickSize(orHigh + tick);
                double stopPrice  = Instrument.MasterInstrument.Round2TickSize(orLow  - tick);
                double stopDist   = Math.Max(entryPrice - stopPrice, GetK1TicksForSymbol() * Instrument.MasterInstrument.TickSize);
                if (!CanEnter(stopDist)) return;

                int qty = CalcContracts(stopDist);
                if (qty < 1) return;

                int q1 = (int)Math.Floor(qty * OrbUsTgtW1);
                int q2 = (int)Math.Floor(qty * OrbUsTgtW2);
                int q3 = qty - q1 - q2;
                if (qty < 3) { q1 = Math.Max(1, qty - 1); q2 = 0; q3 = 1; }
                if (q1 < 1) q1 = 1; if (q2 < 1 && qty >= 3) q2 = 1; q3 = Math.Max(0, qty - q1 - q2);

                double range = Math.Max(Instrument.MasterInstrument.TickSize, orHigh - orLow);
                double t1 = Instrument.MasterInstrument.Round2TickSize(entryPrice + range * OrbUsTp1Mult);
                double t2 = Instrument.MasterInstrument.Round2TickSize(entryPrice + range * OrbUsTp2Mult);
                double t3 = Instrument.MasterInstrument.Round2TickSize(entryPrice + range * OrbUsTp3Mult);

                SetStopLoss("ORB_US_LONG", CalculationMode.Price, stopPrice, false);
                EnterLongStopMarket(qty, entryPrice, "ORB_US_LONG");
                pendingEntries["ORB_US_LONG"] = Time[0];
                orbEntrySubmitted = true;
                orbFirstTargetHit = false;
                entryInitialRiskUsd["ORB_US_LONG"] = stopDist * Instrument.MasterInstrument.PointValue * qty;

                orbQtySplit["ORB_US_LONG"] = new int[] { q1, q2, q3 };
                entryQuantity["ORB_US_LONG"] = qty;
                entryAvgPrice.Remove("ORB_US_LONG");
                orbFilledQty["ORB_US_LONG"] = 0;
                orbTierLogged["ORB_US_LONG"] = 0;
                orbTrailActive.Remove("ORB_US_LONG");

                Log("ORB_US", "Enter", "ORB_US_LONG", entryPrice, stopPrice, t1, "", qty);
                lastSignalBarTime = Time[0];
            }
            // breakout short
            else if (Close[0] < orLow)
            {
                double entryPrice = Instrument.MasterInstrument.Round2TickSize(orLow - tick);
                double stopPrice  = Instrument.MasterInstrument.Round2TickSize(orHigh + tick);
                double stopDist   = Math.Max(stopPrice - entryPrice, GetK1TicksForSymbol() * Instrument.MasterInstrument.TickSize);
                if (!CanEnter(stopDist)) return;

                int qty = CalcContracts(stopDist);
                if (qty < 1) return;

                int q1 = (int)Math.Floor(qty * OrbUsTgtW1);
                int q2 = (int)Math.Floor(qty * OrbUsTgtW2);
                int q3 = qty - q1 - q2;
                if (qty < 3) { q1 = Math.Max(1, qty - 1); q2 = 0; q3 = 1; }
                if (q1 < 1) q1 = 1; if (q2 < 1 && qty >= 3) q2 = 1; q3 = Math.Max(0, qty - q1 - q2);

                double range = Math.Max(Instrument.MasterInstrument.TickSize, orHigh - orLow);
                double t1 = Instrument.MasterInstrument.Round2TickSize(entryPrice - range * OrbUsTp1Mult);
                double t2 = Instrument.MasterInstrument.Round2TickSize(entryPrice - range * OrbUsTp2Mult);
                double t3 = Instrument.MasterInstrument.Round2TickSize(entryPrice - range * OrbUsTp3Mult);

                SetStopLoss("ORB_US_SHORT", CalculationMode.Price, stopPrice, false);
                EnterShortStopMarket(qty, entryPrice, "ORB_US_SHORT");
                pendingEntries["ORB_US_SHORT"] = Time[0];
                orbEntrySubmitted = true;
                orbFirstTargetHit = false;
                entryInitialRiskUsd["ORB_US_SHORT"] = stopDist * Instrument.MasterInstrument.PointValue * qty;

                orbQtySplit["ORB_US_SHORT"] = new int[] { q1, q2, q3 };
                entryQuantity["ORB_US_SHORT"] = qty;
                entryAvgPrice.Remove("ORB_US_SHORT");
                orbFilledQty["ORB_US_SHORT"] = 0;
                orbTierLogged["ORB_US_SHORT"] = 0;
                orbTrailActive.Remove("ORB_US_SHORT");

                Log("ORB_US", "Enter", "ORB_US_SHORT", entryPrice, stopPrice, t1, "", qty);
                lastSignalBarTime = Time[0];
            }
        }

        private void ExecuteMrMid()
        {
            DateTime et = TimeZoneInfo.ConvertTime(Time[0], eastern);
            if (activeSignal == "MR_MID_LONG" && Position.MarketPosition == MarketPosition.Long)
            {
                DateTime anchor = GetUsOpenAnchor(et);
                double avwap = GetAvwapSince(anchor);
                double z = GetZToAvwap(240, anchor);
                if (Close[0] >= avwap)
                {
                    ExitLong("MR_MID_EXIT", "MR_MID_LONG");
                    Log("MR_MID", "ExitTP", "MR_MID_LONG", Close[0], 0, avwap, "NonTrend", Position.Quantity, z);
                }
                else if (mrMidEntryTime != Core.Globals.MinDate && Time[0] >= mrMidEntryTime.AddMinutes(MrMidTimeoutMin))
                {
                    ExitLong("MR_MID_TIMEOUT", "MR_MID_LONG");
                    Log("MR_MID", "Timeout", "MR_MID_LONG", Close[0], 0, avwap, "NonTrend", Position.Quantity, z);
                }
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            double stopDist = Math.Max(atr14[0] * MrMidStopAtr, GetK1TicksForSymbol() * Instrument.MasterInstrument.TickSize);
            if (!CanEnter(stopDist) || adx14[0] >= MrMidAdxMax)
                return;

            DateTime anchorUs = GetUsOpenAnchor(et);
            double zScore = GetZToAvwap(240, anchorUs);
            if (zScore <= -MrMidZ)
            {
                int qty = CalcContracts(stopDist);
                if (qty < 1)
                    return;
                double entryPrice = Close[0];
                double stopPrice = entryPrice - stopDist;
                double targetPrice = GetAvwapSince(anchorUs);
                int stopTicks = (int)Math.Round(stopDist / Instrument.MasterInstrument.TickSize);
                SetStopLoss("MR_MID_LONG", CalculationMode.Ticks, stopTicks, false);
                SetProfitTarget("MR_MID_LONG", CalculationMode.Price, targetPrice);
                entryInitialRiskUsd["MR_MID_LONG"] = stopDist * Instrument.MasterInstrument.PointValue * qty;
                EnterLong(qty, "MR_MID_LONG");
                Log("MR_MID", "Enter", "MR_MID_LONG", entryPrice, stopPrice, targetPrice, "NonTrend", qty, zScore);
                lastSignalBarTime = Time[0];
            }
        }

        private void EnsureAtr10()
        {
            if (atr10 == null)
                atr10 = ATR(10);
        }

        private void ExecuteTcPh()
        {
            EnsureAtr10();

            // No new entries during no-trade block or if in position
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            double k1 = GetK1TicksForSymbol() * Instrument.MasterInstrument.TickSize;
            double stopDist = Math.Max(atr14[0] * 1.5, k1);
            if (!CanEnter(stopDist))
                return;

            // Trend continuation long
            if (ema20[0] > ema50[0] && EmaSlope(ema50, TcPhSlopeLookback) > 0)
            {
                int qty = CalcContracts(stopDist);
                if (qty < 1)
                    return;

                double entryPrice = Instrument.MasterInstrument.Round2TickSize(High[HighestBar(High, 10)] + Instrument.MasterInstrument.TickSize);
                int stopTicks = (int)Math.Round(stopDist / Instrument.MasterInstrument.TickSize);
                SetStopLoss("TC_PH_LONG", CalculationMode.Ticks, stopTicks, false);

                // Trail = ATR10 × 1.0
                int trailTicks = (int)Math.Max(1, Math.Round(atr10[0] / Instrument.MasterInstrument.TickSize));
                SetTrailStop("TC_PH_LONG", CalculationMode.Ticks, trailTicks, false);

                entryInitialRiskUsd["TC_PH_LONG"] = stopDist * Instrument.MasterInstrument.PointValue * qty;
                SubmitLongStopMarket(qty, entryPrice, "TC_PH_LONG");
                Log("TC_PH", "Enter", "TC_PH_LONG", entryPrice, entryPrice - stopDist, 0, "", qty);
                lastSignalBarTime = Time[0];
            }
        }
        #endregion

        #region Exec/OrderRouter
        private readonly Dictionary<string, DateTime> pendingEntries = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, Order> workingEntryOrder = new Dictionary<string, Order>();
        private readonly Dictionary<string, Order> pt1 = new Dictionary<string, Order>();
        private readonly Dictionary<string, Order> pt2 = new Dictionary<string, Order>();
        private readonly Dictionary<string, Order> pt3 = new Dictionary<string, Order>();
        private readonly Dictionary<string, double> entryInitialRiskUsd = new Dictionary<string, double>();
        private readonly Dictionary<string, double> mfeUsd = new Dictionary<string, double>();
        private readonly Dictionary<string, double> maeUsd = new Dictionary<string, double>();
        private readonly Dictionary<string, double> realizedPerSignal = new Dictionary<string, double>();
        private readonly HashSet<string> beMoved = new HashSet<string>();
        private readonly Dictionary<string, int> entryQuantity = new Dictionary<string, int>();
        private readonly Dictionary<string, double> entryAvgPrice = new Dictionary<string, double>();
        private readonly Dictionary<string, int> openQty = new Dictionary<string, int>();
        private readonly Dictionary<string, int> entryDirection = new Dictionary<string, int>();
        private readonly Dictionary<string, int[]> orbQtySplit = new Dictionary<string, int[]>();
        private readonly Dictionary<string, int> orbFilledQty = new Dictionary<string, int>();
        private readonly Dictionary<string, int> orbTierLogged = new Dictionary<string, int>();
        private readonly HashSet<string> orbTrailActive = new HashSet<string>();
        private string activeSignal = string.Empty;

        private void ProcessPendingEntries()
        {
            var keys = new List<string>(pendingEntries.Keys);
            foreach (var signal in keys)
            {
                Order o;
                if (!workingEntryOrder.TryGetValue(signal, out o) || o == null ||
                    (o.OrderState != OrderState.Working && o.OrderState != OrderState.Accepted))
                {
                    pendingEntries.Remove(signal);
                    continue;
                }

                TimeSpan elapsed = Time[0] - pendingEntries[signal];
                TimeSpan persist = TimeSpan.FromSeconds(Math.Min(QueuePersistSec, MitSec));

                if (elapsed >= TimeSpan.FromSeconds(QueueMaxSec))
                {
                    CancelOrder(o);
                    pendingEntries.Remove(signal);
                    Log("CORE", "QueueCancel", signal);
                }
                else if (elapsed >= persist)
                {
                    int qty = Math.Max(1, o.Quantity - o.Filled);
                    CancelOrder(o);
                    double price = (signal.Contains("_LONG") ? GetCurrentAsk() : GetCurrentBid());
                    price = signal.Contains("_LONG")
                        ? Instrument.MasterInstrument.Round2TickSize(price + Instrument.MasterInstrument.TickSize)
                        : Instrument.MasterInstrument.Round2TickSize(price - Instrument.MasterInstrument.TickSize);

                    if (signal.Contains("_LONG"))
                        EnterLongStopMarket(qty, price, signal);
                    else
                        EnterShortStopMarket(qty, price, signal);

                    pendingEntries.Remove(signal);
                    Log("CORE", "QueueMIT", signal, price);
                }
            }
        }

        private void CheckBreakEven()
        {
            foreach (var kv in openQty)
            {
                string sig = kv.Key;
                int qty = kv.Value;
                if (qty <= 0) continue;
                double riskUsd;
                if (!entryInitialRiskUsd.TryGetValue(sig, out riskUsd) || riskUsd <= 0) continue;
                int dir = entryDirection.ContainsKey(sig) ? entryDirection[sig] : 1;
                double entryPrice = GetPositionAveragePrice(sig);
                double pnl = (Close[0] - entryPrice) * dir * Instrument.MasterInstrument.PointValue * qty;
                double mfePrev;
                if (!mfeUsd.TryGetValue(sig, out mfePrev) || pnl > mfePrev)
                    mfeUsd[sig] = pnl;
                if (!maeUsd.ContainsKey(sig) || pnl < maeUsd[sig])
                    maeUsd[sig] = pnl;
                if (beMoved.Contains(sig))
                    continue;
                if (mfeUsd[sig] >= MoveStopToBeAtMfeR * riskUsd)
                {
                    double offset = BeOffsetAtFirstTgtR * atr14[0];
                    double stopPrice = dir == 1 ? entryPrice + offset : entryPrice - offset;
                    SetStopLoss(sig, CalculationMode.Price, stopPrice, false);
                    beMoved.Add(sig);
                    Log("CORE", "MoveStopBE", sig, entryPrice, stopPrice, 0, "", qty, mfeUsd[sig], 0);
                }
            }
        }

        private double GetPositionAveragePrice(string signal)
        {
            double price;
            if (entryAvgPrice.TryGetValue(signal, out price))
                return price;
            double total = 0;
            int qty = 0;
            foreach (Execution exec in Executions)
            {
                if (exec.Order == null) continue;
                string sig = exec.Order.FromEntrySignal;
                if (string.IsNullOrEmpty(sig))
                    sig = exec.Order.Name;
                if (sig != signal) continue;
                OrderAction act = exec.Order.OrderAction;
                if (act == OrderAction.Buy || act == OrderAction.BuyToCover || act == OrderAction.SellShort)
                {
                    total += exec.Price * Math.Abs(exec.Quantity);
                    qty += Math.Abs(exec.Quantity);
                }
            }
            return qty > 0 ? total / qty : 0;
        }

        private void ActivateResidualTrail(string signal)
        {
            double trailDistance = 0.7 * atr14[0];
            double currentPrice = Close[0];
            double entryPrice = GetPositionAveragePrice(signal);
            double stopPrice = signal == "ORB_US_LONG" ? currentPrice - trailDistance : currentPrice + trailDistance;
            SetTrailStop(signal, CalculationMode.Price, stopPrice, false);
            orbTrailActive.Add(signal);
            Log("ORB_US", "TrailStart", signal, entryPrice, stopPrice, 0, "", Position.Quantity);
        }

        private void ClearOrbOrders(string signal)
        {
            SetStopLoss(signal, CalculationMode.Price, 0, false);
            SetTrailStop(signal, CalculationMode.Price, 0, false);
            if (pt1.ContainsKey(signal)) { CancelOrder(pt1[signal]); pt1.Remove(signal); }
            if (pt2.ContainsKey(signal)) { CancelOrder(pt2[signal]); pt2.Remove(signal); }
            if (pt3.ContainsKey(signal)) { CancelOrder(pt3[signal]); pt3.Remove(signal); }
            if (workingEntryOrder.ContainsKey(signal)) workingEntryOrder.Remove(signal);
            entryQuantity.Remove(signal);
            entryAvgPrice.Remove(signal);
            orbQtySplit.Remove(signal);
            orbFilledQty.Remove(signal);
            orbTierLogged.Remove(signal);
            orbTrailActive.Remove(signal);
            orbFirstTargetHit = false;
        }
        #endregion

        #region Execution Helpers
        private void SubmitLongLimit(int quantity, double price, string signal)
        {
            EnterLongLimit(quantity, price, signal);
        }

        private void SubmitLongStopMarket(int quantity, double price, string signal)
        {
            EnterLongStopMarket(quantity, price, signal);
        }

        private double Percentile(List<double> data, double p)
        {
            if (data == null || data.Count == 0) return 0;
            double[] arr = data.ToArray();
            Array.Sort(arr);
            double rank = p * (arr.Length - 1);
            int l = (int)Math.Floor(rank);
            int u = (int)Math.Ceiling(rank);
            if (l == u) return arr[l];
            return arr[l] + (arr[u] - arr[l]) * (rank - l);
        }

        private double EmaSlope(EMA ema, int lookback)
        {
            if (CurrentBar < lookback)
                return 0;
            return ema[0] - ema[lookback];
        }

        private void Flatten(string reason, string module = "CORE")
        {
            string sig = activeSignal;
            int qty = Position.Quantity;
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(reason);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(reason);
            CancelAllOrders();
            pendingEntries.Clear();
            entryInitialRiskUsd.Clear();
            mfeUsd.Clear();
            maeUsd.Clear();
            realizedPerSignal.Clear();
            beMoved.Clear();
            entryAvgPrice.Clear();
            entryDirection.Clear();
            openQty.Clear();
            orbFilledQty.Clear();
            orbTierLogged.Clear();
            orbTrailActive.Clear();
            orbQtySplit.Clear();
            activeSignal = string.Empty;
            Log(module, reason, sig, 0, 0, 0, "", qty);
        }

        private void Log(string module, string reason, string signal = "", double entryPx = 0, double stopPx = 0,
            double targetPx = 0, string fillType = "", int size = 0, double zVwap = 0, double avwapSlope = 0)
        {
            if (logWriter == null) return;

            if (rateLimitedReasons.Contains(reason))
            {
                DateTime last;
                if (lastLogByReason.TryGetValue(reason, out last) && Time[0] - last < TimeSpan.FromMinutes(1))
                    return;
                lastLogByReason[reason] = Time[0];
            }

            DateTime utc = Time[0].ToUniversalTime();
            DateTime et = TimeZoneInfo.ConvertTime(Time[0], eastern);
            int spread = GetSpreadTicks();
            double atrVal = atr14 != null ? atr14[0] : 0;
            double ema20Val = ema20 != null ? ema20[0] : 0;
            double ema50Val = ema50 != null ? ema50[0] : 0;
            double adxVal = adx14 != null ? adx14[0] : 0;
            double edgeScore = 0;
            if (AutoSelectBestSessionStrategy)
            {
                SessionId sid = GetSession(et.TimeOfDay);
                ModuleType mt;
                if (Enum.TryParse(module, out mt))
                {
                    Dictionary<ModuleType, EdgeStats> dict;
                    if (edgeStats.TryGetValue(sid, out dict))
                    {
                        EdgeStats st;
                        if (dict.TryGetValue(mt, out st))
                            edgeScore = st.expectancy * st.sharpe;
                    }
                }
            }

            logWriter.WriteLine(string.Format("{0:o},{1:o},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25},{26},{27},{28},{29}",
                utc,
                et,
                Account != null ? Account.Name : "",
                Instrument != null ? Instrument.FullName : "",
                GetSession(et.TimeOfDay),
                module,
                AutoSelectBestSessionStrategy,
                signal,
                entryPx,
                stopPx,
                targetPx,
                atrVal,
                zVwap,
                avwapSlope,
                ema20Val,
                ema50Val,
                adxVal,
                spread,
                size,
                fillType,
                reason,
                tradeLock ? 1 : 0,
                highWaterMark,
                maxBalance,
                dailyRealized,
                unrealizedNow,
                dailyRealized,
                dayTrades,
                consecutiveLosses,
                edgeScore));
        }
        #endregion

        #region Execution Events
        /// <summary>Track trade results for risk engine.</summary>
        protected override void OnExecutionUpdate(
            Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            if (execution.Order.OrderState != OrderState.Filled)
                return;

            if (rejectLatched)
                rejectLatched = false;

            string orderName = execution.Order.Name;
            string signal = execution.Order.FromEntrySignal;
            if (string.IsNullOrEmpty(signal))
                signal = orderName;

            if (pendingEntries.ContainsKey(orderName))
                pendingEntries.Remove(orderName);

            OrderAction action = execution.Order.OrderAction;
            int qty = Math.Abs(quantity);
            double execPrice = price;

            if (!entryDirection.ContainsKey(signal))
            {
                entryDirection[signal] = (action == OrderAction.Buy || action == OrderAction.BuyToCover) ? 1 : -1;
                openQty[signal] = qty;
                entryAvgPrice[signal] = execPrice;
                mfeUsd[signal] = 0;
                maeUsd[signal] = 0;
                beMoved.Remove(signal);
                activeSignal = signal;
            }
            else
            {
                int dir = entryDirection[signal];
                bool isExit = (dir == 1 && (action == OrderAction.Sell || action == OrderAction.SellShort)) ||
                              (dir == -1 && (action == OrderAction.Buy || action == OrderAction.BuyToCover));
                if (!isExit)
                {
                    int prev = openQty[signal];
                    openQty[signal] = prev + qty;
                    entryAvgPrice[signal] = (entryAvgPrice[signal] * prev + execPrice * qty) / openQty[signal];
                }
                else
                {
                    openQty[signal] = Math.Max(0, openQty[signal] - qty);
                    double pnl = execution.ProfitCurrency;
                    if (!realizedPerSignal.ContainsKey(signal))
                        realizedPerSignal[signal] = 0;
                    realizedPerSignal[signal] += pnl;
                    dailyRealized += pnl;
                    realizedCumulative += pnl;
                    if (pnl < 0) consecutiveLosses++;
                    else consecutiveLosses = 0;

                    bool isOrb = signal == "ORB_US_LONG" || signal == "ORB_US_SHORT";
                    if (isOrb && orderName.Contains("Profit"))
                    {
                        orbFilledQty[signal] += Math.Abs(quantity);
                        int[] split = orbQtySplit[signal];
                        int logged = orbTierLogged[signal];
                        int filled = orbFilledQty[signal];
                        if (filled >= split[0] && logged < 1)
                        {
                            orbTierLogged[signal] = 1;
                            orbFirstTargetHit = true;
                            Log("ORB_US", "ExitTP_T1", signal, price, 0, 0, "", quantity);
                            if (!orbTrailActive.Contains(signal))
                                ActivateResidualTrail(signal);
                        }
                        else if (filled >= split[0] + split[1] && logged < 2)
                        {
                            orbTierLogged[signal] = 2;
                            Log("ORB_US", "ExitTP_T2", signal, price, 0, 0, "", quantity);
                        }
                        else if (filled >= split[0] + split[1] + split[2] && logged < 3)
                        {
                            orbTierLogged[signal] = 3;
                            Log("ORB_US", "ExitTP_T3", signal, price, 0, 0, "", quantity);
                            orbEntryTime = Core.Globals.MinDate;
                            ClearOrbOrders(signal);
                        }
                    }
                    else if (isOrb && orderName.Contains("Stop"))
                    {
                        Log("ORB_US", "ExitSL", signal, price, 0, 0, "", quantity);
                        orbEntryTime = Core.Globals.MinDate;
                        ClearOrbOrders(signal);
                    }
                    else
                    {
                        string rc = pnl >= 0 ? "ExitTP" : "ExitSL";
                        if (!signal.StartsWith("ORB_US"))
                            Log("CORE", rc, signal, price, 0, 0, "", quantity);
                    }

                    if (openQty[signal] <= 0)
                    {
                        dayTrades++;
                        double riskUsd = entryInitialRiskUsd.ContainsKey(signal) ? entryInitialRiskUsd[signal] : 0;
                        double mfe = mfeUsd.ContainsKey(signal) ? mfeUsd[signal] : 0;
                        double mae = maeUsd.ContainsKey(signal) ? maeUsd[signal] : 0;
                        double tradePnl = realizedPerSignal.ContainsKey(signal) ? realizedPerSignal[signal] : pnl;
                        RecordTradeStats(signal, tradePnl, riskUsd, mfe, mae);
                        entryInitialRiskUsd.Remove(signal);
                        mfeUsd.Remove(signal);
                        maeUsd.Remove(signal);
                        beMoved.Remove(signal);
                        entryAvgPrice.Remove(signal);
                        entryDirection.Remove(signal);
                        openQty.Remove(signal);
                        realizedPerSignal.Remove(signal);
                        orbFilledQty.Remove(signal);
                        orbTierLogged.Remove(signal);
                        orbTrailActive.Remove(signal);
                        orbQtySplit.Remove(signal);
                        activeSignal = string.Empty;
                        orbEntryTime = Core.Globals.MinDate;
                        if (signal.StartsWith("BO_LONDON"))
                            boEntryTime = Core.Globals.MinDate;
                    }
                }
            }

            if (signal.StartsWith("MR_ASIA") && (action == OrderAction.Buy || action == OrderAction.Sell))
                mrAsiaEntryTime = time;
            if (signal.StartsWith("TP_EU") && (action == OrderAction.Buy || action == OrderAction.Sell))
                tpEuEntryTime = time;
            if (signal.StartsWith("MR_VWAP") && (action == OrderAction.Buy || action == OrderAction.Sell))
                mrVwapEntryTime = time;
            if (signal.StartsWith("MR_MID") && action == OrderAction.Buy)
                mrMidEntryTime = time;
            if (signal.StartsWith("BO_LONDON") && (action == OrderAction.Buy || action == OrderAction.SellShort))
                boEntryTime = time;
            if (signal == "ORB_US_LONG" && action == OrderAction.Buy)
                orbEntryTime = time;
            if (signal == "ORB_US_SHORT" && action == OrderAction.SellShort)
                orbEntryTime = time;
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity,
            int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            // Track working entry orders by FromEntrySignal
            if (!string.IsNullOrEmpty(order.FromEntrySignal))
            {
                if (order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.SellShort)
                {
                    workingEntryOrder[order.FromEntrySignal] = order;
                }

                if ((order.OrderState == OrderState.PartFilled || order.OrderState == OrderState.Filled)
                    && (order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.SellShort))
                {
                    string sig = order.FromEntrySignal;
                    if (sig == "ORB_US_LONG" || sig == "ORB_US_SHORT")
                    {
                        int[] split;
                        if (orbQtySplit.TryGetValue(sig, out split))
                        {
                            double ep = entryAvgPrice.ContainsKey(sig) ? entryAvgPrice[sig] : averageFillPrice;
                            double range = Math.Max(Instrument.MasterInstrument.TickSize, orHigh - orLow);

                            if (sig == "ORB_US_LONG")
                            {
                                double t1 = Instrument.MasterInstrument.Round2TickSize(ep + range * OrbUsTp1Mult);
                                double t2 = Instrument.MasterInstrument.Round2TickSize(ep + range * OrbUsTp2Mult);
                                double t3 = Instrument.MasterInstrument.Round2TickSize(ep + range * OrbUsTp3Mult);

                                if (split[0] > 0) pt1[sig] = ExitLongLimit(split[0], true, t1, "PT1_" + sig, sig);
                                if (split[1] > 0) pt2[sig] = ExitLongLimit(split[1], true, t2, "PT2_" + sig, sig);
                                if (split[2] > 0) pt3[sig] = ExitLongLimit(split[2], true, t3, "PT3_" + sig, sig);
                            }
                            else
                            {
                                double t1 = Instrument.MasterInstrument.Round2TickSize(ep - range * OrbUsTp1Mult);
                                double t2 = Instrument.MasterInstrument.Round2TickSize(ep - range * OrbUsTp2Mult);
                                double t3 = Instrument.MasterInstrument.Round2TickSize(ep - range * OrbUsTp3Mult);

                                if (split[0] > 0) pt1[sig] = ExitShortLimit(split[0], true, t1, "PT1_" + sig, sig);
                                if (split[1] > 0) pt2[sig] = ExitShortLimit(split[1], true, t2, "PT2_" + sig, sig);
                                if (split[2] > 0) pt3[sig] = ExitShortLimit(split[2], true, t3, "PT3_" + sig, sig);
                            }
                        }
                    }
                }
            }

            if (orderState == OrderState.Rejected && !rejectLatched)
            {
                Flatten("OrderReject");
                tradeLock = true;
                rejectLatched = true;
            }

            if (orderState == OrderState.Canceled && pendingEntries.ContainsKey(order.Name))
                pendingEntries.Remove(order.Name);
        }
        #endregion

        #region Health/Monitor
        private void CheckHealth()
        {
            DateTime now = DateTime.UtcNow;
            if (lastDataTime != Core.Globals.MinDate)
            {
                TimeSpan gap = now - lastDataTime;
                if (!isStaleLatched && gap.TotalSeconds > 3)
                {
                    Flatten("DataStale");
                    tradeLock = true;
                    isStaleLatched = true;
                    staleSince = now;
                }
                else if (isStaleLatched && gap.TotalSeconds <= 1)
                {
                    isStaleLatched = false;
                    staleSince = Core.Globals.MinDate;
                }
            }
            lastDataTime = now;

            if (!isConnected && !disconnectLatched && disconnectStart != Core.Globals.MinDate &&
                (now - disconnectStart).TotalSeconds > 5)
            {
                Flatten("Disconnect");
                tradeLock = true;
                disconnectLatched = true;
            }

            if (disconnectLatched && isConnected && reconnectTime != Core.Globals.MinDate &&
                (now - reconnectTime).TotalSeconds > 2)
            {
                disconnectLatched = false;
            }
        }

        protected override void OnConnectionStatusUpdate(ConnectionStatus status, Connection connection)
        {
            isConnected = status == ConnectionStatus.Connected;
            if (isConnected)
            {
                reconnectTime = DateTime.UtcNow;
                disconnectStart = Core.Globals.MinDate;
            }
            else
            {
                if (disconnectStart == Core.Globals.MinDate)
                    disconnectStart = DateTime.UtcNow;
            }
        }
        #endregion
    }
}

