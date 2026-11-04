import CoronaGameSettingsNewPanel;
import CoronaNewCampaign;
import MainMenuOverlayLoader;

class CoronaUIMain {
    private static var CLASS_NAME: String = "CoronaUIMain";
    private static var s_IsCorona: Boolean = true;
    private static var s_FrameDelayedInitMap: Object = new Object();
    private static var s_HasCustomCampaign: Boolean = false;

    public static function Main(apt: MovieClip) {
        // apt 被 feg_m_main 挂载在 _level0.gShellScreen.coronaMC 上
        trace("[" + CLASS_NAME + "] apt: " + apt);

        // 为了加载日冕的新面板，挂钩 feg_m_main 的函数
        s_FrameDelayedInitMap = new Object();
        s_FrameDelayedInitMap["GameSetupBase"] = function(screen: Object) {
            // 仅限日冕 / AR：拓展面板
            CoronaGameSettingsNewPanel.patchGameSetupBase(screen);
        }
        s_FrameDelayedInitMap["feg_m_mainMenu3D"] = function(screen: Object) {
            var TRACE_PREFIX: String = "[" + CLASS_NAME + "::feg_m_mainMenu3D.frameDelayedInit] ";
            trace(TRACE_PREFIX);
            // 检测客户端版本
            MainMenuOverlayLoader.enableLauncherVersionCheck(screen);
            // 仅限日冕：当点击“战役”按钮时，显示战役警告
            // 但假如有自定义战役包，不警告
            if (!s_HasCustomCampaign) {
                MainMenuOverlayLoader.enableCampaignWarning(screen);
            }
        }
        patchDoScreenTransition();

        // 加载 enhancer
        _level0.hud.createEmptyMovieClip("enhancerAptImpl", 50);
        _level0.hud.enhancerAptImpl.loadMovie("enhancerapt.swf");

        // 适配第四阵营战役
        CoronaNewCampaign.support();

        // 检测是否有自定义战役
        var readResult = readFile("data/custom_campaign.txt");
        if (!readResult || !readResult.content || readResult.content == "") {
            s_HasCustomCampaign = false;
        } else {
            s_HasCustomCampaign = true;
        }
    }

    // 为了添加新的面板，我们需要修改 GameSetupBase
    // 但是 GameSetupBase 在进入遭遇战界面/进入房间之前仍未被加载，根本还不存在，无法修改
    // 等到进入遭遇战界面/进入房间之后，GameSetupBase 已经加载完毕，也来不及修改了
    // 为了解决这个问题，必须在 apt 刚刚被加载、而且 frameDelayedInit 还未被调用的时候，就要修改它。
    // 为了做到这一点，可以先挂钩 feg_m_main 的 doScreenTransition 方法
    // doScreenTransition 负责加载 apt，而刚刚被加载的 apt 并不会立即执行 frameDelayedInit
    // （因为 frameDelayedInit 是在下一帧执行的）
    // 因此我们可以在 doScreenTransition 里面修改 frameDelayedInit
    public static function patchDoScreenTransition(): Void {
        var TRACE_PREFIX: String = "[" + CLASS_NAME + "::patchDoScreenTransition] ";
        var screenPrototype = _global.feg_m_main.prototype;
        // CoronaUIMain_OriginalDoScreenTransition 用于保存原来的 doScreenTransition 方法
        // 之后 doScreenTransition 将会被我们替换
        // 考虑到可能将来会有其他的插件也会修改 doScreenTransition 方法（虽然这种可能性很小）
        // 因此我们取了一个比较长的而且独特的名字： CoronaUIMain_OriginalDoScreenTransition
        // 其他插件应该不会取这个名字，这样就避免了冲突
        if (screenPrototype.CoronaUIMain_OriginalDoScreenTransition) {
            trace(TRACE_PREFIX + "feg_m_main already patched");
            return;
        }
        screenPrototype.CoronaUIMain_OriginalDoScreenTransition = screenPrototype.doScreenTransition;
        screenPrototype.doScreenTransition = function() {
            // 现在函数的 this 是 feg_m_main
            var feg_m_main = this;
            // 先执行原来的 doScreenTransition，让 screen 可以被正确加载
            var returnValue: Boolean = feg_m_main.CoronaUIMain_OriginalDoScreenTransition();
            // 在 screen 被加载之后，尝试修改它们
            tryPatchFrameDelayedInit();
            return returnValue;
        }
    }

    public static function readFile(filePath: String): String {
        var TRACE_PREFIX: String = "[" + CLASS_NAME + "::readFile] ";
        trace(TRACE_PREFIX + "filePath: " + filePath);
        var result = new Object();
        _level0.CoronaUIMainRead = function(text: String) {
            var TRACE_PREFIX: String = "[" + CLASS_NAME + "::CoronaUIMainRead] ";
            trace(TRACE_PREFIX + "text: " + text);
            result.content = text;
        };
        var query = new Object();
        loadVariables("LanyiRa3AptReadFile?"
            + "MOVIECLIP=_level0&"
            + "READ_FUNCTION=CoronaUIMainRead&"
            + "FILE_PATH=data/custom_campaign.txt", query);
        _level0.CoronaUIMainRead = undefined;
        delete _level0.CoronaUIMainRead;
        result.error = query.ERROR;
        result.succeeded = query.SUCCEEDED;
        trace(TRACE_PREFIX + "result.succeeded: " + result.succeeded + ", result.error: " + result.error);
        return result;
    }

    private static function tryPatchFrameDelayedInit(): Void {
        var TRACE_PREFIX: String = "[" + CLASS_NAME + "::tryPatchFrameDelayedInit] ";
        trace(TRACE_PREFIX);

        for (var className: String in s_FrameDelayedInitMap) {
            if (!_global[className]) {
                continue;
            }
            
            var prototype = _global[className].prototype;
            if (prototype.CoronaUIMain_OriginalFrameDelayedInit != undefined) {
                trace(TRACE_PREFIX + className + " already patched");
                continue;
            }
            trace(TRACE_PREFIX + "patching " + className + ".frameDelayedInit");
            prototype.CoronaUIMain_OriginalFrameDelayedInit = prototype.frameDelayedInit;
            // 不能直接写成：
            // prototype.frameDelayedInit = function() { s_FrameDelayedInitMap[className].call(this); ... }
            // 因为 className 是用“var”声明的（ActionScript 2.0 不支持 let、没有块级作用域）
            // for 循环里的每一次迭代都共享同一个 className 变量
            // 因此等到 frameDelayedInit 被调用的时候，className 已经变成最后一次迭代的值了
            // 解决方法就是用一个辅助函数 createdPatchedFrameDelayedInit
            // 利用一个新的函数作用域来“锁定”住每一次迭代的 className 变量
            prototype.frameDelayedInit = createdPatchedFrameDelayedInit(s_FrameDelayedInitMap[className]);
        }
    }

    private static function createdPatchedFrameDelayedInit(newFunction: Function): Function {
        return function() {
            var screen = this;
            newFunction(screen);
            // 在执行我们的自定义逻辑之后，再调用原来的 frameDelayedInit
            screen.CoronaUIMain_OriginalFrameDelayedInit();
        };
    }
}