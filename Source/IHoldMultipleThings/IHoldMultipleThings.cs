using Verse;

namespace IHoldMultipleThings
{
    public interface IHoldMultipleThings
    {
        bool CapacityAt(ThingDef def, IntVec3 storeCell, Map map, out int capacity);

        bool StackableAt(ThingDef def, IntVec3 storeCell, Map map);
    }
}
