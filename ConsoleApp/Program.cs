using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ConsoleApp
{
    public class HourlyQuote
    {
        public DateTime Date { get; set; }
        public string Ticker { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }

        public double Volume1 { get; set; }

        public double Volume2 { get; set; }
    }

    static class Extensions
    {
        public static CultureInfo enUS = new CultureInfo("en-US");

        public static double ToDouble(this string s)
        {
            return double.Parse(s, enUS);
        }

        public static HourlyQuote GetQuote(this List<HourlyQuote> list, DateTime date)
        {
            foreach (var quote in list)
            {
                if (quote.Date.Year == date.Year && quote.Date.Month==date.Month && quote.Date.Day == date.Day && quote.Date.Hour == date.Hour)
                    return quote;
            }
            return null;
        }
    }

    class BackTestSettings
    {
        public DateTime StartDate { get; set; }
        public double TrailingStop { get; set; }

        public double CatchLow { get; set; }

        public double StartBalance { get; set; }

        public double Commission { get; set; }
    }

    class Transaction
    {
        public string Type;
        public DateTime Date;
        public double BTCAmount;
        public double USDAmount;
        public double Rate;

        public string ToString()
        {
            return $"{Date.ToString("yyyy-MM-dd HH:00")} {Type} {BTCAmount.ToString("0.####")} BTC @ {USDAmount.ToString("0.##")} USD at rate {Rate.ToString("0.##")}";
        }
    }

    class Position
    {
        public DateTime Date;
        public double BTCBalance;
        public double USDBalance;

        public double LastHigh;

        public double TakeProfit;
        public double StopLoss;
        public double LimitBuyOrder;

        public List<HourlyQuote> Quotes;
        public List<Transaction> Transactions = new List<Transaction>();

        public List<Transaction> Buys
        {
            get { return Transactions.Where(x => x.Type == "BUY").ToList(); }
        }

        public List<Transaction> Sells
        {
            get { return Transactions.Where(x => x.Type == "SELL").ToList(); }
        }

        public BackTestSettings Settings;
        
        public double USDValue
        {
            get { return USDBalance + Quotes.GetQuote(Date).Open*BTCBalance; }
        }

        public void Buy(double price = 0)
        {
            var quote = Quotes.GetQuote(Date);
            if (quote != null && USDBalance > 0)
            {
                var usd = USDBalance;
                if (price == 0)
                    price = quote.Open;
                var amount = (usd / price * (1 - Settings.Commission));
                BTCBalance += amount;
                USDBalance = 0;
                Transactions.Add(new Transaction
                {
                    Type = "BUY",
                    BTCAmount = amount,
                    USDAmount = usd,
                    Date = Date,
                    Rate = quote.Open
                });
                Console.WriteLine($"{Transactions[Transactions.Count - 1].ToString()} |{BTCBalance.ToString("0.####")} BTC / {USDBalance.ToString("0.##")} USD");
            }
        }

        public void Sell(double price = 0)
        {
            var quote = Quotes.GetQuote(Date);
            if (quote != null && BTCBalance > 0)
            {
                if (price == 0)
                    price = quote.Open;
                var btc = BTCBalance;
                var amount = (btc * price * (1 - Settings.Commission));
                USDBalance += amount;
                BTCBalance = 0;
                Transactions.Add(new Transaction
                {
                    Type = "BUY",
                    BTCAmount = btc,
                    USDAmount = amount,
                    Date = Date,
                    Rate = quote.Open
                });
                Console.WriteLine($"{Transactions[Transactions.Count - 1].ToString()} | BTC: {BTCBalance.ToString("0.####")} | USD: {USDBalance.ToString("0.##")}");
            }
        }
    }

    class Program
    {
        static List<HourlyQuote> BTCUSD = new List<HourlyQuote>();

        static void LoadBTCUSD()
        {
            BTCUSD.Clear();
            var lines = File.ReadAllLines("BTCUSD.csv").ToList();
            lines[0] = null; lines[1] = null;
            CultureInfo enUS = new CultureInfo("en-US");
            foreach (var line in lines)
            {
                if (line == null)
                    continue;
                string[] split = line.Split(',');
                BTCUSD.Add(new HourlyQuote
                {
                    Date = DateTime.ParseExact(split[0], "yyyy-MM-dd hh-tt", enUS, DateTimeStyles.AssumeUniversal),
                    Ticker = split[1],
                    Open = split[2].ToDouble(),
                    High = split[3].ToDouble(),
                    Low = split[4].ToDouble(),
                    Close=split[5].ToDouble(),
                    Volume1=split[6].ToDouble(),
                    Volume2=split[7].ToDouble()
                });
            }

            
        }

        static void Main(string[] args)
        {
            LoadBTCUSD();

            var settings = new BackTestSettings
            {
                StartDate = new DateTime(2017, 8, 17, 5, 0, 0),
                StartBalance = 1000,
                CatchLow = 0.9, //90% from trailing stop
                TrailingStop = 0.9, //90% from LastHigh
                Commission = 0.001
            };

            var position = new Position
            {
                Settings = settings,
                USDBalance = 1000.0,
                BTCBalance = 0.0,
                Date = BTCUSD[BTCUSD.Count-1].Date,
                Quotes = BTCUSD
            };

            position.Buy();

            for (int i = BTCUSD.Count - 1; i >= 0; i--)
            {
                var quote = BTCUSD[i];
                position.Date = quote.Date;
                //Last High
                if (quote.High > position.LastHigh)
                    position.LastHigh = quote.High;
                //1.Buying
                if (position.BTCBalance == 0 && position.LimitBuyOrder != 0)
                {
                    if (position.LimitBuyOrder <= quote.High && position.LimitBuyOrder >= quote.Low)
                    {
                        position.Buy(position.LimitBuyOrder);
                    }
                }
                //3.Trailing Stop
                if (position.BTCBalance > 0)
                {
                    var stoploss = position.LastHigh * position.Settings.TrailingStop;
                    if (quote.Close < stoploss)
                    {
                        position.Sell(stoploss);
                        position.LimitBuyOrder = stoploss * position.Settings.CatchLow;
                    } else
                    {
                        position.StopLoss = stoploss;
                    }
                }
            }
            Console.ReadLine();
        }
    }
}
