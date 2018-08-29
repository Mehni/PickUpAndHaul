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

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return base.ShouldSkip(pawn) || pawn.Faction != Faction.OfPlayer || (!pawn.RaceProps.Humanlike); //hospitality check + misc robots & animals
        }

        //pick up stuff until you can't anymore,
        //while you're up and about, pick up something and haul it
        //before you go out, empty your pockets

        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {

            CompHauledToInventory takenToInventory = pawn.TryGetComp<CompHauledToInventory>();
            if (takenToInventory == null) return null;

            if (thing is Corpse) return null;

            if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, forced)) return null;
            
            if (thing.IsForbidden(pawn) || thing.IsInValidBestStorage()) return null;

            //bulky gear (power armor + minigun) so don't bother.
            if (MassUtility.GearMass(pawn) / MassUtility.Capacity(pawn) >= 0.8f) return null;

            StoragePriority currentPriority = StoreUtility.CurrentStoragePriorityOf(thing);
            if (StoreUtility.TryFindBestBetterStoreCellFor(thing, pawn, pawn.Map, currentPriority, pawn.Faction, out IntVec3 storeCell, true)) 
            {
                //since we've gone through all the effort of getting the loc, might as well use it.
                //Don't multi-haul food to hoppers.
                if (thing.def.IsNutritionGivingIngestible)
                {
                    if (thing.def.ingestible.preferability == FoodPreferability.RawBad || thing.def.ingestible.preferability == FoodPreferability.RawTasty)
                    {
                        List<Thing> thingList = storeCell.GetThingList(thing.Map);
                        for (int i = 0; i < thingList.Count; i++)
                        {
                            if (thingList[i].def == ThingDefOf.Hopper)
                            return HaulAIUtility.HaulToStorageJob(pawn, thing);
                        }
                    }
                }
            }
            else
            {
                JobFailReason.Is("NoEmptyPlaceLower".Translate());
                return null;
            }

            if (MassUtility.EncumbrancePercent(pawn) >= 0.90f)
            {
                Job haul = HaulAIUtility.HaulToStorageJob(pawn, thing);
                return haul;
            }

            //credit to Dingo
            int c = MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing);

            Thing preExistingThing = pawn.Map.thingGrid.ThingAt(storeCell, thing.def);
            if (preExistingThing != null)
                c = thing.def.stackLimit - preExistingThing.stackCount;

            if (c == 0) return HaulAIUtility.HaulToStorageJob(pawn, thing);

            return new Job(PickUpAndHaulJobDefOf.HaulToInventory, thing, storeCell)
            {
                count = c
            };
        }
    }
}