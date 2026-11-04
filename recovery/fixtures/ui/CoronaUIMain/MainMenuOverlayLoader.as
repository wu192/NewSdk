class MainMenuOverlayLoader {
    private static var CLASS_NAME: String = "MainMenuOverlayLoader";

    public static function enableLauncherVersionCheck(screen: Object): Void {
        var TRACE_PREFIX: String = "[" + CLASS_NAME + "::enableLauncherVersionCheck] ";
        var prototype = screen.__proto__;
        trace(TRACE_PREFIX + "prototype: " + prototype);
        if (prototype.CoronaUIMain_OriginalCheckForNewPatch != undefined) {
            return;
        }
        prototype.CoronaUIMain_OriginalCheckForNewPatch = prototype.checkForNewPatch;
        prototype.checkForNewPatch = function() {
            var feg_m_mainMenu3D = this;
            feg_m_mainMenu3D.CoronaUIMain_OriginalCheckForNewPatch();
            loadLanyiOverlay("CORONA_LAUNCHER_VERSION");
        };
    }

    public static function enableCampaignWarning(screen: Object): Void {
        var TRACE_PREFIX: String = "[" + CLASS_NAME + "::enableCampaignWarning] ";
        var prototype = screen.__proto__;
        trace(TRACE_PREFIX + "prototype: " + prototype);
        if (prototype.CoronaUIMain_OriginalProcessCampaignMenuClick != undefined) {
            return;
        }
        prototype.CoronaUIMain_OriginalProcessCampaignMenuClick = prototype.processCampaignMenuClick;
        prototype.processCampaignMenuClick = function(menuItem: Number) {
            loadLanyiOverlay("CAMPAIGN_WARNING");
            var feg_m_mainMenu3D = this;
            feg_m_mainMenu3D.CoronaUIMain_OriginalProcessCampaignMenuClick(menuItem);
        };
    }

    private static function loadLanyiOverlay(data: String): Void {
        var TRACE_PREFIX: String = "[" + CLASS_NAME + "::loadLanyiOverlay] ";
        trace(TRACE_PREFIX);
        _root.lanyiTemporaryMainMenuOverlayData = data;
        _root.createEmptyMovieClip("lanyi_CoronaMainMenuTemporaryOverlayHolderN1", 38).loadMovie("CoronaMainMenuOverlay.swf");
    }
}
