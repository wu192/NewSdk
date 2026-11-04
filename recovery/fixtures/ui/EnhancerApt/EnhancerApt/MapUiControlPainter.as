// 控件绘制自 MapUiManager 机械拆出，仅用于避免单类字节码溢出；数值与顺序保持不变。
class EnhancerApt.MapUiControlPainter {
    private static function controlPart(part: MovieClip, x: Number, y: Number, w: Number, h: Number,
                                        color: Number): Void {
        part._visible = w > 0 && h > 0;
        part._x = x;
        part._y = y;
        part._width = Math.max(0, w);
        part._height = Math.max(0, h);
        (new Color(part)).setRGB(color);
    }

    public static function paint(node: Object, theme: Object): Void {
        var parts = node.object.sprite().visual.controls;
        for (var key in parts) {
            if (parts[key]._visible != undefined) {
                parts[key]._visible = false;
                parts[key]._rotation = 0;
            }
        }
        var w = node.width;
        var h = node.height;
        var color = theme.chromeAccent;
        if (node.skin != "ra3") {
            color = theme.textColor;
        }
        var type = node.type;
        if (type == "Progress" || type == "Cooldown" || type == "Slider") {
            var minimum = node.props.Min == undefined ? 0 : Number(node.props.Min);
            var maximum = node.props.Max == undefined ? 1 : Number(node.props.Max);
            var ratio = maximum == minimum ? 0 : clamp((theme.controlNumber - minimum) / (maximum - minimum), 0, 1);
            controlPart(parts.track, 0, h / 2 - 3, w, 6, 0x0C1118);
            controlPart(parts.fill, 0, h / 2 - 2, w * ratio, 4, color);
            if (type == "Slider") {
                controlPart(parts.thumb, clamp(w * ratio - 4, 0, Math.max(0, w - 8)), h / 2 - 8, 8, 16, color);
            }
        }
        if (type == "Checkbox" || type == "Radio") {
            var size = Math.min(18, Math.min(w, h));
            controlPart(parts.track, 0, (h - size) / 2, size, size, theme.borderDark);
            controlPart(parts.fill, 1, (h - size) / 2 + 1, size - 2, size - 2, theme.bgColor);
            if (node.controlValue == "1") {
                if (type == "Radio") {
                    controlPart(parts.markA, size / 2, h / 2 - 5, 7, 7, color);
                    parts.markA._rotation = 45;
                } else {
                    controlPart(parts.markA, 3, h / 2, 7, 2, color);
                    parts.markA._rotation = 45;
                    controlPart(parts.markB, 7, h / 2 + 5, 11, 2, color);
                    parts.markB._rotation = -50;
                }
            }
            node.object.sprite().tv._x = size + 8;
            node.object.sprite().tv._width = Math.max(0, w - size - 8);
        }
        if (type == "Stepper") {
            var edge = Math.min(18, w / 4);
            controlPart(parts.markA, 6, h / 2 - 1, edge - 4, 2, color);
            controlPart(parts.markB, w - edge - 2, h / 2 - 1, edge - 4, 2, color);
            controlPart(parts.tick, w - edge / 2 - 5, h / 2 - 5, 2, 10, color);
            node.object.sprite().tv._x = edge + 6;
            node.object.sprite().tv._width = Math.max(0, w - 2 * edge - 12);
            var stepperFormat = node.object.sprite().tv.getTextFormat();
            stepperFormat.align = "center";
            node.object.sprite().tv.setTextFormat(stepperFormat);
        }
        if (type == "Select") {
            controlPart(parts.chevronA, w - 18, h / 2 - 2, 7, 2, color);
            parts.chevronA._rotation = 45;
            controlPart(parts.chevronB, w - 13, h / 2 + 3, 7, 2, color);
            parts.chevronB._rotation = -45;
            node.object.sprite().tv._width = Math.max(0, w - 24);
        }
        if (node.focused) {
            controlPart(parts.focusTop, 0, 0, w, 1, color);
            controlPart(parts.focusBottom, 0, h - 1, w, 1, color);
            controlPart(parts.focusLeft, 0, 0, 1, h, color);
            controlPart(parts.focusRight, w - 1, 0, 1, h, color);
        }
    }

    private static function clamp(value: Number, low: Number, high: Number): Number {
        if (value < low) {
            return low;
        }
        if (value > high) {
            return high;
        }
        return value;
    }
}
