using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxRailShape")]
    internal class ZtxRailShapeRules : PartComponentRules
    {
        public override PartComponent CreateComponent()
        {
            return new ZtxRailShapeComponent(this);
        }
    }

    internal class ZtxRailShapeComponent : PartComponent
    {
        private const int NEAR = 3;

        private static readonly OrthogonalRotation[] s_frame =
            { OrthogonalRotation.Up, OrthogonalRotation.Right, OrthogonalRotation.Down, OrthogonalRotation.Left };

        private static readonly ConditionalWeakTable<Part, ZtxRailShapeComponent> s_byPart =
            new ConditionalWeakTable<Part, ZtxRailShapeComponent>();

        private Ship _subscribed;

        public int Bits { get; private set; }

        public event EventHandler<EventArgs> Changed;

        public ZtxRailShapeComponent(ZtxRailShapeRules rules) : base(rules) { }

        public static ZtxRailShapeComponent ForPart(Part part)
        {
            if (part == null) return null;
            s_byPart.TryGetValue(part, out ZtxRailShapeComponent shape);
            return shape;
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                s_byPart.AddOrUpdate(base.Part, this);
                _subscribed = base.Ship;
                if (_subscribed != null) Subscribe(_subscribed, true);
                foreach (PartComponent c in base.Part.Components)
                    if (c is ZtxRailPieceComponent piece) piece.Bind(this);
                Recompute();
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRailShapeComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                if (_subscribed != null)
                {
                    Subscribe(_subscribed, false);
                    _subscribed = null;
                }
                s_byPart.Remove(base.Part);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRailShapeComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }

        private Action<Part> _handler;

        private void Subscribe(Ship ship, bool add)
        {
            object parts = ship.Parts;
            _handler ??= OnPartChanged;
            foreach (string name in new[] { "PartAdded", "PartRemoved" })
            {
                System.Reflection.EventInfo ev = parts.GetType().GetEvent(name);
                if (ev == null) continue;
                if (add) ev.AddEventHandler(parts, _handler);
                else ev.RemoveEventHandler(parts, _handler);
            }
        }

        private void OnPartChanged(Part part)
        {
            Part me = base.Part;
            if (part == null || me == null || part == me) return;
            IntVector2 d = part.Location - me.Location;
            if (Math.Abs(d.X) > NEAR || Math.Abs(d.Y) > NEAR) return;
            Recompute();
        }

        public void Recompute()
        {
            int bits;
            try
            {
                bits = Compute();
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRailShapeComponent.Recompute", ex);
                return;
            }
            if (bits == Bits) return;
            Bits = bits;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private int Compute()
        {
            Part me = base.Part;
            Ship ship = me?.Ship;
            if (ship == null) return 0;
            IntVector2 cell = me.Location;
            Func<IntVector2, IReadOnlyList<IntVector2>> neighbours = c => RailGrid.NeighbourDirs(ship, c);
            Func<IntVector2, IntVector2> upOf = c => RailGrid.UpOf(ship, c);
            List<RailSides.Span> spans = RailSides.Spans(cell, neighbours, upOf, out _);
            return RailSides.PieceBits(cell, spans, ToLocal, c => RailGrid.Blocked(ship, c));
        }

        private int ToLocal(IntVector2 world)
        {
            Part me = base.Part;
            for (int i = 0; i < 4; i++)
                if (s_frame[i].GetShipRelative(me.Rotation, me.FlipX).ToVector() == world) return i;
            return -1;
        }

    }

    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxRailPiece")]
    internal class ZtxRailPieceRules : PartComponentRules
    {
        [Serialize]
        [PortableHash]
        public int Bit;

        public override PartComponent CreateComponent()
        {
            return new ZtxRailPieceComponent(this);
        }
    }

    internal class ZtxRailPieceComponent : PartComponent, IPartComponentToggle
    {
        private ZtxRailShapeComponent _shape;
        private bool _last;

        public new ZtxRailPieceRules Rules
        {
            get { return (ZtxRailPieceRules)base.Rules; }
        }

        public bool IsToggleOn
        {
            get { return _shape != null && (_shape.Bits & (1 << Rules.Bit)) != 0; }
        }

        public bool IsToggleOnAnticipated
        {
            get { return IsToggleOn; }
        }

        public event EventHandler<EventArgs> ToggleChanged;

        public ZtxRailPieceComponent(ZtxRailPieceRules rules) : base(rules) { }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            ZtxRailShapeComponent shape = ZtxRailShapeComponent.ForPart(base.Part);
            if (shape != null) Bind(shape);
        }

        public override void OnPartDetaching()
        {
            if (_shape != null) _shape.Changed -= OnShapeChanged;
            _shape = null;
            base.OnPartDetaching();
        }

        public void Bind(ZtxRailShapeComponent shape)
        {
            if (_shape == shape) return;
            if (_shape != null) _shape.Changed -= OnShapeChanged;
            _shape = shape;
            _shape.Changed += OnShapeChanged;
            OnShapeChanged(this, EventArgs.Empty);
        }

        private void OnShapeChanged(object sender, EventArgs e)
        {
            bool on = IsToggleOn;
            if (on == _last) return;
            _last = on;
            ToggleChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
