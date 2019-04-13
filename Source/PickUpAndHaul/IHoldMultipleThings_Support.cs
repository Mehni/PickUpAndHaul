using Verse;

namespace PickUpAndHaul
{
    public class HoldMultipleThings_Support
    {
        // ReSharper disable SuspiciousTypeConversion.Global
        public static bool CapacityAt(ThingDef def, IntVec3 storeCell, Map map, out int capacity)
        {
            capacity = 0;

            foreach (Thing t in storeCell.GetThingList(map))

                if (t is IHoldMultipleThings.IHoldMultipleThings holderOfMultipleThings)
                    return holderOfMultipleThings.CapacityAt(def, storeCell, map, out capacity);

                else if (t is ThingWithComps thingWith)
                    foreach (ThingComp thingComp in thingWith.AllComps)
                        if (thingComp is IHoldMultipleThings.IHoldMultipleThings compOfHolding)
                            return compOfHolding.CapacityAt(def, storeCell, map, out capacity);

            return false;
        }

        public static bool StackableAt(ThingDef def, IntVec3 storeCell, Map map)
        {
            foreach (Thing t in storeCell.GetThingList(map))

                if (t is IHoldMultipleThings.IHoldMultipleThings holderOfMultipleThings)
                    return holderOfMultipleThings.StackableAt(def, storeCell, map);

                else if (t is ThingWithComps thingWith)
                    foreach (ThingComp thingComp in thingWith.AllComps)
                        if (thingComp is IHoldMultipleThings.IHoldMultipleThings compOfHolding)
                            return compOfHolding.StackableAt(def, storeCell, map);

            return false;
        }
    }
}
