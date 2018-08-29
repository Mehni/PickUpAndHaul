using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PickUpAndHaul
{
    public class JobDriver_HaulToInventory : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return this.pawn.Reserve(this.job.targetA, this.job) && pawn.Reserve(job.targetB, this.job);
        }

        //reserve, goto, take, check for more. Branches off to "all over the place"
        protected override IEnumerable<Toil> MakeNewToils()
        {
            CompHauledToInventory takenToInventory = pawn.TryGetComp<CompHauledToInventory>();
            DesignationDef HaulUrgentlyDesignation = DefDatabase<DesignationDef>.GetNamed("HaulUrgentlyDesignation", false);

            //Thanks to AlexTD for the more dynamic search range
            float searchForOthersRangeFraction = 0.5f;
            float distanceToOthers = 0f;

            Toil wait = Toils_General.Wait(2);
            Toil reserveTargetA = Toils_Reserve.Reserve(TargetIndex.A);

            Toil calculateExtraDistanceToGo = new Toil
            {
                initAction = () =>
                {
                    if (StoreUtility.TryFindStoreCellNearColonyDesperate(this.job.targetA.Thing, this.pawn, out IntVec3 storeLoc))
                        distanceToOthers = (storeLoc - job.targetA.Thing.Position).LengthHorizontal * searchForOthersRangeFraction;
                }
            };
            yield return calculateExtraDistanceToGo;

            Toil checkForOtherItemsToHaulToInventory = CheckForOtherItemsToHaulToInventory(reserveTargetA, TargetIndex.A, distanceToOthers);
            Toil checkForOtherItemsToUrgentlyHaulToInventory = CheckForOtherItemsToHaulToInventory(reserveTargetA, TargetIndex.A, distanceToOthers, x => pawn.Map.designationManager.DesignationOn(x)?.def == HaulUrgentlyDesignation);

            yield return reserveTargetA;

            Toil gotoThing = new Toil
            {
                initAction = () =>
                {
                    this.pawn.pather.StartPath(this.TargetThingA, PathEndMode.ClosestTouch);
                },
                defaultCompleteMode = ToilCompleteMode.PatherArrival
            };
            gotoThing.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return gotoThing;

            Toil takeThing = new Toil
            {
                initAction = () =>
                {
                    Pawn actor = this.pawn;
                    Thing thing = actor.CurJob.GetTarget(TargetIndex.A).Thing;
                    Toils_Haul.ErrorCheckForCarry(actor, thing);

                    //get max we can pick up
                    int num = Mathf.Min(thing.stackCount, MassUtility.CountToPickUpUntilOverEncumbered(actor, thing), job.count);

                    // yo dawg, I heard you like delegates so I put delegates in your delegate, so you can delegate your delegates.
                    // because compilers don't respect IF statements in delegates and toils are fully iterated over as soon as the job starts.
                    try
                    {
                        ((Action)(() =>
                        {
                            if (ModCompatibilityCheck.CombatExtendedIsActive)
                            {
                                CombatExtended.CompInventory ceCompInventory = actor.GetComp<CombatExtended.CompInventory>();
                                ceCompInventory.CanFitInInventory(thing, out num);
                            }
                        }))();
                    }
                    catch (TypeLoadException) { }

                    //can't store more, so queue up hauling if we can + end the current job (smooth/instant transition)
                    if (num <= 0)
                    {
                        Job haul = HaulAIUtility.HaulToStorageJob(actor, thing);
                        if (haul?.TryMakePreToilReservations(actor, false) ?? false)
                        {
                            actor.jobs.jobQueue.EnqueueFirst(haul, JobTag.Misc);
                        }
                        actor.jobs.curDriver.JumpToToil(wait);
                    }
                    else
                    {
                        bool isUrgent = false;
                        if (ModCompatibilityCheck.AllowToolIsActive)
                        {
                            //check BEFORE absorbing the thing, designation disappears when it's in inventory :^)
                            if (pawn.Map.designationManager.DesignationOn(thing)?.def == HaulUrgentlyDesignation)
                            {
                                isUrgent = true;
                            }
                        }

                        actor.inventory.GetDirectlyHeldThings().TryAdd(thing.SplitOff(num)); 
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

                        if (isUrgent)
                        {
                            actor.jobs.curDriver.JumpToToil(checkForOtherItemsToUrgentlyHaulToInventory);
                        }
                    }
                }
            };
            yield return takeThing;
            yield return checkForOtherItemsToHaulToInventory; //we end the job in there, so only one of the checks for duplicates gets called.
            yield return checkForOtherItemsToUrgentlyHaulToInventory;
            yield return wait;
        }


        //regular Toils_Haul.CheckForGetOpportunityDuplicate isn't going to work for our purposes, since we're not carrying anything. 
        //Carrying something yields weird results with unspawning errors when transfering to inventory, so we copy-past-- I mean, implement our own.
        public Toil CheckForOtherItemsToHaulToInventory(Toil getHaulTargetToil, TargetIndex haulableInd, float distanceToOthers, Predicate<Thing> extraValidator = null)
        {
            Toil toil = new Toil();
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                Job curJob = actor.jobs.curJob;
                IntVec3 storeCell = IntVec3.Invalid;

                bool Validator(Thing t) => t.Spawned && HaulAIUtility.PawnCanAutomaticallyHaulFast(actor, t, false)
                                          && !t.IsInValidBestStorage()
                                          && !t.IsForbidden(actor)
                                          && !(t is Corpse)
                                          && StoreUtility.TryFindBestBetterStoreCellFor(t, this.pawn, this.pawn.Map, StoreUtility.CurrentStoragePriorityOf(t), actor.Faction, out storeCell)
                                          && (extraValidator == null || extraValidator(t))
                                          && actor.CanReserve(t);

                Thing thing = GenClosest.ClosestThingReachable(actor.Position, actor.Map, ThingRequest.ForGroup(ThingRequestGroup.HaulableAlways), PathEndMode.ClosestTouch, 
                    TraverseParms.For(actor), Math.Max(distanceToOthers, 12f), Validator);

                //float usedBulkByPct = 1f;
                //float usedWeightByPct = 1f;

                //try
                //{
                //    ((Action)(() =>
                //    {
                //        if (ModCompatibilityCheck.CombatExtendedIsActive)
                //        {
                //            CompInventory ceCompInventory = actor.GetComp<CompInventory>();
                //            usedWeightByPct = ceCompInventory.currentWeight / ceCompInventory.capacityWeight;
                //            usedBulkByPct = ceCompInventory.currentBulk / ceCompInventory.capacityBulk;
                //        }
                //    }))();
                //}
                //catch (TypeLoadException) { }


                if (thing != null && (MassUtility.EncumbrancePercent(actor) <= 0.9f /*|| usedBulkByPct >= 0.7f || usedWeightByPct >= 0.8f*/))
                {
                    curJob.SetTarget(haulableInd, thing);
                    actor.Reserve(storeCell, this.job);
                    this.job.count = 99999; //done for "num", to solve scenarios like hauling 150 meat to single free spot near stove
                    actor.jobs.curDriver.JumpToToil(getHaulTargetToil);
                    return;
                }
                if (thing != null)
                {
                    Job haul = HaulAIUtility.HaulToStorageJob(actor, thing);
                    if (haul?.TryMakePreToilReservations(actor, false) ?? false)
                    {
                        //note that HaulToStorageJob etc doesn't do opportunistic duplicate hauling for items in valid storage. REEEE
                        actor.jobs.jobQueue.EnqueueFirst(haul, JobTag.Misc);
                        this.EndJobWith(JobCondition.Succeeded);
                        return;
                    }
                }
                Job unload = new Job(PickUpAndHaulJobDefOf.UnloadYourHauledInventory, storeCell);
                if (unload.TryMakePreToilReservations(actor, false))
                {
                    actor.jobs.jobQueue.EnqueueFirst(unload, JobTag.Misc);
                    this.EndJobWith(JobCondition.Succeeded);
                }
            };
            return toil;
        }
    }
}