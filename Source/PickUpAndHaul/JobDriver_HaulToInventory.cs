using System.Linq;

namespace PickUpAndHaul;
public class JobDriver_HaulToInventory : JobDriver
{
	private static WorkGiver_HaulToInventory worker;
	public static WorkGiver_HaulToInventory Worker
	{
		get
		{
			return worker ??=
				DefDatabase<WorkGiverDef>.AllDefsListForReading.First(wg => wg.Worker is WorkGiver_HaulToInventory).Worker
					as WorkGiver_HaulToInventory;
		}
	}

	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		Log.Message($"{pawn} starting HaulToInventory job: {job.targetQueueA.ToStringSafeEnumerable()}:{job.countQueue.ToStringSafeEnumerable()}");
		pawn.ReserveAsManyAsPossible(job.targetQueueA, job);
		pawn.ReserveAsManyAsPossible(job.targetQueueB, job);
		return pawn.Reserve(job.targetQueueA[0], job) && pawn.Reserve(job.targetB, job);
	}

	public override IEnumerable<Toil> MakeNewToils()
	{
		var getNextTarget = Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.A);
		
		// do
		{
			// get next item
			yield return getNextTarget;
			// bail if we are too heavy
			yield return CheckForOverencumberedForCombatExtended();
			// walk to the thing
			yield return GotoTarget();
			// place it in our inventory
			yield return PickupTarget();
		}
		// while 
		yield return Toils_Jump.JumpIf(getNextTarget, () => !job.targetQueueA.NullOrEmpty());

		// search for more items around the pawns current position to haul (in case new items spawned while we were 
		// hauling), and queue up a new haul job if we find one.
		yield return SearchMoreHaulables();

		// queue up unload inventory job to happen immediately after the current job, ends the job.
		yield return EnqueueUnloadJob();
	}

	private static List<Thing> TempListForThings { get; } = new();

	// Make the pawn path to the next `Thing` pointed to by `TargetA`
	public Toil GotoTarget()
	{
		var gotoThing = new Toil
		{
			initAction = () => pawn.pather.StartPath(TargetThingA, PathEndMode.ClosestTouch),
			defaultCompleteMode = ToilCompleteMode.PatherArrival
		};
		gotoThing.FailOnDespawnedNullOrForbidden(TargetIndex.A);
		
		return gotoThing;
	}

	// Make the pawn pick up the `Thing` pointed to by `TargetA`. 
	//	pre: The pawn must be at a cell, where they can touch `TargetA`
	public Toil PickupTarget()
	{
		var inventoryComp = pawn.TryGetComp<CompHauledToInventory>();
		
		return new Toil
		{
			initAction = () =>
			{
				var actor = pawn;
				var thing = actor.CurJob.GetTarget(TargetIndex.A).Thing;
				Toils_Haul.ErrorCheckForCarry(actor, thing);

				//get max we can pick up
				var countToPickUp = Mathf.Min(job.count, MassUtility.CountToPickUpUntilOverEncumbered(actor, thing));
				Log.Message($"{actor} is hauling to inventory {thing}:{countToPickUp}");

				if (ModCompatibilityCheck.CombatExtendedIsActive)
				{
					countToPickUp = CompatHelper.CanFitInInventory(pawn, thing);
				}

				if (countToPickUp > 0)
				{
					var splitThing = thing.SplitOff(countToPickUp);
					var shouldMerge = inventoryComp.GetHashSet().Any(x => x.def == thing.def);
					actor.inventory.GetDirectlyHeldThings().TryAdd(splitThing, shouldMerge);
					inventoryComp.RegisterHauledItem(splitThing);

					if (ModCompatibilityCheck.CombatExtendedIsActive)
					{
						CompatHelper.UpdateInventory(pawn);
					}
				}

				//thing still remains, so queue up hauling if we can + end the current job (smooth/instant transition)
				//This will technically release the reservations in the queue, but what can you do
				if (thing.Spawned)
				{
					var haul = HaulAIUtility.HaulToStorageJob(actor, thing, actor.CurJob.playerForced);
					if (haul?.TryMakePreToilReservations(actor, false) ?? false)
					{
						actor.jobs.jobQueue.EnqueueFirst(haul, JobTag.Misc);
						EndJobWith(JobCondition.Succeeded);
					}
				}
			}
		};
	}

	// Search around the pawns current position for more haulables to haul to our inventory. 
	public Toil SearchMoreHaulables()
	{
		return new Toil
		{
			initAction = () =>
			{
				var haulables = TempListForThings;
				haulables.Clear();
				haulables.AddRange(pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling());
				
				Job haulMoreJob = null;
				var haulMoreThing = WorkGiver_HaulToInventory.GetClosestAndRemove(pawn.Position, pawn.Map, haulables, PathEndMode.ClosestTouch,
					TraverseParms.For(pawn), 12, t => (haulMoreJob = Worker.JobOnThing(pawn, t)) != null);

				//WorkGiver_HaulToInventory found more work nearby
				if (haulMoreThing != null)
				{
					Log.Message($"{pawn} hauling again : {haulMoreThing}");
					if (haulMoreJob.TryMakePreToilReservations(pawn, false))
					{
						pawn.jobs.jobQueue.EnqueueFirst(haulMoreJob, JobTag.Misc);
						EndJobWith(JobCondition.Succeeded);
					}
				}
			}
		};
	}

	public Toil EnqueueUnloadJob()
	{
		return new Toil //Queue next job
		{
			initAction = () =>
			{
				var actor = pawn;
				var curJob = actor.jobs.curJob;
				var storeCell = curJob.targetB;

				var unloadJob = JobMaker.MakeJob(PickUpAndHaulJobDefOf.UnloadYourHauledInventory, storeCell);
				if (unloadJob.TryMakePreToilReservations(actor, false))
				{
					actor.jobs.jobQueue.EnqueueFirst(unloadJob, JobTag.Misc);
					EndJobWith(JobCondition.Succeeded);
					//This will technically release the cell reservations in the queue, but what can you do
				}
			}
		};
	}

	/// <summary>
	/// the workgiver checks for encumbered, this is purely extra for CE
	/// </summary>
	/// <returns></returns>
	public Toil CheckForOverencumberedForCombatExtended()
	{
		var toil = new Toil();

		if (!ModCompatibilityCheck.CombatExtendedIsActive)
		{
			return toil;
		}

		toil.initAction = () =>
		{
			var actor = toil.actor;
			var curJob = actor.jobs.curJob;
			var nextThing = curJob.targetA.Thing;

			var ceOverweight = CompatHelper.CeOverweight(pawn);

			if (!(MassUtility.EncumbrancePercent(actor) <= 0.9f && !ceOverweight))
			{
				var haul = HaulAIUtility.HaulToStorageJob(actor, nextThing, curJob.playerForced);
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
