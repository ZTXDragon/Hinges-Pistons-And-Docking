using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;
using Halfling.Scene2D;
using Halfling.Timing;

namespace ZTX.HingesPistonsDocking
{
    internal class RailShipManager : ShipComponent
    {
        private static readonly ConditionalWeakTable<Ship, RailShipManager> s_managers =
            new ConditionalWeakTable<Ship, RailShipManager>();

        private static readonly ID<PartToggleGuiRules> s_modeToggleId = new ID<PartToggleGuiRules>("ztx_rail_mode");
        private static readonly ID<PartToggleGuiRules> s_dwellToggleId = new ID<PartToggleGuiRules>("ztx_rail_dwell");
        private static readonly ID<PartToggleGuiRules> s_target1Id = new ID<PartToggleGuiRules>("ztx_rail_tgt_1");
        private static readonly ID<PartToggleGuiRules> s_target2Id = new ID<PartToggleGuiRules>("ztx_rail_tgt_2");
        private static readonly ID<PartToggleGuiRules> s_speedToggleId = new ID<PartToggleGuiRules>("ztx_rail_speed");
        private static readonly ID<PartToggleGuiRules> s_driveToggleId = new ID<PartToggleGuiRules>("ztx_rail_drive");
        private static readonly ID<PartToggleGuiRules> s_strengthToggleId = new ID<PartToggleGuiRules>("ztx_rail_strength");
        private static readonly ID<PartToggleGuiRules> s_cartIdId = new ID<PartToggleGuiRules>("ztx_rail_cart_id");
        public static ID<PartToggleGuiRules> ModeToggleId => s_modeToggleId;
        public static ID<PartToggleGuiRules> Target1Id => s_target1Id;
        public static ID<PartToggleGuiRules> Target2Id => s_target2Id;
        public static ID<PartToggleGuiRules> DriveToggleId => s_driveToggleId;
        private static readonly ID<PartToggleGuiRules> s_cartName1Id = new ID<PartToggleGuiRules>("ztx_rail_cname_1");
        private static readonly ID<PartToggleGuiRules> s_cartName2Id = new ID<PartToggleGuiRules>("ztx_rail_cname_2");
        private static readonly float[] s_strengthFactors = { 0.5f, 1f, 2f, 4f };
        public const int DRIVE_CART = 0;
        public const int DRIVE_THRUSTERS = 1;
        private const int AUTHORITY_REFRESH_TICKS = 10;
        private static readonly float[] s_speedCells = { 0.5f, 1f, 2f, 3f, 5f, 8f };

        public const int MODE_GOTO = 0;
        public const int MODE_CYCLE = 1;
        public const int MODE_SHUTTLE = 2;
        public const int MODE_FORWARD = 3;
        public const int MODE_BACKWARD = 4;
        private static readonly float[] s_dwellSeconds = { 0f, 2f, 5f, 10f, 20f };
        public static IReadOnlyList<float> SpeedList => s_speedCells;
        public static IReadOnlyList<float> DwellList => s_dwellSeconds;

        private const int SERVICE_INTERVAL_TICKS = 30;
        private const float TICK_SECONDS = 1f / 30f;
        private const float ARRIVE_MAX_S = 0.03f;
        private const float CHAIN_LOOKAHEAD = 4f;
        private const int CHAIN_KEEP_BEHIND = 3;
        private const float BLOCK_RETREAT_STEP = 0.05f;
        private const int BLOCK_HOLD_TICKS = 15;
        private const int PROBE_TICKS = 30;
        private const float PROBE_STEP = 0.05f;
        internal const Halfling.Physics2D.Dynamics.Category RAIL_MASK =
            Halfling.Physics2D.Dynamics.Category.All & ~(Halfling.Physics2D.Dynamics.Category.Cat1 |
            Halfling.Physics2D.Dynamics.Category.Cat2 | Halfling.Physics2D.Dynamics.Category.Cat3 |
            Halfling.Physics2D.Dynamics.Category.Cat4 | Halfling.Physics2D.Dynamics.Category.Cat5);
        private const float TURN_RATE_CHORD = 0.1f;
        private const float AXIS_CHORD = 0.05f;
        private const float REPROJECT_DEAD_BAND = 0.25f;
        private const float MERGE_MAX_ERROR = 0.03f;
        private const float MERGE_MAX_SPEED = 0.05f;
        private const float RELINK_MAX_OFF = 0.6f;
        private const float BREAK_MAX_OFF_PATH = 0.75f;
        private const int MERGE_RETRY_TICKS = 30;
        private const float DIAG_JUMP_CELLS = 0.15f;
        private const float DIAG_TURN_RAD = 0.35f;
        private const int DIAG_JUMP_LOG_CAP = 20;
        private const float DIAG_OVER_TILE = 0.6f;
        private const float TURN_MIN_PER_TICK = 0.07f;
        private const float TURN_PER_CELL = 2.6f;
        private const float CARRY_HOLD_GAP = 0.15f;
        private const float CARRY_MERGE_GAP = 0.25f;
        private const float REPLAN_START_AT_CART = 0.3f;
        private const float HALF_PI = (float)(Math.PI / 2);
        private const float V_EPS = 0.01f;
        private const float FOLLOW_LANE = 0.75f;
        private const float FOLLOW_MIN_ALONG = 0.05f;
        private const float FOLLOW_WINDOW_MIN = 8f;
        private const float FOLLOW_WINDOW_MAX = 40f;
        private const float TURN_SAMPLE = 0.25f;
        private const float CORNER_STEP = 0.25f;
        private const float CORNER_LOOKAHEAD_MAX = 30f;
        private const float CORNER_FORCE_SHARE = 0.85f;
        private const float CHAIN_MARGIN = 1f;
        private const float CURVATURE_WINDOW = 0.25f;
        private const float FACING_BLEND_MAX = 0.7854f;
        private const string STOP_RETARGET = "a new target";
        private const string STOP_DIRECTION = "a new direction";
        private const string STOP_HOME = "GO TO HOME";
        private const string STOP_HOLD = "HOLD";
        private const string STOP_CUTOFF = "a switch closed ahead";
        private const string STOP_BLOCKED = "the lane ahead blocked";
        private static readonly List<Ship> s_loadScratch = new List<Ship>();

        private readonly List<ZtxRailTerminalComponent> _terminals = new List<ZtxRailTerminalComponent>();
        private readonly List<ZtxRailCartComponent> _carts = new List<ZtxRailCartComponent>();
        private static readonly List<Ship> s_assemblyScratch = new List<Ship>();
        private int _tickCounter;
        private int _planAttempts, _planSuccesses;
        private const int REPLAN_TICKS = 10;

        public RailShipManager()
        {
            base.FixedUpdateBucket = 10;
            base.FixedUpdatingEnabled = true;
        }

        #region Static access + registration

        public static RailShipManager GetOrCreate(Ship ship)
        {
            if (ship == null) return null;
            if (s_managers.TryGetValue(ship, out RailShipManager manager)) return manager;
            manager = new RailShipManager();
            RailShipManager captured = manager;
            Ship capturedShip = ship;
            if (ship.Sim != null)
                ship.Sim.EnqueueDeterministic(ship.UniqueID, delegate { capturedShip.Components.Add(captured); });
            else
                ship.Components.Add(manager);
            s_managers.Add(ship, manager);
            return manager;
        }

        public static RailShipManager TryGet(Ship ship)
        {
            if (ship == null) return null;
            s_managers.TryGetValue(ship, out RailShipManager manager);
            return manager;
        }

        public IReadOnlyList<ZtxRailTerminalComponent> Terminals => _terminals;

        public bool HasPairedCarts
        {
            get
            {
                for (int i = 0; i < _terminals.Count; i++)
                    if (_terminals[i]?.Carts.Count > 0) return true;
                return false;
            }
        }

        public void RegisterTerminal(ZtxRailTerminalComponent terminal)
        {
            if (!_terminals.Contains(terminal)) _terminals.Add(terminal);
        }

        public void DeregisterTerminal(ZtxRailTerminalComponent terminal)
        {
            _terminals.Remove(terminal);
        }

        public void RegisterCart(ZtxRailCartComponent cart)
        {
            if (!_carts.Contains(cart)) _carts.Add(cart);
        }

        public void DeregisterCart(ZtxRailCartComponent cart)
        {
            _carts.Remove(cart);
        }

        public static ZtxRailTerminalComponent TerminalFor(Part cartPart)
        {
            ZtxRailCartComponent cart = ZtxRailCartComponent.ForPart(cartPart);
            if (cart?.PairedTerminal != null) return cart.PairedTerminal;
            Ship ship = cartPart?.Ship;
            RailShipManager m = TryGet(ship);
            if (m == null) return null;
            Part touched = ship.Parts[cartPart.Location + CartFacing(cartPart), PartRectType.Physical];
            foreach (ZtxRailTerminalComponent t in m._terminals)
                if (t.Network.Contains(touched)) return t;
            return null;
        }

        public static string TargetName(Part cartPart)
        {
            Part global = GlobalSource(cartPart);
            if (global != null) return TerminalTarget(global);
            int n1 = LogicSource.Mode(cartPart, s_target1Id, 0);
            int n2 = LogicSource.Mode(cartPart, s_target2Id, 0);
            return (LogicRuleDef.UnpackName(n1) + LogicRuleDef.UnpackName(n2)).TrimEnd();
        }

        public static float DwellSeconds(Part cartPart) => DwellOf(GlobalSource(cartPart) ?? cartPart);

        public static float DwellOf(Part p)
        {
            return RailCartRules.DecodeDwell(Meta(p, RailToggles.DwellValueId, 0), Meta(p, s_dwellToggleId, 1));
        }

        public static float CartGap(Part terminalPart) => RailFollow.DecodeGap(Meta(terminalPart, RailToggles.GapValueId, 0));

        public static bool ShipHoldOn(Part terminalPart) => RailShipHold.Decode(Meta(terminalPart, RailToggles.ShipHoldValueId, 0));

        private float _carrierVxPrev, _carrierVyPrev, _carrierOmegaPrev;
        private int _carrierTick = int.MinValue;
        private float _carrierAx, _carrierAy, _carrierAlpha;

        private void MeasureCarrierMotion()
        {
            Halfling.Physics2D.Dynamics.Body b = base.Ship?.Physics?.Body;
            if (b == null) { _carrierTick = int.MinValue; return; }
            float vx = b.LinearVelocity.X, vy = b.LinearVelocity.Y, w = b.AngularVelocity;
            bool fresh = _carrierTick == _tickCounter - 1;
            _carrierAx = fresh ? (vx - _carrierVxPrev) / TICK_SECONDS : 0f;
            _carrierAy = fresh ? (vy - _carrierVyPrev) / TICK_SECONDS : 0f;
            _carrierAlpha = fresh ? (w - _carrierOmegaPrev) / TICK_SECONDS : 0f;
            _carrierVxPrev = vx;
            _carrierVyPrev = vy;
            _carrierOmegaPrev = w;
            _carrierTick = _tickCounter;
        }

        private float ShipPushAlong(ZtxRailCartComponent cart, RailRoute route)
        {
            Halfling.Physics2D.Dynamics.Body carrier = base.Ship?.Physics?.Body;
            if (carrier == null || !s_turnReady || _carrierTick != _tickCounter) return 0f;
            Vector2 t = carrier.GetWorldVector(route.Tangent(cart.S, AXIS_CHORD));
            float cx = carrier.WorldCenter.X, cy = carrier.WorldCenter.Y, w = carrier.AngularVelocity;
            float sum = 0f;
            foreach (var body in s_turnBodies)
                sum += RailShipHold.PushAlong(body.Mass, body.WorldCenter.X - cx, body.WorldCenter.Y - cy, _carrierAx, _carrierAy, _carrierAlpha, w, t.X, t.Y);
            return sum;
        }

        public static float TypedSpeed(Part cartPart) => SpeedOf(GlobalSource(cartPart) ?? cartPart);

        public static float SpeedOf(Part p)
        {
            return RailCartRules.DecodeSpeed(Meta(p, RailToggles.SpeedValueId, 0), Meta(p, s_speedToggleId, 2));
        }

        public static bool GlobalOn(Part terminalPart) => RailGlobal.Decode(Meta(terminalPart, RailToggles.GlobalId, 0));

        public static int GlobalStored(Part terminalPart) => Meta(terminalPart, RailToggles.GlobalId, 0);

        public static Part GlobalSource(Part cartPart)
        {
            ZtxRailTerminalComponent t = cartPart != null ? TerminalFor(cartPart) : null;
            Part tp = t?.Part;
            return RailGlobal.UseTerminal(tp != null, tp != null ? Meta(tp, RailToggles.GlobalId, 0) : 0) ? tp : null;
        }

        public static int TerminalMode(Part terminalPart) => Meta(terminalPart, s_modeToggleId, MODE_GOTO);

        public static string TerminalTarget(Part terminalPart) =>
            (LogicRuleDef.UnpackName(Meta(terminalPart, s_target1Id, 0)) + LogicRuleDef.UnpackName(Meta(terminalPart, s_target2Id, 0))).TrimEnd();

        public static int TerminalDrive(Part terminalPart) => Meta(terminalPart, s_driveToggleId, DRIVE_CART);

        public static bool TerminalMerge(Part terminalPart) => Meta(terminalPart, RailToggles.MergeId, 0) == 1;

        public static int PowerPercent(Part cartPart) => PowerOf(GlobalSource(cartPart) ?? cartPart);

        public static int PowerOf(Part p)
        {
            return RailCartRules.DecodePower(Meta(p, RailToggles.PowerValueId, 0), Meta(p, s_strengthToggleId, 1));
        }

        private static int Meta(Part p, ID<PartToggleGuiRules> id, int fallback)
        {
            var meta = p?.Ship?.Metadata;
            if (meta == null) return fallback;
            return meta.PartUIToggleStates.Get((new PartSaveData(p), id)) ?? fallback;
        }

        public static int Mode(Part cartPart)
        {
            Part global = GlobalSource(cartPart);
            return global != null ? TerminalMode(global) : LogicSource.Mode(cartPart, s_modeToggleId, MODE_GOTO);
        }

        public static float CartSpeed(Part cartPart, ZtxRailTerminalComponent terminal)
        {
            float v = TypedSpeed(cartPart);
            float limit = terminal != null ? Math.Max(0.05f, terminal.Rules.RailSpeed) : v;
            return Math.Min(v, limit);
        }

        public static float Strength(Part cartPart)
        {
            return PowerPercent(cartPart) / 100f;
        }

        private static float CartAccel(ZtxRailTerminalRules r, ZtxRailCartComponent cart, float mass)
        {
            return RailMotion.DriveAccel(r.AccelCap, Strength(cart.Part));
        }

        public static int DriveMode(Part cartPart)
        {
            Part global = GlobalSource(cartPart);
            return global != null ? TerminalDrive(global) : LogicSource.Mode(cartPart, s_driveToggleId, DRIVE_CART);
        }

        public static int CartId(Part cartPart) => LogicSource.Mode(cartPart, s_cartIdId, 0);

        public static string CartName(Part cartPart) =>
            RailCartRules.UnpackName(LogicSource.Mode(cartPart, s_cartName1Id, 0), LogicSource.Mode(cartPart, s_cartName2Id, 0));

        public static CartIntent Intent(Part cartPart) => RailCartRules.IntentOf(Mode(cartPart), TargetName(cartPart).Length == 0);

        public List<Part> CartsOf(ZtxRailTerminalComponent terminal)
        {
            var list = new List<Part>();
            foreach (ZtxRailCartComponent c in terminal.Carts) if (c.Part != null) list.Add(c.Part);
            Ship ship = base.Ship;
            foreach (ZtxRailCartComponent c in _carts)
            {
                Part cp = c.Part;
                if (c.PairedTerminal != null || cp == null || cp.Ship != ship || list.Contains(cp)) continue;
                if (terminal.Network.Contains(ship.Parts[cp.Location + CartFacing(cp), PartRectType.Physical])) list.Add(cp);
            }
            return list;
        }

        private static void SetToggle(Part part, ID<PartToggleGuiRules> id, int value)
        {
            PartUIToggle t = PartUIToggle.GetOnPart(part, id);
            if (t == null || t.Mode == value) return;
            t.Mode = value;
            t.Part.MatchingBlueprintPart?.OnPartUIToggleStatesChanged();
        }

        #endregion

        public override void FixedUpdate(FixedUpdater fixedUpdater, SceneRoot root)
        {
            try
            {
                HingeJointManager.SyncSolverIterations();
                HingeJointManager.EnsureChainSnap(root);
                AssemblyIndex.Tick(base.Ship);
                if (_terminals.Count == 0)
                {
                    _tickCounter++;
                    return;
                }
                Ship ship = base.Ship;
                MeasureCarrierMotion();
                foreach (ZtxRailTerminalComponent terminal in _terminals.ToArray())
                {
                    Part tp = terminal.Part;
                    if (tp == null || tp.Ship != ship) continue;
                    ApplyRailMasks(terminal);
                    foreach (ZtxRailCartComponent cart in terminal.Carts.ToArray())
                    {
                        if (!HingeJointManager.HasPinWithTag(cart))
                        {
                            cart.ClearPair();
                            continue;
                        }
                        try
                        {
                            CartIntent intent = cart.Part != null ? Intent(cart.Part) : CartIntent.Home;
                            if (intent == CartIntent.Travel)
                            {
                                cart.Returning = false;
                                if (!PlanThrottled(_tickCounter, cart.PlanRetryTick, cart.Phase == RailPhase.Moving))
                                {
                                    _planAttempts = 0;
                                    _planSuccesses = 0;
                                    Plan(terminal, cart);
                                    cart.PlanRetryTick = NextPlanRetry(_planAttempts, _planSuccesses, _tickCounter, REPLAN_TICKS);
                                }
                                Drive(terminal, cart);
                                if (cart.Part != null && cart.Phase == RailPhase.Holding && MergeOn(cart.Part) && Mode(cart.Part) == MODE_GOTO)
                                {
                                    CartRest(cart.Part, out _, out _, out string resting);
                                    if (RailCartRules.MergeWanted(MODE_GOTO, false, true, resting, RailNetwork.Key(TargetName(cart.Part)), false))
                                        TryMergeAway(terminal, cart);
                                }
                                if (cart.Part != null && cart.Phase == RailPhase.Dwelling && MergeOn(cart.Part) && Mode(cart.Part) != MODE_GOTO)
                                {
                                    string dwellAt = StopKey(cart.CurrentTile);
                                    if (RailCartRules.MergeWanted(Mode(cart.Part), false, true, dwellAt, "", true))
                                        TryMergeAway(terminal, cart);
                                }
                            }
                            else if (intent == CartIntent.Hold)
                            {
                                DropStaleRoute(terminal, cart);
                                HoldStep(terminal, cart);
                                if (cart.Part != null && cart.Phase == RailPhase.Holding && !cart.StopAt.HasValue && MergeOn(cart.Part))
                                    TryMergeAway(terminal, cart);
                            }
                            else
                            {
                                DropStaleRoute(terminal, cart);
                                if (!cart.Returning)
                                {
                                    cart.Returning = true;
                                    bool homeward = cart.GoalIsHome && !cart.LegIsTurnRound;
                                    if (cart.Phase == RailPhase.Moving && homeward)
                                    {
                                    }
                                    else if (cart.Phase == RailPhase.Moving && cart.V > V_EPS && !cart.MotionFailed && cart.Route != null)
                                    {
                                        if (!SetGoal(terminal, cart, cart.HomeTile, true, false, null, 0, true, true))
                                            RequestStop(terminal, cart, STOP_HOME);
                                    }
                                    else
                                    {
                                        cart.Phase = RailPhase.Holding;
                                        cart.GoalTile = null; cart.LegIsTurnRound = false;
                                    }
                                }
                                if (cart.Phase != RailPhase.Moving && !cart.AtHome)
                                {
                                    if (cart.HomeTile == null) cart.HomeTile = HomeTileNow(ship, terminal.Network, cart);
                                    if (cart.HomeTile == null)
                                        LogRejectOnce(cart, $"GO TO HOME: no rail tile at home ({cart.HomeCell}) -- holding");
                                    else if (!PlanThrottled(_tickCounter, cart.PlanRetryTick, false))
                                    {
                                        _planAttempts = 0;
                                        _planSuccesses = 0;
                                        SetGoal(terminal, cart, cart.HomeTile, true, false);
                                        cart.PlanRetryTick = NextPlanRetry(_planAttempts, _planSuccesses, _tickCounter, REPLAN_TICKS);
                                    }
                                }
                                Drive(terminal, cart);
                                if (cart.Phase == RailPhase.Holding && cart.AtHome)
                                    TryMerge(terminal, cart);
                            }
                            if (intent != CartIntent.Home) GripService.ServiceCart(terminal, cart, _tickCounter);
                            Ship cartShip = cart.Part?.Ship;
                            if (cart.Route != null && cartShip != null && cartShip != ship && cart.PairedTerminal != null)
                            {
                                Vector2 t = cart.Route.Tangent(cart.S, 0.05f);
                                LaneGuard.Register(cartShip, ship.Physics.Body.GetWorldVector(new System.Numerics.Vector2(t.X, t.Y)), terminal.Rules.LaneTolerance);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Exception("RailShipManager cart " + cart.Part?.Location, ex);
                        }
                    }
                }

                UnmergeEndedDwells();
                _tickCounter++;
                if (_tickCounter % SERVICE_INTERVAL_TICKS != 0) return;
                foreach (ZtxRailTerminalComponent terminal in _terminals.ToArray())
                    Service(terminal);
                AssignCartIds();
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("RailShipManager.FixedUpdate", ex);
            }
        }

        private void Service(ZtxRailTerminalComponent terminal)
        {
            Ship ship = base.Ship;
            Part tp = terminal.Part;
            if (ship == null || tp == null || tp.Ship != ship || ship.Physics?.Body == null) return;

            RailNetwork net = terminal.Network;
            net.Scan(ship, tp);
            LogMapOnChange(net, tp);
            LogCurvesOnChange(net, tp);
            if (net.OtherTerminal != null)
                LogRejectOnce(terminal, $"a second Rail Terminal at {net.OtherTerminal.Location} shares this rail -- it stays dormant");
            else if (terminal.LastRejectReason != null && terminal.LastRejectReason.StartsWith("a second"))
                LogRejectOnce(terminal, null);

            foreach (ZtxRailCartComponent cart in terminal.Carts.ToArray())
                ReprojectOrRelease(terminal, cart);

            int candidates = terminal.Carts.Count;
            foreach (ZtxRailCartComponent cart in _carts.ToArray())
            {
                Part cp = cart.Part;
                if (cart.PairedTerminal != null || cp == null || cp.Ship != ship) continue;
                if (IsMergedAway(cp))
                {
                    Part at = ship.Parts[cp.Location + CartFacing(cp), PartRectType.Physical];
                    if (!net.Contains(at)) continue;
                    candidates++;
                    GripService.ServiceCart(terminal, cart, _tickCounter);
                    string target = RailNetwork.Key(TargetName(cp));
                    bool dwelling = cart.MergedDwellEnd >= 0 && _tickCounter < cart.MergedDwellEnd;
                    if (!RailCartRules.MergeWanted(Mode(cp), target.Length == 0, MergeOn(cp), StopKey(at), target, dwelling))
                        UnmergeAway(terminal, cart, at);
                    continue;
                }
                if (Intent(cp) != CartIntent.Travel) continue;
                Part touched = ship.Parts[cp.Location + CartFacing(cp), PartRectType.Physical];
                if (!net.Contains(touched))
                {
                    if (RailNetwork.IsRailPart(touched)) continue;
                    LogRejectOnce(cart, $"arrow edge touches {(touched == null ? "nothing" : touched.Rules.ID.ToString())} at {cp.Location + CartFacing(cp)} -- turn the part so its arrow touches a rail tile");
                    continue;
                }
                candidates++;
                if (cart.MergedDwellEnd >= 0 && _tickCounter < cart.MergedDwellEnd) continue;
                TrySplitCart(terminal, cart, touched);
            }
            if (candidates == 0 && terminal.Carts.Count == 0 && CartsOf(terminal).Count == 0)
                LogRejectOnce(terminal, $"rail of {net.Tiles.Count - 1} tile(s), {net.Stations.Count} station(s), but no Rail Cart touches it -- build a Rail Cart beside the rail with its arrow on a rail tile");
            else if (terminal.LastRejectReason != null && terminal.LastRejectReason.StartsWith("rail of "))
                LogRejectOnce(terminal, null);

            TryRelink(terminal);
        }

        private void AssignCartIds()
        {
            var seen = new HashSet<int>();
            var all = new List<Part>();
            var needs = new List<Part>();
            foreach (ZtxRailTerminalComponent t in _terminals)
            {
                if (t.Part == null || t.Part.Ship != base.Ship) continue;
                foreach (Part cp in CartsOf(t))
                {
                    if (all.Contains(cp)) continue;
                    all.Add(cp);
                    int id = CartId(cp);
                    if (id == 0 || !seen.Add(id)) needs.Add(cp);
                }
            }
            foreach (Part cp in needs)
            {
                int was = CartId(cp);
                int id = RailCartRules.LowestFreeId(seen);
                SetToggle(cp, s_cartIdId, id);
                seen.Add(id);
                Log.Info($"Rail cart at {cp.Location}: Cart ID {id}" + (was != 0 ? $" (another cart already had {was})." : "."));
            }
        }

        private void TrySplitCart(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart, Part touched)
        {
            Ship ship = base.Ship;
            Part tp = terminal.Part;
            Part cp = cart.Part;

            var component = new HashSet<Part> { cp };
            int railCut = 0, areaCut = 0;
            GripArea area = GripStore.Read(cp);
            if (area.On)
            {
                component.UnionWith(GripService.CartEngageSet(ship, cp, area, out areaCut));
            }
            else
            {
                var queue = new Queue<Part>();
                queue.Enqueue(cp);
                while (queue.Count > 0)
                {
                    Part cur = queue.Dequeue();
                    foreach (Part n in StructuralAdjacency.Neighbours(ship, cur))
                    {
                        if (component.Contains(n)) continue;
                        if (StructuralAdjacency.TouchesRail(ship, n)) { railCut++; continue; }
                        component.Add(n);
                        queue.Enqueue(n);
                    }
                }
                foreach (Part p in component)
                {
                    foreach (Part n in StructuralAdjacency.Neighbours(ship, p, ignoreJointSeams: true, ignoreRailEdges: false))
                    {
                        if (component.Contains(n) || RailNetwork.IsRailPart(n) || StructuralAdjacency.TouchesRail(ship, n)) continue;
                        LogRejectOnce(cart, $"cart touches the hull at {n.Location} -- a cart may touch only rail and what is mounted on the rail (or set its grip area)");
                        return;
                    }
                }
            }

            var farParts = new List<Part>(component);
            Ship cartShip = ShipSplitter.Split(ship, farParts, tp);
            if (cartShip?.Physics?.Body == null)
            {
                Log.Error("Rail cart split failed or the cart has no body -- no weld created.");
                return;
            }

            float currentRel = cartShip.Physics.Body.Rotation - ship.Physics.Body.Rotation;
            Vector2 homeGrid = RailRoute.Center(cp.Location);
            HingeJointManager.CreateRailSlider(cart, ship, cartShip, homeGrid * ship.Scale,
                RailRoute.Center(cp.Location) * cartShip.Scale, currentRel, tp, cp);
            cart.HomeTile = touched;
            cart.HomeCell = cp.Location;
            if (touched != null) cart.HomeStep = touched.Location - cp.Location;
            if (touched != null) WriteCartHome(cp, cp.Location - tp.Location, touched.Location - cp.Location);
            Pair(terminal, cart, cartShip, touched, currentRel);
            Log.Info($"Rail terminal at {tp.Location}: cart at {cp.Location} split off ({farParts.Count} part(s)" +
                (area.On ? $", grip area {area.Width}x{area.Length}, {areaCut} link(s) cut at its edge" : railCut > 0 ? $", {railCut} link(s) to rail-mounted parts cut" : "") +
                $") beside {touched.Location}; rail of {terminal.Network.Tiles.Count} tile(s), {terminal.Network.Stations.Count} station(s).");
        }

        private void Pair(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart, Ship cartShip, Part atTile, float refAngle)
        {
            cart.Route = null;
            cart.S = 0f;
            cart.CurrentTile = atTile;
            cart.CameFrom = null;
            cart.GoalTile = null; cart.LegIsTurnRound = false;
            cart.Phase = RailPhase.Holding;
            cart.DwellLeft = 0f;
            cart.RefBase = refAngle;
            cart.CumBase = 0f;
            cart.Heading = null;
            cart.RefActual = null;
            cart.ArrowConst = null;
            cart.FacingBase = null;
            cart.CutOff = false;
            cart.LegIsTurnRound = false;
            cart.FinalGoal = null;
            cart.DiagLastPoint = null;
            cart.DiagJumpLogs = 0;
            cart.ResetMotion();
            cart.CycleAt = null;
            cart.ShuttleToHome = false;
            cart.MergedDwellEnd = -1;
            cart.DwellTile = null;
            cart.Returning = false;
            cart.PairedTerminal = terminal;
            if (!terminal.Carts.Contains(cart)) terminal.Carts.Add(cart);
            LogRejectOnce(cart, null);
        }

        private static void ApplyRailMasks(ZtxRailTerminalComponent terminal)
        {
            RailNetwork net = terminal.Network;
            if (net == null || net.IsEmpty) return;
            foreach (Part tile in net.Tiles)
                HingeJointManager.ApplyCollidesWithToPart(tile, RAIL_MASK);
        }

        internal int RelinkCartsAtLoad()
        {
            Ship ship = base.Ship;
            if (ship?.Sim == null || ship.Physics?.Body == null || _terminals.Count == 0) return 0;
            int before = 0, after = 0;
            foreach (ZtxRailTerminalComponent t in _terminals) before += t.Carts.Count;
            foreach (ZtxRailTerminalComponent t in _terminals.ToArray())
            {
                Part tp = t.Part;
                if (tp == null || tp.Ship != ship || t.Network == null) continue;
                t.Network.Scan(ship, tp);
                TryRelink(t);
            }
            foreach (ZtxRailTerminalComponent t in _terminals) after += t.Carts.Count;
            return after - before;
        }

        public static bool RelinkNeeded(ulong[] unpairedShipIds, ulong ownShipId)
        {
            if (unpairedShipIds == null) return false;
            foreach (ulong id in unpairedShipIds)
                if (id != ownShipId) return true;
            return false;
        }

        private static Cosmoteer.Simulation.SimRoot s_unpairedSim;
        private static int s_unpairedTick = int.MinValue;
        private static ulong[] s_unpairedShips = new ulong[0];

        public static void ResetStatics()
        {
            s_unpairedSim = null;
            s_unpairedTick = int.MinValue;
            s_unpairedShips = new ulong[0];
            s_loadScratch.Clear();
            s_assemblyScratch.Clear();
            s_turnBodies.Clear();
            s_turnCarrier = null;
        }

        private static ulong[] UnpairedCartShips(Cosmoteer.Simulation.SimRoot sim)
        {
            if (ReferenceEquals(sim, s_unpairedSim) && sim.Tick == s_unpairedTick) return s_unpairedShips;
            var ids = new List<ulong>();
            foreach (Ship s in sim.Ships)
            {
                RailShipManager rm = TryGet(s);
                if (rm == null) continue;
                foreach (ZtxRailCartComponent c in rm._carts)
                    if (c?.PairedTerminal == null && c?.Part != null && ReferenceEquals(c.Part.Ship, s)) { ids.Add(AssemblyTopology.Id(s)); break; }
            }
            s_unpairedSim = sim;
            s_unpairedTick = sim.Tick;
            s_unpairedShips = ids.ToArray();
            return s_unpairedShips;
        }

        private void TryRelink(ZtxRailTerminalComponent terminal)
        {
            Ship ship = base.Ship;
            RailNetwork net = terminal.Network;
            Part tp = terminal.Part;
            if (ship?.Sim == null || net.IsEmpty) return;
            if (!RelinkNeeded(UnpairedCartShips(ship.Sim), AssemblyTopology.Id(ship))) return;
            var probed = new HashSet<Ship>();
            foreach (Part tile in net.Tiles)
            {
                foreach (IntVector2 d in s_dirs)
                {
                    IntVector2 cell = tile.Location + d;
                    if (RailGrid.Blocked(ship, cell)) continue;
                    Vector2 probeWorld = ship.Physics.Body.GetWorldPoint(RailRoute.Center(cell) * ship.Scale);
                    Ship other = FindShipAtWorldPoint(ship, probeWorld);
                    if (other?.Physics?.Body == null || other == ship) continue;
                    Part cp = PartAtWorldPoint(other, probeWorld);
                    ZtxRailCartComponent cart = ZtxRailCartComponent.ForPart(cp);
                    if (cart == null || cart.PairedTerminal != null) continue;
                    IntVector2 f = CartFacing(cp);
                    Vector2 arrowWorld = other.Physics.Body.GetWorldVector(new Vector2(f.X, f.Y));
                    Vector2 toTile = new Vector2(-d.X, -d.Y);
                    Vector2 toTileWorld = ship.Physics.Body.GetWorldVector(toTile);
                    if (arrowWorld.X * toTileWorld.X + arrowWorld.Y * toTileWorld.Y < 0.7f)
                    {
                        LogRejectOnce(cart, $"cart on ship {other.UniqueID} sits beside the rail at {cell} but its arrow does not face the rail");
                        continue;
                    }
                    Vector2 cartWorld = other.Physics.Body.GetWorldPoint(RailRoute.Center(cp.Location) * other.Scale);
                    Vector2 cartGrid = ship.Physics.Body.GetLocalPoint(cartWorld) / ship.Scale;
                    if (RailRoute.Dist(cartGrid, RailRoute.Center(cell)) > RELINK_MAX_OFF) continue;
                    float currentRel = other.Physics.Body.Rotation - ship.Physics.Body.Rotation;
                    HingeJointManager.CreateRailSlider(cart, ship, other, cartGrid * ship.Scale,
                        RailRoute.Center(cp.Location) * other.Scale, currentRel, tp, cp);
                    Part homeTile = ship.Parts[cp.Location + f, PartRectType.Physical];
                    cart.HomeTile = net.Contains(homeTile) ? homeTile : tile;
                    cart.HomeCell = cp.Location;
                    cart.HomeStep = cart.HomeTile.Location - cp.Location;
                    if (TryReadCartHome(cp, out IntVector2 fromTerminal, out IntVector2 step))
                    {
                        Part saved = ship.Parts[tp.Location + fromTerminal + step, PartRectType.Physical];
                        if (net.Contains(saved))
                        {
                            cart.HomeCell = tp.Location + fromTerminal;
                            cart.HomeStep = step;
                            cart.HomeTile = saved;
                        }
                    }
                    Pair(terminal, cart, other, tile, currentRel);
                    Log.Info($"Rail terminal at {tp.Location}: relinked cart on ship {other.UniqueID} beside {tile.Location} (home {cart.HomeTile.Location}).");
                }
            }
        }

        private static readonly IntVector2[] s_dirs =
        {
            new IntVector2(0, -1), new IntVector2(1, 0), new IntVector2(0, 1), new IntVector2(-1, 0),
        };

        private void ReprojectOrRelease(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart)
        {
            if (!HingeJointManager.HasPinWithTag(cart)) return;
            Ship ship = base.Ship;
            Part cp = cart.Part;
            Ship cs = cp?.Ship;
            if (cs?.Physics?.Body == null || cs == ship) return;
            RailNetwork net = terminal.Network;
            if (net.IsEmpty)
            {
                LogRejectOnce(cart, "the rail is gone -- holding where it is until the rail is rebuilt");
                cart.Route = null;
                cart.Phase = RailPhase.Holding;
                cart.GoalTile = null; cart.LegIsTurnRound = false;
                return;
            }
            Vector2 grid = CartGrid(cart);
            Part near = net.NearestTile(grid, out float dist);
            if (near == null || dist > 1f + terminal.Rules.RailClearance + BREAK_MAX_OFF_PATH)
            {
                LogRejectOnce(cart, $"the rail broke under the cart ({dist:F2} cells to the nearest tile) -- holding where it is; rebuild the rail and it carries on");
                cart.Route = null;
                cart.Phase = RailPhase.Holding;
                cart.GoalTile = null; cart.LegIsTurnRound = false;
                return;
            }
            if (cart.CurrentTile != null && !net.Contains(cart.CurrentTile)) cart.CurrentTile = near;
            if (cart.HomeTile != null && !net.Contains(cart.HomeTile)) cart.HomeTile = HomeTileNow(terminal.Part?.Ship, net, cart);
            if (cart.Route != null)
            {
                bool intact = true;
                int cutAt = -1;
                bool blocked = false;
                int ahead = cart.Route.TileIndexAt(cart.S);
                for (int ti = 0; ti < cart.Route.Tiles.Count; ti++)
                {
                    Part t = cart.Route.Tiles[ti];
                    if (!net.Contains(t)) { intact = false; break; }
                    if (ti > ahead && t != cart.GoalTile && !RailNetwork.IsOpen(t))
                    {
                        intact = false;
                        cutAt = ti;
                        if (!cart.CutOff) Log.Info($"Rail cart at {cart.Part?.Location}: the switch at {t.Location} on its way was turned off -- stopping short of it.");
                        cart.CutOff = true;
                        break;
                    }
                }
                if (intact && !RailLanes.PathFree(Lanes(net), cart.Route.Tiles, cart.Route.Side, cart.Route.TileIndexAt(cart.S) + 1))
                {
                    intact = false;
                    blocked = true;
                    Log.Info($"Rail cart at {cart.Part?.Location}: its lane ahead is blocked -- re-planning.");
                }
                if (!intact && (cutAt >= 0 || blocked) && cart.Phase == RailPhase.Moving && cart.V > V_EPS && !cart.MotionFailed)
                {
                    int stopIdx = cutAt >= 0 ? cutAt : Math.Min(cart.Route.Tiles.Count - 1, ahead + 1);
                    bool onBlockedCurve = blocked && ahead + 1 < cart.Route.Tiles.Count &&
                        net.ArcBetween(cart.Route.Tiles[ahead].Location, cart.Route.Tiles[ahead + 1].Location) != null;
                    RequestStop(terminal, cart, cutAt >= 0 ? STOP_CUTOFF : STOP_BLOCKED,
                        onBlockedCurve ? (float?)null : cart.Route.TileS(stopIdx) - terminal.Rules.CutOffMargin);
                    return;
                }
                if (!intact)
                {
                    cart.Route = null;
                    cart.CurrentTile = near;
                    cart.Phase = RailPhase.Holding;
                    cart.GoalTile = null; cart.LegIsTurnRound = false;
                }
                else
                {
                    float sp = cart.Route.Project(grid, cart.S - 3f, cart.S + 3f, out _);
                    if (Math.Abs(sp - cart.S) > Math.Max(REPROJECT_DEAD_BAND, terminal.Rules.BlockedError)) cart.S = sp;
                }
            }
        }

        private static void Release(ZtxRailCartComponent cart, string why)
        {
            Part cp = cart.Part;
            HingeJointManager.ReleaseByTag(cart);
            cart.ClearPair();
            Log.Info($"Rail cart at {cp?.Location}: {why}.");
        }

        private void DropStaleRoute(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart)
        {
            Part cp = cart.Part;
            if (cp == null || cart.Route == null) return;
            if (ReferenceEquals(cart.Route.PlannedOn, base.Ship) && (terminal.Part == null || cart.Route.TerminalAt == terminal.Part.Location)) return;
            Log.Info($"Rail cart at {cp.Location}: its rail moved with the ship it is built on -- re-planning from where the cart is.");
            cart.Route = null;
            cart.BlendRoute = null;
            cart.CurrentTile = terminal.Network.NearestTile(CartGrid(cart), out _) ?? cart.CurrentTile;
            cart.Phase = RailPhase.Holding;
            cart.GoalTile = null;
            cart.LegIsTurnRound = false;
        }

        private void Plan(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart)
        {
            Part cp = cart.Part;
            if (cp == null) return;
            RailNetwork net = terminal.Network;
            DropStaleRoute(terminal, cart);
            int mode = Mode(cp);
            string targetName = TargetName(cp);
            Part target = net.FindStation(targetName);
            bool targetIsHome = targetName.Length == 0;

            if (cart.Phase == RailPhase.Dwelling)
            {
                cart.DwellLeft -= TICK_SECONDS;
                if (cart.DwellLeft > 0f) return;
                cart.Phase = RailPhase.Holding;
            }

            switch (mode)
            {
                case MODE_CYCLE:
                {
                    if (cart.Phase == RailPhase.Moving && cart.Route != null && !cart.Route.IsEmpty) { TryChain(terminal, cart, mode); return; }
                    Part next = NextStation(net, cart.CycleAt);
                    if (next == null) { LogRejectOnce(cart, "CYCLE: no station on this rail"); return; }
                    if (cart.GoalTile == next && cart.Phase == RailPhase.Holding && cart.CurrentTile == next)
                    {
                        return;
                    }
                    if (cart.CutOff)
                    {
                        if (!SetGoal(terminal, cart, next, false, false, null, 0, true))
                            LogRejectOnce(cart, $"CYCLE: the way to {RailNetwork.DisplayName(next)} is switched off -- holding in place until it opens");
                        return;
                    }
                    Part tryStation = next;
                    var skippedNames = new List<string>();
                    for (int k = 0; k < net.Stations.Count && tryStation != null; k++)
                    {
                        bool atIt = tryStation == cart.CurrentTile && cart.Phase == RailPhase.Holding && net.Stations.Count > 1;
                        if (!atIt && SetGoal(terminal, cart, tryStation, false, false, null, 0, true))
                        {
                            if (k > 0) Log.Info($"Rail cart at {cp.Location}: CYCLE skipped {k} station(s) its lane cannot reach ({string.Join(", ", skippedNames)}) from {cart.CurrentTile?.Location} at ({CartGrid(cart).X:F2}, {CartGrid(cart).Y:F2}), on to {RailNetwork.DisplayName(tryStation)}.");
                            return;
                        }
                        skippedNames.Add(atIt ? RailNetwork.DisplayName(tryStation) + " (here)" : RailNetwork.DisplayName(tryStation));
                        tryStation = NextStation(net, RailNetwork.DisplayName(tryStation));
                        if (tryStation == next) break;
                    }
                    LogRejectOnce(cart, "CYCLE: no other station can be reached in this cart's lane (blocked, or across the rail with no open end to go round) -- holding");
                    return;
                }
                case MODE_FORWARD:
                case MODE_BACKWARD:
                {
                    bool movingNow = cart.Phase == RailPhase.Moving && cart.V > V_EPS && !cart.MotionFailed && cart.Route != null && !cart.Route.IsEmpty;
                    if (RailCartRules.DirectionChangeStops(movingNow, cart.LastDirMode, mode))
                    {
                        if (cart.StopReason != STOP_DIRECTION) RequestStop(terminal, cart, STOP_DIRECTION);
                        return;
                    }
                    if (cart.Phase == RailPhase.Moving && cart.Route != null && !cart.Route.IsEmpty) { TryChain(terminal, cart, mode); return; }
                    List<Part> walk = DirectionalWalk(net, cart, mode, out Part goal, out int walkSide);
                    if (walk == null || goal == null) { _planAttempts++; LogRejectOnce(cart, "no lane onward from here (the cells beside the rail are blocked)"); DiagNoLane(terminal, cart, cart.CurrentTile, CartGrid(cart), "directional walk"); return; }
                    if (SetGoal(terminal, cart, goal, false, false, walk, walkSide)) cart.LastDirMode = mode;
                    return;
                }
                case MODE_SHUTTLE:
                {
                    if (cart.Phase == RailPhase.Moving && cart.Route != null && !cart.Route.IsEmpty) { TryChain(terminal, cart, mode); return; }
                    if (!targetIsHome && target == null)
                    {
                        LogRejectOnce(cart, $"SHUTTLE: no station named {targetName} on this rail");
                        return;
                    }
                    Part goal = cart.ShuttleToHome || targetIsHome ? cart.HomeTile : target;
                    bool goalHome = cart.ShuttleToHome || targetIsHome;
                    if (goal == null) { LogRejectOnce(cart, "SHUTTLE: home tile is gone"); return; }
                    if (cart.Phase == RailPhase.Holding && ReachedGoal(cart, goal, goalHome))
                    {
                        cart.ShuttleToHome = !cart.ShuttleToHome;
                        goal = cart.ShuttleToHome ? cart.HomeTile : target;
                        goalHome = cart.ShuttleToHome;
                        if (goal == null) return;
                    }
                    SetGoal(terminal, cart, goal, goalHome, false);
                    return;
                }
                default:
                {
                    Part goal = targetIsHome ? cart.HomeTile : target;
                    if (!targetIsHome && target == null)
                    {
                        LogRejectOnce(cart, $"GO TO: no station named {targetName} on this rail -- holding");
                        return;
                    }
                    if (goal == null) { LogRejectOnce(cart, "GO TO: home tile is gone -- holding"); return; }
                    Part goingTo = cart.LegIsTurnRound ? cart.FinalGoal : cart.GoalTile;
                    bool goingHome = cart.LegIsTurnRound ? cart.FinalIsHome : cart.GoalIsHome;
                    if (goingTo == goal && goingHome == targetIsHome) return;
                    if (cart.Phase == RailPhase.Moving && cart.V > V_EPS && !cart.MotionFailed && cart.Route != null)
                    {
                        if (cart.StopReason != STOP_RETARGET && SetGoal(terminal, cart, goal, targetIsHome, false, null, 0, true, true)) return;
                        if (cart.StopReason != STOP_RETARGET) RequestStop(terminal, cart, STOP_RETARGET);
                        return;
                    }
                    SetGoal(terminal, cart, goal, targetIsHome, false);
                    return;
                }
            }
        }

        private static Part NextStation(RailNetwork net, string after)
        {
            if (net.Stations.Count == 0) return null;
            if (string.IsNullOrEmpty(after)) return net.Stations[0];
            string key = RailNetwork.Key(after);
            for (int i = 0; i < net.Stations.Count; i++)
                if (RailNetwork.Key(RailNetwork.DisplayName(net.Stations[i])) == key)
                    return net.Stations[(i + 1) % net.Stations.Count];
            return net.Stations[0];
        }

        private static bool ReachedGoal(ZtxRailCartComponent cart, Part goal, bool goalHome)
        {
            return cart.GoalTile == goal && cart.GoalIsHome == goalHome && cart.Phase == RailPhase.Holding && cart.CurrentTile == goal;
        }

        private static bool RestInCurve(RailNetwork net, ZtxRailCartComponent cart, out Part near, out Part far, out Part beyond, out int oldSide)
        {
            near = far = beyond = null;
            oldSide = 0;
            RailRoute r = cart.Route;
            if (r == null || r.IsEmpty) return false;
            int i = r.TileIndexAt(cart.S);
            if (i + 1 >= r.Tiles.Count || net.ArcBetween(r.Tiles[i].Location, r.Tiles[i + 1].Location) == null) return false;
            if (!(cart.S > r.TileS(i) + 0.5f && cart.S < r.TileS(i + 1) - 0.5f) || !net.Contains(r.Tiles[i + 1])) return false;
            near = r.Tiles[i];
            far = r.Tiles[i + 1];
            oldSide = r.Side;
            if (i + 2 < r.Tiles.Count && net.NeighboursOf(far).Contains(r.Tiles[i + 2])) beyond = r.Tiles[i + 2];
            else
                foreach (Part n in net.NeighboursOf(far))
                    if (n != near && net.ArcBetween(far.Location, n.Location) == null) { beyond = n; break; }
            return true;
        }

        private static Part PlanningTile(RailNetwork net, ZtxRailCartComponent cart, Vector2 grid)
        {
            Part from = net.NearestTile(grid, out float fromDist);
            if (from != null && fromDist < DIAG_OVER_TILE)
            {
                Part beside = null;
                float besideDist = float.MaxValue;
                foreach (Part t in net.Tiles)
                {
                    float d = RailRoute.Dist(RailRoute.Center(t.Location), grid);
                    if (d >= DIAG_OVER_TILE && d < besideDist) { beside = t; besideDist = d; }
                }
                if (beside != null) { from = beside; fromDist = besideDist; }
            }
            if (from == null || fromDist > 2.5f) from = cart.CurrentTile;
            if (cart.CurrentTile != null && net.Contains(cart.CurrentTile) && from != cart.CurrentTile &&
                RailRoute.Dist(RailRoute.Center(cart.CurrentTile.Location), grid) <= fromDist + 0.1f)
                from = cart.CurrentTile;
            RailRoute r = cart.Route;
            if (r != null && !r.IsEmpty)
            {
                int i = r.TileIndexAt(cart.S);
                if (i + 1 < r.Tiles.Count && net.ArcBetween(r.Tiles[i].Location, r.Tiles[i + 1].Location) != null &&
                    cart.S > r.TileS(i) + 0.5f && cart.S < r.TileS(i + 1) - 0.5f && net.Contains(r.Tiles[i + 1]))
                {
                    cart.CameFrom = r.Tiles[i];
                    return r.Tiles[i + 1];
                }
            }
            return from != null && net.Contains(from) ? from : null;
        }

        private List<Part> DirectionalWalk(RailNetwork net, ZtxRailCartComponent cart, int mode, out Part goal, out int side)
        {
            goal = null;
            side = 0;
            Vector2 grid = CartGrid(cart);
            Part from = PlanningTile(net, cart, grid);
            if (from == null) return null;
            int want = mode == MODE_FORWARD ? 1 : -1;
            if (cart.Phase != RailPhase.Moving && RestInCurve(net, cart, out Part cNear, out Part cFar, out Part cBeyond, out int cSide) && from == cFar)
            {
                int known = RailCartRules.CurveRestSide(cSide, want, out bool reverse);
                Part cPrev = reverse ? cBeyond : cNear;
                if (cPrev != null)
                {
                    List<Part> curveWalk = RailLanes.Walk(Lanes(net), cFar, cPrev, known, grid, want, true, net.Tiles.Count * 4 + 4, out goal, out side);
                    if (curveWalk != null) return curveWalk;
                }
            }
            Part prev = cart.LastDirMode == mode && cart.CameFrom != null && net.NeighboursOf(from).Contains(cart.CameFrom) ? cart.CameFrom : null;
            int side0 = prev != null ? RailLanes.SideAt(from.Location, net.ArriveDir(prev, from), grid) : 0;
            return RailLanes.Walk(Lanes(net), from, prev, side0, grid, mode == MODE_FORWARD ? 1 : -1, true,
                net.Tiles.Count * 4 + 4, out goal, out side);
        }

        private readonly Dictionary<Part, int> _mapHash = new Dictionary<Part, int>();
        private readonly Dictionary<Part, string> _curveLog = new Dictionary<Part, string>();

        private void LogCurvesOnChange(RailNetwork net, Part terminalPart, bool force = false)
        {
            if (!Log.VERBOSE && !force) return;
            RailLanes.View view = Lanes(net);
            var lines = new List<string>();
            foreach (RailArc.Shape a in net.Arcs)
            {
                var blockedCells = new List<string>();
                foreach (IntVector2 c in a.TrackCells) if (!view.Free(c)) blockedCells.Add($"{c.X},{c.Y}");
                foreach (IntVector2 c in a.LaneCells(a.Inside)) if (!view.Free(c) && !a.TrackCells.Contains(c)) blockedCells.Add($"{c.X},{c.Y}");
                lines.Add($"curve rail at {a.A.X},{a.A.Y}: linked to {a.B.X},{a.B.Y} (radius {a.TrackRadius:F1}, straight {a.StraightLength}, {(a.Inside ? "inside" : "outside")})" +
                    (blockedCells.Count > 0 ? $" -- cells blocked: {string.Join(" ", blockedCells)}" : ""));
            }
            foreach (Part t in net.Tiles)
            {
                if (!(ZtxRailTileComponent.ForPart(t)?.IsCurve ?? false)) continue;
                if (net.CurveReasons.TryGetValue(t.Location, out string why)) lines.Add($"curve rail at {t.Location.X},{t.Location.Y}: {why}");
            }
            string text = string.Join("\n  ", lines);
            if (_curveLog.TryGetValue(terminalPart, out string old) && old == text) return;
            _curveLog[terminalPart] = text;
            if (lines.Count > 0) Log.Info($"Rail curves of terminal {terminalPart.Location}:\n  {text}");
        }

        private void LogMapOnChange(RailNetwork net, Part terminalPart, bool force = false)
        {
            if (!Log.VERBOSE && !force) return;
            int hash = net.Tiles.Count;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (Part t in net.Tiles)
            {
                hash = hash * 31 + t.Location.X * 7919 + t.Location.Y + (RailNetwork.IsOpen(t) ? 0 : 1) + t.Rotation * 104729;
                if (t.Location.X < minX) minX = t.Location.X;
                if (t.Location.Y < minY) minY = t.Location.Y;
                if (t.Location.X > maxX) maxX = t.Location.X;
                if (t.Location.Y > maxY) maxY = t.Location.Y;
            }
            if (net.Tiles.Count == 0 || (_mapHash.TryGetValue(terminalPart, out int old) && old == hash)) return;
            _mapHash[terminalPart] = hash;
            RailLanes.View view = Lanes(net);
            Ship ship = base.Ship;
            var sb = new System.Text.StringBuilder();
            sb.Append($"Rail map of terminal {terminalPart.Location} (x {minX - 2}..{maxX + 2}, y {minY - 2}..{maxY + 2}):");
            for (int y = minY - 2; y <= maxY + 2; y++)
            {
                sb.Append('\n').Append(y.ToString().PadLeft(5)).Append(' ');
                for (int x = minX - 2; x <= maxX + 2; x++)
                {
                    var c = new IntVector2(x, y);
                    Part p = view.PartAt(c);
                    char ch;
                    if (p != null)
                    {
                        ZtxRailTileComponent t = ZtxRailTileComponent.ForPart(p);
                        if (t == null) ch = 'T';
                        else if (t.IsStop) ch = 'S';
                        else if (t.IsSwitch) ch = RailNetwork.IsOpen(p) ? 'w' : 'W';
                        else if (t.IsCurve) ch = ')';
                        else ch = 'r';

                    }
                    else if (view.Free(c)) ch = '.';
                    else
                    {
                        Part q = ship?.Parts[c, PartRectType.Physical];
                        ch = q != null && q.Rules.ID.ToString().Contains("frame") ? 'f' : 'x';
                    }
                    sb.Append(ch);
                }
            }
            Log.Info(sb.ToString());
            var ub = new System.Text.StringBuilder();
            ub.Append($"Rail sides of terminal {terminalPart.Location} (same frame):");
            for (int y = minY - 2; y <= maxY + 2; y++)
            {
                ub.Append('\n').Append(y.ToString().PadLeft(5)).Append(' ');
                for (int x = minX - 2; x <= maxX + 2; x++)
                {
                    var c = new IntVector2(x, y);
                    Part p = view.PartAt(c);
                    if (p == null) { ub.Append('.'); continue; }
                    if (ZtxRailTerminalComponent.ForPart(p) != null) { ub.Append('T'); continue; }
                    RailSides.Kind kind = RailSides.KindOf(RailGrid.NeighbourDirs(ship, c));
                    IntVector2 up = RailGrid.UpOf(ship, c);
                    int ui = up.Y < 0 ? 0 : up.X > 0 ? 1 : up.Y > 0 ? 2 : 3;
                    ub.Append(kind == RailSides.Kind.Corner ? 'c' : kind == RailSides.Kind.Crossing ? "NESW"[ui] : "^>v<"[ui]);
                }
            }
            Log.Info(ub.ToString());
            LogUnlinked(net, view);
        }

        private void LogUnlinked(RailNetwork net, RailLanes.View view)
        {
            var bad = new List<string>();
            foreach (Part t in net.Tiles)
            {
                if (ZtxRailTerminalComponent.ForPart(t) != null) continue;
                List<Part> nbs = net.NeighboursOf(t);
                if (nbs.Count == 0) continue;
                bool linked = false;
                foreach (Part n in nbs)
                {
                    IntVector2 lv = net.LeaveDir(t, n), ar = net.ArriveDir(t, n);
                    foreach (int s in new[] { 1, -1 })
                    {
                        if (view.LegOk(t.Location, lv, RailLanes.Normal(lv, s)) && view.LegOk(n.Location, ar, RailLanes.Normal(ar, s))) { linked = true; break; }
                    }
                    if (linked) break;
                }
                if (!linked) bad.Add($"{t.Location.X},{t.Location.Y}");
            }
            if (bad.Count > 0)
                Log.Info($"Rail map: {bad.Count} rail tile(s) link to none of their neighbours (turned the wrong way or facing the other side -- rotate them): {string.Join("  ", bad.Take(24))}{(bad.Count > 24 ? " ..." : "")}.");
        }

        private RailLanes.View Lanes(RailNetwork net)
        {
            return new RailLanes.View(net, base.Ship);
        }

        private void TryChain(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart, int mode)
        {
            Part cp = cart.Part;
            RailRoute route = cart.Route;
            if (cp == null || route == null || route.IsEmpty || cart.ViaTile != null || cart.GoalTile == null) return;
            if (cart.LegIsTurnRound) return;
            if (cart.StopAt.HasValue) return;
            float lookahead = Math.Max(CHAIN_LOOKAHEAD, RailMotion.StopDistance(cart.V, cart.LoadBrake) + CHAIN_MARGIN);
            if (cart.GoalS - cart.S > lookahead) return;
            if (DwellSeconds(cp) > 0f) return;
            RailNetwork net = terminal.Network;
            Part g = cart.GoalTile;
            int n = route.Tiles.Count;
            if (n < 2 || route.Tiles[n - 1] != g) return;
            Part before = route.Tiles[n - 2];

            List<Part> next = null;
            Part nextGoal = null;
            bool nextHome = false;
            RailLanes.View lanes = Lanes(net);
            switch (mode)
            {
                case MODE_CYCLE:
                {
                    Part cand = NextStation(net, RailNetwork.DisplayName(g));
                    if (cand == null || cand == g) return;
                    next = RailLanes.Route(lanes, g, before, route.Side, default(Vector2), cand, null, out _, out _);
                    if (next == null) return;
                    nextGoal = cand;
                    break;
                }
                case MODE_FORWARD:
                case MODE_BACKWARD:
                    if (cart.LastDirMode != mode) return;
                    next = RailLanes.Walk(lanes, g, before, route.Side, CartGrid(cart), mode == MODE_FORWARD ? 1 : -1, false,
                        net.Tiles.Count * 4 + 4, out nextGoal, out _);
                    break;
                case MODE_SHUTTLE:
                {
                    string targetName = TargetName(cp);
                    if (targetName.Length == 0 || cart.HomeTile == null) return;
                    Part target = net.FindStation(targetName);
                    if (target == null) return;
                    nextHome = !cart.GoalIsHome;
                    nextGoal = nextHome ? cart.HomeTile : target;
                    next = RailLanes.Route(lanes, g, before, route.Side, default(Vector2), nextGoal, null, out _, out _);
                    break;
                }
                default:
                    return;
            }
            if (next == null || nextGoal == null || next.Count < 2 || next[0] != g) return;

            int i = route.TileIndexAt(cart.S);
            int k = Math.Max(0, i - CHAIN_KEEP_BEHIND);
            var tiles = route.Tiles.GetRange(k, n - k);
            tiles.AddRange(next.GetRange(1, next.Count - 1));
            float sTileOld = route.TileS(i);
            float refNow = cart.RefBase + route.Cum(cart.S) - cart.CumBase;
            float diagPathOld = route.PathHeading(cart.S), diagSOld = cart.S;
            Vector2 diagPtOld = route.Point(cart.S);
            route.Rebuild(tiles, k == 0);
            if (route.IsEmpty) return;
            float shift = sTileOld - route.TileS(i - k);
            cart.S = route.Clamp(cart.S - shift);
            cart.RefBase = refNow;
            cart.CumBase = route.Cum(cart.S);

            int viaIdx = n - 1 - k;
            float viaFoot = route.TileS(viaIdx);
            cart.ViaTile = g;
            cart.ViaIsHome = cart.GoalIsHome;
            cart.ViaS = cart.ViaIsHome ? route.Project(RailRoute.Center(cart.HomeCell), viaFoot - 2f, viaFoot + 2f, out _) : viaFoot;
            cart.GoalTile = nextGoal;
            cart.GoalIsHome = nextHome;
            cart.GoalS = nextHome ? route.Project(RailRoute.Center(cart.HomeCell), route.Length - 3f, route.Length, out _) : route.TileS(tiles.Count - 1);
            if (mode == MODE_SHUTTLE) cart.ShuttleToHome = nextHome;
            Log.Info($"Rail cart at {cp.Location}: chained through {(cart.ViaIsHome ? "HOME" : RailNetwork.DisplayName(g))} -> {(nextHome ? "HOME" : RailNetwork.DisplayName(nextGoal))}, {next.Count} tile(s), route now {route.Length:F1} cells (S {cart.S:F1}, shift {shift:F2}).");
            Log.Info($"Rail cart at {cp.Location}: DIAG chain facing: path heading {diagPathOld * 57.29578f:F1} deg at S {diagSOld:F2} (old route) -> {route.PathHeading(cart.S) * 57.29578f:F1} deg at S {cart.S:F2} (new, keepStart {k == 0}, kept {i - k} tile(s) behind), change {RailRoute.WrapAngle(route.PathHeading(cart.S) - diagPathOld) * 57.29578f:F1} deg; the point at S moved {RailRoute.Dist(diagPtOld, route.Point(cart.S)):F3} cells; facing {(cart.RefActual ?? 0f) * 57.29578f:F1} deg; route starts at tile {tiles[0].Location}.");
        }

        private void Pass(ZtxRailCartComponent cart)
        {
            Part via = cart.ViaTile;
            RailRoute route = cart.Route;
            cart.ViaTile = null;
            if (via == null || route == null) return;
            int idx = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < route.Tiles.Count; i++)
            {
                if (route.Tiles[i] != via) continue;
                float d = Math.Abs(route.TileS(i) - cart.ViaS);
                if (d < bestD) { bestD = d; idx = i; }
            }
            if (idx >= 1) cart.CameFrom = route.Tiles[idx - 1];
            cart.CurrentTile = via;
            if (!cart.ViaIsHome) cart.CycleAt = RailNetwork.DisplayName(via);
            Log.Info($"Rail cart at {cart.Part?.Location}: passed {(cart.ViaIsHome ? "HOME" : RailNetwork.DisplayName(via))}.");
        }

        private void DiagNoLane(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart, Part from, Vector2 grid, string what)
        {
            try
            {
                RailNetwork net = terminal.Network;
                RailRoute r = cart.Route;
                string routeText = "no route";
                if (r != null && !r.IsEmpty)
                {
                    int i = r.TileIndexAt(cart.S);
                    bool arc = i + 1 < r.Tiles.Count && net.ArcBetween(r.Tiles[i].Location, r.Tiles[i + 1].Location) != null;
                    routeText = $"route {r.Length:F1} cells, S {cart.S:F2}, tile {i} {r.Tiles[i].Location} (foot {r.TileS(i):F2})" +
                        (i + 1 < r.Tiles.Count ? $" -> {r.Tiles[i + 1].Location} (foot {r.TileS(i + 1):F2}){(arc ? " over a CURVE" : "")}" : "") + $", side {r.Side}";
                }
                float fromDist = from != null ? RailRoute.Dist(RailRoute.Center(from.Location), grid) : -1f;
                LogThrottle.Info($"nolane|{cart.Part?.Location}|{what}", $"Rail cart at {cart.Part?.Location}: DIAG no lane ({what}): cart at ({grid.X:F2}, {grid.Y:F2}), planning tile {from?.Location} {fromDist:F2} cells away, current tile {cart.CurrentTile?.Location}, came from {cart.CameFrom?.Location}, {routeText}, phase {cart.Phase}, stop reason {cart.StopReason ?? "none"}, mode {Mode(cart.Part)}.");
                if (terminal.Part != null)
                {
                    LogMapOnChange(net, terminal.Part, true);
                    LogCurvesOnChange(net, terminal.Part, true);
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("RailShipManager.DiagNoLane", ex);
            }
        }

        public static bool PlanThrottled(int now, int retryTick, bool moving) => !moving && retryTick >= 0 && now < retryTick;

        public static int NextPlanRetry(int attempts, int successes, int now, int interval) =>
            attempts > 0 && successes == 0 ? now + interval : -1;

        private bool SetGoal(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart, Part goal, bool goalHome, bool forward,
            List<Part> walk = null, int walkSide = 0, bool quiet = false, bool keepSpeedOnly = false)
        {
            _planAttempts++;
            bool ok = SetGoalCore(terminal, cart, goal, goalHome, forward, walk, walkSide, quiet, keepSpeedOnly);
            if (ok) _planSuccesses++;
            return ok;
        }

        private bool SetGoalCore(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart, Part goal, bool goalHome, bool forward,
            List<Part> walk, int walkSide, bool quiet, bool keepSpeedOnly)
        {
            RailNetwork net = terminal.Network;
            Part cp = cart.Part;
            if (goal == null || cp == null) return false;
            Vector2 grid = CartGrid(cart);
            Part from = PlanningTile(net, cart, grid);
            if (from == null) return false;
            Part diagWasTile = cart.CurrentTile;
            cart.CurrentTile = from;

            int laneSide = 0;
            bool turnRound = false;
            List<Part> tiles;
            if (walk != null && walk.Count > 0 && walk[0] == from)
            {
                tiles = new List<Part>(walk);
                laneSide = walkSide;
            }
            else
            {
                RailLanes.View lanes = Lanes(net);
                tiles = RailLanes.Route(lanes, from, null, 0, grid, goal, forward ? cart.CameFrom : null, out laneSide, out turnRound);
                if (tiles == null && forward) tiles = RailLanes.Route(lanes, from, null, 0, grid, goal, null, out laneSide, out turnRound);
            }
            if (tiles == null && (walk == null || walk.Count == 0 || walk[0] != from) && cart.Phase != RailPhase.Moving &&
                RestInCurve(net, cart, out Part rNear, out Part rFar, out Part rBeyond, out int rSide) && from == rFar)
            {
                RailLanes.View laneView = Lanes(net);
                tiles = RailLanes.Route(laneView, rFar, rNear, rSide, grid, goal, null, out laneSide, out turnRound);
                if (tiles == null && rBeyond != null)
                    tiles = RailLanes.Route(laneView, rFar, rBeyond, -rSide, grid, goal, null, out laneSide, out turnRound);
                if (tiles != null) Log.Info($"Rail cart at {cp.Location}: at rest inside the curve {rNear.Location} -> {rFar.Location}: planned with its side ({(tiles.Count > 1 && tiles[1] == rNear ? "back through the curve" : "on round it")}).");
            }
            if (tiles == null && (walk == null || walk.Count == 0 || walk[0] != from))
            {
                RailLanes.View lanes = Lanes(net);
                float reach = 1f + terminal.Rules.RailClearance + 0.6f;
                var alts = new List<Part>();
                foreach (Part t in net.Tiles)
                    if (t != from && RailRoute.Dist(RailRoute.Center(t.Location), grid) <= reach) alts.Add(t);
                alts.Sort((x, y) =>
                {
                    int c = RailRoute.Dist(RailRoute.Center(x.Location), grid).CompareTo(RailRoute.Dist(RailRoute.Center(y.Location), grid));
                    if (c != 0) return c;
                    return x.Location.Y != y.Location.Y ? x.Location.Y.CompareTo(y.Location.Y) : x.Location.X.CompareTo(y.Location.X);
                });
                foreach (Part alt in alts)
                {
                    tiles = RailLanes.Route(lanes, alt, null, 0, grid, goal, forward ? cart.CameFrom : null, out laneSide, out turnRound);
                    if (tiles == null && forward) tiles = RailLanes.Route(lanes, alt, null, 0, grid, goal, null, out laneSide, out turnRound);
                    if (tiles == null) continue;
                    Log.Info($"Rail cart at {cp.Location}: no lane from {from.Location}; planning from {alt.Location} beside it instead.");
                    from = alt;
                    cart.CurrentTile = alt;
                    break;
                }
            }
            if (tiles == null && keepSpeedOnly)
            {
                cart.CurrentTile = diagWasTile;
                return false;
            }
            if (tiles == null)
            {
                if (!quiet)
                {
                    LogRejectOnce(cart, $"no lane from {from.Location} to {(goalHome ? "HOME" : RailNetwork.DisplayName(goal))} on this side of the rail (blocked, a switch closed, or across the rail with no open end to go round) -- holding");
                    DiagNoLane(terminal, cart, from, grid, "route to " + (goalHome ? "HOME" : RailNetwork.DisplayName(goal)));
                }
                cart.Phase = RailPhase.Holding;
                cart.GoalTile = null; cart.LegIsTurnRound = false;
                return false;
            }
            Part behind = null;
            float bestT = 0f;
            foreach (Part n in net.NeighboursOf(from))
            {
                if (net.ArcBetween(from.Location, n.Location) != null) continue;
                if (tiles.Count >= 2 && n == tiles[1]) continue;
                Vector2 a = RailRoute.Center(n.Location), b = RailRoute.Center(from.Location);
                float abx = b.X - a.X, aby = b.Y - a.Y;
                float t = ((grid.X - a.X) * abx + (grid.Y - a.Y) * aby) / (abx * abx + aby * aby);
                float off = Math.Abs((grid.X - a.X) * aby - (grid.Y - a.Y) * abx) / (float)Math.Sqrt(abx * abx + aby * aby);
                if (t > 0.05f && t < 0.95f && off < 2f && (behind == null || t < bestT)) { behind = n; bestT = t; }
            }
            if (behind != null) tiles.Insert(0, behind);
            Vector2? startPoint = null;
            Vector2? prevDir = null;
            if (tiles.Count == 1)
            {
                Part back = null;
                if (cart.CameFrom != null && net.NeighboursOf(from).Contains(cart.CameFrom)) back = cart.CameFrom;
                else if (net.NeighboursOf(from).Count > 0) back = net.NeighboursOf(from)[0];
                if (back != null) tiles.Insert(0, back);
            }
            bool freshPair = cart.FreshPair;
            cart.FreshPair = false;
            if (tiles[0] == from && tiles.Count >= 2 && RailCartRules.StartBlend(cart.CameFrom != null,
                    cart.CameFrom != null && net.NeighboursOf(from).Contains(cart.CameFrom), cart.CameFrom == tiles[1], freshPair))
            {
                startPoint = grid;
                IntVector2 arr = net.ArriveDir(cart.CameFrom, from);
                prevDir = new Vector2(arr.X, arr.Y);
            }

            int side = cart.Route?.Side ?? 1;
            int diagOldSide = side;
            float diagCross = 0f, diagPath = 0f;
            if (tiles.Count >= 2)
            {
                Vector2 a = RailRoute.Center(tiles[0].Location);
                IntVector2 lv = net.LeaveDir(tiles[0], tiles[1]);
                float dx = lv.X, dy = lv.Y;
                float ox = grid.X - a.X, oy = grid.Y - a.Y;
                float cross = dx * oy - dy * ox;
                if (Math.Abs(cross) > 0.2f) side = cross > 0f ? 1 : -1;
                diagCross = cross;
                diagPath = (float)DetMath.Atan2(dy, dx);
            }
            if (laneSide != 0) side = laneSide;

            float refNow = cart.Route != null ? cart.RefBase + cart.Route.Cum(cart.S) - cart.CumBase : (cart.RefActual ?? cart.RefBase);
            var route = new RailRoute();
            route.CornerLead = terminal.Rules.CornerLead;
            route.ArcBetween = net.ArcBetween;
            float? startHeading = cart.Heading;
            route.CompleteEndTurn = turnRound;
            route.Build(tiles, side, terminal.Rules.RailClearance, startPoint, prevDir, startHeading);
            float s0 = route.Project(grid, out float projDist);
            if (projDist > REPLAN_START_AT_CART)
            {
                route.Build(tiles, side, terminal.Rules.RailClearance, grid, null, startHeading);
                s0 = route.Project(grid, out _);
            }
            if (keepSpeedOnly && cart.Route != null)
            {
                float newGoalS = !turnRound && goalHome ? route.Project(RailRoute.Center(cart.HomeCell), out _) : route.TileS(tiles.Count - 1);
                Vector2 nt = route.Tangent(s0, 0.05f), ot = cart.Route.Tangent(cart.S, 0.05f);
                float ns = newGoalS >= s0 ? 1f : -1f, os = cart.GoalS >= cart.S ? 1f : -1f;
                if (!RailCartRules.SameWay(ot.X * os, ot.Y * os, nt.X * ns, nt.Y * ns))
                {
                    cart.CurrentTile = diagWasTile;
                    return false;
                }
            }
            route.AnchorStart(s0, startHeading);
            route.PlannedOn = base.Ship;
            route.TerminalAt = terminal.Part?.Location ?? default;
            cart.Route = route;
            cart.EmergencyLogged = null;
            cart.S = s0;
            cart.RefBase = refNow;
            cart.CumBase = route.Cum(s0);
            cart.ViaTile = null;
            cart.CutOff = false;
            cart.StopAt = null;
            cart.StopReason = null;
            cart.LegIsTurnRound = turnRound;
            cart.FinalGoal = goal;
            cart.FinalIsHome = goalHome;
            if (turnRound)
            {
                cart.GoalTile = tiles[tiles.Count - 1];
                cart.GoalIsHome = false;
                cart.GoalS = route.TileS(tiles.Count - 1);
                Log.Info($"Rail cart at {cp.Location}: the way to {(goalHome ? "HOME" : RailNetwork.DisplayName(goal))} turns round -- first to {cart.GoalTile.Location}, then back.");
            }
            else
            {
                cart.GoalTile = goal;
                cart.GoalIsHome = goalHome;
                cart.GoalS = goalHome ? route.Project(RailRoute.Center(cart.HomeCell), out _) : route.TileS(tiles.Count - 1);
            }
            cart.Phase = RailPhase.Moving;
            LogRejectOnce(cart, null);
            Log.Info($"Rail cart at {cp.Location}: route {from.Location} -> {(goalHome ? "HOME" : RailNetwork.DisplayName(goal))}, {tiles.Count} tile(s), {route.Length:F1} cells, side {side}.");
            LogPlanDiag(net, cart, grid, diagWasTile, from, behind, tiles, diagCross, diagOldSide, side, startHeading, diagPath);
            return true;
        }

        private bool CarriedCaught(ZtxRailCartComponent cart, RailRoute route, float s, bool forward, float scale, out string why)
        {
            why = null;
            Halfling.Physics2D.Dynamics.Body body = cart.Part?.Ship?.Physics?.Body;
            Halfling.Physics2D.Dynamics.Body carrier = base.Ship?.Physics?.Body;
            if (body == null || carrier == null) return false;
            Vector2 a = route.Point(s), b = route.Point(route.Clamp(s + (forward ? 0.25f : -0.25f)));
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            System.Numerics.Vector2 travel = len < 1e-4f ? new System.Numerics.Vector2(0f, 0f)
                : carrier.GetWorldVector(new System.Numerics.Vector2(dx / len, dy / len));
            HingeJointManager.CarriedState(body, travel, out float gap, out bool resisted);
            if (gap > CARRY_HOLD_GAP * scale)
            {
                why = "a pin on what the cart carries is pulling apart -- waiting";
                if (cart.LastRejectReason != why)
                    Log.Info($"Rail cart at {cart.Part?.Location}: carried pin gap {gap / scale:F2} cells at S {s:F2}, tile {route.Tiles[route.TileIndexAt(s)].Location}.");
                return true;
            }
            if (resisted)
            {
                why = "what the cart carries touches something ahead -- waiting";
                return true;
            }
            return false;
        }

        private static void LogPlanDiag(RailNetwork net, ZtxRailCartComponent cart, Vector2 grid, Part wasTile, Part from, Part behind,
            List<Part> tiles, float cross, int oldSide, int side, float? startHeading, float pathHeading)
        {
            Part near = net.NearestTile(grid, out float nearDist);
            string over = nearDist < DIAG_OVER_TILE ? $" -- the cart stands OVER rail tile {near?.Location}" : "";
            string sideText = tiles.Count < 2 ? $"{side} (one tile)"
                : Math.Abs(cross) > 0.2f ? $"{oldSide} -> {side}{(oldSide != side ? " CHANGED" : "")}"
                : $"{side} KEPT from the last route (cross {cross:F2} too small to tell)";
            string heading = "unknown";
            if (startHeading.HasValue && tiles.Count >= 2)
            {
                float off = RailRoute.WrapAngle(startHeading.Value - pathHeading);
                heading = $"{Deg(startHeading.Value):F0} deg vs path {Deg(pathHeading):F0} deg (off {Deg(off):F0}{(Math.Abs(off) > 0.75f * Math.PI ? ": REVERSAL, drives backwards" : Math.Abs(off) > 0.35f ? ": turns on the spot over the first cell" : "")})";
            }
            string first = tiles.Count >= 2 ? $"{tiles[0].Location} -> {tiles[1].Location}" : $"{tiles[0].Location}";
            Log.Info($"Rail cart at {cart.Part?.Location}: DIAG plan: cart at ({grid.X:F2}, {grid.Y:F2}), was at {wasTile?.Location}, plans from {from.Location}{over}; nearest tile {near?.Location} at {nearDist:F2}; behind {behind?.Location}; first step {first}; side {sideText}; heading {heading}.");
        }

        private static float Deg(float rad)
        {
            return rad * 180f / (float)Math.PI;
        }

        private static void DiagJump(ZtxRailCartComponent cart, RailRoute route, Vector2 anchor, float refAngle, float step)
        {
            if (cart.DiagLastPoint.HasValue && cart.DiagJumpLogs < DIAG_JUMP_LOG_CAP)
            {
                float d = RailRoute.Dist(anchor, cart.DiagLastPoint.Value);
                float turn = Math.Abs(refAngle - cart.DiagLastRef);
                if (d > step + DIAG_JUMP_CELLS || turn > DIAG_TURN_RAD)
                {
                    cart.DiagJumpLogs++;
                    int ti = route.TileIndexAt(cart.S);
                    Log.Info($"Rail cart at {cart.Part?.Location}: DIAG JUMP {d:F2} cells, turn {Deg(turn):F0} deg in one tick at S {cart.S:F2} of {route.Length:F1}, tile {route.Tiles[ti].Location} ({ti + 1} of {route.Tiles.Count}), side {route.Side}, phase {cart.Phase}{(cart.DiagJumpLogs == DIAG_JUMP_LOG_CAP ? " (last jump line for this cart)" : "")}.");
                }
            }
            cart.DiagLastPoint = anchor;
            cart.DiagLastRef = refAngle;
        }

        private void Drive(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart)
        {
            Ship ship = base.Ship;
            RailRoute route = cart.Route;
            if (ship?.Physics?.Body == null || route == null || route.IsEmpty)
            {
                if (cart.Phase == RailPhase.Moving && cart.GoalTile != null) Arrive(cart);
                cart.V = 0f;
                HingeJointManager.ReassertFilter(cart);
                return;
            }
            float scale = ShipScale();
            float movedCells = 0f;
            RefreshLoad(terminal, cart);
            if (cart.Phase != RailPhase.Moving) cart.LegThrust = null;
            if (cart.Phase != RailPhase.Moving) { cart.TurnForce = 0f; cart.PlanAccS = 0f; cart.TurnK = 1f; cart.AddedPrev = float.NaN; cart.TurnDeltaV = 0f; }
            if (cart.Phase != RailPhase.Moving) cart.CornerLogged = false;
            bool wasMoving = cart.Phase == RailPhase.Moving;
            if (cart.Phase == RailPhase.Moving && UseMotor(terminal, cart))
            {
                movedCells = MotorStep(terminal, cart, route, scale);
            }
            else if (cart.Phase == RailPhase.Moving)
            {
                float step = CartSpeed(cart.Part, terminal) * TICK_SECONDS;
                float delta = cart.GoalS - cart.S;
                float move = delta < -step ? -step : (delta > step ? step : delta);
                if (!cart.MotionFailed)
                {
                    try
                    {
                        move = MotionStep(terminal, cart, route);
                        cart.SpeedS = move / TICK_SECONDS;
                    }
                    catch (Exception ex)
                    {
                        cart.MotionFailed = true;
                        Log.Exception($"RailShipManager.MotionStep (cart at {cart.Part?.Location}; constant speed for this pairing)", ex);
                    }
                }
                float sNew = route.Clamp(cart.S + move);
                if (move != 0f)
                {
                    if (HingeJointManager.TryGetWeldState(cart, out float error, out _) &&
                        error > terminal.Rules.BlockedError * scale)
                    {
                        LogRejectOnce(cart, $"cart is blocked (anchor lag {error / scale:F2} cells) -- waiting");
                        sNew = cart.S;
                        cart.V = 0f;
                    }
                    else if (CarriedCaught(cart, route, cart.S, move > 0f, scale, out string carriedWhy))
                    {
                        LogRejectOnce(cart, carriedWhy);
                        cart.BlockTicks = BLOCK_HOLD_TICKS;
                        cart.ProbeTicks = PROBE_TICKS;
                        sNew = route.Clamp(cart.S - Math.Sign(move) * BLOCK_RETREAT_STEP);
                        cart.V = 0f;
                    }
                    else if (TouchingAhead(cart, route, cart.S, move > 0f))
                    {
                        LogRejectOnce(cart, "touching something ahead -- waiting");
                        cart.BlockTicks = BLOCK_HOLD_TICKS;
                        cart.ProbeTicks = PROBE_TICKS;
                        sNew = route.Clamp(cart.S - Math.Sign(move) * BLOCK_RETREAT_STEP);
                        cart.V = 0f;
                    }
                    else if (FollowBlocked(cart))
                    {
                        LogRejectOnce(cart, "another cart is in the way -- waiting");
                        sNew = cart.S;
                        cart.V = 0f;
                    }
                    else if (cart.BlockTicks > 0)
                    {
                        cart.BlockTicks--;
                        sNew = cart.S;
                        cart.V = 0f;
                    }
                    else if (cart.ProbeTicks > 0)
                    {
                        cart.ProbeTicks--;
                        float probe = Math.Min(Math.Abs(move), PROBE_STEP);
                        sNew = route.Clamp(cart.S + Math.Sign(move) * probe);
                        cart.V = probe / TICK_SECONDS;
                    }
                    else if (cart.LastRejectReason != null && cart.LastRejectReason.EndsWith("-- waiting"))
                    {
                        LogRejectOnce(cart, null);
                    }
                }
                movedCells = Math.Abs(sNew - cart.S);
                cart.S = sNew;
                if (cart.ViaTile != null && cart.S >= cart.ViaS - ARRIVE_MAX_S) Pass(cart);
                if (cart.StopAt.HasValue && cart.V <= V_EPS)
                {
                    FinishStop(terminal, cart);
                }
                else if (Math.Abs(cart.GoalS - cart.S) <= ARRIVE_MAX_S &&
                    HingeJointManager.TryGetWeldState(cart, out float err2, out _) &&
                    err2 <= terminal.Rules.BlockedError * scale)
                {
                    Arrive(cart);
                }
            }
            route = cart.Route;
            if (route == null || route.IsEmpty)
            {
                HingeJointManager.ReassertFilter(cart);
                return;
            }
            bool diagAngles = HingeJointManager.TryGetRailAngles(cart, out float diagRel, out float diagRefPrev);
            float railFacing = route.PathHeading(cart.S) - route.Side * HALF_PI;
            if (!cart.ArrowConst.HasValue)
            {
                float legacy = cart.RefActualOrLegacy(route);
                cart.ArrowConst = (float)(Math.Round((legacy - railFacing) / HALF_PI) * HALF_PI);
            }
            float refTarget = railFacing + cart.ArrowConst.Value;
            if (cart.RefActual.HasValue)
                refTarget += (float)(Math.Round((cart.RefActual.Value - refTarget) / (2.0 * Math.PI)) * 2.0 * Math.PI);
            if (!ReferenceEquals(cart.BlendRoute, route) || cart.BlendLength != route.Length)
            {
                cart.BlendRoute = route;
                cart.BlendLength = route.Length;
                float gap = cart.RefActual.HasValue ? cart.RefActual.Value - refTarget : 0f;
                if (terminal.Rules.FacingBlendCells > 0f && Math.Abs(gap) > 1e-4f && Math.Abs(gap) <= FACING_BLEND_MAX)
                {
                    cart.FacingBlend = gap;
                    cart.FacingBlendLeft = terminal.Rules.FacingBlendCells;
                }
                else cart.FacingBlendLeft = 0f;
            }
            if (cart.FacingBlendLeft > 0f)
            {
                refTarget += cart.FacingBlend * RailMotion.BlendFactor(cart.FacingBlendLeft, terminal.Rules.FacingBlendCells);
                cart.FacingBlendLeft -= movedCells;
            }
            float refAngle = refTarget;
            if (cart.RefActual.HasValue)
            {
                float maxTurn = TURN_MIN_PER_TICK + TURN_PER_CELL * movedCells;
                float diff = refTarget - cart.RefActual.Value;
                if (diff > maxTurn) refAngle = cart.RefActual.Value + maxTurn;
                else if (diff < -maxTurn) refAngle = cart.RefActual.Value - maxTurn;
            }
            cart.RefActual = refAngle;
            if (diagAngles) DiagPose(cart, route, terminal, diagRel - diagRefPrev, refAngle - (railFacing + cart.ArrowConst.Value), refAngle - diagRel);
            cart.Heading = route.Cum(cart.S);
            Vector2 anchor = route.Point(cart.S);
            DiagJump(cart, route, anchor, refAngle, cart.MotionFailed ? CartSpeed(cart.Part, terminal) * TICK_SECONDS : cart.V * TICK_SECONDS);
            Vector2 axisWorld = ship.Physics.Body.GetWorldVector(route.Tangent(cart.S, AXIS_CHORD));
            HingeJointManager.UpdateRailSlider(cart, anchor * ship.Scale, axisWorld, refAngle);
            if (cart.Phase == RailPhase.Moving && UseMotor(terminal, cart))
            {
                HingeJointManager.SetRailMotor(cart, true, -cart.S * scale, (route.Length - cart.S) * scale,
                    cart.MotorCmd * scale, cart.MotorForceCap);
                HingeJointManager.AlignRailSlider(cart);
                float turnPerCell = TurnPerCell(route, cart.S);
                float sa = route.Clamp(cart.S - TURN_RATE_CHORD), sb = route.Clamp(cart.S + TURN_RATE_CHORD);
                float turnSlope = sb - sa > 1e-4f ? (TurnPerCell(route, sb) - TurnPerCell(route, sa)) / (sb - sa) : 0f;
                float omega = turnPerCell * cart.SpeedS;
                float alpha = turnSlope * cart.SpeedS * cart.SpeedS + turnPerCell * cart.PlanAccS;
                Vector2 worldTan = ship.Physics.Body.GetWorldVector(route.Tangent(cart.S, AXIS_CHORD));
                HingeJointManager.ApplyRailSwing(cart, omega, alpha, worldTan, cart.TurnForce, cart.TurnDeltaV * scale, cart.LoadShips);
            }
            else
            {
                HingeJointManager.SetRailThrusting(cart, false);
                HingeJointManager.SetRailMotor(cart, false, 0f, 0f, 0f, 0f);
            }
            HingeJointManager.ReassertFilter(cart);
            if (Log.VERBOSE && wasMoving && !cart.MotionFailed && _tickCounter % SERVICE_INTERVAL_TICKS == 0)
            {
                HingeJointManager.TryGetWeldState(cart, out float werr, out _);
                Log.Info($"Rail cart at {cart.Part?.Location}: motion {cart.V:F2} / {cart.LastLimit:F2} cells/s, {cart.MotionPhase}, {Math.Abs((cart.StopAt ?? cart.GoalS) - cart.S):F1} cells to {cart.StopReason ?? "the goal"}, weld error {werr / scale:F2} cells, corner inertia x{1f / Math.Max(1e-3f, cart.TurnK):F1}, ship push {cart.ShipPush:F0}{(cart.LegThrust == true ? $", thrust {cart.ThrustFwd:F2} / {cart.ThrustBack:F2}, commanded {cart.ThrustCmd:F2}" : "")}.");
                if (cart.Phase == RailPhase.Moving && (Math.Abs(cart.SpeedS) < 1f || cart.TurnK < 0.5f))
                    LogRailProbe(cart, cart.Route, scale, "1 Hz");
            }
        }

        private void LogRailProbe(ZtxRailCartComponent cart, RailRoute route, float scale, string why)
        {
            Ship cartShip = cart.Part?.Ship;
            var carrier = base.Ship?.Physics?.Body;
            var body = cartShip?.Physics?.Body;
            if (carrier == null || body == null || route == null) return;
            float sc = scale > 0f ? scale : 1f;
            float mass = cart.LoadMass > 0f ? cart.LoadMass : cartShip.Physics.DisplayMass;
            Vector2 axis = carrier.GetWorldVector(route.Tangent(cart.S, AXIS_CHORD));
            Vector2 push = body.GetWorldVector(cart.PushLocal);
            Vector2 vb = body.LinearVelocity, vc = carrier.LinearVelocity;
            Vector2 v = new Vector2(vb.X - vc.X, vb.Y - vc.Y);
            float vAlong = (v.X * axis.X + v.Y * axis.Y) / sc, vAcross = (axis.X * v.Y - axis.Y * v.X) / sc;
            Log.Info($"Rail cart at {cart.Part?.Location}: RAIL PROBE ({why}) S {cart.S:F2} of {route.Length:F2}, path heading {route.PathHeading(cart.S) * 57.29578f:F1} deg, turn {TurnPerCell(route, cart.S):F2} rad/cell, inertia x{1f / Math.Max(1e-3f, cart.TurnK):F1}; push . axis {push.X * axis.X + push.Y * axis.Y:F2}, thrust actual {cart.ThrustActual:F2} commanded {cart.ThrustCmd:F2}, turn force {cart.TurnForce / Math.Max(1e-4f, mass * sc):F2}, speed change {cart.TurnDeltaV:F3}; cart velocity along {vAlong:F2} across {vAcross:F2} cells/s, spin {body.AngularVelocity - carrier.AngularVelocity:F3} rad/s; {HingeJointManager.RailProbe(cart, 1f / TICK_SECONDS, mass, sc)}");
        }

        private void RefreshLoad(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart)
        {
            int version = HingeJointManager.TopologyVersion;
            if (cart.LoadVersion == version && _tickCounter - cart.LoadTick < SERVICE_INTERVAL_TICKS) return;
            cart.LoadVersion = version;
            cart.LoadTick = _tickCounter;
            Part cp = cart.Part;
            Ship cartShip = cp?.Ship;
            Ship host = base.Ship;
            if (cartShip?.Physics?.Body == null || cartShip == host) return;
            HingeJointManager.CartLoadShips(cartShip, host, cart, s_loadScratch, out bool attached);
            float cell = cartShip.Scale.X > 0f ? cartShip.Scale.X : 1f;
            Vector2 centre = cartShip.Physics.Body.GetWorldPoint(RailRoute.Center(cp.Location) * cartShip.Scale);
            float mass = 0f, reach = 0f, comX = 0f, comY = 0f, comM = 0f;
            foreach (Ship s in s_loadScratch)
            {
                mass += s.Physics?.DisplayMass ?? 0f;
                if (s.Physics?.Body != null)
                {
                    float bm = s.Physics.Body.Mass;
                    Vector2 wc = s.Physics.Body.WorldCenter;
                    comX += wc.X * bm;
                    comY += wc.Y * bm;
                    comM += bm;
                }
                IntRect? rect = s.Parts.BoundingRect;
                if (!rect.HasValue || s.Physics?.Body == null) continue;
                IntRect q = rect.Value;
                foreach (Vector2 corner in new[] { new Vector2(q.Left, q.Top), new Vector2(q.Right, q.Top),
                                                   new Vector2(q.Left, q.Bottom), new Vector2(q.Right, q.Bottom) })
                {
                    Vector2 w = s.Physics.Body.GetWorldPoint(corner * s.Scale);
                    reach = Math.Max(reach, RailRoute.Dist(w, centre) / cell);
                }
            }
            if (!(mass > 0f)) mass = cartShip.Physics.DisplayMass;
            cart.LoadMass = mass;
            cart.LoadReach = reach;
            cart.LoadShips.Clear();
            cart.LoadShips.AddRange(s_loadScratch);
            cart.LoadCom = comM > 0f ? RailRoute.Dist(new Vector2(comX / comM, comY / comM), centre) / cell : 0f;
            cart.LoadBodies = s_loadScratch.Count;
            cart.LoadAttached = attached;
            if (cart.LoadLogged) return;
            cart.LoadLogged = true;
            ZtxRailTerminalRules r = terminal.Rules;
            float a = CartAccel(r, cart, mass);
            Log.Info($"Rail cart at {cp.Location}: load {mass:F1} t (cart ship {cartShip.Physics.DisplayMass:F1} t, {s_loadScratch.Count} ship(s)), reach {reach:F2} cells, centre of mass {cart.LoadCom:F2} cells out, attached to the ship: {(attached ? "yes" : "no")}; accel {a:F2}, brake {a * r.BrakeFactor:F2} cells/s^2 (AccelCap {r.AccelCap:F1} x strength {Strength(cp) * 100f:F0}%, any load).");
        }

        private void Footprint(ZtxRailCartComponent cart)
        {
            if (cart.FpTick == _tickCounter) return;
            cart.FpTick = _tickCounter;
            cart.FpFront = cart.FpRear = cart.FpLeft = cart.FpRight = 0f;
            RailRoute route = cart.Route;
            Ship host = base.Ship;
            Halfling.Physics2D.Dynamics.Body carrier = host?.Physics?.Body;
            if (route == null || route.IsEmpty || carrier == null) return;
            Vector2 p0 = route.Point(cart.S), t = route.Tangent(cart.S, AXIS_CHORD);
            float xMax = float.MinValue, xMin = float.MaxValue, yMax = float.MinValue, yMin = float.MaxValue;
            bool any = false;
            int ships = cart.LoadShips.Count;
            for (int i = 0; i <= ships; i++)
            {
                Ship s = i < ships ? cart.LoadShips[i] : (ships == 0 ? cart.Part?.Ship : null);
                if (s?.Physics?.Body == null || ReferenceEquals(s, host)) continue;
                IntRect? rect = s.Parts.BoundingRect;
                if (!rect.HasValue) continue;
                IntRect q = rect.Value;
                for (int c = 0; c < 4; c++)
                {
                    Vector2 corner = new Vector2((c & 1) == 0 ? q.Left : q.Right, (c & 2) == 0 ? q.Top : q.Bottom);
                    Vector2 g = carrier.GetLocalPoint(s.Physics.Body.GetWorldPoint(corner * s.Scale)) / host.Scale;
                    float dx = g.X - p0.X, dy = g.Y - p0.Y;
                    float x = dx * t.X + dy * t.Y, y = -dx * t.Y + dy * t.X;
                    if (x > xMax) xMax = x;
                    if (x < xMin) xMin = x;
                    if (y > yMax) yMax = y;
                    if (y < yMin) yMin = y;
                    any = true;
                }
            }
            if (!any) return;
            cart.FpFront = xMax;
            cart.FpRear = -xMin;
            cart.FpLeft = yMax;
            cart.FpRight = -yMin;
        }

        private static readonly float[] s_turnSamples = new float[256];

        private static void TurnBetween(RailRoute route, float sa, float sb, out float left, out float right)
        {
            float lo = Math.Min(sa, sb), hi = Math.Max(sa, sb);
            int n = Math.Min(s_turnSamples.Length - 1, Math.Max(1, (int)Math.Ceiling((hi - lo) / TURN_SAMPLE)));
            for (int i = 0; i <= n; i++) s_turnSamples[i] = route.PathHeading(lo + (hi - lo) * i / n);
            RailFollow.TurnSums(s_turnSamples, n + 1, out left, out right);
        }

        private float FollowLimit(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart, RailRoute route, float dir, float brake)
        {
            cart.FollowTick = _tickCounter;
            cart.FollowGap = float.MaxValue;
            float setting = CartGap(terminal.Part);
            Footprint(cart);
            float myFront = dir > 0f ? cart.FpFront : cart.FpRear;
            float window = Math.Min(FOLLOW_WINDOW_MAX, FOLLOW_WINDOW_MIN + setting + 2f * cart.LoadReach
                + RailMotion.StopDistance(Math.Max(cart.V, CartSpeed(cart.Part, terminal)), brake));
            float lo = route.Clamp(dir > 0f ? cart.S : cart.S - window), hi = route.Clamp(dir > 0f ? cart.S + window : cart.S);
            if (hi - lo < 1e-3f) return float.MaxValue;
            float best = float.MaxValue;
            ZtxRailCartComponent leader = null;
            float leaderGap = float.MaxValue;
            foreach (ZtxRailTerminalComponent t in _terminals)
            {
                foreach (ZtxRailCartComponent other in t.Carts)
                {
                    if (ReferenceEquals(other, cart) || other.Route == null || other.Route.IsEmpty || !HingeJointManager.HasPinWithTag(other)) continue;
                    float so = route.Project(CartGrid(other), lo, hi, out float lateral);
                    if (!(lateral <= FOLLOW_LANE)) continue;
                    float along = (so - cart.S) * dir;
                    if (along < FOLLOW_MIN_ALONG) continue;
                    Footprint(other);
                    Vector2 tm = route.Tangent(so, AXIS_CHORD), to = other.Route.Tangent(other.S, AXIS_CHORD);
                    float sign = tm.X * to.X + tm.Y * to.Y >= 0f ? 1f : -1f;
                    float near = RailFollow.NearExtent(dir, sign, other.FpFront, other.FpRear);
                    float leftR = Math.Max(cart.FpLeft, sign > 0f ? other.FpLeft : other.FpRight);
                    float rightR = Math.Max(cart.FpRight, sign > 0f ? other.FpRight : other.FpLeft);
                    TurnBetween(route, cart.S, so, out float tl, out float tr);
                    float gap = RailFollow.FootprintGap(along, myFront, near, leftR, rightR, tl, tr);
                    float vLeader = other.Phase == RailPhase.Moving ? other.SpeedS * sign * dir : 0f;
                    float lim = RailFollow.FollowSpeed(vLeader, gap, setting, brake, other.LoadBrake > 0f ? other.LoadBrake : brake, TICK_SECONDS);
                    if (gap < cart.FollowGap) cart.FollowGap = gap;
                    if (lim < best) { best = lim; leader = other; leaderGap = gap; }
                }
            }
            cart.FollowLeader = leader;
            if (leader != null && !ReferenceEquals(leader, cart.FollowLogged))
            {
                cart.FollowLogged = leader;
                Log.Info($"Rail cart at {cart.Part?.Location}: following the cart at {leader.Part?.Location} -- gap {leaderGap:F2} cells between the loads (Cart gap {setting:F1}), footprint {cart.FpFront:F1} ahead / {cart.FpRear:F1} behind / {Math.Max(cart.FpLeft, cart.FpRight):F1} across.");
            }
            return best;
        }

        private void DiagPose(ZtxRailCartComponent cart, RailRoute route, ZtxRailTerminalComponent terminal, float phys, float target, float now)
        {
            float p = RailRoute.WrapAngle(phys) * 57.29578f, t = RailRoute.WrapAngle(target) * 57.29578f, w = RailRoute.WrapAngle(now) * 57.29578f;
            float worst = Math.Max(Math.Abs(p), Math.Abs(t));
            if (!cart.PoseWarned && worst > 15f)
            {
                cart.PoseWarned = true;
                Footprint(cart);
                Log.Info($"Rail cart at {cart.Part?.Location}: DIAG POSE off: physics left it {p:F1} deg from the commanded angle, the command is {t:F1} deg from the rail facing, this tick turns {w:F1} deg; S {cart.S:F2} of {route.Length:F1}, tile {route.Tiles[route.TileIndexAt(cart.S)].Location}, turn {TurnPerCell(route, cart.S):F2} rad/cell, {cart.Phase}, {cart.MotionPhase}, speed {cart.SpeedS:F2}, blend {cart.FacingBlend * 57.29578f:F1} deg over {cart.FacingBlendLeft:F2} cells, footprint {cart.FpFront:F1} / {cart.FpRear:F1} / {cart.FpLeft:F1} / {cart.FpRight:F1}, hold {cart.LastRejectReason ?? "none"}.");
                LogRailProbe(cart, route, ShipScale(), "pose off");
            }
            else if (cart.PoseWarned && worst < 5f)
            {
                cart.PoseWarned = false;
                Log.Info($"Rail cart at {cart.Part?.Location}: DIAG POSE back within 5 deg at S {cart.S:F2}, tile {route.Tiles[route.TileIndexAt(cart.S)].Location}.");
            }
        }

        private bool FollowBlocked(ZtxRailCartComponent cart)
        {
            return cart.FollowTick == _tickCounter && cart.FollowGap <= 0f;
        }

        private float MotionStep(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart, RailRoute route, float? accel = null, float? brake = null)
        {
            ZtxRailTerminalRules r = terminal.Rules;
            float top = CartSpeed(cart.Part, terminal);
            float aGrip = CartAccel(r, cart, cart.LoadMass);
            float a = accel ?? aGrip;
            float b = brake ?? r.BrakeFactor * a;
            float e = r.EmergencyFactor * b;
            cart.LoadAccel = a;
            cart.LoadBrake = b;
            float dir = cart.GoalS >= cart.S ? 1f : -1f;
            float corner = top;
            float soft = Math.Min(top, corner);
            float stopS = cart.StopAt ?? cart.GoalS;
            float dist = Math.Min(Math.Max(0f, (stopS - cart.S) * dir), Math.Abs(cart.GoalS - cart.S));
            float follow = FollowLimit(terminal, cart, route, dir, b);
            float before = cart.V;
            float v = RailMotion.Advance(before, dist, soft, follow, a, b, e, TICK_SECONDS, out float move, out bool dead);
            if (dead && cart.EmergencyLogged != (cart.StopReason ?? "its goal"))
            {
                cart.EmergencyLogged = cart.StopReason ?? "its goal";
                Log.Info($"Rail cart at {cart.Part?.Location}: emergency stop -- {cart.EmergencyLogged} came up {dist:F2} cells ahead at {before:F2} cells/s.");
            }
            cart.V = v;
            float stopLimit = RailMotion.StopSpeed(dist, b, TICK_SECONDS);
            cart.LastLimit = Math.Min(Math.Min(soft, follow), stopLimit);
            if (v > before + 1e-4f) cart.MotionPhase = "accelerating";
            else if (v < before - 1e-4f)
                cart.MotionPhase = stopLimit <= Math.Min(soft, follow) ? "braking for " + (cart.StopReason ?? "the goal")
                    : follow < soft ? "following a cart" : corner < top ? "slowing for a corner" : "slowing";
            else cart.MotionPhase = v <= V_EPS ? "stopped" : "cruising";
            return move * dir;
        }

        private static bool UseMotor(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart)
        {
            return terminal.Rules.MotorDrive && !cart.MotionFailed;
        }

        private static readonly List<Halfling.Physics2D.Dynamics.Body> s_turnBodies = new List<Halfling.Physics2D.Dynamics.Body>();

        private static float TurnPerCell(RailRoute route, float s)
        {
            float sa = route.Clamp(s - TURN_RATE_CHORD), sb = route.Clamp(s + TURN_RATE_CHORD);
            return sb - sa > 1e-4f ? (route.PathHeading(sb) - route.PathHeading(sa)) / (sb - sa) : 0f;
        }

        private void TurnInertia(ZtxRailCartComponent cart, RailRoute route, float scale, out float addedMass)
        {
            addedMass = 0f;
            s_turnReady = false;
            Halfling.Physics2D.Dynamics.Body carrier = base.Ship?.Physics?.Body;
            if (carrier == null || !HingeJointManager.TryGetRailAnchor(cart, out Vector2 anchor)) return;
            s_turnBodies.Clear();
            foreach (Ship sh in cart.LoadShips)
            {
                var body = sh?.Physics?.Body;
                if (body != null && !ReferenceEquals(body, carrier) && !s_turnBodies.Contains(body)) s_turnBodies.Add(body);
            }
            var cb = cart.Part?.Ship?.Physics?.Body;
            if (cb != null && !s_turnBodies.Contains(cb)) s_turnBodies.Add(cb);
            s_turnScale = scale > 0f ? scale : 1f;
            s_turnH0 = route.PathHeading(cart.S);
            s_turnAnchor = anchor;
            s_turnCarrier = carrier;
            s_turnReady = true;
            addedMass = AddedMassAt(route, cart.S);
        }

        private static bool s_turnReady;
        private static float s_turnScale = 1f, s_turnH0;
        private static Vector2 s_turnAnchor;
        private static Halfling.Physics2D.Dynamics.Body s_turnCarrier;

        private static float AddedMassAt(RailRoute route, float s)
        {
            float turn = route.PathHeading(s) - s_turnH0;
            float tp = TurnPerCell(route, s) / s_turnScale;
            Vector2 t = s_turnCarrier.GetWorldVector(route.Tangent(s, AXIS_CHORD));
            float c = (float)DetMath.Cos(turn), sn = (float)DetMath.Sin(turn);
            float sum = 0f;
            foreach (var body in s_turnBodies)
            {
                Vector2 wc = body.WorldCenter;
                float rx0 = wc.X - s_turnAnchor.X, ry0 = wc.Y - s_turnAnchor.Y;
                sum += RailThrustMath.AddedMass(t.X, t.Y, tp, rx0 * c - ry0 * sn, rx0 * sn + ry0 * c, body.Mass, body.IntertiaAboutCentroid);
            }
            return sum;
        }

        private static float CornerLimitAhead(ZtxRailCartComponent cart, RailRoute route, float dir, float force, float brake, float top, out float maxSlope)
        {
            maxSlope = 0f;
            if (!s_turnReady || !(top > 0f)) return float.MaxValue;
            float look = Math.Min(CORNER_LOOKAHEAD_MAX, RailMotion.StopDistance(Math.Max(top, cart.V), brake) + 2f * CORNER_STEP);
            int n = Math.Max(1, (int)Math.Ceiling(look / CORNER_STEP));
            float lim = float.MaxValue;
            float sPrev = cart.S, mPrev = AddedMassAt(route, cart.S);
            for (int j = 0; j < n; j++)
            {
                float sNext = route.Clamp(cart.S + dir * (j + 1) * CORNER_STEP);
                float ds = Math.Abs(sNext - sPrev);
                if (ds < 1e-4f) break;
                float mNext = AddedMassAt(route, sNext);
                float slope = (mNext - mPrev) / ds;
                if (slope > maxSlope) maxSlope = slope;
                float vc = RailFollow.CornerSpeed(force, slope);
                if (vc != float.MaxValue) lim = Math.Min(lim, RailFollow.ApproachSpeed(vc, Math.Abs(sPrev - cart.S), brake, TICK_SECONDS));
                sPrev = sNext;
                mPrev = mNext;
            }
            return lim;
        }

        private static void RefreshAuthority(ZtxRailCartComponent cart, Ship cartShip, Vector2 push, float mass, float scale)
        {
            cart.ThrustFwd = RailThrustDrive.Authority(cartShip, push, mass, scale);
            cart.ThrustBack = RailThrustDrive.Authority(cartShip, new Vector2(-push.X, -push.Y), mass, scale);
        }

        private float MotorStep(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart, RailRoute route, float scale)
        {
            float before = cart.S;
            if (HingeJointManager.TryGetRailMotion(cart, out float translation, out float jointSpeed))
                cart.S = RailThrustMath.IntegrateS(cart.S, translation, scale, route.Length);
            float dir = cart.GoalS >= cart.S ? 1f : -1f;
            float sc = scale > 0f ? scale : 1f;
            float along = dir * jointSpeed / sc;
            cart.SpeedS = jointSpeed / sc;
            cart.V = Math.Max(0f, along);
            ZtxRailTerminalRules r = terminal.Rules;
            Ship cartShip = cart.Part?.Ship;
            Ship ship = base.Ship;
            Vector2 worldDir = new Vector2(0f, 0f), local = new Vector2(0f, 0f);
            if (cartShip?.Physics?.Body != null && ship?.Physics?.Body != null)
            {
                Vector2 t = route.Tangent(cart.S, AXIS_CHORD);
                worldDir = ship.Physics.Body.GetWorldVector(new Vector2(t.X * dir, t.Y * dir));
                local = cartShip.Physics.Body.GetLocalVector(worldDir);
            }
            Vector2 push = local;
            if (cart.Part != null)
            {
                IntVector2 right = OrthogonalRotation.Right.GetShipRelative(cart.Part.Rotation, cart.Part.FlipX).ToVector();
                float sign = local.X * right.X + local.Y * right.Y >= 0f ? 1f : -1f;
                push = new Vector2(right.X * sign, right.Y * sign);
            }
            cart.PushLocal = push;
            float align = Math.Max(0f, push.X * local.X + push.Y * local.Y);
            float fwdOnRail = cart.ThrustFwd * align, backOnRail = cart.ThrustBack * align;
            float mass = cart.LoadMass > 0f ? cart.LoadMass : (cartShip?.Physics?.DisplayMass ?? 0f);
            float bGrip = r.BrakeFactor * CartAccel(r, cart, mass);
            bool wantThrust = DriveMode(cart.Part) == DRIVE_THRUSTERS;
            if (!cart.LegThrust.HasValue || wantThrust != cart.LegWant)
            {
                if (cart.LegThrust.HasValue) Log.Info($"Rail cart at {cart.Part?.Location}: drive switched to {(wantThrust ? "Thrusters" : "Cart")} mid-leg.");
                cart.LegWant = wantThrust;
                if (wantThrust) RefreshAuthority(cart, cartShip, push, mass, sc);
                cart.LegThrust = RailThrustMath.UseThrust(wantThrust, cart.ThrustFwd, r.MinThrustAccel);
                cart.DriveNote = wantThrust && !cart.LegThrust.Value ? "no thrust toward the goal, using cart drive" : "";
                if (wantThrust)
                    Log.Info($"Rail cart at {cart.Part?.Location}: Thrusters mode, thrust {cart.ThrustFwd:F2} forward / {cart.ThrustBack:F2} back cells/s^2 for {mass:F1} t -- this leg runs on {(cart.LegThrust.Value ? "thrusters" : $"cart drive (under {r.MinThrustAccel:F2} toward the goal)")}.");
                cart.ThrustTicks = 0;
                cart.UnderTicks = 0;
            }
            bool thrust = cart.LegThrust.Value;
            TurnInertia(cart, route, sc, out float addedMass);
            float mEff = Math.Max(0.2f * mass, mass + addedMass);
            float k = mass > 0f ? mass / mEff : 1f;
            cart.TurnK = k;
            if (thrust && ++cart.ThrustTicks % AUTHORITY_REFRESH_TICKS == 0) RefreshAuthority(cart, cartShip, push, mass, sc);
            float aCart = CartAccel(r, cart, mass);
            if (!cart.CornerLogged)
            {
                float topNow = CartSpeed(cart.Part, terminal);
                float driveForce1 = CORNER_FORCE_SHARE * mass * Math.Max(0f, (thrust ? fwdOnRail : aCart) - r.RollingResistance);
                float held = CornerLimitAhead(cart, route, dir, driveForce1,
                    thrust ? (backOnRail + bGrip) * k : r.BrakeFactor * aCart * k, topNow, out float swingSlope);
                if (held < topNow)
                {
                    cart.CornerLogged = true;
                    Log.Info($"Rail cart at {cart.Part?.Location}: corner ahead -- its drive ({driveForce1:F0} along the rail) holds full speed through this swing (added mass rising {swingSlope:F0} per cell) only up to {held:F2} cells/s (top {topNow:F2}); faster, the swing takes speed from the load's momentum and gives it back after the corner. More power or a centre of mass closer to the rail keeps more speed.");
                }
            }
            float planMove = thrust ? MotionStep(terminal, cart, route, fwdOnRail * k, (backOnRail + bGrip) * k)
                : MotionStep(terminal, cart, route, aCart * k, r.BrakeFactor * aCart * k);
            float vCmd = RailThrustMath.FinalApproach(cart.V, planMove, TICK_SECONDS);
            bool forward = dir > 0f;
            cart.MotorRetreat = false;
            cart.StallTicks = cart.BlockTicks > 0 ? 0 : RailThrustMath.StallStep(cart.StallTicks, vCmd, along);
            string hold = null;
            if (CarriedCaught(cart, route, cart.S, forward, scale, out string carriedWhy))
            {
                hold = carriedWhy;
                cart.MotorRetreat = true;
            }
            else if (TouchingAhead(cart, route, cart.S, forward))
            {
                hold = "touching something ahead -- waiting";
                cart.MotorRetreat = true;
            }
            else if (RailThrustMath.Stalled(cart.StallTicks))
            {
                hold = $"cart is blocked (planned {vCmd:F2}, moving {along:F2} cells/s) -- waiting";
                if (Log.VERBOSE) LogRailProbe(cart, route, scale, "stalled");
            }
            if (hold != null)
            {
                LogRejectOnce(cart, hold);
                cart.BlockTicks = BLOCK_HOLD_TICKS;
                cart.ProbeTicks = PROBE_TICKS;
                cart.StallTicks = 0;
                vCmd = 0f;
            }
            else if (vCmd > V_EPS && FollowBlocked(cart))
            {
                LogRejectOnce(cart, "another cart is in the way -- waiting");
                vCmd = 0f;
            }
            else if (cart.BlockTicks > 0)
            {
                cart.BlockTicks--;
                vCmd = 0f;
            }
            else if (cart.ProbeTicks > 0)
            {
                cart.ProbeTicks--;
                vCmd = Math.Min(vCmd, PROBE_STEP / TICK_SECONDS);
            }
            else if (cart.LastRejectReason != null && cart.LastRejectReason.EndsWith("-- waiting"))
            {
                LogRejectOnce(cart, null);
            }
            cart.MotorCmd = cart.MotorRetreat ? -dir * BLOCK_RETREAT_STEP / TICK_SECONDS : dir * vCmd;
            float planAccS = 0f;
            float wantS = (dir * vCmd - cart.SpeedS) / TICK_SECONDS;
            bool holdMotor = !thrust;
            if (thrust && cartShip?.Physics?.Body != null)
            {
                Vector2 vb = cartShip.Physics.Body.LinearVelocity;
                float vWorld = (vb.X * worldDir.X + vb.Y * worldDir.Y) / sc;
                float raw = RailThrustMath.RawThrustAccel(vCmd, along, TICK_SECONDS, RailThrustMath.ENGINE_DRAG, vWorld)
                    + (vCmd > V_EPS ? RailThrustMath.RollingAccel(Math.Max(along, vCmd), r.RollingResistance) : 0f);
                float aDes = RailThrustMath.ThrustAccel(raw, fwdOnRail * k, backOnRail * k);
                float thrustNow = RailThrustDrive.Command(cartShip, push, RailThrustMath.ThrustDemand(aDes * mEff * sc, align), TICK_SECONDS);
                float thrustOnRail = RailThrustMath.ThrustOnRail(thrustNow, align);
                cart.ThrustCmd = aDes;
                cart.ThrustActual = thrustNow / Math.Max(1e-4f, mass * sc);
                HingeJointManager.SetRailThrusting(cart, true);
                bool gripOn = cart.MotorRetreat || RailThrustMath.GripOn(raw, backOnRail * k, vCmd);
                holdMotor = gripOn;
                cart.MotorForceCap = gripOn ? mass * (bGrip + cart.ThrustBack) * sc : 0f;
                if (!gripOn)
                {
                    cart.MotorCmd = 0f;
                    cart.MotorForceCap = mass * r.RollingResistance * sc;
                }
                planAccS = gripOn ? RailThrustMath.AchievedAccel(wantS, dir, fwdOnRail * k, (bGrip + backOnRail) * k)
                    : RailThrustMath.NetDrive(dir * thrustOnRail, mass * r.RollingResistance * sc, cart.SpeedS) / (mEff * sc);
                cart.UnderTicks = RailThrustMath.UnderpoweredStep(cart.UnderTicks, RailThrustMath.Saturated(raw, fwdOnRail * k), along);
                bool weak = RailThrustMath.Underpowered(cart.UnderTicks);
                cart.DriveNote = weak ? $"underpowered: {cart.ThrustFwd:F2} cells/s^2 of thrust does not move {mass:F1} t" : "";
                if (weak) LogRejectOnce(cart, $"underpowered -- {cart.ThrustFwd:F2} cells/s^2 of thrust toward the goal does not move the load ({mass:F1} t)");
            }
            else
            {
                HingeJointManager.SetRailThrusting(cart, false);
                cart.ThrustActual = 0f;
                float aDrive = CartAccel(r, cart, mass);
                float driveForce = mass * (aDrive + RailThrustMath.ENGINE_DRAG * Math.Abs(along));
                float brakeForce = mass * r.EmergencyFactor * r.BrakeFactor * aDrive;
                bool brakingNow = cart.MotorRetreat || wantS * dir < 0f;
                cart.MotorForceCap = (brakingNow ? brakeForce : driveForce) * sc;
                planAccS = RailThrustMath.AchievedAccel(wantS, dir, driveForce / mEff, brakeForce / mEff);
            }
            cart.ShipPush = holdMotor ? ShipPushAlong(cart, route) : 0f;
            if (holdMotor && ShipHoldOn(terminal.Part))
            {
                float ownCap = cart.MotorForceCap;
                cart.MotorForceCap = RailShipHold.CompensatedCap(ownCap, cart.ShipPush, true);
                if (Math.Abs(cart.ShipPush) > 0.5f * ownCap)
                    LogThrottle.Info($"shiphold|{cart.Part?.Location}", $"Rail cart at {cart.Part?.Location}: the ship's push along the rail ({cart.ShipPush:F0}) is over half its own drive cap ({ownCap:F0}); cancelled (ship hold on).");
            }
            cart.PlanAccS = planAccS;
            cart.TurnForce = RailThrustMath.TurnForce(addedMass, cart.PlanAccS * sc);
            cart.TurnDeltaV = float.IsNaN(cart.AddedPrev) ? 0f
                : RailThrustMath.TurnSpeed(cart.SpeedS, mass, cart.AddedPrev, addedMass) - cart.SpeedS;
            cart.AddedPrev = addedMass;
            cart.V = Math.Max(0f, along);
            if (cart.ViaTile != null && cart.S >= cart.ViaS - ARRIVE_MAX_S) Pass(cart);
            if (cart.StopAt.HasValue && vCmd <= V_EPS && Math.Abs(along) <= RailThrustMath.STALL_SPEED)
            {
                FinishStop(terminal, cart);
            }
            else if (Math.Abs(cart.GoalS - cart.S) <= ARRIVE_MAX_S && Math.Abs(along) <= RailThrustMath.STALL_SPEED)
            {
                cart.S = route.Clamp(cart.GoalS);
                Arrive(cart);
            }
            return Math.Abs(cart.S - before);
        }

        private float? HoldStopAt(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart)
        {
            RailRoute route = cart.Route;
            if (route == null || cart.Part == null || !MergeOn(cart.Part)) return null;
            float dir = cart.GoalS >= cart.S ? 1f : -1f;
            float b = cart.LoadBrake > 0f ? cart.LoadBrake : CartAccel(terminal.Rules, cart, cart.LoadMass) * terminal.Rules.BrakeFactor;
            float natural = cart.S + dir * RailMotion.StopDistance(cart.V, b);
            var feet = new List<float>(route.Tiles.Count);
            for (int i = 0; i < route.Tiles.Count; i++) feet.Add(route.TileS(i));
            return RailCartRules.HoldStopS(natural, feet, dir > 0f ? 1 : -1);
        }

        private static readonly Dictionary<string, ID<PartToggleGuiRules>> s_cmIds = new Dictionary<string, ID<PartToggleGuiRules>>();

        private static ID<PartToggleGuiRules> CmId(string name)
        {
            lock (s_cmIds)
            {
                if (!s_cmIds.TryGetValue(name, out ID<PartToggleGuiRules> id)) s_cmIds[name] = id = new ID<PartToggleGuiRules>(name);
                return id;
            }
        }

        public static bool MergeOn(Part cartPart)
        {
            Part global = GlobalSource(cartPart);
            return global != null ? TerminalMerge(global) : LogicSource.Mode(cartPart, RailToggles.MergeId, 0) == 1;
        }

        public static bool TryMapCartHomeCell(Ship ship, IntVector2 homeCell, out IntVector2 liveCell)
        {
            liveCell = homeCell;
            RailShipManager mgr = TryGet(ship);
            if (mgr == null || mgr._carts.Count == 0) return false;
            Dictionary<IntVector2, IntVector2> map = mgr.CartHomeMap();
            return map.Count > 0 && map.TryGetValue(homeCell, out liveCell);
        }

        private readonly object _cartMapLock = new object();
        private Dictionary<IntVector2, IntVector2> _cartMap = new Dictionary<IntVector2, IntVector2>();
        private int _cartMapTick = int.MinValue;

        private Dictionary<IntVector2, IntVector2> CartHomeMap()
        {
            Ship ship = base.Ship;
            int tick = ship?.Sim?.Tick ?? int.MinValue;
            lock (_cartMapLock)
            {
                if (tick == _cartMapTick) return _cartMap;
                var map = new Dictionary<IntVector2, IntVector2>();
                foreach (ZtxRailCartComponent c in _carts)
                {
                    Part cp = c.Part;
                    if (cp?.Ship != ship || c.PairedTerminal != null || !IsMergedAway(cp)) continue;
                    CartMergeRecord rec = ReadCartMerge(cp);
                    if (rec == null) continue;
                    GridTransform now = rec.XfNow(cp);
                    for (int i = 0; i < rec.MemberCount; i++)
                    {
                        IntVector2 home = rec.Home + rec.Member(i).Rel;
                        map[home] = now.TransformCell(home);
                    }
                }
                _cartMap = map;
                _cartMapTick = tick;
                return map;
            }
        }

        internal static List<Part> MergedCartsOn(Ship carrier, HashSet<Part> section)
        {
            var take = new List<Part>();
            if (carrier == null || section == null || section.Count == 0) return take;
            foreach (Part cp in carrier.Parts.ToArray())
            {
                if (ZtxRailCartComponent.ForPart(cp) == null || !IsMergedAway(cp)) continue;
                Part faced = carrier.Parts[cp.Location + CartFacing(cp), PartRectType.Physical];
                if (faced == null || !section.Contains(faced)) continue;
                CartMergeRecord rec = ReadCartMerge(cp);
                if (rec == null) continue;
                foreach ((int _, Part p) in CartMembers(cp, rec))
                    if (!section.Contains(p) && !take.Contains(p)) take.Add(p);
            }
            return take;
        }

        private static Part HomeTileNow(Ship carrier, RailNetwork net, ZtxRailCartComponent cart)
        {
            if (cart.HomeTile != null && net.Contains(cart.HomeTile)) return cart.HomeTile;
            if (carrier == null || cart.Part == null) return null;
            IntVector2 step = cart.HomeStep ?? CartFacing(cart.Part);
            Part t = carrier.Parts[cart.HomeCell + step, PartRectType.Physical];
            return net.Contains(t) ? t : null;
        }

        internal static List<Part> MergedAwayCartParts(Ship ship)
        {
            var parts = new List<Part>();
            if (ship == null) return parts;
            foreach (Part cp in ship.Parts.ToArray())
            {
                if (ZtxRailCartComponent.ForPart(cp) == null || !IsMergedAway(cp)) continue;
                CartMergeRecord rec = ReadCartMerge(cp);
                if (rec == null) continue;
                foreach ((int _, Part p) in CartMembers(cp, rec)) parts.Add(p);
            }
            return parts;
        }

        public static bool IsMergedAway(Part cartPart)
        {
            var meta = cartPart?.Ship?.Metadata;
            if (meta == null) return false;
            var psd = new PartSaveData(cartPart);
            return CartMergeRecord.IsAway(n => meta.PartUIToggleStates.Get((psd, CmId(n))));
        }

        internal static CartMergeRecord ReadCartMerge(Part cartPart)
        {
            var meta = cartPart?.Ship?.Metadata;
            if (meta == null) return null;
            var psd = new PartSaveData(cartPart);
            return CartMergeRecord.ReadFrom(n => meta.PartUIToggleStates.Get((psd, CmId(n))));
        }

        internal static void WriteCartHome(Part cartPart, IntVector2 fromTerminal, IntVector2 step)
        {
            var meta = cartPart?.Ship?.Metadata;
            if (meta == null) return;
            var psd = new PartSaveData(cartPart);
            void Set(string n, int v)
            {
                int? value = v == 0 ? (int?)null : v;
                meta.PartUIToggleStates.Set((psd, CmId(n)), value);
                meta.PartUIToggleStates.Set((psd, CmId(n)), value, blueprintMode: true);
            }
            Set("ztx_ch_on", 1);
            Set("ztx_ch_x", fromTerminal.X);
            Set("ztx_ch_y", fromTerminal.Y);
            Set("ztx_ch_sx", step.X);
            Set("ztx_ch_sy", step.Y);
        }

        internal static bool TryReadCartHome(Part cartPart, out IntVector2 fromTerminal, out IntVector2 step)
        {
            fromTerminal = IntVector2.Zero;
            step = IntVector2.Zero;
            var meta = cartPart?.Ship?.Metadata;
            if (meta == null) return false;
            var psd = new PartSaveData(cartPart);
            int Get(string n) => meta.PartUIToggleStates.Get((psd, CmId(n))) ?? 0;
            if (Get("ztx_ch_on") != 1) return false;
            fromTerminal = new IntVector2(Get("ztx_ch_x"), Get("ztx_ch_y"));
            step = new IntVector2(Get("ztx_ch_sx"), Get("ztx_ch_sy"));
            return step != IntVector2.Zero;
        }

        internal static void WriteCartMerge(Part cartPart, CartMergeRecord r, int clearMembersUpTo, int clearFramesUpTo = 0)
        {
            var meta = cartPart?.Ship?.Metadata;
            if (meta == null) return;
            var psd = new PartSaveData(cartPart);
            r.WriteClearing((n, v) =>
            {
                int? value = v == 0 ? (int?)null : v;
                meta.PartUIToggleStates.Set((psd, CmId(n)), value);
                meta.PartUIToggleStates.Set((psd, CmId(n)), value, blueprintMode: true);
            }, clearMembersUpTo, clearFramesUpTo);
        }

        internal static List<(int Index, Part Part)> CartMembers(Part cartPart, CartMergeRecord rec)
        {
            var found = new List<(int, Part)>();
            Ship ship = cartPart?.Ship;
            if (ship == null || rec == null) return found;
            GridTransform xf = rec.XfNow(cartPart);
            var seen = new HashSet<Part>();
            for (int i = 0; i < rec.MemberCount; i++)
            {
                var (rel, hash) = rec.Member(i);
                Part p = ship.Parts[xf.TransformCell(rec.Home + rel), PartRectType.Normal];
                if (p == null || seen.Contains(p) || RailNetwork.IsRailPart(p)) continue;
                if (!MergeRebuild.HashMatches(p.Rules, hash)) continue;
                seen.Add(p);
                found.Add((i, p));
            }
            return found;
        }

        private static string StopKey(Part tile)
        {
            return tile != null && (ZtxRailTileComponent.ForPart(tile)?.IsStop ?? false) ? RailNetwork.Key(RailNetwork.DisplayName(tile)) : null;
        }

        private void TryMergeAway(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart)
        {
            if (cart.MergeRetryTicks > 0) { cart.MergeRetryTicks--; return; }
            if (!HingeJointManager.TryGetWeldState(cart, out float error, out float speed)) return;
            if (error > MERGE_MAX_ERROR || speed > MERGE_MAX_SPEED) return;
            Ship ship = base.Ship;
            Part cp = cart.Part;
            Ship cartShip = cp?.Ship;
            if (cartShip?.Physics?.Body == null || cartShip == ship || ship.Physics?.Body == null) return;
            HingeJointManager.CarriedState(cartShip.Physics.Body, new System.Numerics.Vector2(0f, 0f), out float carriedGap, out _);
            if (carriedGap > CARRY_MERGE_GAP * ShipScale())
            {
                cart.MergeRetryTicks = MERGE_RETRY_TICKS;
                LogRejectOnce(cart, $"a pin on what the cart carries is pulled apart ({carriedGap / ShipScale():F2} cells) -- not merging until it closes");
                return;
            }
            Vector2 world = cartShip.Physics.Body.GetWorldPoint(new Vector2(0.5f, 0.5f) * cartShip.Scale);
            Vector2 g = ship.Physics.Body.GetLocalPoint(world) / ship.Scale;
            if (!GripMath.TryGrid(cartShip.Physics.Body.Rotation - ship.Physics.Body.Rotation, g.X, g.Y, out GridTransform xf))
            {
                cart.MergeRetryTicks = MERGE_RETRY_TICKS;
                LogRejectOnce(cart, "Merge on, but the cart is not lined up with the ship's grid here (a curve, or between cells) -- resting unmerged");
                return;
            }
            if (xf.IsIdentity)
            {
                bool homeDwell = cart.Phase == RailPhase.Dwelling;
                Part homeTile = cart.GoalTile;
                int homeTicks = Math.Max(1, (int)Math.Ceiling(cart.DwellLeft / TICK_SECONDS));
                TryMerge(terminal, cart);
                if (homeDwell && cart.PairedTerminal == null && cp.Ship == ship)
                {
                    cart.MergedDwellEnd = _tickCounter + homeTicks;
                    cart.DwellTile = homeTile;
                    Log.Info($"Rail terminal at {terminal.Part?.Location}: cart merged at {StopKey(homeTile) ?? "home"} (its home cell) for its dwell -- leaves in {homeTicks * TICK_SECONDS:F1} s.");
                }
                return;
            }
            var rec = new CartMergeRecord { Away = true, K = xf.K, T = xf.T, Home = cp.Location, MergeRot = (cp.Rotation + xf.K) & 3 };
            foreach (Part p in cartShip.Parts) rec.AddMember(p.Location - cp.Location, MergeRecord.Hash(p.Rules.ID.ToString()));
            string where = StopKey(ship.Parts[xf.TransformCell(cp.Location) + xf.RotateVector(CartFacing(cp)), PartRectType.Physical]);
            int count = rec.MemberCount;
            bool dwell = cart.Phase == RailPhase.Dwelling;
            Part dwellTile = cart.GoalTile;
            int dwellTicks = Math.Max(1, (int)Math.Ceiling(cart.DwellLeft / TICK_SECONDS));
            int lifted = LiftFramesUnder(ship, cartShip, xf, rec);
            bool merged;
            try { merged = ShipMerger.Merge(ship, cartShip, cart, xf); }
            catch (Exception ex) { LogThrottle.Exception("RailShipManager.TryMergeAway", ex); merged = false; }
            if (!merged)
            {
                if (lifted > 0) PutFramesBack(ship, rec, "the merge was refused");
                cart.MergeRetryTicks = MERGE_RETRY_TICKS;
                LogRejectOnce(cart, "merge refused (a cell the cart needs on the ship is taken, see the Merge line above) -- resting unmerged, retrying every second");
                return;
            }
            cart.ClearPair();
            WriteCartMerge(cp, rec, 0);
            if (dwell)
            {
                cart.MergedDwellEnd = _tickCounter + dwellTicks;
                cart.DwellTile = dwellTile;
                Log.Info($"Rail terminal at {terminal.Part?.Location}: cart merged at {where ?? "?"} for its dwell ({count} part(s), {xf}) -- leaves in {dwellTicks * TICK_SECONDS:F1} s.");
                return;
            }
            Log.Info($"Rail terminal at {terminal.Part?.Location}: cart merged {(where != null ? "at " + where : "where it holds")} ({count} part(s), {xf}) -- hull until its mode, target or Merge changes.");
        }

        private void UnmergeEndedDwells()
        {
            Ship ship = base.Ship;
            List<ZtxRailCartComponent> due = null;
            for (int i = 0; i < _carts.Count; i++)
            {
                ZtxRailCartComponent c = _carts[i];
                if (c.MergedDwellEnd >= 0 && _tickCounter >= c.MergedDwellEnd && c.PairedTerminal == null && c.Part?.Ship == ship)
                    (due ??= new List<ZtxRailCartComponent>()).Add(c);
            }
            if (due == null) return;
            foreach (ZtxRailCartComponent c in due)
            {
                Part cp = c.Part;
                if (!IsMergedAway(cp) && Intent(cp) != CartIntent.Travel) { c.MergedDwellEnd = -1; continue; }
                Part at = ship.Parts[cp.Location + CartFacing(cp), PartRectType.Physical];
                ZtxRailTerminalComponent terminal = null;
                foreach (ZtxRailTerminalComponent t in _terminals)
                    if (t.Part != null && t.Part.Ship == ship && t.Network.Contains(at)) { terminal = t; break; }
                if (terminal == null) { c.MergedDwellEnd = -1; continue; }
                string target = RailNetwork.Key(TargetName(cp));
                if (RailCartRules.MergeWanted(Mode(cp), target.Length == 0, MergeOn(cp), StopKey(at), target, false)) { c.MergedDwellEnd = -1; continue; }
                UnmergeAway(terminal, c, at);
            }
        }

        private void UnmergeAway(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart, Part at)
        {
            bool afterDwell = cart.MergedDwellEnd >= 0;
            string cycleAt = cart.CycleAt;
            bool shuttleHome = cart.ShuttleToHome;
            bool goalHome = cart.GoalIsHome;
            Part cameFrom = cart.CameFrom;
            Part dwellTile = cart.DwellTile;
            if (IsMergedAway(cart.Part)) TryUnmergeAway(terminal, cart, at);
            else TrySplitCart(terminal, cart, at);
            if (!afterDwell) return;
            if (cart.PairedTerminal == null)
            {
                cart.MergedDwellEnd = _tickCounter + SERVICE_INTERVAL_TICKS;
                return;
            }
            int mode = cart.Part != null ? Mode(cart.Part) : MODE_GOTO;
            if (mode != MODE_CYCLE && mode != MODE_SHUTTLE && mode != MODE_FORWARD && mode != MODE_BACKWARD) return;
            cart.CycleAt = cycleAt;
            cart.ShuttleToHome = shuttleHome;
            cart.CameFrom = cameFrom;
            cart.FreshPair = true;
            if (dwellTile != null && dwellTile == at)
            {
                cart.GoalTile = at;
                cart.GoalIsHome = goalHome;
            }
            Log.Info($"Rail cart at {cart.Part?.Location}: dwell at {StopKey(at) ?? "?"} over -- unmerged, on to the next stop.");
        }

        private void TryUnmergeAway(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart, Part at)
        {
            Ship ship = base.Ship;
            Part cp = cart.Part;
            Part tp = terminal.Part;
            CartMergeRecord rec = ReadCartMerge(cp);
            if (rec == null || ship?.Physics?.Body == null) return;
            GridTransform drift = rec.DriftOf(cp);
            GridTransform xf = rec.Xf.Then(drift);
            var parts = new List<Part>();
            var seen = new HashSet<Part>();
            foreach ((int _, Part p) in CartMembers(cp, rec))
            {
                seen.Add(p);
                parts.Add(p);
            }
            if (!seen.Contains(cp))
            {
                LogRejectOnce(cart, "unmerge refused: the cart part is not where its merge record puts it");
                return;
            }
            IntVector2 hereCell = cp.Location;
            int recorded = rec.MemberCount;
            WriteCartMerge(cp, new CartMergeRecord(), recorded, rec.FrameCount);
            Ship split;
            try { split = ShipSplitter.Split(ship, parts, tp); }
            catch (Exception ex) { LogThrottle.Exception("RailShipManager.TryUnmergeAway", ex); split = null; }
            if (split?.Physics?.Body == null || cp.Ship == ship)
            {
                if (cp.Ship == ship) WriteCartMerge(cp, rec, 0);
                LogRejectOnce(cart, "unmerge failed: the split was rolled back (see the Split lines above) -- staying merged, retrying every second");
                return;
            }
            Ship cartShip = ShipRecenter.Regrid(split, xf.Inverse());
            if (cartShip == split)
                Log.Error($"Rail cart at {cp.Location}: unmerged, but its ship kept the merged grid (regrid refused); it rides, but cannot merge back home until re-split.");
            float currentRel = cartShip.Physics.Body.Rotation - ship.Physics.Body.Rotation;
            HingeJointManager.CreateRailSlider(cart, ship, cartShip, RailRoute.Center(hereCell) * ship.Scale,
                RailRoute.Center(cp.Location) * cartShip.Scale, currentRel, tp, cp);
            cart.HomeTile = HomeTileNow(ship, terminal.Network, cart);
            Pair(terminal, cart, cartShip, at, currentRel);
            Log.Info($"Rail terminal at {tp.Location}: cart unmerged at {(StopKey(at) ?? hereCell.ToString())} ({parts.Count} part(s)) -- riding again.");
            if (rec.FrameCount > 0) PutFramesBack(ship, rec, "the cart unmerged", drift);
        }

        private static int LiftFramesUnder(Ship ship, Ship cartShip, GridTransform xf, CartMergeRecord rec)
        {
            var lift = new List<Part>();
            foreach (Part p in cartShip.Parts)
                foreach (IntVector2 cell in p.GetCells(PartRectType.Normal))
                {
                    Part there = ship.Parts[xf.TransformCell(cell), PartRectType.Normal];
                    if (there == null || lift.Contains(there)) continue;
                    if (RailCartRules.LiftIndex(there.Rules.ID.ToString()) < 0) return 0;
                    lift.Add(there);
                }
            int done = 0;
            foreach (Part f in lift)
            {
                try
                {
                    int kind = RailCartRules.LiftIndex(f.Rules.ID.ToString());
                    IntVector2 loc = f.Location;
                    int rotFlip = (f.Rotation & 3) | (f.FlipX ? 4 : 0);
                    BlueprintPart bp = ship.BlueprintParts?.GetMatchingPart(f);
                    if (bp != null) ship.BlueprintParts.RemoveBlueprintPart(bp, removeDoors: true);
                    ship.Parts.RemovePart(f, PartsManager.CrewAction.None, destroyed: false, removeDoors: true, null);
                    rec.AddFrame(kind, loc, rotFlip);
                    done++;
                }
                catch (Exception ex)
                {
                    Log.Exception("RailShipManager.LiftFramesUnder", ex);
                }
            }
            if (done > 0) Log.Info($"Rail cart merge: lifted {done} Open Frame(s) from under the payload; they go back when it unmerges.");
            return done;
        }

        private static void PutFramesBack(Ship ship, CartMergeRecord rec, string why, GridTransform drift = default)
        {
            int back = 0, skipped = 0;
            for (int i = 0; i < rec.FrameCount; i++)
            {
                (int kind, IntVector2 loc, int rotFlip) = rec.Frame(i);
                loc = drift.TransformCell(loc);
                rotFlip = (((rotFlip & 3) + drift.K) & 3) | (rotFlip & 4);
                try
                {
                    if (kind < 0 || kind >= RailCartRules.LIFT_IDS.Length
                        || !ship.Rules.PartsByID.TryGetValue(new ID<PartRules>(RailCartRules.LIFT_IDS[kind]), out PartRules rules) || rules == null)
                    {
                        skipped++;
                        continue;
                    }
                    int rot = rotFlip & 3;
                    bool flip = (rotFlip & 4) != 0;
                    IntVector2 size = rules.GetSize(rot);
                    bool free = true;
                    for (int x = 0; x < size.X && free; x++)
                        for (int y = 0; y < size.Y && free; y++)
                            if (ship.Parts[loc + new IntVector2(x, y), PartRectType.Normal] != null) free = false;
                    if (!free) { skipped++; continue; }
                    Part f = ship.Parts.CreatePart(rules, loc, rot, flip);
                    if (ship.BlueprintParts != null && ship.BlueprintParts.GetMatchingPart(f) == null && ship.BlueprintParts[loc, PartRectType.Normal] == null)
                        ship.BlueprintParts.CreateBlueprintPart(rules, loc, rot, flip);
                    back++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    Log.Exception("RailShipManager.PutFramesBack", ex);
                }
            }
            Log.Info($"Rail cart: {why} -- put back {back} Open Frame(s){(skipped > 0 ? $", {skipped} skipped (cell taken or part unknown)" : "")}.");
        }

        private void HoldStep(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart)
        {
            cart.Returning = false;
            if (cart.Phase == RailPhase.Moving && cart.V > V_EPS && !cart.MotionFailed && cart.Route != null)
            {
                if (cart.StopReason != STOP_HOLD) RequestStop(terminal, cart, STOP_HOLD, HoldStopAt(terminal, cart));
            }
            else if (!cart.StopAt.HasValue && (cart.Phase != RailPhase.Holding || cart.GoalTile != null))
            {
                cart.Phase = RailPhase.Holding;
                cart.GoalTile = null;
                cart.LegIsTurnRound = false;
                cart.ViaTile = null;
                Log.Info($"Rail cart at {cart.Part?.Location}: holding for HOLD.");
            }
            Drive(terminal, cart);
        }

        private static float[] s_spanLo = new float[16], s_spanHi = new float[16];

        private static float StopPastCurves(ZtxRailTerminalComponent terminal, RailRoute route, float target, float dir)
        {
            RailNetwork net = terminal?.Network;
            if (net == null || route == null || route.IsEmpty) return target;
            int n = 0;
            for (int i = 0; i + 1 < route.Tiles.Count; i++)
            {
                if (net.ArcBetween(route.Tiles[i].Location, route.Tiles[i + 1].Location) == null) continue;
                if (n == s_spanLo.Length) { Array.Resize(ref s_spanLo, n * 2); Array.Resize(ref s_spanHi, n * 2); }
                s_spanLo[n] = route.TileS(i);
                s_spanHi[n] = route.TileS(i + 1);
                n++;
            }
            return RailCartRules.StopOutsideCurve(target, dir, s_spanLo, s_spanHi, n);
        }

        private void RequestStop(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart, string reason, float? atS = null)
        {
            RailRoute route = cart.Route;
            if (route == null) return;
            float dir = cart.GoalS >= cart.S ? 1f : -1f;
            float b = cart.LoadBrake > 0f ? cart.LoadBrake : CartAccel(terminal.Rules, cart, cart.LoadMass) * terminal.Rules.BrakeFactor;
            float target = atS ?? cart.S + dir * RailMotion.StopDistance(cart.V, b);
            if (reason != STOP_CUTOFF && reason != STOP_BLOCKED) target = StopPastCurves(terminal, route, target, dir);
            if (dir > 0f) target = Math.Max(cart.S, Math.Min(target, cart.GoalS));
            else target = Math.Min(cart.S, Math.Max(target, cart.GoalS));
            if (cart.StopAt.HasValue) target = dir > 0f ? Math.Min(target, cart.StopAt.Value) : Math.Max(target, cart.StopAt.Value);
            target = route.Clamp(target);
            if (!cart.StopAt.HasValue || Math.Abs(cart.StopAt.Value - target) > 1e-3f)
                Log.Info($"Rail cart at {cart.Part?.Location}: braking to a stop for {reason}, {Math.Abs(target - cart.S):F1} cells on at {cart.V:F2} cells/s.");
            cart.StopAt = target;
            cart.StopReason = reason;
        }

        private void FinishStop(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart)
        {
            string why = cart.StopReason;
            cart.StopAt = null;
            cart.StopReason = null;
            cart.V = 0f;
            cart.LegThrust = null;
            if (why == STOP_CUTOFF || why == STOP_BLOCKED)
            {
                cart.Route = null;
                Part near = terminal.Network.NearestTile(CartGrid(cart), out _);
                if (near != null) cart.CurrentTile = near;
            }
            cart.ViaTile = null;
            cart.Phase = RailPhase.Holding;
            cart.GoalTile = null; cart.LegIsTurnRound = false;
            Log.Info($"Rail cart at {cart.Part?.Location}: stopped for {why ?? "a stop request"}.");
        }

        private void Arrive(ZtxRailCartComponent cart)
        {
            cart.V = 0f;
            cart.LegThrust = null;
            Part cp = cart.Part;
            int n = cart.Route?.Tiles.Count ?? 0;
            if (cart.LegIsTurnRound)
            {
                cart.CameFrom = n >= 2 ? cart.Route.Tiles[n - 2] : cart.CameFrom;
                cart.CurrentTile = cart.GoalTile;
                cart.ViaTile = null;
                cart.LegIsTurnRound = false;
                cart.GoalTile = null; cart.LegIsTurnRound = false;
                cart.Phase = RailPhase.Holding;
                Log.Info($"Rail cart at {cp?.Location}: turning round at {cart.CurrentTile?.Location} for {(cart.FinalIsHome ? "HOME" : RailNetwork.DisplayName(cart.FinalGoal))}.");
                return;
            }
            cart.CameFrom = n >= 2 ? cart.Route.Tiles[n - 2] : cart.CameFrom;
            cart.CurrentTile = cart.GoalTile;
            cart.ViaTile = null;
            int mode = cp != null ? Mode(cp) : MODE_GOTO;
            if (cart.Returning)
            {
                cart.Phase = RailPhase.Holding;
            }
            else if (mode == MODE_CYCLE || mode == MODE_SHUTTLE || mode == MODE_FORWARD || mode == MODE_BACKWARD)
            {
                bool atStation = cart.GoalTile != null && (ZtxRailTileComponent.ForPart(cart.GoalTile)?.IsStop ?? false);
                cart.DwellLeft = cp != null && (atStation || mode == MODE_SHUTTLE) ? DwellSeconds(cp) : 0f;
                cart.Phase = cart.DwellLeft > 0f ? RailPhase.Dwelling : RailPhase.Holding;
                if (!cart.GoalIsHome && cart.GoalTile != null) cart.CycleAt = RailNetwork.DisplayName(cart.GoalTile);
            }
            else
            {
                cart.Phase = RailPhase.Holding;
            }
            Log.Info($"Rail cart at {cp?.Location}: arrived at {(cart.GoalIsHome ? "HOME" : RailNetwork.DisplayName(cart.GoalTile))}{(cart.Phase == RailPhase.Dwelling ? $", dwelling {cart.DwellLeft:F0} s" : "")}.");
        }

        private bool TouchingAhead(ZtxRailCartComponent cart, RailRoute route, float s, bool forward)
        {
            Halfling.Physics2D.Dynamics.Body body = cart.Part?.Ship?.Physics?.Body;
            Halfling.Physics2D.Dynamics.Body carrier = base.Ship?.Physics?.Body;
            if (body == null || carrier == null) return false;
            Vector2 a = route.Point(s), b = route.Point(route.Clamp(s + (forward ? 0.25f : -0.25f)));
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return false;
            System.Numerics.Vector2 travel = carrier.GetWorldVector(new System.Numerics.Vector2(dx / len, dy / len));
            System.Numerics.Vector2 centre = body.WorldCenter;
            for (Halfling.Physics2D.Dynamics.Contacts.ContactEdge e = body.ContactList; e != null; e = e.Next)
            {
                Halfling.Physics2D.Dynamics.Contacts.Contact c = e.Contact;
                if (c == null || !c.IsTouching || !c.Enabled) continue;
                Halfling.Physics2D.Dynamics.Fixture fa = c.FixtureA, fb = c.FixtureB;
                if (fa == null || fb == null) continue;
                Halfling.Physics2D.Dynamics.Fixture other = fa.Body == body ? fb : fa;
                if (other.Body == body) continue;
                if ((other.CollisionCategories & Halfling.Physics2D.Dynamics.Category.Cat1) == 0) continue;
                float tol = (cart.PairedTerminal?.Rules.LaneTolerance ?? 0.05f) * (cart.Part.Ship.WorldUniformScale);
                if (!LaneGuard.ContactInLane(c, travel, tol)) continue;
                Halfling.Physics2D.Dynamics.Fixture mine = ReferenceEquals(other, fb) ? fa : fb;
                if (LaneGuard.FixtureSpan(mine, travel, out float m0, out float m1) && LaneGuard.FixtureSpan(other, travel, out float o0, out float o1))
                {
                    if (RailFollow.ContactAhead(m0, m1, o0, o1)) return true;
                    continue;
                }
                c.GetWorldManifold(out _, out Halfling.Physics2D.Common.FixedArray2<System.Numerics.Vector2> pts);
                int n = c.Manifold.PointCount;
                for (int i = 0; i < n && i < 2; i++)
                {
                    System.Numerics.Vector2 pt = pts[i];
                    if ((pt.X - centre.X) * travel.X + (pt.Y - centre.Y) * travel.Y > 0f) return true;
                }
            }
            return false;
        }

        private void TryMerge(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart)
        {
            if (cart.MergeRetryTicks > 0) { cart.MergeRetryTicks--; return; }
            if (!HingeJointManager.TryGetWeldState(cart, out float error, out float speed)) return;
            if (error > MERGE_MAX_ERROR || speed > MERGE_MAX_SPEED) return;
            Ship ship = base.Ship;
            Part cp = cart.Part;
            Ship cartShip = cp?.Ship;
            if (cartShip == null || cartShip == ship) return;
            HingeJointManager.CarriedState(cartShip.Physics?.Body, new System.Numerics.Vector2(0f, 0f), out float carriedGap, out _);
            if (carriedGap > CARRY_MERGE_GAP * ShipScale())
            {
                cart.MergeRetryTicks = MERGE_RETRY_TICKS;
                LogRejectOnce(cart, $"a pin on what the cart carries is pulled apart ({carriedGap / ShipScale():F2} cells) -- not merging home until it closes");
                return;
            }
            if (cartShip.Physics?.Body == null || ship.Physics?.Body == null) return;
            Vector2 world = cartShip.Physics.Body.GetWorldPoint(new Vector2(0.5f, 0.5f) * cartShip.Scale);
            Vector2 g = ship.Physics.Body.GetLocalPoint(world) / ship.Scale;
            if (!GripMath.TryGrid(cartShip.Physics.Body.Rotation - ship.Physics.Body.Rotation, g.X, g.Y, out GridTransform xf))
            {
                cart.MergeRetryTicks = MERGE_RETRY_TICKS;
                LogRejectOnce(cart, "at home, but not lined up with the ship's grid -- holding at home, welded, retrying every second");
                return;
            }
            if (ShipMerger.Merge(ship, cartShip, cart, xf))
            {
                cart.ClearPair();
                Log.Info($"Rail terminal at {terminal.Part?.Location}: cart at {cp.Location} merged back{(xf.IsIdentity ? "" : $" ({xf}: its rail has moved since it left)")} -- give it another mode or target to send it out again.");
            }
            else
            {
                cart.MergeRetryTicks = MERGE_RETRY_TICKS;
                LogRejectOnce(cart, $"merge of the cart at {cp.Location} blocked -- holding at home, welded, retrying every second (see the merge line above)");
            }
        }

        public static bool ParkedFrom(bool hasRail, int riding) => hasRail && riding == 0;

        public static bool IsTerminalParked(Part terminalPart)
        {
            ZtxRailTerminalComponent terminal = ZtxRailTerminalComponent.ForPart(terminalPart);
            if (terminal == null) return false;
            int riding = 0;
            foreach (ZtxRailCartComponent cart in terminal.Carts)
                if (HingeJointManager.HasPinWithTag(cart)) riding++;
            return ParkedFrom(!terminal.Network.IsEmpty, riding);
        }

        public static bool IsTerminalDone(Part terminalPart, out string why)
        {
            why = "";
            ZtxRailTerminalComponent terminal = ZtxRailTerminalComponent.ForPart(terminalPart);
            if (terminal == null) { why = "no component"; return false; }
            int riding = 0, moving = 0;
            foreach (ZtxRailCartComponent cart in terminal.Carts)
            {
                if (!HingeJointManager.HasPinWithTag(cart)) continue;
                riding++;
                if (cart.Phase == RailPhase.Moving) moving++;
            }
            if (terminal.Network.IsEmpty) { why = "no rail"; return false; }
            if (riding == 0) { why = "all carts parked"; return true; }
            why = moving == 0 ? (riding == 1 ? "cart at rest" : riding + " carts at rest") : (riding - moving) + " of " + riding + " at rest";
            return moving == 0;
        }

        public static void CartRest(Part cartPart, out bool merged, out bool moving, out string resting)
        {
            merged = false;
            moving = false;
            resting = null;
            ZtxRailCartComponent cart = ZtxRailCartComponent.ForPart(cartPart);
            if (cart == null) return;
            if (cart.PairedTerminal == null)
            {
                if (TerminalFor(cartPart) == null) return;
                merged = true;
                resting = IsMergedAway(cartPart)
                    ? StopKey(cartPart.Ship.Parts[cartPart.Location + CartFacing(cartPart), PartRectType.Physical])
                    : "";
                return;
            }
            if (cart.Phase == RailPhase.Moving) { moving = true; return; }
            bool atEnd = cart.Route == null || Math.Abs(cart.GoalS - cart.S) <= 0.05f;
            Part tile = atEnd ? cart.CurrentTile : null;
            if (tile == null) return;
            if (tile == cart.HomeTile) { resting = ""; return; }
            if (ZtxRailTileComponent.ForPart(tile)?.IsStop ?? false) resting = RailNetwork.Key(RailNetwork.DisplayName(tile));
        }

        public static void AddCartsOf(Part terminalPart, List<Part> into)
        {
            ZtxRailTerminalComponent terminal = ZtxRailTerminalComponent.ForPart(terminalPart);
            RailShipManager m = TryGet(terminalPart?.Ship);
            if (terminal == null || m == null) return;
            foreach (Part c in m.CartsOf(terminal)) if (!into.Contains(c)) into.Add(c);
        }

        public static bool IsCartParked(Part cartPart)
        {
            CartRest(cartPart, out bool merged, out _, out _);
            return merged;
        }

        public static bool IsAtStation(Part cartPart, LogicRef station)
        {
            CartRest(cartPart, out _, out _, out string resting);
            bool home = station.Kind == RefKind.On;
            return RailCartRules.AtStation(resting, home, home ? "" : StationKey(cartPart, station) ?? "");
        }

        public static string StationKey(Part cartPart, LogicRef station)
        {
            if (station.Kind == RefKind.On) return "";
            Part s = StationPart(cartPart, station);
            return s != null ? RailNetwork.Key(RailNetwork.DisplayName(s)) : null;
        }

        public static Part StationPart(Part cartPart, LogicRef station)
        {
            if (station.Kind != RefKind.Part) return null;
            ZtxRailTerminalComponent terminal = TerminalFor(cartPart);
            if (terminal == null) return null;
            foreach (Part s in terminal.Network.Stations)
                if (s.Location.X == station.X && s.Location.Y == station.Y) return s;
            Ship carrier = terminal.Part?.Ship;
            if (carrier != null && MergeService.TryMapHomeCell(carrier, new IntVector2(station.X, station.Y), out IntVector2 live))
                foreach (Part s in terminal.Network.Stations)
                    if (s.Location == live) return s;
            return null;
        }

        public static bool IsCartDone(Part cartPart, out string why)
        {
            why = "";
            if (ZtxRailCartComponent.ForPart(cartPart) == null) { why = "no component"; return false; }
            if (TerminalFor(cartPart) == null) { why = "not on a rail"; return false; }
            CartRest(cartPart, out bool merged, out bool moving, out string resting);
            string target = RailNetwork.Key(TargetName(cartPart));
            bool done = RailCartRules.Settle(Mode(cartPart), target.Length == 0, merged, moving, resting, target, MergeOn(cartPart), out _) == SeqRunner.DONE;
            why = done ? CartStatus(cartPart) : "moving";
            return done;
        }

        public static string CartStatus(Part cartPart)
        {
            ZtxRailCartComponent cart = ZtxRailCartComponent.ForPart(cartPart);
            if (cart == null) return "";
            if (cart.PairedTerminal == null)
            {
                if (TerminalFor(cartPart) == null) return "not on a rail";
                if (IsMergedAway(cartPart))
                {
                    string at = StopKey(cartPart.Ship.Parts[cartPart.Location + CartFacing(cartPart), PartRectType.Physical]);
                    return at != null ? "merged at " + at : "merged where it holds";
                }
                return Intent(cartPart) == CartIntent.Hold ? "parked (HOLD)" : "parked at home";
            }
            string goal = cart.GoalTile == null ? "-" : (cart.GoalIsHome ? "HOME" : RailNetwork.DisplayName(cart.GoalTile));
            switch (cart.Phase)
            {
                case RailPhase.Moving:
                {
                    string via = cart.ViaTile == null ? "" : " via " + (cart.ViaIsHome ? "HOME" : RailNetwork.DisplayName(cart.ViaTile));
                    return "moving to " + goal + via + (cart.Route != null ? $" ({cart.GoalS - cart.S:F1} cells left)" : "") +
                        (cart.MotionPhase.Length > 0 ? ", " + cart.MotionPhase : "");
                }
                case RailPhase.Dwelling: return "at " + goal + $", dwelling {cart.DwellLeft:F0} s";
                default: return Intent(cartPart) == CartIntent.Hold ? "holding (HOLD)" : cart.GoalTile != null ? "holding at " + goal : "holding";
            }
        }

        public static string CartMotion(Part cartPart)
        {
            ZtxRailCartComponent cart = ZtxRailCartComponent.ForPart(cartPart);
            if (cart == null || cart.PairedTerminal == null) return "";
            string bodies = cart.LoadBodies == 1 ? "1 body" : cart.LoadBodies + " bodies";
            float limit = cart.Phase == RailPhase.Moving ? cart.LastLimit : 0f;
            string drive = cart.LegThrust == true ? $"thrusters {cart.ThrustFwd:F1} / {cart.ThrustBack:F1} cells/s^2"
                : DriveMode(cartPart) == DRIVE_THRUSTERS ? "thrusters (cart drive this leg)" : "cart";
            return $"Load {cart.LoadMass:F1} t ({bodies}{(cart.LoadAttached ? ", attached to the ship" : "")}), speed {cart.V:F1} / {limit:F1} cells/s, drive {drive}{(cart.DriveNote.Length > 0 ? " -- " + cart.DriveNote : "")}";
        }

        private Vector2 CartGrid(ZtxRailCartComponent cart)
        {
            Ship ship = base.Ship;
            Part cp = cart.Part;
            Ship cs = cp?.Ship;
            if (cs?.Physics?.Body == null || ship?.Physics?.Body == null) return RailRoute.Center(cart.HomeCell);
            Vector2 world = cs.Physics.Body.GetWorldPoint(RailRoute.Center(cp.Location) * cs.Scale);
            return ship.Physics.Body.GetLocalPoint(world) / ship.Scale;
        }

        public static IntVector2 CartFacing(Part p)
        {
            return OrthogonalRotation.Down.GetShipRelative(p.Rotation, p.FlipX).ToVector();
        }

        private float ShipScale()
        {
            float s = base.Ship?.Scale.X ?? 1f;
            return s > 0f ? s : 1f;
        }

        private static Ship FindShipAtWorldPoint(Ship ship, Vector2 worldPoint)
        {
            s_assemblyScratch.Clear();
            HingeJointManager.GetAssemblyShips(ship, s_assemblyScratch);
            foreach (Ship m in s_assemblyScratch)
            {
                if (m == null || m == ship || m.Physics?.Body == null) continue;
                if (PartAtWorldPoint(m, worldPoint) != null) return m;
            }
            return ship.Sim.Ships.DetFindUnderPoint(worldPoint, approximate: false, junk: null, condition: s => s != ship);
        }

        private static Part PartAtWorldPoint(Ship other, Vector2 worldPoint)
        {
            Vector2 grid = other.Physics.Body.GetLocalPoint(worldPoint) / other.Scale;
            var cell = new IntVector2((int)Math.Floor(grid.X), (int)Math.Floor(grid.Y));
            return other.Parts[cell, PartRectType.Physical];
        }

        private static void LogRejectOnce(ZtxRailTerminalComponent terminal, string reason)
        {
            if (terminal == null || terminal.LastRejectReason == reason) return;
            terminal.LastRejectReason = reason;
            if (reason != null) Log.Info($"Rail terminal at {terminal.Part?.Location}: {reason}.");
        }

        private static void LogRejectOnce(ZtxRailCartComponent cart, string reason)
        {
            if (cart == null || cart.LastRejectReason == reason) return;
            cart.LastRejectReason = reason;
            if (reason != null) Log.Info($"Rail cart at {cart.Part?.Location}: {reason}.");
        }
    }
}
