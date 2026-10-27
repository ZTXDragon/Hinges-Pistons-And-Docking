using System;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class DockRelease
    {
        public static float Distance(int facingX, int facingY, int width, int height, float marginCells)
        {
            int length = facingX != 0 ? width : height;
            return length + Math.Max(0f, marginCells);
        }

        public static bool FlyClear(Ship carrier, Part dock, Ship released, IntRect footprint, float marginCells, out float cells)
        {
            cells = 0f;
            var body = carrier?.Physics?.Body;
            if (body == null || dock == null || released == null) return false;
            IntVector2 f = DockGeometry.FacingDir(dock);
            cells = Distance(f.X, f.Y, footprint.Width, footprint.Height, marginCells);
            Vector2 step = body.GetWorldPoint(new Vector2(f.X, f.Y) * carrier.Scale) - body.GetWorldPoint(Vector2.Zero);
            Vector2 destination = released.DetWorldCenter + step * cells;
            MoveCommand command = new MoveCommand(released,
                released.Commands.AllocGlobalCommandID(assertOnSceneThread: false),
                destination, released.DetRotation,
                CollisionAvoidanceFlags.None, isTentative: false);
            released.Commands.EnqueueCommand(command, append: false, confirmed: true);
            return true;
        }
    }
}
