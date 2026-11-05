/** Versioned scope, shared by diagnostics and acceptance fixtures. */
export const fxCapabilities = {
    version: 1,
    fixtures: ['flash.xml', 'smoke.xml', 'sparks.xml', 'aura.xml', 'Mod.xml', 'missing.xml', 'malformed.xml'],
    supported: {
        backend: ['GPU_PARTICLE/Physics.Default/Draw.Gpu/Update.Default/GPUParticle.fx/SIMPLE_QUAD'],
        volume: ['Point','Line','Box','Cylinder','Sphere'], velocity: ['Ortho','Cylindrical','Spherical','Outward'],
        material: ['ADDITIVE','ALPHA','MULTIPLY'], atlas: ['loop','single-row','random-cell','random-row'],
        fxList: ['Particle','Count','SystemLife','Offset','Rotate.XYZ (EA shared-source matrix profile Rx*Ry*Rz)','InitialDelay milliseconds','OnlyIfOnLand','OnlyIfOnWater','StopIfPlayed'],
        assets: ['entry-scoped all/instance/reference Include','local-over-reference definitions','plain XML xi:include','unique-child/id-keyed inheritance; repeated unkeyed sequences require explicit parent Replace','Merge/Replace/Remove/Append','numeric Defines'],
        textures: ['TGA raw/RLE gray/RGB/RGBA all origins','DDS DXT1/DXT3/DXT5/masked RGBA/mips'],
    },
    explicitGaps: ['Rotate.X/Y uses EA shared-source profile, RA3 game-image comparison pending','xi:include xpointer','general Include mode/override semantics pending compiler comparison','unkeyed inherited Color/Alpha/Nugget sequences','object/bone/filter dispatch','Radius/Height FXList placement','custom shader/geometry',
        'CPU Default (round 2)','EventFX/attached systems (round 2)','compiled texture processing (round 2)','soft scene depth (round 2)','game screenshot comparison unavailable'],
} as const;
