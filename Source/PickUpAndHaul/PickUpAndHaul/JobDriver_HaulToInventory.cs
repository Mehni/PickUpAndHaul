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
                    int num = Mathf.Min(this.job.count, thing.stackCount); //hauling jobs are stupid and the count is often set to 99999
                    if (num <= 0)
                    {
                        actor.jobs.curDriver.ReadyForNextToil();
                    }
                    else
                    {
                        actor.inventory.GetDirectlyHeldThings().TryAdd(thing.SplitOff(num), false);
                        takenToInventory.RegisterHauledItem(thing);
                    }
                }
            };
            yield return takeThing;
        }
    }
}