using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx
{
    /// <summary>
    /// Every reading of the vfx format that is a guess rather than a measurement, so calibrating against
    /// Terry (CA's battle map editor) changes this file and nothing else. docs/VFX.md lists each one.
    /// Two more readings live in the structure of ParticleSimulator rather than in a value here: forces
    /// are accelerations in the particle's own space (the emitter's axes for LOCAL_SPACE_POSITION_ROTATION,
    /// world axes otherwise), and a quad's size is ParticleInitialScale x ParticleScale as its full
    /// width and height.
    /// </summary>
    public static class VfxConventions
    {
        /// <summary>ForceA/B/C are cm/s2 while speeds and sizes are metres: falling debris carries -981 (156 vanilla
        /// emitters), rocks -1471.5, sand -490.5 and -1962, against 101 uses of 9.81; debris launched at 5-8 m/s under
        /// -981 arcs and lands within its 1.5 s life. Measured 2026-10-11.</summary>
        public const float ForceToMetres = 0.01f;

        /// <summary>The longest pre-warm simulated, in seconds. Vanilla's StartTime goes down to -20.</summary>
        public const float MaxPreWarm = 30;

        /// <summary>A reference's transform: scale, then its rotation, then its position.</summary>
        public static Matrix ReferenceMatrix(float scale, Vector3 rotationDegrees, Vector3 position) =>
            Matrix.CreateScale(scale) * Rotation(rotationDegrees) * Matrix.CreateTranslation(position);

        /// <summary>Rotations (references, EmitterOrientation): about X, then Y, then Z, in degrees.</summary>
        public static Matrix Rotation(Vector3 degrees) =>
            Matrix.CreateRotationX(MathHelper.ToRadians(degrees.X)) * Matrix.CreateRotationY(MathHelper.ToRadians(degrees.Y)) * Matrix.CreateRotationZ(MathHelper.ToRadians(degrees.Z));

        /// <summary>Spawn direction: SpawnAngle is the azimuth about Y from +X, SpawnSpread the elevation above the XZ plane (90 is straight up).</summary>
        public static Vector3 Direction(float azimuthDegrees, float elevationDegrees)
        {
            var azimuth = MathHelper.ToRadians(azimuthDegrees);
            var elevation = MathHelper.ToRadians(elevationDegrees);
            return new Vector3(MathF.Cos(elevation) * MathF.Cos(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Sin(azimuth));
        }

        /// <summary>An absent SpawnAngle is every azimuth. 8,908 of vanilla's 11,722 continuous_mesh emitters omit it, and
        /// where present it is an azimuth range (0..360 in 802, -180..180 in 366); measured 2026-10-11.</summary>
        public static readonly Vector2 EveryAzimuth = new(0, 360);

        /// <summary>SpawnArc is the spawn ring's start and end angle in degrees, in the XZ plane; 0,0 is the whole ring.</summary>
        public static (float From, float To) RingArc(Vector2 spawnArc) => spawnArc == Vector2.Zero ? (0, 360) : (spawnArc.X, spawnArc.Y);

        /// <summary>SpawnInheritedSpeed is a plain share of the emitter's velocity (332 of its 1,385 vanilla emitters carry 0.5,
        /// nearly all lie in 0..1), given to world-space particles only: a local-space particle already rides with its emitter.
        /// SpawnInheritedDirectionBlend (146 emitters) is not read.</summary>
        public static Vector3 Inherited(Vector3 emitterVelocity, float share) => emitterVelocity * share;

        /// <summary>The preview's terrain is flat ground at y = 0 in the effect's space. ParticlesSpawnOnTerrain (1,503 vanilla
        /// emitters) drops the spawn point onto it. ParticlesFollowTerrain (1,073) is read as holding a particle's height above
        /// the ground as it moves, which on flat ground changes nothing; pinning it to the ground is the other reading.</summary>
        public static Vector3 OnGround(Vector3 position) => new(position.X, 0, position.Z);

        /// <summary>A particle falling into the ground leaves it with its fall scaled by CollisionBouncingFactor (0..1.5 in
        /// vanilla) and its slide cut by CollisionDampingFactor (0..2.2), read as the share lost and clamped to 0..1.</summary>
        public static Vector3 Bounce(Vector3 velocity, float bounce, float damping)
        {
            var keep = 1 - Math.Clamp(damping, 0, 1);
            return new Vector3(velocity.X * keep, -velocity.Y * bounce, velocity.Z * keep);
        }

        /// <summary>vs_flip_uv: flip_u and flip_v are each a particle's chance of a mirrored texture (0.5 in 4,217 and 3,413 of
        /// 4,736 vanilla chains; above 1 is always), and uv_orientation a quarter turn of it (28 chains), the turn first. Returns
        /// the texture coordinate, 0..1 with v down, at a quad corner given x right and y up, 0..1.</summary>
        public static (float U, float V) QuadUv(float x, float y, bool flipU, bool flipV, int turn)
        {
            var (u, v) = turn switch { 1 => (y, x), -1 => (1 - y, 1 - x), _ => (x, 1 - y) };
            return (flipU ? 1 - u : u, flipV ? 1 - v : v);
        }

        /// <summary>CullDistances and ParticleCullDistances, metres from the camera: alpha fades in from x to y and out from z to
        /// w, linearly; a band of no width is a hard edge. ParticleCullDistances sets a near band on 8,882 of 12,408 vanilla
        /// emitters, so a close camera sees particles thin out around it.</summary>
        public static float CullFade(float distance, Vector4 cull)
        {
            var near = cull.Y > cull.X ? Math.Clamp((distance - cull.X) / (cull.Y - cull.X), 0, 1) : distance >= cull.X ? 1 : 0;
            var far = cull.W > cull.Z ? Math.Clamp((cull.W - distance) / (cull.W - cull.Z), 0, 1) : distance <= cull.W ? 1 : 0;
            return near * far;
        }

        /// <summary>vs_rendersize_limiter (985 vanilla chains, nearly all small embers, sparks and droplets): pixelsize_limit is
        /// read as the fewest pixels a particle is drawn across, so a far spark stays visible, rather than a size below which it
        /// is culled. Returns what its size is multiplied by; pixelScale is the pixels a metre covers a metre from the camera.</summary>
        public static float MinPixelGrowth(float size, float distance, float pixelScale, float minPixels)
        {
            if (minPixels <= 0 || pixelScale <= 0 || size <= 0)
                return 1;
            return Math.Max(1, minPixels / (size / Math.Max(distance, 1e-3f) * pixelScale));
        }

        /// <summary>SpawnAreaBox is the box's full size, centred on the emitter.</summary>
        public static Vector3 BoxOffset(Vector3 size, Vector3 random01) => (random01 - new Vector3(0.5f)) * size;

        /// <summary>A curve over the emitter's life: age over LifeTime, wrapped when its LoopAnim flag is set, held at the end otherwise.</summary>
        public static float EmitterTime(float age, float lifeTime, bool loop)
        {
            if (lifeTime <= 0)
                return 0;
            var u = age / lifeTime;
            return loop ? u - MathF.Floor(u) : Math.Min(u, 1);
        }

        /// <summary>Bursts: every BurstInterval seconds, the emitter spawns for the first BurstIntervalLength of them.</summary>
        public static bool InBurst(float age, float interval, float length) => interval <= 0 || age % interval < length;

        /// <summary>ParticleDeceleration and ParticleSpinDeceleration as drag: the speed loses that fraction per second, exponentially.</summary>
        public static float Drag(float deceleration, float dt) => MathF.Exp(-Math.Max(0, deceleration) * dt);

        /// <summary>A reference's lifetime biases add seconds.</summary>
        public static float Biased(float value, float bias) => value + bias;

        /// <summary>ParticleSpinDirectionZ: CW turns the quad clockwise as the camera sees it.</summary>
        public static float SpinSign(string? direction) => direction == "CW" ? -1 : 1;

        /// <summary>additive_blend_mul: 0 blends by alpha, 1 adds, between mixes the two; above 1 it also brightens.</summary>
        public static (float AlphaWeight, float Brightness) Blend(float additiveMul) => (1 - Math.Clamp(additiveMul, 0, 1), Math.Max(1, additiveMul));

        /// <summary>velocity_stretch_mul lengthens a velocity-aligned quad by speed x multiplier, up to velocity_stretch_cap times.</summary>
        public static float Stretch(float speed, float multiplier, float cap) => Math.Clamp(1 + speed * multiplier, 1, cap > 0 ? cap : float.MaxValue);

        /// <summary>vs_animated_texture: no_loops is how many times the strip plays over a particle's life
        /// (using_particle_lifetime) or per second (fixed_animation_speed); without loop it holds the last cell.</summary>
        public static int FlipbookCell(int cells, float lifeFraction, float ageSeconds, float loops, bool fixedSpeed, bool loop)
        {
            var f = (fixedSpeed ? ageSeconds : lifeFraction) * loops;
            f = loop ? f - MathF.Floor(f) : Math.Clamp(f, 0, 0.9999f);
            return Math.Min((int)(f * cells), cells - 1);
        }
    }
}
