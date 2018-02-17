using System;
using System.Collections.Generic;
using System.Diagnostics;
using Verse;
using Verse.AI;
using RimWorld;
using System.Linq;

namespace PickUpAndHaul
{
    public class JobDriver_UnloadYourHauledInventory : JobDriver
    {
        private int countToDrop = -1;
        private int UnloadDuration = 3;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<int>(ref this.countToDrop, "countToDrop", -1, false);
        }

        public override bool TryMakePreToilReservations()
        {
            return true;
        }


        /// <summary>
        /// Find spot, reserve spot, pull thing out of inventory, go to spot, drop stuff, repeat.
        /// </summary>
        /// <returns></returns>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            CompHauledToInventory takenToInventory = pawn.TryGetComp<CompHauledToInventory>();
            HashSet<Thing> carriedThing = takenToInventory.GetHashSet();

            if (ModCompatibilityCheck.ExtendedStorageIsActive)
            {
                UnloadDuration = 12;
            }

            Toil wait = Toils_General.Wait(UnloadDuration);
            Toil celebrate = Toils_General.Wait(UnloadDuration);

            yield return wait;
            Toil findSpot = new Toil
            {
                initAction = () =>
                {
                
                ThingStackPart unloadableThing = FirstUnloadableThing(pawn);                    

                    if (unloadableThing.Count == 0 && carriedThing.Count == 0)
                    {
                        this.EndJobWith(JobCondition.Succeeded);
                    }

                    if (unloadableThing.Count != 0)
                    {
                        StoragePriority currentPriority = HaulAIUtility.StoragePriorityAtFor(pawn.Position, unloadableThing.Thing);
                        if (!StoreUtility.TryFindStoreCellNearColonyDesperate(unloadableThing.Thing, this.pawn, out IntVec3 c))
                        {
                            this.pawn.inventory.innerContainer.TryDrop(unloadableThing.Thing, ThingPlaceMode.Near, unloadableThing.Thing.stackCount, out Thing thing, null);
                            this.EndJobWith(JobCondition.Succeeded);
                        }
                        else
                        {
                            this.job.SetTarget(TargetIndex.A, unloadableThing.Thing);
                            this.job.SetTarget(TargetIndex.B, c);
                            this.countToDrop = unloadableThing.Thing.stackCount;
                        }
                    }
                }
            };
            yield return findSpot;

            yield return Toils_Reserve.Reserve(TargetIndex.B, 1, -1, null);

            yield return new Toil
            {
                initAction = delegate
                {
                    Thing thing = this.job.GetTarget(TargetIndex.A).Thing;
                    if (thing == null || !this.pawn.inventory.innerContainer.Contains(thing))
					{
                        carriedThing.Remove(thing);
                        this.EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    if (!this.pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) || !thing.def.EverStoreable)
					{
                        this.pawn.inventory.innerContainer.TryDrop(thing, ThingPlaceMode.Near, this.countToDrop, out thing, null);
                        this.EndJobWith(JobCondition.Succeeded);
                        carriedThing.Remove(thing);
                    }
					else
					{
                        this.pawn.inventory.innerContainer.TryTransferToContainer(thing, this.pawn.carryTracker.innerContainer, this.countToDrop, out thing, true);
                        this.job.count = this.countToDrop;
                        this.job.SetTarget(TargetIndex.A, thing);
                        carriedThing.Remove(thing);
                    }
                    try
                    {
                        ((Action)(() =>
                        {
                            if (ModCompatibilityCheck.CombatExtendedIsActive)
                            {
                                CombatExtended.CompInventory ceCompInventory = pawn.GetComp<CombatExtended.CompInventory>();
                                ceCompInventory.UpdateInventory();
                            }
                        }))();
                    }
                    catch (TypeLoadException) { }
                    thing.SetForbidden(false, false);
                }
            };

            Toil carryToCell = Toils_Haul.CarryHauledThingToCell(TargetIndex.B);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.Touch);
            yield return carryToCell;
            yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.B, carryToCell, true);
            yield return Toils_Jump.Jump(wait);
            yield return celebrate;
        }

        ThingStackPart FirstUnloadableThing(Pawn pawn)
        {
            CompHauledToInventory itemsTakenToInventory = pawn.TryGetComp<CompHauledToInventory>();
            HashSet<Thing> carriedThings = itemsTakenToInventory.GetHashSet();
            
            //find the overlap.
            IEnumerable<Thing> potentialThingsToUnload =
                from t in pawn.inventory.innerContainer
                where carriedThings.Contains(t)
                select t;

            foreach (Thing thing in carriedThings)
            {
                try
                {
                    if (thing == null)
                    {
                        carriedThings.Remove(thing);
                    }
                }
                catch (Exception arg)
                {
                    Log.Warning("There was an exception thrown by Pick Up And Haul. Pawn will clear inventory. \nException: " + arg);
                    carriedThings.Clear();
                    pawn.inventory.UnloadEverything = true;
                }
                
                //merged partially picked up stacks get a different thingID in inventory
                if (!potentialThingsToUnload.Contains(thing))
                {
                    ThingDef stragglerDef = thing.def;
                    
                    //we have no method of grabbing the newly generated thingID. This is the solution to that.
                    IEnumerable<Thing> dirtyStragglers =
                        from straggler in pawn.inventory.innerContainer
                        where straggler.def == stragglerDef
                        select straggler;

                    carriedThings.Remove(thing);

                    foreach (Thing dirtyStraggler in dirtyStragglers)
                    {
                        return new ThingStackPart(dirtyStraggler, dirtyStraggler.stackCount);
                    }
                }
                return new ThingStackPart(thing, thing.stackCount);
            }
            return default(ThingStackPart);
        }
    }
}