using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CDTag.Common.Hash
{
	/// <summary>
	/// Calculates an SHA1 digest.
	/// </summary>
	public static class SHA1
	{
		/// <summary>
		/// Returns the SHA1 digest of an input stream as a string.
		/// </summary>
		/// <param name="stream">Input stream.</param>
		/// <returns>SHA1 digest as a string.</returns>
		public static string Calculate(Stream stream)
		{
			ArgumentNullException.ThrowIfNull(stream);

			return DigestToString(CalculateDigest(stream));
		}

        /// <summary>Returns the SHA1 digest of a file as a string.</summary>
        /// <param name="file">The file.</param>
        /// <returns>SHA1 digest as a string.</returns>
        public static string CalculateFromFile(FileInfo file)
        {
			ArgumentNullException.ThrowIfNull(file);

            using (FileStream fileStream = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return Calculate(fileStream);
            }
        }

	    /// <summary>
		/// Returns the SHA1 digest of a byte array as a string.
		/// </summary>
		/// <param name="data">The byte array.</param>
		/// <returns>SHA1 digest as a string.</returns>
		public static string Calculate(byte[] data)
		{
			ArgumentNullException.ThrowIfNull(data);

			return DigestToString(CalculateDigest(data));
		}

		/// <summary>
		/// Returns the SHA1 digest of an input stream as a byte array.
		/// </summary>
		/// <param name="stream">The stream.</param>
		/// <returns>SHA1 digest as a byte array.</returns>
		public static byte[] CalculateDigest(Stream stream)
		{
			ArgumentNullException.ThrowIfNull(stream);

			stream.Position = 0;
			return System.Security.Cryptography.SHA1.HashData(stream); // static and thread-safe, unlike a shared SHA1 instance
		}

		/// <summary>
		/// Returns the SHA1 digest of a byte array as a byte array.
		/// </summary>
		/// <param name="data">The byte array.</param>
		/// <returns>SHA1 digest as a byte array.</returns>
		public static byte[] CalculateDigest(byte[] data)
		{
			ArgumentNullException.ThrowIfNull(data);

			using (MemoryStream memoryStream = new MemoryStream(data))
			{
				return CalculateDigest(memoryStream);
			}
		}

		private static string DigestToString(IEnumerable<byte> digest)
		{
			StringBuilder str = new StringBuilder();
			foreach (byte item in digest)
			{
				str.AppendFormat("{0:x2}", item);
			}
			return str.ToString();
		}
	}
}