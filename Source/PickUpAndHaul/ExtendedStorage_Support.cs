using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;
using UnityEngine;

namespace PickUpAndHaul
{
    class ExtendedStorage_Support
    {
        public static bool CapacityAt(ThingDef def, IntVec3 storeCell, Map map, out int capacity)
        {
            if (ModCompatibilityCheck.ExtendedStorageIsActive)
            {
                try
                {
                    return CapacityAtEx(def, storeCell, map, out capacity);
                }
                catch (Exception e)
                {
                    Verse.Log.Warning($"Pick Up And Haul tried to get Extended Storage capacity but it failed: {e}");
                }
            }
            capacity = 0;
            return false;
        }

        public static bool CapacityAtEx(ThingDef def, IntVec3 storeCell, Map map, out int capacity)
        {
            foreach (Thing thing in storeCell.GetThingList(map))
            {
                //thing.Position seems to be the input cell, which can just be handled normally
                if (thing.Position == storeCell) continue;  

                ExtendedStorage.Building_ExtendedStorage storage = thing as ExtendedStorage.Building_ExtendedStorage;
                
                if(storage.StoredThingTotal == 0)
                    capacity = (int)(def.stackLimit * storage.GetStatValue(ExtendedStorage.DefReferences.Stat_ES_StorageFactor));
                else 
                    capacity = storage.ApparentMaxStorage - storage.StoredThingTotal;
                Log.Message($"AT {storeCell} ES: {capacity} = {storage.ApparentMaxStorage} - {storage.StoredThingTotal}");
                return true;
            }
            capacity = 0;
            return false;
        }
    }
}
