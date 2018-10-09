using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using RimWorld;
using Verse;
using Harmony;

namespace PickUpAndHaul
{
    [HarmonyPatch(typeof(StoreUtility), "TryFindBestBetterStoreCellForWorker")]
    public static class StoreUtilityCellSkipper
    {
        public static HashSet<IntVec3> skipCells = null;
        //private static void TryFindBestBetterStoreCellForWorker(Thing t, Pawn carrier, Map map, Faction faction, SlotGroup slotGroup, bool needAccurateResult, ref IntVec3 closestSlot, ref float closestDistSquared, ref StoragePriority foundPriority)
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iLGenerator)
        {
            MethodInfo ListItemInfo = AccessTools.Property(typeof(List<IntVec3>), "Item").GetGetMethod();

            MethodInfo SkipItInfo = AccessTools.Method(typeof(StoreUtilityCellSkipper), nameof(StoreUtilityCellSkipper.SkipIt));

            List<CodeInstruction> instList = instructions.ToList();

            Label continueLabel = iLGenerator.DefineLabel();
            for (int i = 0; i < instList.Count; i++)
            {
                if (instList[i].opcode == OpCodes.Add && instList[i - 1].opcode == OpCodes.Ldc_I4_1 // '++'
                    && instList[i-2].opcode == OpCodes.Ldloc_S)
                {
                    instList[i - 2].labels.Add(continueLabel);//label at i++ to continue loop
                    break;
                }
            }

            for (int i = 0; i < instList.Count; i++)
            {
                yield return instList[i];
                if(instList[i].opcode == OpCodes.Callvirt && instList[i].operand == ListItemInfo)   //cells[i]
                {
                    i++;
                    yield return instList[i];//stloc for cells[i]
                    object localCellIndex = instList[i].operand;//local var index for IntVec3 cell;

                    yield return new CodeInstruction(OpCodes.Ldloc_S, localCellIndex);//cell
                    yield return new CodeInstruction(OpCodes.Call, SkipItInfo);//SkipIt(cell)
                    yield return new CodeInstruction(OpCodes.Brtrue, continueLabel);//if(SkipIt(cell))  continue;
                }
            }
        }

        public static bool SkipIt(IntVec3 cell)
        {
            return skipCells?.Contains(cell) ?? false;
        }
    }
}
