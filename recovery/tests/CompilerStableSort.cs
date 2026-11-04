using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;

// The reference below is the unchanged insertion rule from 072beb3f, with
// only the comparer parameter exposed as its interface. Full SDK byte
// differential remains in the production acceptance workflow.
public static class CompilerStableSort
{
    static int checks, cases;
    static void Require(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    sealed class CountComparer : IComparer<InstanceDeclaration>
    {
        readonly IComparer<InstanceDeclaration> inner;
        public long Calls;
        public CountComparer(IComparer<InstanceDeclaration> inner) { this.inner = inner; }
        public int Compare(InstanceDeclaration a, InstanceDeclaration b) { Calls++; return inner.Compare(a,b); }
    }
    static InstanceDeclaration Instance(string type, string name)
    {
        var xml = new XmlDocument(); var node = xml.CreateElement(type); node.SetAttribute("id",name);
        return new InstanceDeclaration(null) { XmlNode = node };
    }
    static IComparer<InstanceDeclaration> Comparer(IDictionary<uint,int> counts)
    {
        var type = typeof(AssetDeclarationDocument).GetNestedType("TypeDepCompare", BindingFlags.NonPublic);
        return (IComparer<InstanceDeclaration>)Activator.CreateInstance(type, new object[] { counts });
    }
    static void Compare(List2<InstanceDeclaration> input, List2<InstanceDeclaration> seed, IComparer<InstanceDeclaration> comparer)
    {
        input.Sort(comparer);
        var expected = new List2<InstanceDeclaration>(seed);
        foreach (var instance in input) expected.Insert(LegacyPosition(expected,instance,comparer),instance);
        var actual = new List2<InstanceDeclaration>(seed);
        typeof(AssetDeclarationDocument).GetMethod("StableSort_ByAssetDependencies", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null,new object[] {input,actual,comparer});
        Require(expected.SequenceEqual(actual), "StableSort changed the legacy order in case " + cases);
        cases++;
    }
    static void Graphs()
    {
        foreach (string culture in new[] { "en-US", "tr-TR", "" })
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var random = new Random(193);
            for (int sample=0;sample<20;sample++)
            {
                var nodes = new List<InstanceDeclaration>(); var counts = new Dictionary<uint,int>();
                for (int i=0;i<64;i++)
                {
                    var instance = Instance("Type" + random.Next(6), "Name" + random.Next(100000).ToString("D6") + "_" + i);
                    nodes.Add(instance); counts[instance.Handle.TypeId] = random.Next(4);
                }
                // Graph order is independent of schema/name order. Include null,
                // empty and transitive dependency sets, plus arbitrary base seeds.
                for (int i=0;i<nodes.Count;i++)
                {
                    if (i%5==0) continue;
                    var dependencies = nodes[i].AllDependentInstances = new InstanceHandleSet();
                    for (int j=0;j<i;j++) if (random.Next(9)==0)
                    {
                        dependencies.TryAdd(nodes[j].Handle);
                        if (nodes[j].AllDependentInstances != null)
                            foreach (var dependency in nodes[j].AllDependentInstances) dependencies.TryAdd(dependency);
                    }
                }
                var input = new List2<InstanceDeclaration>(); var seed = new List2<InstanceDeclaration>();
                foreach (var node in nodes.OrderBy(value=>random.Next()))
                    if (sample%2==0 && random.Next(4)==0) seed.Add(node); else input.Add(node);
                Compare(input,seed,Comparer(counts));
            }
            // Culture-equal names must retain the original insertion behavior.
            var equal = new List2<InstanceDeclaration>();
            equal.Add(Instance("ArmorTemplate","\u00e9")); equal.Add(Instance("ArmorTemplate","e\u0301"));
            Compare(equal,new List2<InstanceDeclaration>(),Comparer(new Dictionary<uint,int> {{equal[0].Handle.TypeId,0}}));
            // Legacy symbol hashes can alias distinct type names. Its same-type
            // insertion rule uses TypeId, while its schema comparer uses names.
            var seen = new Dictionary<uint,string>(); bool collision=false;
            for(int i=0;i<4096 && !collision;i++)
            {
                string type="Type"+i.ToString("D6"); uint id=InstanceHandle.GetTypeId(type); string previous;
                if(seen.TryGetValue(id,out previous))
                {
                    var aliased=new List2<InstanceDeclaration>();aliased.Add(Instance(previous,"Zulu"));aliased.Add(Instance(type,"Alpha"));
                    Compare(aliased,new List2<InstanceDeclaration>(),Comparer(new Dictionary<uint,int> {{id,0}}));collision=true;
                }
                else seen.Add(id,type);
            }
            Require(collision,"Legacy symbol-hash collision fixture no longer reproduces");
        }
    }
    sealed class IdentityIndex : KeyedCollection2<InstanceHandle, KeyValuePair<InstanceHandle, int>>
    {
        protected override InstanceHandle GetKeyForItem(KeyValuePair<InstanceHandle, int> item) { return item.Key; }
    }
    static void IdentityIndexes()
    {
        // Distinct full identities can have the same legacy XOR hash. A hash
        // index must preserve both, as well as the collection's insertion order.
        var first = new InstanceHandle(0x100u, 0x201u);
        var second = new InstanceHandle(0x101u, 0x200u);
        Require(first.GetHashCode() == second.GetHashCode() && first.CompareTo(second) != 0, "Identity collision fixture");
        var index = new IdentityIndex();
        index.Add(new KeyValuePair<InstanceHandle, int>(second, 2));
        index.Insert(0, new KeyValuePair<InstanceHandle, int>(first, 1));
        Require(index.Select(value => value.Value).SequenceEqual(new[] {1, 2}), "Index changed collection insertion order");
        Require(index[new InstanceHandle(first)].Value == 1 && index[new InstanceHandle(second)].Value == 2, "Hash collision lost a distinct identity");
        bool duplicate = false;
        try { index.Add(new KeyValuePair<InstanceHandle, int>(new InstanceHandle(first), 3)); }
        catch (ArgumentException) { duplicate = true; }
        Require(duplicate && index.Count == 2, "Equal identity must remain a duplicate");
        index[0] = new KeyValuePair<InstanceHandle, int>(new InstanceHandle(first), 4);
        Require(index[first].Value == 4, "Replacing a list entry did not update its identity index");
        index.Remove(first);
        Require(index.Count == 1 && !index.Contains(first) && index[second].Value == 2, "Removing one collision damaged the other identity");
        index.Clear();
        Require(index.Count == 0 && !index.Contains(second), "Clear retained identity entries");
    }
    static object AppendWork()
    {
        const int size=1024;
        var input=new List2<InstanceDeclaration>(); var counts=new Dictionary<uint,int>();
        for(int i=0;input.Count<size && i<100000;i++)
        {
            var node=Instance("Type"+i.ToString("D6"),"Name");
            if(counts.ContainsKey(node.Handle.TypeId))continue;
            input.Add(node);counts.Add(node.Handle.TypeId,0);
        }
        Require(input.Count==size,"Append workload must contain 1024 distinct type identities");
        var comparer=Comparer(counts); input.Sort(comparer);
        var reference=new List2<InstanceDeclaration>(); var slow=new CountComparer(comparer);
        var clock=Stopwatch.StartNew();
        foreach(var node in input) reference.Insert(LegacyPosition(reference,node,slow),node);
        clock.Stop();double referenceMs=clock.Elapsed.TotalMilliseconds;
        var actual=new List2<InstanceDeclaration>(); var fast=new CountComparer(comparer);clock.Restart();
        typeof(AssetDeclarationDocument).GetMethod("StableSort_ByAssetDependencies",BindingFlags.Static|BindingFlags.NonPublic)
            .Invoke(null,new object[] {input,actual,fast});
        clock.Stop();double optimizedMs=clock.Elapsed.TotalMilliseconds;
        Require(reference.SequenceEqual(actual),"Append workload changed asset order");
        Require(slow.Calls==(long)size*(size-1)/2 && fast.Calls<size*4,"Append workload still scans the existing output for each asset");
        Console.WriteLine("StableSort helper only: assets={0}; schema comparisons={1}->{2}; ms={3:F3}->{4:F3}; initial schema sort and SDK build excluded",
            size,slow.Calls,fast.Calls,referenceMs,optimizedMs);
        return new {assets=size,referenceComparisons=slow.Calls,optimizedComparisons=fast.Calls,referenceMs,optimizedMs,
            scope="Single direct production sorting-helper sample, excluding initial schema sort, serialization, native compilation and publication; not an end-to-end SDK speedup"};
    }
    public static int Main(string[] args)
    {
        var culture=Thread.CurrentThread.CurrentCulture;
        try
        {
            Graphs();IdentityIndexes();object work=AppendWork();
            File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new {succeeded=true,sourceCommit=args[2],runId=args[3],checks,
                tests=new[] {"frozen-legacy-order-DAGs-base-prefixes-null-empty-transitive-dependencies-culture-equal-names","identity-index-collisions-insertion-replace-remove-clear","append-workload-linear-comparisons"},cases,work}));
            Console.WriteLine("PASS StableSort: "+cases+" order cases; "+checks+" checks");return 0;
        }
        catch(Exception error) {Console.Error.WriteLine(error);return 1;}
        finally {Thread.CurrentThread.CurrentCulture=culture;}
    }
	private static int LegacyPosition(List2<InstanceDeclaration> finalList, InstanceDeclaration instance, IComparer<InstanceDeclaration> depCompare)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		uint num5 = 0u;
		uint num6 = 0u;
		bool flag = false;
		foreach (InstanceDeclaration final in finalList)
		{
			if (final.Handle.TypeId != num6)
			{
				num3 = num2;
				num6 = final.Handle.TypeId;
			}
			if (instance.AllDependentInstances != null && instance.AllDependentInstances.Contains(final.Handle))
			{
				num = num2 + 1;
				num4 = num;
				if (instance.Handle.TypeId != final.Handle.TypeId)
				{
					flag = false;
					num5 = final.Handle.TypeId;
				}
			}
			else
			{
				if (final.AllDependentInstances != null && final.AllDependentInstances.Contains(instance.Handle))
				{
					if (num3 > num4 && instance.Handle.TypeId != final.Handle.TypeId)
					{
						num = num3;
					}
					break;
				}
				if (instance.Handle.TypeId == final.Handle.TypeId)
				{
					if (!flag)
					{
						num = num2;
						flag = true;
					}
					if (string.Compare(instance.Handle.InstanceName, final.Handle.InstanceName) > 0)
					{
						num = num2 + 1;
					}
				}
				else if (final.Handle.TypeId == num5)
				{
					num = num2 + 1;
				}
				else
				{
					num5 = 0u;
					if (depCompare.Compare(instance, final) > 0)
					{
						num = num2 + 1;
					}
				}
			}
			num2++;
		}
		return num;
	}

}
