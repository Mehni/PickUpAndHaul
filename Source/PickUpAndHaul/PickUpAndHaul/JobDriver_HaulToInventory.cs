using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using System.Diagnostics;
using UnityEngine;
using Verse.Sound;

namespace PickUpAndHaul
{
    public class JobDriver_HaulToInventory : JobDriver
    {
        public override bool TryMakePreToilReservations()
        {
            return this.pawn.Reserve(this.job.targetA, this.job, 1, -1, null);
        }

        [DebuggerHidden]
        protected override IEnumerable<Toil> MakeNewToils()
        {
            CompHauledToInventory takenToInventory = pawn.TryGetComp<CompHauledToInventory>();
            this.FailOnDestroyedOrNull(TargetIndex.A);

            Toil wait = Toils_General.Wait(2);
            Toil reserveTargetA = Toils_Reserve.Reserve(TargetIndex.A, 1, -1, null);
            yield return reserveTargetA;

            Toil gotoThing = new Toil
            {
                initAction = () =>
                {
                    this.pawn.pather.StartPath(this.TargetThingA, PathEndMode.ClosestTouch); //thing to change in case of persistent tweener issues. Shouldn't happen though.
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
                    int num = Mathf.Min(this.job.count, thing.stackCount, MassUtility.CountToPickUpUntilOverEncumbered(actor, thing)); //hauling jobs are stupid and the count is often set to 99999
                    if (num <= 0)
                    {
                        Job haul = HaulAIUtility.HaulToStorageJob(actor, thing);
                        if (haul != null)
                        {
                            if (haul.TryMakePreToilReservations(actor))
                            {
                                actor.jobs.jobQueue.EnqueueFirst(haul, new JobTag?(JobTag.Misc));
                                return;
                            }
                        }
                        actor.jobs.curDriver.JumpToToil(wait);
                    }
                    else
                    {
                        actor.inventory.GetDirectlyHeldThings().TryAdd(thing.SplitOff(num), false); 
                        //Merging and unmerging messes up the picked up ID (which already gets messed up enough)
                        takenToInventory.RegisterHauledItem(thing);
                    }
                }
            };
            yield return takeThing;
            yield return CheckDuplicateItemsToHaulToInventory(reserveTargetA, TargetIndex.A, false);
            yield return wait;
        }


        //regular Toils_Haul.CheckForGetOpportunityDuplicate isn't going to work for our purposes, since we're not carrying anything. 
        //Carrying something yields weird results with unspawning errors when transfering to inventory, so we copy-past-- I mean, implement our own.
        public static Toil CheckDuplicateItemsToHaulToInventory(Toil getHaulTargetToil, TargetIndex haulableInd, bool takeFromValidStorage = false, Predicate<Thing> extraValidator = null)
        {
            Toil toil = new Toil();
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                Job curJob = actor.jobs.curJob;

                Predicate<Thing> validator = (Thing t) => t.Spawned
                && HaulAIUtility.PawnCanAutomaticallyHaulFast(actor, t, false)
                && (takeFromValidStorage || !t.IsInValidStorage())
                && !t.IsForbidden(actor)
                && actor.CanReserve(t, 1, -1, null, false)
                && (extraValidator == null || extraValidator(t));

                Thing thing = GenClosest.ClosestThingReachable(actor.Position, actor.Map, ThingRequest.ForGroup(ThingRequestGroup.HaulableAlways), PathEndMode.ClosestTouch, 
                    TraverseParms.For(actor, Danger.Deadly, TraverseMode.ByPawn, false), 8f, validator, null, 0, -1, false, RegionType.Set_Passable, false);
                if (thing != null && MassUtility.EncumbrancePercent(actor) <= 0.90f)
                {
                    curJob.SetTarget(haulableInd, thing);
                    actor.jobs.curDriver.JumpToToil(getHaulTargetToil);
                }
                else if (thing != null)
                {
                    Job haul = HaulAIUtility.HaulToStorageJob(actor, thing);
                    if (haul != null) //because it can return null, and that ruins my day.
                    {
                        if (haul.TryMakePreToilReservations(actor))
                        {
                            actor.jobs.jobQueue.EnqueueFirst(haul, new JobTag?(JobTag.Misc));
                            return;
                        }
                    }
                }
            };
            return toil;
        }
    }
}