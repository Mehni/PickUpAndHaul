using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace PickUpAndHaul
{
    [StaticConstructorOnStartup]
    public class ModCompatibilityCheck
    {
        public static bool KnownConflict
        {
            get
            {
                return ModsConfig.ActiveModsInLoadOrder.Any(m => m.Name == "Combat Extended" /*|| m.Name == "While You're Up"*/);
            }
        }
    }
}
