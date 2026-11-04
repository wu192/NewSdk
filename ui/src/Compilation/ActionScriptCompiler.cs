using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BinaryAssetBuilder.UiCompiler.Compilation
{
    /// <summary>AS1/AS2 source to structured operations and labels. No editor, shell or Java runtime.</summary>
    internal sealed class ActionScriptCompiler
    {
        private sealed class Token { internal string Text; internal object Value; internal int Line; }
        private sealed class Node
        {
            internal string Kind, Name; internal object Value; internal List<Node> Children = new List<Node>();
            internal Node(string kind, string name = null, object value = null, params Node[] children) { Kind = kind; Name = name; Value = value; Children.AddRange(children); }
        }
        private sealed class Code
        {
            internal readonly List<Instruction> Instructions = new List<Instruction>();
            internal ActionLabel Label() => new ActionLabel();
            internal void Mark(ActionLabel label) => Instructions.Add(new Instruction(-1, label));
            internal void Op(int opcode, object operand = null) => Instructions.Add(new Instruction(opcode, operand));
            internal void Jump(int opcode, ActionLabel label) => Op(opcode, label);
            internal void Push(object value) => Op(0x96, value);
            internal void Register(int register) => Push(new RegisterValue(register));
            internal void Store(int register) => Op(0x87, register);
            internal void Append(ActionProgram program) => Instructions.AddRange(program.Instructions);
            internal ActionProgram Finish(bool end = false) { if (end) Op(0); return new ActionProgram(Instructions); }
        }
        private readonly List<Token> tokens = new List<Token>(); private int cursor;
        private Code code = new Code(); private readonly Stack<Tuple<ActionLabel, ActionLabel>> loops = new Stack<Tuple<ActionLabel, ActionLabel>>();
        private readonly Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        private HashSet<string> locals = new HashSet<string>(StringComparer.Ordinal);
        private As2Class classContext;
        private Func<string, As2Class> classResolver;
        private bool staticInitialization;
        private ActionScriptCompiler(string source) { Lex(source); }
        internal static ActionProgram Compile(string source) { var c = new ActionScriptCompiler(source); c.Imports(null); while (!c.At("<eof>")) c.Statement(); return c.code.Finish(true); }
        private Token Current => tokens[Math.Min(cursor, tokens.Count - 1)];
        private bool At(string text) => Current.Text == text;
        private bool Eat(string text) { if (!At(text)) return false; cursor++; return true; }
        private void Need(string text) { if (!Eat(text)) throw Error("Expected '" + text + "', found '" + Current.Text + "'."); }
        private Exception Error(string text) => new InvalidDataException("AS2 line " + Current.Line + ": " + text);
        private string Identifier() { if (Current.Value != null || At("<eof>") || !(char.IsLetter(Current.Text[0]) || Current.Text[0] == '_' || Current.Text[0] == '$')) throw Error("Expected identifier."); return tokens[cursor++].Text; }
        private void Semicolon() { if (!Eat(";") && !At("}") && !At("<eof>") && (cursor == 0 || Current.Line == tokens[cursor - 1].Line)) throw Error("Expected ';'."); }
        private void TypeAnnotation() { if (Eat(":")) { Identifier(); while (Eat(".")) Identifier(); } }
        private string Qualified() { string name = Identifier(); while (Eat(".")) name += "." + (Eat("*") ? "*" : Identifier()); return name; }
        private void Imports(Func<string, As2Class> require)
        {
            while (Eat("import")) { string name = Qualified(); Semicolon(); if (name.EndsWith(".*", StringComparison.Ordinal)) throw Error("Use explicit AS2 class imports."); require?.Invoke(name); string leaf = name.Substring(name.LastIndexOf('.') + 1); if (aliases.ContainsKey(leaf) && aliases[leaf] != name) throw Error("Ambiguous class import " + leaf); aliases[leaf] = name; }
        }
        private void Statement()
        {
            if (Eat(";")) return;
            if (Eat("{")) { while (!Eat("}")) { if (At("<eof>")) throw Error("Unclosed block."); Statement(); } return; }
            if (Eat("var")) { Variables(); Semicolon(); return; }
            if (Eat("if")) { Need("("); var test = Expression(); Need(")"); ActionLabel no = code.Label(), end = code.Label(); Emit(test); code.Op(0x12); code.Jump(0x9d, no); Statement(); if (Eat("else")) { code.Jump(0x99, end); code.Mark(no); Statement(); code.Mark(end); } else code.Mark(no); return; }
            if (Eat("while"))
            { ActionLabel begin = code.Label(), end = code.Label(); code.Mark(begin); Need("("); Emit(Expression()); Need(")"); code.Op(0x12); code.Jump(0x9d, end); loops.Push(Tuple.Create(end, begin)); Statement(); loops.Pop(); code.Jump(0x99, begin); code.Mark(end); return; }
            if (Eat("do"))
            { ActionLabel begin = code.Label(), condition = code.Label(), end = code.Label(); code.Mark(begin); loops.Push(Tuple.Create(end, condition)); Statement(); loops.Pop(); Need("while"); Need("("); code.Mark(condition); Emit(Expression()); Need(")"); code.Jump(0x9d, begin); code.Mark(end); Semicolon(); return; }
            if (Eat("for")) { For(); return; }
            if (Eat("function")) { string name = Identifier(); EmitFunction(Function(name)); return; }
            if (Eat("return")) { if (At(";") || At("}") || At("<eof>") || Current.Line > tokens[cursor - 1].Line) code.Push(UndefinedValue.Instance); else Emit(Expression()); code.Op(0x3e); Semicolon(); return; }
            if (Eat("break") || Eat("continue")) { bool cont = tokens[cursor - 1].Text == "continue"; var target = cont ? loops.FirstOrDefault(l => l.Item2 != null) : loops.FirstOrDefault(); if (target == null) throw Error("No enclosing loop."); code.Jump(0x99, cont ? target.Item2 : target.Item1); Semicolon(); return; }
            if (Eat("throw")) { Emit(Expression()); code.Op(0x2a); Semicolon(); return; }
            if (Eat("switch")) { Switch(); return; }
            if (At("class") || At("interface") || At("import") || At("#include") || At("try") || At("with")) throw Error("Unsupported AS2 construct '" + Current.Text + "'.");
            Emit(Expression()); code.Op(0x17); Semicolon();
        }
        private void Variables()
        { do { string name = Identifier(); TypeAnnotation(); code.Push(name); if (Eat("=")) { Emit(Expression(2)); code.Op(0x3c); } else code.Op(0x41); } while (Eat(",")); }
        private void For()
        {
            Need("("); int saved = cursor; bool declaration = Eat("var");
            Node iterator = declaration ? new Node("variable", Identifier()) : At(";") ? null : Expression(11); if (declaration) TypeAnnotation();
            if (iterator != null && Eat("in"))
            {
                if (declaration) { code.Push(iterator.Name); code.Op(0x41); }
                Emit(Expression()); Need(")"); code.Op(0x55); ActionLabel next = code.Label(), cleanup = code.Label(), done = code.Label(); code.Mark(next); code.Op(0x4c); code.Push(null); code.Op(0x49); code.Jump(0x9d, cleanup);
                code.Store(0); code.Op(0x17); iterator = Resolve(iterator); Target(iterator, false); code.Register(0); code.Op(iterator.Kind == "variable" ? 0x1d : 0x4f);
                loops.Push(Tuple.Create(cleanup, next)); Statement(); loops.Pop(); code.Jump(0x99, next);
                code.Mark(cleanup); code.Op(0x4c); code.Push(null); code.Op(0x49); code.Jump(0x9d, done); code.Op(0x17); code.Jump(0x99, cleanup); code.Mark(done); code.Op(0x17); return;
            }
            cursor = saved; if (!Eat(";")) { if (Eat("var")) Variables(); else { Emit(Expression()); code.Op(0x17); } Need(";"); }
            ActionLabel begin = code.Label(), increment = code.Label(), end = code.Label(); code.Mark(begin);
            if (!Eat(";")) { Emit(Expression()); Need(";"); code.Op(0x12); code.Jump(0x9d, end); }
            Node step = Eat(")") ? null : Expression(); if (step != null) Need(")");
            loops.Push(Tuple.Create(end, increment)); Statement(); loops.Pop(); code.Mark(increment); if (step != null) { Emit(step); code.Op(0x17); } code.Jump(0x99, begin); code.Mark(end);
        }
        private void Switch()
        {
            Need("("); Emit(Expression()); Need(")"); Need("{");
            // Keep the discriminant on the stack until a case is selected.
            ActionLabel dispatch = code.Label(), end = code.Label(); code.Jump(0x99, dispatch); var cases = new List<Tuple<Node, ActionLabel>>(); ActionLabel otherwise = null;
            loops.Push(Tuple.Create(end, (ActionLabel)null));
            while (!Eat("}"))
            { if (Eat("case")) { Node value = Expression(2); Need(":"); ActionLabel label = code.Label(); code.Mark(label); cases.Add(Tuple.Create(value, label)); }
              else if (Eat("default")) { Need(":"); if (otherwise != null) throw Error("Duplicate default."); otherwise = code.Label(); code.Mark(otherwise); }
              else Statement(); }
            loops.Pop(); code.Jump(0x99, end); code.Mark(dispatch);
            var selections = new List<Tuple<ActionLabel, ActionLabel>>();
            foreach (var item in cases) { code.Op(0x4c); Emit(item.Item1); code.Op(0x66); ActionLabel selected = code.Label(); code.Jump(0x9d, selected); selections.Add(Tuple.Create(selected, item.Item2)); }
            code.Op(0x17); code.Jump(0x99, otherwise != null ? otherwise : end);
            foreach (var selection in selections) { code.Mark(selection.Item1); code.Op(0x17); code.Jump(0x99, selection.Item2); }
            code.Mark(end);
        }
        private static int Precedence(string op)
        {
            switch (op) { case ",": return 1; case "=": case "+=": case "-=": case "*=": case "/=": case "%=": case "&=": case "|=": case "^=": case "<<=": case ">>=": case ">>>=": return 2; case "?": return 3; case "||": return 4; case "&&": return 5; case "|": return 6; case "^": return 7; case "&": return 8; case "==": case "!=": case "===": case "!==": return 9; case "<": case ">": case "<=": case ">=": case "instanceof": return 10; case "<<": case ">>": case ">>>": return 11; case "+": case "-": return 12; case "*": case "/": case "%": return 13; default: return 0; }
        }
        private Node Expression(int minimum = 1)
        {
            Node left = Prefix();
            while (true)
            {
                string op = Current.Text; int p = Precedence(op); if (p == 0 || p < minimum) return left; cursor++;
                if (op == "?") { var yes = Expression(2); Need(":"); left = new Node("ternary", null, null, left, yes, Expression(3)); }
                else left = new Node(p == 2 ? "assign" : "binary", op, null, left, Expression(p == 2 ? p : p + 1));
            }
        }
        private Node Prefix()
        {
            string op = Current.Text;
            if (op == "!" || op == "~" || op == "-" || op == "+" || op == "typeof" || op == "delete" || op == "void" || op == "++" || op == "--") { cursor++; return new Node("unary", op, null, Prefix()); }
            if (Eat("new")) { Node target = Primary(); while (true) { if (Eat(".")) target = new Node("member", null, null, target, new Node("literal", null, Identifier())); else if (Eat("[")) { target = new Node("member", null, null, target, Expression()); Need("]"); } else break; } var result = new Node("new", null, null, target); if (Eat("(")) Arguments(result); return Postfix(result); }
            return Postfix(Primary());
        }
        private Node Primary()
        {
            if (Eat("(")) { Node value = Expression(); Need(")"); return value; }
            if (Eat("function")) { if (!At("(")) throw Error("Named function expressions are unsupported; use a named declaration or anonymous function."); return Function(""); }
            if (Eat("[")) { var result = new Node("array"); if (!Eat("]")) { do { result.Children.Add(At(",") ? new Node("literal", null, UndefinedValue.Instance) : Expression(2)); } while (Eat(",") && !At("]")); Need("]"); } return result; }
            if (Eat("{")) { var result = new Node("object"); if (!Eat("}")) { do { string name = Current.Value != null ? Convert.ToString(tokens[cursor++].Value, CultureInfo.InvariantCulture) : Identifier(); Need(":"); result.Children.Add(new Node("literal", null, name)); result.Children.Add(Expression(2)); } while (Eat(",") && !At("}")); Need("}"); } return result; }
            if (Eat("true")) return new Node("literal", null, true); if (Eat("false")) return new Node("literal", null, false); if (Eat("null")) return new Node("literal"); if (Eat("undefined")) return new Node("literal", null, UndefinedValue.Instance);
            if (Current.Value != null) return new Node("literal", null, tokens[cursor++].Value); return new Node("variable", Identifier());
        }
        private Node Postfix(Node value)
        {
            while (true)
            { if (Eat(".")) value = new Node("member", null, null, value, new Node("literal", null, Identifier())); else if (Eat("[")) { value = new Node("member", null, null, value, Expression()); Need("]"); }
              else if (Eat("(")) { var call = new Node("call", null, null, value); Arguments(call); value = call; }
              else if (Eat("++") || Eat("--")) value = new Node("postfix", tokens[cursor - 1].Text, null, value); else return value; }
        }
        private void Arguments(Node result) { if (!Eat(")")) { do { result.Children.Add(Expression(2)); } while (Eat(",")); Need(")"); } }
        private Node Function(string name)
        {
            Need("("); var parameters = new List<string>(); if (!Eat(")")) { do { parameters.Add(Identifier()); TypeAnnotation(); } while (Eat(",")); Need(")"); } TypeAnnotation();
            Code outer = code; var savedLoops = loops.ToArray(); var outerLocals = locals; locals = new HashSet<string>(outerLocals, StringComparer.Ordinal); locals.UnionWith(parameters); loops.Clear(); code = new Code(); Need("{");
            // AVM1 locals have function scope, including declarations below a use.
            CollectFunctionLocals();
            while (!Eat("}")) { if (At("<eof>")) throw Error("Unclosed function."); Statement(); } code.Push(UndefinedValue.Instance); code.Op(0x3e); ActionProgram body = code.Finish(); code = outer; locals = outerLocals; foreach (var loop in Enumerable.Reverse(savedLoops)) loops.Push(loop);
            return new Node("function", name, Tuple.Create(parameters, body));
        }
        private void CollectFunctionLocals()
        {
            int depth = 1;
            for (int scan = cursor; scan < tokens.Count && depth > 0; scan++)
            {
                string text = tokens[scan].Text;
                if (text == "function" && (scan == 0 || tokens[scan - 1].Text != "."))
                {
                    int body = scan + 1;
                    if (body < tokens.Count && tokens[body].Text != "(") locals.Add(tokens[body].Text);
                    while (body < tokens.Count && tokens[body].Text != "{") body++;
                    int nested = 1; body++;
                    while (body < tokens.Count && nested > 0) { if (tokens[body].Text == "{") nested++; else if (tokens[body].Text == "}") nested--; body++; }
                    scan = body - 1; continue;
                }
                if (text == "{") depth++; else if (text == "}") depth--;
                else if (text == "var" && scan + 1 < tokens.Count)
                {
                    locals.Add(tokens[scan + 1].Text); int nesting = 0;
                    for (int j = scan + 2; j < tokens.Count; j++)
                    { string token = tokens[j].Text; if (token == "(" || token == "[" || token == "{") nesting++; else if (token == ")" || token == "]" || token == "}") { if (nesting == 0) break; nesting--; } if (nesting == 0 && token == ";") break; if (nesting == 0 && token == "," && j + 1 < tokens.Count) locals.Add(tokens[j + 1].Text); }
                }
            }
        }
        private void EmitFunction(Node node)
        { var definition = (Tuple<List<string>, ActionProgram>)node.Value; code.Op(0x9b, new FunctionDefinition { Name = node.Name, Parameters = definition.Item1, Body = definition.Item2 }); }
        private void Emit(Node n)
        {
            n = Resolve(n);
            switch (n.Kind)
            {
                case "literal": code.Push(n.Value); return;
                case "variable": code.Push(n.Name); code.Op(0x1c); return;
                case "member": Emit(n.Children[0]); Emit(n.Children[1]); code.Op(0x4e); return;
                case "function": EmitFunction(n); return;
                case "call": case "new": Call(n); return;
                case "assign": Assign(n.Children[0], n.Children[1], n.Name); return;
                case "unary": case "postfix": Unary(n); return;
                case "array": for (int i = n.Children.Count - 1; i >= 0; i--) Emit(n.Children[i]); code.Push(n.Children.Count); code.Op(0x42); return;
                case "object": for (int i = n.Children.Count - 2; i >= 0; i -= 2) { Emit(n.Children[i]); Emit(n.Children[i + 1]); } code.Push(n.Children.Count / 2); code.Op(0x43); return;
                case "ternary": ActionLabel no = code.Label(), end = code.Label(); Emit(n.Children[0]); code.Op(0x12); code.Jump(0x9d, no); Emit(n.Children[1]); code.Jump(0x99, end); code.Mark(no); Emit(n.Children[2]); code.Mark(end); return;
                case "binary":
                    if (n.Name == ",") { Emit(n.Children[0]); code.Op(0x17); Emit(n.Children[1]); return; }
                    if (n.Name == "&&" || n.Name == "||") { ActionLabel done = code.Label(); Emit(n.Children[0]); code.Op(0x4c); if (n.Name == "&&") code.Op(0x12); code.Jump(0x9d, done); code.Op(0x17); Emit(n.Children[1]); code.Mark(done); return; }
                    Emit(n.Children[0]); Emit(n.Children[1]); Binary(n.Name); return;
                default: throw new InvalidOperationException(n.Kind);
            }
        }
        private void Binary(string op)
        {
            switch (op) { case "+": code.Op(0x47); break; case "-": code.Op(0x0b); break; case "*": code.Op(0x0c); break; case "/": code.Op(0x0d); break; case "%": code.Op(0x3f); break; case "==": case "!=": code.Op(0x49); if (op == "!=") code.Op(0x12); break; case "===": case "!==": code.Op(0x66); if (op == "!==") code.Op(0x12); break; case "<": code.Op(0x48); break; case ">": code.Op(0x67); break; case "<=": code.Op(0x67); code.Op(0x12); break; case ">=": code.Op(0x48); code.Op(0x12); break; case "&": code.Op(0x60); break; case "|": code.Op(0x61); break; case "^": code.Op(0x62); break; case "<<": code.Op(0x63); break; case ">>": code.Op(0x64); break; case ">>>": code.Op(0x65); break; case "instanceof": code.Op(0x54); break; default: throw Error("Unsupported operator " + op); }
        }
        private void Target(Node target, bool read)
        {
            target = Resolve(target);
            if (target.Kind == "variable") { code.Push(target.Name); if (read) { code.Op(0x4c); code.Op(0x1c); } }
            else if (target.Kind == "member") { Emit(target.Children[0]); Emit(target.Children[1]); if (read) { code.Store(2); code.Op(0x17); code.Store(1); code.Register(2); code.Register(1); code.Register(2); code.Op(0x4e); } }
            else throw Error("Expression cannot be assigned.");
        }
        private void Assign(Node target, Node value, string op)
        { target = Resolve(target); Target(target, op != "="); Emit(value); if (op != "=") Binary(op.Substring(0, op.Length - 1)); code.Store(0); code.Op(target.Kind == "variable" ? 0x1d : 0x4f); code.Register(0); }
        private void Unary(Node n)
        {
            Node value = Resolve(n.Children[0]);
            if (n.Name == "++" || n.Name == "--")
            { Target(value, true); if (n.Kind == "postfix") code.Store(3); code.Op(n.Name == "++" ? 0x50 : 0x51); code.Store(0); code.Op(value.Kind == "variable" ? 0x1d : 0x4f); code.Register(n.Kind == "postfix" ? 3 : 0); return; }
            if (n.Name == "delete") { if (value.Kind == "member") { Emit(value.Children[0]); Emit(value.Children[1]); code.Op(0x3a); } else if (value.Kind == "variable") { code.Push(value.Name); code.Op(0x3b); } else throw Error("Invalid delete operand."); return; }
            if (n.Name == "-") { code.Push(0); Emit(value); code.Op(0x0b); return; }
            Emit(value); switch (n.Name) { case "!": code.Op(0x12); break; case "~": code.Push(-1); code.Op(0x62); break; case "+": code.Op(0x4a); break; case "typeof": code.Op(0x44); break; case "void": code.Op(0x17); code.Push(UndefinedValue.Instance); break; }
        }
        private void Call(Node n)
        {
            Node target = Resolve(n.Children[0]); int count = n.Children.Count - 1; bool create = n.Kind == "new";
            if (!create && target.Kind == "variable")
            {
                string name = target.Name; var simple = new Dictionary<string, int> { { "stop", 7 }, { "play", 6 }, { "nextFrame", 4 }, { "prevFrame", 5 }, { "stopAllSounds", 9 }, { "getTimer", 0x34 } };
                if (simple.TryGetValue(name, out int op)) { if (count != 0) throw Error(name + " takes no arguments."); code.Op(op); if (name != "getTimer") code.Push(UndefinedValue.Instance); return; }
                if (name == "trace" || name == "random" || name == "int" || name == "Number" || name == "String") { if (count != 1) throw Error(name + " takes one argument."); Emit(n.Children[1]); code.Op(name == "trace" ? 0x26 : name == "random" ? 0x30 : name == "int" ? 0x18 : name == "Number" ? 0x4a : 0x4b); if (name == "trace") code.Push(UndefinedValue.Instance); return; }
                if (name == "gotoAndPlay" || name == "gotoAndStop") { if (count != 1) throw Error(name + " takes one frame."); Emit(n.Children[1]); code.Op(0x9f, name == "gotoAndPlay"); code.Push(UndefinedValue.Instance); return; }
                if (name == "getURL" || name == "loadMovie" || name == "loadVariables") { if (count < 1 || count > 3) throw Error("Invalid URL arguments."); Emit(n.Children[1]); if (count >= 2) Emit(n.Children[2]); else code.Push(""); byte flags = (byte)(name == "loadMovie" ? 0x40 : name == "loadVariables" ? 0x80 : 0); if (count == 3) { if (n.Children[3].Value is string method) flags |= (byte)(method.ToUpperInvariant() == "POST" ? 2 : method.ToUpperInvariant() == "GET" ? 1 : 0); else throw Error("URL method must be a literal."); } code.Op(0x9a, (int)flags); code.Push(UndefinedValue.Instance); return; }
            }
            for (int i = n.Children.Count - 1; i >= 1; i--) Emit(n.Children[i]); code.Push(count);
            if (target.Kind == "variable" && target.Name == "super" && !create) { Emit(target); code.Push(""); code.Op(0x52); }
            else if (target.Kind == "variable") { code.Push(target.Name); code.Op(create ? 0x40 : 0x3d); }
            else if (target.Kind == "member") { Emit(target.Children[0]); Emit(target.Children[1]); code.Op(create ? 0x53 : 0x52); }
            else { Emit(target); code.Push(""); code.Op(create ? 0x53 : 0x52); }
        }
        private static Node PathNode(string path)
        { var parts = path.Split('.'); Node result = new Node("variable", "_global"); foreach (string part in parts) result = new Node("member", null, null, result, new Node("literal", null, part)); return result; }
        private Node Resolve(Node n)
        {
            if (n.Kind != "variable" || locals.Contains(n.Name)) return n;
            if (classContext != null && classContext.InstanceMembers.Contains(n.Name)) return new Node("member", null, null, new Node("variable", "this"), new Node("literal", null, n.Name));
            if (classContext != null && classContext.StaticMembers.Contains(n.Name)) return new Node("member", null, null, PathNode(classContext.Name), new Node("literal", null, n.Name));
            if (aliases.TryGetValue(n.Name, out string qualified)) { if (classContext != null && qualified != classContext.Name) { classResolver?.Invoke(qualified); if (staticInitialization) classContext.InitializerDependencies.Add(qualified); } return PathNode(qualified); } return n;
        }
        internal static As2Class CompileClass(string source, Func<string, As2Class> require, IReadOnlyDictionary<string, string> implicitTypes = null, Func<string, As2Class> requireBase = null)
        {
            var c = new ActionScriptCompiler(source); c.Imports(require); c.classResolver = require; if (implicitTypes != null) foreach (var type in implicitTypes) if (!c.aliases.ContainsKey(type.Key)) c.aliases.Add(type.Key, type.Value); c.Eat("dynamic"); c.Need("class"); string name = c.Qualified(), leaf = name.Substring(name.LastIndexOf('.') + 1); string parent = c.Eat("extends") ? c.Qualified() : "Object";
            if (c.aliases.TryGetValue(parent, out string qualified)) parent = qualified;
            var baseClass = (requireBase ?? require)(parent); var result = new As2Class { Name = name, BaseName = parent }; if (baseClass != null) { result.InstanceMembers.UnionWith(baseClass.InstanceMembers); result.StaticMembers.UnionWith(baseClass.StaticMembers); }
            if (parent == "MovieClip") result.InstanceMembers.UnionWith(new[] { "_x", "_y", "_alpha", "_width", "_height", "_visible", "_rotation", "_xscale", "_yscale", "_parent", "_root", "_name", "_currentframe", "_totalframes", "attachMovie", "createEmptyMovieClip", "removeMovieClip", "duplicateMovieClip", "swapDepths", "getDepth", "getNextHighestDepth", "localToGlobal", "globalToLocal", "hitTest", "startDrag", "stopDrag" });
            c.aliases[leaf] = name; c.classContext = result; c.Need("{");
            var functions = new List<Tuple<bool, string, int, string>>(); var fields = new List<Tuple<bool, string, Node>>();
            while (!c.Eat("}"))
            {
                bool isStatic = false; while (c.At("public") || c.At("private") || c.At("static")) { if (c.Eat("static")) isStatic = true; else c.cursor++; }
                if (c.Eat("var"))
                { do { string field = c.Identifier(); c.TypeAnnotation(); var set = isStatic ? result.StaticMembers : result.InstanceMembers; if (!set.Add(field)) throw c.Error("Duplicate class member " + field); Node initial = c.Eat("=") ? c.Expression(2) : new Node("literal", null, UndefinedValue.Instance); fields.Add(Tuple.Create(isStatic, field, initial)); } while (c.Eat(",")); c.Semicolon(); }
                else if (c.Eat("function"))
                {
                    string method = c.Identifier(), accessor = ""; if ((method == "get" || method == "set") && !c.At("(")) { accessor = method; method = c.Identifier(); }
                    if (method != leaf) (isStatic ? result.StaticMembers : result.InstanceMembers).Add(method);
                    int function = c.cursor, brace = c.cursor; while (brace < c.tokens.Count && c.tokens[brace].Text != "{") brace++; if (brace == c.tokens.Count) throw c.Error("Missing method body."); int depth = 1, end = brace + 1; while (end < c.tokens.Count && depth != 0) { if (c.tokens[end].Text == "{") depth++; if (c.tokens[end].Text == "}") depth--; end++; } if (depth != 0) throw c.Error("Unclosed class method."); functions.Add(Tuple.Create(isStatic, method, function, accessor)); c.cursor = end;
                }
                else throw c.Error("Expected AS2 field or method.");
            }
            c.Eat(";"); c.Need("<eof>"); var methods = new List<Tuple<bool, string, Node, string>>(); Node constructor = null;
            foreach (var method in functions) { c.cursor = method.Item3; Node function = c.Function(""); if (method.Item2 == leaf) { if (method.Item1 || constructor != null || method.Item4.Length != 0) throw c.Error("Invalid constructor."); constructor = function; } else methods.Add(Tuple.Create(method.Item1, method.Item2, function, method.Item4)); }
            // Instance field expressions execute per construction, never as shared prototype arrays.
            c.code = new Code(); foreach (var field in fields.Where(f => !f.Item1)) { c.Assign(new Node("member", null, null, new Node("variable", "this"), new Node("literal", null, field.Item2)), field.Item3, "="); c.code.Op(0x17); }
            ActionProgram initializer = c.code.Finish();
            if (constructor == null) { c.code = new Code(); if (parent != "Object") { c.Call(new Node("call", null, null, new Node("variable", "super"))); c.code.Op(0x17); } c.code.Append(initializer); c.code.Push(UndefinedValue.Instance); c.code.Op(0x3e); constructor = new Node("function", "", Tuple.Create(new List<string>(), c.code.Finish())); }
            else { var f = (Tuple<List<string>, ActionProgram>)constructor.Value; constructor.Value = Tuple.Create(f.Item1, ActionProgram.Join(initializer, f.Item2)); }
            c.code = new Code(); Node ctor = PathNode(name), prototype = new Node("member", null, null, ctor, new Node("literal", null, "prototype"));
            ActionLabel done = c.code.Label(); c.Emit(ctor); c.code.Push(UndefinedValue.Instance); c.code.Op(0x66); c.code.Op(0x12); c.code.Jump(0x9d, done);
            string[] parts = name.Split('.'); for (int i = 1; i < parts.Length; i++)
            { Node package = PathNode(string.Join(".", parts.Take(i))); ActionLabel exists = c.code.Label(); c.Emit(package); c.code.Push(UndefinedValue.Instance); c.code.Op(0x66); c.code.Op(0x12); c.code.Jump(0x9d, exists); c.Assign(package, new Node("object"), "="); c.code.Op(0x17); c.code.Mark(exists); }
            c.Assign(ctor, constructor, "="); c.code.Op(0x17); c.Emit(ctor); c.Emit(PathNode(parent)); c.code.Op(0x69);
            foreach (var field in fields.Where(f => !f.Item1)) { c.Assign(new Node("member", null, null, prototype, new Node("literal", null, field.Item2)), new Node("literal", null, UndefinedValue.Instance), "="); c.code.Op(0x17); }
            foreach (var method in methods.Where(m => m.Item4.Length == 0)) { c.Assign(new Node("member", null, null, method.Item1 ? ctor : prototype, new Node("literal", null, method.Item2)), method.Item3, "="); c.code.Op(0x17); }
            foreach (var accessors in methods.Where(m => m.Item4.Length != 0).GroupBy(m => Tuple.Create(m.Item1, m.Item2)))
            { var get = accessors.Where(m => m.Item4 == "get").ToArray(); var set = accessors.Where(m => m.Item4 == "set").ToArray(); if (get.Length > 1 || set.Length > 1) throw c.Error("Duplicate AS2 accessor."); Node target = new Node("member", null, null, accessors.Key.Item1 ? ctor : prototype, new Node("literal", null, "addProperty")); var call = new Node("call", null, null, target, new Node("literal", null, accessors.Key.Item2), get.Length == 0 ? new Node("literal") : get[0].Item3, set.Length == 0 ? new Node("literal") : set[0].Item3); c.Call(call); c.code.Op(0x17); }
            c.staticInitialization = true;
            foreach (var field in fields.Where(f => f.Item1)) { c.Assign(new Node("member", null, null, ctor, new Node("literal", null, field.Item2)), field.Item3, "="); c.code.Op(0x17); }
            c.code.Mark(done); result.Code = c.code.Finish(); return result;
        }
        internal static IReadOnlyList<EventProgram> CompileHandlers(string source, bool button)
        {
            if (string.IsNullOrWhiteSpace(source)) return Array.Empty<EventProgram>(); var c = new ActionScriptCompiler(source); var entries = new List<EventProgram>();
            var events = new Dictionary<string, uint>(StringComparer.Ordinal) { { "keyUp", 1 }, { "keyDown", 2 }, { "mouseUp", 4 }, { "mouseDown", 8 }, { "mouseMove", 16 }, { "unload", 32 }, { "enterFrame", 64 }, { "load", 128 }, { "dragOver", 256 }, { "rollOut", 512 }, { "rollOver", 1024 }, { "releaseOutside", 2048 }, { "release", 4096 }, { "press", 8192 }, { "initialize", 16384 }, { "data", 32768 }, { "construct", 65536 }, { "keyPress", 131072 }, { "dragOut", 262144 } };
            while (!c.At("<eof>"))
            {
                if (!c.Eat(button ? "on" : "onClipEvent")) throw c.Error("Instance actions require " + (button ? "on(...)" : "onClipEvent(...)") + " handlers."); c.Need("("); uint flags = 0; byte key = 0;
                do { string name = c.Identifier(); if (!events.TryGetValue(name, out uint value)) throw c.Error("Unknown clip event " + name); flags |= value; if (name == "keyPress") { if (!(c.Current.Value is string text) || text.Length != 1) throw c.Error("keyPress requires a single character."); key = checked((byte)text[0]); c.cursor++; } } while (c.Eat(",")); c.Need(")"); c.code = new Code(); c.Statement(); entries.Add(new EventProgram { Flags = flags, Key = key, Actions = c.code.Finish(true) });
            }
            return entries.AsReadOnly();
        }
        private void Lex(string source)
        {
            int i = 0, line = 1; string[] operators = { ">>>=", "===", "!==", ">>>", "<<=", ">>=", "++", "--", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "==", "!=", "<=", ">=", "&&", "||", "<<", ">>" };
            while (i < source.Length)
            {
                char ch = source[i]; if (char.IsWhiteSpace(ch)) { if (ch == '\n') line++; i++; continue; }
                if (i + 1 < source.Length && source.Substring(i, 2) == "//") { while (i < source.Length && source[i] != '\n') i++; continue; }
                if (i + 1 < source.Length && source.Substring(i, 2) == "/*") { i += 2; while (i + 1 < source.Length && source.Substring(i, 2) != "*/") { if (source[i++] == '\n') line++; } if (i + 1 >= source.Length) throw new InvalidDataException("AS2: unclosed comment."); i += 2; continue; }
                int start = i; object value = null;
                if (ch == '"' || ch == '\'')
                { char quote = ch; i++; var s = new StringBuilder(); while (i < source.Length && source[i] != quote) { ch = source[i++]; if (ch == '\n' || ch == '\r') throw new InvalidDataException("AS2: newline in string."); if (ch == '\\') { if (i == source.Length) break; ch = source[i++]; switch (ch) { case 'n': ch = '\n'; break; case 'r': ch = '\r'; break; case 't': ch = '\t'; break; case 'b': ch = '\b'; break; case 'f': ch = '\f'; break; case 'u': case 'x': int digits = ch == 'u' ? 4 : 2; if (i + digits > source.Length) throw new InvalidDataException("AS2: truncated escape."); ch = (char)int.Parse(source.Substring(i, digits), NumberStyles.HexNumber, CultureInfo.InvariantCulture); i += digits; break; } } s.Append(ch); } if (i == source.Length) throw new InvalidDataException("AS2: unclosed string."); i++; value = s.ToString(); }
                else if (char.IsDigit(ch) || ch == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1]))
                { if (ch == '0' && i + 1 < source.Length && (source[i + 1] == 'x' || source[i + 1] == 'X')) { i += 2; int digits = i; while (i < source.Length && Uri.IsHexDigit(source[i])) i++; value = unchecked((int)uint.Parse(source.Substring(digits, i - digits), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); } else { while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.')) i++; if (i < source.Length && (source[i] == 'e' || source[i] == 'E')) { i++; if (i < source.Length && (source[i] == '+' || source[i] == '-')) i++; while (i < source.Length && char.IsDigit(source[i])) i++; } double number = double.Parse(source.Substring(start, i - start), CultureInfo.InvariantCulture); value = number >= int.MinValue && number <= int.MaxValue && number == Math.Truncate(number) ? (object)(int)number : number; } }
                else if (char.IsLetter(ch) || ch == '_' || ch == '$') { do { i++; } while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_' || source[i] == '$')); }
                else { string op = operators.FirstOrDefault(o => i + o.Length <= source.Length && string.CompareOrdinal(source, i, o, 0, o.Length) == 0); i += op?.Length ?? 1; }
                tokens.Add(new Token { Text = value is string ? "<string>" : value != null ? "<number>" : source.Substring(start, i - start), Value = value, Line = line });
            }
            tokens.Add(new Token { Text = "<eof>", Line = line });
        }
    }
}
