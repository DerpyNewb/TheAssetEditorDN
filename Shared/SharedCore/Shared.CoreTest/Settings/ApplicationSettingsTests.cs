using System.Text.Json;
using Shared.Core.Settings;

namespace Shared.CoreTest.Settings
{
    public class ApplicationSettingsTests
    {
        // ApplicationSettingsService.Save and Load go through JsonSerializer with these options.
        [Test]
        public void KitbashSaveChoices_SurviveTheSettingsFile()
        {
            var choices = new KitbashSaveChoices(GameTypeEnum.Warhammer3, "Rmv7", "WsModel_Warhammer3", "Lod0ForAll", false, false);
            var json = JsonSerializer.Serialize(new ApplicationSettings { KitbashSaveChoices = choices }, new JsonSerializerOptions { WriteIndented = true });

            var loaded = JsonSerializer.Deserialize<ApplicationSettings>(json)!;

            Assert.That(loaded.KitbashSaveChoices, Is.EqualTo(choices));
        }

        [Test]
        public void ASettingsFileFromBeforeTheChoices_LoadsWithNone()
        {
            var loaded = JsonSerializer.Deserialize<ApplicationSettings>("""{ "Theme": 0 }""")!;
            Assert.That(loaded.KitbashSaveChoices, Is.Null);
        }
    }
}
