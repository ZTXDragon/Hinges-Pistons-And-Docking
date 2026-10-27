using System;
using System.Collections.Generic;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class GripMath
    {
        public const int MAX_CELLS = 99;

        public const float GRID_ANGLE_TOL = 0.03f, GRID_CELL_TOL = 0.1f;

        public static bool TryGrid(float relAngle, float gx, float gy, out GridTransform xf)
        {
            xf = GridTransform.Identity;
            double quarter = Math.PI / 2.0;
            double n = Math.Round(relAngle / quarter);
            if (Math.Abs(relAngle - n * quarter) > GRID_ANGLE_TOL) return false;
            var turn = new GridTransform((int)n, IntVector2.Zero);
            var image = turn.TransformPoint(new Vector2(0.5f, 0.5f));
            float dx = gx - image.X, dy = gy - image.Y;
            int tx = (int)Math.Round(dx), ty = (int)Math.Round(dy);
            if (Math.Abs(dx - tx) > GRID_CELL_TOL || Math.Abs(dy - ty) > GRID_CELL_TOL) return false;
            xf = new GridTransform((int)n, new IntVector2(tx, ty));
            return true;
        }

        public static int ClampOffset(int offset, int width)
        {
            int w = Math.Max(1, Math.Min(MAX_CELLS, width));
            return Math.Max(-(w / 2), Math.Min((w - 1) / 2, offset));
        }

        public static void AreaCells(IntVector2 anchor, IntVector2 facing, bool inLine, int width, int length, int offset, List<IntVector2> into)
        {
            var s = new AreaSettings
            {
                Width = Math.Max(1, Math.Min(MAX_CELLS, width)),
                Length = Math.Max(1, Math.Min(MAX_CELLS, length)),
                Position = AreaSensorGeometry.POSITION_FRONT,
            };
            AreaSensorGeometry.Cells(inLine ? anchor - facing : anchor, facing, s, into);
            int o = ClampOffset(offset, s.Width);
            if (o != 0)
            {
                var shift = new IntVector2(-facing.Y * o, facing.X * o);
                for (int i = 0; i < into.Count; i++) into[i] = into[i] + shift;
            }
            if (inLine) into.Remove(anchor);
        }

        public static void CartAreaCells(IntVector2 cartCell, IntVector2 arrow, bool inLine, int width, int length, int offset, List<IntVector2> into)
        {
            into.Clear();
            AreaCells(cartCell, new IntVector2(-arrow.X, -arrow.Y), inLine, width, length, offset, into);
        }

        public static List<T> Select<T>(IEnumerable<T> seeds, Func<T, bool> inside, Func<T, IEnumerable<T>> neighbours,
            Func<T, bool> excluded, out int cut)
        {
            var taken = new List<T>();
            var seen = new HashSet<T>();
            var queue = new Queue<T>();
            foreach (T s in seeds)
            {
                if (s == null || excluded(s) || !inside(s) || !seen.Add(s)) continue;
                taken.Add(s);
                queue.Enqueue(s);
            }
            while (queue.Count > 0)
            {
                T current = queue.Dequeue();
                foreach (T next in neighbours(current))
                {
                    if (next == null || seen.Contains(next) || excluded(next) || !inside(next)) continue;
                    seen.Add(next);
                    taken.Add(next);
                    queue.Enqueue(next);
                }
            }
            cut = 0;
            foreach (T t in taken)
                foreach (T next in neighbours(t))
                    if (next != null && !seen.Contains(next) && !excluded(next)) cut++;
            return taken;
        }
    }
}
