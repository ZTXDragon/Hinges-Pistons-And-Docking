using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal enum InputClass { Unknown = 0, Bool = 1, Value = 2, Actuator = 3, ActuatorPos = 4, Cart = 5, Dock = 6, DockPlain = 7, Terminal = 8 }

    internal enum BRole { None = 0, Input = 1, Reset = 2, Than = 3, Station = 4 }

    internal static class LogicOpCatalog
    {
        private static readonly LogicOp[] s_valueOps = { LogicOp.Above, LogicOp.Below, LogicOp.Equal, LogicOp.Pass, LogicOp.Not };

        private static readonly LogicOp[] s_boolOps =
        {
            LogicOp.Pass, LogicOp.Not, LogicOp.And, LogicOp.Or, LogicOp.Xor, LogicOp.AndNot, LogicOp.Nand, LogicOp.Nor,
            LogicOp.OnDelay, LogicOp.OffDelay, LogicOp.Pulse, LogicOp.Latch, LogicOp.Flip, LogicOp.Blink, LogicOp.Count,
        };

        private static readonly LogicOp[] s_actOps = { LogicOp.Moving, LogicOp.Merged };

        private static readonly LogicOp[] s_cartOps = { LogicOp.Moving, LogicOp.Merged, LogicOp.AtStation, LogicOp.Holding };

        private static readonly LogicOp[] s_terminalOps = { LogicOp.Moving, LogicOp.Pass, LogicOp.Merged };

        private static readonly LogicOp[] s_dockOps =
        {
            LogicOp.Moving, LogicOp.Docked, LogicOp.Crewed, LogicOp.Supplied, LogicOp.Repaired, LogicOp.Charged, LogicOp.Built,
        };

        private static readonly LogicOp[] s_dockPlainOps = { LogicOp.Moving, LogicOp.Docked };

        private static readonly LogicOp[] s_actPosOps = { LogicOp.Moving, LogicOp.Merged, LogicOp.Above, LogicOp.Below, LogicOp.Equal };

        private static readonly LogicOp[] s_allOps =
        {
            LogicOp.Pass, LogicOp.Not, LogicOp.And, LogicOp.Or, LogicOp.Xor, LogicOp.AndNot, LogicOp.Nand, LogicOp.Nor,
            LogicOp.OnDelay, LogicOp.OffDelay, LogicOp.Pulse, LogicOp.Latch, LogicOp.Flip, LogicOp.Blink, LogicOp.Count,
            LogicOp.Above, LogicOp.Below, LogicOp.Equal, LogicOp.Moving, LogicOp.Merged, LogicOp.AtStation, LogicOp.Holding, LogicOp.Docked,
            LogicOp.Crewed, LogicOp.Supplied, LogicOp.Repaired, LogicOp.Charged, LogicOp.Built,
        };

        public static IReadOnlyList<LogicOp> Allowed(InputClass c)
        {
            switch (c)
            {
                case InputClass.Value: return s_valueOps;
                case InputClass.Bool: return s_boolOps;
                case InputClass.Actuator: return s_actOps;
                case InputClass.ActuatorPos: return s_actPosOps;
                case InputClass.Cart: return s_cartOps;
                case InputClass.Terminal: return s_terminalOps;
                case InputClass.Dock: return s_dockOps;
                case InputClass.DockPlain: return s_dockPlainOps;
                default: return s_allOps;
            }
        }

        public static bool IsAllowed(InputClass c, LogicOp op)
        {
            if (op == LogicOp.None) return true;
            foreach (LogicOp o in Allowed(c)) if (o == op) return true;
            return false;
        }

        public static LogicOp Default(InputClass c)
        {
            switch (c)
            {
                case InputClass.Value: return LogicOp.Above;
                case InputClass.Actuator:
                case InputClass.ActuatorPos:
                case InputClass.Terminal: return LogicOp.Moving;
                case InputClass.Cart: return LogicOp.AtStation;
                case InputClass.Dock:
                case InputClass.DockPlain: return LogicOp.Docked;
                default: return LogicOp.Pass;
            }
        }

        public static string LabelKey(InputClass c, LogicOp op)
        {
            if (c == InputClass.Terminal)
            {
                if (op == LogicOp.Pass) return "TerminalAtRest";
                if (op == LogicOp.Merged) return "TerminalParked";
                if (op == LogicOp.Moving) return "TerminalMoving";
            }
            return op.ToString();
        }

        public static bool IsStateOp(LogicOp op) => op == LogicOp.AtOn || op == LogicOp.AtOff || op == LogicOp.Moving || op == LogicOp.Merged || op == LogicOp.AtStation
            || op == LogicOp.Holding || op == LogicOp.Docked || op == LogicOp.Crewed || op == LogicOp.Supplied
            || op == LogicOp.Repaired || op == LogicOp.Charged || op == LogicOp.Built;

        public static BRole RoleOfB(LogicOp op)
        {
            switch (op)
            {
                case LogicOp.And:
                case LogicOp.Or:
                case LogicOp.Xor:
                case LogicOp.AndNot:
                case LogicOp.Nand:
                case LogicOp.Nor:
                    return BRole.Input;
                case LogicOp.Latch:
                case LogicOp.Count:
                    return BRole.Reset;
                case LogicOp.Above:
                case LogicOp.Below:
                case LogicOp.Equal:
                    return BRole.Than;
                case LogicOp.AtStation:
                    return BRole.Station;
                default:
                    return BRole.None;
            }
        }

        public static bool BAccepts(LogicOp op, InputClass c)
        {
            BRole role = RoleOfB(op);
            if (role == BRole.None || role == BRole.Station) return false;
            if (role == BRole.Than) return c == InputClass.Value || c == InputClass.ActuatorPos || c == InputClass.Unknown;
            return c != InputClass.Actuator && c != InputClass.ActuatorPos;
        }

        public static bool ShowsValue(LogicOp op, LogicRef b) => LogicRuleEval.IsCompare(op) ? b.IsNone : LogicRuleEval.UsesParam(op);

        public static bool ShowsUnits(LogicOp op, bool aHasUnits) => LogicRuleEval.IsCompare(op) && aHasUnits;

        public const int UNITS_HIDDEN = 0, UNITS_PERCENT_ONLY = 1, UNITS_CHOICE = 2;

        public static int UnitsState(LogicOp op, bool reportsUnits) =>
            !LogicRuleEval.IsCompare(op) ? UNITS_HIDDEN : reportsUnits ? UNITS_CHOICE : UNITS_PERCENT_ONLY;

        public static int SelfTest(out string first)
        {
            var fails = new List<string>();
            Check(fails, !IsAllowed(InputClass.Value, LogicOp.And), "value input offers AND");
            Check(fails, IsAllowed(InputClass.Value, LogicOp.Above) && IsAllowed(InputClass.Value, LogicOp.Not), "value input lacks ABOVE or IS OFF");
            Check(fails, Allowed(InputClass.Value).Count == 5 && IsAllowed(InputClass.Value, LogicOp.Equal), "value input does not offer exactly 5 operators incl. EQUALS");
            Check(fails, !IsAllowed(InputClass.Bool, LogicOp.Above) && !IsAllowed(InputClass.Bool, LogicOp.Below), "on/off input offers ABOVE or BELOW");
            Check(fails, !IsAllowed(InputClass.Bool, LogicOp.AtOn), "on/off input offers AT ON");
            Check(fails, Allowed(InputClass.Bool).Count == 15, "on/off input does not offer exactly 15 operators");
            Check(fails, Allowed(InputClass.Actuator).Count == 2 && IsAllowed(InputClass.Actuator, LogicOp.Moving) && IsAllowed(InputClass.Actuator, LogicOp.Merged) && !IsAllowed(InputClass.Actuator, LogicOp.Pass), "actuator: states only, incl. MERGED");
            Check(fails, Allowed(InputClass.ActuatorPos).Count == 5 && IsAllowed(InputClass.ActuatorPos, LogicOp.Below) && IsAllowed(InputClass.ActuatorPos, LogicOp.Merged) && !IsAllowed(InputClass.ActuatorPos, LogicOp.And), "positioned actuator: states and compares");
            Check(fails, Allowed(InputClass.Unknown).Count == LogicLine.OP_COUNT - 2, "a gone part does not offer every operator");
            Check(fails, IsAllowed(InputClass.Bool, LogicOp.None) && IsAllowed(InputClass.Value, LogicOp.None) && IsAllowed(InputClass.Actuator, LogicOp.None), "line off not allowed");
            Check(fails, Default(InputClass.Value) == LogicOp.Above && Default(InputClass.Bool) == LogicOp.Pass && Default(InputClass.ActuatorPos) == LogicOp.Moving, "defaults");
            Check(fails, RoleOfB(LogicOp.Latch) == BRole.Reset && RoleOfB(LogicOp.Count) == BRole.Reset, "LATCH / COUNT B is not reset");
            Check(fails, RoleOfB(LogicOp.Below) == BRole.Than && RoleOfB(LogicOp.Nor) == BRole.Input && RoleOfB(LogicOp.Flip) == BRole.None && RoleOfB(LogicOp.AtOn) == BRole.None, "B roles");
            Check(fails, !BAccepts(LogicOp.Above, InputClass.Bool) && BAccepts(LogicOp.Above, InputClass.Value) && BAccepts(LogicOp.And, InputClass.Value), "B accepts");
            Check(fails, !BAccepts(LogicOp.And, InputClass.Actuator) && !BAccepts(LogicOp.Latch, InputClass.ActuatorPos) && BAccepts(LogicOp.Below, InputClass.ActuatorPos), "B and actuators");
            Check(fails, !BAccepts(LogicOp.Pass, InputClass.Bool), "IS ON takes a B");
            Check(fails, ShowsValue(LogicOp.Above, LogicRef.None) && !ShowsValue(LogicOp.Above, LogicRef.On), "ABOVE value box vs B");
            Check(fails, ShowsValue(LogicOp.OnDelay, LogicRef.None) && ShowsValue(LogicOp.Count, LogicRef.On) && !ShowsValue(LogicOp.And, LogicRef.None) && !ShowsValue(LogicOp.AtOff, LogicRef.None), "value box per op");
            Check(fails, ShowsUnits(LogicOp.Below, true) && !ShowsUnits(LogicOp.Below, false) && !ShowsUnits(LogicOp.Pass, true), "units button");
            Check(fails, IsStateOp(LogicOp.Moving) && IsStateOp(LogicOp.Merged) && !IsStateOp(LogicOp.Pass), "state operators");
            Check(fails, RoleOfB(LogicOp.Merged) == BRole.None && !ShowsValue(LogicOp.Merged, LogicRef.None), "MERGED takes no B and no value");
            Check(fails, Allowed(InputClass.Dock).Count == 7 && Allowed(InputClass.DockPlain).Count == 2 && !IsAllowed(InputClass.DockPlain, LogicOp.Crewed), "dock facts per dock kind");
            Check(fails, IsStateOp(LogicOp.Built) && RoleOfB(LogicOp.Repaired) == BRole.None && !ShowsValue(LogicOp.Charged, LogicRef.None), "new facts are plain state operators");
            first = fails.Count > 0 ? fails[0] : "";
            return fails.Count;
        }

        private static void Check(List<string> fails, bool ok, string what)
        {
            if (!ok) fails.Add(what);
        }
    }
}
