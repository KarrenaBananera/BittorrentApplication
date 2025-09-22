using BaseLibS.Parse.Endian;
using BaseLibS.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bittorrent;

public static class MessageDecoder
{
	public static bool DecodeKeepAlive(byte[] bytes)
	{
		if (bytes.Length != 4 || EndianBitConverter.Big.ToInt32(bytes, 0) != 0)
		{
			Console.Error.WriteLine("invalid keep alive");
			return false;
		}
		return true;
	}

	public static bool DecodeChoke(byte[] bytes)
	{
		return DecodeState(bytes, MessageType.Choke);
	}

	public static bool DecodeUnchoke(byte[] bytes)
	{
		return DecodeState(bytes, MessageType.Unchoke);
	}

	public static bool DecodeInterested(byte[] bytes)
	{
		return DecodeState(bytes, MessageType.Interested);
	}

	public static bool DecodeNotInterested(byte[] bytes)
	{
		return DecodeState(bytes, MessageType.NotInterested);
	}
	public static bool DecodeState(byte[] bytes, MessageType type)
	{
		if (bytes.Length != 5 || EndianBitConverter.Big.ToInt32(bytes, 0) != 1 || bytes[4] != (byte)type)
		{
			Console.Error.WriteLine("invalid " + Enum.GetName(typeof(MessageType), type));
			return false;
		}
		return true;
	}

	public static bool DecodeHave(byte[] bytes, out int index)
	{
		index = -1;

		if (bytes.Length != 9 || EndianBitConverter.Big.ToInt32(bytes, 0) != 5)
		{
			Console.Error.WriteLine("invalid have, first byte must equal 0x2");
			return false;
		}

		index = EndianBitConverter.Big.ToInt32(bytes, 5);

		return true;
	}

	public static bool DecodeBitfield(byte[] bytes, int pieces, out bool[] isPieceDownloaded)
	{
		isPieceDownloaded = new bool[pieces];

		int expectedLength = Convert.ToInt32(Math.Ceiling(pieces / 8.0)) + 1;

		if (bytes.Length != expectedLength + 4 || EndianBitConverter.Big.ToInt32(bytes, 0) != expectedLength)
		{
			Console.Error.WriteLine("invalid bitfield, first byte must equal " + expectedLength);
			return false;
		}

		BitArray bitfield = new BitArray(bytes.Skip(5).ToArray());

		for (int i = 0; i < pieces; i++)
			isPieceDownloaded[i] = bitfield[bitfield.Length - 1 - i];

		return true;
	}

	public static bool DecodeCancel(byte[] bytes, out int index, out int begin, out int length)
	{
		index = -1;
		begin = -1;
		length = -1;

		if (bytes.Length != 17 || EndianBitConverter.Big.ToInt32(bytes, 0) != 13)
		{
			Console.Error.WriteLine("invalid cancel message, must be of length 17");
			return false;
		}

		index = EndianBitConverter.Big.ToInt32(bytes, 5);
		begin = EndianBitConverter.Big.ToInt32(bytes, 9);
		length = EndianBitConverter.Big.ToInt32(bytes, 13);

		return true;
	}

	public static bool DecodeRequest(byte[] bytes, out int index, out int begin, out int length)
	{
		index = -1;
		begin = -1;
		length = -1;

		if (bytes.Length != 17 || EndianBitConverter.Big.ToInt32(bytes, 0) != 13)
		{
			Console.Error.WriteLine("invalid request message, must be of length 17");
			return false;
		}

		index = EndianBitConverter.Big.ToInt32(bytes, 5);
		begin = EndianBitConverter.Big.ToInt32(bytes, 9);
		length = EndianBitConverter.Big.ToInt32(bytes, 13);

		return true;
	}

	public static bool DecodePiece(byte[] bytes, out int index, out int begin, out byte[] data)
	{
		index = -1;
		begin = -1;
		data = new byte[0];

		if (bytes.Length < 13)
		{
			Console.Error.WriteLine("invalid piece message");
			return false;
		}

		int length = EndianBitConverter.Big.ToInt32(bytes, 0) - 9;
		index = EndianBitConverter.Big.ToInt32(bytes, 5);
		begin = EndianBitConverter.Big.ToInt32(bytes, 9);

		data = new byte[length];
		Buffer.BlockCopy(bytes, 13, data, 0, length);

		return true;
	}

	public static bool DecodeHandshake(byte[] bytes, out byte[] hash, out string id)
	{
		hash = new byte[20];
		id = "";

		if (bytes.Length != 68 || bytes[0] != 19)
		{
			Console.Error.WriteLine("invalid handshake, must be of length 68 and first byte must equal 19");
			return false;
		}

		if (Encoding.UTF8.GetString(bytes.Skip(1).Take(19).ToArray()) != "BitTorrent protocol")
		{
			Console.Error.WriteLine("invalid handshake, protocol must equal \"BitTorrent protocol\"");
			return false;
		}

		hash = bytes.Skip(28).Take(20).ToArray();

		id = BDecoder.ToHex((bytes.Skip(48).Take(20).ToArray()));
		return true;
	}
}
