using System;
using System.Collections.Generic;
using System.Linq;

namespace YuRis_Tool
{
    class ExprInstructionSet
    {
        int _scriptId = -1;
        public List<Instruction> _insts = new List<Instruction>();
        public Instruction _inst;

        public ExprInstructionSet(int scriptId)
        {
            _scriptId = scriptId;
        }

        public ExprInstructionSet(int scriptId, Instruction instruction)
        {
            _scriptId = scriptId;
            _inst = instruction;
        }

        public virtual void GetInstructions(Span<byte> data, bool rawExpr = false)
        {
            int offset = 0;

            while (offset < data.Length)
            {
                _insts.Add(Instruction.GetInstruction(_scriptId, data, ref offset));
            }
            Evaluate();
        }

        public void Evaluate()
        {
            var stack = new Stack<Instruction>();

            // Local helpers for diagnostics (kept inside method to avoid changing class surface)
            InvalidOperationException BuildEvalException(string reason, int index, Instruction current, Exception inner = null)
            {
                var exprCtx = this is AssignExprInstSet aes
                    ? $"Expr: Keyword='{aes.exprInfo?.Keyword}', LoadOp='{aes.LoadOp}', ResultType='{aes.exprInfo?.ResultType}'"
                    : "Expr: <no-assign-context>";

                string DumpInsts()
                {
                    try
                    {
                        var lines = new List<string>(_insts.Count);
                        for (int i = 0; i < _insts.Count; i++)
                        {
                            var inst = _insts[i];
                            string type = inst?.GetType().Name ?? "<null>";
                            string desc = inst?.ToString() ?? "<null>";
                            lines.Add($"[{i}] {type}: {desc}");
                        }
                        return string.Join(Environment.NewLine, lines);
                    }
                    catch { return "<failed to dump instructions>"; }
                }

                string DumpStack()
                {
                    try
                    {
                        if (stack.Count == 0) return "<empty>";
                        return string.Join(" | ", stack.Select(s => $"{s?.GetType().Name}:{s}"));
                    }
                    catch { return "<failed to dump stack>"; }
                }

                var currentInfo = current == null ? "<none>" : $"{current.GetType().Name}:{current}";
                var msg = $@"EVAL_DIAGNOSTIC
Reason: {reason}
At instruction index: {index}
Current instruction: {currentInfo}
ScriptId: {_scriptId}
{exprCtx}
StackCount: {stack.Count}
Stack Snapshot: {DumpStack()}
Instruction List:
{DumpInsts()}";

                return new InvalidOperationException(msg, inner);
            }

            try
            {
                for (int idx = 0; idx < _insts.Count; idx++)
                {
                    var t = _insts[idx];
                    switch (t)
                    {
                        case ArithmeticOperator ao:
                            {
                                if (stack.Count < 2)
                                {
                                    if (this is AssignExprInstSet aes && stack.Count == 1)
                                    {
                                        ao.Right = stack.Pop();
                                        ao.Left = new KeywordRef(aes.exprInfo?.Keyword);
                                        stack.Push(ao);
                                        break;
                                    }
                                    throw BuildEvalException("Arithmetic operator requires 2 operands, stack underflow.", idx, t);
                                }
                                ao.Right = stack.Pop();
                                ao.Left = stack.Pop();
                                stack.Push(ao);
                                break;
                            }
                        case RelationalOperator ro:
                            {
                                if (stack.Count < 2)
                                {
                                    if (this is AssignExprInstSet aes && stack.Count == 1)
                                    {
                                        ro.Right = stack.Pop();
                                        ro.Left = new KeywordRef(aes.exprInfo?.Keyword);
                                        stack.Push(ro);
                                        break;
                                    }
                                    throw BuildEvalException("Relational operator requires 2 operands, stack underflow.", idx, t);
                                }
                                ro.Right = stack.Pop();
                                ro.Left = stack.Pop();
                                stack.Push(ro);
                                break;
                            }
                        case LogicalOperator lo:
                            {
                                if (stack.Count < 2)
                                {
                                    if (this is AssignExprInstSet aes && stack.Count == 1)
                                    {
                                        lo.Right = stack.Pop();
                                        lo.Left = new KeywordRef(aes.exprInfo?.Keyword);
                                        stack.Push(lo);
                                        break;
                                    }
                                    throw BuildEvalException("Logical/bitwise operator requires 2 operands, stack underflow.", idx, t);
                                }
                                lo.Right = stack.Pop();
                                lo.Left = stack.Pop();
                                stack.Push(lo);
                                break;
                            }
                        case UnaryOperator unaryOp:
                            {
                                if (unaryOp.Operator == UnaryOperator.Type.Negate)
                                {
                                    if (stack.Count < 1)
                                        throw BuildEvalException("Negate operator requires 1 operand, stack underflow.", idx, t);
                                    switch (stack.Peek())
                                    {
                                        case ByteLiteral bl:
                                            {
                                                bl.Value = Convert.ToSByte(-bl.Value);
                                                break;
                                            }
                                        case ShortLiteral sl:
                                            {
                                                sl.Value = Convert.ToInt16(-sl.Value);
                                                break;
                                            }
                                        case IntLiteral il:
                                            {
                                                il.Value = -il.Value;
                                                break;
                                            }
                                        case LongLiteral ll:
                                            {
                                                ll.Value = -ll.Value;
                                                break;
                                            }
                                        case DecimalLiteral dl:
                                            {
                                                dl.Value = -dl.Value;
                                                break;
                                            }
                                        case ArrayAccess aai:
                                            {
                                                aai.Negate ^= true;
                                                break;
                                            }
                                        case VariableAccess vai:
                                            {
                                                vai.Negate ^= true;
                                                break;
                                            }
                                        case ArithmeticOperator aoi:
                                            {
                                                aoi.Negate ^= true;
                                                break;
                                            }
                                        default:
                                            {
                                                throw BuildEvalException($"Selected object ({stack.Peek()}) does not support negate operator!", idx, t);
                                            }
                                    }
                                }
                                else
                                {
                                    if (stack.Count < 1)
                                        throw BuildEvalException("Unary operator requires 1 operand, stack underflow.", idx, t);
                                    unaryOp.Operand = stack.Pop();
                                    stack.Push(unaryOp);
                                }
                                break;
                            }
                        case ArrayAccess aa:
                            {
                                if (stack.Count < 1)
                                    throw BuildEvalException("ArrayAccess expects indices and a VariableRef base, but stack is empty.", idx, t);
                                var indices = new List<Instruction>();
                                var top = stack.Pop();
                                while (top is not VariableRef)
                                {
                                    indices.Add(top);
                                    if (stack.Count == 0)
                                        throw BuildEvalException("ArrayAccess missing VariableRef before indices are exhausted (unterminated indices).", idx, t);
                                    top = stack.Pop();
                                }
                                indices.Reverse();
                                stack.Push(new ArrayAccess((VariableRef)top, indices.ToArray()));
                                break;
                            }
                        case Nop:
                            continue;
                        default:
                            {
                                stack.Push(t);
                                break;
                            }
                    }
                }

                if (stack.Count != 1)
                {
                    throw BuildEvalException($"Expression did not resolve to a single value (StackCount={stack.Count}).", _insts.Count - 1, null);
                }

                _inst = stack.Single();
            }
            catch (InvalidOperationException)
            {
                // Already enriched by BuildEvalException; just rethrow.
                throw;
            }
            catch (Exception ex)
            {
                // Wrap unexpected exceptions with diagnostic context
                throw BuildEvalException("Unhandled exception during Evaluate().", -1, null, ex);
            }
        }

        public override string ToString()
        {
            return $"{_inst}";
        }
    }

    class AssignExprInstSet : ExprInstructionSet
    {
        public YSCM.ExpressionInfo exprInfo;
        public string LoadOp;
        public AssignExprInstSet(int scriptId, YSCM.ExpressionInfo info, string loadOp = "=") : base(scriptId)
        {
            exprInfo = info;
            LoadOp = loadOp;
        }

        public override void GetInstructions(Span<byte> data, bool stringExpr)
        {
            if (stringExpr)
            {
                _insts.Add(new RawStringLiteral(Extensions.DefaultEncoding.GetString(data)));//FIXME
                Evaluate();
                return;
            }
            base.GetInstructions(data);
        }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(exprInfo.Keyword))
            {
                return $"{_inst}";
            }

            string br = "";
            if (_inst is VariableAccess va && va._varInfo.Dimensions.Length > 0)
            {
                br = "()";
            }
            else if (_inst is VariableRef vr && vr._varInfo.Dimensions.Length > 0)
            {
                br = "()";
            }

            return $"{exprInfo.Keyword}{LoadOp}{_inst}{br}";
        }
    }
}
