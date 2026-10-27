using System;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;

namespace ZTX.HingesPistonsDocking
{
    internal abstract class ZtxLogicTickerComponent : PartComponent, IPartComponentToggle, ILogicTicker
    {
        private bool _on;

        public bool IsToggleOn => _on;
        public bool IsToggleOnAnticipated => _on;
        public event EventHandler<EventArgs> ToggleChanged;

        protected string LastRejectReason;

        public string LastWhyNot => LastRejectReason ?? "";

        protected ZtxLogicTickerComponent(PartComponentRules rules) : base(rules) { }

        public abstract void Tick();

        protected void SetToggle(bool on)
        {
            if (on == _on) return;
            _on = on;
            ToggleChanged?.Invoke(this, EventArgs.Empty);
        }

        protected void LogRejectOnce(string reason)
        {
            if (LastRejectReason == reason) return;
            LastRejectReason = reason;
            if (reason != null)
                Log.Info($"{GetType().Name} at {base.Part?.Location}: {reason}.");
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                LogicShipManager.GetOrCreate(base.Ship)?.Register(this);
            }
            catch (Exception ex)
            {
                Log.Exception(GetType().Name + ".OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                LogicShipManager.TryGet(base.Ship)?.Deregister(this);
            }
            catch (Exception ex)
            {
                Log.Exception(GetType().Name + ".OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }
    }
}
