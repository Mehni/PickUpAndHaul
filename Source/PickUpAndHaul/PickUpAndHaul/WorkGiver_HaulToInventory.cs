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

        public override bool ShouldSkip(Pawn pawn)
        {
            return base.ShouldSkip(pawn) && (pawn.Faction != Faction.OfPlayer) && (!pawn.RaceProps.Humanlike); //hospitality check + misc robots & animals
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

            if (t.IsForbidden(pawn) || StoreUtility.IsInValidBestStorage(t))
            {
                return null;
            }

            if (ModCompatibilityCheck.Simplesidearms && t.def.defName.Contains("Chunk")) //because who doesn't love hardcoded checks?
            {
                return HaulAIUtility.HaulToStorageJob(pawn, t);
            }

            //if bulky gear (power armor + minigun) would prevent them carrying lots, don't bother.
            if (MassUtility.GearMass(pawn) / MassUtility.Capacity(pawn) >= 0.7f)
            {
                return null;
            }

            StoragePriority currentPriority = HaulAIUtility.StoragePriorityAtFor(t.Position, t);
            if (StoreUtility.TryFindBestBetterStoreCellFor(t, pawn, pawn.Map, currentPriority, pawn.Faction, out IntVec3 storeCell, true)) 
            {
                //since we've gone through all the effort of getting the loc, might as well use it.
                //Don't multi-haul food to hoppers.
                if (t.def.IsNutritionGivingIngestible)
                {
                    if (t.def.ingestible.preferability == FoodPreferability.RawBad || t.def.ingestible.preferability == FoodPreferability.RawTasty)
                    {
                        List<Thing> thingList = storeCell.GetThingList(t.Map);
                        for (int i = 0; i < thingList.Count; i++)
                        {
                            Thing thing = thingList[i];
                            if (thing.def == ThingDefOf.Hopper)
                                return HaulAIUtility.HaulToStorageJob(pawn, t);
                        }
                    }
                }
            }
            else
            {
                JobFailReason.Is("NoEmptyPlaceLower".Translate());
                return null;
            }

            if (MassUtility.EncumbrancePercent(pawn) >= 0.80f)
            {
                Job haul = HaulAIUtility.HaulToStorageJob(pawn, t);
                return haul;
            }

            //credit to Dingo
            int c = MassUtility.CountToPickUpUntilOverEncumbered(pawn, t);

            if (c == 0)
            {
                return HaulAIUtility.HaulToStorageJob(pawn, t);
            }

            else if (c >= 2 && t.def.BaseMass >= 1)
            {
                return HaulAIUtility.HaulToStorageJob(pawn, t);
            }

            return new Job(PickUpAndHaulJobDefOf.HaulToInventory, t)
            {
                count = c
            };
        }
    }
}