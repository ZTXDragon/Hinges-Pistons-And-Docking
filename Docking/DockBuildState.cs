using Cosmoteer.Ships;
using Cosmoteer.Simulation.MediaEffects;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class DockBuildState
    {
        public MultiMediaEffectNode Beam;
        public MultiMediaEffectNode BeamHit;

        public int IdleTicks;

        public Ship BeamShip;
        public Ship BeamHitShip;
    }
}
