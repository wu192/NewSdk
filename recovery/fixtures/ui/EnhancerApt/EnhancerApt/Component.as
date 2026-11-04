import EnhancerApt.GameObject;
import EnhancerApt.World;
import EnhancerApt.Bind;

// 和 GameObject 一样，这边的 Component 也在试图抄袭 Unity 的 Component
// 虽然抄得不太像 23333
// 总之，一般都需要通过各种 Component 往 GameObject 添加逻辑
// 从 Component 派生的类，可以实现以下方法：
// - awake()，在 Component 被添加到 GameObject 的时候调用
// - start()，在 Component 被添加到 GameObject 之后的第一帧被调用
// - update()，每帧被调用
// - lateUpdate()，在所有的 update() 均被调用之后，再被调用
// start 与 update 对应 World 的 frameListeners
// 而 lateUpdate 则对应 World 的 afterFrameListener
class EnhancerApt.Component {
    private var _self: GameObject;
    private var _world: World;
    private var _componentId: String;
    private var _started: Boolean;

    // 当 Component 被添加到 GameObject 的时候，
    // 调用 awake() 并进行其他各种初始化
    public function onAdded(self: GameObject, id: Number): Void {
        if (_self) {
            throw new Error("Cannot add component to multiple GameObjects")
        }
        _self = self;
        _world = _self.world();
        _componentId = self.id() + "_c" + id;
        _started = false;

        // 假如用户重写了 start / update，
        // 那么就往 World 添加 frame listener 从而能够让 World 调用它们
        // 否则的话，就不加了，毕竟默认的 start 与 update 什么都不会做
        var hasStart = start !== Component.prototype.start;
        var hasUpdate = update !== Component.prototype.update;
        if (hasStart || hasUpdate) {
            var callUpdate = Bind.noArg(this, function() {
                if (!_self || _self.isDestroyed()) {
                    return;
                }
                if (!_started) {
                    _started = true;
                    start();
                    if (!_self || _self.isDestroyed()) {
                        return;
                    }
                    if (!hasUpdate) {
                        _world.removeOnFrameListener(_componentId);
                    }
                }
                update();
            });
            _world.addOnFrameListener(_componentId, callUpdate);
        }

        // 假如用户重写了 lateUpdate，
        // 就往 World 添加 after frame listener
        if (lateUpdate !== Component.prototype.lateUpdate) {
            var callLateUpdate = Bind.noArg(this, function() {
                if (!_self || _self.isDestroyed()) {
                    return;
                }
                lateUpdate();
            })
            _world.addAfterFrameListener(_componentId, callLateUpdate);
        }

        // 调用 awake()
        awake();
    }

    // 当 Component 被移除时，清理各种东西
    public function onRemoved(): Void {
        if (!_world) {
            return;
        }
        _self = undefined;
        _world.removeOnFrameListener(_componentId);
        _world.removeAfterFrameListener(_componentId);
        _world = undefined;
        _componentId = undefined;
        _started = false;
    }

    public function gameObject(): GameObject { return _self; }

    public function awake(): Void { }
    public function start(): Void { }
    public function update(): Void { }
    public function lateUpdate(): Void { }
}
