using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GameWorld.Core.Services.SceneSaving;
using GameWorld.Core.Services.SceneSaving.Geometry;
using GameWorld.Core.Services.SceneSaving.Lod;
using GameWorld.Core.Services.SceneSaving.Material;
using Moq;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel.LodHeader;

namespace GameWorld.Core.Test.Services.SceneSaving
{
    internal class GeometrySaveSettingsTests
    {
        [TestCase]
        public void Initialize_Wh3()
        {
            // Arrange
            var gameSettings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);

            // Act
            var saveSettings = new GeometrySaveSettings(gameSettings);

            // Assert
            Assert.That(saveSettings.GeometryOutputType, Is.EqualTo(GeometryStrategy.Rmv7));
            Assert.That(saveSettings.MaterialOutputType, Is.EqualTo(MaterialStrategy.WsModel_Warhammer3));
            Assert.That(saveSettings.LodGenerationMethod, Is.EqualTo(LodStrategy.MeshOptimizer));
        }

        [TestCase]
        public void Initialize_OldGame()
        {
            // Arrange
            var gameSettings = new ApplicationSettingsService(GameTypeEnum.Rome2);

            // Act
            var saveSettings = new GeometrySaveSettings(gameSettings);

            // Assert
            Assert.That(saveSettings.GeometryOutputType, Is.EqualTo(GeometryStrategy.Rmv6));
            Assert.That(saveSettings.MaterialOutputType, Is.EqualTo(MaterialStrategy.None));
            Assert.That(saveSettings.LodGenerationMethod, Is.EqualTo(LodStrategy.MeshOptimizer));
        }


        [TestCase]
        public void InitializeLodSettings_2_LodsFromHeader()
        {
            // Arrange
            var gameSettings = new ApplicationSettingsService(GameTypeEnum.Rome2);
            RmvLodHeader[] lodHeader = [
                new Mock<RmvLodHeader>().Object,
                new Mock<RmvLodHeader>().Object];

            // Act
            var saveSettings = new GeometrySaveSettings(gameSettings);
            saveSettings.InitializeLodSettings(lodHeader);

            // Assert
            AssertLodHeaderSettings(saveSettings, 2, [false, false]);
        }

        [TestCase]
        public void InitializeLodSettings_4_LodsFromHeader()
        {
            // Arrange
            var gameSettings = new ApplicationSettingsService(GameTypeEnum.Rome2);
            RmvLodHeader[] lodHeader = [
                new Mock<RmvLodHeader>().Object,
                new Mock<RmvLodHeader>().Object,
                new Mock<RmvLodHeader>().Object,
                new Mock<RmvLodHeader>().Object ];

            // Act
            var saveSettings = new GeometrySaveSettings(gameSettings);
            saveSettings.InitializeLodSettings(lodHeader);

            // Assert
            AssertLodHeaderSettings(saveSettings, 4, [false, false, true, true]);
        }


        [TestCase]
        public void InitializeLodSettings_RefreshNumberOfLods_2()
        {
            // Arrange
            var gameSettings = new ApplicationSettingsService(GameTypeEnum.Rome2);
            RmvLodHeader[] lodHeader = [
                new Mock<RmvLodHeader>().Object,
                new Mock<RmvLodHeader>().Object,
                new Mock<RmvLodHeader>().Object,
                new Mock<RmvLodHeader>().Object ];

            // Act
            var saveSettings = new GeometrySaveSettings(gameSettings);
            saveSettings.InitializeLodSettings(lodHeader);
            saveSettings.NumberOfLodsToGenerate = 2;
            saveSettings.RefreshLodSettings();

            // Assert
            AssertLodHeaderSettings(saveSettings, 2, [false, false]);
        }

        [TestCase]
        public void InitializeLodSettings_RefreshNumberOfLods_5()
        {
            // Arrange
            var gameSettings = new ApplicationSettingsService(GameTypeEnum.Rome2);
            RmvLodHeader[] lodHeader = [
                new Mock<RmvLodHeader>().Object,
                new Mock<RmvLodHeader>().Object,
                new Mock<RmvLodHeader>().Object,
                new Mock<RmvLodHeader>().Object ];

            // Act
            var saveSettings = new GeometrySaveSettings(gameSettings);
            saveSettings.InitializeLodSettings(lodHeader);
            saveSettings.NumberOfLodsToGenerate = 5;
            saveSettings.RefreshLodSettings();

            // Assert
            AssertLodHeaderSettings(saveSettings, 5, [false, false, true, true, true]);
        }


        // Upstream #322: the next editor's save offers the last save's choices, in the game they were made in.
        [TestCase]
        public void RememberedChoices_AreOfferedByTheNextSettings()
        {
            var gameSettings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var first = new GeometrySaveSettings(gameSettings)
            {
                LodGenerationMethod = LodStrategy.Lod0ForAll,
                OnlySaveVisible = false,
                DisplayDialogOnNextSave = false,
                GeometryOutputType = GeometryStrategy.Rmv6,
            };
            first.RememberChoices();

            var next = new GeometrySaveSettings(gameSettings);

            Assert.That(next.LodGenerationMethod, Is.EqualTo(LodStrategy.Lod0ForAll));
            Assert.That(next.OnlySaveVisible, Is.False);
            Assert.That(next.DisplayDialogOnNextSave, Is.False);
            Assert.That(next.GeometryOutputType, Is.EqualTo(GeometryStrategy.Rmv6));
            Assert.That(next.MaterialOutputType, Is.EqualTo(MaterialStrategy.WsModel_Warhammer3));
        }

        [TestCase]
        public void RememberedChoices_FromAnotherGame_AreIgnored()
        {
            var gameSettings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            new GeometrySaveSettings(gameSettings) { LodGenerationMethod = LodStrategy.Lod0ForAll, OnlySaveVisible = false }.RememberChoices();
            gameSettings.CurrentSettings.CurrentGame = GameTypeEnum.Rome2;

            var next = new GeometrySaveSettings(gameSettings);

            Assert.That(next.LodGenerationMethod, Is.EqualTo(LodStrategy.MeshOptimizer));
            Assert.That(next.OnlySaveVisible, Is.True);
            Assert.That(next.MaterialOutputType, Is.EqualTo(MaterialStrategy.None));
        }

        [TestCase]
        public void ARememberedStrategyThatNoLongerExists_KeepsTheDefault()
        {
            var gameSettings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            gameSettings.CurrentSettings.KitbashSaveChoices = new KitbashSaveChoices(GameTypeEnum.Warhammer3, "Rmv9", "Gone", "Lod0ForAll", true, true);

            var next = new GeometrySaveSettings(gameSettings);

            Assert.That(next.GeometryOutputType, Is.EqualTo(GeometryStrategy.Rmv7));
            Assert.That(next.MaterialOutputType, Is.EqualTo(MaterialStrategy.WsModel_Warhammer3));
            Assert.That(next.LodGenerationMethod, Is.EqualTo(LodStrategy.Lod0ForAll));
        }

        void AssertLodHeaderSettings(GeometrySaveSettings settings, int expectedCount, bool[] shouldBeOptimized)
        {
            Assert.That(settings.LodSettingsPerLod.Count, Is.EqualTo(expectedCount));
            Assert.That(settings.NumberOfLodsToGenerate, Is.EqualTo(expectedCount));
            Assert.That(shouldBeOptimized.Count, Is.EqualTo(expectedCount));

            for (var i = 0; i < expectedCount; i++)
            {
                Assert.That(settings.LodSettingsPerLod[i].OptimizeAlpha, Is.EqualTo(shouldBeOptimized[i]));
                Assert.That(settings.LodSettingsPerLod[i].OptimizeVertex, Is.EqualTo(shouldBeOptimized[i]));
            }
        }
    }
}
