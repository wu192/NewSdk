import EnhancerApt.Bind;
import EnhancerApt.CommonBtn;
import EnhancerApt.World;
import EnhancerApt.GameObject;

class EnhancerApt.CenterTopBtnsManager {

    private var _container: MovieClip;
    private var _btn1: MovieClip;
    private var _btn2: MovieClip;
    private var _btn3: MovieClip;
    private var _btn4: MovieClip;
    private var _btn5: MovieClip;
    private var _btns: Object;
    private var _world: World;

    private var _contextMenu: MovieClip;

    private var _delayInitId: Object;

    public function CenterTopBtnsManager(screen: MovieClip, world: World) {
        _world = world;
        // _container = screen.createEmptyMovieClip("TestEAButton1", 1101);
        _container = _world.instantiate("ScreenCenterTopBtn").sprite();
        _container._x = 350;
        _container._y = 0;
        _btns = {}
        _delayInitId = {};
        _container._visible = false;

        _contextMenu = _world.instantiate("ScreenCenterTopBtnContext").sprite();
        _contextMenu._x = -400;
        _contextMenu._y = -400;

        screen.CenterTopBtnShowContextMenu = Bind.twoArg(this, function(index: Number, content: String) {
            trace("CenterTopBtnShowContextMenu " + String(index) + " " + content);
            _contextMenu._x = 350 + (index - 1) * 60;
            _contextMenu._y = 70;
            _contextMenu.tv.text = content;
        });

        screen.CenterTopBtnHideContextMenu = Bind.twoArg(this, function(index: Number, content: String) {
            _contextMenu._x = -400;
            _contextMenu._y = -400;
            _contextMenu.tv.text = "";
        });

        // EAStandardBtn

        screen.toggleCenterTopBtnsContainerVisibility = Bind.twoArg(this, function(visible: Number) {
            if (visible == 1) {
                _container._visible = true;
                switch (_root.currentFaction) {
                    case "Allies":
                        _container.gotoAndStop("Allies");
                        break;
                    case "Japan":
                        _container.gotoAndStop("Empire");
                        break;
                    case "Soviet":
                        _container.gotoAndStop("Soviet");
                        break;
                    case "Celestial":
                        _container.gotoAndStop("Celestial");
                        break;
                    default:
                        _container.gotoAndStop("Celestial");
                        break;
                }
            } else {
                _container._visible = false;
            }
        });

        screen.updateCenterTopBtnImage = Bind.twoArg(this, function(index: Number) {
            if (_btns[index]) {
                this.destroyCenterTopButton(index);
            }

            var btn = _container.createEmptyMovieClip("EAStandardButton" + String(index), 100 + index);
            btn.loadMovie("StandardCommandButton.swf");
            btn._x = 15 + (index - 1) * 60;
            btn._y = 15;

            //TODO override original apt to setEnable?
            _delayInitId[index] = setInterval(Bind.noArgCapture1(this, index, function(idx) {
                clearInterval(_delayInitId[idx]);
                _delayInitId[idx] = undefined;
                delete _delayInitId[idx];
                _btns[idx].SetEnabled(true);
            }), 50)

            _btns[index] = btn;

            var addr = _global.Pointer.encode(btn);
            fscommand("CallGameFunction", "%loadTestImage" + addr + "," + String(index));
        });

        screen.loadCenterTopBtnImage = Bind.twoArg(this, function(index: Number) {
            var addr = _global.Pointer.encode(_btns[index - 1]);
            // fscommand("CallGameFunction", "%loadTestImage" + addr);
        });

        screen.CenterTopBtnCenterTopBtnToggleVisibility = Bind.twoArg(this, function(index: Number, visibility: Number) {
            if (visibility == 1) {
                _btns[index]._visible = true;
            } else {
                _btns[index]._visible = false;
            }
        });

        screen.destroyAllCenterTopBtns = Bind.twoArg(this, function(a: String) {
            for (var prop:String in _btns) {
                this.destroyCenterTopButton(prop);
            }
            _container._visible = false;

            _contextMenu._x = -400;
            _contextMenu._y = -400;
            _contextMenu.tv.text = "";
        });

        //////////

        screen.CenterTopBtnToggleHighLight = Bind.twoArg(this, function(index: Number, enable: Number) {
            _btns[index].SetHighlighted(enable);
        });

        screen.CenterTopBtnSetTimerOverlayValue = Bind.twoArg(this, function(index: Number, value: Number) {
            _btns[index].SetTimerValue(value);
        });

        screen.CenterTopBtnToggleTimerOverlay = Bind.twoArg(this, function(index: Number, visible: Number) {
            if (visible == 1) {
                _btns[index].SetTimerVisibility(1);
            } else {
                _btns[index].SetTimerVisibility(0);
            }
        });

        screen.CenterTopBtnSetHealthBarValue = Bind.twoArg(this, function(index: Number, value: Number) {
            _btns[index].SetHealthBarValue(value);
        });

        screen.CenterTopBtnToggleHealthBar = Bind.twoArg(this, function(index: Number, visible: Number) {
            if (visible == 1) {
                _btns[index].SetHealthBarVisibility(1);
            } else {
                _btns[index].SetHealthBarVisibility(0);
            }
        });

        screen.CenterTopBtnToggleSelected = Bind.twoArg(this, function(index: Number, selected: Number) {
            if (selected == 1) {
                _btns[index].SetSelected(1);
            } else {
                _btns[index].SetSelected(0);
            }
        });

        screen.CenterTopBtnToggleEnable = Bind.twoArg(this, function(index: Number, enable: Number) {
            if (enable == 1) {
                _btns[index].SetEnabled(1);
            } else {
                _btns[index].SetEnabled(0);
            }
        });

    }

    private function destroyCenterTopButton(index: Number) {
        var delayInitId: Number = _delayInitId[index];
        if (delayInitId != undefined) {
            clearInterval(delayInitId);
            _delayInitId[index] = undefined;
            delete _delayInitId[index];
        }
        var button: MovieClip = _btns[index];
        if (button) {
            button.unloadMovie();
            button.removeMovieClip();
        }
        _btns[index] = undefined;
        delete _btns[index];
    }

}