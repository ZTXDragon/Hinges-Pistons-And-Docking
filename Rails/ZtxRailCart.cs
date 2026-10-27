using Cosmoteer.Ships;
using System.Collections.Generic;
using System;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    internal enum RailPhase
    {
        Holding,
        Moving,
        Dwelling,
    }

    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxRailCart")]
    internal class ZtxRailCartRules : PartComponentRules
    {
        public override PartComponent CreateComponent()
        {
            return new ZtxRailCartComponent(this);
        }
    }

    internal class ZtxRailCartComponent : PartComponent
    {
        private static readonly ConditionalWeakTable<Part, ZtxRailCartComponent> s_byPart =
            new ConditionalWeakTable<Part, ZtxRailCartComponent>();

        public new ZtxRailCartRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxRailCartRules>(base.Rules); }
        }

        public ZtxRailTerminalComponent PairedTerminal;

        public int PlanRetryTick = -1;

        public RailRoute Route;

        public float S;

        public Part CurrentTile;

        public Part CameFrom;

        public Part GoalTile;
        public bool GoalIsHome;
        public float GoalS;

        public Part ViaTile;
        public bool ViaIsHome;
        public float ViaS;
        public RailPhase Phase;
        public float DwellLeft;

        public float RefBase;
        public float CumBase;

        public string EmergencyLogged;
        public Part HomeTile;
        public IntVector2? HomeStep;

        public IntVector2 HomeCell
        {
            get
            {
                Part t = PairedTerminal?.Part ?? _homeAnchor;
                if (t == null) return _homeCell;
                if (_homeAnchor == null || !_homeFromTerminal.HasValue)
                {
                    _homeFromTerminal = _homeCell - t.Location;
                    _homeAnchor = t;
                }
                else if (!ReferenceEquals(_homeAnchor, t))
                {
                    IntVector2 now = _homeAnchor.Location + _homeFromTerminal.Value;
                    _homeFromTerminal = now - t.Location;
                    _homeAnchor = t;
                }
                return t.Location + _homeFromTerminal.Value;
            }
            set
            {
                _homeCell = value;
                _homeFromTerminal = null;
                _homeAnchor = null;
            }
        }

        private IntVector2 _homeCell;
        private IntVector2? _homeFromTerminal;
        private Part _homeAnchor;

        public string CycleAt;
        public int MergedDwellEnd = -1;
        public Part DwellTile;
        public bool FreshPair;

        public bool ShuttleToHome;

        public int LastDirMode = -1;

        public float? Heading;

        public int BlockTicks;
        public int ProbeTicks;

        public bool Returning;

        public string LastRejectReason;

        public int MergeRetryTicks;

        public Vector2? DiagLastPoint;
        public float? RefActual;
        public bool LegIsTurnRound;
        public float? FacingBase;
        public bool CutOff;
        public float? ArrowConst;

        public float RefActualOrLegacy(RailRoute route)
        {
            return RefActual ?? (RefBase + route.Cum(S) - CumBase);
        }
        public Part FinalGoal;
        public bool FinalIsHome;
        public float DiagLastRef;
        public int DiagJumpLogs;

        public float V;
        public float LoadMass;
        public float LoadReach;
        public float LoadCom;
        public int LoadBodies;
        public bool LoadAttached;
        public int LoadVersion = -1;
        public int LoadTick;
        public bool LoadLogged;
        public float LoadAccel;
        public float LoadBrake;
        public float LastLimit;
        public string MotionPhase = "";
        public float? StopAt;
        public string StopReason;
        public RailRoute BlendRoute;
        public float BlendLength = -1f;
        public float FacingBlend;
        public float FacingBlendLeft;
        public bool MotionFailed;
        public int StallTicks;
        public float MotorCmd;
        public bool MotorRetreat;
        public float MotorForceCap;
        public bool? LegThrust;
        public float ThrustFwd;
        public float ThrustBack;
        public int ThrustTicks;
        public int UnderTicks;
        public string DriveNote = "";
        public float ThrustCmd;
        public bool LegWant;
        public float SpeedS;
        public float ShipPush;
        public float PlanAccS;
        public float TurnForce;
        public float TurnK = 1f;
        public float AddedPrev = float.NaN;
        public float TurnDeltaV;
        public float ThrustActual;
        public Vector2 PushLocal;
        public readonly List<Ship> LoadShips = new List<Ship>();

        public void ResetMotion()
        {
            V = 0f;
            LoadVersion = -1;
            LoadLogged = false;
            LastLimit = 0f;
            MotionPhase = "";
            StopAt = null;
            StopReason = null;
            BlendRoute = null;
            BlendLength = -1f;
            FacingBlendLeft = 0f;
            MotionFailed = false;
            StallTicks = 0;
            MotorCmd = 0f;
            MotorRetreat = false;
            MotorForceCap = 0f;
            LegThrust = null;
            ThrustFwd = 0f;
            ThrustBack = 0f;
            ThrustTicks = 0;
            UnderTicks = 0;
            DriveNote = "";
            PlanAccS = 0f;
            TurnForce = 0f;
            TurnK = 1f;
            AddedPrev = float.NaN;
            TurnDeltaV = 0f;
            FpTick = -1;
            CornerLogged = false;
            FollowTick = -1;
            FollowGap = float.MaxValue;
            FollowLeader = null;
            FollowLogged = null;
        }

        public int FpTick = -1;
        public float FpFront, FpRear, FpLeft, FpRight;
        public bool PoseWarned;
        public bool CornerLogged;
        public int FollowTick = -1;
        public float FollowGap = float.MaxValue;
        public ZtxRailCartComponent FollowLeader;
        public ZtxRailCartComponent FollowLogged;

        public bool AtHome
        {
            get { return Phase == RailPhase.Holding && GoalIsHome && GoalTile != null && CurrentTile == GoalTile; }
        }

        public ZtxRailCartComponent(ZtxRailCartRules rules) : base(rules) { }

        public static ZtxRailCartComponent ForPart(Part part)
        {
            if (part == null) return null;
            s_byPart.TryGetValue(part, out ZtxRailCartComponent cart);
            return cart;
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                s_byPart.AddOrUpdate(base.Part, this);
                RailShipManager.GetOrCreate(base.Ship)?.RegisterCart(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRailCartComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                s_byPart.Remove(base.Part);
                if (PairedTerminal != null && !HingeJointManager.SuspendOrRelease(this, base.Part))
                    ClearPair();
                RailShipManager.TryGet(base.Ship)?.DeregisterCart(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRailCartComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }

        public void ClearPair()
        {
            PairedTerminal?.Carts.Remove(this);
            PairedTerminal = null;
            Route = null;
            GoalTile = null;
            ViaTile = null;
            Phase = RailPhase.Holding;
            Returning = false;
            ResetMotion();
        }
    }
}
