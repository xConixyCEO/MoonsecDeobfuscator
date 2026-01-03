using System.Text;
using MoonsecDeobfuscator.Bytecode.Models;

namespace MoonsecDeobfuscator.Deobfuscation.Bytecode;

public class Disassembler
{
    private readonly Function _rootFunction;
    private readonly StringBuilder _builder = new();
    private int _indentLevel = 0;
    private int _currentLine = 0;
    private List<Instruction>? _currentInstructions;

    public Disassembler(Function rootFunction)
    {
        _rootFunction = rootFunction;
    }

    public string Disassemble()
    {
        _builder.AppendLine("-- Decompiled with MoonSec V3 deobfuscator by #tupsutumppu");
        _builder.AppendLine("-- Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        _builder.AppendLine();
        
        DisassembleFunction(_rootFunction, true);
        return _builder.ToString();
    }

    private void DisassembleFunction(Function function, bool isRoot = false)
    {
        var localVarNames = new Dictionary<int, string>();
        var instructions = function.Instructions;
        _currentInstructions = instructions;
        
        // Process instructions to generate Lua code
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            _currentLine = i;
            
            // Skip dead instructions
            if (instruction.IsDead) continue;
            
            // Generate code based on opcode
            var codeLine = GenerateLuaCode(instruction, function, localVarNames);
            
            if (!string.IsNullOrEmpty(codeLine))
            {
                // Add indentation
                _builder.Append(new string(' ', _indentLevel * 4));
                _builder.AppendLine(codeLine);
            }
        }
    }

    private string GenerateLuaCode(Instruction instruction, Function function, Dictionary<int, string> localVarNames)
    {
        var A = instruction.A;
        var B = instruction.B;
        var C = instruction.C;

        switch (instruction.OpCode)
        {
            case OpCode.GetGlobal:
                if (B < function.Constants.Count && function.Constants[B] is StringConstant strConst)
                {
                    var globalName = strConst.Value;
                    // Check if this is a common Roblox API call pattern
                    if (globalName == "_G")
                    {
                        return "";
                    }
                    else if (globalName == "game" || globalName == "workspace" || globalName == "script")
                    {
                        return $"local R{A} = {globalName}";
                    }
                    else
                    {
                        return $"local R{A} = {globalName}";
                    }
                }
                break;

            case OpCode.LoadK:
                if (B < function.Constants.Count)
                {
                    var constant = function.Constants[B];
                    var constStr = FormatConstant(constant);
                    return $"local R{A} = {constStr}";
                }
                break;

            case OpCode.SetTable:
                if (B >= 256 && C >= 256)
                {
                    var key = FormatConstant(function.Constants[B - 256]);
                    var value = FormatConstant(function.Constants[C - 256]);
                    return $"R{instruction.A}[{key}] = {value}";
                }
                break;

            case OpCode.Move:
                // Local variable assignment
                var sourceName = GetRegisterName(B, localVarNames);
                localVarNames[A] = sourceName;
                return $"local R{A} = {sourceName}";

            case OpCode.Call:
                // Handle function calls
                if (_currentInstructions != null && C == 2 && instruction.A == 0 && 
                    _currentLine > 0 && _currentInstructions[_currentLine - 1]?.OpCode == OpCode.Self)
                {
                    // Pattern: GetService call
                    if (_currentLine > 1 && _currentInstructions[_currentLine - 2]?.OpCode == OpCode.LoadK)
                    {
                        var serviceConstant = function.Constants[_currentInstructions[_currentLine - 2].B];
                        var serviceName = FormatConstant(serviceConstant);
                        return $"local R{A} = game:GetService({serviceName})";
                    }
                }
                else if (A == 9 && C == 2 && _currentInstructions != null && 
                         _currentLine > 0 && _currentInstructions[_currentLine - 1]?.OpCode == OpCode.LoadBool)
                {
                    // Pattern: HttpGet with boolean
                    return $"local R{A} = game:HttpGet(R{instruction.A + 1}, R{instruction.A + 2}, true)";
                }
                break;

            case OpCode.Test:
                // Conditional jump - start of if statement
                if (_currentInstructions != null && C == 1 && 
                    _currentLine + 1 < _currentInstructions.Count && 
                    _currentInstructions[_currentLine + 1]?.OpCode == OpCode.Jmp)
                {
                    var condition = C == 0 ? $"not R{A}" : $"R{A}";
                    
                    _indentLevel++;
                    _builder.Append(new string(' ', (_indentLevel - 1) * 4));
                    _builder.AppendLine($"if {condition} then");
                    return "";
                }
                break;

            case OpCode.Jmp:
                // Handle jumps
                if (B > 0)
                {
                    // Forward jump (skip block)
                    return "";
                }
                else if (B < 0)
                {
                    // Backward jump (loop end)
                    _indentLevel--;
                    _builder.Append(new string(' ', _indentLevel * 4));
                    _builder.AppendLine("end");
                    return "";
                }
                break;

            case OpCode.Return:
                if (B == 1)
                {
                    return "return";
                }
                else if (B > 1)
                {
                    var returns = string.Join(", ", Enumerable.Range(0, B - 1).Select(i => $"R{A + i}"));
                    return $"return {returns}";
                }
                break;

            case OpCode.NewTable:
                return $"local R{A} = {{}}";

            case OpCode.GetTable:
                var table = GetRegisterName(B, localVarNames);
                var index = C >= 256 ? FormatConstant(function.Constants[C - 256]) : $"R{C}";
                return $"local R{A} = {table}[{index}]";

            case OpCode.Eq:
                if (_currentInstructions != null && 
                    _currentLine + 1 < _currentInstructions.Count && 
                    _currentInstructions[_currentLine + 1]?.OpCode == OpCode.Jmp)
                {
                    var left = B >= 256 ? FormatConstant(function.Constants[B - 256]) : $"R{B}";
                    var right = C >= 256 ? FormatConstant(function.Constants[C - 256]) : $"R{C}";
                    var op = A == 0 ? "==" : "~=";
                    return $"if {left} {op} {right} then";
                }
                break;

            case OpCode.ForPrep:
                // Start of numeric for loop
                _indentLevel++;
                _builder.Append(new string(' ', (_indentLevel - 1) * 4));
                _builder.AppendLine($"for R{A} = R{A}, R{A + 1}, R{A + 2} do");
                return "";

            case OpCode.ForLoop:
                // Loop body continues
                return "";

            case OpCode.TForLoop:
                // Generic for loop (pairs/ipairs)
                var iterator = $"R{A}";
                var state = $"R{A + 1}";
                var control = $"R{A + 2}";
                return $"{string.Join(", ", Enumerable.Range(3, C).Select(i => $"R{A + i}"))} = {iterator}({state}, {control})";

            case OpCode.Closure:
                if (B < function.Functions.Count)
                {
                    return $"local R{A} = function() -- function_{function.Functions[B].Name}";
                }
                break;

            case OpCode.LoadBool:
                var value = B != 0 ? "true" : "false";
                return $"local R{A} = {value}";

            case OpCode.LoadNil:
                if (B - A == 0)
                {
                    return $"local R{A} = nil";
                }
                break;

            case OpCode.SetGlobal:
                if (B < function.Constants.Count && function.Constants[B] is StringConstant strConst2)
                {
                    return $"{strConst2.Value} = R{A}";
                }
                break;
        }

        return "";
    }

    private string GetRegisterName(int register, Dictionary<int, string> localVarNames)
    {
        if (localVarNames.ContainsKey(register))
        {
            return localVarNames[register];
        }
        
        // Generate meaningful names based on usage
        return $"R{register}";
    }

    private string FormatConstant(Constant constant)
    {
        if (constant is StringConstant str)
        {
            return $"\"{str.Value}\"";
        }
        else if (constant is NumberConstant num)
        {
            return num.Value.ToString();
        }
        else if (constant is BoolConstant boolConst)
        {
            return boolConst.Value ? "true" : "false";
        }
        else if (constant is NilConstant)
        {
            return "nil";
        }
        
        return constant.ToString();
    }

    private string GetIndent()
    {
        return new string(' ', _indentLevel * 4);
    }
}
