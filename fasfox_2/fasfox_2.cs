// -------------------------------------------------------------------------------------------------
//
//
//    This robot is intended to be used as a sample and does not guarantee any particular outcome or
//    profit of any kind. Use it at your own risk
//
//    All changes to this file will be lost on next application start.
//    If you are going to modify this file please make a copy using the "Duplicate" command.
//
//    The "Sample Martingale Robot" creates a random Sell or Buy order. If the Stop loss is hit, a new 
//    order of the same type (Buy / Sell) is created with double the Initial Volume amount. The robot will 
//    continue to double the volume amount for  all orders created until one of them hits the take Profit. 
//    After a Take Profit is hit, a new random Buy or Sell order is created with the Initial Volume amount.
//
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class MartingaleRobot : Robot
    {
        private MovingAverage _fastMa;
        private MovingAverage _slowMa;

        [Parameter()]
        public DataSeries SourceSeries { get; set; }

        [Parameter(DefaultValue = "FasFox")]
        public string EALabel { get; set; }

        [Parameter("MA Type")]
        public MovingAverageType MAType { get; set; }

        [Parameter("Slow Periods", DefaultValue = 10)]
        public int SlowPeriods { get; set; }

        [Parameter("Fast Periods", DefaultValue = 5)]
        public int FastPeriods { get; set; }

        [Parameter(DefaultValue = 100000)]
        public int Volume { get; set; }

        [Parameter("Trigger (pips)", DefaultValue = 10)]
        public int Trigger { get; set; }

        [Parameter("Trailing Stop (pips)", DefaultValue = 10)]
        public int TrailingStop { get; set; }

        [Parameter("MinBalance", DefaultValue = 5000)]
        public double MinBalance { get; set; }

        [Parameter("MinLoss", DefaultValue = -200.0)]
        public double MinLoss { get; set; }

        [Parameter(DefaultValue = 3)]
        public int MaxPositions { get; set; }



        [Parameter("Initial Volume", DefaultValue = 10000, MinValue = 0)]
        public int InitialVolume { get; set; }

        [Parameter("Stop Loss", DefaultValue = 40)]
        public int StopLoss { get; set; }

        [Parameter("Take Profit", DefaultValue = 40)]
        public int TakeProfit { get; set; }

        private Random random = new Random();
        //================================================================================
        //                                                             ON START
        //================================================================================
        protected override void OnStart()
        {
            Positions.Closed += OnPositionsClosed;

            // ExecuteOrder(InitialVolume, GetRandomTradeType());

            foreach (var position in Account.Positions)
            {
                if (position.TradeType == TradeType.Buy)
                {
                    Trade.CreateSellMarketOrder(Symbol, Volume);
                    break;
                    // optional break to hedge only one position
                }
                else
                {
                    Trade.CreateBuyMarketOrder(Symbol, Volume);
                    break;
                    // optional break to hedge only one position
                }
            }
            Positions.Closed += OnPositionsClosed;

            _fastMa = Indicators.MovingAverage(SourceSeries, FastPeriods, MAType);
            _slowMa = Indicators.MovingAverage(SourceSeries, SlowPeriods, MAType);

            Positions.Opened += PositionsOnOpened;
            Positions.Closed += PositionsOnClosed;
        }
        //================================================================================
        //                                                             EXECUTIVE ORDER
        //================================================================================
        private void ExecuteOrder(long volume, TradeType tradeType)
        {
            var result = ExecuteMarketOrder(tradeType, Symbol, volume, "Martingale", StopLoss, TakeProfit);

            if (result.Error == ErrorCode.NoMoney)
                Stop();
        }

        //================================================================================
        //                                                             On Closed Position
        //================================================================================
        private void OnPositionsClosed(PositionClosedEventArgs args)
        {
            Print("Closed");
            var position = args.Position;

            if (position.Label != "Martingale" || position.SymbolCode != Symbol.Code)
                return;

            if (position.GrossProfit > 0)
            {
                ExecuteOrder(InitialVolume, GetRandomTradeType());
            }
            else
            {
                ExecuteOrder((int)position.Volume * 1, position.TradeType);
            }
        }
        //================================================================================
        //                                                             RANDOM TRADE-TYPE
        //================================================================================
        private TradeType GetRandomTradeType()
        {
            return random.Next(2) == 0 ? TradeType.Buy : TradeType.Sell;
        }


        protected override void OnTick()
        {
            foreach (var position in Positions)
            {
                if (position.StopLoss == null)
                {
                    Print("Modifying {0}", position.Id);
                    ModifyPosition(position, GetAbsoluteStopLoss(position, 10), GetAbsoluteTakeProfit(position, 10));
                }
            }
        }
        //================================================================================
        //                                                           ON ABSOLUTE LOSS STOP
        //================================================================================
        private double GetAbsoluteStopLoss(Position position, int stopLossInPips)
        {
            return position.TradeType == TradeType.Buy ? position.EntryPrice - Symbol.PipSize * stopLossInPips : position.EntryPrice + Symbol.PipSize * stopLossInPips;
        }

        private double GetAbsoluteTakeProfit(Position position, int takeProfitInPips)
        {
            return position.TradeType == TradeType.Buy ? position.EntryPrice + Symbol.PipSize * takeProfitInPips : position.EntryPrice - Symbol.PipSize * takeProfitInPips;
        }

        protected override void OnBar()
        {
            var cBotPositions = Positions.FindAll(EALabel);

            if (cBotPositions.Length > MaxPositions)
                return;

            var currentSlowMa = _slowMa.Result.Last(0);
            var currentFastMa = _fastMa.Result.Last(0);

            var previousSlowMa = _slowMa.Result.Last(1);
            var previousFastMa = _fastMa.Result.Last(1);

            // Condition to Buy
            if (previousSlowMa > previousFastMa && currentSlowMa <= currentFastMa)
                ExecuteMarketOrder(TradeType.Buy, Symbol, Volume, EALabel, StopLoss, TakeProfit);
            else if (previousSlowMa < previousFastMa && currentSlowMa >= currentFastMa)
                ExecuteMarketOrder(TradeType.Sell, Symbol, Volume, EALabel, StopLoss, TakeProfit);

            // Some condition to close all positions
            if (Account.Balance < MinBalance)
                foreach (var position in cBotPositions)
                    ClosePosition(position);

            // Some condition to close one position
            foreach (var position in cBotPositions)
                if (position.GrossProfit < MinLoss)
                    ClosePosition(position);

            // Trailing Stop for all positions
            SetTrailingStop();
        }

        private void PositionsOnOpened(PositionOpenedEventArgs obj)
        {
            Position openedPosition = obj.Position;
            if (openedPosition.Label != EALabel)
                return;

            Print("position opened at {0}", openedPosition.EntryPrice);
        }

        private void PositionsOnClosed(PositionClosedEventArgs obj)
        {
            Position closedPosition = obj.Position;
            if (closedPosition.Label != EALabel)
                return;

            Print("position closed with {0} gross profit", closedPosition.GrossProfit);
        }

        private void SetTrailingStop()
        {
            var sellPositions = Positions.FindAll(EALabel, Symbol, TradeType.Sell);

            foreach (Position position in sellPositions)
            {
                double distance = position.EntryPrice - Symbol.Ask;

                if (distance < Trigger * Symbol.PipSize)
                    continue;

                double newStopLossPrice = Symbol.Ask + TrailingStop * Symbol.PipSize;

                if (position.StopLoss == null || newStopLossPrice < position.StopLoss)
                    ModifyPosition(position, newStopLossPrice, position.TakeProfit);
            }

            var buyPositions = Positions.FindAll(EALabel, Symbol, TradeType.Buy);

            foreach (Position position in buyPositions)
            {
                double distance = Symbol.Bid - position.EntryPrice;

                if (distance < Trigger * Symbol.PipSize)
                    continue;

                double newStopLossPrice = Symbol.Bid - TrailingStop * Symbol.PipSize;
                if (position.StopLoss == null || newStopLossPrice > position.StopLoss)
                    ModifyPosition(position, newStopLossPrice, position.TakeProfit);
            }
        }
    }
}
