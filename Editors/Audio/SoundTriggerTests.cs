using Editors.Audio.SoundTriggers;
using Shared.GameFormats.DB;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

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

        // A small in-memory bank. Hirc ids are arbitrary; a sound's hirc id is its source id plus 10000, so a resolver
        // that returned the hirc id where it should return the source id would fail.
        sealed class Bank
        {
            const uint SoundIdOffset = 10000;
            uint _next = 1000000;

            public Dictionary<uint, HircItem> Items { get; } = [];
            public HashSet<uint> Wems { get; } = [];
            public Dictionary<string, AudioTagEvents> Rows { get; } = new(StringComparer.Ordinal);

            public SoundTriggerResolver Resolver() => new(
                tag => Rows.GetValueOrDefault(tag),
                id => Items.GetValueOrDefault(id),
                Wems.Contains);

            public Bank Row(string tag, string battleEvent, string campaignEvent = "")
            {
                Rows[tag] = new AudioTagEvents(battleEvent, campaignEvent);
                return this;
            }

            public uint Sound(uint sourceId, bool wemExists = true)
            {
                var id = sourceId + SoundIdOffset;
                Items[id] = new CAkSound_V136
                {
                    Id = id,
                    HircType = AkBkHircType.Sound,
                    AkBankSourceData = new AkBankSourceData_V136 { AkMediaInformation = new AkBankSourceData_V136.AkMediaInformation_V136 { SourceId = sourceId } },
                };
                if (wemExists)
                    Wems.Add(sourceId);
                return id;
            }

            /// <summary>An event whose actions are the given ones, in order. Returns the event's id.</summary>
            public uint Event(string name, params uint[] actionIds)
            {
                var id = WwiseHash.Compute(name);
                Items[id] = new CAkEvent_V136
                {
                    Id = id,
                    HircType = AkBkHircType.Event,
                    Actions = actionIds.Select(x => new CAkEvent_V136.Action_V136 { ActionId = x }).ToList(),
                };
                return id;
            }

            public uint Action(AkActionType type, uint targetId)
            {
                var id = _next++;
                Items[id] = new CAkAction_V136 { Id = id, HircType = AkBkHircType.Action, ActionType = type, IdExt = targetId };
                return id;
            }

            public uint Play(uint targetId) => Action(AkActionType.Play, targetId);

            public uint Random(params uint[] children)
            {
                var id = _next++;
                Items[id] = new CAkRanSeqCntr_V136
                {
                    Id = id,
                    HircType = AkBkHircType.RandomSequenceContainer,
                    CAkPlayList = new CAkRanSeqCntr_V136.CAkPlayList_V136
                    {
                        Playlist = children.Select(x => new CAkRanSeqCntr_V136.CAkPlayList_V136.AkPlaylistItem_V136 { PlayId = x, Weight = 50000 }).ToList(),
                    },
                };
                return id;
            }

            public uint Blend(params uint[] children)
            {
                var id = _next++;
                Items[id] = new CAkLayerCntr_V136 { Id = id, HircType = AkBkHircType.LayerContainer, Children = new Children_V136 { ChildIds = [.. children] } };
                return id;
            }

            public uint Mixer(params uint[] children)
            {
                var id = _next++;
                Items[id] = new CAkActorMixer_V136 { Id = id, HircType = AkBkHircType.ActorMixer, Children = new Children_V136 { ChildIds = [.. children] } };
                return id;
            }

            public uint Switch(string group, string defaultCase, params (string Case, uint Child)[] cases)
            {
                var id = _next++;
                Items[id] = new CAkSwitchCntr_V136
                {
                    Id = id,
                    HircType = AkBkHircType.SwitchContainer,
                    GroupId = WwiseHash.Compute(group),
                    DefaultSwitch = WwiseHash.Compute(defaultCase),
                    SwitchList = cases
                        .Select(x => (ICAkSwitchCntr.ICAkSwitchPackage)new CAkSwitchCntr_V136.CAkSwitchPackage_V136 { SwitchId = WwiseHash.Compute(x.Case), NodeIdList = [x.Child] })
                        .ToList(),
                };
                return id;
            }
        }

        static readonly IReadOnlyDictionary<uint, uint> NoChoices = new Dictionary<uint, uint>();
        static uint Hash(string name) => WwiseHash.Compute(name);

        static SoundTriggerResult Resolve(Bank bank, string tag = "Tag", bool campaign = false, IReadOnlyDictionary<uint, uint>? choices = null) =>
            bank.Resolver().Resolve(tag, campaign, choices ?? NoChoices);

        static uint[] Leaves(SoundTriggerResult result) => SoundNodes.Leaves(result.Sound!).ToArray();

        [Test]
        public void Resolve_UsesTheTableRow_NotTheTagName()
        {
            var bank = new Bank().Row("Drop_Weapon_Rest", "Battle_Individual_Melee_Weapon_Drop_Weapon_Rest");
            bank.Event("Battle_Individual_Melee_Weapon_Drop_Weapon_Rest", bank.Play(bank.Sound(101)));
            bank.Event("Battle_Individual_Drop_Weapon_Rest", bank.Play(bank.Sound(999)));

            var result = Resolve(bank, "Drop_Weapon_Rest");

            Assert.Multiple(() =>
            {
                Assert.That(result.Tag, Is.EqualTo("Drop_Weapon_Rest"));
                Assert.That(result.Silent, Is.Null);
                Assert.That(Leaves(result), Is.EqualTo(new uint[] { 101 }));
            });
        }

        [Test]
        public void Resolve_NoRow_IsSilent()
        {
            var result = Resolve(new Bank(), "Nothing");

            Assert.Multiple(() =>
            {
                Assert.That(result.Silent, Is.EqualTo("no audio_metadata_tags_tables row"));
                Assert.That(result.Sound, Is.Null);
                Assert.That(result.Switches, Is.Empty);
            });
        }

        [Test]
        public void Resolve_EmptyEvent_AndMissingEvent_AreSilent()
        {
            var bank = new Bank()
                .Row("EmptyBattle", "", "Campaign_Event")
                .Row("EmptyCampaign", "Battle_Event", "  ")
                .Row("Missing", "Battle_Event_Nobody_Loaded");
            bank.Event("Campaign_Event", bank.Play(bank.Sound(1)));

            Assert.Multiple(() =>
            {
                Assert.That(Resolve(bank, "EmptyBattle").Silent, Is.EqualTo("the row names no battle event"));
                Assert.That(Resolve(bank, "EmptyCampaign", campaign: true).Silent, Is.EqualTo("the row names no campaign event"));
                Assert.That(Resolve(bank, "Missing").Silent, Is.EqualTo("event Battle_Event_Nobody_Loaded is in no loaded bank"));
                Assert.That(Resolve(bank, "EmptyBattle").Sound, Is.Null);
            });
        }

        [Test]
        public void Resolve_EventWithNoPlayAction_IsSilent()
        {
            var bank = new Bank().Row("Stopper", "Stop_Event").Row("Nothing", "Empty_Event");
            bank.Event("Stop_Event", bank.Action(AkActionType.Stop_E_O, bank.Sound(7)));
            bank.Event("Empty_Event");

            Assert.Multiple(() =>
            {
                Assert.That(Resolve(bank, "Stopper").Silent, Is.EqualTo("event Stop_Event has no Play action"));
                Assert.That(Resolve(bank, "Nothing").Silent, Is.EqualTo("event Empty_Event has no Play action"));
            });
        }

        [Test]
        public void Resolve_PlayActionWhoseTargetIsNotLoaded_IsDropped()
        {
            var bank = new Bank().Row("Tag", "Event_A").Row("OnlyBad", "Event_B");
            bank.Event("Event_A", bank.Play(404), bank.Play(bank.Sound(11)));
            bank.Event("Event_B", bank.Play(404));

            Assert.Multiple(() =>
            {
                Assert.That(Leaves(Resolve(bank, "Tag")), Is.EqualTo(new uint[] { 11 }));
                Assert.That(Resolve(bank, "OnlyBad").Silent, Is.EqualTo("event Event_B has no Play action"));
            });
        }

        [Test]
        public void Resolve_SeveralPlayActions_PlayTogether()
        {
            var bank = new Bank().Row("Tag", "Event_A");
            bank.Event("Event_A", bank.Play(bank.Sound(21)), bank.Action(AkActionType.Stop_E_O, bank.Sound(99)), bank.Play(bank.Sound(22)));

            var result = Resolve(bank);

            Assert.Multiple(() =>
            {
                Assert.That(result.Sound, Is.InstanceOf<SoundAll>());
                Assert.That(Leaves(result), Is.EqualTo(new uint[] { 21, 22 }));
                Assert.That(SoundNodes.Pick(result.Sound!, new Random(1)), Is.EqualTo(new uint[] { 21, 22 }));
            });
        }

        [Test]
        public void Resolve_EventIdThatIsNotAnEvent_AndMusicObject_AreNotSupported()
        {
            var bank = new Bank().Row("NotAnEvent", "Mixer_Name").Row("Music", "Music_Event");
            var mixerId = Hash("Mixer_Name");
            bank.Items[mixerId] = new CAkActorMixer_V136 { Id = mixerId, HircType = AkBkHircType.ActorMixer };
            var segment = new CAkMusicSegment_V136 { Id = 5, HircType = AkBkHircType.Music_Segment };
            bank.Items[segment.Id] = segment;
            bank.Event("Music_Event", bank.Play(segment.Id));

            Assert.Multiple(() =>
            {
                Assert.That(Resolve(bank, "NotAnEvent").Silent, Is.EqualTo("not supported: ActorMixer"));
                Assert.That(Resolve(bank, "Music").Silent, Is.EqualTo("not supported: Music_Segment"));
            });
        }

        [Test]
        public void Resolve_Switch_PlaysDefault_ThenTheChoice()
        {
            var bank = new Bank().Row("Tag", "Step");
            var grass = bank.Sound(201);
            var stone = bank.Sound(202);
            bank.Event("Step", bank.Play(bank.Switch("Surface", "Grass", ("Grass", grass), ("Stone", stone))));

            var byDefault = Resolve(bank);
            var chosen = Resolve(bank, choices: new Dictionary<uint, uint> { [Hash("Surface")] = Hash("Stone") });

            Assert.Multiple(() =>
            {
                Assert.That(Leaves(byDefault), Is.EqualTo(new uint[] { 201 }));
                Assert.That(byDefault.Switches, Has.Count.EqualTo(1));
                Assert.That(byDefault.Switches[0].GroupId, Is.EqualTo(Hash("Surface")));
                Assert.That(byDefault.Switches[0].DefaultSwitch, Is.EqualTo(Hash("Grass")));
                Assert.That(byDefault.Switches[0].Values, Is.EqualTo(new[] { Hash("Grass"), Hash("Stone") }));
                Assert.That(Leaves(chosen), Is.EqualTo(new uint[] { 202 }));
            });
        }

        [Test]
        public void Resolve_Switch_ChoiceNoCaseHas_FallsBackToTheDefault()
        {
            var bank = new Bank().Row("Tag", "Step");
            bank.Event("Step", bank.Play(bank.Switch("Surface", "Stone", ("Grass", bank.Sound(201)), ("Stone", bank.Sound(202)))));

            var result = Resolve(bank, choices: new Dictionary<uint, uint> { [Hash("Surface")] = Hash("Snow") });

            Assert.That(Leaves(result), Is.EqualTo(new uint[] { 202 }));
        }

        [Test]
        public void Resolve_SwitchWithNoDefault_PlaysTheFirstCase()
        {
            var bank = new Bank().Row("Tag", "Step");
            bank.Event("Step", bank.Play(bank.Switch("Surface", "None", ("Grass", bank.Sound(201)), ("Stone", bank.Sound(202)))));

            var result = Resolve(bank);

            Assert.Multiple(() =>
            {
                Assert.That(Leaves(result), Is.EqualTo(new uint[] { 201 }));
                Assert.That(result.Switches.Single().DefaultSwitch, Is.EqualTo(Hash("None")));
            });
        }

        [Test]
        public void Resolve_SameSwitchGroupInTwoContainers_ListsItOnce()
        {
            var bank = new Bank().Row("Tag", "Step");
            var inner = bank.Switch("Surface", "Mud", ("Mud", bank.Sound(301)), ("Ice", bank.Sound(302)));
            var outer = bank.Switch("Surface", "Grass", ("Grass", inner), ("Stone", bank.Sound(303)));
            bank.Event("Step", bank.Play(outer));

            var result = Resolve(bank);

            Assert.Multiple(() =>
            {
                Assert.That(result.Switches, Has.Count.EqualTo(1));
                Assert.That(result.Switches[0].DefaultSwitch, Is.EqualTo(Hash("Grass")));
                Assert.That(result.Switches[0].Values, Is.EqualTo(new[] { Hash("Grass"), Hash("Stone") }));
            });
        }

        [Test]
        public void Resolve_Random_IsOneOf_Blend_IsAll()
        {
            var bank = new Bank().Row("Rand", "Rand_Event").Row("Layer", "Layer_Event").Row("Mix", "Mix_Event");
            bank.Event("Rand_Event", bank.Play(bank.Random(bank.Sound(301), bank.Sound(302))));
            bank.Event("Layer_Event", bank.Play(bank.Blend(bank.Sound(401), bank.Sound(402))));
            bank.Event("Mix_Event", bank.Play(bank.Mixer(bank.Sound(411), bank.Sound(412))));

            var random = Resolve(bank, "Rand");
            var blend = Resolve(bank, "Layer");
            var mixer = Resolve(bank, "Mix");
            var seeded = new Random(7);
            var picks = Enumerable.Range(0, 40).Select(_ => SoundNodes.Pick(random.Sound!, seeded)).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(random.Sound, Is.InstanceOf<SoundOneOf>());
                Assert.That(Leaves(random), Is.EqualTo(new uint[] { 301, 302 }));
                Assert.That(picks.All(x => x.Count == 1), Is.True);
                Assert.That(picks.Select(x => x.Single()).Distinct().Order(), Is.EqualTo(new uint[] { 301, 302 }));
                Assert.That(blend.Sound, Is.InstanceOf<SoundAll>());
                Assert.That(SoundNodes.Pick(blend.Sound!, new Random(7)), Is.EqualTo(new uint[] { 401, 402 }));
                Assert.That(mixer.Sound, Is.InstanceOf<SoundAll>());
                Assert.That(SoundNodes.Pick(mixer.Sound!, new Random(7)), Is.EqualTo(new uint[] { 411, 412 }));
            });
        }

        [Test]
        public void Resolve_NestedContainers_PickOneThenPlayAll()
        {
            var bank = new Bank().Row("Tag", "Nested");
            var oneOf = bank.Random(bank.Sound(1), bank.Sound(2));
            bank.Event("Nested", bank.Play(bank.Blend(bank.Sound(3), oneOf)));

            var result = Resolve(bank);
            var seeded = new Random(3);
            var picks = Enumerable.Range(0, 40).Select(_ => SoundNodes.Pick(result.Sound!, seeded)).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(Leaves(result), Is.EqualTo(new uint[] { 3, 1, 2 }));
                Assert.That(picks.All(x => x.Count == 2 && x[0] == 3), Is.True);
                Assert.That(picks.Select(x => x[1]).Distinct().Order(), Is.EqualTo(new uint[] { 1, 2 }));
            });
        }

        [Test]
        public void Resolve_MissingWems_AreDropped_AllMissing_IsSilent()
        {
            var some = new Bank().Row("Tag", "Event_A");
            some.Event("Event_A", some.Play(some.Random(some.Sound(501), some.Sound(502, wemExists: false))));
            var none = new Bank().Row("Tag", "Event_A");
            none.Event("Event_A", none.Play(none.Random(none.Sound(501, wemExists: false), none.Sound(502, wemExists: false))));

            var partial = Resolve(some);
            var allMissing = Resolve(none);

            Assert.Multiple(() =>
            {
                Assert.That(partial.Silent, Is.Null);
                Assert.That(partial.Sound, Is.EqualTo(new SoundLeaf(501)));
                Assert.That(Leaves(partial), Is.EqualTo(new uint[] { 501 }));
                Assert.That(allMissing.Sound, Is.Null);
                Assert.That(allMissing.Silent, Is.EqualTo("WEM not found: 501.wem"));
            });
        }

        [Test]
        public void Resolve_ContainerThatContainsItself_DoesNotLoopForever()
        {
            var bank = new Bank().Row("Tag", "Loop");
            var mixerId = 777u;
            bank.Items[mixerId] = new CAkActorMixer_V136 { Id = mixerId, HircType = AkBkHircType.ActorMixer, Children = new Children_V136 { ChildIds = [mixerId, bank.Sound(1)] } };
            bank.Event("Loop", bank.Play(mixerId));

            var result = Resolve(bank);

            Assert.That(Leaves(result), Is.EqualTo(new uint[] { 1 }));
        }

        [Test]
        public void Resolve_CampaignFlag_UsesTheCampaignEvent()
        {
            var bank = new Bank().Row("Tag", "Battle_Event", "Campaign_Event");
            bank.Event("Battle_Event", bank.Play(bank.Sound(1)));
            bank.Event("Campaign_Event", bank.Play(bank.Sound(2)));

            Assert.Multiple(() =>
            {
                Assert.That(Leaves(Resolve(bank, campaign: false)), Is.EqualTo(new uint[] { 1 }));
                Assert.That(Leaves(Resolve(bank, campaign: true)), Is.EqualTo(new uint[] { 2 }));
            });
        }
    }
}
