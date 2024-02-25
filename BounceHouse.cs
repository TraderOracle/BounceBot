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
using OFT.Attributes.Editors;
using System.Collections.ObjectModel;
using String = System.String;
using Color = System.Drawing.Color;
using MColor = System.Windows.Media.Color;

public class BounceHouse : ATAS.Strategies.Chart.ChartStrategy
{
    #region VARIABLES

    private Order globalOrder;

    private const String sVersion = "Beta 1.4";
    private const int ACTIVE = 1;
    private const int STOPPED = 2;
    private int _lastBar = -1;
    private int globalBar = -1;
    private bool _lastBarCounted = false;
    decimal st = 0;

    private int iPrevOrderBar = -1;
    private int iFontSize = 12;
    private int iBotStatus = ACTIVE;
    private Stopwatch clock = new Stopwatch();
    private Rectangle rc = new Rectangle() { X = 50, Y = 50, Height = 200, Width = 400 };
    private DateTime dtStart = DateTime.Now;
    private String sLastTrade = String.Empty;
    private String sLastLog = String.Empty;
    private int iMinADX = 0;
    private decimal iBuffer = 0;

    #endregion

    #region TRADING OPTIONS

    private bool bEnterKAMA9 = true;
    private bool bEnterVWAP = true;
    private bool bEnterEMA200 = true;
    private bool bEnterEMA21 = false;
    private bool bShowLines = true;

    private int iAdvMaxContracts = 20;
    private int iMaxLoss = 50000;
    private int iMaxProfit = 50000;
    private int iTradeDirection = 1;
    private int iBotType = 1;

    [Display(GroupName = "General", Name = "Show Lines")]
    public bool ShowLines { get => bShowLines; set { bShowLines = value; RecalculateValues(); } }

    private class tradeDir : Collection<Entity>
    {

        public tradeDir()
            : base(new[]
            {
                    new Entity { Value = 1, Name = "Both long and short" },
                    new Entity { Value = 2, Name = "Longs only" },
                    new Entity { Value = 3, Name = "Shorts only" },
                    new Entity { Value = 4, Name = "With trend direction" },
            }) { }
    }
    [Display(GroupName = "General", Name = "Trade Direction")]
    [ComboBoxEditor(typeof(tradeDir), DisplayMember = nameof(Entity.Name), ValueMember = nameof(Entity.Value))]
    public int trDir { get => iTradeDirection; set { if (value < 0) return; iTradeDirection = value; RecalculateValues(); } }

    private class botType : Collection<Entity>
    {
        public botType()
            : base(new[]
            {
                    new Entity { Value = 1, Name = "Fully Automated" },
                    new Entity { Value = 2, Name = "Trades but no stop" },
                    new Entity { Value = 3, Name = "Indicator Mode (no trades)" },
            })
        { }
    }
    [Display(GroupName = "General", Name = "Bot Type")]
    [ComboBoxEditor(typeof(botType), DisplayMember = nameof(Entity.Name), ValueMember = nameof(Entity.Value))]
    public int botTyp { get => iBotType; set { if (value < 0) return; iBotType = value; RecalculateValues(); } }


    [Display(GroupName = "General", Name = "Max simultaneous contracts", Order = int.MaxValue)]
    [Range(1, 90)]
    public int AdvMaxContracts { get => iAdvMaxContracts; set { iAdvMaxContracts = value; RecalculateValues(); } }

    [Display(GroupName = "General", Name = "Maximum Loss", Description = "Maximum amount of money lost before the bot shuts off")]
    [Range(1, 90000)]
    public int MaxLoss { get => iMaxLoss; set { iMaxLoss = value; RecalculateValues(); } }

    [Display(GroupName = "General", Name = "Maximum Profit", Description = "Maximum profit before the bot shuts off")]
    [Range(1, 90000)]
    public int MaxProfit { get => iMaxProfit; set { iMaxProfit = value; RecalculateValues(); } }

    [Display(GroupName = "Trade When These Lines Wicked", Name = "Kaufman Avg 9")]
    public bool EnterKAMA9 { get => bEnterKAMA9; set { bEnterKAMA9 = value; RecalculateValues(); } }

    [Display(GroupName = "Trade When These Lines Wicked", Name = "Daily VWAP")]
    public bool EnterVWAP { get => bEnterVWAP; set { bEnterVWAP = value; RecalculateValues(); } }

    [Display(GroupName = "Trade When These Lines Wicked", Name = "200 EMA")]
    public bool EnterEMA200 { get => bEnterEMA200; set { bEnterEMA200 = value; RecalculateValues(); } }

    [Display(GroupName = "Trade When These Lines Wicked", Name = "21 EMA")]
    public bool EnterEMA21 { get => bEnterEMA21; set { bEnterEMA21 = value; RecalculateValues(); } }

    #endregion

    #region INDICATORS

    private readonly VWAP _VWAP = new VWAP() { VWAPOnly = true, Type = VWAP.VWAPPeriodType.Daily, TWAPMode = VWAP.VWAPMode.VWAP, VolumeMode = VWAP.VolumeType.Total, Period = 300 };
    private readonly EMA Ema21 = new EMA() { Period = 21 };
    private readonly EMA Ema200 = new EMA() { Period = 200 };
    private readonly KAMA _kama9 = new KAMA() { ShortPeriod = 2, LongPeriod = 109, EfficiencyRatioPeriod = 9 };
    private readonly T3 _t3 = new T3() { Period = 10, Multiplier = 1 };
    private readonly Pivots _pivots = new Pivots();
    private readonly SuperTrend _st = new SuperTrend() { Period = 11, Multiplier = 2m };

    private readonly ValueDataSeries _posSeries = new("Regular Buy Signal") { Color = MColor.FromArgb(255, 0, 255, 0), VisualType = VisualMode.UpArrow, Width = 2 };
    private readonly ValueDataSeries _negSeries = new("Regular Sell Signal") { Color = MColor.FromArgb(255, 255, 104, 48), VisualType = VisualMode.DownArrow, Width = 2 };

    private readonly ValueDataSeries _lineVWAP = new("VWAP") { Color = MColor.FromArgb(180, 30, 114, 250), VisualType = VisualMode.Line, Width = 3 };
    private readonly ValueDataSeries _lineEMA200 = new("EMA 200") { Color = MColor.FromArgb(255, 165, 166, 164), VisualType = VisualMode.Line, Width = 3 };
    private readonly ValueDataSeries _lineEMA21 = new("EMA 21") { Color = MColor.FromArgb(180, 98, 252, 3), VisualType = VisualMode.Line, Width = 2 };
    private readonly ValueDataSeries _lineKAMA = new("KAMA") { Color = MColor.FromArgb(180, 252, 186, 3), VisualType = VisualMode.Line, Width = 2 };

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

        DataSeries[0] = _posSeries;
        DataSeries.Add(_negSeries);
        DataSeries.Add(_lineVWAP);
        DataSeries.Add(_lineEMA200);
        DataSeries.Add(_lineEMA21);
        DataSeries.Add(_lineKAMA);

        Add(_kama9);
        Add(_VWAP);
        Add(_pivots);
        Add(_st);

        iBuffer = 10;
    }

    protected override void OnCalculate(int bar, decimal value)
    {
        if (bar == 0)
        {
            _lastBarCounted = false;
            return;
        }
//        else if (bar < CurrentBar - 3)
//            return;

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
        var pvt = ((ValueDataSeries)_pivots.DataSeries[0])[pbar];
        st = ((ValueDataSeries)_st.DataSeries[0])[pbar];

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

        if (red && candle.Open < t3 && candle.Close < t3 && CurrentPosition > 0)
        {
            CloseCurrentPosition("T3 crossed", bar);
            prevBar = -1;
        }
        if (green && candle.Open > t3 && candle.Close > t3 && CurrentPosition < 0)
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

        if (bShowLines && bEnterEMA200)
            _lineEMA200[pbar] = e200;

        if (bShowLines && bEnterEMA21)
            _lineEMA21[pbar] = e21;

        if (bShowLines && bEnterKAMA9)
            _lineKAMA[pbar] = kama9;

        if (bShowLines && bEnterVWAP)
            _lineVWAP[pbar] = vwap;

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

    private void OpenPosition(String sReason, IndicatorCandle c, int bar, OrderDirections direction)
    {
        var sD = "";

        // Limit 1 order per bar
        if (iPrevOrderBar == bar)
            return;
        else
            iPrevOrderBar = bar;

        if (iBotStatus == STOPPED)
        {
            AddLog("Attempted to open position, but bot was stopped");
            return;
        }

        decimal _tick = ChartInfo.PriceChartContainer.Step;
        if (direction == OrderDirections.Buy)
        {
            _posSeries[bar-1] = c.Low - (_tick * 2);
            sD = sReason + " LONG (" + bar + ")";
            sLastTrade = "Bar " + bar + " - " + sReason + " LONG at " + c.Close;
        }
        else
        {
            sD = sReason + " SHORT (" + bar + ")";
            sLastTrade = "Bar " + bar + " - " + sReason + " SHORT at " + c.Close;
            _negSeries[bar-1] = c.High + (_tick * 2);
        }

        if (iBotType == 3) // "Indicator Mode (no trades)"
            return;

        if (CurrentPosition >= iAdvMaxContracts)
        {
            AddLog("Attempted to open more than (max) contracts, trade canceled");
            return;
        }

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
        if (iBotType > 1) // "Indicator Mode (no trades)"
            return;

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

    protected int CheckLineWick(decimal line, bool chec, IndicatorCandle candle)
    {
        int iRes = 0;
        if (!chec) return 0;

        var red = candle.Close < candle.Open;
        var green = candle.Close > candle.Open;

        if (green && candle.Open > line && candle.Low < line)
            iRes = 1;

        if (red && candle.Open < line && candle.High > line)
            iRes = -1;

        if (iTradeDirection == 2 && iRes == -1) // Value = 2, Name = "Longs only"
        {
            AddLog("Wanted to short, but settings: LONG ONLY");
            iRes = 0;
        }
        if (iTradeDirection == 3 && iRes == 1) // Value = 3, Name = "Shorts only"
        {
            AddLog("Wanted to long, but settings: SHORT ONLY");
            iRes = 0;
        }
        if (iTradeDirection == 4 && iRes == 1 && st < 0) // Value = 4, Name = "With supertrend"
        {
            AddLog("Skipped trade due to 'With Trend Only' setting");
            iRes = 0;
        }
        if (iTradeDirection == 4 && iRes == -1 && st > 0) // Value = 4, Name = "With supertrend"
        {
            AddLog("Skipped trade due to 'With Trend Only' setting");
            iRes = 0;
        }

        return iRes;
    }

    #endregion

}

