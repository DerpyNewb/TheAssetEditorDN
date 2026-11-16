using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace GameWorld.Core.Test.Services
{
    internal class SkeletonAnimationLookUpHelperTests
    {
        [Test]
        public void GetAnimationsForSkeleton_ReturnsImmediately_WhenDataAlreadyExists()
        {
            var releaseInitialLoad = new ManualResetEventSlim(false);
            var packFileService = CreatePackFileServiceThatBlocksInitialLoad(releaseInitialLoad);
            var eventHub = new Mock<IGlobalEventHub>();
            var helper = new SkeletonAnimationLookUpHelper(packFileService.Object, eventHub.Object);

            var container = new Mock<IPackFileContainer>().Object;
            var expected = new ObservableCollection<AnimationReference>
            {
                new AnimationReference("animations\\battle\\unit\\idle.anim", container)
            };

            SetAnimationLookupEntry(helper, "human_skeleton", expected);

            var stopwatch = Stopwatch.StartNew();
            var result = helper.GetAnimationsForSkeleton("human_skeleton");
            stopwatch.Stop();

            Assert.That(result, Is.SameAs(expected));
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(200));

            releaseInitialLoad.Set();
            helper.Dispose();
        }

        [Test]
        public async Task GetAnimationsForSkeleton_WaitsForInitialLoad_WhenDataMissing()
        {
            var releaseInitialLoad = new ManualResetEventSlim(false);
            var packFileService = CreatePackFileServiceThatBlocksInitialLoad(releaseInitialLoad);
            var eventHub = new Mock<IGlobalEventHub>();
            var helper = new SkeletonAnimationLookUpHelper(packFileService.Object, eventHub.Object);

            var pendingCall = Task.Run(() => helper.GetAnimationsForSkeleton("missing_skeleton"));
            await Task.Delay(100);

            Assert.That(pendingCall.IsCompleted, Is.False);

            releaseInitialLoad.Set();

            var completed = await Task.WhenAny(pendingCall, Task.Delay(3000));
            Assert.That(completed, Is.SameAs(pendingCall));

            var result = await pendingCall;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(0));

            helper.Dispose();
        }

        // The joint-limit measure reads a skeleton's clip list on a worker. A pack refresh changes that list under the helper's
        // lock, so a copy taken without it met the refresh halfway: "Collection was modified", and the measure failed.
        [Test]
        public void CopyAnimationsForSkeleton_IsSafeWhileARefreshChangesTheList()
        {
            var release = new ManualResetEventSlim(true);
            var helper = new SkeletonAnimationLookUpHelper(CreatePackFileServiceThatBlocksInitialLoad(release).Object, new Mock<IGlobalEventHub>().Object);
            var container = new Mock<IPackFileContainer>().Object;
            var list = new ObservableCollection<AnimationReference>(Enumerable.Range(0, 2000).Select(x => new AnimationReference($"clip{x}.anim", container)));
            SetAnimationLookupEntry(helper, "human_skeleton", list);
            var threadLock = typeof(SkeletonAnimationLookUpHelper).GetField("_threadLock", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(helper)!;

            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var refresh = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                    lock (threadLock)
                    {
                        list.Add(new AnimationReference("added.anim", container));
                        list.RemoveAt(list.Count - 1);
                    }
            });

            var copies = 0;
            Assert.That(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    Assert.That(helper.CopyAnimationsForSkeleton("human_skeleton").Count, Is.InRange(2000, 2001));
                    copies++;
                }
            }, Throws.Nothing);
            refresh.Wait();
            TestContext.Out.WriteLine($"{copies} copies");
            helper.Dispose();
        }

        private static Mock<IPackFileService> CreatePackFileServiceThatBlocksInitialLoad(ManualResetEventSlim releaseInitialLoad)
        {
            var packFileService = new Mock<IPackFileService>(MockBehavior.Strict);
            packFileService.Setup(x => x.GetAllPackfileContainers())
                .Returns(() =>
                {
                    releaseInitialLoad.Wait();
                    return [];
                });

            return packFileService;
        }

        private static void SetAnimationLookupEntry(
            SkeletonAnimationLookUpHelper helper,
            string skeletonName,
            ObservableCollection<AnimationReference> entries)
        {
            var field = typeof(SkeletonAnimationLookUpHelper)
                .GetField("_skeletonNameToAnimationMap", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null);

            var map = field!.GetValue(helper) as Dictionary<string, ObservableCollection<AnimationReference>>;
            Assert.That(map, Is.Not.Null);

            map![skeletonName] = entries;
        }
    }
}
