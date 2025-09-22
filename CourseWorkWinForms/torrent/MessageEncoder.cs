using BaseLibS.Parse.Endian;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bittorrent;

public static class MessageEncoder
{
	public static byte[] EncodeHandshake(byte[] hash, string id)
	{
		byte[] message = new byte[68];
		message[0] = 19;
		Buffer.BlockCopy(Encoding.UTF8.GetBytes("BitTorrent protocol"), 0, message, 1, 19);
		Buffer.BlockCopy(hash, 0, message, 28, 20);
		Buffer.BlockCopy(Encoding.UTF8.GetBytes(id), 0, message, 48, 20);

		return message;
	}

	public static byte[] EncodeKeepAlive()
	{
		return EndianBitConverter.Big.GetBytes(0);
	}

	public static byte[] EncodeChoke()
	{
		return EncodeState(MessageType.Choke);
	}

	public static byte[] EncodeInterested()
	{
		return EncodeState(MessageType.Interested);
	}

	public static byte[] EncodeUnchoke()
	{
		return EncodeState(MessageType.Unchoke);
	}

	public static byte[] EncodeState(MessageType type)
	{
		byte[] message = new byte[5];
		Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(1), 0, message, 0, 4);
		message[4] = (byte)type;
		return message;
	}

	public static byte[] EncodeHave(int index)
	{
		byte[] message = new byte[9];
		Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(5), 0, message, 0, 4);
		message[4] = (byte)MessageType.Have;
		Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(index), 0, message, 5, 4);

		return message;
	}

	public static byte[] EncodeBitfield(bool[] isPieceDownloaded)
	{
		int numPieces = isPieceDownloaded.Length;
		int numBytes = Convert.ToInt32(Math.Ceiling(numPieces / 8.0));
		int numBits = numBytes * 8;

		int length = numBytes + 1;

		byte[] message = new byte[length + 4];
		Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(length), 0, message, 0, 4);
		message[4] = (byte)MessageType.Bitfield;

		bool[] downloaded = new bool[numBits];
		for (int i = 0; i < numPieces; i++)
			downloaded[i] = isPieceDownloaded[i];

		BitArray bitfield = new BitArray(downloaded);
		BitArray reversed = new BitArray(numBits);
		for (int i = 0; i < numBits; i++)
			reversed[i] = bitfield[numBits - i - 1];

		reversed.CopyTo(message, 5);

		return message;
	}

	public static byte[] EncodePiece(int index, int begin, byte[] data)
	{
		int length = data.Length + 9;

		byte[] message = new byte[length + 4];
		Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(length), 0, message, 0, 4);
		message[4] = (byte)MessageType.Piece;
		Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(index), 0, message, 5, 4);
		Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(begin), 0, message, 9, 4);
		Buffer.BlockCopy(data, 0, message, 13, data.Length);

		return message;
	}

	public static byte[] EncodeCancel(int index, int begin, int length)
	{
		byte[] message = new byte[17];
		Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(13), 0, message, 0, 4);
		message[4] = (byte)MessageType.Cancel;
		Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(index), 0, message, 5, 4);
		Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(begin), 0, message, 9, 4);
		Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(length), 0, message, 13, 4);

		return message;
	}

	public static byte[] EncodeNotInterested()
	{
		return EncodeState(MessageType.NotInterested);
	}
	public static byte[] EncodeRequest(int index, int begin, int length)
	{
		byte[] message = new byte[17];
		Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(13), 0, message, 0, 4);
		message[4] = (byte)MessageType.Request;
		Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(index), 0, message, 5, 4);
		Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(begin), 0, message, 9, 4);
		Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(length), 0, message, 13, 4);

		return message;
	}
}
