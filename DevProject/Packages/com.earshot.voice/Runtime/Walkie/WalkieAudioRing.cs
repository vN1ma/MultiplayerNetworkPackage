using System;

namespace Earshot.Voice
{
    /// <summary>
    /// Einfacher Ringpuffer fuer Audio-Samples (Audio-Thread + Main-Thread).
    /// </summary>
    internal sealed class WalkieAudioRing
    {
        private readonly object gate = new object();
        private float[] buffer;
        private int write;
        private int read;
        private int count;

        public WalkieAudioRing(int capacitySamples)
        {
            buffer = new float[Math.Max(256, capacitySamples)];
        }

        public void Clear()
        {
            lock (gate)
            {
                write = 0;
                read = 0;
                count = 0;
            }
        }

        public void EnsureCapacity(int capacitySamples)
        {
            lock (gate)
            {
                if (buffer.Length >= capacitySamples) return;
                buffer = new float[capacitySamples];
                write = 0;
                read = 0;
                count = 0;
            }
        }

        public void Write(float[] data, int offset, int length)
        {
            if (data == null || length <= 0) return;

            lock (gate)
            {
                for (int i = 0; i < length; i++)
                {
                    if (count >= buffer.Length)
                    {
                        // Ueberlauf: aeltestes Sample verwerfen.
                        read = (read + 1) % buffer.Length;
                        count--;
                    }

                    buffer[write] = data[offset + i];
                    write = (write + 1) % buffer.Length;
                    count++;
                }
            }
        }

        public int Read(float[] into, int offset, int length)
        {
            if (into == null || length <= 0) return 0;

            lock (gate)
            {
                int n = Math.Min(length, count);
                for (int i = 0; i < n; i++)
                {
                    into[offset + i] = buffer[read];
                    read = (read + 1) % buffer.Length;
                    count--;
                }

                for (int i = n; i < length; i++)
                {
                    into[offset + i] = 0f;
                }

                return n;
            }
        }

        public int Available
        {
            get { lock (gate) return count; }
        }
    }
}
