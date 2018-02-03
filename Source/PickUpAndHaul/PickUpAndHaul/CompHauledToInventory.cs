using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;
using UnityEngine;
using Verse.AI;

namespace PickUpAndHaul
{
    public class CompHauledToInventory : ThingComp
    {
        private HashSet<Thing> TakenToInventory = new HashSet<Thing>();

        public HashSet<Thing> GetHashSet()
        {
            return TakenToInventory;
        }
        
        public void RegisterHauledItem(Thing thing)
        {
            this.TakenToInventory.Add(thing);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look<Thing>(ref this.TakenToInventory, "ThingsHauledToInventory", LookMode.Reference);
        }
    }
}
