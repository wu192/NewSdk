

class EnhancerApt.Bind {

    public static function noArg(o: Object, f: Function): Function {
        return function() { return f.call(o); };
    }

    public static function noArgCapture1(o: Object, arg1: Object, f: Function): Function {
        return function() {
            return f.call(o, arg1);
        }
    }

    public static function oneArg(o: Object, f: Function): Function {
        return function(a) { return f.call(o, a); };
    }

    public static function twoArg(o: Object, f: Function): Function {
        return function(a, b) { return f.call(o, a, b); };
    }

    public static function threeArg(o: Object, f: Function): Function {
        return function(a, b, c) { return f.call(o, a, b, c); };
    }

    public static function fourArg(o: Object, f: Function): Function {
        return function(a, b, c, d) { return f.call(o, a, b, c, d); };
    }

    public static function fiveArg(o: Object, f: Function): Function {
        return function(a, b, c, d, e) { return f.call(o, a, b, c, d, e); };
    }
}