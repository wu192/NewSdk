// APT instruction/table layouts derived from SWF2APT (GPL-3.0).
// The input is compiler operations; no encoded AVM1/SWF actions are created/read.
using System;
using System.Collections.Generic;
using System.IO;
using BinaryAssetBuilder.UiCompiler.Compilation;

namespace BinaryAssetBuilder.UiCompiler.Runtime
{
    internal sealed class ActionEncoder
    {
        internal sealed class Operation
        {
            internal Instruction Source;
            internal ActionLabel FunctionEnd;
            internal int Constant = -1;
        }
        internal sealed class Site
        {
            internal string Key;
            internal readonly List<Operation> Operations = new List<Operation>();
            internal readonly Dictionary<ActionLabel, int> Positions = new Dictionary<ActionLabel, int>();
            internal readonly Dictionary<int, int> BranchOrigins = new Dictionary<int, int>();
        }
        private readonly List<object> constants = new List<object>();
        internal Site Plan(ActionProgram program, string key)
        {
            var site = new Site { Key = key };
            void Append(ActionProgram body)
            {
                foreach (Instruction instruction in body.Instructions)
                {
                    var op = new Operation { Source = instruction };
                    if (instruction.Operation == 0x96) { op.Constant = constants.Count; constants.Add(instruction.Operand); }
                    site.Operations.Add(op);
                    if (instruction.Operand is FunctionDefinition function)
                    { op.FunctionEnd = new ActionLabel(); Append(function.Body); site.Operations.Add(new Operation { Source = new Instruction(-1, op.FunctionEnd) }); }
                }
            }
            Append(program); return site;
        }
        internal void Write(RuntimeOutput output, int pass, Site site)
        {
            string key = site.Key;
            output.Pointer(pass, key); output.Align(pass + 1); output.Resolve(pass + 1, key);
            for (int i = 0; i < site.Operations.Count; i++)
            {
                var operation = site.Operations[i]; var instruction = operation.Source; string item = key + "/" + i;
                if (instruction.Operation == -1)
                { if (output.Pass == pass + 1) site.Positions[(ActionLabel)instruction.Operand] = output.Size; continue; }
                output.Write(pass + 1, (byte)instruction.Operation);
                switch (instruction.Operation)
                {
                    case 0x96:
                        output.Align(pass + 1); output.Write(pass + 1, 1); output.Pointer(pass + 1, item);
                        output.Align(pass + 2); output.Resolve(pass + 2, item); output.Write(pass + 2, operation.Constant); break;
                    case 0x99: case 0x9d:
                        output.Align(pass + 1); output.Pointer(pass + 1, item);
                        if (output.Pass == pass + 1) site.BranchOrigins[i] = output.Size;
                        if (output.Pass == pass + 2)
                        { if (!site.Positions.TryGetValue((ActionLabel)instruction.Operand, out int target)) throw new InvalidDataException("Unbound action label."); output.Patch(pass + 2, item, target - site.BranchOrigins[i]); }
                        break;
                    case 0x87: output.Align(pass + 1); output.Write(pass + 1, (int)instruction.Operand); break;
                    case 0x9f: output.Align(pass + 1); output.Write(pass + 1, (bool)instruction.Operand); break;
                    case 0x9b:
                        var function = (FunctionDefinition)instruction.Operand;
                        output.Align(pass + 1); output.Pointer(pass + 1, item + "/name"); output.Write(pass + 1, function.Parameters.Count); output.Pointer(pass + 1, item + "/args");
                        output.Align(pass + 2); output.Resolve(pass + 2, item + "/name"); output.Write(pass + 2, function.Name);
                        output.Align(pass + 2); output.Resolve(pass + 2, item + "/args");
                        for (int p = 0; p < function.Parameters.Count; p++)
                        { output.Pointer(pass + 2, item + "/arg/" + p); output.Align(pass + 3); output.Resolve(pass + 3, item + "/arg/" + p); output.Write(pass + 3, function.Parameters[p]); }
                        output.Pointer(pass + 1, item + "/end"); output.Align(pass + 1); output.Write(pass + 1, 0x98765432u); output.Write(pass + 1, 0x12345678u);
                        if (output.Pass == pass + 1) site.BranchOrigins[i] = output.Size;
                        if (output.Pass == pass + 2) output.Patch(pass + 2, item + "/end", site.Positions[operation.FunctionEnd] - site.BranchOrigins[i]);
                        break;
                }
            }
        }
        internal void WriteConstants(RuntimeOutput output, int mainCharacter)
        {
            output.Begin("Apt constant file\u001a\0");
            for (output.Pass = 1; output.Pass <= 3; output.Pass++)
            {
                output.Write(1, mainCharacter); output.Write(1, constants.Count); output.Pointer(1, "constants"); output.Align(2); output.Resolve(2, "constants");
                for (int i = 0; i < constants.Count; i++)
                {
                    object value = constants[i]; output.Align(2);
                    if (value is string text)
                    { output.Write(2, 1); output.Pointer(2, "s/" + i); output.Align(3); output.Resolve(3, "s/" + i); output.Write(3, text); }
                    else if (value is RegisterValue register) { output.Write(2, 4); output.Write(2, register.Index); }
                    else if (value is bool boolean) { output.Write(2, 5); output.Write(2, boolean ? 1 : 0); }
                    else if (value is int integer) { output.Write(2, 7); output.Write(2, integer); }
                    else if (value == null || value is UndefinedValue) { output.Write(2, 3); output.Write(2, 0); }
                    else { output.Write(2, 6); output.Write(2, Convert.ToSingle(value)); }
                }
            }
            output.Complete();
        }
    }
}
