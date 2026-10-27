using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints.Graphics;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Rendering;
using Cosmoteer.Simulation.Overlays;
using Halfling.Physics2D.Common;
using Halfling.Physics2D.Dynamics;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxRailArc")]
    internal class ZtxRailArcRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        public AtlasSprite LineSprite;

        [Serialize(Optional = true)]
        public AtlasSprite BedSprite;

        [Serialize(Optional = true)]
        public ID<ShipRenderLayerRules> LineLayer;

        [Serialize(Optional = true)]
        public ID<ShipRenderLayerRules> BedLayer;

        [Serialize(Optional = true)]
        [PortableHash]
        public float BandHalfWidth = 0.5f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float SegmentLength = 0.25f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float HitSegmentLength = 0.5f;

        [Serialize(Optional = true)]
        public CappedLine PreviewLine;

        static ZtxRailArcRules()
        {
            try
            {
                BlueprintOverlayManager.RegisterOverlay(1100, () => new RailArcOverlay());
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRailArcRules overlay registration", ex);
            }
        }

        public override IEnumerable<AtlasSprite> GetAtlasSprites(ID<ShipRenderLayerRules> layerID)
        {
            if (layerID == LineLayer && LineSprite != null) yield return LineSprite;
            if (layerID == BedLayer && BedSprite != null) yield return BedSprite;
        }

        public override PartComponent CreateComponent()
        {
            return new ZtxRailArcComponent(this);
        }
    }

    internal class ZtxRailArcComponent : PartComponent
    {
        private Ship _subscribed;
        private Action<Part> _handler;
        private readonly List<ManagedAtlasQuad> _quads = new List<ManagedAtlasQuad>();
        private string _lastDrawn = "";
        private readonly List<ArcHitKey> _hits = new List<ArcHitKey>();
        private ShipPhysics _hitPhysics;
        private Ship _bodyShip;
        private string _lastHits = "";

        public new ZtxRailArcRules Rules
        {
            get { return (ZtxRailArcRules)base.Rules; }
        }

        public ZtxRailArcComponent(ZtxRailArcRules rules) : base(rules) { }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                _subscribed = base.Ship;
                if (_subscribed != null) Subscribe(_subscribed, true);
                if (_subscribed != null)
                {
                    _bodyShip = _subscribed;
                    _bodyShip.Physics.PhysicsWrapper.CreatedBody += OnCreatedBody;
                    _bodyShip.Physics.PhysicsWrapper.DestroyingBody += OnDestroyingBody;
                }
                Recompute();
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRailArcComponent.OnPartAttached", ex);
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
                ClearHits();
                if (_bodyShip != null)
                {
                    _bodyShip.Physics.PhysicsWrapper.CreatedBody -= OnCreatedBody;
                    _bodyShip.Physics.PhysicsWrapper.DestroyingBody -= OnDestroyingBody;
                    _bodyShip = null;
                }
                RailArcArt.Destroy(_quads);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRailArcComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }

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
            int reach = (RailCurves.RulesOf(me.Ship)?.CurveMaxDistance ?? 6) + 1;
            Halfling.Geometry.IntVector2 d = part.Location - me.Location;
            if (Math.Abs(d.X) > reach || Math.Abs(d.Y) > reach) return;
            Recompute();
        }

        private void OnCreatedBody(object sender, EventArgs e)
        {
            Recompute();
        }

        private void OnDestroyingBody(object sender, EventArgs e)
        {
            try
            {
                ClearHits();
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRailArcComponent.OnDestroyingBody", ex);
            }
        }

        private void ClearHits()
        {
            if (_hitPhysics != null)
                foreach (ArcHitKey k in _hits)
                    if (_hitPhysics._customFixtures.ContainsKey(k)) _hitPhysics.RemoveCustomCollider(k);
            _hits.Clear();
            _hitPhysics = null;
        }

        private void AddHits(Ship ship, Part me, RailArc.Shape s)
        {
            ShipPhysics phys = ship.Physics;
            if (phys?.Body == null) return;
            bool mineA = s.A == me.Location;
            float scale = ship.WorldUniformScale;
            Category cats = CollisionCategories.GetCollisionCategoriesFor(me.Rules, isHigh: false);
            foreach (RailArc.HitPiece h in RailArc.HitPieces(s, Rules.BandHalfWidth, Rules.HitSegmentLength))
            {
                if (h.OwnerA != mineA) continue;
                var v = new Vertices(4) { h.P1 * scale, h.P2 * scale, h.P4 * scale, h.P3 * scale };
                var key = new ArcHitKey(ArcHitKeyRules.Shared, h);
                phys.AddCustomCollider(key, v, 0f, cats, me);
                phys.GetCustomFixture(key).CollidesWith = RailShipManager.RAIL_MASK;
                _hits.Add(key);
            }
            _hitPhysics = phys;
        }

        public void Recompute()
        {
            try
            {
                RailArcArt.Destroy(_quads);
                ClearHits();
                Part me = base.Part;
                Ship ship = me?.Ship;
                if (ship == null) return;
                string drawn = "", hits = "";
                foreach (RailArc.Shape s in RailCurves.Pairs(ship, out _, true))
                {
                    if (!s.Has(me.Location)) continue;
                    if (s.A == me.Location)
                    {
                        _quads.AddRange(RailArcArt.Create(ship, s, Rules));
                        drawn = $"to {s.B.X},{s.B.Y}: {_quads.Count} pieces";
                    }
                    try
                    {
                        AddHits(ship, me, s);
                        hits = $"{_hits.Count} hit pieces{(ship.Physics?.Body == null ? " (no physics body yet)" : "")}";
                    }
                    catch (Exception ex)
                    {
                        Log.Exception("ZtxRailArcComponent.AddHits", ex);
                    }
                    break;
                }
                if (drawn != _lastDrawn)
                {
                    _lastDrawn = drawn;
                    if (Log.VERBOSE && drawn.Length > 0) Log.Info($"Curve rail at {me.Location.X},{me.Location.Y}: drawing the curve {drawn}{(ship.Renderer == null ? " (no ship renderer yet)" : "")}.");
                }
                if (hits != _lastHits)
                {
                    _lastHits = hits;
                    if (Log.VERBOSE && hits.Length > 0) Log.Info($"Curve rail at {me.Location.X},{me.Location.Y}: {hits}.");
                }
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRailArcComponent.Recompute", ex);
            }
        }
    }
}
