using System.Collections;
using System.Collections.Generic;
namespace BinaryAssetBuilder.UiCompiler
{
    public sealed class UiAssetGroup : IReadOnlyList<CompiledUiAsset>
    {
        private readonly IReadOnlyList<CompiledUiAsset> assets;
        public IReadOnlyList<string> InputFiles { get; }
        public int Count => assets.Count;
        public CompiledUiAsset this[int index] => assets[index];
        internal UiAssetGroup(IReadOnlyList<CompiledUiAsset> assets, IReadOnlyList<string> inputs) { this.assets = assets; InputFiles = inputs; }
        public IEnumerator<CompiledUiAsset> GetEnumerator() => assets.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
