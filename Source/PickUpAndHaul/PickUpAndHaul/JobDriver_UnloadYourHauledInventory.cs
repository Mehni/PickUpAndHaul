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

        private const int UnloadDuration = 10;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<int>(ref this.countToDrop, "countToDrop", -1, false);
        }

        public override bool TryMakePreToilReservations()
        {
            return true;
        }

        [DebuggerHidden]
        protected override IEnumerable<Toil> MakeNewToils()
        {
            CompHauledToInventory takenToInventory = pawn.TryGetComp<CompHauledToInventory>();
            HashSet<Thing> carriedThing = takenToInventory.GetHashSet();

            Toil wait = Toils_General.Wait(UnloadDuration);
            Toil celebrate = Toils_General.Wait(10);
            //Log.Message(pawn + " is unloading.");
            foreach(Thing x in carriedThing)
            {
                //Log.Message(pawn + " has " + x + " in carriedthing");

            }
            foreach(Thing y in pawn.inventory.innerContainer)
            {
                //Log.Message(pawn + " has " + y + " in inventory");
            }

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
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.Touch);
            yield return new Toil
            {
                initAction = delegate
                {
                    Thing thing = this.job.GetTarget(TargetIndex.A).Thing;
                    if (thing == null || !this.pawn.inventory.innerContainer.Contains(thing))
					{
                        this.EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    if (!this.pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) || !thing.def.EverStoreable)
					{
                        this.pawn.inventory.innerContainer.TryDrop(thing, ThingPlaceMode.Near, this.countToDrop, out thing, null);
                        this.EndJobWith(JobCondition.Succeeded);
                        //Log.Message(pawn + " unloaded " +thing);
                        carriedThing.Remove(thing);
                    }
					else
					{
                        this.pawn.inventory.innerContainer.TryTransferToContainer(thing, this.pawn.carryTracker.innerContainer, this.countToDrop, out thing, true);
                        this.job.count = this.countToDrop;
                        this.job.SetTarget(TargetIndex.A, thing);
                        //Log.Message(pawn + " unloaded " + thing);
                        carriedThing.Remove(thing);
                    }
                    thing.SetForbidden(false, false);
                }
            };
            Toil carryToCell = Toils_Haul.CarryHauledThingToCell(TargetIndex.B);
            yield return carryToCell;
            yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.B, carryToCell, true);
            yield return Toils_Jump.Jump(wait);
            yield return celebrate;
        }

        ThingStackPart FirstUnloadableThing(Pawn pawn)
        {
            CompHauledToInventory takenToInventory = pawn.TryGetComp<CompHauledToInventory>();
            HashSet<Thing> carriedThing = takenToInventory.GetHashSet();
            
            //List<Thing> mergedList = pawn.inventory.innerContainer.Union(carriedThing).ToList();

            var potentialThingToUnload =
                from t in pawn.inventory.innerContainer
                where carriedThing.Contains(t)
                select t;

            foreach (Thing thing in carriedThing)
            {
                //Log.Message(pawn + " carries " + thing);
                if (!potentialThingToUnload.Contains(thing))
                {
                    ThingDef stragglerDef = thing.def;

                    //pick up partial stacks creates a new thingID that I have no method of grabbing. This is the solution to that.
                    var dirtyStragglers =
                        from straggler in pawn.inventory.innerContainer
                        where straggler.def == stragglerDef
                        select straggler;

                    foreach (Thing dirtyStraggler in dirtyStragglers)
                    {
                        foreach (Thing leftbehind in carriedThing)
                        {
                            Log.Warning(pawn + " thinks he has " + leftbehind + " with count " + leftbehind.stackCount);
                        }
                        foreach (Thing inventory in pawn.inventory.innerContainer)
                        {
                            Log.Warning(pawn + " actually has " + inventory + " with count " + inventory.stackCount);
                        }
                        Predicate<Thing> validator = (Thing t) => t.def == stragglerDef;
                        carriedThing.RemoveWhere(validator);
                        return new ThingStackPart(dirtyStraggler, dirtyStraggler.stackCount);
                    }
                }
                return new ThingStackPart(thing, thing.stackCount); //pawn.inventory.innerContainer.Intersect(carriedThing).Max(t => t.stackCount));
            }
            return default(ThingStackPart);
        }
    }
}