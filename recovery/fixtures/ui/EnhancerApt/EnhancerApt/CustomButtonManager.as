import EnhancerApt.Bind;
import EnhancerApt.DepthSorter;
import EnhancerApt.MapUiLayers;
import EnhancerApt.GameObject;
import EnhancerApt.World;

class EnhancerApt.CustomButtonManager {
    private var _layers: MapUiLayers;
    private var _container: MovieClip;
    private var _textContainer: MovieClip;
    private var _virtualListBackground: MovieClip;
    private var _btns: Object;
    private var _texts: Object;
    private var _world: World;
    private var _customButtonWorld: World;
    private var _customTextWorld: World;

    private var _contextMenu: MovieClip;
    private var _contextMenuButtonIndex: Number;

    private var _delayInitId: Object;
    private var _buttonImageStyles: Object;

    public function CustomButtonManager(screen: MovieClip, world: World, layers: MapUiLayers) {
        trace("CustomButtonManager init");

        _world = world;
        // _container = screen.createEmptyMovieClip("TestEAButton1", 1101);
        // CustomButtonLayer是fla里的一个MovieClip
        var virtualListBackgroundHolder: GameObject = _world.instantiate("SideBarStructureVirtualCommandListBackground");
        _layers = layers;
        _layers.ensureAbove(virtualListBackgroundHolder);
        _container = layers.holder().sprite();
        _textContainer = _container;
        _virtualListBackground = virtualListBackgroundHolder.sprite();
        _customButtonWorld = layers.sharedWorld();
        _customTextWorld = _customButtonWorld;
        _virtualListBackground._x = 0;
        _virtualListBackground._y = 0;
        _virtualListBackground._visible = false;
        _btns = {}
        _texts = {}
        _delayInitId = {};
        _buttonImageStyles = {};
        _container._visible = true;

        _contextMenu = _world.instantiate("ScreenCenterTopBtnContext").sprite();
        _contextMenu._x = -400;
        _contextMenu._y = -400;
        _contextMenuButtonIndex = undefined;

        trace("CustomButtonManager init variables done, now registering functions to screen");

        screen.ShowSideBarStructureVirtualCommandList = Bind.fourArg(this, function(x: Number, y: Number, width: Number, height: Number) {
            _virtualListBackground._visible = true;
            _virtualListBackground._x = x;
            _virtualListBackground._y = y;
            // _virtualListBackground._width = width;
            // _virtualListBackground._height = height;
            switch (_root.currentFaction) {
                case "Allies":
                    _virtualListBackground.gotoAndStop("Allies");
                    break;
                case "Japan":
                    _virtualListBackground.gotoAndStop("Empire");
                    break;
                case "Soviet":
                    _virtualListBackground.gotoAndStop("Soviet");
                    break;
                case "Celestial":
                    _virtualListBackground.gotoAndStop("Celestial");
                    break;
                default:
                    _virtualListBackground.gotoAndStop("Allies");
                    break;
            }
        });

        screen.SideBarVirtualCommandListHide = Bind.noArg(this, function() {
            _virtualListBackground._visible = false;
        });

        screen.CustomButtonShowContextMenu = Bind.twoArg(this, function(index: Number, content: String) {
            trace("CustomButtonShowContextMenu " + String(index) + " " + content);
            var btn: MovieClip = this.getCustomButtonMc(index);
            if (!btn) {
                return;
            }
            var nx = 0;
            var ny = 0;
            if (btn._x > 500) {
                nx = btn._x - _contextMenu._width;
            } else {
                nx = btn._x + btn._width;
            }
            if (btn._y > 330) {
                ny = btn._y - (_contextMenu._height - btn._height);
            } else {
                ny = btn._y;
            }

            _contextMenu._x = nx;
            _contextMenu._y = ny;
            _contextMenu.tv.text = content;
            _contextMenuButtonIndex = index;
        });

        screen.CustomButtonHideContextMenu = Bind.twoArg(this, function(index: Number, content: String) {
            this.hideCustomButtonContextMenu(index);
        });

        // EAStandardBtn

        screen.updateCustomButtonImage = Bind.fourArg(this, function(index: Number, xpos: Number, ypos: Number, groupIndex: Number) {
            if (_btns[index]) {
                this.destroyCustomButton(index, true);
            }

            // CustomButtonLayer是fla里的一个MovieClip
            var buttonHolder: GameObject = _customButtonWorld.instantiate("CustomButtonLayer");
            var btn: MovieClip = buttonHolder.sprite();
            btn.loadMovie("StandardCommandButton.swf");
            btn._x = xpos;
            btn._y = ypos;

            //TODO override original apt to setEnable?
            _delayInitId[index] = setInterval(Bind.noArgCapture1(this, index, function(idx) {
                clearInterval(_delayInitId[idx]);
                _delayInitId[idx] = undefined;
                delete _delayInitId[idx];
                var button: MovieClip = this.getCustomButtonMc(idx);
                if (button) {
                    button.SetEnabled(true);
                    this.applyCustomImageColor(idx, button);
                }
            }), 50)

            _btns[index] = buttonHolder;
            this.registerButtonLayer(index, buttonHolder, groupIndex, true);

            var addr = _global.Pointer.encode(btn);
            fscommand("CallGameFunction", "%loadCustomBtnImage" + addr + "," + String(index));
        });

        screen.removeCustomButton = Bind.oneArg(this, function(index: Number) {
            this.destroyCustomButton(index);
        });

        screen.CustomButtonToggleVisibility = Bind.twoArg(this, function(index: Number, visibility: Number) {
            var button: MovieClip = this.getCustomButtonMc(index);
            if (!button) {
                return;
            }
            if (visibility == 1) {
                button._visible = true;
            } else {
                button._visible = false;
            }
        });

        screen.destroyAllCustomButtons = Bind.twoArg(this, function(a: String) {
            for (var prop:String in _btns) {
                this.destroyCustomButton(prop);
            }
            for (var prop:String in _texts) {
                _layers.remove("text:" + prop, false);
                _customTextWorld.destroy(_texts[prop])
                _texts[prop] = undefined;
                delete _texts[prop];
            }

            _contextMenu._x = -400;
            _contextMenu._y = -400;
            _contextMenuButtonIndex = undefined;
            _contextMenu.tv.text = "";
        });

        //////////

        screen.CustomButtonToggleHighLight = Bind.twoArg(this, function(index: Number, enable: Number) {
            var button: MovieClip = this.getCustomButtonMc(index);
            if (!button) {
                return;
            }
            button.SetHighlighted(enable);
            this.applyCustomImageColor(index, button);
        });

        screen.CustomButtonSetTimerOverlayValue = Bind.twoArg(this, function(index: Number, value: Number) {
            var button: MovieClip = this.getCustomButtonMc(index);
            if (!button) {
                return;
            }
            button.SetTimerValue(value);
        });

        screen.CustomButtonToggleTimerOverlay = Bind.twoArg(this, function(index: Number, visible: Number) {
            var button: MovieClip = this.getCustomButtonMc(index);
            if (!button) {
                return;
            }
            if (visible == 1) {
                button.SetTimerVisibility(1);
            } else {
                button.SetTimerVisibility(0);
            }
        });

        screen.CustomButtonSetHealthBarValue = Bind.twoArg(this, function(index: Number, value: Number) {
            var button: MovieClip = this.getCustomButtonMc(index);
            if (!button) {
                return;
            }
            button.SetHealthBarValue(value);
        });

        screen.CustomButtonToggleHealthBar = Bind.twoArg(this, function(index: Number, visible: Number) {
            var button: MovieClip = this.getCustomButtonMc(index);
            if (!button) {
                return;
            }
            if (visible == 1) {
                button.SetHealthBarVisibility(1);
            } else {
                button.SetHealthBarVisibility(0);
            }
        });

        screen.CustomButtonToggleSelected = Bind.twoArg(this, function(index: Number, selected: Number) {
            var button: MovieClip = this.getCustomButtonMc(index);
            if (!button) {
                return;
            }
            if (selected == 1) {
                button.SetSelected(1);
            } else {
                button.SetSelected(0);
            }
            this.applyCustomImageColor(index, button);
        });

        screen.CustomButtonToggleEnable = Bind.twoArg(this, function(index: Number, enable: Number) {
            var button: MovieClip = this.getCustomButtonMc(index);
            if (!button) {
                return;
            }
            if (enable == 1) {
                button.SetEnabled(1);
            } else {
                button.SetEnabled(0);
            }
            // 共享层焦点命中检测对导入的标准按钮和可调整尺寸的自定义按钮使用相同禁用状态。
            button.DATA_disabled = enable != 1;
            if (button.hitRegion) {
                button.hitRegion.enabled = enable == 1;
            }
            this.applyCustomImageColor(index, button);
        });

        //////////

        screen.updateCustomImageWithCustomSize = Bind.twoArg(this, function(index: Number, params: String) {
            if (_btns[index]) {
                this.destroyCustomButton(index, true);
            }

            var paramArr = params.split(",")
            var xpos: Number = Number(paramArr[0]);
            var ypos: Number = Number(paramArr[1]);
            var width: Number = Number(paramArr[2]);
            var height: Number = Number(paramArr[3]);
            var isButton: Boolean = (paramArr[4] === "true");
            var groupIndex: Number = Number(paramArr[5]);
            trace("updateCustomImageWithCustomSize " + String(index) + " " + xpos + "," + ypos + "," + width + "," + height + "," + isButton);

            var buttonHolder: GameObject = _customButtonWorld.instantiate("CustomImage");
            var btn: MovieClip = buttonHolder.sprite();
            var customButtonManager: CustomButtonManager = this;
            btn._x = xpos;
            btn._y = ypos;
            btn.image._width = width;
            btn.image._height = height;
            if (!isButton) {
                btn.hitRegion.removeMovieClip();
                btn.overlay.removeMovieClip();
            } else {
                var hitRegion: Button = btn.hitRegion;
                var overlay: MovieClip = btn.overlay;
                hitRegion._width = width;
                hitRegion._height = height;
                overlay._width = width;
                overlay._height = height;
                overlay._visible = false;

                hitRegion.onRollOver = function() {
                    if (btn.DATA_disabled === true) {
                        return;
                    }
                    overlay._visible = true;
                    var color: Color = new Color(overlay);
                    color.setRGB(0xFFFFFF);
                    overlay._alpha = 25;
                    fscommand(_global.qualifyName(btn, "OnRollOver"), "");
                };
                hitRegion.onPress = function() {
                    if (btn.DATA_disabled === true) {
                        return;
                    }
                    overlay._visible = true;
                    var color: Color = new Color(overlay);
                    color.setRGB(0x000000);
                    overlay._alpha = 25;
                };
                hitRegion.onRollOut = hitRegion.onDragOut = function() {
                    if (btn.DATA_disabled === true) {
                        return;
                    }
                    overlay._visible = false;
                    fscommand(_global.qualifyName(btn, "OnRollOut"), "");
                };
                hitRegion.onRelease = function() {
                    if (btn.DATA_disabled === true) {
                        return;
                    }
                    fscommand(_global.qualifyName(btn, "OnClicked"), "");
                    overlay._visible = false;
                };
                hitRegion.onReleaseOutside = function() {
                    overlay._visible = false;
                };
            }

            btn.SetEnabled = function(enable: Boolean) {
                if (enable) {
                    btn.DATA_disabled = false;
                    delete btn.DATA_disabled;
                    btn.gotoAndStop("_enabled");
                } else {
                    btn.DATA_disabled = true;
                    btn.gotoAndStop("_disabled");
                }
                // 回调提前返回不会把原生 Button 从 APT 命中路由中移除。
                // 必须禁用命中对象本身，下层 Input 才能收到与 frontAt() 判定一致的点击。
                if (btn.hitRegion) {
                    btn.hitRegion.enabled = enable;
                }

                var intervalId: Number = setInterval(function() {
                    clearInterval(intervalId);
                    intervalId = undefined;

                    if (btn.image) {
                        btn.image._width = width;
                        btn.image._height = height;
                    }
                    if (btn.disabledImage) {
                        btn.disabledImage._width = width;
                        btn.disabledImage._height = height;
                    }
                    customButtonManager.applyCustomImageColor(index, btn);
                }, 50);
            };

            _btns[index] = buttonHolder;
            this.registerButtonLayer(index, buttonHolder, groupIndex, isButton);

            var addr = _global.Pointer.encode(btn);
            fscommand("CallGameFunction", "%loadCustomBtnImage" + addr + "," + String(index));
        });
        trace("CustomButtonManager: screen.updateCustomImageWithCustomSize registered: " + String(screen.updateCustomImageWithCustomSize));

        screen.CustomImageSetColor = Bind.threeArg(this, function(index: Number, colorString: String, alphaString: String) {
            var button: MovieClip = this.getCustomButtonMc(index);
            if (!button) {
                return;
            }
            _buttonImageStyles[index] = {
                rgb: Number(colorString),
                alpha: Number(alphaString)
            };
            this.applyCustomImageColor(index, button);
        });

        ///////////

        screen.CustomTextCreate = Bind.threeArg(this, function(index: Number, text: String, params: String) {

            var paramArr = params.split(",")
            if (_texts[index]) {
                _layers.remove("text:" + String(index), true);
                _customTextWorld.destroy(_texts[index])
            }

            var customText: GameObject = _customTextWorld.instantiate("CustomText");
            _layers.register("text:" + String(index), customText, Number(paramArr[8]), Number(index), undefined);
            customText.setPosition({ x : Number(paramArr[0]), y: Number(paramArr[1])});
            
            var textColor: Number = Number(paramArr[2]);
            var textSize: Number = Number(paramArr[3]);
            var alignX: String = paramArr[4];
            var hasTextAlignX: Boolean = (paramArr[7] === "1");

            var fontName: String = "Lucida Sans";
            var textFormat: TextFormat = new TextFormat(fontName, textSize);
            textFormat.color = textColor;
            if (alignX === "left") {
                textFormat.align = "left";
            } else if (alignX === "right") {
                textFormat.align = "right";
            } else {
                textFormat.align = "center";
            }

            var customTextMc: MovieClip = customText.sprite();
            var textField: TextField = customTextMc.tv;
            textField._width = 1;
            textField._height = 1;
            textField.textColor = textColor;

            customTextMc.DATA_hasTextAlignX = hasTextAlignX;
            customTextMc.DATA_textAlignX = alignX;
            customTextMc.DATA_textContent = text;
            customTextMc.DATA_textFieldBaseX = textField._x;

            
            if (!isColorDark(textColor) && text.length > 0 && text.indexOf('&') == -1) {
                text = text + "&outline"; // 假如文本颜色较浅则加上描边
            }
            textField.text = text;
            textField.setTextFormat(textFormat);

            textField.wordWrap = false;
            
            if (alignX === "left") {
                textField.autoSize = "left";
            } else if (alignX === "right") {
                textField.autoSize = "right";
            } else {
                textField.autoSize = "center";
            }

            var bg = customTextMc.bg;
            customTextMc.DATA_textBgBaseX = bg._x;
            var bgColor = paramArr[5];
            if (bgColor != undefined && bgColor != null && bgColor.length > 0) {
                bg._visible = true;
                bg._width = textField._width;
                bg._height = textField._height;
                var _color = new Color(bg);
                _color.setRGB(Number(bgColor));
            } else {
                bg._visible = false;
            }
            var bgAlpha = paramArr[6];
            if (bgAlpha != undefined && bgAlpha != null && bgAlpha.length > 0) {
                bg._alpha = Number(bgAlpha);
            }

            this.applyCustomTextLayout(customTextMc);

            _texts[index] = customText;
        });

        screen.CustomTextUpdateText = Bind.twoArg(this, function(index: Number, content: String) {
            var textObject: GameObject = _texts[index];
            if (textObject) {
                var customTextMc: MovieClip = textObject.sprite();
                var textField: TextField = customTextMc.tv;
                this.resetCustomTextLayout(customTextMc);
                customTextMc.DATA_textContent = content;
                if (!isColorDark(textField.textColor) && content.length > 0 && content.indexOf('&') == -1) {
                    content = content + "&outline"; // 假如文本颜色较浅则加上描边
                }
                textField.text = content;
                this.applyCustomTextLayout(customTextMc);
            }
        });

        screen.CustomTextMove = Bind.threeArg(this, function(index: Number, xpos: Number, ypos: Number) {
            var textObject: GameObject = _texts[index];
            if (textObject) {
                textObject.setPosition({ x : xpos, y: ypos});
            }
        });

        screen.CustomTextRemove = Bind.oneArg(this, function(index: Number) {
            if (_texts[index]) {
                _layers.remove("text:" + String(index), false);
                _customTextWorld.destroy(_texts[index]);
                delete _texts[index];
            }
        });
        screen.CustomTextSetGroup = Bind.twoArg(this, function(index: Number, group: Number) {
            _layers.setGroup("text:" + String(index), group);
        });
        screen.CustomButtonSetGroup = Bind.twoArg(this, function(index: Number, group: Number) {
            _layers.setGroup("button:" + String(index), group);
        });

        screen.CustomTextToggleVisibility = Bind.twoArg(this, function(index: Number, visible: Number) {
            var textObject: GameObject = _texts[index];
            if (textObject) {
                textObject.sprite()._visible = (visible == 1);
            }
        });

        // 让某个自定义文本“变成”专门用来显示报错的文本
        // 它原先的 index 会被废弃
        screen.CustomTextIsErrorMessage = Bind.threeArg(this, function(index: Number, title: String, titleColor: Number) {
            var textIndex: String = "ErrorMsg";
            if (_texts[textIndex]) {
                return; // 假如已经有报错文本了就不再创建新的
            }
            // 将一个已有的自定义文本改造成报错文本
            var customText: GameObject = _texts[index];
            _texts[index] = undefined;
            delete _texts[index];
            if (!customText) {
                return;
            }
            // 报错信息默认不可见，通过 SetErrorMessage 来设置其内容和可见性
            customText.sprite()._visible = false;
            if (screen.g_disableErrorMessage) {
                _layers.remove("text:" + String(index), false);
                _customTextWorld.destroy(customText);
                return; // 假如用户选择了不显示报错信息就直接销毁它
            }
            _texts[textIndex] = customText;
            _layers.remove("text:" + String(index), false);
            _layers.register("text:" + textIndex, customText, 0, 0, undefined);
            
            var mc: MovieClip = customText.sprite();
            
            var textField: TextField = mc.tv;
            var titleFormat: TextFormat = textField.getTextFormat();
            titleFormat.color = titleColor;

            // 为报错信息创建一个标题
            mc.createTextField("tvTitle", 100, 0, 0, 1, 1);
            var tvTitle: TextField = mc.tvTitle;
            tvTitle.setTextFormat(titleFormat);
            tvTitle.wordWrap = false;
            tvTitle.autoSize = "left";
            if (!isColorDark(titleColor) && title.length > 0 && title.indexOf('&') == -1) {
                title = title + "&outline"; // 假如文本颜色较浅则加上描边
            }
            tvTitle.text = title;
            // 让报错信息标题可以被点击：创建一个透明按钮覆盖其上方
            var button: MovieClip = mc.attachMovie("Generic1pxTransparentButton", "button", 98);
            var hitRegion: Button = button.hitRegion;
            hitRegion._width = tvTitle._width;
            hitRegion._height = tvTitle._height;
            hitRegion.onRollOver = function() { tvTitle.textColor = 0xFFFFFF; };
            hitRegion.onRollOut = function() { tvTitle.textColor = titleFormat.color; };
            // 点击报错信息标题后隐藏报错信息
            hitRegion.onRelease = Bind.noArg(this, function() {
                screen.g_disableErrorMessage = true;
                var self: GameObject = _texts[textIndex];
                _texts[textIndex] = undefined;
                delete _texts[textIndex];
                _layers.remove("text:" + textIndex, false);
                _customTextWorld.destroy(self);
            });
            // 把自定义文本移动到标题下方
            textField._y += tvTitle._height;

            // HACK:
            // 在不更新 DLL 的情况下
            // 我们需要移动一下 performance statistics 的位置
            // 虽然它与报错信息没有任何关系
            // 但这里是唯一一个可以在不更新 DLL 的情况下，稳定拿到实际坐标的地方
            var perf: MovieClip = _level0.performanceApt567;
            if (perf) {
                perf._y = mc._y - 50;
                perf._x = mc._x - 50;
            }
        });

        screen.SetErrorMessage = Bind.oneArg(this, function(text: String) {
            if (screen.g_disableErrorMessage) {
                return;
            }
            var textIndex: String = "ErrorMsg";
            if (_texts[textIndex]) {
                var mc: MovieClip = _texts[textIndex].sprite();
                var textField: TextField = mc.tv;
                var textColor: Number = textField.textColor;

                mc._visible = (text.length > 0);
                if (!isColorDark(textColor) && text.length > 0 && text.indexOf('&') == -1) {
                    text = text + "&outline"; // 假如文本颜色较浅则加上描边
                }
                textField.text = text;
            }
        });

    }

    private function getCustomButtonMc(index: Number): MovieClip {
        var buttonHolder: GameObject = _btns[index];
        if (buttonHolder && !buttonHolder.isDestroyed()) {
            return buttonHolder.sprite();
        }
        return null;
    }

    private function hideCustomButtonContextMenu(index: Number): Void {
        if (_contextMenuButtonIndex != index) {
            return;
        }
        _contextMenuButtonIndex = undefined;
        _contextMenu._x = -400;
        _contextMenu._y = -400;
        _contextMenu.tv.text = "";
    }

    private function destroyCustomButton(index: Number, keepSequence: Boolean) {
        this.hideCustomButtonContextMenu(index);
        _layers.remove("button:" + String(index), keepSequence);
        var delayInitId: Number = _delayInitId[index];
        if (delayInitId != undefined) {
            clearInterval(delayInitId);
            _delayInitId[index] = undefined;
            delete _delayInitId[index];
        }
        var buttonHolder: GameObject = _btns[index];
        if (buttonHolder && !buttonHolder.isDestroyed()) {
            var button: MovieClip = buttonHolder.sprite();
            button.unloadMovie();
            _customButtonWorld.destroy(buttonHolder);
        }
        _btns[index] = undefined;
        delete _btns[index];
        _buttonImageStyles[index] = undefined;
        delete _buttonImageStyles[index];
    }

    private function registerButtonLayer(
        index: Number, object: GameObject, groupIndex: Number, interactive: Boolean): Void {
        var mc = object.sprite();
        var hit = function(x, y) {
            return interactive && !object.isDestroyed() && mc._visible && !mc.DATA_disabled && x >= mc._x &&
                y >= mc._y && x < mc._x + mc._width && y < mc._y + mc._height;
        };
        _layers.register("button:" + String(index), object, groupIndex, index, hit);
    }

    private function applyCustomImageColor(index: Number, button: MovieClip): Void {
        var style: Object = _buttonImageStyles[index];
        if (!style || !button) {
            return;
        }

        var alpha: Number = Math.round(Number(style.alpha) * 100 / 255);
        this.applyCustomImageColorToTarget(button.image, Number(style.rgb), alpha);
        this.applyCustomImageColorToTarget(button.disabledImage, Number(style.rgb), alpha);
    }

    private function applyCustomImageColorToTarget(target: MovieClip, rgb: Number, alpha: Number): Void {
        if (!target) {
            return;
        }

        var color: Color = new Color(target);
        color.setRGB(rgb);
        target._alpha = alpha;
    }

    private function resetCustomTextLayout(customTextMc: MovieClip): Void {
        if (customTextMc.DATA_hasTextAlignX === true) {
            customTextMc.tv._x = Number(customTextMc.DATA_textFieldBaseX);
        }
    }


    private function applyCustomTextLayout(customTextMc: MovieClip): Void {
        if (customTextMc.DATA_hasTextAlignX !== true) {
            return;
        }

        // APT 的居中 autoSize 还会移动文本框；先按左锚点测量，再直接设置最终边界，
        // 避免内容多次变化后偏移累积。
        var textField: TextField = customTextMc.tv;
        textField.autoSize = "left";
        if (customTextMc.DATA_textAlignX === "center") {
            textField._x = -textField._width / 2;
        } else if (customTextMc.DATA_textAlignX === "right") {
            textField._x = -textField._width;
        } else {
            textField._x = 0;
        }

        var bg: MovieClip = customTextMc.bg;
        if (bg._visible) {
            bg._width = textField._width;
            bg._height = textField._height;
            bg._x = Number(customTextMc.DATA_textBgBaseX) + textField._x - Number(customTextMc.DATA_textFieldBaseX);
        }
    }

    public static function isColorDark(color: Number): Boolean {
        var red: Number = (color >> 16) & 0xFF;
        var green: Number = (color >> 8) & 0xFF;
        var blue: Number = color & 0xFF;
        var brightness: Number = (red * 299 + green * 587 + blue * 114) / 1000;
        if (brightness < 50) {
            return true;
        }
        return false;
    }

}
