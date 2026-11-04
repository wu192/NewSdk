import EnhancerApt.Bind;

class EnhancerApt.CommonBtn {

    private var _btn: MovieClip;

    public function CommonBtn(btn: MovieClip) {
        trace("BtnDialogChoiceBtn init")

        _btn = btn;

        _btn.gotoAndStop("normal");

        _btn.onRollOver = bind(function() {
            _btn.gotoAndStop("hover");
        });
        _btn.onPress = bind(function() {
            _btn.gotoAndStop("pressed");
        });
        _btn.onRollOut = bind(function() {
            _btn.gotoAndStop("normal");
        });
    }

    public function setHandler(f: Function): Void {
        _btn.onRelease = f;
    }

    public function setText(t: String): Void {
        _btn.tv.text = t;
    }

    public function setVisibility(visibility: Boolean): Void {
        _btn._visible = visibility;
    }

    private function bind(f: Function): Function {
        var self = this;
        return function() { f.call(self); };
    }

}