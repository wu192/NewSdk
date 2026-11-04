import EnhancerApt.Bind;
import EnhancerApt.CommonBtn;
import EnhancerApt.World;
import EnhancerApt.GameObject;

class EnhancerApt.PublicBoardManager {

    private var _publicBoard: GameObject;

    public function PublicBoardManager(screen: MovieClip, _world: World) {
        //max height 468 , draw from bottom
        _publicBoard = _world.instantiate("PublicBoard");
        trace("PublicBoardManager init " + _publicBoard);
        _publicBoard.setPosition({ x : 109, y: 123});
        _publicBoard.sprite().tv.text = "";
        _publicBoard.sprite().tvShadow.text = "";

        screen.setPublicBoardText = Bind.oneArg(this, function(content: String) {
            _publicBoard.sprite().tv.text = content;
            _publicBoard.sprite().tvShadow.text = content;
        });

        screen.setPublicBoardTextColor = Bind.oneArg(this, function(color: Number) {
            _publicBoard.sprite().tv.textColor = color;
            var shadow: TextField = _publicBoard.sprite().tvShadow;

            color = Number(color);
            var red: Number = (color >> 16) & 0xFF;
            var green: Number = (color >> 8) & 0xFF;
            var blue: Number = color & 0xFF;
            var brightness: Number = (red * 299 + green * 587 + blue * 114) / 1000;
            if (brightness < 50) {
                shadow._visible = false;
            } else {
                shadow._visible = true;
            }
        });

    }

}