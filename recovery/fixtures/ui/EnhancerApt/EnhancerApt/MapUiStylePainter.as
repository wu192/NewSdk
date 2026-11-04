import EnhancerApt.MapUiControlPainter;

// 内部拆类：绘制只接收原 MapUiManager 作为上下文，主题、动效和场景状态仍由 Manager 持有。
// manager 上的动态字段/方法是同包内部适配，不是地图公开 API，也不在此复制任何状态。
class EnhancerApt.MapUiStylePainter {
    public static function paintStyle(manager: Object, node: Object, theme: Object): Void {
        var p = node.props;
        var mc = node.object.sprite();
        mc.tv._visible = node.type != "Input" && node.type != "Image" &&
            node.type != "Container" && node.type != "Column" && node.type != "Row" && node.type != "Grid" &&
            node.type != "Scroll" && node.type != "Dialog";
        mc.image._visible = node.imageReady && p.Texture != undefined &&
            (node.type == "Image" || node.type == "Button");
        if (mc.tv) {
            mc.tv.text = manager.controlLabel(node);
            mc.tv.autoSize = false;
            mc.tv.multiline = node.type != "Input" && (p.Wrap == "1" || p.Multiline == "1");
            mc.tv.wordWrap = p.Wrap == "1";
            var format = mc.tv.getTextFormat();
            format.size = theme.size;
            format.align = theme.align;
            if (theme.font != undefined) {
                format.font = theme.font;
            }
            mc.tv.setTextFormat(format);
            mc.tv.textColor = theme.textColor;
        }
        if (mc.TextFieldMC) {
            var inputFormat = mc.TextFieldMC.dataTF.getTextFormat();
            inputFormat.size = theme.size;
            inputFormat.align = theme.align;
            if (theme.font != undefined) {
                inputFormat.font = theme.font;
            }
            mc.TextFieldMC.dataTF.setTextFormat(inputFormat);
            mc.TextFieldMC.dataTF.textColor = theme.textColor;
            if (mc.placeholder) {
                mc.placeholder.setTextFormat(inputFormat);
            }
        }
        if (mc.placeholder) {
            mc.placeholder.text = p.Placeholder == undefined ? "" : p.Placeholder;
        }
        // MapUiChrome 自带背景，启用时隐藏旧背景和边框条，避免叠加绘制。
        // 缺少该资源或节点类型不支持时，保留旧视觉路径。
        var chrome = mc.chrome;
        var chromeOn = chrome != undefined && theme.chromeOn == true;
        if (chrome != undefined) {
            if (chromeOn) {
                manager._theme.paintChrome(chrome, node.type, node.width, node.height, theme);
            } else {
                chrome._visible = false;
            }
        }
        mc.bg._visible = chromeOn ? false : theme.bgVisible;
        mc.bg._alpha = theme.bgAlpha;
        if (theme.bgVisible && !chromeOn) {
            (new Color(mc.bg)).setRGB(theme.bgColor);
        }
        var border = theme.borderWidth;
        var names = ["borderTop", "borderRight", "borderBottom", "borderLeft"];
        var colors = [theme.borderLight, theme.borderDark, theme.borderDark, theme.borderLight];
        for (var i = 0; i < names.length; i++) {
            mc[names[i]]._visible = chromeOn ? false : border > 0;
            (new Color(mc[names[i]])).setRGB(colors[i]);
        }
        mc.borderTop._width = node.width;
        mc.borderTop._height = border;
        mc.borderBottom._y = node.height - border;
        mc.borderBottom._width = node.width;
        mc.borderBottom._height = border;
        mc.borderLeft._width = border;
        mc.borderLeft._height = node.height;
        mc.borderRight._x = node.width - border;
        mc.borderRight._width = border;
        mc.borderRight._height = node.height;
        mc._alpha = theme.alpha * theme.opacity / 100 *
            (node.displayAlpha == undefined ? 1 : node.displayAlpha / 100);
        MapUiControlPainter.paint(node, theme);
        // 开启指针事件的 Image 需要可命中的 hitRegion；_alpha 仍为 0，不产生视觉变化。
        // Tabs/Menu 的鼠标事件由内部选项行负责；父控件仍是键盘焦点和语义操作目标。
        var hitVisible = (manager.isInteractive(node) && node.type != "Tabs" && node.type != "Menu") ||
            manager.isPointerEnabled(node);
        mc.hitRegion._visible = hitVisible;
        mc.hitRegion.enabled = manager._sceneActive && hitVisible && node.visible && !node.disabled;
    }
}
