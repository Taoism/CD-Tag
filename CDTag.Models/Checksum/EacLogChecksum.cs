using System;
using System.Text;

namespace CDTag.Model.Checksum
{
    /// <summary>
    /// The "==== Log checksum ... ====" line EAC 0.99+ appends to its logs: the log text without line breaks, as UTF-16LE,
    /// encrypted with Rijndael (256-bit block and key, CBC, zero IV, zero padding); the checksum is the last cipher block.
    /// Checked against real EAC 1.x logs (see EacLogChecksumTests).
    /// </summary>
    public static class EacLogChecksum
    {
        private static readonly byte[] Key = Convert.FromHexString("9378716CF13E4265AE55338E940B376184DA389E50647726B35F6F341EE3EFD9");

        /// <summary>The checksum (64 upper-case hex characters) of the log text that precedes the checksum line.</summary>
        public static string Compute(string logText)
        {
            string text = logText.Replace("\r", "").Replace("\n", "").Replace("\uFEFF", "").Replace("\uFFFE", "");
            byte[] plain = Encoding.Unicode.GetBytes(text);
            var cipher = new Rijndael(Key, 32);

            var chain = new byte[32];
            var block = new byte[32];
            for (int offset = 0; offset < plain.Length; offset += 32)
            {
                int count = Math.Min(32, plain.Length - offset);
                Array.Clear(block, 0, 32);
                Buffer.BlockCopy(plain, offset, block, 0, count);
                for (int i = 0; i < 32; i++)
                    block[i] ^= chain[i];
                cipher.EncryptBlock(block, chain);
            }
            return Convert.ToHexString(chain);
        }

        /// <summary>Encrypts one block with Rijndael and a 256-bit key; a 16-byte block is AES-256 (used to test this class).</summary>
        public static byte[] EncryptBlock(byte[] key, byte[] block)
        {
            var output = new byte[block.Length];
            new Rijndael(key, block.Length).EncryptBlock(block, output);
            return output;
        }

        /// <summary>Rijndael encryption with a 128- or 256-bit block (Nb = 4 or 8) and a 256-bit key (Nk = 8, 14 rounds).</summary>
        private sealed class Rijndael
        {
            private const int Rounds = 14;
            private static readonly byte[] SBox = CreateSBox();
            private readonly int Nb;
            // Row shifts: Nb = 4 (AES) 0,1,2,3; Nb = 8 0,1,3,4.
            private readonly int[] Shift;
            private readonly byte[] _roundKeys;

            public Rijndael(byte[] key, int blockBytes)
            {
                if (key.Length != 32 || (blockBytes != 16 && blockBytes != 32))
                    throw new ArgumentException("256-bit key and 128- or 256-bit block only.");
                Nb = blockBytes / 4;
                Shift = Nb == 4 ? new[] { 0, 1, 2, 3 } : new[] { 0, 1, 3, 4 };
                _roundKeys = ExpandKey(key);
            }

            public void EncryptBlock(byte[] input, byte[] output)
            {
                int size = Nb * 4;
                var state = new byte[size];
                Buffer.BlockCopy(input, 0, state, 0, size);
                AddRoundKey(state, 0);
                for (int round = 1; round < Rounds; round++)
                {
                    SubBytes(state);
                    ShiftRows(state);
                    MixColumns(state);
                    AddRoundKey(state, round);
                }
                SubBytes(state);
                ShiftRows(state);
                AddRoundKey(state, Rounds);
                Buffer.BlockCopy(state, 0, output, 0, size);
            }

            // State layout: byte (row r, column c) is state[c * 4 + r], as in the input block.
            private void AddRoundKey(byte[] state, int round)
            {
                int offset = round * Nb * 4;
                for (int i = 0; i < Nb * 4; i++)
                    state[i] ^= _roundKeys[offset + i];
            }

            private static void SubBytes(byte[] state)
            {
                for (int i = 0; i < state.Length; i++)
                    state[i] = SBox[state[i]];
            }

            private void ShiftRows(byte[] state)
            {
                var row = new byte[Nb];
                for (int r = 1; r < 4; r++)
                {
                    for (int c = 0; c < Nb; c++)
                        row[c] = state[((c + Shift[r]) % Nb) * 4 + r];
                    for (int c = 0; c < Nb; c++)
                        state[c * 4 + r] = row[c];
                }
            }

            private void MixColumns(byte[] state)
            {
                for (int c = 0; c < Nb; c++)
                {
                    int i = c * 4;
                    byte a0 = state[i], a1 = state[i + 1], a2 = state[i + 2], a3 = state[i + 3];
                    state[i] = (byte)(Mul2(a0) ^ Mul3(a1) ^ a2 ^ a3);
                    state[i + 1] = (byte)(a0 ^ Mul2(a1) ^ Mul3(a2) ^ a3);
                    state[i + 2] = (byte)(a0 ^ a1 ^ Mul2(a2) ^ Mul3(a3));
                    state[i + 3] = (byte)(Mul3(a0) ^ a1 ^ a2 ^ Mul2(a3));
                }
            }

            private static byte Mul2(byte b)
            {
                return (byte)((b << 1) ^ ((b & 0x80) != 0 ? 0x1B : 0));
            }

            private static byte Mul3(byte b)
            {
                return (byte)(Mul2(b) ^ b);
            }

            private byte[] ExpandKey(byte[] key)
            {
                const int nk = 8;
                int words = Nb * (Rounds + 1);
                var w = new byte[words * 4];
                Buffer.BlockCopy(key, 0, w, 0, 32);
                byte rcon = 1;
                var temp = new byte[4];
                for (int i = nk; i < words; i++)
                {
                    Buffer.BlockCopy(w, (i - 1) * 4, temp, 0, 4);
                    if (i % nk == 0)
                    {
                        byte t = temp[0];
                        temp[0] = (byte)(SBox[temp[1]] ^ rcon);
                        temp[1] = SBox[temp[2]];
                        temp[2] = SBox[temp[3]];
                        temp[3] = SBox[t];
                        rcon = Mul2(rcon);
                    }
                    else if (i % nk == 4)
                    {
                        for (int k = 0; k < 4; k++)
                            temp[k] = SBox[temp[k]];
                    }
                    for (int k = 0; k < 4; k++)
                        w[i * 4 + k] = (byte)(w[(i - nk) * 4 + k] ^ temp[k]);
                }
                return w;
            }

            private static byte[] CreateSBox()
            {
                var sbox = new byte[256];
                byte p = 1, q = 1;
                do
                {
                    // p * 3 in GF(2^8), q / 3 (multiplicative inverse walk).
                    p = (byte)(p ^ (p << 1) ^ ((p & 0x80) != 0 ? 0x1B : 0));
                    q ^= (byte)(q << 1);
                    q ^= (byte)(q << 2);
                    q ^= (byte)(q << 4);
                    if ((q & 0x80) != 0)
                        q ^= 0x09;
                    byte x = (byte)(q ^ Rotl(q, 1) ^ Rotl(q, 2) ^ Rotl(q, 3) ^ Rotl(q, 4));
                    sbox[p] = (byte)(x ^ 0x63);
                } while (p != 1);
                sbox[0] = 0x63;
                return sbox;
            }

            private static byte Rotl(byte b, int shift)
            {
                return (byte)((b << shift) | (b >> (8 - shift)));
            }
        }
    }
}
