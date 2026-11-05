import type { Range, Vec3 } from './types';

/** RA3 local FX stream: research 12-rng, fixed revision 6f50d090. */
export class FxRandom {
    readonly words = new Uint32Array(6);
    calls = 0;
    constructor(seed = 1) {
        [0xf22d0e56, 0x883126e9, 0xc624dd2f, 0x0702c49c, 0x9e353f7d, 0x6fdf3b64]
            .forEach((k, i) => this.words[i] = (seed + k) >>> 0);
    }
    next(): number {
        const w = this.words;
        let t = (w[4] + w[5]) >>> 0;
        let c = Number(t < w[5] || t < w[4]);
        w[4] = t;
        for (let j = 3; j >= 1; j--) {
            const old = w[j]; t = (t + ((old + c) >>> 0)) >>> 0;
            c = Number(t < old); w[j] = t;
        }
        w[0] = (w[0] + ((t + c) >>> 0)) >>> 0;
        for (let j = 5; j >= 0; j--) { w[j] = (w[j] + 1) >>> 0; if (w[j] !== 0) break; }
        this.calls++;
        return w[0];
    }
    /** RN coefficient 2^-32; spread is stored as float32 before native x87 arithmetic. */
    range(low: number, high: number): number {
        const spread = Math.fround(high - low);
        return spread <= 0 ? high : Math.fround(low + this.next() * 2 ** -32 * spread);
    }
    sample(r: Range): number { return r.low === r.high && r.type === 'CONSTANT' ? r.low : this.range(r.low, r.high); }
    integer(low: number, high: number): number {
        const span = (high - low + 1) >>> 0;
        return span === 0 ? high : (low + this.next() % span) >>> 0;
    }
    direction(): Vec3 {
        // The native algorithm rejects only a near-zero cube, not points outside a sphere.
        for (let attempt = 0; attempt < 1024; attempt++) {
            const v: Vec3 = [this.range(-1, 1), this.range(-1, 1), this.range(-1, 1)];
            if (v.every(x => Math.abs(x) <= 2 ** -23)) continue;
            const inv = Math.fround(1 / Math.hypot(...v));
            return v.map(x => Math.fround(x * inv)) as Vec3;
        }
        throw new Error('方向取样达到预览重试预算');
    }
}

/** Public standard shader table, Windows branch. Table lookups never advance FxRandom. */
export const RANDOM_TABLE: readonly (readonly number[])[] = [
    [0.957897000503582,0.378870525347763,0.363571890095615,0.328760908964182],
    [0.176237249178937,0.914575110140635,0.436093504459558,0.347628420779846],
    [0.74321894178331,0.442561117711613,0.997454573448046,0.362213792964882],
    [0.489662653412263,0.666423292057291,0.830569846431306,0.974257713027646],
    [0.894767456143942,0.823621517393205,0.930970549797899,0.0521984733736147],
    [0.141100733280401,0.652700235943417,0.00845252291957532,0.643057387894008],
    [0.22979899343149,0.897999019438672,0.753821963794792,0.131650373484698],
    [0.581538391481718,0.283691652574809,0.0915647105845903,0.407480940983646],
    [0.270604393931209,0.72281806272639,0.16943672558123,0.146905246115598],
    [0.599555574957733,0.096453300376983,0.876398320463974,0.117222883790552],
    [0.510171077745377,0.942157193101133,0.195788791440078,0.916756497322788],
    [0.0354679287382584,0.534009408848821,0.167321778635773,0.557676417292525],
    [0.861564026888471,0.225160105115149,0.0823319997715926,0.87900387469963],
    [0.173086589845635,0.89351880243715,0.0139240580314164,0.178738120005564],
    [0.826938346482238,0.771814362160947,0.439535939575699,0.49396301649165],
    [0.542361347073796,0.721861671956157,0.769731420798597,0.587856677976187],
].map(row => row.map(Math.fround));
export function tableValue(seed: number, channel: number, component = 0): number {
    const x = (11 * channel + 3 * seed) / 16;
    return RANDOM_TABLE[Math.trunc((x - Math.floor(x)) * 16)][component];
}
export function tableSample(range: Range, seed: number, channel: number, component = 0): number {
    return range.low + tableValue(seed, channel, component) * (range.high - range.low);
}
