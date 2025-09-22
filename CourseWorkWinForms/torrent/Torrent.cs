using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Bittorrent;

public class Torrent
{
	public event EventHandler<List<IPEndPoint>> PeerListUpdated;
	public event EventHandler<int> PieceVerified;
	public event EventHandler<bool> FilesDownloaded;
	private object[] fileWriteLocks;

	public bool IsCompleted { get { return VerifiedPieceCount == PieceCount; } }
	public bool IsStarted { get { return VerifiedPieceCount > 0; } }
	public string Name { get; private set; }
	public bool? IsPrivate { get; private set; }
	public List<FileItem> Files { get; private set; } = new List<FileItem>();
	public string FileDirectory { get { return (Files.Count > 1 ? Name + Path.DirectorySeparatorChar : ""); } }
	public string DownloadDirectory { get; private set; }
	public int PieceCount { get { return PieceHashes.Length; } }
	public byte[] Infohash { get; private set; } = new byte[20];
	public List<Tracker> Trackers { get; } = new List<Tracker>();
	public string Comment { get; set; }
	public string CreatedBy { get; set; }
	public DateTime CreationDate { get; set; }
	public Encoding Encoding { get; set; }

	public double Progress
	{
		get
		{
			return IsBlockAcquired.Average(x => x.Average(y => y ? 1 : 0));
		}
	}
	public bool[] IsPieceVerified { get; private set; } = new bool[0];
	public bool[][] IsBlockAcquired { get; private set; } 
	public string UrlSafeStringInfohash { get { return Encoding.UTF8.GetString(WebUtility.UrlEncodeToBytes(this.Infohash, 0, 20)); } }

	public int BlockSize { get; private set; }
	public int PieceSize { get; private set; }
	public long TotalSize { get { return Files.Sum(x => x.Size); } }
	public int VerifiedPieceCount { get { return IsPieceVerified.Count(x => x); } }
	public long Uploaded { get; set; } = 0;
	public long Downloaded { get { return PieceSize * VerifiedPieceCount; } } 
	public long Left { get { return TotalSize - Downloaded; } }
	public byte[][] PieceHashes { get; private set; }

	public string Size
	{
		get
		{
			return FileItem.BytesToString(TotalSize);
		}
	}
	private static SHA1 sha1 = SHA1.Create();

	public Torrent(string name, string location, List<FileItem> files, List<string> trackers,
		int pieceSize, byte[] pieceHashes = null, int blockSize = 16384, bool? isPrivate = false)
	{
		Name = name;
		DownloadDirectory = location;
		Files = files;

		foreach (var tracker in trackers)
		{
			var newTracker = new Tracker(tracker, this);
			Trackers.Add(newTracker);
			newTracker.PeerListUpdated += (obj, peers) => {
				if (PeerListUpdated != null)
					PeerListUpdated(this, peers);
					}; 
		}
		PieceSize = pieceSize;
		BlockSize = blockSize;
		IsPrivate = isPrivate;
		fileWriteLocks = new object[Files.Count];
		for (int i = 0; i < this.Files.Count; i++)
			fileWriteLocks[i] = new object();

		int count = Convert.ToInt32(Math.Ceiling(TotalSize / Convert.ToDouble(PieceSize)));

		PieceHashes = new byte[count][];
		IsPieceVerified = new bool[count];
		IsBlockAcquired = new bool[count][];

		for (int i = 0; i < PieceCount; i++)
			IsBlockAcquired[i] = new bool[GetBlockCount(i)];

		for (int i = 0; i < PieceCount; i++)
		{
			PieceHashes[i] = new byte[20];
			Buffer.BlockCopy(pieceHashes, i * 20, PieceHashes[i], 0, 20);
		}

		object info = BEncode.TorrentInfoToBEncodingObject(this);
		byte[] infoBytes = BEncode.Encode(info);
		Infohash = SHA1.Create().ComputeHash(infoBytes);

		for (int i = 0; i < PieceCount; i++)
			Verify(i);
	}

	public int GetBlockCount(int piece)
	{
		return Convert.ToInt32(Math.Ceiling(GetPieceSize(piece) / (double)BlockSize));
	}

	public int GetBlockSize(int piece, int block)
	{
		if (block == GetBlockCount(piece) - 1)
		{
			int remainder = Convert.ToInt32(GetPieceSize(piece) % BlockSize);
			if (remainder != 0)
				return remainder;
		}

		return BlockSize;
	}
	public int GetPieceSize(int piece)
	{
		if (piece == PieceCount - 1)
		{
			int remainder = Convert.ToInt32(TotalSize % PieceSize);
			if (remainder != 0)
				return remainder;
		}

		return PieceSize;
	}

	public void UpdateTrackers(string id, int port)
	{
		foreach (var tracker in Trackers)
			tracker.Update(this, id, port);
	}

	private void Verify(int piece)
	{
		byte[] hash = GetHash(piece);

		bool isVerified = (hash != null && hash.SequenceEqual(PieceHashes[piece]));

		if (isVerified)
		{
			Console.Error.WriteLine("done download piece" + piece);
			IsPieceVerified[piece] = true;

			for (int j = 0; j < IsBlockAcquired[piece].Length; j++)
				IsBlockAcquired[piece][j] = true;
			if (IsPieceVerified.All(x => x == true))
			{
				Console.Error.WriteLine("all downloaded");
				FilesDownloaded?.Invoke(this, true);
			}
			PieceVerified?.Invoke(this, piece);
			
			return;
		}

		IsPieceVerified[piece] = false;

		// reload the entire piece
		if (IsBlockAcquired[piece].All(x => x))
		{
			Console.Error.WriteLine("Wrong piece hash: " + piece);
			for (int j = 0; j < IsBlockAcquired[piece].Length; j++)
				IsBlockAcquired[piece][j] = false;
		}
	}

	public byte[] GetHash(int piece)
	{
		byte[] data = ReadPiece(piece);

		if (data == null)
			return null;

		return sha1.ComputeHash(data);
	}

	public byte[] ReadPiece(int piece)
	{
		return Read(piece * PieceSize, GetPieceSize(piece));
	}

	public byte[] ReadBlock(int piece, int offset, int length)
	{
		return Read(piece * PieceSize + offset, length);
	}
	private byte[] Read(long start, int length)
	{
		long end = start + length;
		byte[] buffer = new byte[length];

		for (int i = 0; i < Files.Count; i++)
		{
			if ((start < Files[i].Offset && end < Files[i].Offset) ||
				(start > Files[i].Offset + Files[i].Size && end > Files[i].Offset + Files[i].Size))
				continue;

			string filePath = DownloadDirectory + Path.DirectorySeparatorChar + FileDirectory + Files[i].Path;

			if (!File.Exists(filePath))
				return null;

			long fstart = Math.Max(0, start - Files[i].Offset);
			long fend = Math.Min(end - Files[i].Offset, Files[i].Size);
			int flength = Convert.ToInt32(fend - fstart);
			int bstart = Math.Max(0, Convert.ToInt32(Files[i].Offset - start));
			lock (fileWriteLocks[i])
			{
				using (Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				{
					stream.Seek(fstart, SeekOrigin.Begin);
					stream.Read(buffer, bstart, flength);
				}
			}
		}

		return buffer;
	}

	public void WriteBlock(int piece, int block, byte[] bytes)
	{
		Write(piece * PieceSize + block * BlockSize, bytes);
		IsBlockAcquired[piece][block] = true;
		Verify(piece);
	}

	private void Write(long start, byte[] bytes)
	{
		long end = start + bytes.Length;

		for (int i = 0; i < Files.Count; i++)
		{
			if ((start < Files[i].Offset && end < Files[i].Offset) ||
				(start > Files[i].Offset + Files[i].Size && end > Files[i].Offset + Files[i].Size))
				continue;

			string filePath = DownloadDirectory + Path.DirectorySeparatorChar + FileDirectory + Files[i].Path;
			//Console.Error.WriteLine($"Writing in {Files[i].Path}");
			//Console.Error.WriteLine($"full {filePath}");

			string dir = Path.GetDirectoryName(filePath);
			if (!Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			lock (fileWriteLocks[i])
			{
				using (Stream stream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
				{
					long fstart = Math.Max(0, start - Files[i].Offset);
					long fend = Math.Min(end - Files[i].Offset, Files[i].Size);
					int flength = Convert.ToInt32(fend - fstart);
					int bstart = Math.Max(0, Convert.ToInt32(Files[i].Offset - start));

					stream.Seek(fstart, SeekOrigin.Begin);
					stream.Write(bytes, bstart, flength);
				}
			}
		}
	}
}
