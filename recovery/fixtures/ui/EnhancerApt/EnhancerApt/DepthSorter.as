import EnhancerApt.GameObject;

class EnhancerApt.DepthSorter {
    /**
     * 将数组中所有对象的深度重新分配，使得按深度升序排列后的对象顺序，
     * 与按 getIndex 升序排列的顺序完全一致。
     * 
     * @param objects       同一世界中的对象数组（深度值互不相同）
     * @param getIndex      用于获取排序索引的函数
     * @param getGameObject 用于从数组元素获取 GameObject 的函数
     */
    public static function sortByIndex(objects: Array, getIndex: Function, getGameObject: Function): Void {
        var desired:Array = objects.slice();

        desired.sort(compareByIndex(getIndex));

        reassignDepthsByOrder(desired, getGameObject);
    }

    /**
     * 将两个数组的对象重新分配深度：
     * - bottomArray 中所有对象的深度都小于 topArray 中所有对象的深度；
     * - 每个数组内部按 getIndex 升序排列（index 小 → 深度小）。
     *
     * @param bottomArray  深度应较小的数组
     * @param topArray     深度应较大的数组
     * @param getIndex     获取数组内排序索引的函数
     * @param getGameObject 获取对应 GameObject 的函数
     */
    public static function sortTwoArraysByDepth(bottomArray: Array, topArray: Array, getIndex: Function, getGameObject: Function): Void {
        // 1. 两个数组分别按 index 升序排序
        var sortedBottom: Array = bottomArray.slice();
        sortedBottom.sort(compareByIndex(getIndex));

        var sortedTop: Array = topArray.slice();
        sortedTop.sort(compareByIndex(getIndex));

        // 2. 合并得到全局目标顺序（先下后上）
        var targetOrder: Array = sortedBottom.concat(sortedTop);

        // 3. 执行深度重排
        reassignDepthsByOrder(targetOrder, getGameObject);
    }

    /**
     * 将一组对象按指定顺序重新分配深度（深度集合不变）：
     * 将 targetOrder 中所有对象重新分配已有的 depth。
     * depth 的数值集合保持不变，仅改变对象与 depth 的对应关系。
     * targetOrder[0] 获得最小 depth，targetOrder[1] 获得第二小 depth，以此类推。
     *
     * @param targetOrder  已按期望深度升序排列的对象数组
     * @param getGameObject 获取 GameObject 的函数
     * 前置条件：
     * - targetOrder 中对象互不重复；
     * - 所有对象当前 depth 两两不同；
     * - GameObject.swapDepth() 会立即交换两个对象的 depth。
     */
    private static function reassignDepthsByOrder(targetOrder: Array, getGameObject: Function): Void {
        // 当前深度升序排序
        var byDepth: Array = targetOrder.slice();
        byDepth.sort(function(a, b) {
            var da: Number = getGameObject(a).depth();
            var db: Number = getGameObject(b).depth();
            if (da < db) {
                return -1;
            }
            if (da > db) {
                return 1;
            }
            return 0;
        });

        var sortedDepths: Array = [];   // sortedDepths[i] = 初始第 i 小的深度值
        var owner: Object = {};         // depth → 当前拥有该深度的对象

        var i: Number;
        for (i = 0; i < byDepth.length; ++i) {
            var item = byDepth[i];
            var d: Number = getGameObject(item).depth();
            sortedDepths[i] = d;
            owner[d] = item;
        }

        // 逐位置对齐：Assign each target object its desired depth.
        for (i = 0; i < targetOrder.length; ++i) {
            var targetItem = targetOrder[i];
            var targetGO: GameObject = getGameObject(targetItem);
            var wantedDepth: Number = sortedDepths[i];

            if (targetGO.depth() == wantedDepth) {
                continue;  // 已在正确位置
            }

            var otherItem = owner[wantedDepth];
            var otherGO: GameObject = getGameObject(otherItem);

            var oldDepth: Number = targetGO.depth();

            targetGO.swapDepth(otherGO);

            // 更新映射：交换后 target 获得 wantedDepth，other 获得 oldDepth
            owner[wantedDepth] = targetItem;
            owner[oldDepth] = otherItem;
        }
    }

    private static function compareByIndex(getIndex: Function): Function {
        return function(a, b) {
            var ia: Number = getIndex(a);
            var ib: Number = getIndex(b);
            if (ia < ib) {
                return -1;
            }
            if (ia > ib) {
                return 1;
            }
            return 0;
        };
    }
}