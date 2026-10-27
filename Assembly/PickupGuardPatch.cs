using System;
using Cosmoteer.Crew;
using Cosmoteer.Ships.Crew.Jobs;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyLib.HarmonyPatch(typeof(ResourceTransferJob), "PickupResources")]
    internal static class PickupGuardPatch
    {
        static Exception Finalizer(Exception __exception, ResourceTransferJob __instance, CrewSoul crew, ref Cosmoteer.Crew.Destination newDest)
        {
            if (!(__exception is NullReferenceException)) return __exception;
            try
            {
                LogThrottle.Info("PickupGuard|" + __instance?.Ship?.UniqueID,
                    $"Crew pickup guard: a resource job of ship {__instance?.Ship?.UniqueID} threw a null reference at pickup -- source {(__instance?.Source == null ? "null" : __instance.Source.GetType().Name + " on ship " + __instance.Source.Ship?.UniqueID)}, "
                    + $"sink on ship {__instance?.Sink?.Ship?.UniqueID}, crew home ship {crew?.HomeShip?.UniqueID}, standing on ship {crew?.CurrentShip?.UniqueID}, "
                    + $"form {crew?.Form?.GetType().Name ?? "none"}, carrying {crew?.CarriedResource.Quantity}, job is the crew's {ReferenceEquals(crew?.Job, __instance)}. The crew was taken off the job instead of crashing the game.");
                if (crew != null && ReferenceEquals(crew.Job, __instance)) __instance.UnassignCrew(crew);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("PickupGuardPatch", ex);
            }
            newDest = default(Cosmoteer.Crew.Destination);
            return null;
        }
    }
}
