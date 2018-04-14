using System;
using System.Linq;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace PickUpAndHaul
{
    public class JobDriver_HaulToInventory : JobDriver
    {


        public override bool TryMakePreToilReservations()
        {
            this.pawn.ReserveAsManyAsPossible(this.job.GetTargetQueue(TargetIndex.A), this.job, 1, -1, null);
            return this.pawn.Reserve(this.job.targetA, this.job, 1, -1, null);
        }

        //reserve, goto, take, check for more. Branches off to "all over the place"
        protected override IEnumerable<Toil> MakeNewToils()
        {
            CompHauledToInventory takenToInventory = pawn.TryGetComp<CompHauledToInventory>();
            HashSet<Thing> carriedThings = takenToInventory.GetHashSet();

            Toil wait = Toils_General.Wait(2);

            Toil gotoThing = new Toil
            {
                initAction = () =>
                {
                    this.pawn.pather.StartPath(this.TargetThingA, PathEndMode.ClosestTouch);
                },
                defaultCompleteMode = ToilCompleteMode.PatherArrival,
            };
            gotoThing.FailOnDespawnedNullOrForbidden(TargetIndex.A);

            Toil checkForOtherItemsToHaulToInventory = CheckForOtherItemsToHaulToInventory(gotoThing, TargetIndex.A);
            yield return gotoThing;

            Toil takeThing = new Toil
            {
                initAction = () =>
                {
                    Pawn actor = this.pawn;
                    Thing thing = actor.CurJob.GetTarget(TargetIndex.A).Thing;
                    Toils_Haul.ErrorCheckForCarry(actor, thing);

                    //get max we can pick up
                    int countToPickUp = Mathf.Min(thing.stackCount, MassUtility.CountToPickUpUntilOverEncumbered(actor, thing));

                    // yo dawg, I heard you like delegates so I put delegates in your delegate, so you can delegate your delegates.
                    // because compilers don't respect IF statements in delegates and toils are fully iterated over as soon as the job starts.
                    try
                    {
                        ((Action)(() =>
                        {
                            if (ModCompatibilityCheck.CombatExtendedIsActive)
                            {
                                CombatExtended.CompInventory ceCompInventory = actor.GetComp<CombatExtended.CompInventory>();
                                ceCompInventory.CanFitInInventory(thing, out countToPickUp, false, false);
                            }
                        }))();
                    }
                    catch (TypeLoadException) { }

                    if (countToPickUp > 0)
                    {
                        actor.inventory.GetDirectlyHeldThings().TryAdd(thing.SplitOff(countToPickUp), true); 
                        takenToInventory.RegisterHauledItem(thing);

                        try
                        {
                            ((Action)(() =>
                            {
                                if (ModCompatibilityCheck.CombatExtendedIsActive)
                                {
                                    CombatExtended.CompInventory ceCompInventory = actor.GetComp<CombatExtended.CompInventory>();
                                    ceCompInventory.UpdateInventory();
                                }
                            }))();
                        }
                        catch (TypeLoadException) { }
                    }
                    //thing still remains, so queue up hauling if we can + end the current job (smooth/instant transition)
                    if (thing.Spawned)
                    {
                        Job haul = HaulAIUtility.HaulToStorageJob(actor, thing);
                        if (haul?.TryMakePreToilReservations(actor) ?? false)
                        {
                            actor.jobs.jobQueue.EnqueueFirst(haul, new JobTag?(JobTag.Misc));
                        }
                        actor.jobs.curDriver.JumpToToil(wait);
                    }
                }
            };
            yield return takeThing;
            yield return checkForOtherItemsToHaulToInventory; //we end the job in there, so only one of the checks for duplicates gets called.
            yield return wait;
        }


        //regular Toils_Haul.CheckForGetOpportunityDuplicate isn't going to work for our purposes, since we're not carrying anything. 
        //Carrying something yields weird results with unspawning errors when transfering to inventory, so we copy-past-- I mean, implement our own.
        public Toil CheckForOtherItemsToHaulToInventory(Toil getHaulTargetToil, TargetIndex haulableInd)
        {
            Toil toil = new Toil();
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                Job curJob = actor.jobs.curJob;

                if(curJob.targetQueueA.NullOrEmpty())
                {
                    Job job = new Job(PickUpAndHaulJobDefOf.UnloadYourHauledInventory);
                    if (job.TryMakePreToilReservations(actor))
                    {
                        actor.jobs.jobQueue.EnqueueFirst(job, new JobTag?(JobTag.Misc));
                        this.EndJobWith(JobCondition.Succeeded);
                    }
                    return;
                }
                LocalTargetInfo nextThing = curJob.targetQueueA.FirstOrDefault();
                curJob.targetQueueA.RemoveAt(0);
                float usedBulkByPct = 1f;
                float usedWeightByPct = 1f;

                try
                {
                    ((Action)(() =>
                    {
                        if (ModCompatibilityCheck.CombatExtendedIsActive)
                        {
                            CombatExtended.CompInventory ceCompInventory = actor.GetComp<CombatExtended.CompInventory>();
                            usedWeightByPct = ceCompInventory.currentWeight / ceCompInventory.capacityWeight;
                            usedBulkByPct = ceCompInventory.currentBulk / ceCompInventory.capacityBulk;
                        }
                    }))();
                }
                catch (TypeLoadException) { }


                if (MassUtility.EncumbrancePercent(actor) <= 0.9f || usedBulkByPct >= 0.7f || usedWeightByPct >= 0.8f)
                {
                    curJob.SetTarget(haulableInd, nextThing);
                    actor.jobs.curDriver.JumpToToil(getHaulTargetToil);
                }
                else
                {
                    Job haul = HaulAIUtility.HaulToStorageJob(actor, nextThing.Thing);
                    if (haul?.TryMakePreToilReservations(actor) ?? false)
                    {
                        actor.jobs.jobQueue.EnqueueFirst(haul, new JobTag?(JobTag.Misc));
                        this.EndJobWith(JobCondition.Succeeded);
                    }
                }
            };
            return toil;
        }
    }
}