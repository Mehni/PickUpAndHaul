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

        //Thanks to AlexTD for the more dynamic search range
        float searchForOthersRangeFraction = 0.5f;

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

            //bulky gear (power armor + minigun) so don't bother.
            if (MassUtility.GearMass(pawn) / MassUtility.Capacity(pawn) >= 0.8f) return null;

            Predicate<Thing> validator = (Thing t) => t.Spawned
                && !t.IsInValidBestStorage()
                && !t.IsForbidden(pawn)
                && !(t is Corpse)
                && pawn.CanReserve(t);

            Predicate<Thing> validatorFirst = (Thing t) =>
                validator(t) && HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced);

            if (!validatorFirst(thing)) return null;

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

            Job job = new Job(PickUpAndHaulJobDefOf.HaulToInventory, thing, storeCell)
            {
                count = c
            };


            //Find extra things than can be hauled to inventory, queue to reserve them
            DesignationDef HaulUrgentlyDesignation = DefDatabase<DesignationDef>.GetNamed("HaulUrgentlyDesignation", false);
            bool isUrgent = false;
            if (ModCompatibilityCheck.AllowToolIsActive &&
                pawn.Map.designationManager.DesignationOn(thing)?.def == HaulUrgentlyDesignation)
                isUrgent = true;

            Predicate<Thing> validatorExtra = (Thing t) =>
                t != thing
                && !job.targetQueueA.Contains(t)
                && (!isUrgent || pawn.Map.designationManager.DesignationOn(t)?.def == HaulUrgentlyDesignation)
                && validator(t)
                && HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, false);//forced is false, may differ from first thing

            
            //Find what fits in inventory
            job.targetQueueA = new List<LocalTargetInfo>();
            Thing nextThing = thing; //initially thing just for Position
            int nextThingLeftOverCount = 0;

            float encumberance = MassUtility.EncumbrancePercent(pawn);
            encumberance += AddedEnumberance(pawn, thing);

            //TODO check CE along with encumberance
            //float usedBulkByPct = 1f;
            //float usedWeightByPct = 1f;

            //try
            //{
            //    ((Action)(() =>
            //    {
            //        if (ModCompatibilityCheck.CombatExtendedIsActive)
            //        {
            //            CombatExtended.CompInventory ceCompInventory = pawn.GetComp<CombatExtended.CompInventory>();
            //            usedWeightByPct = ceCompInventory.currentWeight / ceCompInventory.capacityWeight;
            //            usedBulkByPct = ceCompInventory.currentBulk / ceCompInventory.capacityBulk;
            //        }
            //    }))();
            //}
            //catch (TypeLoadException) { }

            if (encumberance > 1f)// || usedBulkByPct >= 0.7f || usedWeightByPct >= 0.8f))
            {
                //first stack will be split to inventory AND carried
                nextThingLeftOverCount = CountPastCapacity(pawn, nextThing, encumberance);
            }
            else
            {
                float distanceToOthers = Math.Max(12f, (storeCell - thing.Position).LengthHorizontalSquared * (searchForOthersRangeFraction * searchForOthersRangeFraction));

                while (GenClosest.ClosestThingReachable(nextThing.Position, thing.Map, ThingRequest.ForGroup(ThingRequestGroup.HaulableAlways),
                    PathEndMode.ClosestTouch, TraverseParms.For(pawn), distanceToOthers, validatorExtra)
                    is Thing found)
                {
                    nextThing = found;
                    job.targetQueueA.Add(nextThing);

                    encumberance += AddedEnumberance(pawn, nextThing);
                    //TODO: CE also

                    if (encumberance > 1)// || usedBulkByPct >= 0.7f || usedWeightByPct >= 0.8f))
                    {
                        //can't CountToPickUpUntilOverEncumbered here, pawn doesn't actually hold these things yet
                        nextThingLeftOverCount = CountPastCapacity(pawn, nextThing, encumberance);
                        break;
                    }
                }
            }

            //Find what can be carried
            ThingDef carryDef = nextThing.def;
            Predicate<Thing> validatorCarry = (Thing t) =>
                 t.def == carryDef
                 && validatorExtra(t);

            int carryCapacity = pawn.carryTracker.MaxStackSpaceEver(carryDef) - nextThingLeftOverCount;

            while (GenClosest.ClosestThingReachable(nextThing.Position, thing.Map, ThingRequest.ForGroup(ThingRequestGroup.HaulableAlways),
                PathEndMode.ClosestTouch, TraverseParms.For(pawn), 8f, validatorCarry)    //8f hardcoded in CheckForGetOpportunityDuplicate
                is Thing found)
            {
                nextThing = found;

                carryCapacity -= nextThing.stackCount;

                if (carryCapacity < 0)
                    break;

                //Only reserve stack if you can carry entire stack
                job.targetQueueA.Add(nextThing);
            }

            return job;
        }

        public static float AddedEnumberance(Pawn pawn, Thing thing)
        {
            return thing.stackCount * thing.GetStatValue(StatDefOf.Mass, true) / MassUtility.Capacity(pawn);
        }

        public static int CountPastCapacity(Pawn pawn, Thing thing, float encumberance)
        {
            return (int)Math.Ceiling((encumberance-1) *  MassUtility.Capacity(pawn) / thing.GetStatValue(StatDefOf.Mass, true));
        }
    }
}