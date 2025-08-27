using System.Linq;

namespace PickUpAndHaul;
public class WorkGiver_HaulToInventory : WorkGiver_HaulGeneral
{
	//Thanks to AlexTD for the more dynamic search range
	//And queueing
	//And optimizing
	private const float SEARCH_FOR_OTHERS_RANGE_FRACTION = 0.5f;

	public override bool ShouldSkip(Pawn pawn, bool forced = false)
		=> base.ShouldSkip(pawn, forced)
		|| pawn.Faction != Faction.OfPlayerSilentFail
		|| !Settings.IsAllowedRace(pawn.RaceProps)
		|| pawn.GetComp<CompHauledToInventory>() == null
		|| pawn.IsQuestLodger()
		|| OverAllowedGearCapacity(pawn);

	public static bool GoodThingToHaul(Thing t, Pawn pawn)
		=> OkThingToHaul(t, pawn)
		&& IsNotCorpseOrAllowed(t)
		&& !t.IsInValidBestStorage();

	public static bool OkThingToHaul(Thing t, Pawn pawn)
		=> t.Spawned
		&& pawn.CanReserve(t)
		&& !t.IsForbidden(pawn);

	public static bool IsNotCorpseOrAllowed(Thing t) => Settings.AllowCorpses || t is not Corpse;

	public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
	{
		var list = pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling().ToList();
		Comparer.rootCell = pawn.Position;
		list.Sort(Comparer);
		return list;
	}

	private static ThingPositionComparer Comparer { get; } = new();
	public class ThingPositionComparer : IComparer<Thing>
	{
		public IntVec3 rootCell;
		public int Compare(Thing x, Thing y) => (x.Position - rootCell).LengthHorizontalSquared.CompareTo((y.Position - rootCell).LengthHorizontalSquared);
	}

	public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
		=> OkThingToHaul(thing, pawn)
		&& IsNotCorpseOrAllowed(thing)
		&& HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, forced)
		&& StoreUtility.TryFindBestBetterStorageFor(thing, pawn, pawn.Map, StoreUtility.CurrentStoragePriorityOf(thing), pawn.Faction, out _, out _, false);

	//bulky gear (power armor + minigun) so don't bother.
	public static bool OverAllowedGearCapacity(Pawn pawn) => MassUtility.GearMass(pawn) / MassUtility.Capacity(pawn) >= Settings.MaximumOccupiedCapacityToConsiderHauling;

	//pick up stuff until you can't anymore,
	//while you're up and about, pick up something and haul it
	//before you go out, empty your pockets
	public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
	{
		if (!OkThingToHaul(thing, pawn) || !HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, forced))
		{
			return null;
		}

		if (OverAllowedGearCapacity(pawn)
			|| pawn.GetComp<CompHauledToInventory>() is null // Misc. Robots compatibility
															 // See https://github.com/catgirlfighter/RimWorld_CommonSense/blob/master/Source/CommonSense11/CommonSense/OpportunisticTasks.cs#L129-L140
			|| !IsNotCorpseOrAllowed(thing) //This WorkGiver gets hijacked by AllowTool and expects us to urgently haul corpses.
			|| MassUtility.WillBeOverEncumberedAfterPickingUp(pawn, thing, 1)) //https://github.com/Mehni/PickUpAndHaul/pull/18
		{
			return HaulAIUtility.HaulToStorageJob(pawn, thing, forced);
		}

		var map = pawn.Map;
		var designationManager = map.designationManager;
		var currentPriority = StoreUtility.CurrentStoragePriorityOf(thing);
		ThingOwner nonSlotGroupThingOwner = null;
		StoreTarget storeTarget;
		if (StoreUtility.TryFindBestBetterStorageFor(thing, pawn, map, currentPriority, pawn.Faction, out var targetCell, out var haulDestination, true))
		{
			if (haulDestination is ISlotGroupParent)
			{
				//since we've gone through all the effort of getting the loc, might as well use it.
				//Don't multi-haul food to hoppers.
				if (HaulToHopperJob(thing, targetCell, map))
				{
					return HaulAIUtility.HaulToStorageJob(pawn, thing, forced);
				}
				else
				{
					storeTarget = new(targetCell);
				}
			}
			else if (haulDestination is Thing destinationAsThing && (nonSlotGroupThingOwner = destinationAsThing.TryGetInnerInteractableThingOwner()) != null)
			{
				storeTarget = new(destinationAsThing);
			}
			else
			{
				Verse.Log.Error("Don't know how to handle HaulToStorageJob for storage " + haulDestination.ToStringSafe() + ". thing=" + thing.ToStringSafe());
				return null;
			}
		}
		else
		{
			JobFailReason.Is("NoEmptyPlaceLower".Translate());
			return null;
		}

		//credit to Dingo
		var capacityStoreCell
			= storeTarget.container is null ? CapacityAt(thing, storeTarget.cell, map)
			: nonSlotGroupThingOwner.GetCountCanAccept(thing);

		if (capacityStoreCell == 0)
		{
			return HaulAIUtility.HaulToStorageJob(pawn, thing, forced);
		}

		var job = JobMaker.MakeJob(PickUpAndHaulJobDefOf.HaulToInventory, null, storeTarget);   //Things will be in queues
		Log.Message($"-------------------------------------------------------------------");
		Log.Message($"------------------------------------------------------------------");//different size so the log doesn't count it 2x
		Log.Message($"{pawn} job found to haul: {thing} to {storeTarget}:{capacityStoreCell}, looking for more now");

		//Find what fits in inventory, set nextThingLeftOverCount to be 
		var nextThingLeftOverCount = 0;
		var encumberance = MassUtility.EncumbrancePercent(pawn);
		job.targetQueueA = new List<LocalTargetInfo>(); //more things
		job.targetQueueB = new List<LocalTargetInfo>(); //more storage; keep in mind the job doesn't use it, but reserve it so you don't over-haul
		job.countQueue = new List<int>();//thing counts

		var ceOverweight = false;

		if (ModCompatibilityCheck.CombatExtendedIsActive)
		{
			ceOverweight = CompatHelper.CeOverweight(pawn);
		}

		var distanceToHaul = (storeTarget.Position - thing.Position).LengthHorizontal * SEARCH_FOR_OTHERS_RANGE_FRACTION;
		var distanceToSearchMore = Math.Max(12f, distanceToHaul);

		//Find extra things than can be hauled to inventory, queue to reserve them
		var haulUrgentlyDesignation = PickUpAndHaulDesignationDefOf.haulUrgently;
		var isUrgent = ModCompatibilityCheck.AllowToolIsActive && designationManager.DesignationOn(thing)?.def == haulUrgentlyDesignation;

		var haulables = new List<Thing>(map.listerHaulables.ThingsPotentiallyNeedingHauling());
		Comparer.rootCell = thing.Position;
		haulables.Sort(Comparer);

		var nextThing = thing;
		var lastThing = thing;

		var storeCellCapacity = new Dictionary<StoreTarget, CellAllocation>()
		{
			[storeTarget] = new(nextThing, capacityStoreCell)
		};
		//skipTargets = new() { storeTarget };
		skipCells = new();
		skipThings = new();
		if (storeTarget.container != null)
		{
			skipThings.Add(storeTarget.container);
		}
		else
		{
			skipCells.Add(storeTarget.cell);
		}

		bool Validator(Thing t)
			=> (!isUrgent || designationManager.DesignationOn(t)?.def == haulUrgentlyDesignation)
			&& GoodThingToHaul(t, pawn) && HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, false); //forced is false, may differ from first thing

		haulables.Remove(thing);

		do
		{
			if (AllocateThingAtCell(storeCellCapacity, pawn, nextThing, job))
			{
				lastThing = nextThing;
				encumberance += AddedEncumberance(pawn, nextThing);

				if (encumberance > 1 || ceOverweight)
				{
					//can't CountToPickUpUntilOverEncumbered here, pawn doesn't actually hold these things yet
					nextThingLeftOverCount = CountPastCapacity(pawn, nextThing, encumberance);
					job.countQueue.Pop();
					job.countQueue.Add(nextThingLeftOverCount);
					Log.Message($"Inventory allocated, will carry {nextThing}:{nextThingLeftOverCount}");

					// We are now out of inventory space - and should bail right away.
					break;
				}
			}
		}
		while ((nextThing = GetClosestAndRemove(lastThing.Position, map, haulables, PathEndMode.ClosestTouch,
			TraverseParms.For(pawn), distanceToSearchMore, Validator)) != null);

		skipCells = null;
		skipThings = null;
		return job;
	}

	private static bool HaulToHopperJob(Thing thing, IntVec3 targetCell, Map map)
	{
		if (thing.def.IsNutritionGivingIngestible
			&& thing.def.ingestible.preferability is FoodPreferability.RawBad or FoodPreferability.RawTasty)
		{
			var thingList = targetCell.GetThingList(map);
			for (var i = 0; i < thingList.Count; i++)
			{
				if (thingList[i].def == ThingDefOf.Hopper)
				{
					return true;
				}
			}
		}
		return false;
	}

	public struct StoreTarget : IEquatable<StoreTarget>
	{
		public IntVec3 cell;
		public Thing container;
		public IntVec3 Position => container?.Position ?? cell;

		public StoreTarget(IntVec3 cell)
		{
			this.cell = cell;
			container = null;
		}
		public StoreTarget(Thing container)
		{
			cell = default;
			this.container = container;
		}

		public bool Equals(StoreTarget other) => container is null ? other.container is null && cell == other.cell : container == other.container;
		public override int GetHashCode() => container?.GetHashCode() ?? cell.GetHashCode();
		public override string ToString() => container?.ToString() ?? cell.ToString();
		public override bool Equals(object obj) => obj is StoreTarget target ? Equals(target) : obj is Thing thing ? container == thing : obj is IntVec3 intVec && cell == intVec;
		public static bool operator ==(StoreTarget left, StoreTarget right) => left.Equals(right);
		public static bool operator !=(StoreTarget left, StoreTarget right) => !left.Equals(right);
		public static implicit operator LocalTargetInfo(StoreTarget target) => target.container != null ? target.container : target.cell;
	}

	public static Thing GetClosestAndRemove(IntVec3 center, Map map, List<Thing> searchSet, PathEndMode peMode, TraverseParms traverseParams, float maxDistance = 9999f, Predicate<Thing> validator = null)
	{
		if (searchSet == null || !searchSet.Any())
		{
			return null;
		}

		var maxDistanceSquared = maxDistance * maxDistance;

		while (FindClosestThing(searchSet, center, out var i) is { } closestThing)
		{
			searchSet.RemoveAt(i);
			if (!closestThing.Spawned)
			{
				continue;
			}

			if ((center - closestThing.Position).LengthHorizontalSquared > maxDistanceSquared)
			{
				break;
			}

			if (!map.reachability.CanReach(center, closestThing, peMode, traverseParams))
			{
				continue;
			}

			if (validator == null || validator(closestThing))
			{
				return closestThing;
			}
		}

		return null;
	}

	public static Thing FindClosestThing(List<Thing> searchSet, IntVec3 center, out int index)
	{
		if (!searchSet.Any())
		{
			index = -1;
			return null;
		}

		var closestThing = searchSet[0];
		index = 0;
		var closestThingSquaredLength = (center - closestThing.Position).LengthHorizontalSquared;
		var count = searchSet.Count;
		for (var i = 1; i < count; i++)
		{
			if (closestThingSquaredLength > (center - searchSet[i].Position).LengthHorizontalSquared)
			{
				closestThing = searchSet[i];
				index = i;
				closestThingSquaredLength = (center - closestThing.Position).LengthHorizontalSquared;
			}
		}
		return closestThing;
	}

	public class CellAllocation
	{
		public Thing allocated;
		public int capacity;

		public CellAllocation(Thing a, int c)
		{
			allocated = a;
			capacity = c;
		}
	}

	public static int CapacityAt(Thing thing, IntVec3 storeCell, Map map)
	{
		if (HoldMultipleThings_Support.CapacityAt(thing, storeCell, map, out var capacity))
		{
			Log.Message($"Found external capacity of {capacity}");
			return capacity;
		}

		return storeCell.GetItemStackSpaceLeftFor(map, thing.def);
	}

	public static bool Stackable(Thing nextThing, KeyValuePair<StoreTarget, CellAllocation> allocation)
		=> nextThing == allocation.Value.allocated
		|| allocation.Value.allocated.CanStackWith(nextThing)
		|| HoldMultipleThings_Support.StackableAt(nextThing, allocation.Key.cell, nextThing.Map);

	public static bool AllocateThingAtCell(Dictionary<StoreTarget, CellAllocation> storeCellCapacity, Pawn pawn, Thing nextThing, Job job)
	{
		var map = pawn.Map;
		var allocation = storeCellCapacity.FirstOrDefault(kvp =>
			kvp.Key is var storeTarget
			&& (storeTarget.container?.TryGetInnerInteractableThingOwner().CanAcceptAnyOf(nextThing)
			?? storeTarget.cell.GetSlotGroup(map).parent.Accepts(nextThing))
			&& Stackable(nextThing, kvp));
		var storeCell = allocation.Key;

		//Can't stack with allocated cells, find a new cell:
		if (storeCell == default)
		{
			var currentPriority = StoreUtility.CurrentStoragePriorityOf(nextThing);
			if (StoreUtility.TryFindBestBetterStorageFor(nextThing, pawn, map, currentPriority, pawn.Faction, out var nextStoreCell, out var haulDestination))
			{
				if (HasThingOwner(haulDestination, out var innerInteractableThingOwner))
				{
					var destinationAsThing = (Thing)haulDestination;
					storeCell = new(destinationAsThing);
					job.targetQueueB.Add(destinationAsThing);

					storeCellCapacity[storeCell] = new(nextThing, innerInteractableThingOwner.GetCountCanAccept(nextThing));
					Log.Message($"{pawn} New haulDestination for unstackable {nextThing} = {haulDestination}");
				}
				else
				{
					storeCell = new(nextStoreCell);
					job.targetQueueB.Add(nextStoreCell);

					storeCellCapacity[storeCell] = new(nextThing, CapacityAt(nextThing, nextStoreCell, map));
					Log.Message($"{pawn} New cell for unstackable {nextThing} = {nextStoreCell}");
				}
			}
			else
			{
				Log.Message($"{pawn} {nextThing} can't stack with allocated cells");

				if (job.targetQueueA.NullOrEmpty())
				{
					job.targetQueueA.Add(nextThing);
				}

				return false;
			}
		}

		job.targetQueueA.Add(nextThing);
		var count = nextThing.stackCount;
		storeCellCapacity[storeCell].capacity -= count;
		Log.Message($"{pawn} allocating {nextThing}:{count}, now {storeCell}:{storeCellCapacity[storeCell].capacity}");

		while (storeCellCapacity[storeCell].capacity <= 0)
		{
			var capacityOver = -storeCellCapacity[storeCell].capacity;
			storeCellCapacity.Remove(storeCell);

			Log.Message($"{pawn} overdone {storeCell} by {capacityOver}");

			if (capacityOver == 0)
			{
				break;  //don't find new cell, might not have more of this thing to haul
			}

			var currentPriority = StoreUtility.CurrentStoragePriorityOf(nextThing);
			if (StoreUtility.TryFindBestBetterStorageFor(nextThing, pawn, map, currentPriority, pawn.Faction, out var nextStoreCell, out var nextHaulDestination))
			{
				if (HasThingOwner(nextHaulDestination, out var innerInteractableThingOwner))
				{
					var destinationAsThing = (Thing)nextHaulDestination;
					storeCell = new(destinationAsThing);
					job.targetQueueB.Add(destinationAsThing);
					
					var capacity = innerInteractableThingOwner.GetCountCanAccept(nextThing) - capacityOver;

					storeCellCapacity[storeCell] = new(nextThing, capacity);
					Log.Message($"{pawn} New haulDestination {nextHaulDestination}:{capacity}, allocated extra {capacityOver}");
				}
				else
				{
					storeCell = new(nextStoreCell);
					job.targetQueueB.Add(nextStoreCell);

					var capacity = CapacityAt(nextThing, nextStoreCell, map) - capacityOver;
					storeCellCapacity[storeCell] = new(nextThing, capacity);
					Log.Message($"{pawn} New cell {nextStoreCell}:{capacity}, allocated extra {capacityOver}");
				}
			}
			else
			{
				count -= capacityOver;
				job.countQueue.Add(count);
				Log.Message($"{pawn} Nowhere else to store, allocated {nextThing}:{count}");
				return false;
			}
		}
		job.countQueue.Add(count);
		Log.Message($"{pawn} {nextThing}:{count} allocated");
		return true;
	}

	//public static HashSet<StoreTarget> skipTargets;
	public static HashSet<IntVec3> skipCells;
	public static HashSet<Thing> skipThings;

	public static float AddedEncumberance(Pawn pawn, Thing thing)
		=> thing.stackCount * thing.GetStatValue(StatDefOf.Mass) / MassUtility.Capacity(pawn);

	public static int CountPastCapacity(Pawn pawn, Thing thing, float encumberance)
		=> (int)Math.Ceiling((encumberance - 1) * MassUtility.Capacity(pawn) / thing.GetStatValue(StatDefOf.Mass));

	public static bool HasThingOwner(IHaulDestination destination, out ThingOwner thingOwner)
	{
		if (destination is Thing thing)
		{
			thingOwner = thing.TryGetInnerInteractableThingOwner();
			return thingOwner != null;
		}
		thingOwner = null;
		return false;
	}
}

public static class PickUpAndHaulDesignationDefOf
{
	public static DesignationDef haulUrgently = DefDatabase<DesignationDef>.GetNamedSilentFail("HaulUrgentlyDesignation");
}
