using BaseLibS.Parse.Endian;
using BaseLibS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static Bittorrent.MessageDecoder;
using static Bittorrent.MessageEncoder;
namespace Bittorrent;

public class Peer
{
	public event EventHandler Disconnected;
	public event EventHandler StateChanged;
	public event EventHandler<DataRequest> BlockRequested;
	public event EventHandler<DataRequest> BlockCancelled;
	public event EventHandler<DataPackage> BlockReceived;
	private TcpClient TcpClient { get; set; }
	public string LocalId { get; set; }
	public string Id { get; set; }
	public Torrent Torrent { get; private set; }
	public IPEndPoint IPEndPoint { get; private set; }
	public string Key { get { return IPEndPoint.ToString(); } }
	public DateTime LastActive;
	public DateTime LastKeepAlive = DateTime.MinValue;

	public bool[] IsPieceDownloaded = new bool[0];
	public bool[][] IsBlockRequested = new bool[0][];

	private NetworkStream stream { get; set; }
	private const int bufferSize = 17000;
	private byte[] streamBuffer = new byte[bufferSize];
	private List<byte> data = new List<byte>();
	public int BlocksRequested { get { return IsBlockRequested.Sum(x => x.Count(y => y)); } }

	public bool IsHandshakeSent { get; private set; }
	public bool IsHandshakeReceived { get; private set; } = false;
	public bool IsDisconnected { get; private set; }
	public bool Chocked { get; private set; } = true;
	public bool IsChokeSent { get; private set; } = true;

	public bool IsInterestedReceived { get; private set; } = false;
	public bool IsInterestedSent { get; private set; } = false;
	public int PiecesDownloadedCount { get { return IsPieceDownloaded.Count(x => x); } }
	public bool IsCompleted { get { return PiecesDownloadedCount == Torrent.PieceCount; } }
	public long Uploaded { get; private set; }
	public long Downloaded { get; private set; }

	public int PiecesRequiredAvailable
	{ get { return IsPieceDownloaded.Select((x, i) => x && !Torrent.IsPieceVerified[i]).Count(x => x); } }
	public Peer(Torrent torrent, string localId, TcpClient client) : this(torrent, localId)
	{
		TcpClient = client;
		IPEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
	}

	public Peer(Torrent torrent, string localId, IPEndPoint endPoint) : this(torrent, localId)
	{
		IPEndPoint = endPoint;
	}

	private Peer(Torrent torrent, string localId)
	{
		LocalId = localId;
		Torrent = torrent;

		LastActive = DateTime.UtcNow;
		IsPieceDownloaded = new bool[Torrent.PieceCount];
		IsBlockRequested = new bool[Torrent.PieceCount][];
		for (int i = 0; i < Torrent.PieceCount; i++)
			IsBlockRequested[i] = new bool[Torrent.GetBlockCount(i)];
	}

	private void SendBytes(byte[] bytes)
	{
		try
		{
			stream.Write(bytes, 0, bytes.Length);
		}
		catch (Exception e)
		{
			Disconnect();
		}
	}
	public async void Connect()
	{
		if (TcpClient == null)
		{
			TcpClient = new TcpClient();
			try
			{
				await TcpClient.ConnectAsync(IPEndPoint);
			}
			catch (Exception e)
			{
				Disconnect();
				return;
			}
		}
		Console.Error.WriteLine("peer: " + IPEndPoint.ToString() + " connected");
		try
		{
			stream = TcpClient.GetStream();
			stream.BeginRead(streamBuffer, 0, Peer.bufferSize, new AsyncCallback(HandleRead), null);
		}
		catch (Exception e)
		{
			Disconnect();
			return;
		}
		SendHandshake();
		if (IsHandshakeReceived)
			SendBitfield(Torrent.IsPieceVerified);

	}
	public void SendBitfield(bool[] isPieceDownloaded)
	{
		Console.Error.WriteLine(Torrent.Name + $"-> bitfield {IPEndPoint} " + String.Join("", isPieceDownloaded.Select(x => x ? 1 : 0)));
		SendBytes(EncodeBitfield(isPieceDownloaded));
	}
	private void HandleRead(IAsyncResult ar)
	{
		int bytes = 0;
		try
		{
			bytes = stream.EndRead(ar);
			if (bytes == 0)
				throw new Exception("No answer");
		}
		catch (Exception e)
		{
			Disconnect();
			return;
		}

		data.AddRange(streamBuffer.Take(bytes));

		int messageLength = GetMessageLength(data);
		while (data.Count >= messageLength)
		{
			HandleMessage(data.Take(messageLength).ToArray());
			data = data.Skip(messageLength).ToList();

			messageLength = GetMessageLength(data);
		}

		try
		{
			stream.BeginRead(streamBuffer, 0, Peer.bufferSize, new AsyncCallback(HandleRead), null);
		}
		catch (Exception e)
		{
			Disconnect();
		}
	}


	private void HandleMessage(byte[] bytes)
	{
		LastActive = DateTime.UtcNow;

		MessageType type = GetMessageType(bytes);

		if (type == MessageType.Unknown)
		{
			return;
		}
		else if (type == MessageType.Handshake)
		{
			byte[] hash;
			string id;
			if (DecodeHandshake(bytes, out hash, out id))
			{
				HandleHandshake(hash, id);
				return;
			}
		}

		else if (type == MessageType.KeepAlive && DecodeKeepAlive(bytes))
		{
			HandleKeepAlive();
			return;
		}
		else if (type == MessageType.Choke && DecodeChoke(bytes))
		{
			HandleChoke();
			return;
		}
		else if (type == MessageType.Unchoke && DecodeUnchoke(bytes))
		{
			HandleUnchoke();
			return;
		}
		else if (type == MessageType.Interested && DecodeInterested(bytes))
		{
			HandleInterested();
			return;
		}
		else if (type == MessageType.NotInterested && DecodeNotInterested(bytes))
		{
			HandleNotInterested();
			return;
		}
		else if (type == MessageType.Have)
		{
			int index;
			if (DecodeHave(bytes, out index))
			{
				HandleHave(index);
				return;
			}
		}
		else if (type == MessageType.Bitfield)
		{
			bool[] isPieceDownloaded;
			if (DecodeBitfield(bytes, IsPieceDownloaded.Length, out isPieceDownloaded))
			{
				HandleBitfield(isPieceDownloaded);
				return;
			}
		}
		else if (type == MessageType.Request)
		{
			int index;
			int begin;
			int length;
			if (DecodeRequest(bytes, out index, out begin, out length))
			{
				HandleRequest(index, begin, length);
				return;
			}
		}
		else if (type == MessageType.Piece)
		{
			int index;
			int begin;
			byte[] data;
			if (DecodePiece(bytes, out index, out begin, out data))
			{
				HandlePiece(index, begin, data);
				return;
			}
		}
		else if (type == MessageType.Cancel)
		{
			int index;
			int begin;
			int length;
			if (DecodeCancel(bytes, out index, out begin, out length))
			{
				HandleCancel(index, begin, length);
				return;
			}
		}
		else if (type == MessageType.Port)
		{
			Console.Error.WriteLine(" <- port: " + String.Join("", bytes.Select(x => x.ToString("x2"))));
			return;
		}

		Console.Error.WriteLine(" Unhandled incoming message " + String.Join("", bytes.Select(x => x.ToString("x2"))));
		Disconnect();
	}

	public void SendKeepAlive()
	{
		if (LastKeepAlive > DateTime.UtcNow.AddSeconds(-30))
			return;

		Console.Error.WriteLine(Torrent.Name +  "-> keep alive " + IPEndPoint);
		SendBytes(EncodeKeepAlive());
		LastKeepAlive = DateTime.UtcNow;
	}

	public void SendUnchoke()
	{
		if (!IsChokeSent)
			return;

		Console.Error.WriteLine(Torrent.Name+ "-> unchoke" + IPEndPoint);
		SendBytes(EncodeUnchoke());
		IsChokeSent = false;
	}

	public void SendInterested()
	{
		if (IsInterestedSent)
			return;

		Console.Error.WriteLine(Torrent.Name + "-> Intrested " + IPEndPoint);

		SendBytes(EncodeInterested());
		IsInterestedSent = true;
	}

	public void SendNotInterested()
	{
		if (!IsInterestedSent)
			return;

		Console.Error.WriteLine(Torrent.Name + "-> not intrested " + IPEndPoint);
		SendBytes(EncodeNotInterested());
		IsInterestedSent = false;
	}

	public void SendHave(int index)
	{
		Console.Error.WriteLine(Torrent.Name + "-> have " + IPEndPoint);

		SendBytes(EncodeHave(index));
	}
	public void SendRequest(int index, int begin, int length)
	{
		Console.Error.WriteLine(Torrent.Name + $"-> request {IPEndPoint} " + index + ", " + begin + ", " + length);
		SendBytes(EncodeRequest(index, begin, length));
	}

	public void SendPiece(int index, int begin, byte[] data)
	{
		Console.Error.WriteLine(Torrent.Name + $"-> piece {IPEndPoint} " + index + ", " + begin + ", " + data.Length);

		SendBytes(EncodePiece(index, begin, data));
		Uploaded += data.Length;
	}

	public void SendCancel(int index, int begin, int length)
	{
		Console.Error.WriteLine(Torrent.Name + "-> cancel " + IPEndPoint);

		SendBytes(EncodeCancel(index, begin, length));
	}
	private MessageType GetMessageType(byte[] bytes)
	{
		if (!IsHandshakeReceived)
			return MessageType.Handshake;

		if (bytes.Length == 4 && EndianBitConverter.Big.ToInt32(bytes, 0) == 0)
			return MessageType.KeepAlive;

		if (bytes.Length > 4 && Enum.IsDefined(typeof(MessageType), (int)bytes[4]))
			return (MessageType)bytes[4];

		return MessageType.Unknown;
	}


	private int GetMessageLength(List<byte> data)
	{
		if (!IsHandshakeReceived)
			return 68;

		if (data.Count < 4)
			return int.MaxValue;

		return EndianBitConverter.Big.ToInt32(data.ToArray(), 0) + 4;
	}
	public void Disconnect()
	{
		if (!IsDisconnected)
		{
			IsDisconnected = true;
			Console.Error.WriteLine("peer: " + IPEndPoint.ToString() + " disconnected");
		}
		TcpClient?.Close();
		Disconnected?.Invoke(this, new EventArgs());
	}

	private void SendHandshake()
	{
		if (IsHandshakeSent)
			return;

		Console.Error.WriteLine($"{Torrent.Name}-> handshake{this.IPEndPoint}");
		SendBytes(EncodeHandshake(Torrent.Infohash, LocalId));
		IsHandshakeSent = true;
	}


	private void HandleHandshake(byte[] hash, string id)
	{
		Console.Error.WriteLine(Torrent.Name + "<- handshake" + IPEndPoint);

		if (!Torrent.Infohash.SequenceEqual(hash))
		{
			Console.Error.WriteLine("invalid handshake, incorrect torrent hash: expecting="
				+ BDecoder.ToHex(Torrent.Infohash) + ", received =" +
				String.Join("", hash.Select(x => x.ToString("x2"))));
			Disconnect();
			return;
		}

		Id = id;

		IsHandshakeReceived = true;
		SendBitfield(Torrent.IsPieceVerified);
	}

	private void HandleKeepAlive()
	{
		Console.Error.WriteLine(Torrent.Name + "<- keep alive");
	}

	private void HandleChoke()
	{
		Console.Error.WriteLine(Torrent.Name + "<- choke " + IPEndPoint);
		Chocked = true;

		StateChanged?.Invoke(this, new EventArgs());
	}

	private void HandleUnchoke()
	{
		Console.Error.WriteLine(Torrent.Name + "<- unchoke " + IPEndPoint);
		Chocked = false;
		StateChanged?.Invoke(this, new EventArgs());
	}

	private void HandleInterested()
	{
		Console.Error.WriteLine(Torrent.Name+ "<- interested " + IPEndPoint);
		IsInterestedReceived = true;

		StateChanged?.Invoke(this, new EventArgs());
	}

	private void HandleNotInterested()
	{
		Console.Error.WriteLine(Torrent.Name+  "<- not interested " + IPEndPoint);
		IsInterestedReceived = false;

		StateChanged?.Invoke(this, new EventArgs());
	}

	private void HandleHave(int index)
	{
		IsPieceDownloaded[index] = true;
		Console.Error.WriteLine(Torrent.Name + $"<- have {IPEndPoint} " + index);

		var handler = StateChanged;
		if (handler != null)
			handler(this, new EventArgs());
	}

	private void HandleBitfield(bool[] isPieceDownloaded)
	{
		for (int i = 0; i < Torrent.PieceCount; i++)
			IsPieceDownloaded[i] = IsPieceDownloaded[i] || isPieceDownloaded[i];

		Console.Error.WriteLine(Torrent.Name+ $"<- bitfield {IPEndPoint} want pieces: " + isPieceDownloaded.Count(x => x==false));

		StateChanged?.Invoke(this, new EventArgs());
	}

	private void HandleRequest(int index, int begin, int length)
	{
		Console.Error.WriteLine(Torrent.Name+ "<- request "+ this.IPEndPoint +
			" index:"+ index + ", " + begin + ", " + length);

		BlockRequested?.Invoke(this, new DataRequest()
			{
				Peer = this,
				Piece = index,
				Begin = begin,
				Length = length
			});
	}

	private void HandlePiece(int index, int begin, byte[] data)
	{
		Console.Error.WriteLine(Torrent.Name + "<- piece " + this.IPEndPoint + " index:" +
			index + ", " + begin + ", " + data.Length);
		Downloaded += data.Length;

		BlockReceived?.Invoke(this, new DataPackage()
		{
			Peer = this,
			Piece = index,
			Block = begin / Torrent.BlockSize,
			Data = data
		});
	}

	private void HandleCancel(int index, int begin, int length)
	{
		Console.Error.WriteLine(Torrent.Name + " <- cancel" + this.IPEndPoint);

		BlockCancelled?.Invoke(this, new DataRequest()
		{
			Peer = this,
			Piece = index,
			Begin = begin,
			Length = length,
		});
	}
}
