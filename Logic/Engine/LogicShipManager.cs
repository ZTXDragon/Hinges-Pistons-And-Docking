using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships;
using Halfling.Scene2D;
using Halfling.Timing;

namespace ZTX.HingesPistonsDocking
{
    internal interface ILogicTicker
    {
        void Tick();
    }

    internal class LogicShipManager : ShipComponent
    {
        private static readonly ConditionalWeakTable<Ship, LogicShipManager> s_managers =
            new ConditionalWeakTable<Ship, LogicShipManager>();

        private readonly List<ILogicTicker> _tickers = new List<ILogicTicker>();

        public readonly List<ZtxRemoteLinkComponent> Links = new List<ZtxRemoteLinkComponent>();

        public LogicShared.Table SharedNow { get; private set; } = new LogicShared.Table();
        public LogicShared.Table SharedLast { get; private set; } = new LogicShared.Table();

        public static float TickSeconds = 1f / 60f;

        public int TickCount { get; private set; }
        private static bool s_stepLogged;
        private int _costWindow = -1;
        private int _costTicks;

        public LogicShipManager()
        {
            base.FixedUpdateBucket = 10;
            base.FixedUpdatingEnabled = true;
        }

        public static LogicShipManager GetOrCreate(Ship ship)
        {
            if (ship == null) return null;
            if (s_managers.TryGetValue(ship, out LogicShipManager manager)) return manager;

            manager = new LogicShipManager();
            LogicShipManager captured = manager;
            Ship capturedShip = ship;
            if (ship.Sim != null)
            {
                ship.Sim.EnqueueDeterministic(ship.UniqueID, delegate
                {
                    capturedShip.Components.Add(captured);
                });
            }
            else
            {
                ship.Components.Add(manager);
            }
            s_managers.Add(ship, manager);
            return manager;
        }

        public static LogicShipManager TryGet(Ship ship)
        {
            if (ship == null) return null;
            s_managers.TryGetValue(ship, out LogicShipManager manager);
            return manager;
        }

        public IEnumerable<ZtxRuleEngineComponent> Engines()
        {
            foreach (ILogicTicker t in _tickers)
                if (t is ZtxRuleEngineComponent e) yield return e;
        }

        public void Register(ILogicTicker ticker)
        {
            if (ticker != null && !_tickers.Contains(ticker))
                _tickers.Add(ticker);
        }

        public void Deregister(ILogicTicker ticker)
        {
            _tickers.Remove(ticker);
        }

        public override void FixedUpdate(FixedUpdater fixedUpdater, SceneRoot root)
        {
            TickCount++;
            LogicShared.Table done = SharedNow;
            SharedNow = SharedLast;
            SharedLast = done;
            SharedNow.Clear();
            if (_tickers.Count == 0) return;
            float step = (float)fixedUpdater.Interval;
            if (step > 0f) TickSeconds = step;
            if (!s_stepLogged) { s_stepLogged = true; Log.Info("logic tick step " + TickSeconds + " s."); }
            bool timed = LogicCost.Active;
            if (timed)
            {
                if (_costWindow != LogicCost.WindowId) { _costWindow = LogicCost.WindowId; _costTicks = 0; }
                LogicCost.NoteRounds(++_costTicks);
            }
            foreach (ILogicTicker t in _tickers.ToArray())
            {
                try
                {
                    if (timed)
                    {
                        long t0 = LogicCost.Now;
                        t.Tick();
                        LogicCost.AddTicker(t, LogicCost.Now - t0);
                    }
                    else t.Tick();
                }
                catch (Exception ex)
                {
                    LogThrottle.Exception("LogicShipManager.FixedUpdate(" + t.GetType().Name + ")", ex);
                }
            }
            if (timed) LogicCost.MaybeReport();
        }
    }
}
