using Editors.Audio.SoundTriggers;
using Shared.GameFormats.DB;

namespace Test.Audio
{
    [TestFixture]
    internal class SoundTriggerTests
    {
        static DbRow Tag(string key, string battleStart, string campaignStart = "") => new(
            new Dictionary<string, object>
            {
                ["key"] = key,
                ["sound_event_battle_start"] = battleStart,
                ["sound_event_campaign_start"] = campaignStart,
            }, 22, "test_pack", "db/audio_metadata_tags_tables/test");

        [Test]
        public void TagTable_FirstRowWins_ExactCase()
        {
            var tags = AudioMetadataTags.From([Tag("Drop_Weapon_Rest", "A", "CA"), Tag("Drop_Weapon_Rest", "B", "CB"), Tag("drop_weapon_rest", "C", "CC")]);

            Assert.Multiple(() =>
            {
                Assert.That(tags.Find("Drop_Weapon_Rest")!.BattleStart, Is.EqualTo("A"));
                Assert.That(tags.Find("Drop_Weapon_Rest")!.CampaignStart, Is.EqualTo("CA"));
                Assert.That(tags.Find("drop_weapon_rest"), Is.EqualTo(new AudioTagEvents("C", "CC")));
                Assert.That(tags.Find("DROP_WEAPON_REST"), Is.Null);
                Assert.That(tags.Count, Is.EqualTo(2));
            });
        }
    }
}
