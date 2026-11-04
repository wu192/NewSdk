// RA3 地图 Widget 的视觉策略。MapUiManager 管理悬停、按下、选中、焦点及禁用状态，
// 本类统一决定外观，避免视觉规则与交互处理互相耦合。
// 两种绘制路径互斥：回退路径只修改已有背景、四边边框、文字及图像；
// chrome 路径使用自带背景和框架的 MapUiChrome，并消费相同的 resolve() 结果。
// 其命名 MapUiFill 子对象均为 1×1 纯色矩形，不使用绘图 API、渐变、发光或鼠标处理器。
// 仅在属性、几何或交互状态变化时重新解析和绘制，不逐帧分配对象。
// 所有 chrome 层裁限在 Widget 矩形内；过小的节点隐藏装饰，避免越界。
class EnhancerApt.MapUiTheme {
    private var _faction: String;
    private var _factionRaw: String;
    private var _accent: Number;
    private var _accentDeep: Number;
    private var _surfaceButton: Number;
    private var _surfaceInput: Number;
    private var _surfaceImage: Number;
    private var _surfaceText: Number;
    private var _surfacePanel: Number;
    private var _surfacePressed: Number;
    private var _surfaceDisabled: Number;
    private var _bevelLight: Number;
    private var _bevelDark: Number;
    private var _bevelDisabledLight: Number;
    private var _bevelDisabledDark: Number;
    private var _textWarm: Number;
    private var _textDisabled: Number;
    private var _borderWidth: Number;
    private var _size: Number;
    private var _align: String;
    private var _font: String;
    private var _backgroundAlpha: Number;
    // Panels/columns keep an inset title band no higher than this.
    private var _chromeTitleMax: Number;

    public function MapUiTheme() {
        // Dark charcoal / slate surfaces. Inputs sit one shade deeper (recessed).
        _surfaceButton = 0x262B31;
        _surfaceInput = 0x14171B;
        _surfaceImage = 0x1E2227;
        _surfaceText = 0x202429;
        _surfacePanel = 0x1B1F23;
        _surfacePressed = 0x1A1E22;
        _surfaceDisabled = 0x181B1E;
        // Metallic bevel: light on top / left, dark on bottom / right.
        _bevelLight = 0x8C959E;
        _bevelDark = 0x0D1013;
        _bevelDisabledLight = 0x3D4348;
        _bevelDisabledDark = 0x111417;
        _textWarm = 0xEDE6D7;
        // Muted but still readable on the disabled surface.
        _textDisabled = 0x938D83;
        _borderWidth = 1;
        _size = 14;
        _align = "left";
        // 未显式设置 Props.Font 时，保留资源中制作好的字体。
        _font = undefined;
        _backgroundAlpha = 100;
        _chromeTitleMax = 40;
        _faction = "";
        _factionRaw = undefined;
        _accent = 0x8A939C;
        _accentDeep = 0x515860;
        applyFaction();
    }

    // 将游戏阵营名规范化；帝国在游戏侧使用 Japan。
    public static function factionForName(raw: String): String {
        var key = normalized(raw);
        if (key == "allies") {
            return "Allies";
        }
        if (key == "soviet") {
            return "Soviet";
        }
        if (key == "japan" || key == "empire") {
            return "Empire";
        }
        if (key == "celestial") {
            return "Celestial";
        }
        return "Neutral";
    }

    // Flash MX 2004 的 AS2 String 没有 trim，须手动去除首尾空白。
    public static function normalized(raw: String): String {
        if (raw == undefined || raw == null) {
            return "";
        }
        var text = String(raw).toLowerCase();
        var start = 0;
        var end = text.length;
        while (start < end && text.charAt(start) == " ") {
            start++;
        }
        while (end > start && text.charAt(end - 1) == " ") {
            end--;
        }
        return text.substring(start, end);
    }

    // 混合两个 0xRRGGBB 颜色；amount 为 0 保留 base，为 1 使用 target。
    public static function blend(base: Number, target: Number, amount: Number): Number {
        if (amount <= 0) {
            return Number(base);
        }
        if (amount > 1) {
            amount = 1;
        }
        var br = (Number(base) >> 16) & 0xFF;
        var bg = (Number(base) >> 8) & 0xFF;
        var bb = Number(base) & 0xFF;
        var tr = (Number(target) >> 16) & 0xFF;
        var tg = (Number(target) >> 8) & 0xFF;
        var tb = Number(target) & 0xFF;
        var r = Math.round(br + (tr - br) * amount);
        var g = Math.round(bg + (tg - bg) * amount);
        var b = Math.round(bb + (tb - bb) * amount);
        if (r < 0) {
            r = 0;
        }
        if (r > 255) {
            r = 255;
        }
        if (g < 0) {
            g = 0;
        }
        if (g > 255) {
            g = 255;
        }
        if (b < 0) {
            b = 0;
        }
        if (b > 255) {
            b = 255;
        }
        return (r << 16) | (g << 8) | b;
    }

    // Current canonical faction, re-read from _root.currentFaction on change.
    public function faction(): String {
        var raw = currentFactionRaw();
        if (raw != _factionRaw) {
            _factionRaw = raw;
            applyFaction();
        }
        return _faction;
    }

    // Skin='ra3' 启用主题，'plain' 或其他显式值使用旧视觉；
    // 未设置 Skin 时继承最近祖先解析后的皮肤。
    public function skinFor(explicit: String, inherited: String): String {
        if (explicit == undefined || explicit == null) {
            return inherited == "ra3" ? "ra3" : "plain";
        }
        return normalized(explicit) == "ra3" ? "ra3" : "plain";
    }

    // Turns widget props plus interaction state into concrete clip values.
    // Returns: bgVisible, bgColor, bgAlpha, textColor, borderWidth,
    // borderLight (top/left), borderDark (bottom/right), size, font, align, alpha.
    // Extra chrome tokens consumed by paintChrome():
    // chromeOn, chromeState ("idle"/"hover"/"pressed"/"selected"/"focused"/"disabled"),
    // chromeBody, chromeBodyAlpha, chromeLight, chromeDark, chromeAccent,
    // chromeAccentDeep, chromeShine, chromeShineAlpha, chromeTitleMax, chromeIndicator,
    // stateFlags (hover/pressed/selected/focused/disabled 可同时为 true 的组合语义).
    public function resolve(
        type: String, props: Object, skin: String, hover: Boolean, pressed: Boolean, selected: Boolean,
        focused: Boolean, disabled: Boolean): Object {
        if (skin == "ra3") {
            return themed(type, props, hover, pressed, selected, focused, disabled);
        }
        return plain(props, hover, pressed, selected, focused, disabled);
    }

    private function themed(
        type: String, props: Object, hover: Boolean, pressed: Boolean, selected: Boolean, focused: Boolean,
        disabled: Boolean): Object {
        if (props == undefined || props == null) {
            props = {};
        }
        // Props.Theme 只覆盖本次解析的阵营配色；auto 始终重新读取 _root，
        // 显式值不写回阵营缓存，避免污染下一次 auto。
        var theme = themeFaction(props.Theme);
        var accent = _accent;
        var accentDeep = _accentDeep;
        if (theme == "auto") {
            faction();
            accent = _accent;
            accentDeep = _accentDeep;
        } else {
            var palette = accentPair(theme);
            accent = palette.accent;
            accentDeep = palette.accentDeep;
        }
        var surface = surfaceFor(type);
        var light = _bevelLight;
        var dark = _bevelDark;
        var text = _textWarm;
        var decorated = isDecoratedType(type);
        var borderWidth = decorated ? _borderWidth : 0;
        var size = _size;
        var align = _align;
        var font = _font;
        var bgAlpha = _backgroundAlpha;
        var alpha = 100;

        // 先用显式属性覆盖主题默认值，再应用交互状态，保证覆盖优先级固定。
        if (props.Background != undefined) {
            surface = Number(props.Background);
        }
        if (props.Color != undefined) {
            text = Number(props.Color);
        }
        if (props.BorderColor != undefined) {
            light = Number(props.BorderColor);
            dark = light;
        }
        if (props.BorderWidth != undefined) {
            borderWidth = Number(props.BorderWidth);
        }
        if (props.Size != undefined) {
            size = Number(props.Size);
        }
        if (props.Font != undefined) {
            font = props.Font;
        }
        if (props.TextAlignX != undefined) {
            align = props.TextAlignX;
        }
        if (props.BackgroundAlpha != undefined) {
            bgAlpha = Number(props.BackgroundAlpha);
        }

        if (disabled) {
            // 禁用态优先并吞掉其余状态，保持轮廓但不再响应交互。
            surface = blend(surface, _surfaceDisabled, 0.82);
            light = blend(light, _bevelDisabledLight, 0.82);
            dark = blend(dark, _bevelDisabledDark, 0.82);
            text = _textDisabled;
        } else {
            if (selected) {
                // 选中先铺阵营底色，后续状态只在该底色上叠加。
                surface = blend(surface, accent, 0.22);
                light = accent;
                dark = accentDeep;
                text = blend(text, 0xFFFFFF, 0.10);
            }
            if (hover) {
                // 悬停仅提亮，不改变布局、尺寸或几何位置，避免鼠标边界抖动。
                surface = blend(surface, 0xFFFFFF, 0.09);
                light = blend(light, 0xFFFFFF, 0.22);
                dark = blend(dark, 0xFFFFFF, 0.10);
                text = blend(text, 0xFFFFFF, 0.15);
            }
            if (pressed) {
                // 按下继续压暗已选底色并翻转斜边，形成可叠加的下凹反馈。
                surface = blend(surface, 0x000000, selected ? 0.34 : 0.22);
                if (selected) {
                    surface = blend(surface, accent, 0.18);
                }
                var raised = light;
                light = blend(raised, _bevelDark, 0.85);
                dark = blend(raised, 0xFFFFFF, 0.12);
                text = blend(text, 0xFFFFFF, 0.24);
            }
            if (focused) {
                // 焦点框最后覆盖四边，与选中底色、悬停提亮或按下下凹同时存在。
                if (!selected) {
                    surface = blend(surface, accent, 0.12);
                }
                light = accent;
                dark = accentDeep;
            }
        }

        // Chrome state mirrors the interaction chain that already won above.
        var chromeState = "idle";
        if (disabled) {
            chromeState = "disabled";
        } else if (pressed) {
            chromeState = "pressed";
        } else if (selected) {
            chromeState = "selected";
        } else if (focused) {
            chromeState = "focused";
        } else if (hover) {
            chromeState = "hover";
        }
        var chromeAccent = accent;
        var chromeAccentDeep = accentDeep;
        var chromeIndicator = false;
        var chromeShineAlpha = 0;
        if (decorated) {
            if (disabled) {
                // 禁用控件保留轮廓，但丢失活动强调色。
                chromeAccent = blend(accent, _surfaceDisabled, 0.78);
                chromeAccentDeep = blend(accentDeep, _surfaceDisabled, 0.78);
            } else if (isPanelType(type)) {
                // 面板始终在标题带下保留阵营强调条。
                chromeIndicator = true;
            } else if (selected || focused) {
                // 控件只在选中或聚焦时抬起强调条。
                chromeIndicator = true;
            }
            // 上沿高光属于凸起控件表面；按下或禁用时取消，保持硬边反馈。
            if (isControlType(type) && !disabled && !pressed) {
                chromeShineAlpha = 38;
            }
        }

        var chromeShine = blend(surface, 0xFFFFFF, 0.38);
        return {
            skin: "ra3",
            bgVisible: decorated || props.Background != undefined,
            bgColor: surface,
            bgAlpha: bgAlpha,
            textColor: text,
            borderWidth: borderWidth,
            borderLight: light,
            borderDark: dark,
            size: size,
            font: font,
            align: align,
            alpha: alpha,
            chromeOn: decorated,
            chromeState: chromeState,
            stateFlags: stateFlags(hover, pressed, selected, focused, disabled),
            chromeBody: surface,
            chromeBodyAlpha: bgAlpha,
            chromeLight: light,
            chromeDark: dark,
            chromeAccent: chromeAccent,
            chromeAccentDeep: chromeAccentDeep,
            chromeShine: chromeShine,
            chromeShineAlpha: chromeShineAlpha,
            chromeTitleMax: isPanelType(type) ? _chromeTitleMax : 0,
            chromeIndicator: chromeIndicator
        };
    }

    // Legacy visuals: exactly the pre-theme behavior of MapUiManager.style().
    private function plain(
        props: Object, hover: Boolean, pressed: Boolean, selected: Boolean, focused: Boolean,
        disabled: Boolean): Object {
        if (props == undefined || props == null) {
            props = {};
        }
        var borderWidth = props.BorderWidth == undefined ? 0 : Number(props.BorderWidth);
        var borderColor = props.BorderColor == undefined ? 0xFFFFFF : Number(props.BorderColor);
        return {
            skin: "plain",
            bgVisible: props.Background != undefined,
            bgColor: props.Background == undefined ? 0 : Number(props.Background),
            bgAlpha: props.BackgroundAlpha == undefined ? 100 : Number(props.BackgroundAlpha),
            textColor: props.Color == undefined ? 0xFFFFFF : Number(props.Color),
            borderWidth: borderWidth,
            borderLight: borderColor,
            borderDark: borderColor,
            size: props.Size == undefined ? 14 : Number(props.Size),
            font: props.Font,
            align: props.TextAlignX == undefined ? "left" : props.TextAlignX,
            alpha: disabled ? 50 : 100,
            // Plain skin never opts into chrome; the manager keeps the legacy path.
            chromeOn: false,
            chromeState: disabled ? "disabled" : "idle",
            stateFlags: stateFlags(hover, pressed, selected, focused, disabled),
            chromeBody: 0,
            chromeBodyAlpha: 0,
            chromeLight: 0,
            chromeDark: 0,
            chromeAccent: 0,
            chromeAccentDeep: 0,
            chromeShine: 0,
            chromeShineAlpha: 0,
            chromeTitleMax: 0,
            chromeIndicator: false
        };
    }

    // Props.Theme 的缺省值与 auto 跟随根阵营；显式未知值一律回退 Neutral。
    private function themeFaction(raw: String): String {
        if (raw == undefined || raw == null) {
            return "auto";
        }
        var key = normalized(raw);
        if (key == "auto") {
            return "auto";
        }
        if (key == "allies") {
            return "Allies";
        }
        if (key == "soviet") {
            return "Soviet";
        }
        if (key == "empire" || key == "japan") {
            return "Empire";
        }
        if (key == "celestial") {
            return "Celestial";
        }
        return "Neutral";
    }

    // 阵营只决定强调色；所有面板与控件共享同一套固定几何结构。
    private function accentPair(factionName: String): Object {
        if (factionName == "Allies") {
            return {accent: 0x8CCBE4, accentDeep: 0x4E7F97};
        }
        if (factionName == "Soviet") {
            return {accent: 0xB0584E, accentDeep: 0x6E332C};
        }
        if (factionName == "Empire") {
            return {accent: 0xD2A24C, accentDeep: 0x8A6A2E};
        }
        if (factionName == "Celestial") {
            return {accent: 0x9C7BD6, accentDeep: 0x5E4A85};
        }
        return {accent: 0x8A939C, accentDeep: 0x515860};
    }

    private function isPanelType(type: String): Boolean {
        return type == "Container" || type == "Column" || type == "Dialog" || type == "Scroll" ||
            type == "Grid" || type == "Menu" || type == "Tooltip";
    }

    private function isControlType(type: String): Boolean {
        return type == "Button" || type == "Select" || type == "Tabs" || type == "Stepper" ||
            type == "Checkbox" || type == "Radio";
    }

    private function isDecoratedType(type: String): Boolean {
        return isPanelType(type) || isControlType(type) || type == "Input";
    }

    // 各状态可同时为真；chromeState 仍保留既有的单状态优先级。
    private function stateFlags(
        hover: Boolean, pressed: Boolean, selected: Boolean, focused: Boolean, disabled: Boolean): Object {
        return {
            hover: hover == true,
            pressed: pressed == true,
            selected: selected == true,
            focused: focused == true,
            disabled: disabled == true
        };
    }

    private function surfaceFor(type: String): Number {
        if (type == "Input") {
            return _surfaceInput;
        }
        if (isControlType(type)) {
            return _surfaceButton;
        }
        if (type == "Image") {
            return _surfaceImage;
        }
        if (type == "Text") {
            return _surfaceText;
        }
        return _surfacePanel;
    }

    // 用已解析的视觉参数绘制资源中的 MapUiChrome；启用后管理器隐藏旧背景和边框。
    // 这里只调整已有纯色矩形，不创建对象或交互处理器。fill() 限制每层不超过 w/h；
    // 过小节点只保留主体并隐藏装饰。
    public function paintChrome(chrome: MovieClip, type: String, w: Number, h: Number, r: Object): Void {
        if (chrome == undefined) {
            return;
        }
        if (r == undefined || r.chromeOn != true || w < 1 || h < 1) {
            chrome._visible = false;
            return;
        }
        chrome._visible = true;
        hideChromeParts(chrome);

        var isPanel = isPanelType(type);
        var isButton = isControlType(type);
        var isInput = type == "Input";
        var body = finiteOr(r.chromeBody, 0x1B1F23);
        var bodyAlpha = finiteOr(r.chromeBodyAlpha, 100);
        var light = finiteOr(r.chromeLight, 0x8C959E);
        var dark = finiteOr(r.chromeDark, 0x0D1013);
        var accent = finiteOr(r.chromeAccent, 0x8A939C);
        var accentDeep = finiteOr(r.chromeAccentDeep, 0x515860);
        var railOn = r.chromeIndicator == true;
        var step = finiteOr(r.borderWidth, 1);
        if (step < 0) {
            step = 0;
        }
        if (step > 3) {
            step = 3;
        }

        // The body is never transparent unless the widget asked for it explicitly.
        fill(chrome.body, 0, 0, w, h, body, bodyAlpha, w, h);

        // Tiny widgets keep only the body; decorations need room to read.
        if (w < 12 || h < 8 || step < 1) {
            return;
        }
        var lightEdge = blend(body, light, 0.62);
        var darkEdge = blend(body, dark, 0.72);

        if (isPanel) {
            // At the game's 800px viewport a one-pixel frame becomes subpixel.
            // Keep a readable native-style metal lip inside the same rectangle.
            step = 3;
            lightEdge = blend(0xADBDC4, accent, 0.28);
            darkEdge = 0x080C0F;
            var band = titleBand(finiteOr(r.chromeTitleMax, 0), h);
            fill(chrome.insetTop, 0, 0, w, step, lightEdge, 100, w, h);
            fill(chrome.innerLeft, 0, 0, step, h, lightEdge, 100, w, h);
            fill(chrome.insetBottom, 0, h - step, w, step, darkEdge, 100, w, h);
            fill(chrome.innerRight, w - step, 0, step, h, darkEdge, 100, w, h);
            if (band > step) {
                fill(chrome.titleBand, step, step, w - 2 * step, band - step, blend(body, accentDeep, 0.78), 100, w, h);
                fill(
                    chrome.titleEdge, step, band - 2, w - 2 * step, 2, railOn ? accent : accentDeep, railOn ? 100 : 80,
                    w, h);
            }
            // Two metal rails and a narrow faction strip match the native HUD's
            // inset plates. They remain inside the authored rectangle.
            fill(chrome.shine, step + 1, step + 1, w - 2 * step - 2, 2, blend(lightEdge, 0xFFFFFF, 0.30), 85, w, h);
            fill(chrome.indicator, step, band + 2, 3, h - band - step - 4, accentDeep, 85, w, h);
            var corner = panelCorner(w, h);
            fill(chrome.cornerTL, 0, 0, corner, corner, blend(lightEdge, 0xFFFFFF, 0.40), 100, w, h);
            fill(chrome.cornerTR, w - corner, 0, corner, corner, lightEdge, 100, w, h);
            fill(chrome.cornerBL, 0, h - corner, corner, corner, darkEdge, 100, w, h);
            fill(chrome.cornerBR, w - corner, h - corner, corner, corner, blend(darkEdge, 0x000000, 0.40), 100, w, h);
            var inset = step + 2;
            var rivet = panelRivet(w, h);
            fill(chrome.rivetTL, inset, inset, rivet, rivet, blend(lightEdge, 0xFFFFFF, 0.35), 95, w, h);
            fill(chrome.rivetTR, w - inset - rivet, inset, rivet, rivet, blend(lightEdge, 0xFFFFFF, 0.20), 90, w, h);
            fill(chrome.rivetBL, inset, h - inset - rivet, rivet, rivet, blend(darkEdge, 0xFFFFFF, 0.18), 90, w, h);
            fill(chrome.rivetBR, w - inset - rivet, h - inset - rivet, rivet, rivet, darkEdge, 95, w, h);
            return;
        }

        if (isInput) {
            // Recessed: dark shadow on top/left, catch light on bottom/right.
            var recess = step < 2 ? 2 : step;
            fill(chrome.insetTop, 0, 0, w, recess, darkEdge, 100, w, h);
            fill(chrome.innerLeft, 0, 0, recess, h, darkEdge, 100, w, h);
            fill(chrome.insetBottom, 0, h - recess, w, recess, lightEdge, 100, w, h);
            fill(chrome.innerRight, w - recess, 0, recess, h, lightEdge, 100, w, h);
            // `shine` doubles as the hard inner shadow of the recessed well.
            var innerShadow = h - 2 * recess - 2 > 0 ? 2 : 1;
            fill(chrome.shine, recess, recess, w - 2 * recess, innerShadow, blend(body, 0x000000, 0.55), 100, w, h);
            if (railOn) {
                fill(chrome.indicator, recess + 2, h - recess - 3, w - 2 * recess - 4, 2, accent, 100, w, h);
            }
            return;
        }

        if (isButton) {
            // Raised two-step face: outer light step, face body, dark lower step.
            var face = step < 2 ? 2 : step;
            fill(chrome.insetTop, 0, 0, w, face, lightEdge, 100, w, h);
            fill(chrome.innerLeft, 0, 0, face, h, lightEdge, 100, w, h);
            fill(chrome.insetBottom, 0, h - face, w, face, darkEdge, 100, w, h);
            fill(chrome.innerRight, w - face, 0, face, h, darkEdge, 100, w, h);
            var sheenAlpha = finiteOr(r.chromeShineAlpha, 0);
            if (sheenAlpha > 0) {
                // Hard upper sheen band, never a gradient.
                var sheen = Math.round((h - 2 * face) * 0.40);
                if (sheen < 2) {
                    sheen = 2;
                }
                if (sheen > 4) {
                    sheen = 4;
                }
                fill(
                    chrome.shine, face, face, w - 2 * face, sheen, finiteOr(r.chromeShine, 0xFFFFFF), sheenAlpha, w, h);
            }
            if (railOn) {
                fill(chrome.indicator, face + 2, h - face - 3, w - 2 * face - 4, 2, accent, 100, w, h);
            }
            return;
        }
    }

    // Panels keep the inset title band well inside the frame and never above 40.
    private function titleBand(titleMax: Number, h: Number): Number {
        if (titleMax <= 0 || h < 20) {
            return 0;
        }
        var band = Math.round(h * 0.3);
        if (band > titleMax) {
            band = titleMax;
        }
        if (band < 10) {
            band = 10;
        }
        if (band > h - 6) {
            band = h - 6;
        }
        if (band < 0) {
            band = 0;
        }
        return band;
    }

    private function panelCorner(w: Number, h: Number): Number {
        if (w < 16 || h < 16) {
            return 2;
        }
        return 7;
    }

    private function panelRivet(w: Number, h: Number): Number {
        if (w < 20 || h < 14) {
            return 1;
        }
        return 2;
    }

    // 放置已有 MapUiFill 层，并将其范围裁限在 Widget 矩形内。
    private function fill(
        mc: MovieClip, x: Number, y: Number, fw: Number, fh: Number, color: Number, alpha: Number, w: Number,
        h: Number): Void {
        if (mc == undefined) {
            return;
        }
        if (x < 0) {
            fw = fw + x;
            x = 0;
        }
        if (y < 0) {
            fh = fh + y;
            y = 0;
        }
        if (x + fw > w) {
            fw = w - x;
        }
        if (y + fh > h) {
            fh = h - y;
        }
        if (fw <= 0 || fh <= 0) {
            mc._visible = false;
            return;
        }
        mc._visible = true;
        mc._x = x;
        mc._y = y;
        mc._width = fw;
        mc._height = fh;
        new Color(mc).setRGB(color);
        mc._alpha = alpha;
    }

    private function hideChromeParts(chrome: MovieClip): Void {
        hideChromePart(chrome.body);
        hideChromePart(chrome.insetTop);
        hideChromePart(chrome.insetBottom);
        hideChromePart(chrome.innerLeft);
        hideChromePart(chrome.innerRight);
        hideChromePart(chrome.titleBand);
        hideChromePart(chrome.titleEdge);
        hideChromePart(chrome.shine);
        hideChromePart(chrome.indicator);
        hideChromePart(chrome.cornerTL);
        hideChromePart(chrome.cornerTR);
        hideChromePart(chrome.cornerBL);
        hideChromePart(chrome.cornerBR);
        hideChromePart(chrome.rivetTL);
        hideChromePart(chrome.rivetTR);
        hideChromePart(chrome.rivetBL);
        hideChromePart(chrome.rivetBR);
    }

    private function hideChromePart(mc: MovieClip): Void {
        if (mc != undefined) {
            mc._visible = false;
        }
    }

    private function finiteOr(raw: Object, fallback: Number): Number {
        var value = Number(raw);
        if (isNaN(value)) {
            return fallback;
        }
        return value;
    }

    private function applyFaction(): Void {
        _faction = factionForName(_factionRaw);
        var palette = accentPair(_faction);
        _accent = palette.accent;
        _accentDeep = palette.accentDeep;
    }

    private function currentFactionRaw(): String {
        if (typeof _root == "undefined" || _root == undefined) {
            return "";
        }
        if (_root.currentFaction == undefined) {
            return "";
        }
        return String(_root.currentFaction);
    }
}
