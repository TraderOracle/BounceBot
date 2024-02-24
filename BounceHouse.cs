using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Diagnostics;
using ATAS.DataFeedsCore;
using ATAS.Indicators;
using ATAS.Indicators.Technical;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using OFT.Rendering.Control;
using static ATAS.Indicators.Technical.SampleProperties;

using String = System.String;
using Utils.Common.Logging;
using System;

public class BounceHouse : ATAS.Strategies.Chart.ChartStrategy
{
    #region VARIABLES

    private Order globalOrder;

    private const String sVersion = "Beta 1.2";
    private const int ACTIVE = 1;
    private const int STOPPED = 2;
    private int _lastBar = -1;
    private int globalBar = -1;
    private bool _lastBarCounted = false;

    private int iPrevOrderBar = -1;
    private int iFontSize = 12;
    private int iBotStatus = ACTIVE;
    private Stopwatch clock = new Stopwatch();
    private Rectangle rc = new Rectangle() { X = 50, Y = 50, Height = 200, Width = 400 };
    private DateTime dtStart = DateTime.Now;
    private String sLastTrade = String.Empty;
    private String sLastLog = String.Empty;
    private bool bAttendedMode = true;
    private int iMinADX = 0;
    private decimal iBuffer = 0;

    #region TRADING OPTIONS

    private bool bEnterKAMA9 = true;
    private bool bEnterVWAP = true;
    private bool bEnterEMA200 = true;
    private bool bEnterEMA21 = false;

    private int iAdvMaxContracts = 20;
    private int iMaxLoss = 50000;
    private int iMaxProfit = 50000;

    [Display(GroupName = "Trade When These Lines Wicked", Name = "Kaufman Avg 9")]
    public bool EnterKAMA9 { get => bEnterKAMA9; set { bEnterKAMA9 = value; RecalculateValues(); } }

    [Display(GroupName = "Trade When These Lines Wicked", Name = "Daily VWAP")]
    public bool EnterVWAP { get => bEnterVWAP; set { bEnterVWAP = value; RecalculateValues(); } }

    [Display(GroupName = "Trade When These Lines Wicked", Name = "200 EMA")]
    public bool EnterEMA200 { get => bEnterEMA200; set { bEnterEMA200 = value; RecalculateValues(); } }

    [Display(GroupName = "Trade When These Lines Wicked", Name = "21 EMA")]
    public bool EnterEMA21 { get => bEnterEMA21; set { bEnterEMA21 = value; RecalculateValues(); } }

    [Display(GroupName = "General", Name = "Attended Mode", Description = "You handle the stops, take profits")]
    public bool AttendedMode { get => bAttendedMode; set { bAttendedMode = value; RecalculateValues(); } }

    [Display(GroupName = "General", Name = "Max simultaneous contracts", Order = int.MaxValue)]
    [Range(1, 90)]
    public int AdvMaxContracts { get => iAdvMaxContracts; set { iAdvMaxContracts = value; RecalculateValues(); } }

    [Display(GroupName = "General", Name = "Maximum Loss", Description = "Maximum amount of money lost before the bot shuts off")]
    [Range(1, 90000)]
    public int MaxLoss { get => iMaxLoss; set { iMaxLoss = value; RecalculateValues(); } }

    [Display(GroupName = "General", Name = "Maximum Profit", Description = "Maximum profit before the bot shuts off")]
    [Range(1, 90000)]
    public int MaxProfit { get => iMaxProfit; set { iMaxProfit = value; RecalculateValues(); } }

    #endregion


    #endregion

    #region INDICATORS

    private readonly VWAP _VWAP = new VWAP() { TWAPMode = VWAP.VWAPMode.VWAP, Period = 1, Days = 1, VolumeMode = VWAP.VolumeType.Total };
    private readonly EMA Ema21 = new EMA() { Period = 21 };
    private readonly EMA Ema200 = new EMA() { Period = 200 };
    private readonly KAMA _kama9 = new KAMA() { ShortPeriod = 2, LongPeriod = 109, EfficiencyRatioPeriod = 9 };
    private readonly T3 _t3 = new T3() { Period = 10, Multiplier = 1 };

    #endregion

    #region RENDER CONTEXT

    protected override void OnRender(RenderContext context, DrawingLayouts layout)
    {
        var font = new RenderFont("Calibri", iFontSize);
        var fontB = new RenderFont("Calibri", iFontSize, FontStyle.Bold);
        int upY = 50;
        int upX = 50;
        var txt = String.Empty;
        Size tsize;

        switch (iBotStatus)
        {
            case ACTIVE:
                TimeSpan t = TimeSpan.FromMilliseconds(clock.ElapsedMilliseconds);
                String an = String.Format("{0:D2}:{1:D2}:{2:D2}", t.Hours, t.Minutes, t.Seconds);
                txt = $"BounceHouse version " + sVersion;
                context.DrawString(txt, font, Color.Aqua, upX, upY);
                tsize = context.MeasureString(txt, fontB);
                upY += tsize.Height + 6;
                txt = $"ACTIVE on {TradingManager.Portfolio.AccountID} since " + dtStart.ToString() + " (" + an + ")";
                context.DrawString(txt, font, Color.PowderBlue, upX, upY);
                if (!clock.IsRunning)
                    clock.Start();
                break;
            case STOPPED:
                txt = $"BounceHouse STOPPED on {TradingManager.Portfolio.AccountID}";
                context.DrawString(txt, fontB, Color.Orange, upX, upY);
                if (clock.IsRunning)
                    clock.Stop();
                break;
        }
        tsize = context.MeasureString(txt, fontB);
        upY += tsize.Height + 6;

        if (TradingManager.Portfolio != null && TradingManager.Position != null)
        {
            var cl = Color.Lime;
            if (TradingManager.Position.RealizedPnL < 0)
                cl = Color.Orange;
            txt = $"{TradingManager.MyTrades.Count()} trades, with PNL: {TradingManager.Position.RealizedPnL}";
            if (iBotStatus == STOPPED) { txt = String.Empty; sLastTrade = String.Empty; }
            context.DrawString(txt, fontB, cl, upX, upY);
            upY += tsize.Height + 6;
            txt = sLastTrade;
            context.DrawString(txt, font, Color.White, upX, upY);
        }

        if (sLastLog != String.Empty && iBotStatus == ACTIVE)
        {
            upY += tsize.Height + 6;
            txt = $"Last Log: " + sLastLog;
            context.DrawString(txt, font, Color.Yellow, upX, upY);
        }
    }

    #endregion

    public BounceHouse()
    {
        EnableCustomDrawing = true;
        Add(_kama9);
        Add(_VWAP);

        iBuffer = 10;
    }

    protected int CheckLineWick(decimal line, bool chec, IndicatorCandle candle)
    {
        if (!chec)return 0;

        var red = candle.Close < candle.Open;
        var green = candle.Close > candle.Open;

        if (green && candle.Open > line && candle.Low < line)
            return 1;

        if (red && candle.Open < line && candle.High > line)
            return -1;

        return 0;
    }

    protected override void OnCalculate(int bar, decimal value)
    {
        if (bar == 0)
        {
            _lastBarCounted = false;
            return;
        }
        else if (bar < CurrentBar - 3)
            return;

        if (ClosedPnL >= Math.Abs(iMaxLoss))
        {
            AddLog("Max loss reached, bot is shutting off");
            iBotStatus = STOPPED;
        }
        if (ClosedPnL >= Math.Abs(iMaxProfit))
        {
            AddLog("Max profit reached, bot is shutting off");
            iBotStatus = STOPPED;
        }

        var pbar = bar - 1;
        var prevBar = _lastBar;
        _lastBar = bar;

        if (prevBar == bar)
            return;

        var candle = GetCandle(pbar);
        value = candle.Close;

        #region INDICATOR CALCULATIONS

        Ema21.Calculate(pbar, value);
        _t3.Calculate(pbar, value);

        var kama9 = ((ValueDataSeries)_kama9.DataSeries[0])[pbar];
        var e200 = ((ValueDataSeries)Ema200.DataSeries[0])[pbar];
        var e21 = ((ValueDataSeries)Ema21.DataSeries[0])[pbar];
        var vwap = ((ValueDataSeries)_VWAP.DataSeries[0])[pbar];
        var t3 = ((ValueDataSeries)_t3.DataSeries[0])[pbar];

        #endregion

        #region CANDLE CALCULATIONS

        iBuffer = ChartInfo.PriceChartContainer.Step;

        var red = candle.Close < candle.Open;
        var green = candle.Close > candle.Open;
        var p1C = GetCandle(pbar - 1);
        var c1G = p1C.Open < p1C.Close;
        var c1R = p1C.Open > p1C.Close;

        var c0Body = Math.Abs(candle.Close - candle.Open);

        #endregion

        #region OPEN AND CLOSE POSITIONS

        bool closeLong = (red && candle.Open < t3 && candle.Close < t3) && CurrentPosition > 0;
        bool closeShort = (green && candle.Open > t3 && candle.Close > t3) && CurrentPosition < 0;

        if (!bAttendedMode && red && candle.Open < t3 && candle.Close < t3 && CurrentPosition > 0)
        {
            CloseCurrentPosition("T3 crossed", bar);
            prevBar = -1;
        }
        if (!bAttendedMode && green && candle.Open > t3 && candle.Close > t3 && CurrentPosition < 0)
        {
            CloseCurrentPosition("T3 crossed", bar);
            prevBar = -1;
        }

        if (CheckLineWick(kama9, bEnterKAMA9, candle) > 0)
            OpenPosition("KAMA wick", candle, bar, OrderDirections.Buy);
        if (CheckLineWick(kama9, bEnterKAMA9, candle) < 0)
            OpenPosition("KAMA wick", candle, bar, OrderDirections.Sell);

        if (CheckLineWick(e200, bEnterEMA200, candle) > 0)
            OpenPosition("EMA 200 wick", candle, bar, OrderDirections.Buy);
        if (CheckLineWick(e200, bEnterEMA200, candle) < 0)
            OpenPosition("EMA 200 wick", candle, bar, OrderDirections.Sell);

        if (CheckLineWick(e21, bEnterEMA21, candle) > 0)
            OpenPosition("EMA 21 wick", candle, bar, OrderDirections.Buy);
        if (CheckLineWick(e21, bEnterEMA21, candle) < 0)
            OpenPosition("EMA 21 wick", candle, bar, OrderDirections.Sell);

        if (CheckLineWick(vwap, bEnterVWAP, candle) > 0)
            OpenPosition("VWAP wick", candle, bar, OrderDirections.Buy);
        if (CheckLineWick(vwap, bEnterVWAP, candle) < 0)
            OpenPosition("VWAP wick", candle, bar, OrderDirections.Sell);

        #endregion

    }

    #region POSITION METHODS

    private void StopLoss(int bar, OrderDirections dir)
    {
        var candle = GetCandle(bar-3);

        var order = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Direction = dir == OrderDirections.Buy ? OrderDirections.Sell : OrderDirections.Buy,
            Type = OrderTypes.Limit,
            Price = dir == OrderDirections.Buy ? candle.Low - iBuffer : candle.High + iBuffer,
            QuantityToFill = 1
        };
        //OpenOrder(order);
    }

    private void BouncePosition(String sReason, IndicatorCandle c, int bar, OrderDirections direction)
    {
        if (CurrentPosition != 0)
        {
            CloseCurrentPosition("Closing current before opening new", bar);
            OpenPosition(sReason, c, bar, direction);
        }
        else
        {
            OpenPosition(sReason, c, bar, direction);
        }
    }

    private void OpenPosition(String sReason, IndicatorCandle c, int bar, OrderDirections direction)
    {
        if (iBotStatus == STOPPED)
        {
            AddLog("Attempted to open position, but bot was stopped");
            return;
        }
        if (CurrentPosition >= iAdvMaxContracts)
        {
            AddLog("Attempted to open more than (max) contracts, trade canceled");
            return;
        }

        // Limit 1 order per bar
        if (iPrevOrderBar == bar)
            return;
        else
            iPrevOrderBar = bar;

        var sD = direction == OrderDirections.Buy ? sReason + " LONG (" + bar + ")" : sReason + " SHORT (" + bar + ")";
        sLastTrade = direction == OrderDirections.Buy ? "Bar " + bar + " - " + sReason + " LONG at " + c.Close : "Bar " + bar + " - " + sReason + " SHORT at " + c.Close;

        var order = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Direction = direction,
            Type = OrderTypes.Market,
            QuantityToFill = 1,
            Comment = sD
        };
        globalOrder = order;
        globalBar = bar;
        OpenOrder(order);
        AddLog(sLastTrade);
    }

    private void CloseCurrentPosition(String s, int bar)
    {
        if (iBotStatus == STOPPED)
        {
            AddLog("Attempted to close position, but bot was stopped");
            return;
        }

        // Limit 1 order per bar
        if (iPrevOrderBar == bar)
            return;
        else
            iPrevOrderBar = bar;

        var order = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Direction = CurrentPosition > 0 ? OrderDirections.Sell : OrderDirections.Buy,
            Type = OrderTypes.Market,
            QuantityToFill = Math.Abs(CurrentPosition),
            Comment = "Position closed, reason: " + s
        };
        globalOrder = order;
        OpenOrder(order);
        AddLog("Closed, reason: " + s);
    }

    protected override void OnOrderChanged(Order order)
    {
        if (order == globalOrder)
        {
            switch (order.Status())
            {
                case OrderStatus.None:
                    // The order has an undefined status (you need to wait for the next method calls).
                    break;
                case OrderStatus.Placed:
                    // the order is placed.
                    break;
                case OrderStatus.Filled:
                    StopLoss(globalBar, globalOrder.Direction);
                    // the order is filled.
                    break;
                case OrderStatus.PartlyFilled:
                    // the order is partially filled.
                    {
                        var unfilled = order.Unfilled; // this is a unfilled volume.

                        break;
                    }
                case OrderStatus.Canceled:
                    // the order is canceled.
                    break;
            }
        }
    }

    #endregion

    #region MISC METHODS

    private bool IsPointInsideRectangle(Rectangle rectangle, Point point)
    {
        return point.X >= rectangle.X && point.X <= rectangle.X + rectangle.Width && point.Y >= rectangle.Y && point.Y <= rectangle.Y + rectangle.Height;
    }

    public override bool ProcessMouseClick(RenderControlMouseEventArgs e)
    {
        if (e.Button == RenderControlMouseButtons.Left && IsPointInsideRectangle(rc, e.Location))
        {
            CloseCurrentPosition("Closing current position", CurrentBar - 1);
            return true;
        }

        return false;
    }

    private void AddLog(String s)
    {
        sLastLog = s;
    }

    #endregion

}

