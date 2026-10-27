using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;

namespace ZTX.HingesPistonsDocking
{
    internal static class OrderRebuild
    {
        public static int KindOf(Command c)
        {
            if (c is MoveCommand) return AssemblyGraph.ORDER_MOVE;
            if (c is AttackCommand) return AssemblyGraph.ORDER_ATTACK;
            if (c is FollowCommand) return AssemblyGraph.ORDER_FOLLOW;
            if (c is IAttackCommand) return AssemblyGraph.ORDER_ATTACK;
            return AssemblyGraph.ORDER_OTHER;
        }

        public static Command For(Command c, Ship to, CommandID id)
        {
            if (c == null || to == null) return null;
            switch (c)
            {
                case MoveCommand m:
                    return new MoveCommand(to, id, m.Location, m.Rotation, m.CollisionAvoidanceFlags, isTentative: false, m.ForceDestinationRotation)
                    {
                        RotationTarget = m.RotationTarget,
                        ForceDestinationRotation = m.ForceDestinationRotation,
                        CollisionAvoidanceFlags = m.CollisionAvoidanceFlags,
                        WaitForCrew = m.WaitForCrew,
                    };
                case AttackCommand a:
                    if (a.Target == null || ReferenceEquals(a.Target, to)) return null;
                    return new AttackCommand(to, id, a.Target, a.CollisionAvoidanceFlags, isTentative: false, isConfirmed: false)
                    {
                        FollowRadius = a.FollowRadius,
                        FollowAngle = a.FollowAngle,
                        UseWorldRelativeFollowAngle = a.UseWorldRelativeFollowAngle,
                        Rotation = a.Rotation,
                        RotationMode = a.RotationMode,
                        RotationTarget = a.RotationTarget,
                        CollisionAvoidanceFlags = a.CollisionAvoidanceFlags,
                        WaitForCrew = a.WaitForCrew,
                    };
                case FollowCommand f:
                    if (f.Target == null || ReferenceEquals(f.Target, to)) return null;
                    return new FollowCommand(to, id, f.Target, f.CollisionAvoidanceFlags, isTentative: false, waitForCrew: f.WaitForCrew)
                    {
                        FollowRadius = f.FollowRadius,
                        FollowAngle = f.FollowAngle,
                        UseWorldRelativeFollowAngle = f.UseWorldRelativeFollowAngle,
                        Rotation = f.Rotation,
                        RotationMode = f.RotationMode,
                        RotationTarget = f.RotationTarget,
                        CollisionAvoidanceFlags = f.CollisionAvoidanceFlags,
                    };
                default:
                    return null;
            }
        }
    }
}
