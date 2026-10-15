using Editors.Ipc;
using Shared.Core.PackFiles.Models;

namespace Test.Ipc
{
    public class IpcRequestHandlerTests
    {
        [Test]
        public async Task HandleAsync_ReturnsUnsupportedAction_ForUnknownAction()
        {
            var packLoader = new FakePackLoader();
            var lookup = new FakeLookup();
            var opener = new FakeOpenExecutor();
            var notifier = new FakeNotifier();
            var sut = new IpcRequestHandler(packLoader, lookup, opener, notifier);

            var result = await sut.HandleAsync(new IpcRequest { Action = "save", Path = "x" }, CancellationToken.None);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Is.EqualTo("Unsupported action"));
            Assert.That(opener.OpenCallCount, Is.Zero);
            Assert.That(notifier.CallCount, Is.Zero);
            Assert.That(packLoader.CallCount, Is.Zero);
        }

        [Test]
        public async Task Ping_AnswersWithoutTouchingAnything()
        {
            var packLoader = new FakePackLoader();
            var opener = new FakeOpenExecutor();
            var sut = new IpcRequestHandler(packLoader, new FakeLookup(), opener, new FakeNotifier());

            var result = await sut.HandleAsync(new IpcRequest { Action = "ping" }, CancellationToken.None);

            Assert.That((result.Ok, packLoader.CallCount, opener.OpenCallCount), Is.EqualTo((true, 0, 0)));
        }

        [Test]
        public async Task Load_LoadsThePack_AndOpensNothing()
        {
            var packLoader = new FakePackLoader();
            var opener = new FakeOpenExecutor();
            var sut = new IpcRequestHandler(packLoader, new FakeLookup(), opener, new FakeNotifier());

            var loaded = await sut.HandleAsync(new IpcRequest { Action = "load", PackPathOnDisk = @"F:\data\mod.pack" }, CancellationToken.None);
            var empty = await sut.HandleAsync(new IpcRequest { Action = "load" }, CancellationToken.None);
            packLoader.Result = PackLoadResult.Fail("Pack file could not be loaded");
            var failed = await sut.HandleAsync(new IpcRequest { Action = "load", PackPathOnDisk = @"F:\data\bad.pack" }, CancellationToken.None);

            Assert.That(loaded.Ok, Is.True);
            Assert.That((packLoader.CallCount, opener.OpenCallCount), Is.EqualTo((2, 0)), "the empty request loads nothing, and nothing is opened");
            Assert.That((empty.Ok, empty.Error), Is.EqualTo((false, "packPathOnDisk is empty")));
            Assert.That((failed.Ok, failed.Error), Is.EqualTo((false, "Pack file could not be loaded")));
        }

        [Test]
        public async Task Find_ListsMatchingPaths_WithForwardSlashesAccepted_AndSaysWhenItCutTheListShort()
        {
            var lookup = new FakeLookup { Paths = [@"variantmeshes\a\karl_franz.rigid_model_v2", @"variantmeshes\a\karl_franz.wsmodel", @"variantmeshes\b\other.rigid_model_v2"] };
            var sut = new IpcRequestHandler(new FakePackLoader(), lookup, new FakeOpenExecutor(), new FakeNotifier());

            var all = await sut.HandleAsync(new IpcRequest { Action = "find", Path = "a/KARL" }, CancellationToken.None);
            var one = await sut.HandleAsync(new IpcRequest { Action = "find", Path = "karl", Limit = 1 }, CancellationToken.None);
            var none = await sut.HandleAsync(new IpcRequest { Action = "find", Path = " " }, CancellationToken.None);

            Assert.That((all.Ok, all.Files, all.Truncated), Is.EqualTo((true, lookup.Paths.Take(2).ToList(), false)));
            Assert.That(lookup.LastSearch, Is.EqualTo("karl"));
            Assert.That((one.Files, one.Truncated), Is.EqualTo((lookup.Paths.Take(1).ToList(), true)));
            Assert.That((none.Ok, none.Error), Is.EqualTo((false, "Path is empty")));
        }

        [Test]
        public async Task HandleAsync_ReturnsError_ForEmptyPath()
        {
            var sut = new IpcRequestHandler(new FakePackLoader(), new FakeLookup(), new FakeOpenExecutor(), new FakeNotifier());

            var result = await sut.HandleAsync(new IpcRequest { Action = "open", Path = "   " }, CancellationToken.None);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Is.EqualTo("Path is empty"));
        }

        [Test]
        public async Task HandleAsync_ReturnsNotFound_AndShowsDialog_WhenLookupFails()
        {
            var packLoader = new FakePackLoader();
            var lookup = new FakeLookup();
            var opener = new FakeOpenExecutor();
            var notifier = new FakeNotifier();
            var sut = new IpcRequestHandler(packLoader, lookup, opener, notifier);

            var result = await sut.HandleAsync(new IpcRequest
            {
                Action = "open",
                Path = @"C:\tmp\variantmeshes\foo\bar.rigid_model_v2"
            }, CancellationToken.None);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Is.EqualTo("File not found"));
            Assert.That(result.NormalizedPath, Is.EqualTo(@"variantmeshes\foo\bar.rigid_model_v2"));
            Assert.That(lookup.LastRequestedPath, Is.EqualTo(@"variantmeshes\foo\bar.rigid_model_v2"));
            Assert.That(notifier.CallCount, Is.EqualTo(1));
            Assert.That(notifier.LastPath, Is.EqualTo(@"variantmeshes\foo\bar.rigid_model_v2"));
            Assert.That(opener.OpenCallCount, Is.Zero);
            Assert.That(packLoader.CallCount, Is.Zero);
        }

        [Test]
        public async Task HandleAsync_OpensFile_AndReturnsOk_WhenLookupSucceeds()
        {
            var packFile = PackFile.CreateFromBytes("bird.rigid_model_v2", []);
            var packLoader = new FakePackLoader();
            var lookup = new FakeLookup { Result = packFile };
            var opener = new FakeOpenExecutor();
            var notifier = new FakeNotifier();
            var sut = new IpcRequestHandler(packLoader, lookup, opener, notifier);

            var result = await sut.HandleAsync(new IpcRequest
            {
                Action = "open",
                Path = @"variantmeshes\foo\bird.rigid_model_v2"
            }, CancellationToken.None);

            Assert.That(result.Ok, Is.True);
            Assert.That(opener.OpenCallCount, Is.EqualTo(1));
            Assert.That(opener.LastFile, Is.SameAs(packFile));
            Assert.That(opener.LastBringToFront, Is.True);
            Assert.That(opener.LastOpenInExistingKitbashTab, Is.False);
            Assert.That(notifier.CallCount, Is.Zero);
            Assert.That(packLoader.CallCount, Is.Zero);
        }

        [Test]
        public async Task HandleAsync_DoesNotBringToFront_WhenBringToFrontFalse()
        {
            var packFile = PackFile.CreateFromBytes("bird.rigid_model_v2", []);
            var packLoader = new FakePackLoader();
            var lookup = new FakeLookup { Result = packFile };
            var opener = new FakeOpenExecutor();
            var sut = new IpcRequestHandler(packLoader, lookup, opener, new FakeNotifier());

            var result = await sut.HandleAsync(new IpcRequest
            {
                Action = "open",
                Path = @"variantmeshes\foo\bird.rigid_model_v2",
                BringToFront = false
            }, CancellationToken.None);

            Assert.That(result.Ok, Is.True);
            Assert.That(opener.OpenCallCount, Is.EqualTo(1));
            Assert.That(opener.LastBringToFront, Is.False);
            Assert.That(opener.LastOpenInExistingKitbashTab, Is.False);
            Assert.That(packLoader.CallCount, Is.Zero);
        }

        [Test]
        public async Task HandleAsync_LoadsPackFromDisk_WhenPackPathProvided()
        {
            var packFile = PackFile.CreateFromBytes("arb_base_elephant.rigid_model_v2", []);
            var packLoader = new FakePackLoader();
            var lookup = new FakeLookup { Result = packFile };
            var opener = new FakeOpenExecutor();
            var sut = new IpcRequestHandler(packLoader, lookup, opener, new FakeNotifier());

            var result = await sut.HandleAsync(new IpcRequest
            {
                Action = "open",
                Path = "variantmeshes/wh_variantmodels/el1/arb/arb_new_elephants/arb_base_elephant/arb_base_elephant.rigid_model_v2",
                PackPathOnDisk = "k:/SteamLibrary/steamapps/common/Total War WARHAMMER III/data/ovn_araby.pack"
            }, CancellationToken.None);

            Assert.That(result.Ok, Is.True);
            Assert.That(packLoader.CallCount, Is.EqualTo(1));
            Assert.That(packLoader.LastPackPath, Is.EqualTo("k:/SteamLibrary/steamapps/common/Total War WARHAMMER III/data/ovn_araby.pack"));
            Assert.That(opener.OpenCallCount, Is.EqualTo(1));
            Assert.That(opener.LastOpenInExistingKitbashTab, Is.False);
        }

        [Test]
        public async Task HandleAsync_ReturnsFailure_WhenPackLoadFails()
        {
            var packLoader = new FakePackLoader
            {
                Result = PackLoadResult.Fail("Pack file load failed")
            };
            var lookup = new FakeLookup();
            var opener = new FakeOpenExecutor();
            var notifier = new FakeNotifier();
            var sut = new IpcRequestHandler(packLoader, lookup, opener, notifier);

            var result = await sut.HandleAsync(new IpcRequest
            {
                Action = "open",
                Path = @"variantmeshes\foo\bird.rigid_model_v2",
                PackPathOnDisk = @"k:\mods\ovn_araby.pack"
            }, CancellationToken.None);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Is.EqualTo("Pack file load failed"));
            Assert.That(packLoader.CallCount, Is.EqualTo(1));
            Assert.That(opener.OpenCallCount, Is.Zero);
            Assert.That(notifier.CallCount, Is.Zero);
        }

        [Test]
        public async Task HandleAsync_PassesOpenInExistingKitbashTabFlag_ToExecutor()
        {
            var packFile = PackFile.CreateFromBytes("arb_base_elephant_1.wsmodel", []);
            var packLoader = new FakePackLoader();
            var lookup = new FakeLookup { Result = packFile };
            var opener = new FakeOpenExecutor();
            var sut = new IpcRequestHandler(packLoader, lookup, opener, new FakeNotifier());

            var result = await sut.HandleAsync(new IpcRequest
            {
                Action = "open",
                Path = @"variantmeshes\foo\arb_base_elephant_1.wsmodel",
                OpenInExistingKitbashTab = true
            }, CancellationToken.None);

            Assert.That(result.Ok, Is.True);
            Assert.That(opener.OpenCallCount, Is.EqualTo(1));
            Assert.That(opener.LastOpenInExistingKitbashTab, Is.True);
        }

        private class FakeLookup : IExternalPackFileLookup
        {
            public PackFile? Result { get; set; }
            public string? LastRequestedPath { get; private set; }

            public PackFile? FindByPath(string path)
            {
                LastRequestedPath = path;
                return Result;
            }

            public List<string> Paths { get; set; } = [];
            public string? LastSearch { get; private set; }

            public List<string> Search(string text, int limit)
            {
                LastSearch = text;
                return Paths.Where(x => x.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(limit).ToList();
            }
        }

        private class FakePackLoader : IExternalPackLoader
        {
            public int CallCount { get; private set; }
            public string? LastPackPath { get; private set; }
            public PackLoadResult Result { get; set; } = PackLoadResult.Ok();

            public Task<PackLoadResult> EnsureLoadedAsync(string packPathOnDisk, CancellationToken cancellationToken)
            {
                CallCount++;
                LastPackPath = packPathOnDisk;
                return Task.FromResult(Result);
            }
        }

        private class FakeOpenExecutor : IExternalFileOpenExecutor
        {
            public int OpenCallCount { get; private set; }
            public PackFile? LastFile { get; private set; }
            public bool LastBringToFront { get; private set; }
            public bool LastOpenInExistingKitbashTab { get; private set; }

            public Task OpenAsync(PackFile file, bool bringToFront, bool openInExistingKitbashTab, CancellationToken cancellationToken)
            {
                OpenCallCount++;
                LastFile = file;
                LastBringToFront = bringToFront;
                LastOpenInExistingKitbashTab = openInExistingKitbashTab;
                return Task.CompletedTask;
            }
        }

        private class FakeNotifier : IIpcUserNotifier
        {
            public int CallCount { get; private set; }
            public string? LastPath { get; private set; }

            public Task ShowExternalOpenFailedAsync(string normalizedPath, CancellationToken cancellationToken)
            {
                CallCount++;
                LastPath = normalizedPath;
                return Task.CompletedTask;
            }
        }
    }
}
