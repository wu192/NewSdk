import EnhancerApt.Bind;
import EnhancerApt.GameObject;

class EnhancerApt.UnitTopBtn {

    private var _gameObj: GameObject;
    private var _btn: MovieClip;
    private var _hitRegion: Button;
    private var _overlay: MovieClip;
    private var _handler: Function;
    private var _customImage: Boolean;
    private var _width: Number;
    private var _height: Number;

    public function UnitTopBtn(btn: GameObject, width: Number, height: Number) {
        trace("BtnDialogChoiceBtn init")

        _gameObj = btn;
        _btn = btn.sprite();
        _customImage = !isNaN(width) && !isNaN(height);

        if (_customImage) {
            this.initCustomImage(width, height);
        } else {
            this.initDefaultButton();
        }
    }

    private function initDefaultButton(): Void {
        _btn.gotoAndStop("normal");
        _hitRegion = _btn.hitregion;

        _hitRegion.onRollOver = bind(function() {
            _btn.gotoAndStop("hover");
        });
        _hitRegion.onPress = bind(function() {
            _btn.gotoAndStop("pressed");
        });
        _hitRegion.onRollOut = bind(function() {
            _btn.gotoAndStop("normal");
        });
    }

    private function initCustomImage(width: Number, height: Number): Void {
        _width = width;
        _height = height;

        _btn.image._width = width;
        _btn.image._height = height;
        if (_btn.disabledImage) {
            _btn.disabledImage._width = width;
            _btn.disabledImage._height = height;
        }

        _hitRegion = _btn.hitRegion;
        _overlay = _btn.overlay;
        _hitRegion._width = width;
        _hitRegion._height = height;
        _overlay._width = width;
        _overlay._height = height;
        _overlay._visible = false;

        _hitRegion.onRollOver = bind(function() {
            this.showOverlay(0xFFFFFF);
        });
        _hitRegion.onPress = bind(function() {
            this.showOverlay(0x000000);
        });
        _hitRegion.onRollOut = _hitRegion.onDragOut = bind(function() {
            _overlay._visible = false;
        });
        _hitRegion.onRelease = bind(function() {
            _overlay._visible = false;
            if (_handler) {
                _handler();
            }
        });
        _hitRegion.onReleaseOutside = bind(function() {
            _overlay._visible = false;
        });
    }

    private function showOverlay(rgb: Number): Void {
        _overlay._visible = true;
        var color: Color = new Color(_overlay);
        color.setRGB(rgb);
        _overlay._alpha = 25;
    }

    public function setHandler(f: Function): Void {
        _handler = f;
        if (!_customImage) {
            _hitRegion.onRelease = f;
        }
    }

    public function setPosition(v: Object): Void {
        if (_customImage) {
            _gameObj.setPosition({x: v.x - _width / 2, y: v.y - _height / 2});
        } else {
            _gameObj.setPosition(v);
        }
    }

    public function gameObj() {
        return _gameObj;
    }

    private function bind(f: Function): Function {
        var self = this;
        return function() { f.call(self); };
    }

}
