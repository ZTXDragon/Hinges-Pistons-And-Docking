using System;
using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class LogicBuildQueue
    {
        private readonly List<(string Label, Action Piece)> _items = new List<(string, Action)>();
        private int _next;
        private int _insert;

        public int Pending => _items.Count - _next;

        public int Done { get; private set; }

        public string SlowestLabel { get; private set; } = "";
        public long SlowestStamps { get; private set; }

        public void Enqueue(string label, Action piece) => _items.Add((label, piece));

        public void InsertNext(string label, Action piece) => _items.Insert(_insert++, (label, piece));

        public void Clear()
        {
            _items.Clear();
            _next = 0;
            _insert = 0;
            Done = 0;
            SlowestLabel = "";
            SlowestStamps = 0;
        }

        public long Run(double budgetMs)
        {
            if (_next >= _items.Count) return 0;
            long t0 = LogicCost.Now;
            do
            {
                var (label, piece) = _items[_next++];
                _insert = _next;
                long p0 = LogicCost.Now;
                try { piece(); }
                catch (Exception ex) { Log.Exception("LogicBuildQueue." + label, ex); }
                long took = LogicCost.Now - p0;
                if (took > SlowestStamps)
                {
                    SlowestStamps = took;
                    SlowestLabel = label;
                }
                Done++;
            }
            while (_next < _items.Count && LogicCost.Ms(LogicCost.Now - t0) < budgetMs);
            if (_next >= _items.Count)
            {
                _items.Clear();
                _next = 0;
                _insert = 0;
            }
            return LogicCost.Now - t0;
        }
    }
}
