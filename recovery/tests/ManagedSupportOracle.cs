using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;

// Runs in separate processes/directories so original and rebuilt assembly identities cannot alias.
class ManagedSupportOracle
{
    static readonly List<string[]> Rows = new List<string[]>();
    static Assembly Expressions, Verifier;
    static Type ValueType;
    static BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    static string Text(object value) { return value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture); }
    static object Call(object target, string method, params object[] args)
    {
        return target.GetType().GetMethod(method).Invoke(target, args);
    }
    static string Error(Exception ex)
    {
        while (ex is TargetInvocationException) ex = ex.InnerException;
        var code = ex.GetType().GetProperty("ErrorCode");
        return ex.GetType().FullName + ":" + ex.Message + ":" + (code == null ? "" : Text(code.GetValue(ex, null)));
    }
    static void Case(string name, Func<string> action)
    {
        string result;
        try { result = "OK:" + action(); }
        catch (Exception ex) { result = "ERR:" + Error(ex); }
        Rows.Add(new[] { name, result });
    }
    static AssetDeclarationDocument Document()
    {
        var doc = new AssetDeclarationDocument();
        Type stateType = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        object state = Activator.CreateInstance(stateType, true);
        stateType.GetMethod("FromScratch").Invoke(state, null);
        stateType.GetField("AllDefines").SetValue(state, new DefinitionSet());
        stateType.GetField("SourcePath").SetValue(state, "managed-support.xml");
        var xml = new XmlDocument();
        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("ea", "uri:ea.com:eala:asset");
        stateType.GetField("NamespaceManager").SetValue(state, ns);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(doc, state);
        return doc;
    }
    static object Evaluator(AssetDeclarationDocument doc)
    {
        return Activator.CreateInstance(Expressions.GetType("BinaryAssetBuilder.ExpressionEval.ExpressionEvaluator"), new object[] { doc });
    }
    static object Value(string[] spec)
    {
        return ValueType.GetMethod(spec[0]).Invoke(null, new object[] { spec[1] });
    }
    static string State(object value)
    {
        return Text(ValueType.GetField("_ValueType", All).GetValue(value)) + ":" +
            Text(ValueType.GetField("_Int", All).GetValue(value)) + ":" +
            ((double)ValueType.GetField("_Float", All).GetValue(value)).ToString("R", CultureInfo.InvariantCulture) + ":" +
            Text(ValueType.GetField("_String", All).GetValue(value));
    }
    static string Signature(MethodBase m)
    {
        var parts = new List<string>();
        foreach (var p in m.GetParameters()) parts.Add(p.ParameterType.ToString());
        return m.Name + "(" + string.Join(",", parts.ToArray()) + ")" + (m is MethodInfo ? ":" + ((MethodInfo)m).ReturnType : "");
    }
    static void Contract(Assembly assembly)
    {
        var entries = new List<string>();
        foreach (var type in assembly.GetTypes())
        {
            if (type.Name.StartsWith("<") || type.Namespace == "System.Runtime.CompilerServices") continue;
            entries.Add("type:" + type.FullName + ":" + type.BaseType);
            foreach (var it in type.GetInterfaces()) entries.Add(type.FullName + ":interface:" + it.FullName);
            foreach (var f in type.GetFields(All))
            {
                // The concurrent verifier replaces this one private static
                // field with an explicit context parameter. Other contracts
                // and all behavioral comparisons stay exact.
                if (type.FullName == "BinaryAssetBuilder.GameDataVerifier.Plugin+GameObjectVerifier" && f.Name == "_NamespaceManager")
                {
                    if (!f.IsPrivate || !f.IsStatic || f.FieldType != typeof(XmlNamespaceManager))
                        throw new Exception("Unexpected legacy verifier namespace field contract");
                    continue;
                }
                entries.Add(type.FullName + ":field:" + f.Name + ":" + f.FieldType + ":" + (f.IsLiteral ? Text(f.GetRawConstantValue()) : ""));
            }
            foreach (var m in type.GetMethods(All))
            {
                string signature = Signature(m);
                if (type.FullName == "BinaryAssetBuilder.GameDataVerifier.Plugin+GameObjectVerifier")
                {
                    string[] legacyContextHelpers = {
                        "Verify(System.String,System.String,System.Xml.XmlNode):System.Boolean",
                        "VerifyDrawModules(System.Xml.XmlNode,System.String):System.Boolean",
                        "VerifyDrawModules_StateNameReferences(System.Xml.XmlNode,System.Collections.Generic.Dictionary`2[System.String,System.Int32]&,System.String):System.Boolean",
                        "VerifyDrawModules_UniqueConditionsYes(System.Xml.XmlNode,System.String,System.Collections.Generic.Dictionary`2[System.String,System.Int32]&):System.Boolean",
                        "VerifyDrawModules_DuplicateDefaultStates(System.Xml.XmlNode,System.String):System.Boolean",
                        "VerifyModuleIds(System.Xml.XmlNode,System.String,System.String):System.Boolean"
                    };
                    if (Array.IndexOf(legacyContextHelpers, signature) >= 0)
                    {
                        if (!m.IsPrivate || !m.IsStatic) throw new Exception("Unexpected legacy verifier helper visibility");
                        signature = signature.Replace("):System.Boolean", ",System.Xml.XmlNamespaceManager):System.Boolean");
                    }
                }
                entries.Add(type.FullName + ":method:" + signature);
            }
            foreach (var c in type.GetConstructors(All)) entries.Add(type.FullName + ":ctor:" + Signature(c));
        }
        if (assembly.GetName().Name == "BinaryAssetBuilder.GameDataVerifier")
            Rows.Add(new[] { "contract-policy", "Normalize only GameObjectVerifier private static _NamespaceManager removal and explicit XmlNamespaceManager addition to its six private context helpers; all other inventory and behavioral rows remain exact." });
        entries.Sort(StringComparer.Ordinal);
        foreach (string entry in entries) Rows.Add(new[] { "contract", entry });
    }
    static void ExpressionCases()
    {
        var specs = new List<string[]>();
        foreach (string n in new[] { "0", "1", "-1", "2", "2147483647", "-2147483648", "2147483648", "bad", "" }) specs.Add(new[] { "NewInteger", n });
        foreach (string n in new[] { "0", "1.5", "2.5", "-1.5", "1e20", "NaN", "Infinity", "1,5", "bad" }) specs.Add(new[] { "NewFloat", n });
        foreach (string n in new[] { "\"\"", "\"x\"", "\"2\"", "\"1.5\"", "'abc'", "", "x" }) specs.Add(new[] { "NewQuotedString", n });
        foreach (string n in new[] { null, "", "3", "1.25", "hello", "2147483648", "NaN" }) specs.Add(new[] { "NewFromLookup", n });
        foreach (string culture in new[] { "en-US", "de-DE", "tr-TR" })
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
            for (int i = 0; i < specs.Count; i++)
            {
                var left = specs[i];
                foreach (string unary in new[] { "AsInteger", "AsFloat", "AsString", "Not", "Negate", "FuncRound" })
                {
                    string method = unary;
                    Case(culture + ":unary:" + i + ":" + method, delegate { object v = Value(left); return Text(Call(v, method)) + ":" + State(v); });
                }
                for (int j = 0; j < specs.Count; j++)
                {
                    var right = specs[j];
                    foreach (string binary in new[] { "LogicalOr", "LogicalAnd", "IsEqual", "IsLessThan", "IsGreaterThan", "Multiply", "Divide", "Modulo", "Add", "Subtract" })
                    {
                        string method = binary;
                        Case(culture + ":binary:" + i + ":" + j + ":" + method, delegate { object l = Value(left), r = Value(right); Call(l, method, r); return State(l) + "|" + State(r); });
                    }
                }
            }
            var doc = Document();
            var other = Document();
            foreach (string name in new[] { "I", "F", "S", "PENDING", "EMPTY" })
                doc.AllDefines[name] = new Definition { Name = name, Document = name == "I" ? doc : other,
                    EvaluatedValue = name == "I" ? "7" : name == "F" ? "1.5" : name == "S" ? "hello" : name == "EMPTY" ? "" : null };
            object evaluator = Evaluator(doc);
            foreach (string expression in new[] {
                "=1+2*3", "=(1+2)*3", "=7/2", "=8/2", "=5%2", "=-2+3", "=1=1", "=1!=2", "=1<2", "=2>1", "=1<=1", "=2>=2",
                "=1 or 0 and 0", "=0 and (1/0)", "=1 or (1/0)", "=round(2.5)", "=round(-1.5)", "=round(2)", "=round('x')",
                "='a'+'b'", "=\"a\"+'b'", "='2'+3", "='a'='A'", "='a'<'b'", "=1.5%1", "=1/0", "=1.5/0", "=2147483647+1", "=-2147483648/-1",
                "=$I+$F", "=$S+'!'", "=$EMPTY", "=$MISSING", "=$PENDING", "=", "", "1+2", "=1+", "=(1", "=1 2", "=foo", "=1@2", "=1e2", "=.5", "=1.", "=1\n+\t2", "=1==1", "=not 0", "=!0", "=1!=1!=0", "='unterminated", "=$I" })
            {
                string input = expression;
                Case(culture + ":parse:" + expression, delegate { return Text(Call(evaluator, "Evaluate", input)); });
            }
            foreach (string raw in new[] { null, "", "literal", "=1+2", "=$I", "=$F", "=$MISSING", "=1/0" })
            {
                string original = raw;
                Case(culture + ":definition:" + Text(raw), delegate {
                    var d = new Definition { Name = "D", OriginalValue = original, EvaluatedValue = "cached", Document = doc };
                    string error = "";
                    try { Call(evaluator, "EvaluateDefinition", d); Call(evaluator, "EvaluateDefinition", d); } catch (Exception ex) { error = Error(ex); }
                    return Text(d.OriginalValue) + "|" + Text(d.EvaluatedValue) + "|" + error;
                });
            }
            Case(culture + ":used-defines", delegate { var parts = new List<string>(); foreach (var kv in doc.UsedDefines) parts.Add(kv.Key + "=" + kv.Value); return string.Join(";", parts.ToArray()); });
            foreach (string lookup in new[] { "$I", "$F", "$S", "$MISSING", "$PENDING", "", "$", "I", null })
            { string input = lookup; Case(culture + ":lookup:" + Text(lookup), delegate { return Text(Call(evaluator, "LookupDefine", input)); }); }
        }
        Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
        if ((string)Call(Evaluator(Document()), "Evaluate", "=1+2*3") != "7") throw new Exception("Expression sanity failed");
        ExpressionEvaluatorWrapper.LoadAssembly();
        var wrapped = ExpressionEvaluatorWrapper.GetEvaluator(Document());
        if (wrapped == null || wrapped.Evaluate("=7/2") != "3.5") throw new Exception("Core expression wrapper failed");
        Case("core-wrapper", delegate { return wrapped.Evaluate("=7/2"); });
    }
    static bool Verify(object plugin, string body, string root)
    {
        var doc = Document();
        var xml = new XmlDocument();
        xml.LoadXml("<" + root + " xmlns='uri:ea.com:eala:asset' id='G'>" + body + "</" + root + ">");
        var declaration = new InstanceDeclaration(doc);
        declaration.XmlNode = xml.DocumentElement;
        return ((IAssetBuilderVerifierPlugin)plugin).VerifyInstance(declaration);
    }
    static void VerifierCases()
    {
        string[] bodies = {
            "", "<Behaviors><A id='a'/></Behaviors>", "<Behaviors><A/></Behaviors>", "<Behaviors><A id=''/></Behaviors>",
            "<Behaviors><A id='a'/><B id='a'/></Behaviors>", "<Behaviors><A id='I'/></Behaviors><AI><B id='i'/></AI>",
            "<Behaviors><A id='a'/></Behaviors><Draws><D id='A'/></Draws>", "<Body><A id='a'/><B id='a'/></Body>",
            "<Other><A/><B/></Other>", "<Draws><D id='d'><AnimationState ParseCondStateType='PARSE_DEFAULT'/></D></Draws>",
            "<Draws><D id='d'><AnimationState ParseCondStateType='PARSE_DEFAULT'/><AnimationState ParseCondStateType='PARSE_DEFAULT'/></D></Draws>",
            "<Draws><D id='d'><AnimationState ConditionsYes='A B'/><AnimationState ConditionsYes='A B'/></D></Draws>",
            "<Draws><D id='d'><AnimationState ConditionsYes='A B'/><AnimationState ConditionsYes='B A'/></D></Draws>",
            "<Draws><D id='d'><AnimationState ConditionsYes='A B'/><AnimationState ConditionsYes='A C'/></D></Draws>",
            "<Draws><D id='d'><AnimationState ConditionsYes='A  B'/><AnimationState ConditionsYes='B A'/></D></Draws>",
            "<Draws><D id='d'><AnimationState ConditionsYes='A A'/><AnimationState ConditionsYes='A B'/></D></Draws>",
            "<Draws><D id='d'><AnimationState StateName='idle'/><AnimationState><Script>CurDrawableSetTransitionAnimState(\"idle\")</Script></AnimationState></D></Draws>",
            "<Draws><D id='d'><AnimationState><Script>CurDrawableSetTransitionAnimState(\"missing\")</Script></AnimationState></D></Draws>",
            "<Draws><D id='d'><AnimationState StateName='x'/><AnimationState StateName='x'/><AnimationState><Script>CurDrawableSetTransitionAnimState(\"x\")</Script></AnimationState></D></Draws>",
            "<Draws><D id='d'><AnimationState><Script>unrelated()</Script></AnimationState></D></Draws>",
            "<Draws><D id='d'><AnimationState><Script/></AnimationState></D></Draws>",
            "<Draws><D id='d'><AnimationState><Script>CurDrawableSetTransitionAnimState(bad)</Script></AnimationState></D></Draws>",
            "<Draws><D id='d'><AnimationState StateName='x'/><AnimationState><Script><![CDATA[CurDrawableSetTransitionAnimState(\"bad\");CurDrawableSetTransitionAnimState(\"x\")]]></Script></AnimationState></D></Draws>"
        };
        foreach (string culture in new[] { "en-US", "tr-TR" })
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
            foreach (int platform in new[] { 0, 1, 2, 99 })
            {
                var plugin = Activator.CreateInstance(Verifier.GetType("BinaryAssetBuilder.GameDataVerifier.Plugin"), true);
                ((IAssetBuilderPluginBase)plugin).Initialize(null, (TargetPlatform)platform);
                ((IAssetBuilderPluginBase)plugin).ReInitialize(null, (TargetPlatform)platform);
                for (int i = 0; i < bodies.Length; i++)
                {
                    string body = bodies[i];
                    foreach (string root in new[] { "GameObject", "Other", "ea:GameObject" })
                    {
                        string node = root;
                        // Prefixed names require the matching namespace declaration.
                        string input = body;
                        Case(culture + ":verify:" + platform + ":" + i + ":" + node, delegate {
                            if (node.Contains(":")) {
                                var xml = new XmlDocument(); xml.LoadXml("<ea:GameObject xmlns:ea='uri:ea.com:eala:asset' id='G'>" + input + "</ea:GameObject>");
                                var d = new InstanceDeclaration(Document()); d.XmlNode = xml.DocumentElement;
                                return Text(((IAssetBuilderVerifierPlugin)plugin).VerifyInstance(d));
                            }
                            return Text(Verify(plugin, input, node));
                        });
                    }
                }
                Case(culture + ":null-node:" + platform, delegate { return Text(((IAssetBuilderVerifierPlugin)plugin).VerifyInstance(new InstanceDeclaration(Document()))); });
                Case(culture + ":null-instance:" + platform, delegate { return Text(((IAssetBuilderVerifierPlugin)plugin).VerifyInstance(null)); });
                if (!Verify(plugin, bodies[0], "GameObject") || Verify(plugin, bodies[2], "GameObject")) throw new Exception("Verifier sanity failed");
            }
        }
    }
    static int Main(string[] args)
    {
        try {
            Expressions = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BinaryAssetBuilder.ExpressionEval.dll"));
            Verifier = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BinaryAssetBuilder.GameDataVerifier.dll"));
            ValueType = Expressions.GetType("BinaryAssetBuilder.ExpressionEval.ExpressionValue", true);
            Contract(Expressions); Contract(Verifier);
            ExpressionCases(); VerifierCases();
            File.WriteAllText(args[0], new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(Rows));
            Console.WriteLine("PASS managed support cases=" + Rows.Count);
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
