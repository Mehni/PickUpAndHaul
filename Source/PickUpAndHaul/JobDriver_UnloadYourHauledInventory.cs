﻿using System.Linq;

namespace PickUpAndHaul;

public class JobDriver_UnloadYourHauledInventory : JobDriver
{
	private int _countToDrop = -1;
	private int _unloadDuration = 3;

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look<int>(ref _countToDrop, "countToDrop", -1);
	}

	public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

	/// <summary>
	/// Find spot, reserve spot, pull thing out of inventory, go to spot, drop stuff, repeat.
	/// </summary>
	/// <returns></returns>
	public override IEnumerable<Toil> MakeNewToils()
	{
		if (ModCompatibilityCheck.ExtendedStorageIsActive)
		{
			_unloadDuration = 20;
		}

		var begin = Toils_General.Wait(_unloadDuration);
		yield return begin;

		var carriedThings = pawn.TryGetComp<CompHauledToInventory>().GetHashSet();
		yield return FindTargetOrDrop(carriedThings);
		yield return PullItemFromInventory(carriedThings, begin);

		var releaseReservation = ReleaseReservation();
		var carryToCell = Toils_Haul.CarryHauledThingToCell(TargetIndex.B);

		// Equivalent to if (TargetB.HasThing)
		yield return Toils_Jump.JumpIf(carryToCell, TargetIsCell);

		var carryToContainer = Toils_Haul.CarryHauledThingToContainer();
		yield return carryToContainer;
		yield return Toils_Haul.DepositHauledThingInContainer(TargetIndex.B, TargetIndex.None);
		yield return Toils_Haul.JumpToCarryToNextContainerIfPossible(carryToContainer, TargetIndex.B);
		// Equivalent to jumping out of the else block
		yield return Toils_Jump.Jump(releaseReservation);

		// Equivalent to else
		yield return carryToCell;
		yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.B, carryToCell, true);

		//If the original cell is full, PlaceHauledThingInCell will set a different TargetIndex resulting in errors on yield return Toils_Reserve.Release.
		//We still gotta release though, mostly because of Extended Storage.
		yield return releaseReservation;
		yield return Toils_Jump.Jump(begin);
	}

	private bool TargetIsCell() => !TargetB.HasThing;

	private Toil ReleaseReservation()
	{
		return new()
		{
			initAction = () =>
			{
				if (pawn.Map.reservationManager.ReservedBy(job.targetB, pawn, pawn.CurJob))
				{
					pawn.Map.reservationManager.Release(job.targetB, pawn, pawn.CurJob);
				}
			}
		};
	}

	private Toil PullItemFromInventory(HashSet<Thing> carriedThings, Toil wait)
	{
		return new()
		{
			initAction = () =>
			{
				var thing = job.GetTarget(TargetIndex.A).Thing;
				if (thing == null || !pawn.inventory.innerContainer.Contains(thing))
				{
					carriedThings.Remove(thing);
					pawn.jobs.curDriver.JumpToToil(wait);
					return;
				}
				if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) || !thing.def.EverStorable(false))
				{
					Log.Message($"Pawn {pawn} incapable of hauling, dropping {thing}");
					pawn.inventory.innerContainer.TryDrop(thing, ThingPlaceMode.Near, _countToDrop, out thing);
					EndJobWith(JobCondition.Succeeded);
					carriedThings.Remove(thing);
				}
				else
				{
					pawn.inventory.innerContainer.TryTransferToContainer(thing, pawn.carryTracker.innerContainer,
						_countToDrop, out thing);
					job.count = _countToDrop;
					job.SetTarget(TargetIndex.A, thing);
					carriedThings.Remove(thing);
				}

				if (ModCompatibilityCheck.CombatExtendedIsActive)
				{
					CompatHelper.UpdateInventory(pawn);
				}

				thing.SetForbidden(false, false);
			}
		};
	}

	private Toil FindTargetOrDrop(HashSet<Thing> carriedThings)
	{
		return new()
		{
			initAction = () =>
			{
				var unloadableThing = FirstUnloadableThing(pawn, carriedThings);

				if (unloadableThing.Count == 0)
				{
					if (carriedThings.Count == 0)
					{
						EndJobWith(JobCondition.Succeeded);
					}
					return;
				}

				var currentPriority = StoragePriority.Unstored; // Currently in pawns inventory, so it's unstored
				if (StoreUtility.TryFindBestBetterStorageFor(unloadableThing.Thing, pawn, pawn.Map, currentPriority,
						pawn.Faction, out var cell, out var destination))
				{
					job.SetTarget(TargetIndex.A, unloadableThing.Thing);
					if (cell == IntVec3.Invalid)
					{
						job.SetTarget(TargetIndex.B, destination as Thing);
					}
					else
					{
						job.SetTarget(TargetIndex.B, cell);
					}

					Log.Message($"{pawn} found destination {job.targetB} for thing {unloadableThing.Thing}");
					if (!pawn.Map.reservationManager.Reserve(pawn, job, job.targetB))
					{
						Log.Message(
							$"{pawn} failed reserving destination {job.targetB}, dropping {unloadableThing.Thing}");
						pawn.inventory.innerContainer.TryDrop(unloadableThing.Thing, ThingPlaceMode.Near,
							unloadableThing.Thing.stackCount, out _);
						EndJobWith(JobCondition.Incompletable);
						return;
					}
					_countToDrop = unloadableThing.Thing.stackCount;
				}
				else
				{
					Log.Message(
						$"Pawn {pawn} unable to find hauling destination, dropping {unloadableThing.Thing}");
					pawn.inventory.innerContainer.TryDrop(unloadableThing.Thing, ThingPlaceMode.Near,
						unloadableThing.Thing.stackCount, out _);
					EndJobWith(JobCondition.Succeeded);
				}
			}
		};
	}

	private static ThingCount FirstUnloadableThing(Pawn pawn, HashSet<Thing> carriedThings)
	{
		var innerPawnContainer = pawn.inventory.innerContainer;

		foreach (var thing in carriedThings.OrderBy(t => t.def.FirstThingCategory?.index).ThenBy(x => x.def.defName))
		{
			//find the overlap.
			if (!innerPawnContainer.Contains(thing))
			{
				//merged partially picked up stacks get a different thingID in inventory
				var stragglerDef = thing.def;
				carriedThings.Remove(thing);

				//we have no method of grabbing the newly generated thingID. This is the solution to that.
				for (var i = 0; i < innerPawnContainer.Count; i++)
				{
					var dirtyStraggler = innerPawnContainer[i];
					if (dirtyStraggler.def == stragglerDef)
					{
						return new ThingCount(dirtyStraggler, dirtyStraggler.stackCount);
					}
				}
			}
			return new ThingCount(thing, thing.stackCount);
		}
		return default;
	}
}
