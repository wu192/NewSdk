
class CoronaGameSettingsNewPanel {
    private static var CLASS_NAME = "CoronaGameSettingsNewPanel";

    private static var TEXT_COLOR = 0xF3A856;
    private static var TEXT_SHADOW_COLOR = 0x000000;
    private static var TEXT_FONT = "Red Alert";
    private static var TEXT_SIZE = 20;
    private static var TEXT_FONT_SMALL = "Lucida Sans";
    private static var TEXT_SIZE_SMALL = 16;

    private static var LABEL_TO_CHECKBOX_OFFSET_X = 33.25;
    private static var LABEL_TO_CHECKBOX_OFFSET_Y = -13.1;
    private static var DROPDOWN_TO_LABEL_OFFSET_X = 160;
    private static var RULE_ROW_HEIGHT = 36;
    private static var CONTENT_LEFT = 42;
    // 假如某一行没有勾选框，而是由文本开头的，那么往左一点比较好
    // 局域网警告和经济作弊就是这种情况
    private static var CONTENT_LEFT_FOR_TEXT = CONTENT_LEFT - 18;
    private static var CONTENT_LINE_1_Y = 85 - 40;
    private static var AI_MONEY_CHEAT_Y = CONTENT_LINE_1_Y + RULE_ROW_HEIGHT + 5;
    private static var CONTENT_LINE_2_Y = AI_MONEY_CHEAT_Y + RULE_ROW_HEIGHT + 20;
    private static var CONTENT_LINE_3_Y = CONTENT_LINE_2_Y + RULE_ROW_HEIGHT;
    private static var CONTENT_LINE_4_Y = CONTENT_LINE_3_Y + RULE_ROW_HEIGHT;

    // 新的面板
    private static var TAB_PANEL_ID = "coronaNewPanel";
    private static var TAB_PANEL_DEPTH = 300;
    // 局域网警告信息面板
    private static var LAN_WARNING_PANEL_ID = "coronaLanWarningPanel";
    private static var LAN_WARNING_PANEL_DEPTH = 301;
    private static var LAN_WARNING_TEXTFIELD_ID = "coronaLanWarningTextField";
    // 新的面板的选项卡按钮
    private static var TAB_BUTTON_ID = "coronaNewPanelButton";
    private static var TAB_BUTTON_DEPTH = 302;
    // 遭遇战里新增的规则选项卡按钮
    private static var SKIRMISH_NEW_RULES_TAB_BUTTON_ID = "coronaNewRulesPanelButton";
    private static var SKIRMISH_NEW_RULES_TAB_BUTTON_DEPTH = 303;
    
    private var _gameSettings: MovieClip;
    private var _gameSetup: Object;
    private var _panel: MovieClip; // 新的面板
    private var _lanWarningPanel: MovieClip;
    private var _isSkirmish: Boolean;
    private var _isLocalAreaNetwork: Boolean;
    private var _isHost: Boolean;
    // 文本格式（字体等）
    private var _textFormat: TextFormat;
    private var _shadowTextFormat: TextFormat;
    private var _smallTextFormat: TextFormat;
    private var _shadowSmallTextFormat: TextFormat;
    // 创建新的 MovieClip 或 TextField 时使用的深度
    private var _lastDepth: Number;
    // 用于更新各个组件的函数
    private var _refreshFunctions: Array;
    // 用于启用禁用各个组件的函数
    private var _setEnableFunctions: Array;

    public static function patchGameSetupBase(gameSetup: Object) {
        var gameSettings: MovieClip = _global.Cafe2_BaseUIScreen.m_screen.gameSettings;
        if (!gameSettings) {
            return;
        }
        // 如果新面板已经创建过了，就不再创建
        if (gameSettings[TAB_BUTTON_ID]) {
            return;
        }
        // 创建新面板
        new CoronaGameSettingsNewPanel(gameSettings, gameSetup);
    }

    public function CoronaGameSettingsNewPanel(gameSettings: MovieClip, gameSetup: Object) {
        _gameSettings = gameSettings;
        _gameSetup = gameSetup;
        // _global.GAME.MODE 由 fes_m_gameSetup 的构造函数设置
        // 由于 NewPanel（以及 tryInitializeOnce）是由 GameSetupBase 的 frameDelayedInit 调用的
        // 而 frameDelayedInit 是在构造函数执行之后的下一帧执行的
        // 因此构造函数必然已经执行过了，_global.GAME.MODE 也必然已经设置过了
        _isSkirmish = (_global.GAME.MODE === _global.ENUM.GAME_MODE_SKIRMISH);
        _isLocalAreaNetwork = (_global.GAME.MODE === _global.ENUM.GAME_MODE_LAN_MP);
        var ret = new Object();
        loadVariables("QueryGameEngine?IsPcGameHost", ret);
        _isHost = ret.IsPcGameHost == "1";
        _textFormat = new TextFormat(TEXT_FONT, TEXT_SIZE, TEXT_COLOR);
        _shadowTextFormat = new TextFormat(TEXT_FONT, TEXT_SIZE, TEXT_SHADOW_COLOR);
        _smallTextFormat = new TextFormat(TEXT_FONT_SMALL, TEXT_SIZE_SMALL, TEXT_COLOR);
        _shadowSmallTextFormat = new TextFormat(TEXT_FONT_SMALL, TEXT_SIZE_SMALL, TEXT_SHADOW_COLOR);
        _lastDepth = 1;
        _refreshFunctions = new Array();
        _setEnableFunctions = new Array();

        createTab();
    }

    private function createTab(): Void {
        createTabButtons("$RULE:Enhance");
        // 创建新的扩展规则面板
        _panel = _gameSettings.createEmptyMovieClip(
            TAB_PANEL_ID,
            TAB_PANEL_DEPTH
        );
        // 初始状态下需要不可见，否则会与正在初始化的其他面板重叠
        _panel._visible = false;
        _panel._x = _gameSettings.rulesPanel._x;
        _panel._y = _gameSettings.rulesPanel._y;
        // 创建局域网警告
        _lanWarningPanel = _gameSettings.createEmptyMovieClip(
            LAN_WARNING_PANEL_ID,
            LAN_WARNING_PANEL_DEPTH
        );
        _lanWarningPanel._x = _gameSettings.rulesPanel._x;
        _lanWarningPanel._y = _gameSettings.rulesPanel._y;
        createLanWarning("$RULE:Enhance_LAN_Warning");
        // 局域网警告也初始不可见，玩家点击选项卡按钮的时候再显示
        _lanWarningPanel._visible = false;

        // 经济作弊按理说是在第一行的
        // 不过，按照我们目前的代码，越早创建的组件，深度越低
        // 我们希望经济作弊的菜单在最上面，以避免下拉菜单被其他组件遮挡
        // 因此我们把经济作弊的菜单放在最后创建

        // 阵营拓展模式勾选框
        var enhancedModeCheckBox: MovieClip = createCheckBox(
            "enhancedModeCheckBox",
            "Enhancer#Query#CoronaPVECheckBox",
            "",
            "CoronaPVECheckBox_VALUE"
        );
        enhancedModeCheckBox._x = CONTENT_LEFT;
        enhancedModeCheckBox._y = CONTENT_LINE_2_Y;
        createTextField(
            "enhancedModeLabel",
            "$RULE:CoronaPVE",
            CONTENT_LEFT + LABEL_TO_CHECKBOX_OFFSET_X,
            CONTENT_LINE_2_Y + LABEL_TO_CHECKBOX_OFFSET_Y
        );

        // 禁止超武勾选框
        var noSuperWeaponsCheckBox: MovieClip = createCheckBox(
            "noSuperWeaponsCheckBox",
            "Enhancer#Query#DisableSuperWeaponCheckBox",
            "",
            "DisableSuperWeaponCheckBox_VALUE"
        );
        noSuperWeaponsCheckBox._x = CONTENT_LEFT;
        noSuperWeaponsCheckBox._y = CONTENT_LINE_3_Y;
        createTextField(
            "noSuperWeaponsLabel",
            "$RULE:DisableSuperWeapon",
            CONTENT_LEFT + LABEL_TO_CHECKBOX_OFFSET_X,
            CONTENT_LINE_3_Y + LABEL_TO_CHECKBOX_OFFSET_Y
        );

        // 散矿勾选框
        var scatterOreNodeCheckBox: MovieClip = createCheckBox(
            "scatterOreNodeCheckBox",
            "Enhancer#Query#ScatterOreNodeCheckBox",
            "",
            "ScatterOreNodeCheckBox_VALUE"
        );
        scatterOreNodeCheckBox._x = CONTENT_LEFT;
        scatterOreNodeCheckBox._y = CONTENT_LINE_4_Y;
        createTextField(
            "scatterOreNodeLabel",
            "$RULE:ScatterOreNode",
            CONTENT_LEFT + LABEL_TO_CHECKBOX_OFFSET_X,
            CONTENT_LINE_4_Y + LABEL_TO_CHECKBOX_OFFSET_Y
        );

        // ai经济作弊下拉菜单
        createTextField(
            "AIMoneyCheatLabel",
            "$RULE:AICheatMultiplier",
            CONTENT_LEFT_FOR_TEXT,
            AI_MONEY_CHEAT_Y
        );
        var moneyCheatMenu: MovieClip = createDropDownMenu(
            "AIMoneyCheatLabel",
            "Enhancer#Query#AI_CHEAT_MULTIPLIER_OPTIONS",
            "",
            "AI_CHEAT_MULTIPLIER_OPTIONS_VALUES",
            "AI_CHEAT_MULTIPLIER_OPTIONS_STRINGS"
        );
        moneyCheatMenu._x = CONTENT_LEFT_FOR_TEXT + DROPDOWN_TO_LABEL_OFFSET_X;
        moneyCheatMenu._y = AI_MONEY_CHEAT_Y;

        // 玩家经济作弊下拉菜单
        createTextField(
            "moneyCheatLabel",
            "$RULE:CheatMultiplier",
            CONTENT_LEFT_FOR_TEXT,
            CONTENT_LINE_1_Y
        );
        var moneyCheatAdditionalQuery: String = "";
        if (!_isSkirmish) {
            moneyCheatAdditionalQuery = "Online=1";
        }
        var moneyCheatMenu: MovieClip = createDropDownMenu(
            "moneyCheatMenu",
            "Enhancer#Query#CHEAT_MULTIPLIER_OPTIONS",
            moneyCheatAdditionalQuery,
            "CHEAT_MULTIPLIER_OPTIONS_VALUES",
            "CHEAT_MULTIPLIER_OPTIONS_STRINGS"
        );
        moneyCheatMenu._x = CONTENT_LEFT_FOR_TEXT + DROPDOWN_TO_LABEL_OFFSET_X;
        moneyCheatMenu._y = CONTENT_LINE_1_Y;


        // 挂钩各种函数
        hookFunctions();
        // 在下一帧直接更新一遍所有数据
        onNextFrame(_global.bind0(this, function() {
            for (var i: Number = 0; i < _refreshFunctions.length; ++i) {
                _refreshFunctions[i]();
            }
            for (var i: Number = 0; i < _setEnableFunctions.length; ++i) {
                _setEnableFunctions[i](_isHost /* 只有房主才可以启用规则 */);
            }
        }));
    }

    private function hookFunctions(): Void {
        // 此时替换/挂钩函数还来得及，
        // 因为目前我们还处于 GameSetupBase 的 frameDelayedInit 调用的过程中
        // 此时大部分 GameSetup 的函数还未被调用或引用，可以被替换

        // 用于刷新各个组件的函数
        // 和上面的 CoronaGameSettingsNewPanel_OriginalFrameDelayedInit 一样
        // 这里也使用了一个比较长的而且独特的名字，避免冲突
        _gameSetup.CoronaGameSettingsNewPanel_OriginalRefreshComponents = _gameSetup.refreshComponents;
        _gameSetup.refreshComponents = _global.bind0(this, function() {
            _gameSetup.CoronaGameSettingsNewPanel_OriginalRefreshComponents();
            // 更新各个面板选项
            for (var i: Number = 0; i < _refreshFunctions.length; ++i) {
                _refreshFunctions[i]();
            }
        });

        // 在线游戏开始倒计时之后，禁用所有组件
        // 或者在倒计时被中断之后，重新启用组件
        _gameSetup.CoronaGameSettingsNewPanel_m_localMessageDispatcher = _gameSetup.m_localMessageDispatcher;
        _gameSetup.m_localMessageDispatcher = _global.bind1DynamicParams(this, function(messageCode) {
            if (messageCode === _global.MSGCODE.FE_MP_ONLINE_GAME_COUNTDOWN_START) {
                for (var i: Number = 0; i < _setEnableFunctions.length; ++i) {
                    // 开始倒计时之后，无论是否是房主，都要禁用拓展面板的规则
                    // 这与其他原版组件不同，因为拓展面板在倒计时开始之后无法再与其他玩家同步
                    _setEnableFunctions[i](false);
                }
            }
            return _gameSetup.CoronaGameSettingsNewPanel_m_localMessageDispatcher(messageCode);
        });
        _gameSetup.CoronaGameSettingsNewPanel_OriginalDisableGameSetupUI = _gameSetup.disableGameSetupUI;
        _gameSetup.disableGameSetupUI = _global.bind0(this, function() {
            _gameSetup.CoronaGameSettingsNewPanel_OriginalDisableGameSetupUI();
            for (var i: Number = 0; i < _setEnableFunctions.length; ++i) {
                _setEnableFunctions[i](false);
            }
        });
        _gameSetup.CoronaGameSettingsNewPanel_OriginalEnableGameSetupUI = _gameSetup.enableGameSetupUI;
        _gameSetup.enableGameSetupUI = _global.bind0(this, function() {
            _gameSetup.CoronaGameSettingsNewPanel_OriginalEnableGameSetupUI();
            for (var i: Number = 0; i < _setEnableFunctions.length; ++i) {
                _setEnableFunctions[i](_isHost /* 只有房主才可以启用规则 */);
            }
        });

        if (_isSkirmish) {
            return;
        }
        // 假如不是遭遇战，那么就是在线游戏或者局域网
        // 在线游戏 / 局域网的规则面板和地图面板是在同一个面板上的，
        // 需要按需隐藏或者显示它们。
        
        // 替换原有的选项卡按钮的 onShowMapPanel 和 onShowRulesPanel 方法
        _gameSetup.CoronaGameSettingsNewPanel_OriginalOnShowMapPanel = _gameSetup.onShowMapPanel;
        _gameSetup.onShowMapPanel = function() {
            // 注意这个 function 里面的 this 是 GameSetup 的实例
            var gameSetup = this;
            gameSetup.CoronaGameSettingsNewPanel_OriginalOnShowMapPanel();
            var gameSettings = _global.Cafe2_BaseUIScreen.m_screen.gameSettings;
            if (gameSettings && gameSettings[TAB_PANEL_ID]) {
                gameSettings[TAB_PANEL_ID]._visible = false;
            }
            if (gameSettings && gameSettings[LAN_WARNING_PANEL_ID]) {
                gameSettings[LAN_WARNING_PANEL_ID]._visible = false;
            }
        }
        _gameSetup.CoronaGameSettingsNewPanel_OriginalOnShowRulesPanel = _gameSetup.onShowRulesPanel;
        _gameSetup.onShowRulesPanel = function() {
            // 注意这个 function 里面的 this 是 GameSetupBase 的实例
            var gameSetup = this;
            gameSetup.CoronaGameSettingsNewPanel_OriginalOnShowRulesPanel();
            var gameSettings =  _global.Cafe2_BaseUIScreen.m_screen.gameSettings;
            if (gameSettings && gameSettings[TAB_PANEL_ID]) {
                gameSettings[TAB_PANEL_ID]._visible = false;
            }
            if (gameSettings && gameSettings[LAN_WARNING_PANEL_ID]) {
                gameSettings[LAN_WARNING_PANEL_ID]._visible = false;
            }
        }
    }

    // 创建选项卡按钮
    // 对于在线游戏或者局域网，仅仅创建新的拓展规则选项卡按钮
    // 对于遭遇战，创建新的拓展规则选项卡按钮，以及规则选项卡按钮
    private function createTabButtons(tabButtonLabel: String): Void {
        var newTabButton: MovieClip = createTabButton(
            TAB_BUTTON_ID,
            TAB_BUTTON_DEPTH,
            tabButtonLabel
        );
        newTabButton._x = 345;
        newTabButton._y = 1;

        newTabButton.setOnMouseUpFunction(_global.bind0(this, onShowNewPanel));
        // 下面这些函数在 newTabButton 刚刚创建的时候是无法调用的
        // 因此等到下一帧再调用
        onNextFrame(function() {
            newTabButton.noIntro();
            newTabButton.enable();
            newTabButton.show();
            newTabButton.visual_unhighlight();
        });

        if (_isSkirmish) {
            // 假如是遭遇战，除了创建新的面板之外，
            // 还需要创建规则选项卡按钮

            var rulesTabButton: MovieClip = createTabButton(
                SKIRMISH_NEW_RULES_TAB_BUTTON_ID,
                SKIRMISH_NEW_RULES_TAB_BUTTON_DEPTH,
                "$TabRules"
            );
            rulesTabButton.setOnMouseUpFunction(_global.bind0(this, onShowSkirmishRulesPanel));
            onNextFrame(function() {
                rulesTabButton.noIntro();
                rulesTabButton.enable();
                rulesTabButton.show();
                rulesTabButton.visual_highlight();
            });
            var rulesPanel: MovieClip = _gameSettings.rulesPanel;
            rulesTabButton._x = rulesPanel._x - 5;
            rulesTabButton._y = 1;

            newTabButton._x = rulesPanel._x + 170;

            rulesTabButton._y += 34;
            newTabButton._y += 34;

            // 创建一个长条摆在遭遇战新加的选项卡下面，好看一点
            // yellowBar 是 GameSettings 所属的 fe_m_assetsGameSetup.swf Library 里的一个组件
            // 因此可以在 GameSettings 里面直接 attachMovie
            var bar: MovieClip = _gameSettings.createEmptyMovieClip("bar", 400);
            bar.attachMovie("yellowBar", "yellowBar", 1);
            bar._rotation = -90;
            bar._xscale = 225;
            bar._yscale = 175;
            
            bar._x = rulesPanel._x;
            bar._y = rulesPanel._y + 35;
        }

        // 让新的选项卡闪两下
        var intervalId: Number;
        var count: Number = 0;
        intervalId = createSafeInterval(function() {
            ++count;
            if (count % 2 == 1) {
                newTabButton.doUnselectedRollover();
            }
            else {
                newTabButton.doUnselect();
            }
            if (count > 4) {
                clearInterval(intervalId);
                newTabButton.restoreVisualState();
                return;
            }
        }, 200);
    }

    // 创建局域网警告
    private function createLanWarning(text: String): Void {
        var name: String = LAN_WARNING_TEXTFIELD_ID;
        var x = CONTENT_LEFT_FOR_TEXT;
        var y = CONTENT_LINE_1_Y;

        _lanWarningPanel.createTextField(
            name + "_shadow",
            ++_lastDepth,
            x + 2,
            y + 2,
            500,
            100
        );
        var newShadowTextField: TextField = _lanWarningPanel[name + "_shadow"];
        newShadowTextField.setTextFormat(_shadowSmallTextFormat);
        newShadowTextField.text = text;
        newShadowTextField.wordWrap = true;

        _lanWarningPanel.createTextField(
            name,
            ++_lastDepth,
            x,
            y,
            500,
            100
        );
        var newTextField: TextField = _lanWarningPanel[name];
        newTextField.setTextFormat(_smallTextFormat);
        newTextField.text = text + "&Outline";
        newTextField.wordWrap = true;
    }

    // 显示新的扩展规则面板、隐藏规则面板并视情况一并隐藏地图面板
    private function onShowNewPanel(): Void {
        var TRACE_PREFIX: String = "[" + CLASS_NAME + "::onShowNewPanel] ";
        trace(TRACE_PREFIX);
        
        _gameSettings.rulesPanel._visible = false;
        if (!_isSkirmish) {
            _gameSettings.mapPanel._visible = false;
        }
        if (!_panel) {
            return;
        }
        // 假如不是局域网，直接显示面板
        if (!_isLocalAreaNetwork) {
            _panel._visible = true;
            _lanWarningPanel._visible = false;
            return;
        }
        // 假如是局域网，显示局域网警告
        // 让局域网警告过一段时间之后慢慢消失，同时新的扩展规则面板慢慢显示
        _panel._visible = true;
        _lanWarningPanel._visible = true;
        _panel._alpha = 0;
        _lanWarningPanel._alpha = 100;
        var intervalId: Number;
        var count: Number = 400;
        intervalId = createSafeInterval(_global.bind0(this, function() {
            count -= 5;
            var alpha: Number = Math.max(0, Math.min(100, count));
            var inverseAlpha: Number = Math.max(20, 100 - alpha);
            _lanWarningPanel._alpha = alpha;
            _panel._alpha = inverseAlpha;
            if (alpha <= 0 || !_panel._visible) {
                // alpha <= 0 代表渐变结束，而 !_panel._visible 代表选项卡又被切换了
                clearInterval(intervalId);
                return;
            }
        }), 40);
    }

    // 显示遭遇战的规则面板并隐藏新的扩展规则面板
    private function onShowSkirmishRulesPanel(): Void {
        var TRACE_PREFIX: String = "[" + CLASS_NAME + "::onShowSkirmishRulesPanel] ";
        trace(TRACE_PREFIX);
        
        _gameSettings.rulesPanel._visible = true;
        if (_panel) {
            _panel._visible = false;
        }
    }

    private function createCheckBox(
        name: String,
        loadVariablesPath: String,
        additionalQuery: String,
        variableName: String
    ): MovieClip {
        var checkBox: MovieClip = _panel.attachMovie(
            "std_MouseCheckBoxSymbol",
            name,
            ++_lastDepth, {
                m_width: 200,
                m_textAlign: false,
                m_labelPosition: "right align",
                m_label_x: 0,
                m_label_y: 0,
                m_focusDirs: "Up/Down",
                m_iconType: "default",
                m_extLabel: "",
                m_nTruncateType: 0,
                m_label: "",
                m_bEnabled: true,
                m_refFM: "_root.gFM",
                m_initiallySelected: false,
                m_tabIndex: 1,
                m_bVisible: true,
                m_contentSymbol: "checkBoxContentClipSymbol"
            }
        );
        checkBox.setOnMouseDownFunction(function() {
            var url: String = formatQueryUrl(loadVariablesPath, "Toggle=1", additionalQuery);
            var ret = new Object();
            loadVariables(url, ret);
        });
        _refreshFunctions.push(function() {
            var url: String = formatQueryUrl(loadVariablesPath, "", additionalQuery);
            var ret = new Object();
            loadVariables(url, ret);
            ret[variableName] === "1" ? checkBox.check() : checkBox.unCheck();
        });
        _setEnableFunctions.push(function(enable) {
            if (enable) {
                checkBox.enable();
            }
            else {
                checkBox.disable();
            }
        });
        return checkBox;
    }

    private function createDropDownMenu(
        name: String,
        loadVariablesPath: String,
        additionalQuery: String,
        valueVariablesName: String,
        textVariablesName: String
    ): MovieClip {
        var dropDownMenu: MovieClip = _panel.attachMovie(
            "std_MouseDropDownMenuSymbol",
            name,
            ++_lastDepth, {
                m_focusDirs: "Up/Down",
                m_refFM: "_root.gFM",
                m_initiallySelected: false,
                m_tabIndex: -1,
                m_bVisible: true,
                m_visibleEntryCount: 5,
                m_width: 140,
                m_contentSymbol: "TextDropdownMenuContentSymbol",
                m_entrySymbol: "std_mouseListboxEntryTextElementSymbol",
                m_ButtonWidth: 27
            }
        );
        dropDownMenu.setOnChange(function() {
            var value = String(dropDownMenu.getValueAtIndex(dropDownMenu.getCurrentIndex()));
            var url: String = formatQueryUrl(loadVariablesPath, "SetValue=" + value, additionalQuery);
            var ret = new Object();
            loadVariables(url, ret);
        });
        _refreshFunctions.push(function() {
            var url: String = formatQueryUrl(loadVariablesPath, "", additionalQuery);
            var ret = new Object();
            loadVariables(url, ret);
            var values: Array = ret[valueVariablesName].split(",");
            // 第一个元素是当前选中的值的序号
            // 移除第一个元素并保存
            var selectedIndex: Number = Number(values.shift());
            var strings: Array = ret[textVariablesName].split(",");
            dropDownMenu.setData(strings, values);
            dropDownMenu.setSelectedIndex(selectedIndex);
        });
        _setEnableFunctions.push(function(enable) {
            if (enable) {
                dropDownMenu.enable();
            }
            else {
                dropDownMenu.disable();
            }
        });
        return dropDownMenu;
    }

    private static function formatQueryUrl(base: String, query: String, additional: String): String {
        var url: String = base;
        // ensure query is a string, if not, let it be an empty string
        if (!query) {
            query = "";
        }
        if (additional && additional.length > 0) {
            if (query.length > 0) {
                query += "&";
            }
            query += additional;
        }
        if (query.length > 0) {
            url += "?";
            url += query;
        }
        return url;
    }

    // 创建文本框（带阴影）
    private function createTextField(name: String, text: String, x: Number, y: Number, isSmall: Boolean): Void {
        _panel.createTextField(
            name + "_shadow",
            ++_lastDepth,
            x + 2,
            y + 4,
            500,
            80
        );
        var newShadowTextField: TextField = _panel[name + "_shadow"];
        newShadowTextField.setTextFormat(_shadowTextFormat);
        newShadowTextField.text = text;
        newShadowTextField.wordWrap = true;

        _panel.createTextField(
            name,
            ++_lastDepth,
            x,
            y,
            500,
            80
        );
        var newTextField: TextField = _panel[name];
        newTextField.setTextFormat(_textFormat);
        newTextField.text = text + "&Outline";
        newTextField.wordWrap = true;

        if (isSmall === true) {
            newShadowTextField.setTextFormat(_shadowSmallTextFormat);
            newTextField.setTextFormat(_smallTextFormat);
        }
    }

    // 创建选项卡按钮
    private function createTabButton(name: String, depth: Number, label: String): MovieClip {
        return _gameSettings.attachMovie(
            "std_MouseButtonSymbol",
            name,
            depth, {
                m_width: 400,
                m_textAlign: false,
                m_labelPosition: "right align",
                m_label_x: 0,
                m_label_y: 0,
                m_focusDirs: "Up/Down",
                m_iconType: "default",
                m_extLabel: "",
                m_nTruncateType: 0,
                m_label: label,
                m_bEnabled: true,
                m_refFM: "_root.gFM",
                m_initiallySelected: false,
                m_tabIndex: -1,
                m_bVisible: true,
                m_contentSymbol: "MPtabButtonContentClip"
            }
        );
    }

    // 在下一帧执行函数（使用 createSafeInterval）
    private function onNextFrame(f: Function): Void {
        var intervalId: Number;
        intervalId = createSafeInterval(function() {
            clearInterval(intervalId);
            f();
        }, 40); 
        // 红警3里面的帧率是 30fps，一帧是 33.3333ms
        // 40ms 是一个比较安全的值，可以保证在下一帧执行
        // 而且，APT 里的时间是与帧率严格相关的，
        // 哪怕游戏由于性能原因导致实际帧率下降，
        // APT 里感受到的帧率也依然是 30fps，只是 apt 自己的时间变慢了而已
    }

    // 避免让函数在 _gameSettings 不复存在的时候还能被继续调用
    // 以避免出现一些奇怪的问题
    private function createSafeInterval(f: Function, interval: Number): Number {
        var intervalId: Number;
        intervalId = setInterval(_global.bind0(this, function() {
            if (_gameSettings) {
                f();
            }
            else {
                clearInterval(intervalId);
            }
        }), interval);
        return intervalId;
    }
}