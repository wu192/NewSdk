import EnhancerApt.World;
import EnhancerApt.GameObject;
import EnhancerApt.Bind;

// 地图创建的所有根对象在同一个 World 中互为兄弟；Group 是业务标签，不是 Flash 深度。
// 交换已占用的深度，保持 World 分配器与原生输入法对象的有效性。
class EnhancerApt.MapUiLayers {
    private var _holder: GameObject;
    private var _world: World;
    private var _items: Object;
    private var _groups: Object;
    private var _sequence: Number;
    private var _savedSequence: Object;
    private var _modalKey: String;
    private var _popupKey: String;

    public function MapUiLayers(screen: MovieClip, world: World) {
        _holder = world.instantiate("CustomButtonLayer");
        _holder.sprite()._x = 0;
        _holder.sprite()._y = 0;
        _world = new World(_holder.sprite(), world.width(), world.height());
        _holder.sprite().onEnterFrame = Bind.noArg(_world, _world.update);
        _items = {};
        _modalKey = "";
        _popupKey = "";
        _groups = {};
        _savedSequence = {};
        _sequence = 1;
        screen.UISetGroupLayer = Bind.twoArg(this, function(group: String, layer: String) {
            this.group(Number(group)).layer = Number(layer);
            reorder();
        });
        screen.UIClearGroupLayer = Bind.oneArg(this, function(group: String) {
            this.group(Number(group)).layer = 0;
            reorder();
        });
        screen.UISortGroupByIndex = Bind.oneArg(this, function(group: String) {
            this.group(Number(group)).byIndex = true;
            reorder();
        });
        screen.UIGroupAboveGroup = Bind.twoArg(this, function(top: String, bottom: String) {
            if (top == bottom) {
                return;
            }
            var low = this.group(Number(bottom));
            var high = this.group(Number(top));
            high.layer = low.layer + 1;
            reorder();
        });
        screen.UIResetGroupLayers = Bind.noArg(this, function() {
            _groups = {};
            _savedSequence = {};
            _sequence = 1;
            // HUD 可能跨地图存活；各管理器移除地图对象前，先按原有稳定顺序重建分组顺序。
            var list = entries();
            list.sort(function(a, b) {
                return a.sequence - b.sequence;
            });
            for (var i = 0; i < list.length; i++) {
                list[i].sequence = _sequence++;
                _savedSequence[list[i].key] = list[i].sequence;
                this.group(list[i].group);
            }
            reorder();
        });
        screen.UIGroupLayersBuild = Bind.noArg(this, function() {
            fscommand("CallGameFunction", "%MapUi?layers|shared-map-roots-v1");
        });
    }
    // 原生 APT 在管理器调用点将 world() 解析为 undefined；
    // 使用独立名称的 getter，仍返回同一个有效的 _world。
    public function sharedWorld(): World {
        return _world;
    }
    public function holder(): GameObject {
        return _holder;
    }
    public function ensureAbove(other: GameObject): Void {
        if (_holder.depth() < other.depth()) {
            _holder.swapDepth(other);
        }
    }
    private function group(index: Number): Object {
        if (!_groups[index]) {
            _groups[index] = {layer: 0, sequence: _sequence++, byIndex: false};
        }
        return _groups[index];
    }
    public function register(key: String, object: GameObject, groupIndex: Number, index: Number, hit: Function): Void {
        if (isNaN(groupIndex)) {
            groupIndex = 0;
        }
        group(groupIndex);
        if (_savedSequence[key] == undefined) {
            _savedSequence[key] = _sequence++;
        }
        _items[key] =
            {key: key, object: object, group: groupIndex, index: index, sequence: _savedSequence[key], hit: hit};
        reorder();
    }
    public function remove(key: String, keepSequence: Boolean): Void {
        delete _items[key];
        if (!keepSequence) {
            delete _savedSequence[key];
        }
    }
    public function setGroup(key: String, index: Number): Void {
        var item = _items[key];
        if (!item) {
            return;
        }
        if (isNaN(index)) {
            index = 0;
        }
        group(index);
        item.group = index;
        reorder();
    }
    private function entries(): Array {
        var items = [];
        for (var key in _items) {
            var item = _items[key];
            if (item.object && !item.object.isDestroyed()) {
                items.push(item);
            }
        }
        return items;
    }
    public function setPopup(key: String): Void {
        _popupKey = key;
        reorder();
    }
    public function setModal(key: String): Void {
        _modalKey = key;
        reorder();
    }
    public function reorder(): Void {
        var items = entries();
        var groups = _groups;
        var modalKey = _modalKey;
        var popupKey = _popupKey;
        // Layer 相同时按分组创建顺序稳定排列；组内默认按创建顺序，
        // 只有显式启用旧式 Index 排序才采用对应索引。
        items.sort(function(a, b) {
            if ((a.key == popupKey) != (b.key == popupKey)) {
                return a.key == popupKey ? 1 : -1;
            }
            if ((a.key == modalKey) != (b.key == modalKey)) {
                return a.key == modalKey ? 1 : -1;
            }
            var ga = groups[a.group];
            var gb = groups[b.group];
            if (ga.layer != gb.layer) {
                return ga.layer - gb.layer;
            }
            if (ga.sequence != gb.sequence) {
                return ga.sequence - gb.sequence;
            }
            if (ga.byIndex && a.index != b.index) {
                return a.index - b.index;
            }
            return a.sequence - b.sequence;
        });
        for (var i = 0; i < items.length; i++) {
            for (var j = i + 1; j < items.length; j++) {
                if (items[i].object.depth() > items[j].object.depth()) {
                    items[i].object.swapDepth(items[j].object);
                }
            }
        }
    }
    // 此查询仅用于释放焦点；真正点击仍由原生 APT Button 路由，
    // 其命中次序与渲染使用相同的兄弟深度。
    public function frontAt(x: Number, y: Number): String {
        var front;
        var items = entries();
        for (var i = 0; i < items.length; i++) {
            var item = items[i];
            if (item.hit && item.hit(x, y) && (!front || item.object.depth() > front.object.depth())) {
                front = item;
            }
        }
        return front ? front.key : "";
    }
}
