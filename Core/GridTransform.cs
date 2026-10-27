using System;
using Halfling;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal readonly struct GridTransform
    {
        public readonly int K;
        public readonly IntVector2 T;

        private static readonly Vector2[] OFFSETS =
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        };

        public GridTransform(int k, IntVector2 t)
        {
            K = Mathx.Wrap(k, 0, 3);
            T = t;
        }

        public static GridTransform Identity => new GridTransform(0, IntVector2.Zero);

        public bool IsIdentity => K == 0 && T == IntVector2.Zero;

        public float Theta => K * ((float)Math.PI / 2f);

        public IntVector2 TransformCell(IntVector2 cell)
        {
            return cell.Rotate90(K) + T;
        }

        public IntVector2 RotateVector(IntVector2 v)
        {
            return v.Rotate90(K);
        }

        public GridTransform Inverse()
        {
            int k = (4 - K) % 4;
            var back = new GridTransform(k, IntVector2.Zero);
            return new GridTransform(k, back.RotateVector(-T));
        }

        public GridTransform Then(GridTransform next)
        {
            return new GridTransform(K + next.K, next.RotateVector(T) + next.T);
        }

        public Vector2 TransformPoint(Vector2 p)
        {
            float c;
            float s;
            switch (K)
            {
                case 1: c = 0f; s = 1f; break;
                case 2: c = -1f; s = 0f; break;
                case 3: c = 0f; s = -1f; break;
                default: c = 1f; s = 0f; break;
            }
            var rotated = new Vector2(p.X * c - p.Y * s, p.X * s + p.Y * c);
            return rotated + OFFSETS[K] + new Vector2(T.X, T.Y);
        }

        public Vector2 ContinuousOffset => OFFSETS[K] + new Vector2(T.X, T.Y);

        public static bool TryFromDockPair(IntVector2 cellA, IntVector2 facingA,
            IntVector2 cellB, IntVector2 facingB, out GridTransform xf)
        {
            IntVector2 wanted = new IntVector2(-facingA.X, -facingA.Y);
            for (int k = 0; k < 4; k++)
            {
                if (facingB.Rotate90(k) == wanted)
                {
                    IntVector2 t = (cellA + facingA) - cellB.Rotate90(k);
                    xf = new GridTransform(k, t);
                    return true;
                }
            }
            xf = Identity;
            return false;
        }

        public override string ToString()
        {
            return $"(k={K}, t={T})";
        }
    }
}
