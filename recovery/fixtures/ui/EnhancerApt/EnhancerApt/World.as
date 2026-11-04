import EnhancerApt.GameObject;
import EnhancerApt.Bind;

// 存放并管理游戏里所有的 GameObject
// 并在每帧调用各种需要的函数、运行游戏逻辑
class EnhancerApt.World {
    private var _movieClip: MovieClip;
    private var _width: Number;
    private var _height: Number;
    private var _updaters: Object;
    private var _lateUpdaters: Object;
    private var _gameObjects: Object;
    private var _occupiedDepths: Array;
    private var _freeDepths: Array;
    private var _nextId: Number;
    public var paused: Boolean;
    public var statistics: String;

    public function World(movieClip: MovieClip, width: Number, height: Number) {
        _movieClip = movieClip;
        _width = width;
        _height = height;
        _updaters = {};
        _lateUpdaters = {};
        _gameObjects = {};
        _occupiedDepths = [];
        _freeDepths = [];
        _nextId = 1;
        paused = false;
        statistics = "";
    }

    public function movieClip(): MovieClip { return _movieClip; }
    public function width(): Number { return _width; }
    public function height(): Number { return _height; }

    // 需要在每帧调用以执行各种游戏逻辑
    public function update(): Void {
        if (paused) {
            return;
        }
        var objectCount = 0;
        for (var k in _gameObjects) {
            _gameObjects[k].updateInitialPosition();
            ++objectCount;
        }

        var updaters = [];
        var lateUpdaters = [];
        for (var k in _updaters) {
            updaters.push({id: k, callback: _updaters[k]});
        }
        for (var k in _lateUpdaters) {
            lateUpdaters.push({id: k, callback: _lateUpdaters[k]});
        }

        var length = updaters.length;
        var lateUpdatersLength = lateUpdaters.length;
        for (var i = 0; i < length; ++i) {
            if (_updaters[updaters[i].id] === updaters[i].callback) {
                updaters[i].callback();
            }
        }
        for (var i = 0; i < lateUpdatersLength; ++i) {
            if (_lateUpdaters[lateUpdaters[i].id] === lateUpdaters[i].callback) {
                lateUpdaters[i].callback();
            }
        }
        statistics = "Objects: " + objectCount + ";\n";
        statistics += "Updaters: " + length + "; ";
        statistics += "Late updaters: " + lateUpdatersLength + ";\n";
        statistics += "Occupied depths: " + _occupiedDepths.length + "; ";
        statistics += "Free depths: " + _freeDepths.length + ";\n";
        statistics += "Next id: " + _nextId + ";"
    }

    // 创建一个新的 GameObject
    // movieClipId 是必须的参数，将会根据这个 id 来创建
    public function instantiate(movieClipId: String, depth: Number): GameObject {
        // 在 Flash 里，depth 不仅用于排列顺序，而且还起到了某种类似于 ID 的作用
        // 可是好多 depth 相关的 API 在 Apt 里都没有，因此需要手动管理一下（
        // 一般来说，在这个弹幕游戏里，是不太需要关心 depth 的顺序的
        // 但是至少要确保 depth 不能与现有的重复，但也不能耗尽地太快
        // 因为可用的 depth 范围貌似只有十万来着（没记错的话）
        if (isNaN(depth)) {
            if (_freeDepths.length > 0) {
                depth = Number(_freeDepths.shift());
            }
            else {
                depth = _occupiedDepths.length;
            }
        }
        if (_occupiedDepths[depth]) {
            destroy(_occupiedDepths[depth]);
        }
        trace("World instantiate " + String(_nextId) + " | " + String(depth));

        trace("World instantiate _movieClip " + _movieClip);

        var objectId = "i__" + (_nextId++);
        var sprite = _movieClip.attachMovie(movieClipId, objectId, depth);

        trace("World instantiate sprite " + sprite);

        var object = new GameObject(this, objectId, sprite);
        _gameObjects[object.id()] = object;
        _occupiedDepths[depth] = object;
        return object;
    }

    // 配合 GameObject.swapDepth()
    public function notifyDepthSwap(lhs: GameObject, rhs: GameObject) {
        _occupiedDepths[lhs.depth()] = lhs;
        _occupiedDepths[rhs.depth()] = rhs;
    }

    // 销毁一个 GameObject，它的所有组件也都会在销毁之前被卸载
    public function destroy(gameObject: GameObject): Void {
        if (!gameObject || gameObject.isDestroyed()) {
            return;
        }
        trace("World destroy");
        var sprite: MovieClip = gameObject.sprite();
        var depth = gameObject.depth();
        gameObject.markDestroyed();
        gameObject.removeAllComponents();
        delete _gameObjects[gameObject.id()];
        // APT 的动态对象 delete 可能留下属性；不要继续保留已销毁对象的强引用。
        if (_gameObjects[gameObject.id()] !== undefined) {
            var remainingObjects = {};
            for (var objectId in _gameObjects) {
                if (objectId != gameObject.id()) {
                    remainingObjects[objectId] = _gameObjects[objectId];
                }
            }
            _gameObjects = remainingObjects;
        }
        if (depth !== undefined) {
            delete _occupiedDepths[depth];
            _freeDepths.push(depth);
        }
        sprite.removeMovieClip();
        gameObject.releaseSprite();
    }

    public function destroyAll() {
        var objects = [];
        for (var k in _gameObjects) {
            objects.push(_gameObjects[k]);
        }
        var length = objects.length;
        for (var i = 0; i < length; ++i) {
            destroy(objects[i]);
        }
        _gameObjects = {};
        _updaters = {};
        _lateUpdaters = {};
        _occupiedDepths = [];
        _freeDepths = [];
    }

    // 在所有的 GameObject 里找出符合类型的 Component
    public function findComponents(type: Object): Array {
        var result = [];
        for (var k in _gameObjects) {
            var component = _gameObjects[k].getComponent(type);
            if (component) {
                result.push(component);
            }
        }
        return result;
    }

    // 添加一个普通的、每帧执行（指被 update 调用）的函数
    public function addOnFrameListener(id: String, f: Function): Void {
        _updaters[id] = f;
    }

    public function removeOnFrameListener(id: String): Void {
        delete _updaters[id];
    }

    // 添加一个普通的、在下一帧执行一次的函数
    public function onNextFrame(f: Function): String {
        var handlerId = "f__" + (_nextId++);
        addOnFrameListener(handlerId, Bind.noArg(this, function() {
            removeOnFrameListener(handlerId);
            f();
        }));
        return handlerId;
    }

    // 添加一个每帧执行的函数，
    // 不过，它会在所有“普通的函数”都被调用了之后才会被调用
    public function addAfterFrameListener(id: String, f: Function): Void {
        _lateUpdaters[id] = f;
    }

    public function removeAfterFrameListener(id: String): Void {
        delete _lateUpdaters[id];
    }

    // 添加一个在下一帧执行的函数
    // 不过，它会在所有“普通的函数”都被调用了之后才会被调用
    public function onAfterNextFrame(f: Function): String {
        var handlerId = "f__" + (_nextId++);
        addAfterFrameListener(handlerId, Bind.noArg(this, function() {
            removeAfterFrameListener(handlerId);
            f();
        }));
        return handlerId;
    }
}
