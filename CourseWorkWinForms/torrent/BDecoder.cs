using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Bittorrent;

public class BDecoder
{
	public static Torrent LoadFromFile(string filePath, string downloadPath)
	{
		object obj = DecodeFile(filePath);
		string name = Path.GetFileNameWithoutExtension(filePath);

		return BEncodingObjectToTorrent(obj, name, downloadPath);
	}

	public static string ToHex(byte[] data)
	{
		return String.Join("", data.Select(x => x.ToString("x2")));
	}
	public static object DecodeFile(string path)
	{
		if (!File.Exists(path))
			throw new FileNotFoundException("unable to find file: " + path);

		byte[] bytes = File.ReadAllBytes(path);

		return BDecoder.Decode(bytes);
	}
	private static Torrent BEncodingObjectToTorrent(object bencoding, string name, string downloadPath)
	{
		Dictionary<string, object> obj = (Dictionary<string, object>)bencoding;

		if (obj == null)
			throw new Exception("not a torrent file");

		List<string> trackers = new List<string>();
		if (obj.ContainsKey("announce"))
			trackers.Add(DecodeUTF8String(obj["announce"]));

		if (obj.ContainsKey("announce-list"))
		{
			var trackersList = obj["announce-list"] as List<object>;
			foreach (var tracker in trackersList)
			{
				var trackerObj = tracker as List<object>;
				trackers.Add(DecodeUTF8String(trackerObj[0]));
			}
		}
		trackers = trackers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		if (!obj.ContainsKey("info"))
			throw new Exception("Missing info section");


		Dictionary<string, object> info = (Dictionary<string, object>)obj["info"];

		if (info == null)
			throw new Exception("error");

		List<FileItem> files = new List<FileItem>();

		if (info.ContainsKey("name") && info.ContainsKey("length"))
		{
			files.Add(new FileItem()
			{
				Path = DecodeUTF8String(info["name"]),
				Size = (long)info["length"]
			});
		}
		else if (info.ContainsKey("files"))
		{
			long running = 0;

			foreach (object item in (List<object>)info["files"])
			{
				var dict = item as Dictionary<string, object>;

				if (dict == null || !dict.ContainsKey("path") || !dict.ContainsKey("length"))
					throw new Exception("error: incorrect file specification");

				string path = String.Join(Path.DirectorySeparatorChar.ToString(), ((List<object>)dict["path"]).Select(x => DecodeUTF8String(x)));

				long size = (long)dict["length"];

				files.Add(new FileItem()
				{
					Path = path,
					Size = size,
					Offset = running
				});

				running += size;
			}
		}
		else
		{
			throw new Exception("error: no files specified in torrent");
		}

		if (!info.ContainsKey("piece length"))
			throw new Exception("error");
		int pieceSize = Convert.ToInt32(info["piece length"]);

		if (!info.ContainsKey("pieces"))
			throw new Exception("error");
		byte[] pieceHashes = (byte[])info["pieces"];

		bool? isPrivate = null;
		if (info.ContainsKey("private"))
			isPrivate = ((long)info["private"]) == 1L;

		Torrent torrent = new Torrent(name, downloadPath, files, trackers, pieceSize, pieceHashes, 16384, isPrivate);

		if (obj.ContainsKey("comment"))
			torrent.Comment = DecodeUTF8String(obj["comment"]);

		if (obj.ContainsKey("created by"))
			torrent.CreatedBy = DecodeUTF8String(obj["created by"]);

		if (obj.ContainsKey("creation date"))
			torrent.CreationDate = UnixTimeStampToDateTime(Convert.ToDouble(obj["creation date"]));

		if (obj.ContainsKey("encoding"))
			torrent.Encoding = Encoding.GetEncoding(DecodeUTF8String(obj["encoding"]));
		
		return torrent;

	}
	public static object Decode(byte[] bytes)
	{
		IEnumerator<byte> enumerator = ((IEnumerable<byte>)bytes).GetEnumerator();
		string str2 = string.Join(" ", bytes);
		string str = Encoding.UTF8.GetString(bytes);
		enumerator.MoveNext();

		return DecodeNextObject(enumerator);
	}
	private static bool test = false;
	public static object DecodeTest(byte[] bytes)
	{
		IEnumerator<byte> enumerator = ((IEnumerable<byte>)bytes).GetEnumerator();
		enumerator.MoveNext();
		test = true;
		return DecodeNextObject(enumerator);
	}

	private static object DecodeNextObject(IEnumerator<byte> enumerator)
	{
		if (enumerator.Current == 'd') // 100
			return DecodeDictionary(enumerator);

		if (enumerator.Current == 'l')
			return DecodeList(enumerator);

		if (enumerator.Current == 'i') //105
			return DecodeNumber(enumerator);

		var res = DecodeByteArray(enumerator);
		if (test == true)
		{
			return DecodeUTF8String(res);
		}
		return res;
	}

	public static string DecodeUTF8String(object obj)
	{
		byte[] bytes = obj as byte[];

		if (bytes == null)
			throw new Exception("unable to decode utf-8 string, object is not a byte array");

		return Encoding.UTF8.GetString(bytes);
	}
	private static Dictionary<string, object> DecodeDictionary(IEnumerator<byte> enumerator)
	{
		Dictionary<string, object> dict = new Dictionary<string, object>();
		List<string> keys = new List<string>();

		while (enumerator.MoveNext())
		{
			if (enumerator.Current == 'e')
				break;

			
			string key = Encoding.UTF8.GetString(DecodeByteArray(enumerator));
			enumerator.MoveNext();
			object val = DecodeNextObject(enumerator);

			keys.Add(key);
			dict.Add(key, val);
		}

		
		return dict;
	}
	private static long DecodeNumber(IEnumerator<byte> enumerator)
	{
		List<byte> bytes = new List<byte>();

		while (enumerator.MoveNext())
		{
			if (enumerator.Current == 'e') //101
				break;

			bytes.Add(enumerator.Current);
		}

		string numAsString = Encoding.UTF8.GetString(bytes.ToArray());

		return Int64.Parse(numAsString);
	}
	private static byte[] DecodeByteArray(IEnumerator<byte> enumerator)
	{
		List<byte> lengthBytes = new List<byte>();

		do
		{
			if (enumerator.Current == ':') //58
				break;

			lengthBytes.Add(enumerator.Current);
		}
		while (enumerator.MoveNext());

		string lengthString = Encoding.UTF8.GetString(lengthBytes.ToArray());

		int length;
		if (!Int32.TryParse(lengthString, out length))
			throw new Exception("unable to parse length of byte array");

		byte[] bytes = new byte[length];

		for (int i = 0; i < length; i++)
		{
			enumerator.MoveNext();
			bytes[i] = enumerator.Current;
		}

		return bytes;
	}

	private static List<object> DecodeList(IEnumerator<byte> enumerator)
	{
		List<object> list = new List<object>();

		while (enumerator.MoveNext())
		{
			if (enumerator.Current == 'e')
				break;

			list.Add(DecodeNextObject(enumerator));
		}
		return list;
	}
	public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
	{
		DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
		dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
		return dtDateTime;
	}
}

