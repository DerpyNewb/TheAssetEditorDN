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
                Assert.That(Resolve(bank, "OnlyBad").Silent, Is.EqualTo("event Event_B plays nothing found in the loaded banks"));
            });
        }

        [Test]
        public void Resolve_PlayActionsThatReachNothing_AreNotTheSameAsNoPlayAction()
        {
            var bank = new Bank().Row("EmptyContainer", "Event_C").Row("StopAndUnloaded", "Event_D").Row("WemWins", "Event_E");
            bank.Event("Event_C", bank.Play(bank.Random()));
            bank.Event("Event_D", bank.Action(AkActionType.Stop_E_O, bank.Sound(7)), bank.Play(404));
            bank.Event("Event_E", bank.Play(404), bank.Play(bank.Sound(8, wemExists: false)));

            Assert.Multiple(() =>
            {
                Assert.That(Resolve(bank, "EmptyContainer").Silent, Is.EqualTo("event Event_C plays nothing found in the loaded banks"));
                Assert.That(Resolve(bank, "StopAndUnloaded").Silent, Is.EqualTo("event Event_D plays nothing found in the loaded banks"));
                Assert.That(Resolve(bank, "WemWins").Silent, Is.EqualTo("WEM not found: 8.wem"));
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

        // The crossing rule, against a 2.0 s clip.
        const double ClipLength = 2.0;

        static (double From, double To, bool Wrapped) StepOf(double previous, double now, double elapsed, double speed = 1) =>
            SoundTriggerCrossing.Step(previous, now, ClipLength, elapsed, speed);

        static bool Crosses(double start, (double From, double To, bool Wrapped) step) => SoundTriggerCrossing.Crossed(start, step, ClipLength);

        [Test]
        public void Step_Playback_FiresTagsInTheSpanCovered()
        {
            var step = StepOf(0.50, 0.52, 0.02);

            Assert.Multiple(() =>
            {
                Assert.That(step.Wrapped, Is.False);
                Assert.That(Crosses(0.51, step), Is.True);
                Assert.That(Crosses(0.50, step), Is.False);
                Assert.That(Crosses(0.53, step), Is.False);
            });
        }

        [Test]
        public void Step_LoopWrap_FiresBothSides()
        {
            var step = StepOf(1.99, 0.01, 0.02);

            Assert.Multiple(() =>
            {
                Assert.That(step.Wrapped, Is.True);
                Assert.That(Crosses(1.995, step), Is.True);
                Assert.That(Crosses(0.0, step), Is.True);
                Assert.That(Crosses(0.005, step), Is.True);
                Assert.That(Crosses(1.98, step), Is.False);
                Assert.That(Crosses(0.02, step), Is.False);
            });
        }

        [Test]
        public void Step_Jump_FiresOnlyTheLastFrame()
        {
            var forward = StepOf(0.2, 1.5, 0.02);
            var back = StepOf(1.5, 0.2, 0.02);

            Assert.Multiple(() =>
            {
                Assert.That(forward.Wrapped, Is.False);
                Assert.That(Crosses(1.0, forward), Is.False);
                Assert.That(Crosses(1.49, forward), Is.True);
                Assert.That(back.Wrapped, Is.False);
                Assert.That(Crosses(1.8, back), Is.False);
                Assert.That(Crosses(0.1, back), Is.False);
                Assert.That(Crosses(0.19, back), Is.True);
            });
        }

        [Test]
        public void Step_ElapsedIsCapped_AndScaledBySpeed()
        {
            var resume = StepOf(1.0, 0.016, 30);
            var normal = StepOf(0.5, 0.56, 0.02);
            var slow = StepOf(0.5, 0.56, 0.02, speed: 0.25);

            Assert.Multiple(() =>
            {
                Assert.That(resume.Wrapped, Is.False);
                Assert.That(Crosses(1.5, resume), Is.False);
                Assert.That(Crosses(1.99, resume), Is.False);
                Assert.That(Crosses(0.0, resume), Is.True);
                Assert.That(normal, Is.EqualTo((0.5, 0.56, false)));
                Assert.That(slow.Wrapped, Is.False);
                Assert.That(slow.From, Is.EqualTo(0.555).Within(0.001));
                Assert.That(Crosses(0.51, slow), Is.False);
            });
        }

        [Test]
        public void Crossed_TagOutsideTheClip_NeverFires()
        {
            // The jump's span starts before zero so a tag at 0.0 can fire; a tag before the clip must still not.
            var steps = new[] { StepOf(0.5, 0.52, 0.02), StepOf(1.99, 0.01, 0.02), StepOf(1.0, 0.016, 30), StepOf(0.2, 1.99, 0.02) };

            Assert.Multiple(() =>
            {
                foreach (var step in steps)
                {
                    Assert.That(Crosses(-0.1, step), Is.False);
                    Assert.That(Crosses(-0.05, step), Is.False);
                    Assert.That(Crosses(2.1, step), Is.False);
                }
            });
        }

        // The session, over a bank where each named tag plays one sound.
        static Bank Sounding(params (string Tag, uint Source)[] tags)
        {
            var bank = new Bank();
            foreach (var (tag, source) in tags)
            {
                bank.Row(tag, tag + "_Event");
                bank.Event(tag + "_Event", bank.Play(bank.Sound(source)));
            }
            return bank;
        }

        static SoundTriggerTag Trigger(string tag, float start) => new(tag, start, "slot .snd.meta");

        sealed class SessionRun
        {
            public List<uint> Played { get; } = [];
            public List<uint> Prepared { get; } = [];
            public SoundTriggerSession Session { get; }

            public SessionRun(Bank bank, bool prepareResult = true) =>
                Session = new SoundTriggerSession(bank.Resolver(), id => { Prepared.Add(id); return prepareResult; }, Played.Add, new Random(1));

            public SessionRun Rebuild(params SoundTriggerTag[] tags)
            {
                Session.Rebuild(tags, campaign: false);
                return this;
            }

            public SessionRun Advance(double now, double elapsed = 0.02, bool playing = true, double speed = 1)
            {
                Session.Advance(now, playing, elapsed, speed, ClipLength);
                return this;
            }

            /// <summary>One frame of 0.02 s per index, looping the 2.0 s clip.</summary>
            public SessionRun Frames(int first, int count)
            {
                foreach (var i in Enumerable.Range(first, count))
                    Advance(i % 100 / 50.0);
                return this;
            }
        }

        [Test]
        public void Session_PlayStartAtZero_FiresTagAtZero()
        {
            // Two tags, so a stale position that wrongly counts as a wrap would also fire the one at 1.5.
            var run = new SessionRun(Sounding(("Start", 1), ("Late", 2))).Rebuild(Trigger("Start", 0f), Trigger("Late", 1.5f));
            run.Advance(1.3, playing: false).Advance(0.016, elapsed: 5);

            var resumed = new SessionRun(Sounding(("Tag", 3))).Rebuild(Trigger("Tag", 0.9f));
            resumed.Advance(1.0, elapsed: 0.016).Advance(1.016, elapsed: 30);

            Assert.Multiple(() =>
            {
                Assert.That(run.Played, Is.EqualTo(new uint[] { 1 }));
                Assert.That(resumed.Played, Is.Empty);
            });
        }

        [Test]
        public void Session_NotPlaying_FiresNothing_ButRemembersTheTime()
        {
            var run = new SessionRun(Sounding(("Tag", 1))).Rebuild(Trigger("Tag", 1.0f));

            run.Advance(1.0, playing: false);
            var whilePaused = run.Played.ToList();
            run.Advance(1.02);

            Assert.Multiple(() =>
            {
                Assert.That(whilePaused, Is.Empty);
                Assert.That(run.Played, Is.Empty);
            });
        }

        [Test]
        public void Session_FiresEachTagOncePerPass()
        {
            var run = new SessionRun(Sounding(("Tag", 1))).Rebuild(Trigger("Tag", 1.0f));

            run.Frames(48, 4);
            var firstPass = run.Played.ToList();
            run.Frames(52, 100);

            Assert.Multiple(() =>
            {
                Assert.That(firstPass, Is.EqualTo(new uint[] { 1 }));
                Assert.That(run.Played, Is.EqualTo(new uint[] { 1, 1 }));
            });
        }

        [Test]
        public void Session_UnsavedEdit_FiresAtTheNewTime()
        {
            var run = new SessionRun(Sounding(("Tag", 1))).Rebuild(Trigger("Tag", 0.5f)).Rebuild(Trigger("Tag", 1.0f));

            run.Frames(23, 5);
            var throughOldTime = run.Played.ToList();
            run.Frames(28, 25);

            Assert.Multiple(() =>
            {
                Assert.That(throughOldTime, Is.Empty);
                Assert.That(run.Played, Is.EqualTo(new uint[] { 1 }));
            });
        }

        [Test]
        public void Session_Rebuild_KeepsThePlayheadTime()
        {
            // The frame reports almost no elapsed time, so the step from the remembered 0.50 is a forward span only if Rebuild kept it.
            var run = new SessionRun(Sounding(("Tag", 1))).Rebuild();
            run.Advance(0.50);
            run.Rebuild(Trigger("Tag", 0.51f));
            run.Advance(0.52, elapsed: 0.001);

            Assert.That(run.Played, Is.EqualTo(new uint[] { 1 }));
        }

        [Test]
        public void Session_AdvanceBeforeRebuild_DoesNothing()
        {
            var run = new SessionRun(Sounding(("Tag", 1)));

            Assert.DoesNotThrow(() => run.Advance(0.5));
            Assert.That(run.Played, Is.Empty);
        }

        [Test]
        public void Session_Status_SummaryThenOneLinePerSilentTag()
        {
            var run = new SessionRun(Sounding(("A", 1), ("B", 2)));
            run.Rebuild(Trigger("A", 0.1f), Trigger("Derpy_Swing_01", 0.2f), Trigger("B", 0.3f));

            Assert.That(run.Session.StatusLines, Is.EqualTo(new[]
            {
                "3 triggers: 2 play, 1 silent in game",
                "Derpy_Swing_01 (slot .snd.meta): no audio_metadata_tags_tables row",
            }));
        }

        [Test]
        public void Session_Status_NoTags_OneTag_AndAllSilent()
        {
            var run = new SessionRun(Sounding(("A", 1)));

            Assert.Multiple(() =>
            {
                Assert.That(run.Rebuild().Session.StatusLines, Is.EqualTo(new[] { "0 triggers" }));
                Assert.That(run.Rebuild(Trigger("A", 0.1f)).Session.StatusLines, Is.EqualTo(new[] { "1 trigger: 1 play, 0 silent in game" }));
                Assert.That(run.Rebuild(Trigger("X", 0.1f), Trigger("Y", 0.2f)).Session.StatusLines, Has.Count.EqualTo(3));
                Assert.That(run.Session.StatusLines[0], Is.EqualTo("2 triggers: 0 play, 2 silent in game"));
            });
        }

        [Test]
        public void Session_FailedPrepare_StillCountsAsPlayable()
        {
            var run = new SessionRun(Sounding(("A", 1)), prepareResult: false).Rebuild(Trigger("A", 0.1f));

            Assert.Multiple(() =>
            {
                Assert.That(run.Session.StatusLines, Is.EqualTo(new[] { "1 trigger: 1 play, 0 silent in game" }));
                Assert.That(run.Prepared, Is.EqualTo(new uint[] { 1 }));
            });
        }

        [Test]
        public void Session_LeavesAndSwitches_AreDistinctAcrossTags()
        {
            var bank = new Bank().Row("A", "Ea").Row("B", "Eb");
            var grass = bank.Sound(201);
            var stone = bank.Sound(202);
            var shared = bank.Sound(300);
            bank.Event("Ea", bank.Play(bank.Switch("Surface", "Grass", ("Grass", grass), ("Stone", stone))), bank.Play(shared));
            bank.Event("Eb", bank.Play(bank.Switch("Surface", "Stone", ("Stone", stone), ("Grass", grass))), bank.Play(shared));
            var run = new SessionRun(bank).Rebuild(Trigger("A", 0.1f), Trigger("B", 0.2f));

            Assert.Multiple(() =>
            {
                Assert.That(run.Session.Leaves, Is.EqualTo(new uint[] { 201, 300, 202 }));
                Assert.That(run.Prepared, Is.EqualTo(new uint[] { 201, 300, 202 }));
                Assert.That(run.Session.Switches, Has.Count.EqualTo(1));
                Assert.That(run.Session.Switches[0].DefaultSwitch, Is.EqualTo(Hash("Grass")));
            });
        }

        [Test]
        public void Session_SwitchChoice_SurvivesRebuild()
        {
            var bank = new Bank().Row("Tag", "Step");
            bank.Event("Step", bank.Play(bank.Switch("Surface", "Grass", ("Grass", bank.Sound(201)), ("Stone", bank.Sound(202)))));
            var run = new SessionRun(bank);
            run.Session.SwitchChoices[Hash("Surface")] = Hash("Stone");

            run.Rebuild(Trigger("Tag", 0.5f)).Rebuild(Trigger("Tag", 0.5f)).Advance(0.5);

            Assert.Multiple(() =>
            {
                Assert.That(run.Session.Leaves, Is.EqualTo(new uint[] { 202 }));
                Assert.That(run.Played, Is.EqualTo(new uint[] { 202 }));
            });
        }

        [Test]
        public void Session_Offset_FiresEarly()
        {
            var run = new SessionRun(Sounding(("Tag", 1))).Rebuild(Trigger("Tag", 0.50f));
            run.Session.OffsetSeconds = 0.03;

            run.Advance(0.46);
            var before = run.Played.ToList();
            run.Advance(0.48);

            Assert.Multiple(() =>
            {
                Assert.That(before, Is.Empty);
                Assert.That(run.Played, Is.EqualTo(new uint[] { 1 }));
            });
        }
    }
}
