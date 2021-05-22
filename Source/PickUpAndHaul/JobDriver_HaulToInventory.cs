namespace PickUpAndHaul
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using RimWorld;
    using UnityEngine;
    using Verse;
    using Verse.AI;

    public class JobDriver_HaulToInventory : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Log.Message($"{pawn} starting HaulToInventory job: {job.targetQueueA.ToStringSafeEnumerable()}:{job.countQueue.ToStringSafeEnumerable()}");
            pawn.ReserveAsManyAsPossible(job.targetQueueA, job);
            pawn.ReserveAsManyAsPossible(job.targetQueueB, job);
            return pawn.Reserve(job.targetQueueA[0], job) && pawn.Reserve(job.targetB, job);
        }

        //get next, goto, take, check for more. Branches off to "all over the place"
        protected override IEnumerable<Toil> MakeNewToils()
        {
            CompHauledToInventory takenToInventory = pawn.TryGetComp<CompHauledToInventory>();

            Toil wait = Toils_General.Wait(2);

            Toil nextTarget = Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.A); //also does count
            yield return nextTarget;

            yield return CheckForOverencumberedForCombatExtended();

            Toil gotoThing = new Toil
            {
                initAction = () =>
                {
                    pawn.pather.StartPath(TargetThingA, PathEndMode.ClosestTouch);
                },
                defaultCompleteMode = ToilCompleteMode.PatherArrival
            };
            gotoThing.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return gotoThing;

            Toil takeThing = new Toil
            {
                initAction = () =>
                {
                    Pawn actor = pawn;
                    Thing thing = actor.CurJob.GetTarget(TargetIndex.A).Thing;
                    Toils_Haul.ErrorCheckForCarry(actor, thing);

                    //get max we can pick up
                    int countToPickUp = Mathf.Min(job.count, MassUtility.CountToPickUpUntilOverEncumbered(actor, thing));
                    Log.Message($"{actor} is hauling to inventory {thing}:{countToPickUp}");

                    if (ModCompatibilityCheck.CombatExtendedIsActive)
                    {
                        countToPickUp = CompatHelper.CanFitInInventory(pawn, thing);
                    }

                    if (countToPickUp > 0)
                    {
                        Thing splitThing = thing.SplitOff(countToPickUp);
                        bool shouldMerge = takenToInventory.GetHashSet().Any(x => x.def == thing.def);
                        actor.inventory.GetDirectlyHeldThings().TryAdd(splitThing, shouldMerge);
                        takenToInventory.RegisterHauledItem(splitThing);

                        if (ModCompatibilityCheck.CombatExtendedIsActive)
                        {
                            CompatHelper.UpdateInventory(pawn);
                        }
                    }

                    //thing still remains, so queue up hauling if we can + end the current job (smooth/instant transition)
                    //This will technically release the reservations in the queue, but what can you do
                    if (thing.Spawned)
                    {
                        Job haul = HaulAIUtility.HaulToStorageJob(actor, thing);
                        if (haul?.TryMakePreToilReservations(actor, false) ?? false)
                        {
                            actor.jobs.jobQueue.EnqueueFirst(haul, JobTag.Misc);
                        }
                        actor.jobs.curDriver.JumpToToil(wait);
                    }
                }
            };
            yield return takeThing;
            yield return Toils_Jump.JumpIf(nextTarget, () => !job.targetQueueA.NullOrEmpty());

            //Find more to haul, in case things spawned while this was in progess
            yield return new Toil
            {
                initAction = () =>
                {
                    List<Thing> haulables = pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling();
                    WorkGiver_HaulToInventory haulMoreWork = DefDatabase<WorkGiverDef>.AllDefsListForReading.First(wg => wg.Worker is WorkGiver_HaulToInventory).Worker as WorkGiver_HaulToInventory;
                    Thing haulMoreThing = GenClosest.ClosestThing_Global(pawn.Position, haulables, 12, t => haulMoreWork.HasJobOnThing(pawn, t));

                    //WorkGiver_HaulToInventory found more work nearby
                    if (haulMoreThing != null)
                    {
                        Log.Message($"{pawn} hauling again : {haulMoreThing}");
                        Job haulMoreJob = haulMoreWork.JobOnThing(pawn, haulMoreThing);

                        if (haulMoreJob.TryMakePreToilReservations(pawn, false))
                        {
                            pawn.jobs.jobQueue.EnqueueFirst(haulMoreJob, JobTag.Misc);
                            EndJobWith(JobCondition.Succeeded);
                        }
                    }
                }
            };

            //maintain cell reservations on the trip back
            //TODO: do that when we carry things
            //I guess that means TODO: implement carrying the rest of the items in this job instead of falling back on HaulToStorageJob
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.ClosestTouch);

            yield return new Toil //Queue next job
            {
                initAction = () =>
                {
                    Pawn actor = pawn;
                    Job curJob = actor.jobs.curJob;
                    LocalTargetInfo storeCell = curJob.targetB;

                    Job unloadJob = JobMaker.MakeJob(PickUpAndHaulJobDefOf.UnloadYourHauledInventory, storeCell);
                    if (unloadJob.TryMakePreToilReservations(actor, false))
                    {
                        actor.jobs.jobQueue.EnqueueFirst(unloadJob, JobTag.Misc);
                        EndJobWith(JobCondition.Succeeded);
                        //This will technically release the cell reservations in the queue, but what can you do
                    }
                }
            };
            yield return wait;
        }

        /// <summary>
        /// the workgiver checks for encumbered, this is purely extra for CE
        /// </summary>
        /// <returns></returns>
        public Toil CheckForOverencumberedForCombatExtended()
        {
            Toil toil = new Toil();

            if (!ModCompatibilityCheck.CombatExtendedIsActive)
            {
                return toil;
            }

            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                Job curJob = actor.jobs.curJob;
                Thing nextThing = curJob.targetA.Thing;

                var ceOverweight = CompatHelper.CeOverweight(pawn);

                if (!(MassUtility.EncumbrancePercent(actor) <= 0.9f && !ceOverweight))
                {
                    Job haul = HaulAIUtility.HaulToStorageJob(actor, nextThing);
                    if (haul?.TryMakePreToilReservations(actor, false) ?? false)
                    {
                        //note that HaulToStorageJob etc doesn't do opportunistic duplicate hauling for items in valid storage. REEEE
                        actor.jobs.jobQueue.EnqueueFirst(haul, JobTag.Misc);
                        EndJobWith(JobCondition.Succeeded);
                    }
                }
            };

            return toil;
        }
    }
}