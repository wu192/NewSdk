import EnhancerApt.Bind;
import EnhancerApt.CommonBtn;
import EnhancerApt.World;
import EnhancerApt.GameObject;

class EnhancerApt.LongTextDialogManger {

    private var _longText: TextField;
    private var _longTextReferenceHeight: Number;
    private var _textHolder: MovieClip;
    private var _textMask: MovieClip;
    private var _btn1: CommonBtn;
    private var _btn2: CommonBtn;
    private var _btn3: CommonBtn;
    private var _dialog: GameObject;
    private var _mask: MovieClip;

    private var _scrollBar: MovieClip;
    private var _trackHeight: Number;
    private var _thumbX: Number;
    private var _thumbMinY: Number;
    private var _thumbMaxY: Number;

    public function LongTextDialogManger(screen: MovieClip, _world: World) {
        _dialog = _world.instantiate("LongTextDialog");
        _dialog.setPosition({ x : 303, y: 128});

        var textAndMaskHolder: MovieClip = _dialog.sprite().textAndMaskHolder;
        _textHolder = textAndMaskHolder.textMc;
        _textMask = textAndMaskHolder.mask;
        _longText = _textHolder.longtext;
        _longText.autoSize = "left";
        _longText.wordWrap = true;
        _longTextReferenceHeight = _textMask._height;
        _btn1 = new CommonBtn(_dialog.sprite().btn1);
        _btn2 = new CommonBtn(_dialog.sprite().btn2);
        _btn3 = new CommonBtn(_dialog.sprite().btn3);
        _mask = screen.mask;

        // _longText.html = true;

        screen.toggleLongTextDialog = Bind.oneArg(this, function(visibility: Number) {
            trace("toggleLongTextDialog " + visibility);
            if (visibility == 1) {
                this.doToggleLongTextDialog(true);
                this.prepareUI();
            } else {
                this.doToggleLongTextDialog(false);
            }
        });

        _btn1.setHandler(Bind.noArg(this, function() {
            fscommand("CallGameFunction", "%EnhancerApt_LongTextDialog_OnClick_1");
            this.doToggleLongTextDialog(false)
        }));
        _btn2.setHandler(Bind.noArg(this, function() {
            fscommand("CallGameFunction", "%EnhancerApt_LongTextDialog_OnClick_2");
            this.doToggleLongTextDialog(false)
        }));
        _btn3.setHandler(Bind.noArg(this, function() {
            fscommand("CallGameFunction", "%EnhancerApt_LongTextDialog_OnClick_3");
            this.doToggleLongTextDialog(false)
        }));

        _scrollBar = _dialog.sprite().scrollbar;
        var btnUp: MovieClip = _scrollBar.btnUp;
        var btnDown: MovieClip = _scrollBar.btnDown;
        var thumb: MovieClip = _scrollBar.thumb;
        // 根据实际轨道尺寸计算滑块移动范围 (根据你的美术微调数值)
        _trackHeight = _scrollBar._height; // 轨道总高
        var btnSpace: Number = btnUp._height + btnDown._height; // 上下按钮占据的总高度
        _thumbX = thumb._x; // 滑块的固定X位置
        _thumbMinY = btnUp._height;
        _thumbMaxY = _trackHeight - btnDown._height - thumb._height;

        btnUp.onPress = Bind.noArg(this, function() {
            scrollContent(10);   // 每次向上滚动10像素，你可以根据需要调整这个值
            updateThumb();
        });
        btnDown.onPress = Bind.noArg(this, function() {
            scrollContent(-10);    // 每次向下滚动10像素
            updateThumb();
        });
        // 滑块拖动
        setupThumbDrag();
        // 初始化滑块位置
        updateThumb();
        // 默认隐藏
        _scrollBar._visible = false;

        screen.toggleLongTextDialog(0);
    }

    private function doToggleLongTextDialog(visibility: Boolean) {
        _dialog.sprite()._visible = visibility;
        _mask._visible = visibility;
    }

    private function prepareUI() {
        var longTextDialogUI = {};
        // longTextDialogUI.longText = "<b>This is bold text</b><br><i>This is italic text</i>";
        // longTextDialogUI.btn1 = "Button1";
        // longTextDialogUI.btn2 = "Button2";
        // longTextDialogUI.btn3 = "Button3";

        loadVariables("EnhancerApt_LongTextDialog_UI", longTextDialogUI);
        _longText._y = 0; // 重置文本位置
        _longText.text = longTextDialogUI.longText;

        // var text = "";
        // for (var i = 0; i < 20; ++i) {
        //     text += "Line " + i + "\n";
        // }
        // _longText.text = text;   // 测试用长文本
        // longTextDialogUI.btn1 = "Button1";

        updateThumb();

        if (longTextDialogUI.btn1) {
            _btn1.setVisibility(true);
            _btn1.setText(longTextDialogUI.btn1);
        } else {
            _btn1.setVisibility(false);
        }

        if (longTextDialogUI.btn2) {
            _btn2.setVisibility(true);
            _btn2.setText(longTextDialogUI.btn2);
        } else {
            _btn2.setVisibility(false);
        }

        if (longTextDialogUI.btn3) {
            _btn3.setVisibility(true);
            _btn3.setText(longTextDialogUI.btn3);
        } else {
            _btn3.setVisibility(false);
        }
    }

    // 核心：移动文本字段，并更新滑块
    public function scrollContent(deltaY: Number): Void {
        var contentHeight: Number = _longText._height;
        if (contentHeight <= _longTextReferenceHeight) return;   // 内容不足，不滚动

        // 当前 _y 范围：0 (顶部) 到 -(contentHeight - _longTextReferenceHeight) (底部)
        var minY: Number = 0;
        var maxY: Number = _longTextReferenceHeight - contentHeight; // 负数
        // 计算新位置并限制在范围内
        var newY: Number = _longText._y + deltaY;
        if (newY > minY) newY = minY;
        if (newY < maxY) newY = maxY;
        _longText._y = newY;

        updateThumb();
    }

    // 更新滑块在轨道上的位置（根据 _y 映射）
    private function updateThumb(): Void {
        var needScroll: Boolean = (_longText._height > _longTextReferenceHeight);
        _scrollBar._visible = needScroll;
        if (needScroll) {
            var maxMove: Number = _longTextReferenceHeight - _longText._height; // <=0
            var ratio: Number = (_longText._y - 0) / (maxMove - 0); // 0 到 1
            _scrollBar.thumb._y = _thumbMinY + ratio * (_thumbMaxY - _thumbMinY);
        }
    }

    // 拖拽滑块反向控制文本位置
    private function setupThumbDrag(): Void {
        var thumb: MovieClip = _scrollBar.thumb;
        thumb.onPress = Bind.noArg(this, function(): Void {
            // 保存初始鼠标位置和滑块位置
            var mouseStartY: Number = _root._ymouse;          // 全局鼠标 Y
            var thumbStartY: Number = thumb._y;               // 滑块初始 Y (相对其父容器)

            // 定义鼠标移动处理函数
            var onMouseMoveHandler: Function = Bind.noArg(this, function(): Void {
                var deltaY: Number = _root._ymouse - mouseStartY;
                var newY: Number = thumbStartY + deltaY;
                // 限制在轨道内
                if (newY < _thumbMinY) newY = _thumbMinY;
                if (newY > _thumbMaxY) newY = _thumbMaxY;
                thumb._y = newY;
                // 同步文本字段位置 (可选：如果你想即时跟随，但通常只释放时更新)
                // 这里仅在释放时更新文本字段，以减少重绘开销
                // updateAfterEvent();   // 如果需要流畅显示
            });

            // 将处理函数存储在 thumb 上以便移除
            thumb.onMouseMove = onMouseMoveHandler;
        });

        thumb.onRelease = Bind.noArg(this, function(): Void {
            // 移除鼠标移动监听
            thumb.onMouseMove = null;
            delete thumb.onMouseMove;
            // 根据滑块位置改变文本字段的 _y
            applyThumbPositionToTextField();
        });

        thumb.onReleaseOutside = Bind.noArg(this, function(): Void {
            thumb.onMouseMove = null;
            delete thumb.onMouseMove;
            applyThumbPositionToTextField();
        });
    }

    // 新增：根据滑块位置设置文本字段 _y
    private function applyThumbPositionToTextField(): Void {
        var thumb: MovieClip = _scrollBar.thumb;
        var pos: Number = thumb._y - _thumbMinY;
        var range: Number = _thumbMaxY - _thumbMinY;
        if (range > 0 && _longText._height > _longTextReferenceHeight) {
            var ratio: Number = pos / range;
            var maxMove: Number = _longTextReferenceHeight - _longText._height;  // 负数或0
            _longText._y = ratio * maxMove;                       // 0 到 maxMove
        }
        updateThumb();   // 修正因取整或手动拖动导致的微小偏差
    }
}