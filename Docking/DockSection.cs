using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Construction;
using Cosmoteer.Ships.Doors;
using Cosmoteer.Ships.Parts;

namespace ZTX.HingesPistonsDocking
{
    internal static class DockSection
    {
        public static int TakeFromQueue(Ship ship, List<PartInfo> parts, List<DoorInfo> doors)
        {
            ConstructionManager cm = ship?.OptionalConstructionManager;
            if (cm == null) return 0;
            int taken = 0;
            foreach (PartInfo pi in parts)
            {
                foreach (PartInfo under in pi.GetUnderlyingConstructions())
                    if (cm.TryRemovePartConstruct(under)) taken++;
                if (cm.TryRemovePartConstruct(pi)) taken++;
            }
            foreach (DoorInfo di in doors)
                if (cm.TryRemoveDoorConstruct(di)) taken++;
            return taken;
        }
    }
}
