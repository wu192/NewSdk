import { test } from 'node:test';
import assert from 'node:assert/strict';
import { atlasFrame, compileParticle, FxRandom, integrateDamping, ParticleRuntime, ParticleWorld, sampleParticle,
    sampleVelocity, sampleVolume, tableValue, localRotationMatrix, transformVector, type FxNode, type ParticleConfig, type Range } from '../webview/fx/runtime';
const node = (name: string, attributes: Record<string, string> = {}, children: FxNode[] = []): FxNode => ({ name, attributes, children });
const rv = (name: string, low: number, high = low): FxNode => node(name, { Low: String(low), High: String(high) });
function fixture(): FxNode {
    return node('FXParticleSystemTemplate', { id: 'Contract', Type: 'GPU_PARTICLE' }, [
        rv('Lifetime', 30), rv('BurstCount', 1), rv('BurstDelay', 2), rv('InitialDelay', 2), rv('Size', 2),
        node('Physics', {}, [node('Default')]), node('Draw', {}, [node('Gpu', { EnableSoftParticles: 'false' })]),
        node('Volume', {}, [node('Point')]), node('Velocity', {}, [node('Ortho', {}, [node('Position', {}, [rv('x', 1), rv('y', 0), rv('z', 2)])])]),
        node('Event'), node('Update', {}, [node('Default')]),
    ]);
}
function config(): ParticleConfig { const result = compileParticle(fixture()); assert.deepEqual(result.issues, []); return result.config!; }
const range = (low: number, high = low): Range => ({ low, high, type: low === high ? 'CONSTANT' : 'UNIFORM' });

test('GPU contract: InitialDelay and positive BurstDelay retain the extra logical update', () => {
    const runtime = new ParticleRuntime(config(), 7); runtime.seek(8);
    assert.deepEqual(runtime.particles.map(p => p.creationFrame), [2, 5, 8]);
    assert.equal(runtime.random.calls, 3);
    assert.ok(runtime.particles.every(p => !Number.isInteger(p.seed)));
});
test('OneShot zero delay repeats; finite system window closes emission and retains stock', () => {
    const c = config(); c.initialDelay = range(0); c.burstDelay = range(0); c.isOneShot = true;
    const repeated = new ParticleRuntime(c); repeated.seek(3); assert.equal(repeated.particles.length, 4);
    c.burstDelay = range(1); const latched = new ParticleRuntime(c); latched.seek(3); assert.equal(latched.particles.length, 1);
    c.burstDelay = range(0); c.systemLifetime = 1;
    const finite = new ParticleRuntime(c); finite.seek(20); assert.equal(finite.particles.length, 1);
    finite.seek(30); assert.equal(finite.particles.length, 1); finite.seek(31); assert.equal(finite.particles.length, 0);
    const stopped = new ParticleRuntime(c, 1, { systemLifeFrames: 0 }); stopped.seek(3); assert.equal(stopped.particles.length, 0);
});
test('six-word RNG carries updated words; degenerate integer and float helpers consume differently', () => {
    const r = new FxRandom(0);
    assert.deepEqual(Array.from(r.words), [0xf22d0e56,0x883126e9,0xc624dd2f,0x0702c49c,0x9e353f7d,0x6fdf3b64]);
    r.words.set([0,0,0,0,0xffffffff,1]); assert.equal(r.next(), 1); assert.deepEqual(Array.from(r.words), [1,1,1,1,0,2]);
    const before = r.calls; assert.equal(r.range(2,2), 2); assert.equal(r.calls, before);
    assert.equal(r.integer(4,4), 4); assert.equal(r.calls, before + 1);
    assert.equal(r.range(9,3), 3); assert.equal(r.calls, before + 1);
});
test('full GPU damping, gravity, XY orbit and size use analytic frame age', () => {
    const c = config(); c.gravity = -0.5; c.drift = [1,2,3]; c.velocityDamping = range(0.5);
    c.sizeRate = range(2); c.sizeRateDamping = range(0.5); c.angleXY = range(Math.PI/2);
    c.angularRateZ = range(0.1); c.angularDamping = range(0.5);
    const b = { id: 0, position: [5,7,11] as [number,number,number], velocity: [2,0,4] as [number,number,number], life:30,creationFrame:0,seed:0 };
    const p = sampleParticle(b,c,2), integral = 0.75/Math.log(2);
    assert.ok(Math.abs(p.position[0]-9)<1e-10);
    assert.ok(Math.abs(p.position[1]-(7-(2*integral+2)))<1e-10);
    assert.ok(Math.abs(p.position[2]-(11+4*integral+6-1))<1e-10);
    assert.ok(Math.abs(p.size-(2+2*integral))<1e-10); assert.ok(Math.abs(p.angleZ-0.1*integral)<1e-10);
    assert.notEqual(integrateDamping(1,30),30); assert.equal(sampleParticle(b,c,30).alive,true); assert.equal(sampleParticle(b,c,30.001).alive,false);
});
test('GPU RGBA uses Frame slots and Color time; Alpha time is retained but unused', () => {
    const root = fixture(); root.children.push(node('Colors',{},[
        node('Color',{Frame:'1',RelativeAge:'0'},[node('Color',{r:'0',g:'1',b:'2'})]),
        node('Color',{Frame:'2',RelativeAge:'100'},[node('Color',{r:'2',g:'3',b:'4'})]),
        node('Color',{Frame:'8',RelativeAge:'30'},[node('Color',{r:'99',g:'99',b:'99'})]),rv('ColorScale',2)]),
        node('Alphas',{},[node('Alpha',{Frame:'1',RelativeAge:'90',Low:'0.2',High:'0.2'}),node('Alpha',{Frame:'2',RelativeAge:'10',Low:'0.8',High:'0.8'})]));
    const result = compileParticle(root); assert.ok(result.config); assert.ok(result.issues.some(i=>i.code==='alpha-time')); assert.ok(result.issues.some(i=>i.code==='slot'));
    const r = new ParticleRuntime(result.config!);r.seek(2);
    assert.deepEqual(sampleParticle(r.particles[0],result.config!,15,[0.2,0.8,0,0]).color,[2,4,6,1]);
    assert.equal(result.config!.colors![7].value[0],99);
});
test('Atlas retains full TotalFrames and distinguishes four modes', () => {
    const a = {framesPerRow:4,totalFrames:10,singleRow:0,randTex:0,speedMultiplier:1};
    assert.deepEqual(atlasFrame(a,0,9.5),{current:[0.25,0.5],next:[0,0],blend:0.5,scale:0.25});
    a.singleRow=2; assert.deepEqual(atlasFrame(a,0,3.5),{current:[0.75,0.25],next:[0.75,0.25],blend:0,scale:0.25});
    a.randTex=1; const fixed=atlasFrame(a,0,0);assert.deepEqual(atlasFrame(a,0,20),fixed);assert.deepEqual(fixed.current,fixed.next);assert.equal(fixed.blend,0);
    a.randTex=0;a.singleRow=-1; assert.deepEqual(atlasFrame(a,0,1.5).current,[0.25,Math.trunc(4*tableValue(0,2))/4]);
});
class ScriptedRandom extends FxRandom {
    trace:string[]=[];constructor(private values:number[]){super();}
    override range(low:number,high:number):number{this.trace.push(`Q(${low},${high})`);return this.values.shift()!;}
    override sample(r:Range):number{this.trace.push('R');return this.values.shift()??r.low;}
    override integer(low:number,high:number):number{this.trace.push(`I(${low},${high})`);return this.values.shift()!;}
    override direction():[number,number,number]{this.trace.push('D');return [1,0,0];}
}
test('native nonuniform volume order and hollow Box seventh face are preserved', () => {
    const c=config();c.volume.type='Sphere';c.volume.radius=10;
    const sphere=new ScriptedRandom([2]);assert.deepEqual(sampleVolume(c.volume,sphere),[2,0,0]);assert.deepEqual(sphere.trace,['Q(0,10)','D']);
    c.volume.type='Cylinder';c.volume.length=8;c.volume.offset=[1,2,3];
    const cylinder=new ScriptedRandom([0,2,-4]);assert.deepEqual(sampleVolume(c.volume,cylinder),[3,2,-1]);assert.equal(cylinder.trace.length,3);
    c.volume.type='Box';c.volume.hollow=true;c.volume.halfSize=[1,2,3];const box=new ScriptedRandom([6,0.5,1]);
    assert.deepEqual(sampleVolume(c.volume,box),[0.5,1,3]);assert.equal(box.trace[0],'I(0,6)');
});
test('Outward uses specific Volume callback: Point resamples and axial Line is zero', () => {
    const c=config();c.velocity.type='Outward';c.volume.type='Line';c.volume.start=[0,0,0];c.volume.end=[0,0,2];
    assert.deepEqual(sampleVelocity(c,[0,0,1],new ScriptedRandom([2,3])).map(v=>v===0?0:v),[0,0,0]);
    c.volume.end=[2,0,0];assert.deepEqual(sampleVelocity(c,[1,0,0],new ScriptedRandom([2,3])).map(v=>v===0?0:v),[0,2,3]);
    c.volume.type='Point';const rng=new ScriptedRandom([2,3]);assert.deepEqual(sampleVelocity(c,[0,0,0],rng),[2,0,0]);assert.deepEqual(rng.trace,['R','R','D']);
});
test('seek restores alpha/random/emitter state; redraw does not consume random; snapshots bounded', async () => {
    const c=config();c.initialDelay=range(0);c.alphas=Array.from({length:8},()=>({time:0,range:range(0,1)}));
    const r=new ParticleRuntime(c,123,{snapshotBytes:64*1024});r.seek(120);
    const births=JSON.stringify(r.particles),state=Array.from(r.random.words),alphas=r.alphaSamples.slice();
    for(let i=0;i<100;i++)sampleParticle(r.particles[0],c,2,r.alphaSamples);assert.deepEqual(Array.from(r.random.words),state);
    r.seek(5);await r.seekAsync(120);assert.equal(JSON.stringify(r.particles),births);assert.deepEqual(Array.from(r.random.words),state);assert.deepEqual(r.alphaSamples,alphas);
    assert.ok(r.snapshotBytes<=64*1024);const abort=new AbortController();abort.abort();await assert.rejects(r.seekAsync(5000,abort.signal),/取消/);
    r.dispose();assert.equal(r.particles.length,0);assert.equal(r.snapshotCount,0);
});
test('world shares ordered stream and checks global budget before allocation', () => {
    const c=config();c.initialDelay=range(0);c.burstDelay=range(0);
    const world=new ParticleWorld([{id:'a',config:c},{id:'b',config:c}],7,10);world.seek(2);
    assert.equal(world.systems[0].runtime.random,world.systems[1].runtime.random);
    const births=JSON.stringify(world.systems.map(s=>s.runtime.particles));world.seek(0);world.seek(2);assert.equal(JSON.stringify(world.systems.map(s=>s.runtime.particles)),births);
    world.seek(10);assert.match(world.budgetExceeded!,/预算/);assert.ok(world.systems.reduce((n,s)=>n+s.runtime.particles.length,0)<=10);world.dispose();assert.equal(world.systems.length,0);
});
test('unsupported fields, invalid lifetime and unevaluated expressions never silently play', () => {
    let root=fixture();root.attributes.SlaveSystem='Another';assert.equal(compileParticle(root).config,undefined);
    root=fixture();root.children.find(n=>n.name==='Lifetime')!.attributes.Low='0';assert.ok(compileParticle(root).issues.some(i=>i.code==='value'&&i.path==='Lifetime'));
    root=fixture();root.children.find(n=>n.name==='Size')!.attributes.Low='=unknown';assert.ok(compileParticle(root).issues.some(i=>i.message.includes('尚未求值')));
});

// Native binder 0x6C4B2D reads template Size; CPU 0x6C1336 alone adds system +0x128.
test('ordinary GPU StartSizeRate preserves shared random consumption without changing shader size', () => {
    const root=fixture(); root.children.push(rv('StartSizeRate',20,21));
    const result=compileParticle(root);assert.ok(result.config);assert.ok(result.issues.some(i=>i.code==='gpu-unused'));
    const c=result.config!;c.initialDelay=range(0);c.burstDelay=range(0);
    const r=new ParticleRuntime(c);r.seek(3);assert.equal(r.random.calls,8);assert.equal(r.startSizeAccumulator,50);
    assert.equal(sampleParticle(r.particles[3],c,1).size,2);
    r.seek(0);r.seek(3);assert.equal(r.startSizeAccumulator,50);
});
test('Multiply with Colors and absent Alpha uses binder zero-alpha default',()=>{
    const c=config();c.shader='MULTIPLY';c.colors=Array.from({length:8},()=>({time:0,value:[0.2,0.3,0.4]}));c.initialDelay=range(0);
    const r=new ParticleRuntime(c);r.seek(0);assert.deepEqual(r.alphaSamples,[0,0,0,0]);
    assert.deepEqual(sampleParticle(r.particles[0],c,0,r.alphaSamples).color,[0.8,0.7,0.6,0]);
});

test('FXList millisecond InitialDelay override preserves template-constructor RNG draw',()=>{
    const c=config();c.initialDelay=range(100,101);
    const r=new ParticleRuntime(c,1,{initialDelayOverrideMs:range(100)});
    assert.equal(r.random.calls,1);r.seek(2);assert.equal(r.particles.length,0);r.seek(3);assert.equal(r.particles.length,1);
    assert.equal(r.random.calls,2);
});

test('position transform profile uses right-handed X/Y/Z bases and Rx*Ry*Rz post-multiplication',()=>{
    const close=(actual:number[],expected:number[])=>actual.forEach((v,i)=>assert.ok(Math.abs(v-expected[i])<1e-12));
    const q=Math.PI/2;
    close(transformVector([0,1,0],localRotationMatrix([q,0,0])),[0,0,1]);
    close(transformVector([1,0,0],localRotationMatrix([0,q,0])),[0,0,-1]);
    close(transformVector([1,0,0],localRotationMatrix([0,0,q])),[0,1,0]);
    close(transformVector([1,0,0],localRotationMatrix([q,0,q])),[0,0,1]);
    const c=config();c.initialDelay=range(0);
    const r=new ParticleRuntime(c,1,{rotationXYZ:[q,0,q],offset:[3,4,5]});r.seek(0);
    close(r.particles[0].position,[3,4,5]);close(r.particles[0].velocity,[0,-2,1]);
});
