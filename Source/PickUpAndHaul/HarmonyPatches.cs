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
            HarmonyInstance harmony = HarmonyInstance.Create(id: "mehni.rimworld.pickupandhaul.main");

            harmony.Patch(original: AccessTools.Method(typeof(FloatMenuMakerMap), "AddHumanlikeOrders"),
                transpiler: new HarmonyMethod(typeof(HarmonyPatches), nameof(FloatMenuMakerMad_AddHumanlikeOrders_Transpiler)));

            harmony.Patch(original: AccessTools.Method(typeof(JobGiver_DropUnusedInventory), "TryGiveJob"),
                postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(DropUnusedInventory_PostFix)));

            harmony.Patch(original: AccessTools.Method(typeof(JobDriver_HaulToCell), "MakeNewToils"),
                postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(JobDriver_HaulToCell_PostFix)));

            harmony.Patch(original: AccessTools.Method(typeof(Pawn_InventoryTracker), "Notify_ItemRemoved"),
                postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Pawn_InventoryTracker_PostFix)));

            harmony.Patch(original: AccessTools.Method(typeof(JobGiver_DropUnusedInventory), "Drop"),
                prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Drop_Prefix)));

            harmony.Patch(original: AccessTools.Method(typeof(JobGiver_Idle), "TryGiveJob"),
                postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(IdleJoy_Postfix)));

            //await async UpdateAllowTool(async)
            //try
            //{
            //    ((Action)(() =>
            //    {
            //        if (ModCompatibilityCheck.AllowToolIsActive)
            //        {
            //            harmony.Patch(AccessTools.Method(typeof(AllowTool.WorkGiver_HaulUrgently), nameof(AllowTool.WorkGiver_HaulUrgently.JobOnThing)),
            //                new HarmonyMethod(typeof(HarmonyPatches), nameof(AllowToolHaulUrgentlyJobOnThing_PreFix)), null, null);
            //        }
            //    }))();
            //}
            //catch (TypeLoadException) { }

            ////Thanks to AlexTD for the While You're Up functionality improvement
            // Currently commented out because Why Does It Still exist even?
            //try
            //{
            //    ((Action)(() =>
            //    {
            //        if (ModCompatibilityCheck.WhileYoureUpIsActive)
            //        {
            //            harmony.Patch(AccessTools.Method(typeof(WhileYoureUp.Utils), "MaybeHaulOtherStuffFirst"),
            //                null, new HarmonyMethod(typeof(HarmonyPatches), nameof(WhileYoureUpMaybeHaulOtherStuffFirst_PostFix)), null);
            //        }
            //    }))();
            //}
            //catch (TypeLoadException) { }

            Verse.Log.Message("PickUpAndHaul v0.1.0.5 welcomes you to RimWorld with pointless logspam.");
            harmony.PatchAll();
        }

        private static bool AllowToolHaulUrgentlyJobOnThing_PreFix(ref Job __result, Pawn pawn, Thing t, bool forced = false)
        {
            if (ModCompatibilityCheck.AllowToolIsActive)
            {
                //allowTool HaulUrgently
                CompHauledToInventory takenToInventory = pawn.TryGetComp<CompHauledToInventory>();

                if (pawn.RaceProps.Humanlike
                    && pawn.Faction == Faction.OfPlayer
                    && t is Corpse == false
                    && takenToInventory != null
                    && !t.def.defName.Contains("Chunk") //most of the time we don't have space for it
                    )
                {
                    StoragePriority currentPriority = StoreUtility.CurrentStoragePriorityOf(t);
                    if (!StoreUtility.TryFindBestBetterStoreCellFor(t, pawn, pawn.Map, currentPriority, pawn.Faction, out IntVec3 storeCell))
                    {
                        JobFailReason.Is("NoEmptyPlaceLower".Translate());
                        return false;
                    }

                    WorkGiver_HaulToInventory haulWG = (WorkGiver_HaulToInventory)pawn.workSettings.WorkGiversInOrderNormal.Find(wg => wg is WorkGiver_HaulToInventory);

                    Job haul = haulWG.JobOnThing(pawn, t, forced);
                    __result = haul;
                    return false;
                }
            }
            return true;
        }

        private static bool Drop_Prefix(Pawn pawn, Thing thing)
        {
            CompHauledToInventory takenToInventory = pawn.TryGetComp<CompHauledToInventory>();
            if (takenToInventory == null)
                return true;

            HashSet<Thing> carriedThing = takenToInventory.GetHashSet();

            if (carriedThing.Contains(thing))
            {
                return false;
            }
            return true;
        }

        private static void Pawn_InventoryTracker_PostFix(Pawn_InventoryTracker __instance, Thing item)
        {
            CompHauledToInventory takenToInventory = __instance.pawn.TryGetComp<CompHauledToInventory>();
            if (takenToInventory == null)
                return;

            HashSet<Thing> carriedThing = takenToInventory.GetHashSet();
            if (carriedThing?.Count != 0)
            {
                if (carriedThing.Contains(item))
                {
                    carriedThing.Remove(item);
                }
            }
        }


        private static void JobDriver_HaulToCell_PostFix(JobDriver_HaulToCell __instance)
        {
            CompHauledToInventory takenToInventory = __instance.pawn.TryGetComp<CompHauledToInventory>();
            if (takenToInventory == null)
                return;

            HashSet<Thing> carriedThing = takenToInventory.GetHashSet();

            if (__instance.job.haulMode == HaulMode.ToCellStorage
                && __instance.pawn.Faction == Faction.OfPlayer
                && __instance.pawn.RaceProps.Humanlike
                && __instance.pawn.carryTracker.CarriedThing is Corpse == false
                && carriedThing != null
                && carriedThing.Count != 0) //deliberate hauling job. Should unload.
            {
                PawnUnloadChecker.CheckIfPawnShouldUnloadInventory(__instance.pawn, true);
            }
        }

        public static void IdleJoy_Postfix(Pawn pawn)
        {
            PawnUnloadChecker.CheckIfPawnShouldUnloadInventory(pawn, true);
        }

        public static void DropUnusedInventory_PostFix(Pawn pawn)
        {
            PawnUnloadChecker.CheckIfPawnShouldUnloadInventory(pawn);
        }

        public static IEnumerable<CodeInstruction> FloatMenuMakerMad_AddHumanlikeOrders_Transpiler(IEnumerable<CodeInstruction> instructions)
        {

            MethodInfo playerHome = AccessTools.Property(typeof(Map), nameof(Map.IsPlayerHome)).GetGetMethod();
            List<CodeInstruction> instructionList = instructions.ToList();

            bool patched = false;

            foreach (CodeInstruction instruction in instructionList)
            {
                if (!patched && instruction.operand == playerHome && !ModCompatibilityCheck.CombatExtendedIsActive) 
                {
                    instruction.opcode  = OpCodes.Ldc_I4_0;
                    instruction.operand = null;
                    yield return instruction;
                    patched = true;
                }
                yield return instruction;
            }
        }

        //Thanks to AlexTD
        //Job WhileYoureUp.Utils.MaybeHaulOtherStuffFirst(Pawn pawn, LocalTargetInfo end)
        public static void WhileYoureUpMaybeHaulOtherStuffFirst_PostFix(Pawn pawn, LocalTargetInfo end, ref Job __result)
        {
            if (__result == null || __result.def != JobDefOf.HaulToCell)
                return;

            if (!(pawn.workSettings.WorkGiversInOrderNormal.FirstOrDefault(wg => wg is WorkGiver_HaulToInventory) is WorkGiver_HaulToInventory worker)) return;

            Job myJob = worker.JobOnThing(pawn, __result.targetA.Thing);
            if (myJob == null)
                return;

            __result = myJob;
        }
    }
}