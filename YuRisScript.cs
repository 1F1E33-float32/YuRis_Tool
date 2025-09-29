using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YuRis_Tool
{
    class YuRisScript
    {
        string _dirPath;
        YSCM _yscm;
        YSLB _yslb;
        YSTL _ystl;
        byte[] _ybnKey;

        public void Init(string dirPath, byte[] ybnKey)
        {
            _dirPath = dirPath;

            _yscm = new YSCM();
            _yscm.Load(Path.Combine(dirPath, "ysc.ybn"));

            _yslb = new YSLB();
            _yslb.Load(Path.Combine(dirPath, "ysl.ybn"));

            _ystl = new YSTL();
            _ystl.Load(Path.Combine(dirPath, "yst_list.ybn"));

            YSVR.Load(Path.Combine(dirPath, "ysv.ybn"));

            _ybnKey = ybnKey;
        }

        public bool Decompile(int scriptIndex, TextWriter outputStream = null)
        {
            Console.Write($"Decompiling yst{scriptIndex:D5}.ybn ...");
            var ystb = new YSTB(_yscm, _yslb);
            if (!ystb.Load(Path.Combine(_dirPath, $"yst{scriptIndex:D5}.ybn"), scriptIndex, _ybnKey))
                return false;


            outputStream ??= Console.Out;

            var commands = ystb.Commands;
            var nestDepth = 0;

            for (var i = 0; i < commands.Count; i++)
            {
                var labels = _yslb.Find(scriptIndex, i);

                if (labels != null)
                {
                    foreach (var label in labels)
                        outputStream.WriteLine($"#={label.Name}");
                }

                var cmd = commands[i];
                switch (cmd.Id.ToString())
                {
                    case "IF":
                    case "LOOP":
                        {
                            outputStream.Write("".PadLeft(nestDepth * 4, ' '));
                            nestDepth++;
                            break;
                        }
                    case "ELSE":
                        {
                            outputStream.Write("".PadLeft((nestDepth - 1) * 4, ' '));
                            break;
                        }
                    case "IFEND":
                    case "LOOPEND":
                        {
                            nestDepth--;
                            outputStream.Write("".PadLeft(nestDepth * 4, ' '));
                            break;
                        }
                    default:
                        {
                            outputStream.Write("".PadLeft(nestDepth * 4, ' '));
                            break;
                        }
                }
                outputStream.WriteLine(cmd);
            }
            return true;
        }

        public void DecompileProject()
        {
            List<string> sourcePaths = new List<string>();

            foreach (var script in _ystl)
            {
                var sourcePath = Path.Combine(_dirPath, script.Source);
                Directory.CreateDirectory(Path.GetDirectoryName(sourcePath));
                sourcePaths.Add(sourcePath);

                using var textWriter = new StringWriter();
                if (Decompile(script.Id, textWriter))
                {
                    //File.WriteAllText(sourcePath, textWriter.ToString());
                    var data = textWriter.ToString();
                    if (data.StartsWith("END[]") && data.Length < 8)
                        File.WriteAllText(sourcePath, "//Empty file.");
                    else
                        File.WriteAllBytes(sourcePath, Extensions.DefaultEncoding.GetBytes(data[..^8]));
                    Console.Write($" -> {sourcePath}");
                }
                else
                {
                    Console.Write($" -> Failed. No such file.");
                }
                Console.WriteLine("");
            }

            string[] longestCommonPathComponents = sourcePaths
                .Select(path => path.Split(Path.DirectorySeparatorChar))
                .Transpose()
                .Select(parts => parts.Distinct(StringComparer.OrdinalIgnoreCase))
                .TakeWhile(distinct => distinct.Count() == 1)
                .Select(distinct => distinct.First())
                .Append("global.txt")
                .ToArray();

            using var globalVarWriter = new StringWriter();
            YSVR.WriteGlobalVarDecl(globalVarWriter);
            //File.WriteAllText(Path.Combine(longestCommonPathComponents), globalVarWriter.ToString());
            File.WriteAllBytes(Path.Combine(longestCommonPathComponents), Extensions.DefaultEncoding.GetBytes(globalVarWriter.ToString()));
        }

        // JSON output support
        public void DecompileProjectJson()
        {
            var scripts = new List<(int Id, string Source)>();

            foreach (var script in _ystl)
            {
                var sourcePath = Path.Combine(_dirPath, script.Source);
                Directory.CreateDirectory(Path.GetDirectoryName(sourcePath));
                scripts.Add((script.Id, sourcePath));

                var model = BuildScriptModel(script.Id, script.Source);
                if (model == null)
                {
                    Console.WriteLine($"Decompiling yst{script.Id:D5}.ybn ... -> Failed. No such file.");
                    continue;
                }

                var jsonPath = Path.ChangeExtension(sourcePath, ".json");
                var json = System.Text.Json.JsonSerializer.Serialize(model, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                File.WriteAllText(jsonPath, json, System.Text.Encoding.UTF8);
                Console.WriteLine($"Decompiling yst{script.Id:D5}.ybn ... -> {jsonPath}");
            }

            // Write globals as JSON next to inferred global.txt location
            string[] longestCommonPathComponents = scripts
                .Select(s => Path.Combine(_dirPath, s.Source).Split(Path.DirectorySeparatorChar))
                .Transpose()
                .Select(parts => parts.Distinct(StringComparer.OrdinalIgnoreCase))
                .TakeWhile(distinct => distinct.Count() == 1)
                .Select(distinct => distinct.First())
                .Append("global.json")
                .ToArray();

            var globalsJsonPath = Path.Combine(longestCommonPathComponents);
            var globalsModel = BuildGlobalsModel();
            var globalsJson = System.Text.Json.JsonSerializer.Serialize(globalsModel, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            File.WriteAllText(globalsJsonPath, globalsJson, System.Text.Encoding.UTF8);
        }

        object BuildScriptModel(int scriptIndex, string source)
        {
            var ystb = new YSTB(_yscm, _yslb);
            if (!ystb.Load(Path.Combine(_dirPath, $"yst{scriptIndex:D5}.ybn"), scriptIndex, _ybnKey))
                return null;

            var commands = ystb.Commands;
            var items = new List<object>(commands.Count);
            int nestDepth = 0;

            for (int i = 0; i < commands.Count; i++)
            {
                var cmd = commands[i];
                var idName = cmd.Id.ToString();
                var labels = _yslb.Find(scriptIndex, i) ?? new List<YSLB.Label>();

                int lineNestBefore;
                switch (idName)
                {
                    case "IF":
                    case "LOOP":
                        lineNestBefore = nestDepth;
                        nestDepth++;
                        break;
                    case "ELSE":
                        lineNestBefore = Math.Max(0, nestDepth - 1);
                        break;
                    case "IFEND":
                    case "LOOPEND":
                        nestDepth = Math.Max(0, nestDepth - 1);
                        lineNestBefore = nestDepth;
                        break;
                    default:
                        lineNestBefore = nestDepth;
                        break;
                }

                var exprs = new List<object>();
                if (cmd.Expressions != null)
                {
                    foreach (var e in cmd.Expressions)
                    {
                        exprs.Add(new
                        {
                            id = e.Id,
                            flag = e.Flag,
                            argLoadFn = e.ArgLoadFn,
                            argLoadOp = e.ArgLoadOp,
                            loadOp = e.GetLoadOp(),
                            instructionSize = e.InstructionSize,
                            instructionOffset = e.InstructionOffset,
                            text = e.ExprInsts?.ToString(),
                            ast = SerializeInstruction(e.ExprInsts?._inst)
                        });
                    }
                }

                items.Add(new
                {
                    index = i,
                    id = idName,
                    idNumeric = Convert.ToInt32(cmd.Id),
                    exprCount = cmd.ExprCount,
                    labelId = cmd.LabelId,
                    lineNumber = cmd.LineNumber,
                    nest = lineNestBefore,
                    labels = labels.Select(l => l.Name).ToArray(),
                    expressions = exprs
                });
            }

            return new
            {
                scriptId = scriptIndex,
                source,
                commands = items
            };
        }

        object BuildGlobalsModel()
        {
            var list = new List<object>();
            foreach (var variable in YSVR.EnumerateVariables())
            {
                list.Add(new
                {
                    scope = variable.Scope.ToString(),
                    scriptIndex = variable.ScriptIndex,
                    variableId = variable.VariableId,
                    type = variable.Type,
                    name = YSVR.GetDecompiledVarName(variable),
                    dimensions = variable.Dimensions ?? Array.Empty<uint>(),
                    value = SerializeInstruction(variable.Value as Instruction) ?? variable.Value
                });
            }
            return list;
        }

        object SerializeInstruction(Instruction inst)
        {
            if (inst == null) return null;

            switch (inst)
            {
                case ArithmeticOperator ao:
                    return new
                    {
                        kind = "Arithmetic",
                        op = ao.GetOperator(ao.Operator),
                        negate = ao.Negate,
                        left = SerializeInstruction(ao.Left),
                        right = SerializeInstruction(ao.Right)
                    };
                case RelationalOperator ro:
                    return new
                    {
                        kind = "Relational",
                        op = ro.GetOperator(ro.Operator),
                        left = SerializeInstruction(ro.Left),
                        right = SerializeInstruction(ro.Right)
                    };
                case LogicalOperator lo:
                    return new
                    {
                        kind = "Logical",
                        op = lo.GetOperator(lo.Operator),
                        left = SerializeInstruction(lo.Left),
                        right = SerializeInstruction(lo.Right)
                    };
                case UnaryOperator uo:
                    return new
                    {
                        kind = "Unary",
                        op = uo.Operator.ToString(),
                        operand = SerializeInstruction(uo.Operand)
                    };
                case VariableAccess va:
                    return new
                    {
                        kind = "VariableAccess",
                        mode = ((char)va.Mode).ToString(),
                        negate = va.Negate,
                        name = YSVR.GetDecompiledVarName(va._varInfo),
                        scope = va._varInfo.Scope.ToString(),
                        variableId = va._varInfo.VariableId,
                        scriptIndex = va._varInfo.ScriptIndex,
                        type = va._varInfo.Type,
                        dimensions = va._varInfo.Dimensions ?? Array.Empty<uint>()
                    };
                case VariableRef vr:
                    return new
                    {
                        kind = "VariableRef",
                        mode = ((char)vr.Mode).ToString(),
                        name = YSVR.GetDecompiledVarName(vr._varInfo),
                        scope = vr._varInfo.Scope.ToString(),
                        variableId = vr._varInfo.VariableId,
                        scriptIndex = vr._varInfo.ScriptIndex,
                        type = vr._varInfo.Type,
                        dimensions = vr._varInfo.Dimensions ?? Array.Empty<uint>()
                    };
                case ArrayAccess aa:
                    return new
                    {
                        kind = "ArrayAccess",
                        negate = aa.Negate,
                        variable = SerializeInstruction(aa.Variable),
                        indices = aa.Indices?.Select(SerializeInstruction).ToArray() ?? Array.Empty<object>()
                    };
                case KeywordRef kr:
                    return new
                    {
                        kind = "KeywordRef",
                        name = kr.Name
                    };
                case RawStringLiteral rsl:
                    return new
                    {
                        kind = "RawString",
                        value = rsl.Value
                    };
                case ByteLiteral bl:
                    return new { kind = "Literal", literalType = "byte", value = bl.Value };
                case ShortLiteral sl:
                    return new { kind = "Literal", literalType = "short", value = sl.Value };
                case IntLiteral il:
                    return new { kind = "Literal", literalType = "int", value = il.Value };
                case LongLiteral ll:
                    return new { kind = "Literal", literalType = "long", value = ll.Value };
                case DecimalLiteral dl:
                    return new { kind = "Literal", literalType = "double", value = dl.Value };
                case StringLiteral stl:
                    return new { kind = "Literal", literalType = "string", value = stl.Value };
                case Nop:
                    return new { kind = "Nop" };
                default:
                    return new { kind = inst.GetType().Name, text = inst.ToString() };
            }
        }
    }
}
