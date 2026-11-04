import EnhancerApt.Bind;

// 内部拆类：选项、弹出与提示逻辑只接收原 MapUiManager 作为上下文。
// manager 上的字段和方法是同一实例的内部动态访问适配，不是地图公开 API；
// 状态仍只由 MapUiManager 持有，静态函数不复制或缓存任何字段。
// 回调必须继续绑定原 Manager 接收者，尤其弹出遮罩的关闭入口。
class EnhancerApt.MapUiOptions {
    public static function controlLabel(manager: Object, node: Object): String {
        if (node.type == "Badge" || node.type == "Stepper") {
            return node.controlValue;
        }
        if (node.type == "Select" || node.type == "Tabs") {
            for (var i = 0; i < node.options.length; i++) {
                if (node.options[i].key == node.controlValue) {
                    return node.options[i].label;
                }
            }
        }
        return node.props.Content == undefined ? "" : node.props.Content;
    }

    public static function controlChange(manager: Object, node: Object, value: String): Void {
        if (!manager.isInteractive(node) || node.props.ReadOnly == "1") {
            return;
        }
        fscommand("CallGameFunction", "%MapUi?control|" + node.token + "|" + manager.encodeHex(value));
    }

    public static function activate(manager: Object, node: Object): Void {
        if (node.optionKey != undefined) {
            var owner = manager._nodes[node.internalOwner];
            if (owner) {
                manager.controlChange(owner, node.optionKey);
            }
            manager.closePopup();
        } else if (node.type == "Select") {
            if (manager._popupOwner == node.token) {
                manager.closePopup();
            } else {
                manager.openPopup(node);
            }
        } else if (node.type == "Button" || node.type == "Checkbox" || node.type == "Radio") {
            manager.emit("click", node.token);
        } else if (node.type == "Stepper") {
            var step = node.props.Step == undefined ? 1 : Number(node.props.Step);
            var direction = node.object.sprite()._xmouse < node.width / 2 ? -1 : 1;
            manager.controlChange(node, String(Number(node.controlValue) + step * direction));
        } else if (node.type == "Slider") {
            manager.changeSlider(node);
        }
    }

    public static function changeSlider(manager: Object, node: Object): Void {
        var minimum = node.props.Min == undefined ? 0 : Number(node.props.Min);
        var maximum = node.props.Max == undefined ? 1 : Number(node.props.Max);
        var ratio = manager.clamp(node.object.sprite()._xmouse / Math.max(1, node.width), 0, 1);
        manager.controlChange(node, String(minimum + (maximum - minimum) * ratio));
    }

    public static function inheritedTheme(manager: Object, node: Object): String {
        if (node.props.Theme != undefined) {
            return node.props.Theme;
        }
        var parent = manager._nodes[node.parent];
        return parent == undefined ? "auto" : manager.inheritedTheme(parent);
    }

    public static function removeOptions(manager: Object, node: Object): Void {
        var tokens = [];
        for (var token in manager._nodes) {
            if (manager._nodes[token].internalOwner == node.token && manager._nodes[token].optionKey != undefined) {
                tokens.push(token);
            }
        }
        for (var i = 0; i < tokens.length; i++) {
            manager.destroyNode(tokens[i]);
        }
    }

    public static function choice(manager: Object, owner: Object, parent: String, index: Number,
                                  x: Number, y: Number, width: Number, height: Number): Void {
        var option = owner.options[index];
        var token = "~option:" + owner.token + ":" + index;
        var previous = manager._nodes[token];
        if (previous != undefined && previous.optionKey != option.key) {
            // 同位置换了业务选项时结束旧按压，避免释放时意外激活新选项。
            manager.destroyNode(token);
        }
        manager.createNode(token, "Button", parent);
        var row = manager._nodes[token];
        row.internalOwner = owner.token;
        row.optionKey = option.key;
        row.props = {Content: option.label, Skin: owner.skin, Theme: manager.inheritedTheme(owner),
                     Transition: "0.10", TextAlignX: owner.type == "Tabs" ? "center" : "left"};
        row.selected = option.key == owner.controlValue;
        manager.clipRectangle(token, x + "," + y + "," + width + "," + height);
        manager.rectangle(token, x + "," + y + "," + width + "," + height + "," +
                          (owner.visible ? "1" : "0") + "," + (owner.disabled || option.disabled ? "1" : "0"));
        if (owner.type != "Select") {
            var clip = owner.clip;
            var left = Math.max(x, clip.x);
            var top = Math.max(y, clip.y);
            manager.clipRectangle(token, left + "," + top + "," +
                                  Math.max(0, Math.min(x + width, clip.x + clip.width) - left) + "," +
                                  Math.max(0, Math.min(y + height, clip.y + clip.height) - top));
        }
    }

    public static function renderOptions(manager: Object, node: Object): Void {
        var count = node.width > 0 && node.height > 0 ? node.options.length : 0;
        var retained = {};
        for (var i = 0; i < count; i++) {
            retained["~option:" + node.token + ":" + i] = true;
        }
        var obsolete = [];
        for (var token in manager._nodes) {
            var row = manager._nodes[token];
            if (row.internalOwner == node.token && row.optionKey != undefined && retained[token] != true) {
                obsolete.push(token);
            }
        }
        for (var i = 0; i < obsolete.length; i++) {
            manager.destroyNode(obsolete[i]);
        }
        // 重排和选择变化复用仍在的行，鼠标按下与释放之间不销毁命中对象。
        for (var i = 0; i < count; i++) {
            var w = node.type == "Tabs" ? node.width / Math.max(1, count) : node.width;
            var h = node.type == "Tabs" ? node.height : 28;
            manager.choice(node, node.token, i, node.x + (node.type == "Tabs" ? i * w : 0),
                           node.y + (node.type == "Tabs" ? 0 : i * h), w, h);
        }
        node.object.sprite().tv._visible = false;
    }

    public static function closePopup(manager: Object): Void {
        var owner = manager._popupOwner;
        manager._popupOwner = "";
        manager._layers.setPopup("");
        if (owner != "") {
            fscommand("CallGameFunction", "%MapUi?popup|" + owner + "|0");
        }
        if (owner != "") {
            manager.destroyNode("~popup:" + owner);
        }
    }

    public static function openPopup(manager: Object, node: Object): Void {
        manager.closePopup();
        manager._popupStart = 0;
        manager.closeTooltip();
        if (node.options.length == 0) {
            return;
        }
        manager._popupOwner = node.token;
        fscommand("CallGameFunction", "%MapUi?popup|" + node.token + "|1");
        var token = "~popup:" + node.token;
        manager.createNode(token, "Container", "0");
        var popup = manager._nodes[token];
        popup.internalOwner = node.token;
        popup.props = {Skin: node.skin, Theme: manager.inheritedTheme(node)};
        var width = Math.min(Math.max(120, node.width), manager._world.width());
        var height = Math.min(node.options.length * 28, Math.floor(manager._world.height() / 28) * 28);
        var x = manager.clamp(node.x, 0, Math.max(0, manager._world.width() - width));
        var y = node.y + node.height;
        if (y + height > manager._world.height()) {
            y = Math.max(0, node.y - height);
        }
        manager.clipRectangle(token, x + "," + y + "," + width + "," + height);
        manager.rectangle(token, x + "," + y + "," + width + "," + height + ",1,0");
        manager._layers.setPopup("widget:" + token);
        var shield = popup.object.sprite().modalShield;
        shield._visible = true;
        shield.enabled = true;
        shield._alpha = 0;
        shield._x = -x - manager._originX;
        shield._y = -y - manager._originY;
        shield._width = manager._world.width();
        shield._height = manager._world.height();
        shield.onPress = Bind.noArg(manager, manager.closePopup);
        manager.renderPopupRows(node, popup);
    }

    public static function renderPopupRows(manager: Object, owner: Object, popup: Object): Void {
        manager.removeOptions(owner);
        var count = Math.floor(popup.height / 28);
        manager._popupStart = manager.clamp(manager._popupStart, 0, Math.max(0, owner.options.length - count));
        for (var i = manager._popupStart; i < Math.min(owner.options.length, manager._popupStart + count); i++) {
            manager.choice(owner, popup.token, i, popup.x, popup.y + (i - manager._popupStart) * 28, popup.width, 28);
        }
    }

    public static function wheel(manager: Object, delta: String, px: String, py: String): Void {
        manager.closeTooltip();
        var steps = Number(delta);
        if (steps == 0 || isNaN(steps)) {
            return;
        }
        if (manager._popupOwner != "" && manager._nodes[manager._popupOwner]) {
            manager._popupStart -= steps > 0 ? 3 : -3;
            manager.renderPopupRows(manager._nodes[manager._popupOwner], manager._nodes["~popup:" + manager._popupOwner]);
            return;
        }
        var x = Number(px);
        var y = Number(py);
        var target;
        for (var token in manager._nodes) {
            var node = manager._nodes[token];
            if (node.type == "Scroll" && manager.isLive(node) && x >= node.clip.x && y >= node.clip.y &&
                x < node.clip.x + node.clip.width && y < node.clip.y + node.clip.height &&
                (target == undefined || manager.aboveWithinRoot(node, target))) {
                target = node;
            }
        }
        if (target) {
            var offset = manager.clamp(target.scrollY - steps * 36, 0,
                                       Math.max(0, target.contentHeight - target.height));
            fscommand("CallGameFunction", "%MapUi?scroll|" + target.token + "|" + target.scrollX + "|" + offset);
        }
    }

    public static function closeTooltip(manager: Object): Void {
        manager._tooltipOwner = "";
        manager.destroyNode("~tooltip");
        if (manager._popupOwner == "") {
            manager._layers.setPopup("");
        }
    }

    public static function showTooltip(manager: Object, node: Object): Void {
        if (manager._nodes["~tooltip"] || !manager.isLive(node) || !node.hover) {
            return;
        }
        manager.createNode("~tooltip", "Tooltip", "0");
        var tip = manager._nodes["~tooltip"];
        tip.internalOwner = node.token;
        tip.props = {Content: node.props.Tooltip, Skin: node.skin, Theme: manager.inheritedTheme(node),
                     Wrap: "1", Size: "14", Background: "1185564", BackgroundAlpha: "98"};
        var width = Math.min(280, manager._world.width());
        var height = Math.min(100, manager._world.height());
        var x = manager.clamp(node.x + 12, 0, manager._world.width() - width);
        var y = manager.clamp(node.y + node.height + 8, 0, manager._world.height() - height);
        manager.clipRectangle("~tooltip", x + "," + y + "," + width + "," + height);
        manager.rectangle("~tooltip", x + "," + y + "," + width + "," + height + ",1,0");
        manager._layers.setPopup("widget:~tooltip");
    }
}
