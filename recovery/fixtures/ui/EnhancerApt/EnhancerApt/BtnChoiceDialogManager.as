import EnhancerApt.Bind;
import EnhancerApt.CommonBtn;
import EnhancerApt.World;
import EnhancerApt.GameObject;

class EnhancerApt.BtnChoiceDialogManager {

    private var _dialog: GameObject;
    private var _btnDialogBtns: Array;
    private var _mask: MovieClip;

    public function BtnChoiceDialogManager(screen: MovieClip, _world: World) {
        _dialog = _world.instantiate("BtnChoiceDialog");
        _dialog.setPosition({ x : 485, y: 64});

        _btnDialogBtns = [];
        _btnDialogBtns.push(new CommonBtn(_dialog.sprite().btn1));
        _btnDialogBtns.push(new CommonBtn(_dialog.sprite().btn2));
        _btnDialogBtns.push(new CommonBtn(_dialog.sprite().btn3));
        _btnDialogBtns.push(new CommonBtn(_dialog.sprite().btn4));
        _btnDialogBtns.push(new CommonBtn(_dialog.sprite().btn5));
        _btnDialogBtns.push(new CommonBtn(_dialog.sprite().btn6));
        _btnDialogBtns.push(new CommonBtn(_dialog.sprite().btn7));

        _mask = screen.mask

        screen.toggleCustomBtnDialog = Bind.oneArg(this, function(visibility: Number) {
            if (visibility == 1) {
                this.doToggleDialog(true);
                this.prepareUI();
            } else {
                this.doToggleDialog(false);
            }
        });

        for (var i = 0;i < _btnDialogBtns.length;i++) {
            _btnDialogBtns[i].setHandler(Bind.noArgCapture1(this, i, function(index) {
                fscommand("CallGameFunction", "%EnhancerApt_CustomBtnDialog_OnClick_" + String(index + 1));
                this.doToggleDialog(false)
            }));
        }

        screen.toggleCustomBtnDialog(0);
    }

    private function doToggleDialog(visibility: Boolean) {
        _dialog.sprite()._visible = visibility;
        _mask._visible = visibility;
    }

    private function prepareUI() {
        var btnDialogUI = {};
        loadVariables("EnhancerApt_CustomBtnDialog_UI", btnDialogUI);
        var btnTexts = btnDialogUI.btnTexts.split(",");
        var btnNum = btnTexts.length;

        _dialog.sprite().btnDialogTitle._visible = false;

        for (var i = 0;i < _btnDialogBtns.length;i++) {
            _btnDialogBtns[i].setVisibility(false);
        }

        if (btnNum > 0) {
            _mask._visible = true;

            if (btnDialogUI.title.length > 0) {
                _dialog.sprite().btnDialogTitle._visible = true;
                _dialog.sprite().btnDialogTitle.text = btnDialogUI.title;
            }

            for (var i = 0;i < _btnDialogBtns.length && i < btnNum;i++) {
                if (btnTexts[i].length == 0) {
                    break;
                }
                _btnDialogBtns[i].setVisibility(true);
                _btnDialogBtns[i].setText(btnTexts[i]);
            }


        } else {
            this.doToggleDialog(false)
        }
    }

}