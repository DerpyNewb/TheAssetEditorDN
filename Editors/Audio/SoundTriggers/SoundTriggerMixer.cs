#nullable enable
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Shared.GameFormats.Audio.Containers.Ogg;

namespace Editors.Audio.SoundTriggers
{
    /// <summary>Plays sound sources by id, any number at once. Each source is decoded once to 48 kHz stereo and kept; every Play starts a new
    /// voice over that copy, so the same sound triggered twice sounds twice. It plays on its own output device, apart from the Audio Explorer's SoundEngine.</summary>
    public sealed class SoundTriggerMixer : IDisposable
    {
        public static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        // Guards the cache, the disposed flag and every change to the mixer's inputs. Decoding and the device's own calls stay outside it.
        readonly object _gate = new();
        readonly Dictionary<uint, float[]> _cache = [];
        readonly MixingSampleProvider _mixer = new(Format) { ReadFully = true };
        IWavePlayer? _device;
        bool _disposed;

        /// <param name="openDevice">False builds the mixer with no output device, for tests that read <see cref="Output"/> themselves.</param>
        /// <param name="latencyMs">The device's buffer length. A shorter one starts a sound sooner and is likelier to crackle.</param>
        public SoundTriggerMixer(bool openDevice = true, int latencyMs = 30)
        {
            if (!openDevice)
                return;

            // WASAPI, then WaveOut when WASAPI cannot start, as SoundEngine does. SoundEngine's WaveOut runs at 150 ms; this one's 60 ms
            // floor, with SoundEngine's 3 buffers, was never measured, so whether it plays without dropouts is not known.
            try
            {
                _device = new WasapiOut(AudioClientShareMode.Shared, true, latencyMs);
                _device.Init(_mixer);
            }
            catch
            {
                _device?.Dispose();
                _device = new WaveOutEvent { DesiredLatency = Math.Max(latencyMs, 60), NumberOfBuffers = 3 };
                _device.Init(_mixer);
            }
            _device.Play();
        }

        /// <summary>What the device plays. Always fills a read, with silence when no voice is sounding.</summary>
        public ISampleProvider Output => _mixer;

        /// <summary>Decodes the source and keeps it. False when the bytes are null or do not decode. A source that is already prepared is not asked for again.</summary>
        public bool Prepare(uint sourceId, Func<byte[]?> wemBytes)
        {
            lock (_gate)
            {
                if (_cache.ContainsKey(sourceId))
                    return true;
            }

            var pcm = Decode(wemBytes());
            if (pcm == null)
                return false;

            lock (_gate)
            {
                if (_disposed)
                    return false;
                _cache[sourceId] = pcm;
            }
            return true;
        }

        /// <summary>Starts a new voice. A source that is not prepared is ignored, which after Dispose is every source.</summary>
        public void Play(uint sourceId)
        {
            lock (_gate)
            {
                if (!_cache.TryGetValue(sourceId, out var pcm))
                    return;

                try
                {
                    _mixer.AddMixerInput(new Voice(pcm));
                }
                catch (InvalidOperationException)
                {
                    // NAudio takes 1,024 voices at most. A device that is not reading never ends one, so the extra one is dropped.
                }
            }
        }

        public void StopAll()
        {
            lock (_gate)
                _mixer.RemoveAllMixerInputs();
        }

        /// <summary>Drops every cached source that is not in the set. A voice already playing finishes on its own.</summary>
        public void Keep(IEnumerable<uint> sourceIds)
        {
            var keep = sourceIds.ToHashSet();
            lock (_gate)
            {
                foreach (var id in _cache.Keys.Where(id => !keep.Contains(id)).ToList())
                    _cache.Remove(id);
            }
        }

        public void Dispose()
        {
            IWavePlayer? device;
            lock (_gate)
            {
                _disposed = true;
                _mixer.RemoveAllMixerInputs();
                _cache.Clear();
                device = _device;
                _device = null;
            }

            // Outside the lock: stopping the device waits for its thread, which may be reading Output.
            device?.Stop();
            device?.Dispose();
        }

        // WEM -> Ogg -> float PCM, as SoundEngine decodes it, then to 48 kHz stereo. Null when it does not decode.
        static float[]? Decode(byte[]? wem)
        {
            if (wem == null)
                return null;

            try
            {
                var ogg = OggFile.CreateFromWemBytes(wem).WriteData();
                using var reader = new VorbisWaveReader(new MemoryStream(ogg, writable: false));

                ISampleProvider source = reader;
                if (source.WaveFormat.Channels == 1)
                    source = new MonoToStereoSampleProvider(source);
                else if (source.WaveFormat.Channels > 2)
                {
                    var firstTwo = new MultiplexingSampleProvider([source], 2);
                    firstTwo.ConnectInputToOutput(0, 0);
                    firstTwo.ConnectInputToOutput(1, 1);
                    source = firstTwo;
                }
                if (source.WaveFormat.SampleRate != Format.SampleRate)
                    source = new WdlResamplingSampleProvider(source, Format.SampleRate);

                var samples = new List<float>();
                var buffer = new float[Format.SampleRate * Format.Channels];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    samples.AddRange(new ArraySegment<float>(buffer, 0, read));
                return samples.ToArray();
            }
            catch (Exception)
            {
                return null;
            }
        }

        // One play of a cached source. Each voice has its own position over the shared array.
        sealed class Voice(float[] pcm) : ISampleProvider
        {
            int _position;

            public WaveFormat WaveFormat => Format;

            public int Read(float[] buffer, int offset, int count)
            {
                var n = Math.Min(count, pcm.Length - _position);
                Array.Copy(pcm, _position, buffer, offset, n);
                _position += n;
                return n;
            }
        }
    }
}
