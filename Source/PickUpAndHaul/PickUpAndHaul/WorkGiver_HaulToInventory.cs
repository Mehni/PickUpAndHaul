using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace PickUpAndHaul
{
    
    public class WorkGiver_HaulToInventory : WorkGiver_HaulGeneral
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

            CompHauledToInventory takenToInventory = pawn.TryGetComp<CompHauledToInventory>();

            if (t is Corpse)
            {
                return null;
            }
            if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced))
            {
                return null;
            }

            if (ModCompatibilityCheck.KnownConflict)
            {
                return null;
            }

            if (pawn.Faction != Faction.OfPlayer) //hospitality check
            {
                return null;
            }

            if (!pawn.RaceProps.Humanlike) //misc robots & animals
            {
                return null;
            }

            if (t.IsForbidden(pawn) || StoreUtility.IsInValidBestStorage(t))
            {
                return null;
            }

            //if bulky gear (power armor + minigun) would prevent them carrying lots, don't bother.
            if (MassUtility.GearMass(pawn) / MassUtility.Capacity(pawn) >= 0.7f)
            {
                return null;
            }

            StoragePriority currentPriority = HaulAIUtility.StoragePriorityAtFor(t.Position, t);
            if (!StoreUtility.TryFindBestBetterStoreCellFor(t, pawn, pawn.Map, currentPriority, pawn.Faction, out IntVec3 storeCell, true))
            {
                JobFailReason.Is("NoEmptyPlaceLower".Translate());
                return null;
            }

            //if (MassUtility.EncumbrancePercent(pawn) >= 0.90f)
            //{
            //    return new Job(PickUpAndHaulJobDefOf.UnloadYourHauledInventory);
            //}

            if (MassUtility.EncumbrancePercent(pawn) >= 0.80f)
            {
                Job haul = HaulAIUtility.HaulToStorageJob(pawn, t);
                return haul;
            }

            Job job = new Job(PickUpAndHaulJobDefOf.HaulToInventory, t)
            {
                count = MassUtility.CountToPickUpUntilOverEncumbered(pawn, t)
            };
            if (job.count == 0 && t.def.defName.Contains("Chunk"))
            {
                job.count = 1;
            }

            if (job.count >= 2 && t.def.BaseMass >= 1)
            {
                Job haul = HaulAIUtility.HaulToStorageJob(pawn, t);
                return haul;
            }
            if (job.count == 0)
            {
                Job haul = HaulAIUtility.HaulToStorageJob(pawn, t);
                return haul;
            }
            return job;
        }
    }
}