import EnhancerApt.Component;
import EnhancerApt.World;

// 试图抄袭 Unity 的 GameObject，虽然抄得不太像（
// 但是，与 Unity 类似，需要往 GameObject 添加各种 Component 来实现各种功能。
// 与 Unity 的区别较大的是，这边的 GameObject 直接提供了部分 Transform 的功能，
// 而且不支持子物体。
class EnhancerApt.GameObject {
    private var _world: World;
    private var _id: String;
    private var _sprite: MovieClip;
    private var _destroyed: Boolean;
    private var _initialX: Number;          // 用于在每一帧存储“上一次”的坐标
    private var _initialY: Number;          // 见 updateInitialPosition()
    private var _depth: Number;
    private var _components: Array;         // 储存各个组件，方便查询。
    private var _nextComponentId: Number;   // 实际上组件是由 World 来调用的

    public function GameObject(world: World, id: String, sprite: MovieClip) {
        _world = world;
        _id = id;
        _sprite = sprite;
        _destroyed = false;
        _components = [];
        _nextComponentId = 0;
        _depth = sprite.getDepth();
        updateInitialPosition();
    }

    public function isDestroyed(): Boolean {
        return _destroyed || !_sprite;
    }
    // World 在调用用户或组件清理之前先标记失效，防止清理过程重入仍访问已销毁对象。
    public function markDestroyed(): Void { _destroyed = true; }
    public function releaseSprite(): Void { _sprite = undefined; }
    public function world(): World { return _world; }
    public function id(): String { return _id; }
    public function sprite(): MovieClip { return _sprite; }
    public function depth(): Number { return _depth; }

    public function getPosition(): Object {
        return { x: _sprite._x, y: _sprite._y };
    }
    public function setPosition(v: Object): Void {
        _sprite._x = v.x;
        _sprite._y = v.y;
    }

    // 这个方法由 World 在每帧调用，用来更新所谓的“上一次”的坐标。
    // 上一次的坐标通过 previousPosition() 提供。
    public function updateInitialPosition(): Void {
        _initialX = _sprite._x;
        _initialY = _sprite._y;
    }

    // 获取“上一次”的坐标。
    // 配合 GameObject 的当前坐标，可以计算出 GameObject 的“速度”。
    // 不过，由于组件一般会在 update() 里更新坐标，
    // 因此最好在组件的 lateUpdate() 里再获取最新坐标，
    // lateUpdate() 保证在所有组件的 update() 被调用之后才被调用。
    public function previousPosition(): Object {
        return { x: _initialX, y: _initialY };
    }

    // 包装 Flash 的 swapDepth 方法，以便能适配 GameObject
    public function swapDepth(other: GameObject): Void {
        if (isDestroyed() || other.isDestroyed()) {
            return;
        }
        _sprite.swapDepths(other._sprite);
        _depth = _sprite.getDepth();
        other._depth = other._sprite.getDepth();
        _world.notifyDepthSwap(this, other);
    }

    public function direction(other: GameObject): Object {
        return {
            x: other._sprite._x - _sprite._x,
            y: other._sprite._y - _sprite._y
        };
    }

    public function distance(other: GameObject): Number {
        var dx = other._sprite._x - _sprite._x;
        var dy = other._sprite._y - _sprite._y;
        return Math.sqrt(dx * dx + dy * dy);
    }

    // 添加一个组件。与 Unity 一样，同一类型的组件只能添加一个。
    public function addComponent(c: Component) {
        if (isDestroyed()) {
            throw new Error("GameObject already destroyed");
        }
        if (getComponent(c) !== undefined) {
            throw new Error("Component already added");
        }
        ++_nextComponentId;
        _components.push(c);
        c.onAdded(this, _nextComponentId)
        return c;
    }

    // 尝试获取一个组件。可以直接传构造函数（或者说，“类型”？）、
    // 根据类型来获取组件。
    // 优先匹配类型完全符合的组件。
    // 但假如前者没找到，也可以通过基类获取派生类。
    public function getComponent(type: Object) {
        var candidate = undefined;
        var length = _components.length;
        for (var i = 0; i < length; ++i) {
            var component = _components[i];
            if (instanceOf(component, type)) {
                candidate = component;
                if (instanceOf(type, component)) {
                    return component;
                }
            }
        }
        return candidate;
    }

    // 尝试移除一个组件，查询方式同 getComponent
    public function removeComponent(type: Object): Void {
        var candidateIndex = undefined;
        var length = _components.length;
        for (var i = 0; i < length; ++i) {
            var component = _components[i];
            if (instanceOf(component, type)) {
                candidateIndex = i;
                if (instanceOf(type, component)) {
                    break;
                }
            }
        }
        if (candidateIndex !== undefined) {
            _components.splice(candidateIndex, 1)[0].onRemoved();
        }
    }

    public function removeAllComponents(): Void {
        var components = _components;
        _components = [];
        var length = components.length;
        for (var i = 0; i < length; ++i) {
            components[i].onRemoved();
        }
    }

    // 由于老版本 Flash（以及 Apt）的 instanceof 有问题
    // 因此只好自己写一个（
    // 参数既可以是实例，也可以是构造函数
    // 以后应该把这个东西作为一个工具函数，
    // 丢到 utilities 里面供所有代码使用
    // 不过重构好麻烦呀……
    private static function instanceOf(self, check): Boolean {
        self = typeof self === 'function'
            ? self.prototype
            : self.__proto__;
        check = typeof check === 'function'
            ? check.prototype
            : check.__proto__;
        while (self) {
            if (self === check) {
                return true;
            }
            self = self.__proto__;
        }
        return false;
    }
}
