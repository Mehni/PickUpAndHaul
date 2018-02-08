using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using UnityEngine;
using Harmony;
using System.Reflection.Emit;
using System.Reflection;
using Verse.AI;

namespace PickUpAndHaul
{

    [StaticConstructorOnStartup]
    static class HarmonyPatches
    {

        static HarmonyPatches()
        {
            HarmonyInstance harmony = HarmonyInstance.Create("mehni.rimworld.pickupthatcan.main");

            harmony.Patch(AccessTools.Method(typeof(FloatMenuMakerMap), "AddHumanlikeOrders"), null, null,
                new HarmonyMethod(typeof(HarmonyPatches), nameof(FloatMenuMakerMad_AddHumanlikeOrders_Transpiler)));

            harmony.Patch(AccessTools.Method(typeof(JobGiver_DropUnusedInventory), "TryGiveJob"), null,
                new HarmonyMethod(typeof(HarmonyPatches), nameof(DropUnusedInventory_PostFix)), null);

            harmony.Patch(AccessTools.Method(typeof(JobDriver_HaulToCell), "MakeNewToils"), null,
                new HarmonyMethod(typeof(HarmonyPatches), nameof(JobDriver_HaulToCell_PostFix)), null);

            harmony.Patch(AccessTools.Method(typeof(Pawn_InventoryTracker), "Notify_ItemRemoved"), null,
                new HarmonyMethod(typeof(HarmonyPatches), nameof(Pawn_InventoryTracker_PostFix)), null);

            harmony.Patch(AccessTools.Method(typeof(JobGiver_DropUnusedInventory), "Drop"),
                new HarmonyMethod(typeof(HarmonyPatches), nameof(Drop_Prefix)), null, null);

            harmony.Patch(AccessTools.Method(typeof(JobGiver_Idle), "TryGiveJob"), null,
                new HarmonyMethod(typeof(HarmonyPatches), nameof(IdleJoy_Postfix)), null);

            if (ModCompatibilityCheck.KnownConflict) Log.Message("Pick Up And Haul has found a conflicting mod and will lay dormant.");
            else Log.Message("PickUpAndHaul v0.18.1.5 welcomes you to RimWorld with pointless logspam.");
        }

        private static bool Drop_Prefix(ref Pawn pawn, ref Thing thing)
        {
            CompHauledToInventory takenToInventory = pawn.TryGetComp<CompHauledToInventory>();
            if (takenToInventory == null)
            {
                return true;
            }
            HashSet<Thing> carriedThing = takenToInventory.GetHashSet();

            if (carriedThing.Contains(thing))
            {
                return false;
            }
            return true;
        }

        private static void Pawn_InventoryTracker_PostFix(Pawn_InventoryTracker __instance, ref Thing item)
        {
            CompHauledToInventory takenToInventory = __instance.pawn.TryGetComp<CompHauledToInventory>();
            if (takenToInventory == null)
            {
                return;
            }

            HashSet<Thing> carriedThing = takenToInventory.GetHashSet();

            //if (__instance.pawn.Spawned) //weird issue with worldpawns was caused by not having the comp
            //{
            //    if (__instance.pawn.Faction?.IsPlayer ?? false) //roaming muffalo
            //    {
            if (carriedThing?.Count != 0)
            {
                if (carriedThing.Contains(item))
                {
                    carriedThing.Remove(item);
                }
            }
            //    }
            //} 
        }

        private static void JobDriver_HaulToCell_PostFix(JobDriver_HaulToCell __instance)
        {
            CompHauledToInventory takenToInventory = __instance.pawn.TryGetComp<CompHauledToInventory>();
            if (takenToInventory == null)
            {
                return;
            }
            HashSet<Thing> carriedThing = takenToInventory.GetHashSet();

            if (__instance.job.haulMode == HaulMode.ToCellStorage
                && __instance.pawn.Faction == Faction.OfPlayer
                && __instance.pawn.RaceProps.Humanlike
                && __instance.pawn.carryTracker.CarriedThing is Corpse == false
                //&& !__instance.pawn.carryTracker.CarriedThing.def.defName.Contains("Chunk") //HaulAsideJobFor is handled by HaulMode.ToCellStorage
                && carriedThing != null
                && carriedThing.Count !=0) //deliberate hauling job. Should unload.
            {
                PawnUnloadChecker.CheckIfPawnShouldUnloadInventory(__instance.pawn, true);
            }
            else //we could politely ask
            {
                PawnUnloadChecker.CheckIfPawnShouldUnloadInventory(__instance.pawn);
            }
        }

        public static void IdleJoy_Postfix(ref Pawn pawn)
        {
            PawnUnloadChecker.CheckIfPawnShouldUnloadInventory(pawn, true);
        }

        public static void DropUnusedInventory_PostFix(ref Pawn pawn)
        {
            PawnUnloadChecker.CheckIfPawnShouldUnloadInventory(pawn);
        }

        public static IEnumerable<CodeInstruction> FloatMenuMakerMad_AddHumanlikeOrders_Transpiler(IEnumerable<CodeInstruction> instructions)
        {

            MethodInfo playerHome = AccessTools.Property(typeof(Map), nameof(Map.IsPlayerHome)).GetGetMethod();
            List<CodeInstruction> instructionList = instructions.ToList();

            //instructionList.RemoveRange(instructions.FirstIndexOf(ci => ci.operand == playerHome) - 3, 5);
            //return instructionList;

            bool patched = false;

            for (int i = 0; i < instructionList.Count; i++)
            {
                CodeInstruction instruction = instructionList[i];
                if (!patched && instruction.operand == playerHome && !ModCompatibilityCheck.KnownConflict)
                //if (instructionList[i + 3].opcode == OpCodes.Callvirt && instruction.operand == playerHome)
                //if (instructionList[i + 3].operand == playerHome)
                {
                    {
                        instruction.opcode = OpCodes.Ldc_I4_0;
                        instruction.operand = null;
                        yield return instruction;
                        patched = true;
                    }
                    //    //{ instructionList[i + 5].labels = instruction.labels;}
                    //    instructionList.RemoveRange(i, 5);
                    //    patched = true;
                }
                yield return instruction;
            }
        }
    }
}