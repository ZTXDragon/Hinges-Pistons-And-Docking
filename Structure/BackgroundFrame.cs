using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Data;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Colliders;
using Halfling.Physics2D.Dynamics;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxBackgroundFrame")]
    internal class ZtxBackgroundFrameRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public ID<PartComponentRules>[] Colliders;

        public override PartComponent CreateComponent()
        {
            return new ZtxBackgroundFrameComponent(this);
        }
    }

    internal class ZtxBackgroundFrameComponent : PartComponent, ILogicTicker
    {
        public ZtxBackgroundFrameComponent(ZtxBackgroundFrameRules rules) : base(rules) { }

        public new ZtxBackgroundFrameRules Rules => (ZtxBackgroundFrameRules)base.Rules;

        public void Tick()
        {
            BackgroundFrame.ApplyMask(base.Part, Rules.Colliders);
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                LogicShipManager.GetOrCreate(base.Ship)?.Register(this);
                BackgroundFrame.ApplyMask(base.Part, Rules.Colliders);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxBackgroundFrameComponent.OnPartAttached", ex);
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
                Log.Exception("ZtxBackgroundFrameComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }
    }

    internal static class BackgroundFrame
    {
        public static readonly Category Mask = RailShipManager.RAIL_MASK;

        public static void ApplyMask(Part part, ID<PartComponentRules>[] colliders = null)
        {
            ShipPhysics phys = part?.Ship?.Physics;
            if (phys == null) return;
            if (colliders != null && colliders.Length > 0)
            {
                ApplyMaskTo(part, phys, colliders);
                return;
            }
            HingeJointManager.ApplyCollidesWithToPart(part, Mask);
            IReadOnlyList<Fixture> custom = phys.GetCustomFixtures(part);
            if (custom == null) return;
            for (int i = 0; i < custom.Count; i++)
            {
                Fixture f = custom[i];
                if (f != null && f.CollidesWith != Mask) f.CollidesWith = Mask;
            }
        }

        private static void ApplyMaskTo(Part part, ShipPhysics phys, ID<PartComponentRules>[] colliders)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                if (!part.TryGetComponent<BaseCollider>(colliders[i], out BaseCollider collider)) continue;
                if (!phys._customFixtures.TryGetValue(collider, out ShipPhysics.CustomFixtureInfo info)) continue;
                Fixture f = info.Fixture;
                if (f != null && f.CollidesWith != Mask) f.CollidesWith = Mask;
            }
        }

        public static bool IsWholeFrame(ZtxBackgroundFrameRules rules)
        {
            return rules != null && (rules.Colliders == null || rules.Colliders.Length == 0);
        }

        private static readonly ConcurrentDictionary<PartRules, bool> s_isBackground = new ConcurrentDictionary<PartRules, bool>();

        public static bool HasMarker(IEnumerable<Type> componentTypes)
        {
            if (componentTypes == null) return false;
            foreach (Type t in componentTypes)
                if (t == typeof(ZtxBackgroundFrameRules)) return true;
            return false;
        }

        public static bool Is(PartRules rules)
        {
            if (rules == null) return false;
            return s_isBackground.GetOrAdd(rules, r => HasMarker(TypesOf(r)) && WholeFrameIn(r));
        }

        private static bool WholeFrameIn(PartRules rules)
        {
            foreach (PartComponentRules c in rules.Components)
                if (c is ZtxBackgroundFrameRules frame && IsWholeFrame(frame)) return true;
            return false;
        }

        private static IEnumerable<Type> TypesOf(PartRules rules)
        {
            foreach (PartComponentRules c in rules.Components)
                if (c != null) yield return c.GetType();
        }
    }
}
