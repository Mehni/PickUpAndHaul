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

namespace PickUpThatCan
{
    [StaticConstructorOnStartup]
    static class HarmonyPatches
    {

        static HarmonyPatches()
        {
            HarmonyInstance harmony = HarmonyInstance.Create("mehni.rimworld.pickupthatcan.main");

            harmony.Patch(AccessTools.Method(typeof(FloatMenuMakerMap), "AddHumanlikeOrders"), null, null,
                new HarmonyMethod(typeof(HarmonyPatches), nameof(FloatMenuMakerMad_AddHumanlikeOrders_Transpiler)));
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
                if (!patched && instruction.operand == playerHome)       // if (instructionList[i + 3].opcode == OpCodes.Callvirt && instruction.operand == playerHome)
                {
                    {
                        instruction.opcode = OpCodes.Ldc_I4_0;
                        instruction.operand = null;
                        yield return instruction;
                        patched = true;
                    }
                    //if (instructionList[i + 3].operand == playerHome)
                    //{
                    //    Log.Message(instructionList[i + 5].opcode.ToString());
                    //    Log.Message(instructionList[i + 5].labels.ToString());
                    //    //{ instructionList[i + 5].labels = instruction.labels;}
                    //    Log.Message(instruction.opcode.ToString());
                    //    //Log.Message(instruction.operand.ToString());
                    //    Log.Message(instruction.labels.ToString());
                    //    instructionList.RemoveRange(i, 5);
                    //    patched = true;
                    //}
                }
                yield return instruction;
            }
        }
    }
}