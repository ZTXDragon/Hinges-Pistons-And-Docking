using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxPortGate")]
    internal class ZtxPortGateRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public ID<PartComponentRules>[] Ports;

        public override PartComponent CreateComponent()
        {
            return new ZtxPortGateComponent(this);
        }
    }

    internal class ZtxPortGateComponent : PartComponent, IPartComponentToggle, ILogicTicker
    {
        private readonly List<IPartComponentToggle> _ports = new List<IPartComponentToggle>();
        private bool _on = true;

        public new ZtxPortGateRules Rules => (ZtxPortGateRules)base.Rules;

        public bool IsToggleOn => _on;
        public bool IsToggleOnAnticipated => _on;
        public event EventHandler<EventArgs> ToggleChanged;

        public ZtxPortGateComponent(ZtxPortGateRules rules) : base(rules) { }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                _ports.Clear();
                if (Rules.Ports != null)
                {
                    foreach (ID<PartComponentRules> id in Rules.Ports)
                    {
                        if (base.Part.TryGetComponent<IPartComponentToggle>(id, out IPartComponentToggle t)) _ports.Add(t);
                    }
                }
                LogicShipManager.GetOrCreate(base.Ship)?.Register(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxPortGateComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                LogicShipManager.TryGet(base.Ship)?.Deregister(this);
                _ports.Clear();
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxPortGateComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }

        public void Tick()
        {
            bool anyConnected = false;
            for (int i = 0; i < _ports.Count; i++)
            {
                if (_ports[i].IsToggleOn) { anyConnected = true; break; }
            }
            bool on = !anyConnected;
            if (on == _on) return;
            _on = on;
            ToggleChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
