// EnhancerApt.MapUiMotion：地图 UI 的数值与颜色过渡模型。
// 只使用 Flash MX 2004 支持的 AS2 语法，不依赖时间轴、控件或外部类。
class EnhancerApt.MapUiMotion {
    private var MAX_ACTIVE:Number;              // 同时存在的过渡上限
    private var MAX_DURATION:Number;            // 单次过渡最长秒数
    private var MAX_STEP:Number;                // 单帧最多推进的秒数
    private var MAX_CHANNEL:Number;             // RGB 通道上限
    private var EPSILON:Number;                 // 吸收浮点累加误差
    private var RECORD_PREFIX:String;           // 隔离用户键与对象原型

    private var records:Object;
    private var activeCount:Number;

    public function MapUiMotion() {
        this.MAX_ACTIVE = 1024;
        this.MAX_DURATION = 5;
        this.MAX_STEP = 0.1;
        this.MAX_CHANNEL = 255;
        this.EPSILON = 0.000000001;
        this.RECORD_PREFIX = "motion_";
        this.records = {};
        this.activeCount = 0;
    }

    // 建立或替换过渡；返回起点副本，duration 为 0 时返回目标副本。
    public function retarget(key:String, current:Object, target:Object,
            duration:Number, colors:Object):Object {
        if (typeof key != "string" || !this.isFiniteNumber(duration)
                || duration < 0 || duration > this.MAX_DURATION) {
            return undefined;
        }
        var input:Object = this.prepareInput(current, target, colors);
        if (input == null) {
            return undefined;
        }

        var id:String = this.RECORD_PREFIX + key;
        var old:Object = this.records[id];
        var start:Object;
        if (old != null) {
            // 活动键必须从当前插值结果续接，忽略调用方的 current。
            var sampled:Object = this.sample(old, this.progressOf(old));
            start = {};
            for (var name in input.to) {
                if (this.hasOwn(input.to, name)) {
                    // 新出现的字段没有旧插值可用，退回本次 current。
                    start[name] = this.hasOwn(sampled, name)
                        ? sampled[name] : input.from[name];
                }
            }
        } else {
            start = input.from;
        }

        if (duration == 0) {
            this.drop(id, old);
            return this.copyValues(input.to);
        }
        if (this.sameValues(start, input.to)) {
            this.drop(id, old);
            return this.copyValues(start);
        }
        if (old == null && this.activeCount >= this.MAX_ACTIVE) {
            return undefined;
        }

        var record:Object = {
            key: key,
            from: this.copyValues(start),
            to: this.copyValues(input.to),
            colors: input.colors,
            elapsed: 0,
            duration: duration
        };
        this.records[id] = record;
        if (old == null) {
            this.activeCount++;
        }
        return this.copyValues(start);
    }

    // 推进所有活动过渡；非法时长不产生任何变化。
    public function tick(seconds:Number):Array {
        var updates:Array = [];
        if (!this.isFiniteNumber(seconds) || seconds < 0) {
            return updates;
        }
        var step:Number = seconds;
        if (step > this.MAX_STEP) {
            step = this.MAX_STEP; // 长帧只推进固定上限，避免跳过中间状态
        }

        var ids:Array = [];
        for (var id in this.records) {
            if (this.hasOwn(this.records, id)) {
                ids.push(id);
            }
        }

        var i:Number;
        for (i = 0; i < ids.length; i++) {
            var record:Object = this.records[ids[i]];
            if (record == null) {
                continue;
            }
            var elapsed:Number = record.elapsed + step;
            var finished:Boolean = false;
            if (elapsed >= record.duration) {
                elapsed = record.duration;
                finished = true;
            } else if (step > 0 && record.duration - elapsed <= this.EPSILON) {
                elapsed = record.duration;
                finished = true;
            }
            record.elapsed = elapsed;

            var values:Object = this.sample(record, this.progressOf(record));
            updates.push({key: record.key, values: values, finished: finished});
            if (finished) {
                this.drop(ids[i], record);
            }
        }
        return updates;
    }

    // 删除指定活动键；不存在的键静默忽略。
    public function remove(key:String):Void {
        if (typeof key != "string") {
            return;
        }
        var id:String = this.RECORD_PREFIX + key;
        if (this.hasOwn(this.records, id)) {
            this.drop(id, this.records[id]);
        }
    }

    // 清空全部活动状态，之后不会再有旧过渡的回调。
    public function clear():Void {
        this.records = {};
        this.activeCount = 0;
    }

    public function count():Number {
        return this.activeCount;
    }

    // 先完整校验并复制，确保任何非法输入都不会改动已有状态。
    private function prepareInput(current:Object, target:Object, colors:Object):Object {
        if (!this.isRecord(current) || !this.isRecord(target) || !this.isRecord(colors)) {
            return null;
        }
        var from:Object = {};
        var to:Object = {};
        var flags:Object = {};
        var name:String;
        for (name in current) {
            if (this.hasOwn(current, name)) {
                if (!this.isFiniteNumber(current[name]) || !this.hasOwn(target, name)
                        || !this.isFiniteNumber(target[name])) {
                    return null;
                }
                from[name] = current[name];
                to[name] = target[name];
            }
        }
        for (name in target) {
            if (this.hasOwn(target, name)
                    && (!this.hasOwn(current, name) || !this.isFiniteNumber(target[name]))) {
                return null; // 双向检查，保证键集合完全一致
            }
        }
        for (name in colors) {
            if (this.hasOwn(colors, name)) {
                if (colors[name] !== true || !this.hasOwn(from, name)) {
                    return null;
                }
                flags[name] = true;
            }
        }
        return {from: from, to: to, colors: flags};
    }

    private function progressOf(record:Object):Number {
        if (record.duration <= 0) {
            return 1;
        }
        var t:Number = record.elapsed / record.duration;
        if (t < 0) {
            t = 0;
        } else if (t > 1) {
            t = 1;
        }
        return t * t * (3 - 2 * t); // cubic smoothstep
    }

    // 按给定进度生成独立结果；颜色字段按 0xRRGGBB 分通道插值。
    private function sample(record:Object, progress:Number):Object {
        var values:Object = {};
        for (var name in record.from) {
            if (this.hasOwn(record.from, name)) {
                var a:Number = record.from[name];
                var b:Number = record.to[name];
                var value:Number;
                if (record.colors[name] === true) {
                    value = this.interpolateColor(a, b, progress);
                } else {
                    value = a + (b - a) * progress;
                }
                values[name] = value;
            }
        }
        return values;
    }

    // 拆出红、绿、蓝通道，各自取整夹紧后重新打包为 0xRRGGBB。
    private function interpolateColor(a:Number, b:Number, progress:Number):Number {
        var ar:Number = (a >> 16) & this.MAX_CHANNEL;
        var ag:Number = (a >> 8) & this.MAX_CHANNEL;
        var ab:Number = a & this.MAX_CHANNEL;
        var br:Number = (b >> 16) & this.MAX_CHANNEL;
        var bg:Number = (b >> 8) & this.MAX_CHANNEL;
        var bb:Number = b & this.MAX_CHANNEL;
        var red:Number = this.clampChannel(Math.round(ar + (br - ar) * progress));
        var green:Number = this.clampChannel(Math.round(ag + (bg - ag) * progress));
        var blue:Number = this.clampChannel(Math.round(ab + (bb - ab) * progress));
        return (red << 16) | (green << 8) | blue;
    }

    private function clampChannel(value:Number):Number {
        if (value <= 0) {
            return 0;
        }
        if (value > this.MAX_CHANNEL) {
            return this.MAX_CHANNEL;
        }
        return value;
    }

    private function sameValues(a:Object, b:Object):Boolean {
        for (var name in a) {
            if (this.hasOwn(a, name) && (!this.hasOwn(b, name) || a[name] !== b[name])) {
                return false;
            }
        }
        for (name in b) {
            if (this.hasOwn(b, name) && !this.hasOwn(a, name)) {
                return false;
            }
        }
        return true;
    }

    private function copyValues(source:Object):Object {
        var copy:Object = {};
        for (var name in source) {
            if (this.hasOwn(source, name)) {
                copy[name] = source[name];
            }
        }
        return copy;
    }

    // 删除记录并同步计数，供完成、替换和清空路径复用。
    private function drop(id:String, record:Object):Void {
        if (record == null) {
            return;
        }
        delete this.records[id];
        if (this.records[id] != undefined) {
            var remaining:Object = {};
            for (var name in this.records) {
                if (name != id) {
                    remaining[name] = this.records[name];
                }
            }
            this.records = remaining;
        }
        if (this.activeCount > 0) {
            this.activeCount--;
        }
    }

    private function isRecord(value):Boolean {
        return typeof value == "object" && value != null && !(value instanceof Array);
    }

    private function isFiniteNumber(value):Boolean {
        // APT 不提供全局 isFinite；NaN 和正负无穷相减均不等于 0。
        return typeof value == "number" && value - value == 0;
    }

    private function hasOwn(source:Object, name:String):Boolean {
        if (Object.prototype.hasOwnProperty != undefined) {
            return Boolean(Object.prototype.hasOwnProperty.call(source, name));
        }
        // APT 没有 hasOwnProperty。输入只来自本框架建立的普通数值表；
        // 排除原型继承成员，记录键另有固定前缀，不接受自定义原型数据模型。
        return source[name] != undefined &&
            (source.__proto__ == undefined || source[name] !== source.__proto__[name]);
    }
}
