using System.Globalization;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx
{
    public class ParticleSimulatorTests
    {
        static VfxCurve C(params float[] values) => VfxCurve.Constant(values);

        // From one value at t = 0 to another at t = 1, linear.
        static VfxCurve Ramp(float from, float to) => VfxCurve.Parse(string.Create(CultureInfo.InvariantCulture,
            $"x_min,0,0;0.0,{from},linear,linear,0,0,0,0,1;1.0,{to},linear,linear,0,0,0,0,1|x_max,0,0;0.0,{from},linear,linear,0,0,0,0,1;1.0,{to},linear,linear,0,0,0,0,1:x,0;0,1"));

        // A random point between two constants, drawn per particle.
        static VfxCurve Between(float min, float max) => VfxCurve.Parse(string.Create(CultureInfo.InvariantCulture,
            $"x_min,0,0;0.0,{min},linear,linear,0,0,0,0,1|x_max,0,0;0.0,{max},linear,linear,0,0,0,0,1:x,1;0,1"));

        // Ten particles a second that live 100 s and do not move, from an emitter that never stops.
        static EmitterSettings Steady(Action<EmitterSettings> change = null)
        {
            var s = new EmitterSettings { Name = "test", InfiniteLife = true, Rate = C(10), ParticleLife = C(100), Spread = (0, 0) };
            change?.Invoke(s);
            return s;
        }

        static ParticleSimulator Run(EmitterSettings s, float time, Func<float, Matrix> effectWorldAt = null, float spawnEnd = float.PositiveInfinity, int seed = 1, Action<string> log = null)
        {
            var simulator = new ParticleSimulator([s], seed, effectWorldAt ?? (_ => Matrix.Identity), spawnEnd, log);
            simulator.AdvanceTo(time);
            return simulator;
        }

        static int Count(ParticleSimulator simulator) => simulator.Emitters.Sum(x => x.Particles.Count);
        static List<ParticleState> States(ParticleSimulator simulator) => simulator.Emitters.SelectMany(ParticleSimulator.States).ToList();

        // A modder's rate past 2^25 particles a step made "accumulator -= 1" a no-op, so the spawn loop never ended.
        [Test]
        public void HugeEmissionRate_StopsAtTheCap_AndReturns()
        {
            var run = Task.Run(() => Run(Steady(x => x.Rate = C(1e12f)), 0.5f));
            Assert.That(run.Wait(TimeSpan.FromSeconds(20)), Is.True, "AdvanceTo returned");
            Assert.That(Count(run.Result), Is.EqualTo(ParticleSimulator.ParticleCap));
        }

        // A NaN lifetime bias (a malformed override attribute) passed "life <= 0", and the particle never died.
        [Test]
        public void NaNLifetimeBias_SpawnsNothing() => Assert.That(Count(Run(Steady(x => x.LifeBias = (float.NaN, float.NaN)), 1)), Is.Zero);

        // The bone turns a quarter turn about Y and walks 5 m along X in the first second.
        static Matrix MovingBone(float t) => Matrix.CreateRotationY(t * MathHelper.PiOver2) * Matrix.CreateTranslation(5 * t, 0, 0);

        [Test]
        public void SameSeed_SameParticles_OtherSeed_Others()
        {
            EmitterSettings Spray() => Steady(s => { s.SpawnAngle = new Vector2(0, 360); s.Spread = (10, 80); s.InitialSpeed = Between(1, 5); });
            var a = States(Run(Spray(), 2));
            Assert.That(a, Is.Not.Empty);
            Assert.That(States(Run(Spray(), 2)), Is.EqualTo(a));
            Assert.That(States(Run(Spray(), 2, seed: 2)), Is.Not.EqualTo(a));
        }

        [Test]
        public void SpawnedCount_IsTheIntegralOfTheRateCurve()
        {
            // 10 a second rising to 30 over the emitter's 1 s life: 20 in all.
            var s = Steady(x => { x.InfiniteLife = false; x.LifeTime = (1, 1); x.Rate = Ramp(10, 30); });
            Assert.That(Count(Run(s, 2)), Is.EqualTo(20).Within(1));
        }

        [Test]
        public void NegativeStartTime_PreWarms()
        {
            var s = Steady(x => x.StartTime = (-5, -5));
            Assert.That(Count(Run(s, -0.5f)), Is.Zero, "the effect has not started");
            Assert.That(Count(Run(s, 0)), Is.EqualTo(50).Within(1), "5 s of 10 a second, already there at the start");
        }

        [Test]
        public void PreWarm_IsCappedAt30Seconds()
        {
            var s = Steady(x => { x.StartTime = (-1000, -1000); x.ParticleLife = C(10000); });
            Assert.That(Count(Run(s, 0)), Is.EqualTo(300).Within(1));
        }

        [Test]
        public void ParticlesDie_AtTheirLifetime()
        {
            var simulator = Run(Steady(x => x.ParticleLife = C(0.5f)), 3);
            Assert.That(Count(simulator), Is.EqualTo(5).Within(1), "10 a second, living half a second");
            Assert.That(simulator.Emitters[0].Particles.All(x => x.Age < x.Life));
        }

        [Test]
        public void ZeroParticleLife_SpawnsNothing() => Assert.That(Count(Run(Steady(x => x.ParticleLife = C(0)), 1)), Is.Zero);

        // SkipParticlePositionUpdate (651 vanilla emitters, the nuln rifle's head among them): a particle stays where it
        // spawned and keeps its velocity, which still aligns and stretches it.
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void SkipPositionUpdate_KeepsParticlesWhereTheySpawned(bool skip, bool moved)
        {
            var states = States(Run(Steady(x => { x.InitialSpeed = C(5); x.SkipPositionUpdate = skip; }), 1));
            Assert.That(states, Has.Count.GreaterThan(5));
            Assert.That(states.Any(x => x.Position.Length() > 1), Is.EqualTo(moved));
            Assert.That(states.All(x => x.Velocity.Length() > 4.9f), Is.True);
        }

        [Test]
        public void ZeroEmitterLifeTime_GivesFiniteStates()
        {
            var states = States(Run(Steady(x => { x.LifeTime = (0, 0); x.LoopRate = true; x.InitialSpeed = C(1); }), 1));
            Assert.That(states, Is.Not.Empty);
            Assert.That(states.All(x => float.IsFinite(x.Position.X + x.Position.Y + x.Position.Z + x.Alpha + x.Size.X)));
        }

        // Forces are cm/s2: vanilla's falling debris carries -981 (156 emitters), rocks -1471.5, sand -490.5 and
        // -1962, against 101 uses of 9.81. Read as m/s2, Belegar's rising embers (+98.1..196.2) shot up at 20-40 m/s.
        [Test]
        public void ConstantForce_IsInCentimetresPerSecondSquared_AndGivesTheAnalyticPath()
        {
            // One particle, 2 m/s along X, under one g downward: -981 cm/s2, 9.81 m/s2.
            var s = Steady(x => { x.MaxParticles = 1; x.InitialSpeed = C(2); x.SpawnAngle = Vector2.Zero; x.Forces = [C(0, -981, 0)]; });
            var p = Run(s, 1).Emitters[0].Particles.Single();
            Assert.That(Vector3.Distance(p.Position, new Vector3(2 * p.Age, -4.905f * p.Age * p.Age, 0)), Is.LessThan(1e-3f));
        }

        [TestCase(SimulationSpace.World, 0f, 0f, 1f, Description = "stays where it spawned")]
        [TestCase(SimulationSpace.LocalPositionRotation, 6f, 0f, 0f, Description = "moves and turns with the bone")]
        [TestCase(SimulationSpace.LocalPositionOnly, 5f, 0f, 1f, Description = "moves with the bone, does not turn")]
        public void SimulationSpace_DecidesWhatTheBoneCarries(SimulationSpace space, float x, float y, float z)
        {
            // One still particle spawned 1 m along the emitter's Z: a ring of radius 1, at 90 degrees.
            var s = Steady(e => { e.Space = space; e.MaxParticles = 1; e.Rate = C(600); e.SpawnRing = C(1, 1); e.SpawnArc = new Vector2(90, 90); });
            var state = States(Run(s, 1, MovingBone)).Single();
            Assert.That(Vector3.Distance(state.Position, new Vector3(x, y, z)), Is.LessThan(0.15f));
        }

        [Test]
        public void ScrubBack_ThenForward_EqualsAStraightRun()
        {
            EmitterSettings Spray() => Steady(s => { s.SpawnAngle = new Vector2(0, 360); s.InitialSpeed = Between(1, 5); s.ParticleLife = Between(0.5f, 2); });
            var straight = Run(Spray(), 2, MovingBone);
            var scrubbed = Run(Spray(), 3, MovingBone);
            scrubbed.AdvanceTo(2);
            Assert.That(States(scrubbed), Is.EqualTo(States(straight)));
        }

        [Test]
        public void TagEnd_StopsSpawning_AndTheLiveOnesLiveOn() => Assert.That(Count(Run(Steady(), 2, spawnEnd: 0.5f)), Is.EqualTo(5).Within(1));

        [Test]
        public void ParticleCap_Holds_AndIsLoggedOnce()
        {
            var log = new List<string>();
            var simulator = Run(Steady(x => x.Rate = C(100000)), 0.5f, log: log.Add);
            Assert.That(Count(simulator), Is.EqualTo(ParticleSimulator.ParticleCap));
            Assert.That(log, Has.Count.EqualTo(1));
        }

        [Test]
        public void OverLife_SizeAndAlphaFollowTheirCurves()
        {
            // Spawned at size 2 and alpha 1, living 2 s; ParticleScale goes 1 to 3 and curve_alpha 1 to 0 over that life.
            var s = Steady(x => { x.MaxParticles = 1; x.ParticleLife = C(2); x.InitialScale = C(2, 2, 2); x.Scale = Ramp(1, 3); x.Alpha = Ramp(1, 0); });
            var simulator = Run(s, 1);
            var p = simulator.Emitters[0].Particles.Single();
            var state = States(simulator).Single();
            var life = p.Age / p.Life;
            Assert.That(state.Size.X, Is.EqualTo(2 * (1 + 2 * life)).Within(1e-3));
            Assert.That(state.Alpha, Is.EqualTo(1 - life).Within(1e-3));
        }

        [TestCase(0.5f, 0f, 1f, false, true, 32)]
        [TestCase(0.5f, 0f, 0.5f, false, true, 16)]
        [TestCase(0f, 0.25f, 2f, true, true, 32, Description = "fixed speed: loops per second")]
        [TestCase(0.75f, 0f, 2f, false, false, 63, Description = "no loop: holds the last cell")]
        [TestCase(0.75f, 0f, 2f, false, true, 32, Description = "loop: wraps")]
        public void FlipbookCell_OfSixtyFour(float lifeFraction, float age, float loops, bool fixedSpeed, bool loop, int expected) =>
            Assert.That(VfxConventions.FlipbookCell(64, lifeFraction, age, loops, fixedSpeed, loop), Is.EqualTo(expected));
    }
}
