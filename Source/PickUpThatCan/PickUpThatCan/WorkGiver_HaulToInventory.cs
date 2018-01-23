using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace PickUpThatCan
{
    public class WorkGiver_HaulToInventory : WorkGiver_Haul
    {
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling();
        }

        public override bool ShouldSkip(Pawn pawn)
        {
            return pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling().Count == 0;
        }

        //pick up stuff until you can't anymore,
        //while you're up and about, pick up something and haul it
        //before you go out, empty your pockets
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t is Corpse)
            {
                return null;
            }
            if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced))
            {
                return null;
            }

            if (MassUtility.EncumbrancePercent(pawn) >= 0.90f)
            {
                Job haul = HaulAIUtility.HaulToStorageJob(pawn, t);
                pawn.inventory.UnloadEverything = true;
                return haul;
            }

            if (pawn.inventory.UnloadEverything == true)
            {
                return new Job(JobDefOf.UnloadYourInventory);      
            }

            pawn.inventory.UnloadEverything = false;
            Job job = new Job(JobDefOf.TakeInventory, t);
            job.count = MassUtility.CountToPickUpUntilOverEncumbered(pawn, t);
            pawn.jobs.EndCurrentJob(JobCondition.Succeeded, false);
            return job;
        }
    }
}