using System;
using System.Collections.Generic;
using Cosmoteer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Halfling.Physics2D.Dynamics;
using Halfling.Physics2D.Dynamics.Contacts;
using Halfling.Physics2D.Dynamics.Joints;
using Halfling.Scene2D;
using HVec = Halfling.Geometry.Vector2;

namespace ZTX.HingesPistonsDocking
{
    internal static class HingeJointManager
    {
        private sealed class PinRecord
        {
            public Joint Joint;
            public SimRoot Sim;
            public World World;
            public Ship ShipA;
            public Ship ShipB;
            public object Tag;
            public Part PartA;
            public Part PartB;
            public List<Part> ExtraParts;
            public bool Separate;
            public Ship Visitor;
            public short Group;
            public EventHandler<Fixture> OnFixtureAdded;
            public float MotorBand = float.NaN;
            public int StallTicks;
            public float LastTranslation = float.NaN;
            public float HoldErrMax;
            public int HoldTicks;
            public float MotorTarget = float.NaN;
            public float BandRate;
            public double WeldTurns;
            public bool RailDrive;
            public bool RailThrust;
            public bool Driven100;
            public bool SliderClamped;
            public bool MotorLocked;
            public float LockedTarget = float.NaN;
            public float LoadInertia, LoadMass;
            public float ParentInertia, ParentMass;
            public bool Blocked;
            public float SnapMaxAng, SnapMaxPos;

            public bool Suspended;
            public int SuspendedAtTick = SimTicks.NEVER;
            public SimRoot SuspendedSim;
            public JointType SavedKind;
            public HVec OffA, OffB, AxisWorld;
            public float FrameA, FrameB;
            public float JointAngle0;
            public bool SavedLimit, SavedMotor, SavedCollide;
            public float SavedLower, SavedUpper, SavedMotorSpeed, SavedMotorMax;
            public float SavedFreq, SavedDamp;
        }

        private static readonly List<PinRecord> s_pins = new List<PinRecord>();

        private const int RIGID_VELOCITY_ITERATIONS = 8;
        private const int RIGID_POSITION_ITERATIONS = 6;
        private const float MOTOR_HOLD_REPORT = 0.00436f;
        private static int s_baseVelocityIterations = -1;
        private static int s_basePositionIterations = -1;
        private static bool s_solverRaised;

        private const float SNAP_EPS_ANG = 1e-5f;
        private const float SNAP_EPS_POS = 1e-5f;
        private static SceneRoot s_snapRoot;
        private static readonly System.Reflection.EventInfo s_afterPhysicsEvent =
            typeof(SceneRoot).GetEvent("AfterPhysicsUpdate");
        private static readonly EventHandler<FixedUpdateEventArgs> s_afterPhysicsHandler = OnAfterPhysics;
        private static readonly Dictionary<Body, List<PinRecord>> s_snapEdges = new Dictionary<Body, List<PinRecord>>();
        private static readonly HashSet<Body> s_snapIncoming = new HashSet<Body>();
        private static readonly HashSet<Body> s_snapDone = new HashSet<Body>();
        private static readonly Queue<Body> s_snapQueue = new Queue<Body>();
        private static readonly HashSet<PinRecord> s_snapCycleLogged = new HashSet<PinRecord>();
        private static readonly Dictionary<Body, Ship> s_snapShip = new Dictionary<Body, Ship>();
        private static readonly Dictionary<Body, List<PinRecord>> s_snapAdj = new Dictionary<Body, List<PinRecord>>();
        private static readonly List<Body> s_snapComponent = new List<Body>();
        private static readonly HashSet<Body> s_snapSeen = new HashSet<Body>();
        private static int s_snapLastPosed = -1, s_snapLastRoots = -1;

        public static void EnsureChainSnap(SceneRoot root)
        {
            if (root == null || ReferenceEquals(s_snapRoot, root)) return;
            UnhookChainSnap();
            if (s_afterPhysicsEvent == null)
            {
                Log.Info("Chain snap: SceneRoot.AfterPhysicsUpdate not found; chains are not snapped.");
                return;
            }
            s_snapRoot = root;
            s_afterPhysicsEvent.AddEventHandler(root, s_afterPhysicsHandler);
            Log.Info("Chain snap: hooked to the scene's after-physics event.");
        }

        private static void UnhookChainSnap()
        {
            if (s_snapRoot != null)
            {
                try { s_afterPhysicsEvent?.RemoveEventHandler(s_snapRoot, s_afterPhysicsHandler); }
                catch (Exception ex) { Log.Exception("HingeJointManager.UnhookChainSnap", ex); }
                s_snapRoot = null;
            }
            s_snapEdges.Clear(); s_snapIncoming.Clear(); s_snapDone.Clear(); s_snapQueue.Clear();
            s_snapCycleLogged.Clear(); s_snapShip.Clear(); s_snapAdj.Clear(); s_snapComponent.Clear(); s_snapSeen.Clear();
            s_snapLastPosed = -1; s_snapLastRoots = -1;
        }

        private static void OnAfterPhysics(object sender, FixedUpdateEventArgs e)
        {
            try { SnapChains(); }
            catch (Exception ex) { LogThrottle.Exception("HingeJointManager.SnapChains", ex); }
        }

        private static void SnapChains()
        {
            if (s_pins.Count == 0)
            {
                if (s_snapLastPosed != 0) { s_snapLastPosed = 0; s_snapLastRoots = 0; }
                return;
            }
            KeepJointAnglesContinuous();
            s_snapEdges.Clear();
            s_snapAdj.Clear();
            s_snapIncoming.Clear();
            s_snapShip.Clear();
            SimRoot sim = null;
            foreach (PinRecord pin in s_pins)
            {
                if (pin.Suspended || pin.Joint == null) continue;
                if (pin.RailDrive) continue;
                if (!(pin.Joint is RevoluteJoint) && !(pin.Joint is PrismaticJoint)) continue;
                Body a = pin.Joint.BodyA, b = pin.Joint.BodyB;
                if (a == null || b == null || a.World == null || !ReferenceEquals(a.World, b.World) || a.World.IsLocked) continue;
                AddSnapEdge(s_snapEdges, a, pin);
                AddSnapEdge(s_snapAdj, a, pin);
                AddSnapEdge(s_snapAdj, b, pin);
                s_snapIncoming.Add(b);
                if (pin.ShipA != null) s_snapShip[a] = pin.ShipA;
                if (pin.ShipB != null) s_snapShip[b] = pin.ShipB;
                if (sim == null) sim = pin.Sim;
            }
            AssemblyGraph groups = AssemblyTopology.GroupsOf(sim);
            s_snapDone.Clear();
            s_snapSeen.Clear();
            int roots = 0;
            int posed = 0;
            foreach (Body start in s_snapAdj.Keys)
            {
                if (s_snapSeen.Contains(start)) continue;
                Body root = SnapRootOf(start, groups);
                roots++;
                s_snapQueue.Clear();
                s_snapQueue.Enqueue(root);
                s_snapDone.Add(root);
                while (s_snapQueue.Count > 0)
                {
                    Body parent = s_snapQueue.Dequeue();
                    foreach (PinRecord pin in s_snapAdj[parent])
                    {
                        bool parentIsA = ReferenceEquals(pin.Joint.BodyA, parent);
                        Body child = parentIsA ? pin.Joint.BodyB : pin.Joint.BodyA;
                        if (ReferenceEquals(child, parent) || IsTreeEdge(pin, parent, child)) continue;
                        if (!s_snapDone.Add(child))
                        {
                            if (s_snapCycleLogged.Add(pin))
                                Log.Info($"Chain snap: joint at {pin.PartA?.Location} closes a loop; its far body is posed from its first parent only.");
                            continue;
                        }
                        s_snapTreeEdges.Add(pin);
                        if (SnapChild(pin, parent, child, parentIsA)) posed++;
                        s_snapQueue.Enqueue(child);
                    }
                }
            }
            s_snapTreeEdges.Clear();
            int children = s_snapIncoming.Count;
            if (children != s_snapLastPosed || roots != s_snapLastRoots)
            {
                s_snapLastPosed = children;
                s_snapLastRoots = roots;
                if (Log.VERBOSE) Log.Info($"Chain snap: {children} body(ies) from {roots} root(s) ({posed} moved this tick).");
            }
            ComputeLoads();
        }

        private static readonly HashSet<PinRecord> s_snapTreeEdges = new HashSet<PinRecord>();

        private static bool IsTreeEdge(PinRecord pin, Body parent, Body child) =>
            s_snapTreeEdges.Contains(pin) && s_snapDone.Contains(child);

        private static void AddSnapEdge(Dictionary<Body, List<PinRecord>> map, Body body, PinRecord pin)
        {
            if (!map.TryGetValue(body, out List<PinRecord> list))
            {
                list = new List<PinRecord>(2);
                map[body] = list;
            }
            list.Add(pin);
        }

        private static Body SnapRootOf(Body start, AssemblyGraph groups)
        {
            s_snapComponent.Clear();
            s_snapQueue.Clear();
            s_snapQueue.Enqueue(start);
            s_snapSeen.Add(start);
            while (s_snapQueue.Count > 0)
            {
                Body cur = s_snapQueue.Dequeue();
                s_snapComponent.Add(cur);
                foreach (PinRecord pin in s_snapAdj[cur])
                {
                    Body other = ReferenceEquals(pin.Joint.BodyA, cur) ? pin.Joint.BodyB : pin.Joint.BodyA;
                    if (s_snapSeen.Add(other)) s_snapQueue.Enqueue(other);
                }
            }
            Body best = null;
            int bestIndex = int.MaxValue;
            foreach (Body b in s_snapComponent)
            {
                int i = s_snapShip.TryGetValue(b, out Ship s) && s != null ? groups.TreeIndex(AssemblyTopology.Id(s)) : -1;
                if (i >= 0 && i < bestIndex) { best = b; bestIndex = i; }
            }
            if (best != null) return best;
            foreach (Body b in s_snapComponent)
                if (!s_snapIncoming.Contains(b)) return b;
            return start;
        }

        private static readonly Queue<Body> s_loadQueue = new Queue<Body>();
        private static readonly HashSet<Body> s_loadSeen = new HashSet<Body>();

        public static Dictionary<T, List<T>> WeldNeighbours<T>(List<(T, T)> edges)
        {
            var adj = new Dictionary<T, List<T>>();
            foreach ((T a, T b) in edges) AddWeldEdge(adj, a, b, null);
            return adj;
        }

        private static void AddWeldEdge<T>(Dictionary<T, List<T>> adj, T a, T b, Stack<List<T>> pool)
        {
            if (!adj.TryGetValue(a, out List<T> la)) adj[a] = la = pool != null && pool.Count > 0 ? pool.Pop() : new List<T>();
            la.Add(b);
            if (EqualityComparer<T>.Default.Equals(a, b)) return;
            if (!adj.TryGetValue(b, out List<T> lb)) adj[b] = lb = pool != null && pool.Count > 0 ? pool.Pop() : new List<T>();
            lb.Add(a);
        }

        private static readonly Stack<List<Body>> s_weldPool = new Stack<List<Body>>();
        private static readonly Dictionary<Body, List<Body>> s_jointAdj = new Dictionary<Body, List<Body>>();
        private static readonly HashSet<Body> s_loadSet = new HashSet<Body>();
        private static readonly List<Body> s_parentScratch = new List<Body>();

        public static List<T> ParentSide<T>(Dictionary<T, List<T>> adj, T root, HashSet<T> excluded)
        {
            var result = new List<T>();
            ParentSideInto(adj, root, excluded, result, new Queue<T>(), new HashSet<T>());
            return result;
        }

        public static List<Ship> JointSideShips(Ship docked, Ship carrier)
        {
            var adj = new Dictionary<Ship, List<Ship>>();
            foreach (PinRecord pin in s_pins)
            {
                if (pin.Suspended || pin.Joint == null || !(pin.Tag is ZtxHingeBaseComponent)) continue;
                Ship a = pin.ShipA, b = pin.ShipB;
                if (a == null || b == null || ReferenceEquals(a, b)) continue;
                if (!adj.TryGetValue(a, out List<Ship> la)) adj[a] = la = new List<Ship>();
                if (!adj.TryGetValue(b, out List<Ship> lb)) adj[b] = lb = new List<Ship>();
                la.Add(b);
                lb.Add(a);
            }
            return DockThrustMath.DockedSide(adj, docked, carrier);
        }

        public static List<Ship> ArmsFrom(Ship ship, Func<Part, bool> fromPart)
        {
            var result = new List<Ship>();
            var seen = new HashSet<Ship> { ship };
            foreach (PinRecord pin in s_pins)
            {
                if (pin.Suspended || pin.Joint == null || !(pin.Tag is ZtxHingeBaseComponent b)) continue;
                Part basePart = b.Part;
                if (basePart == null || !ReferenceEquals(basePart.Ship, ship) || !fromPart(basePart)) continue;
                Ship arm = ReferenceEquals(pin.ShipA, ship) ? pin.ShipB : pin.ShipA;
                if (arm == null || ReferenceEquals(arm, ship)) continue;
                foreach (Ship s in JointSideShips(arm, ship))
                    if (seen.Add(s)) result.Add(s);
            }
            return result;
        }

        public static void ParentSideInto<T>(Dictionary<T, List<T>> adj, T root, HashSet<T> excluded, List<T> result, Queue<T> queue, HashSet<T> seen)
        {
            result.Clear();
            queue.Clear();
            seen.Clear();
            if (excluded != null && excluded.Contains(root)) return;
            queue.Enqueue(root);
            seen.Add(root);
            while (queue.Count > 0)
            {
                T n = queue.Dequeue();
                result.Add(n);
                if (!adj.TryGetValue(n, out List<T> next)) continue;
                foreach (T o in next)
                    if ((excluded == null || !excluded.Contains(o)) && seen.Add(o)) queue.Enqueue(o);
            }
        }

        private static void ComputeLoads()
        {
            foreach (List<Body> l in s_jointAdj.Values) { l.Clear(); s_weldPool.Push(l); }
            s_jointAdj.Clear();
            foreach (PinRecord w in s_pins)
            {
                if (w.Suspended || w.Joint == null) continue;
                AddWeldEdge(s_jointAdj, w.Joint.BodyA, w.Joint.BodyB, s_weldPool);
            }
            foreach (KeyValuePair<Body, List<PinRecord>> kv in s_snapEdges)
            {
                foreach (PinRecord pin in kv.Value)
                {
                    Body first = pin.Joint?.BodyB;
                    if (first == null) continue;
                    HVec pivot = pin.Joint.WorldAnchorA;
                    float inertia = 0f, mass = 0f;
                    s_loadQueue.Clear();
                    s_loadSeen.Clear();
                    s_loadQueue.Enqueue(first);
                    s_loadSeen.Add(first);
                    while (s_loadQueue.Count > 0)
                    {
                        Body b = s_loadQueue.Dequeue();
                        float m = b.Mass;
                        HVec lc = b.LocalCenter;
                        HVec wc = b.WorldCenter;
                        float icom = b.Inertia - m * (lc.X * lc.X + lc.Y * lc.Y);
                        float dx = wc.X - pivot.X, dy = wc.Y - pivot.Y;
                        inertia += Math.Max(0f, icom) + m * (dx * dx + dy * dy);
                        mass += m;
                        if (s_snapEdges.TryGetValue(b, out List<PinRecord> outs))
                            foreach (PinRecord o in outs)
                            {
                                Body c = o.Joint?.BodyB;
                                if (c != null && s_loadSeen.Add(c)) s_loadQueue.Enqueue(c);
                            }
                    }
                    pin.LoadInertia = inertia;
                    pin.LoadMass = mass;
                    Body root = pin.Joint.BodyA;
                    float pInertia = 0f, pMass = 0f;
                    s_loadSet.Clear();
                    s_loadSet.UnionWith(s_loadSeen);
                    ParentSideInto(s_jointAdj, root, s_loadSet, s_parentScratch, s_loadQueue, s_loadSeen);
                    foreach (Body b in s_parentScratch)
                    {
                        if (b == null || b.World == null) continue;
                        float m = b.Mass;
                        HVec lc = b.LocalCenter;
                        HVec wc = b.WorldCenter;
                        float icom = b.Inertia - m * (lc.X * lc.X + lc.Y * lc.Y);
                        float dx = wc.X - pivot.X, dy = wc.Y - pivot.Y;
                        pInertia += Math.Max(0f, icom) + m * (dx * dx + dy * dy);
                        pMass += m;
                    }
                    pin.ParentInertia = pInertia;
                    pin.ParentMass = pMass;
                }
            }
        }

        private const double TWO_PI = Math.PI * 2.0;
        private const int JOINT_TURN_LOG_CAP = 30;
        private static int s_jointTurnLogs;

        private static void KeepJointAnglesContinuous()
        {
            foreach (PinRecord pin in s_pins)
            {
                if (pin.Suspended || pin.Joint == null) continue;
                Body a = pin.Joint.BodyA, b = pin.Joint.BodyB;
                if (a == null || b == null) continue;
                float reference;
                if (pin.Joint is RevoluteJoint rj) reference = rj.ReferenceAngle;
                else if (pin.Joint is PrismaticJoint pj) reference = pj.ReferenceAngle;
                else if (pin.Joint is WeldJoint wj) reference = wj.ReferenceAngle;
                else continue;
                double off = b.Rotation - a.Rotation - reference;
                if (Math.Abs(off) <= Math.PI) continue;
                double turns = Math.Round(off / TWO_PI);
                if (turns == 0.0) continue;
                float shifted = (float)(reference + turns * TWO_PI);
                if (pin.Joint is RevoluteJoint r2) r2.ReferenceAngle = shifted;
                else if (pin.Joint is PrismaticJoint p2) p2.ReferenceAngle = shifted;
                else if (pin.Joint is WeldJoint w2) w2.ReferenceAngle = shifted;
                if (s_jointTurnLogs < JOINT_TURN_LOG_CAP)
                {
                    s_jointTurnLogs++;
                    Log.Info($"Joint at {pin.PartA?.Location} ({pin.Joint.GetType().Name}): {off * 180.0 / Math.PI:F0} deg off its reference -- a body angle jumped by whole turns; re-referenced by {turns:F0} turn(s).");
                }
            }
        }

        private static readonly Queue<Body> s_carryQueue = new Queue<Body>();
        private static readonly HashSet<Body> s_carrySet = new HashSet<Body>();
        private const float CARRY_RESIST_DOT = 0.3f;

        public static void CarriedState(Body cart, HVec travel, out float maxGap, out bool resisted)
        {
            maxGap = 0f;
            resisted = false;
            if (cart == null) return;
            s_carryQueue.Clear();
            s_carrySet.Clear();
            s_carrySet.Add(cart);
            s_carryQueue.Enqueue(cart);
            while (s_carryQueue.Count > 0)
            {
                Body b = s_carryQueue.Dequeue();
                foreach (PinRecord pin in s_pins)
                {
                    if (pin.Suspended || pin.Joint == null || !ReferenceEquals(pin.Joint.BodyA, b)) continue;
                    Body c = pin.Joint.BodyB;
                    if (c == null) continue;
                    if (pin.Joint is RevoluteJoint r)
                    {
                        HVec wa = r.WorldAnchorA, wb = r.WorldAnchorB;
                        float dx = wb.X - wa.X, dy = wb.Y - wa.Y;
                        float gap = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (gap > maxGap) maxGap = gap;
                    }
                    if (s_carrySet.Add(c)) s_carryQueue.Enqueue(c);
                }
            }
            if (travel.X == 0f && travel.Y == 0f) return;
            foreach (Body body in s_carrySet)
            {
                if (ReferenceEquals(body, cart)) continue;
                for (ContactEdge e = body.ContactList; e != null; e = e.Next)
                {
                    Contact c = e.Contact;
                    if (c == null || !c.IsTouching || !c.Enabled || c.Manifold.PointCount == 0) continue;
                    Fixture fa = c.FixtureA, fb = c.FixtureB;
                    if (fa == null || fb == null || fa.IsSensor || fb.IsSensor) continue;
                    bool mineIsB = ReferenceEquals(fb.Body, body);
                    if (!mineIsB && !ReferenceEquals(fa.Body, body)) continue;
                    Fixture other = mineIsB ? fa : fb;
                    if (other.Body == null || s_carrySet.Contains(other.Body)) continue;
                    if ((other.CollisionCategories & Category.Cat1) == 0) continue;
                    c.GetWorldManifold(out System.Numerics.Vector2 normal, out _);
                    float fx = mineIsB ? normal.X : -normal.X;
                    float fy = mineIsB ? normal.Y : -normal.Y;
                    if (fx * travel.X + fy * travel.Y < -CARRY_RESIST_DOT) { resisted = true; return; }
                }
            }
        }

        private static bool SnapChild(PinRecord pin, Body parent, Body child, bool parentIsA)
        {
            float ap = parent.Rotation;
            HVec pp = parent.Position;
            float ac;
            HVec oc;
            bool rigid = false;
            if (pin.Joint is RevoluteJoint r)
            {
                rigid = pin.MotorLocked && !float.IsNaN(pin.MotorBand);
                float q = rigid && TouchingCount(child) == 0 ? pin.MotorBand : r.JointAngle;
                (oc, ac) = RevolutePose(pp, ap, r.LocalAnchorA, r.LocalAnchorB, r.ReferenceAngle, q, parentIsA);
            }
            else if (pin.Joint is PrismaticJoint sl)
            {
                rigid = pin.SliderClamped && !float.IsNaN(pin.MotorBand);
                float d = rigid ? pin.MotorBand : sl.JointTranslation;
                (oc, ac) = PrismaticPose(pp, ap, sl.LocalAnchorA, sl.LocalAnchorB, sl.LocalXAxis, sl.ReferenceAngle, d, parentIsA);
            }
            else return false;
            HVec cp = child.Position;
            float dx = oc.X - cp.X, dy = oc.Y - cp.Y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            float dAng = Math.Abs(WrapAngle(ac - child.Rotation));
            if (dAng > pin.SnapMaxAng) pin.SnapMaxAng = dAng;
            if (dist > pin.SnapMaxPos) pin.SnapMaxPos = dist;
            bool moved = false;
            if (dAng >= SNAP_EPS_ANG || dist >= SNAP_EPS_POS)
            {
                child.SetTransform((System.Numerics.Vector2)oc, ac);
                child.Awake = true;
                moved = true;
            }
            if (rigid)
            {
                HVec vp = parent.LinearVelocity;
                float wp = parent.AngularVelocity;
                HVec cpar = parent.WorldCenter;
                HVec cchi = child.WorldCenter;
                float rx = cchi.X - cpar.X, ry = cchi.Y - cpar.Y;
                child.LinearVelocity = new System.Numerics.Vector2(vp.X - wp * ry, vp.Y + wp * rx);
                child.AngularVelocity = wp;
            }
            return moved;
        }

        public static (HVec Position, float Angle) RevolutePose(HVec parentPos, float parentAngle, HVec localA, HVec localB,
            float referenceAngle, float jointAngle, bool parentIsA)
        {
            if (parentIsA)
            {
                float ab = parentAngle + referenceAngle + jointAngle;
                HVec pivot = parentPos + Rotate(localA, parentAngle);
                return (pivot - Rotate(localB, ab), ab);
            }
            float aa = parentAngle - referenceAngle - jointAngle;
            HVec pivotB = parentPos + Rotate(localB, parentAngle);
            return (pivotB - Rotate(localA, aa), aa);
        }

        public static (HVec Position, float Angle) PrismaticPose(HVec parentPos, float parentAngle, HVec localA, HVec localB,
            HVec localXAxis, float referenceAngle, float translation, bool parentIsA)
        {
            if (parentIsA)
            {
                float ab = parentAngle + referenceAngle;
                HVec axis = Rotate(localXAxis, parentAngle);
                return (parentPos + Rotate(localA, parentAngle) + axis * translation - Rotate(localB, ab), ab);
            }
            float aa = parentAngle - referenceAngle;
            HVec axisA = Rotate(localXAxis, aa);
            return (parentPos + Rotate(localB, parentAngle) - axisA * translation - Rotate(localA, aa), aa);
        }

        private static float WrapAngle(float a)
        {
            while (a > Math.PI) a -= (float)(2.0 * Math.PI);
            while (a < -Math.PI) a += (float)(2.0 * Math.PI);
            return a;
        }

        public static void SyncSolverIterations()
        {
            CaptureSolverBase();
        }

        private static void CaptureSolverBase()
        {
            if (s_baseVelocityIterations > 0) return;
            s_baseVelocityIterations = Halfling.Physics2D.Settings.VelocityIterations;
            s_basePositionIterations = Halfling.Physics2D.Settings.PositionIterations;
        }

        public static int[] SolverIterationsFor(bool simHasPins, int baseVelocity, int basePosition) =>
            simHasPins
                ? new[] { Math.Max(baseVelocity, RIGID_VELOCITY_ITERATIONS), Math.Max(basePosition, RIGID_POSITION_ITERATIONS) }
                : new[] { baseVelocity, basePosition };

        internal static void ApplySolverIterationsFor(SimRoot sim)
        {
            try
            {
                CaptureSolverBase();
                bool has = false;
                foreach (PinRecord p in s_pins)
                    if (ReferenceEquals(p.Sim, sim)) { has = true; break; }
                int[] it = SolverIterationsFor(has, s_baseVelocityIterations, s_basePositionIterations);
                Halfling.Physics2D.Settings.VelocityIterations = it[0];
                Halfling.Physics2D.Settings.PositionIterations = it[1];
                if (has != s_solverRaised)
                {
                    s_solverRaised = has;
                    Log.Info($"Physics solver: {it[0]}/{it[1]} velocity/position iterations for this sim's steps ({(has ? "joints exist" : "no joints")}).");
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("HingeJointManager.ApplySolverIterationsFor", ex);
            }
        }
        private static readonly HashSet<World> s_subscribedWorlds = new HashSet<World>();

        private static short s_nextGroup = -1;

        public static void CreateTaggedPin(object tag, Ship shipA, Ship shipB, HVec worldPivot,
            bool collideConnected, Part hingePartA = null, Part hingePartB = null,
            float? referenceAngle = null, float? lowerLimit = null, float? upperLimit = null, HVec? worldAnchorB = null)
        {
            if (tag == null || shipA == null || shipB == null || shipA == shipB) return;
            if (HasPinWithTag(tag)) return;

            Body a = shipA.Physics?.Body;
            Body b = shipB.Physics?.Body;
            if (a == null || b == null) return;
            World world = a.World;
            if (world == null || b.World != world) return;

            var joint = new RevoluteJoint(a, b, worldPivot, worldAnchorB ?? worldPivot, useWorldCoordinates: true)
            {
                CollideConnected = collideConnected,
            };
            if (referenceAngle.HasValue) joint.ReferenceAngle = referenceAngle.Value;
            if (lowerLimit.HasValue && upperLimit.HasValue)
            {
                joint.LowerLimit = lowerLimit.Value;
                joint.UpperLimit = upperLimit.Value;
                joint.LimitEnabled = true;
            }
            Subscribe(world);
            world.AddAsync(joint);
            var pin = new PinRecord { Joint = joint, ShipA = shipA, ShipB = shipB, Tag = tag, PartA = hingePartA, PartB = hingePartB };
            pin.Sim = shipA?.Sim;
            pin.World = world;
            s_pins.Add(pin);
            TopologyChanged();
            if (hingePartA != null && hingePartB != null)
                SetupCollisionFilter(pin);
            Log.Info($"Tagged pin created between ships {shipA.UniqueID} and {shipB.UniqueID} at ({worldPivot.X:F1}, {worldPivot.Y:F1}). CollideConnected={collideConnected}, group={pin.Group}.");
            InvalidateAssemblyCaches(shipA, shipB);
            LogSkeleton(shipA);
        }

        public static void CreateTaggedWeld(object tag, Ship shipA, Ship shipB, HVec worldAnchor,
            bool collideConnected, Part dockPartA = null, Part dockPartB = null)
        {
            if (tag == null || shipA == null || shipB == null || shipA == shipB) return;
            if (HasPinWithTag(tag)) return;

            Body a = shipA.Physics?.Body;
            Body b = shipB.Physics?.Body;
            if (a == null || b == null) return;
            World world = a.World;
            if (world == null || b.World != world) return;

            var joint = new WeldJoint(a, b, worldAnchor, worldAnchor, useWorldCoordinates: true)
            {
                CollideConnected = collideConnected,
            };
            Subscribe(world);
            world.AddAsync(joint);
            a.Awake = true;
            b.Awake = true;
            var pin = new PinRecord { Joint = joint, ShipA = shipA, ShipB = shipB, Tag = tag, PartA = dockPartA, PartB = dockPartB };
            pin.Sim = shipA?.Sim;
            pin.World = world;
            s_pins.Add(pin);
            TopologyChanged();
            if (dockPartA != null && dockPartB != null)
                SetupCollisionFilter(pin);
            Log.Info($"Weld created between ships {shipA.UniqueID} and {shipB.UniqueID} at ({worldAnchor.X:F1}, {worldAnchor.Y:F1}). CollideConnected={collideConnected}, group={pin.Group}.");
            InvalidateAssemblyCaches(shipA, shipB);
            LogSkeleton(shipA);
        }

        public static void CreateAlignedWeld(object tag, Ship shipA, Ship shipB,
            HVec localAnchorA, HVec localAnchorB, float referenceAngle,
            bool collideConnected, Part dockPartA, Part dockPartB)
        {
            if (tag == null || shipA == null || shipB == null || shipA == shipB) return;
            if (HasPinWithTag(tag)) return;

            Body a = shipA.Physics?.Body;
            Body b = shipB.Physics?.Body;
            if (a == null || b == null) return;
            World world = a.World;
            if (world == null || b.World != world) return;

            var joint = new WeldJoint(a, b, localAnchorA, localAnchorB, useWorldCoordinates: false)
            {
                CollideConnected = collideConnected,
                ReferenceAngle = referenceAngle,
            };
            Subscribe(world);
            world.AddAsync(joint);
            a.Awake = true;
            b.Awake = true;
            var pin = new PinRecord { Joint = joint, ShipA = shipA, ShipB = shipB, Tag = tag, PartA = dockPartA, PartB = dockPartB };
            pin.Sim = shipA?.Sim;
            pin.World = world;
            s_pins.Add(pin);
            TopologyChanged();
            if (dockPartA != null && dockPartB != null)
                SetupCollisionFilter(pin);
            Log.Info($"Aligned weld created between ships {shipA.UniqueID} and {shipB.UniqueID} (refAngle {referenceAngle:F3}). CollideConnected={collideConnected}, group={pin.Group}.");
            InvalidateAssemblyCaches(shipA, shipB);
            LogSkeleton(shipA);
        }

        public static void CreateTaggedSlider(object tag, Ship shipA, Ship shipB, HVec worldAnchorA, HVec worldAnchorB, HVec worldAxis,
            bool collideConnected, Part partA, Part partB, float referenceAngle)
        {
            if (tag == null || shipA == null || shipB == null || shipA == shipB) return;
            if (HasPinWithTag(tag)) return;

            Body a = shipA.Physics?.Body;
            Body b = shipB.Physics?.Body;
            if (a == null || b == null) return;
            World world = a.World;
            if (world == null || b.World != world) return;

            var joint = new PrismaticJoint(a, b, worldAnchorA, worldAnchorB, worldAxis, useWorldCoordinates: true)
            {
                CollideConnected = collideConnected,
                ReferenceAngle = referenceAngle,
            };
            float t0 = joint.JointTranslation;
            if (t0 < 0f) t0 = 0f;
            if (t0 <= SLIDER_RIGID_HALF) joint.SetLimits(0f, 0f);
            else joint.SetLimits(t0 - SLIDER_RIGID_HALF, t0 + SLIDER_RIGID_HALF);
            joint.LimitEnabled = true;
            Subscribe(world);
            world.AddAsync(joint);
            a.Awake = true;
            b.Awake = true;
            var pin = new PinRecord { Joint = joint, ShipA = shipA, ShipB = shipB, Tag = tag, PartA = partA, PartB = partB, MotorBand = t0 };
            pin.Sim = shipA?.Sim;
            pin.World = world;
            s_pins.Add(pin);
            TopologyChanged();
            if (partA != null && partB != null)
                SetupCollisionFilter(pin);
            Log.Info($"Slider created between ships {shipA.UniqueID} and {shipB.UniqueID} at ({worldAnchorA.X:F1}, {worldAnchorA.Y:F1}), axis ({worldAxis.X:F2}, {worldAxis.Y:F2}), extension {t0:F2}. CollideConnected={collideConnected}, group={pin.Group}.");
            InvalidateAssemblyCaches(shipA, shipB);
            LogSkeleton(shipA);
        }

        public static void CreateRailSlider(object tag, Ship shipA, Ship shipB, HVec localAnchorA, HVec localAnchorB,
            float referenceAngle, Part partA, Part partB)
        {
            if (tag == null || shipA == null || shipB == null || shipA == shipB) return;
            if (HasPinWithTag(tag)) return;
            Body a = shipA.Physics?.Body;
            Body b = shipB.Physics?.Body;
            if (a == null || b == null) return;
            World world = a.World;
            if (world == null || b.World != world) return;
            HVec worldA = a.GetWorldPoint(localAnchorA);
            HVec worldB = b.GetWorldPoint(localAnchorB);
            HVec axis = a.GetWorldVector(new HVec(1f, 0f));
            var joint = new PrismaticJoint(a, b, worldA, worldB, axis, useWorldCoordinates: true)
            {
                CollideConnected = true,
                ReferenceAngle = referenceAngle,
            };
            joint.SetLimits(0f, 0f);
            joint.LimitEnabled = true;
            Subscribe(world);
            world.AddAsync(joint);
            a.Awake = true;
            b.Awake = true;
            var pin = new PinRecord { Joint = joint, ShipA = shipA, ShipB = shipB, Tag = tag, PartA = partA, PartB = partB, RailDrive = true };
            pin.Sim = shipA?.Sim;
            pin.World = world;
            s_pins.Add(pin);
            TopologyChanged();
            if (partA != null && partB != null)
                SetupCollisionFilter(pin);
            Log.Info($"Rail slider created between ships {shipA.UniqueID} and {shipB.UniqueID} (refAngle {referenceAngle:F3}, locked along the rail). group={pin.Group}.");
            InvalidateAssemblyCaches(shipA, shipB);
            LogSkeleton(shipA);
        }

        public static void UpdateRailSlider(object tag, HVec localAnchorA, HVec worldAxis, float? referenceAngle)
        {
            PinRecord pin = FindPin(tag);
            if (pin == null || !pin.RailDrive || !(pin.Joint is PrismaticJoint slider)) return;
            bool changed = false;
            HVec cur = slider.LocalAnchorA;
            if (cur.X != localAnchorA.X || cur.Y != localAnchorA.Y)
            {
                slider.LocalAnchorA = localAnchorA;
                changed = true;
            }
            HVec localAxis = slider.BodyA.GetLocalVector(worldAxis);
            HVec have = slider.LocalXAxis;
            if (Math.Abs(localAxis.X - have.X) > 1e-5f || Math.Abs(localAxis.Y - have.Y) > 1e-5f)
            {
                slider.Axis = worldAxis;
                changed = true;
            }
            if (referenceAngle.HasValue)
            {
                float wanted = NearestReference(pin, slider.BodyA, slider.BodyB, referenceAngle.Value);
                if (slider.ReferenceAngle != wanted)
                {
                    slider.ReferenceAngle = wanted;
                    changed = true;
                }
            }
            if (changed)
            {
                slider.BodyA.Awake = true;
                slider.BodyB.Awake = true;
            }
        }

        public static bool IsRailDriven(Ship ship)
        {
            if (ship == null) return false;
            foreach (PinRecord pin in s_pins)
                if (pin.RailDrive && !pin.Suspended && ReferenceEquals(pin.ShipB, ship)) return true;
            return false;
        }

        public static void SetRailThrusting(object tag, bool on)
        {
            PinRecord pin = FindPin(tag);
            if (pin != null && pin.RailDrive) pin.RailThrust = on;
        }

        public static bool IsRailThrusting(Ship ship)
        {
            if (ship == null) return false;
            foreach (PinRecord pin in s_pins)
                if (pin.RailDrive && pin.RailThrust && !pin.Suspended && ReferenceEquals(pin.ShipB, ship)) return true;
            return false;
        }

        public static float AlignRailSlider(object tag)
        {
            PinRecord pin = FindPin(tag);
            if (pin == null || !pin.RailDrive || !(pin.Joint is PrismaticJoint slider)) return 0f;
            Body a = slider.BodyA, b = slider.BodyB;
            if (a == null || b == null) return 0f;
            float err = slider.ReferenceAngle - (b.Rotation - a.Rotation);
            if (Math.Abs(err) < 1e-5f) return 0f;
            HVec pivot = slider.WorldAnchorB;
            HVec pos = b.Position;
            float c = (float)DetMath.Cos(err), s = (float)DetMath.Sin(err);
            float rx = pos.X - pivot.X, ry = pos.Y - pivot.Y;
            var np = new HVec(pivot.X + rx * c - ry * s, pivot.Y + rx * s + ry * c);
            b.SetTransform((System.Numerics.Vector2)np, b.Rotation + err);
            b.Awake = true;
            return err;
        }

        public static string RailProbe(object tag, float invDt, float mass, float scale)
        {
            PinRecord pin = FindPin(tag);
            if (pin == null || !pin.RailDrive || !(pin.Joint is PrismaticJoint slider)) return "no rail slider";
            Body a = slider.BodyA, b = slider.BodyB;
            if (a == null || b == null) return "no bodies";
            float sc = scale > 0f ? scale : 1f;
            float per = 1f / Math.Max(1e-4f, mass * sc);
            HVec ax = a.GetWorldVector(slider.LocalXAxis);
            HVec reaction = slider.GetReactionForce(invDt);
            float rAlong = reaction.X * ax.X + reaction.Y * ax.Y;
            float rAcross = ax.X * reaction.Y - ax.Y * reaction.X;
            float motor = slider.GetMotorForce(invDt);
            HVec anchor = slider.WorldAnchorB;
            var sb = new System.Text.StringBuilder();
            sb.Append($"joint: translation {slider.JointTranslation / sc:F3}, speed {slider.JointSpeed / sc:F2}, motor {(slider.MotorEnabled ? "on" : "off")} at {slider.MotorSpeed / sc:F2} cap {slider.MaxMotorForce * per:F2} gives {motor * per:F2}, limits {slider.LowerLimit / sc:F1}..{slider.UpperLimit / sc:F1} {(slider.LimitEnabled ? "on" : "off")} give {(rAlong - motor) * per:F2}; reaction along {rAlong * per:F2}, across {rAcross * per:F2}, torque {slider.GetReactionTorque(invDt) * per / sc:F2}; angle {(b.Rotation - a.Rotation - slider.ReferenceAngle) * 57.29578f:F1} deg off the reference; contacts:");
            int n = 0;
            for (ContactEdge e = b.ContactList; e != null; e = e.Next)
            {
                Contact c = e.Contact;
                if (c == null || !c.IsTouching || c.Manifold.PointCount == 0) continue;
                Fixture fa = c.FixtureA, fb = c.FixtureB;
                if (fa == null || fb == null) continue;
                bool mineIsB = ReferenceEquals(fb.Body, b);
                Fixture mine = mineIsB ? fb : fa, other = mineIsB ? fa : fb;
                c.GetWorldManifold(out System.Numerics.Vector2 normal, out Halfling.Physics2D.Common.FixedArray2<System.Numerics.Vector2> pts);
                float nx = mineIsB ? normal.X : -normal.X, ny = mineIsB ? normal.Y : -normal.Y;
                float imp = 0f;
                for (int i = 0; i < c.Manifold.PointCount && i < 2; i++) imp += c.Manifold.Points[i].NormalImpulse;
                HVec lp = a.GetLocalVector(new HVec(pts[0].X - anchor.X, pts[0].Y - anchor.Y));
                string who = ReferenceEquals(other.Body, a) ? "carrier" : $"body {other.Body?.GetHashCode() ?? 0:X}";
                sb.Append($" [{who}{(c.Enabled ? "" : " DISABLED")}{(other.IsSensor ? " sensor" : "")} cat {(int)other.CollisionCategories:X}/{(int)mine.CollisionCategories:X} group {other.CollisionGroup}/{mine.CollisionGroup} at ({lp.X / sc:F2}, {lp.Y / sc:F2}) push {imp * invDt * per:F2} along {nx * ax.X + ny * ax.Y:F2}]");
                n++;
            }
            if (n == 0) sb.Append(" none");
            return sb.ToString();
        }

        public static bool TryGetRailAngles(object tag, out float relative, out float reference)
        {
            relative = reference = 0f;
            PinRecord pin = FindPin(tag);
            if (pin == null || !pin.RailDrive || !(pin.Joint is PrismaticJoint slider) || slider.BodyA == null || slider.BodyB == null) return false;
            relative = slider.BodyB.Rotation - slider.BodyA.Rotation;
            reference = slider.ReferenceAngle;
            return true;
        }

        public static bool TryGetRailAnchor(object tag, out HVec anchor)
        {
            anchor = default;
            PinRecord pin = FindPin(tag);
            if (pin == null || !pin.RailDrive || !(pin.Joint is PrismaticJoint slider)) return false;
            anchor = slider.WorldAnchorB;
            return true;
        }

        public static void ApplyRailSwing(object tag, float omega, float alpha, HVec worldTangent, float alongForce, float alongDeltaV, IReadOnlyList<Ship> load)
        {
            PinRecord pin = FindPin(tag);
            if (pin == null || !pin.RailDrive || !(pin.Joint is PrismaticJoint slider)) return;
            Body a = slider.BodyA, b = slider.BodyB;
            if (a == null || b == null) return;
            HVec anchor = slider.WorldAnchorB;
            float fx = 0f, fy = 0f, alongT = 0f, torque = 0f, mass = 0f;
            void Add(Body body)
            {
                mass += body.Mass;
                HVec com = body.WorldCenter;
                float rx = com.X - anchor.X, ry = com.Y - anchor.Y;
                RailThrustMath.SwingForces(rx, ry, omega, alpha, body.Mass, body.IntertiaAboutCentroid, worldTangent.X, worldTangent.Y,
                    out float bx, out float by, out float bt, out float bq);
                fx += bx; fy += by; alongT += bt; torque += bq;
            }
            Add(b);
            if (load != null)
                foreach (Ship s in load)
                {
                    Body sb = s?.Physics?.Body;
                    if (sb != null && !ReferenceEquals(sb, a) && !ReferenceEquals(sb, b)) Add(sb);
                }
            if (!float.IsNaN(alongForce) && !float.IsInfinity(alongForce))
                b.ApplyForce(new HVec(alongForce * worldTangent.X, alongForce * worldTangent.Y), b.WorldCenter);
            if (!float.IsNaN(alongDeltaV) && !float.IsInfinity(alongDeltaV) && alongDeltaV != 0f)
                b.ApplyLinearImpulse(new HVec(mass * alongDeltaV * worldTangent.X, mass * alongDeltaV * worldTangent.Y), b.WorldCenter);
            float nx = fx - alongT * worldTangent.X, ny = fy - alongT * worldTangent.Y;
            if (float.IsNaN(nx + ny + torque) || float.IsInfinity(nx + ny + torque)) return;
            a.ApplyForce(new HVec(-nx, -ny), anchor);
            a.ApplyTorque(-torque);
        }

        public static bool TryGetRailMotion(object tag, out float translation, out float speed)
        {
            translation = 0f;
            speed = 0f;
            PinRecord pin = FindPin(tag);
            if (pin == null || !pin.RailDrive || !(pin.Joint is PrismaticJoint slider)) return false;
            translation = slider.JointTranslation;
            speed = slider.JointSpeed;
            return true;
        }

        public static void SetRailMotor(object tag, bool free, float lower, float upper, float motorSpeed, float maxForce)
        {
            PinRecord pin = FindPin(tag);
            if (pin == null || !pin.RailDrive || !(pin.Joint is PrismaticJoint slider)) return;
            if (free)
            {
                if (upper - lower < 0.02f) { lower -= 0.01f; upper += 0.01f; }
                if (slider.LowerLimit != lower || slider.UpperLimit != upper) slider.SetLimits(lower, upper);
                slider.LimitEnabled = true;
                slider.MaxMotorForce = maxForce;
                slider.MotorSpeed = motorSpeed;
                slider.MotorEnabled = true;
            }
            else
            {
                if (slider.LowerLimit != 0f || slider.UpperLimit != 0f) slider.SetLimits(0f, 0f);
                slider.LimitEnabled = true;
                slider.MotorEnabled = false;
            }
        }

        private const float SLIDER_RIGID_HALF = 0.012f;
        private const float SLIDER_FOLLOW_LEAD = 0.3f;
        private const float SLIDER_GAIN = 6f;
        private const float SLIDER_ARRIVE = 0.05f;
        private const int SLIDER_STALL_TICKS = 15;
        private const float SLIDER_STALL_MOVE = 0.003f;
        private const float SLIDER_BACKOFF = 0.2f;
        private const float SLIDER_FORCE_PER_MASS = 12f;

        public static void UpdateSliderPositionHold(object tag, float targetTranslation, float maxTranslation, float strengthFrac, float rate, float accel)
        {
            PinRecord pin = FindPin(tag);
            if (pin == null || !(pin.Joint is PrismaticJoint slider)) return;

            if (maxTranslation < 0f) maxTranslation = 0f;
            if (targetTranslation < 0f) targetTranslation = 0f;
            else if (targetTranslation > maxTranslation) targetTranslation = maxTranslation;
            if (rate < 0.05f) rate = 0.05f;

            float actual = slider.JointTranslation;
            float band = pin.MotorBand;
            if (float.IsNaN(band)) band = actual;
            AdvanceBand(pin, ref band, targetTranslation, rate, accel);
            float slideAuthority = SLIDER_FORCE_PER_MASS * Math.Max(0.05f, strengthFrac);
            float lead = Math.Max(SLIDER_FOLLOW_LEAD, Math.Max(rate / SLIDER_GAIN, rate * rate / (2f * slideAuthority)) + 0.1f);
            if (band > actual + lead) band = actual + lead;
            else if (band < actual - lead) band = actual - lead;
            if (band < 0f) band = 0f;
            else if (band > maxTranslation) band = maxTranslation;

            float moved = float.IsNaN(pin.LastTranslation) ? 1f : Math.Abs(actual - pin.LastTranslation);
            pin.LastTranslation = actual;
            bool saturated = Math.Abs(band - actual) >= lead - 0.001f && band != targetTranslation;
            if (saturated && moved < SLIDER_STALL_MOVE) pin.StallTicks++;
            else pin.StallTicks = 0;
            if (pin.StallTicks >= SLIDER_STALL_TICKS)
            {
                pin.StallTicks = 0;
                band = band > actual ? Math.Max(0f, actual - SLIDER_BACKOFF)
                                     : Math.Min(maxTranslation, actual + SLIDER_BACKOFF);
            }
            pin.MotorBand = band;

            bool rigid = strengthFrac >= 0.999f;
            bool arrived = rigid && band == targetTranslation
                && Math.Abs(targetTranslation - actual) <= SLIDER_ARRIVE;
            pin.SliderClamped = arrived;
            if (arrived)
            {
                if (targetTranslation <= SLIDER_RIGID_HALF)
                    SetSliderLimits(slider, 0f, 0f);
                else
                    SetSliderLimits(slider, targetTranslation - SLIDER_RIGID_HALF, targetTranslation + SLIDER_RIGID_HALF);
            }
            else
            {
                SetSliderLimits(slider, 0f, maxTranslation);
            }

            if (strengthFrac <= 0f)
            {
                if (slider.MotorEnabled) slider.MotorEnabled = false;
                return;
            }
            float error = band - actual;
            float speed = SLIDER_GAIN * error;
            float stopCap = (float)Math.Sqrt(2.0 * slideAuthority * Math.Abs(error)) + 0.02f;
            float cap = Math.Min(rate, stopCap);
            if (speed > cap) speed = cap;
            else if (speed < -cap) speed = -cap;
            slider.MotorSpeed = speed;
            float ma = pin.ParentMass > 0f ? pin.ParentMass : slider.BodyA.Mass;
            float mb = pin.LoadMass > 0f ? pin.LoadMass : slider.BodyB.Mass;
            float reduced = (ma > 0f && mb > 0f) ? (ma * mb / (ma + mb)) : Math.Max(ma, mb);
            slider.MaxMotorForce = strengthFrac * reduced * SLIDER_FORCE_PER_MASS;
            if (!slider.MotorEnabled) slider.MotorEnabled = true;
        }

        private static void SetSliderLimits(PrismaticJoint slider, float lower, float upper)
        {
            if (slider.LowerLimit != lower || slider.UpperLimit != upper)
                slider.SetLimits(lower, upper);
            if (!slider.LimitEnabled) slider.LimitEnabled = true;
        }

        public static bool TryGetPinShips(object tag, out Ship shipA, out Ship shipB)
        {
            shipA = null;
            shipB = null;
            foreach (PinRecord pin in s_pins)
            {
                if (!ReferenceEquals(pin.Tag, tag) || pin.Suspended) continue;
                shipA = pin.ShipA;
                shipB = pin.ShipB;
                return shipA != null && shipB != null;
            }
            return false;
        }

        public static bool TryGetSliderState(object tag, out float translation, out float speed)
        {
            translation = 0f;
            speed = 0f;
            PinRecord pin = FindPin(tag);
            if (pin == null || !(pin.Joint is PrismaticJoint slider)) return false;
            translation = slider.JointTranslation;
            speed = slider.JointSpeed;
            return true;
        }

        private static float NearestReference(PinRecord pin, Body a, Body b, float referenceAngle)
        {
            double rel = b.Rotation - a.Rotation;
            double turns = Math.Round((rel - referenceAngle) / TWO_PI);
            if (turns != pin.WeldTurns)
            {
                Log.Info($"Rail joint: commanded angle re-aligned by {turns - pin.WeldTurns:F0} turn(s) to the cart's real angle (a body angle jumped by whole turns).");
                pin.WeldTurns = turns;
            }
            return (float)(referenceAngle + turns * TWO_PI);
        }

        public static bool TryGetWeldState(object tag, out float anchorError, out float relSpeed)
        {
            anchorError = 0f;
            relSpeed = 0f;
            PinRecord pin = FindPin(tag);
            if (pin == null || pin.Joint == null || !(pin.Joint is WeldJoint || pin.RailDrive)) return false;
            Joint weld = pin.Joint;
            HVec wa = weld.WorldAnchorA;
            HVec wb = weld.WorldAnchorB;
            float dx = wb.X - wa.X, dy = wb.Y - wa.Y;
            anchorError = (float)Math.Sqrt(dx * dx + dy * dy);
            HVec va = weld.BodyA.LinearVelocity;
            HVec vb = weld.BodyB.LinearVelocity;
            float vx = vb.X - va.X, vy = vb.Y - va.Y;
            relSpeed = (float)Math.Sqrt(vx * vx + vy * vy);
            return true;
        }

        public struct RodSegment
        {
            public HVec A;
            public HVec B;
            public int Rods;
            public float Scale;
            public float TailCells;
        }

        private const string PISTON_HEAD_ID = "ztx.hinges_pistons_docking.piston_head";
        private const string CORRIDOR_PISTON_HEAD_ID = "ztx.hinges_pistons_docking.corridor_piston_head";

        public const float HEAD_ROD_CELLS = 10f;

        public static bool TryGetTipExtensionCells(Part tip, out float cells)
        {
            cells = 0f;
            if (tip == null) return false;
            foreach (PinRecord pin in s_pins)
            {
                if (!(pin.Joint is PrismaticJoint slider)) continue;
                if (pin.RailDrive) continue;
                Part basePart = (pin.Tag as ZtxPistonBaseComponent)?.Part;
                Part other = pin.PartA == basePart ? pin.PartB : pin.PartA;
                if (other != tip) continue;
                float scale = basePart?.Ship != null ? basePart.Ship.Scale.X : 1f;
                cells = slider.JointTranslation / (scale > 0f ? scale : 1f);
                return true;
            }
            return false;
        }

        public static void GetSliderRods(List<RodSegment> rods)
        {
            rods.Clear();
            foreach (PinRecord pin in s_pins)
            {
                if (!(pin.Joint is PrismaticJoint slider)) continue;
                if (pin.RailDrive) continue;
                Part pa = pin.PartA;
                Part pb = pin.PartB;
                if (pa?.Ship == null || pb?.Ship == null) continue;
                if (slider.JointTranslation <= 0.02f) continue;
                var piston = pin.Tag as ZtxPistonBaseComponent;
                Part basePart = piston?.Part;
                if (basePart != null && basePart == pb) { pb = pa; pa = basePart; }
                rods.Add(new RodSegment
                {
                    A = pa.WorldPhysicalCenter,
                    B = pb.WorldPhysicalCenter,
                    Rods = piston?.Rods.Count ?? 0,
                    Scale = pa.Ship.Scale.X,
                    TailCells = pb.Rules.ID.ToString() == PISTON_HEAD_ID || pb.Rules.ID.ToString() == CORRIDOR_PISTON_HEAD_ID ? HEAD_ROD_CELLS : 0f,
                });
            }
        }

        private static PinRecord FindPin(object tag)
        {
            if (tag == null) return null;
            foreach (PinRecord pin in s_pins)
                if (ReferenceEquals(pin.Tag, tag))
                    return pin;
            return null;
        }

        public static void UpdateAngleLimits(object tag, float lowerLimit, float upperLimit)
        {
            if (tag == null) return;
            foreach (PinRecord pin in s_pins)
            {
                if (!ReferenceEquals(pin.Tag, tag)) continue;
                if (pin.Joint is RevoluteJoint revolute)
                {
                    if (revolute.LowerLimit != lowerLimit) revolute.LowerLimit = lowerLimit;
                    if (revolute.UpperLimit != upperLimit) revolute.UpperLimit = upperLimit;
                    if (!revolute.LimitEnabled) revolute.LimitEnabled = true;
                }
                return;
            }
        }

        private const float MOTOR_SERVO_GAIN = 15f;
        private const float MOTOR_FOLLOW_LEAD = 0.15f;
        private const float MOTOR_ARRIVE = 0.02f;
        private const float MOTOR_TORQUE_PER_INERTIA = 12f;
        private const float MOTOR_CONTACT_CLEAR = 0.2f;
        private const float MOTOR_ENVELOPE = (float)Math.PI / 2f;
        private const float FIXED_DT = 1f / 30f;

        private static void AdvanceBand(PinRecord pin, ref float band, float target, float maxRate, float accel)
        {
            float diff = target - band;
            if (float.IsInfinity(accel) || accel <= 0f)
            {
                float step = maxRate * FIXED_DT;
                if (diff > step) { band += step; pin.BandRate = maxRate; }
                else if (diff < -step) { band -= step; pin.BandRate = -maxRate; }
                else { band = target; pin.BandRate = diff / FIXED_DT; }
                return;
            }
            float dist = Math.Abs(diff);
            float dir = diff > 0f ? 1f : (diff < 0f ? -1f : 0f);
            float v = pin.BandRate;
            float vStop = (float)Math.Sqrt(2.0 * accel * dist);
            float vTarget = dir * Math.Min(maxRate, vStop);
            float dv = accel * FIXED_DT;
            if (v < vTarget - dv) v += dv;
            else if (v > vTarget + dv) v -= dv;
            else v = vTarget;
            float move = v * FIXED_DT;
            if (dir != 0f && move * dir >= dist) { band = target; v = 0f; }
            else band += move;
            pin.BandRate = v;
        }

        public static void UpdateMotorPositionHold(object tag, float targetAngle, float strengthFrac, float maxRate, float accel)
        {
            if (tag == null) return;
            PinRecord pin = null;
            foreach (PinRecord p in s_pins)
                if (ReferenceEquals(p.Tag, tag)) { pin = p; break; }
            if (pin == null || !(pin.Joint is RevoluteJoint revolute)) return;

            if (targetAngle > MOTOR_ENVELOPE) targetAngle = MOTOR_ENVELOPE;
            else if (targetAngle < -MOTOR_ENVELOPE) targetAngle = -MOTOR_ENVELOPE;
            pin.MotorTarget = targetAngle;
            if (maxRate < 0.01f) maxRate = 0.01f;

            float actual = revolute.JointAngle;
            float band = pin.MotorBand;
            if (float.IsNaN(band)) { band = actual; pin.BandRate = 0f; }
            float authority = MOTOR_TORQUE_PER_INERTIA * Math.Max(0.05f, strengthFrac);
            bool latched = pin.MotorLocked && strengthFrac >= 0.999f && targetAngle == pin.LockedTarget && !float.IsNaN(pin.MotorBand);
            float dir = targetAngle > band ? 1f : (targetAngle < band ? -1f : 0f);
            pin.Blocked = !latched && dir != 0f && ContactResists(revolute, dir);
            if (latched)
            {
                band = targetAngle;
                pin.BandRate = 0f;
            }
            else if (pin.Blocked)
            {
                band = actual;
                pin.BandRate = 0f;
            }
            else
            {
                AdvanceBand(pin, ref band, targetAngle, maxRate, accel);
                float lead = Math.Max(MOTOR_FOLLOW_LEAD, Math.Max(maxRate / MOTOR_SERVO_GAIN, maxRate * maxRate / (2f * authority)) + 0.05f);
                if (band > actual + lead) band = actual + lead;
                else if (band < actual - lead) band = actual - lead;
            }
            pin.MotorBand = band;

            pin.Driven100 = strengthFrac >= 0.999f;
            if (strengthFrac <= 0f)
            {
                pin.MotorLocked = false;
                pin.LockedTarget = float.NaN;
                OpenEnvelope(revolute);
                if (revolute.MotorEnabled) revolute.MotorEnabled = false;
                return;
            }
            bool arrived = latched || (pin.Driven100 && band == targetAngle && pin.BandRate == 0f
                && Math.Abs(actual - targetAngle) <= MOTOR_ARRIVE);
            if (arrived)
            {
                pin.MotorLocked = true;
                pin.LockedTarget = targetAngle;
                if (revolute.LowerLimit != band) revolute.LowerLimit = band;
                if (revolute.UpperLimit != band) revolute.UpperLimit = band;
                if (!revolute.LimitEnabled) revolute.LimitEnabled = true;
                revolute.MotorSpeed = 0f;
                if (!revolute.MotorEnabled) revolute.MotorEnabled = true;
            }
            else
            {
                pin.MotorLocked = false;
                pin.LockedTarget = float.NaN;
                OpenEnvelope(revolute);
                float e = band - actual;
                float pTerm = MOTOR_SERVO_GAIN * e;
                float stopCap = (float)Math.Sqrt(2.0 * authority * Math.Abs(e)) + 0.05f;
                if (pTerm > stopCap) pTerm = stopCap;
                else if (pTerm < -stopCap) pTerm = -stopCap;
                float speed = pin.BandRate + pTerm;
                float cap = maxRate * 2f + 1f;
                if (speed > cap) speed = cap;
                else if (speed < -cap) speed = -cap;
                revolute.MotorSpeed = speed;
                float ia = pin.ParentInertia > 0f ? pin.ParentInertia : revolute.BodyA.Inertia;
                float ib = pin.LoadInertia > 0f ? pin.LoadInertia : revolute.BodyB.Inertia;
                float reduced = (ia > 0f && ib > 0f) ? (ia * ib / (ia + ib)) : Math.Max(ia, ib);
                revolute.MaxMotorTorque = strengthFrac * reduced * MOTOR_TORQUE_PER_INERTIA;
                if (!revolute.MotorEnabled) revolute.MotorEnabled = true;
            }
            if (pin.Driven100)
            {
                float holdErr = Math.Abs(actual - band);
                if (holdErr > pin.HoldErrMax) pin.HoldErrMax = holdErr;
                if (++pin.HoldTicks >= 30)
                {
                    if (Log.VERBOSE && (pin.HoldErrMax > MOTOR_HOLD_REPORT || pin.SnapMaxAng > MOTOR_HOLD_REPORT || pin.Blocked))
                        Log.Info($"Motor hold at {pin.PartA?.Location}: max error {pin.HoldErrMax * 180.0 / Math.PI:F2} deg this second (band {band * 180.0 / Math.PI:F1} deg, rate {pin.BandRate * 180.0 / Math.PI:F0} deg/s, locked {(pin.MotorLocked ? "yes" : "no")}, blocked {(pin.Blocked ? "yes" : "no")}, touching {TouchingCount(revolute.BodyB)}, jspeed {revolute.JointSpeed * 180.0 / Math.PI:F1} deg/s, load inertia {pin.LoadInertia:F0} on parent {pin.ParentInertia:F0}, snap max {pin.SnapMaxAng * 180.0 / Math.PI:F2} deg / {pin.SnapMaxPos:F3} units, solver {Halfling.Physics2D.Settings.VelocityIterations}/{Halfling.Physics2D.Settings.PositionIterations}).");
                    pin.HoldTicks = 0;
                    pin.HoldErrMax = 0f;
                    pin.SnapMaxAng = 0f;
                    pin.SnapMaxPos = 0f;
                }
            }
        }

        private static readonly Queue<Body> s_holdQueue = new Queue<Body>();
        private static readonly HashSet<Body> s_holdSet = new HashSet<Body>();
        private static bool ContactResists(RevoluteJoint r, float dir)
        {
            Body arm = r.BodyB;
            if (arm == null) return false;
            HVec pivot = r.WorldAnchorA;
            s_holdQueue.Clear();
            s_holdSet.Clear();
            s_holdQueue.Enqueue(arm);
            s_holdSet.Add(arm);
            while (s_holdQueue.Count > 0)
            {
                Body b = s_holdQueue.Dequeue();
                if (s_snapEdges.TryGetValue(b, out List<PinRecord> outs))
                    foreach (PinRecord o in outs)
                    {
                        Body c = o.Joint?.BodyB;
                        if (c != null && s_holdSet.Add(c)) s_holdQueue.Enqueue(c);
                    }
            }
            foreach (Body body in s_holdSet)
            {
                for (ContactEdge e = body.ContactList; e != null; e = e.Next)
                {
                    Contact c = e.Contact;
                    if (c == null || !c.IsTouching || !c.Enabled || c.Manifold.PointCount == 0) continue;
                    Fixture fa = c.FixtureA, fb = c.FixtureB;
                    if (fa == null || fb == null || fa.IsSensor || fb.IsSensor) continue;
                    bool mineIsB = ReferenceEquals(fb.Body, body);
                    if (!mineIsB && !ReferenceEquals(fa.Body, body)) continue;
                    Body other = mineIsB ? fa.Body : fb.Body;
                    if (other == null || ReferenceEquals(other, body) || s_holdSet.Contains(other)) continue;
                    c.GetWorldManifold(out System.Numerics.Vector2 normal, out Halfling.Physics2D.Common.FixedArray2<System.Numerics.Vector2> pts);
                    float fx = mineIsB ? normal.X : -normal.X;
                    float fy = mineIsB ? normal.Y : -normal.Y;
                    int n = c.Manifold.PointCount;
                    for (int i = 0; i < n && i < 2; i++)
                    {
                        float rx = pts[i].X - pivot.X, ry = pts[i].Y - pivot.Y;
                        float lever = dir * (rx * fy - ry * fx);
                        float rlen = (float)Math.Sqrt(rx * rx + ry * ry);
                        if (lever < MOTOR_CONTACT_CLEAR * rlen) return true;
                    }
                }
            }
            return false;
        }

        private static int TouchingCount(Body body)
        {
            int n = 0;
            for (ContactEdge e = body?.ContactList; e != null; e = e.Next)
            {
                Contact c = e.Contact;
                if (c != null && c.IsTouching && c.Manifold.PointCount > 0) n++;
            }
            return n;
        }

        private static void OpenEnvelope(RevoluteJoint revolute)
        {
            if (revolute.LowerLimit != -MOTOR_ENVELOPE) revolute.LowerLimit = -MOTOR_ENVELOPE;
            if (revolute.UpperLimit != MOTOR_ENVELOPE) revolute.UpperLimit = MOTOR_ENVELOPE;
            if (!revolute.LimitEnabled) revolute.LimitEnabled = true;
        }

        public static void ResetMotorBand(object tag)
        {
            if (tag == null) return;
            foreach (PinRecord p in s_pins)
                if (ReferenceEquals(p.Tag, tag)) { p.MotorBand = float.NaN; p.BandRate = 0f; p.MotorLocked = false; p.LockedTarget = float.NaN; return; }
        }

        public static void DisableMotor(object tag)
        {
            RevoluteJoint revolute = FindRevolute(tag);
            if (revolute != null && revolute.MotorEnabled)
                revolute.MotorEnabled = false;
            foreach (PinRecord p in s_pins)
                if (ReferenceEquals(p.Tag, tag)) { p.MotorLocked = false; p.LockedTarget = float.NaN; break; }
        }

        public static bool TryGetJointState(object tag, out float angle, out float relAngVel)
        {
            angle = 0f;
            relAngVel = 0f;
            RevoluteJoint revolute = FindRevolute(tag);
            if (revolute == null) return false;
            angle = revolute.JointAngle;
            relAngVel = revolute.BodyB.AngularVelocity - revolute.BodyA.AngularVelocity;
            return true;
        }

        internal static void CollectDockPins(List<(object Tag, Ship A, Ship B, bool Separate)> into, SimRoot sim)
        {
            into.Clear();
            foreach (PinRecord pin in s_pins)
            {
                if (pin.Suspended || pin.Joint == null || (sim != null && pin.Sim != null && !ReferenceEquals(pin.Sim, sim))) continue;
                if (!(pin.Tag is ZtxDockClampComponent) && !(pin.Tag is ZtxDockPadComponent)) continue;
                into.Add((pin.Tag, pin.ShipA, pin.ShipB, pin.Separate));
            }
        }

        public static void SetDockSeparate(object tag, bool separate, Ship visitor)
        {
            foreach (PinRecord pin in s_pins)
            {
                if (!ReferenceEquals(pin.Tag, tag)) continue;
                Ship v = separate ? visitor : null;
                if (pin.Separate == separate && ReferenceEquals(pin.Visitor, v)) return;
                pin.Separate = separate;
                pin.Visitor = v;
                TopologyChanged();
                Log.Info($"Dock join: the ship docked at {(tag as PartComponent)?.Part?.Location} is now {(separate ? "its own ship (separate)" : "joined as one ship")}.");
                return;
            }
        }

        public static void GetAssemblyShips(Ship ship, List<Ship> result, bool rigidOnly = false, bool identity = false)
        {
            result.Clear();
            if (ship == null) return;
            result.Add(ship);
            if (s_pins.Count == 0) return;
            Adjacency adj = GetAdjacency();
            HashSet<Ship> seen = t_seen ?? (t_seen = new HashSet<Ship>());
            seen.Clear();
            seen.Add(ship);
            for (int i = 0; i < result.Count; i++)
            {
                Ship cur = result[i];
                if (adj.Live.TryGetValue(cur, out List<PinRecord> pins))
                {
                    foreach (PinRecord pin in pins)
                    {
                        if (rigidOnly && !IsRigidPin(pin)) continue;
                        if (identity && pin.Separate) continue;
                        Ship other = pin.ShipA == cur ? pin.ShipB : pin.ShipA;
                        if (other != null && seen.Add(other)) result.Add(other);
                    }
                }
                if (!rigidOnly && adj.Suspended.Count > 0)
                {
                    foreach (PinRecord pin in adj.Suspended)
                    {
                        if (identity && pin.Separate) continue;
                        Ship a = pin.PartA?.Ship, b = pin.PartB?.Ship;
                        if (a == null || b == null || ReferenceEquals(a, b)) continue;
                        Ship other = a == cur ? b : (b == cur ? a : null);
                        if (other != null && seen.Add(other)) result.Add(other);
                    }
                }
            }
        }

        public static void CartLoadShips(Ship cartShip, Ship hostShip, object railTag, List<Ship> result, out bool attached)
        {
            result.Clear();
            attached = false;
            if (cartShip == null) return;
            result.Add(cartShip);
            if (s_pins.Count == 0) return;
            Adjacency adj = GetAdjacency();
            var seen = new HashSet<Ship> { cartShip };
            for (int i = 0; i < result.Count; i++)
            {
                if (!adj.Live.TryGetValue(result[i], out List<PinRecord> pins)) continue;
                foreach (PinRecord pin in pins)
                {
                    if (ReferenceEquals(pin.Tag, railTag)) continue;
                    Ship other = pin.ShipA == result[i] ? pin.ShipB : pin.ShipA;
                    if (other == null || !seen.Add(other)) continue;
                    if (other == hostShip)
                    {
                        attached = true;
                        continue;
                    }
                    result.Add(other);
                }
            }
        }

        private static int s_topologyVersion;
        public static int TopologyVersion => s_topologyVersion;

        internal static bool CollectAssemblyEdges(List<(Ship A, Ship B, bool SameShip)> into, SimRoot sim = null)
        {
            into.Clear();
            bool anySuspended = false;
            foreach (PinRecord pin in s_pins)
            {
                if (sim != null && pin.Sim != null && !ReferenceEquals(pin.Sim, sim)) continue;
                if (pin.Separate) continue;
                bool same = pin.Tag is ZtxHingeBaseComponent || pin.Tag is ZtxRailCartComponent;
                Ship a, b;
                if (pin.Suspended)
                {
                    anySuspended = true;
                    a = pin.PartA?.Ship;
                    b = pin.PartB?.Ship;
                }
                else
                {
                    if (pin.Joint == null) continue;
                    a = pin.ShipA;
                    b = pin.ShipB;
                }
                if (a == null || b == null || ReferenceEquals(a, b)) continue;
                into.Add((a, b, same));
            }
            return anySuspended;
        }
        private static void TopologyChanged() { s_topologyVersion++; }

        private sealed class Adjacency
        {
            public int Version;
            public readonly Dictionary<Ship, List<PinRecord>> Live = new Dictionary<Ship, List<PinRecord>>();
            public readonly List<PinRecord> Suspended = new List<PinRecord>();
        }
        private static Adjacency s_adjacency;
        [ThreadStatic] private static HashSet<Ship> t_seen;

        private static Adjacency GetAdjacency()
        {
            Adjacency a = s_adjacency;
            int v = s_topologyVersion;
            if (a != null && a.Version == v) return a;
            a = new Adjacency { Version = v };
            foreach (PinRecord pin in s_pins)
            {
                if (pin.Suspended) { a.Suspended.Add(pin); continue; }
                if (pin.Joint == null || pin.ShipA == null || pin.ShipB == null) continue;
                AddEdge(a.Live, pin.ShipA, pin);
                AddEdge(a.Live, pin.ShipB, pin);
            }
            s_adjacency = a;
            return a;
        }

        private static void AddEdge(Dictionary<Ship, List<PinRecord>> map, Ship ship, PinRecord pin)
        {
            if (!map.TryGetValue(ship, out List<PinRecord> list)) map[ship] = list = new List<PinRecord>();
            list.Add(pin);
        }

        private const float RIGID_BAND_EPS = 0.05f;
        private const float RIGID_SLIDE_EPS = 0.1f;

        private static bool IsRigidPin(PinRecord pin)
        {
            return pin != null && (pin.Driven100 || pin.RailDrive || IsRigidJoint(pin.Joint));
        }

        private static bool IsRigidJoint(Joint joint)
        {
            if (joint is WeldJoint) return true;
            if (joint is RevoluteJoint revolute)
                return revolute.LimitEnabled && (revolute.UpperLimit - revolute.LowerLimit) <= RIGID_BAND_EPS;
            if (joint is PrismaticJoint slider)
                return slider.LimitEnabled && (slider.UpperLimit - slider.LowerLimit) <= RIGID_SLIDE_EPS;
            return false;
        }

        public static bool IsRigidlyJointed(Ship ship)
        {
            if (ship == null || s_pins.Count == 0) return false;
            Adjacency adj = GetAdjacency();
            if (adj.Live.TryGetValue(ship, out List<PinRecord> pins))
                foreach (PinRecord pin in pins)
                    if (IsRigidPin(pin)) return true;
            foreach (PinRecord pin in adj.Suspended)
                if ((pin.ShipA == ship || pin.ShipB == ship) && IsRigidPin(pin))
                    return true;
            return false;
        }

        public static bool IsJointed(Ship ship)
        {
            if (ship == null || s_pins.Count == 0) return false;
            Adjacency adj = GetAdjacency();
            if (adj.Live.ContainsKey(ship)) return true;
            foreach (PinRecord pin in adj.Suspended)
                if (pin.PartA?.Ship == ship || pin.PartB?.Ship == ship) return true;
            return false;
        }

        public static bool TryGetUpstreamRevolute(Ship head, Ship member, out RevoluteJoint joint)
        {
            joint = null;
            if (head == null || member == null || ReferenceEquals(head, member)) return false;
            var seen = new HashSet<Ship> { head };
            var queue = new Queue<Ship>();
            queue.Enqueue(head);
            while (queue.Count > 0)
            {
                Ship cur = queue.Dequeue();
                foreach (PinRecord pin in s_pins)
                {
                    if (pin.Suspended || pin.Joint == null) continue;
                    Ship other = pin.ShipA == cur ? pin.ShipB : (pin.ShipB == cur ? pin.ShipA : null);
                    if (other == null || seen.Contains(other)) continue;
                    seen.Add(other);
                    queue.Enqueue(other);
                    if (ReferenceEquals(other, member))
                    {
                        joint = pin.Joint as RevoluteJoint;
                        return joint != null;
                    }
                }
            }
            return false;
        }

        public static void CollectJointedShips(List<Ship> result) => CollectJointedShips(result, null);

        public static void CollectJointedShips(List<Ship> result, SimRoot sim)
        {
            result.Clear();
            foreach (PinRecord pin in s_pins)
            {
                if (sim != null && pin.Sim != null && !ReferenceEquals(pin.Sim, sim)) continue;
                Ship a, b;
                if (pin.Suspended) { a = pin.PartA?.Ship; b = pin.PartB?.Ship; }
                else { if (pin.Joint == null) continue; a = pin.ShipA; b = pin.ShipB; }
                if (a != null && !result.Contains(a)) result.Add(a);
                if (b != null && !result.Contains(b)) result.Add(b);
            }
        }

        private sealed class AssemblyProps
        {
            public float Mass;
            public float Inertia;
            public HVec ComWorld;
            public bool Jointed;
            public int StampTick = SimTicks.NEVER;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Ship, AssemblyProps> s_propsCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Ship, AssemblyProps>();

        public static bool TryGetAssemblyProperties(Ship ship, out float mass, out float inertia, out HVec comWorld)
        {
            if (ship == null || s_pins.Count == 0 || !GetAdjacency().Live.ContainsKey(ship))
            {
                mass = 0f;
                inertia = 0f;
                comWorld = default;
                return false;
            }
            lock (s_propsLock)
                return TryGetAssemblyPropertiesCore(ship, out mass, out inertia, out comWorld);
        }

        private static readonly object s_propsLock = new object();

        private static bool TryGetAssemblyPropertiesCore(Ship ship, out float mass, out float inertia, out HVec comWorld)
        {
            mass = 0f;
            inertia = 0f;
            comWorld = default;
            if (ship?.Physics?.Body == null || s_pins.Count == 0) return false;

            int now = ship.Sim?.Tick ?? SimTicks.NEVER;
            if (s_propsCache.TryGetValue(ship, out AssemblyProps cached) && now != SimTicks.NEVER && SimTicks.IsFresh(now, cached.StampTick, 0))
            {
                mass = cached.Mass;
                inertia = cached.Inertia;
                comWorld = cached.ComWorld;
                return cached.Jointed;
            }
            if (cached == null)
                s_propsCache.Add(ship, cached = new AssemblyProps());
            cached.StampTick = now;
            cached.Jointed = false;

            var ships = new List<Ship>();
            GetAssemblyShips(ship, ships, rigidOnly: true);
            if (ships.Count == 1) return false;

            float m = 0f, comX = 0f, comY = 0f;
            foreach (Ship s in ships)
            {
                Body b = s.Physics?.Body;
                if (b == null) continue;
                m += b.Mass;
                comX += b.Mass * b.WorldCenter.X;
                comY += b.Mass * b.WorldCenter.Y;
            }
            if (m <= 0f) return false;
            comX /= m;
            comY /= m;

            float inert = 0f;
            foreach (Ship s in ships)
            {
                Body b = s.Physics?.Body;
                if (b == null) continue;
                float dx = b.WorldCenter.X - comX;
                float dy = b.WorldCenter.Y - comY;
                inert += b.IntertiaAboutCentroid + b.Mass * (dx * dx + dy * dy);
            }
            cached.Mass = mass = m;
            cached.Inertia = inertia = inert;
            cached.ComWorld = comWorld = new HVec(comX, comY);
            cached.Jointed = true;
            return true;
        }

        public static bool TryGetAssemblyVelocity(Ship ship, out HVec velocity)
        {
            velocity = default;
            if (ship?.Physics?.Body == null || s_pins.Count == 0) return false;
            var ships = new List<Ship>();
            GetAssemblyShips(ship, ships, rigidOnly: true);
            if (ships.Count <= 1) return false;
            float m = 0f, px = 0f, py = 0f;
            foreach (Ship s in ships)
            {
                Body b = s.Physics?.Body;
                if (b == null) continue;
                m += b.Mass;
                px += b.Mass * b.LinearVelocity.X;
                py += b.Mass * b.LinearVelocity.Y;
            }
            if (m <= 0f) return false;
            velocity = new HVec(px / m, py / m);
            return true;
        }

        internal static void GetRigidClusterSorted(Ship member, List<Ship> into)
        {
            GetAssemblyShips(member, into, rigidOnly: true);
            into.Sort((a, b) => a.UniqueID._id.CompareTo(b.UniqueID._id));
        }

        internal static Ship PilotOfCluster(List<Ship> cluster, AssemblyGraph groups)
        {
            var ids = new List<ulong>(cluster.Count);
            foreach (Ship s in cluster) ids.Add(AssemblyTopology.Id(s));
            HashSet<ulong> visitors = null;
            foreach (PinRecord pin in s_pins)
            {
                if (!pin.Separate || pin.Visitor == null || !cluster.Contains(pin.Visitor)) continue;
                (visitors ??= new HashSet<ulong>()).Add(AssemblyTopology.Id(pin.Visitor));
            }
            if (visitors != null) ids = DockJoin.PilotCandidates(ids, visitors);
            ulong id = groups.PilotOfCluster(ids);
            foreach (Ship s in cluster)
                if (AssemblyTopology.Id(s) == id) return s;
            return null;
        }

        public static Ship GetDesignatedPilot(Ship member)
        {
            if (member == null) return null;
            if (FlightStage.TryGetPilot(member, out Ship planned)) return planned;
            var ships = new List<Ship>();
            GetRigidClusterSorted(member, ships);
            if (ships.Count <= 1) return member;
            return PilotOfCluster(ships, AssemblyTopology.GroupsOf(member.Sim)) ?? member;
        }

        internal static void CollectMarkedTops(HashSet<ulong> into, SimRoot sim)
        {
            into.Clear();
            foreach (PinRecord pin in s_pins)
            {
                if (sim != null && pin.Sim != null && !ReferenceEquals(pin.Sim, sim)) continue;
                Part pa = pin.PartA;
                if (pa?.Ship != null && AnchorMarker.Has(pa)) into.Add(AssemblyTopology.Id(pa.Ship));
            }
        }

        internal static bool HasTopMark(Ship ship)
        {
            foreach (PinRecord pin in s_pins)
                if (pin.PartA != null && ReferenceEquals(pin.PartA.Ship, ship) && AnchorMarker.Has(pin.PartA)) return true;
            return false;
        }

        internal static bool MarkTop(Ship ship)
        {
            foreach (PinRecord pin in s_pins)
            {
                if (pin.PartA == null || !ReferenceEquals(pin.PartA.Ship, ship)) continue;
                AnchorMarker.Set(pin.PartA);
                TopologyChanged();
                return true;
            }
            return false;
        }

        public static void LogSkeleton(Ship anyMember)
        {
            try
            {
                if (anyMember?.Physics?.Body == null) return;
                var ships = new List<Ship>();
                GetAssemblyShips(anyMember, ships);
                if (ships.Count <= 1) return;

                Ship root = ships[0];
                foreach (Ship s in ships)
                    if (s.Physics?.Body != null && (root.Physics?.Body == null || s.Physics.Body.Mass > root.Physics.Body.Mass))
                        root = s;

                var sb = new System.Text.StringBuilder();
                var comR = root.Physics.Body.WorldCenter;
                sb.Append($"Assembly skeleton ({ships.Count} ships): ROOT ship {root.UniqueID} mass {root.Physics.Body.Mass:F1} COM ({comR.X:F1}, {comR.Y:F1})");
                var visited = new List<Ship> { root };
                AppendChildren(sb, root, visited, 1);
                Log.Info(sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Exception("HingeJointManager.LogSkeleton", ex);
            }
        }

        private static void AppendChildren(System.Text.StringBuilder sb, Ship parent, List<Ship> visited, int depth)
        {
            foreach (PinRecord pin in s_pins)
            {
                Ship child = null;
                if (pin.ShipA == parent && !visited.Contains(pin.ShipB)) child = pin.ShipB;
                else if (pin.ShipB == parent && !visited.Contains(pin.ShipA)) child = pin.ShipA;
                if (child?.Physics?.Body == null || pin.Joint == null) continue;
                visited.Add(child);
                HVec anchor = pin.Joint.WorldAnchorA;
                HVec com = child.Physics.Body.WorldCenter;
                string kind = pin.Tag == null ? "debug pin"
                    : pin.Joint is RevoluteJoint ? "hinge"
                    : pin.RailDrive ? "rail" : pin.Joint is PrismaticJoint ? "piston" : "weld";
                sb.Append($"\n{new string(' ', depth * 2)}-> [{kind}] anchor ({anchor.X:F1}, {anchor.Y:F1}) -> ship {child.UniqueID} mass {child.Physics.Body.Mass:F1} COM ({com.X:F1}, {com.Y:F1})");
                AppendChildren(sb, child, visited, depth + 1);
            }
        }

        public struct SkeletonLink
        {
            public HVec ComA;
            public HVec Anchor;
            public HVec ComB;
            public bool IsWeld;
        }

        public static void GetOverlayData(List<SkeletonLink> links, List<(HVec Com, float Mass)> assemblyComs)
        {
            links.Clear();
            assemblyComs.Clear();
            var seen = new List<Ship>();
            foreach (PinRecord pin in s_pins)
            {
                Body a = pin.ShipA?.Physics?.Body;
                Body b = pin.ShipB?.Physics?.Body;
                if (a == null || b == null || pin.Joint == null) continue;
                links.Add(new SkeletonLink
                {
                    ComA = a.WorldCenter,
                    Anchor = pin.Joint.WorldAnchorA,
                    ComB = b.WorldCenter,
                    IsWeld = pin.Joint is WeldJoint || pin.RailDrive,
                });
                if (!seen.Contains(pin.ShipA))
                {
                    var members = new List<Ship>();
                    GetAssemblyShips(pin.ShipA, members);
                    seen.AddRange(members);
                    if (TryGetAssemblyProperties(pin.ShipA, out float mass, out _, out HVec com))
                        assemblyComs.Add((com, mass));
                }
            }
        }

        private static RevoluteJoint FindRevolute(object tag)
        {
            if (tag == null) return null;
            foreach (PinRecord pin in s_pins)
                if (ReferenceEquals(pin.Tag, tag))
                    return pin.Joint as RevoluteJoint;
            return null;
        }

        public static void ReassertFilter(object tag)
        {
            if (tag == null) return;
            foreach (PinRecord pin in s_pins)
            {
                if (!ReferenceEquals(pin.Tag, tag)) continue;
                if (pin.Group != 0) ApplyCollisionFilter(pin);
                return;
            }
        }

        private static void SetupCollisionFilter(PinRecord pin)
        {
            pin.Group = s_nextGroup;
            s_nextGroup = (short)((s_nextGroup == short.MinValue) ? -1 : (s_nextGroup - 1));
            ApplyCollisionFilter(pin);

            pin.OnFixtureAdded = delegate
            {
                try { ApplyCollisionFilter(pin); }
                catch (Exception ex) { Log.Exception("HingeJointManager.OnFixtureAdded", ex); }
            };
            ShipPhysics physA = pin.ShipA.Physics;
            ShipPhysics physB = pin.ShipB.Physics;
            if (physA != null) physA.FixtureAdded += pin.OnFixtureAdded;
            if (physB != null && !ReferenceEquals(physB, physA)) physB.FixtureAdded += pin.OnFixtureAdded;
        }

        private static void ApplyCollisionFilter(PinRecord pin)
        {
            ApplyGroupToPart(pin.PartA, pin.Group);
            ApplyGroupToPart(pin.PartB, pin.Group);
            if (pin.ExtraParts != null)
                foreach (Part p in pin.ExtraParts)
                    ApplyGroupToPart(p, pin.Group);
        }

        public static void AddFilteredPart(object tag, Part part)
        {
            PinRecord pin = FindPin(tag);
            if (pin == null || part == null) return;
            if (pin.ExtraParts == null) pin.ExtraParts = new List<Part>();
            if (!pin.ExtraParts.Contains(part)) pin.ExtraParts.Add(part);
            if (pin.Group != 0) ApplyGroupToPart(part, pin.Group);
        }

        public static void RemoveFilteredPart(object tag, Part part)
        {
            PinRecord pin = FindPin(tag);
            if (pin?.ExtraParts == null || part == null) return;
            pin.ExtraParts.Remove(part);
            if (pin.Group != 0) ResetGroupIfOurs(part, pin.Group);
        }

        public static void ApplyCollidesWithToPart(Part part, Category mask)
        {
            ShipPhysics phys = part?.Ship?.Physics;
            if (phys == null) return;
            foreach (Halfling.Geometry.IntVector2 cell in part.GetCells(PartRectType.Physical))
            {
                if (phys._fixturesByCell.TryGetValue(cell, out var info) &&
                    info.Fixture != null && info.Fixture.CollidesWith != mask)
                {
                    info.Fixture.CollidesWith = mask;
                }
            }
        }

        private static void ApplyGroupToPart(Part part, short group)
        {
            ShipPhysics phys = part?.Ship?.Physics;
            if (phys == null) return;
            foreach (Halfling.Geometry.IntVector2 cell in part.GetCells(PartRectType.Physical))
            {
                if (phys._fixturesByCell.TryGetValue(cell, out var info) &&
                    info.Fixture != null && info.Fixture.CollisionGroup != group)
                {
                    info.Fixture.CollisionGroup = group;
                }
            }
        }

        private static void ClearCollisionFilter(PinRecord pin)
        {
            if (pin.Group == 0) return;
            ShipPhysics physA = pin.ShipA?.Physics;
            ShipPhysics physB = pin.ShipB?.Physics;
            if (physA != null && pin.OnFixtureAdded != null) physA.FixtureAdded -= pin.OnFixtureAdded;
            if (physB != null && pin.OnFixtureAdded != null && !ReferenceEquals(physB, physA)) physB.FixtureAdded -= pin.OnFixtureAdded;
            ResetGroupIfOurs(pin.PartA, pin.Group);
            ResetGroupIfOurs(pin.PartB, pin.Group);
            if (pin.ExtraParts != null)
                foreach (Part p in pin.ExtraParts)
                    ResetGroupIfOurs(p, pin.Group);
            pin.Group = 0;
        }

        private static void ResetGroupIfOurs(Part part, short group)
        {
            ShipPhysics phys = part?.Ship?.Physics;
            if (phys == null) return;
            foreach (Halfling.Geometry.IntVector2 cell in part.GetCells(PartRectType.Physical))
            {
                if (phys._fixturesByCell.TryGetValue(cell, out var info) &&
                    info.Fixture != null && info.Fixture.CollisionGroup == group)
                {
                    info.Fixture.CollisionGroup = 0;
                }
            }
        }

        private static int s_surgeryDepth;
        private static readonly int SUSPEND_TIMEOUT_TICKS = SimTicks.WindowFor(3000f, SimTicks.DEFAULT_TICK_SECONDS);

        private static bool SuspendExpired(PinRecord pin)
        {
            SimRoot sim = pin.SuspendedSim;
            return sim == null || !SimTicks.IsFresh(sim.Tick, pin.SuspendedAtTick, SUSPEND_TIMEOUT_TICKS);
        }

        public static void BeginSurgery() { s_surgeryDepth++; }
        public static void EndSurgery() { if (s_surgeryDepth > 0) s_surgeryDepth--; }

        public static bool IsMigrating(Part part)
        {
            if (s_surgeryDepth > 0) return true;
            try { return part?.Ship?.StructureContiguity?.IsSplitting ?? false; }
            catch { return false; }
        }

        public static bool SuspendOrRelease(object tag, Part part)
        {
            if (tag == null) return false;
            PinRecord pin = FindPin(tag);
            if (pin == null) return false;
            if (pin.Suspended) return true;
            if (pin.Joint == null || pin.PartA?.Ship == null || pin.PartB?.Ship == null || !IsMigrating(part))
            {
                ReleasePin(pin);
                return false;
            }
            try
            {
                SuspendPin(pin);
                return true;
            }
            catch (Exception ex)
            {
                Log.Exception("HingeJointManager.SuspendOrRelease", ex);
                ReleasePin(pin);
                return false;
            }
        }

        private static float PartFrame(Part part)
        {
            float body = part?.Ship?.Physics?.Body?.Rotation ?? 0f;
            return body + (part?.Rotation ?? 0) * (float)(Math.PI / 2.0);
        }

        private static HVec Rotate(HVec v, float angle)
        {
            if (angle == 0f) return v;
            float c = (float)DetMath.Cos(angle), s = (float)DetMath.Sin(angle);
            return new HVec(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }

        private static void SuspendPin(PinRecord pin)
        {
            Joint j = pin.Joint;
            Body a = j.BodyA;
            Body b = j.BodyB;
            HVec wa = j.WorldAnchorA;
            HVec wb = j.WorldAnchorB;
            pin.SavedKind = j.JointType;
            pin.OffA = wa - pin.PartA.DetWorldPhysicalCenter;
            pin.FrameA = PartFrame(pin.PartA);
            pin.OffB = wb - pin.PartB.DetWorldPhysicalCenter;
            pin.FrameB = PartFrame(pin.PartB);
            pin.SavedCollide = j.CollideConnected;
            float refAngle;
            switch (j)
            {
                case RevoluteJoint r:
                    refAngle = r.ReferenceAngle;
                    pin.SavedLimit = r.LimitEnabled; pin.SavedLower = r.LowerLimit; pin.SavedUpper = r.UpperLimit;
                    pin.SavedMotor = r.MotorEnabled; pin.SavedMotorSpeed = r.MotorSpeed; pin.SavedMotorMax = r.MaxMotorTorque;
                    break;
                case PrismaticJoint s:
                    refAngle = s.ReferenceAngle;
                    pin.SavedLimit = s.LimitEnabled; pin.SavedLower = s.LowerLimit; pin.SavedUpper = s.UpperLimit;
                    pin.SavedMotor = s.MotorEnabled; pin.SavedMotorSpeed = s.MotorSpeed; pin.SavedMotorMax = s.MaxMotorForce;
                    pin.AxisWorld = a.GetWorldVector(s.LocalXAxis);
                    break;
                case WeldJoint w:
                    refAngle = w.ReferenceAngle;
                    pin.SavedFreq = w.FrequencyHz; pin.SavedDamp = w.DampingRatio;
                    break;
                default:
                    throw new InvalidOperationException("unsupported joint type " + j.JointType);
            }
            pin.JointAngle0 = b.Rotation - a.Rotation - refAngle;
            pin.Suspended = true;
            TopologyChanged();
            pin.SuspendedSim = pin.PartA?.Ship?.Sim ?? pin.PartB?.Ship?.Sim ?? pin.ShipA?.Sim;
            pin.SuspendedAtTick = pin.SuspendedSim?.Tick ?? SimTicks.NEVER;
            float sgX = wb.X - wa.X, sgY = wb.Y - wa.Y;
            Log.Info($"Joint suspended for part migration: {pin.SavedKind} between ships {pin.ShipA?.UniqueID} and {pin.ShipB?.UniqueID} (joint angle {pin.JointAngle0:F3}, anchor gap {Math.Sqrt(sgX * sgX + sgY * sgY):F3} units).");

            World world = a.World ?? pin.World;
            if (world != null && world.JointList.Contains(j))
            {
                world.RemoveAsync(j);
            }
            else
            {
                ClearCollisionFilter(pin);
                pin.Joint = null;
            }
        }

        internal static int SuspendedPinCount(SimRoot sim)
        {
            int n = 0;
            foreach (PinRecord pin in s_pins)
                if (pin.Suspended && ReferenceEquals(pin.Sim, sim)) n++;
            return n;
        }

        private static SimRoot s_saveBlockSim;
        private static Cosmoteer.Game.GameSaveHandler.GameSavingUnblocker? s_saveBlock;

        internal static void UpdateSaveGuard(SimRoot sim)
        {
            if (sim?.Game == null) return;
            bool suspended = SuspendedPinCount(sim) > 0;
            if (suspended && !s_saveBlock.HasValue)
            {
                s_saveBlock = sim.Game.BlockGameSaving();
                s_saveBlockSim = sim;
            }
            else if (s_saveBlock.HasValue && (!suspended || !ReferenceEquals(s_saveBlockSim, sim)))
            {
                s_saveBlock.Value.Dispose();
                s_saveBlock = null;
                s_saveBlockSim = null;
            }
        }

        public static void ResumeSuspended()
        {
            if (s_pins.Count == 0) return;
            for (int i = s_pins.Count - 1; i >= 0; i--)
            {
                PinRecord pin = s_pins[i];
                if (!pin.Suspended) continue;
                try
                {
                    ResumePin(pin);
                }
                catch (Exception ex)
                {
                    Log.Exception("HingeJointManager.ResumeSuspended", ex);
                    DropSuspended(pin, "the resume threw, see the line above");
                }
            }
        }

        private static void ResumePin(PinRecord pin)
        {
            if (pin.Joint != null) return;
            Part pa = pin.PartA;
            Part pb = pin.PartB;
            if (pa == null || pb == null) { DropSuspended(pin, "a part is gone"); return; }
            Ship sa = pa.Ship;
            Ship sb = pb.Ship;
            if (sa == null || sb == null)
            {
                if (SuspendExpired(pin))
                    DropSuspended(pin, "a part never reached a ship");
                return;
            }
            if (ReferenceEquals(sa, sb)) { DropSuspended(pin, "both parts ended up on one ship"); return; }
            if (s_surgeryDepth > 0 || IsMigrating(pa) || IsMigrating(pb)) return;
            Body a = sa.Physics?.Body;
            Body b = sb.Physics?.Body;
            if (a == null || b == null || a.World == null || b.World != a.World)
            {
                if (SuspendExpired(pin))
                    DropSuspended(pin, "no physics body for both parts");
                return;
            }

            HVec wa = pa.DetWorldPhysicalCenter + Rotate(pin.OffA, PartFrame(pa) - pin.FrameA);
            HVec wb = pb.DetWorldPhysicalCenter + Rotate(pin.OffB, PartFrame(pb) - pin.FrameB);
            float refAngle = b.Rotation - a.Rotation - pin.JointAngle0;
            Joint joint;
            switch (pin.SavedKind)
            {
                case JointType.Revolute:
                {
                    var r = new RevoluteJoint(a, b, wa, wb, useWorldCoordinates: true)
                    {
                        CollideConnected = pin.SavedCollide,
                        ReferenceAngle = refAngle,
                    };
                    if (pin.SavedLimit) { r.LowerLimit = pin.SavedLower; r.UpperLimit = pin.SavedUpper; r.LimitEnabled = true; }
                    if (pin.SavedMotor) { r.MotorSpeed = pin.SavedMotorSpeed; r.MaxMotorTorque = pin.SavedMotorMax; r.MotorEnabled = true; }
                    joint = r;
                    break;
                }
                case JointType.Prismatic:
                {
                    HVec axis = Rotate(pin.AxisWorld, PartFrame(pa) - pin.FrameA);
                    var s = new PrismaticJoint(a, b, wa, wb, axis, useWorldCoordinates: true)
                    {
                        CollideConnected = pin.SavedCollide,
                        ReferenceAngle = refAngle,
                    };
                    if (pin.SavedLimit) { s.SetLimits(pin.SavedLower, pin.SavedUpper); s.LimitEnabled = true; }
                    if (pin.SavedMotor) { s.MotorSpeed = pin.SavedMotorSpeed; s.MaxMotorForce = pin.SavedMotorMax; s.MotorEnabled = true; }
                    joint = s;
                    break;
                }
                case JointType.Weld:
                {
                    joint = new WeldJoint(a, b, wa, wb, useWorldCoordinates: true)
                    {
                        CollideConnected = pin.SavedCollide,
                        ReferenceAngle = refAngle,
                        FrequencyHz = pin.SavedFreq,
                        DampingRatio = pin.SavedDamp,
                    };
                    break;
                }
                default:
                    DropSuspended(pin, "unsupported joint kind " + pin.SavedKind);
                    return;
            }
            Subscribe(a.World);
            a.World.AddAsync(joint);
            a.Awake = true;
            b.Awake = true;
            pin.Joint = joint;
            pin.World = a.World;
            pin.Sim = sa.Sim ?? pin.Sim;
            pin.ShipA = sa;
            pin.ShipB = sb;
            pin.Suspended = false;
            TopologyChanged();
            SetupCollisionFilter(pin);
            float gapX = wb.X - wa.X, gapY = wb.Y - wa.Y;
            Log.Info($"Joint resumed after part migration: {pin.SavedKind} between ships {sa.UniqueID} and {sb.UniqueID} at ({wa.X:F1}, {wa.Y:F1}), anchor gap {Math.Sqrt(gapX * gapX + gapY * gapY):F3} units, group={pin.Group}.");
            InvalidateAssemblyCaches(sa, sb);
            LogSkeleton(sa);
        }

        private static void DropSuspended(PinRecord pin, string why)
        {
            ClearCollisionFilter(pin);
            s_pins.Remove(pin);
            TopologyChanged();
            Log.Info($"Suspended joint dropped -- {why}. Active pins: {s_pins.Count}.");
            InvalidateAssemblyCaches(pin.ShipA, pin.ShipB);
        }

        private static readonly HashSet<object> s_quietRelease = new HashSet<object>();

        public static void ReleaseByTagDeliberately(object tag)
        {
            if (tag == null) return;
            lock (s_quietRelease) s_quietRelease.Add(tag);
            ReleaseByTag(tag);
        }

        public static void ReleaseByTag(object tag)
        {
            if (tag == null) return;
            foreach (PinRecord pin in s_pins.ToArray())
                if (ReferenceEquals(pin.Tag, tag))
                    ReleasePin(pin);
        }

        public static void GetTagsLinking(Ship ship, Ship other, object excludeTag, List<object> result)
        {
            if (ship == null || other == null || result == null) return;
            foreach (PinRecord pin in s_pins)
            {
                if (pin.Tag == null || ReferenceEquals(pin.Tag, excludeTag)) continue;
                Ship a = pin.PartA?.Ship ?? pin.ShipA;
                Ship b = pin.PartB?.Ship ?? pin.ShipB;
                bool links = (ReferenceEquals(a, ship) && ReferenceEquals(b, other)) ||
                             (ReferenceEquals(b, ship) && ReferenceEquals(a, other));
                if (links) result.Add(pin.Tag);
            }
        }

        public static bool HasPinWithTag(object tag)
        {
            if (tag == null) return false;
            foreach (PinRecord pin in s_pins)
                if (ReferenceEquals(pin.Tag, tag))
                    return true;
            return false;
        }

        public static void ResetQuietReleases()
        {
            lock (s_quietRelease) s_quietRelease.Clear();
        }

        public static void ResetSim(SimRoot sim)
        {
            if (sim == null) return;
            int dropped = 0;
            for (int i = s_pins.Count - 1; i >= 0; i--)
            {
                PinRecord pin = s_pins[i];
                if (pin.Sim != null && !ReferenceEquals(pin.Sim, sim)) continue;
                s_pins.RemoveAt(i);
                dropped++;
            }
            World world = sim.PhysicsWorld;
            if (world != null && s_subscribedWorlds.Remove(world)) world.JointRemoved -= OnJointRemoved;
            if (ReferenceEquals(s_snapRoot, sim)) UnhookChainSnap();
            SyncSolverIterations();
            if (dropped > 0)
            {
                TopologyChanged();
                Log.Info($"Sim disposed: {dropped} pin record(s) of that sim dropped; {s_pins.Count} of other sims kept.");
            }
        }

        public static int ActivePinCount => s_pins.Count;

        private static void InvalidateAssemblyCaches(Ship shipA, Ship shipB)
        {
            try
            {
                var ships = new List<Ship>();
                foreach (Ship seed in new[] { shipA, shipB })
                {
                    if (seed == null) continue;
                    var members = new List<Ship>();
                    GetAssemblyShips(seed, members);
                    foreach (Ship s in members)
                        if (!ships.Contains(s)) ships.Add(s);
                    if (!ships.Contains(seed)) ships.Add(seed);
                }
                foreach (Ship s in ships)
                {
                    s.Thrusters?.ClearCachedAccelerations();
                    ThrusterFireDiag.Watch(s);
                }
                AssemblyFacade.RefreshCommandGates(ships);
            }
            catch (Exception ex)
            {
                Log.Exception("HingeJointManager.InvalidateAssemblyCaches", ex);
            }
        }

        private static void ReleasePin(PinRecord pin)
        {
            World world = pin.World ?? pin.Joint?.BodyA?.World;
            pin.Suspended = false;
            TopologyChanged();
            if (world != null && pin.Joint != null && world.JointList.Contains(pin.Joint))
            {
                world.RemoveAsync(pin.Joint);
                Log.Info($"Pin release requested for ships {pin.ShipA.UniqueID} / {pin.ShipB.UniqueID}.");
            }
            else
            {
                ClearCollisionFilter(pin);
                s_pins.Remove(pin);
            TopologyChanged();
                InvalidateAssemblyCaches(pin.ShipA, pin.ShipB);
                Log.Info("Pin record dropped (joint already destroyed).");
            }
        }

        private static void Subscribe(World world)
        {
            if (world == null || !s_subscribedWorlds.Add(world)) return;
            world.JointRemoved += OnJointRemoved;
        }

        private static void OnJointRemoved(World sender, Joint joint)
        {
            try
            {
                for (int i = 0; i < s_pins.Count; i++)
                {
                    if (!ReferenceEquals(s_pins[i].Joint, joint)) continue;
                    if (s_pins[i].Suspended)
                    {
                        ClearCollisionFilter(s_pins[i]);
                        s_pins[i].Joint = null;
                        InvalidateAssemblyCaches(s_pins[i].ShipA, s_pins[i].ShipB);
                        return;
                    }
                    Log.Info($"Pin gone (ships {s_pins[i].ShipA.UniqueID} / {s_pins[i].ShipB.UniqueID}). Active pins: {s_pins.Count - 1}.");
                    ClearCollisionFilter(s_pins[i]);
                    Ship wasA = s_pins[i].ShipA;
                    Ship wasB = s_pins[i].ShipB;
                    object cutTag = s_pins[i].Tag;
                    bool quiet;
                    lock (s_quietRelease) quiet = cutTag != null && s_quietRelease.Remove(cutTag);
                    if (!quiet) AssemblyVerdict.NoteCut(wasA, wasB, cutTag is ZtxHingeBaseComponent || cutTag is ZtxRailCartComponent);
                    s_pins.RemoveAt(i);
                    TopologyChanged();
                    InvalidateAssemblyCaches(wasA, wasB);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("HingeJointManager.OnJointRemoved", ex);
            }
        }
    }
}
