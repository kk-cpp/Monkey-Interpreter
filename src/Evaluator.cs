﻿internal class Evaluator
{
    public IMonkeyObject? Eval(INode? node, MonkeyEnvironment env)
    {
        if (node == null) return null;

        switch (node)
        {
            // statements
            case AstProgram n:
                return EvalProgram(n.Statements, env);
            case BlockStatement n:
                var block = EvalBlockStatement(n, env);
                return block;
            case ExpressionStatement n:
                return Eval(n.Expression, env);
            case ReturnStatement n:
                var ret = Eval(n.ReturnValue, env);
                if (IsError(ret)) return ret;
                return new MonkeyReturn(ret);
            case LetStatement n:
                var letRet = Eval(n.Value, env);
                if (IsError(letRet)) return letRet;
                if (letRet != null) env.Set(n.Name?.TokenLiteral(), letRet);
                break;

            // expression
            case IntegerLiteral n:
                return new MonkeyInteger(n.Value);
            case BooleanExpression n:
                return NativeBoolToBooleanObject(n.Value);
            case PrefixExpression n:
                var prefixExpRight = Eval(n.Right, env);
                if (IsError(prefixExpRight)) return prefixExpRight;
                return EvalPrefixExpression(n.TokenLiteral(), prefixExpRight);
            case InfixExpression n:
                var left = Eval(n.Left, env);
                if (IsError(left)) return left;
                var right = Eval(n.Right, env);
                if (IsError(right)) return right;
                return EvalInfixExpression(n.TokenLiteral(), left, right);
            case IfExpression n:
                return EvalIfExpression(n, env);
            case Identifier n:
                return EvalIdentifier(n, env);
            case FunctionLiteral n:
                var param = n.Parameter;
                var body = n.Body;
                return new MonkeyFunction() { Body = body, Parameter = param, Env = env };
            case CallExpression callExpression:
                if (callExpression.Function?.TokenLiteral() == "quote")
                {
                    return Quote(callExpression.Arguments[0], env);
                }
                var func = Eval(callExpression.Function, env);
                if (IsError(func))
                    return func;
                var args = EvalExpressions(callExpression.Arguments, env);
                if (args.Count == 1 && IsError(args[0]))
                    return args[0];
                return ApplyFunction(func, args);
            case StringLiteral n:
                return new MonkeyString(n.Value);
            case ArrayLiteral arrayLiteral:
                var eles = EvalExpressions(arrayLiteral.Element, env);
                if (eles.Count == 1 && IsError(eles[0]))
                {
                    return eles[0];
                }
                return new MonkeyArray(eles);
            case IndexExpression indexExpression:
                left = Eval(indexExpression.Left, env);
                if (IsError(left))
                {
                    return left;
                }
                var index = Eval(indexExpression.Index, env);
                if (IsError(index))
                {
                    return index;
                }

                return EvalIndexExpression(left, index);

            case HashLiteral hashLiteral:
                return EvalHashLiteral(hashLiteral, env);
        }

        return null;
    }

    private IMonkeyObject NativeBoolToBooleanObject(bool value)
    {
        return new MonkeyBoolean(value);
    }

    public IMonkeyObject? EvalProgram(List<IStatement> statements, MonkeyEnvironment env)
    {
        IMonkeyObject? obj = null;
        foreach (var stmt in statements)
        {
            obj = Eval(stmt, env);
            switch (obj)
            {
                case MonkeyReturn monkeyReturn:
                    return monkeyReturn.Value;
                case MonkeyError monkeyError:
                    return monkeyError;
            }

        }
        return obj;
    }

    public IMonkeyObject? EvalBlockStatement(BlockStatement blockStatement, MonkeyEnvironment env)
    {
        IMonkeyObject? obj = null;
        foreach (var stmt in blockStatement.Statements)
        {
            obj = Eval(stmt, env);
            if (obj != null)
            {
                if (obj.GetMonkeyObjectType() == MonkeyObjectType.Return && obj.GetMonkeyObjectType() == MonkeyObjectType.Error)
                {
                    return obj;
                }
            }
        }

        return obj;
    }

    public IMonkeyObject EvalPrefixExpression(string op, IMonkeyObject? right)
    {
        if (right == null) return new MonkeyNull();

        switch (op)
        {
            case "!":
                return EvalBangOperatorExpression(right);
            case "-":
                return EvalMinusPrefiOperatorExpression(right);
            default:
                return NewError("unknown operator:", op, right.GetMonkeyObjectType().ToString());
        }
    }

    public IMonkeyObject EvalBangOperatorExpression(IMonkeyObject right)
    {
        switch (right)
        {
            case MonkeyBoolean boolean:
                return NativeBoolToBooleanObject(!boolean.Value);
            case null:
                return NativeBoolToBooleanObject(true);
            default:
                return NativeBoolToBooleanObject(false);
        }
    }

    public IMonkeyObject EvalMinusPrefiOperatorExpression(IMonkeyObject right)
    {
        if (right.GetMonkeyObjectType() != MonkeyObjectType.Integer)
        {
            return NewError("unknown operator:", right.GetMonkeyObjectType().ToString());
        }

        var r = right as MonkeyInteger;
        if (r == null)
        {
            return new MonkeyNull();
        }

        return new MonkeyInteger(-r.Value);
    }

    public IMonkeyObject EvalInfixExpression(string op, IMonkeyObject? left, IMonkeyObject? right)
    {
        if (left == null || right == null)
        {
            return NewError("MonkeyObject is null:");
        }

        if (left.GetMonkeyObjectType() != right.GetMonkeyObjectType())
        {
            return NewError("type mismatch:", left.GetMonkeyObjectType().ToString(), op, right.GetMonkeyObjectType().ToString());
        }

        if (left.GetMonkeyObjectType() == MonkeyObjectType.Integer && right.GetMonkeyObjectType() == MonkeyObjectType.Integer)
        {
            return EvalIntegerInfixExpression(op, left, right);
        }

        if (left.GetMonkeyObjectType() == MonkeyObjectType.String && right.GetMonkeyObjectType() == MonkeyObjectType.String)
        {
            return EvalStringInfixExpression(op, left, right);
        }

        switch (op)
        {
            case "==":
                return NativeBoolToBooleanObject(left.Inspect() == right.Inspect());
            case "!=":
                return NativeBoolToBooleanObject(left.Inspect() != right.Inspect());
            default:
                return new MonkeyNull();
        }
    }

    public IMonkeyObject EvalIntegerInfixExpression(string op, IMonkeyObject left, IMonkeyObject right)
    {
        var rv = right as MonkeyInteger;
        var lv = left as MonkeyInteger;

        if (lv == null || rv == null)
        {
            return NewError("MonkeyObject is not MonkeyInteger:", left.GetMonkeyObjectType().ToString(), right.GetMonkeyObjectType().ToString());
        }

        switch (op)
        {
            case "+":
                return new MonkeyInteger(lv.Value + rv.Value);
            case "-":
                return new MonkeyInteger(lv.Value - rv.Value);
            case "*":
                return new MonkeyInteger(lv.Value * rv.Value);
            case "/":
                return new MonkeyInteger(lv.Value / rv.Value);
            case "<":
                return NativeBoolToBooleanObject(lv.Value < rv.Value);
            case ">":
                return NativeBoolToBooleanObject(lv.Value > rv.Value);
            case "==":
                return NativeBoolToBooleanObject(lv.Value == rv.Value);
            case "!=":
                return NativeBoolToBooleanObject(lv.Value != rv.Value);
            default:
                return NewError("unknown operator:", left.GetMonkeyObjectType().ToString(), op, right.GetMonkeyObjectType().ToString());

        }
    }

    public IMonkeyObject? EvalIfExpression(IfExpression expression, MonkeyEnvironment env)
    {
        var condiion = Eval(expression.Condition, env);
        if (IsError(condiion))
        {
            return condiion;
        }
        if (IsTruthy(condiion))
        {
            return Eval(expression.Consequence, env);
        }
        else if (expression.Alternative != null)
        {
            return Eval(expression.Alternative, env);
        }
        else
        {
            return new MonkeyNull();
        }
    }

    public bool IsTruthy(IMonkeyObject? val)
    {
        switch (val)
        {
            case MonkeyNull monkeyNull:
                return false;
            case MonkeyBoolean monkeyBoolean:
                return monkeyBoolean.Value;
            default:
                return false;//默认不是应该false吗？书上是true
        }
    }

    public static MonkeyError NewError(params string[] msg)
    {
        return new MonkeyError(string.Join(" ", msg));
    }

    public bool IsError(IMonkeyObject? obj)
    {
        if (obj == null) return false;
        if (obj.GetMonkeyObjectType() != MonkeyObjectType.Error) return false;
        return true;
    }

    public IMonkeyObject? EvalIdentifier(Identifier node, MonkeyEnvironment env)
    {
        (IMonkeyObject? obj, bool ok) = env.Get(node.TokenLiteral());
        if (ok)
        {
            return obj;
        }

        if (Builtins.builtins.TryGetValue(node.Value ?? string.Empty, out MonkeyBuiltin? builtin))
        {
            return builtin;
        }

        return NewError("identifier not found: " + node.Value);
    }

    public List<IMonkeyObject?> EvalExpressions(List<IExpression?>? exps, MonkeyEnvironment environment)
    {
        var result = new List<IMonkeyObject?>();
        if (exps != null)
        {
            foreach (var e in exps)
            {
                var eval = Eval(e, environment);
                if (IsError(eval))
                {
                    return new List<IMonkeyObject?> { eval };
                }
                result.Add(eval);
            }
        }
        return result;
    }

    public IMonkeyObject? ApplyFunction(IMonkeyObject? func, List<IMonkeyObject?>? args)
    {

        switch (func)
        {
            case MonkeyFunction monkeyFunction:
                var extendedenv = ExtendFunctionEnv(monkeyFunction, args);
                var eval = Eval(monkeyFunction.Body, extendedenv);
                return UnwarpReturnValue(eval);
            case MonkeyBuiltin builtin:
                return builtin.Fn?.Invoke(args);
            default:
                return NewError($"not a function: {func?.GetMonkeyObjectType()}");
        }
    }

    public MonkeyEnvironment ExtendFunctionEnv(MonkeyFunction monkeyFunction, List<IMonkeyObject?>? args)
    {
        var env = MonkeyEnvironment.NewEnclosedEnvironment(monkeyFunction.Env);
        if (monkeyFunction.Parameter != null)
        {
            for (int i = 0; i < monkeyFunction.Parameter.Count; i++)
            {
                if (args != null && args.Count > i && args[i] != null)
                    env.Set(monkeyFunction.Parameter[i].Value, args[i]);
            }
        }
        return env;
    }

    public IMonkeyObject? UnwarpReturnValue(IMonkeyObject? obj)
    {
        if (obj is MonkeyReturn monkeyReturn)
        {
            return monkeyReturn.Value;
        }
        return obj;
    }

    public IMonkeyObject EvalStringInfixExpression(string op, IMonkeyObject left, IMonkeyObject right)
    {
        var rv = right as MonkeyString;
        var lv = left as MonkeyString;

        if (lv == null || rv == null)
        {
            return NewError("MonkeyObject is not MonkeyString:", left.GetMonkeyObjectType().ToString(), right.GetMonkeyObjectType().ToString());
        }

        if (op != "+")
        {
            return NewError("unknown operator:", op);
        }

        return new MonkeyString(lv.Value + rv.Value);
    }

    public IMonkeyObject? EvalIndexExpression(IMonkeyObject? Left, IMonkeyObject? index)
    {
        if (Left == null || index == null) return NewError("IndexExpression() arguments, Left or index is null");
        switch (Left)
        {
            case IMonkeyObject l when (l.GetMonkeyObjectType() == MonkeyObjectType.Array) && (index.GetMonkeyObjectType() == MonkeyObjectType.Integer):
                return EvalArrayIndexExpression(Left, index);
            case IMonkeyObject l when l.GetMonkeyObjectType() == MonkeyObjectType.Hash:
                return EvalHashIndexExpreesion(Left, index);
            default:
                return NewError("index op not supported", Left.GetMonkeyObjectType().ToString());
        }
    }

    public IMonkeyObject? EvalArrayIndexExpression(IMonkeyObject Left, IMonkeyObject index)
    {
        var array = Left as MonkeyArray;
        var idx = index as MonkeyInteger;

        if (array == null || idx == null) return new MonkeyNull();

        var max = array.Elements.Count - 1;
        if (idx.Value < 0 || idx.Value > max)
        {
            return new MonkeyNull();
        }
        return array.Elements[idx.Value];
    }

    public IMonkeyObject EvalHashLiteral(HashLiteral hashLiteral, MonkeyEnvironment env)
    {
        var pairs = new Dictionary<HashKey, HashPair>();
        foreach (var item in hashLiteral.Pairs)
        {
            var key = Eval(item.Key, env);
            if (key == null) continue;
            if (IsError(key))
            {
                return key;
            }
            var ok = key is IHashKey;
            if (!ok)
            {
                return NewError("unusable as hash key: ", key.GetMonkeyObjectType().ToString());
            }
            var val = Eval(item.Value, env);
            if (val == null) continue;
            if (IsError(val))
            {
                return val;
            }
            var hashkey = key as IHashKey;
            if (hashkey == null) continue;

            pairs.Add(hashkey.HashKey(), new HashPair() { Key = key, Value = val });
        }
        return new MonkeyHash() { Pairs = pairs };
    }

    public IMonkeyObject? EvalHashIndexExpreesion(IMonkeyObject Hash, IMonkeyObject Index)
    {
        var hashobject = Hash as MonkeyHash;
        if (hashobject == null) return null;
        var ok = Index is IHashKey;
        if (!ok)
        {
            return NewError("unusable as hash key: ", Index.GetMonkeyObjectType().ToString());
        }
        var key = (Index as IHashKey);
        if (key == null) return null;
        if (hashobject.Pairs.TryGetValue(key.HashKey(), out HashPair hashPair))
        {
            return hashPair.Value;
        }
        return null;
    }

    #region quote
    public IMonkeyObject Quote(INode? node, MonkeyEnvironment env)
    {
        node = EvalUnquoteCalls(node, env);
        if (node == null) return new MonkeyNull();
        return new MonkeyQuote(node);
    }

    public INode? EvalUnquoteCalls(INode? quoted, MonkeyEnvironment env)
    {
        AstModify.ModifierFunc modifier = (INode? node) =>
        {
            if (!IsUnquoteCall(node))
            {
                return node;
            }

            CallExpression? call = node as CallExpression;
            if (call == null) return node;

            if (call.Arguments.Count != 1)
            {
                return node;
            }

            var unquoted = Eval(call.Arguments[0], env);
            return ConvertObjectToAstNode(unquoted);
        };
        return AstModify.Modify(quoted, modifier);
    }

    public bool IsUnquoteCall(INode? node)
    {
        if (node == null) return false;
        CallExpression? callExpression = node as CallExpression;
        if (callExpression == null) return false;

        return callExpression.Function.TokenLiteral() == "unquote";
    }

    public INode? ConvertObjectToAstNode(IMonkeyObject? obj)
    {
        if (obj == null) return null;

        switch (obj)
        {
            case MonkeyInteger m:
                return new IntegerLiteral(new Token(TokenType.INT, m.Value.ToString()));
            case MonkeyBoolean m:
                var t = new Token(m.Value == true ? TokenType.TRUE : TokenType.FALSE, m.Value.ToString());
                return new BooleanExpression(t, m.Value);
            case MonkeyQuote m:
                return m.Node;
            default:
                return null;
        }
    }

    #endregion
    #region macro
    public void DefineMacros(AstProgram program, MonkeyEnvironment env)
    {
        List<int> definitions = new();

        for (int i = 0; i < program.Statements.Count; i++)
        {
            if (IsMacroDefinition(program.Statements[i]))
            {
                AddMacro(program.Statements[i], env);
                definitions.Add(i);
            }
        }

        definitions.Reverse();//要从后向前删除
        foreach (var definitionIndex in definitions)
        {
            program.Statements.RemoveAt(definitionIndex);
        }
    }

    public bool IsMacroDefinition(IStatement node)
    {
        var letStatement = node as LetStatement;
        if (letStatement == null) return false;
        if (letStatement.Value is not MacroLiteral) return false;
        return true;
    }

    private void AddMacro(IStatement stmt, MonkeyEnvironment env)
    {
        var letStatement = stmt as LetStatement;
        if (letStatement == null) return;
        var macroLiteral = letStatement.Value as MacroLiteral;
        if (macroLiteral == null) return;

        var macro = new MonkeyMacro(macroLiteral.Parameter, env, macroLiteral.Body);
        env.Set(letStatement.Name?.Value, macro);
    }

    public INode ExpandMacros(INode program, MonkeyEnvironment env)
    {
        AstModify.ModifierFunc modifier = (INode node) => {
            var callExpression = node as CallExpression;
            if (callExpression == null) return node;

            var macro = IsMacroCall(callExpression, env);
            if (macro == null) return node;

            var args = QuoteArgs(callExpression);
            var evalEnv = ExtendMacroEnv(macro, args);
            var evaluated = Eval(macro.Body, evalEnv);

            var quote = evaluated as MonkeyQuote;
            if (quote == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("we only support returning AST-nodes from macros");
                Console.ResetColor();
                return node;
            }

            return quote.Node;
        };

        return AstModify.Modify(program, modifier);
    }

    private MonkeyMacro? IsMacroCall(CallExpression exp, MonkeyEnvironment env) {
        var identifier = exp.Function as Identifier;
        if (identifier == null) return null;

        if (identifier.Value == null) return null;
        (IMonkeyObject? obj, bool ok) = env.Get(identifier.Value);
        if (!ok) return null;

        var macro = obj as MonkeyMacro;
        if (macro == null) return null;

        return macro;
    }

    private List<MonkeyQuote> QuoteArgs(CallExpression exp) {
        List<MonkeyQuote> args = new();

        foreach (var a in exp.Arguments) {
            var node = a as INode;
            if (node == null) continue;
            args.Add(new MonkeyQuote(node));
        }

        return args;
    }

    private MonkeyEnvironment ExtendMacroEnv(MonkeyMacro macro, List<MonkeyQuote> args) {
        var extended = MonkeyEnvironment.NewEnclosedEnvironment(macro.Env);
    
        for (int i = 0; i < macro.Parameters.Count; i++) {
            extended.Set(macro.Parameters[i].String(), args[i]);
        }

        return extended;
    }
    #endregion
}
