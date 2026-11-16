using System.Collections.ObjectModel;
using GameWorld.Core.Animation;
using GameWorld.Core.Animation.Posing;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;

namespace GameWorld.Core.Test.Animation.Posing
{
    public class JointLimitStoreTests
    {
        const int Lower = TestSkeletons.Lower;
        string _folder = null!;
        GameSkeleton _skeleton = null!;
        List<AnimationReference> _references = null!;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "jointlimits_" + Guid.NewGuid().ToString("N"));
            _skeleton = TestSkeletons.Arm();
            _skeleton.SkeletonName = "test_arm";
            _references = [];
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
        }

        /// <summary>A clip twisting the lower arm through the given angles, saved as .anim bytes in a container.</summary>
        void AddClip(bool ca, string packPath, params float[] twists)
        {
            var clip = new AnimationClip();
            foreach (var twist in twists)
            {
                var frame = TestSkeletons.BindFrame(_skeleton);
                frame.Rotation[Lower] = SwingTwist.Compose(twist, Vector2.Zero, Vector3.UnitX);
                clip.DynamicFrames.Add(frame);
            }
            clip.PlayTimeInSec = twists.Length / 20f;
            var bytes = AnimationFile.ConvertToBytes(clip.ConvertToFileFormat(_skeleton));
            var name = $"animations/test/clip{_references.Count}.anim";

            File.WriteAllText(packPath, "pack");
            var container = new Mock<IPackFileContainer>();
            container.SetupGet(x => x.IsCaPackFile).Returns(ca);
            container.SetupGet(x => x.SystemFilePath).Returns(packPath);
            container.SetupGet(x => x.Name).Returns(Path.GetFileName(packPath));
            container.Setup(x => x.FindFile(name)).Returns(PackFile.CreateFromBytes(Path.GetFileName(name), bytes));
            _references.Add(new AnimationReference(name, container.Object));
        }

        JointLimitStore Store()
        {
            var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
            lookup.Setup(x => x.CopyAnimationsForSkeleton("test_arm")).Returns(() => _references.ToList());
            return new JointLimitStore(lookup.Object, _folder);
        }

        string PackPath(string name) => Path.Combine(Directory.CreateDirectory(_folder + "_packs").FullName, name);

        [TearDown]
        public void RemovePacks()
        {
            if (Directory.Exists(_folder + "_packs"))
                Directory.Delete(_folder + "_packs", true);
        }

        [Test]
        public async Task Measure_UsesCaContainersOnly()
        {
            AddClip(true, PackPath("data.pack"), -0.4f, 0.6f);
            AddClip(false, PackPath("mod.pack"), 2.5f);

            var limits = await Store().MeasureAsync(_skeleton, null, CancellationToken.None);

            Assert.That(limits, Is.Not.Null);
            Assert.That(limits!.TryGet(Lower, out var limit), Is.True);
            Assert.That(limit.TwistCenter, Is.EqualTo(0.1f).Within(1e-3f));
            Assert.That(limit.TwistHalfWidth, Is.EqualTo(0.5f + MathHelper.ToRadians(5)).Within(1e-3f));
        }

        [Test]
        public async Task Measure_ThenTryLoad_RoundTrips()
        {
            AddClip(true, PackPath("data.pack"), -0.4f, 0.6f);
            var store = Store();

            var measured = await store.MeasureAsync(_skeleton, null, CancellationToken.None);
            var loaded = store.TryLoad(_skeleton);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Limits.Keys, Is.EquivalentTo(measured!.Limits.Keys));
            foreach (var (bone, limit) in measured.Limits)
            {
                var back = loaded.Limits[bone];
                Assert.That(back.TwistCenter, Is.EqualTo(limit.TwistCenter).Within(1e-6f));
                Assert.That(back.TwistHalfWidth, Is.EqualTo(limit.TwistHalfWidth).Within(1e-6f));
                Assert.That(Vector2.Distance(back.SwingMin, limit.SwingMin), Is.LessThan(1e-6f));
                Assert.That(Vector2.Distance(back.SwingMax, limit.SwingMax), Is.LessThan(1e-6f));
            }
        }

        [Test]
        public async Task TryLoad_StaleStamp_ReturnsNull()
        {
            var pack = PackPath("data.pack");
            AddClip(true, pack, -0.4f, 0.6f);
            var store = Store();
            await store.MeasureAsync(_skeleton, null, CancellationToken.None);

            File.SetLastWriteTimeUtc(pack, DateTime.UtcNow.AddDays(1));   // a game patch

            Assert.That(store.TryLoad(_skeleton), Is.Null);
        }

        [Test]
        public void TryLoad_NothingMeasured_ReturnsNull()
        {
            AddClip(true, PackPath("data.pack"), -0.4f, 0.6f);

            Assert.That(Store().TryLoad(_skeleton), Is.Null);
        }

        [Test]
        public async Task Override_SurvivesARemeasure()
        {
            AddClip(true, PackPath("data.pack"), -0.4f, 0.6f);
            var store = Store();
            await store.MeasureAsync(_skeleton, null, CancellationToken.None);

            store.SaveOverride(_skeleton, Lower, null);   // widened: free
            var remeasured = await store.MeasureAsync(_skeleton, null, CancellationToken.None);

            Assert.That(remeasured!.TryGet(Lower, out _), Is.False);
            Assert.That(store.TryLoad(_skeleton)!.TryGet(Lower, out _), Is.False);

            store.ClearOverride(_skeleton, Lower);
            Assert.That(store.TryLoad(_skeleton)!.TryGet(Lower, out _), Is.True, "reset brings the measured limit back");
        }

        [Test]
        public async Task NoCaClips_ReturnsNull_AndWritesNothing()
        {
            AddClip(false, PackPath("mod.pack"), -0.4f, 0.6f);

            var limits = await Store().MeasureAsync(_skeleton, null, CancellationToken.None);

            Assert.That(limits, Is.Null);
            Assert.That(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder).Any(), Is.False);
        }

        [Test]
        public void CancelledMeasure_LeavesNoFile()
        {
            AddClip(true, PackPath("data.pack"), -0.4f, 0.6f);
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();

            Assert.CatchAsync<OperationCanceledException>(() => Store().MeasureAsync(_skeleton, null, cancel.Token));
            Assert.That(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder).Any(), Is.False);
        }

        // An IO error writing the cache threw away a measure that had just read every CA clip.
        [Test]
        public async Task AFailedCacheWrite_StillReturnsTheMeasurement()
        {
            AddClip(true, PackPath("data.pack"), -0.4f, 0.6f);
            File.WriteAllText(_folder, "a file where the cache folder should be");
            try
            {
                var limits = await Store().MeasureAsync(_skeleton, null, CancellationToken.None);

                Assert.That(limits, Is.Not.Null);
                Assert.That(limits!.TryGet(Lower, out _), Is.True);
            }
            finally
            {
                File.Delete(_folder);
            }
        }

        [Test]
        public async Task ABrokenClip_IsSkipped()
        {
            AddClip(true, PackPath("data.pack"), -0.4f, 0.6f);
            var broken = new Mock<IPackFileContainer>();
            broken.SetupGet(x => x.IsCaPackFile).Returns(true);
            broken.Setup(x => x.FindFile("animations/test/broken.anim")).Returns(PackFile.CreateFromBytes("broken.anim", [1, 2, 3]));
            _references.Add(new AnimationReference("animations/test/broken.anim", broken.Object));

            var limits = await Store().MeasureAsync(_skeleton, null, CancellationToken.None);

            Assert.That(limits!.TryGet(Lower, out _), Is.True);
        }
    }
}
