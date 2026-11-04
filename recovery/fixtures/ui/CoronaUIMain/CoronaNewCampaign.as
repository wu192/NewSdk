class CoronaNewCampaign {
    private static var CLASS_NAME = "CoronaNewCampaign";

    public static function support(): Void {
        // 适配第四阵营战役：
        _global.ENUM.CAMPAIGN_TYPE_CELESTIAL = 3;
        trace("[" + CLASS_NAME + "] CAMPAIGN_TYPE_CELESTIAL: " + _global.ENUM.CAMPAIGN_TYPE_CELESTIAL);
        var chapterSelectPrototype = _global.mouse_chapterSelectComponent.prototype;
        if (chapterSelectPrototype.CoronaNewCampaign_OriginalInitGUI) {
            trace("[" + CLASS_NAME + "] mouse_chapterSelectComponent already patched");
            return;
        }
        chapterSelectPrototype.CoronaNewCampaign_OriginalInitGUI = chapterSelectPrototype.initGUI;
        chapterSelectPrototype.initGUI = function() {
            // 现在函数的 this 是 mouse_chapterSelectComponent
            return mouseChapterSelectInitGUI(this);
        }
    }

    private static function mouseChapterSelectInitGUI(self): Void {
        trace("[" + CLASS_NAME + "::mouseChapterSelectInitGUI] self: " + self);
        self.missionList.setNumEntries(9);
        self.missionList.setVerticalPadding(0);
        self.missionList.addListener(self);
        var missionListEntries: Array = new Array();
        var missionListEntry: Object = new Object();
        missionListEntry.symbol = "std_mouseListboxEntryTextElementSymbol";
        missionListEntry.width = 432;
        missionListEntry.leftMargin = 0;
        missionListEntry.rightMargin = 0;
        missionListEntry.fontSize = 14;
        missionListEntries.push(missionListEntry);
        self.missionList.init(missionListEntries, 22);
        self.missionList.attachScrollbarController(self.missionListScrollbar);
        self.radioButton_allies.setOnMouseDownFunction(_global.bind1(self, self.setMissionList, _global.ENUM.CAMPAIGN_TYPE_ALLIES));
        self.radioButton_soviets.setOnMouseDownFunction(_global.bind1(self, self.setMissionList, _global.ENUM.CAMPAIGN_TYPE_SOVIET));
        self.radioButton_japan.setOnMouseDownFunction(_global.bind1(self, self.setMissionList, _global.ENUM.CAMPAIGN_TYPE_RISINGSUN));
        // 绑定新按钮
        self.radioButton_celestial.setOnMouseDownFunction(function() {
            trace("[" + CLASS_NAME + "] Celestial radio button clicked");
            self.setMissionList(_global.ENUM.CAMPAIGN_TYPE_CELESTIAL);
        });
        self.radioButton_celestial.show();
        self.radioButton_celestial.enable();

        var currentRadioButton = undefined;
        var ret = new Object();
        loadVariables("QueryGameEngine?CAMPAIGN_MISSION&CURRENT_FACTION", ret);
        trace("[" + CLASS_NAME + "::mouseChapterSelectInitGUI] CURRENT_FACTION: " + ret.CURRENT_FACTION);
        switch(Number(ret.CURRENT_FACTION)) {
            case _global.ENUM.CAMPAIGN_TYPE_ALLIES:
            default:
                currentRadioButton = self.radioButton_allies;
                self.setMissionList(_global.ENUM.CAMPAIGN_TYPE_ALLIES);
                break;
            case _global.ENUM.CAMPAIGN_TYPE_SOVIET:
                currentRadioButton = self.radioButton_soviets;
                self.setMissionList(_global.ENUM.CAMPAIGN_TYPE_SOVIET);
                break;
            case _global.ENUM.CAMPAIGN_TYPE_RISINGSUN:
                currentRadioButton = self.radioButton_japan;
                self.setMissionList(_global.ENUM.CAMPAIGN_TYPE_RISINGSUN);
                break;
            case _global.ENUM.CAMPAIGN_TYPE_CELESTIAL:
                trace("[" + CLASS_NAME + "::mouseChapterSelectInitGUI] CELESTIAL campaign detected");
                currentRadioButton = self.radioButton_celestial;
                self.setMissionList(_global.ENUM.CAMPAIGN_TYPE_CELESTIAL);
                break;
        }
        self.radioController_faction.SetSelectedRadioButton(currentRadioButton);
        var campaignMissionIndex: Number = ret.CAMPAIGN_MISSION;
        if (campaignMissionIndex < 0) {
            campaignMissionIndex = 0;
        }
        self.missionList.setSelectedIndex(campaignMissionIndex);
    }
}