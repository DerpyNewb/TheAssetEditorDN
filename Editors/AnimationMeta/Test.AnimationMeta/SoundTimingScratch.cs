using System.Diagnostics;
using System.IO;
using System.Reflection;
using Editors.AnimationMeta.SuperView;
using Editors.Audio.SoundTriggers;
using Microsoft.Extensions.DependencyInjection;
using NAudio.Wave;
using Shared.Core.Commands;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Scratch: how late a SuperView sound reaches the audio device, measured on one Stopwatch. A 1000 Hz tone takes the path a trigger
    // takes (the session fires it, the mixer starts a voice over its cached samples, WASAPI plays it) and WASAPI loopback records it. No
    // WEM is made: that needs Wwise, so the tone goes straight into the mixer's cache. Explicit, as AudioOffset plays tones on the PC's
    // audio device for about 35 s.
    public class SoundTimingScratch
    {
        const uint Tone = 1000;
        const int Rate = 48000;
        const int Window = Rate / 200;   // 5 ms
        const double ClipLength = 0.5, TagStart = 0.25, ToneSeconds = 0.2;
        const int Firings = 20;
        // SuperView advances once per CompositionTarget.Rendering, which is once per display refresh: 74 Hz on this PC.
        const double FrameSeconds = 1 / 74.0;

        [Test, Explicit]
        public void AudioOffset()
        {
            Measure(30, 0);
            Measure(50, 0);
            Measure(30, SuperViewViewModel.SoundLeadSeconds);
        }

        // The lead is real time, so at half speed a sound should still land as near the tag's crossing as at speed 1. One measurement,
        // as it plays 20 tones a second apart.
        [Test, Explicit]
        public void AudioOffsetAtHalfSpeed() => Measure(30, SuperViewViewModel.SoundLeadSeconds, speed: 0.5);

        static void Measure(int latencyMs, double offsetSeconds, double speed = 1)
        {
            var clock = Stopwatch.StartNew();
            using var mixer = new SoundTriggerMixer(openDevice: true, latencyMs: latencyMs);
            var cache = (Dictionary<uint, float[]>)typeof(SoundTriggerMixer).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(mixer)!;
            cache[Tone] = ToneSamples();

            var fires = new List<double>();
            var session = new SoundTriggerSession(Resolver(), _ => true, id =>
            {
                fires.Add(clock.Elapsed.TotalSeconds);
                mixer.Play(id);
            }, new Random(1)) { OffsetSeconds = offsetSeconds };
            session.Rebuild([new SoundTriggerTag("Timing_Tone", (float)TagStart, "scratch")], campaign: false);
            Assert.That(session.StatusLines[0], Is.EqualTo("1 trigger: 1 plays, 0 silent in game"));

            // Each packet keeps when it arrived and how many samples had arrived by its end. A packet never arrives before its samples
            // were played, so the packet that came soonest after its samples gives the clock time of every sample near it.
            var packets = new List<(double Arrived, long End)>();
            var samples = new List<float>();
            using var capture = new WasapiLoopbackCapture();
            var format = capture.WaveFormat;
            Assert.That((format.Encoding, format.BitsPerSample, format.SampleRate), Is.EqualTo((WaveFormatEncoding.IeeeFloat, 32, Rate)), "the loopback's format");
            capture.DataAvailable += (_, e) =>
            {
                var arrived = clock.Elapsed.TotalSeconds;
                lock (packets)
                {
                    for (var i = 0; i < e.BytesRecorded / format.BlockAlign; i++)
                    {
                        var sum = 0f;
                        for (var c = 0; c < format.Channels; c++)
                            sum += BitConverter.ToSingle(e.Buffer, i * format.BlockAlign + c * 4);
                        samples.Add(sum / format.Channels);
                    }
                    packets.Add((arrived, samples.Count));
                }
            };
            capture.StartRecording();
            Wait(clock, clock.Elapsed.TotalSeconds + 0.5);

            // One Advance per display frame, the playhead following the clock at the speed given.
            var start = clock.Elapsed.TotalSeconds;
            var last = start;
            var frameTimes = new List<double>();
            for (var frame = 1; fires.Count < Firings; frame++)
            {
                var now = Wait(clock, start + frame * FrameSeconds);
                session.Advance((now - start) * speed % ClipLength, true, now - last, speed, ClipLength);
                frameTimes.Add(now - last);
                last = now;
            }
            Wait(clock, clock.Elapsed.TotalSeconds + ToneSeconds + 0.3);
            capture.StopRecording();

            float[] x;
            (double Arrived, long End)[] p;
            lock (packets)
                (x, p) = (samples.ToArray(), packets.ToArray());

            // A sample's clock time, and the sample at a clock time, from the packets within a second of it.
            double Lead(double t) => p.Where(q => Math.Abs(q.Arrived - t) < 1).Min(q => q.Arrived - (double)q.End / Rate);
            double TimeOf(long n) => (double)n / Rate + Lead((double)n / Rate + p[0].Arrived - (double)p[0].End / Rate);
            long SampleAt(double t) => (long)Math.Round((t - Lead(t)) * Rate);

            var afterFire = new List<double>();
            var afterCrossing = new List<double>();
            var gaps = 0;
            var peak = 0.0;
            for (var k = 0; k < fires.Count; k++)
            {
                // Nothing can sound before it is fired, so the 50 ms before the fire is the noise floor.
                var from = SampleAt(fires[k] - 0.05);
                var floor = Math.Max(Median(Enumerable.Range(0, 10).Select(i => Level(x, from + i * Window))), -80);
                var threshold = floor + 15;
                long onset = -1;
                for (var n = from; n + Window < x.Length && n < from + Rate / 2; n += Rate / 1000)
                {
                    if (Level(x, n) < threshold)
                        continue;
                    onset = n;
                    var amplitude = Math.Pow(10, threshold / 20);
                    while (Math.Abs(x[onset]) < amplitude)
                        onset++;
                    break;
                }
                if (onset < 0)
                {
                    TestContext.Out.WriteLine($"  firing {k}: not heard");
                    continue;
                }

                var crossing = start + (k * ClipLength + TagStart) / speed;
                afterFire.Add(TimeOf(onset) - fires[k]);
                afterCrossing.Add(TimeOf(onset) - crossing);

                // A dropout is a 5 ms stretch inside the tone that the loopback heard as quiet.
                for (var i = 0; i < (int)(ToneSeconds * Rate / Window) - 1; i++)
                {
                    var level = Level(x, onset + i * Window);
                    if (level < threshold)
                        gaps++;
                    peak = Math.Max(peak, level);
                }
            }

            TestContext.Out.WriteLine($"latency {latencyMs} ms, OffsetSeconds {offsetSeconds * 1000:0.0} ms, speed {speed:0.##}: {afterFire.Count} of {fires.Count} firings heard, "
                + $"frames {Median(frameTimes) * 1000:0.00} ms apart");
            TestContext.Out.WriteLine($"  onset after the session fired:     median {Median(afterFire) * 1000:0.0} ms, {afterFire.Min() * 1000:0.0} to {afterFire.Max() * 1000:0.0}");
            TestContext.Out.WriteLine($"  onset after the clock crossed tag: median {Median(afterCrossing) * 1000:0.0} ms, {afterCrossing.Min() * 1000:0.0} to {afterCrossing.Max() * 1000:0.0}");
            TestContext.Out.WriteLine($"  quiet 5 ms windows inside the tones (dropouts): {gaps}; "
                + $"tone level {peak:0.0} dB; packets {p.Length}, leads spread {(p.Max(q => q.Arrived - (double)q.End / Rate) - p.Min(q => q.Arrived - (double)q.End / Rate)) * 1000:0.0} ms");
            TestContext.Out.WriteLine("  onsets after the fire, ms: " + string.Join(" ", afterFire.Select(v => (v * 1000).ToString("0.0"))));
            Assert.That(afterFire, Has.Count.EqualTo(Firings), "every firing is heard");
        }

        // For the picture's offset, which is not measured yet: the PCM a recording of SuperView can contain, so a screen-and-loopback
        // recording can be lined up with it by cross-correlation. The clip is Throt's STAND_IDLE_1 with only its four Footstep_Light_Skaven
        // tags left in the Sound tab, plus the persistent .snd.meta's trigger. Every source each can play is decoded as the mixer decodes it,
        // into <SOUND_TIMING_OUT>\<tag>_<source id>.wav.
        [Test, Explicit]
        public void ExportFootstepSounds()
        {
            var data = Environment.GetEnvironmentVariable("WH3_DATA")!;
            var output = Environment.GetEnvironmentVariable("SOUND_TIMING_OUT") ?? Path.Combine(Path.GetTempPath(), "sound_timing");
            Directory.CreateDirectory(output);
            var runner = new AssetEditorTestRunner();
            var loader = runner.ServiceProvider.GetRequiredService<IPackFileContainerLoader>();
            foreach (var pack in new[] { "db.pack", "audio_base_bnk.pack", "audio_base.pack", "audio_en_bnk.pack", "audio_en.pack", "anim.pack", "anim2.pack", "anim_3.pack" })
            {
                var container = loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(data, pack), true);
                container.IsCaPackFile = true;
                runner.PackFileService.AddContainer(container);
            }
            var superView = (SuperViewViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.SuperView_Editor);
            var resolver = superView.LoadSounds(CancellationToken.None).GetAwaiter().GetResult();
            var decode = typeof(SoundTriggerMixer).GetMethod("Decode", BindingFlags.NonPublic | BindingFlags.Static)!;

            var persistent = runner.PackFileService.FindFile(@"animations\audio\battle\persistent\hu17_dlc16_throt_persistent_metadata_alive_0.{4g6w17}.snd.meta")!;
            var tags = new MetaDataFileParser(new MetaDataDatabase()).ParseFile(persistent.DataSource.ReadData()).Attributes.Select(x => x switch
            {
                SoundTrigger_v10 tag => (tag.SoundEvent, tag.StartTime),
                SoundTrigger_v4 tag => (tag.SoundEvent, tag.StartTime),
                _ => (null!, 0),
            }).Where(x => x.SoundEvent != null).ToList();
            foreach (var (soundEvent, start) in tags)
                TestContext.Out.WriteLine($"persistent .snd.meta: {soundEvent} at {start:0.000} s");

            foreach (var tag in tags.Select(x => x.SoundEvent).Prepend("Footstep_Light_Skaven").Distinct())
            {
                var result = resolver.Resolve(tag, false, new Dictionary<uint, uint>());
                var leaves = result.Sound == null ? [] : SoundNodes.Leaves(result.Sound).Distinct().ToList();
                TestContext.Out.WriteLine($"{tag}: {result.Silent ?? "plays"}, {leaves.Count} sources");
                foreach (var id in leaves)
                {
                    var wem = runner.PackFileService.FindFile($@"audio\wwise\{id}.wem") ?? runner.PackFileService.FindFile($@"audio\wwise\english(uk)\{id}.wem");
                    if (decode.Invoke(null, [wem?.DataSource.ReadData()]) is not float[] pcm)
                        continue;
                    using var writer = new WaveFileWriter(Path.Combine(output, $"{tag}_{id}.wav"), SoundTriggerMixer.Format);
                    writer.WriteSamples(pcm, 0, pcm.Length);
                }
            }
        }

        static double Wait(Stopwatch clock, double until)
        {
            double now;
            while ((now = clock.Elapsed.TotalSeconds) < until)
                Thread.SpinWait(50);
            return now;
        }

        // The 1000 Hz component's amplitude over one 5 ms window, in dB: a full-scale sine is 0.
        static double Level(float[] x, long from)
        {
            double re = 0, im = 0;
            for (var i = 0; i < Window; i++)
            {
                var angle = 2 * Math.PI * 1000 * i / Rate;
                re += x[from + i] * Math.Cos(angle);
                im -= x[from + i] * Math.Sin(angle);
            }
            return 20 * Math.Log10(2 * Math.Sqrt(re * re + im * im) / Window + 1e-12);
        }

        static double Median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }

        // 200 ms of 1000 Hz at half scale, starting at phase 0, stereo at the mixer's rate.
        static float[] ToneSamples()
        {
            var tone = new float[(int)(ToneSeconds * Rate) * 2];
            for (var i = 0; i < tone.Length / 2; i++)
                tone[2 * i] = tone[2 * i + 1] = (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / Rate));
            return tone;
        }

        // A tag whose battle event plays the tone: the tag row, the event, its Play action and the sound.
        static SoundTriggerResolver Resolver()
        {
            var (eventId, actionId, soundId) = (WwiseHash.Compute("Timing_Tone_Event"), 1u, 2u);
            var items = new Dictionary<uint, HircItem>
            {
                [soundId] = new CAkSound_V136
                {
                    Id = soundId,
                    HircType = AkBkHircType.Sound,
                    AkBankSourceData = new AkBankSourceData_V136 { AkMediaInformation = new AkBankSourceData_V136.AkMediaInformation_V136 { SourceId = Tone } },
                },
                [actionId] = new CAkAction_V136 { Id = actionId, HircType = AkBkHircType.Action, ActionType = AkActionType.Play, IdExt = soundId },
                [eventId] = new CAkEvent_V136 { Id = eventId, HircType = AkBkHircType.Event, Actions = [new CAkEvent_V136.Action_V136 { ActionId = actionId }] },
            };
            return new SoundTriggerResolver(tag => tag == "Timing_Tone" ? new AudioTagEvents("Timing_Tone_Event", "") : null, id => items.GetValueOrDefault(id), _ => true);
        }
    }
}
