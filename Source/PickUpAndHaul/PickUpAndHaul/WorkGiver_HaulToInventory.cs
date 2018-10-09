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
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogMessage(string x)
        {
            Log.Message(x);
        }

        //Thanks to AlexTD for the more dynamic search range
        //And queueing
        //And optimizing
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
            int capacityStoreCell = CapacityAt(thing.def, storeCell, pawn.Map);

            if (capacityStoreCell == 0) return HaulAIUtility.HaulToStorageJob(pawn, thing);

            Job job = new Job(PickUpAndHaulJobDefOf.HaulToInventory, null, storeCell);   //Things will be in queues
            LogMessage($"-------------------------------------------------------------------");
            LogMessage($"------------------------------------------------------------------");//different size so the log doesn't count it 2x
            LogMessage($"{pawn} job is haulin {thing} to {storeCell}:{capacityStoreCell}");

            //Find extra things than can be hauled to inventory, queue to reserve them
            DesignationDef HaulUrgentlyDesignation = DefDatabase<DesignationDef>.GetNamed("HaulUrgentlyDesignation", false);
            bool isUrgent = false;
            if (ModCompatibilityCheck.AllowToolIsActive &&
                pawn.Map.designationManager.DesignationOn(thing)?.def == HaulUrgentlyDesignation)
                isUrgent = true;

            Predicate<Thing> validatorExtra = (Thing t) =>
                !job.targetQueueA.Contains(t) &&
                (!isUrgent || pawn.Map.designationManager.DesignationOn(t)?.def == HaulUrgentlyDesignation) &&
                validator(t) && HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, false);//forced is false, may differ from first thing


            //Find what fits in inventory, set nextThingLeftOverCount to be 
            int nextThingLeftOverCount = 0;
            float encumberance = MassUtility.EncumbrancePercent(pawn);
            job.targetQueueA = new List<LocalTargetInfo>(); //more things
            job.targetQueueB = new List<LocalTargetInfo>(); //more storage; keep in mind the job doesn't use it, but reserve it so you don't over-haul
            job.countQueue = new List<int>();//thing counts


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

            float distanceToHaul = (storeCell - thing.Position).LengthHorizontal * searchForOthersRangeFraction;
            float distanceToSearchMore = Math.Max(12f, distanceToHaul);

            Thing nextThing = thing;
            do
            {
                if (AllocateThingAtCell(ref capacityStoreCell, out int stackCount, pawn, nextThing, job, ref storeCell))
                {
                    StoreUtilityCellSkipper.skipCells = null;
                    return job;
                }

                encumberance += AddedEnumberance(pawn, nextThing);

                if (encumberance > 1)// || usedBulkByPct >= 0.7f || usedWeightByPct >= 0.8f))//TODO: CE also
                {
                    //can't CountToPickUpUntilOverEncumbered here, pawn doesn't actually hold these things yet
                    nextThingLeftOverCount = CountPastCapacity(pawn, nextThing, encumberance);
                    LogMessage($"Inventory allocated, will carry {nextThing}:{nextThingLeftOverCount}");
                    break;
                }
            }
            while ((nextThing = GenClosest.ClosestThingReachable(nextThing.Position, thing.Map, ThingRequest.ForGroup(ThingRequestGroup.HaulableAlways),
                PathEndMode.ClosestTouch, TraverseParms.For(pawn), distanceToSearchMore, validatorExtra))
                is Thing);

            if (nextThing == null)
            {
                StoreUtilityCellSkipper.skipCells = null;
                return job;
            }

            //Find what can be carried
            //this doesn't actually get pickupandhauled, but will hold the reservation so others don't grab what this pawn can carry
            ThingDef carryDef = nextThing.def;
            Predicate<Thing> validatorCarry = (Thing t) =>
                 t.def == carryDef
                 && validatorExtra(t);
            LogMessage($"Looking for more {carryDef}");

            int carryCapacity = pawn.carryTracker.MaxStackSpaceEver(carryDef) - nextThingLeftOverCount;

            while ((nextThing = GenClosest.ClosestThingReachable(nextThing.Position, thing.Map, ThingRequest.ForGroup(ThingRequestGroup.HaulableAlways),
                PathEndMode.ClosestTouch, TraverseParms.For(pawn), 8f, validatorCarry))    //8f hardcoded in CheckForGetOpportunityDuplicate
                is Thing)
            {
                carryCapacity -= nextThing.stackCount;

                if (AllocateThingAtCell(ref capacityStoreCell, out int stackCount, pawn, nextThing, job, ref storeCell))
                    break;

                if (carryCapacity <= 0)
                {
                    int lastCount = job.countQueue.Pop() + carryCapacity;
                    job.countQueue.Add(lastCount);
                    Log.Message($"Nevermind, last count is {lastCount}");
                    break;
                }
            }

            StoreUtilityCellSkipper.skipCells = null;
            return job;
        }

        public static int CapacityAt(ThingDef def, IntVec3 storeCell, Map map)
        {
            int capacity = def.stackLimit;

            Thing preExistingThing = map.thingGrid.ThingAt(storeCell, def);
            if (preExistingThing != null)
                capacity = def.stackLimit - preExistingThing.stackCount;

            return capacity;
        }

        public static bool AllocateThingAtCell(ref int capacityStoreCell, out int stackCount, Pawn pawn, Thing nextThing, Job job, ref IntVec3 storeCell)
        {
            job.targetQueueA.Add(nextThing);
            stackCount = nextThing.stackCount;
            capacityStoreCell -= stackCount;
            LogMessage($"{pawn} allocating {nextThing}:{stackCount}, now {storeCell}:{capacityStoreCell}");

            bool searchDone = false;

            while (capacityStoreCell <= 0)
            {
                int capacityOver = -capacityStoreCell;
                LogMessage($"{pawn} overdone {storeCell} by {capacityOver}");

                if(StoreUtilityCellSkipper.skipCells == null)
                    StoreUtilityCellSkipper.skipCells = new HashSet<IntVec3>();
                StoreUtilityCellSkipper.skipCells.Add(storeCell);

                StoragePriority currentPriority = StoreUtility.CurrentStoragePriorityOf(nextThing);//How necessary is this?
                if (StoreUtility.TryFindBestBetterStoreCellFor(nextThing, pawn, pawn.Map, currentPriority, pawn.Faction, out IntVec3 nextStoreCell, false))
                {
                    storeCell = nextStoreCell;
                    job.targetQueueB.Add(storeCell);

                    capacityStoreCell = CapacityAt(nextThing.def, storeCell, pawn.Map);
                    capacityStoreCell -= capacityOver;

                    LogMessage($"New cell {storeCell}:{capacityStoreCell}, allocated extra {capacityOver}");
                }
                else
                {
                    stackCount -= capacityOver;
                    LogMessage($"Nowhere else to store, job is going, {nextThing}:{stackCount}");
                    StoreUtilityCellSkipper.skipCells = null;
                    searchDone = true;//nowhere else to hold it, so job is ready
                    break;
                }
            }
            job.countQueue.Add(stackCount);
            LogMessage($"{nextThing}:{stackCount} allocated, now using {storeCell}:{capacityStoreCell}");
            return searchDone;
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