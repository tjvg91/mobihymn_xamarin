using System;
using System.Collections.Generic;
using System.IO;

using NAudio.Wave;
using MeltySynth;
using Android.Bluetooth;
using System.Linq;

namespace MobiHymn2.Utils
{
    public class MidiAudioStream : ISampleProvider
    {
        private static WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);


        private Synthesizer synthesizer;
        private MidiFileSequencer sequencer;

        private float[] bufferLeft;
        private float[] bufferRight;

        private object mutex;

        public MidiAudioStream(string soundFontPath)
        {
            synthesizer = new Synthesizer(soundFontPath, SampleRate);

            Init();
        }

        public MidiAudioStream(Stream soundFontStream)
        {
            var soundfont = new SoundFont(soundFontStream);
            synthesizer = new Synthesizer(soundfont, SampleRate);

            Init();
        }

        private void Init()
        {
            sequencer = new MidiFileSequencer(synthesizer);


            mutex = new object();
        }

        public IEnumerable<(short left, short right)> EnumerateSamples(int? loopStart)
        {
            while (true)
            {
                lock (mutex)
                {
                    sequencer.RenderInt16(bufferLeft, bufferRight);
                }

                for (var t = 0; t < bufferLeft.Length; t++)
                {
                    yield return (bufferLeft[t], bufferRight[t]);
                }
            }
        }

        public void Play(Stream stream, bool loop)
        {
            lock (mutex)
            {
                sequencer.Play(new MidiFile(stream), loop);
            }
        }

        public void Play(string file, bool loop)
        {
            lock (mutex)
            {
                sequencer.Play(new MidiFile(file), loop);
            }
        }

        public void Play(MidiFile midiFile, bool loop)
        {
            lock (mutex)
            {
                sequencer.Play(midiFile, loop);
            }
        }

        public void Stop()
        {
            lock (mutex)
            {
                sequencer.Stop();
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            lock (mutex)
            {
                sequencer.RenderInterleaved(buffer.AsSpan(offset, count));
            }

            return count;
        }

        public void RenderWave(MidiFile midiFile)
        {
            bufferLeft = new float[(int)(SampleRate * midiFile.Length.TotalSeconds / sequencer.Speed)];
            bufferRight = new float[(int)(SampleRate * midiFile.Length.TotalSeconds / sequencer.Speed)];

            sequencer.Render(bufferLeft, bufferRight);
        }

        private static void WriteWaveFile(float[] left, float[] right, int sampleRate, string path)
        {
            var leftMax = left.Max(x => Math.Abs(x));
            var rightMax = right.Max(x => Math.Abs(x));
            var a = 0.99F / Math.Max(leftMax, rightMax);

            var format = new WaveFormat(sampleRate, 16, 2);
            using (var writer = new WaveFileWriter(path, format))
            {
                for (var t = 0; t < left.Length; t++)
                {
                    writer.WriteSample(a * left[t]);
                    writer.WriteSample(a * right[t]);
                }
            }
        }

        public int? Samples => null;
        public int Channels => 2;
        public int Bits => 16;
        public int SampleRate => 44100;

        public WaveFormat WaveFormat => format;
    }
}
