using System;
using Verse;

namespace PickUpAndHaul
{
    internal class CompatHelper
    {
        public static bool CeOverweight(Pawn pawn)
        {
            CombatExtended.CompInventory ceCompInventory = pawn.GetComp<CombatExtended.CompInventory>();
            float usedWeightByPct = ceCompInventory.currentWeight / ceCompInventory.capacityWeight;
            float usedBulkByPct = ceCompInventory.currentBulk / ceCompInventory.capacityBulk;

            return usedBulkByPct >= 0.7f || usedWeightByPct >= 0.8f;
        }

        public static int CanFitInInventory(Pawn pawn, Thing thing)
        {
            CombatExtended.CompInventory ceCompInventory = pawn.GetComp<CombatExtended.CompInventory>();
            ceCompInventory.CanFitInInventory(thing, out int countToPickUp);

            return countToPickUp;
        }

        internal static void UpdateInventory(Pawn pawn)
        {
            CombatExtended.CompInventory ceCompInventory = pawn.GetComp<CombatExtended.CompInventory>();
            ceCompInventory.UpdateInventory();
        }
    }
}