import EnhancerApt.World;
import EnhancerApt.GameObject;
import EnhancerApt.Bind;
import EnhancerApt.MapUiTheme;
import EnhancerApt.MapUiLayers;
import EnhancerApt.MapUiMotion;
import EnhancerApt.MapUiControlPainter;
import EnhancerApt.MapUiOptions;
import EnhancerApt.MapUiStylePainter;

// 框架内部适配层；地图 API 不向调用者暴露 MovieClip 或原生控件 ID。
class EnhancerApt.MapUiManager {
    private var _layers: MapUiLayers;
    private var _world: World;
    private var _holder: GameObject;
    private var _screen: MovieClip;
    private var _nodes: Object;
    private var _focused: String;
    private var _batch: Boolean;
    private var _dirtyStyles: Object;
    private var _sceneEpoch: String;
    private var _sceneActive: Boolean;
    private var _incrementalTemplates: Boolean;
    private var _originX: Number;
    private var _originY: Number;
    private var _order: Array;
    private var _alive: Boolean;
    private var _templatesChecked: Boolean;
    private var _templatesValid: Boolean;
    private var _templatePlain: GameObject;
    private var _templateField: GameObject;
    private var _templateFrames: Number;
    private var _theme: MapUiTheme;
    private var _motion: Object;
    private var _visibilityMotion: Object;
    private var _motionTime: Number;
    private var _modal: String;
    private var _systemPageActive: Boolean;
    private var _popupOwner: String;
    private var _popupStart: Number;
    private var _tooltipOwner: String;
    private var _tooltipAt: Number;
    // 指针事件状态：同一时刻只允许一个节点持有捕获；拖动只改精灵位置做本地反馈，
    // 声明位置始终由地图持有，结束时通过偏移让地图自行更新。
    private var _pointerCapture: String;
    private var _dragActive: Boolean;
    private var _dragOriginX: Number;
    private var _dragOriginY: Number;
    private var _grabX: Number;
    private var _grabY: Number;
    private var _dragOffsetX: Number;
    private var _dragOffsetY: Number;
    private var _dragThreshold: Number;

    public function MapUiManager(screen: MovieClip, world: World, layers: MapUiLayers) {
        _layers = layers;
        _holder = layers.holder();
        _screen = screen;
        _world = layers.sharedWorld();
        _nodes = {};
        _focused = "";
        _systemPageActive = false;
        _batch = false;
        _dirtyStyles = {};
        _sceneEpoch = "";
        _sceneActive = true;
        _incrementalTemplates = false;
        _originX = 0;
        _originY = 0;
        _order = [];
        _alive = true;
        _templatesChecked = false;
        _templatesValid = false;
        _templateFrames = 0;
        _theme = new MapUiTheme();
        _motion = new MapUiMotion();
        _visibilityMotion = new MapUiMotion();
        _motionTime = getTimer();
        _modal = "0";
        _popupOwner = "";
        _popupStart = 0;
        _tooltipOwner = "";
        _tooltipAt = 0;
        _pointerCapture = "";
        _dragActive = false;
        _dragOriginX = 0;
        _dragOriginY = 0;
        _grabX = 0;
        _grabY = 0;
        _dragOffsetX = 0;
        _dragOffsetY = 0;
        // 4 像素位移阈值：小于该距离视为点击，不进入拖动。
        _dragThreshold = 4;
        _world.addOnFrameListener("map_ui_inputs", Bind.noArg(this, poll));
        _holder.sprite().onUnload = Bind.noArg(this, dispose);
        _holder.sprite().onMouseDown = Bind.noArg(this, function() {
            if (!_alive) {
                return;
            }
            var node = _nodes[_focused];
            if (node == undefined || node.token == undefined) {
                return;
            }
            if (!isLive(node)) {
                return;
            }
            var mc = _holder.sprite();
            var frontRoot = _layers.frontAt(mc._xmouse, mc._ymouse);
            var front = frontRoot.substr(0, 7) == "widget:" ?
                _nodes[frontInRoot(frontRoot.substr(7), mc._xmouse, mc._ymouse)] : undefined;
            // 页签、菜单及弹出选项共享拥有者的键盘焦点；点击这些内部行不算移出控件。
            if (front == undefined || (front.token != node.token && front.internalOwner != node.token)) {
                emit("blur", node.token);
            }
        });
        screen.MapUiHello = Bind.noArg(this, function() {
            // 原生 APT 可能到后续渲染帧才填入 attachMovie 的第 0 帧子对象。
            // Hello 读取已完成的探测，不把首次查找失败永久缓存，也不重复创建探测对象。
            // 先报告扩展能力，最后仍以原有 ready 结束握手，兼容原有探测调用方。
            fscommand("CallGameFunction", "%MapUi?pointerReady|1");
            fscommand("CallGameFunction", "%MapUi?incrementalReady|" + (_incrementalTemplates ? "1" : "0"));
            fscommand("CallGameFunction", "%MapUi?ready|" + (_templatesChecked && _templatesValid ? "2" : "0"));
        });
        screen.MapUiModal = Bind.oneArg(this, function(token: String) {
            _modal = token;
            _layers.setModal(token == "0" ? "" : "widget:" + token);
            for (var key in _nodes) {
                var n = _nodes[key];
                if (n.internalOwner != undefined) {
                    continue;
                }
                var shield = n.object.sprite().modalShield;
                shield._visible = n.token == token && n.visible;
                shield.enabled = shield._visible;
                shield._alpha = 35;
                shield._x = -n.x - _originX;
                shield._y = -n.y - _originY;
                shield._width = _world.width();
                shield._height = _world.height();
            }
        });
        screen.MapUiCancelPointer = Bind.noArg(this, cancelPointerCapture);
        screen.MapUiOrigin = Bind.twoArg(this, function(x: String, y: String) {
            _originX = Number(x);
            _originY = Number(y);
            // 仅 Widget 根对象使用 Widget 视口原点；共享容器保持固定，避免旧屏幕对齐 UI 跟着移动。
            for (var token in _nodes) {
                var node = _nodes[token];
                if (!_nodes[node.parent]) {
                    node.object.sprite()._x = node.x + _originX;
                    node.object.sprite()._y = node.y + _originY;
                }
            }
        });
        screen.MapUiBegin = Bind.noArg(this, function() {
            _batch = true;
            _order = [];
            var scene = {};
            loadVariables("EnhancerApt_WidgetScene", scene);
            if (scene.epoch != undefined) {
                _sceneEpoch = String(scene.epoch);
                _sceneActive = String(scene.active) == "1";
            }
        });
        screen.MapUiCreate = Bind.threeArg(this, createNode);
        screen.MapUiProperty = Bind.threeArg(this, property);
        screen.MapUiTexture = Bind.threeArg(this, function(token: String, texture: String, ticket: String) {
            var node = _nodes[token];
            if (node) {
                node.imageTicket = ticket;
                property(token, "Texture", texture);
            }
        });
        screen.MapUiImageReady = Bind.threeArg(this, function(token: String, ticket: String, success: String) {
            var node = _nodes[token];
            if (!node || !_sceneActive || node.imageTicket != ticket || success != "1") {
                return;
            }
            var mc = node.object.sprite();
            var old = mc.image;
            old._visible = false;
            mc.image = mc.imageBack;
            mc.imageBack = old;
            node.imageReady = true;
            style(node);
            if (!_batch) {
                present(node);
            }
        });
        screen.MapUiRect = Bind.twoArg(this, rectangle);
        screen.MapUiClip = Bind.twoArg(this, clipRectangle);
        screen.MapUiHighlight = Bind.oneArg(this, function(token: String) {
            destroyNode("~highlight");
            var target = _nodes[token];
            if (!target || !target.visible) {
                return;
            }
            createNode("~highlight", "Container", "0");
            var highlight = _nodes["~highlight"];
            highlight.internalOwner = token;
            highlight.props = {BorderWidth: "2", BorderColor: "16768845", Transition: "0"};
            var clip = target.clip;
            clipRectangle("~highlight", clip.x + "," + clip.y + "," + clip.width + "," + clip.height);
            rectangle("~highlight", target.x + "," + target.y + "," + target.width + "," + target.height + ",1,0");
        });
        screen.MapUiMeasure = Bind.threeArg(this, requestMeasurement);
        screen.MapUiScrollMetrics = Bind.twoArg(this, function(token: String, encoded: String) {
            var node = _nodes[token];
            if (node) {
                var values = encoded.split(",");
                node.scrollX = Number(values[0]);
                node.scrollY = Number(values[1]);
                node.contentWidth = Number(values[2]);
                node.contentHeight = Number(values[3]);
            }
        });
        screen.MapUiWheel = Bind.threeArg(this, wheel);
        screen.MapUiOrder = Bind.twoArg(this, function(token: String, order: String) {
            _order.push(token);
        });
        screen.MapUiInput = Bind.twoArg(this, input);
        screen.MapUiTraverse = Bind.oneArg(this, traverse);
        screen.MapUiDestroy = Bind.oneArg(this, destroyNode);
        screen.MapUiReset = Bind.noArg(this, reset);
        screen.MapUiCommit = Bind.noArg(this, function() {
            // 重排仅交换受管理的深度，不重新挂载输入框，以保留焦点和输入状态。
            for (var i = 0; i < _order.length; i++) {
                var n = _nodes[_order[i]];
                if (!n || !_nodes[n.parent]) {
                    continue;
                }
                for (var j = i + 1; j < _order.length; j++) {
                    var other = _nodes[_order[j]];
                    if (other && n.parent == other.parent && n.object.depth() > other.object.depth()) {
                        n.object.swapDepth(other.object);
                    }
                }
            }
            if (_order.length > 0) {
                _layers.reorder();
            }
            _batch = false;
            var dirty = _dirtyStyles;
            _dirtyStyles = {};
            for (var token in dirty) {
                if (_nodes[token]) {
                    style(_nodes[token]);
                    present(_nodes[token]);
                }
            }
        });
        _templatePlain = _world.instantiate("MapUiNodeV2");
        _templateField = _world.instantiate("MapUiInputV2");
        if (_templatePlain.sprite()) {
            _templatePlain.sprite()._visible = false;
        }
        if (_templateField.sprite()) {
            _templateField.sprite()._visible = false;
        }
        _world.addAfterFrameListener("map_ui_templates", Bind.noArg(this, checkTemplateProbes));
    }

    private function validV2Template(mc: MovieClip, input: Boolean): Boolean {
        if (mc == undefined || mc.visual == undefined || mc.visual.mask == undefined ||
            mc.visual.content == undefined || mc.visual.controls == undefined || mc.childHost == undefined ||
            mc.measureLabel == undefined || mc.measureLabel.tv == undefined || mc.modalShield == undefined) {
            return false;
        }
        var content = mc.visual.content;
        return content.bg != undefined && content.hitRegion != undefined &&
            (!input || (content.TextFieldMC != undefined && content.TextFieldMC.tv != undefined));
    }

    private function aliasV2Visual(mc: MovieClip): Void {
        var visual = mc.visual.content;
        var names = ["bg", "image", "label", "placeholderClip", "TextFieldMC", "hitRegion", "chrome",
                     "borderTop", "borderRight", "borderBottom", "borderLeft"];
        for (var i = 0; i < names.length; i++) {
            mc[names[i]] = visual[names[i]];
        }
        mc.modalShield._visible = false;
        mc.modalShield.enabled = false;
        mc.measureLabel._visible = false;
        mc.measureLabel.bg._visible = false;
    }

    private function clipRectangle(token: String, encoded: String): Void {
        var node = _nodes[token];
        if (!node) {
            return;
        }
        var parts = encoded.split(",");
        node.clip = {x: Number(parts[0]), y: Number(parts[1]), width: Number(parts[2]), height: Number(parts[3])};
        applyClip(node);
    }

    private function applyClip(node: Object): Void {
        var mc = node.object.sprite();
        var clip = node.clip;
        mc.visual._visible = (node.visible || node.displayAlpha > 0) && clip.width > 0 && clip.height > 0;
        // RA3 APT 保留 ClipDepth，但仍绘制遮罩自身；与现有长文本遮罩一样保持透明。
        // 不设 _visible=false，否则引擎可能连裁剪路径也停用。
        mc.visual.mask._alpha = 0;
        mc.visual.mask._x = clip.x - node.x;
        mc.visual.mask._y = clip.y - node.y;
        mc.visual.mask._width = Math.max(0, clip.width);
        mc.visual.mask._height = Math.max(0, clip.height);
        // hitRegion 与绘制使用同一个有效交集，完全离屏的控件不能夺取点击。
        mc.hitRegion._x = clip.x - node.x;
        mc.hitRegion._y = clip.y - node.y;
        mc.hitRegion._width = Math.max(0, clip.width);
        mc.hitRegion._height = Math.max(0, clip.height);
    }

    private function requestMeasurement(token: String, ticket: String, width: String): Void {
        var node = _nodes[token];
        if (!node) {
            return;
        }
        node.measureTicket = ticket;
        node.measureWidth = Number(width);
        node.measurePending = true;
    }

    private function finishMeasurement(node: Object): Void {
        var mc = node.object.sprite();
        var field = mc.measureLabel.tv;
        if (!field || !mc.tv) {
            return;
        }
        field.setTextFormat(mc.tv.getTextFormat());
        field.multiline = node.props.Wrap == "1" || node.props.Multiline == "1";
        field.wordWrap = node.props.Wrap == "1";
        field._width = node.measureWidth > 0 ? node.measureWidth : 100000;
        field.autoSize = "left";
        field.text = node.props.Content == undefined ? "" : node.props.Content;
        var width = field._width;
        var height = field._height;
        if (isNaN(width) || isNaN(height) || width < 0 || height < 0) {
            return;
        }
        node.measurePending = false;
        fscommand("CallGameFunction", "%MapUi?measure|" + node.token + "|" + node.measureTicket + "|" +
                  width + "|" + height);
    }

    private function decodeHex(text: String): String {
        var result = "";
        for (var i = 0; i + 3 < text.length; i += 4) {
            // APT 提供字符转换，但不保证 Flash 全局 parseInt；逐位解析 UTF-16 编码。
            var code = 0;
            for (var j = 0; j < 4; j++) {
                var digit = text.charCodeAt(i + j);
                if (digit >= 48 && digit <= 57) {
                    digit -= 48;
                } else if (digit >= 65 && digit <= 70) {
                    digit -= 55;
                } else if (digit >= 97 && digit <= 102) {
                    digit -= 87;
                } else {
                    return "";
                }
                code = code * 16 + digit;
            }
            result += String.fromCharCode(code);
        }
        return result;
    }

    private function encodeHex(text: String): String {
        var digits = "0123456789ABCDEF";
        var result = "";
        for (var i = 0; i < text.length; i++) {
            var code = text.charCodeAt(i);
            for (var shift = 12; shift >= 0; shift -= 4) {
                result += digits.charAt((code >> shift) & 15);
            }
        }
        return result;
    }

    // 以下为拆类后的薄包装：状态仍只由本类持有，静态实现以 this 作为内部 manager 上下文。
    // 这些入口继续供动态回调与本类调用，不改变节点 token、fscommand 或事件顺序。
    private function controlLabel(node: Object): String {
        return MapUiOptions.controlLabel(this, node);
    }

    private function controlChange(node: Object, value: String): Void {
        MapUiOptions.controlChange(this, node, value);
    }

    private function activate(node: Object): Void {
        MapUiOptions.activate(this, node);
    }

    private function changeSlider(node: Object): Void {
        MapUiOptions.changeSlider(this, node);
    }

    private function inheritedTheme(node: Object): String {
        return MapUiOptions.inheritedTheme(this, node);
    }

    private function removeOptions(node: Object): Void {
        MapUiOptions.removeOptions(this, node);
    }

    private function choice(owner: Object, parent: String, index: Number,
                            x: Number, y: Number, width: Number, height: Number): Void {
        MapUiOptions.choice(this, owner, parent, index, x, y, width, height);
    }

    private function renderOptions(node: Object): Void {
        MapUiOptions.renderOptions(this, node);
    }

    private function closePopup(): Void {
        MapUiOptions.closePopup(this);
    }

    private function openPopup(node: Object): Void {
        MapUiOptions.openPopup(this, node);
    }

    private function renderPopupRows(owner: Object, popup: Object): Void {
        MapUiOptions.renderPopupRows(this, owner, popup);
    }

    private function wheel(delta: String, px: String, py: String): Void {
        MapUiOptions.wheel(this, delta, px, py);
    }

    private function closeTooltip(): Void {
        MapUiOptions.closeTooltip(this);
    }

    private function showTooltip(node: Object): Void {
        MapUiOptions.showTooltip(this, node);
    }

    private function checkTemplateProbes(): Void {
        if (!_alive || _templatesChecked) {
            return;
        }
        _templateFrames++;
        var plain = _templatePlain ? _templatePlain.sprite() : undefined;
        var field = _templateField ? _templateField.sprite() : undefined;
        _templatesValid = validV2Template(plain, false) && validV2Template(field, true);
        _incrementalTemplates = _templatesValid && plain.visual.content.imageBack != undefined &&
            field.visual.content.imageBack != undefined && plain.visual.content.imageBack.image != undefined &&
            field.visual.content.imageBack.image != undefined;
        // 限制不完整模板的探测寿命；子对象提前出现即可结束探测，过早的 Hello 仍可重试。
        if (_templatesValid || _templateFrames >= 60) {
            _templatesChecked = true;
            trace("MapUi template probe frames=" + _templateFrames + " valid=" + _templatesValid);
            cleanupTemplateProbes();
        }
    }
    private function cleanupTemplateProbes(): Void {
        _world.removeAfterFrameListener("map_ui_templates");
        if (_templatePlain) {
            _world.destroy(_templatePlain);
        }
        if (_templateField) {
            _world.destroy(_templateField);
        }
        _templatePlain = undefined;
        _templateField = undefined;
    }

    private function createNode(token: String, type: String, parent: String): Void {
        if (_nodes[token]) {
            return;
        }
        var parentNode = _nodes[parent];
        var world = parentNode ? parentNode.children : _world;
        var symbol = type == "Input" ? "MapUiInputV2" : "MapUiNodeV2";
        var object = world.instantiate(symbol);
        var mc = object.sprite();
        // 从 attachMovie 起保持隐藏，批次提交完成才允许显示。
        mc._visible = false;
        aliasV2Visual(mc);
        mc.imageBack = mc.visual.content.imageBack;
        if (mc.imageBack) {
            mc.imageBack._visible = false;
            // MapUiPassiveImage 只复制原生 image 子元件；旧 CustomImage 才有 overlay/hitRegion。
            if (mc.imageBack.overlay) {
                mc.imageBack.overlay._visible = false;
            }
            if (mc.imageBack.hitRegion) {
                mc.imageBack.hitRegion._visible = false;
                mc.imageBack.hitRegion.enabled = false;
            }
        }
        // Widget 自行处理交互，故只显示原生 RenderImage；旧模板的按钮层仍主动隐藏。
        if (mc.image.overlay) {
            mc.image.overlay._visible = false;
        }
        if (mc.image.hitRegion) {
            mc.image.hitRegion._visible = false;
            mc.image.hitRegion.enabled = false;
        }
        if (mc.label) {
            mc.label._x = 0;
            mc.label._y = 0;
            mc.label.bg._visible = false;
            mc.tv = mc.label.tv;
        }
        if (mc.placeholderClip) {
            mc.placeholderClip._x = 0;
            mc.placeholderClip._y = 0;
            mc.placeholderClip.bg._visible = false;
            mc.placeholder = mc.placeholderClip.tv;
        }
        if (mc.TextFieldMC) {
            mc.TextFieldMC._x = 0;
            mc.TextFieldMC._y = 0;
            mc.TextFieldMC.bg._visible = false;
            if (mc.TextFieldMC.dataTF == undefined) {
                mc.TextFieldMC.dataTF = mc.TextFieldMC.tv;
            }
        }
        var node = {
            token: token,
            type: type,
            parent: parent,
            object: object,
            world: world,
            children: new World(mc.childHost, 0, 0),
            props: {},
            x: 0,
            y: 0,
            width: 0,
            height: 0,
            visible: true,
            disabled: false,
            text: "",
            nativeId: undefined,
            hover: false,
            pressed: false,
            selected: false,
            focused: false,
            skin: "plain",
            clip: {x: 0, y: 0, width: 0, height: 0},
            controlValue: "",
            options: [],
            measureTicket: "",
            measureWidth: 0,
            measurePending: false,
            scrollX: 0,
            scrollY: 0,
            contentWidth: 0,
            contentHeight: 0,
            imageTicket: "",
            imageReady: false,
            visualTheme: undefined,
            targetTheme: undefined,
            // 本次按下是否因拖动结束或取消而抑制 Button click，按下时复位。
            suppressClick: false
        };
        _nodes[token] = node;
        if (!parentNode) {
            var manager = this;
            _layers.register("widget:" + token, object, 0, Number(token), function(x, y) {
                return manager.hitRoot(token, x, y);
            });
        }
        mc._visible = false;
        var manager = this;
        mc.hitRegion.onRollOver = function() {
            if (!manager.isHitActive(node)) {
                return;
            }
            node.hover = true;
            if (node.props.Tooltip != undefined && node.props.Tooltip != "") {
                manager._tooltipOwner = node.token;
                manager._tooltipAt = getTimer() + 400;
            }
            manager.refresh(node);
        };
        mc.hitRegion.onRollOut = function() {
            if (!manager.isHitActive(node)) {
                return;
            }
            // 捕获期间命中区可能暂时离开光标（例如拖动被父容器夹紧），
            // 按下状态保持到释放或取消，不由 rollout 清除。
            if (manager.isPointerCaptured(node)) {
                return;
            }
            node.hover = false;
            node.pressed = false;
            if (manager._tooltipOwner == node.token) {
                manager.closeTooltip();
            }
            manager.refresh(node);
        };
        mc.hitRegion.onPress = function() {
            if (!manager.isHitActive(node)) {
                return;
            }
            node.pressed = true;
            if (node.type != "Input") {
                manager.emit("focus", node.internalOwner == undefined ? node.token : node.internalOwner);
            }
            if (node.type == "Slider") {
                manager.changeSlider(node);
            }
            node.suppressClick = false;
            if (manager.isPointerEnabled(node)) {
                // 按下即捕获指针，后续移动与释放只由该节点处理。
                manager.pointerDown(node);
            }
            if (node.type == "Input") {
                manager.emit("focus", node.token);
                manager.mouse(node, "Down");
            }
            manager.refresh(node);
        };
        mc.hitRegion.onRelease = function() {
            if (!manager.isHitActive(node)) {
                return;
            }
            node.pressed = false;
            node.hover = true;
            manager.refresh(node);
            if (manager.isPointerEnabled(node)) {
                manager.pointerUp(node);
            }
            if (node.type == "Input") {
                manager.mouse(node, "Up");
            }
            // 拖动结束的释放不再补发 click；地图按 dragEnd 的最终偏移更新声明位置。
            if (!node.suppressClick) {
                manager.activate(node);
            }
        };
        mc.hitRegion.onReleaseOutside = function() {
            if (!manager.isHitActive(node)) {
                return;
            }
            node.pressed = false;
            node.hover = false;
            manager.refresh(node);
            if (manager.isPointerEnabled(node)) {
                // 区域外释放同样结束捕获：先上报最终偏移，再还原声明位置。
                manager.pointerUp(node);
            }
            if (node.type == "Input") {
                manager.mouse(node, "Up");
            }
        };
        mc.hitRegion.onMouseMove = function() {
            if (!manager.isLive(node)) {
                return;
            }
            if (node.type == "Slider" && manager.isPointerCaptured(node)) {
                manager.changeSlider(node);
            }
            if (manager.isPointerEnabled(node)) {
                // 指针上报复用节点自身命中区回调；未捕获时只有最前悬停节点上报，
                // 重叠节点不会同时产生 pointerMove。
                manager.pointerMove(node);
            }
            if (node.type == "Input" && manager.isFocused(node.token)) {
                manager.mouse(node, "Move");
            }
        };
        mc.executeOnEnterFunction = function() {
            if (!manager.isConfirmable(node)) {
                return;
            }
            manager.emit("confirm", node.token);
        };
        if (type == "Input") {
            var result = {};
            loadVariables("QueryGameEngine?NEXT_TEXT_INPUT_FIELD_ID", result);
            node.nativeId = Number(result.NEXT_TEXT_INPUT_FIELD_ID);
            emit("beginInput", token);
            fscommand(
                "CallGameFunction",
                "%CreateTextInputField?Name=" + mc + "|ID=" + node.nativeId +
                    "|Password=0|CharLimit=2048|AllowScroll=1");
            emit("endInput", token);
            mc.TextFieldMC.dataTF.text = "$EDITABLE_TEXT_" + node.nativeId;
            mc.TextFieldMC.dataTF.multiline = false;
            mc.TextFieldMC.dataTF.wordWrap = false;
        }
        mc.image._visible = type == "Image" || type == "Button";
        mc.tv._visible = type == "Text" || type == "Button";
        mc.hitRegion._visible = type == "Button" || type == "Input";
        mc.hitRegion._alpha = 0;
        mc.placeholder._visible = false;
    }

    public function emit(event: String, token: String): Void {
        if (!_alive) {
            return;
        }
        fscommand("CallGameFunction", "%MapUi?" + event + "|" + token);
    }
    private function rootToken(node: Object): String {
        var parent = _nodes[node.parent];
        return parent ? rootToken(parent) : node.token;
    }
    private function depthPath(node: Object): Array {
        var parent = _nodes[node.parent];
        var path = parent ? depthPath(parent) : [];
        path.push(node.object.depth());
        return path;
    }
    private function aboveWithinRoot(a: Object, b: Object): Boolean {
        var ap = depthPath(a);
        var bp = depthPath(b);
        for (var i = 0; i < Math.min(ap.length, bp.length); i++) {
            if (ap[i] != bp[i]) {
                return ap[i] > bp[i];
            }
        }
        return ap.length > bp.length;
    }
    public function frontInRoot(token: String, x: Number, y: Number): String {
        x -= _originX;
        y -= _originY;
        var front;
        for (var key in _nodes) {
            var node = _nodes[key];
            if (isHitActive(node) && rootToken(node) == token && x >= node.clip.x && y >= node.clip.y &&
                x < node.clip.x + node.clip.width && y < node.clip.y + node.clip.height &&
                (!front || aboveWithinRoot(node, front))) {
                front = node;
            }
        }
        return front ? front.token : "";
    }
    public function hitRoot(token: String, x: Number, y: Number): Boolean {
        // 捕获期间该根对象独占命中：即使拖动中的精灵被夹紧或暂时离开指针，也不让世界点击穿透。
        var captured = _pointerCapture == "" ? undefined : _nodes[_pointerCapture];
        if (captured != undefined && rootToken(captured) == token) {
            return true;
        }
        return token == _modal || frontInRoot(token, x, y) != "";
    }

    public function isFocused(token: String): Boolean {
        return _focused == token;
    }
    // 所有原生回调共用交互检查：隐藏、禁用及已销毁节点的迟到回调均不再传入游戏。
    public function isLive(node: Object): Boolean {
        return _alive && _sceneActive && node != undefined && !node.object.isDestroyed() && node.visible && !node.disabled &&
            (_modal == "0" || rootToken(node) == _modal ||
             (node.internalOwner != undefined && _nodes[node.internalOwner] != undefined &&
              rootToken(_nodes[node.internalOwner]) == _modal));
    }
    public function isInteractive(node: Object): Boolean {
        return isLive(node) && node.clip.width > 0 && node.clip.height > 0 &&
            (node.type == "Button" || node.type == "Input" || node.type == "Checkbox" || node.type == "Radio" ||
             node.type == "Slider" || node.type == "Stepper" || node.type == "Select" || node.type == "Tabs" ||
             node.type == "Menu");
    }
    public function isConfirmable(node: Object): Boolean {
        return isLive(node) && node.type == "Input" && isFocused(node.token);
    }
    public function refresh(node: Object): Void {
        if (!_alive) {
            return;
        }
        style(node);
    }
    public function mouse(node: Object, kind: String): Void {
        if (!_alive || node == undefined || node.object.isDestroyed()) {
            return;
        }
        var mc = node.object.sprite();
        fscommand(
            "CallGameFunction",
            "%OnTextInputFieldMouse" + kind + "?ID=" + node.nativeId + "|x=" + mc._xmouse + "|y=" + mc._ymouse);
    }

    // 指针事件为显式 opt-in：PointerEvents=1 或 Draggable=1，缺省时旧控件行为完全不变。
    // 仅 Image/Button 参与：Input 保持原生 Down/Up/Move 流程，Text 等只读节点不上报。
    public function isPointerEnabled(node: Object): Boolean {
        if (!_alive || node == undefined || node.object == undefined || node.object.isDestroyed()) {
            return false;
        }
        // 显式检查可见与禁用：隐藏或禁用的节点不参与指针事件，也不能保持捕获。
        if (!isLive(node) || node.clip.width <= 0 || node.clip.height <= 0) {
            return false;
        }
        if (node.type == "Slider") {
            return true;
        }
        if (node.type != "Image" && node.type != "Button") {
            return false;
        }
        return node.props.PointerEvents == "1" || node.props.Draggable == "1";
    }
    // 命中区回调的共用入口：旧按钮／输入框，或显式开启指针事件的节点。
    public function isHitActive(node: Object): Boolean {
        return !_systemPageActive && (isInteractive(node) || isPointerEnabled(node));
    }
    // 拖动只对非根 Image/Button 生效；根对象的位置由地图分组布局控制。
    public function isDraggable(node: Object): Boolean {
        if (!isPointerEnabled(node) || node.props.Draggable != "1") {
            return false;
        }
        if (node.type != "Image" && node.type != "Button") {
            return false;
        }
        return _nodes[node.parent] != undefined;
    }
    public function isPointerCaptured(node: Object): Boolean {
        return node != undefined && _pointerCapture == node.token;
    }
    // 事件格式：%MapUi?事件|token|x|y；x/y 为精灵局部 _xmouse/_ymouse，只经 fscommand 上报。
    private function emitPointer(event: String, node: Object): Void {
        if (!_alive || node == undefined || node.object.isDestroyed()) {
            return;
        }
        var mc = node.object.sprite();
        fscommand(
            "CallGameFunction",
            "%MapUi?" + event + "|" + node.token + "|" + mc._xmouse + "|" + mc._ymouse);
    }
    // 拖动类事件追加 |offsetX|offsetY，表示相对声明位置的偏移（取整到整数像素）。
    private function emitDrag(event: String, node: Object): Void {
        if (!_alive || node == undefined || node.object.isDestroyed()) {
            return;
        }
        var mc = node.object.sprite();
        fscommand(
            "CallGameFunction",
            "%MapUi?" + event + "|" + node.token + "|" + mc._xmouse + "|" + mc._ymouse + "|" +
                _dragOffsetX + "|" + _dragOffsetY);
    }
    // 按下即捕获：同一时刻只允许一个节点持有指针，新的按下取消旧捕获。
    private function pointerDown(node: Object): Void {
        if (!isPointerEnabled(node)) {
            return;
        }
        if (_pointerCapture != "" && _pointerCapture != node.token) {
            cancelPointerCapture();
        }
        var mc = node.object.sprite();
        _pointerCapture = node.token;
        _dragActive = false;
        // 声明基准在按下时就记录：即使未越过阈值，取消也能还原到正确位置而不是初始值。
        _dragOriginX = mc._x;
        _dragOriginY = mc._y;
        // 抓取偏移：按下点相对精灵原点的位置，拖动期间保持光标落在同一点。
        _grabX = mc._xmouse;
        _grabY = mc._ymouse;
        _dragOffsetX = 0;
        _dragOffsetY = 0;
        emitPointer("pointerDown", node);
    }
    // 移动：未捕获时只有最前悬停节点上报；捕获节点负责阈值判定与拖动跟随。
    private function pointerMove(node: Object): Void {
        if (!isPointerEnabled(node)) {
            return;
        }
        if (_pointerCapture != node.token) {
            // 已有其它节点持有捕获时全部静默，避免和拖动中的节点重复上报。
            if (_pointerCapture != "" || !node.hover) {
                return;
            }
            // 未捕获时用根对象坐标复核：只有真正最前的悬停节点才上报。
            var hoverMc = node.object.sprite();
            var rootX = node.x + _originX + hoverMc._xmouse;
            var rootY = node.y + _originY + hoverMc._ymouse;
            if (frontInRoot(rootToken(node), rootX, rootY) != node.token) {
                return;
            }
            emitPointer("pointerMove", node);
            return;
        }
        var mc = node.object.sprite();
        if (!_dragActive && isDraggable(node) && dragExceedsThreshold(mc)) {
            beginDrag(node);
        }
        if (_dragActive) {
            updateDrag(node);
        }
        emitPointer("pointerMove", node);
    }
    private function dragExceedsThreshold(mc: MovieClip): Boolean {
        var dx = mc._xmouse - _grabX;
        var dy = mc._ymouse - _grabY;
        return dx * dx + dy * dy >= _dragThreshold * _dragThreshold;
    }
    private function beginDrag(node: Object): Void {
        var mc = node.object.sprite();
        _dragActive = true;
        // 记录拖动前的精灵位置：拖动期间的偏移与结束还原都以它为基准。
        _dragOriginX = mc._x;
        _dragOriginY = mc._y;
        _dragOffsetX = 0;
        _dragOffsetY = 0;
        emitDrag("dragStart", node);
    }
    private function updateDrag(node: Object): Void {
        var parent = _nodes[node.parent];
        var mc = node.object.sprite();
        if (parent == undefined || mc._parent == undefined) {
            return;
        }
        // 光标在父容器坐标系中的位置减去抓取偏移，得到保持抓取点的精灵位置。
        var x = mc._parent._xmouse - _grabX;
        var y = mc._parent._ymouse - _grabY;
        // 夹紧在父节点内容区内，超界部分不回弹，指针移回后继续跟随。
        mc._x = clamp(x, 0, Math.max(0, parent.width - node.width));
        mc._y = clamp(y, 0, Math.max(0, parent.height - node.height));
        _dragOffsetX = Math.round(mc._x - _dragOriginX);
        _dragOffsetY = Math.round(mc._y - _dragOriginY);
    }
    // 释放：拖动结束先上报最终偏移，再还原声明位置；本次释放不再补发 click。
    private function pointerUp(node: Object): Void {
        if (_pointerCapture != node.token) {
            return;
        }
        if (_dragActive) {
            // 释放可能发生在最后一次 move 回调之后：先按当前光标重采样，再结算最终偏移。
            if (!node.object.isDestroyed()) {
                updateDrag(node);
                var mc = node.object.sprite();
                _dragOffsetX = Math.round(mc._x - _dragOriginX);
                _dragOffsetY = Math.round(mc._y - _dragOriginY);
            }
            // 先还原声明位置再上报：地图收到 dragEnd 后立即重下 MapUiRect 也不会被本地还原覆盖。
            restoreDrag(node);
            emitDrag("dragEnd", node);
            node.suppressClick = true;
        }
        emitPointer("pointerUp", node);
        _dragActive = false;
        _pointerCapture = "";
    }
    // 还原到拖动前的声明位置；最终偏移已在 dragEnd/dragCancel 中上报。
    private function restoreDrag(node: Object): Void {
        if (node == undefined || node.object.isDestroyed()) {
            return;
        }
        var mc = node.object.sprite();
        mc._x = _dragOriginX;
        mc._y = _dragOriginY;
    }
    // 取消：还原精灵并上报 dragCancel，未进入拖动时偏移保持 0，同样抑制本次 click。
    private function cancelPointer(node: Object): Void {
        if (node == undefined || _pointerCapture != node.token) {
            return;
        }
        if (_dragActive && !node.object.isDestroyed()) {
            var mc = node.object.sprite();
            _dragOffsetX = Math.round(mc._x - _dragOriginX);
            _dragOffsetY = Math.round(mc._y - _dragOriginY);
        }
        // 先还原再上报，语义与 dragEnd 一致：取消不改变声明，地图无需再下发几何。
        restoreDrag(node);
        emitDrag("dragCancel", node);
        node.suppressClick = true;
        node.pressed = false;
        refresh(node);
        _dragActive = false;
        _pointerCapture = "";
    }
    // DLL 在窗口失焦等场景调用；节点已销毁时只清空本地捕获状态。
    private function cancelPointerCapture(): Void {
        var captured = _pointerCapture == "" ? undefined : _nodes[_pointerCapture];
        if (captured == undefined) {
            _dragActive = false;
            _pointerCapture = "";
            return;
        }
        cancelPointer(captured);
    }
    private function clamp(value: Number, low: Number, high: Number): Number {
        if (value < low) {
            return low;
        }
        if (value > high) {
            return high;
        }
        return value;
    }

    private function property(token: String, name: String, value: String): Void {
        var node = _nodes[token];
        if (!node) {
            return;
        }
        if (value == "") {
            delete node.props[name];
            // 某些 APT 动态属性不接受 delete，重建该节点的小属性表以恢复缺省值。
            if (node.props[name] != undefined) {
                var retainedProps = {};
                for (var retainedName in node.props) {
                    if (retainedName != name) {
                        retainedProps[retainedName] = node.props[retainedName];
                    }
                }
                node.props = retainedProps;
            }
        } else {
            node.props[name] = value;
        }
        var mc = node.object.sprite();
        if (name == "GroupIndex" && !_nodes[node.parent]) {
            _layers.setGroup("widget:" + token, value == "" ? 0 : Number(value));
            return;
        }
        if (name == "ControlValue") {
            node.controlValue = value;
            if (_popupOwner == token) {
                closePopup();
            }
            if (node.type == "Checkbox" || node.type == "Radio") {
                node.selected = value == "1";
            }
        }
        if (name == "OptionsHex") {
            if (_popupOwner == token) {
                closePopup();
            }
            node.options = [];
            var entries = value.split(";");
            for (var i = 0; i < entries.length; i++) {
                var fields = entries[i].split(":");
                if (fields.length == 3) {
                    node.options.push({key: decodeHex(fields[0]), label: decodeHex(fields[1]), disabled: fields[2] == "1"});
                }
            }
        }
        if (name == "Selected") {
            node.selected = value == "1";
        }
        if (name == "ValueHex") {
            fscommand("CallGameFunction", "%MapUi?set|" + token + "|" + value);
        }
        if (name == "Texture") {
            if (value == "") {
                node.imageReady = false;
                mc.image._visible = false;
                if (mc.imageBack) {
                    mc.imageBack._visible = false;
                }
            } else if (mc.imageBack && node.imageTicket != "") {
                mc.imageBack._visible = false;
                mc.imageBack.image._width = node.width;
                mc.imageBack.image._height = node.height;
                fscommand("CallGameFunction", "%MapUi?image2|" + token + "|" + node.imageTicket + "|" +
                    _global.Pointer.encode(mc.imageBack));
            } else {
                // 原有 DLL 仍走旧图片协议；新能力只在 DLL 与模板同时匹配时启用。
                node.imageReady = true;
                fscommand("CallGameFunction", "%MapUi?image|" + token + "|" + _global.Pointer.encode(mc.image));
            }
        }
        // 子节点会继承 Skin，因此变更后须重新解析整个子树。
        if (name == "Skin" || name == "Theme") {
            styleTree(node);
        } else {
            style(node);
        }
        if ((name == "OptionsHex" || name == "ControlValue") && (node.type == "Tabs" || node.type == "Menu")) {
            renderOptions(node);
        }
    }

    // 沿父链解析是否启用皮肤，具体映射规则由 MapUiTheme 统一管理。
    private function skinOf(node: Object): String {
        var parent = _nodes[node.parent];
        var inherited = parent ? skinOf(parent) : "plain";
        return _theme.skinFor(node.props.Skin, inherited);
    }

    private function styleTree(node: Object): Void {
        style(node);
        for (var key in _nodes) {
            if (_nodes[key].parent == node.token) {
                styleTree(_nodes[key]);
            }
        }
    }

    private function style(node: Object): Void {
        if (_batch && node != undefined) {
            _dirtyStyles[node.token] = true;
            return;
        }
        if (!_alive || node == undefined || node.object.isDestroyed()) {
            return;
        }
        if (!validV2Template(node.object.sprite(), node.type == "Input")) {
            _dirtyStyles[node.token] = true;
            node.object.sprite()._visible = false;
            return;
        }
        var p = node.props;
        var mc = node.object.sprite();
        node.skin = skinOf(node);
        var themedProps = {};
        for (var name in p) {
            themedProps[name] = p[name];
        }
        themedProps.Theme = inheritedTheme(node);
        var theme = _theme.resolve(
            node.type, themedProps, node.skin, node.hover, node.pressed, node.selected, node.focused, node.disabled);
        theme.controlNumber = Number(node.controlValue);
        if (isNaN(theme.controlNumber)) {
            theme.controlNumber = 0;
        }
        theme.opacity = p.Opacity == undefined ? 100 : Number(p.Opacity);
        var fields = ["bgColor", "bgAlpha", "textColor", "borderLight", "borderDark", "alpha", "chromeBody",
                      "chromeBodyAlpha", "chromeLight", "chromeDark", "chromeAccent", "chromeAccentDeep",
                      "chromeShine", "chromeShineAlpha", "controlNumber", "opacity"];
        var colors = {bgColor: true, textColor: true, borderLight: true, borderDark: true, chromeBody: true,
                      chromeLight: true, chromeDark: true, chromeAccent: true, chromeAccentDeep: true, chromeShine: true};
        var from = {};
        var to = {};
        var changed = node.targetTheme == undefined;
        for (var i = 0; i < fields.length; i++) {
            var name = fields[i];
            to[name] = theme[name];
            from[name] = node.visualTheme == undefined ? to[name] : node.visualTheme[name];
            changed = changed || node.targetTheme[name] != to[name];
        }
        if (changed) {
            var duration = p.Transition == undefined ? (node.skin == "ra3" ? 0.12 : 0) : Number(p.Transition);
            var start = _motion.retarget(node.token, from, to, node.disabled ? 0 : duration, colors);
            node.targetTheme = theme;
            node.visualTheme = {};
            for (var name in theme) {
                node.visualTheme[name] = theme[name];
            }
            if (start != undefined) {
                for (var name in start) {
                    node.visualTheme[name] = start[name];
                }
            }
        } else {
            // 离散状态立即切换，活动数值继续沿当前过渡走向目标。
            for (var name in theme) {
                if (to[name] == undefined) {
                    node.visualTheme[name] = theme[name];
                }
            }
        }
        paintStyle(node, node.visualTheme);
    }

    // 绘制实现的薄包装；原 this 仍作为内部 manager 上下文传给静态类。
    private function paintStyle(node: Object, theme: Object): Void {
        MapUiStylePainter.paintStyle(this, node, theme);
    }

    private function rectangle(token: String, encoded: String): Void {
        var node = _nodes[token];
        if (!node) {
            return;
        }
        var r = encoded.split(",");
        node.x = Number(r[0]);
        node.y = Number(r[1]);
        node.width = Number(r[2]);
        node.height = Number(r[3]);
        var wasVisible = node.visible;
        node.visible = r[4] == "1";
        node.disabled = r[5] == "1";
        if (wasVisible != node.visible || node.displayAlpha == undefined) {
            var duration = node.props.Transition == undefined ?
                (skinOf(node) == "ra3" ? 0.12 : 0) : Number(node.props.Transition);
            var current = node.displayAlpha == undefined ? 0 : node.displayAlpha;
            var state = _visibilityMotion.retarget(token, {alpha: current},
                {alpha: node.visible ? 100 : 0}, duration, {});
            node.displayAlpha = state == undefined ? (node.visible ? 100 : 0) : state.alpha;
        }
        var captured = _pointerCapture == token;
        if (!node.visible || node.disabled) {
            // 隐藏或禁用时清除临时交互状态，避免重新显示后残留悬停、按下或焦点效果。
            node.hover = false;
            node.pressed = false;
            if (captured) {
                cancelPointer(node);
            }
            if (node.focused) {
                releaseFocus(node);
            }
        } else if (captured) {
            // 地图在交互中重下声明几何：旧原点与内容边界已失效，先取消还原再由地图重新下发。
            cancelPointer(node);
        }
        var parent = _nodes[node.parent];
        var mc = node.object.sprite();
        mc._x = node.x - (parent ? parent.x : 0) + (parent ? 0 : _originX);
        mc._y = node.y - (parent ? parent.y : 0) + (parent ? 0 : _originY);
        // 新节点在最终样式和几何都就绪前不可见；已显示节点保持上一帧内容到提交边界。
        if (!_batch || !mc._visible) {
            mc._visible = false;
        }
        mc.bg._width = node.width;
        mc.bg._height = node.height;
        mc.image.image._width = node.width;
        mc.image.image._height = node.height;
        if (mc.imageBack) {
            mc.imageBack.image._width = node.width;
            mc.imageBack.image._height = node.height;
        }
        if (mc.image.disabledImage) {
            mc.image.disabledImage._width = node.width;
            mc.image.disabledImage._height = node.height;
        }
        mc.hitRegion._width = node.width;
        mc.hitRegion._height = node.height;
        if (mc.tv) {
            mc.tv._width = node.width;
            mc.tv._height = node.height;
        }
        if (mc.TextFieldMC) {
            mc.TextFieldMC.dataTF._width = node.width;
            mc.TextFieldMC.dataTF._height = node.height;
        }
        if (mc.placeholder) {
            mc.placeholder._width = node.width;
            mc.placeholder._height = node.height;
        }
        applyClip(node);
        style(node);
        if (!_batch) {
            present(node);
        }
        if (node.type == "Tabs" || node.type == "Menu") {
            renderOptions(node);
        }
        if (_popupOwner == token && (!node.visible || node.disabled)) {
            closePopup();
        }
    }

    // 先释放真实的原生焦点，再向游戏报告 blur，保证回调观察到的状态一致。
    private function releaseFocus(node: Object): Void {
        if (!node.focused) {
            return;
        }
        input(node.token, "blur");
        emit("blur", node.token);
    }

    private function input(token: String, action: String): Void {
        var node = _nodes[token];
        if (!node) {
            return;
        }
        if (action == "focus") {
            if (!_alive || node.disabled || !node.visible) {
                return;
            }
            if (_focused != "" && _focused != token) {
                input(_focused, "blur");
            }
            _focused = token;
            node.focused = true;
            if (node.type == "Input") {
                fscommand("CallGameFunction", "%FocusTextInputField?ID=" + node.nativeId);
            }
            style(node);
        }
        if (action == "blur") {
            var wasFocused = node.focused || _focused == token;
            if (node.type == "Input") {
                fscommand("CallGameFunction", "%BlurTextInputField?ID=" + node.nativeId);
            }
            node.focused = false;
            if (_focused == token) {
                _focused = "";
            }
            if (wasFocused) {
                style(node);
            }
        }
    }

    private function traverse(direction: String): Void {
        // 焦点顺序、模态过滤和滚入统一由原生声明树决定。
        // 带出按键发生时的焦点，原生队列会拒绝已切换焦点的迟到消息。
        if (_focused != "" && _nodes[_focused] != undefined) {
            fscommand("CallGameFunction", "%MapUi?key|" + _focused + "|" +
                (direction == "-1" ? "backtab" : "tab"));
        }
    }

    // 仅控制本管理器的节点，不能隐藏与旧按钮共享的 holder。
    private function present(node: Object): Void {
        var mc = node.object.sprite();
        var ready = validV2Template(mc, node.type == "Input") && node.visualTheme != undefined &&
            (node.props.Texture == undefined || node.imageReady);
        mc._visible = _sceneActive && ready && (node.visible || node.displayAlpha > 0);
    }

    private function checkScene(): Boolean {
        var scene = {};
        loadVariables("EnhancerApt_WidgetScene", scene);
        // 旧 DLL 未提供查询时维持原有行为；增量能力不会在该组合下开放。
        if (scene.epoch == undefined) {
            return _sceneActive;
        }
        if (String(scene.active) == "1" && (_sceneEpoch == "" || _sceneEpoch == String(scene.epoch))) {
            return _sceneActive;
        }
        if (_sceneActive) {
            _sceneActive = false;
            _pointerCapture = "";
            _dragActive = false;
            _focused = "";
            _tooltipOwner = "";
            // 场景失效只关闭显示/命中，不能再调用旧原生 Input 或图片指针。
            for (var token in _nodes) {
                var node = _nodes[token];
                _motion.remove(token);
                _visibilityMotion.remove(token);
                node.measurePending = false;
                var mc = node.object.sprite();
                mc._visible = false;
                mc.hitRegion.enabled = false;
                mc.modalShield._visible = false;
                mc.modalShield.enabled = false;
            }
        }
        return false;
    }

    private function poll(): Void {
        if (!_alive || _batch) {
            return;
        }
        if (!checkScene()) {
            return;
        }
        var dirty = _dirtyStyles;
        _dirtyStyles = {};
        for (var dirtyToken in dirty) {
            if (_nodes[dirtyToken]) {
                style(_nodes[dirtyToken]);
                present(_nodes[dirtyToken]);
            }
        }
        // 读取引擎实际页面，暂停菜单关闭后才恢复 Widget 焦点；不以 Esc 的次数猜测状态。
        var page = _level0.gSM == undefined ? "" : _level0.gSM.m_sCurrentScreenId;
        var active = page != undefined && page != "";
        if (active != _systemPageActive) {
            _systemPageActive = active;
            if (active) {
                closePopup();
                closeTooltip();
                cancelPointerCapture();
            }
            fscommand("CallGameFunction", "%MapUi?system|" + (active ? "1" : "0"));
        }
        if (_systemPageActive) {
            return;
        }
        // 指针事件只在命中区回调中上报，这里仅兜底校验捕获节点仍然有效，
        // 不逐帧发送指针事件，也不新增任何网络请求。
        if (_pointerCapture != "") {
            var captured = _nodes[_pointerCapture];
            if (captured == undefined || !isPointerEnabled(captured)) {
                cancelPointerCapture();
            }
        }
        for (var measurementToken in _nodes) {
            var measurementNode = _nodes[measurementToken];
            if (measurementNode.measurePending) {
                finishMeasurement(measurementNode);
            }
        }
        var now = getTimer();
        if (_popupOwner == "" && _tooltipOwner != "" && now >= _tooltipAt && _nodes[_tooltipOwner]) {
            showTooltip(_nodes[_tooltipOwner]);
        }
        var elapsed = Math.max(0, (now - _motionTime) / 1000);
        var visibilityUpdates = _visibilityMotion.tick(elapsed);
        for (var visibilityIndex = 0; visibilityIndex < visibilityUpdates.length; visibilityIndex++) {
            var visibilityUpdate = visibilityUpdates[visibilityIndex];
            var visibleNode = _nodes[visibilityUpdate.key];
            if (visibleNode) {
                visibleNode.displayAlpha = visibilityUpdate.values.alpha;
                present(visibleNode);
                applyClip(visibleNode);
                paintStyle(visibleNode, visibleNode.visualTheme);
            }
        }
        var updates = _motion.tick(elapsed);
        _motionTime = now;
        for (var motionIndex = 0; motionIndex < updates.length; motionIndex++) {
            var update = updates[motionIndex];
            var animated = _nodes[update.key];
            if (animated) {
                for (var field in update.values) {
                    animated.visualTheme[field] = update.values[field];
                }
                paintStyle(animated, animated.visualTheme);
            }
        }
        if (_focused != "" && _nodes[_focused] && _nodes[_focused].type == "Input" && isLive(_nodes[_focused])) {
            emit("poll", _focused);
        }
        for (var token in _nodes) {
            var node = _nodes[token];
            if (node.type == "Input" && node.visible && node.object.sprite().placeholder) {
                var result = {};
                loadVariables("QueryGameEngine?TEXT_INPUT_FIELD_CONTENTS?ID=" + node.nativeId, result);
                node.object.sprite().placeholder._visible =
                    String(result.TEXT_INPUT_FIELD_CONTENTS) == "" && _focused != token;
            }
        }
    }

    private function destroyNode(token: String): Void {
        var node = _nodes[token];
        if (!node) {
            return;
        }
        if (_popupOwner == token) {
            closePopup();
        }
        if (_tooltipOwner == token) {
            closeTooltip();
        }
        // 先停止绘制与命中，再释放对象；APT 延迟移除显示对象时也不能留下可见残影。
        var mc = node.object.sprite();
        mc._visible = false;
        mc.hitRegion.enabled = false;
        mc.modalShield.enabled = false;
        delete mc.modalShield.onPress;
        // 销毁前结束捕获：还原精灵并让地图收到 dragCancel，避免拖动状态残留在 DLL。
        cancelPointer(node);
        _motion.remove(token);
        _visibilityMotion.remove(token);
        if (_focused == token && node.type != "Input") {
            _focused = "";
        }
        if (!_nodes[node.parent]) {
            _layers.remove("widget:" + token, false);
        }
        delete _dirtyStyles[token];
        delete _nodes[token];
        // APT 对动态键可能未实际删除；不能让销毁记录阻止同键重新创建。
        if (_nodes[token] != undefined) {
            var remaining = {};
            for (var retained in _nodes) {
                if (retained != token) {
                    remaining[retained] = _nodes[retained];
                }
            }
            _nodes = remaining;
        }
        if (node.type == "Input") {
            inputBeforeDestroy(node);
            fscommand("CallGameFunction", "%DestroyTextInputField?ID=" + node.nativeId);
        }
        var descendants = [];
        for (var key in _nodes) {
            if (_nodes[key].parent == token) {
                descendants.push(key);
            }
        }
        for (var i = 0; i < descendants.length; i++) {
            destroyNode(descendants[i]);
        }
        node.children.destroyAll();
        delete mc.hitRegion.onPress;
        delete mc.hitRegion.onRelease;
        delete mc.hitRegion.onMouseMove;
        delete mc.hitRegion.onReleaseOutside;
        delete mc.hitRegion.onRollOver;
        delete mc.hitRegion.onRollOut;
        delete mc.executeOnEnterFunction;
        node.world.destroy(node.object);
        // 原生 APT 移除显示对象后仍可能保留其库实例；保留引用的视觉子树也必须停止绘制。
        mc._visible = false;
        mc.visual._visible = false;
        mc.visual._alpha = 0;
        mc.childHost._visible = false;
    }
    private function inputBeforeDestroy(node: Object): Void {
        // 即使 ID 不活跃，原生 blur 仍会恢复共享焦点缓存；
        // 销毁无关输入框时不能调用它抢走另一个输入框的焦点。
        if (node.focused || _focused == node.token) {
            fscommand("CallGameFunction", "%BlurTextInputField?ID=" + node.nativeId);
        }
        node.focused = false;
        if (_focused == node.token) {
            _focused = "";
        }
    }
    private function reset(): Void {
        var tokens = [];
        for (var token in _nodes) {
            tokens.push(token);
        }
        for (var i = 0; i < tokens.length; i++) {
            destroyNode(tokens[i]);
        }
        _nodes = {};
        _focused = "";
        _modal = "0";
        _popupOwner = "";
        _tooltipOwner = "";
        _layers.setPopup("");
        _layers.setModal("");
        _motion.clear();
        _visibilityMotion.clear();
        _order = [];
        _batch = false;
        _pointerCapture = "";
        _dragActive = false;
        _dragOffsetX = 0;
        _dragOffsetY = 0;
    }
    private function dispose(): Void {
        if (!_alive) {
            return;
        }
        _alive = false;
        reset();
        _world.removeOnFrameListener("map_ui_inputs");
        cleanupTemplateProbes();
        delete _holder.sprite().onMouseDown;
        delete _holder.sprite().onUnload;
        // 管理器失效后不再响应 DLL 的失焦取消，避免操作已释放的节点。
        delete _screen.MapUiCancelPointer;
    }
}
