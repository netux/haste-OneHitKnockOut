using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.Text;

namespace HasteOHKOMod;

internal static class Util
{
    public static void DebugLogInstructions(IEnumerable<Instruction> instrs)
    {
        foreach (var instruction in instrs)
            UnityEngine.Debug.Log($"\t{InstructionToString(instruction)}");

        static string InstructionToString(Instruction instruction) => $"{instruction.Offset:X4} {instruction.OpCode} {InstructionOperandToString(instruction.Operand)}";

        static string InstructionOperandToString(object operand)
        {
            if (operand is ILLabel label)
            {
                return $"(label→ {InstructionToString(label.Target)})";
            }
            else
            {
                return operand?.ToString() ?? "null";
            }
        }
    }
}
