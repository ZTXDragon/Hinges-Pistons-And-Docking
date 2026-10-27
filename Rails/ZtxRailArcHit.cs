using Cosmoteer.Ships.Parts.Colliders;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class ArcHitKeyRules : BaseColliderRules
    {
        public static readonly ArcHitKeyRules Shared =
            (ArcHitKeyRules)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ArcHitKeyRules));

        private ArcHitKeyRules() { }

        public override float GetWeight()
        {
            return 0f;
        }
    }

    internal sealed class ArcHitKey : BaseCollider
    {
        private readonly Vector2 _p1, _p2, _p3, _p4;

        public ArcHitKey(ArcHitKeyRules rules, RailArc.HitPiece piece) : base(rules, false)
        {
            _p1 = piece.P1;
            _p2 = piece.P2;
            _p3 = piece.P3;
            _p4 = piece.P4;
        }

        public override bool ContainsPoint(Vector2 point)
        {
            Vector2[] ring = { _p1, _p2, _p4, _p3 };
            int pos = 0, neg = 0;
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = ring[i], b = ring[(i + 1) % 4];
                float c = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
                if (c > 0f) pos++;
                else if (c < 0f) neg++;
            }
            return pos == 0 || neg == 0;
        }

        public override void AddCollider() { }

        public override void RemoveCollider() { }
    }
}
