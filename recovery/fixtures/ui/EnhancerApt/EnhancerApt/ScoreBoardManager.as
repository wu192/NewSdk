import EnhancerApt.Bind;
import EnhancerApt.CommonBtn;
import EnhancerApt.World;
import EnhancerApt.GameObject;

class EnhancerApt.ScoreBoardManager {

    private static var MAX_ROWS: Number = 9;
    private static var DEFAULT_ROWS: Number = 7;
    private var _scoreBoard: GameObject;
    private var _rowCount: Number;
    private var _initialY: Object;
    private var _backgroundHeight: Number;
    private var _rowSpacing: Number;

    public function ScoreBoardManager(screen: MovieClip, _world: World) {
        //max height 468 , draw from bottom
        _scoreBoard = _world.instantiate("ScoreBoard");
        trace("ScoreBoardManager init " + _scoreBoard);
        _scoreBoard.setPosition({ x : 730, y: 0});
        _scoreBoard.sprite().title.text = "";

        // 文本框由 FLA 提供，改变行数前记录制作时的 y 坐标，避免多次切换累计位移。
        _rowCount = DEFAULT_ROWS;
        _initialY = {};
        rememberInitialPositions();
        var background: MovieClip = _scoreBoard.sprite().panel.background;
        if (background != undefined) {
            _backgroundHeight = background._height;
        }
        if (getField(1, 6) != undefined && getField(1, 5) != undefined) {
            _rowSpacing = getField(1, 6)._y - getField(1, 5)._y;
        }

        clearColumns();
        applyRowCount();

        _scoreBoard.sprite()._visible = false;

        screen.setScoreBoardTitle = Bind.oneArg(this, function(text: String) {
            _scoreBoard.sprite().title.text = text;
        });

        screen.setScoreBoardCol1 = Bind.oneArg(this, function(text: String) {
            setColumn(1, text);
        });
        screen.setScoreBoardCol2 = Bind.oneArg(this, function(text: String) {
            setColumn(2, text);
        });
        screen.setScoreBoardCol3 = Bind.oneArg(this, function(text: String) {
            setColumn(3, text);
        });

        // C++ 调用可能传 Number 或数值字符串，在此统一处理。
        screen.setScoreBoardRowCount = Bind.oneArg(this, function(value) {
            setRowCount(value);
        });

        screen.setScoreBoardVisibility = Bind.oneArg(this, function(visibility: Number) {
            if (visibility == 1) {
                _scoreBoard.sprite()._visible = true;
            } else {
                _scoreBoard.sprite()._visible = false;
            }
        });

        screen.resetScoreBoard = Bind.oneArg(this, function() {
            _scoreBoard.sprite().title.text = "";
            _rowCount = DEFAULT_ROWS;
            clearColumns();
            applyRowCount();
            _scoreBoard.sprite().panel._visible = true;
            _scoreBoard.sprite()._visible = false;
        });

        _scoreBoard.sprite().btn.onPress = bind(function() {
            _scoreBoard.sprite().panel._visible = !_scoreBoard.sprite().panel._visible;
            // if (_scoreBoard.sprite().panel._visible) {
            //     _scoreBoard.sprite().btn._rotation = 0;
            // } else {
            //     _scoreBoard.sprite().btn._rotation = 180;
            // }
        });

    }

    private function setRowCount(value): Void {
        var count: Number = Number(value);
        // FLA 仅提供 7 行和 9 行布局，分别对应六人和八人容量加一行表头。
        if (count != 7 && count != 9) {
            return;
        }
        _rowCount = count;
        applyRowCount();
    }

    private function applyRowCount(): Void {
        relayoutFields();
        var background: MovieClip = _scoreBoard.sprite().panel.background;
        if (background != undefined && !isNaN(_backgroundHeight) && !isNaN(_rowSpacing)) {
            background._height = _backgroundHeight + (_rowCount - DEFAULT_ROWS) * _rowSpacing;
        }
        for (var column = 1; column <= 3; ++column) {
            for (var row = 0; row < MAX_ROWS; ++row) {
                var field: TextField = getField(column, row);
                if (field == undefined) {
                    continue;
                }
                if (row < _rowCount) {
                    field._visible = true;
                } else {
                    field.text = "";
                    field._visible = false;
                }
            }
        }
    }

    private function setColumn(column: Number, text: String): Void {
        if (text == undefined) {
            text = "";
        }
        var values: Array = text.split("\n");
        for (var row = 0; row < MAX_ROWS; ++row) {
            var field: TextField = getField(column, row);
            if (field == undefined) {
                continue;
            }
            if (row < _rowCount) {
                field.text = (row < values.length) ? values[row] : "";
            } else {
                // 新内容较短时清空尾部旧数据，避免残留上一帧或上一局的文字。
                field.text = "";
            }
        }
    }

    private function clearColumns(): Void {
        for (var column = 1; column <= 3; ++column) {
            setColumn(column, "");
        }
    }

    private function getField(column: Number, row: Number): TextField {
        // 只使用 FLA 中已有文本框，不动态创建文本框，以保留原生字体和资源兼容性。
        return _scoreBoard.sprite().panel["col" + column + "_" + row];
    }

    private function fieldKey(column: Number, row: Number): String {
        return "col" + column + "_" + row;
    }

    private function rememberInitialPositions(): Void {
        for (var column = 1; column <= 3; ++column) {
            for (var row = 0; row < MAX_ROWS; ++row) {
                var field: TextField = getField(column, row);
                if (field == undefined || field._y == undefined) {
                    continue;
                }
                var key: String = fieldKey(column, row);
                if (_initialY[key] == undefined) {
                    _initialY[key] = field._y;
                }
            }
        }
    }

    private function relayoutFields(): Void {
        for (var column = 1; column <= 3; ++column) {
            var firstRow: Number = -1;
            var firstY = undefined;
            var lastRow: Number = -1;
            var lastY = undefined;

            for (var scan = 0; scan < MAX_ROWS; ++scan) {
                var savedY = _initialY[fieldKey(column, scan)];
                if (savedY == undefined) {
                    continue;
                }
                if (firstRow < 0) {
                    firstRow = scan;
                    firstY = savedY;
                }
                lastRow = scan;
                lastY = savedY;
            }

            var spacing = undefined;
            if (firstRow >= 0 && lastRow > firstRow) {
                spacing = (lastY - firstY) / (lastRow - firstRow);
            }

            for (var row = 0; row < MAX_ROWS; ++row) {
                var field: TextField = getField(column, row);
                if (field == undefined) {
                    continue;
                }
                var saved = _initialY[fieldKey(column, row)];
                if (saved != undefined) {
                    // 原有行保留 FLA 中的制作位置。
                    field._y = saved;
                } else if (spacing != undefined) {
                    // 新增索引 7/8 的行沿用相同间距。
                    field._y = lastY + (row - lastRow) * spacing;
                }
            }
        }
    }

    private function bind(f: Function): Function {
        var self = this;
        return function() { f.call(self); };
    }

}
