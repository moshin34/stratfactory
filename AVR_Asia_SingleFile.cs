// STRATEGY TEMPLATE (v1.3) — PER-ACCOUNT ONLY HWM (immutable), Cutoff/AutoFlat, Failsafes
// CREATED: 2025-08-13
// TARGET: NinjaTrader 8 (.NET 4.8, C# 7.3). One public class per file.
// NAMESPACE: Standalone.Strategies ; CLASS NAME must equal FILE NAME.
//
// ********* CRITICAL, IMMUTABLE RULE *********
// High-Water Mark (HWM) & BREACH markers are keyed **PER-ACCOUNT ONLY**.
// DO NOT include instrument or strategy name in the persistence key.
// This is a hard requirement and part of the IMMUTABLE plumbing.
// ********************************************
//
// Strategy implementation generated from template. Only edit inside the ENTRY region.

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace Standalone.Strategies
{
    public class AVR_Asia_SingleFile : Strategy
    {
        #region Required Risk Toggles
        [NinjaScriptProperty, Display(Name="UseRiskManager", GroupName="Risk", Order=1)]
        public bool UseRiskManager { get; set; } = true;

        [NinjaScriptProperty, Display(Name="MaxDailyLoss ($)", GroupName="Risk", Order=2)]
        public double MaxDailyLoss { get; set; } = 700;

        [NinjaScriptProperty, Display(Name="CircuitBreakerDrawdown ($)", GroupName="Risk", Order=3)]
        public double CircuitBreakerDrawdown { get; set; } = 2000;

        [NinjaScriptProperty, Display(Name="PropTrailingDD ($)", GroupName="Risk", Order=4, Description="Trailing drawdown vs persistent equity high-water mark")]
        public double PropTrailingDD { get; set; } = 2500;

        [NinjaScriptProperty, Display(Name="Lockout (min)", GroupName="Risk", Order=5)]
        public int LockoutMinutes { get; set; } = 120;
        #endregion

//== BEGIN IMMUTABLE: DIAGNOSTICS ==
#region Diagnostics
private string _stratName;
private string _symbol;
private string _accountName;
private string _tf;
private string _runId;
private System.IO.StreamWriter _telemetry;
private long _telemetryBytes;

[NinjaScriptProperty]
[Display(Name="VerboseLogging", GroupName="Diagnostics", Order=900)]
public bool VerboseLogging { get; set; } = true;

[NinjaScriptProperty]
[Display(Name="WhyNoTrade", GroupName="Diagnostics", Order=901)]
public bool WhyNoTrade { get; set; } = true;

[NinjaScriptProperty]
[Display(Name="EnableJsonTelemetry", GroupName="Diagnostics", Order=902)]
public bool EnableJsonTelemetry { get; set; } = false;

[NinjaScriptProperty]
[Display(Name="TelemetryFilePath", GroupName="Diagnostics", Order=903)]
public string TelemetryFilePath { get; set; } =
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
    + System.IO.Path.DirectorySeparatorChar + "NinjaTrader 8"
    + System.IO.Path.DirectorySeparatorChar + "log"
    + System.IO.Path.DirectorySeparatorChar + "StrategyLogs";

[NinjaScriptProperty]
[Display(Name="TelemetryMaxKB", GroupName="Diagnostics", Order=904)]
[Range(64, 10240)]
public int TelemetryMaxKB { get; set; } = 1024;

[NinjaScriptProperty]
[Display(Name="StrategyTag", GroupName="Diagnostics", Order=905)]
public string StrategyTag { get; set; } = "";

[NinjaScriptProperty]
[Display(Name="ExternalRefUrl", GroupName="Diagnostics", Order=906)]
public string ExternalRefUrl { get; set; } = "";

private void Dx_Init()
{
    _runId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
    _stratName = Name;
    _symbol = Instrument != null ? Instrument.FullName : "<unknown>";
    try { _accountName = Account != null ? Account.Name : "<none>"; } catch { _accountName = "<none>"; }
    _tf = BarsPeriod.BarsPeriodType + ":" + BarsPeriod.Value;

    if (EnableJsonTelemetry)
    {
        try
        {
            if (!System.IO.Directory.Exists(TelemetryFilePath))
                System.IO.Directory.CreateDirectory(TelemetryFilePath);

            var file = System.IO.Path.Combine(TelemetryFilePath, _stratName + ".jsonl");
            _telemetry = new System.IO.StreamWriter(file, append: true) { AutoFlush = true };
            _telemetryBytes = new System.IO.FileInfo(file).Exists ? new System.IO.FileInfo(file).Length : 0;
        }
        catch (Exception ex)
        {
            Print($"[CFG] [{_stratName}] telemetry=disabled error={ex.Message}");
            EnableJsonTelemetry = false;
        }
    }
    Dx_Cfg();
}

private void Dx_Close()
{
    try { _telemetry?.Dispose(); } catch { }
}

private void Dx_Log(string tag, string kv)
{
    if (tag == "FILTER" && !WhyNoTrade) return;
    if (!VerboseLogging && (tag == "CFG" || tag == "STATE" || tag == "FILTER" || tag == "ORD"))
        return;
    Print($"[{tag}] [{_stratName}] {kv}");
    Dx_Json(tag, kv);
}

private void Dx_Why(string code, string detail)
{
    if (!WhyNoTrade) return;
    Dx_Log("FILTER", $"reason={code} detail={detail}");
}

private void Dx_Cfg()
{
    var p = new System.Text.StringBuilder();
    p.Append("{\"ts\":\"").Append(Times[0][0].ToUniversalTime().ToString("o"))
     .Append("\",\"event\":\"CFG\",\"runId\":\"").Append(JsonEsc(_runId))
     .Append("\",\"strat\":\"").Append(JsonEsc(_stratName))
     .Append("\",\"symbol\":\"").Append(JsonEsc(_symbol))
     .Append("\",\"tf\":\"").Append(JsonEsc(_tf))
     .Append("\",\"account\":\"").Append(JsonEsc(_accountName))
     .Append("\",\"tag\":\"").Append(JsonEsc(StrategyTag))
     .Append("\",\"ref\":\"").Append(JsonEsc(ExternalRefUrl))
     .Append("\",\"params\":{")
     .Append("\"UseRiskManager\":").Append(UseRiskManager.ToString().ToLower()).Append(",")
     .Append("\"MaxDailyLoss\":").Append(MaxDailyLoss.ToString("F2")).Append(",")
     .Append("\"CircuitBreakerDrawdown\":").Append(CircuitBreakerDrawdown.ToString("F2")).Append(",")
     .Append("\"PropTrailingDD\":").Append(PropTrailingDD.ToString("F2")).Append(",")
     .Append("\"LockoutMinutes\":").Append(LockoutMinutes)
     .Append("}}");
    Dx_WriteLine(p.ToString());
}

private void Dx_Json(string tag, string kv)
{
    if (!EnableJsonTelemetry) return;
    var parts = kv.Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries);
    var sb = new System.Text.StringBuilder();
    sb.Append("{\"ts\":\"").Append(Times[0][0].ToUniversalTime().ToString("o"))
      .Append("\",\"event\":\"").Append(JsonEsc(tag))
      .Append("\",\"runId\":\"").Append(JsonEsc(_runId))
      .Append("\",\"strat\":\"").Append(JsonEsc(_stratName))
      .Append("\",\"symbol\":\"").Append(JsonEsc(_symbol))
      .Append("\",\"tf\":\"").Append(JsonEsc(_tf))
      .Append("\",\"account\":\"").Append(JsonEsc(_accountName))
      .Append("\",\"ctx\":{");
    int written = 0;
    for (int i=0; i<parts.Length; i++)
    {
        var p = parts[i];
        var eq = p.IndexOf('=');
        if (eq <= 0) continue;
        var k = p.Substring(0, eq);
        var v = p.Substring(eq+1);
        if (written++>0) sb.Append(",");
        sb.Append("\"").Append(JsonEsc(k)).Append("\":\"").Append(JsonEsc(v)).Append("\"");
    }
    sb.Append("}}");
    Dx_WriteLine(sb.ToString());
}

private void Dx_WriteLine(string line)
{
    try
    {
        if (_telemetry == null) return;
        _telemetry.WriteLine(line);
        _telemetryBytes += line.Length + 1;
        var maxBytes = (long)TelemetryMaxKB * 1024L;
        if (_telemetryBytes > maxBytes)
        {
            _telemetry.Dispose();
            var path = System.IO.Path.Combine(TelemetryFilePath, _stratName + ".jsonl");
            var rotated = System.IO.Path.Combine(TelemetryFilePath, _stratName + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jsonl");
            System.IO.File.Move(path, rotated);
            _telemetry = new System.IO.StreamWriter(path, append: true) { AutoFlush = true };
            _telemetryBytes = 0;
            Print($"[CFG] [{_stratName}] telemetry=rotated file={rotated}");
        }
    }
    catch (Exception ex)
    {
        Print($"[CFG] [{_stratName}] telemetry=error msg={ex.Message}");
        EnableJsonTelemetry = false;
    }
}

private static string JsonEsc(string s)
{
    if (string.IsNullOrEmpty(s)) return "";
    return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n","\\n").Replace("\r","\\r");
}
#endregion
//== END IMMUTABLE: DIAGNOSTICS ==

//== BEGIN IMMUTABLE: RTM ==
#region Risk Profiles, Sizing, Exits (UI)
public enum RiskProfile { ECP, PCP, DCP, HR }
public enum PositionSizingMode { FixedContracts, PercentEquityRisk, MaxLossPerTrade }
public enum EntryRiskMode { StopTicks, ATRx }
public enum TrailType { Ticks, ATRx }

[NinjaScriptProperty, Display(Name="EnableLongs", GroupName="Entry", Order=5)]
public bool EnableLongs { get; set; } = true;

[NinjaScriptProperty, Display(Name="EnableShorts", GroupName="Entry", Order=6)]
public bool EnableShorts { get; set; } = true;

[NinjaScriptProperty, Display(Name="Session Window", GroupName="Filters", Order=10, Description="Format HH:mm–HH:mm, local time")]
public string SessionWindow { get; set; } = "";

// Cutoff / auto-flat
[NinjaScriptProperty, Display(Name="SessionCutoffTime (HH:mm)", GroupName="Filters", Order=15)]
public string SessionCutoffTime { get; set; } = "";

[NinjaScriptProperty, Display(Name="AutoFlatAtClose", GroupName="Filters", Order=16)]
public bool AutoFlatAtClose { get; set; } = true;

[NinjaScriptProperty, Display(Name="Cooldown Bars", GroupName="Filters", Order=17)]
[Range(0, 500)]
public int CooldownBars { get; set; } = 0;

[NinjaScriptProperty, Display(Name="Max Trades / Day", GroupName="Filters", Order=18)]
[Range(0, 100)]
public int MaxTradesPerDay { get; set; } = 99;

[NinjaScriptProperty, Display(Name="Consec Loss Lockout", GroupName="Filters", Order=19)]
[Range(0, 20)]
public int ConsecLossLockout { get; set; } = 0;

[NinjaScriptProperty, Display(Name="Pause After Win", GroupName="Filters", Order=20)]
public bool PauseAfterWin { get; set; } = false;

// Risk core
[NinjaScriptProperty, Display(Name="RiskProfile", GroupName="Risk", Order=30)]
public RiskProfile RiskProfileMode { get; set; } = RiskProfile.DCP;

[NinjaScriptProperty, Display(Name="PositionSizingMode", GroupName="Risk", Order=31)]
public PositionSizingMode SizingMode { get; set; } = PositionSizingMode.FixedContracts;

[NinjaScriptProperty, Display(Name="DefaultQuantity", GroupName="Risk", Order=32), Range(1, 100)]
public int DefaultQuantity { get; set; } = 1;

[NinjaScriptProperty, Display(Name="AccountEquityEstimate ($)", GroupName="Risk", Order=33), Range(0, double.MaxValue)]
public double AccountEquityEstimate { get; set; } = 50000;

[NinjaScriptProperty, Display(Name="RiskPerTrade ($ or %)", GroupName="Risk", Order=34), Range(0, 100000)]
public double RiskPerTrade { get; set; } = 250;

[NinjaScriptProperty, Display(Name="EntryRiskMode", GroupName="Risk", Order=35)]
public EntryRiskMode EntryRisk { get; set; } = EntryRiskMode.StopTicks;

[NinjaScriptProperty, Display(Name="Stop (ticks)", GroupName="Risk", Order=36), Range(1, 500)]
public int StopTicks { get; set; } = 40;

[NinjaScriptProperty, Display(Name="ATR Period", GroupName="Risk", Order=37), Range(5, 200)]
public int ATRPeriod { get; set; } = 14;

[NinjaScriptProperty, Display(Name="ATR Mult (x)", GroupName="Risk", Order=38), Range(0.1, 20)]
public double ATRMult { get; set; } = 1.0;

// Persistent High-Water Mark (Prop trailing DD) — PER-ACCOUNT ONLY
[NinjaScriptProperty, Display(Name="PersistHWM", GroupName="Risk", Order=39)]
public bool PersistHWM { get; set; } = true;

[NinjaScriptProperty, Display(Name="HWMFilePath", GroupName="Risk", Order=40, Description="Files are keyed PER-ACCOUNT ONLY.")]
public string HWMFilePath { get; set; } =
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
    + System.IO.Path.DirectorySeparatorChar + "NinjaTrader 8"
    + System.IO.Path.DirectorySeparatorChar + "log"
    + System.IO.Path.DirectorySeparatorChar + "StrategyHWM";

// Exits
[NinjaScriptProperty, Display(Name="BE at R (x)", GroupName="Exits", Order=50), Range(0.0, 10.0)]
public double BreakEvenAtR { get; set; } = 1.0;

[NinjaScriptProperty, Display(Name="BE Plus (ticks)", GroupName="Exits", Order=51), Range(0, 50)]
public int BEPlusTicks { get; set; } = 0;

[NinjaScriptProperty, Display(Name="TP1 @ R (x)", GroupName="Exits", Order=52), Range(0.1, 20)]
public double TP1_RMultiple { get; set; } = 1.0;

[NinjaScriptProperty, Display(Name="TP1 %", GroupName="Exits", Order=53), Range(0, 100)]
public int TP1_Pct { get; set; } = 50;

[NinjaScriptProperty, Display(Name="TP2 @ R (x)", GroupName="Exits", Order=54), Range(0.1, 20)]
public double TP2_RMultiple { get; set; } = 2.0;

[NinjaScriptProperty, Display(Name="TP2 %", GroupName="Exits", Order=55), Range(0, 100)]
public int TP2_Pct { get; set; } = 25;

[NinjaScriptProperty, Display(Name="Trail Start @ R (x)", GroupName="Exits", Order=56), Range(0.0, 20)]
public double TP3_TrailingStartR { get; set; } = 2.0;

[NinjaScriptProperty, Display(Name="TP3 % (Trail)", GroupName="Exits", Order=57), Range(0, 100)]
public int TP3_Pct { get; set; } = 25;

[NinjaScriptProperty, Display(Name="Trail Type", GroupName="Exits", Order=58)]
public TrailType TrailMode { get; set; } = TrailType.Ticks;

[NinjaScriptProperty, Display(Name="Trail (ticks)", GroupName="Exits", Order=59), Range(1, 500)]
public int TrailTicks { get; set; } = 30;

[NinjaScriptProperty, Display(Name="Trail ATR Mult (x)", GroupName="Exits", Order=60), Range(0.1, 20)]
public double TrailATRMult { get; set; } = 1.0;

// Failsafes
[NinjaScriptProperty, Display(Name="FlattenOnDisconnect", GroupName="Failsafes", Order=70)]
public bool FlattenOnDisconnect { get; set; } = true;

[NinjaScriptProperty, Display(Name="ProtectOnStopReject", GroupName="Failsafes", Order=71)]
public bool ProtectOnStopReject { get; set; } = true;

// Toggle: start next session in lockout once per breach
[NinjaScriptProperty, Display(Name="StartNextSessionLockoutOnBreach", GroupName="Failsafes", Order=72)]
public bool StartNextSessionLockoutOnBreach { get; set; } = true;
#endregion

#region RTM Internals
private string _entrySignal = "ENTRY";
private string _tp1Signal = "TP1";
private string _tp2Signal = "TP2";

private double _avgEntry;
private double _initStop;
private int _rticks;
private int _tp1Qty;
private int _tp2Qty;
private int _trailQty;
private bool _beMoved;
private bool _trailArmed;

private int _tradesToday;
private int _losingStreak;
private DateTime _lastTradeTime = DateTime.MinValue;

// Persistent High-Water Mark (never daily reset)
private double _hwmEquity = 0;
// Lockout timer and optional session cutoff
private DateTime _lockoutUntil = DateTime.MinValue;
private TimeSpan? _cutoffTs = null;
// Prior-breach flag (for next-session lockout)
private bool _breachFlag = false;

private NinjaTrader.NinjaScript.Indicators.ATR _atr;
#endregion

#region RTM Helpers (PER-ACCOUNT file stem)
private static string Sanitize(string s)
{
    if (string.IsNullOrEmpty(s)) return "NA";
    foreach (var c in System.IO.Path.GetInvalidFileNameChars())
        s = s.Replace(c, '_');
    return s;
}

private string RTM_FileStem()
{
    // ***** PER-ACCOUNT ONLY ***** — DO NOT change to include instrument or strategy
    string acct = "<BACKTEST>";
    try { if (Account != null && !string.IsNullOrEmpty(Account.Name)) acct = Account.Name; } catch { }
    return Sanitize(acct);
}
#endregion

#region RTM Life-cycle Hooks
private void RTM_OnStateChange()
{
    if (State == State.SetDefaults)
    {
        // defaults declared in properties
    }
    else if (State == State.Configure)
    {
        if (_atr == null) _atr = ATR(ATRPeriod);
    }
    else if (State == State.DataLoaded)
    {
        // Normalize TP percentages
        int sum = TP1_Pct + TP2_Pct + TP3_Pct;
        if (sum <= 0) { TP1_Pct = 50; TP2_Pct = 25; TP3_Pct = 25; sum = 100; }
        if (sum != 100)
        {
            double f = 100.0 / sum;
            TP1_Pct = Math.Max(0, (int)Math.Round(TP1_Pct * f));
            TP2_Pct = Math.Max(0, (int)Math.Round(TP2_Pct * f));
            TP3_Pct = 100 - TP1_Pct - TP2_Pct;
            Print($"[CFG] [{Name}] normalized TP pct: {TP1_Pct}/{TP2_Pct}/{TP3_Pct}");
        }

        // HWM init & optional load (PER-ACCOUNT ONLY)
        try
        {
            var eq = Account != null ? Account.Get(AccountItem.NetLiquidation, Currency.UsDollar) : AccountEquityEstimate;
            if (eq > 0 && _hwmEquity <= 0) _hwmEquity = eq;

            if (PersistHWM)
            {
                if (!System.IO.Directory.Exists(HWMFilePath))
                    System.IO.Directory.CreateDirectory(HWMFilePath);

                var stem = RTM_FileStem();
                var fn = System.IO.Path.Combine(HWMFilePath, stem + "_HWM.txt");
                if (System.IO.File.Exists(fn))
                {
                    var text = System.IO.File.ReadAllText(fn).Trim();
                    double saved;
                    if (double.TryParse(text, out saved) && saved > 0)
                        _hwmEquity = Math.Max(_hwmEquity, saved);
                }

                var bf = System.IO.Path.Combine(HWMFilePath, stem + "_BREACH.txt");
                if (StartNextSessionLockoutOnBreach && System.IO.File.Exists(bf))
                {
                    _breachFlag = true;
                }
            }

            // Parse cutoff once
            if (!string.IsNullOrWhiteSpace(SessionCutoffTime))
            {
                TimeSpan ts;
                _cutoffTs = TimeSpan.TryParse(SessionCutoffTime, out ts) ? ts : (TimeSpan?)null;
            }
        }
        catch { /* ignore */ }
    }
}
#endregion

#region RTM Entry Arming API
private int RTM_ComputeQty(double entryPrice, double stopPrice)
{
    int q = DefaultQuantity;
    try
    {
        double equity = Account != null
            ? Account.Get(AccountItem.NetLiquidation, Currency.UsDollar)
            : AccountEquityEstimate;
        if (equity <= 0) equity = AccountEquityEstimate;

        double riskPerContract = Math.Abs(entryPrice - stopPrice) / TickSize * Instrument.MasterInstrument.PointValue * TickSize;
        if (riskPerContract <= 0) return Math.Max(1, DefaultQuantity);

        if (SizingMode == PositionSizingMode.FixedContracts)
            q = DefaultQuantity;
        else if (SizingMode == PositionSizingMode.PercentEquityRisk)
            q = (int)Math.Floor((equity * (RiskPerTrade / 100.0)) / riskPerContract);
        else if (SizingMode == PositionSizingMode.MaxLossPerTrade)
            q = (int)Math.Floor(RiskPerTrade / riskPerContract);

        q = Math.Max(1, q);
    }
    catch { q = Math.Max(1, DefaultQuantity); }
    return q;
}

public void RTM_ArmEntryLong(double plannedEntryPrice)
{
    double stop = (EntryRisk == EntryRiskMode.ATRx)
        ? plannedEntryPrice - (_atr[0] * ATRMult)
        : plannedEntryPrice - (StopTicks * TickSize);
    RTM_Prime(plannedEntryPrice, stop, true);
}

public void RTM_ArmEntryShort(double plannedEntryPrice)
{
    double stop = (EntryRisk == EntryRiskMode.ATRx)
        ? plannedEntryPrice + (_atr[0] * ATRMult)
        : plannedEntryPrice + (StopTicks * TickSize);
    RTM_Prime(plannedEntryPrice, stop, false);
}

private void RTM_Prime(double entry, double stop, bool isLong)
{
    _avgEntry = entry;
    _initStop = stop;
    _rticks = (int)Math.Max(1, Math.Round(Math.Abs(entry - stop) / TickSize));

    int qty = RTM_ComputeQty(entry, stop);
    _tp1Qty = (int)Math.Max(0, Math.Round(qty * (TP1_Pct / 100.0)));
    _tp2Qty = (int)Math.Max(0, Math.Round(qty * (TP2_Pct / 100.0)));
    _trailQty = Math.Max(0, qty - _tp1Qty - _tp2Qty);
    if (_tp1Qty + _tp2Qty + _trailQty != qty) _trailQty = Math.Max(0, qty - _tp1Qty - _tp2Qty);

    _beMoved = false;
    _trailArmed = false;

    Print($"[ENTRY] [{Name}] side={(isLong ? "Long":"Short")} price={entry:F2} qty={qty} RTicks={_rticks}");

    if (isLong) EnterLong(qty, _entrySignal);
    else EnterShort(qty, _entrySignal);
}
#endregion

#region RTM Runtime Management
private void RTM_OnBarUpdate()
{
    if (CurrentBar < 5) return;

    // Session cutoff enforcement (disallow new entries and optional auto-flat)
    if (_cutoffTs.HasValue)
    {
        var nowLocal = Time[0];
        var cutoffToday = nowLocal.Date + _cutoffTs.Value;
        if (nowLocal >= cutoffToday)
        {
            if (AutoFlatAtClose && Position.MarketPosition != MarketPosition.Flat)
            {
                if (Position.MarketPosition == MarketPosition.Long) ExitLong();
                else if (Position.MarketPosition == MarketPosition.Short) ExitShort();
                Dx_Log("RISK", $"auto_flat=1 reason=SessionCutoff time={nowLocal:HH:mm}");
            }
            // Disable entries; ENTRY block should respect EnableEntries
            EnableEntries = false;
        }
    }

    // Move to break-even
    if (!_beMoved && Position.MarketPosition != MarketPosition.Flat && _rticks > 0)
    {
        double rNowTicks = Position.MarketPosition == MarketPosition.Long
            ? (Close[0] - _avgEntry) / TickSize
            : (_avgEntry - Close[0]) / TickSize;

        if (rNowTicks >= BreakEvenAtR * _rticks)
        {
            double bePrice = _avgEntry + (Position.MarketPosition == MarketPosition.Long ? BEPlusTicks * TickSize : -BEPlusTicks * TickSize);
            RTM_UpdateStop(bePrice);
            _beMoved = true;
            Print($"[EXIT] [{Name}] reason=BEMove stop={bePrice:F2} rNowTicks={rNowTicks:F1}");
        }
    }

    // Trailing logic arming & updates
    if (Position.MarketPosition != MarketPosition.Flat && !_trailArmed && _rticks > 0)
    {
        double rNowTicks = Position.MarketPosition == MarketPosition.Long
            ? (Close[0] - _avgEntry) / TickSize
            : (_avgEntry - Close[0]) / TickSize;
        if (rNowTicks >= TP3_TrailingStartR * _rticks)
            _trailArmed = true;
    }

    if (Position.MarketPosition != MarketPosition.Flat && _trailArmed)
    {
        double newStop;
        if (TrailMode == TrailType.Ticks)
        {
            newStop = Position.MarketPosition == MarketPosition.Long
                ? Close[0] - TrailTicks * TickSize
                : Close[0] + TrailTicks * TickSize;
        }
        else
        {
            double offs = _atr[0] * TrailATRMult;
            newStop = Position.MarketPosition == MarketPosition.Long ? Close[0] - offs : Close[0] + offs;
        }

        if (Position.MarketPosition == MarketPosition.Long)
        {
            if (newStop > _initStop) { _initStop = newStop; RTM_UpdateStop(_initStop); Print($"[EXIT] [{Name}] reason=TrailUpdate stop={_initStop:F2}"); }
        }
        else
        {
            if (newStop < _initStop) { _initStop = newStop; RTM_UpdateStop(_initStop); Print($"[EXIT] [{Name}] reason=TrailUpdate stop={_initStop:F2}"); }
        }
    }
}

private void RTM_OnOrderUpdate(Order order) { /* reserved for future */ }

private void RTM_OnExecutionUpdate(Execution execution, Order order)
{
    if (execution == null || order == null) return;

    if (order.FromEntrySignal == _entrySignal && order.OrderState == OrderState.Filled)
    {
        _avgEntry = execution.Price;
        RTM_UpdateStop(_initStop);

        double tp1Price, tp2Price;
        if (Position.MarketPosition == MarketPosition.Long)
        {
            tp1Price = _avgEntry + TP1_RMultiple * _rticks * TickSize;
            tp2Price = _avgEntry + TP2_RMultiple * _rticks * TickSize;
        }
        else
        {
            tp1Price = _avgEntry - TP1_RMultiple * _rticks * TickSize;
            tp2Price = _avgEntry - TP2_RMultiple * _rticks * TickSize;
        }

        if (_tp1Qty > 0) ExitFromPositionLimit(_tp1Qty, tp1Price, _tp1Signal);
        if (_tp2Qty > 0) ExitFromPositionLimit(_tp2Qty, tp2Price, _tp2Signal);
    }
}
#endregion

#region RTM Helpers
private void RTM_UpdateStop(double price)
{
    SetStopLoss(_entrySignal, CalculationMode.Price, price, false);
}

private void ExitFromPositionLimit(int qty, double price, string signalName)
{
    if (Position.MarketPosition == MarketPosition.Long)
        ExitLongLimit(qty, price, signalName, _entrySignal);
    else if (Position.MarketPosition == MarketPosition.Short)
        ExitShortLimit(qty, price, signalName, _entrySignal);
}

private void RTM_SaveHWMIfNeeded(double equity)
{
    if (equity <= 0) return;
    if (equity > _hwmEquity)
    {
        _hwmEquity = equity;
        if (PersistHWM)
        {
            try
            {
                if (!System.IO.Directory.Exists(HWMFilePath))
                    System.IO.Directory.CreateDirectory(HWMFilePath);
                var stem = RTM_FileStem();
                var fn = System.IO.Path.Combine(HWMFilePath, stem + "_HWM.txt");
                System.IO.File.WriteAllText(fn, _hwmEquity.ToString("F2"));
            } catch { }
        }
    }
}
#endregion
//== END IMMUTABLE: RTM ==

        #region Entry Parameters
        [NinjaScriptProperty, Display(Name="EnableEntries", GroupName="Entry", Order=100)]
        public bool EnableEntries { get; set; } = true;
        #endregion

        #region Session_Clock
        private static readonly TimeSpan AO = new TimeSpan(20, 0, 0);
        private static readonly TimeSpan ENTRY_START = new TimeSpan(20, 5, 0);
        private static readonly TimeSpan ENTRY_END = new TimeSpan(23, 15, 0);
        private DateTime _sessionDate = DateTime.MinValue;
        private double _preHigh = double.MinValue;
        private double _preLow = double.MaxValue;
        private double _onRange;

        private void UpdateSession()
        {
            var tod = Time[0].TimeOfDay;
            if (tod >= new TimeSpan(18, 0, 0) && tod < AO)
            {
                _preHigh = Math.Max(_preHigh, High[0]);
                _preLow = Math.Min(_preLow, Low[0]);
            }
            if (Time[0].Date != _sessionDate && tod >= AO)
            {
                _sessionDate = Time[0].Date;
                _onRange = (_preHigh > double.MinValue && _preLow < double.MaxValue) ? _preHigh - _preLow : 0;
                _preHigh = double.MinValue;
                _preLow = double.MaxValue;
                _sumPV = _sumV = 0;
                _tradesToday = 0;
                _longAttemptLive = _shortAttemptLive = false;
                _addonLongUsed = _addonShortUsed = false;
                _coolOffUntil = DateTime.MinValue;
            }
        }
        #endregion

        #region Indicators_Local
        private EMA _ema20_5m;
        private ATR _atr20_5m;
        private double _sumPV;
        private double _sumV;
        private Series<double> _avwapSeries;
        private double _avwap;
        private double _dvap;
        private const double KC_MULT = 1.4;
        private const double EXT_MULT = 0.5;
        private const int BAND_TOL_TICKS = 2;
        private const double k_ATR = 0.55;
        private const int STOP_PAD_TICKS = 2;
        private const double TR_MULT = 2.2;
        private const double TR_MULT_TIGHT = 1.6;
        private const double Z_MIN = 0.80;
        private const double ONR_MAX_RATIO = 1.10;
        private const double VWAP_SLOPE_MAX = 0.10;

        private void UpdateAVWAP()
        {
            if (Time[0].TimeOfDay >= AO)
            {
                double p = (High[0] + Low[0] + Close[0]) / 3.0;
                _sumPV += p * Volume[0];
                _sumV += Volume[0];
                if (_sumV > 0)
                {
                    _avwap = _sumPV / _sumV;
                    _avwapSeries[0] = _avwap;
                }
            }
        }

        private (double upper, double lower, double atr) GetKeltnerSnapshot()
        {
            double atr = _atr20_5m[0];
            double mid = _ema20_5m[0];
            double upper = mid + KC_MULT * atr;
            double lower = mid - KC_MULT * atr;
            return (upper, lower, atr);
        }

        private void UpdateDailyNR()
        {
            if (CurrentBars[2] < 8) return;
            double range1 = Highs[2][1] - Lows[2][1];
            double min4 = double.MaxValue;
            double min7 = double.MaxValue;
            for (int i = 1; i <= 4; i++) min4 = Math.Min(min4, Highs[2][i] - Lows[2][i]);
            for (int i = 1; i <= 7; i++) min7 = Math.Min(min7, Highs[2][i] - Lows[2][i]);
            _priorNR4 = range1 <= min4 + 1e-6;
            _priorNR7 = range1 <= min7 + 1e-6;
        }
        #endregion

        #region Filters
        private bool _priorNR4;
        private bool _priorNR7;
        private double _lastBid = double.NaN;
        private double _lastAsk = double.NaN;

        private bool PassesContraction() => _priorNR4 || _priorNR7;

        private bool PassesLowTrendRegime()
        {
            if (CurrentBar < 30) return true;
            double past = _avwapSeries[30];
            double slope = Math.Abs(_avwap - past) / TickSize / 30.0;
            return slope <= VWAP_SLOPE_MAX;
        }

        private bool PassesOvernightCompression()
        {
            double atr = _atr20_5m[0];
            if (atr <= 0) return false;
            return (_onRange / atr) <= ONR_MAX_RATIO;
        }

        private bool PassesSpreadGuard()
        {
            int maxSpread = GetMaxSpreadTicks();
            if (double.IsNaN(_lastBid) || double.IsNaN(_lastAsk)) return true;
            double spreadTicks = (_lastAsk - _lastBid) / TickSize;
            return spreadTicks <= maxSpread;
        }

        private bool PassesTimeWindow()
        {
            var tod = Time[0].TimeOfDay;
            return tod >= ENTRY_START && tod <= ENTRY_END;
        }
        #endregion

        #region StateMachine
        private enum SMState { IDLE, ARMED, TRIGGERED, ADDON, LOCKOUT }
        private SMState _state = SMState.IDLE;
        private int _tradesToday;
        private bool _longAttemptLive;
        private bool _shortAttemptLive;
        private bool _addonLongUsed;
        private bool _addonShortUsed;
        private DateTime _coolOffUntil = DateTime.MinValue;
        #endregion

        #region Orders_Exec
        private int ComputePlannedStopTicksLong(double lower)
        {
            double atr = _atr20_5m[0];
            double stopPrice = lower - k_ATR * atr - STOP_PAD_TICKS * TickSize;
            int ticks = (int)Math.Round((Close[0] - stopPrice) / TickSize);
            return Math.Max(ticks, GetMinStopTicks());
        }

        private int ComputePlannedStopTicksShort(double upper)
        {
            double atr = _atr20_5m[0];
            double stopPrice = upper + k_ATR * atr + STOP_PAD_TICKS * TickSize;
            int ticks = (int)Math.Round((stopPrice - Close[0]) / TickSize);
            return Math.Max(ticks, GetMinStopTicks());
        }

        private double ProjectedOpenRiskUSD(int stopTicks)
        {
            int slip = Instrument.MasterInstrument.Name.StartsWith("M") ? 1 : 2;
            return (stopTicks + slip) * TickSize * Instrument.MasterInstrument.PointValue;
        }

        private int GetMaxSpreadTicks()
        {
            switch (Instrument.MasterInstrument.Name)
            {
                case "MGC":
                case "GC": return 3;
                case "MCL":
                case "CL": return 4;
                case "MES":
                case "ES": return 2;
                case "MNQ":
                case "NQ": return 4;
                default: return int.MaxValue;
            }
        }

        private int GetMinStopTicks()
        {
            switch (Instrument.MasterInstrument.Name)
            {
                case "MGC":
                case "GC": return 15;
                case "MCL":
                case "CL": return 20;
                case "MES":
                case "ES": return 12;
                case "MNQ":
                case "NQ": return 20;
                default: return 10;
            }
        }

        private bool SignalOvershootSnapbackLong(double lower)
        {
            bool overshoot = Low[1] < lower - BAND_TOL_TICKS * TickSize;
            bool snapback = Close[0] > lower && Close[0] > High[1];
            _dvap = Math.Abs(Close[0] - _avwap) / _atr20_5m[0];
            return overshoot && snapback && _dvap >= Z_MIN && !_longAttemptLive;
        }

        private bool SignalOvershootSnapbackShort(double upper)
        {
            bool overshoot = High[1] > upper + BAND_TOL_TICKS * TickSize;
            bool snapback = Close[0] < upper && Close[0] < Low[1];
            _dvap = Math.Abs(Close[0] - _avwap) / _atr20_5m[0];
            return overshoot && snapback && _dvap >= Z_MIN && !_shortAttemptLive;
        }

        private void TryAddonBIfEligible() { }
        #endregion

        #region Diagnostics_Local
        // placeholder for future HUD or diagnostics extensions
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AVR_Asia_SingleFile";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsUnmanaged = false;
                IsInstantiatedOnEachOptimizationIteration = false;
                SessionCutoffTime = "23:15";
                AutoFlatAtClose = true;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 5);
                AddDataSeries(BarsPeriodType.Day, 1);
                RTM_OnStateChange();
                Dx_Init();
            }
            else if (State == State.DataLoaded)
            {
                _ema20_5m = EMA(BarsArray[1], 20);
                _atr20_5m = ATR(BarsArray[1], 20);
                _avwapSeries = new Series<double>(this);
            }
            else if (State == State.Terminated)
            {
                Dx_Close();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 5) return;

            // Disconnect heuristic → flatten & lockout
            try
            {
                if (FlattenOnDisconnect && Account != null)
                {
                    double eqCheck = Account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
                    RTM_SaveHWMIfNeeded(eqCheck);
                }
            }
            catch
            {
                if (Position.MarketPosition == MarketPosition.Long) ExitLong();
                else if (Position.MarketPosition == MarketPosition.Short) ExitShort();
                _lockoutUntil = Time[0].AddMinutes(LockoutMinutes);
                Dx_Log("RISK", $"protective_flat=1 reason=DisconnectDetected lockoutMin={LockoutMinutes}");
                return;
            }

            // Trailing DD & CircuitBreaker vs PERSISTENT HWM (PER ACCOUNT ONLY)
            if (UseRiskManager && Account != null)
            {
                var equity = Account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
                RTM_SaveHWMIfNeeded(equity);

                var draw = _hwmEquity - equity; // never daily-reset
                if (draw >= PropTrailingDD || draw >= CircuitBreakerDrawdown)
                {
                    if (Position.MarketPosition == MarketPosition.Long) ExitLong();
                    else if (Position.MarketPosition == MarketPosition.Short) ExitShort();

                    _lockoutUntil = Time[0].AddMinutes(LockoutMinutes);

                    // Persist next-session lockout flag (PER ACCOUNT ONLY) if toggle is on
                    if (StartNextSessionLockoutOnBreach && PersistHWM)
                    {
                        try
                        {
                            if (!System.IO.Directory.Exists(HWMFilePath))
                                System.IO.Directory.CreateDirectory(HWMFilePath);
                            var stem = RTM_FileStem();
                            System.IO.File.WriteAllText(System.IO.Path.Combine(HWMFilePath, stem + "_BREACH.txt"), DateTime.UtcNow.ToString("o"));
                        } catch { }
                    }

                    Dx_Log("RISK", $"breach=1 type={(draw >= PropTrailingDD ? "PropDD":"CircuitBreaker")} draw={draw:F2} hwm={_hwmEquity:F2} eq={equity:F2} lockoutMin={LockoutMinutes}");
                    return;
                }
            }

            // Honor lockout window
            if (Time[0] < _lockoutUntil)
            {
                Dx_Why("LOCKOUT", $"until={_lockoutUntil:HH:mm}");
                return;
            }

            // Apply prior-breach start-of-session lockout once (and clear marker)
            if (_breachFlag && StartNextSessionLockoutOnBreach)
            {
                _breachFlag = false; // apply once
                _lockoutUntil = Time[0].AddMinutes(LockoutMinutes);
                Dx_Log("RISK", $"start_lockout=1 reason=PriorBreach lockoutMin={LockoutMinutes}");

                // remove marker so it's "once per breach"
                try
                {
                    var stem = RTM_FileStem();
                    var bf = System.IO.Path.Combine(HWMFilePath, stem + "_BREACH.txt");
                    if (System.IO.File.Exists(bf)) System.IO.File.Delete(bf);
                } catch { }
                return;
            }

            // Always run RTM (handles BE/Trail updates & cutoff)
            RTM_OnBarUpdate();

            if (BarsInProgress == 1) { return; }
            if (BarsInProgress == 2) { UpdateDailyNR(); return; }
            if (BarsInProgress != 0) return;

            UpdateSession();
            UpdateAVWAP();

            if (!EnableEntries) { Dx_Why("ENTRIES_DISABLED", "EnableEntries=false"); return; }

//== BEGIN ENTRY LOGIC (EDITABLE) ==
            if (CurrentBar < 50) return;

            // Gating
            if (!PassesTimeWindow()) { Dx_Why("TIME_WINDOW", "20:05–23:15 only"); return; }
            if (!PassesContraction()) { Dx_Why("NRX", "no NR4/NR7 yesterday"); return; }
            if (!PassesLowTrendRegime()) { Dx_Why("VWAP_SLOPE", "too trended"); return; }
            if (!PassesOvernightCompression()) { Dx_Why("ONR", "overnight range too large"); return; }
            if (!PassesSpreadGuard()) { Dx_Why("SPREAD", "wider than MAX"); return; }
            if (Time[0] < _coolOffUntil) { Dx_Why("COOL_OFF", $"until={_coolOffUntil:HH:mm}"); return; }
            if (_tradesToday >= 2) { Dx_Why("TRADES_MAX", "2 per session"); return; }
            if (!EnableLongs && !EnableShorts) { Dx_Why("ENTRIES_DISABLED", "both sides off"); return; }
            if (Position.MarketPosition != MarketPosition.Flat) { Dx_Why("POS_OPEN", "already in trade"); return; }

            var ks = GetKeltnerSnapshot();

            if (EnableLongs && SignalOvershootSnapbackLong(ks.lower))
            {
                var plannedStopTicks = ComputePlannedStopTicksLong(ks.lower);
                if (ProjectedOpenRiskUSD(plannedStopTicks) > 600) { Dx_Why("OPEN_RISK", ">600"); return; }
                RTM_ArmEntryLong(Close[0]);
                _longAttemptLive = true;
                return;
            }

            if (EnableShorts && SignalOvershootSnapbackShort(ks.upper))
            {
                var plannedStopTicks = ComputePlannedStopTicksShort(ks.upper);
                if (ProjectedOpenRiskUSD(plannedStopTicks) > 600) { Dx_Why("OPEN_RISK", ">600"); return; }
                RTM_ArmEntryShort(Close[0]);
                _shortAttemptLive = true;
                return;
            }

            TryAddonBIfEligible();
//== END ENTRY LOGIC (EDITABLE) ==
        }

        protected override void OnExecutionUpdate(Execution execution, Order order)
        {
            RTM_OnExecutionUpdate(execution, order);

            if (order != null && order.Name == "ENTRY" && execution.OrderState == OrderState.Filled)
            {
                _tradesToday++;
            }

            if (execution != null && order != null && order.Name != null && order.Name.ToUpper().Contains("STOP") && execution.OrderState == OrderState.Filled)
            {
                _coolOffUntil = Time[0].AddMinutes(15);
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                _longAttemptLive = false;
                _shortAttemptLive = false;
            }
        }

        protected override void OnOrderUpdate(Order order)
        {
            RTM_OnOrderUpdate(order);

            // Fail-safe: if a stop/target tied to the ENTRY gets rejected, flatten & lockout
            if (ProtectOnStopReject && order != null && order.OrderState == OrderState.Rejected)
            {
                if (order.FromEntrySignal == "ENTRY")
                {
                    if (Position.MarketPosition == MarketPosition.Long) ExitLong();
                    else if (Position.MarketPosition == MarketPosition.Short) ExitShort();
                    _lockoutUntil = Time[0].AddMinutes(LockoutMinutes);
                    Dx_Log("RISK", $"protective_flat=1 reason=StopOrTargetRejected order={order.Name} lockoutMin={LockoutMinutes}");
                }
            }
        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (marketDataUpdate.MarketDataType == MarketDataType.Bid)
                _lastBid = marketDataUpdate.Price;
            else if (marketDataUpdate.MarketDataType == MarketDataType.Ask)
                _lastAsk = marketDataUpdate.Price;
        }
    }
}
