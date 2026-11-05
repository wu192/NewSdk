/** Standard high GPU path, Z-up. Formula contract: research 41/42 at 6f50d090. */
export const vertexShader = `#version 300 es
precision highp float;
layout(location=0) in vec3 corner;
layout(location=4) in vec2 cornerTexCoord;
layout(location=1) in vec4 birthPositionLife;
layout(location=2) in vec4 birthVelocityFrame;
layout(location=3) in float birthSeed;
uniform mat4 viewProjection;
uniform vec3 cameraRight;
uniform vec3 cameraUp;
uniform float currentFrame;
uniform vec3 gravity;
uniform vec3 drift;
uniform vec2 velocityDamping;
uniform vec3 sizeMin;
uniform vec3 sizeSpread;
uniform vec3 xyMin;
uniform vec3 xySpread;
uniform vec3 zMin;
uniform vec3 zSpread;
uniform vec2 colorScale;
uniform vec4 timeKeys;
uniform vec4 colorSlope[3];
uniform vec4 colorIntercept[3];
uniform vec4 atlas;
uniform float atlasSpeed;
uniform bool groundAligned;
uniform bool perpendicularBottom;
uniform vec3 geometrySizeMin;
uniform vec3 geometrySizeSpread;
uniform vec3 geometryRateMin;
uniform vec3 geometryRateSpread;
uniform vec3 geometryDampingMin;
uniform vec3 geometryDampingSpread;
uniform vec3 geometryAngleMin;
uniform vec3 geometryAngleSpread;
uniform vec3 geometryAngleRateMin;
uniform vec3 geometryAngleRateSpread;
uniform vec2 geometryAngleDamping;
uniform int geometryRotation;
uniform vec4 randomTable[16];
out vec2 texCoord;
out vec2 nextTexCoord;
out float texBlend;
out vec4 particleColor;
out float softScale;
float integral(float d,float t) {
    if (d==1.0) d=1.0001;
    return (pow(d,t)-1.0)/log(d);
}
vec4 rand4(float channel) {
    int row=int(16.0*fract(channel*11.0/16.0+birthSeed*3.0/16.0));
    return randomTable[row];
}
vec2 atlasUv(float frame,vec2 uv) {
    float f=mod(frame,atlas.y);
    vec2 cell=floor(vec2(mod(f,atlas.x),f/atlas.x));
    return (cell+uv)/atlas.x;
}
// HLSL uses row vectors. Columns here are its EulerXYZ rows.
mat3 eulerRotation(vec3 a) {
    vec3 c=cos(a),s=sin(a);
    return mat3(vec3(c.y*c.z,c.y*s.z,-s.y),
        vec3(s.y*s.x*c.z-c.x*s.z,s.y*s.x*s.z+c.x*c.z,c.y*s.x),
        vec3(s.y*c.x*c.z+s.x*s.z,s.y*c.x*s.z-s.x*c.z,c.y*c.x));
}
mat3 velocityRotation(vec3 d) {
    float full=length(d),xy=length(d.xy);
    float pitch=full==0.0?0.0:-asin(clamp(d.z/full,-1.0,1.0));
    float yaw=xy==0.0?0.0:atan(d.y,d.x);
    return eulerRotation(vec3(0.0,pitch,yaw));
}
void main() {
    float age=currentFrame-birthVelocityFrame.w;
    vec3 sr=sizeMin+rand4(3.0).xyz*sizeSpread;
    float size=sr.x+sr.y*integral(sr.z,age);
    float d=velocityDamping.x+rand4(13.0).x*velocityDamping.y;
    vec3 pos=birthPositionLife.xyz+birthVelocityFrame.xyz*integral(d,age)
        +(drift+0.5*age*gravity)*age;
    vec3 xy=xyMin+rand4(9.0).xyz*xySpread;
    float angleXY=xy.x+xy.y*integral(xy.z,age);
    vec2 displacement=pos.xy-birthPositionLife.xy;
    if (!perpendicularBottom) pos.xy=birthPositionLife.xy+vec2(displacement.x*cos(angleXY)+displacement.y*sin(angleXY),
        -displacement.x*sin(angleXY)+displacement.y*cos(angleXY));
    vec3 z=zMin+rand4(2.0).xyz*zSpread;
    float angle=z.x+z.y*integral(z.z,age);
    vec2 c=vec2(corner.x*cos(angle)+corner.y*sin(angle),-corner.x*sin(angle)+corner.y*cos(angle))*size;
    vec3 world=pos+(groundAligned ? vec3(c,0.0):cameraRight*c.x+cameraUp*c.y);
    softScale=abs(size)>1e-8?5.0/size:0.0;
    if (perpendicularBottom) {
        vec3 randoms=rand4(3.0).xyz;
        vec3 start=geometrySizeMin+randoms*geometrySizeSpread;
        vec3 rate=geometryRateMin+randoms*geometryRateSpread;
        vec3 damp=geometryDampingMin+randoms*geometryDampingSpread;
        vec3 sizes=start+rate*vec3(integral(damp.x,age),integral(damp.y,age),integral(damp.z,age));
        vec3 local=corner*sizes;
        if (geometryRotation!=0) {
            float ad=geometryAngleDamping.x+randoms.x*geometryAngleDamping.y;
            vec3 angles=geometryAngleMin+randoms*geometryAngleSpread
                +(geometryAngleRateMin+randoms*geometryAngleRateSpread)*integral(ad,age);
            local=eulerRotation(angles)*local;
            if (geometryRotation==1) {
                vec3 direction=pos-birthPositionLife.xyz;
                if (length(direction)<0.001) direction=birthVelocityFrame.xyz;
                local=velocityRotation(direction)*local;
            }
        }
        world=pos+local;
        softScale=abs(sizes.x)>1e-8?1.5/sizes.x:0.0;
    }
    gl_Position=viewProjection*vec4(world,1.0);
    if (age<0.0 || age>birthPositionLife.w) gl_Position=vec4(2.0,2.0,2.0,1.0);
    float relativeAge=age/birthPositionLife.w;
    int segment=relativeAge<timeKeys.y?0:(relativeAge<timeKeys.z?1:2);
    particleColor=(colorSlope[segment]*relativeAge+colorIntercept[segment])
        *(colorScale.x+rand4(4.0).x*colorScale.y);
    vec2 uv=cornerTexCoord;
    float frame=age*atlasSpeed;
    float row=floor(atlas.x*rand4(2.0).x);
    float col=floor(atlas.x*rand4(7.0).x);
    float f=min(frame,atlas.x-1.0);
    float next=min(frame+1.0,atlas.x-1.0);
    if (atlas.z>0.0 && atlas.w==0.0) {
        texCoord=(vec2(floor(f),atlas.z-1.0)+uv)/atlas.x;
        nextTexCoord=(vec2(floor(next),atlas.z-1.0)+uv)/atlas.x;
        texBlend=fract(f);
    } else if (atlas.w!=0.0) {
        texCoord=(vec2(col,row)+uv)/atlas.x;
        nextTexCoord=texCoord;
        texBlend=0.0;
    } else if (atlas.z==-1.0) {
        texCoord=(vec2(floor(f),row)+uv)/atlas.x;
        nextTexCoord=(vec2(floor(next),row)+uv)/atlas.x;
        texBlend=fract(f);
    } else {
        texCoord=atlasUv(frame,uv);
        nextTexCoord=atlasUv(frame+1.0,uv);
        texBlend=fract(frame);
    }
}`;

/** GPU standard, GPU geometry and CPU high technique have distinct color orders. */
export const fragmentShader = `#version 300 es
precision highp float;
uniform sampler2D particleTexture;
uniform sampler2D sceneDepth;
uniform bool missingTexture;
uniform int colorOrder;
uniform int softTarget;
uniform vec2 viewport;
uniform vec2 clipPlanes;
uniform float alphaTestReference;
in vec2 texCoord;
in vec2 nextTexCoord;
in float texBlend;
in vec4 particleColor;
in float softScale;
out vec4 outputColor;
float viewDepth(float depth) {
    return clipPlanes.x*clipPlanes.y/(clipPlanes.y-depth*(clipPlanes.y-clipPlanes.x));
}
void main() {
    if (missingTexture) {
        vec2 local=fract(texCoord*8.0);
        if (min(min(local.x,1.0-local.x),min(local.y,1.0-local.y))>0.08) discard;
        outputColor=vec4(0.9,0.25,0.7,0.6);
    } else {
        vec4 texel=mix(texture(particleTexture,texCoord),texture(particleTexture,nextTexCoord),texBlend);
        if (colorOrder==1) texel.rgb=pow(max(texel.rgb,vec3(0.0)),vec3(2.2));
        outputColor=texel*particleColor;
        if (colorOrder==0) outputColor.rgb=pow(max(outputColor.rgb,vec3(0.0)),vec3(2.2));
        if (softTarget!=0) {
            float background=texture(sceneDepth,gl_FragCoord.xy/viewport).r;
            float fade=background>=1.0?1.0:clamp((viewDepth(background)-viewDepth(gl_FragCoord.z))*softScale,0.0,1.0);
            if (softTarget==1) outputColor.a*=fade;
            else outputColor.rgb*=fade;
        }
    }
    if (alphaTestReference>=0.0 && outputColor.a<alphaTestReference) discard;
}`;

/** CPU DefaultDraw uploads current state; GPU birth/physics/atlas math never runs here. */
export const cpuVertexShader = `#version 300 es
precision highp float;
layout(location=0) in vec3 corner;
layout(location=4) in vec2 cornerTexCoord;
layout(location=1) in vec4 positionSize;
layout(location=2) in vec4 currentColor;
layout(location=3) in float currentAngle;
uniform mat4 viewProjection;
uniform vec3 cameraRight;
uniform vec3 cameraUp;
uniform bool groundAligned;
out vec2 texCoord;
out vec2 nextTexCoord;
out float texBlend;
out vec4 particleColor;
out float softScale;
void main() {
    float c=cos(currentAngle),s=sin(currentAngle);
    vec2 relative=vec2(corner.x*c+corner.y*s,-corner.x*s+corner.y*c)*positionSize.w;
    vec3 world=positionSize.xyz+(groundAligned?vec3(relative,0.0):cameraRight*relative.x+cameraUp*relative.y);
    gl_Position=viewProjection*vec4(world,1.0);
    texCoord=cornerTexCoord;nextTexCoord=texCoord;texBlend=0.0;particleColor=currentColor;softScale=0.0;
}`;

/** Infinite flat synthetic ground, projected by exactly the same camera as particles. */
export const groundVertexShader = `#version 300 es
precision highp float;
void main() {
    vec2 p=vec2(float((gl_VertexID<<1)&2),float(gl_VertexID&2));
    gl_Position=vec4(p*2.0-1.0,0.0,1.0);
}`;
export const groundFragmentShader = `#version 300 es
precision highp float;
uniform mat4 viewProjection;
uniform vec3 eye;
uniform vec3 cameraRight;
uniform vec3 cameraUp;
uniform vec3 cameraBackward;
uniform vec2 viewport;
uniform vec2 projectionScale;
uniform float groundHeight;
out vec4 outputColor;
void main() {
    vec2 ndc=gl_FragCoord.xy/viewport*2.0-1.0;
    vec3 ray=-cameraBackward+cameraRight*ndc.x*projectionScale.x+cameraUp*ndc.y*projectionScale.y;
    if (abs(ray.z)<1e-8) discard;
    float t=(groundHeight-eye.z)/ray.z;
    if (t<=0.0) discard;
    vec4 clip=viewProjection*vec4(eye+ray*t,1.0);
    float depth=clip.z/clip.w*0.5+0.5;
    if (depth<0.0||depth>1.0) discard;
    gl_FragDepth=depth;
    outputColor=vec4(0.0);
}`;

export const lineVertexShader = `#version 300 es
precision highp float;
layout(location=0) in vec3 position;
layout(location=1) in vec4 color;
uniform mat4 viewProjection;
out vec4 lineColor;
void main(){gl_Position=viewProjection*vec4(position,1.0);lineColor=color;}`;
export const lineFragmentShader = `#version 300 es
precision highp float;
in vec4 lineColor;out vec4 outputColor;
void main(){outputColor=lineColor;}`;


/** Reconstructed special families upload bounded world-space ribbons and stretched sprites. */
export const specialVertexShader = `#version 300 es
precision highp float;
layout(location=0) in vec3 position;
layout(location=1) in vec2 vertexUv;
layout(location=2) in vec4 vertexColor;
layout(location=3) in vec2 vertexEnvironmentUv;
uniform mat4 viewProjection;
out vec2 texCoord;
out vec2 environmentUv;
out vec4 particleColor;
void main() {
    gl_Position=viewProjection*vec4(position,1.0);
    texCoord=vertexUv;environmentUv=vertexEnvironmentUv;particleColor=vertexColor;
}`;
export const specialFragmentShader = `#version 300 es
precision highp float;
uniform sampler2D particleTexture;
uniform sampler2D environmentTexture;
uniform bool missingTexture;
uniform bool hasEnvironment;
uniform bool repeatUv;
uniform int blendMode;
uniform float alphaTestReference;
in vec2 texCoord;
in vec2 environmentUv;
in vec4 particleColor;
out vec4 outputColor;
void main() {
    vec2 uv=repeatUv?fract(texCoord):texCoord;
    vec4 texel;
    if(missingTexture) {
        vec2 local=fract(uv*8.0);
        if(min(min(local.x,1.0-local.x),min(local.y,1.0-local.y))>0.08)discard;
        texel=vec4(0.9,0.25,0.7,0.6);
    } else texel=texture(particleTexture,uv);
    // Special materials use the mutable CPU path's texture-to-linear then tint order.
    texel.rgb=pow(max(texel.rgb,vec3(0.0)),vec3(2.2));
    if(hasEnvironment) {
        vec3 environment=pow(max(texture(environmentTexture,vec2(fract(environmentUv.x),clamp(environmentUv.y,0.0,1.0))).rgb,vec3(0.0)),vec3(2.2));
        texel.rgb=mix(texel.rgb,environment,0.35);
    }
    outputColor=texel*particleColor;
    if(alphaTestReference>=0.0&&outputColor.a<alphaTestReference)discard;
    if(blendMode==0)outputColor.rgb*=outputColor.a;
    else if(blendMode==2)outputColor.rgb=mix(vec3(1.0),outputColor.rgb,clamp(outputColor.a,0.0,1.0));
}`;
