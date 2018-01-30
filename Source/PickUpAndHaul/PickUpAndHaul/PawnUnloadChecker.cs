using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;


namespace PickUpAndHaul
{
    public class PawnUnloadChecker
    {

        public static void CheckIfPawnShouldUnloadInventory(Pawn pawn, bool forced = false)
        {
            Job job = new Job(PickUpAndHaulJobDefOf.UnloadYourHauledInventory);
            CompHauledToInventory takenToInventory = pawn.TryGetComp<CompHauledToInventory>();
            HashSet<Thing> carriedThing = takenToInventory.GetHashSet();

            if (ModCompatibilityCheck.KnownConflict)
            {
                return;
            }

            if (forced)
            {
                if (job.TryMakePreToilReservations(pawn))
                {
                    pawn.jobs.jobQueue.EnqueueFirst(job, new JobTag?(JobTag.Misc));
                }
            }

            if (pawn.inventory.innerContainer.Count >= 5 || MassUtility.EncumbrancePercent(pawn) >= 0.90f)
            {
                if (job.TryMakePreToilReservations(pawn))
                {
                    pawn.jobs.jobQueue.EnqueueFirst(job, new JobTag?(JobTag.Misc));
                }
            }

            if (Find.TickManager.TicksGame % 120 == 0 && pawn.inventory.innerContainer.Count >= 2) //we don't wind up in this function often, so this is like a lottery!
            {
                Log.Message("[PickUpAndHaul] " + pawn + " cleared haul-state and will drop inventory.");
                carriedThing.Clear();
                pawn.inventory.UnloadEverything = true;
            }
        }
    }

    [DefOf]
    public static class PickUpAndHaulJobDefOf
    {
        public static JobDef UnloadYourHauledInventory;
        public static JobDef HaulToInventory;
    }
}