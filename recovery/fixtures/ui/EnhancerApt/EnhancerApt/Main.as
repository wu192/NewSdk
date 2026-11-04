import EnhancerApt.Bind;
import EnhancerApt.CommonBtn;
import EnhancerApt.World;
import EnhancerApt.LongTextDialogManger;
import EnhancerApt.PublicBoardManager;
import EnhancerApt.BtnChoiceDialogManager
import EnhancerApt.GameObject;
import EnhancerApt.UnitTopBtn;
import EnhancerApt.CenterTopBtnsManager
import EnhancerApt.ScoreBoardManager;
import EnhancerApt.CustomButtonManager;
import EnhancerApt.MapUiManager;
import EnhancerApt.MapUiLayers;

class EnhancerApt.Main {

    private var _screen: MovieClip;
    private var _btnDialogBtns: Array;
    private var _screenAllWorld: World;

    private var uniTopTextsMap: Object;
    private var uniTopBtnsMap: Object;
    // private var testEAButtonLayer: MovieClip;
    private var delayInitId: Number;

    public function Main(screen: MovieClip) {
        trace("EnhancerApt.Main");
        _screen = screen;
        _screen.mask._visible = false;

        _global.enhancerAptFirstLoaded = true;
        if (_global.enhancerAptFirstLoaded && _global.enhancerAptSecondLoaded == undefined) {
            trace("EnhancerApt.Main init");
            _global.enhancerAptSecondLoaded = true;
            this.initMC();
            return;
        }
    }

    private var eaBtnIndex = 0;

    private function initMC() {
        trace("EnhancerApt.Main initMC");
        // _screen.mTestImage._type = "RenderImage";
        // var imageMapValue = _global.qualifyName(_screen.mTestImage, "Image");
        // trace("_global.qualifyName " + _global.qualifyName);
        // trace("imageMapValue " + imageMapValue);
        // _screen.mTestImage._imageMap = imageMapValue;
        // _screen.mTestImage._mode = "";
        // _screen.mTestImage.attachMovie("Button_AlliedFutureTank","Image",0);


        _screen.enableCampaignMode = function(enable: String) {
            fscommand("CallGameFunction", "%SetMapType?MapType=" + enable);
        }
        

        uniTopTextsMap = {};
        uniTopBtnsMap = {};
        var screenAllMovieClip = _screen.createEmptyMovieClip("world1", 1);
        _screenAllWorld = new World(screenAllMovieClip, _screen._width, _screen._height);

        var worldMovieClip2 = _screen.createEmptyMovieClip("world2", 5);
        var _world2 = new World(worldMovieClip2, _screen._width, _screen._height);


        new PublicBoardManager(_screen, _world2);
        new ScoreBoardManager(_screen, _world2);

        new CenterTopBtnsManager(_screen, _world2);
        new LongTextDialogManger(_screen, _world2);
        new BtnChoiceDialogManager(_screen, _world2);
        var mapUiLayers = new MapUiLayers(_screen, _world2);
        new CustomButtonManager(_screen, _world2, mapUiLayers);
        new MapUiManager(_screen, _world2, mapUiLayers);



        // _screen.AddImageButton = Bind.twoArg(this, function(x: Number, y: Number) {
        //
        //     var testEAButton = _screen.createEmptyMovieClip("TestEAButton" + String(eaBtnIndex), 100 + eaBtnIndex);
        //     eaBtnIndex = eaBtnIndex + 1;
        //     testEAButton.loadMovie("StandardCommandButton.swf");
        //     // testEAButton.attachMovie("fakeEABtn", "mybtn" + String(eaBtnIndex), eaBtnIndex + 10);
        //
        //
        //     trace("testEAButtonClip " + String(x) + " " + String(y));
        //
        //     // 等待加载完再调用，不然会找不到函数
        //     delayInitId = setInterval(Bind.noArgCapture1(this, testEAButton, function(mc) {
        //         clearInterval(delayInitId);
        //         mc.SetEnabled(true);
        //     }), 100)
        //
        //
        //     testEAButton._x = x;
        //     testEAButton._y = y;
        //     var addr = _global.Pointer.encode(testEAButton);
        //
        //     fscommand("CallGameFunction", "%loadTestImage" + addr);
        // });

        _screen.traceAllMovieClips = Bind.threeArg(this, function(mc:MovieClip, indent:String, dep: Number) {
            if (dep >= 4) {
                return;
            }
            trace(indent + mc._name); // Trace the current MovieClip's name
            for (var i in mc) {
                if (typeof mc[i] === 'movieclip') {
                    _screen.traceAllMovieClips(mc[i], indent + "  ", dep + 1); // Recursively trace child MovieClips
                }
            }
        });


        _screen.createScreenAllWorld = Bind.threeArg(this, function(content: String) {
            // delayInitId = setInterval(Bind.noArg(this, function(mc) {
            //     clearInterval(delayInitId);
            //     trace("traceAllMovieClips");
            //     _screen.traceAllMovieClips(_level0.hud.content, "", 0);
            // }), 1000)
            // trace("traceAllMovieClips");
            // _screen.traceAllMovieClips(_level0.hud, "", 0);

            // trace("createScreenAllWorld " + _level0.hud.content.main.popUpMetaStage);
            // var worldMovieClip1 = _level0.hud.content.main.popUpMetaStage.createEmptyMovieClip("enhancerworld1", 6);
            // var worldMovieClip = worldMovieClip1.createEmptyMovieClip("enhancerworld2", 1);

            // worldMovieClip.loadMovie("enhancerapt.swf");
            // _screenAllWorld = new World(worldMovieClip, _screen._width, _screen._height);
        });

        _screen.createUnitTopText = Bind.threeArg(this, function(objectId: String, content: String, color: Number) {
            trace("createUnitTopText " + content);
            var unitTopText = _screenAllWorld.instantiate("UnitTopText");
            unitTopText.setPosition({ x : -100, y: -100});
            this.updateUnitTopTextField(unitTopText, content, color);
            uniTopTextsMap[objectId] = unitTopText;


        });


        _screen.updateUnitTopText = Bind.threeArg(this, function(objectId: String, content: String, color: Number) {
            if (uniTopTextsMap[objectId]) {
                this.updateUnitTopTextField(uniTopTextsMap[objectId], content, color);
            }
        });
        _screen.destroyUnitTopText = Bind.oneArg(this, function(objectId: String) {
            if (uniTopTextsMap[objectId]) {
                _screenAllWorld.destroy(uniTopTextsMap[objectId])
                uniTopTextsMap[objectId] = undefined;
            }
        });
        _screen.destroyAllUnitTopText = Bind.oneArg(this, function(a: String) {
            for (var objectId:String in uniTopTextsMap) {
                if (uniTopTextsMap[objectId]) {
                    _screenAllWorld.destroy(uniTopTextsMap[objectId])
                    delete uniTopTextsMap[objectId];
                }
            }
        });

        ///////////////////////

        _screen.createUnitTopButton = Bind.oneArg(this, function(objectId: String) {
            trace("createUnitTopButton");
            var unitTopButton = _screenAllWorld.instantiate("UnitTopButton");
            var unitTopBtn = new UnitTopBtn(unitTopButton);
            unitTopBtn.setPosition({ x : -100, y: -100});
            unitTopBtn.setHandler(Bind.noArgCapture1(this, objectId, function(objId) {
                trace("unitTopButton click " + objId);
                fscommand("CallGameFunction", "%UnitTopButtonClick?Id=" + objId);
            }))
            uniTopBtnsMap[objectId] = unitTopBtn;
        });

        _screen.createCustomUnitTopButton = Bind.twoArg(this, function(objectId: String, params: String) {
            trace("createCustomUnitTopButton");
            var paramArr: Array = params.split(",");
            var width: Number = Number(paramArr[0]);
            var height: Number = Number(paramArr[1]);
            var unitTopButton: GameObject = _screenAllWorld.instantiate("CustomImage");
            var unitTopBtn: UnitTopBtn = new UnitTopBtn(unitTopButton, width, height);
            unitTopBtn.setPosition({x: -100, y: -100});
            unitTopBtn.setHandler(Bind.noArgCapture1(this, objectId, function(objId) {
                trace("custom unitTopButton click " + objId);
                fscommand("CallGameFunction", "%UnitTopButtonClick?Id=" + objId);
            }));
            uniTopBtnsMap[objectId] = unitTopBtn;

            var addr: String = _global.Pointer.encode(unitTopButton.sprite());
            fscommand("CallGameFunction", "%loadUnitTopBtnImage" + addr + "," + objectId);
        });

        _screen.destroyUnitTopButton = Bind.oneArg(this, function(objectId: String) {
            if (uniTopBtnsMap[objectId]) {
                _screenAllWorld.destroy(uniTopBtnsMap[objectId].gameObj())
                uniTopBtnsMap[objectId] = undefined;
            }
        });
        _screen.destroyAllUnitTopBtns = Bind.oneArg(this, function(a: String) {
            for (var objectId:String in uniTopBtnsMap) {
                if (uniTopBtnsMap[objectId]) {
                    _screenAllWorld.destroy(uniTopBtnsMap[objectId].gameObj())
                    uniTopBtnsMap[objectId] = undefined;
                }
            }
        });


        _screen.updateUnitTopTextPos = Bind.oneArg(this, function(a: String) {
            trace("updateUnitTopTextPos");
            var allPos : Object = {};
            loadVariables("EnhancerApt_GetUnitTopTextPos", allPos);
            trace("updateUnitTopTextPos " + allPos);

            for (var objectId:String in uniTopTextsMap) {
                if (uniTopTextsMap[objectId]) {
                    uniTopTextsMap[objectId].setPosition({ x : -200, y: -200});
                }
            }

            for (var key:String in allPos) {
                if (uniTopTextsMap[key]) {
                    var posArr = allPos[key].split(",");
                    trace("updateUnitTopTextPos " + uniTopTextsMap[key].sprite());
                    var finalX = Number(posArr[0]);
                    var finalY = Number(posArr[1]);

                    if (finalX > 1090 && finalY < 250) {
                        continue;
                    }
                    if (finalX > 1130 && finalY > 640) {
                        continue;
                    }
                    uniTopTextsMap[key].setPosition({ x : finalX, y: finalY});
                }
            }
        });

        _screen.updateUnitTopBtnPos = Bind.oneArg(this, function(a: String) {
            trace("updateUnitTopBtnPos");
            var allPos : Object = {};
            loadVariables("EnhancerApt_GetUnitTopBtnPos", allPos);
            trace("updateUnitTopBtnPos " + allPos);

            for (var objectId:String in uniTopBtnsMap) {
                if (uniTopBtnsMap[objectId]) {
                    uniTopBtnsMap[objectId].setPosition({ x : -200, y: -200});
                }
            }

            for (var key:String in allPos) {
                if (uniTopBtnsMap[key]) {
                    var posArr = allPos[key].split(",");
                    trace("updateUnitTopBtnPos " + uniTopBtnsMap[key]);
                    var finalX = Number(posArr[0]);
                    var finalY = Number(posArr[1]);

                    if (finalX > 1090 && finalY < 250) {
                        continue;
                    }
                    if (finalX > 1130 && finalY > 640) {
                        continue;
                    }
                    uniTopBtnsMap[key].setPosition({ x : finalX, y: finalY});
                }
            }
        });


        _screen.toggleElementVisibility = function(element: String, visibility: Number) {
            trace("toggleElement " + element);
            if (visibility == 1) {
                eval(element)._visible = 1;
            } else {
                eval(element)._visible = 0;
            }

        }

        _screen.toggleMaskVisibility = Bind.oneArg(this, function(visibility: Number) {
            if (visibility == 1) {
                _screen.mask._visible = 1;
            } else {
                _screen.mask._visible = 0;
            }
        });

        _screen.HideTopRightUIComponents = Bind.oneArg(this, function(aptMaxX: Number) {
            _level0.hud.content.upperSidebarLayer.upperSidebar.sidebarFrame.alpha = 0;
            _level0.hud.content.upperSidebarLayer.upperSidebar.mainCommandBar._visible = 0;
            _level0.hud.content.upperSidebarLayer.upperSidebar.resourceDisplay.alpha = 0;
            _level0.hud.content.upperSidebarLayer.upperSidebar.energyMeter._visible = 0;
            _level0.hud.content.upperSidebarLayer.upperSidebar.commandModeBar._visible = 0;

            _level0.hud.content.upperSidebarLayer.upperSidebar.radar._x = -470;  //45
            // _level0.hud.content.upperSidebarLayer.upperSidebar.radar._y = 0;   //540
            _level0.hud.content.upperSidebarLayer._x = aptMaxX;  //45
            // _level0.hud.content.upperSidebarLayer._y = 0;   //540
        });

    }

    private function updateUnitTopTextField(unitTopText: GameObject, content: String, color: Number): Void {
        var textMc: MovieClip = unitTopText.sprite();
        var textField: TextField = textMc.tv;
        // APT 对多行文本只按首行测量 autoSize，后续更宽的行可能被折行或裁切。
        // 每个显式行分别使用原生自动尺寸文本框，并保留模板字体。
        var format: TextFormat = textField.getTextFormat();
        var lines: Array = content.split("\r\n").join("\n").split("\r").join("\n").split("\n");
        var oldCount: Number = Number(textMc.DATA_unitTopLineCount);
        if (isNaN(oldCount)) {
            oldCount = 0;
        }
        var lineY: Number = textField._y;
        var lineHeight: Number = Number(format.size);
        textField._visible = false;
        for (var i: Number = 0; i < lines.length; i++) {
            var name: String = "unitTopLine" + i;
            if (textMc[name] == undefined) {
                textMc.createTextField(name, 100 + i, 0, lineY, 1, 1);
            }
            var lineField: TextField = textMc[name];
            lineField.wordWrap = false;
            lineField.multiline = false;
            lineField.autoSize = "left";
            // 空行也用非空文本测量高度，以保留正确行距。
            lineField.text = (lines[i].length == 0) ? " " : lines[i];
            lineField.setTextFormat(format);
            lineField.textColor = color;
            lineHeight = Math.max(lineHeight, lineField.textHeight);
            lineField._x = -lineField._width / 2;
            lineField._y = lineY;
            lineY += lineHeight;
        }
        for (var j: Number = lines.length; j < oldCount; j++) {
            textMc["unitTopLine" + j].removeTextField();
        }
        textMc.DATA_unitTopLineCount = lines.length;

    }

}
