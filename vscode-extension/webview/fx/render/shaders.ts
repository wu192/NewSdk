/** Standard high GPU path, Z-up. Formula contract: research 41/42 at 6f50d090. */
export const vertexShader = `#version 300 es
precision highp float;
layout(location=0) in vec2 corner;
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
uniform vec4 randomTable[16];
out vec2 texCoord;
out vec2 nextTexCoord;
out float texBlend;
out vec4 particleColor;
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
    pos.xy=birthPositionLife.xy+vec2(displacement.x*cos(angleXY)+displacement.y*sin(angleXY),
        -displacement.x*sin(angleXY)+displacement.y*cos(angleXY));
    vec3 z=zMin+rand4(2.0).xyz*zSpread;
    float angle=z.x+z.y*integral(z.z,age);
    vec2 c=vec2(corner.x*cos(angle)+corner.y*sin(angle),-corner.x*sin(angle)+corner.y*cos(angle))*size;
    vec3 world=pos+(groundAligned ? vec3(c,0.0):cameraRight*c.x+cameraUp*c.y);
    gl_Position=viewProjection*vec4(world,1.0);
    if (age<0.0 || age>birthPositionLife.w) gl_Position=vec4(2.0,2.0,2.0,1.0);
    float relativeAge=age/birthPositionLife.w;
    int segment=relativeAge<timeKeys.y?0:(relativeAge<timeKeys.z?1:2);
    particleColor=(colorSlope[segment]*relativeAge+colorIntercept[segment])
        *(colorScale.x+rand4(4.0).x*colorScale.y);
    vec2 uv=corner*vec2(1.0,-1.0)+0.5;
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

export const fragmentShader = `#version 300 es
precision highp float;
uniform sampler2D particleTexture;
uniform bool missingTexture;
in vec2 texCoord;
in vec2 nextTexCoord;
in float texBlend;
in vec4 particleColor;
out vec4 outputColor;
void main() {
    if (missingTexture) {
        vec2 local=fract(texCoord*8.0);
        if (min(min(local.x,1.0-local.x),min(local.y,1.0-local.y))>0.08) discard;
        outputColor=vec4(0.9,0.25,0.7,0.6);
    } else {
        vec4 combined=mix(texture(particleTexture,texCoord),texture(particleTexture,nextTexCoord),texBlend)*particleColor;
        // Explicit GPU high path order: interpolate texture, multiply RGBA, gamma-to-linear RGB.
        outputColor=vec4(pow(combined.rgb,vec3(2.2)),combined.a);
    }
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
