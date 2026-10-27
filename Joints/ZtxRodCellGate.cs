using System;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxRodCellGate")]
    internal class ZtxRodCellGateRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public int Cell = 1;

        public override PartComponent CreateComponent()
        {
            return new ZtxRodCellGateComponent(this);
        }
    }

    internal class ZtxRodCellGateComponent : PartComponent, IPartComponentToggle, ILogicTicker
    {
        private const float OPEN_MARGIN = 0.015f;

        private bool _on;

        public new ZtxRodCellGateRules Rules => (ZtxRodCellGateRules)base.Rules;

        public bool IsToggleOn => _on;
        public bool IsToggleOnAnticipated => _on;
        public event EventHandler<EventArgs> ToggleChanged;

        public ZtxRodCellGateComponent(ZtxRodCellGateRules rules) : base(rules) { }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                LogicShipManager.GetOrCreate(base.Ship)?.Register(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRodCellGateComponent.OnPartAttached", ex);
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
                Log.Exception("ZtxRodCellGateComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }

        public void Tick()
        {
            bool known = HingeJointManager.TryGetTipExtensionCells(base.Part, out float cells)
                         || MergeService.TryGetMergedTipCells(base.Part, out cells);
            bool on = known && cells > Rules.Cell - 1 + OPEN_MARGIN;
            if (on == _on) return;
            _on = on;
            ToggleChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
