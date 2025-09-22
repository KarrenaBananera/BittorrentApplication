using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bittorrent;

public static class BEncode
{
	private static byte DictionaryStart = System.Text.Encoding.UTF8.GetBytes("d")[0]; // 100
	private static byte DictionaryEnd = System.Text.Encoding.UTF8.GetBytes("e")[0]; // 101
	private static byte ListStart = System.Text.Encoding.UTF8.GetBytes("l")[0]; // 108
	private static byte ListEnd = System.Text.Encoding.UTF8.GetBytes("e")[0]; // 101
	private static byte NumberStart = System.Text.Encoding.UTF8.GetBytes("i")[0]; // 105
	private static byte NumberEnd = System.Text.Encoding.UTF8.GetBytes("e")[0]; // 101
	private static byte ByteArrayDivider = System.Text.Encoding.UTF8.GetBytes(":")[0]; //  58
	public static byte[] Encode(object obj)
	{
		MemoryStream buffer = new MemoryStream();

		EncodeNextObject(buffer, obj);

		return buffer.ToArray();
	}

	public static void SaveToFile(Torrent torrent)
	{
		object obj = TorrentToBEncodingObject(torrent);

		EncodeToFile(obj, torrent.Name + ".torrent");
	}


	public static void EncodeToFile(object obj, string path)
	{
		File.WriteAllBytes(path, Encode(obj));
	}

	private static void EncodeNextObject(MemoryStream buffer, object obj)
	{
		if (obj is byte[])
			EncodeByteArray(buffer, (byte[])obj);
		else if (obj is string)
			EncodeString(buffer, (string)obj);
		else if (obj is long)
			EncodeNumber(buffer, (long)obj);
		else if (obj.GetType() == typeof(List<object>))
			EncodeList(buffer, (List<object>)obj);
		else if (obj.GetType() == typeof(Dictionary<string, object>))
			EncodeDictionary(buffer, (Dictionary<string, object>)obj);
		else
			throw new Exception("unable to encode type " + obj.GetType());
	}

	private static void EncodeNumber(MemoryStream buffer, long input)
	{
		buffer.Append(NumberStart);
		buffer.Append(Encoding.UTF8.GetBytes(Convert.ToString(input)));
		buffer.Append(NumberEnd);
	}

	private static void EncodeByteArray(MemoryStream buffer, byte[] body)
	{
		buffer.Append(Encoding.UTF8.GetBytes(Convert.ToString(body.Length)));
		buffer.Append(ByteArrayDivider);
		buffer.Append(body);
	}

	private static void EncodeString(MemoryStream buffer, string input)
	{
		EncodeByteArray(buffer, Encoding.UTF8.GetBytes(input));
	}

	private static void EncodeList(MemoryStream buffer, List<object> input)
	{
		buffer.Append(ListStart);
		foreach (var item in input)
			EncodeNextObject(buffer, item);
		buffer.Append(ListEnd);
	}

	private static void EncodeDictionary(MemoryStream buffer, Dictionary<string, object> input)
	{
		buffer.Append(DictionaryStart);

		var sortedKeys = input.Keys.ToList().OrderBy(x => BitConverter.ToString(Encoding.UTF8.GetBytes(x)));

		foreach (var key in sortedKeys)
		{
			EncodeString(buffer, key);
			EncodeNextObject(buffer, input[key]);
		}
		buffer.Append(DictionaryEnd);
	}

	public static long DateTimeToUnixTimestamp(DateTime time)
	{
		return Convert.ToInt64((DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds);
	}

	private static object TorrentToBEncodingObject(Torrent torrent)
	{
		Dictionary<string, object> dict = new Dictionary<string, object>();

		if (torrent.Trackers.Count == 1)
			dict["announce"] = Encoding.UTF8.GetBytes(torrent.Trackers[0].Address);
		else
			dict["announce"] = torrent.Trackers.Select(x => (object)Encoding.UTF8.GetBytes(x.Address)).ToList();
		dict["comment"] = Encoding.UTF8.GetBytes(torrent.Comment);
		dict["created by"] = Encoding.UTF8.GetBytes(torrent.CreatedBy);
		dict["creation date"] = DateTimeToUnixTimestamp(torrent.CreationDate);
		dict["encoding"] = Encoding.UTF8.GetBytes(Encoding.UTF8.WebName.ToUpper());
		dict["info"] = TorrentInfoToBEncodingObject(torrent);

		return dict;
	}

	public static object TorrentInfoToBEncodingObject(Torrent torrent)
	{
		Dictionary<string, object> dict = new Dictionary<string, object>();

		dict["piece length"] = (long)torrent.PieceSize;
		byte[] pieces = new byte[20 * torrent.PieceCount];
		for (int i = 0; i < torrent.PieceCount; i++)
			Buffer.BlockCopy(torrent.PieceHashes[i], 0, pieces, i * 20, 20);
		dict["pieces"] = pieces;

		if (torrent.IsPrivate.HasValue)
			dict["private"] = torrent.IsPrivate.Value ? 1L : 0L;

		if (torrent.Files.Count == 1)
		{
			dict["name"] = Encoding.UTF8.GetBytes(torrent.Files[0].Path);
			dict["length"] = torrent.Files[0].Size;
		}
		else
		{
			List<object> files = new List<object>();

			foreach (var f in torrent.Files)
			{
				Dictionary<string, object> fileDict = new Dictionary<string, object>();
				fileDict["path"] = f.Path.Split(Path.DirectorySeparatorChar).Select(x => (object)Encoding.UTF8.GetBytes(x)).ToList();
				fileDict["length"] = f.Size;
				files.Add(fileDict);
			}

			dict["files"] = files;
			dict["name"] = Encoding.UTF8.GetBytes(torrent.FileDirectory.Substring(0, torrent.FileDirectory.Length - 1));
		}

		return dict;
	}
}


public static class MemoryStreamExtensions
{
	public static void Append(this MemoryStream stream, byte value)
	{
		stream.Append(new[] { value });
	}

	public static void Append(this MemoryStream stream, byte[] values)
	{
		stream.Write(values, 0, values.Length);
	}
}